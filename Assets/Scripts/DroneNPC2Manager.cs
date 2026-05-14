using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DroneNPC2Manager : MonoBehaviour
{
    [Header("DroneNPC2 Prefab")]
    public DroneNPC2 dronePrefab;

    [Header("出生點 / Waypoints")]
    public Transform[] spawnPoints;

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
    [Tooltip("場景中的玩家 Camera，例如 CenterEyeAnchor 上的 Camera")]
    public Camera playerCamera;

    [Tooltip("用來判斷是否被建築遮住，例如 Building / Wall")]
    public LayerMask visibilityBlockerLayer;

    [Tooltip("是否要求 DroneNPC2 只能在玩家看不到的 spawn point 出現")]
    public bool spawnOnlyWhenHiddenFromPlayer = true;

    [Tooltip("出生點離玩家鏡頭至少多遠")]
    public float minHiddenSpawnDistance = 25f;

    [Tooltip("視野邊界容忍值，越大代表越嚴格要離開畫面")]
    public float viewportPadding = 0.1f;

    [Tooltip("找不到玩家看不到的位置時，是否允許在看得到的位置生成")]
    public bool allowVisibleSpawnFallback = false;

    [Tooltip("找不到隱藏出生點時，隔多久再嘗試生成")]
    public float hiddenSpawnRetryDelay = 1f;

    private readonly List<DroneNPC2> activeDrones = new List<DroneNPC2>();
    private readonly Queue<DroneNPC2> pooledDrones = new Queue<DroneNPC2>();
    private readonly HashSet<int> usedSpawnIndices = new HashSet<int>();

    private int pendingRespawnCount = 0;
    private float nextSpawnAttemptTime = 0f;

    public Transform[] Waypoints
    {
        get { return spawnPoints; }
    }

    void Awake()
    {
        PrewarmPool();
    }

    void Start()
    {
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
            Debug.LogWarning("DroneNPC2Manager: dronePrefab 沒有設定");
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
            Debug.LogWarning("DroneNPC2Manager: Pool 不足，而且不允許擴充");
            return false;
        }

        Transform spawnPoint = spawnPoints[spawnIndex];

        drone.Initialize(
            this,
            spawnPoint.position,
            spawnPoint.rotation,
            spawnIndex,
            spawnPoints
        );

        // 做法 B：Manager 把場景中的 Camera / 遮蔽 Layer 傳給 DroneNPC2
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

        // 在鏡頭後面，或畫面外，算玩家看不到
        if (!inFront || !insideView)
        {
            return true;
        }

        // 在畫面內，但中間被建築擋住，也算玩家看不到
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

    void OnDrawGizmosSelected()
    {
        if (spawnPoints == null)
        {
            return;
        }

        Gizmos.color = Color.cyan;

        foreach (Transform point in spawnPoints)
        {
            if (point != null)
            {
                Gizmos.DrawWireSphere(point.position, 0.4f);
            }
        }
    }
}