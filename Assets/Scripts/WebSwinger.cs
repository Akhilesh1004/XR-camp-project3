using UnityEngine;

public class WebSwinger : MonoBehaviour
{
    [Header("綁定物件")]
    public Rigidbody playerRigidbody;
    public Transform handTransform;
    public LineRenderer lineRenderer;

    [Header("擺盪參數設定")]
    public LayerMask swingableLayer;
    public float maxSwingDistance = 200f;

    public float springForce = 10f;
    public float damper = 7f;
    public float massScale = 4.5f;
    public float releaseBoostForce = 5f;

    [Header("自動收線設定")]
    public float autoReelSpeed = 8f;
    public float minWebLength = 2f;
    public bool enableAutoReel = true;

    [Header("輸入設定")]
    public OVRInput.Button swingButton = OVRInput.Button.PrimaryIndexTrigger;
    public OVRInput.Controller controller = OVRInput.Controller.RTouch;

    [Header("額外推力")]
    public float continuousBoostForce = 5f;
    public float outwardPushForce = 2f;

    private SpringJoint joint;
    private Vector3 swingPoint;

    private bool hasPendingSwing = false;
    private Vector3 pendingSwingPoint;

    public static int activeSwingCount = 0;
    public static int pendingSwingCount = 0;

    public static bool IsSwinging
    {
        get { return activeSwingCount > 0 || pendingSwingCount > 0; }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        activeSwingCount = 0;
        pendingSwingCount = 0;
    }

    void Start()
    {
        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 0;
        }
    }

    void Update()
    {
        if (OVRInput.GetDown(swingButton, controller))
        {
            if (WallGrabber.IsGrabbing)
            {
                StartPendingSwing();
            }
            else
            {
                StartSwing();
            }
        }

        if (OVRInput.GetUp(swingButton, controller))
        {
            if (joint != null)
            {
                StopSwing();
            }

            if (hasPendingSwing)
            {
                CancelPendingSwing();
            }
        }

        // 抓牆時預掛蛛絲，放開牆後才真正啟動 SpringJoint
        if (hasPendingSwing && !WallGrabber.IsGrabbing)
        {
            ActivatePendingSwing();
        }

        if (joint != null && enableAutoReel)
        {
            HandleAutoReeling();
        }
    }

    void LateUpdate()
    {
        if (lineRenderer == null)
        {
            return;
        }

        if (joint != null)
        {
            lineRenderer.SetPosition(0, handTransform.position);
            lineRenderer.SetPosition(1, swingPoint);
        }
        else if (hasPendingSwing)
        {
            lineRenderer.SetPosition(0, handTransform.position);
            lineRenderer.SetPosition(1, pendingSwingPoint);
        }
    }

    void FixedUpdate()
    {
        if (joint != null)
        {
            ApplyContinuousForwardForce();
        }
    }

    void StartSwing()
    {
        if (joint != null || hasPendingSwing)
        {
            return;
        }

        if (Physics.Raycast(handTransform.position, handTransform.forward, out RaycastHit hit, maxSwingDistance, swingableLayer))
        {
            CreateSwingJoint(hit.point, true);
        }
    }

    void StartPendingSwing()
    {
        if (joint != null || hasPendingSwing)
        {
            return;
        }

        if (Physics.Raycast(handTransform.position, handTransform.forward, out RaycastHit hit, maxSwingDistance, swingableLayer))
        {
            pendingSwingPoint = hit.point;
            hasPendingSwing = true;
            pendingSwingCount++;

            if (lineRenderer != null)
            {
                lineRenderer.positionCount = 2;
            }

            Debug.Log("Pending swing created: " + gameObject.name);
        }
    }

    void ActivatePendingSwing()
    {
        if (!hasPendingSwing || joint != null)
        {
            return;
        }

        Vector3 point = pendingSwingPoint;

        hasPendingSwing = false;
        pendingSwingCount = Mathf.Max(0, pendingSwingCount - 1);

        // 放開牆後才正式建立 SpringJoint
        // 這裡不給 upBoost，避免跟 WallGrabber 的甩出速度疊太強
        CreateSwingJoint(point, false);

        Debug.Log("Pending swing activated: " + gameObject.name);
    }

    void CancelPendingSwing()
    {
        if (!hasPendingSwing)
        {
            return;
        }

        hasPendingSwing = false;
        pendingSwingCount = Mathf.Max(0, pendingSwingCount - 1);

        if (joint == null && lineRenderer != null)
        {
            lineRenderer.positionCount = 0;
        }

        Debug.Log("Pending swing cancelled: " + gameObject.name);
    }

    void CreateSwingJoint(Vector3 point, bool applyStartBoost)
    {
        if (joint != null)
        {
            return;
        }

        swingPoint = point;

        joint = playerRigidbody.gameObject.AddComponent<SpringJoint>();
        joint.autoConfigureConnectedAnchor = false;
        joint.connectedAnchor = swingPoint;

        activeSwingCount++;

        float distanceFromPoint = Vector3.Distance(playerRigidbody.position, swingPoint);

        joint.maxDistance = distanceFromPoint * 0.9f;
        joint.minDistance = distanceFromPoint * 0.1f;

        joint.spring = springForce;
        joint.damper = damper;
        joint.massScale = massScale;

        if (applyStartBoost)
        {
            playerRigidbody.AddForce(Vector3.up * 5f, ForceMode.Impulse);
        }

        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 2;
        }

        Debug.Log("Swing joint created: " + gameObject.name);
    }

    void StopSwing()
    {
        ForceStopSwing(true);
    }

    public void ForceStopSwing(bool applyBoost)
    {
        if (hasPendingSwing)
        {
            CancelPendingSwing();
        }

        if (joint == null)
        {
            return;
        }

        Destroy(joint);
        joint = null;

        activeSwingCount = Mathf.Max(0, activeSwingCount - 1);

        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 0;
        }

        if (applyBoost && playerRigidbody.velocity.sqrMagnitude > 0.01f)
        {
            Vector3 boostDirection = playerRigidbody.velocity.normalized;
            playerRigidbody.AddForce(boostDirection * releaseBoostForce, ForceMode.Impulse);
        }

        Debug.Log("Swing stopped: " + gameObject.name);
    }

    // 給 WallGrabber 呼叫：
    // 正在擺盪時抓牆，先停掉 SpringJoint；
    // 如果蛛絲按鍵還按著，就把原本蛛絲點轉成 pending swing。
    public void SuspendActiveSwingForWallGrab()
    {
        if (joint == null)
        {
            return;
        }

        Vector3 savedSwingPoint = swingPoint;

        Destroy(joint);
        joint = null;

        activeSwingCount = Mathf.Max(0, activeSwingCount - 1);

        bool swingButtonStillHeld = OVRInput.Get(swingButton, controller);

        if (swingButtonStillHeld)
        {
            pendingSwingPoint = savedSwingPoint;

            if (!hasPendingSwing)
            {
                hasPendingSwing = true;
                pendingSwingCount++;
            }

            if (lineRenderer != null)
            {
                lineRenderer.positionCount = 2;
            }

            Debug.Log("Active swing suspended and converted to pending: " + gameObject.name);
        }
        else
        {
            if (!hasPendingSwing && lineRenderer != null)
            {
                lineRenderer.positionCount = 0;
            }

            Debug.Log("Active swing stopped by wall grab: " + gameObject.name);
        }
    }

    void HandleAutoReeling()
    {
        if (joint == null)
        {
            return;
        }

        joint.maxDistance -= autoReelSpeed * Time.deltaTime;

        if (joint.maxDistance < minWebLength)
        {
            joint.maxDistance = minWebLength;
        }
    }

    void ApplyContinuousForwardForce()
    {
        Vector3 toPoint = swingPoint - playerRigidbody.position;

        if (toPoint.sqrMagnitude < 0.001f)
        {
            return;
        }

        Vector3 toPointDir = toPoint.normalized;

        Vector3 velocity = playerRigidbody.velocity;

        if (velocity.sqrMagnitude < 0.01f)
        {
            return;
        }

        Vector3 velocityDir = velocity.normalized;
        Vector3 tangentDir = Vector3.ProjectOnPlane(velocityDir, toPointDir).normalized;

        playerRigidbody.AddForce(
            tangentDir * continuousBoostForce + (-toPointDir * outwardPushForce),
            ForceMode.Force
        );
    }

    void OnDisable()
    {
        ForceStopSwing(false);
    }
}