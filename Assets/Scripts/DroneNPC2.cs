using System.Collections.Generic;
using UnityEngine;

public class DroneNPC2 : MonoBehaviour
{
    private enum Drone2State
    {
        MovingToDestination,
        Finished
    }

    [Header("搬運物件設定")]
    public GameObject[] cargoPrefabs;
    public Transform cargoAnchor;
    public bool addRigidbodyToDroppedCargo = true;
    public float cargoDropDownVelocity = 1.5f;

    [Header("移動設定")]
    public float moveSpeed = 6f;
    public float rotateSpeed = 10f;
    public float waypointReachDistance = 2f;

    [Header("Waypoint Graph A*")]
    public DroneWaypointGraph waypointGraph;
    public float pathNodeReachDistance = 2f;
    public float pathRepathInterval = 3f;

    private readonly List<Vector3> currentPath = new List<Vector3>();
    private int currentPathIndex = 0;
    private float nextRepathTime = 0f;
    private int pathVariantSeed = 0;

    [Header("目的地限制")]
    public float minDestinationDistanceFromSpawn = 35f;

    [Header("玩家視野外消失")]
    public Camera playerCamera;
    public LayerMask visibilityBlockerLayer;
    public bool disappearOnlyWhenHiddenFromPlayer = true;
    public float minDisappearDistance = 25f;
    public float viewportPadding = 0.15f;

    [Header("避障設定")]
    public LayerMask obstacleLayer;
    public float obstacleDetectDistance = 10f;
    public float obstacleAvoidRadius = 1f;
    public float obstacleAvoidWeight = 2.2f;
    public float upwardAvoidWeight = 1.2f;
    public float candidateCheckDistance = 6f;
    public float steeringSmooth = 10f;

    [Header("進階局部避障")]
    public bool useAdvancedLocalAvoidance = true;
    public float sideProbeAngle = 35f;
    public float wideProbeAngle = 70f;
    public float targetDirectionWeight = 1.2f;
    public float clearanceWeight = 2.8f;
    public float smoothDirectionWeight = 1f;
    public float emergencyAvoidRadius = 2.5f;
    public float emergencyAvoidWeight = 3.5f;
    public float avoidanceMemoryDuration = 0.6f;

    private Vector3 lastAvoidDirection = Vector3.zero;
    private float avoidanceMemoryTimer = 0f;
    private readonly Collider[] nearbyObstacleHits = new Collider[16];

    [Header("動態障礙物閃避")]
    public bool enableDynamicObstacleAvoidance = true;
    public LayerMask dynamicObstacleLayer;
    public float dynamicObstacleDetectRadius = 40f;
    public float dynamicPredictionTime = 1.2f;
    public float dynamicThreatRadius = 3.5f;
    public float dynamicAvoidWeight = 9f;
    public float dynamicUpBias = 0.3f;
    public float dynamicMinRelativeSpeed = 2f;
    public bool allowBackwardDynamicDodge = true;
    public bool allowDownwardDynamicDodge = true;
    public float dynamicBackwardWeight = 0.7f;
    public float dynamicDownwardWeight = 0.4f;

    private readonly Collider[] dynamicObstacleHits = new Collider[32];
    private float currentMoveSpeed = 0f;

    [Header("卡住脫困")]
    public float stuckCheckInterval = 0.5f;
    public float stuckMoveThreshold = 0.25f;
    public float stuckUpwardEscapeWeight = 2.5f;

    private Vector3 lastStuckCheckPosition;
    private float lastStuckCheckTime;
    private bool isStuck;

    [Header("受破壞設定")]
    public LayerMask damageLayer;
    public LayerMask destroyOnCollisionLayer;
    public int maxHealth = 1;
    public GameObject destroyedEffectPrefab;

    [Header("破壞後警戒 / Forced Hunt")]
    public float alertDuration = 10f;
    public float alertDetectRange = 120f;
    public int forcedHunterCountOnDestroyed = 2;
    public bool chooseClosestHuntersToPlayer = true;

    [Header("高度限制，可選")]
    public bool limitFlightHeight = false;
    public float minFlightY = 2f;
    public float maxFlightY = 80f;

    private Drone2State state = Drone2State.MovingToDestination;

    private DroneNPC2Manager manager;
    private Rigidbody rb;

    private Vector3 originPosition;
    private Quaternion originRotation;
    private int originSpawnIndex = -1;

    private Transform[] waypoints;
    private Transform destinationWaypoint;

    private Vector3 currentMoveDirection;
    private GameObject currentCargo;

    private int currentHealth;
    private bool hasBeenInitialized = false;
    private bool isFinishing = false;

    public void SetVisibilityContext(Camera camera, LayerMask blockerLayer)
    {
        if (camera != null)
        {
            playerCamera = camera;
        }

        if (blockerLayer.value != 0)
        {
            visibilityBlockerLayer = blockerLayer;
        }
    }

    public void Initialize(
        DroneNPC2Manager owner,
        Vector3 spawnPosition,
        Quaternion spawnRotation,
        int spawnIndex,
        Transform[] sharedWaypoints,
        DroneWaypointGraph graph
    )
    {
        manager = owner;
        originPosition = spawnPosition;
        originRotation = spawnRotation;
        originSpawnIndex = spawnIndex;
        waypoints = sharedWaypoints;
        waypointGraph = graph;

        transform.position = originPosition;
        transform.rotation = originRotation;

        if (rb != null)
        {
            rb.position = originPosition;
            rb.rotation = originRotation;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        currentMoveDirection = transform.forward;
        currentMoveSpeed = 0f;

        currentHealth = maxHealth;
        isFinishing = false;
        state = Drone2State.MovingToDestination;

        lastAvoidDirection = Vector3.zero;
        avoidanceMemoryTimer = 0f;

        lastStuckCheckPosition = transform.position;
        lastStuckCheckTime = Time.time;
        isStuck = false;

        ClearPath();

        ClearCargo();
        SpawnRandomCargo();

        destinationWaypoint = ChooseRandomDestinationNotSpawn();
        BuildPathToDestination();

        pathVariantSeed = Random.Range(0, 999999);
        hasBeenInitialized = true;
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();

        if (rb != null)
        {
            rb.useGravity = false;
            rb.angularVelocity = Vector3.zero;
        }
    }

    void OnEnable()
    {
        if (!hasBeenInitialized)
        {
            return;
        }

        currentHealth = maxHealth;
        isFinishing = false;
        state = Drone2State.MovingToDestination;
        currentMoveDirection = transform.forward;
        currentMoveSpeed = 0f;

        lastAvoidDirection = Vector3.zero;
        avoidanceMemoryTimer = 0f;

        lastStuckCheckPosition = transform.position;
        lastStuckCheckTime = Time.time;
        isStuck = false;
    }

    void FixedUpdate()
    {
        UpdateAvoidanceMemory();

        if (state == Drone2State.Finished || isFinishing)
        {
            return;
        }

        CheckDestroyByCollisionSphere();
        CheckStuck();

        if (state == Drone2State.Finished || isFinishing)
        {
            return;
        }

        if (destinationWaypoint == null)
        {
            destinationWaypoint = ChooseRandomDestinationNotSpawn();
            BuildPathToDestination();

            if (destinationWaypoint == null)
            {
                FinishNormally();
                return;
            }
        }

        if (isStuck || Time.time >= nextRepathTime)
        {
            BuildPathToDestination();
        }

        float distanceToDestination = Vector3.Distance(
            transform.position,
            destinationWaypoint.position
        );

        if (distanceToDestination <= waypointReachDistance)
        {
            if (!disappearOnlyWhenHiddenFromPlayer || IsHiddenFromPlayer(transform.position))
            {
                FinishNormally();
                return;
            }

            destinationWaypoint = ChooseRandomDestinationNotSpawn();
            BuildPathToDestination();

            if (destinationWaypoint == null)
            {
                return;
            }
        }

        if (!FollowCurrentPath(moveSpeed))
        {
            MoveTowards(destinationWaypoint.position, moveSpeed);
        }
    }

    void BuildPathToDestination()
    {
        ClearPath();

        if (destinationWaypoint == null)
        {
            return;
        }

        if (waypointGraph == null)
        {
            currentPath.Add(destinationWaypoint.position);
            currentPathIndex = 0;
            return;
        }

        bool found = waypointGraph.TryFindPathPositions(
            transform.position,
            destinationWaypoint.position,
            out List<Vector3> path,
            pathVariantSeed,
            false,
            false
        );

        if (found)
        {
            currentPath.AddRange(path);
            currentPathIndex = 0;
        }
        else
        {
            currentPath.Add(destinationWaypoint.position);
            currentPathIndex = 0;
        }

        nextRepathTime = Time.time + pathRepathInterval + Random.Range(0f, 0.8f);
        pathVariantSeed++;
    }

    bool FollowCurrentPath(float speed)
    {
        if (currentPath.Count == 0 || currentPathIndex >= currentPath.Count)
        {
            return false;
        }

        Vector3 target = currentPath[currentPathIndex];

        if (Vector3.Distance(transform.position, target) <= pathNodeReachDistance)
        {
            currentPathIndex++;

            if (currentPathIndex >= currentPath.Count)
            {
                return false;
            }

            target = currentPath[currentPathIndex];
        }

        MoveTowards(target, speed);
        return true;
    }

    void ClearPath()
    {
        currentPath.Clear();
        currentPathIndex = 0;
    }

    Transform ChooseRandomDestinationNotSpawn()
    {
        if (waypoints == null || waypoints.Length <= 1)
        {
            return null;
        }

        List<Transform> candidates = new List<Transform>();

        for (int i = 0; i < waypoints.Length; i++)
        {
            if (i == originSpawnIndex || waypoints[i] == null)
            {
                continue;
            }

            float distanceFromSpawn = Vector3.Distance(
                originPosition,
                waypoints[i].position
            );

            if (distanceFromSpawn < minDestinationDistanceFromSpawn)
            {
                continue;
            }

            candidates.Add(waypoints[i]);
        }

        if (candidates.Count > 0)
        {
            return candidates[Random.Range(0, candidates.Count)];
        }

        for (int i = 0; i < waypoints.Length; i++)
        {
            if (i != originSpawnIndex && waypoints[i] != null)
            {
                return waypoints[i];
            }
        }

        return null;
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

        if (distance < minDisappearDistance)
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

    void SpawnRandomCargo()
    {
        if (cargoPrefabs == null || cargoPrefabs.Length == 0)
        {
            return;
        }

        GameObject prefab = cargoPrefabs[Random.Range(0, cargoPrefabs.Length)];

        if (prefab == null)
        {
            return;
        }

        Transform parent = cargoAnchor != null ? cargoAnchor : transform;
        Vector3 prefabScale = prefab.transform.localScale;

        currentCargo = Instantiate(prefab);
        currentCargo.transform.SetParent(parent, false);
        currentCargo.transform.localPosition = Vector3.zero;
        currentCargo.transform.localRotation = Quaternion.identity;
        currentCargo.transform.localScale = DivideScale(prefabScale, parent.lossyScale);

        Rigidbody[] rigidbodies = currentCargo.GetComponentsInChildren<Rigidbody>();

        foreach (Rigidbody cargoRb in rigidbodies)
        {
            cargoRb.isKinematic = true;
            cargoRb.useGravity = false;
            cargoRb.velocity = Vector3.zero;
            cargoRb.angularVelocity = Vector3.zero;
        }

        Collider[] colliders = currentCargo.GetComponentsInChildren<Collider>();

        foreach (Collider c in colliders)
        {
            c.enabled = false;
        }
    }

    Vector3 DivideScale(Vector3 targetWorldScale, Vector3 parentWorldScale)
    {
        return new Vector3(
            parentWorldScale.x != 0f ? targetWorldScale.x / parentWorldScale.x : targetWorldScale.x,
            parentWorldScale.y != 0f ? targetWorldScale.y / parentWorldScale.y : targetWorldScale.y,
            parentWorldScale.z != 0f ? targetWorldScale.z / parentWorldScale.z : targetWorldScale.z
        );
    }

    void ClearCargo()
    {
        if (currentCargo == null)
        {
            return;
        }

        Destroy(currentCargo);
        currentCargo = null;
    }

    void DropCargo()
    {
        if (currentCargo == null)
        {
            return;
        }

        GameObject dropped = currentCargo;
        currentCargo = null;

        dropped.transform.SetParent(null, true);

        Rigidbody[] rigidbodies = dropped.GetComponentsInChildren<Rigidbody>();

        if (rigidbodies.Length == 0 && addRigidbodyToDroppedCargo)
        {
            Rigidbody newRb = dropped.AddComponent<Rigidbody>();
            newRb.isKinematic = false;
            newRb.useGravity = true;
            newRb.velocity = Vector3.down * cargoDropDownVelocity;
        }
        else
        {
            foreach (Rigidbody cargoRb in rigidbodies)
            {
                cargoRb.isKinematic = false;
                cargoRb.useGravity = true;
                cargoRb.velocity = Vector3.down * cargoDropDownVelocity;
            }
        }

        Collider[] colliders = dropped.GetComponentsInChildren<Collider>();

        foreach (Collider c in colliders)
        {
            c.enabled = true;
        }
    }

    void MoveTowards(Vector3 targetPosition, float speed)
    {
        Vector3 toTarget = targetPosition - transform.position;

        if (toTarget.sqrMagnitude < 0.001f)
        {
            return;
        }

        Vector3 desiredDirection = toTarget.normalized;
        currentMoveSpeed = speed;

        Vector3 steeredDirection = GetAvoidedDirection(desiredDirection, targetPosition);

        if (currentMoveDirection.sqrMagnitude < 0.001f)
        {
            currentMoveDirection = steeredDirection;
        }
        else
        {
            currentMoveDirection = Vector3.Slerp(
                currentMoveDirection,
                steeredDirection,
                Time.fixedDeltaTime * steeringSmooth
            ).normalized;
        }

        Vector3 nextPosition =
            transform.position +
            currentMoveDirection * speed * Time.fixedDeltaTime;

        if (limitFlightHeight)
        {
            nextPosition.y = Mathf.Clamp(nextPosition.y, minFlightY, maxFlightY);
        }

        if (rb != null)
        {
            rb.MovePosition(nextPosition);
        }
        else
        {
            transform.position = nextPosition;
        }

        RotateTowards(currentMoveDirection);
    }

    Vector3 GetAvoidedDirection(Vector3 desiredDirection, Vector3 targetPosition)
    {
        Vector3 obstacleRepulsion = GetObstacleRepulsion();
        Vector3 dynamicAvoidance = GetDynamicObstacleAvoidance(desiredDirection);

        bool frontBlocked = IsDirectionBlocked(desiredDirection, obstacleDetectDistance);

        bool hasEmergencyObstacle = obstacleRepulsion.sqrMagnitude > 0.001f;
        bool hasDynamicThreat = dynamicAvoidance.sqrMagnitude > 0.001f;

        if (!frontBlocked && !hasEmergencyObstacle && !hasDynamicThreat && !isStuck)
        {
            return desiredDirection;
        }

        Vector3 toTarget = (targetPosition - transform.position).normalized;

        Vector3 right = Vector3.Cross(Vector3.up, desiredDirection).normalized;

        if (right.sqrMagnitude < 0.001f)
        {
            right = transform.right;
        }

        Vector3 left = -right;

        Vector3[] candidates =
        {
            desiredDirection,
            (Quaternion.AngleAxis(sideProbeAngle, Vector3.up) * desiredDirection).normalized,
            (Quaternion.AngleAxis(-sideProbeAngle, Vector3.up) * desiredDirection).normalized,
            (Quaternion.AngleAxis(wideProbeAngle, Vector3.up) * desiredDirection).normalized,
            (Quaternion.AngleAxis(-wideProbeAngle, Vector3.up) * desiredDirection).normalized,
            (desiredDirection + Vector3.up * upwardAvoidWeight).normalized,
            (desiredDirection + right * obstacleAvoidWeight).normalized,
            (desiredDirection + left * obstacleAvoidWeight).normalized,
            (desiredDirection + right * obstacleAvoidWeight + Vector3.up * upwardAvoidWeight).normalized,
            (desiredDirection + left * obstacleAvoidWeight + Vector3.up * upwardAvoidWeight).normalized,
            (desiredDirection - Vector3.up * 0.35f).normalized,
            lastAvoidDirection
        };

        Vector3 bestDirection = desiredDirection;
        float bestScore = -999999f;

        Vector3 currentDir = currentMoveDirection.sqrMagnitude > 0.001f
            ? currentMoveDirection.normalized
            : desiredDirection;

        Vector3 repulsionDir = obstacleRepulsion.sqrMagnitude > 0.001f
            ? obstacleRepulsion.normalized
            : Vector3.zero;

        Vector3 dynamicAvoidDir = dynamicAvoidance.sqrMagnitude > 0.001f
            ? dynamicAvoidance.normalized
            : Vector3.zero;

        foreach (Vector3 raw in candidates)
        {
            if (raw.sqrMagnitude < 0.001f)
            {
                continue;
            }

            Vector3 candidate = raw.normalized;

            float clearanceScore = GetClearDistance(candidate) / candidateCheckDistance;
            float targetScore = Vector3.Dot(candidate, toTarget);
            float smoothScore = Vector3.Dot(candidate, currentDir);

            float repulsionScore = repulsionDir.sqrMagnitude > 0.001f
                ? Vector3.Dot(candidate, repulsionDir)
                : 0f;

            float dynamicScore = dynamicAvoidDir.sqrMagnitude > 0.001f
                ? Vector3.Dot(candidate, dynamicAvoidDir)
                : 0f;

            float stuckBonus = isStuck && candidate.y > 0f
                ? stuckUpwardEscapeWeight
                : 0f;

            float heightPenalty = 0f;

            if (limitFlightHeight)
            {
                float predictedY = transform.position.y + candidate.y * candidateCheckDistance;

                if (predictedY < minFlightY || predictedY > maxFlightY)
                {
                    heightPenalty = 3f;
                }
            }

            float memoryBonus = 0f;

            if (avoidanceMemoryTimer > 0f && lastAvoidDirection.sqrMagnitude > 0.001f)
            {
                memoryBonus = Vector3.Dot(candidate, lastAvoidDirection.normalized) * 0.7f;
            }

            float score =
                targetScore * targetDirectionWeight +
                clearanceScore * clearanceWeight +
                smoothScore * smoothDirectionWeight +
                repulsionScore * emergencyAvoidWeight +
                dynamicScore * dynamicAvoidWeight +
                stuckBonus +
                memoryBonus -
                heightPenalty;

            if (score > bestScore)
            {
                bestScore = score;
                bestDirection = candidate;
            }
        }

        if (repulsionDir.sqrMagnitude > 0.001f)
        {
            bestDirection = (bestDirection + repulsionDir * emergencyAvoidWeight).normalized;
        }

        if (dynamicAvoidDir.sqrMagnitude > 0.001f)
        {
            bestDirection = (bestDirection + dynamicAvoidDir * dynamicAvoidWeight).normalized;
        }

        lastAvoidDirection = bestDirection;
        avoidanceMemoryTimer = avoidanceMemoryDuration;

        return bestDirection.normalized;
    }

    Vector3 GetDynamicObstacleAvoidance(Vector3 desiredDirection)
    {
        if (!enableDynamicObstacleAvoidance || dynamicObstacleLayer.value == 0)
        {
            return Vector3.zero;
        }

        int hitCount = Physics.OverlapSphereNonAlloc(
            transform.position,
            dynamicObstacleDetectRadius,
            dynamicObstacleHits,
            dynamicObstacleLayer,
            QueryTriggerInteraction.Collide
        );

        Vector3 droneVelocity = currentMoveDirection.sqrMagnitude > 0.001f
            ? currentMoveDirection.normalized * currentMoveSpeed
            : desiredDirection.normalized * currentMoveSpeed;

        Vector3 totalAvoidance = Vector3.zero;

        for (int i = 0; i < hitCount; i++)
        {
            Collider obstacle = dynamicObstacleHits[i];

            if (obstacle == null ||
                obstacle.transform == transform ||
                obstacle.transform.IsChildOf(transform))
            {
                continue;
            }

            Rigidbody obstacleRb = obstacle.attachedRigidbody;

            if (obstacleRb == rb)
            {
                continue;
            }

            Vector3 relativePosition = obstacle.bounds.center - transform.position;
            Vector3 obstacleVelocity = obstacleRb != null ? obstacleRb.velocity : Vector3.zero;
            Vector3 relativeVelocity = obstacleVelocity - droneVelocity;

            float relativeSpeedSqr = relativeVelocity.sqrMagnitude;

            if (relativeSpeedSqr < dynamicMinRelativeSpeed * dynamicMinRelativeSpeed)
            {
                continue;
            }

            float timeToClosest =
                -Vector3.Dot(relativePosition, relativeVelocity) / relativeSpeedSqr;

            if (timeToClosest < 0f || timeToClosest > dynamicPredictionTime)
            {
                continue;
            }

            Vector3 closestRelativePosition =
                relativePosition + relativeVelocity * timeToClosest;

            float closestDistance = closestRelativePosition.magnitude;

            if (closestDistance > dynamicThreatRadius)
            {
                continue;
            }

            Vector3 rawDodgeDirection = -closestRelativePosition;

            if (rawDodgeDirection.sqrMagnitude < 0.001f)
            {
                rawDodgeDirection = Vector3.Cross(relativeVelocity.normalized, Vector3.up);

                if (rawDodgeDirection.sqrMagnitude < 0.001f)
                {
                    rawDodgeDirection = transform.right;
                }
            }

            rawDodgeDirection.Normalize();

            Vector3 bestDodge = ChooseBestDynamicDodgeDirection(rawDodgeDirection);

            float distanceThreat = 1f - Mathf.Clamp01(closestDistance / dynamicThreatRadius);
            float timeThreat = 1f - Mathf.Clamp01(timeToClosest / dynamicPredictionTime);
            float threatStrength = distanceThreat * timeThreat;

            totalAvoidance += bestDodge * threatStrength;
        }

        return totalAvoidance;
    }

    Vector3 ChooseBestDynamicDodgeDirection(Vector3 rawDodgeDirection)
    {
        Vector3 forward = currentMoveDirection.sqrMagnitude > 0.001f
            ? currentMoveDirection.normalized
            : transform.forward;

        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

        if (right.sqrMagnitude < 0.001f)
        {
            right = transform.right;
        }

        Vector3 backward = -forward;

        Vector3[] candidates =
        {
            rawDodgeDirection,
            right,
            -right,
            Vector3.up,
            Vector3.down,
            backward,
            (right + Vector3.up).normalized,
            (-right + Vector3.up).normalized,
            (right + Vector3.down).normalized,
            (-right + Vector3.down).normalized,
            (backward + Vector3.up).normalized,
            (backward + Vector3.down).normalized
        };

        Vector3 best = rawDodgeDirection;
        float bestScore = Vector3.Dot(best, rawDodgeDirection);

        foreach (Vector3 raw in candidates)
        {
            if (raw.sqrMagnitude < 0.001f)
            {
                continue;
            }

            Vector3 candidate = raw.normalized;

            if (!allowDownwardDynamicDodge && candidate.y < -0.2f)
            {
                continue;
            }

            if (!allowBackwardDynamicDodge)
            {
                float backwardAmount = Vector3.Dot(candidate, backward);

                if (backwardAmount > 0.5f)
                {
                    continue;
                }
            }

            float escapeScore = Vector3.Dot(candidate, rawDodgeDirection);
            float clearanceScore = GetClearDistance(candidate) / candidateCheckDistance;
            float upScore = candidate.y > 0f ? dynamicUpBias : 0f;

            float downPenalty = candidate.y < 0f
                ? Mathf.Abs(candidate.y) * (1f - dynamicDownwardWeight)
                : 0f;

            float backwardDot = Vector3.Dot(candidate, backward);
            float backwardScore = backwardDot > 0f
                ? backwardDot * dynamicBackwardWeight
                : 0f;

            float score =
                escapeScore * 3f +
                clearanceScore * 2f +
                upScore +
                backwardScore -
                downPenalty;

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best.normalized;
    }

    bool IsDirectionBlocked(Vector3 direction, float distance)
    {
        if (obstacleLayer.value == 0 || direction.sqrMagnitude < 0.001f)
        {
            return false;
        }

        return Physics.SphereCast(
            transform.position,
            obstacleAvoidRadius,
            direction.normalized,
            out RaycastHit hit,
            distance,
            obstacleLayer,
            QueryTriggerInteraction.Ignore
        );
    }

    Vector3 GetObstacleRepulsion()
    {
        if (obstacleLayer.value == 0)
        {
            return Vector3.zero;
        }

        Vector3 repulsion = Vector3.zero;

        int hitCount = Physics.OverlapSphereNonAlloc(
            transform.position,
            emergencyAvoidRadius,
            nearbyObstacleHits,
            obstacleLayer,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < hitCount; i++)
        {
            Collider obstacle = nearbyObstacleHits[i];

            if (obstacle == null)
            {
                continue;
            }

            Vector3 closestPoint = obstacle.ClosestPoint(transform.position);
            Vector3 away = transform.position - closestPoint;

            if (away.sqrMagnitude < 0.0001f)
            {
                away = transform.position - obstacle.bounds.center;
            }

            if (away.sqrMagnitude < 0.0001f)
            {
                continue;
            }

            float distance = away.magnitude;
            float strength = 1f - Mathf.Clamp01(distance / emergencyAvoidRadius);

            repulsion += away.normalized * strength;
        }

        return repulsion;
    }

    float GetClearDistance(Vector3 direction)
    {
        if (obstacleLayer.value == 0 || direction.sqrMagnitude < 0.001f)
        {
            return candidateCheckDistance;
        }

        if (Physics.SphereCast(
            transform.position,
            obstacleAvoidRadius,
            direction.normalized,
            out RaycastHit hit,
            candidateCheckDistance,
            obstacleLayer,
            QueryTriggerInteraction.Ignore))
        {
            return hit.distance;
        }

        return candidateCheckDistance;
    }

    void UpdateAvoidanceMemory()
    {
        if (avoidanceMemoryTimer > 0f)
        {
            avoidanceMemoryTimer -= Time.fixedDeltaTime;

            if (avoidanceMemoryTimer <= 0f)
            {
                avoidanceMemoryTimer = 0f;
                lastAvoidDirection = Vector3.zero;
            }
        }
    }

    void RotateTowards(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);

        Quaternion nextRotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            Time.fixedDeltaTime * rotateSpeed
        );

        if (rb != null)
        {
            rb.MoveRotation(nextRotation);
        }
        else
        {
            transform.rotation = nextRotation;
        }
    }

    void CheckDestroyByCollisionSphere()
    {
        if (destroyOnCollisionLayer.value == 0)
        {
            return;
        }

        bool touchingDestroyLayer = Physics.CheckSphere(
            transform.position,
            obstacleAvoidRadius,
            destroyOnCollisionLayer,
            QueryTriggerInteraction.Ignore
        );

        if (touchingDestroyLayer)
        {
            DestroyByDamage();
        }
    }

    void CheckStuck()
    {
        if (Time.time - lastStuckCheckTime < stuckCheckInterval)
        {
            return;
        }

        float movedDistance = Vector3.Distance(transform.position, lastStuckCheckPosition);

        isStuck =
            movedDistance < stuckMoveThreshold &&
            state != Drone2State.Finished &&
            !isFinishing;

        lastStuckCheckPosition = transform.position;
        lastStuckCheckTime = Time.time;
    }

    public void TakeDamage(int damage = 1)
    {
        if (isFinishing || state == Drone2State.Finished)
        {
            return;
        }

        currentHealth -= damage;

        if (currentHealth <= 0)
        {
            DestroyByDamage();
        }
    }

    public void DestroyByDamage()
    {
        if (isFinishing)
        {
            return;
        }

        isFinishing = true;
        state = Drone2State.Finished;

        DropCargo();

        if (destroyedEffectPrefab != null)
        {
            Instantiate(destroyedEffectPrefab, transform.position, Quaternion.identity);
        }

        DroneAlertSystem.BroadcastDroneNPC2Destroyed(
            transform.position,
            alertDuration,
            alertDetectRange,
            forcedHunterCountOnDestroyed,
            chooseClosestHuntersToPlayer
        );

        if (manager != null)
        {
            manager.NotifyDroneFinished(this, originSpawnIndex, true);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    void FinishNormally()
    {
        if (isFinishing)
        {
            return;
        }

        isFinishing = true;
        state = Drone2State.Finished;

        ClearCargo();

        if (manager != null)
        {
            manager.NotifyDroneFinished(this, originSpawnIndex, false);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (IsInLayerMask(collision.gameObject.layer, damageLayer))
        {
            TakeDamage(1);
            return;
        }

        if (IsInLayerMask(collision.gameObject.layer, destroyOnCollisionLayer))
        {
            DestroyByDamage();
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (IsInLayerMask(other.gameObject.layer, damageLayer))
        {
            TakeDamage(1);
            return;
        }

        if (IsInLayerMask(other.gameObject.layer, destroyOnCollisionLayer))
        {
            DestroyByDamage();
        }
    }

    bool IsInLayerMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }

    public void PrepareForPool()
    {
        ClearCargo();
        ClearPath();

        isFinishing = false;
        state = Drone2State.Finished;

        destinationWaypoint = null;
        currentMoveDirection = transform.forward;
        currentMoveSpeed = 0f;
        currentHealth = maxHealth;

        isStuck = false;
        lastAvoidDirection = Vector3.zero;
        avoidanceMemoryTimer = 0f;

        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }
}