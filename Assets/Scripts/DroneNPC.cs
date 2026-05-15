using UnityEngine;

public class DroneNPC : MonoBehaviour
{
    private enum DroneState
    {
        Patrol,
        Chasing,
        Exploding
    }

    public enum PatrolMode
    {
        Sequential,
        DiversifiedSequential,
        Random
    }

    [Header("目標設定")]
    public string playerTag = "Player";

    [Header("偵測與追逐")]
    public float detectRange = 40f;
    public float giveUpRange = 100f;
    public float giveUpDelay = 3f;

    public float chaseSpeed = 8f;
    public float patrolSpeed = 4f;
    public float rotateSpeed = 10f;

    public Vector3 playerTargetOffset = new Vector3(0f, 1f, 0f);

    [Header("DroneNPC2 被破壞時的警戒設定")]
    public float alertChaseSpeedMultiplier = 1.25f;

    [Tooltip("警戒時，giveUpRange 至少會是 alertDetectRange + 這個距離")]
    public float alertGiveUpExtraRange = 40f;

    private bool isAlerted = false;
    private float alertTimer = 0f;
    private float currentAlertDetectRange = 0f;
    private Vector3 lastAlertPosition;

    [Header("爆炸設定")]
    public float explodeRange = 1.3f;
    public LayerMask explodeOnCollisionLayer;
    public float collisionExplodeRadius = 0.45f;
    public GameObject explosionPrefab;

    [Header("Waypoint 巡邏 / 導航")]
    public PatrolMode patrolMode = PatrolMode.DiversifiedSequential;

    public float waypointReachDistance = 1.5f;
    public float repathInterval = 0.25f;
    public float patrolRepathInterval = 2.0f;
    public bool repathPatrolWhenBlocked = true;

    public LayerMask obstacleLayer;
    public float pathCheckRadius = 0.6f;

    public float maxWaypointSelectDistance = 45f;
    public float maxDetourExtraDistance = 12f;

    public bool directChaseWhenPathClear = true;

    [Header("巡邏軌跡差異化")]
    public bool randomizePatrolDirection = true;
    public int minPatrolStep = 1;
    public int maxPatrolStep = 2;

    private int patrolDirection = 1;
    private int patrolStep = 1;

    [Header("近距離避障")]
    public bool enableLocalAvoidance = true;
    public float obstacleDetectDistance = 8f;
    public float obstacleAvoidRadius = 0.9f;
    public float obstacleAvoidWeight = 2f;
    public float upwardAvoidWeight = 1.2f;
    public float candidateCheckDistance = 5f;
    public float steeringSmooth = 6.5f;

    [Header("進階局部避障")]
    public bool useAdvancedLocalAvoidance = true;

    public float sideProbeAngle = 35f;
    public float wideProbeAngle = 70f;

    public float targetDirectionWeight = 1.2f;
    public float clearanceWeight = 2.4f;
    public float smoothDirectionWeight = 0.7f;

    public float emergencyAvoidRadius = 1.5f;
    public float emergencyAvoidWeight = 2.5f;
    public float avoidanceMemoryDuration = 0.45f;

    private Vector3 lastAvoidDirection = Vector3.zero;
    private float avoidanceMemoryTimer = 0f;
    private readonly Collider[] nearbyObstacleHits = new Collider[16];

    [Header("動態障礙物閃避")]
    public bool enableDynamicObstacleAvoidance = true;

    [Tooltip("會主動閃避這些 Layer，例如 Projectile / FlyingObstacle / Vehicle")]
    public LayerMask dynamicObstacleLayer;

    [Tooltip("偵測動態障礙物的半徑")]
    public float dynamicObstacleDetectRadius = 8f;

    [Tooltip("預測幾秒內是否會撞上")]
    public float dynamicPredictionTime = 1.2f;

    [Tooltip("預測最近距離小於這個值，就視為有撞擊風險")]
    public float dynamicThreatRadius = 2f;

    [Tooltip("動態閃避方向權重")]
    public float dynamicAvoidWeight = 6f;

    [Tooltip("動態閃避時往上的偏好，不是強制往上")]
    public float dynamicUpBias = 0.3f;

    [Tooltip("相對速度小於這個值時，不當成高速威脅")]
    public float dynamicMinRelativeSpeed = 1f;

    [Tooltip("是否允許動態閃避時後退")]
    public bool allowBackwardDynamicDodge = true;

    [Tooltip("是否允許動態閃避時往下")]
    public bool allowDownwardDynamicDodge = true;

    [Tooltip("後退閃避權重。太高會讓無人機常常煞停後退")]
    public float dynamicBackwardWeight = 0.7f;

    [Tooltip("往下閃避權重。太高可能更容易撞地")]
    public float dynamicDownwardWeight = 0.5f;

    private float currentMoveSpeed = 0f;
    private readonly Collider[] dynamicObstacleHits = new Collider[32];

    [Header("卡住脫困")]
    public float stuckCheckInterval = 0.5f;
    public float stuckMoveThreshold = 0.25f;
    public float stuckUpwardEscapeWeight = 2.5f;

    [Header("高度限制，可選")]
    public bool limitFlightHeight = false;
    public float minFlightY = 1.5f;
    public float maxFlightY = 80f;

    private DroneState state = DroneState.Patrol;

    private DroneGameManager manager;
    private Transform player;
    private Rigidbody rb;

    private Vector3 originPosition;
    private Quaternion originRotation;
    private int originSpawnIndex = -1;

    private Transform[] waypoints;

    private Transform currentNavigationWaypoint;
    private Transform currentPatrolWaypoint;

    private int currentPatrolIndex = -1;

    private Vector3 currentMoveDirection;

    private float lastRepathTime = -999f;
    private float lastPatrolRepathTime = -999f;

    private Vector3 lastStuckCheckPosition;
    private float lastStuckCheckTime;
    private bool isStuck;

    private float outOfRangeTimer = 0f;

    private bool hasBeenInitialized = false;

    public int SpawnIndex
    {
        get { return originSpawnIndex; }
    }

    public void Initialize(
        DroneGameManager owner,
        Vector3 spawnPosition,
        Quaternion spawnRotation,
        int spawnIndex,
        Transform[] sharedWaypoints
    )
    {
        manager = owner;
        originPosition = spawnPosition;
        originRotation = spawnRotation;
        originSpawnIndex = spawnIndex;
        waypoints = sharedWaypoints;

        transform.position = originPosition;
        transform.rotation = originRotation;

        currentNavigationWaypoint = null;
        currentPatrolWaypoint = null;

        currentPatrolIndex = spawnIndex;
        SetupPatrolVariation();

        currentMoveDirection = transform.forward;
        currentMoveSpeed = 0f;

        lastRepathTime = -999f;
        lastPatrolRepathTime = -999f;

        lastStuckCheckPosition = transform.position;
        lastStuckCheckTime = Time.time;
        isStuck = false;

        outOfRangeTimer = 0f;

        isAlerted = false;
        alertTimer = 0f;
        currentAlertDetectRange = 0f;

        lastAvoidDirection = Vector3.zero;
        avoidanceMemoryTimer = 0f;

        state = DroneState.Patrol;
        hasBeenInitialized = true;

        FindPlayer();

        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = originPosition;
            rb.rotation = originRotation;
        }
    }

    void SetupPatrolVariation()
    {
        patrolDirection = 1;
        patrolStep = 1;

        if (patrolMode != PatrolMode.DiversifiedSequential)
        {
            return;
        }

        if (randomizePatrolDirection)
        {
            patrolDirection = Random.value < 0.5f ? -1 : 1;
        }

        int safeMinStep = Mathf.Max(1, minPatrolStep);
        int safeMaxStep = Mathf.Max(safeMinStep, maxPatrolStep);

        patrolStep = Random.Range(safeMinStep, safeMaxStep + 1);
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
        DroneAlertSystem.OnDroneNPC2Destroyed += HandleDroneNPC2DestroyedAlert;

        if (!hasBeenInitialized)
        {
            return;
        }

        state = DroneState.Patrol;

        currentNavigationWaypoint = null;
        currentPatrolWaypoint = null;

        currentMoveDirection = transform.forward;
        currentMoveSpeed = 0f;

        lastRepathTime = -999f;
        lastPatrolRepathTime = -999f;

        lastStuckCheckPosition = transform.position;
        lastStuckCheckTime = Time.time;
        isStuck = false;

        outOfRangeTimer = 0f;

        lastAvoidDirection = Vector3.zero;
        avoidanceMemoryTimer = 0f;
    }

    void OnDisable()
    {
        DroneAlertSystem.OnDroneNPC2Destroyed -= HandleDroneNPC2DestroyedAlert;
    }

    void FixedUpdate()
    {
        UpdateAvoidanceMemory();

        if (state == DroneState.Exploding)
        {
            return;
        }

        if (player == null)
        {
            FindPlayer();
        }

        CheckCollisionExplosion();
        CheckStuck();
        UpdateAlertTimer();

        if (state == DroneState.Exploding)
        {
            return;
        }

        float distanceToPlayer = Mathf.Infinity;

        if (player != null)
        {
            distanceToPlayer = Vector3.Distance(transform.position, player.position);
        }

        switch (state)
        {
            case DroneState.Patrol:
                HandlePatrol(distanceToPlayer);
                break;

            case DroneState.Chasing:
                HandleChasing(distanceToPlayer);
                break;
        }
    }

    void FindPlayer()
    {
        GameObject playerObject = GameObject.FindGameObjectWithTag(playerTag);

        if (playerObject != null)
        {
            player = playerObject.transform;
        }
    }

    void HandleDroneNPC2DestroyedAlert(
        Vector3 alertPosition,
        float alertDuration,
        float alertDetectRange
    )
    {
        isAlerted = true;
        alertTimer = alertDuration;
        currentAlertDetectRange = alertDetectRange;
        lastAlertPosition = alertPosition;

        currentNavigationWaypoint = null;
        currentPatrolWaypoint = null;

        if (player != null)
        {
            float distanceToPlayer = Vector3.Distance(transform.position, player.position);

            if (distanceToPlayer <= currentAlertDetectRange)
            {
                state = DroneState.Chasing;
                outOfRangeTimer = 0f;
            }
        }
    }

    void UpdateAlertTimer()
    {
        if (!isAlerted)
        {
            return;
        }

        alertTimer -= Time.fixedDeltaTime;

        if (alertTimer <= 0f)
        {
            isAlerted = false;
            alertTimer = 0f;
            currentAlertDetectRange = 0f;
        }
    }

    void HandlePatrol(float distanceToPlayer)
    {
        outOfRangeTimer = 0f;
        currentNavigationWaypoint = null;

        float effectiveDetectRange = isAlerted
            ? Mathf.Max(detectRange, currentAlertDetectRange)
            : detectRange;

        if (player != null && distanceToPlayer <= effectiveDetectRange)
        {
            currentPatrolWaypoint = null;
            state = DroneState.Chasing;
            return;
        }

        if (NeedNewPatrolWaypoint())
        {
            currentPatrolWaypoint = ChoosePatrolWaypoint();
            lastPatrolRepathTime = Time.time;
        }

        if (currentPatrolWaypoint != null)
        {
            MoveTowards(currentPatrolWaypoint.position, patrolSpeed);
        }
    }

    bool NeedNewPatrolWaypoint()
    {
        if (currentPatrolWaypoint == null)
        {
            return true;
        }

        if (Vector3.Distance(transform.position, currentPatrolWaypoint.position) <= waypointReachDistance)
        {
            return true;
        }

        if (Time.time - lastPatrolRepathTime >= patrolRepathInterval)
        {
            if (isStuck)
            {
                return true;
            }

            if (repathPatrolWhenBlocked && !HasClearPath(transform.position, currentPatrolWaypoint.position))
            {
                return true;
            }
        }

        return false;
    }

    Transform ChoosePatrolWaypoint()
    {
        if (waypoints == null || waypoints.Length == 0)
        {
            return null;
        }

        if (patrolMode == PatrolMode.Random)
        {
            return ChooseRandomPatrolWaypoint();
        }

        return ChooseSequentialPatrolWaypoint();
    }

    Transform ChooseSequentialPatrolWaypoint()
    {
        if (waypoints == null || waypoints.Length == 0)
        {
            return null;
        }

        if (currentPatrolIndex < 0 || currentPatrolIndex >= waypoints.Length)
        {
            currentPatrolIndex = 0;
        }

        int waypointCount = waypoints.Length;

        int direction = patrolMode == PatrolMode.DiversifiedSequential
            ? patrolDirection
            : 1;

        int step = patrolMode == PatrolMode.DiversifiedSequential
            ? Mathf.Max(1, patrolStep)
            : 1;

        for (int i = 0; i < waypointCount; i++)
        {
            int nextIndex = WrapIndex(currentPatrolIndex + direction * step * (i + 1), waypointCount);
            Transform candidate = waypoints[nextIndex];

            if (candidate == null)
            {
                continue;
            }

            float distance = Vector3.Distance(transform.position, candidate.position);

            if (distance <= waypointReachDistance)
            {
                continue;
            }

            if (maxWaypointSelectDistance > 0f && distance > maxWaypointSelectDistance)
            {
                continue;
            }

            if (HasClearPath(transform.position, candidate.position))
            {
                currentPatrolIndex = nextIndex;
                return candidate;
            }
        }

        for (int i = 0; i < waypointCount; i++)
        {
            int nextIndex = WrapIndex(currentPatrolIndex + direction * step * (i + 1), waypointCount);
            Transform candidate = waypoints[nextIndex];

            if (candidate == null)
            {
                continue;
            }

            float distance = Vector3.Distance(transform.position, candidate.position);

            if (distance <= waypointReachDistance)
            {
                continue;
            }

            if (maxWaypointSelectDistance > 0f && distance > maxWaypointSelectDistance)
            {
                continue;
            }

            currentPatrolIndex = nextIndex;
            return candidate;
        }

        return null;
    }

    int WrapIndex(int index, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        while (index < 0)
        {
            index += count;
        }

        while (index >= count)
        {
            index -= count;
        }

        return index;
    }

    Transform ChooseRandomPatrolWaypoint()
    {
        Transform bestWaypoint = null;
        float bestScore = -999999f;

        foreach (Transform waypoint in waypoints)
        {
            if (waypoint == null)
            {
                continue;
            }

            if (waypoint == currentPatrolWaypoint)
            {
                continue;
            }

            float distanceToWaypoint = Vector3.Distance(transform.position, waypoint.position);

            if (distanceToWaypoint <= waypointReachDistance)
            {
                continue;
            }

            if (maxWaypointSelectDistance > 0f && distanceToWaypoint > maxWaypointSelectDistance)
            {
                continue;
            }

            if (!HasClearPath(transform.position, waypoint.position))
            {
                continue;
            }

            float distanceScore = -distanceToWaypoint * 0.05f;
            float randomScore = Random.Range(0f, 2f);

            float forwardScore = 0f;

            if (currentMoveDirection.sqrMagnitude > 0.001f)
            {
                Vector3 toWaypoint = (waypoint.position - transform.position).normalized;
                forwardScore = Vector3.Dot(currentMoveDirection.normalized, toWaypoint) * 2f;
            }

            float heightScore = waypoint.position.y > transform.position.y ? 0.3f : 0f;

            float stuckBonus = 0f;

            if (isStuck && waypoint.position.y > transform.position.y)
            {
                stuckBonus = stuckUpwardEscapeWeight;
            }

            float score =
                distanceScore +
                forwardScore +
                heightScore +
                randomScore +
                stuckBonus;

            if (score > bestScore)
            {
                bestScore = score;
                bestWaypoint = waypoint;
            }
        }

        if (bestWaypoint == null && waypoints.Length > 0)
        {
            bestWaypoint = waypoints[Random.Range(0, waypoints.Length)];
        }

        return bestWaypoint;
    }

    void HandleChasing(float distanceToPlayer)
    {
        if (player == null)
        {
            state = DroneState.Patrol;
            return;
        }

        if (distanceToPlayer <= explodeRange)
        {
            Explode();
            return;
        }

        float effectiveGiveUpRange = giveUpRange;

        if (isAlerted)
        {
            effectiveGiveUpRange = Mathf.Max(
                giveUpRange,
                currentAlertDetectRange + alertGiveUpExtraRange
            );
        }

        if (distanceToPlayer >= effectiveGiveUpRange)
        {
            outOfRangeTimer += Time.fixedDeltaTime;

            if (outOfRangeTimer >= giveUpDelay)
            {
                outOfRangeTimer = 0f;
                currentNavigationWaypoint = null;
                currentPatrolWaypoint = null;
                state = DroneState.Patrol;
                return;
            }
        }
        else
        {
            outOfRangeTimer = 0f;
        }

        Vector3 finalTarget = player.position + playerTargetOffset;
        Vector3 navigationTarget = GetNavigationTarget(finalTarget);

        float effectiveChaseSpeed = isAlerted
            ? chaseSpeed * alertChaseSpeedMultiplier
            : chaseSpeed;

        MoveTowards(navigationTarget, effectiveChaseSpeed);
    }

    Vector3 GetNavigationTarget(Vector3 finalTarget)
    {
        if (directChaseWhenPathClear && HasClearPath(transform.position, finalTarget))
        {
            currentNavigationWaypoint = null;
            return finalTarget;
        }

        bool needRepath =
            currentNavigationWaypoint == null ||
            Time.time - lastRepathTime >= repathInterval ||
            Vector3.Distance(transform.position, currentNavigationWaypoint.position) <= waypointReachDistance ||
            !HasClearPath(transform.position, currentNavigationWaypoint.position);

        if (needRepath)
        {
            currentNavigationWaypoint = ChooseBestWaypoint(finalTarget);
            lastRepathTime = Time.time;
        }

        if (currentNavigationWaypoint != null)
        {
            return currentNavigationWaypoint.position;
        }

        return finalTarget;
    }

    Transform ChooseBestWaypoint(Vector3 finalTarget)
    {
        if (waypoints == null || waypoints.Length == 0)
        {
            return null;
        }

        Transform bestWaypoint = null;
        float bestScore = -999999f;

        Vector3 toFinalTarget = finalTarget - transform.position;

        if (toFinalTarget.sqrMagnitude < 0.001f)
        {
            return null;
        }

        Vector3 finalDirection = toFinalTarget.normalized;
        float currentDistanceToGoal = Vector3.Distance(transform.position, finalTarget);

        foreach (Transform waypoint in waypoints)
        {
            if (waypoint == null)
            {
                continue;
            }

            float distanceToWaypoint = Vector3.Distance(transform.position, waypoint.position);

            if (distanceToWaypoint <= waypointReachDistance)
            {
                continue;
            }

            if (maxWaypointSelectDistance > 0f && distanceToWaypoint > maxWaypointSelectDistance)
            {
                continue;
            }

            float waypointDistanceToGoal = Vector3.Distance(waypoint.position, finalTarget);

            if (waypointDistanceToGoal > currentDistanceToGoal + maxDetourExtraDistance)
            {
                continue;
            }

            if (!HasClearPath(transform.position, waypoint.position))
            {
                continue;
            }

            Vector3 toWaypoint = waypoint.position - transform.position;

            if (toWaypoint.sqrMagnitude < 0.001f)
            {
                continue;
            }

            float directionScore = Vector3.Dot(toWaypoint.normalized, finalDirection) * 4f;
            float distanceToGoalScore = -waypointDistanceToGoal * 0.1f;
            float waypointDistancePenalty = -distanceToWaypoint * 0.03f;
            float clearToGoalBonus = HasClearPath(waypoint.position, finalTarget) ? 8f : 0f;

            float stuckBonus = 0f;

            if (isStuck && waypoint.position.y > transform.position.y)
            {
                stuckBonus = stuckUpwardEscapeWeight;
            }

            float score =
                directionScore +
                distanceToGoalScore +
                waypointDistancePenalty +
                clearToGoalBonus +
                stuckBonus;

            if (score > bestScore)
            {
                bestScore = score;
                bestWaypoint = waypoint;
            }
        }

        return bestWaypoint;
    }

    bool HasClearPath(Vector3 from, Vector3 to)
    {
        if (obstacleLayer.value == 0)
        {
            return true;
        }

        Vector3 direction = to - from;
        float distance = direction.magnitude;

        if (distance < 0.01f)
        {
            return true;
        }

        direction.Normalize();

        bool blocked = Physics.SphereCast(
            from,
            pathCheckRadius,
            direction,
            out RaycastHit hit,
            distance,
            obstacleLayer,
            QueryTriggerInteraction.Ignore
        );

        return !blocked;
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

        Vector3 steeredDirection = desiredDirection;

        if (enableLocalAvoidance)
        {
            steeredDirection = GetAvoidedDirection(desiredDirection, targetPosition);
        }

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
        if (obstacleLayer.value == 0 && dynamicObstacleLayer.value == 0)
        {
            return desiredDirection;
        }

        if (!useAdvancedLocalAvoidance)
        {
            return GetSimpleAvoidedDirection(desiredDirection, targetPosition);
        }

        Vector3 obstacleRepulsion = GetObstacleRepulsion();
        Vector3 dynamicAvoidance = GetDynamicObstacleAvoidance(desiredDirection);

        bool frontBlocked = false;

        if (obstacleLayer.value != 0)
        {
            frontBlocked = IsDirectionBlocked(
                desiredDirection,
                obstacleDetectDistance
            );
        }

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

        Vector3 yawRightSmall = Quaternion.AngleAxis(sideProbeAngle, Vector3.up) * desiredDirection;
        Vector3 yawLeftSmall = Quaternion.AngleAxis(-sideProbeAngle, Vector3.up) * desiredDirection;

        Vector3 yawRightWide = Quaternion.AngleAxis(wideProbeAngle, Vector3.up) * desiredDirection;
        Vector3 yawLeftWide = Quaternion.AngleAxis(-wideProbeAngle, Vector3.up) * desiredDirection;

        Vector3[] candidateDirections =
        {
            desiredDirection,

            yawRightSmall.normalized,
            yawLeftSmall.normalized,

            yawRightWide.normalized,
            yawLeftWide.normalized,

            (desiredDirection + Vector3.up * upwardAvoidWeight).normalized,

            (desiredDirection + right * obstacleAvoidWeight).normalized,
            (desiredDirection + left * obstacleAvoidWeight).normalized,

            (desiredDirection + right * obstacleAvoidWeight + Vector3.up * upwardAvoidWeight).normalized,
            (desiredDirection + left * obstacleAvoidWeight + Vector3.up * upwardAvoidWeight).normalized,

            (desiredDirection - Vector3.up * 0.35f).normalized,

            (desiredDirection + right * obstacleAvoidWeight - Vector3.up * 0.25f).normalized,
            (desiredDirection + left * obstacleAvoidWeight - Vector3.up * 0.25f).normalized,

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

        foreach (Vector3 rawCandidate in candidateDirections)
        {
            if (rawCandidate.sqrMagnitude < 0.001f)
            {
                continue;
            }

            Vector3 candidate = rawCandidate.normalized;

            float clearDistance = GetClearDistance(candidate);
            float clearanceScore = clearDistance / candidateCheckDistance;

            float targetScore = Vector3.Dot(candidate, toTarget);
            float smoothScore = Vector3.Dot(candidate, currentDir);

            float repulsionScore = 0f;

            if (repulsionDir.sqrMagnitude > 0.001f)
            {
                repulsionScore = Vector3.Dot(candidate, repulsionDir);
            }

            float dynamicAvoidScore = 0f;

            if (dynamicAvoidDir.sqrMagnitude > 0.001f)
            {
                dynamicAvoidScore = Vector3.Dot(candidate, dynamicAvoidDir);
            }

            float stuckBonus = 0f;

            if (isStuck && candidate.y > 0f)
            {
                stuckBonus = stuckUpwardEscapeWeight;
            }

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
                dynamicAvoidScore * dynamicAvoidWeight +
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

    Vector3 GetSimpleAvoidedDirection(Vector3 desiredDirection, Vector3 targetPosition)
    {
        bool frontBlocked = false;

        if (obstacleLayer.value != 0)
        {
            frontBlocked = Physics.SphereCast(
                transform.position,
                obstacleAvoidRadius,
                desiredDirection,
                out RaycastHit hit,
                obstacleDetectDistance,
                obstacleLayer,
                QueryTriggerInteraction.Ignore
            );
        }

        Vector3 dynamicAvoidance = GetDynamicObstacleAvoidance(desiredDirection);

        if (!frontBlocked && !isStuck && dynamicAvoidance.sqrMagnitude < 0.001f)
        {
            return desiredDirection;
        }

        Vector3 toTarget = (targetPosition - transform.position).normalized;

        Vector3 right = Vector3.Cross(Vector3.up, desiredDirection).normalized;

        if (right.sqrMagnitude < 0.001f)
        {
            right = transform.right;
        }

        Vector3 dynamicAvoidDir = dynamicAvoidance.sqrMagnitude > 0.001f
            ? dynamicAvoidance.normalized
            : Vector3.zero;

        Vector3[] candidateDirections =
        {
            desiredDirection,
            (desiredDirection + right * obstacleAvoidWeight).normalized,
            (desiredDirection - right * obstacleAvoidWeight).normalized,
            (desiredDirection + Vector3.up * upwardAvoidWeight).normalized,
            (desiredDirection + right * obstacleAvoidWeight + Vector3.up * upwardAvoidWeight).normalized,
            (desiredDirection - right * obstacleAvoidWeight + Vector3.up * upwardAvoidWeight).normalized,
            (desiredDirection - Vector3.up * 0.35f).normalized,
            dynamicAvoidDir
        };

        Vector3 bestDirection = desiredDirection;
        float bestScore = -99999f;

        foreach (Vector3 candidate in candidateDirections)
        {
            if (candidate.sqrMagnitude < 0.001f)
            {
                continue;
            }

            float clearDistance = GetClearDistance(candidate.normalized);
            float targetScore = Vector3.Dot(candidate.normalized, toTarget);
            float clearanceScore = clearDistance / candidateCheckDistance;

            float dynamicScore = 0f;

            if (dynamicAvoidDir.sqrMagnitude > 0.001f)
            {
                dynamicScore = Vector3.Dot(candidate.normalized, dynamicAvoidDir);
            }

            float stuckBonus = 0f;

            if (isStuck && candidate.y > 0f)
            {
                stuckBonus = stuckUpwardEscapeWeight;
            }

            float score =
                targetScore * 1.2f +
                clearanceScore * 2.0f +
                dynamicScore * dynamicAvoidWeight +
                stuckBonus;

            if (score > bestScore)
            {
                bestScore = score;
                bestDirection = candidate.normalized;
            }
        }

        return bestDirection.normalized;
    }

    Vector3 GetDynamicObstacleAvoidance(Vector3 desiredDirection)
    {
        if (!enableDynamicObstacleAvoidance)
        {
            return Vector3.zero;
        }

        if (dynamicObstacleLayer.value == 0)
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

        if (hitCount <= 0)
        {
            return Vector3.zero;
        }

        Vector3 droneVelocity;

        if (currentMoveDirection.sqrMagnitude > 0.001f)
        {
            droneVelocity = currentMoveDirection.normalized * currentMoveSpeed;
        }
        else
        {
            droneVelocity = desiredDirection.normalized * currentMoveSpeed;
        }

        Vector3 totalAvoidance = Vector3.zero;

        for (int i = 0; i < hitCount; i++)
        {
            Collider obstacle = dynamicObstacleHits[i];

            if (obstacle == null)
            {
                continue;
            }

            if (obstacle.transform == transform || obstacle.transform.IsChildOf(transform))
            {
                continue;
            }

            Rigidbody obstacleRb = obstacle.attachedRigidbody;

            if (obstacleRb == rb)
            {
                continue;
            }

            Vector3 obstaclePosition = obstacle.bounds.center;

            Vector3 obstacleVelocity = Vector3.zero;

            if (obstacleRb != null)
            {
                obstacleVelocity = obstacleRb.velocity;
            }

            Vector3 relativePosition = obstaclePosition - transform.position;
            Vector3 relativeVelocity = obstacleVelocity - droneVelocity;

            float relativeSpeedSqr = relativeVelocity.sqrMagnitude;

            if (relativeSpeedSqr < dynamicMinRelativeSpeed * dynamicMinRelativeSpeed)
            {
                float closeDistance = relativePosition.magnitude;

                if (closeDistance < dynamicThreatRadius)
                {
                    Vector3 away = -relativePosition.normalized;
                    totalAvoidance += away * (1f - closeDistance / dynamicThreatRadius);
                }

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

        Vector3 up = Vector3.up;
        Vector3 down = Vector3.down;
        Vector3 backward = -forward;

        Vector3[] dodgeCandidates =
        {
            rawDodgeDirection,
            right,
            -right,
            up,
            down,
            backward,
            (right + up).normalized,
            (-right + up).normalized,
            (right + down).normalized,
            (-right + down).normalized,
            (backward + up).normalized,
            (backward + down).normalized
        };

        Vector3 bestDodge = rawDodgeDirection;
        float bestScore = Vector3.Dot(bestDodge, rawDodgeDirection);

        foreach (Vector3 candidateRaw in dodgeCandidates)
        {
            if (candidateRaw.sqrMagnitude < 0.001f)
            {
                continue;
            }

            Vector3 candidate = candidateRaw.normalized;

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

            float downPenalty = 0f;

            if (candidate.y < 0f)
            {
                downPenalty = Mathf.Abs(candidate.y) * (1f - dynamicDownwardWeight);
            }

            float backwardScore = 0f;
            float backwardDot = Vector3.Dot(candidate, backward);

            if (backwardDot > 0f)
            {
                backwardScore = backwardDot * dynamicBackwardWeight;
            }

            float heightPenalty = 0f;

            if (limitFlightHeight)
            {
                float predictedY = transform.position.y + candidate.y * candidateCheckDistance;

                if (predictedY < minFlightY || predictedY > maxFlightY)
                {
                    heightPenalty = 5f;
                }
            }

            float score =
                escapeScore * 3f +
                clearanceScore * 2f +
                upScore +
                backwardScore -
                downPenalty -
                heightPenalty;

            if (score > bestScore)
            {
                bestScore = score;
                bestDodge = candidate;
            }
        }

        return bestDodge.normalized;
    }

    bool IsDirectionBlocked(Vector3 direction, float distance)
    {
        if (obstacleLayer.value == 0)
        {
            return false;
        }

        if (direction.sqrMagnitude < 0.001f)
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
        if (obstacleLayer.value == 0)
        {
            return candidateCheckDistance;
        }

        if (direction.sqrMagnitude < 0.001f)
        {
            return 0f;
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

    void CheckCollisionExplosion()
    {
        if (explodeOnCollisionLayer.value == 0)
        {
            return;
        }

        bool touchingExplosionLayer = Physics.CheckSphere(
            transform.position,
            collisionExplodeRadius,
            explodeOnCollisionLayer,
            QueryTriggerInteraction.Ignore
        );

        if (touchingExplosionLayer)
        {
            Explode();
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
            state != DroneState.Exploding;

        lastStuckCheckPosition = transform.position;
        lastStuckCheckTime = Time.time;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (IsInLayerMask(collision.gameObject.layer, explodeOnCollisionLayer))
        {
            Explode();
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (IsInLayerMask(other.gameObject.layer, explodeOnCollisionLayer))
        {
            Explode();
        }
    }

    bool IsInLayerMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }

    void Explode()
    {
        if (state == DroneState.Exploding)
        {
            return;
        }

        state = DroneState.Exploding;

        if (explosionPrefab != null)
        {
            Instantiate(explosionPrefab, transform.position, Quaternion.identity);
        }

        if (manager != null)
        {
            manager.NotifyDroneExploded(this, originSpawnIndex);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    public void PrepareForPool()
    {
        state = DroneState.Exploding;

        currentNavigationWaypoint = null;
        currentPatrolWaypoint = null;

        outOfRangeTimer = 0f;
        isStuck = false;

        isAlerted = false;
        alertTimer = 0f;
        currentAlertDetectRange = 0f;

        lastAvoidDirection = Vector3.zero;
        avoidanceMemoryTimer = 0f;
        currentMoveSpeed = 0f;

        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, giveUpRange);

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, explodeRange);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, obstacleAvoidRadius);

        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, collisionExplodeRadius);

        Gizmos.color = Color.gray;
        Gizmos.DrawWireSphere(transform.position, emergencyAvoidRadius);

        Gizmos.color = Color.black;
        Gizmos.DrawWireSphere(transform.position, dynamicObstacleDetectRadius);

        if (currentPatrolWaypoint != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(transform.position, currentPatrolWaypoint.position);
            Gizmos.DrawWireSphere(currentPatrolWaypoint.position, 0.6f);
        }

        if (currentNavigationWaypoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, currentNavigationWaypoint.position);
            Gizmos.DrawWireSphere(currentNavigationWaypoint.position, 0.6f);
        }
    }
}