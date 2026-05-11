using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class WebSwinger : MonoBehaviour
{
    [Header("綁定物件")]
    public Rigidbody playerRigidbody; // 你的玩家根物件 (帶有 Rigidbody 的那層)
    public Transform handTransform;   // 發射點 (你的左手或右手控制器 Transform)
    public LineRenderer lineRenderer; // 用來畫蛛絲

    [Header("擺盪參數設定")]
    public LayerMask swingableLayer;  // 剛剛設定的 Swingable Layer
    public float maxSwingDistance = 200f; // 蛛絲最遠射程
    
    // 影響手感的關鍵參數
    public float springForce = 10f;     // 彈簧拉力
    public float damper = 7f;            // 阻尼(避免無限彈跳)
    public float massScale = 4.5f;       // 質量影響
    public float releaseBoostForce = 5f; // 放開時的推進力

    [Header("自動收線設定 (Auto-Reeling)")]
    // 自動收線的速度
    public float autoReelSpeed = 8f;     
    // 蛛絲最短能收到多短 (避免收到 0 卡進牆壁裡)
    public float minWebLength = 2f;      
    // 是否啟用自動收線
    public bool enableAutoReel = true;

    [Header("輸入設定 (Meta 控制器)")]
    public OVRInput.Button swingButton = OVRInput.Button.PrimaryIndexTrigger; // 食指扳機
    public OVRInput.Controller controller = OVRInput.Controller.RTouch;       // 右手

    private SpringJoint joint;
    private Vector3 swingPoint;
    public float continuousBoostForce = 5f; // 持續推力的強度

    void Start()
    {
        // 初始化時隱藏蛛絲
        lineRenderer.positionCount = 0; 
    }

    void Update()
    {
        // 按下按鈕的瞬間：發射蛛絲
        if (OVRInput.GetDown(swingButton, controller))
        {
            Debug.Log("Swing button pressed. Attempting to start swing.");
            StartSwing();
        }
        // 鬆開按鈕的瞬間：切斷蛛絲
        else if (OVRInput.GetUp(swingButton, controller))
        {
            StopSwing();
        }
        if (joint != null && enableAutoReel)
        {
            HandleAutoReeling();
        }
    }

    void LateUpdate()
    {
        // 如果正在擺盪，每幀更新蛛絲的視覺起終點
        if (joint != null)
        {
            lineRenderer.SetPosition(0, handTransform.position);
            lineRenderer.SetPosition(1, swingPoint);
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
        RaycastHit hit;
        // 從手部朝前方發射射線
        if (Physics.Raycast(handTransform.position, handTransform.forward, out hit, maxSwingDistance, swingableLayer))
        {
            swingPoint = hit.point;

            // 在玩家本體動態加上 SpringJoint
            joint = playerRigidbody.gameObject.AddComponent<SpringJoint>();
            joint.autoConfigureConnectedAnchor = false;
            joint.connectedAnchor = swingPoint;

            // 計算當前距離
            float distanceFromPoint = Vector3.Distance(playerRigidbody.position, swingPoint);
            
            // 【手感秘訣】將 maxDistance 設為稍微短於實際距離，這會立刻產生一股把玩家往上/往前拉的張力
            joint.maxDistance = distanceFromPoint * 0.9f; 
            joint.minDistance = distanceFromPoint * 0.1f;

            joint.spring = springForce;
            joint.damper = damper;
            joint.massScale = massScale;

            Vector3 upBoost = Vector3.up * 5f; 
            playerRigidbody.AddForce(upBoost, ForceMode.Impulse);

            // 顯示蛛絲
            lineRenderer.positionCount = 2;
        }
    }

    void StopSwing()
    {
        if (joint != null)
        {
            Destroy(joint);
            lineRenderer.positionCount = 0;

            // 【手感秘訣】在放開的瞬間，順著當前的移動方向給予一個推力，產生「飛出去」的速度感
            Vector3 boostDirection = playerRigidbody.velocity.normalized;
            playerRigidbody.AddForce(boostDirection * releaseBoostForce, ForceMode.Impulse);
        }
    }

    // 【新增】自動收線的方法
    void HandleAutoReeling()
    {
        // 只要 joint 存在，就每幀自動減少 maxDistance
        joint.maxDistance -= autoReelSpeed * Time.deltaTime;

        // 確保繩子不會比 minWebLength 還短
        if (joint.maxDistance < minWebLength)
        {
            joint.maxDistance = minWebLength;
        }
    }

    void ApplyContinuousForwardForce()
    {
        // 1. 取得指向掛鉤點的方向
        Vector3 toPoint = (swingPoint - playerRigidbody.position).normalized;
        
        // 2. 取得玩家目前的速度方向
        Vector3 velocityDir = playerRigidbody.velocity.normalized;

        // 3. 計算「切線方向」：這會讓玩家傾向於繞著點轉，而不是直衝向點
        Vector3 tangentDir = Vector3.ProjectOnPlane(velocityDir, toPoint).normalized;

        // 4. 給予切線推力 + 微微向外的力
        float tangentBoost = continuousBoostForce;
        float outwardPush = 2f; // 輕微把玩家往外推，防止貼牆

        playerRigidbody.AddForce(tangentDir * tangentBoost + (-toPoint * outwardPush), ForceMode.Force);
    }
}