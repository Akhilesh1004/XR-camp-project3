using UnityEngine;

public class WallGrabber : MonoBehaviour
{
    [Header("綁定物件")]
    public Rigidbody playerRigidbody; // 玩家的 Rigidbody
    public Transform handTransform;
    
    [Header("抓取設定")]
    public LayerMask wallLayer;       // 牆壁的 Layer (也可以共用 Swingable)
    public OVRInput.Controller controller; // LTouch 或 RTouch
    public OVRInput.Button grabButton = OVRInput.Button.PrimaryHandTrigger; // 中指扳機

    [Header("攀爬與甩動設定")]
    public float throwMultiplier = 1.2f; // 甩出去的力道倍率 (越大飛越遠)

    private bool isTouchingWall = false;
    private bool isGrabbing = false;
    private Vector3 previousHandWorldPos;
    private Vector3 handWorldVelocity;

    // 當手部碰撞體「碰到」牆壁時
    void OnTriggerEnter(Collider other)
    {
        if (IsWall(other))
        {
            isTouchingWall = true;
            Debug.Log("Hand touched wall: " + other.name);
        }
    }

    // 當手部碰撞體「離開」牆壁時
    void OnTriggerExit(Collider other)
    {
        if (IsWall(other))
        {
            isTouchingWall = false;

            // 先不要自動 Release，否則很容易一抓就立刻放開
            Debug.Log("Hand left wall: " + other.name);
        }
    }

    void Update()
    {
        if (isTouchingWall && OVRInput.GetDown(grabButton, controller))
        {
            GrabWall();
        }

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

    bool IsWall(Collider other)
    {
        return (wallLayer.value & (1 << other.gameObject.layer)) != 0;
    }


    void GrabWall()
    {
        Debug.Log("Grab wall");

        isGrabbing = true;

        playerRigidbody.useGravity = false;
        playerRigidbody.velocity = Vector3.zero;
        playerRigidbody.angularVelocity = Vector3.zero;

        previousHandWorldPos = handTransform.position;
        handWorldVelocity = Vector3.zero;
    }

    void MovePlayerOnWall()
    {
       Vector3 currentHandWorldPos = handTransform.position;

        Vector3 delta = currentHandWorldPos - previousHandWorldPos;

        handWorldVelocity = delta / Time.fixedDeltaTime;

        // 手往下拉，身體往上移
        playerRigidbody.MovePosition(playerRigidbody.position - delta);

        previousHandWorldPos = currentHandWorldPos;
    }

    void ReleaseWall()
    {
        Debug.Log("Release wall");

        isGrabbing = false;

        playerRigidbody.useGravity = true;

        // 手往下甩，玩家往反方向飛
        playerRigidbody.velocity = -handWorldVelocity * throwMultiplier;
    }
}