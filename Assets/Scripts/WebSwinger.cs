using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

public class WebSwinger : MonoBehaviour
{
    [Header("綁定物件")]
    public Rigidbody playerRigidbody;
    public Transform handTransform;
    public LineRenderer lineRenderer;
    private GameObject currentHitObject;

    [Header("擺盪參數設定")]
    public LayerMask swingableLayer;
    public LayerMask GroundLayer;
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

    [Header("URP 後處理")]
    public Volume globalVolume; 
    private ColorAdjustments colorAdjustments;

    [Header("預瞄指示設定")]
    public GameObject reticlePrefab;
    private GameObject spawnedReticle;
    public float minScale = 0.05f;
    public float scaleFactor = 0.01f;

    [Header("射擊設定")]
    public GameObject bulletPrefab;
    public Transform firePoint;
    public float bulletSpeed = 50f;
    public float shootCooldown = 0.2f;
    private float lastShootTime;
    private bool canShoot = false;
    private bool ThisHandGrabbing = false;

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
        if (reticlePrefab != null)
        {
            spawnedReticle = Instantiate(reticlePrefab);
            spawnedReticle.SetActive(false); // 初始隱藏
        }
        if (globalVolume != null && globalVolume.profile.TryGet(out colorAdjustments))
        {
            Debug.Log("ColorAdjustments found in Volume profile.");
        }
    }

    void Update()
    {
        if (OVRInput.GetDown(swingButton, controller))
        {
            if (!canShoot)
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
            OnEnableR();
            swingStartTime = Time.time;
        }

        if (controller == OVRInput.Controller.LTouch && OVRInput.GetDown(OVRInput.Button.Three) || 
            controller == OVRInput.Controller.RTouch && OVRInput.GetDown(OVRInput.Button.One))
        {
            canShoot = true;
        }

        if (controller == OVRInput.Controller.LTouch && OVRInput.GetUp(OVRInput.Button.Three) || 
            controller == OVRInput.Controller.RTouch && OVRInput.GetUp(OVRInput.Button.One))
        {
            canShoot = false;
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
            StopVibration();
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

        // slow motion for better aiming when按著 B 鍵
        if (OVRInput.GetDown(OVRInput.Button.Two) && controller == OVRInput.Controller.RTouch) 
        {
            StartSlowMotion();
        }
        if (OVRInput.GetUp(OVRInput.Button.Two) && controller == OVRInput.Controller.RTouch)
        {
            StopSlowMotion();
        }

        if (CheckShootInput())
        {
            if (Time.time - lastShootTime >= shootCooldown)
            {
                Shoot();
                lastShootTime = Time.time;
            }
        }

        if (OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, controller))
        {
            ThisHandGrabbing = true;
        }
        if (OVRInput.GetUp(OVRInput.Button.PrimaryHandTrigger, controller))
        {
            ThisHandGrabbing = false;
        }

        // 預瞄指示
        UpdateReticle();
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
            currentHitObject = hit.collider.gameObject;
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
        if (colorAdjustments != null)
        {
            colorAdjustments.saturation.value = -100f;
        }
    }

    void StopSlowMotion()
    {
        Time.timeScale = 1f;
        Time.fixedDeltaTime = normalFixedDeltaTime;
        if (colorAdjustments != null)
        {
            colorAdjustments.saturation.value = 0f;
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

        OVRInput.SetControllerVibration(0.1f, 0.07f, controller);

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
            float powerMultiplier = 5f; // 預設單手推力
            bool isSlingshot = false;

            if (activeSwingCount >= 2)
            {
                float angle = GetAngleBetweenWebs();
                int boostLayer = LayerMask.NameToLayer("Ground");

                bool bothOnBoostLayer = true;
                Vector3 combinedWebDir = Vector3.zero;

                foreach (var script in activeSwingerScripts)
                {
                    if (script.joint != null)
                    {
                        if (script.currentHitObject == null || script.currentHitObject.layer != boostLayer)
                        {
                            bothOnBoostLayer = false;
                        }
                        combinedWebDir += (script.swingPoint - playerRigidbody.position).normalized;
                    }
                }

                if (angle < 50f && ArmSwingLocomotion.Instance.ReturnGrounded() && bothOnBoostLayer)
                {
                    isSlingshot = true;
                    powerMultiplier = 20f;
                    
                    Vector3 shootDirection = combinedWebDir.normalized;
                    shootDirection.y = Mathf.Max(shootDirection.y, 0.1f);

                    playerRigidbody.AddForce(shootDirection * powerMultiplier, ForceMode.Impulse);

                    foreach (var script in activeSwingerScripts)
                    {
                        script.ForceStopSwing(false);
                    }
                }
            }

            if (!isSlingshot)
            {
                playerRigidbody.AddForce(Vector3.up * powerMultiplier, ForceMode.Impulse);
            }
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
                ArmSwingLocomotion instance = ArmSwingLocomotion.Instance;
                bool bothOnBoostLayer = true;
                int boostLayer = LayerMask.NameToLayer("Ground");
                foreach (var script in activeSwingerScripts)
                {
                    if (script.joint != null)
                    {
                        if (script.currentHitObject == null || script.currentHitObject.layer != boostLayer)
                        {
                            bothOnBoostLayer = false;
                            break;
                        }
                    }
                }
                if (angle < 50f && instance.ReturnGrounded() && bothOnBoostLayer)
                {
                    powerMultiplier = 20f;
                }
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

    void UpdateReticle()
    {
        if (spawnedReticle == null) return;

        if (joint != null || hasPendingSwing || ThisHandGrabbing)
        {
            spawnedReticle.SetActive(false);
            return;
        }

        RaycastHit hit;
        if (Physics.Raycast(handTransform.position, handTransform.forward, out hit, maxSwingDistance, swingableLayer))
        {

            float distance = Vector3.Distance(handTransform.position, hit.point);
            float minShowDistance = 3f;
            if (distance < minShowDistance)
            {
                spawnedReticle.SetActive(false);
                return;
            }
            spawnedReticle.SetActive(true);
        
            // spawnedReticle.transform.position = hit.point + (hit.normal * 0.05f);
            Vector3 targetPos = hit.point + (hit.normal * 0.05f);
            spawnedReticle.transform.position = Vector3.Lerp(spawnedReticle.transform.position, targetPos, 0.5f);
            spawnedReticle.transform.rotation = Quaternion.LookRotation(-hit.normal);
            
            float currentScale = minScale + (distance * scaleFactor);
            
            spawnedReticle.transform.localScale = new Vector3(currentScale, currentScale, currentScale);
        }
        else
        {
            spawnedReticle.SetActive(false);
        }
    }

    private bool CheckShootInput()
    {
        if (controller == OVRInput.Controller.LTouch)
        {
            return OVRInput.Get(OVRInput.Button.One, controller) && 
                OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, controller);
        }
        else if (controller == OVRInput.Controller.RTouch)
        {
            return OVRInput.Get(OVRInput.Button.One, controller) && 
                OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, controller);
        }

        return false;
    }

    void Shoot()
    {
        if (bulletPrefab == null || firePoint == null) return;

        GameObject bullet = Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);
        
        Rigidbody rb = bullet.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = firePoint.forward * bulletSpeed;
        }

        Destroy(bullet, 3f);

        OVRInput.SetControllerVibration(0.7f, 0.5f, controller);
        Invoke("StopVibration", 0.1f);
    }

    void StopVibration()
    {
        OVRInput.SetControllerVibration(0, 0, controller);
    }
}