using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class WebSwingerFISH : MonoBehaviour
{
    [Header("綁定物件")]
    public Rigidbody playerRigidbody; // 你的玩家根物件 (帶有 Rigidbody 的那層)
    public Transform handTransform;   // 發射點 (你的左手或右手控制器 Transform)
    public LineRenderer lineRenderer; // 用來畫蛛絲

    [Header("擺盪參數設定")]
    public LayerMask swingableLayer;  // 剛剛設定的 Swingable Layer
    public float maxSwingDistance = 50f; // 蛛絲最遠射程
    
    // 影響手感的關鍵參數
    public float springForce = 10f;     // 彈簧拉力
    public float damper = 7f;            // 阻尼(避免無限彈跳)
    public float massScale = 4.5f;       // 質量影響
    public float releaseBoostForce = 5f; // 放開時的推進力

    [Header("輸入設定 (Meta 控制器)")]
    public OVRInput.Button swingButton = OVRInput.Button.PrimaryIndexTrigger; // 食指扳機
    public OVRInput.Controller controller = OVRInput.Controller.RTouch;       // 右手

    [Header("貝茲曲線設定")]
    public int segmentCount = 20;
    public float curveOffset = -0.5f;

    private SpringJoint joint;
    private Vector3 swingPoint;

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
    }

    void LateUpdate()
    {
        if (joint != null)
        {
            DrawSwingingCurve(handTransform.position, swingPoint);
        }
        else
        {
            lineRenderer.positionCount = 0; // 沒在擺盪時隱藏
        }
    }

    void DrawSwingingCurve(Vector3 start, Vector3 end)
    {
        lineRenderer.positionCount = segmentCount;
        
        // 計算控制點：取中點並向下偏移
        // 你也可以根據繩索長度動態調整偏移量，讓長繩子垂得更厲害
        Vector3 midPoint = (start + end) / 2f;
        Vector3 controlPoint = midPoint + Vector3.up * curveOffset;

        for (int i = 0; i < segmentCount; i++)
        {
            float t = i / (float)(segmentCount - 1);
            // 二次貝茲曲線公式
            Vector3 point = Mathf.Pow(1 - t, 2) * start + 
                            2 * (1 - t) * t * controlPoint + 
                            Mathf.Pow(t, 2) * end;
            lineRenderer.SetPosition(i, point);
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
            joint.maxDistance = distanceFromPoint * 0.6f; 
            joint.minDistance = distanceFromPoint * 0.2f;

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
}