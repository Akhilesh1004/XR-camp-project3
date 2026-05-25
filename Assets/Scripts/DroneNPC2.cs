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
    public float moveSpeed = 5.0f;
    public float destinationReachDistance = 4f;

    [Header("3D Grid Path")]
    public DroneWaypointGraph grid;
    public float pathNodeReachDistance = 6.5f;
    public float lookAheadDistance = 28f;

    [Tooltip("路徑段落進度推進距離。避免追過 node 後目標跑到身後造成繞圈。")]
    public float pathAdvanceDistance = 2.5f;

    [Tooltip("如果目標點落在身後，直接跳到下一段，不原地轉回去追。")]
    public float behindTargetDotThreshold = -0.15f;
    public float pathRepathInterval = 20f;
    public float minDestinationDistanceFromSpawn = 180f;

    [Header("動態避障：Bullet")]
    public bool enableDynamicObstacleAvoidance = true;
    public LayerMask dynamicObstacleLayer;
    public float dynamicObstacleDetectRadius = 22f;
    public float dynamicAvoidanceInterval = 0.75f;
    public float dynamicPredictionTime = 0.8f;
    public float dynamicThreatRadius = 4.2f;
    public float dynamicAvoidWeight = 4.5f;
    public float dynamicUpBias = 0.3f;
    public float dynamicMinRelativeSpeed = 5f;
    public bool allowBackwardDynamicDodge = true;
    public bool allowDownwardDynamicDodge = true;
    public float dynamicBackwardWeight = 0.7f;
    public float dynamicDownwardWeight = 0.4f;

    [Header("飛行手感")]
    public float acceleration = 4.5f;
    public float deceleration = 6f;
    public float steeringSmooth = 5.5f;
    public float maxBankAngle = 14f;
    public float bankSmooth = 3.5f;

    [Header("Anti-Stuck")]
    public float stuckCheckInterval = 1.4f;
    public float stuckMoveThreshold = 0.18f;
    public int maxStuckCountBeforeNewDestination = 3;
    public float pathRequestTimeout = 6f;
    public float pathNodeTimeout = 5.0f;

    [Header("Performance Throttle")]
    public float movementClearCheckInterval = 1.0f;
    public int blockedStepTolerance = 4;

    [Tooltip("大量 DroneNPC2 時建議 false。送貨路線已由 A* path 保證安全，卡住再重算即可。")]
    public bool hardStopDuringPathFollow = false;

    [Header("受破壞設定")]
    public LayerMask damageLayer;
    public LayerMask destroyOnCollisionLayer;
    public int maxHealth = 1;
    public DroneEffectPool destroyedEffectPool;

    [Header("破壞後警戒 / Forced Hunt")]
    public float alertDuration = 10f;
    public float alertDetectRange = 120f;
    public int forcedHunterCountOnDestroyed = 2;
    public bool chooseClosestHuntersToPlayer = true;

    private readonly List<Vector3> currentPath = new List<Vector3>();
    private readonly Collider[] dynamicObstacleHits = new Collider[24];

    private int currentPathIndex = 0;
    private float nextRepathTime = 0f;
    private int pathVariantSeed = 0;
    private int pathRequestToken = 0;
    private bool waitingForPath = false;
    private float pathRequestStartTime;
    private float currentNodeStartTime;

    private Vector3 destinationPosition;
    private bool hasDestination = false;

    private Vector3 cachedDynamicAvoidance = Vector3.zero;
    private float nextDynamicAvoidanceTime = 0f;
    private int dynamicFrameOffset = 0;

    private float currentSpeed = 0f;
    private float currentBankAngle = 0f;
    private Vector3 currentMoveDirection = Vector3.forward;

    private Vector3 lastStuckCheckPosition;
    private float lastStuckCheckTime;
    private bool isStuck;
    private int stuckCount = 0;

    private float nextMovementClearCheckTime = 0f;
    private bool cachedMovementStepBlocked = false;
    private int blockedStepCount = 0;

    private Drone2State state = Drone2State.MovingToDestination;
    private DroneNPC2Manager manager;
    private Vector3 originPosition;
    private Quaternion originRotation;
    private GameObject currentCargo;
    private int currentHealth;
    private bool hasBeenInitialized = false;
    private bool isFinishing = false;

    void Awake()
    {
        dynamicFrameOffset = Mathf.Abs(GetInstanceID()) % 17;
    }

    public void Initialize(
        DroneNPC2Manager owner,
        Vector3 spawnPosition,
        Quaternion spawnRotation,
        DroneWaypointGraph gridReference
    )
    {
        manager = owner;
        originPosition = spawnPosition;
        originRotation = spawnRotation;
        grid = gridReference;

        if (destroyedEffectPool == null)
        {
            destroyedEffectPool = DroneEffectPool.Instance;
        }

        transform.position = originPosition;
        transform.rotation = originRotation;

        currentMoveDirection = transform.forward;
        currentSpeed = 0f;
        currentBankAngle = 0f;

        currentHealth = maxHealth;
        isFinishing = false;
        state = Drone2State.MovingToDestination;

        lastStuckCheckPosition = transform.position;
        lastStuckCheckTime = Time.time;
        isStuck = false;
        stuckCount = 0;
        nextMovementClearCheckTime = 0f;
        cachedMovementStepBlocked = false;
        blockedStepCount = 0;
        nextMovementClearCheckTime = 0f;
        cachedMovementStepBlocked = false;
        blockedStepCount = 0;

        ClearPath();
        ClearCargo();
        SpawnRandomCargo();

        pathVariantSeed = Random.Range(0, 999999);
        pathRequestToken++;

        PickNewDestination();
        RequestPathToDestination(false);

        hasBeenInitialized = true;
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
        currentSpeed = 0f;
        currentBankAngle = 0f;
        lastStuckCheckPosition = transform.position;
        lastStuckCheckTime = Time.time;
        isStuck = false;
        stuckCount = 0;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        if (state == Drone2State.Finished || isFinishing)
        {
            return;
        }

        CheckStuck();
        CheckPathRequestTimeout();

        if (!hasDestination)
        {
            PickNewDestination();

            if (!hasDestination)
            {
                FinishNormally();
                return;
            }

            RequestPathToDestination(false);
        }

        if (!waitingForPath &&
            Time.time >= nextRepathTime &&
            (currentPath.Count == 0 || currentPathIndex >= currentPath.Count || isStuck || IsCurrentPathSegmentBlocked()))
        {
            RequestPathToDestination(false);
        }

        float distanceToDestination = Vector3.Distance(transform.position, destinationPosition);

        if (distanceToDestination <= destinationReachDistance)
        {
            FinishNormally();
            return;
        }

        FollowCurrentPath(moveSpeed, dt);
    }

    void PickNewDestination()
    {
        hasDestination = false;

        if (grid == null || !grid.IsReady)
        {
            return;
        }

        if (grid.TryGetRandomWalkablePointFarFrom(originPosition, minDestinationDistanceFromSpawn, out Vector3 point))
        {
            destinationPosition = point;
            hasDestination = true;
        }
    }

    bool IsCurrentPathSegmentBlocked()
    {
        if (grid == null || currentPath.Count == 0 || currentPathIndex >= currentPath.Count)
        {
            return false;
        }

        return !grid.HasClearPath(transform.position, currentPath[currentPathIndex]);
    }

    void RequestPathToDestination(bool highPriority)
    {
        if (!hasDestination || grid == null || !grid.IsReady)
        {
            return;
        }

        waitingForPath = true;
        pathRequestStartTime = Time.time;
        ClearPath();

        int token = ++pathRequestToken;
        int variant = pathVariantSeed++;
        nextRepathTime = Time.time + pathRepathInterval + Random.Range(0f, 2f);

        DronePathRequestManager.RequestPath(
            grid,
            transform.position,
            destinationPosition,
            variant,
            (success, path) =>
            {
                if (this == null || !gameObject.activeInHierarchy || token != pathRequestToken || state == Drone2State.Finished || isFinishing)
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
                else
                {
                    HandlePathFailure();
                }
            },
            highPriority
        );
    }

    void HandlePathFailure()
    {
        stuckCount++;

        if (stuckCount >= maxStuckCountBeforeNewDestination)
        {
            stuckCount = 0;
            hasDestination = false;
            ClearPath();
        }
    }

    bool FollowCurrentPath(float targetSpeed, float dt)
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

        Vector3 target = GetLookAheadTarget();

        Vector3 toLookTarget = target - transform.position;

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

            target = GetLookAheadTarget();
        }

        MoveTowards(target, targetSpeed, dt);
        return true;
    }

    Vector3 GetLookAheadTarget()
    {
        if (currentPath.Count == 0)
        {
            return transform.position + currentMoveDirection.normalized * lookAheadDistance;
        }

        int index = Mathf.Clamp(currentPathIndex, 0, currentPath.Count - 1);

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

        while (currentPathIndex < currentPath.Count)
        {
            Vector3 node = currentPath[currentPathIndex];
            float distanceToNode = Vector3.Distance(transform.position, node);

            if (distanceToNode <= pathNodeReachDistance)
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

                if (t > 1.0f && distanceToNode <= lookAheadDistance + pathAdvanceDistance)
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

    void ClearPath()
    {
        currentPath.Clear();
        currentPathIndex = 0;
        currentNodeStartTime = Time.time;
    }

    void MoveTowards(Vector3 targetPosition, float targetSpeed, float dt)
    {
        Vector3 toTarget = targetPosition - transform.position;

        if (toTarget.sqrMagnitude < 0.001f)
        {
            ApplySpeed(0f, dt);
            return;
        }

        Vector3 desiredDirection = toTarget.normalized;
        Vector3 dynamicAvoidance = GetDynamicObstacleAvoidanceThrottled(desiredDirection);
        Vector3 finalDirection = desiredDirection;

        if (dynamicAvoidance.sqrMagnitude > 0.001f)
        {
            finalDirection = (desiredDirection + dynamicAvoidance.normalized * dynamicAvoidWeight).normalized;
        }

        currentMoveDirection = Vector3.Slerp(
            currentMoveDirection.sqrMagnitude < 0.001f ? finalDirection : currentMoveDirection,
            finalDirection,
            dt * steeringSmooth
        ).normalized;

        ApplySpeed(targetSpeed, dt);
        Vector3 nextPosition = transform.position + currentMoveDirection * currentSpeed * dt;

        if (GetCachedMovementStepBlocked(nextPosition))
        {
            blockedStepCount++;
            currentSpeed = Mathf.Lerp(currentSpeed, 0f, dt * deceleration);

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

    bool GetCachedMovementStepBlocked(Vector3 nextPosition)
    {
        if (grid == null)
        {
            return false;
        }

        // 送貨 Drone 不要每步硬停；相信 3D Grid path。
        // 如果真的卡住，CheckStuck() 會重算路或換目的地。
        if (!hardStopDuringPathFollow)
        {
            return false;
        }

        if (Time.time < nextMovementClearCheckTime)
        {
            return false;
        }

        nextMovementClearCheckTime =
            Time.time +
            movementClearCheckInterval +
            Random.Range(0f, movementClearCheckInterval * 0.5f);

        cachedMovementStepBlocked = !grid.HasClearPath(transform.position, nextPosition);
        return cachedMovementStepBlocked;
    }

    void ApplySpeed(float targetSpeed, float dt)
    {
        float rate = targetSpeed > currentSpeed ? acceleration : deceleration;
        currentSpeed = Mathf.Lerp(currentSpeed, targetSpeed, dt * rate);
    }

    Vector3 GetDynamicObstacleAvoidanceThrottled(Vector3 desiredDirection)
    {
        // 1. CD 時間還沒到，直接沿用上一次的避障結果。
        if (Time.time < nextDynamicAvoidanceTime)
        {
            return cachedDynamicAvoidance;
        }

        // 2. CD 到了，但還不是這台 Drone 的分幀 slot，繼續等，避免同一幀大量 Physics query。
        if (((Time.frameCount + dynamicFrameOffset) % 37) != 0)
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

        return best.normalized;
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

        float movedDistance = Vector3.Distance(transform.position, lastStuckCheckPosition);
        isStuck = movedDistance < stuckMoveThreshold && state != Drone2State.Finished && !isFinishing;

        if (isStuck)
        {
            stuckCount++;

            if (stuckCount >= maxStuckCountBeforeNewDestination)
            {
                stuckCount = 0;
                hasDestination = false;
                ClearPath();
            }
        }
        else
        {
            stuckCount = 0;
        }

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
            HandlePathFailure();
        }
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

        if (destroyedEffectPool == null)
        {
            destroyedEffectPool = DroneEffectPool.Instance;
        }

        if (destroyedEffectPool != null)
        {
            destroyedEffectPool.Play(transform.position, Quaternion.identity);
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
            manager.NotifyDroneFinished(this, true);
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
            manager.NotifyDroneFinished(this, false);
        }
        else
        {
            gameObject.SetActive(false);
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

    public void PrepareForPool()
    {
        pathRequestToken++;
        waitingForPath = false;
        ClearCargo();
        ClearPath();
        isFinishing = false;
        state = Drone2State.Finished;
        hasDestination = false;
        destinationPosition = Vector3.zero;
        currentMoveDirection = transform.forward;
        currentSpeed = 0f;
        currentBankAngle = 0f;
        currentHealth = maxHealth;
        isStuck = false;
        stuckCount = 0;
        blockedStepCount = 0;
        cachedMovementStepBlocked = false;
        nextMovementClearCheckTime = 0f;
    }
}
