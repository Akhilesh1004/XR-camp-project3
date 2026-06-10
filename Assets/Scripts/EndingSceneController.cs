using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement; // 必須引入此命名空間才能切換場景

public class EndingSceneController : MonoBehaviour
{
    public FadeScreen fadeScreen;
    void Start()
    {
        fadeScreen.FadeIn();
    }

    void Update()
    {
        // 保持空白
    }
}