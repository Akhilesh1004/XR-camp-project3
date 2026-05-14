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

    [Header("生成規則")]
    [Tooltip("是否允許多台無人機從同一個 spawn point 出生")]
    public bool allowDuplicateSpawnPoint = true;

    private readonly List<DroneNPC> aliveDrones = new List<DroneNPC>();
    private readonly HashSet<int> usedSpawnIndices = new HashSet<int>();

    private int pendingRespawnCount = 0;

    public Transform[] Waypoints
    {
        get { return spawnPoints; }
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
        aliveDrones.RemoveAll(drone => drone == null);

        int totalExpected = aliveDrones.Count + pendingRespawnCount;

        if (totalExpected < targetDroneCount)
        {
            FillDrones();
        }
    }

    void FillDrones()
    {
        while (aliveDrones.Count + pendingRespawnCount < targetDroneCount)
        {
            SpawnOneDrone();
        }
    }

    void SpawnOneDrone()
    {
        if (dronePrefab == null)
        {
            Debug.LogWarning("DroneGameManager: dronePrefab 沒有設定");
            return;
        }

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning("DroneGameManager: spawnPoints 沒有設定");
            return;
        }

        int spawnIndex = GetRandomSpawnIndex();

        if (spawnIndex < 0)
        {
            Debug.LogWarning("DroneGameManager: 找不到可用的 spawn point");
            return;
        }

        Transform spawnPoint = spawnPoints[spawnIndex];

        DroneNPC drone = Instantiate(
            dronePrefab,
            spawnPoint.position,
            spawnPoint.rotation
        );

        drone.Initialize(
            this,
            spawnPoint.position,
            spawnPoint.rotation,
            spawnIndex,
            spawnPoints
        );

        aliveDrones.Add(drone);
        usedSpawnIndices.Add(spawnIndex);
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

    public void NotifyDroneDestroyed(DroneNPC drone, int spawnIndex)
    {
        if (drone != null)
        {
            aliveDrones.Remove(drone);
        }

        if (spawnIndex >= 0)
        {
            usedSpawnIndices.Remove(spawnIndex);
        }

        StartCoroutine(RespawnAfterDelay());
    }

    IEnumerator RespawnAfterDelay()
    {
        pendingRespawnCount++;

        yield return new WaitForSeconds(respawnDelay);

        pendingRespawnCount = Mathf.Max(0, pendingRespawnCount - 1);

        aliveDrones.RemoveAll(drone => drone == null);

        if (aliveDrones.Count < targetDroneCount)
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