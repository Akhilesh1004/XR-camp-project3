using UnityEngine;
using System.Collections.Generic;

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
    public float massScale = 6f;
    public float releaseBoostForce = 5f;
    public float boostDuration = 5f;
    private float swingStartTime;

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

    [Header("慢動作效果")]
    public float slowTimeScale = 0.3f;
    private float normalFixedDeltaTime;

    private SpringJoint joint;
    private Vector3 swingPoint;

    private bool hasPendingSwing = false;
    private Vector3 pendingSwingPoint;

    public static int activeSwingCount = 0;
    public static int pendingSwingCount = 0;
    private static List<WebSwinger> activeSwingerScripts = new List<WebSwinger>();
    void OnEnableR() { activeSwingerScripts.Add(this); }
    void OnDisableR() { activeSwingerScripts.Remove(this); }

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
        normalFixedDeltaTime = Time.fixedDeltaTime;
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
            OnEnableR();
            swingStartTime = Time.time;
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
            OnDisableR();
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

        // slow motion for better aiming when按著 A 鍵
        if (OVRInput.GetDown(OVRInput.Button.One) && controller == OVRInput.Controller.RTouch) 
        {
            StartSlowMotion();
        }
        if (OVRInput.GetUp(OVRInput.Button.One) && controller == OVRInput.Controller.RTouch)
        {
            StopSlowMotion();
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
            ArmSwingLocomotion instance = ArmSwingLocomotion.Instance;
            if (instance.ReturnGrounded())
            {
                CreateSwingJoint(hit.point, true);
            }
            else
            {
                CreateSwingJoint(hit.point, false);
            }
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

    void StartSlowMotion()
    {
        Time.timeScale = slowTimeScale;
        // 必須縮小 fixedDeltaTime，物理才不會卡頓
        Time.fixedDeltaTime = normalFixedDeltaTime * Time.timeScale;
    }

    void StopSlowMotion()
    {
        Time.timeScale = 1f;
        Time.fixedDeltaTime = normalFixedDeltaTime;
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
            Vector3 boostDirection = playerRigidbody.velocity;
            boostDirection.y = Mathf.Max(boostDirection.y, 0f);

            if (boostDirection.sqrMagnitude > 0.01f)
            {
                boostDirection.Normalize();
                playerRigidbody.AddForce(boostDirection * releaseBoostForce, ForceMode.Impulse);
            }
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

    bool BothHandsActive()
    {
        return activeSwingCount >= 2;
    }

    void ApplyContinuousForwardForce()
    {
        float powerMultiplier = 1.0f;

        if (activeSwingCount >= 2)
        {
            float angle = GetAngleBetweenWebs();

            if (angle > 150f)
            {
                powerMultiplier = 0f;
            }
            else
            {
                powerMultiplier = 0.5f;
            }
        }
        
        float timeSinceStart = Time.time - swingStartTime;
        if (timeSinceStart > boostDuration)
        {
            powerMultiplier = 0f;
        }

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

        Vector3 tangentDir = Vector3.ProjectOnPlane(velocity, toPointDir);
        if (tangentDir.sqrMagnitude < 0.01f)
        {
            return;
        }

        tangentDir.Normalize();
        Vector3 horizontalTangent = Vector3.ProjectOnPlane(tangentDir, Vector3.up);
        if (horizontalTangent.sqrMagnitude > 0.001f)
        {
            tangentDir = horizontalTangent.normalized;
        }

        Vector3 outwardDir = Vector3.ProjectOnPlane(-toPointDir, Vector3.up);
        if (outwardDir.sqrMagnitude > 0.001f)
        {
            outwardDir.Normalize();
        }
        else
        {
            outwardDir = -toPointDir;
        }

        playerRigidbody.AddForce(
            tangentDir * continuousBoostForce * powerMultiplier + outwardDir * outwardPushForce,
            ForceMode.Force
        );
    }

    float GetAngleBetweenWebs()
    {
        // 找到兩個正在擺盪的點
        List<Vector3> activePoints = new List<Vector3>();
        foreach (var script in activeSwingerScripts)
        {
            if (script.joint != null)
            {
                // 計算從玩家指向掛鉤點的向量
                Vector3 dirToPoint = (script.swingPoint - playerRigidbody.position).normalized;
                activePoints.Add(dirToPoint);
            }
        }

        if (activePoints.Count >= 2)
        {
            // 使用 Vector3.Angle 計算兩個向量之間的夾角 (0~180度)
            return Vector3.Angle(activePoints[0], activePoints[1]);
        }

        return 0f;
    }

    void OnDisable()
    {
        ForceStopSwing(false);
    }
}