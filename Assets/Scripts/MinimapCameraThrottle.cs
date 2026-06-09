using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class MinimapCameraThrottle : MonoBehaviour
{
    [Tooltip("小地圖每秒更新次數。手腕 UI 通常 4~8 已足夠。")]
    public float updatesPerSecond = 5f;
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
        }
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
}
