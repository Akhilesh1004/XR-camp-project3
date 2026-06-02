using System.Collections.Generic;
using UnityEngine;

public class DroneGameManager : MonoBehaviour
{
    [Header("DroneNPC Prefab")]
    public DroneNPC dronePrefab;

    [Header("3D Grid")]
    public DroneWaypointGraph grid;

    [Header("場上數量")]
    public int targetDroneCount = 110;
    public bool spawnOnStart = true;
    public float respawnDelay = 3f;

    [Header("Spawn Throttle")]
    [Tooltip("一次只補一台，避免開場多台 Drone 同一幀 A* 導致 FPS spike")]
    public float spawnInterval = 0.08f;

    [Header("Object Pool")]
    public int initialPoolSize = 110;
    public bool allowPoolExpansion = true;

    [Header("Performance")]
    public float activeListCleanupInterval = 1f;

    [Header("生成設定")]
    public string playerTag = "Player";
    public bool avoidSpawnNearPlayer = true;
    public float minSpawnDistanceFromPlayer = 100f;

    [Header("Spawn Visibility")]
    [Tooltip("補 Drone 時避開相機視野，避免 object pool 啟用時直接 pop-in。")]
    public bool avoidVisibleSpawn = true;
    public int spawnPositionMaxAttempts = 12;
    public float preventVisibleSpawnDistance = 420f;
    public float spawnViewportPadding = 0.08f;

    [Header("Large World Local Population")]
    [Tooltip("只維持玩家附近固定數量的 active Drone。遠距巡邏 Drone 會回到 pool，追擊與警戒 Drone 不會被回收。")]
    public bool restrictPopulationToPlayerArea = false;
    public float localSpawnMinDistance = 80f;
    public float localSpawnMaxDistance = 300f;
    public float localRecycleDistance = 420f;
    public float localPopulationRefreshInterval = 0.5f;
    public int maxLocalRecyclesPerRefresh = 4;

    private readonly List<DroneNPC> activeDrones = new List<DroneNPC>();
    private readonly Queue<DroneNPC> pooledDrones = new Queue<DroneNPC>();

    private int pendingRespawnCount = 0;
    private Transform player;
    private Camera spawnVisibilityCamera;
    private float nextSpawnTime = 0f;
    private float nextActiveListCleanupTime = 0f;
    private float nextLocalPopulationRefreshTime = 0f;

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
        CleanupInactiveDronesThrottled();
        RecycleDistantPatrolDronesThrottled();

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

    void RecycleDistantPatrolDronesThrottled()
    {
        if (!restrictPopulationToPlayerArea ||
            Time.time < nextLocalPopulationRefreshTime)
        {
            return;
        }

        nextLocalPopulationRefreshTime =
            Time.time +
            Mathf.Max(0.1f, localPopulationRefreshInterval);

        if (player == null)
        {
            FindPlayer();
        }

        if (player == null)
        {
            return;
        }

        float recycleDistance = Mathf.Max(localSpawnMaxDistance, localRecycleDistance);
        float recycleDistanceSqr = recycleDistance * recycleDistance;
        int maxRecycles = Mathf.Max(1, maxLocalRecyclesPerRefresh);
        int recycled = 0;

        for (int i = activeDrones.Count - 1; i >= 0; i--)
        {
            DroneNPC drone = activeDrones[i];

            if (drone == null ||
                !drone.CanRecycleForLocalPopulation ||
                (drone.transform.position - player.position).sqrMagnitude <= recycleDistanceSqr)
            {
                continue;
            }

            activeDrones.RemoveAt(i);
            ReturnDroneToPool(drone);
            recycled++;

            if (recycled >= maxRecycles)
            {
                break;
            }
        }
    }

    void CleanupInactiveDronesThrottled()
    {
        if (Time.time < nextActiveListCleanupTime)
        {
            return;
        }

        nextActiveListCleanupTime = Time.time + Mathf.Max(0.1f, activeListCleanupInterval);

        for (int i = activeDrones.Count - 1; i >= 0; i--)
        {
            DroneNPC drone = activeDrones[i];

            if (drone == null || !drone.gameObject.activeSelf)
            {
                activeDrones.RemoveAt(i);
            }
        }
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

        if (!TryGetSpawnPosition(out Vector3 spawnPosition))
        {
            return false;
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

    bool TryGetSpawnPosition(out Vector3 spawnPosition)
    {
        int attempts = avoidVisibleSpawn
            ? Mathf.Max(1, spawnPositionMaxAttempts)
            : 1;

        for (int i = 0; i < attempts; i++)
        {
            if (!TryGetSpawnPositionCandidate(out spawnPosition))
            {
                return false;
            }

            if (!avoidVisibleSpawn || !IsLikelyVisibleSpawnPosition(spawnPosition))
            {
                return true;
            }
        }

        spawnPosition = Vector3.zero;
        return false;
    }

    bool TryGetSpawnPositionCandidate(out Vector3 spawnPosition)
    {
        spawnPosition = Vector3.zero;

        if (restrictPopulationToPlayerArea)
        {
            if (player == null)
            {
                FindPlayer();
            }

            if (player == null ||
                !grid.TryGetRandomWalkablePointInRange(
                    player.position,
                    localSpawnMinDistance,
                    localSpawnMaxDistance,
                    out spawnPosition))
            {
                return false;
            }
        }
        else if (avoidSpawnNearPlayer)
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

        return true;
    }

    bool IsLikelyVisibleSpawnPosition(Vector3 position)
    {
        if (spawnVisibilityCamera == null)
        {
            spawnVisibilityCamera = Camera.main;
        }

        if (spawnVisibilityCamera == null)
        {
            return false;
        }

        float distance = Mathf.Max(1f, preventVisibleSpawnDistance);

        if ((position - spawnVisibilityCamera.transform.position).sqrMagnitude >
            distance * distance)
        {
            return false;
        }

        Vector3 viewport = spawnVisibilityCamera.WorldToViewportPoint(position);
        float padding = Mathf.Max(0f, spawnViewportPadding);

        return viewport.z > 0f &&
               viewport.x >= -padding &&
               viewport.x <= 1f + padding &&
               viewport.y >= -padding &&
               viewport.y <= 1f + padding;
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
