using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class MinimapCameraThrottle : MonoBehaviour
{
    [Tooltip("小地圖每秒更新次數。手腕 UI 通常 4~8 已足夠。")]
    public float updatesPerSecond = 5f;

    [Header("Player Centered Map")]
    public bool followPlayer = true;
    public Transform target;
    public string targetTag = "Player";
    public float cameraHeight = 600f;
    public float orthographicSize = 180f;
    public bool rotateWithTarget = false;

    [Header("Fog")]
    [Tooltip("讓小地圖 Camera 渲染時暫時不受 RenderSettings Fog 影響")]
    public bool disableFogWhileRendering = true;


    private Camera minimapCamera;
    private float nextRenderTime;

    void Awake()
    {
        minimapCamera = GetComponent<Camera>();

        if (minimapCamera != null)
        {
            minimapCamera.enabled = false;
            minimapCamera.orthographic = true;
        }

        FindTargetIfNeeded();
    }

    void OnEnable()
    {
        nextRenderTime = 0f;
    }

    void LateUpdate()
    {
        if (minimapCamera == null)
        {
            return;
        }

        float interval = 1f / Mathf.Max(0.1f, updatesPerSecond);

        if (Time.unscaledTime < nextRenderTime)
        {
            return;
        }

        nextRenderTime = Time.unscaledTime + interval;
        RenderMinimapOnce();
    }

    void RenderMinimapOnce()
    {
        UpdateCameraTransform();

        if (!disableFogWhileRendering)
        {
            minimapCamera.Render();
            return;
        }

        bool previousFogState = RenderSettings.fog;

        RenderSettings.fog = false;

        try
        {
            minimapCamera.Render();
        }
        finally
        {
            RenderSettings.fog = previousFogState;
        }
    }

    void UpdateCameraTransform()
    {
        if (!followPlayer)
        {
            return;
        }

        FindTargetIfNeeded();

        if (target == null)
        {
            return;
        }

        minimapCamera.orthographic = true;
        minimapCamera.orthographicSize = Mathf.Max(1f, orthographicSize);

        Vector3 targetPosition = target.position;
        transform.position = new Vector3(
            targetPosition.x,
            targetPosition.y + Mathf.Max(1f, cameraHeight),
            targetPosition.z
        );

        float yaw = rotateWithTarget ? target.eulerAngles.y : 0f;
        transform.rotation = Quaternion.Euler(90f, yaw, 0f);
    }

    void FindTargetIfNeeded()
    {
        if (target != null)
        {
            return;
        }

        GameObject targetObject = GameObject.FindGameObjectWithTag(targetTag);

        if (targetObject != null)
        {
            target = targetObject.transform;
        }
    }
}
