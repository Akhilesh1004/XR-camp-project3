using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FadeScreen : MonoBehaviour
{
    public bool fadeOnStart = true;
    public float fadeDuration = 2f;
    public Color fadeColor = Color.black;
    private Renderer rend;

    void Start()
    {
        rend = GetComponent<Renderer>();
        if (fadeOnStart)
        {
            FadeIn();
        }
    }

    public void FadeIn()
    {
        Fade(1, 0);
    }

    public void FadeOut()
    {
        Fade(0, 1);
    }

    public void Fade(float alphaIn, float alphaOut)
    {
        StartCoroutine(FadeScreenCoroutine(alphaIn, alphaOut));
    }

    IEnumerator FadeScreenCoroutine(float alphaIn, float alphaOut)
    {
        float t = 0;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            Color newColor = fadeColor;
            newColor.a = Mathf.Lerp(alphaIn, alphaOut, t / fadeDuration);

            rend.material.color = newColor;
            yield return null;
        }

        Color finalColor = fadeColor;
        finalColor.a = alphaOut;
        rend.material.color = finalColor;
    }
}
