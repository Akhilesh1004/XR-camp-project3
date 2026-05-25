
using System.Collections.Generic;
using UnityEngine;

public class DroneCrowdDirector : MonoBehaviour
{
    public static DroneCrowdDirector Instance { get; private set; }

    [Header("Attack Limits")]
    public int maxChasingDrones = 16;
    public int maxCloseAttackDrones = 4;
    public float cleanupInterval = 0.5f;

    private readonly HashSet<DroneNPC> chasingDrones = new HashSet<DroneNPC>();
    private readonly HashSet<DroneNPC> closeAttackDrones = new HashSet<DroneNPC>();
    private readonly List<DroneNPC> staleDrones = new List<DroneNPC>();
    private float nextCleanupTime = 0f;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Debug.LogWarning("場上有多個 DroneCrowdDirector，會使用第一個 Instance。");
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public static DroneCrowdDirector GetOrCreate()
    {
        if (Instance != null)
        {
            return Instance;
        }

        DroneCrowdDirector existing = FindObjectOfType<DroneCrowdDirector>();

        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }

        GameObject obj = new GameObject("DroneCrowdDirector");
        Instance = obj.AddComponent<DroneCrowdDirector>();
        return Instance;
    }

    public bool TryEnterChase(DroneNPC drone)
    {
        CleanupNulls();

        if (drone == null)
        {
            return false;
        }

        if (chasingDrones.Contains(drone))
        {
            return true;
        }

        if (chasingDrones.Count >= maxChasingDrones)
        {
            return false;
        }

        chasingDrones.Add(drone);
        return true;
    }

    public void ExitChase(DroneNPC drone)
    {
        if (drone == null)
        {
            return;
        }

        chasingDrones.Remove(drone);
        closeAttackDrones.Remove(drone);
    }

    public bool TryEnterCloseAttack(DroneNPC drone)
    {
        CleanupNulls();

        if (drone == null)
        {
            return false;
        }

        if (closeAttackDrones.Contains(drone))
        {
            return true;
        }

        if (closeAttackDrones.Count >= maxCloseAttackDrones)
        {
            return false;
        }

        closeAttackDrones.Add(drone);
        return true;
    }

    public void ExitCloseAttack(DroneNPC drone)
    {
        if (drone == null)
        {
            return;
        }

        closeAttackDrones.Remove(drone);
    }

    void CleanupNulls()
    {
        if (Time.time < nextCleanupTime)
        {
            return;
        }

        nextCleanupTime = Time.time + Mathf.Max(0.1f, cleanupInterval);

        RemoveInvalidDrones(chasingDrones);
        RemoveInvalidDrones(closeAttackDrones);
    }

    void RemoveInvalidDrones(HashSet<DroneNPC> drones)
    {
        staleDrones.Clear();

        foreach (DroneNPC drone in drones)
        {
            if (drone == null || !drone.gameObject.activeInHierarchy)
            {
                staleDrones.Add(drone);
            }
        }

        for (int i = 0; i < staleDrones.Count; i++)
        {
            drones.Remove(staleDrones[i]);
        }
    }
}
