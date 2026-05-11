using System.Collections.Generic;
using UnityEngine;

public class WallGrabber : MonoBehaviour
{
    [Header("綁定物件")]
    public Rigidbody playerRigidbody;

    [Tooltip("實際手部/控制器的 Transform，用來做 CheckSphere")]
    public Transform handTransform;

    [Tooltip("OVR CameraRig 裡的 TrackingSpace。沒有指定就使用 playerRigidbody.transform")]
    public Transform trackingSpaceTransform;

    [Header("抓取設定")]
    public LayerMask wallLayer;
    public float handDetectRadius = 0.12f;

    public OVRInput.Controller controller = OVRInput.Controller.RTouch;
    public OVRInput.Button grabButton = OVRInput.Button.PrimaryHandTrigger;

    [Header("攀爬與甩動設定")]
    public float throwMultiplier = 1.2f;
    public float maxThrowSpeed = 8f;

    private bool isTouchingWall = false;
    private bool isGrabbing = false;

    private Vector3 previousHandLocalPos;

    private static WallGrabber activeGrabber;
    private static readonly List<WallGrabber> grabbingHands = new List<WallGrabber>();

    void Update()
    {
        isTouchingWall = Physics.CheckSphere(
            handTransform.position,
            handDetectRadius,
            wallLayer,
            QueryTriggerInteraction.Ignore
        );

        if (!isGrabbing && isTouchingWall && OVRInput.GetDown(grabButton, controller))
        {
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

        Debug.Log("Grab wall: " + gameObject.name);

        isGrabbing = true;

        if (!grabbingHands.Contains(this))
        {
            grabbingHands.Add(this);
        }

        // 最新抓住的手成為主控手
        SetAsActiveGrabber();

        playerRigidbody.useGravity = false;
        playerRigidbody.velocity = Vector3.zero;
        playerRigidbody.angularVelocity = Vector3.zero;
    }

    void SetAsActiveGrabber()
    {
        activeGrabber = this;

        // 切換主控手時一定要重設 previous position
        // 不然下一次 MovePlayerOnWall 會吃到舊 delta，造成瞬移
        previousHandLocalPos = OVRInput.GetLocalControllerPosition(controller);
    }

    void MovePlayerOnWall()
    {
        Vector3 currentHandLocalPos = OVRInput.GetLocalControllerPosition(controller);

        Vector3 localDelta = currentHandLocalPos - previousHandLocalPos;

        Vector3 worldDelta = LocalDirectionToWorld(localDelta);

        // 手往下，玩家往上
        // 手往右，玩家往左
        playerRigidbody.MovePosition(playerRigidbody.position - worldDelta);

        previousHandLocalPos = currentHandLocalPos;
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
            // 還有另一隻手抓著牆
            // 如果放開的是主控手，就把控制權交給最後一隻還抓著的手
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

        // 沒有任何手抓牆了
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

    Vector3 LocalDirectionToWorld(Vector3 localDirection)
    {
        if (trackingSpaceTransform != null)
        {
            return trackingSpaceTransform.TransformDirection(localDirection);
        }

        return playerRigidbody.transform.TransformDirection(localDirection);
    }

    void OnDisable()
    {
        ReleaseWall(false);
    }

    void OnDrawGizmosSelected()
    {
        if (handTransform == null)
        {
            return;
        }

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(handTransform.position, handDetectRadius);
    }
}