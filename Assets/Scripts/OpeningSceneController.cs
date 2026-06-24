using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement; // 必須引入此命名空間才能切換場景

public class OpeningSceneController : MonoBehaviour
{
    public FadeScreen fadeScreen;
    public PostProcessEffectController postProcessEffectController;

    [Header("場景設定")]
    public string nextSceneName; // 要切換的場景名稱
    public float delayBeforeFade = 3f; // 等待 N 秒後開始淡出

    [Header("跳過快捷設定")]
    [Tooltip("連按兩下的時間視窗（秒）")]
    public float doublePressWindow = 0.4f;

    private AsyncOperation asyncLoad;
    private bool skipWait = false;
    private float lastSpacePressTime = -999f;
    private float lastAButtonPressTime = -999f;
    private float timer = 0f;
    private bool isStarted = false;

    void Start()
    {
        timer = 0f;
        isStarted = true;
        StartCoroutine(OpeningSequence());
        asyncLoad = SceneManager.LoadSceneAsync(nextSceneName);
        asyncLoad.allowSceneActivation = false;
    }

    void Update()
    {
        if (isStarted) timer += Time.deltaTime;

        // Space 連按兩下
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (timer - lastSpacePressTime <= doublePressWindow)
                skipWait = true;
            lastSpacePressTime = timer;
        }

        // 手把 A 鍵連按兩下
        if (OVRInput.GetDown(OVRInput.Button.One))
        {
            if (timer - lastAButtonPressTime <= doublePressWindow)
                skipWait = true;
            lastAButtonPressTime = timer;
        }
    }

    IEnumerator OpeningSequence()
    {
        float targetTime = delayBeforeFade - 3f;
        while (timer < targetTime && !skipWait)
            yield return null;
        skipWait = false;

        postProcessEffectController.PlayEffect();
        // fadeScreen.FadeOut();

        yield return new WaitForSeconds(fadeScreen.fadeDuration+3f);

        // SceneManager.LoadScene(nextSceneName);
        asyncLoad.allowSceneActivation = true;
    }
}