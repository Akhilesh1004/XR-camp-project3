using System;
using UnityEngine;

public static class DroneAlertSystem
{
    public static event Action<Vector3, float, float> OnDroneNPC2Destroyed;

    public static void BroadcastDroneNPC2Destroyed(
        Vector3 alertPosition,
        float alertDuration,
        float alertDetectRange
    )
    {
        OnDroneNPC2Destroyed?.Invoke(alertPosition, alertDuration, alertDetectRange);
    }
}