using System;
using UnityEngine;

public class CP_MetroStationDoors : MonoBehaviour
{
    [Serializable]
    public class ModuleDoor
    {
        [Tooltip("Door transform.")]
        public Transform door;

        [Tooltip("How far this door moves from closed position when fully open.")]
        public Vector3 openLocalOffset = new Vector3(0.8f, 0f, 0f);

        [HideInInspector] public Vector3 closedLocalPosition;
        [HideInInspector] public Vector3 openedLocalPosition;
    }

    [Header("Detection")]
    [Tooltip("Box trigger collider for this door module. It is used only as a volume reference.")]
    [SerializeField] private BoxCollider triggerZone;

    [Tooltip("Optional tag filter for train colliders.")]
    [SerializeField] private string requiredTag = "";

    [Tooltip("Optional layer mask for train colliders.")]
    [SerializeField] private LayerMask detectionMask = ~0;

    [Header("Module Doors")]
    [SerializeField] private ModuleDoor[] doors;

    [Header("Motion")]
    [Tooltip("Time in seconds to fully open.")]
    [SerializeField] private float openDuration = 0.8f;

    [Tooltip("Time in seconds to fully close.")]
    [SerializeField] private float closeDuration = 0.8f;

    private const int MaxDetectedColliders = 64;
    private readonly Collider[] overlapResults = new Collider[MaxDetectedColliders];

    private MetroSplineTrainController activeTrain;
    private bool targetOpen;
    private float transitionTime;
    private Vector3[] transitionStartPositions;

    private void Awake()
    {
        CacheDoorPositions();
        PrepareTrigger();
        SnapDoorsClosed();
    }

    private void OnEnable()
    {
        CacheDoorPositions();
        PrepareTrigger();
        SnapDoorsClosed();
    }

    private void Update()
    {
        activeTrain = FindTrainInZone();
        UpdateTargetState();
        UpdateDoorAnimation(Time.deltaTime);
    }

    private void PrepareTrigger()
    {
        if (triggerZone != null)
            triggerZone.isTrigger = true;
    }

    private void CacheDoorPositions()
    {
        if (doors == null)
            return;

        for (int i = 0; i < doors.Length; i++)
        {
            if (doors[i] == null || doors[i].door == null)
                continue;

            doors[i].closedLocalPosition = doors[i].door.localPosition;
            doors[i].openedLocalPosition = doors[i].closedLocalPosition + doors[i].openLocalOffset;
        }

        EnsureTransitionArray();
    }

    private void EnsureTransitionArray()
    {
        int count = doors != null ? doors.Length : 0;

        if (transitionStartPositions == null || transitionStartPositions.Length != count)
            transitionStartPositions = new Vector3[count];
    }

    private void SnapDoorsClosed()
    {
        if (doors == null)
            return;

        for (int i = 0; i < doors.Length; i++)
        {
            if (doors[i] == null || doors[i].door == null)
                continue;

            doors[i].door.localPosition = doors[i].closedLocalPosition;
        }

        targetOpen = false;
        transitionTime = 0f;
    }

    private MetroSplineTrainController FindTrainInZone()
    {
        if (triggerZone == null)
            return null;

        Vector3 halfExtents = Vector3.Scale(triggerZone.size, triggerZone.transform.lossyScale) * 0.5f;
        Vector3 center = triggerZone.transform.TransformPoint(triggerZone.center);
        Quaternion rotation = triggerZone.transform.rotation;

        int hitCount = Physics.OverlapBoxNonAlloc(
            center,
            halfExtents,
            overlapResults,
            rotation,
            detectionMask,
            QueryTriggerInteraction.Collide);

        MetroSplineTrainController foundTrain = null;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = overlapResults[i];
            if (hit == null)
                continue;

            if (!PassesTagFilter(hit))
                continue;

            MetroSplineTrainController train = hit.GetComponentInParent<MetroSplineTrainController>();
            if (train == null)
                continue;

            foundTrain = train;
            break;
        }

        for (int i = 0; i < hitCount; i++)
            overlapResults[i] = null;

        return foundTrain;
    }

    private void UpdateTargetState()
    {
        bool newTargetOpen = false;

        if (activeTrain != null)
        {
            bool trainDoorsOpen = activeTrain.AreLeftDoorsOpen || activeTrain.AreRightDoorsOpen;
            newTargetOpen = activeTrain.IsDwellingAtStation && trainDoorsOpen;
        }

        if (newTargetOpen != targetOpen)
        {
            targetOpen = newTargetOpen;
            transitionTime = 0f;
            CaptureCurrentDoorPositions();
        }
    }

    private void CaptureCurrentDoorPositions()
    {
        EnsureTransitionArray();

        if (doors == null)
            return;

        for (int i = 0; i < doors.Length; i++)
        {
            if (doors[i] == null || doors[i].door == null)
                continue;

            transitionStartPositions[i] = doors[i].door.localPosition;
        }
    }

    private void UpdateDoorAnimation(float dt)
    {
        float duration = targetOpen ? Mathf.Max(0.01f, openDuration) : Mathf.Max(0.01f, closeDuration);

        transitionTime += dt;
        float linearT = Mathf.Clamp01(transitionTime / duration);
        float easedT = linearT * linearT;

        if (doors != null)
        {
            for (int i = 0; i < doors.Length; i++)
            {
                if (doors[i] == null || doors[i].door == null)
                    continue;

                Vector3 targetPosition = targetOpen
                    ? doors[i].openedLocalPosition
                    : doors[i].closedLocalPosition;

                doors[i].door.localPosition = Vector3.Lerp(
                    transitionStartPositions[i],
                    targetPosition,
                    easedT);
            }
        }
    }

    private bool PassesTagFilter(Collider other)
    {
        if (string.IsNullOrWhiteSpace(requiredTag))
            return true;

        return other.CompareTag(requiredTag);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        openDuration = Mathf.Max(0.01f, openDuration);
        closeDuration = Mathf.Max(0.01f, closeDuration);

        if (triggerZone != null)
            triggerZone.isTrigger = true;
    }
#endif
}
