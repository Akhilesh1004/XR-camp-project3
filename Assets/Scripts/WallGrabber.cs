using System.Collections.Generic;
using UnityEngine;

public class WallGrabber : MonoBehaviour
{
    [Header("綁定物件")]
    public Rigidbody playerRigidbody;

    [Tooltip("玩家身上的 CapsuleCollider，用來防止爬牆 / 上屋頂時穿模")]
    public CapsuleCollider playerCapsule;

    [Tooltip("實際手部 / 控制器 Transform，用來做 CheckSphere")]
    public Transform handTransform;

    [Tooltip("OVR CameraRig 裡的 TrackingSpace")]
    public Transform trackingSpaceTransform;

    [Tooltip("頭盔攝影機，例如 CenterEyeAnchor。用來決定爬上屋頂的前方")]
    public Transform headCamera;

    [Header("互斥系統")]
    [Tooltip("把左右手的 WebSwinger 都拖進來。正在擺盪時抓牆會暫停蛛絲")]
    public WebSwinger[] webSwingers;

    [Header("抓牆設定")]
    public LayerMask wallLayer;
    public float handDetectRadius = 0.12f;

    public OVRInput.Controller controller = OVRInput.Controller.RTouch;
    public OVRInput.Button grabButton = OVRInput.Button.PrimaryHandTrigger;

    [Header("玩家碰撞設定")]
    [Tooltip("玩家移動時不能穿過的 Layer。通常是 Building / Wall / Roof / Ground。不要包含 Player / Hand")]
    public LayerMask collisionLayer;

    public float skinWidth = 0.05f;
    public float maxClimbStepPerFixedUpdate = 0.18f;

    [Header("攀爬與甩動設定")]
    public float throwMultiplier = 1.2f;
    public float maxThrowSpeed = 8f;

    [Header("自動爬上一般牆頂")]
    public bool enableAutoClimbOver = true;

    [Tooltip("可站立表面，例如 Roof / Ground / Building")]
    public LayerMask walkableLayer;

    [Tooltip("從手的前方多少距離開始往下找屋頂")]
    public float ledgeForwardProbe = 0.55f;

    [Tooltip("從手的上方多少高度開始往下找屋頂")]
    public float ledgeUpProbe = 0.35f;

    [Tooltip("往下找屋頂的距離")]
    public float ledgeDownProbe = 1.2f;

    [Tooltip("找屋頂時的 SphereCast 半徑。比 Raycast 穩")]
    public float ledgeProbeRadius = 0.18f;

    [Tooltip("成功爬上屋頂後，往前推一點，避免卡在邊緣")]
    public float climbForwardNudge = 0.45f;

    [Tooltip("玩家腳底離屋頂表面的安全高度")]
    public float climbUpClearance = 0.06f;

    [Tooltip("屋頂 / 可站立表面的最大角度")]
    public float maxWalkableSurfaceAngle = 60f;

    [Tooltip("爬上屋頂後的水平速度")]
    public float climbOverExitSpeed = 1.2f;

    [Tooltip("避免連續觸發 climb over")]
    public float climbOverCooldown = 0.35f;

    [Header("震動")]
    public float grabVibrationFrequency = 0.1f;
    public float grabVibrationAmplitude = 0.3f;
    public float grabVibrationDuration = 0.1f;

    private bool isTouchingWall = false;
    private bool isGrabbing = false;

    private Vector3 previousHandLocalPos;
    private float lastClimbOverTime = -999f;

    private static WallGrabber activeGrabber;
    private static readonly List<WallGrabber> grabbingHands = new List<WallGrabber>();

    public static bool IsGrabbing
    {
        get { return grabbingHands.Count > 0; }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        activeGrabber = null;
        grabbingHands.Clear();
    }

    void Awake()
    {
        if (playerCapsule == null && playerRigidbody != null)
        {
            playerCapsule = playerRigidbody.GetComponent<CapsuleCollider>();
        }
    }

    void Update()
    {
        if (playerRigidbody == null || handTransform == null)
        {
            return;
        }

        isTouchingWall = Physics.CheckSphere(
            handTransform.position,
            handDetectRadius,
            wallLayer,
            QueryTriggerInteraction.Ignore
        );

        if (!isGrabbing && isTouchingWall && OVRInput.GetDown(grabButton, controller))
        {
            StartGrabVibration();
            GrabWall();
        }

        if (isGrabbing && OVRInput.GetUp(grabButton, controller))
        {
            ReleaseWall(true);
        }
    }

    void FixedUpdate()
    {
        if (isGrabbing && activeGrabber == this)
        {
            MovePlayerOnWall();
        }
    }

    void GrabWall()
    {
        if (isGrabbing)
        {
            return;
        }

        if (webSwingers != null)
        {
            foreach (WebSwinger swinger in webSwingers)
            {
                if (swinger != null)
                {
                    swinger.SuspendActiveSwingForWallGrab();
                }
            }
        }

        Debug.Log("Grab wall: " + gameObject.name);

        isGrabbing = true;

        if (!grabbingHands.Contains(this))
        {
            grabbingHands.Add(this);
        }

        SetAsActiveGrabber();

        playerRigidbody.useGravity = false;
        playerRigidbody.velocity = Vector3.zero;
        playerRigidbody.angularVelocity = Vector3.zero;
    }

    void SetAsActiveGrabber()
    {
        activeGrabber = this;
        previousHandLocalPos = OVRInput.GetLocalControllerPosition(controller);
    }

    void MovePlayerOnWall()
    {
        Vector3 currentHandLocalPos = OVRInput.GetLocalControllerPosition(controller);

        Vector3 localDelta = currentHandLocalPos - previousHandLocalPos;
        Vector3 worldDelta = LocalDirectionToWorld(localDelta);

        Vector3 desiredMove = -worldDelta;

        if (desiredMove.magnitude > maxClimbStepPerFixedUpdate)
        {
            desiredMove = desiredMove.normalized * maxClimbStepPerFixedUpdate;
        }

        // 一般牆面：照常用手拉來爬
        SafeMovePlayer(desiredMove);

        // 一般牆頂：偵測前方屋頂平面，成功就自動爬上去
        if (enableAutoClimbOver)
        {
            TryAutoClimbOverLedge();
        }

        previousHandLocalPos = currentHandLocalPos;
    }

    bool TryAutoClimbOverLedge()
    {
        if (Time.time - lastClimbOverTime < climbOverCooldown)
        {
            return false;
        }

        Vector3 forward = GetFlatForward();

        if (forward.sqrMagnitude < 0.001f)
        {
            return false;
        }

        Vector3 probeOrigin =
            handTransform.position +
            forward * ledgeForwardProbe +
            Vector3.up * ledgeUpProbe;

        Debug.DrawRay(
            probeOrigin,
            Vector3.down * ledgeDownProbe,
            Color.yellow,
            0.1f
        );

        if (!Physics.SphereCast(
            probeOrigin,
            ledgeProbeRadius,
            Vector3.down,
            out RaycastHit hit,
            ledgeDownProbe,
            walkableLayer,
            QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        float surfaceAngle = Vector3.Angle(hit.normal, Vector3.up);

        if (surfaceAngle > maxWalkableSurfaceAngle)
        {
            return false;
        }

        Vector3 targetPosition = CalculateStandingPositionOnSurface(hit.point, forward);

        if (!IsCapsuleClearAt(targetPosition))
        {
            Debug.Log("Auto climb blocked: no room for player capsule");
            return false;
        }

        lastClimbOverTime = Time.time;

        playerRigidbody.MovePosition(targetPosition);
        playerRigidbody.velocity = forward * climbOverExitSpeed;
        playerRigidbody.angularVelocity = Vector3.zero;

        Debug.Log("Auto climb over ledge onto: " + hit.collider.name);

        ReleaseWall(false);
        return true;
    }

    Vector3 CalculateStandingPositionOnSurface(Vector3 surfacePoint, Vector3 forward)
    {
        if (playerCapsule == null)
        {
            return playerRigidbody.position +
                   Vector3.up * 0.6f +
                   forward * climbForwardNudge;
        }

        GetCapsuleWorldPoints(out Vector3 p1, out Vector3 p2, out float radius);

        float currentBottomY = Mathf.Min(p1.y, p2.y) - radius;
        float targetBottomY = surfacePoint.y + climbUpClearance;

        float deltaY = targetBottomY - currentBottomY;

        return playerRigidbody.position +
               Vector3.up * deltaY +
               forward * climbForwardNudge;
    }

    void SafeMovePlayer(Vector3 move)
    {
        if (move.sqrMagnitude < 0.000001f)
        {
            return;
        }

        if (playerCapsule == null)
        {
            playerRigidbody.MovePosition(playerRigidbody.position + move);
            return;
        }

        GetCapsuleWorldPoints(out Vector3 p1, out Vector3 p2, out float radius);

        Vector3 direction = move.normalized;
        float distance = move.magnitude;

        float castRadius = Mathf.Max(0.01f, radius - 0.02f);

        if (Physics.CapsuleCast(
            p1,
            p2,
            castRadius,
            direction,
            out RaycastHit hit,
            distance + skinWidth,
            collisionLayer,
            QueryTriggerInteraction.Ignore))
        {
            float safeDistance = Mathf.Max(0f, hit.distance - skinWidth);
            playerRigidbody.MovePosition(playerRigidbody.position + direction * safeDistance);
        }
        else
        {
            playerRigidbody.MovePosition(playerRigidbody.position + move);
        }
    }

    bool IsCapsuleClearAt(Vector3 targetRootPosition)
    {
        if (playerCapsule == null)
        {
            return true;
        }

        GetCapsuleWorldPoints(out Vector3 p1, out Vector3 p2, out float radius);

        Vector3 offset = targetRootPosition - playerRigidbody.position;
        float checkRadius = Mathf.Max(0.01f, radius - 0.03f);

        bool blocked = Physics.CheckCapsule(
            p1 + offset,
            p2 + offset,
            checkRadius,
            collisionLayer,
            QueryTriggerInteraction.Ignore
        );

        return !blocked;
    }

    void GetCapsuleWorldPoints(out Vector3 p1, out Vector3 p2, out float radius)
    {
        float scaleY = Mathf.Abs(playerCapsule.transform.lossyScale.y);
        float scaleX = Mathf.Abs(playerCapsule.transform.lossyScale.x);
        float scaleZ = Mathf.Abs(playerCapsule.transform.lossyScale.z);

        radius = playerCapsule.radius * Mathf.Max(scaleX, scaleZ);
        float height = Mathf.Max(playerCapsule.height * scaleY, radius * 2f);

        Vector3 center = playerCapsule.transform.TransformPoint(playerCapsule.center);

        float halfHeight = Mathf.Max(0f, height * 0.5f - radius);

        p1 = center + Vector3.up * halfHeight;
        p2 = center - Vector3.up * halfHeight;
    }

    Vector3 LocalDirectionToWorld(Vector3 localDirection)
    {
        if (trackingSpaceTransform != null)
        {
            return trackingSpaceTransform.TransformDirection(localDirection);
        }

        return playerRigidbody.transform.TransformDirection(localDirection);
    }

    Vector3 GetFlatForward()
    {
        Transform forwardSource = headCamera != null ? headCamera : trackingSpaceTransform;

        if (forwardSource != null)
        {
            Vector3 forward = forwardSource.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude > 0.001f)
            {
                return forward.normalized;
            }
        }

        Vector3 fallback = playerRigidbody.transform.forward;
        fallback.y = 0f;

        if (fallback.sqrMagnitude > 0.001f)
        {
            return fallback.normalized;
        }

        return Vector3.forward;
    }

    void ReleaseWall(bool allowThrow)
    {
        if (!isGrabbing)
        {
            return;
        }

        Debug.Log("Release wall: " + gameObject.name);

        bool wasActive = activeGrabber == this;

        isGrabbing = false;
        grabbingHands.Remove(this);

        if (grabbingHands.Count > 0)
        {
            if (wasActive)
            {
                WallGrabber nextGrabber = grabbingHands[grabbingHands.Count - 1];
                nextGrabber.SetAsActiveGrabber();

                playerRigidbody.velocity = Vector3.zero;
                playerRigidbody.angularVelocity = Vector3.zero;
            }

            playerRigidbody.useGravity = false;
            return;
        }

        activeGrabber = null;
        playerRigidbody.useGravity = true;

        if (allowThrow)
        {
            Vector3 localHandVelocity = OVRInput.GetLocalControllerVelocity(controller);
            Vector3 worldHandVelocity = LocalDirectionToWorld(localHandVelocity);

            Vector3 throwVelocity = -worldHandVelocity * throwMultiplier;

            if (throwVelocity.magnitude > maxThrowSpeed)
            {
                throwVelocity = throwVelocity.normalized * maxThrowSpeed;
            }

            playerRigidbody.velocity = throwVelocity;
        }
    }

    void OnDisable()
    {
        StopVibration();
        ReleaseWall(false);
    }

    void StartGrabVibration()
    {
        OVRInput.SetControllerVibration(
            grabVibrationFrequency,
            grabVibrationAmplitude,
            controller
        );

        CancelInvoke(nameof(StopVibration));
        Invoke(nameof(StopVibration), grabVibrationDuration);
    }

    void StopVibration()
    {
        OVRInput.SetControllerVibration(0f, 0f, controller);
    }

    void OnDrawGizmosSelected()
    {
        if (handTransform == null)
        {
            return;
        }

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(handTransform.position, handDetectRadius);

        if (enableAutoClimbOver)
        {
            Vector3 forward = GetFlatForward();

            Vector3 probeOrigin =
                handTransform.position +
                forward * ledgeForwardProbe +
                Vector3.up * ledgeUpProbe;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(probeOrigin, ledgeProbeRadius);
            Gizmos.DrawLine(
                probeOrigin,
                probeOrigin + Vector3.down * ledgeDownProbe
            );
        }
    }
}