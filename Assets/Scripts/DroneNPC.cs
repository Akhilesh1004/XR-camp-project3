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
    public Vector3 playerTargetOffset = new Vector3(0f, 1.1f, 0f);

    [Header("AI LOD / FPS 50")]
    [Tooltip("Patrol 狀態多久檢查一次玩家距離。提高可大幅降低 110 台巡邏 Drone 的 CPU。")]
    public float patrolPlayerCheckInterval = 0.6f;

    [Tooltip("Chasing 狀態多久更新一次玩家距離快取。Close Attack 仍會用即時 target。")]
    public float chasePlayerCheckInterval = 0.16f;

    [Tooltip("追擊名額滿時，巡邏 Drone 多久重試一次。避免大量 Drone 每幀搶同一個 chase slot。")]
    public float chaseSlotRetryInterval = 0.35f;

    [Header("偵測與追逐")]
    public float detectRange = 55f;
    public float giveUpRange = 220f;
    public float giveUpDelay = 4f;
    public float patrolSpeed = 5.2f;
    public float chaseSpeed = 10.5f;

    [Header("爆炸設定")]
    public float explodeRange = 2.2f;
    public LayerMask explodeOnCollisionLayer;
    public DroneEffectPool explosionPool;

    [Header("Close Attack")]
    public float closeAttackRange = 22f;
    public float forceDirectAttackRange = 10f;
    public float directChaseLockDuration = 1.0f;
    public bool ignoreAvoidanceDuringCloseAttack = true;
    public float closeAttackCollisionRadius = 0.6f;
    public float blockedCloseAttackExplodeRange = 3.0f;

    [Header("3D Grid Path")]
    public DroneWaypointGraph grid;
    public float pathNodeReachDistance = 6f;
    public float lookAheadDistance = 26f;

    [Tooltip("路徑段落進度推進距離。避免追過 node 後目標跑到身後造成繞圈。")]
    public float pathAdvanceDistance = 2.5f;

    [Tooltip("如果目標點落在身後，直接跳到下一段，不原地轉回去追。")]
    public float behindTargetDotThreshold = -0.15f;
    public float patrolRepathInterval = 16f;
    public float chaseRepathInterval = 5.0f;
    public float forcedHuntRepathInterval = 3.5f;
    public float minPatrolDestinationDistance = 80f;
    public float maxPatrolDestinationDistance = 220f;

    [Header("靜態障礙：Building / Ground")]
    public LayerMask obstacleLayer;

    [Header("動態避障：Bullet")]
    public bool enableDynamicObstacleAvoidance = true;
    public LayerMask dynamicObstacleLayer;
    public float dynamicObstacleDetectRadius = 26f;
    public float dynamicAvoidanceInterval = 0.5f;
    public float dynamicPredictionTime = 0.8f;
    public float dynamicThreatRadius = 4.0f;
    public float dynamicAvoidWeight = 6f;
    public float dynamicUpBias = 0.35f;
    public float dynamicMinRelativeSpeed = 5f;
    public bool allowBackwardDynamicDodge = true;
    public bool allowDownwardDynamicDodge = true;
    public float dynamicBackwardWeight = 0.7f;
    public float dynamicDownwardWeight = 0.4f;

    [Header("飛行手感")]
    public float acceleration = 6f;
    public float deceleration = 8f;
    public float steeringSmooth = 6.5f;
    public float closeAttackSteeringSmooth = 18f;
    public float maxBankAngle = 18f;
    public float bankSmooth = 4f;

    [Header("Anti-Stuck")]
    public float stuckCheckInterval = 1.2f;
    public float stuckMoveThreshold = 0.18f;
    public float pathRequestTimeout = 5f;
    public float pathNodeTimeout = 2.8f;

    [Header("Performance Throttle")]
    public float movementClearCheckInterval = 0.9f;
    public float lineOfSightCheckInterval = 1.0f;
    public int blockedStepTolerance = 4;

    [Tooltip("大量 Drone 時建議 false。一般 path 已由 3D Grid 保證安全，不要每台每段移動都 SphereCast。")]
    public bool hardStopDuringPathFollow = false;

    [Tooltip("Patrol 時是否啟用子彈動態避障。150 台時預設關閉，只讓追擊者更積極閃子彈。")]
    public bool enableDynamicAvoidanceWhilePatrolling = false;

    [Header("Visual / Far LOD")]
    public bool enableVisualLOD = true;
    public float visualCullDistance = 240f;
    public float visualLODCheckInterval = 0.35f;
    public bool disableFarAnimators = true;
    public bool disableChildMeshColliders = true;
    public bool optimizeRendererSettings = true;
    public bool enableFarSimulationLOD = true;
    public float farSimulationDistance = 240f;
    public float farSimulationInterval = 0.12f;
    public float maxFarSimulationDelta = 0.25f;

    [Header("爆炸中斷玩家移動能力")]
    public bool interruptPlayerMobilityOnExplode = true;
    public float mobilityInterruptRadius = 3f;
    public LayerMask mobilityInterruptLayer;
    public float mobilityDisableDuration = 0.6f;
    public bool clearPlayerVelocityOnInterrupt = false;

    [Header("Alert / Forced Hunt")]
    public float alertChaseSpeedMultiplier = 1.15f;
    public float alertGiveUpExtraRange = 80f;
    public float forcedHunterSpeedMultiplier = 1.25f;

    private readonly List<Vector3> currentPath = new List<Vector3>();
    private readonly Collider[] dynamicObstacleHits = new Collider[24];
    private readonly Collider[] mobilityHits = new Collider[16];

    private int currentPathIndex = 0;
    private float nextRepathTime = 0f;
    private int pathVariantSeed = 0;
    private int pathRequestToken = 0;
    private bool waitingForPath = false;
    private float pathRequestStartTime;
    private float currentNodeStartTime;

    private Vector3 patrolDestination;
    private bool hasPatrolDestination = false;

    private Vector3 cachedDynamicAvoidance = Vector3.zero;
    private float nextDynamicAvoidanceTime = 0f;
    private int dynamicFrameOffset = 0;

    private float currentSpeed = 0f;
    private float currentBankAngle = 0f;
    private Vector3 currentMoveDirection = Vector3.forward;

    private Vector3 lastStuckCheckPosition;
    private float lastStuckCheckTime;
    private bool isStuck;

    private float nextMovementClearCheckTime = 0f;
    private bool cachedMovementStepBlocked = false;
    private int blockedStepCount = 0;
    private float nextLineOfSightCheckTime = 0f;
    private bool cachedLineOfSight = false;

    private bool isAlerted = false;
    private float alertTimer = 0f;
    private float currentAlertDetectRange = 0f;
    private bool isForcedHunter = false;

    private float directChaseLockTimer = 0f;
    private bool isCloseAttacking = false;

    private DroneState state = DroneState.Patrol;
    private DroneGameManager manager;
    private DroneCrowdDirector crowdDirector;
    private Transform player;
    private Vector3 cachedPlayerTarget;
    private float cachedDistanceToPlayer = Mathf.Infinity;
    private float nextPlayerCheckTime = 0f;

    private float outOfRangeTimer = 0f;
    private bool hasBeenInitialized = false;
    private float nextChaseAttemptTime = 0f;
    private readonly DroneVisualOptimizer visualOptimizer = new DroneVisualOptimizer();
    private float nextVisualLODCheckTime = 0f;
    private float nextFarSimulationTime = 0f;
    private float accumulatedFarSimulationDt = 0f;

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

    void Awake()
    {
        dynamicFrameOffset = Mathf.Abs(GetInstanceID()) % 31;
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
        ReleaseCrowdSlots();
    }

    public void Initialize(
        DroneGameManager owner,
        Vector3 spawnPosition,
        Quaternion spawnRotation,
        DroneWaypointGraph gridReference
    )
    {
        manager = owner;
        grid = gridReference;
        crowdDirector = DroneCrowdDirector.GetOrCreate();

        if (explosionPool == null)
        {
            explosionPool = DroneEffectPool.Instance;
        }

        transform.position = spawnPosition;
        transform.rotation = spawnRotation;

        currentMoveDirection = transform.forward;
        currentSpeed = 0f;
        currentBankAngle = 0f;

        ClearPath();
        hasPatrolDestination = false;
        nextRepathTime = Time.time + Random.Range(0.3f, 2f);
        pathVariantSeed = Random.Range(0, 999999);
        pathRequestToken++;
        waitingForPath = false;

        lastStuckCheckPosition = transform.position;
        lastStuckCheckTime = Time.time;
        isStuck = false;
        currentNodeStartTime = Time.time;
        nextMovementClearCheckTime = 0f;
        cachedMovementStepBlocked = false;
        blockedStepCount = 0;
        nextLineOfSightCheckTime = 0f;
        cachedLineOfSight = false;

        outOfRangeTimer = 0f;
        isAlerted = false;
        alertTimer = 0f;
        currentAlertDetectRange = 0f;
        isForcedHunter = false;
        isCloseAttacking = false;
        directChaseLockTimer = 0f;

        state = DroneState.Patrol;
        hasBeenInitialized = true;

        FindPlayer();
        visualOptimizer.Initialize(
            gameObject,
            disableChildMeshColliders,
            optimizeRendererSettings
        );

        nextVisualLODCheckTime = 0f;
        nextFarSimulationTime =
            Time.time +
            Random.Range(0f, Mathf.Max(0.01f, farSimulationInterval));
        accumulatedFarSimulationDt = 0f;

        UpdateVisualLOD(true);

        cachedPlayerTarget = player != null ? GetPlayerTarget() : transform.position;
        cachedDistanceToPlayer = player != null
            ? Vector3.Distance(transform.position, cachedPlayerTarget)
            : Mathf.Infinity;
        nextPlayerCheckTime = Time.time + Random.Range(0f, patrolPlayerCheckInterval);
        nextChaseAttemptTime = Time.time + Random.Range(0f, chaseSlotRetryInterval);
    }

    void Update()
    {
        float dt = Time.deltaTime;

        if (state == DroneState.Exploding)
        {
            return;
        }

        if (player == null)
        {
            FindPlayer();
        }

        UpdateVisualLOD(false);

        if (ShouldThrottleFarSimulation(ref dt))
        {
            return;
        }

        UpdateCachedPlayerInfo();

        UpdateAlertTimer(dt);
        CheckStuck();
        CheckPathRequestTimeout();
        UpdateDirectChaseLock(dt);

        switch (state)
        {
            case DroneState.Patrol:
                HandlePatrol(cachedDistanceToPlayer, dt);
                break;

            case DroneState.Chasing:
                HandleChasing(cachedDistanceToPlayer, dt);
                break;
        }
    }

    void UpdateVisualLOD(bool force)
    {
        if (!enableVisualLOD)
        {
            if (force)
            {
                visualOptimizer.ForceVisible(disableFarAnimators);
            }

            return;
        }

        if (!force && Time.time < nextVisualLODCheckTime)
        {
            return;
        }

        float interval = Mathf.Max(0.05f, visualLODCheckInterval);
        nextVisualLODCheckTime =
            Time.time +
            interval +
            Random.Range(0f, interval * 0.35f);

        bool shouldBeVisible = true;

        if (state != DroneState.Chasing && player != null)
        {
            float cullDistance = Mathf.Max(1f, visualCullDistance);
            shouldBeVisible =
                (transform.position - player.position).sqrMagnitude <=
                cullDistance * cullDistance;
        }

        visualOptimizer.SetVisible(shouldBeVisible, disableFarAnimators);
    }

    bool ShouldThrottleFarSimulation(ref float dt)
    {
        if (!enableFarSimulationLOD ||
            state != DroneState.Patrol ||
            isAlerted ||
            isForcedHunter ||
            player == null)
        {
            accumulatedFarSimulationDt = 0f;
            return false;
        }

        float throttleDistance = Mathf.Max(1f, farSimulationDistance);
        float distanceSqr = (transform.position - player.position).sqrMagnitude;

        if (distanceSqr <= throttleDistance * throttleDistance)
        {
            accumulatedFarSimulationDt = 0f;
            return false;
        }

        accumulatedFarSimulationDt += dt;

        if (Time.time < nextFarSimulationTime)
        {
            return true;
        }

        float interval = Mathf.Max(0.02f, farSimulationInterval);
        nextFarSimulationTime =
            Time.time +
            interval +
            Random.Range(0f, interval * 0.35f);

        dt = Mathf.Min(accumulatedFarSimulationDt, Mathf.Max(dt, maxFarSimulationDelta));
        accumulatedFarSimulationDt = 0f;
        return false;
    }

    void UpdateCachedPlayerInfo()
    {
        if (player == null)
        {
            cachedPlayerTarget = transform.position;
            cachedDistanceToPlayer = Mathf.Infinity;
            nextPlayerCheckTime = Time.time + 0.5f;
            return;
        }

        float interval = state == DroneState.Patrol
            ? patrolPlayerCheckInterval
            : chasePlayerCheckInterval;

        if (isCloseAttacking || directChaseLockTimer > 0f)
        {
            interval = Mathf.Min(interval, 0.06f);
        }

        if (Time.time < nextPlayerCheckTime)
        {
            return;
        }

        cachedPlayerTarget = GetPlayerTarget();
        cachedDistanceToPlayer = Vector3.Distance(transform.position, cachedPlayerTarget);

        nextPlayerCheckTime =
            Time.time +
            interval +
            Random.Range(0f, interval * 0.5f);
    }

    void FindPlayer()
    {
        GameObject playerObject = GameObject.FindGameObjectWithTag(playerTag);

        if (playerObject != null)
        {
            player = playerObject.transform;
        }
    }

    Vector3 GetPlayerTarget()
    {
        if (player == null)
        {
            return transform.position;
        }

        return player.position + playerTargetOffset;
    }

    void HandlePatrol(float distanceToPlayer, float dt)
    {
        if (isCloseAttacking && crowdDirector != null)
        {
            crowdDirector.ExitCloseAttack(this);
        }

        isCloseAttacking = false;
        outOfRangeTimer = 0f;

        float effectiveDetectRange = isAlerted
            ? Mathf.Max(detectRange, currentAlertDetectRange)
            : detectRange;

        if (player != null && distanceToPlayer <= effectiveDetectRange)
        {
            if (Time.time >= nextChaseAttemptTime)
            {
                float retryInterval = Mathf.Max(0.05f, chaseSlotRetryInterval);
                nextChaseAttemptTime =
                    Time.time +
                    retryInterval +
                    Random.Range(0f, retryInterval * 0.5f);

                if (crowdDirector == null || crowdDirector.TryEnterChase(this))
                {
                    ClearPath();
                    state = DroneState.Chasing;
                    nextRepathTime = Time.time + Random.Range(0.2f, 1.0f);
                    return;
                }
            }
        }

        if (NeedNewPatrolPath())
        {
            RequestNewPatrolPath();
        }

        FollowCurrentPath(patrolSpeed, dt, false);
    }

    bool NeedNewPatrolPath()
    {
        if (waitingForPath)
        {
            return false;
        }

        if (!hasPatrolDestination || currentPath.Count == 0 || currentPathIndex >= currentPath.Count)
        {
            return true;
        }

        return Time.time >= nextRepathTime && isStuck;
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

    void HandleChasing(float distanceToPlayer, float dt)
    {
        if (player == null)
        {
            FindPlayer();

            if (player == null)
            {
                ExitChaseToPatrol();
                return;
            }
        }

        Vector3 target = GetPlayerTarget();

        if (distanceToPlayer <= explodeRange)
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
                outOfRangeTimer += dt;

                if (outOfRangeTimer >= giveUpDelay)
                {
                    ExitChaseToPatrol();
                    return;
                }
            }
            else
            {
                outOfRangeTimer = 0f;
            }
        }

        bool shouldCloseAttack =
            distanceToPlayer <= forceDirectAttackRange ||
            distanceToPlayer <= closeAttackRange ||
            directChaseLockTimer > 0f;

        if (shouldCloseAttack)
        {
            bool closeAllowed = crowdDirector == null || crowdDirector.TryEnterCloseAttack(this);

            if (closeAllowed)
            {
                isCloseAttacking = true;
                directChaseLockTimer = directChaseLockDuration;
                ClearPath();
                MoveTowards(target, GetEffectiveChaseSpeed(), dt, true);
                return;
            }
        }

        isCloseAttacking = false;

        if (crowdDirector != null)
        {
            crowdDirector.ExitCloseAttack(this);
        }

        bool hasLineOfSight = HasCachedLineOfSight(target);

        if (hasLineOfSight)
        {
            directChaseLockTimer = Mathf.Max(directChaseLockTimer, 0.25f);
            MoveTowards(target, GetEffectiveChaseSpeed(), dt, false);
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
            RequestPathTo(target, isForcedHunter, interval);
        }

        FollowCurrentPath(GetEffectiveChaseSpeed(), dt, false);
    }

    float GetEffectiveChaseSpeed()
    {
        float speed = chaseSpeed;

        if (isAlerted)
        {
            speed *= alertChaseSpeedMultiplier;
        }

        if (isForcedHunter)
        {
            speed *= forcedHunterSpeedMultiplier;
        }

        return speed;
    }

    void ExitChaseToPatrol()
    {
        ReleaseCrowdSlots();
        outOfRangeTimer = 0f;
        isCloseAttacking = false;
        ClearPath();
        state = DroneState.Patrol;
    }

    void ReleaseCrowdSlots()
    {
        if (crowdDirector != null)
        {
            crowdDirector.ExitChase(this);
            crowdDirector.ExitCloseAttack(this);
        }
    }

    void RequestPathTo(Vector3 target, bool highPriority, float interval)
    {
        if (grid == null || !grid.IsReady)
        {
            return;
        }

        waitingForPath = true;
        pathRequestStartTime = Time.time;
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
                    currentNodeStartTime = Time.time;
                    SkipReachedPathNodes();
                }
            },
            highPriority
        );
    }

    bool FollowCurrentPath(float targetSpeed, float dt, bool closeAttack)
    {
        if (currentPath.Count == 0 || currentPathIndex >= currentPath.Count)
        {
            return false;
        }

        AdvancePathIndexByProgress();

        if (currentPathIndex >= currentPath.Count)
        {
            return false;
        }

        Vector3 lookTarget = GetLookAheadTarget();

        // 防止 Drone 追過頭後，lookTarget 掉到身後，導致原地繞圈。
        Vector3 toLookTarget = lookTarget - transform.position;

        if (toLookTarget.sqrMagnitude > 0.001f &&
            currentMoveDirection.sqrMagnitude > 0.001f &&
            Vector3.Dot(currentMoveDirection.normalized, toLookTarget.normalized) < behindTargetDotThreshold)
        {
            currentPathIndex++;
            currentNodeStartTime = Time.time;

            if (currentPathIndex >= currentPath.Count)
            {
                return false;
            }

            lookTarget = GetLookAheadTarget();
        }

        MoveTowards(lookTarget, targetSpeed, dt, closeAttack);
        return true;
    }

    Vector3 GetLookAheadTarget()
    {
        if (currentPath.Count == 0)
        {
            return transform.position + currentMoveDirection.normalized * lookAheadDistance;
        }

        int index = Mathf.Clamp(currentPathIndex, 0, currentPath.Count - 1);

        // 從目前位置在「目前段落」上的投影點開始往前看，而不是從 transform.position 直接追某個 node。
        Vector3 segmentStart = index == 0 ? transform.position : currentPath[index - 1];
        Vector3 segmentEnd = currentPath[index];

        Vector3 projected = ProjectPointOnSegment(transform.position, segmentStart, segmentEnd);
        Vector3 previous = projected;
        float remaining = lookAheadDistance;

        for (int i = index; i < currentPath.Count; i++)
        {
            Vector3 next = currentPath[i];
            float segment = Vector3.Distance(previous, next);

            if (segment >= remaining)
            {
                return Vector3.Lerp(previous, next, remaining / segment);
            }

            remaining -= segment;
            previous = next;
        }

        return currentPath[currentPath.Count - 1];
    }

    void SkipReachedPathNodes()
    {
        AdvancePathIndexByProgress();
    }

    void AdvancePathIndexByProgress()
    {
        if (currentPath.Count == 0)
        {
            return;
        }

        float nodeReachDistanceSqr = pathNodeReachDistance * pathNodeReachDistance;
        float advanceDistance = lookAheadDistance + pathAdvanceDistance;
        float advanceDistanceSqr = advanceDistance * advanceDistance;

        while (currentPathIndex < currentPath.Count)
        {
            Vector3 node = currentPath[currentPathIndex];
            float distanceToNodeSqr = (transform.position - node).sqrMagnitude;

            if (distanceToNodeSqr <= nodeReachDistanceSqr)
            {
                currentPathIndex++;
                currentNodeStartTime = Time.time;
                continue;
            }

            Vector3 segmentStart = currentPathIndex == 0 ? transform.position : currentPath[currentPathIndex - 1];
            Vector3 segmentEnd = currentPath[currentPathIndex];
            Vector3 segment = segmentEnd - segmentStart;
            float segmentLengthSqr = segment.sqrMagnitude;

            if (segmentLengthSqr > 0.001f)
            {
                float t = Vector3.Dot(transform.position - segmentStart, segment) / segmentLengthSqr;

                // 已經通過這個 node 一段距離，就不要回頭追，直接進下一段。
                if (t > 1.0f && distanceToNodeSqr <= advanceDistanceSqr)
                {
                    currentPathIndex++;
                    currentNodeStartTime = Time.time;
                    continue;
                }
            }

            if (Time.time - currentNodeStartTime > pathNodeTimeout)
            {
                currentPathIndex++;
                currentNodeStartTime = Time.time;
                continue;
            }

            break;
        }
    }

    Vector3 ProjectPointOnSegment(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float lengthSqr = ab.sqrMagnitude;

        if (lengthSqr <= 0.001f)
        {
            return b;
        }

        float t = Vector3.Dot(point - a, ab) / lengthSqr;
        t = Mathf.Clamp01(t);
        return a + ab * t;
    }

    bool IsCurrentPathSegmentBlocked()
    {
        if (grid == null || currentPath.Count == 0 || currentPathIndex >= currentPath.Count)
        {
            return false;
        }

        return !grid.HasClearPath(transform.position, currentPath[currentPathIndex]);
    }

    void ClearPath()
    {
        currentPath.Clear();
        currentPathIndex = 0;
        currentNodeStartTime = Time.time;
    }

    void MoveTowards(Vector3 targetPosition, float targetSpeed, float dt, bool closeAttack)
    {
        Vector3 toTarget = targetPosition - transform.position;

        if (toTarget.sqrMagnitude < 0.001f)
        {
            ApplySpeed(0f, dt);
            return;
        }

        Vector3 desiredDirection = toTarget.normalized;
        Vector3 finalDirection = desiredDirection;

        bool allowDynamicAvoidance =
            enableDynamicObstacleAvoidance &&
            (!(closeAttack && ignoreAvoidanceDuringCloseAttack)) &&
            (state == DroneState.Chasing || enableDynamicAvoidanceWhilePatrolling);

        if (allowDynamicAvoidance)
        {
            Vector3 dynamicAvoidance = GetDynamicObstacleAvoidanceThrottled(desiredDirection);

            if (dynamicAvoidance.sqrMagnitude > 0.001f)
            {
                finalDirection = (desiredDirection + dynamicAvoidance.normalized * dynamicAvoidWeight).normalized;
            }
        }
        else
        {
            cachedDynamicAvoidance = Vector3.zero;
        }

        float steer = closeAttack ? closeAttackSteeringSmooth : steeringSmooth;

        currentMoveDirection = Vector3.Slerp(
            currentMoveDirection.sqrMagnitude < 0.001f ? finalDirection : currentMoveDirection,
            finalDirection,
            dt * steer
        ).normalized;

        ApplySpeed(targetSpeed, dt);

        Vector3 nextPosition = transform.position + currentMoveDirection * currentSpeed * dt;

        if (GetCachedMovementStepBlocked(nextPosition, targetPosition, closeAttack))
        {
            blockedStepCount++;
            currentSpeed = Mathf.Lerp(currentSpeed, 0f, dt * deceleration);

            if (closeAttack)
            {
                return;
            }

            if (blockedStepCount >= blockedStepTolerance)
            {
                isStuck = true;
                ClearPath();
                nextRepathTime = 0f;
                blockedStepCount = 0;
            }

            return;
        }

        blockedStepCount = 0;
        transform.position = nextPosition;

        if (currentSpeed > 0.15f)
        {
            RotateTowards(currentMoveDirection, dt);
        }

        isStuck = false;
    }

    void ApplySpeed(float targetSpeed, float dt)
    {
        float rate = targetSpeed > currentSpeed ? acceleration : deceleration;
        currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, dt * rate);
    }

    bool HasCachedLineOfSight(Vector3 target)
    {
        if (grid == null)
        {
            return false;
        }

        if (Time.time < nextLineOfSightCheckTime)
        {
            return cachedLineOfSight;
        }

        nextLineOfSightCheckTime =
            Time.time +
            lineOfSightCheckInterval +
            Random.Range(0f, lineOfSightCheckInterval * 0.5f);

        cachedLineOfSight = grid.HasClearPath(transform.position, target);
        return cachedLineOfSight;
    }

    bool GetCachedMovementStepBlocked(Vector3 nextPosition, Vector3 attackTarget, bool closeAttack)
    {
        if (closeAttack)
        {
            return IsMovementStepBlocked(nextPosition, attackTarget, true);
        }

        // 一般巡邏 / 遠距追逐：不要每步硬停，否則會原地轉圈且大量 SphereCast。
        // 靜態安全主要交給 A* path；卡住時由 stuck detection / repath 處理。
        if (!hardStopDuringPathFollow)
        {
            return false;
        }

        if (Time.time < nextMovementClearCheckTime)
        {
            // 只快取「沒有 blocked」的結果；blocked 不快取，避免卡住後一直停。
            return false;
        }

        nextMovementClearCheckTime =
            Time.time +
            movementClearCheckInterval +
            Random.Range(0f, movementClearCheckInterval * 0.5f);

        cachedMovementStepBlocked = IsMovementStepBlocked(nextPosition, attackTarget, false);
        return cachedMovementStepBlocked;
    }

    bool IsMovementStepBlocked(Vector3 nextPosition, Vector3 attackTarget, bool closeAttack)
    {
        if (!closeAttack)
        {
            return grid != null && !grid.HasClearPath(transform.position, nextPosition);
        }

        Vector3 movement = nextPosition - transform.position;
        float distance = movement.magnitude;

        if (distance <= 0.001f || obstacleLayer.value == 0)
        {
            return false;
        }

        Vector3 direction = movement / distance;

        bool blocked = Physics.SphereCast(
            transform.position,
            closeAttackCollisionRadius,
            direction,
            out RaycastHit hit,
            distance,
            obstacleLayer,
            QueryTriggerInteraction.Ignore
        );

        if (!blocked)
        {
            return false;
        }

        float explodeDistanceSqr = blockedCloseAttackExplodeRange * blockedCloseAttackExplodeRange;

        if ((transform.position - attackTarget).sqrMagnitude <= explodeDistanceSqr)
        {
            Explode();
        }

        return true;
    }

    Vector3 GetDynamicObstacleAvoidanceThrottled(Vector3 desiredDirection)
    {
        // 1. CD 時間還沒到，直接沿用上一次的避障結果。
        if (Time.time < nextDynamicAvoidanceTime)
        {
            return cachedDynamicAvoidance;
        }

        // 2. CD 到了，但還不是這台 Drone 的分幀 slot，繼續等，避免同一幀大量 Physics query。
        if (((Time.frameCount + dynamicFrameOffset) % 31) != 0)
        {
            return cachedDynamicAvoidance;
        }

        // 3. 只有時間到了，而且剛好輪到自己的 frame slot，才執行高成本掃描。
        nextDynamicAvoidanceTime =
            Time.time +
            dynamicAvoidanceInterval +
            Random.Range(0f, dynamicAvoidanceInterval * 0.5f);

        cachedDynamicAvoidance = GetDynamicObstacleAvoidance(desiredDirection);
        return cachedDynamicAvoidance;
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
            ? currentMoveDirection.normalized * Mathf.Max(currentSpeed, 0.1f)
            : desiredDirection.normalized * Mathf.Max(currentSpeed, 0.1f);

        Vector3 totalAvoidance = Vector3.zero;

        for (int i = 0; i < hitCount; i++)
        {
            Collider obstacle = dynamicObstacleHits[i];

            if (obstacle == null || obstacle.transform == transform || obstacle.transform.IsChildOf(transform))
            {
                continue;
            }

            if (player != null && (obstacle.transform == player || obstacle.transform.IsChildOf(player)))
            {
                continue;
            }

            Rigidbody obstacleRb = obstacle.attachedRigidbody;
            Vector3 obstaclePosition = obstacle.bounds.center;
            Vector3 obstacleVelocity = obstacleRb != null ? obstacleRb.velocity : Vector3.zero;

            Vector3 relativePosition = obstaclePosition - transform.position;
            Vector3 relativeVelocity = obstacleVelocity - droneVelocity;

            float relativeSpeedSqr = relativeVelocity.sqrMagnitude;

            if (relativeSpeedSqr < dynamicMinRelativeSpeed * dynamicMinRelativeSpeed)
            {
                continue;
            }

            float timeToClosest = -Vector3.Dot(relativePosition, relativeVelocity) / relativeSpeedSqr;

            if (timeToClosest < 0f || timeToClosest > dynamicPredictionTime)
            {
                continue;
            }

            Vector3 closestRelativePosition = relativePosition + relativeVelocity * timeToClosest;
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
        Vector3 forward = currentMoveDirection.sqrMagnitude > 0.001f ? currentMoveDirection.normalized : transform.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

        if (right.sqrMagnitude < 0.001f)
        {
            right = transform.right;
        }

        Vector3 backward = -forward;

        Vector3 best = rawDodgeDirection;
        float bestScore = Vector3.Dot(best, rawDodgeDirection);

        ConsiderDynamicDodgeCandidate(rawDodgeDirection, rawDodgeDirection, backward, ref best, ref bestScore);
        ConsiderDynamicDodgeCandidate(right, rawDodgeDirection, backward, ref best, ref bestScore);
        ConsiderDynamicDodgeCandidate(-right, rawDodgeDirection, backward, ref best, ref bestScore);
        ConsiderDynamicDodgeCandidate(Vector3.up, rawDodgeDirection, backward, ref best, ref bestScore);
        ConsiderDynamicDodgeCandidate(Vector3.down, rawDodgeDirection, backward, ref best, ref bestScore);
        ConsiderDynamicDodgeCandidate(backward, rawDodgeDirection, backward, ref best, ref bestScore);
        ConsiderDynamicDodgeCandidate(right + Vector3.up, rawDodgeDirection, backward, ref best, ref bestScore);
        ConsiderDynamicDodgeCandidate(-right + Vector3.up, rawDodgeDirection, backward, ref best, ref bestScore);
        ConsiderDynamicDodgeCandidate(right + Vector3.down, rawDodgeDirection, backward, ref best, ref bestScore);
        ConsiderDynamicDodgeCandidate(-right + Vector3.down, rawDodgeDirection, backward, ref best, ref bestScore);
        ConsiderDynamicDodgeCandidate(backward + Vector3.up, rawDodgeDirection, backward, ref best, ref bestScore);
        ConsiderDynamicDodgeCandidate(backward + Vector3.down, rawDodgeDirection, backward, ref best, ref bestScore);

        return best.normalized;
    }

    void ConsiderDynamicDodgeCandidate(
        Vector3 raw,
        Vector3 rawDodgeDirection,
        Vector3 backward,
        ref Vector3 best,
        ref float bestScore
    )
    {
        if (raw.sqrMagnitude < 0.001f)
        {
            return;
        }

        Vector3 candidate = raw.normalized;

        if (!allowDownwardDynamicDodge && candidate.y < -0.2f)
        {
            return;
        }

        if (!allowBackwardDynamicDodge)
        {
            float backwardAmount = Vector3.Dot(candidate, backward);

            if (backwardAmount > 0.5f)
            {
                return;
            }
        }

        float escapeScore = Vector3.Dot(candidate, rawDodgeDirection);
        float upScore = candidate.y > 0f ? dynamicUpBias : 0f;
        float downPenalty = candidate.y < 0f ? Mathf.Abs(candidate.y) * (1f - dynamicDownwardWeight) : 0f;
        float backwardDot = Vector3.Dot(candidate, backward);
        float backwardScore = backwardDot > 0f ? backwardDot * dynamicBackwardWeight : 0f;
        float score = escapeScore * 3f + upScore + backwardScore - downPenalty;

        if (score > bestScore)
        {
            bestScore = score;
            best = candidate;
        }
    }

    void RotateTowards(Vector3 direction, float dt)
    {
        if (direction.sqrMagnitude < 0.001f)
        {
            return;
        }

        Quaternion lookRotation = Quaternion.LookRotation(direction, Vector3.up);

        Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 flatDirection = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;

        float signedTurn = 0f;

        if (flatForward.sqrMagnitude > 0.001f && flatDirection.sqrMagnitude > 0.001f)
        {
            signedTurn = Vector3.SignedAngle(flatForward, flatDirection, Vector3.up) / 90f;
            signedTurn = Mathf.Clamp(signedTurn, -1f, 1f);
        }

        float targetBank = -signedTurn * maxBankAngle;
        currentBankAngle = Mathf.Lerp(currentBankAngle, targetBank, dt * bankSmooth);

        Quaternion bankRotation = Quaternion.Euler(0f, 0f, currentBankAngle);
        Quaternion targetRotation = lookRotation * bankRotation;

        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, dt * steeringSmooth);
    }

    void CheckStuck()
    {
        if (Time.time - lastStuckCheckTime < stuckCheckInterval)
        {
            return;
        }

        float stuckThresholdSqr = stuckMoveThreshold * stuckMoveThreshold;
        isStuck =
            (transform.position - lastStuckCheckPosition).sqrMagnitude < stuckThresholdSqr &&
            state != DroneState.Exploding;

        lastStuckCheckPosition = transform.position;
        lastStuckCheckTime = Time.time;
    }

    void CheckPathRequestTimeout()
    {
        if (!waitingForPath)
        {
            return;
        }

        if (Time.time - pathRequestStartTime > pathRequestTimeout)
        {
            waitingForPath = false;
            pathRequestToken++;
            ClearPath();
            nextRepathTime = 0f;
        }
    }

    void UpdateDirectChaseLock(float dt)
    {
        if (directChaseLockTimer > 0f)
        {
            directChaseLockTimer -= dt;

            if (directChaseLockTimer < 0f)
            {
                directChaseLockTimer = 0f;
            }
        }
    }

    void HandleDroneNPC2DestroyedAlert(Vector3 alertPosition, float alertDuration, float alertDetectRange)
    {
        isAlerted = true;
        alertTimer = alertDuration;
        currentAlertDetectRange = alertDetectRange;

        if (player != null)
        {
            float alertRangeSqr = currentAlertDetectRange * currentAlertDetectRange;

            if ((transform.position - player.position).sqrMagnitude <= alertRangeSqr)
            {
                if (crowdDirector == null || crowdDirector.TryEnterChase(this))
                {
                    ClearPath();
                    state = DroneState.Chasing;
                    outOfRangeTimer = 0f;
                    nextRepathTime = Time.time + Random.Range(0.2f, 1.2f);
                }
            }
        }
    }

    void UpdateAlertTimer(float dt)
    {
        if (!isAlerted || isForcedHunter)
        {
            return;
        }

        alertTimer -= dt;

        if (alertTimer <= 0f)
        {
            isAlerted = false;
            alertTimer = 0f;
            currentAlertDetectRange = 0f;
        }
    }

    public float GetForcedHuntSelectionDistance(Vector3 alertPosition, bool preferDistanceToPlayer)
    {
        if (preferDistanceToPlayer)
        {
            if (player == null)
            {
                FindPlayer();
            }

            if (player != null)
            {
                return (transform.position - player.position).sqrMagnitude;
            }
        }

        return (transform.position - alertPosition).sqrMagnitude;
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

        if (crowdDirector != null && !crowdDirector.TryEnterChase(this))
        {
            return;
        }

        isForcedHunter = true;
        isAlerted = true;
        alertTimer = 999999f;

        ClearPath();
        outOfRangeTimer = 0f;
        nextRepathTime = Time.time + Random.Range(0.2f, 1.5f);

        state = DroneState.Chasing;
    }

    void OnTriggerEnter(Collider other)
    {
        if (IsInLayerMask(other.gameObject.layer, explodeOnCollisionLayer))
        {
            Explode();
        }
    }

    public void TakeDamage(int damage = 1)
    {
        DestroyByDamage();
    }

    public void DestroyByDamage()
    {
        Explode();
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
        ReleaseCrowdSlots();
        InterruptPlayerMobility();

        if (explosionPool == null)
        {
            explosionPool = DroneEffectPool.Instance;
        }

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

        int mask = mobilityInterruptLayer.value != 0 ? mobilityInterruptLayer.value : ~0;

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

            PlayerMobilityInterruptReceiver receiver = hit.GetComponentInParent<PlayerMobilityInterruptReceiver>();

            if (receiver != null)
            {
                receiver.InterruptMobility(mobilityDisableDuration, clearPlayerVelocityOnInterrupt);
                return;
            }
        }
    }

    public void PrepareForPool()
    {
        pathRequestToken++;
        waitingForPath = false;
        ReleaseCrowdSlots();

        state = DroneState.Exploding;
        ClearPath();

        outOfRangeTimer = 0f;
        isStuck = false;
        isAlerted = false;
        alertTimer = 0f;
        currentAlertDetectRange = 0f;
        isForcedHunter = false;
        isCloseAttacking = false;
        directChaseLockTimer = 0f;
        currentSpeed = 0f;
        currentBankAngle = 0f;
        blockedStepCount = 0;
        cachedMovementStepBlocked = false;
        cachedLineOfSight = false;
        nextMovementClearCheckTime = 0f;
        nextLineOfSightCheckTime = 0f;
        nextVisualLODCheckTime = 0f;
        accumulatedFarSimulationDt = 0f;
        visualOptimizer.ForceVisible(disableFarAnimators);
    }
}
