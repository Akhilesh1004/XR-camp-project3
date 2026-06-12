using System.Collections;
using UnityEngine;

public class FadeScreen : MonoBehaviour
{
    public bool fadeOnStart = true;
    public float fadeDuration = 2f;
    public Color fadeColor = Color.black;
    private Renderer rend;
    private Material fadeMaterial;
    private Coroutine fadeCoroutine;

    void Start()
    {
        InitializeRenderer();

        if (fadeOnStart)
        {
            SetAlpha(1f);
            FadeIn();
        }
    }

    public void FadeIn()
    {
        StartFade(0f);
    }

    public void FadeOut()
    {
        StartFade(1f);
    }

    public void Fade(float alphaIn, float alphaOut)
    {
        InitializeRenderer();
        SetAlpha(alphaIn);
        StartFade(alphaOut);
    }

    void StartFade(float alphaOut)
    {
        InitializeRenderer();

        if (fadeMaterial == null)
        {
            return;
        }

        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
        }

        fadeCoroutine = StartCoroutine(FadeScreenCoroutine(GetCurrentAlpha(), alphaOut));
    }

    IEnumerator FadeScreenCoroutine(float alphaIn, float alphaOut)
    {
        if (fadeDuration <= 0f)
        {
            SetAlpha(alphaOut);
            fadeCoroutine = null;
            yield break;
        }

        float t = 0;
        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            SetAlpha(Mathf.Lerp(alphaIn, alphaOut, t / fadeDuration));
            yield return null;
        }

        SetAlpha(alphaOut);
        fadeCoroutine = null;
    }

    void InitializeRenderer()
    {
        if (rend == null)
        {
            rend = GetComponent<Renderer>();
        }

        if (fadeMaterial == null && rend != null)
        {
            fadeMaterial = rend.material;
        }
    }

    float GetCurrentAlpha()
    {
        if (fadeMaterial == null)
        {
            return fadeColor.a;
        }

        return fadeMaterial.color.a;
    }

    void SetAlpha(float alpha)
    {
        InitializeRenderer();

        if (fadeMaterial == null)
        {
            return;
        }

        Color newColor = fadeColor;
        newColor.a = Mathf.Clamp01(alpha);
        fadeMaterial.color = newColor;
    }
}
