using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DroneGameManager : MonoBehaviour
{
    [Header("無人機 Prefab")]
    public DroneNPC dronePrefab;

    [Header("出生點 / Waypoints")]
    public Transform[] spawnPoints;

    [Header("場上數量")]
    public int targetDroneCount = 3;
    public bool spawnOnStart = true;
    public float respawnDelay = 3f;

    [Header("Object Pool")]
    public int initialPoolSize = 5;
    public bool allowPoolExpansion = true;

    [Header("生成規則")]
    public bool allowDuplicateSpawnPoint = false;

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
            return false;
        }

        DroneNPC drone = GetDroneFromPool();

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
            if (point != null)
            {
                Gizmos.DrawWireSphere(point.position, 0.4f);
            }
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
                if (spawnPoints[j] != null)
                {
                    Gizmos.DrawLine(spawnPoints[i].position, spawnPoints[j].position);
                }
            }
        }
    }
}