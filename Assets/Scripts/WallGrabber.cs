using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class WallGrabber : MonoBehaviour
{
    [Header("綁定物件")]
    public Rigidbody playerRigidbody;

    [Tooltip("實際手部 / 控制器 Transform，用來做 CheckSphere")]
    public Transform handTransform;

    [Tooltip("OVR CameraRig 裡的 TrackingSpace。建議指定；沒有指定就使用 playerRigidbody.transform")]
    public Transform trackingSpaceTransform;

    [Header("互斥系統")]
    [Tooltip("把左右手的 WebSwinger 都拖進來。正在擺盪時抓牆會暫停蛛絲。")]
    public WebSwinger[] webSwingers;

    [Header("抓取設定")]
    public LayerMask wallLayer;
    public float handDetectRadius = 0.12f;

    public OVRInput.Controller controller = OVRInput.Controller.RTouch;
    public OVRInput.Button grabButton = OVRInput.Button.PrimaryHandTrigger;

    [Header("攀爬與甩動設定")]
    public float throwMultiplier = 1.2f;
    public float maxThrowSpeed = 8f;
    public bool enableMantle = true;
    public LayerMask mantleWalkableLayer;
    public float mantlePullDownSpeed = 0.35f;
    public float mantleForwardProbe = 0.45f;
    public float mantleUpProbe = 0.8f;
    public float mantleDownProbe = 1.4f;
    public float mantleForwardMove = 0.75f;
    public float mantleUpMove = 0.55f;
    public float maxMantleSurfaceAngle = 30f;
    public float mantleCooldown = 0.4f;
    public CapsuleCollider playerCapsule;
    public LayerMask collisionLayer;
    public float skinWidth = 0.05f;
    public float maxClimbStepPerFixedUpdate = 0.18f;

    private float lastMantleTime = -999f;

    private bool isTouchingWall = false;
    private bool isGrabbing = false;

    private Vector3 previousHandLocalPos;

    private static WallGrabber activeGrabber;
    private static readonly List<WallGrabber> grabbingHands = new List<WallGrabber>();

    public static bool IsGrabbing
    {
        get { return grabbingHands.Count > 0; }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        activeGrabber = null;
        grabbingHands.Clear();
    }

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
            OVRInput.SetControllerVibration(0.1f, 0.3f, controller);
            Invoke("StopVibration", 0.1f);
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

        Debug.Log("Try grab wall: " + gameObject.name);

        // 如果正在蛛絲擺盪中抓牆：
        // 停掉 active SpringJoint。
        // 如果蛛絲鍵還按著，WebSwinger 會自動轉成 pending swing。
        if (webSwingers != null)
        {
            foreach (WebSwinger swinger in webSwingers)
            {
                if (swinger != null)
                {
                    swinger.SuspendActiveSwingForWallGrab();
                }
            }
        }

        Debug.Log("Grab wall: " + gameObject.name);

        isGrabbing = true;

        if (!grabbingHands.Contains(this))
        {
            grabbingHands.Add(this);
        }

        SetAsActiveGrabber();

        playerRigidbody.useGravity = false;
        playerRigidbody.velocity = Vector3.zero;
        playerRigidbody.angularVelocity = Vector3.zero;
    }

    void SetAsActiveGrabber()
    {
        activeGrabber = this;

        // 切換主控手時，一定要重設 previous position
        // 不然會吃到舊 delta，造成瞬移
        previousHandLocalPos = OVRInput.GetLocalControllerPosition(controller);
    }

    void MovePlayerOnWall()
    {
        Vector3 currentHandLocalPos = OVRInput.GetLocalControllerPosition(controller);

        Vector3 localDelta = currentHandLocalPos - previousHandLocalPos;
        Vector3 worldDelta = LocalDirectionToWorld(localDelta);

        Vector3 desiredMove = -worldDelta;

        if (desiredMove.magnitude > maxClimbStepPerFixedUpdate)
        {
            desiredMove = desiredMove.normalized * maxClimbStepPerFixedUpdate;
        }

        SafeMovePlayer(desiredMove);

        TryMantle(worldDelta);

        previousHandLocalPos = currentHandLocalPos;
    }

    void SafeMovePlayer(Vector3 move)
    {
        if (move.sqrMagnitude < 0.000001f)
        {
            return;
        }

        if (playerCapsule == null)
        {
            playerRigidbody.MovePosition(playerRigidbody.position + move);
            return;
        }

        GetCapsuleWorldPoints(out Vector3 p1, out Vector3 p2, out float radius);

        Vector3 direction = move.normalized;
        float distance = move.magnitude;

        if (Physics.CapsuleCast(
            p1,
            p2,
            radius,
            direction,
            out RaycastHit hit,
            distance + skinWidth,
            collisionLayer,
            QueryTriggerInteraction.Ignore))
        {
            float safeDistance = Mathf.Max(0f, hit.distance - skinWidth);
            playerRigidbody.MovePosition(playerRigidbody.position + direction * safeDistance);
        }
        else
        {
            playerRigidbody.MovePosition(playerRigidbody.position + move);
        }
    }

    void GetCapsuleWorldPoints(out Vector3 p1, out Vector3 p2, out float radius)
    {
        float scaleY = Mathf.Abs(playerCapsule.transform.lossyScale.y);
        float scaleX = Mathf.Abs(playerCapsule.transform.lossyScale.x);
        float scaleZ = Mathf.Abs(playerCapsule.transform.lossyScale.z);

        radius = playerCapsule.radius * Mathf.Max(scaleX, scaleZ);
        float height = Mathf.Max(playerCapsule.height * scaleY, radius * 2f);

        Vector3 center = playerCapsule.transform.TransformPoint(playerCapsule.center);

        float halfHeight = Mathf.Max(0f, height * 0.5f - radius);

        p1 = center + Vector3.up * halfHeight;
        p2 = center - Vector3.up * halfHeight;
    }

    void TryMantle(Vector3 handWorldDelta)
    {
        if (!enableMantle)
        {
            Debug.Log("Mantle fail: enableMantle is false");
            return;
        }

        if (Time.time - lastMantleTime < mantleCooldown)
        {
            Debug.Log("Mantle fail: cooldown");
            return;
        }

        float handDownSpeed = -handWorldDelta.y / Time.fixedDeltaTime;

        if (handDownSpeed < mantlePullDownSpeed)
        {
            Debug.Log("Mantle fail: handDownSpeed too low = " + handDownSpeed);
            return;
        }

        Vector3 forward = GetFlatForward();

        if (forward.sqrMagnitude < 0.001f)
        {
            Debug.Log("Mantle fail: forward invalid");
            return;
        }

        Vector3 probeOrigin =
            handTransform.position +
            forward * mantleForwardProbe +
            Vector3.up * mantleUpProbe;

        Debug.DrawRay(
            probeOrigin,
            Vector3.down * (mantleUpProbe + mantleDownProbe),
            Color.yellow,
            0.2f
        );

        if (!Physics.SphereCast(
            probeOrigin,
            0.18f,
            Vector3.down,
            out RaycastHit hit,
            mantleUpProbe + mantleDownProbe,
            mantleWalkableLayer,
            QueryTriggerInteraction.Ignore))
        {
            Debug.Log("Mantle fail: no walkable surface hit");
            return;
        }

        float surfaceAngle = Vector3.Angle(hit.normal, Vector3.up);

        Debug.Log(
            "Mantle hit: " + hit.collider.name +
            ", angle = " + surfaceAngle +
            ", layer = " + LayerMask.LayerToName(hit.collider.gameObject.layer)
        );

        if (surfaceAngle > maxMantleSurfaceAngle)
        {
            Debug.Log("Mantle fail: surface angle too steep = " + surfaceAngle);
            return;
        }

        lastMantleTime = Time.time;

        Vector3 upMove = Vector3.up * mantleUpMove;
        Vector3 forwardMove = forward * mantleForwardMove;

        SafeMovePlayer(upMove);
        SafeMovePlayer(forwardMove);

        playerRigidbody.velocity = forward * 1.5f;
        playerRigidbody.angularVelocity = Vector3.zero;

        Debug.Log("Mantle success onto roof: " + hit.collider.name);

        ReleaseWall(false);
    }

    Vector3 GetFlatForward()
    {
        Transform forwardSource = trackingSpaceTransform;

        if (forwardSource != null)
        {
            Vector3 forward = forwardSource.forward;
            forward.y = 0f;
            return forward.normalized;
        }

        Vector3 fallback = playerRigidbody.transform.forward;
        fallback.y = 0f;
        return fallback.normalized;
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

        if (enableMantle)
        {
            Vector3 forward = GetFlatForward();

            Vector3 probeOrigin =
                handTransform.position +
                forward * mantleForwardProbe +
                Vector3.up * mantleUpProbe;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(probeOrigin, 0.08f);
            Gizmos.DrawLine(
                probeOrigin,
                probeOrigin + Vector3.down * (mantleUpProbe + mantleDownProbe)
            );
        }
    }

    void StopVibration()
    {
        OVRInput.SetControllerVibration(0, 0, controller);
    }
}