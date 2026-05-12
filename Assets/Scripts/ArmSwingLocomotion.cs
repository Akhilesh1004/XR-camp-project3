using UnityEngine;

public class ArmSwingLocomotion : MonoBehaviour
{
    [Header("綁定物件")]
    public Rigidbody playerRigidbody;

    [Tooltip("頭盔攝影機，例如 CenterEyeAnchor，用來決定跑步方向")]
    public Transform headCamera;

    [Header("控制器設定")]
    public OVRInput.Controller leftController = OVRInput.Controller.LTouch;
    public OVRInput.Controller rightController = OVRInput.Controller.RTouch;

    [Header("跑步參數")]
    public float swingMultiplier = 2.5f;
    public float maxMoveSpeed = 10f;
    public float activationThreshold = 0.5f;
    public float deceleration = 5f;
    public float accelerationSmooth = 4f;

    [Header("地面偵測")]
    public LayerMask groundLayer;
    public float groundCheckDistance = 1.1f;

    private bool isGrounded;

    void FixedUpdate()
    {
        CheckGrounded();

        // 優先級：
        // 1. 抓牆最高
        // 2. 蛛絲擺盪 / pending swing 次高
        // 3. 空中不能跑
        // 4. 只有在地面且沒有其他移動技能時，才允許擺臂跑
        if (WallGrabber.IsGrabbing || WebSwinger.IsSwinging || !isGrounded)
        {
            return;
        }

        ProcessArmSwing();
    }

    void CheckGrounded()
    {
        isGrounded = Physics.Raycast(
            playerRigidbody.position,
            Vector3.down,
            groundCheckDistance,
            groundLayer,
            QueryTriggerInteraction.Ignore
        );

        // Debug.DrawRay(
        //     playerRigidbody.position,
        //     Vector3.down * groundCheckDistance,
        //     isGrounded ? Color.green : Color.red
        // );
    }

    void ProcessArmSwing()
    {
        Vector3 leftVel = OVRInput.GetLocalControllerVelocity(leftController);
        Vector3 rightVel = OVRInput.GetLocalControllerVelocity(rightController);

        float totalSwingSpeed = leftVel.magnitude + rightVel.magnitude;

        Vector3 currentHorizontalVelocity = playerRigidbody.velocity;
        currentHorizontalVelocity.y = 0f;

        if (totalSwingSpeed > activationThreshold)
        {
            Vector3 moveDirection = headCamera.forward;
            moveDirection.y = 0f;

            if (moveDirection.sqrMagnitude < 0.001f)
            {
                return;
            }

            moveDirection.Normalize();

            float targetSpeed = Mathf.Clamp(
                totalSwingSpeed * swingMultiplier,
                0f,
                maxMoveSpeed
            );

            Vector3 targetVelocity = moveDirection * targetSpeed;

            Vector3 newHorizontalVelocity = Vector3.Lerp(
                currentHorizontalVelocity,
                targetVelocity,
                Time.fixedDeltaTime * accelerationSmooth
            );

            playerRigidbody.velocity = new Vector3(
                newHorizontalVelocity.x,
                playerRigidbody.velocity.y,
                newHorizontalVelocity.z
            );
        }
        else
        {
            if (currentHorizontalVelocity.magnitude > 0.1f)
            {
                Vector3 brakedHorizontalVelocity = Vector3.Lerp(
                    currentHorizontalVelocity,
                    Vector3.zero,
                    Time.fixedDeltaTime * deceleration
                );

                playerRigidbody.velocity = new Vector3(
                    brakedHorizontalVelocity.x,
                    playerRigidbody.velocity.y,
                    brakedHorizontalVelocity.z
                );
            }
        }
    }
}