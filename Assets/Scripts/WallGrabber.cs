using UnityEngine;

public class WallGrabber : MonoBehaviour
{
    [Header("綁定物件")]
    public Rigidbody playerRigidbody; // 玩家的 Rigidbody
    
    [Header("抓取設定")]
    public LayerMask wallLayer;       // 牆壁的 Layer (也可以共用 Swingable)
    public OVRInput.Controller controller; // LTouch 或 RTouch
    public OVRInput.Button grabButton = OVRInput.Button.PrimaryHandTrigger; // 中指扳機

    [Header("攀爬與甩動設定")]
    public float throwMultiplier = 1.2f; // 甩出去的力道倍率 (越大飛越遠)

    private bool isTouchingWall = false;
    private bool isGrabbing = false;

    private Vector3 previousHandLocalPos;

    // 當手部碰撞體「碰到」牆壁時
    void OnTriggerEnter(Collider other)
    {
        // 檢查碰到的東西是不是在牆壁 Layer 裡
        if ((wallLayer.value & (1 << other.gameObject.layer)) > 0)
        {
            isTouchingWall = true;
        }
    }

    // 當手部碰撞體「離開」牆壁時
    void OnTriggerExit(Collider other)
    {
        if ((wallLayer.value & (1 << other.gameObject.layer)) > 0)
        {
            isTouchingWall = false;
            if (isGrabbing) 
            {
                ReleaseWall();
            }
        }
    }

    void Update()
    {
        // 當手貼著牆壁，且「按下」中指按鈕時：抓住牆壁
        if (isTouchingWall && OVRInput.GetDown(grabButton, controller))
        {
            GrabWall();
        }

        // 當「鬆開」中指按鈕時：放開牆壁
        if (isGrabbing && OVRInput.GetUp(grabButton, controller))
        {
            ReleaseWall();
        }
    }

    void FixedUpdate()
    {
        // 物理移動必須寫在 FixedUpdate 裡才會平滑
        if (isGrabbing)
        {
            MovePlayerOnWall();
        }
    }

    void GrabWall()
    {
        isGrabbing = true;
        
        // 1. 關閉重力，這樣就不會往下掉
        playerRigidbody.useGravity = false;
        
        // 2. 將當下的速度歸零，把動能消除，實現「瞬間黏在牆上」的感覺
        playerRigidbody.velocity = Vector3.zero;
        playerRigidbody.angularVelocity = Vector3.zero;

        previousHandLocalPos = OVRInput.GetLocalControllerPosition(controller);
    }

    void MovePlayerOnWall()
    {
        // 1. 取得現在的手部相對位置
        Vector3 currentHandLocalPos = OVRInput.GetLocalControllerPosition(controller);
        
        // 2. 計算手移動了多少距離 (Delta)
        Vector3 delta = currentHandLocalPos - previousHandLocalPos;
        
        // 3. 將相對位移轉換為世界座標的方向 (根據玩家面朝方向)
        Vector3 worldDelta = playerRigidbody.transform.TransformDirection(delta);

        // 4. 玩家往手的反方向移動 (手往下扯，人往上爬)
        // 使用 MovePosition 讓物理引擎接管，避免穿模
        playerRigidbody.MovePosition(playerRigidbody.position - worldDelta);

        // 5. 更新手部位置，準備給下一幀計算
        previousHandLocalPos = currentHandLocalPos;
    }

    void ReleaseWall()
    {
        isGrabbing = false;
        
        // 恢復重力，玩家開始往下掉
        playerRigidbody.useGravity = true;

        // 【新增：攀岩甩動效應 (Throwing)】
        // 取得手部釋放瞬間的「揮動速度」
        Vector3 handVelocity = OVRInput.GetLocalControllerVelocity(controller);
        
        // 將手的速度轉換成世界座標
        Vector3 worldHandVelocity = playerRigidbody.transform.TransformDirection(handVelocity);

        // 根據手的揮動速度，給予玩家一個反向的推力
        // 你在現實中用力往下揮並放開中指，遊戲裡你就會往上飛！
        playerRigidbody.velocity = -worldHandVelocity * throwMultiplier;
    }
}