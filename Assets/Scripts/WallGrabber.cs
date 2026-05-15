using System.Collections.Generic;
using UnityEngine;

public class WallGrabber : MonoBehaviour
{
    [Header("綁定物件")]
    public Rigidbody playerRigidbody;
    public CapsuleCollider playerCapsule;
    public Transform handTransform;
    public Transform trackingSpaceTransform;
    public Transform headCamera;

    [Header("互斥系統")]
    public WebSwinger[] webSwingers;

    [Header("抓牆設定")]
    public LayerMask wallLayer;
    public float handDetectRadius = 0.12f;
    public OVRInput.Controller controller = OVRInput.Controller.RTouch;
    public OVRInput.Button grabButton = OVRInput.Button.PrimaryHandTrigger;

    [Header("玩家碰撞設定")]
    public LayerMask collisionLayer;

    [Header("頭頂阻擋偵測 (屋簷防卡)")]
    public float headBlockCheckRadius = 0.16f;
    public float headBlockCheckDistance = 0.22f;
    public float overheadNormalThreshold = 0.35f;

    [Header("抓取安全限制 (防 Air-Climb)")]
    public float maxGrabReach = 1.05f;
    public float normalRefreshSpeed = 12f;

    [Header("物理牽引式爬牆")]
    public float climbVelocityMultiplier = 0.85f;
    public float maxClimbVelocity = 4.5f;
    public float maxClimbVelocityChange = 0.9f;
    public float holdDamping = 10f;
    public float outwardAssistVelocity = 0.35f;
    public float upwardAssistThreshold = 0.03f;

    [Header("攀爬與甩動設定")]
    public float throwMultiplier = 1.1f;
    public float maxThrowSpeed = 7.0f;
    public float minThrowSpeed = 0.3f;
    public float releaseVelocityBlend = 0.65f;

    [Header("震動")]
    public float grabVibrationFrequency = 0.1f;
    public float grabVibrationAmplitude = 0.3f;
    public float grabVibrationDuration = 0.1f;

    private bool isTouchingWall = false;
    private bool isGrabbing = false;

    private Vector3 previousHandLocalPos;
    private Vector3 grabbedWallNormal = Vector3.zero;
    private Vector3 grabWorldPoint; 

    private static WallGrabber activeGrabber;
    private static readonly List<WallGrabber> grabbingHands = new List<WallGrabber>();

    public static bool IsGrabbing => grabbingHands.Count > 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        activeGrabber = null;
        grabbingHands.Clear();
    }

    void Awake()
    {
        if (playerCapsule == null && playerRigidbody != null)
        {
            playerCapsule = playerRigidbody.GetComponent<CapsuleCollider>();
        }
    }

    void Update()
    {
        if (playerRigidbody == null || handTransform == null)
            return;

        isTouchingWall = Physics.CheckSphere(
            handTransform.position,
            handDetectRadius,
            wallLayer,
            QueryTriggerInteraction.Ignore
        );

        if (!isGrabbing && isTouchingWall && OVRInput.GetDown(grabButton, controller))
        {
            StartGrabVibration();
            GrabWall();
        }

        if (isGrabbing && OVRInput.GetUp(grabButton, controller))
        {
            ReleaseWall(true); // 正常放開才給予拋物推力
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
        if (isGrabbing) return;

        if (TryGetWallContact(out grabbedWallNormal, out Vector3 contactPoint))
        {
            grabWorldPoint = contactPoint;
        }
        else
        {
            grabbedWallNormal = -GetFlatForward();
            grabWorldPoint = handTransform.position; 
        }

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
        previousHandLocalPos = OVRInput.GetLocalControllerPosition(controller);
    }

    void MovePlayerOnWall()
    {
        // 防呆：超過抓取距離直接斷開，且不給予拋出動能，防止爆衝
        if (Vector3.Distance(handTransform.position, grabWorldPoint) > maxGrabReach)
        {
            ReleaseWall(false);
            return;
        }

        if (TryGetWallContact(out Vector3 refreshedNormal, out _))
        {
            grabbedWallNormal = Vector3.Slerp(
                grabbedWallNormal,
                refreshedNormal,
                normalRefreshSpeed * Time.fixedDeltaTime
            );
        }

        Vector3 currentHandLocalPos = OVRInput.GetLocalControllerPosition(controller);
        Vector3 localDelta = currentHandLocalPos - previousHandLocalPos;
        Vector3 worldDelta = LocalDirectionToWorld(localDelta);

        Vector3 desiredMove = -worldDelta;

        if (desiredMove.sqrMagnitude < 0.000001f)
        {
            Vector3 dampedVelocity = Vector3.MoveTowards(
                playerRigidbody.velocity,
                Vector3.zero,
                holdDamping * Time.fixedDeltaTime
            );

            playerRigidbody.velocity = dampedVelocity;
            previousHandLocalPos = currentHandLocalPos;
            return;
        }

        Vector3 targetVelocity = desiredMove / Time.fixedDeltaTime;
        targetVelocity *= climbVelocityMultiplier;

        if (targetVelocity.magnitude > maxClimbVelocity)
        {
            targetVelocity = targetVelocity.normalized * maxClimbVelocity;
        }

        bool wantsToMoveUp = desiredMove.y > upwardAssistThreshold;

        if (wantsToMoveUp)
        {
            bool blockedAbove = IsHeadBlockedAbove();

            if (blockedAbove)
            {
                Vector3 outward = Vector3.ProjectOnPlane(grabbedWallNormal, Vector3.up);

                if (outward.sqrMagnitude > 0.001f)
                {
                    outward.Normalize();
                    targetVelocity += outward * outwardAssistVelocity;
                }
            }
        }

        Vector3 velocityChange = targetVelocity - playerRigidbody.velocity;

        if (velocityChange.magnitude > maxClimbVelocityChange)
        {
            velocityChange = velocityChange.normalized * maxClimbVelocityChange;
        }

        playerRigidbody.AddForce(velocityChange, ForceMode.VelocityChange);

        previousHandLocalPos = currentHandLocalPos;
    }

    bool IsHeadBlockedAbove()
    {
        if (playerCapsule == null) return false;

        GetCapsuleWorldPoints(out Vector3 p1, out Vector3 p2, out float radius);

        Vector3 up = Vector3.up;
        Vector3 headSphereCenter = p1;

        float checkRadius = Mathf.Min(headBlockCheckRadius, radius * 0.65f);
        checkRadius = Mathf.Max(0.04f, checkRadius);

        Vector3 overlapCenter = headSphereCenter + up * 0.03f;

        Collider[] overlapped = Physics.OverlapSphere(
            overlapCenter,
            checkRadius,
            collisionLayer,
            QueryTriggerInteraction.Ignore
        );

        foreach (Collider col in overlapped)
        {
            if (IsOverheadCollider(col, overlapCenter, checkRadius))
            {
                return true;
            }
        }

        RaycastHit[] hits = Physics.SphereCastAll(
            headSphereCenter,
            checkRadius,
            up,
            headBlockCheckDistance,
            collisionLayer,
            QueryTriggerInteraction.Ignore
        );

        if (hits == null || hits.Length == 0) return false;

        foreach (RaycastHit hit in hits)
        {
            float overheadDot = Vector3.Dot(hit.normal, Vector3.down);

            if (overheadDot >= overheadNormalThreshold)
            {
                return true;
            }
        }

        return false;
    }

    bool IsOverheadCollider(Collider col, Vector3 sphereCenter, float checkRadius)
    {
        Vector3 closestPoint = col.ClosestPoint(sphereCenter);
        Vector3 fromSurfaceToSphere = sphereCenter - closestPoint;

        if (fromSurfaceToSphere.sqrMagnitude > 0.0001f)
        {
            float overheadDot = Vector3.Dot(fromSurfaceToSphere.normalized, Vector3.down);
            return overheadDot >= overheadNormalThreshold;
        }

        // 改用 bounds.min.y 來判定底部是否接近頭頂，避免大樓高牆誤判
        float colliderBottomY = col.bounds.min.y;
        float allowedPenetration = checkRadius * 0.75f;

        return colliderBottomY >= sphereCenter.y - allowedPenetration;
    }

    bool TryGetWallContact(out Vector3 normal, out Vector3 point)
    {
        normal = Vector3.zero;
        point = handTransform.position;

        Vector3 bodyCenter = playerRigidbody.position;

        if (headCamera != null)
        {
            bodyCenter = headCamera.position;
        }
        else if (playerCapsule != null)
        {
            bodyCenter = playerCapsule.transform.TransformPoint(playerCapsule.center);
        }

        Vector3 toHand = handTransform.position - bodyCenter;

        if (toHand.sqrMagnitude > 0.0001f)
        {
            Vector3 dir = toHand.normalized;
            float dist = toHand.magnitude + handDetectRadius + 0.4f;

            if (Physics.SphereCast(
                bodyCenter,
                0.05f,
                dir,
                out RaycastHit hit,
                dist,
                wallLayer,
                QueryTriggerInteraction.Ignore))
            {
                Vector3 n = hit.normal;
                n.y = 0f;

                if (n.sqrMagnitude > 0.001f)
                {
                    normal = n.normalized;
                    point = hit.point;
                    return true;
                }
            }
        }

        Collider[] hits = Physics.OverlapSphere(
            handTransform.position,
            handDetectRadius,
            wallLayer,
            QueryTriggerInteraction.Ignore
        );

        if (hits == null || hits.Length == 0) return false;

        Vector3 bestNormal = Vector3.zero;
        Vector3 bestPoint = handTransform.position;
        float bestDist = float.MaxValue;

        foreach (Collider col in hits)
        {
            Vector3 p = col.ClosestPoint(handTransform.position);
            Vector3 n = handTransform.position - p;

            if (n.sqrMagnitude < 0.0001f) continue;

            float d = n.sqrMagnitude;

            if (d < bestDist)
            {
                bestDist = d;
                bestNormal = n.normalized;
                bestPoint = p;
            }
        }

        bestNormal.y = 0f;

        if (bestNormal.sqrMagnitude < 0.001f) return false;

        normal = bestNormal.normalized;
        point = bestPoint;
        return true;
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

    Vector3 LocalDirectionToWorld(Vector3 localDirection)
    {
        if (trackingSpaceTransform != null)
        {
            return trackingSpaceTransform.TransformDirection(localDirection);
        }

        return playerRigidbody.transform.TransformDirection(localDirection);
    }

    Vector3 GetFlatForward()
    {
        Transform forwardSource = headCamera != null ? headCamera : trackingSpaceTransform;

        if (forwardSource != null)
        {
            Vector3 forward = forwardSource.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude > 0.001f)
            {
                return forward.normalized;
            }
        }

        Vector3 fallback = playerRigidbody.transform.forward;
        fallback.y = 0f;

        if (fallback.sqrMagnitude > 0.001f)
        {
            return fallback.normalized;
        }

        return Vector3.forward;
    }

    void ReleaseWall(bool allowThrow)
    {
        if (!isGrabbing) return;

        bool wasActive = activeGrabber == this;

        isGrabbing = false;
        grabbingHands.Remove(this);

        if (grabbingHands.Count > 0)
        {
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

        activeGrabber = null;
        playerRigidbody.useGravity = true;

        if (allowThrow)
        {
            Vector3 localHandVelocity = OVRInput.GetLocalControllerVelocity(controller);
            Vector3 worldHandVelocity = LocalDirectionToWorld(localHandVelocity);

            Vector3 throwVelocity = -worldHandVelocity * throwMultiplier;

            if (throwVelocity.magnitude < minThrowSpeed)
            {
                throwVelocity = Vector3.zero;
            }
            else if (throwVelocity.magnitude > maxThrowSpeed)
            {
                throwVelocity = throwVelocity.normalized * maxThrowSpeed;
            }

            playerRigidbody.velocity = Vector3.Lerp(
                playerRigidbody.velocity,
                throwVelocity,
                releaseVelocityBlend
            );
        }
    }

    void OnDisable()
    {
        StopVibration();
        ReleaseWall(false);
    }

    void StartGrabVibration()
    {
        OVRInput.SetControllerVibration(grabVibrationFrequency, grabVibrationAmplitude, controller);
        CancelInvoke(nameof(StopVibration));
        Invoke(nameof(StopVibration), grabVibrationDuration);
    }

    void StopVibration()
    {
        OVRInput.SetControllerVibration(0f, 0f, controller);
    }

    void OnDrawGizmosSelected()
    {
        if (handTransform == null) return;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(handTransform.position, handDetectRadius);
    }
}