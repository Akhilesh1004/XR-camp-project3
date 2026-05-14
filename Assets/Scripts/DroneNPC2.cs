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
    public float moveSpeed = 5f;
    public float rotateSpeed = 8f;
    public float waypointReachDistance = 1.3f;

    [Header("目的地限制")]
    public float minDestinationDistanceFromSpawn = 25f;

    [Header("玩家視野外消失")]
    [Tooltip("這個欄位由 DroneNPC2Manager 自動傳入，Prefab 可以保持 None")]
    public Camera playerCamera;

    [Tooltip("這個欄位由 DroneNPC2Manager 自動傳入，Prefab 可以保持空白或設預設")]
    public LayerMask visibilityBlockerLayer;

    public bool disappearOnlyWhenHiddenFromPlayer = true;
    public float minDisappearDistance = 15f;
    public float viewportPadding = 0.1f;

    [Header("避障設定")]
    public LayerMask obstacleLayer;
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

    public float alertDuration = 8f;
    public float alertDetectRange = 120f;

    public GameObject destroyedEffectPrefab;

    [Header("高度限制，可選")]
    public bool limitFlightHeight = false;
    public float minFlightY = 1.5f;
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

        if (rb != null)
        {
            rb.position = originPosition;
            rb.rotation = originRotation;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        currentMoveDirection = transform.forward;
        currentHealth = maxHealth;
        isFinishing = false;
        state = Drone2State.MovingToDestination;

        lastAvoidDirection = Vector3.zero;
        avoidanceMemoryTimer = 0f;

        lastStuckCheckPosition = transform.position;
        lastStuckCheckTime = Time.time;
        isStuck = false;

        ClearCargo();
        SpawnRandomCargo();

        destinationWaypoint = ChooseRandomDestinationNotSpawn();

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

            if (destinationWaypoint == null)
            {
                FinishNormally();
                return;
            }
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

            // 玩家看得到，不能突然消失，改選下一個目的地繼續飛
            destinationWaypoint = ChooseRandomDestinationNotSpawn();

            if (destinationWaypoint == null)
            {
                return;
            }
        }

        MoveTowards(destinationWaypoint.position, moveSpeed);
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
            if (i == originSpawnIndex)
            {
                continue;
            }

            if (waypoints[i] == null)
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

        // 如果沒有符合距離條件的 waypoint，就退回只排除出生點
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

        currentCargo = Instantiate(prefab, parent);
        currentCargo.transform.localPosition = Vector3.zero;
        currentCargo.transform.localRotation = Quaternion.identity;

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
        if (obstacleLayer.value == 0)
        {
            return desiredDirection;
        }

        if (!useAdvancedLocalAvoidance)
        {
            return GetSimpleAvoidedDirection(desiredDirection, targetPosition);
        }

        Vector3 obstacleRepulsion = GetObstacleRepulsion();

        bool frontBlocked = IsDirectionBlocked(
            desiredDirection,
            obstacleDetectDistance
        );

        bool hasEmergencyObstacle = obstacleRepulsion.sqrMagnitude > 0.001f;

        if (!frontBlocked && !hasEmergencyObstacle && !isStuck)
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

        lastAvoidDirection = bestDirection;
        avoidanceMemoryTimer = avoidanceMemoryDuration;

        return bestDirection.normalized;
    }

    Vector3 GetSimpleAvoidedDirection(Vector3 desiredDirection, Vector3 targetPosition)
    {
        bool frontBlocked = Physics.SphereCast(
            transform.position,
            obstacleAvoidRadius,
            desiredDirection,
            out RaycastHit hit,
            obstacleDetectDistance,
            obstacleLayer,
            QueryTriggerInteraction.Ignore
        );

        if (!frontBlocked && !isStuck)
        {
            return desiredDirection;
        }

        Vector3 toTarget = (targetPosition - transform.position).normalized;

        Vector3 right = Vector3.Cross(Vector3.up, desiredDirection).normalized;

        if (right.sqrMagnitude < 0.001f)
        {
            right = transform.right;
        }

        Vector3[] candidateDirections =
        {
            desiredDirection,
            (desiredDirection + right * obstacleAvoidWeight).normalized,
            (desiredDirection - right * obstacleAvoidWeight).normalized,
            (desiredDirection + Vector3.up * upwardAvoidWeight).normalized,
            (desiredDirection + right * obstacleAvoidWeight + Vector3.up * upwardAvoidWeight).normalized,
            (desiredDirection - right * obstacleAvoidWeight + Vector3.up * upwardAvoidWeight).normalized,
            (desiredDirection - Vector3.up * 0.35f).normalized
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

            float stuckBonus = 0f;

            if (isStuck && candidate.y > 0f)
            {
                stuckBonus = stuckUpwardEscapeWeight;
            }

            float score =
                targetScore * 1.2f +
                clearanceScore * 2.0f +
                stuckBonus;

            if (score > bestScore)
            {
                bestScore = score;
                bestDirection = candidate.normalized;
            }
        }

        return bestDirection.normalized;
    }

    bool IsDirectionBlocked(Vector3 direction, float distance)
    {
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
            alertDetectRange
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

        isFinishing = false;
        state = Drone2State.Finished;

        destinationWaypoint = null;
        currentMoveDirection = transform.forward;
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

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, obstacleAvoidRadius);

        Gizmos.color = Color.gray;
        Gizmos.DrawWireSphere(transform.position, emergencyAvoidRadius);

        if (destinationWaypoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, destinationWaypoint.position);
            Gizmos.DrawWireSphere(destinationWaypoint.position, 0.6f);
        }
    }
}