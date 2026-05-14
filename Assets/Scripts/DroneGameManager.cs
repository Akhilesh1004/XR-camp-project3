using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DroneGameManager : MonoBehaviour
{
    [Header("無人機 Prefab")]
    public DroneNPC dronePrefab;

    [Header("出生點 / Waypoints")]
    [Tooltip("這些點同時作為無人機出生點與飛行導航 waypoint")]
    public Transform[] spawnPoints;

    [Header("場上數量")]
    [Tooltip("場上應該維持幾台無人機")]
    public int targetDroneCount = 3;

    [Tooltip("遊戲開始時是否自動生成")]
    public bool spawnOnStart = true;

    [Tooltip("無人機爆炸後幾秒補一台")]
    public float respawnDelay = 3f;

    [Header("Object Pool 設定")]
    [Tooltip("一開始預先建立幾台無人機")]
    public int initialPoolSize = 8;

    [Tooltip("Pool 不夠時是否允許動態新增")]
    public bool allowPoolExpansion = true;

    [Header("生成規則")]
    [Tooltip("是否允許多台無人機從同一個 spawn point 出生")]
    public bool allowDuplicateSpawnPoint = true;

    private readonly List<DroneNPC> activeDrones = new List<DroneNPC>();
    private readonly Queue<DroneNPC> pooledDrones = new Queue<DroneNPC>();
    private readonly HashSet<int> usedSpawnIndices = new HashSet<int>();

    private int pendingRespawnCount = 0;

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

        int totalExpected = activeDrones.Count + pendingRespawnCount;

        if (totalExpected < targetDroneCount)
        {
            FillDrones();
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
            Debug.LogWarning("DroneGameManager: dronePrefab 沒有設定");
            return false;
        }

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning("DroneGameManager: spawnPoints 沒有設定");
            return false;
        }

        int spawnIndex = GetRandomSpawnIndex();

        if (spawnIndex < 0)
        {
            Debug.LogWarning("DroneGameManager: 找不到可用的 spawn point");
            return false;
        }

        DroneNPC drone = GetDroneFromPool();

        if (drone == null)
        {
            Debug.LogWarning("DroneGameManager: Pool 裡沒有可用的 Drone，而且不允許擴充");
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

        drone.gameObject.SetActive(true);

        activeDrones.Add(drone);
        usedSpawnIndices.Add(spawnIndex);

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

    int GetRandomSpawnIndex()
    {
        if (allowDuplicateSpawnPoint)
        {
            return Random.Range(0, spawnPoints.Length);
        }

        List<int> candidates = new List<int>();

        for (int i = 0; i < spawnPoints.Length; i++)
        {
            if (!usedSpawnIndices.Contains(i))
            {
                candidates.Add(i);
            }
        }

        if (candidates.Count == 0)
        {
            return -1;
        }

        return candidates[Random.Range(0, candidates.Count)];
    }

    public void NotifyDroneExploded(DroneNPC drone, int spawnIndex)
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

    void ReturnDroneToPool(DroneNPC drone)
    {
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
            if (point == null)
            {
                continue;
            }

            Gizmos.DrawWireSphere(point.position, 0.4f);
        }

        Gizmos.color = Color.blue;

        for (int i = 0; i < spawnPoints.Length; i++)
        {
            if (spawnPoints[i] == null)
            {
                continue;
            }

            for (int j = i + 1; j < spawnPoints.Length; j++)
            {
                if (spawnPoints[j] == null)
                {
                    continue;
                }

                Gizmos.DrawLine(spawnPoints[i].position, spawnPoints[j].position);
            }
        }
    }
}