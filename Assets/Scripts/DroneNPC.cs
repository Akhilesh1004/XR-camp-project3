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
    public float detectRange = 40f;
    public float giveUpRange = 100f;

    [Tooltip("超過 giveUpRange 後，持續幾秒才真正放棄追逐")]
    public float giveUpDelay = 3f;

    public float chaseSpeed = 8f;
    public float patrolSpeed = 4f;
    public float rotateSpeed = 10f;

    [Tooltip("追玩家時的目標偏移，例如 y=1 會追玩家上半身")]
    public Vector3 playerTargetOffset = new Vector3(0f, 1f, 0f);

    [Header("爆炸設定")]
    public float explodeRange = 1.3f;

    [Tooltip("撞到這些 Layer 會爆炸，例如 Building / Wall / Ground")]
    public LayerMask explodeOnCollisionLayer;

    [Tooltip("主動偵測碰撞爆炸用的半徑")]
    public float collisionExplodeRadius = 0.45f;

    public GameObject explosionPrefab;
    public bool destroyAfterExplode = true;

    [Header("Waypoint 巡邏 / 導航")]
    [Tooltip("到 waypoint 多近時，視為抵達")]
    public float waypointReachDistance = 1.5f;

    [Tooltip("追逐時每隔多久重新選一次導航 waypoint")]
    public float repathInterval = 0.25f;

    [Tooltip("巡邏時如果卡住或路徑不通，每隔多久重新選點")]
    public float patrolRepathInterval = 1.0f;

    [Tooltip("如果路徑被這些 Layer 擋住，就改走 waypoint")]
    public LayerMask obstacleLayer;

    [Tooltip("路徑檢查用 SphereCast 半徑，建議接近無人機半徑")]
    public float pathCheckRadius = 0.6f;

    [Tooltip("Waypoint 太遠時是否仍可選。0 代表不限制")]
    public float maxWaypointSelectDistance = 45f;

    [Tooltip("允許 waypoint 比目前位置更遠離目標多少距離。越小越積極追玩家")]
    public float maxDetourExtraDistance = 12f;

    [Tooltip("如果直線看得到玩家，就直接追玩家")]
    public bool directChaseWhenPathClear = true;

    [Header("近距離避障")]
    public bool enableLocalAvoidance = true;
    public float obstacleDetectDistance = 7f;
    public float obstacleAvoidRadius = 0.8f;
    public float obstacleAvoidWeight = 2f;
    public float upwardAvoidWeight = 1.1f;
    public float candidateCheckDistance = 4f;
    public float steeringSmooth = 6.5f;

    [Header("卡住脫困")]
    public float stuckCheckInterval = 0.5f;
    public float stuckMoveThreshold = 0.25f;
    public float stuckUpwardEscapeWeight = 2.5f;

    [Header("高度限制，可選")]
    [Tooltip("不是固定高度，只是防止飛太低或飛太高")]
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

    // 追逐時用的導航 waypoint
    private Transform currentNavigationWaypoint;

    // 巡邏時用的 waypoint
    private Transform currentPatrolWaypoint;

    private Vector3 currentMoveDirection;

    private float lastRepathTime = -999f;
    private float lastPatrolRepathTime = -999f;

    private Vector3 lastStuckCheckPosition;
    private float lastStuckCheckTime;
    private bool isStuck;

    private float outOfRangeTimer = 0f;

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
        currentMoveDirection = transform.forward;

        lastStuckCheckPosition = transform.position;
        lastStuckCheckTime = Time.time;

        state = DroneState.Patrol;
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

    void Start()
    {
        if (originPosition == Vector3.zero)
        {
            originPosition = transform.position;
            originRotation = transform.rotation;
        }

        if (currentMoveDirection.sqrMagnitude < 0.001f)
        {
            currentMoveDirection = transform.forward;
        }

        lastStuckCheckPosition = transform.position;
        lastStuckCheckTime = Time.time;

        FindPlayer();
    }

    void FixedUpdate()
    {
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

    void HandlePatrol(float distanceToPlayer)
    {
        outOfRangeTimer = 0f;
        currentNavigationWaypoint = null;

        if (player != null && distanceToPlayer <= detectRange)
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
        else
        {
            // 沒有 waypoint 可用時，回到出生點附近
            MoveTowards(originPosition, patrolSpeed);
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
            if (!HasClearPath(transform.position, currentPatrolWaypoint.position))
            {
                return true;
            }

            if (isStuck)
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

            float forwardScore = 0f;

            if (currentMoveDirection.sqrMagnitude > 0.001f)
            {
                Vector3 toWaypoint = (waypoint.position - transform.position).normalized;
                forwardScore = Vector3.Dot(currentMoveDirection.normalized, toWaypoint) * 2f;
            }

            float heightScore = 0f;

            if (waypoint.position.y > transform.position.y)
            {
                heightScore = 0.3f;
            }

            float randomScore = Random.Range(0f, 2f);

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

        // 如果找不到看得到的 waypoint，就退而求其次隨機選一個
        if (bestWaypoint == null)
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

        if (distanceToPlayer >= giveUpRange)
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

        MoveTowards(navigationTarget, chaseSpeed);
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
        if (obstacleLayer.value == 0)
        {
            return desiredDirection;
        }

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
            (desiredDirection - Vector3.up * 0.35f).normalized,
            (desiredDirection + right * obstacleAvoidWeight - Vector3.up * 0.25f).normalized,
            (desiredDirection - right * obstacleAvoidWeight - Vector3.up * 0.25f).normalized
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

            float heightPenalty = 0f;

            if (limitFlightHeight)
            {
                float predictedY = transform.position.y + candidate.y * candidateCheckDistance;

                if (predictedY < minFlightY || predictedY > maxFlightY)
                {
                    heightPenalty = 3f;
                }
            }

            float score =
                targetScore * 1.2f +
                clearanceScore * 2.0f +
                stuckBonus -
                heightPenalty;

            if (score > bestScore)
            {
                bestScore = score;
                bestDirection = candidate.normalized;
            }
        }

        return bestDirection.normalized;
    }

    float GetClearDistance(Vector3 direction)
    {
        if (obstacleLayer.value == 0)
        {
            return candidateCheckDistance;
        }

        if (Physics.SphereCast(
            transform.position,
            obstacleAvoidRadius,
            direction,
            out RaycastHit hit,
            candidateCheckDistance,
            obstacleLayer,
            QueryTriggerInteraction.Ignore))
        {
            return hit.distance;
        }

        return candidateCheckDistance;
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
            manager.NotifyDroneDestroyed(this, originSpawnIndex);
        }

        if (destroyAfterExplode)
        {
            Destroy(gameObject);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }

    void SetPositionAndRotation(Vector3 position, Quaternion rotation)
    {
        if (rb != null)
        {
            rb.position = position;
            rb.rotation = rotation;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        transform.position = position;
        transform.rotation = rotation;
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