using UnityEngine;

public class ArmSwingLocomotion : MonoBehaviour
{
    public static ArmSwingLocomotion Instance { get; private set; }

    [Header("綁定物件")]
    public Rigidbody playerRigidbody;

    [Tooltip("頭盔攝影機，例如 CenterEyeAnchor，用來決定跑步方向")]
    public Transform headCamera;

    [Header("控制器設定")]
    public OVRInput.Controller leftController = OVRInput.Controller.LTouch;
    public OVRInput.Controller rightController = OVRInput.Controller.RTouch;
    public Transform leftControllerAnchor;
    public Transform rightControllerAnchor;

    [Header("跑步參數")]
    public float swingMultiplier = 2.5f;
    public float maxMoveSpeed = 10f;
    public float activationThreshold = 0.5f;
    public float deceleration = 5f;
    public float accelerationSmooth = 4f;

    [Header("地面偵測")]
    public LayerMask groundLayer;
    public float groundCheckDistance = 1.1f;
    public float groundCheckRadius = 0.25f;
    public float maxGroundAngle = 30f;
    private bool isGrounded;
    private Vector3 groundNormal = Vector3.up;

    private void Awake()
    {
        // 初始化單例
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }

        if (leftControllerAnchor == null)
            leftControllerAnchor = GameObject.Find("LeftHandAnchor").transform;
        if (rightControllerAnchor == null)
            rightControllerAnchor = GameObject.Find("RightHandAnchor").transform;
    }

    void FixedUpdate()
    {
        CheckGrounded();

        // 優先級：
        // 1. 抓牆最高
        // 2. 蛛絲擺盪 / pending swing 次高
        // 3. 空中不能跑
        // 4. 只有在地面且沒有其他移動技能時，才允許擺臂跑
        if (WallGrabber.IsGrabbing || WebSwinger.IsSwinging || !isGrounded)
        {
            return;
        }

        ProcessArmSwing();
    }

    public bool ReturnGrounded()
    {
        CheckGrounded();
        return isGrounded;
    }

    void CheckGrounded()
    {
        Vector3 origin = playerRigidbody.position + Vector3.up * 0.1f;

        if (Physics.SphereCast(
            origin,
            groundCheckRadius,
            Vector3.down,
            out RaycastHit hit,
            groundCheckDistance,
            groundLayer,
            QueryTriggerInteraction.Ignore))
        {
            float groundAngle = Vector3.Angle(hit.normal, Vector3.up);

            if (groundAngle <= maxGroundAngle)
            {
                isGrounded = true;
                groundNormal = hit.normal;
                return;
            }
        }

        isGrounded = false;
        groundNormal = Vector3.up;
    }

    void ProcessArmSwing()
    {
        Vector3 leftVel = OVRInput.GetLocalControllerVelocity(leftController);
        Vector3 rightVel = OVRInput.GetLocalControllerVelocity(rightController);

        float totalSwingSpeed = leftVel.magnitude + rightVel.magnitude;

        Vector3 currentHorizontalVelocity = playerRigidbody.velocity;
        currentHorizontalVelocity.y = 0f;

        if (totalSwingSpeed > activationThreshold)
        {
            // Vector3 forwardDirection = Camera.main.transform.forward;
            // forwardDirection.y = 0f;
            // forwardDirection.Normalize();

            // Vector3 moveDirection = forwardDirection;

            // moveDirection.Normalize();

            Vector3 leftForward = leftControllerAnchor.forward;
            Vector3 rightForward = rightControllerAnchor.forward;
            Vector3 combinedForward = (leftForward + rightForward) * 0.5f;
            
            Vector3 moveDirection = Vector3.ProjectOnPlane(combinedForward, groundNormal);

            if (moveDirection.sqrMagnitude < 0.001f)
            {
                return;
            }

            moveDirection.Normalize();

            float targetSpeed = Mathf.Clamp(
                totalSwingSpeed * swingMultiplier,
                0f,
                maxMoveSpeed
            );

            Vector3 targetVelocity = moveDirection * targetSpeed;

            Vector3 newVelocity = Vector3.Lerp(
                currentHorizontalVelocity,
                targetVelocity,
                Time.fixedDeltaTime * accelerationSmooth
            );

            if (isGrounded && newVelocity.y > 0f)
            {
                newVelocity.y = Mathf.Min(newVelocity.y, 1f);
            }

            playerRigidbody.velocity = newVelocity;
        }
        else
        {
            if (currentHorizontalVelocity.magnitude > 0.1f)
            {
                Vector3 brakedHorizontalVelocity = Vector3.Lerp(
                    currentHorizontalVelocity,
                    Vector3.zero,
                    Time.fixedDeltaTime * deceleration
                );

                playerRigidbody.velocity = new Vector3(
                    brakedHorizontalVelocity.x,
                    playerRigidbody.velocity.y,
                    brakedHorizontalVelocity.z
                );
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        if (playerRigidbody == null)
        {
            return;
        }

        Gizmos.color = isGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(
            playerRigidbody.position + Vector3.down * groundCheckDistance,
            groundCheckRadius
        );
    }
}