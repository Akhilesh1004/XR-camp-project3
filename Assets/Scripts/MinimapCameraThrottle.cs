using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class MinimapCameraThrottle : MonoBehaviour
{
    [Tooltip("小地圖每秒更新次數。手腕 UI 通常 4~8 已足夠。")]
    public float updatesPerSecond = 5f;

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
        minimapCamera.Render();
    }
}
