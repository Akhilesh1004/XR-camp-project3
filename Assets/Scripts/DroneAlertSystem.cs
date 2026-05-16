using System;
using System.Collections.Generic;
using UnityEngine;

public static class DroneAlertSystem
{
    public static event Action<Vector3, float, float> OnDroneNPC2Destroyed;

    private static readonly List<DroneNPC> registeredDrones = new List<DroneNPC>();
    private static readonly List<DroneNPC> candidateDrones = new List<DroneNPC>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        registeredDrones.Clear();
        candidateDrones.Clear();
        OnDroneNPC2Destroyed = null;
    }

    public static void RegisterDrone(DroneNPC drone)
    {
        if (drone == null)
        {
            return;
        }

        if (!registeredDrones.Contains(drone))
        {
            registeredDrones.Add(drone);
        }
    }

    public static void UnregisterDrone(DroneNPC drone)
    {
        if (drone == null)
        {
            return;
        }

        registeredDrones.Remove(drone);
        candidateDrones.Remove(drone);
    }

    public static void BroadcastDroneNPC2Destroyed(
        Vector3 alertPosition,
        float alertDuration,
        float alertDetectRange,
        int forcedHunterCount = 0,
        bool chooseClosestHuntersToPlayer = true
    )
    {
        OnDroneNPC2Destroyed?.Invoke(alertPosition, alertDuration, alertDetectRange);

        ActivateForcedHunters(
            alertPosition,
            forcedHunterCount,
            chooseClosestHuntersToPlayer
        );
    }

    private static void ActivateForcedHunters(
        Vector3 alertPosition,
        int forcedHunterCount,
        bool chooseClosestHuntersToPlayer
    )
    {
        if (forcedHunterCount <= 0)
        {
            return;
        }

        candidateDrones.Clear();

        for (int i = registeredDrones.Count - 1; i >= 0; i--)
        {
            DroneNPC drone = registeredDrones[i];

            if (drone == null)
            {
                registeredDrones.RemoveAt(i);
                continue;
            }

            if (!drone.CanBecomeForcedHunter)
            {
                continue;
            }

            candidateDrones.Add(drone);
        }

        if (candidateDrones.Count == 0)
        {
            return;
        }

        candidateDrones.Sort((a, b) =>
        {
            float da = a.GetForcedHuntSelectionDistance(
                alertPosition,
                chooseClosestHuntersToPlayer
            );

            float db = b.GetForcedHuntSelectionDistance(
                alertPosition,
                chooseClosestHuntersToPlayer
            );

            return da.CompareTo(db);
        });

        int count = Mathf.Min(forcedHunterCount, candidateDrones.Count);

        for (int i = 0; i < count; i++)
        {
            candidateDrones[i].BeginForcedHunt();
        }
    }
}