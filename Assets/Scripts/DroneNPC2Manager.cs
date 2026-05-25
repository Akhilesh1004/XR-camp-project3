using System.Collections.Generic;
using UnityEngine;

public class DroneNPC2Manager : MonoBehaviour
{
    [Header("DroneNPC2 Prefab")]
    public DroneNPC2 dronePrefab;

    [Header("3D Grid")]
    public DroneWaypointGraph grid;

    [Header("場上數量")]
    public int targetDroneCount = 40;
    public bool spawnOnStart = true;
    public float respawnDelay = 5f;

    [Header("Spawn Throttle")]
    [Tooltip("一次只補一台，避免開場多台 Drone 同一幀 A* 導致 FPS spike")]
    public float spawnInterval = 0.15f;

    [Header("Object Pool")]
    public int initialPoolSize = 40;
    public bool allowPoolExpansion = true;

    private readonly List<DroneNPC2> activeDrones = new List<DroneNPC2>();
    private readonly Queue<DroneNPC2> pooledDrones = new Queue<DroneNPC2>();

    private int pendingRespawnCount = 0;
    private float nextSpawnTime = 0f;

    void Awake()
    {
        PrewarmPool();
    }

    void Start()
    {
        nextSpawnTime = Time.time + Random.Range(0.2f, 0.8f);
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

    void PrewarmPool()
    {
        if (dronePrefab == null)
        {
            Debug.LogWarning("DroneNPC2Manager: dronePrefab 沒有設定");
            return;
        }

        int count = Mathf.Max(initialPoolSize, targetDroneCount);

        for (int i = 0; i < count; i++)
        {
            DroneNPC2 drone = Instantiate(dronePrefab, transform);
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

        if (!grid.TryGetRandomWalkablePoint(out Vector3 spawnPosition))
        {
            return false;
        }

        DroneNPC2 drone = GetDroneFromPool();

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

    DroneNPC2 GetDroneFromPool()
    {
        if (pooledDrones.Count > 0)
        {
            return pooledDrones.Dequeue();
        }

        if (!allowPoolExpansion)
        {
            return null;
        }

        DroneNPC2 drone = Instantiate(dronePrefab, transform);
        drone.gameObject.SetActive(false);
        return drone;
    }

    public void NotifyDroneFinished(DroneNPC2 drone, bool wasDestroyed)
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

    void ReturnDroneToPool(DroneNPC2 drone)
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
