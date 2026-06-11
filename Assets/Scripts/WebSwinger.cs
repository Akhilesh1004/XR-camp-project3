using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

public class WebSwinger : MonoBehaviour
{
    [Header("綁定物件")]
    [SerializeField] private Animator animator;
    public Rigidbody playerRigidbody;
    public Transform handTransform;
    public LineRenderer[] lineRenderers; 
    public GameObject webEffectPrefab;  
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

    [Header("射擊/子彈類型設定")]
    public bool useParticleBullet = true; // 🌟 新增開關：true = 純粒子特效 / false = 一般實體物理子彈
    public GameObject bulletPrefab;      // 放入對應的子彈或粒子 Prefab
    public Transform firePoint;
    public float bulletSpeed = 50f;      // 🌟 加回：一般子彈飛行的速度
    public float shootCooldown = 0.2f;
    private float lastShootTime;
    private bool canShoot = false;
    private bool ThisHandGrabbing = false;

    [Header("蛛絲音效")]
    public AudioSource webAudioSource;
    public AudioClip webShootSound;
    public float webShootVolume = 1f;
    public AudioClip webLoopSound;
    public float webLoopVolume = 0.45f;
    public float webLoopPitch = 1f;

    private SpringJoint joint;
    private Vector3 swingPoint;

    private bool hasPendingSwing = false;
    private Vector3 pendingSwingPoint;

    public static int activeSwingCount = 0;
    public static int pendingSwingCount = 0;

    private bool isWristUIOpen = false;
    private bool isPausedMidway = false;
    private bool isWaitingToPause = false;
    private bool isGrabWaitingToPause = false;
    private bool isGrabPausedMidway = false;
    
    private static List<WebSwinger> activeSwingerScripts = new List<WebSwinger>();
    void OnEnableR() { if (!activeSwingerScripts.Contains(this)) activeSwingerScripts.Add(this); }
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
        activeSwingerScripts.Clear();
    }

    void Start()
    {
        if (lineRenderers != null)
        {
            foreach (var lr in lineRenderers)
            {
                if (lr != null) lr.positionCount = 0;
            }
        }

        if (webEffectPrefab != null)
        {
            webEffectPrefab.SetActive(false);
        }

        normalFixedDeltaTime = Time.fixedDeltaTime;
        if (reticlePrefab != null)
        {
            spawnedReticle = Instantiate(reticlePrefab);
            spawnedReticle.SetActive(false); 
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
                animator.speed = 1f;
                animator.Play("shoot_state", 0, 0f);
                isPausedMidway = false;       
                isWaitingToPause = true;
                if (WallGrabber.IsGrabbing)
                {
                    StartPendingSwing();
                }
                else
                {
                    StartSwing();
                }
                PlayWebShootSound();
            }
            OnEnableR();
            swingStartTime = Time.time;
        }

        if (OVRInput.Get(swingButton, controller)) 
        {
            // 如果正在轉場（Transition），先不要檢查進度，讓新動畫順利播進去
            if (!animator.IsInTransition(0))
            {
                AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);

                // 確保不是 Idle，且我們正在等待暫停，且進度過半了
                if (!stateInfo.IsName("Idle") && isWaitingToPause && stateInfo.normalizedTime >= 0.6f)
                {
                    // 改用 speed = 0 暫停，這樣按下的前半段動畫才會是完全流暢的！
                    animator.speed = 0f; 
                    isWaitingToPause = false; // 停下來了，不用再重複執行這段
                    isPausedMidway = true;
                }
            }
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
            animator.speed = 1f;
            isWaitingToPause = false;
            isPausedMidway = false;
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

        if (hasPendingSwing && !WallGrabber.IsGrabbing)
        {
            ActivatePendingSwing();
        }

        if (joint != null && enableAutoReel)
        {
            HandleAutoReeling();
        }

        if (OVRInput.GetDown(OVRInput.Button.Two) && controller == OVRInput.Controller.RTouch) 
        {
            StartSlowMotion();
        }
        if (OVRInput.GetUp(OVRInput.Button.Two) && controller == OVRInput.Controller.RTouch)
        {
            StopSlowMotion();
        }

        if (OVRInput.GetDown(OVRInput.Button.Four)) 
        {
            isWristUIOpen = !isWristUIOpen;
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
            
            animator.speed = 1f;
            animator.Play("grab_state", 0, 0f); 
            isGrabPausedMidway = false;
            isGrabWaitingToPause = true;
        }
        if (OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, controller))
        {
            if (!animator.IsInTransition(0))
            {
                AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);

                // 專門監控抓取動畫，到了 50% (0.5f) 就卡住
                if (stateInfo.IsName("grab_state") && isGrabWaitingToPause && stateInfo.normalizedTime >= 0.5f)
                {
                    animator.speed = 0f;
                    isGrabWaitingToPause = false;
                    isGrabPausedMidway = true;
                }
            }
        }
        if (OVRInput.GetUp(OVRInput.Button.PrimaryHandTrigger, controller))
        {
            ThisHandGrabbing = false;

            animator.speed = 1f;
            isGrabWaitingToPause = false;
            isGrabPausedMidway = false;
        }

        UpdateReticle();
    }

    void LateUpdate()
    {
        bool isSwingingNow = joint != null;
        bool isPendingNow = hasPendingSwing;

        if (lineRenderers != null && lineRenderers.Length > 0)
        {
            foreach (var lr in lineRenderers)
            {
                if (lr == null) continue;

                if (isSwingingNow)
                {
                    lr.SetPosition(0, handTransform.position);
                    lr.SetPosition(1, swingPoint);
                }
                else if (isPendingNow)
                {
                    lr.SetPosition(0, handTransform.position);
                    lr.SetPosition(1, pendingSwingPoint);
                }
            }
        }

        if (webEffectPrefab != null && webEffectPrefab.activeSelf)
        {
            if (isSwingingNow)
            {
                UpdateEffectTransform(swingPoint);
            }
            else if (isPendingNow)
            {
                UpdateEffectTransform(pendingSwingPoint);
            }
        }
    }

    void UpdateEffectTransform(Vector3 targetPoint)
    {
        webEffectPrefab.transform.position = handTransform.position;
        Vector3 direction = targetPoint - handTransform.position;

        if (direction.sqrMagnitude > 0.001f)
        {
            webEffectPrefab.transform.rotation = Quaternion.LookRotation(direction.normalized);
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

            if (lineRenderers != null)
            {
                foreach (var lr in lineRenderers)
                {
                    if (lr != null) lr.positionCount = 2;
                }
            }

            if (webEffectPrefab != null)
            {
                webEffectPrefab.SetActive(true);
            }

            StartWebLoopSound();

            Debug.Log("Pending swing created: " + gameObject.name);
        }
    }

    void StartSlowMotion()
    {
        Time.timeScale = slowTimeScale;
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

        if (joint == null && lineRenderers != null)
        {
            foreach (var lr in lineRenderers)
            {
                if (lr != null) lr.positionCount = 0;
            }
            
            if (webEffectPrefab != null)
            {
                webEffectPrefab.SetActive(false);
            }

            StopWebLoopSound();
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
            float powerMultiplier = 5f; 
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

        if (lineRenderers != null)
        {
            foreach (var lr in lineRenderers)
            {
                if (lr != null) lr.positionCount = 2;
            }
        }

        if (webEffectPrefab != null)
        {
            webEffectPrefab.SetActive(true);
        }

        StartWebLoopSound();

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

        if (lineRenderers != null)
        {
            foreach (var lr in lineRenderers)
            {
                if (lr != null) lr.positionCount = 0;
            }
        }

        if (webEffectPrefab != null)
        {
            webEffectPrefab.SetActive(false);
        }

        StopWebLoopSound();

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

            if (lineRenderers != null)
            {
                foreach (var lr in lineRenderers)
                {
                    if (lr != null) lr.positionCount = 2;
                }
            }

            if (webEffectPrefab != null)
            {
                webEffectPrefab.SetActive(true);
            }

            StartWebLoopSound();

            Debug.Log("Active swing suspended and converted to pending: " + gameObject.name);
        }
        else
        {
            if (!hasPendingSwing && lineRenderers != null)
            {
                foreach (var lr in lineRenderers)
                {
                    if (lr != null) lr.positionCount = 0;
                }
            }

            if (webEffectPrefab != null)
            {
                webEffectPrefab.SetActive(false);
            }

            StopWebLoopSound();

            Debug.Log("Active swing stopped by wall grab: " + gameObject.name);
        }
    }

    void PlayWebShootSound()
    {
        if (webShootSound == null)
        {
            return;
        }

        AudioSource source = GetWebAudioSource();

        if (source != null)
        {
            source.PlayOneShot(webShootSound, webShootVolume);
        }
    }

    void StartWebLoopSound()
    {
        if (webLoopSound == null)
        {
            return;
        }

        AudioSource source = GetWebAudioSource();

        if (source == null)
        {
            return;
        }

        if (source.clip != webLoopSound)
        {
            source.clip = webLoopSound;
        }

        source.loop = true;
        source.volume = webLoopVolume;
        source.pitch = webLoopPitch;

        if (!source.isPlaying)
        {
            source.Play();
        }
    }

    void StopWebLoopSound()
    {
        if (webAudioSource == null || webAudioSource.clip != webLoopSound)
        {
            return;
        }

        webAudioSource.Stop();
        webAudioSource.loop = false;
        webAudioSource.clip = null;
    }

    AudioSource GetWebAudioSource()
    {
        if (webAudioSource == null)
        {
            webAudioSource = gameObject.AddComponent<AudioSource>();
        }

        webAudioSource.playOnAwake = false;
        webAudioSource.spatialBlend = 1f;
        return webAudioSource;
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
        List<Vector3> activePoints = new List<Vector3>();
        foreach (var script in activeSwingerScripts)
        {
            if (script.joint != null)
            {
                Vector3 dirToPoint = (script.swingPoint - playerRigidbody.position).normalized;
                activePoints.Add(dirToPoint);
            }
        }

        if (activePoints.Count >= 2)
        {
            return Vector3.Angle(activePoints[0], activePoints[1]);
        }

        return 0f;
    }

    void OnDisable()
    {
        StopWebLoopSound();
        StopVibration();
        StopSlowMotion();
        ForceStopSwing(false);
        OnDisableR();
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

    // 🌟 已更新：根據 bool 選項自動切換「粒子模式」或「物理子彈模式」
    void Shoot()
    {
        if (bulletPrefab == null || firePoint == null) return;

        // 生成物件複製品
        GameObject spawnedBullet = Instantiate(bulletPrefab, firePoint.position, firePoint.rotation);

        if (useParticleBullet)
        {
            // A. 純粒子模式：物件不給速度，原地停留 3 秒後自動消亡（走 OnParticleTrigger 判定）
            Destroy(spawnedBullet, 3f);
        }
        else
        {
            // B. 加回原本的邏輯：給予物理速度飛出去（走原本的 OnTriggerEnter 判定）
            Rigidbody rb = spawnedBullet.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = firePoint.forward * bulletSpeed;
            }
            
            // 原本被註解掉的物理子彈自動銷毀（可自由調整時間）
            // Destroy(spawnedBullet, 3f); 
        }

        // 震動回饋
        OVRInput.SetControllerVibration(0.7f, 0.5f, controller);
        Invoke("StopVibration", 0.1f);
    }

    void StopVibration()
    {
        OVRInput.SetControllerVibration(0, 0, controller);
    }
}
