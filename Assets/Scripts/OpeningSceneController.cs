using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement; // 必須引入此命名空間才能切換場景

public class OpeningSceneController : MonoBehaviour
{
    public FadeScreen fadeScreen;
    
    [Header("場景設定")]
    public string nextSceneName; // 要切換的場景名稱
    public float delayBeforeFade = 3f; // 等待 N 秒後開始淡出
    private AsyncOperation asyncLoad;

    void Start()
    {
        StartCoroutine(OpeningSequence());
        asyncLoad = SceneManager.LoadSceneAsync(nextSceneName);
        asyncLoad.allowSceneActivation = false;
    }

    void Update()
    {
        // 保持空白
    }

    IEnumerator OpeningSequence()
    {
        yield return new WaitForSeconds(delayBeforeFade);

        fadeScreen.FadeOut();

        yield return new WaitForSeconds(fadeScreen.fadeDuration);

        // SceneManager.LoadScene(nextSceneName);
        asyncLoad.allowSceneActivation = true;
    }
}