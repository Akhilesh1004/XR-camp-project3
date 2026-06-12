using UnityEngine;

public class SpeedLineVisibilityController : MonoBehaviour
{
    [Header("Speed Source")]
    public Rigidbody playerRigidbody;
    public Transform fallbackVelocitySource;
    public bool horizontalSpeedOnly = true;

    [Header("Speed Line Visual")]
    public GameObject visualRoot;
    public Renderer[] speedLineRenderers;
    public bool startHidden = true;
    public bool disableRenderersWhenHidden = true;

    [Header("Threshold")]
    public float showSpeed = 12f;
    public float hideSpeed = 9f;
    public float fullIntensitySpeed = 25f;
    public float fadeSharpness = 12f;

    [Header("Optional Material Alpha")]
    public bool driveMaterialAlpha = false;
    public string baseColorProperty = "_BaseColor";
    public string colorProperty = "_Color";

    private readonly MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
    private Vector3 lastFallbackPosition;
    private bool hasLastFallbackPosition;
    private float currentIntensity;
    private bool visualShouldBeVisible;

    void Awake()
    {
        AutoBindReferences();

        if (startHidden)
        {
            currentIntensity = 0f;
            visualShouldBeVisible = false;
            ApplyVisuals(0f, true);
        }
    }

    void OnEnable()
    {
        AutoBindReferences();
        PrimeFallbackVelocitySample();
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;

        if (dt <= 0f)
        {
            return;
        }

        float speed = GetCurrentSpeed(dt);

        if (visualShouldBeVisible)
        {
            visualShouldBeVisible = speed > Mathf.Max(0f, hideSpeed);
        }
        else
        {
            visualShouldBeVisible = speed >= Mathf.Max(0f, showSpeed);
        }

        float targetIntensity = visualShouldBeVisible
            ? Mathf.InverseLerp(
                Mathf.Max(0.01f, showSpeed),
                Mathf.Max(showSpeed + 0.01f, fullIntensitySpeed),
                speed)
            : 0f;

        float lerpT = 1f - Mathf.Exp(-Mathf.Max(0.01f, fadeSharpness) * dt);
        currentIntensity = Mathf.Lerp(currentIntensity, targetIntensity, lerpT);

        ApplyVisuals(currentIntensity, false);
    }

    void AutoBindReferences()
    {
        if (visualRoot == null)
        {
            visualRoot = gameObject;
        }

        if (speedLineRenderers == null || speedLineRenderers.Length == 0)
        {
            speedLineRenderers = visualRoot.GetComponentsInChildren<Renderer>(true);
        }

        if (playerRigidbody == null)
        {
            playerRigidbody = GetComponentInParent<Rigidbody>();
        }

        if (fallbackVelocitySource == null)
        {
            fallbackVelocitySource = playerRigidbody != null
                ? playerRigidbody.transform
                : transform;
        }
    }

    void PrimeFallbackVelocitySample()
    {
        if (fallbackVelocitySource == null)
        {
            hasLastFallbackPosition = false;
            return;
        }

        lastFallbackPosition = fallbackVelocitySource.position;
        hasLastFallbackPosition = true;
    }

    float GetCurrentSpeed(float dt)
    {
        Vector3 velocity;

        if (playerRigidbody != null)
        {
            velocity = playerRigidbody.velocity;
        }
        else if (fallbackVelocitySource != null)
        {
            if (!hasLastFallbackPosition)
            {
                PrimeFallbackVelocitySample();
                return 0f;
            }

            Vector3 currentPosition = fallbackVelocitySource.position;
            velocity = (currentPosition - lastFallbackPosition) / dt;
            lastFallbackPosition = currentPosition;
        }
        else
        {
            return 0f;
        }

        if (horizontalSpeedOnly)
        {
            velocity.y = 0f;
        }

        return velocity.magnitude;
    }

    void ApplyVisuals(float intensity, bool force)
    {
        if (speedLineRenderers == null)
        {
            return;
        }

        float alpha = Mathf.Clamp01(intensity);
        bool rendererEnabled = !disableRenderersWhenHidden || alpha > 0.01f;

        foreach (Renderer renderer in speedLineRenderers)
        {
            if (renderer == null)
            {
                continue;
            }

            if (force || renderer.enabled != rendererEnabled)
            {
                renderer.enabled = rendererEnabled;
            }

            if (driveMaterialAlpha)
            {
                renderer.GetPropertyBlock(propertyBlock);
                Color tint = Color.white;
                tint.a = alpha;
                propertyBlock.SetColor(baseColorProperty, tint);
                propertyBlock.SetColor(colorProperty, tint);
                renderer.SetPropertyBlock(propertyBlock);
            }
        }
    }

    void OnValidate()
    {
        showSpeed = Mathf.Max(0f, showSpeed);
        hideSpeed = Mathf.Clamp(hideSpeed, 0f, showSpeed);
        fullIntensitySpeed = Mathf.Max(showSpeed + 0.01f, fullIntensitySpeed);
        fadeSharpness = Mathf.Max(0.01f, fadeSharpness);
    }
}
