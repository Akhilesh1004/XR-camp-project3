using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement; // 必須引入此命名空間才能切換場景

public class MainSceneController : MonoBehaviour
{
    public FadeScreen fadeScreen;
    
    [Header("場景設定")]
    public string nextSceneNameGoodEnding; // 好結局場景名稱
    public string nextSceneNameBadEnding; // 壞結局場景名稱
    private string nextSceneName; // 實際要切換的場景名稱

    void Start()
    {
        fadeScreen.FadeIn();
    }

    void Update()
    {
        // 保持空白
    }

    public void TriggerSceneTransition(bool isGoodEnding)
    {
        nextSceneName = isGoodEnding ? nextSceneNameGoodEnding : nextSceneNameBadEnding;
        StartCoroutine(OpeningSequence());
    }

    IEnumerator OpeningSequence()
    {
        fadeScreen.FadeOut();

        yield return new WaitForSeconds(fadeScreen.fadeDuration);

        SceneManager.LoadScene(nextSceneName);
    }
}