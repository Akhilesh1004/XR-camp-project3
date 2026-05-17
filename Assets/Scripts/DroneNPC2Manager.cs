using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DroneNPC2Manager : MonoBehaviour
{
    [Header("DroneNPC2 Prefab")]
    public DroneNPC2 dronePrefab;

    [Header("出生點 / Waypoints")]
    public Transform[] spawnPoints;

    [Header("Waypoint Graph A*")]
    public DroneWaypointGraph waypointGraph;

    [Header("場上數量")]
    public int targetDroneCount = 2;
    public bool spawnOnStart = true;
    public float respawnDelay = 5f;

    [Header("Object Pool")]
    public int initialPoolSize = 2;
    public bool allowPoolExpansion = true;

    [Header("生成規則")]
    public bool allowDuplicateSpawnPoint = true;

    [Header("玩家視野外生成")]
    public Camera playerCamera;
    public LayerMask visibilityBlockerLayer;
    public bool spawnOnlyWhenHiddenFromPlayer = true;
    public float minHiddenSpawnDistance = 25f;
    public float viewportPadding = 0.1f;
    public bool allowVisibleSpawnFallback = false;
    public float hiddenSpawnRetryDelay = 1f;

    private readonly List<DroneNPC2> activeDrones = new List<DroneNPC2>();
    private readonly Queue<DroneNPC2> pooledDrones = new Queue<DroneNPC2>();
    private readonly HashSet<int> usedSpawnIndices = new HashSet<int>();

    private int pendingRespawnCount = 0;
    private float nextSpawnAttemptTime = 0f;

    void Awake()
    {
        if (waypointGraph != null && waypointGraph.waypoints == null)
        {
            waypointGraph.SetWaypoints(spawnPoints, true);
        }

        PrewarmPool();
    }

    void Start()
    {
        if (waypointGraph != null)
        {
            waypointGraph.SetWaypoints(spawnPoints, true);
        }

        if (spawnOnStart)
        {
            FillDrones();
        }
    }

    void Update()
    {
        activeDrones.RemoveAll(drone => drone == null || !drone.gameObject.activeSelf);

        if (Time.time < nextSpawnAttemptTime)
        {
            return;
        }

        int expectedCount = activeDrones.Count + pendingRespawnCount;

        if (expectedCount < targetDroneCount)
        {
            FillDrones();
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

    void FillDrones()
    {
        while (activeDrones.Count + pendingRespawnCount < targetDroneCount)
        {
            bool success = SpawnOneDrone();

            if (!success)
            {
                break;
            }
        }
    }

    bool SpawnOneDrone()
    {
        if (dronePrefab == null)
        {
            return false;
        }

        if (spawnPoints == null || spawnPoints.Length < 2)
        {
            Debug.LogWarning("DroneNPC2Manager: spawnPoints 至少需要 2 個");
            return false;
        }

        int spawnIndex = GetRandomSpawnIndex();

        if (spawnIndex < 0)
        {
            return false;
        }

        DroneNPC2 drone = GetDroneFromPool();

        if (drone == null)
        {
            return false;
        }

        Transform spawnPoint = spawnPoints[spawnIndex];

        drone.Initialize(
            this,
            spawnPoint.position,
            spawnPoint.rotation,
            spawnIndex,
            spawnPoints,
            waypointGraph
        );

        drone.SetVisibilityContext(playerCamera, visibilityBlockerLayer);

        drone.gameObject.SetActive(true);

        activeDrones.Add(drone);
        usedSpawnIndices.Add(spawnIndex);

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

    int GetRandomSpawnIndex()
    {
        List<int> hiddenCandidates = new List<int>();
        List<int> visibleFallbackCandidates = new List<int>();

        for (int i = 0; i < spawnPoints.Length; i++)
        {
            if (spawnPoints[i] == null)
            {
                continue;
            }

            if (!allowDuplicateSpawnPoint && usedSpawnIndices.Contains(i))
            {
                continue;
            }

            bool hidden = IsHiddenFromPlayer(spawnPoints[i].position);

            if (!spawnOnlyWhenHiddenFromPlayer || hidden)
            {
                hiddenCandidates.Add(i);
            }
            else
            {
                visibleFallbackCandidates.Add(i);
            }
        }

        if (hiddenCandidates.Count > 0)
        {
            return hiddenCandidates[Random.Range(0, hiddenCandidates.Count)];
        }

        if (allowVisibleSpawnFallback && visibleFallbackCandidates.Count > 0)
        {
            return visibleFallbackCandidates[Random.Range(0, visibleFallbackCandidates.Count)];
        }

        nextSpawnAttemptTime = Time.time + hiddenSpawnRetryDelay;
        return -1;
    }

    bool IsHiddenFromPlayer(Vector3 worldPosition)
    {
        Camera cam = playerCamera != null ? playerCamera : Camera.main;

        if (cam == null)
        {
            return true;
        }

        Vector3 cameraPosition = cam.transform.position;
        float distance = Vector3.Distance(cameraPosition, worldPosition);

        if (distance < minHiddenSpawnDistance)
        {
            return false;
        }

        Vector3 viewportPoint = cam.WorldToViewportPoint(worldPosition);

        bool inFront = viewportPoint.z > 0f;

        bool insideView =
            viewportPoint.x >= -viewportPadding &&
            viewportPoint.x <= 1f + viewportPadding &&
            viewportPoint.y >= -viewportPadding &&
            viewportPoint.y <= 1f + viewportPadding;

        if (!inFront || !insideView)
        {
            return true;
        }

        if (visibilityBlockerLayer.value != 0)
        {
            Vector3 direction = worldPosition - cameraPosition;
            float rayDistance = direction.magnitude;

            if (rayDistance > 0.01f)
            {
                direction.Normalize();

                if (Physics.Raycast(
                    cameraPosition,
                    direction,
                    out RaycastHit hit,
                    rayDistance,
                    visibilityBlockerLayer,
                    QueryTriggerInteraction.Ignore))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public void NotifyDroneFinished(DroneNPC2 drone, int spawnIndex, bool wasDestroyed)
    {
        if (drone == null)
        {
            return;
        }

        activeDrones.Remove(drone);

        if (spawnIndex >= 0)
        {
            usedSpawnIndices.Remove(spawnIndex);
        }

        ReturnDroneToPool(drone);

        StartCoroutine(RespawnAfterDelay());
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

    IEnumerator RespawnAfterDelay()
    {
        pendingRespawnCount++;

        yield return new WaitForSeconds(respawnDelay);

        pendingRespawnCount = Mathf.Max(0, pendingRespawnCount - 1);

        activeDrones.RemoveAll(drone => drone == null || !drone.gameObject.activeSelf);

        if (activeDrones.Count < targetDroneCount)
        {
            SpawnOneDrone();
        }
    }
}