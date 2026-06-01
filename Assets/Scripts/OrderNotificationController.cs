using System.Collections;
using UnityEngine;

public class OrderNotificationController : MonoBehaviour
{
    public static OrderNotificationController Instance { get; private set; }

    [Header("UI 引用 (改用 CanvasGroup)")]
    [Tooltip("新訂單跳出提示的 CanvasGroup")]
    public CanvasGroup notificationCanvasGroup;
    
    [Tooltip("你原本的手腕 UI 控制器")]
    public WristUIController wristUI;
    
    [Tooltip("手腕 UI 上的 CanvasGroup (用來判斷其 Alpha 是否顯示)")]
    public CanvasGroup wristUICanvasGroup;

    [Header("音效設定")]
    [Tooltip("新訂單到來的提示音效")]
    public AudioClip orderAlertClip;
    [Range(0f, 1f)] public float soundVolume = 1f;

    [Header("動畫設定")]
    [Tooltip("漸隱動畫持續時間（秒）")]
    public float fadeDuration = 0.5f;

    private Coroutine _hideCoroutine;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        // 預設將通知 Canvas 隱藏（Alpha 設為 0，且不擋住射線）
        if (notificationCanvasGroup != null)
        {
            SetCanvasGroupState(notificationCanvasGroup, 0f);
        }
    }

    void Update()
    {
        // ➔ 【核心需求 3】：當手腕 UI 出現時（Alpha 大於 0），通知 Canvas 必須立刻消失
        if (notificationCanvasGroup != null && notificationCanvasGroup.alpha > 0f)
        {
            if (IsWristUIOpen())
            {
                // 手腕 UI 一開，立刻強制隱藏通知
                HideNotificationImmediate();
            }
        }
    }

    /// <summary>
    /// 檢查手腕 UI 目前是否處於開啟（可見）狀態
    /// </summary>
    private bool IsWristUIOpen()
    {
        // 優先檢查是否有掛 CanvasGroup 且 alpha > 0
        if (wristUICanvasGroup != null)
        {
            return wristUICanvasGroup.alpha > 0f;
        }
        
        // 後備方案：如果你的 wristUI 開關依然會切換 GameObject 本身的 Active 狀態
        if (wristUI != null)
        {
            return wristUI.gameObject.activeSelf;
        }

        return false;
    }

    /// <summary>
    /// 觸發新訂單通知
    /// </summary>
    public void TriggerNotification()
    {
        // ➔ 【核心需求 1】：跳出提示音
        if (DeliveryGameManager.Instance != null && orderAlertClip != null)
        {
            DeliveryGameManager.Instance.PlaySound(orderAlertClip, soundVolume);
        }

        // ➔ 【核心需求 3】：如果玩家手腕 UI 已經是打開的，就不要再跳出通知
        if (IsWristUIOpen()) return;

        // ➔ 【核心需求 2】：透過 Alpha 顯現 Canvas
        if (notificationCanvasGroup != null)
        {
            SetCanvasGroupState(notificationCanvasGroup, 1f);

            // 防呆：重設上一次的倒數協程
            if (_hideCoroutine != null) StopCoroutine(_hideCoroutine);
            
            // 開始 5 秒倒數與漸隱動畫
            _hideCoroutine = StartCoroutine(StartNotificationTimer(5f));
        }
    }

    private IEnumerator StartNotificationTimer(float delay)
    {
        // 保持完全顯示 5 秒
        yield return new WaitForSeconds(delay);

        // ➔ 【平滑優化】：5 秒過後，進入動態漸隱動畫
        float elapsedTime = 0f;
        while (elapsedTime < fadeDuration)
        {
            elapsedTime += Time.deltaTime;
            if (notificationCanvasGroup != null)
            {
                notificationCanvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsedTime / fadeDuration);
            }
            yield return null;
        }

        HideNotificationImmediate();
    }

    /// <summary>
    /// 立刻讓通知消失並關閉互動
    /// </summary>
    private void HideNotificationImmediate()
    {
        if (notificationCanvasGroup != null)
        {
            SetCanvasGroupState(notificationCanvasGroup, 0f);
        }
        
        if (_hideCoroutine != null)
        {
            StopCoroutine(_hideCoroutine);
            _hideCoroutine = null;
        }
    }

    /// <summary>
    /// 輔助函式：同步控制 CanvasGroup 的透明度與碰撞開關
    /// </summary>
    private void SetCanvasGroupState(CanvasGroup cg, float alpha)
    {
        cg.alpha = alpha;
        // 如果 alpha 為 0，就關閉 blocksRaycasts 與 interactable，確保射線可以穿透它，不擋到後面的 VR UI 互動
        cg.blocksRaycasts = alpha > 0f;
        cg.interactable = alpha > 0f;
    }
}