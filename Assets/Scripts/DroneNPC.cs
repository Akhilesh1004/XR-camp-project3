using System.Collections;
using UnityEngine;

public class DroneNPC : MonoBehaviour
{
    private enum DroneState
    {
        Idle,
        Chasing,
        Returning,
        Exploded
    }

    [Header("目標設定")]
    public string playerTag = "Player";

    [Header("偵測與追逐")]
    [Tooltip("Player 進入這個範圍後，無人機開始追逐")]
    public float detectRange = 10f;

    [Tooltip("Player 拉開超過這個距離後，無人機放棄追逐並回原點")]
    public float giveUpRange = 18f;

    [Tooltip("追逐玩家的速度")]
    public float chaseSpeed = 5f;

    [Tooltip("回到原點的速度")]
    public float returnSpeed = 3f;

    [Tooltip("旋轉面向目標的速度")]
    public float rotateSpeed = 8f;

    [Header("爆炸設定")]
    [Tooltip("距離玩家小於這個距離時爆炸")]
    public float explodeRange = 1.2f;

    [Tooltip("爆炸特效 Prefab，可不填")]
    public GameObject explosionPrefab;

    [Tooltip("爆炸後幾秒重新生成")]
    public float respawnDelay = 3f;

    [Header("可選：傷害設定")]
    public bool dealDamage = false;
    public int damageAmount = 20;

    [Header("簡易避障")]
    public bool enableObstacleAvoidance = true;

    [Tooltip("建築物 / 牆壁 / 障礙物所在 Layer")]
    public LayerMask obstacleLayer;

    [Tooltip("無人機前方多遠開始偵測障礙物")]
    public float obstacleDetectDistance = 4f;

    [Tooltip("SphereCast 半徑，越大越不容易貼牆")]
    public float obstacleAvoidRadius = 0.6f;

    [Tooltip("避障轉向強度")]
    public float obstacleAvoidStrength = 2.5f;

    [Tooltip("遇到障礙物時往上繞的傾向")]
    public float upwardAvoidStrength = 1.2f;

    [Header("子彈設定")]
    public string bulletTag = "Bullet";

    private DroneState state = DroneState.Idle;

    private Transform player;
    private Vector3 originPosition;
    private Quaternion originRotation;

    private Rigidbody rb;
    private Renderer[] renderers;
    private Collider[] colliders;

    void Start()
    {
        originPosition = transform.position;
        originRotation = transform.rotation;

        rb = GetComponent<Rigidbody>();
        renderers = GetComponentsInChildren<Renderer>();
        colliders = GetComponentsInChildren<Collider>();

        FindPlayer();
    }

    void Update()
    {
        if (state == DroneState.Exploded)
        {
            return;
        }

        if (player == null)
        {
            FindPlayer();
            return;
        }

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        switch (state)
        {
            case DroneState.Idle:
                HandleIdle(distanceToPlayer);
                break;

            case DroneState.Chasing:
                HandleChasing(distanceToPlayer);
                break;

            case DroneState.Returning:
                HandleReturning(distanceToPlayer);
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

    void HandleIdle(float distanceToPlayer)
    {
        if (distanceToPlayer <= detectRange)
        {
            state = DroneState.Chasing;
        }
    }

    void HandleChasing(float distanceToPlayer)
    {
        if (distanceToPlayer >= giveUpRange)
        {
            state = DroneState.Returning;
            return;
        }

        if (distanceToPlayer <= explodeRange)
        {
            Explode();
            return;
        }

        MoveTowards(player.position, chaseSpeed);
    }

    void HandleReturning(float distanceToPlayer)
    {
        // 回原點途中，如果玩家又靠近，重新追逐
        if (distanceToPlayer <= detectRange)
        {
            state = DroneState.Chasing;
            return;
        }

        float distanceToOrigin = Vector3.Distance(transform.position, originPosition);

        if (distanceToOrigin <= 0.1f)
        {
            SetPositionAndRotation(originPosition, originRotation);
            state = DroneState.Idle;
            return;
        }

        MoveTowards(originPosition, returnSpeed);
    }

    void MoveTowards(Vector3 targetPosition, float speed)
    {
        // 不固定高度：直接追玩家實際位置
        Vector3 toTarget = targetPosition - transform.position;

        if (toTarget.sqrMagnitude < 0.001f)
        {
            return;
        }

        Vector3 moveDirection = toTarget.normalized;

        if (enableObstacleAvoidance)
        {
            moveDirection = GetAvoidedDirection(moveDirection, targetPosition);
        }

        Vector3 nextPosition = transform.position + moveDirection * speed * Time.deltaTime;

        if (rb != null)
        {
            rb.MovePosition(nextPosition);
        }
        else
        {
            transform.position = nextPosition;
        }

        RotateTowards(moveDirection);
    }

    Vector3 GetAvoidedDirection(Vector3 desiredDirection, Vector3 targetPosition)
    {
        bool blocked = Physics.SphereCast(
            transform.position,
            obstacleAvoidRadius,
            desiredDirection,
            out RaycastHit hit,
            obstacleDetectDistance,
            obstacleLayer,
            QueryTriggerInteraction.Ignore
        );

        if (!blocked)
        {
            return desiredDirection;
        }

        // 沿著障礙物表面滑開，而不是直直撞上去
        Vector3 slideDirection = Vector3.ProjectOnPlane(desiredDirection, hit.normal);

        if (slideDirection.sqrMagnitude < 0.001f)
        {
            slideDirection = transform.right;
        }

        slideDirection.Normalize();

        // 左右兩個方向，選比較接近目標的那一邊
        Vector3 rightAvoid = Vector3.Cross(Vector3.up, hit.normal).normalized;
        Vector3 leftAvoid = -rightAvoid;

        Vector3 toTarget = (targetPosition - transform.position).normalized;

        if (Vector3.Dot(leftAvoid, toTarget) > Vector3.Dot(rightAvoid, toTarget))
        {
            rightAvoid = leftAvoid;
        }

        Vector3 avoidDirection =
            slideDirection +
            rightAvoid * obstacleAvoidStrength +
            Vector3.up * upwardAvoidStrength;

        return avoidDirection.normalized;
    }

    void RotateTowards(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            Time.deltaTime * rotateSpeed
        );
    }

    void Explode()
    {
        state = DroneState.Exploded;

        if (explosionPrefab != null)
        {
            Instantiate(explosionPrefab, transform.position, Quaternion.identity);
        }

        if (dealDamage && player != null)
        {
            // 如果你的 Player 有 Health 腳本，可以在這裡改成你的傷害函式
            // 範例：
            // player.GetComponent<PlayerHealth>()?.TakeDamage(damageAmount);
        }

        StartCoroutine(RespawnRoutine());
    }

    void ExplodeWithDamage()
    {
        state = DroneState.Exploded;

        if (explosionPrefab != null)
        {
            Instantiate(explosionPrefab, transform.position, Quaternion.identity);
        }

        StartCoroutine(RespawnRoutine());
    }

    IEnumerator RespawnRoutine()
    {
        SetDroneVisible(false);
        SetDroneCollision(false);

        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        yield return new WaitForSeconds(respawnDelay);

        SetPositionAndRotation(originPosition, originRotation);

        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        SetDroneVisible(true);
        SetDroneCollision(true);

        state = DroneState.Idle;
    }

    void SetPositionAndRotation(Vector3 position, Quaternion rotation)
    {
        if (rb != null)
        {
            rb.position = position;
            rb.rotation = rotation;
        }

        transform.position = position;
        transform.rotation = rotation;
    }

    void SetDroneVisible(bool visible)
    {
        foreach (Renderer r in renderers)
        {
            if (r != null)
            {
                r.enabled = visible;
            }
        }
    }

    void SetDroneCollision(bool enabled)
    {
        foreach (Collider c in colliders)
        {
            if (c != null)
            {
                c.enabled = enabled;
            }
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
    }

    void OnTriggerEnter(Collider other)
    {
        if (state == DroneState.Exploded)
        {
            return;
        }

        if (other.CompareTag(bulletTag))
        {
            ExplodeWithDamage();
        }
    }
}