using System.Collections.Generic;
using UnityEngine;

public class DroneGameManager : MonoBehaviour
{
    [Header("DroneNPC Prefab")]
    public DroneNPC dronePrefab;

    [Header("3D Grid")]
    public DroneWaypointGraph grid;

    [Header("場上數量")]
    public int targetDroneCount = 4;
    public bool spawnOnStart = true;
    public float respawnDelay = 3f;

    [Header("Spawn Throttle")]
    [Tooltip("一次只補一台，避免開場多台 Drone 同一幀 A* 導致 FPS spike")]
    public float spawnInterval = 0.08f;

    [Header("Object Pool")]
    public int initialPoolSize = 100;
    public bool allowPoolExpansion = true;

    [Header("生成設定")]
    public string playerTag = "Player";
    public bool avoidSpawnNearPlayer = true;
    public float minSpawnDistanceFromPlayer = 80f;

    private readonly List<DroneNPC> activeDrones = new List<DroneNPC>();
    private readonly Queue<DroneNPC> pooledDrones = new Queue<DroneNPC>();

    private int pendingRespawnCount = 0;
    private Transform player;
    private float nextSpawnTime = 0f;

    void Awake()
    {
        FindPlayer();
        PrewarmPool();
    }

    void Start()
    {
        nextSpawnTime = Time.time + Random.Range(0.1f, 0.4f);
    }

    void Update()
    {
        activeDrones.RemoveAll(drone => drone == null || !drone.gameObject.activeSelf);

        if (!spawnOnStart)
        {
            return;
        }

        if (grid == null || !grid.IsReady)
        {
            return;
        }

        TrySpawnOneIfNeeded();
    }

    void TrySpawnOneIfNeeded()
    {
        if (Time.time < nextSpawnTime)
        {
            return;
        }

        int expectedCount = activeDrones.Count + pendingRespawnCount;

        if (expectedCount >= targetDroneCount)
        {
            return;
        }

        bool spawned = SpawnOneDrone();

        nextSpawnTime = Time.time + spawnInterval;

        if (!spawned)
        {
            nextSpawnTime = Time.time + Mathf.Max(0.5f, spawnInterval);
        }
    }

    void FindPlayer()
    {
        GameObject playerObject = GameObject.FindGameObjectWithTag(playerTag);

        if (playerObject != null)
        {
            player = playerObject.transform;
        }
    }

    void PrewarmPool()
    {
        if (dronePrefab == null)
        {
            Debug.LogWarning("DroneGameManager: dronePrefab 沒有設定");
            return;
        }

        int count = Mathf.Max(initialPoolSize, targetDroneCount);

        for (int i = 0; i < count; i++)
        {
            DroneNPC drone = Instantiate(dronePrefab, transform);
            drone.gameObject.SetActive(false);
            pooledDrones.Enqueue(drone);
        }
    }

    bool SpawnOneDrone()
    {
        if (dronePrefab == null || grid == null || !grid.IsReady)
        {
            return false;
        }

        Vector3 spawnPosition;

        if (avoidSpawnNearPlayer)
        {
            if (player == null)
            {
                FindPlayer();
            }

            if (player != null)
            {
                if (!grid.TryGetRandomWalkablePointFarFrom(
                    player.position,
                    minSpawnDistanceFromPlayer,
                    out spawnPosition))
                {
                    return false;
                }
            }
            else
            {
                if (!grid.TryGetRandomWalkablePoint(out spawnPosition))
                {
                    return false;
                }
            }
        }
        else
        {
            if (!grid.TryGetRandomWalkablePoint(out spawnPosition))
            {
                return false;
            }
        }

        DroneNPC drone = GetDroneFromPool();

        if (drone == null)
        {
            return false;
        }

        Quaternion spawnRotation = Quaternion.Euler(
            0f,
            Random.Range(0f, 360f),
            0f
        );

        drone.Initialize(
            this,
            spawnPosition,
            spawnRotation,
            grid
        );

        drone.gameObject.SetActive(true);
        activeDrones.Add(drone);

        return true;
    }

    DroneNPC GetDroneFromPool()
    {
        if (pooledDrones.Count > 0)
        {
            return pooledDrones.Dequeue();
        }

        if (!allowPoolExpansion)
        {
            return null;
        }

        DroneNPC drone = Instantiate(dronePrefab, transform);
        drone.gameObject.SetActive(false);
        return drone;
    }

    public void NotifyDroneExploded(DroneNPC drone)
    {
        if (drone == null)
        {
            return;
        }

        activeDrones.Remove(drone);
        ReturnDroneToPool(drone);
        pendingRespawnCount++;
        nextSpawnTime = Mathf.Max(nextSpawnTime, Time.time + respawnDelay);
        Invoke(nameof(ReleasePendingRespawn), respawnDelay);
    }

    void ReleasePendingRespawn()
    {
        pendingRespawnCount = Mathf.Max(0, pendingRespawnCount - 1);
    }

    void ReturnDroneToPool(DroneNPC drone)
    {
        if (drone == null)
        {
            return;
        }

        drone.PrepareForPool();
        drone.gameObject.SetActive(false);
        drone.transform.SetParent(transform);
        pooledDrones.Enqueue(drone);
    }
}
