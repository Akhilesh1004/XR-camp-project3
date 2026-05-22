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
    public float moveSpeed = 5.2f;
    public float rotateSpeed = 9f;
    public float destinationReachDistance = 3.5f;

    [Header("3D Grid Path")]
    public DroneWaypointGraph grid;
    public float pathNodeReachDistance = 1.8f;
    public float pathRepathInterval = 8f;
    public float minDestinationDistanceFromSpawn = 160f;

    private readonly List<Vector3> currentPath = new List<Vector3>();
    private int currentPathIndex = 0;
    private float nextRepathTime = 0f;
    private int pathVariantSeed = 0;
    private int pathRequestToken = 0;
    private bool waitingForPath = false;

    private Vector3 destinationPosition;
    private bool hasDestination = false;

    [Header("動態障礙物閃避")]
    public bool enableDynamicObstacleAvoidance = true;
    public LayerMask dynamicObstacleLayer;
    public float dynamicObstacleDetectRadius = 35f;
    public float dynamicPredictionTime = 1.1f;
    public float dynamicThreatRadius = 3.5f;
    public float dynamicAvoidWeight = 8f;
    public float dynamicUpBias = 0.3f;
    public float dynamicMinRelativeSpeed = 2f;
    public bool allowBackwardDynamicDodge = true;
    public bool allowDownwardDynamicDodge = true;
    public float dynamicBackwardWeight = 0.7f;
    public float dynamicDownwardWeight = 0.4f;

    [Tooltip("100 台無人機時，不要每個 FixedUpdate 都掃動態障礙")]
    public float dynamicAvoidanceInterval = 0.18f;

    private readonly Collider[] dynamicObstacleHits = new Collider[24];
    private Vector3 cachedDynamicAvoidance = Vector3.zero;
    private float nextDynamicAvoidanceTime = 0f;
    private float currentMoveSpeed = 0f;

    [Header("卡住脫困")]
    public float stuckCheckInterval = 0.8f;
    public float stuckMoveThreshold = 0.18f;

    private Vector3 lastStuckCheckPosition;
    private float lastStuckCheckTime;
    private bool isStuck;

    [Header("受破壞設定")]
    public LayerMask damageLayer;
    public LayerMask destroyOnCollisionLayer;
    public float collisionCheckRadius = 1f;
    public float collisionCheckInterval = 0.2f;
    public int maxHealth = 1;
    public DroneEffectPool destroyedEffectPool;

    private float nextCollisionCheckTime = 0f;

    [Header("破壞後警戒 / Forced Hunt")]
    public float alertDuration = 10f;
    public float alertDetectRange = 120f;
    public int forcedHunterCountOnDestroyed = 2;
    public bool chooseClosestHuntersToPlayer = true;

    [Header("高度限制，可選")]
    public bool limitFlightHeight = false;
    public float minFlightY = 2f;
    public float maxFlightY = 160f;

    private Drone2State state = Drone2State.MovingToDestination;

    private DroneNPC2Manager manager;
    private Rigidbody rb;

    private Vector3 originPosition;
    private Quaternion originRotation;

    private Vector3 currentMoveDirection;
    private GameObject currentCargo;

    private int currentHealth;
    private bool hasBeenInitialized = false;
    private bool isFinishing = false;

    void EnsureDestroyedEffectPool()
    {
        if (destroyedEffectPool == null)
        {
            destroyedEffectPool = DroneEffectPool.Instance;
        }
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

        EnsureDestroyedEffectPool();

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

        lastStuckCheckPosition = transform.position;
        lastStuckCheckTime = Time.time;
        isStuck = false;

        ClearPath();

        ClearCargo();
        SpawnRandomCargo();

        pathVariantSeed = Random.Range(0, 999999);
        pathRequestToken++;

        PickNewDestination();
        RequestPathToDestination(false);

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

        lastStuckCheckPosition = transform.position;
        lastStuckCheckTime = Time.time;
        isStuck = false;
    }

    void FixedUpdate()
    {
        if (state == Drone2State.Finished || isFinishing)
        {
            return;
        }

        if (Time.time >= nextCollisionCheckTime)
        {
            nextCollisionCheckTime = Time.time + collisionCheckInterval;
            CheckDestroyByCollisionSphere();
        }

        CheckStuck();

        if (state == Drone2State.Finished || isFinishing)
        {
            return;
        }

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
            (currentPath.Count == 0 ||
             currentPathIndex >= currentPath.Count ||
             isStuck ||
             IsCurrentPathSegmentBlocked()))
        {
            RequestPathToDestination(false);
        }

        float distanceToDestination = Vector3.Distance(
            transform.position,
            destinationPosition
        );

        if (distanceToDestination <= destinationReachDistance)
        {
            FinishNormally();
            return;
        }

        if (waitingForPath && currentPath.Count == 0)
        {
            return;
        }

        if (!FollowCurrentPath(moveSpeed))
        {
            // No direct fallback through walls. Wait for path or request another destination.
            if (!waitingForPath)
            {
                RequestPathToDestination(false);
            }
        }
    }

    void PickNewDestination()
    {
        hasDestination = false;

        if (grid == null || !grid.IsReady)
        {
            return;
        }

        if (grid.TryGetRandomWalkablePointFarFrom(
            originPosition,
            minDestinationDistanceFromSpawn,
            out Vector3 point))
        {
            destinationPosition = point;
            hasDestination = true;
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

    void RequestPathToDestination(bool highPriority)
    {
        if (!hasDestination || grid == null || !grid.IsReady)
        {
            return;
        }

        waitingForPath = true;
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
                if (this == null ||
                    !gameObject.activeInHierarchy ||
                    token != pathRequestToken ||
                    state == Drone2State.Finished ||
                    isFinishing)
                {
                    return;
                }

                waitingForPath = false;

                if (success && path != null && path.Count > 0)
                {
                    currentPath.Clear();
                    currentPath.AddRange(path);
                    currentPathIndex = 0;
                    SkipReachedPathNodes();
                }
                else
                {
                    hasDestination = false;
                    ClearPath();
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

        Vector3 dynamicAvoidance = GetDynamicObstacleAvoidanceThrottled(desiredDirection);
        Vector3 moveDirection = desiredDirection;

        if (dynamicAvoidance.sqrMagnitude > 0.001f)
        {
            moveDirection = (
                desiredDirection +
                dynamicAvoidance.normalized * dynamicAvoidWeight
            ).normalized;
        }

        if (currentMoveDirection.sqrMagnitude < 0.001f)
        {
            currentMoveDirection = moveDirection;
        }
        else
        {
            currentMoveDirection = Vector3.Slerp(
                currentMoveDirection,
                moveDirection,
                Time.fixedDeltaTime * 6f
            ).normalized;
        }

        Vector3 nextPosition =
            transform.position +
            currentMoveDirection * speed * Time.fixedDeltaTime;

        if (grid != null && !grid.HasClearPath(transform.position, nextPosition))
        {
            ClearPath();
            nextRepathTime = 0f;
            isStuck = true;

            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

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

        RotateTowards(currentMoveDirection);
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

            float downPenalty = candidate.y < 0f
                ? Mathf.Abs(candidate.y) * (1f - dynamicDownwardWeight)
                : 0f;

            float backwardDot = Vector3.Dot(candidate, backward);
            float backwardScore = backwardDot > 0f
                ? backwardDot * dynamicBackwardWeight
                : 0f;

            float score =
                escapeScore * 3f +
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
            collisionCheckRadius,
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
        currentMoveSpeed = 0f;
        currentHealth = maxHealth;

        isStuck = false;

        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }
}
