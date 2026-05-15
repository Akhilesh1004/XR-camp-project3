using UnityEngine;

public class ArmSwingLocomotion : MonoBehaviour
{
    public static ArmSwingLocomotion Instance { get; private set; }

    [Header("綁定物件")]
    public Rigidbody playerRigidbody;

    [Tooltip("頭盔攝影機，備用方向來源")]
    public Transform headCamera;

    [Header("控制器設定")]
    public OVRInput.Controller leftController = OVRInput.Controller.LTouch;
    public OVRInput.Controller rightController = OVRInput.Controller.RTouch;

    [Tooltip("左手手把 Anchor，例如 LeftHandAnchor")]
    public Transform leftControllerAnchor;

    [Tooltip("右手手把 Anchor，例如 RightHandAnchor")]
    public Transform rightControllerAnchor;

    [Header("跑步參數")]
    [Tooltip("擺臂速度轉換成移動速度的倍率")]
    public float swingMultiplier = 2.5f;

    [Tooltip("最大水平移動速度")]
    public float maxMoveSpeed = 10f;

    [Tooltip("左右手總擺動速度門檻")]
    public float activationThreshold = 1.2f;

    [Tooltip("每一隻手各自需要超過的擺動速度門檻")]
    public float perHandActivationThreshold = 0.45f;

    [Tooltip("是否要求雙手都要擺動才可以前進")]
    public bool requireBothHands = true;

    [Tooltip("擺臂輸入平滑，越大反應越快，越小越不敏感")]
    public float swingInputSmooth = 8f;

    [Tooltip("停止擺臂後的減速速度")]
    public float deceleration = 5f;

    [Tooltip("加速平滑，越大加速越快")]
    public float accelerationSmooth = 4f;

    [Header("手把前進方向穩定")]
    [Tooltip("使用手把 forward 作為前進方向")]
    public bool useControllerForwardDirection = true;

    [Tooltip("手把方向平滑，越大越跟手，越小越穩")]
    public float controllerDirectionSmooth = 6f;

    [Tooltip("手把平均方向太小時，不更新方向，避免左右手方向互相抵消造成抖動")]
    public float minControllerForwardMagnitude = 0.2f;

    [Tooltip("左右手方向差太多時，沿用上一個穩定方向")]
    public float maxControllerForwardAngle = 120f;

    [Header("地面偵測")]
    public LayerMask groundLayer;
    public float groundCheckDistance = 1.1f;
    public float groundCheckRadius = 0.25f;
    public float maxGroundAngle = 30f;

    private bool isGrounded;
    private Vector3 groundNormal = Vector3.up;

    private float smoothedSwingSpeed = 0f;

    private Vector3 smoothedMoveDirection = Vector3.forward;
    private bool hasSmoothedMoveDirection = false;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        if (playerRigidbody == null)
        {
            playerRigidbody = GetComponent<Rigidbody>();
        }

        if (leftControllerAnchor == null)
        {
            GameObject leftHand = GameObject.Find("LeftHandAnchor");

            if (leftHand != null)
            {
                leftControllerAnchor = leftHand.transform;
            }
        }

        if (rightControllerAnchor == null)
        {
            GameObject rightHand = GameObject.Find("RightHandAnchor");

            if (rightHand != null)
            {
                rightControllerAnchor = rightHand.transform;
            }
        }

        if (headCamera == null && Camera.main != null)
        {
            headCamera = Camera.main.transform;
        }
    }

    private void FixedUpdate()
    {
        if (playerRigidbody == null)
        {
            return;
        }

        CheckGrounded();

        /*
         * 優先級：
         * 1. 抓牆最高
         * 2. 蛛絲擺盪次高
         * 3. 空中不能擺臂跑
         * 4. 只有在地面且沒有其他移動技能時，才允許擺臂跑
         */
        if (WallGrabber.IsGrabbing || WebSwinger.IsSwinging || !isGrounded)
        {
            smoothedSwingSpeed = 0f;
            return;
        }

        ProcessArmSwing();
    }

    public bool ReturnGrounded()
    {
        CheckGrounded();
        return isGrounded;
    }

    private void CheckGrounded()
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

    private void ProcessArmSwing()
    {
        Vector3 leftVel = OVRInput.GetLocalControllerVelocity(leftController);
        Vector3 rightVel = OVRInput.GetLocalControllerVelocity(rightController);

        float leftSpeed = leftVel.magnitude;
        float rightSpeed = rightVel.magnitude;
        float totalSwingSpeed = leftSpeed + rightSpeed;

        Vector3 currentVelocity = playerRigidbody.velocity;

        Vector3 currentHorizontalVelocity = currentVelocity;
        currentHorizontalVelocity.y = 0f;

        bool leftHandActive = leftSpeed > perHandActivationThreshold;
        bool rightHandActive = rightSpeed > perHandActivationThreshold;

        bool swingActivated;

        if (requireBothHands)
        {
            swingActivated =
                leftHandActive &&
                rightHandActive &&
                totalSwingSpeed > activationThreshold;
        }
        else
        {
            swingActivated = totalSwingSpeed > activationThreshold;
        }

        float targetSwingSpeed = swingActivated ? totalSwingSpeed : 0f;

        smoothedSwingSpeed = Mathf.Lerp(
            smoothedSwingSpeed,
            targetSwingSpeed,
            Time.fixedDeltaTime * swingInputSmooth
        );

        if (smoothedSwingSpeed > activationThreshold)
        {
            Vector3 moveDirection = GetMoveDirection();

            if (moveDirection.sqrMagnitude < 0.001f)
            {
                ApplyDeceleration(currentHorizontalVelocity, currentVelocity);
                return;
            }

            moveDirection.Normalize();

            float effectiveSwingSpeed = smoothedSwingSpeed - activationThreshold;

            float targetSpeed = Mathf.Clamp(
                effectiveSwingSpeed * swingMultiplier,
                0f,
                maxMoveSpeed
            );

            Vector3 targetHorizontalVelocity = moveDirection * targetSpeed;

            Vector3 newHorizontalVelocity = Vector3.Lerp(
                currentHorizontalVelocity,
                targetHorizontalVelocity,
                Time.fixedDeltaTime * accelerationSmooth
            );

            playerRigidbody.velocity = new Vector3(
                newHorizontalVelocity.x,
                currentVelocity.y,
                newHorizontalVelocity.z
            );
        }
        else
        {
            ApplyDeceleration(currentHorizontalVelocity, currentVelocity);
        }
    }

    private Vector3 GetMoveDirection()
    {
        Vector3 rawDirection = Vector3.zero;

        if (useControllerForwardDirection &&
            leftControllerAnchor != null &&
            rightControllerAnchor != null)
        {
            Vector3 leftForward = leftControllerAnchor.forward;
            Vector3 rightForward = rightControllerAnchor.forward;

            leftForward = Vector3.ProjectOnPlane(leftForward, groundNormal);
            rightForward = Vector3.ProjectOnPlane(rightForward, groundNormal);

            if (leftForward.sqrMagnitude > 0.001f)
            {
                leftForward.Normalize();
            }

            if (rightForward.sqrMagnitude > 0.001f)
            {
                rightForward.Normalize();
            }

            float handForwardAngle = Vector3.Angle(leftForward, rightForward);

            if (handForwardAngle > maxControllerForwardAngle)
            {
                if (hasSmoothedMoveDirection)
                {
                    return smoothedMoveDirection;
                }
            }

            rawDirection = (leftForward + rightForward) * 0.5f;
        }
        else if (headCamera != null)
        {
            rawDirection = headCamera.forward;
            rawDirection = Vector3.ProjectOnPlane(rawDirection, groundNormal);
        }
        else
        {
            rawDirection = transform.forward;
            rawDirection = Vector3.ProjectOnPlane(rawDirection, groundNormal);
        }

        if (rawDirection.sqrMagnitude <
            minControllerForwardMagnitude * minControllerForwardMagnitude)
        {
            if (hasSmoothedMoveDirection)
            {
                return smoothedMoveDirection;
            }

            rawDirection = transform.forward;
            rawDirection = Vector3.ProjectOnPlane(rawDirection, groundNormal);
        }

        rawDirection.Normalize();

        if (!hasSmoothedMoveDirection)
        {
            smoothedMoveDirection = rawDirection;
            hasSmoothedMoveDirection = true;
        }
        else
        {
            float t = 1f - Mathf.Exp(
                -controllerDirectionSmooth * Time.fixedDeltaTime
            );

            smoothedMoveDirection = Vector3.Slerp(
                smoothedMoveDirection,
                rawDirection,
                t
            );

            if (smoothedMoveDirection.sqrMagnitude > 0.001f)
            {
                smoothedMoveDirection.Normalize();
            }
        }

        return smoothedMoveDirection;
    }

    private void ApplyDeceleration(
        Vector3 currentHorizontalVelocity,
        Vector3 currentVelocity
    )
    {
        if (currentHorizontalVelocity.magnitude <= 0.1f)
        {
            return;
        }

        Vector3 brakedHorizontalVelocity = Vector3.Lerp(
            currentHorizontalVelocity,
            Vector3.zero,
            Time.fixedDeltaTime * deceleration
        );

        playerRigidbody.velocity = new Vector3(
            brakedHorizontalVelocity.x,
            currentVelocity.y,
            brakedHorizontalVelocity.z
        );
    }

    private void OnDrawGizmosSelected()
    {
        if (playerRigidbody == null)
        {
            return;
        }

        Gizmos.color = isGrounded ? Color.green : Color.red;

        Vector3 origin = playerRigidbody.position + Vector3.up * 0.1f;
        Vector3 end = origin + Vector3.down * groundCheckDistance;

        Gizmos.DrawLine(origin, end);
        Gizmos.DrawWireSphere(end, groundCheckRadius);
    }
}