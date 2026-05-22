using System.Collections.Generic;
using UnityEngine;

public class DroneNPC : MonoBehaviour
{
    private enum DroneState
    {
        Patrol,
        Chasing,
        Exploding
    }

    [Header("目標設定")]
    public string playerTag = "Player";

    [Header("偵測與追逐")]
    public float detectRange = 55f;
    public float giveUpRange = 180f;
    public float giveUpDelay = 4f;
    public float chaseSpeed = 10.5f;
    public float patrolSpeed = 5.2f;
    public float rotateSpeed = 11f;
    public Vector3 playerTargetOffset = new Vector3(0f, 1f, 0f);

    [Header("警戒設定")]
    public float alertChaseSpeedMultiplier = 1.15f;
    public float alertGiveUpExtraRange = 80f;

    private bool isAlerted = false;
    private float alertTimer = 0f;
    private float currentAlertDetectRange = 0f;

    [Header("Forced Hunt 強制追擊")]
    public float forcedHunterSpeedMultiplier = 1.25f;
    private bool isForcedHunter = false;

    public bool CanBecomeForcedHunter
    {
        get
        {
            return hasBeenInitialized &&
                   gameObject.activeInHierarchy &&
                   state != DroneState.Exploding &&
                   !isForcedHunter;
        }
    }

    [Header("爆炸設定")]
    public float explodeRange = 2.0f;
    public LayerMask explodeOnCollisionLayer;
    public float collisionExplodeRadius = 0.45f;
    public DroneEffectPool explosionPool;

    [Header("爆炸中斷玩家移動能力")]
    public bool interruptPlayerMobilityOnExplode = true;
    public float mobilityInterruptRadius = 3f;
    public LayerMask mobilityInterruptLayer;
    public float mobilityDisableDuration = 0.6f;
    public bool clearPlayerVelocityOnInterrupt = false;

    private readonly Collider[] mobilityHits = new Collider[16];

    [Header("3D Grid Path")]
    public DroneWaypointGraph grid;

    [Header("Grid Patrol")]
    public float minPatrolDestinationDistance = 80f;
    public float maxPatrolDestinationDistance = 220f;
    public float patrolRepathInterval = 10f;

    [Header("Grid Chase")]
    public float chaseRepathInterval = 3f;
    public float forcedHuntRepathInterval = 2.2f;
    public float pathNodeReachDistance = 1.8f;

    private readonly List<Vector3> currentPath = new List<Vector3>();
    private int currentPathIndex = 0;
    private float nextRepathTime = 0f;
    private int pathVariantSeed = 0;
    private int pathRequestToken = 0;
    private bool waitingForPath = false;

    private Vector3 patrolDestination;
    private bool hasPatrolDestination = false;

    [Header("自由追逐")]
    public bool directChaseWhenClear = true;
    public float directChaseSpeedMultiplier = 1.05f;
    public float lineOfSightCheckInterval = 0.35f;

    [Tooltip("距離玩家小於這個範圍時，優先使用穩定近距離追擊，避免 A* / 直追反覆切換。")]
    public float closeAttackRange = 22f;

    [Tooltip("距離玩家非常近時直接追，不再等待 grid path。這可以避免卡在玩家旁邊轉圈。")]
    public float forceDirectAttackRange = 10f;

    [Tooltip("直線可追成立後鎖定一小段時間，避免每 0.35 秒在 path / direct chase 間抖動。")]
    public float directChaseLockDuration = 1.0f;

    [Tooltip("近距離撞擊玩家時，忽略動態避障與局部避障，避免把玩家當障礙物繞圈。")]
    public bool ignoreAvoidanceDuringCloseAttack = true;

    [Tooltip("近距離撞擊模式的轉向速度。越大越不會繞圈。")]
    public float closeAttackSteeringSmooth = 18f;

    [Tooltip("近距離撞擊模式不要使用 Grid 的大半徑 hard stop，避免停在玩家眼前。")]
    public bool useSmallCollisionCheckDuringCloseAttack = true;

    [Tooltip("近距離撞擊模式的硬防撞半徑。建議 0.35~0.6，不要用 grid lineCheckRadius=2。")]
    public float closeAttackCollisionRadius = 0.45f;

    [Tooltip("近距離撞擊時如果被防撞擋住，但已經距離目標小於此值，就直接爆炸，避免停在玩家眼前。")]
    public float blockedCloseAttackExplodeRange = 3.0f;

    private float nextLineOfSightCheckTime = 0f;
    private bool cachedCanDirectChase = false;
    private float directChaseLockedUntil = 0f;

    [Header("近距離靜態避障")]
    [Tooltip("100 台無人機建議 false，主要靠 3D Grid + hard collision check。需要更靈活近距追逐時再打開。")]
    public bool enableLocalAvoidance = false;

    public LayerMask obstacleLayer;
    public float obstacleDetectDistance = 8f;
    public float obstacleAvoidRadius = 1f;
    public float obstacleAvoidWeight = 2f;
    public float upwardAvoidWeight = 1f;
    public float candidateCheckDistance = 5f;
    public float steeringSmooth = 9f;

    [Header("進階局部避障")]
    public float sideProbeAngle = 35f;
    public float wideProbeAngle = 70f;
    public float targetDirectionWeight = 1.6f;
    public float clearanceWeight = 2.2f;
    public float smoothDirectionWeight = 1.1f;
    public float emergencyAvoidRadius = 1.8f;
    public float emergencyAvoidWeight = 2.5f;
    public float avoidanceMemoryDuration = 0.6f;

    private Vector3 lastAvoidDirection = Vector3.zero;
    private float avoidanceMemoryTimer = 0f;
    private readonly Collider[] nearbyObstacleHits = new Collider[12];

    [Header("動態障礙物閃避")]
    public bool enableDynamicObstacleAvoidance = true;
    public LayerMask dynamicObstacleLayer;
    public float dynamicObstacleDetectRadius = 35f;
    public float dynamicPredictionTime = 1.1f;
    public float dynamicThreatRadius = 3.5f;
    public float dynamicAvoidWeight = 9f;
    public float dynamicUpBias = 0.3f;
    public float dynamicMinRelativeSpeed = 2f;
    public bool allowBackwardDynamicDodge = true;
    public bool allowDownwardDynamicDodge = true;
    public float dynamicBackwardWeight = 0.6f;
    public float dynamicDownwardWeight = 0.4f;
    public float dynamicAvoidanceInterval = 0.18f;

    private readonly Collider[] dynamicObstacleHits = new Collider[24];
    private Vector3 cachedDynamicAvoidance = Vector3.zero;
    private float nextDynamicAvoidanceTime = 0f;
    private float currentMoveSpeed = 0f;

    [Header("卡住脫困")]
    public float stuckCheckInterval = 0.8f;
    public float stuckMoveThreshold = 0.18f;
    public float stuckUpwardEscapeWeight = 2f;

    private Vector3 lastStuckCheckPosition;
    private float lastStuckCheckTime;
    private bool isStuck;

    [Header("高度限制，可選")]
    public bool limitFlightHeight = false;
    public float minFlightY = 2f;
    public float maxFlightY = 160f;

    private DroneState state = DroneState.Patrol;

    private DroneGameManager manager;
    private Transform player;
    private Rigidbody rb;

    private Vector3 originPosition;
    private Quaternion originRotation;

    private Vector3 currentMoveDirection;
    private float outOfRangeTimer = 0f;
    private bool hasBeenInitialized = false;

    void StopRigidbodyMotion()
    {
        if (rb == null)
        {
            return;
        }

        if (!rb.isKinematic)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    void EnsureExplosionPool()
    {
        if (explosionPool == null)
        {
            explosionPool = DroneEffectPool.Instance;
        }
    }

    public void Initialize(
        DroneGameManager owner,
        Vector3 spawnPosition,
        Quaternion spawnRotation,
        DroneWaypointGraph gridReference
    )
    {
        manager = owner;
        originPosition = spawnPosition;
        originRotation = spawnRotation;
        grid = gridReference;

        EnsureExplosionPool();

        transform.position = originPosition;
        transform.rotation = originRotation;

        if (rb != null)
        {
            rb.position = originPosition;
            rb.rotation = originRotation;
            StopRigidbodyMotion();
        }

        currentMoveDirection = transform.forward;
        currentMoveSpeed = 0f;

        ClearPath();
        hasPatrolDestination = false;

        nextRepathTime = Time.time + Random.Range(0.3f, 2.0f);
        pathVariantSeed = Random.Range(0, 999999);
        pathRequestToken++;
        waitingForPath = false;

        nextLineOfSightCheckTime = Time.time + Random.Range(0f, lineOfSightCheckInterval);
        cachedCanDirectChase = false;
        directChaseLockedUntil = 0f;

        lastStuckCheckPosition = transform.position;
        lastStuckCheckTime = Time.time;
        isStuck = false;

        outOfRangeTimer = 0f;
        isAlerted = false;
        alertTimer = 0f;
        currentAlertDetectRange = 0f;
        isForcedHunter = false;

        lastAvoidDirection = Vector3.zero;
        avoidanceMemoryTimer = 0f;

        state = DroneState.Patrol;
        hasBeenInitialized = true;

        FindPlayer();
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();

        if (rb != null)
        {
            rb.useGravity = false;
            rb.isKinematic = true;
        }
    }

    void OnEnable()
    {
        DroneAlertSystem.RegisterDrone(this);
        DroneAlertSystem.OnDroneNPC2Destroyed += HandleDroneNPC2DestroyedAlert;
    }

    void OnDisable()
    {
        DroneAlertSystem.OnDroneNPC2Destroyed -= HandleDroneNPC2DestroyedAlert;
        DroneAlertSystem.UnregisterDrone(this);
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

        float distanceToPlayer = player != null
            ? Vector3.Distance(transform.position, player.position)
            : Mathf.Infinity;

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

        if (player != null)
        {
            float distanceToPlayer = Vector3.Distance(transform.position, player.position);

            if (distanceToPlayer <= currentAlertDetectRange)
            {
                ClearPath();
                state = DroneState.Chasing;
                outOfRangeTimer = 0f;
                nextRepathTime = Time.time + Random.Range(0.2f, 1.2f);
            }
        }
    }

    void UpdateAlertTimer()
    {
        if (!isAlerted || isForcedHunter)
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

    public float GetForcedHuntSelectionDistance(
        Vector3 alertPosition,
        bool preferDistanceToPlayer
    )
    {
        if (preferDistanceToPlayer)
        {
            if (player == null)
            {
                FindPlayer();
            }

            if (player != null)
            {
                return Vector3.Distance(transform.position, player.position);
            }
        }

        return Vector3.Distance(transform.position, alertPosition);
    }

    public void BeginForcedHunt()
    {
        if (state == DroneState.Exploding)
        {
            return;
        }

        if (player == null)
        {
            FindPlayer();
        }

        isForcedHunter = true;
        isAlerted = true;
        alertTimer = 999999f;

        ClearPath();
        outOfRangeTimer = 0f;
        nextRepathTime = Time.time + Random.Range(0.2f, 1.5f);

        state = DroneState.Chasing;
    }

    void HandlePatrol(float distanceToPlayer)
    {
        outOfRangeTimer = 0f;

        float effectiveDetectRange = isAlerted
            ? Mathf.Max(detectRange, currentAlertDetectRange)
            : detectRange;

        if (player != null && distanceToPlayer <= effectiveDetectRange)
        {
            ClearPath();
            state = DroneState.Chasing;
            nextRepathTime = Time.time + Random.Range(0.2f, 1.0f);
            return;
        }

        if (NeedNewPatrolPath())
        {
            RequestNewPatrolPath();
        }

        FollowCurrentPath(patrolSpeed);
    }

    bool NeedNewPatrolPath()
    {
        if (waitingForPath)
        {
            return false;
        }

        if (!hasPatrolDestination)
        {
            return true;
        }

        if (currentPath.Count == 0)
        {
            return true;
        }

        if (currentPathIndex >= currentPath.Count)
        {
            return true;
        }

        if (Time.time >= nextRepathTime && isStuck)
        {
            return true;
        }

        return false;
    }

    void RequestNewPatrolPath()
    {
        if (grid == null || !grid.IsReady)
        {
            return;
        }

        bool gotPoint = grid.TryGetRandomWalkablePointInRange(
            transform.position,
            minPatrolDestinationDistance,
            maxPatrolDestinationDistance,
            out patrolDestination
        );

        if (!gotPoint)
        {
            hasPatrolDestination = false;
            return;
        }

        hasPatrolDestination = true;
        RequestPathTo(patrolDestination, false, patrolRepathInterval);
    }

    void CancelPendingPathAndUseDirectChase()
    {
        if (waitingForPath)
        {
            pathRequestToken++;
            waitingForPath = false;
        }

        ClearPath();
    }

    void HandleChasing(float distanceToPlayer)
    {
        if (player == null)
        {
            FindPlayer();

            if (player == null)
            {
                if (!isForcedHunter)
                {
                    state = DroneState.Patrol;
                }

                return;
            }
        }

        Vector3 finalTarget = player.position + playerTargetOffset;
        float distanceToExplosionTarget = Vector3.Distance(transform.position, finalTarget);

        // Use the body / camera-rig target instead of Player root.
        // This avoids drones circling around the rig root near the floor.
        if (distanceToExplosionTarget <= explodeRange)
        {
            Explode();
            return;
        }

        if (!isForcedHunter)
        {
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
                    ClearPath();
                    state = DroneState.Patrol;
                    return;
                }
            }
            else
            {
                outOfRangeTimer = 0f;
            }
        }
        else
        {
            outOfRangeTimer = 0f;
        }

        float effectiveSpeed = chaseSpeed;

        if (isAlerted)
        {
            effectiveSpeed = chaseSpeed * alertChaseSpeedMultiplier;
        }

        if (isForcedHunter)
        {
            effectiveSpeed = chaseSpeed * forcedHunterSpeedMultiplier;
        }

        bool forceDirectAttack = distanceToExplosionTarget <= forceDirectAttackRange;

        if (Time.time >= nextLineOfSightCheckTime)
        {
            nextLineOfSightCheckTime =
                Time.time +
                lineOfSightCheckInterval +
                Random.Range(0f, lineOfSightCheckInterval * 0.5f);

            cachedCanDirectChase =
                directChaseWhenClear &&
                grid != null &&
                grid.HasClearPath(transform.position, finalTarget);

            if (cachedCanDirectChase)
            {
                directChaseLockedUntil = Time.time + directChaseLockDuration;
            }
        }

        bool lockedDirectChase = Time.time <= directChaseLockedUntil;
        bool closeStableDirectChase =
            distanceToExplosionTarget <= closeAttackRange &&
            (cachedCanDirectChase || lockedDirectChase || forceDirectAttack);

        if (forceDirectAttack || lockedDirectChase || closeStableDirectChase)
        {
            CancelPendingPathAndUseDirectChase();

            bool closeAttackNoAvoidance =
                ignoreAvoidanceDuringCloseAttack &&
                distanceToExplosionTarget <= closeAttackRange;

            MoveTowards(
                finalTarget,
                effectiveSpeed * directChaseSpeedMultiplier,
                closeAttackNoAvoidance,
                true
            );

            return;
        }

        float interval = isForcedHunter ? forcedHuntRepathInterval : chaseRepathInterval;

        if (!waitingForPath &&
            Time.time >= nextRepathTime &&
            (currentPath.Count == 0 ||
             currentPathIndex >= currentPath.Count ||
             isStuck ||
             IsCurrentPathSegmentBlocked()))
        {
            RequestPathTo(finalTarget, isForcedHunter, interval);
        }

        if (!FollowCurrentPath(effectiveSpeed))
        {
            // Wait for queued path instead of doing direct wall-through fallback.
            return;
        }
    }

    void RequestPathTo(Vector3 target, bool highPriority, float interval)
    {
        if (grid == null || !grid.IsReady)
        {
            return;
        }

        waitingForPath = true;
        ClearPath();

        int token = ++pathRequestToken;
        int variant = pathVariantSeed++;

        nextRepathTime = Time.time + interval + Random.Range(0f, interval * 0.35f);

        DronePathRequestManager.RequestPath(
            grid,
            transform.position,
            target,
            variant,
            (success, path) =>
            {
                if (this == null ||
                    !gameObject.activeInHierarchy ||
                    token != pathRequestToken ||
                    state == DroneState.Exploding)
                {
                    return;
                }

                waitingForPath = false;

                if (success && path != null && path.Count > 0)
                {
                    currentPath.Clear();
                    currentPath.AddRange(path);
                    currentPathIndex = 0;
                    isStuck = false;
                    SkipReachedPathNodes();
                }
            },
            highPriority
        );
    }

    bool FollowCurrentPath(float speed)
    {
        if (currentPath.Count == 0 || currentPathIndex >= currentPath.Count)
        {
            return false;
        }

        SkipReachedPathNodes();

        if (currentPathIndex >= currentPath.Count)
        {
            return false;
        }

        MoveTowards(currentPath[currentPathIndex], speed);
        return true;
    }

    void SkipReachedPathNodes()
    {
        while (currentPathIndex < currentPath.Count &&
               Vector3.Distance(transform.position, currentPath[currentPathIndex]) <= pathNodeReachDistance)
        {
            currentPathIndex++;
        }
    }

    bool IsCurrentPathSegmentBlocked()
    {
        if (grid == null)
        {
            return false;
        }

        if (currentPath.Count == 0 || currentPathIndex >= currentPath.Count)
        {
            return false;
        }

        return !grid.HasClearPath(transform.position, currentPath[currentPathIndex]);
    }

    void ClearPath()
    {
        currentPath.Clear();
        currentPathIndex = 0;
    }

    void MoveTowards(
        Vector3 targetPosition,
        float speed,
        bool ignoreAvoidance = false,
        bool useCloseAttackSteering = false
    )
    {
        Vector3 toTarget = targetPosition - transform.position;

        if (toTarget.sqrMagnitude < 0.001f)
        {
            return;
        }

        Vector3 desiredDirection = toTarget.normalized;
        currentMoveSpeed = speed;

        Vector3 steeredDirection = desiredDirection;

        if (!ignoreAvoidance)
        {
            if (enableLocalAvoidance)
            {
                steeredDirection = GetAvoidedDirection(desiredDirection, targetPosition);
            }
            else
            {
                Vector3 dynamicAvoidance = GetDynamicObstacleAvoidanceThrottled(desiredDirection);

                if (dynamicAvoidance.sqrMagnitude > 0.001f)
                {
                    steeredDirection = (
                        desiredDirection +
                        dynamicAvoidance.normalized * dynamicAvoidWeight
                    ).normalized;
                }
            }
        }

        if (currentMoveDirection.sqrMagnitude < 0.001f)
        {
            currentMoveDirection = steeredDirection;
        }
        else
        {
            float turnSpeed = useCloseAttackSteering
                ? closeAttackSteeringSmooth
                : steeringSmooth;

            currentMoveDirection = Vector3.Slerp(
                currentMoveDirection,
                steeredDirection,
                Time.fixedDeltaTime * turnSpeed
            ).normalized;
        }

        Vector3 nextPosition =
            transform.position +
            currentMoveDirection * speed * Time.fixedDeltaTime;

        bool stepBlocked = IsMovementStepBlocked(
            nextPosition,
            targetPosition,
            useCloseAttackSteering
        );

        if (stepBlocked)
        {
            ClearPath();
            nextRepathTime = 0f;
            isStuck = true;

            StopRigidbodyMotion();

            return;
        }

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

        isStuck = false;

        RotateTowards(currentMoveDirection);
    }

    bool IsMovementStepBlocked(
        Vector3 nextPosition,
        Vector3 attackTarget,
        bool isCloseAttackMove
    )
    {
        if (!isCloseAttackMove)
        {
            return grid != null && !grid.HasClearPath(transform.position, nextPosition);
        }

        if (!useSmallCollisionCheckDuringCloseAttack)
        {
            return false;
        }

        Vector3 movement = nextPosition - transform.position;
        float distance = movement.magnitude;

        if (distance <= 0.001f)
        {
            return false;
        }

        Vector3 direction = movement / distance;

        bool blocked = false;

        if (obstacleLayer.value != 0)
        {
            blocked = Physics.SphereCast(
                transform.position,
                closeAttackCollisionRadius,
                direction,
                out RaycastHit hit,
                distance,
                obstacleLayer,
                QueryTriggerInteraction.Ignore
            );
        }
        else if (grid != null)
        {
            // Fallback only. If obstacleLayer is set correctly, close attack will not use the big grid radius.
            blocked = !grid.HasClearPath(transform.position, nextPosition);
        }

        if (!blocked)
        {
            return false;
        }

        float distanceToTarget = Vector3.Distance(transform.position, attackTarget);

        if (distanceToTarget <= blockedCloseAttackExplodeRange)
        {
            Explode();
        }

        return true;
    }

    Vector3 GetDynamicObstacleAvoidanceThrottled(Vector3 desiredDirection)
    {
        if (Time.time < nextDynamicAvoidanceTime)
        {
            return cachedDynamicAvoidance;
        }

        nextDynamicAvoidanceTime =
            Time.time +
            dynamicAvoidanceInterval +
            Random.Range(0f, dynamicAvoidanceInterval * 0.5f);

        cachedDynamicAvoidance = GetDynamicObstacleAvoidance(desiredDirection);
        return cachedDynamicAvoidance;
    }

    Vector3 GetAvoidedDirection(Vector3 desiredDirection, Vector3 targetPosition)
    {
        Vector3 obstacleRepulsion = GetObstacleRepulsion();
        Vector3 dynamicAvoidance = GetDynamicObstacleAvoidanceThrottled(desiredDirection);

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

        Vector3 yawRightSmall = Quaternion.AngleAxis(sideProbeAngle, Vector3.up) * desiredDirection;
        Vector3 yawLeftSmall = Quaternion.AngleAxis(-sideProbeAngle, Vector3.up) * desiredDirection;
        Vector3 yawRightWide = Quaternion.AngleAxis(wideProbeAngle, Vector3.up) * desiredDirection;
        Vector3 yawLeftWide = Quaternion.AngleAxis(-wideProbeAngle, Vector3.up) * desiredDirection;

        Vector3[] candidates =
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
                memoryBonus;

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

        if (hitCount <= 0)
        {
            return Vector3.zero;
        }

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

            // Important: do not dodge the player target.
            // If Player is included in dynamicObstacleLayer, the drone will orbit around the player forever.
            if (player != null &&
                (obstacle.transform == player ||
                 obstacle.transform.IsChildOf(player) ||
                 player.IsChildOf(obstacle.transform) ||
                 obstacle.CompareTag(playerTag)))
            {
                continue;
            }

            Vector3 obstaclePosition = obstacle.bounds.center;
            Vector3 obstacleVelocity = obstacleRb != null ? obstacleRb.velocity : Vector3.zero;

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

        Vector3 backward = -forward;

        Vector3[] dodgeCandidates =
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

            float score =
                escapeScore * 3f +
                clearanceScore * 2f +
                upScore +
                backwardScore -
                downPenalty;

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

        InterruptPlayerMobility();

        if (explosionPool != null)
        {
            explosionPool.Play(transform.position, Quaternion.identity);
        }

        if (manager != null)
        {
            manager.NotifyDroneExploded(this);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    void InterruptPlayerMobility()
    {
        if (!interruptPlayerMobilityOnExplode)
        {
            return;
        }

        int mask = mobilityInterruptLayer.value != 0
            ? mobilityInterruptLayer.value
            : ~0;

        int hitCount = Physics.OverlapSphereNonAlloc(
            transform.position,
            mobilityInterruptRadius,
            mobilityHits,
            mask,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = mobilityHits[i];

            if (hit == null)
            {
                continue;
            }

            PlayerMobilityInterruptReceiver receiver =
                hit.GetComponentInParent<PlayerMobilityInterruptReceiver>();

            if (receiver != null)
            {
                receiver.InterruptMobility(
                    mobilityDisableDuration,
                    clearPlayerVelocityOnInterrupt
                );

                return;
            }
        }
    }

    public void PrepareForPool()
    {
        pathRequestToken++;
        waitingForPath = false;

        state = DroneState.Exploding;

        ClearPath();

        outOfRangeTimer = 0f;
        isStuck = false;

        isAlerted = false;
        alertTimer = 0f;
        currentAlertDetectRange = 0f;
        isForcedHunter = false;

        hasPatrolDestination = false;
        patrolDestination = Vector3.zero;

        lastAvoidDirection = Vector3.zero;
        avoidanceMemoryTimer = 0f;
        currentMoveSpeed = 0f;

        StopRigidbodyMotion();
    }
}
