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

    [Header("Performance")]
    public float activeListCleanupInterval = 1f;

    [Header("Spawn Visibility")]
    [Tooltip("補 Drone 時避開相機視野，避免 object pool 啟用時直接 pop-in。")]
    public bool avoidVisibleSpawn = true;
    public int spawnPositionMaxAttempts = 12;
    public float preventVisibleSpawnDistance = 420f;
    public float spawnViewportPadding = 0.08f;

    [Header("Large World Local Population")]
    [Tooltip("只維持玩家附近固定數量的送貨 Drone。遠距 Drone 會回到 pool 並在玩家附近補回。")]
    public bool restrictPopulationToPlayerArea = false;
    public string playerTag = "Player";
    public float localSpawnMinDistance = 60f;
    public float localSpawnMaxDistance = 300f;
    public float localDestinationMaxDistanceFromPlayer = 360f;
    public float localRecycleDistance = 440f;
    public float localPopulationRefreshInterval = 0.5f;
    public int maxLocalRecyclesPerRefresh = 4;
    public int localDestinationSampleAttempts = 12;

    private readonly List<DroneNPC2> activeDrones = new List<DroneNPC2>();
    private readonly Queue<DroneNPC2> pooledDrones = new Queue<DroneNPC2>();

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
        nextSpawnTime = Time.time + Random.Range(0.2f, 0.8f);
    }

    void Update()
    {
        CleanupInactiveDronesThrottled();
        RecycleDistantDronesThrottled();

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

    void RecycleDistantDronesThrottled()
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
            DroneNPC2 drone = activeDrones[i];

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
            DroneNPC2 drone = activeDrones[i];

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

    void FindPlayer()
    {
        GameObject playerObject = GameObject.FindGameObjectWithTag(playerTag);

        if (playerObject != null)
        {
            player = playerObject.transform;
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
        else if (!grid.TryGetRandomWalkablePoint(out spawnPosition))
        {
            return false;
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

    public bool TryGetDeliveryDestination(
        Vector3 origin,
        float minDistance,
        out Vector3 destination
    )
    {
        destination = Vector3.zero;

        if (grid == null || !grid.IsReady)
        {
            return false;
        }

        if (!restrictPopulationToPlayerArea)
        {
            return grid.TryGetRandomWalkablePointFarFrom(
                origin,
                minDistance,
                out destination
            );
        }

        if (player == null)
        {
            FindPlayer();
        }

        if (player == null)
        {
            return false;
        }

        float minDistanceSqr = minDistance * minDistance;
        int attempts = Mathf.Max(1, localDestinationSampleAttempts);

        for (int i = 0; i < attempts; i++)
        {
            if (!grid.TryGetRandomWalkablePointNear(
                player.position,
                localDestinationMaxDistanceFromPlayer,
                out Vector3 candidate))
            {
                return false;
            }

            if ((candidate - origin).sqrMagnitude >= minDistanceSqr)
            {
                destination = candidate;
                return true;
            }
        }

        return false;
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
