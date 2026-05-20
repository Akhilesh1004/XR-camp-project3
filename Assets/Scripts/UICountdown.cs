using UnityEngine;
using TMPro;

public class UICountdown : MonoBehaviour
{
    [Header("UI 顯示元件")]
    public TextMeshProUGUI countdownText;

    [Header("倒數設定")]
    public float totalTimeInSeconds = 60f;
    
    private float currentTime;
    private bool isTimerRunning = false;

    void Start()
    {
        currentTime = totalTimeInSeconds;
        isTimerRunning = true;
    }

    void Update()
    {
        if (!isTimerRunning || countdownText == null) return;

        if (currentTime > 0)
        {
            currentTime -= Time.deltaTime;
            
            UpdateTimerDisplay(currentTime);
        }
        else
        {
            currentTime = 0;
            isTimerRunning = false;
            OnTimerEnd();
        }
    }

    void UpdateTimerDisplay(float timeToDisplay)
    {
        if (timeToDisplay < 0) timeToDisplay = 0;

        int minutes = Mathf.FloorToInt(timeToDisplay / 60);
        int seconds = Mathf.FloorToInt(timeToDisplay % 60);
        
        countdownText.text = string.Format("{0:00}:{1:00}", minutes, seconds);

        /* // 💡 如果你想要驚險感，小於 10 秒時顯示到小數點後兩位（例如 09.45）
        if (timeToDisplay < 10f)
        {
            countdownText.text = timeToDisplay.ToString("F2");
            countdownText.color = Color.red; // 順便變紅色增加緊張感
        }
        */
    }
    void OnTimerEnd()
    {
        countdownText.text = "TIME UP!";
        Debug.Log("【倒數結束】時間到了！");
    }

    public void ResetTimer(float newTime)
    {
        totalTimeInSeconds = newTime;
        currentTime = newTime;
        isTimerRunning = true;
    }
}