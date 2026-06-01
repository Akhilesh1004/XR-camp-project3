using System;
using System.Collections.Generic;
using UnityEngine;

public class MetroSplineTrainController : MonoBehaviour
{
    public enum ModelForwardAxis
    {
        PositiveZ,
        NegativeZ,
        PositiveX,
        NegativeX
    }

    [Serializable]
    public class DoorPanel
    {
        [Tooltip("Door transform that moves in local space.")]
        public Transform door;

        [Tooltip("Local offset when the door is fully open.")]
        public Vector3 openLocalOffset = new Vector3(0.6f, 0f, 0f);

        [HideInInspector] public Vector3 closedLocalPosition;
    }

    [Serializable]
    public class TrainCar
    {
        [Tooltip("Root transform of this car.")]
        public Transform root;

        [Tooltip("Distance from the previous car along the spline.")]
        public float spacingFromPrevious = 8f;

        [Tooltip("Doors on the left side.")]
        public DoorPanel[] leftDoors;

        [Tooltip("Doors on the right side.")]
        public DoorPanel[] rightDoors;
    }

    [Serializable]
    public class StationStop
    {
        [Tooltip("Scene transform that marks the station stop position.")]
        public Transform stopPoint;

        [Tooltip("How long the train waits at this stop.")]
        public float dwellTime = 3f;

        [Tooltip("Open left doors at this stop.")]
        public bool openLeftDoors = true;

        [Tooltip("Open right doors at this stop.")]
        public bool openRightDoors = false;
    }

    private enum TrainState
    {
        Moving,
        Dwelling
    }

    private struct SplineSample
    {
        public float distance;
        public Vector3 position;
        public Vector3 forward;
    }

    private class RuntimeStation
    {
        public Transform stopPoint;
        public float routeDistance;
        public float dwellTime;
        public bool openLeftDoors;
        public bool openRightDoors;
    }

    [Header("Spline Path")]
    [Tooltip("Visible path points in scene order.")]
    [SerializeField] private Transform[] waypoints;

    [Tooltip("If enabled, the train reverses at the ends.")]
    [SerializeField] private bool pingPong = true;

    [Header("Spline Quality")]
    [Tooltip("How many samples per spline segment are used for lookup.")]
    [SerializeField] private int samplesPerSegment = 30;

    [Header("Train")]
    [Tooltip("Cars in order. First car is the head car.")]
    [SerializeField] private TrainCar[] cars;

    [Header("Movement")]
    [Tooltip("Cruise speed in units per second.")]
    [SerializeField] private float cruiseSpeed = 12f;

    [Tooltip("Acceleration in units per second squared.")]
    [SerializeField] private float acceleration = 3f;

    [Tooltip("Braking in units per second squared.")]
    [SerializeField] private float braking = 5f;

    [Tooltip("Distance threshold for snapping exactly to stop.")]
    [SerializeField] private float stopSnapDistance = 0.05f;

    [Tooltip("Moves the train stop point forward/backward along the route relative to the stop marker. Positive value means the head car stops a bit earlier in its travel direction.")]
    [SerializeField] private float stopAlignmentOffset = 0f;

    [Header("Model Orientation")]
    [Tooltip("Which local axis of the train model is considered forward.")]
    [SerializeField] private ModelForwardAxis modelForwardAxis = ModelForwardAxis.PositiveZ;

    [Tooltip("Optional extra euler rotation after axis correction.")]
    [SerializeField] private Vector3 modelRotationOffset = Vector3.zero;

    [Header("Doors")]
    [Tooltip("Door opening speed.")]
    [SerializeField] private float doorOpenSpeed = 2.5f;

    [Tooltip("Door closing speed.")]
    [SerializeField] private float doorCloseSpeed = 3.5f;

    [Header("Stations")]
    [Tooltip("Stops assigned by transform.")]
    [SerializeField] private StationStop[] stations;

    [Header("Startup")]
    [Tooltip("If true, train starts automatically.")]
    [SerializeField] private bool playOnStart = true;

    [Tooltip("Initial normalized position of the head car on route.")]
    [Range(0f, 1f)]
    [SerializeField] private float startNormalizedPosition = 0f;

    [Tooltip("1 = forward, -1 = backward.")]
    [SerializeField] private int startDirection = 1;

    [Tooltip("If true, train starts already moving.")]
    [SerializeField] private bool startMoving = true;

    private readonly List<SplineSample> samples = new List<SplineSample>();
    private readonly List<RuntimeStation> runtimeStations = new List<RuntimeStation>();

    private float totalRouteLength;
    private float headDistance;
    private float currentSpeed;
    private int currentDirection = 1;
    private bool isPlaying;

    private TrainState state = TrainState.Moving;
    private float dwellTimer;

    private bool doorsLeftOpen;
    private bool doorsRightOpen;

    public bool AreLeftDoorsOpen => doorsLeftOpen;
    public bool AreRightDoorsOpen => doorsRightOpen;
    public bool IsDwellingAtStation => state == TrainState.Dwelling;

    private void Start()
    {
        RebuildSpline();
        CacheClosedDoorPositions();

        currentDirection = startDirection >= 0 ? 1 : -1;
        headDistance = Mathf.Clamp01(startNormalizedPosition) * totalRouteLength;
        currentSpeed = startMoving ? cruiseSpeed : 0f;

        UpdateTrainTransformsImmediate();

        if (playOnStart)
            isPlaying = true;
    }

    private void Update()
    {
        if (!ValidateRuntime())
            return;

        UpdateDoors(Time.deltaTime);

        if (!isPlaying)
            return;

        if (state == TrainState.Dwelling)
            UpdateDwelling(Time.deltaTime);
        else
            UpdateMovement(Time.deltaTime);

        UpdateTrainTransformsImmediate();
    }

    public void Play()
    {
        isPlaying = true;
    }

    public void StopTrain()
    {
        isPlaying = false;
        currentSpeed = 0f;
    }

    [ContextMenu("Rebuild Spline")]
    public void RebuildSpline()
    {
        BuildSplineSamples();
        BuildStations();
        headDistance = Mathf.Clamp(headDistance, 0f, totalRouteLength);
    }

    private void BuildSplineSamples()
    {
        samples.Clear();
        totalRouteLength = 0f;

        int validWaypointCount = GetValidWaypointCount();
        if (validWaypointCount < 2)
            return;

        int segmentCount = validWaypointCount - 1;
        int stepsPerSegment = Mathf.Max(3, samplesPerSegment);

        Vector3 prevPos = GetCatmullRomPosition(0, 0f);
        Vector3 previewNext = GetCatmullRomPosition(0, 0.01f);
        Vector3 prevForward = (previewNext - prevPos).normalized;

        if (prevForward.sqrMagnitude < 0.0001f)
            prevForward = Vector3.forward;

        samples.Add(new SplineSample
        {
            distance = 0f,
            position = prevPos,
            forward = prevForward
        });

        for (int seg = 0; seg < segmentCount; seg++)
        {
            for (int step = 1; step <= stepsPerSegment; step++)
            {
                float t = step / (float)stepsPerSegment;
                Vector3 pos = GetCatmullRomPosition(seg, t);
                Vector3 tangent = GetCatmullRomTangent(seg, t).normalized;

                totalRouteLength += Vector3.Distance(prevPos, pos);

                if (tangent.sqrMagnitude < 0.0001f)
                    tangent = (pos - prevPos).normalized;

                if (tangent.sqrMagnitude < 0.0001f)
                    tangent = prevForward;

                samples.Add(new SplineSample
                {
                    distance = totalRouteLength,
                    position = pos,
                    forward = tangent
                });

                prevPos = pos;
                prevForward = tangent;
            }
        }
    }

    private void BuildStations()
    {
        runtimeStations.Clear();

        if (stations == null || samples.Count < 2)
            return;

        for (int i = 0; i < stations.Length; i++)
        {
            if (stations[i] == null || stations[i].stopPoint == null)
                continue;

            RuntimeStation rs = new RuntimeStation
            {
                stopPoint = stations[i].stopPoint,
                routeDistance = FindProjectedDistanceOnSpline(stations[i].stopPoint.position),
                dwellTime = Mathf.Max(0f, stations[i].dwellTime),
                openLeftDoors = stations[i].openLeftDoors,
                openRightDoors = stations[i].openRightDoors
            };

            runtimeStations.Add(rs);
        }

        runtimeStations.Sort((a, b) => a.routeDistance.CompareTo(b.routeDistance));
    }

    private float FindProjectedDistanceOnSpline(Vector3 worldPos)
    {
        if (samples.Count == 0)
            return 0f;

        if (samples.Count == 1)
            return samples[0].distance;

        float bestDistance = samples[0].distance;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < samples.Count - 1; i++)
        {
            Vector3 a = samples[i].position;
            Vector3 b = samples[i + 1].position;
            Vector3 ab = b - a;
            float abLenSqr = ab.sqrMagnitude;

            float t = 0f;
            if (abLenSqr > 0.000001f)
                t = Mathf.Clamp01(Vector3.Dot(worldPos - a, ab) / abLenSqr);

            Vector3 projected = a + ab * t;
            float sqr = (projected - worldPos).sqrMagnitude;

            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                bestDistance = Mathf.Lerp(samples[i].distance, samples[i + 1].distance, t);
            }
        }

        return bestDistance;
    }

    private void CacheClosedDoorPositions()
    {
        if (cars == null)
            return;

        for (int i = 0; i < cars.Length; i++)
        {
            if (cars[i] == null)
                continue;

            CacheDoorArray(cars[i].leftDoors);
            CacheDoorArray(cars[i].rightDoors);
        }
    }

    private void CacheDoorArray(DoorPanel[] panels)
    {
        if (panels == null)
            return;

        for (int i = 0; i < panels.Length; i++)
        {
            if (panels[i] != null && panels[i].door != null)
                panels[i].closedLocalPosition = panels[i].door.localPosition;
        }
    }

    private bool ValidateRuntime()
    {
        if (GetValidWaypointCount() < 2)
            return false;

        if (cars == null || cars.Length == 0 || cars[0] == null || cars[0].root == null)
            return false;

        if (samples.Count < 2 || totalRouteLength <= 0.001f)
            return false;

        return true;
    }

    private void UpdateMovement(float dt)
    {
        RuntimeStation nextStation = GetNextStationAhead();

        bool mustBrakeForEnd = pingPong && IsApproachingRouteEnd();
        float distanceToEnd = GetDistanceToEndAhead();

        float targetSpeed = cruiseSpeed;

        if (nextStation != null)
        {
            float stopDistance = GetEffectiveStopDistance(nextStation);
            float distanceToStation = Mathf.Abs(stopDistance - headDistance);
            float brakingDistance = GetRequiredBrakingDistance(currentSpeed, braking);

            if (distanceToStation <= brakingDistance + stopSnapDistance)
                targetSpeed = 0f;

            currentSpeed = Mathf.MoveTowards(
                currentSpeed,
                targetSpeed,
                (targetSpeed < currentSpeed ? braking : acceleration) * dt
            );

            float moveDelta = currentDirection * currentSpeed * dt;
            float previousDistance = headDistance;
            headDistance += moveDelta;

            if (PassedTarget(previousDistance, headDistance, stopDistance, currentDirection) ||
                Mathf.Abs(stopDistance - headDistance) <= stopSnapDistance)
            {
                headDistance = stopDistance;
                currentSpeed = 0f;
                BeginDwell(nextStation);
                return;
            }
        }
        else if (mustBrakeForEnd)
        {
            float brakingDistance = GetRequiredBrakingDistance(currentSpeed, braking);

            if (distanceToEnd <= brakingDistance + stopSnapDistance)
                targetSpeed = 0f;

            currentSpeed = Mathf.MoveTowards(
                currentSpeed,
                targetSpeed,
                (targetSpeed < currentSpeed ? braking : acceleration) * dt
            );

            float moveDelta = currentDirection * currentSpeed * dt;
            float previousDistance = headDistance;
            headDistance += moveDelta;

            float endDistance = currentDirection > 0 ? totalRouteLength : 0f;

            if (PassedTarget(previousDistance, headDistance, endDistance, currentDirection) ||
                Mathf.Abs(endDistance - headDistance) <= stopSnapDistance)
            {
                headDistance = endDistance;
                currentSpeed = 0f;

                RuntimeStation terminalStop = new RuntimeStation
                {
                    stopPoint = null,
                    routeDistance = endDistance,
                    dwellTime = 3f,
                    openLeftDoors = true,
                    openRightDoors = false
                };

                BeginDwell(terminalStop);
                return;
            }
        }
        else
        {
            currentSpeed = Mathf.MoveTowards(currentSpeed, cruiseSpeed, acceleration * dt);
            headDistance += currentDirection * currentSpeed * dt;

            if (pingPong)
                headDistance = Mathf.Clamp(headDistance, 0f, totalRouteLength);
            else
                headDistance = Mathf.Repeat(headDistance, totalRouteLength);
        }
    }

    private void UpdateDwelling(float dt)
    {
        dwellTimer -= dt;

        if (dwellTimer > 0f)
            return;

        doorsLeftOpen = false;
        doorsRightOpen = false;

        if (pingPong)
        {
            if (Mathf.Abs(headDistance - totalRouteLength) <= stopSnapDistance)
                currentDirection = -1;
            else if (Mathf.Abs(headDistance) <= stopSnapDistance)
                currentDirection = 1;
        }

        state = TrainState.Moving;
    }

    private void BeginDwell(RuntimeStation station)
    {
        state = TrainState.Dwelling;
        dwellTimer = station.dwellTime;
        doorsLeftOpen = station.openLeftDoors;
        doorsRightOpen = station.openRightDoors;
    }

    private RuntimeStation GetNextStationAhead()
    {
        if (runtimeStations.Count == 0)
            return null;

        if (currentDirection > 0)
        {
            for (int i = 0; i < runtimeStations.Count; i++)
            {
                float stopDistance = GetEffectiveStopDistance(runtimeStations[i]);
                if (stopDistance > headDistance + stopSnapDistance)
                    return runtimeStations[i];
            }
        }
        else
        {
            for (int i = runtimeStations.Count - 1; i >= 0; i--)
            {
                float stopDistance = GetEffectiveStopDistance(runtimeStations[i]);
                if (stopDistance < headDistance - stopSnapDistance)
                    return runtimeStations[i];
            }
        }

        return null;
    }

    private float GetEffectiveStopDistance(RuntimeStation station)
    {
        float effective = station.routeDistance - currentDirection * stopAlignmentOffset;
        return Mathf.Clamp(effective, 0f, totalRouteLength);
    }

    private bool IsApproachingRouteEnd()
    {
        if (currentDirection > 0)
            return headDistance < totalRouteLength;

        return headDistance > 0f;
    }

    private float GetDistanceToEndAhead()
    {
        return currentDirection > 0 ? totalRouteLength - headDistance : headDistance;
    }

    private float GetRequiredBrakingDistance(float speed, float brakeForce)
    {
        if (brakeForce <= 0.0001f)
            return 0f;

        return (speed * speed) / (2f * brakeForce);
    }

    private bool PassedTarget(float previous, float current, float target, int dir)
    {
        return dir > 0
            ? previous <= target && current >= target
            : previous >= target && current <= target;
    }

    private void UpdateTrainTransformsImmediate()
    {
        if (cars == null || samples.Count == 0)
            return;

        float distanceFromHead = 0f;
        Quaternion axisCorrection = GetModelAxisCorrection();

        for (int i = 0; i < cars.Length; i++)
        {
            if (cars[i] == null || cars[i].root == null)
                continue;

            if (i > 0)
                distanceFromHead += Mathf.Max(0f, cars[i].spacingFromPrevious);

            float carDistance = headDistance - currentDirection * distanceFromHead;
            carDistance = Mathf.Clamp(carDistance, 0f, totalRouteLength);

            SplineSample sample = SampleByDistance(carDistance, currentDirection);

            cars[i].root.position = sample.position;
            cars[i].root.rotation =
                Quaternion.LookRotation(sample.forward, Vector3.up) *
                axisCorrection *
                Quaternion.Euler(modelRotationOffset);
        }
    }

    private Quaternion GetModelAxisCorrection()
    {
        switch (modelForwardAxis)
        {
            case ModelForwardAxis.PositiveZ:
                return Quaternion.identity;
            case ModelForwardAxis.NegativeZ:
                return Quaternion.Euler(0f, 180f, 0f);
            case ModelForwardAxis.PositiveX:
                return Quaternion.Euler(0f, -90f, 0f);
            case ModelForwardAxis.NegativeX:
                return Quaternion.Euler(0f, 90f, 0f);
            default:
                return Quaternion.identity;
        }
    }

    private SplineSample SampleByDistance(float distance, int direction)
    {
        distance = Mathf.Clamp(distance, 0f, totalRouteLength);

        if (samples.Count == 0)
            return default;

        if (distance <= 0f)
        {
            SplineSample s = samples[0];
            if (direction < 0) s.forward = -s.forward;
            return s;
        }

        if (distance >= totalRouteLength)
        {
            SplineSample s = samples[samples.Count - 1];
            if (direction < 0) s.forward = -s.forward;
            return s;
        }

        int low = 0;
        int high = samples.Count - 1;

        while (high - low > 1)
        {
            int mid = (low + high) / 2;
            if (samples[mid].distance < distance)
                low = mid;
            else
                high = mid;
        }

        SplineSample a = samples[low];
        SplineSample b = samples[high];

        float range = b.distance - a.distance;
        float t = range > 0.0001f ? (distance - a.distance) / range : 0f;

        Vector3 pos = Vector3.Lerp(a.position, b.position, t);
        Vector3 fwd = Vector3.Slerp(a.forward, b.forward, t).normalized;

        if (fwd.sqrMagnitude < 0.0001f)
            fwd = (b.position - a.position).normalized;

        if (direction < 0)
            fwd = -fwd;

        return new SplineSample
        {
            distance = distance,
            position = pos,
            forward = fwd
        };
    }

    private void UpdateDoors(float dt)
    {
        if (cars == null)
            return;

        for (int i = 0; i < cars.Length; i++)
        {
            if (cars[i] == null)
                continue;

            UpdateDoorArray(cars[i].leftDoors, doorsLeftOpen, dt);
            UpdateDoorArray(cars[i].rightDoors, doorsRightOpen, dt);
        }
    }

    private void UpdateDoorArray(DoorPanel[] panels, bool open, float dt)
    {
        if (panels == null)
            return;

        float speed = open ? doorOpenSpeed : doorCloseSpeed;

        for (int i = 0; i < panels.Length; i++)
        {
            if (panels[i] == null || panels[i].door == null)
                continue;

            Vector3 targetLocalPos = open
                ? panels[i].closedLocalPosition + panels[i].openLocalOffset
                : panels[i].closedLocalPosition;

            panels[i].door.localPosition = Vector3.MoveTowards(
                panels[i].door.localPosition,
                targetLocalPos,
                speed * dt
            );
        }
    }

    private Vector3 GetCatmullRomPosition(int segmentIndex, float t)
    {
        int validWaypointCount = GetValidWaypointCount();
        if (validWaypointCount < 2)
            return transform.position;

        Vector3 p0 = GetWaypointPositionClamped(segmentIndex - 1);
        Vector3 p1 = GetWaypointPositionClamped(segmentIndex);
        Vector3 p2 = GetWaypointPositionClamped(segmentIndex + 1);
        Vector3 p3 = GetWaypointPositionClamped(segmentIndex + 2);

        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }

    private Vector3 GetCatmullRomTangent(int segmentIndex, float t)
    {
        int validWaypointCount = GetValidWaypointCount();
        if (validWaypointCount < 2)
            return Vector3.forward;

        Vector3 p0 = GetWaypointPositionClamped(segmentIndex - 1);
        Vector3 p1 = GetWaypointPositionClamped(segmentIndex);
        Vector3 p2 = GetWaypointPositionClamped(segmentIndex + 1);
        Vector3 p3 = GetWaypointPositionClamped(segmentIndex + 2);

        float t2 = t * t;

        return 0.5f * (
            (-p0 + p2) +
            2f * (2f * p0 - 5f * p1 + 4f * p2 - p3) * t +
            3f * (-p0 + 3f * p1 - 3f * p2 + p3) * t2
        );
    }

    private Vector3 GetWaypointPositionClamped(int index)
    {
        List<Transform> validWaypoints = GetValidWaypoints();
        if (validWaypoints.Count == 0)
            return transform.position;

        index = Mathf.Clamp(index, 0, validWaypoints.Count - 1);

        Transform wp = validWaypoints[index];
        return wp != null ? wp.position : transform.position;
    }

    private int GetValidWaypointCount()
    {
        return GetValidWaypoints().Count;
    }

    private List<Transform> GetValidWaypoints()
    {
        List<Transform> result = new List<Transform>();

        if (waypoints == null)
            return result;

        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] != null)
                result.Add(waypoints[i]);
        }

        return result;
    }

    private void OnDrawGizmos()
    {
        List<Transform> validWaypoints = GetValidWaypoints();
        if (validWaypoints.Count < 2)
            return;

        Gizmos.color = Color.cyan;

        Vector3 prev = Vector3.zero;
        bool hasPrev = false;

        int segmentCount = validWaypoints.Count - 1;
        int stepsPerSegment = Mathf.Max(3, samplesPerSegment);

        for (int seg = 0; seg < segmentCount; seg++)
        {
            for (int step = 0; step <= stepsPerSegment; step++)
            {
                float t = step / (float)stepsPerSegment;
                Vector3 pos = GetCatmullRomPosition(seg, t);

                if (hasPrev)
                    Gizmos.DrawLine(prev, pos);

                prev = pos;
                hasPrev = true;
            }
        }

        Gizmos.color = Color.yellow;

        if (stations != null)
        {
            for (int i = 0; i < stations.Length; i++)
            {
                if (stations[i] != null && stations[i].stopPoint != null)
                    Gizmos.DrawSphere(stations[i].stopPoint.position, 0.3f);
            }
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        samplesPerSegment = Mathf.Max(3, samplesPerSegment);
        cruiseSpeed = Mathf.Max(0.01f, cruiseSpeed);
        acceleration = Mathf.Max(0.01f, acceleration);
        braking = Mathf.Max(0.01f, braking);
        stopSnapDistance = Mathf.Max(0.001f, stopSnapDistance);
        doorOpenSpeed = Mathf.Max(0.01f, doorOpenSpeed);
        doorCloseSpeed = Mathf.Max(0.01f, doorCloseSpeed);
    }
#endif
}
