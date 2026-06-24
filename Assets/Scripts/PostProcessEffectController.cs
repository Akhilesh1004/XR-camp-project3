using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 同時將 Lens Distortion、Chromatic Aberration intensity 從 0 推到 1，並調整 FOV。
/// 呼叫 PlayEffect() 觸發，或開啟 playOnStart 讓它自動跑。
/// </summary>
public class PostProcessEffectController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("拖入 Global Volume")]
    public Volume globalVolume;
    [Tooltip("拖入要調整 FOV 的 Camera（留空則自動尋找 MainCamera）")]
    public Camera targetCamera;
    [Tooltip("拖入 FadeScreen")]
    public FadeScreen fadeScreen;

    [Header("Effect Settings")]
    [Tooltip("動畫總時長（秒）")]
    public float duration = 1f;
    [Tooltip("緩動曲線，X=時間進度 0~1，Y=效果進度 0~1")]
    public AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("FOV Settings")]
    public float fovFrom = 90f;
    public float fovTo = 120f;

    [Header("Lens Distortion")]
    [Range(-1f, 1f)] public float lensDistortionFrom = 0f;
    [Range(-1f, 1f)] public float lensDistortionTo = 1f;

    [Header("Chromatic Aberration")]
    [Range(0f, 1f)] public float chromaticAberrationFrom = 0f;
    [Range(0f, 1f)] public float chromaticAberrationTo = 1f;

    [Header("Auto Play")]
    public bool playOnStart = false;

    private LensDistortion _lensDistortion;
    private ChromaticAberration _chromaticAberration;
    private Coroutine _effectCoroutine;

    void Start()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (globalVolume != null && globalVolume.profile != null)
        {
            globalVolume.profile.TryGet(out _lensDistortion);
            globalVolume.profile.TryGet(out _chromaticAberration);
        }

        SetValues(0f);

        if (playOnStart)
            PlayEffect();
    }

    public void PlayEffect()
    {
        if (_effectCoroutine != null)
            StopCoroutine(_effectCoroutine);

        _effectCoroutine = StartCoroutine(RunEffect());
    }

    public void ResetEffect()
    {
        if (_effectCoroutine != null)
        {
            StopCoroutine(_effectCoroutine);
            _effectCoroutine = null;
        }
        SetValues(0f);
    }

    private IEnumerator RunEffect()
    {
        float elapsed = 0f;

        fadeScreen.FadeOut();

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float curvedT = curve.Evaluate(t);
            SetValues(curvedT);
            yield return null;
        }

        SetValues(1f);
        _effectCoroutine = null;
    }

    private void SetValues(float t)
    {
        if (_lensDistortion != null)
            _lensDistortion.intensity.value = Mathf.Lerp(lensDistortionFrom, lensDistortionTo, t);

        if (_chromaticAberration != null)
            _chromaticAberration.intensity.value = Mathf.Lerp(chromaticAberrationFrom, chromaticAberrationTo, t);

        if (targetCamera != null)
            targetCamera.fieldOfView = Mathf.Lerp(fovFrom, fovTo, t);
    }
}
