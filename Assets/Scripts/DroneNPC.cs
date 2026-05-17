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
    public float detectRange = 50f;
    public float giveUpRange = 150f;
    public float giveUpDelay = 4f;
    public float chaseSpeed = 12f;
    public float patrolSpeed = 6f;
    public float rotateSpeed = 12f;
    public Vector3 playerTargetOffset = new Vector3(0f, 1f, 0f);

    [Header("警戒設定")]
    public float alertChaseSpeedMultiplier = 1.25f;
    public float alertGiveUpExtraRange = 60f;

    private bool isAlerted = false;
    private float alertTimer = 0f;
    private float currentAlertDetectRange = 0f;

    [Header("Forced Hunt 強制追擊")]
    public float forcedHunterSpeedMultiplier = 1.35f;

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
    public float explodeRange = 1.3f;
    public LayerMask explodeOnCollisionLayer;
    public float collisionExplodeRadius = 0.45f;
    public GameObject explosionPrefab;

    [Header("爆炸中斷玩家移動能力")]
    public bool interruptPlayerMobilityOnExplode = true;
    public float mobilityInterruptRadius = 3f;
    public LayerMask mobilityInterruptLayer;
    public float mobilityDisableDuration = 0.6f;
    public bool clearPlayerVelocityOnInterrupt = false;

    [Header("Waypoint Graph A*")]
    public DroneWaypointGraph waypointGraph;

    [Header("Graph Patrol")]
    public float minPatrolDestinationDistance = 25f;
    public float maxPatrolDestinationDistance = 120f;
    public float patrolRepathInterval = 3f;
    public int recentDestinationMemory = 3;

    [Header("Graph Chase")]
    public float chaseRepathInterval = 1f;
    public float forcedHuntRepathInterval = 0.7f;
    public float pathNodeReachDistance = 2f;
    public bool directChaseWhenPathClear = true;

    private readonly List<Vector3> currentPath = new List<Vector3>();
    private int currentPathIndex = 0;
    private int currentPatrolGoalIndex = -1;
    private readonly Queue<int> recentPatrolGoals = new Queue<int>();
    private float nextRepathTime = 0f;
    private int pathVariantSeed = 0;

    [Header("近距離避障")]
    public bool enableLocalAvoidance = true;
    public LayerMask obstacleLayer;
    public float obstacleDetectDistance = 12f;
    public float obstacleAvoidRadius = 1f;
    public float obstacleAvoidWeight = 2.3f;
    public float upwardAvoidWeight = 1.2f;
    public float candidateCheckDistance = 7f;
    public float steeringSmooth = 12f;

    [Header("進階局部避障")]
    public bool useAdvancedLocalAvoidance = true;
    public float sideProbeAngle = 35f;
    public float wideProbeAngle = 70f;
    public float targetDirectionWeight = 1.4f;
    public float clearanceWeight = 2.6f;
    public float smoothDirectionWeight = 0.9f;
    public float emergencyAvoidRadius = 2.5f;
    public float emergencyAvoidWeight = 3.5f;
    public float avoidanceMemoryDuration = 0.6f;

    private Vector3 lastAvoidDirection = Vector3.zero;
    private float avoidanceMemoryTimer = 0f;
    private readonly Collider[] nearbyObstacleHits = new Collider[16];

    [Header("動態障礙物閃避")]
    public bool enableDynamicObstacleAvoidance = true;
    public LayerMask dynamicObstacleLayer;
    public float dynamicObstacleDetectRadius = 45f;
    public float dynamicPredictionTime = 1.2f;
    public float dynamicThreatRadius = 3.5f;
    public float dynamicAvoidWeight = 10f;
    public float dynamicUpBias = 0.3f;
    public float dynamicMinRelativeSpeed = 2f;
    public bool allowBackwardDynamicDodge = true;
    public bool allowDownwardDynamicDodge = true;
    public float dynamicBackwardWeight = 0.6f;
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

    [Header("高度限制，可選")]
    public bool limitFlightHeight = false;
    public float minFlightY = 2f;
    public float maxFlightY = 80f;

    private DroneState state = DroneState.Patrol;

    private DroneGameManager manager;
    private Transform player;
    private Rigidbody rb;

    private Vector3 originPosition;
    private Quaternion originRotation;
    private int originSpawnIndex = -1;
    private Transform[] waypoints;

    private Vector3 currentMoveDirection;
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

        currentPath.Clear();
        currentPathIndex = 0;
        currentPatrolGoalIndex = -1;
        nextRepathTime = 0f;
        pathVariantSeed = Random.Range(0, 999999);

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
            rb.angularVelocity = Vector3.zero;
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

        if (isStuck)
        {
            ClearPath();
            nextRepathTime = 0f;
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

        if (player != null)
        {
            float distanceToPlayer = Vector3.Distance(transform.position, player.position);

            if (distanceToPlayer <= currentAlertDetectRange)
            {
                ClearPath();
                state = DroneState.Chasing;
                outOfRangeTimer = 0f;
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
            return;
        }

        if (NeedNewPatrolPath())
        {
            BuildNewPatrolPath();
        }

        FollowCurrentPath(patrolSpeed);
    }

    bool NeedNewPatrolPath()
    {
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

    void BuildNewPatrolPath()
    {
        ClearPath();

        if (waypointGraph == null || waypoints == null || waypoints.Length == 0)
        {
            return;
        }

        int startIndex = waypointGraph.GetClosestWaypointIndex(transform.position, false);
        int goalIndex = ChoosePatrolGoalIndex(startIndex);

        if (goalIndex < 0)
        {
            return;
        }

        currentPatrolGoalIndex = goalIndex;
        RememberPatrolGoal(goalIndex);

        bool found = waypointGraph.TryFindPathPositions(
            transform.position,
            waypoints[goalIndex].position,
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
            currentPath.Add(waypoints[goalIndex].position);
            currentPathIndex = 0;
        }

        nextRepathTime = Time.time + patrolRepathInterval + Random.Range(0f, 0.8f);
        pathVariantSeed++;
    }

    int ChoosePatrolGoalIndex(int startIndex)
    {
        if (waypoints == null || waypoints.Length == 0)
        {
            return -1;
        }

        List<int> candidates = new List<int>();

        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null || i == startIndex)
            {
                continue;
            }

            if (recentPatrolGoals.Contains(i))
            {
                continue;
            }

            float distance = Vector3.Distance(transform.position, waypoints[i].position);

            if (distance < minPatrolDestinationDistance)
            {
                continue;
            }

            if (maxPatrolDestinationDistance > 0f && distance > maxPatrolDestinationDistance)
            {
                continue;
            }

            candidates.Add(i);
        }

        if (candidates.Count == 0)
        {
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] != null && i != startIndex)
                {
                    candidates.Add(i);
                }
            }
        }

        if (candidates.Count == 0)
        {
            return -1;
        }

        return candidates[Random.Range(0, candidates.Count)];
    }

    void RememberPatrolGoal(int goalIndex)
    {
        recentPatrolGoals.Enqueue(goalIndex);

        while (recentPatrolGoals.Count > recentDestinationMemory)
        {
            recentPatrolGoals.Dequeue();
        }
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

        Vector3 finalTarget = player.position + playerTargetOffset;

        float effectiveSpeed = chaseSpeed;

        if (isAlerted)
        {
            effectiveSpeed = chaseSpeed * alertChaseSpeedMultiplier;
        }

        if (isForcedHunter)
        {
            effectiveSpeed = chaseSpeed * forcedHunterSpeedMultiplier;
        }

        if (directChaseWhenPathClear &&
            waypointGraph != null &&
            waypointGraph.HasClearPath(transform.position, finalTarget))
        {
            ClearPath();
            MoveTowards(finalTarget, effectiveSpeed);
            return;
        }

        float interval = isForcedHunter ? forcedHuntRepathInterval : chaseRepathInterval;

        if (currentPath.Count == 0 ||
            currentPathIndex >= currentPath.Count ||
            Time.time >= nextRepathTime ||
            isStuck)
        {
            BuildChasePath(finalTarget, interval);
        }

        if (!FollowCurrentPath(effectiveSpeed))
        {
            MoveTowards(finalTarget, effectiveSpeed);
        }
    }

    void BuildChasePath(Vector3 finalTarget, float interval)
    {
        ClearPath();

        if (waypointGraph == null)
        {
            return;
        }

        bool found = waypointGraph.TryFindPathPositions(
            transform.position,
            finalTarget,
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

        nextRepathTime = Time.time + interval + Random.Range(0f, 0.25f);
        pathVariantSeed++;
    }

    bool FollowCurrentPath(float speed)
    {
        if (currentPath.Count == 0 || currentPathIndex >= currentPath.Count)
        {
            return false;
        }

        Vector3 target = currentPath[currentPathIndex];

        float distance = Vector3.Distance(transform.position, target);

        if (distance <= pathNodeReachDistance)
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

        foreach (Vector3 raw in dodgeCandidates)
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

    void InterruptPlayerMobility()
    {
        if (!interruptPlayerMobilityOnExplode)
        {
            return;
        }

        int mask = mobilityInterruptLayer.value;

        Collider[] hits = mask != 0
            ? Physics.OverlapSphere(
                transform.position,
                mobilityInterruptRadius,
                mobilityInterruptLayer,
                QueryTriggerInteraction.Ignore
            )
            : Physics.OverlapSphere(
                transform.position,
                mobilityInterruptRadius,
                ~0,
                QueryTriggerInteraction.Ignore
            );

        foreach (Collider hit in hits)
        {
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
        state = DroneState.Exploding;

        ClearPath();

        outOfRangeTimer = 0f;
        isStuck = false;

        isAlerted = false;
        alertTimer = 0f;
        currentAlertDetectRange = 0f;
        isForcedHunter = false;

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

        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, mobilityInterruptRadius);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, obstacleAvoidRadius);

        Gizmos.color = Color.gray;
        Gizmos.DrawWireSphere(transform.position, emergencyAvoidRadius);

        Gizmos.color = Color.black;
        Gizmos.DrawWireSphere(transform.position, dynamicObstacleDetectRadius);
    }
}