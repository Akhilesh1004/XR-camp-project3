using UnityEngine;

public class WallGrabber : MonoBehaviour
{
    [Header("綁定物件")]
    public Rigidbody playerRigidbody; // 玩家的 Rigidbody
    
    [Header("抓取設定")]
    public LayerMask wallLayer;       // 牆壁的 Layer (也可以共用 Swingable)
    public OVRInput.Controller controller; // LTouch 或 RTouch
    public OVRInput.Button grabButton = OVRInput.Button.PrimaryHandTrigger; // 中指扳機

    private bool isTouchingWall = false;
    private bool isGrabbing = false;

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

    void GrabWall()
    {
        isGrabbing = true;
        
        // 1. 關閉重力，這樣就不會往下掉
        playerRigidbody.useGravity = false;
        
        // 2. 將當下的速度歸零，把動能消除，實現「瞬間黏在牆上」的感覺
        playerRigidbody.velocity = Vector3.zero;
        playerRigidbody.angularVelocity = Vector3.zero;
    }

    void ReleaseWall()
    {
        isGrabbing = false;
        
        // 恢復重力，玩家開始往下掉
        playerRigidbody.useGravity = true;
    }
}