using System.Collections.Generic;
using UnityEngine;
using Debug = UnityEngine.Debug;
using UnityEngine.UI;
using TMPro;
using System.Numerics;
using Vector3 = UnityEngine.Vector3;
using Quaternion = UnityEngine.Quaternion;

public class WristUIController : MonoBehaviour
{
    [Header("參考物件")]
    public CanvasGroup uiCanvasGroup;
    public OVRInput.Controller controller = OVRInput.Controller.LTouch;
    public OVRInput.Button Button = OVRInput.Button.Four;
    public Transform handTransform;

    [Header("射線與滑鼠圖標設定")]
    public RectTransform customCursor;
    public bool StartVisible = false;
    public LayerMask uiLayerMask = 1 << 5; // 預設 UI layer = 5

    [Header("縮放效果設定")]
    [Tooltip("射線指著按鈕時的縮放倍率，乘上按鈕原本大小")]
    public float hoveredScale = 1.1f;
    // [Tooltip("正常狀態下的縮放倍率，乘上按鈕原本大小")]
    [Tooltip("縮放動畫速度")]
    public float lerpSpeed = 10f;

    [Header("動態生成設定")]
    public GameObject orderOptionPrefab;
    public Transform contentContainer;
    public Transform acceptedOrderContainer;
    public GameObject acceptedOrderEntryPrefab;
    public UnityEngine.Vector3 originalPrefabScale = Vector3.one;

    [Header("獨立分數顯示")]
    [Tooltip("專門拿來顯示獨立分數的 TextMeshPro 元件")]
    public TextMeshProUGUI scoreTextMeshPro; 
    [Tooltip("專門拿來顯示獨立分數的傳統 Text 元件 (若沒用 TMP 可以用這個)")]
    public Text scoreStandardText;

    [Header("手腕專屬音效檔案")]
    public AudioClip uiOpenSound;
    public AudioClip uiCloseSound;
    public AudioClip uiClickSound;
    public AudioClip uiCancelSound;

    [Header("隨身聽音樂播放器設定")]
    [Tooltip("用來播放背景音樂的 AudioSource（建議掛在同一個物件上）")]
    public AudioSource bgmAudioSource;
    [Tooltip("背景音樂歌曲清單")]
    public List<AudioClip> playlist = new List<AudioClip>();
    public List<string> playlistNames = new List<string>();

    [Header("隨身聽 UI 顯示面版")]
    [Tooltip("手腕 UI 上顯示歌名的 TextMeshPro 元件 (SongName)")]
    public TextMeshProUGUI songNameText;
    [Tooltip("PlaySong 按鈕上的 Image 元件")]
    public Image playButtonImage;
    [Tooltip("播放狀態下的圖案 (例如：暫停符號 || )")]
    public Sprite playSprite;
    [Tooltip("暫停狀態下的圖案 (例如：播放符號 ▷ )")]
    public Sprite pauseSprite;


    // 隨身聽內部控制變數
    private int currentSongIndex = 0;

    private Dictionary<Button, UnityEngine.Vector3> buttonOriginalScales = new Dictionary<Button, UnityEngine.Vector3>();
    private Button lastHoveredButton;
    private GameObject lastHoveredOrderObject;

    // 用來儲存目前已經在 UI 上生成的「等待接受訂單」項目，方便追蹤、更新時間或單獨刪除
    private Dictionary<int, GameObject> activeUIOrderEntries = new Dictionary<int, GameObject>();
    // 用來儲存已接受訂單的 UI 項目 (支持多個同時接受的訂單)
    private Dictionary<int, GameObject> acceptedOrderUIEntries = new Dictionary<int, GameObject>();

    void Start()
    {
        Button[] staticButtons = GetComponentsInChildren<Button>(true);
        foreach (Button btn in staticButtons)
        {
            if (btn != null && !buttonOriginalScales.ContainsKey(btn))
            {
                buttonOriginalScales[btn] = btn.transform.localScale;
            }
        }

        if (uiCanvasGroup != null) SetUIVisibility(StartVisible);

        if (bgmAudioSource != null)
        {
            bgmAudioSource.volume = 0.5f;
        }
        
        // 初始時先清空容器（安全起見）
        ClearUIList();
    }

    public void StartGame()
    {
        AutoPlayMusicOnStart();
    }

    void Update()
    {
        if (uiCanvasGroup == null || handTransform == null) return;

        // 切換 UI 顯示狀態
        if (OVRInput.GetDown(Button))
        {
            Debug.Log("Toggle UI visibility");
            if (uiCanvasGroup.alpha > 0.9f)
            {
                DeliveryGameManager.Instance.PlaySound(uiCloseSound, 0.1f);
            }
            else
            {
                DeliveryGameManager.Instance.PlaySound(uiOpenSound, 0.1f);
            }
            SetUIVisibility(uiCanvasGroup.alpha < 0.1f);
        }

        // ➔ 如果 UI 正開啟，且遊戲進行中，動態更新 UI 上的倒數計時文字
        if (uiCanvasGroup.alpha > 0.9f && DeliveryGameManager.Instance != null && DeliveryGameManager.Instance.GameActive)
        {
            UpdateUIDowncountTimers();
            UpdateActiveOrderTimer();
            UpdateIndependentScore();
        }

        Button currentHoveredButton = null;

        if (uiCanvasGroup.alpha > 0.9f)
        {
            Vector3 rayOrigin = handTransform.position;
            Vector3 rayDirection = handTransform.forward;
            float maxRayDistance = 5f;

            if (Physics.Raycast(rayOrigin, rayDirection, out RaycastHit hit, maxRayDistance, uiLayerMask))
            {
                if (customCursor != null)
                {
                    if (!customCursor.gameObject.activeSelf) customCursor.gameObject.SetActive(true);
                    Vector3 exactHitPoint = hit.point;
                    customCursor.position = exactHitPoint + (rayOrigin - exactHitPoint).normalized * 0.005f;
                    customCursor.rotation = Quaternion.LookRotation(-hit.normal, uiCanvasGroup.transform.up);
                }

                // ➔ 不管是打到按鈕還是卡片本身，先透過 FindOrderEntryRoot 往上抓出 Order_UI_ 根節點
                GameObject hoveredOrderEntry = FindOrderEntryRoot(hit.collider.transform);
                
                if (hoveredOrderEntry != null && hoveredOrderEntry.name.StartsWith("Order_UI_"))
                {
                    ShowHoverPreviewForOrder(hoveredOrderEntry);

                    // 解析 ID 並觸發外送小地圖預覽
                    string idString = hoveredOrderEntry.name.Replace("Order_UI_", "");
                    if (int.TryParse(idString, out int hoveredOrderId))
                    {
                        if (DeliveryGameManager.Instance != null)
                        {
                            DeliveryGameManager.Instance.ShowOrderLocationPreview(hoveredOrderId);
                        }
                    }
                }
                else
                {
                    // ➔ 修正：當指著隨身聽時，也順手清掉卡片的暫時 Hover 預覽，不擋游標
                    HideHoverPreviewForOrder(lastHoveredOrderObject);
                    if (DeliveryGameManager.Instance != null)
                    {
                        DeliveryGameManager.Instance.ClearOrderLocationPreview();
                    }
                }

                // ➔ 單獨處理按鈕的變大縮放與點擊事件
                Button targetButton = hit.collider.GetComponent<Button>();
                Slider targetSlider = hit.collider.GetComponentInParent<Slider>(); // 你的原版變數

                if (targetButton != null && targetButton.interactable)
                {
                    string btnName = hit.collider.name;
                    // bool shouldScale = (btnName == "YesBotton" || btnName == "NoBotton");
                    bool shouldScale = true;

                    // 只有特定按鈕在 Hover 的時候才會設定為 currentHoveredButton 並執行變大動畫
                    if (shouldScale) {
                        currentHoveredButton = targetButton;

                        Vector3 targetOriginalScale = GetOriginalScale(targetButton);
                        targetButton.transform.localScale = Vector3.Lerp(
                            targetButton.transform.localScale, 
                            targetOriginalScale * hoveredScale, 
                            Time.deltaTime * lerpSpeed
                        );
                    }

                    if (OVRInput.GetDown(OVRInput.Button.One))
                    {
                        if (btnName == "YesBotton")
                        {
                            DeliveryGameManager.Instance.PlaySound(uiClickSound);
                        }
                        else if (btnName == "NoBotton")
                        {
                            DeliveryGameManager.Instance.PlaySound(uiCancelSound);
                        }
                        else if (btnName == "PlaySong")
                        {
                            TogglePlayStatus(); 
                        }
                        else if (btnName == "NextSong")
                        {
                            ChangeSong(1); 
                        }
                        else if (btnName == "PreviousSong")
                        {
                            ChangeSong(-1); 
                        }
                        else if (btnName == "Louder")
                        {
                            VolumeChanged(0.1f);
                        }
                        else if (btnName == "Quieter")
                        {
                            VolumeChanged(-0.1f);
                        }
                        
                        Debug.Log($"點擊了按鈕: {hit.collider.name}");
                        
                        if (DeliveryGameManager.Instance != null)
                        {
                            DeliveryGameManager.Instance.ClearOrderLocationPreview();
                        }

                        targetButton.onClick.Invoke();
                    }
                }
            }
            else
            {
                if (customCursor != null && customCursor.gameObject.activeSelf)
                {
                    customCursor.gameObject.SetActive(false);
                }
                
                HideHoverPreviewForOrder(lastHoveredOrderObject);

                if (DeliveryGameManager.Instance != null)
                {
                    DeliveryGameManager.Instance.ClearOrderLocationPreview();
                }
            }
        }

        // ➔ 【你的第一段縮放復原】：修正原本乘上 normalScale 的衝突，改回最乾淨的初始絕對值
        if (lastHoveredButton != null && lastHoveredButton != currentHoveredButton)
        {
            string btnName = lastHoveredButton.name;
            // bool shouldScale = (btnName == "YesBotton" || btnName == "NoBotton");
            bool shouldScale = true;

            if (shouldScale) {
                // 直接指定字典裡的初始乾淨大小，不另外乘以常數 1
                lastHoveredButton.transform.localScale = GetOriginalScale(lastHoveredButton);
            }
            else {
                lastHoveredButton = null;
            }
        }

        // ➔ 【你的第二段平滑 Lerp 縮放復原】
        if (lastHoveredButton != null && lastHoveredButton != currentHoveredButton)
        {
            string btnName = lastHoveredButton.name;
            // bool shouldScale = (btnName == "YesBotton" || btnName == "NoBotton");
            bool shouldScale = true;

            if (shouldScale)
            {
                // 終點目標也是直接抓最乾淨的初始大小
                Vector3 targetNormalScale = GetOriginalScale(lastHoveredButton);
                lastHoveredButton.transform.localScale = Vector3.Lerp(
                    lastHoveredButton.transform.localScale, 
                    targetNormalScale, 
                    Time.deltaTime * lerpSpeed
                );
                
                if (Vector3.Distance(lastHoveredButton.transform.localScale, targetNormalScale) < 0.01f)
                {
                    lastHoveredButton.transform.localScale = targetNormalScale;
                    lastHoveredButton = null;
                }
            }
            else
            {
                lastHoveredButton = null;
            }
        }

        if (currentHoveredButton != null)
        {
            lastHoveredButton = currentHoveredButton;
        }
    }

    void SetUIVisibility(bool visible)
    {
        uiCanvasGroup.alpha = visible ? 1f : 0f;
        uiCanvasGroup.interactable = visible;
        uiCanvasGroup.blocksRaycasts = visible;

        if (customCursor != null) customCursor.gameObject.SetActive(visible);

        if (visible)
        {
            RefreshOrderList();
        }
    }

    #region 訂單動態生成與更新核心 (與 GameManager 串接)

    public void ClearUIList()
    {
        HideHoverPreviewForOrder(lastHoveredOrderObject);

        foreach (var kvp in activeUIOrderEntries)
        {
            if (kvp.Value != null)
            {
                CleanupButtonScales(kvp.Value);
                Destroy(kvp.Value);
            }
        }
        activeUIOrderEntries.Clear();
        ClearActiveOrderDisplay();
    }

    public void RefreshOrderList()
    {
        if (orderOptionPrefab == null || contentContainer == null || DeliveryGameManager.Instance == null) return;

        ClearUIList();

        List<DeliveryOrder> currentOrders = DeliveryGameManager.Instance.AllOrders;

        foreach (DeliveryOrder order in currentOrders)
        {
            if (order.state != OrderState.WaitingAccept) continue;

            int currentOrderId = order.orderId;
            string foodName = order.food.foodName;
            int foodValue = order.food.foodValue;
            float maxDeliveryTime = order.food.maxDeliveryTime;

            GameObject newOrderObj = Instantiate(orderOptionPrefab, contentContainer);
            newOrderObj.transform.localScale = originalPrefabScale;
            newOrderObj.name = $"Order_UI_{currentOrderId}";
            newOrderObj.transform.localPosition = new Vector3(newOrderObj.transform.localPosition.x, newOrderObj.transform.localPosition.y, 0f);

            Transform infoTransform = newOrderObj.transform.Find("OrderInfo");
            if (infoTransform != null)
            {
                TextMeshProUGUI infoText = infoTransform.GetComponent<TextMeshProUGUI>();
                if (infoText != null) 
                {
                    infoText.text = $"{foodName} ({order.waitingTimer:F1}sec)\n" +
                                    $"Score: {foodValue}\nTime Limit: {maxDeliveryTime:F1}min";
                }
            }

            Transform yesTextTransform = newOrderObj.transform.Find("YesBotton/Text (TMP)");
            if (yesTextTransform != null)
            {
                TextMeshProUGUI yesText = yesTextTransform.GetComponent<TextMeshProUGUI>();
                if (yesText != null) yesText.text = "Yes";
            }

            Transform noTextTransform = newOrderObj.transform.Find("NoBotton/Text (TMP)");
            if (noTextTransform != null)
            {
                TextMeshProUGUI noText = noTextTransform.GetComponent<TextMeshProUGUI>();
                if (noText != null) noText.text = "No";
            }

            Button yesBtn = newOrderObj.transform.Find("YesBotton")?.GetComponent<Button>();
            if (yesBtn != null)
            {
                if (!buttonOriginalScales.ContainsKey(yesBtn)) buttonOriginalScales[yesBtn] = yesBtn.transform.localScale;
                yesBtn.onClick.RemoveAllListeners();
                yesBtn.onClick.AddListener(() => OnOrderChoiceClicked(currentOrderId, true));
            }

            Button noBtn = newOrderObj.transform.Find("NoBotton")?.GetComponent<Button>();
            if (noBtn != null)
            {
                if (!buttonOriginalScales.ContainsKey(noBtn)) buttonOriginalScales[noBtn] = noBtn.transform.localScale;
                noBtn.onClick.RemoveAllListeners();
                noBtn.onClick.AddListener(() => OnOrderChoiceClicked(currentOrderId, false));
            }

            activeUIOrderEntries.Add(currentOrderId, newOrderObj);
        }

        RefreshAcceptedOrderDisplay();
    }

    void UpdateUIDowncountTimers()
    {
        if (DeliveryGameManager.Instance == null) return;

        foreach (var kvp in activeUIOrderEntries)
        {
            int orderId = kvp.Key;
            GameObject uiObj = kvp.Value;

            if (uiObj == null) continue;

            DeliveryOrder dataOrder = DeliveryGameManager.Instance.AllOrders.Find(o => o.orderId == orderId);
            if (dataOrder != null && dataOrder.state == OrderState.WaitingAccept)
            {
                Transform infoTransform = uiObj.transform.Find("OrderInfo");
                if (infoTransform != null)
                {
                    int currentOrderId = dataOrder.orderId;
                    string foodName = dataOrder.food.foodName;
                    int foodValue = dataOrder.food.foodValue;
                    float maxDeliveryTime = dataOrder.food.maxDeliveryTime;
                    TextMeshProUGUI infoText = infoTransform.GetComponent<TextMeshProUGUI>();
                    if (infoText != null)
                    {
                        infoText.text = $"{foodName} ({Mathf.Max(0, dataOrder.waitingTimer):F1}sec)\n" +
                                        $"Score: {foodValue}\nTime Limit: {maxDeliveryTime:F1}min";
                    }
                }
            }
        }
    }

    void UpdateActiveOrderTimer()
    {
        if (DeliveryGameManager.Instance == null) return;

        List<DeliveryOrder> allOrders = DeliveryGameManager.Instance.AllOrders;
        List<int> ordersToRemove = new List<int>();

        foreach (var kvp in acceptedOrderUIEntries)
        {
            int orderId = kvp.Key;
            GameObject orderUIEntry = kvp.Value;

            if (orderUIEntry == null)
            {
                ordersToRemove.Add(orderId);
                continue;
            }

            DeliveryOrder order = allOrders.Find(o => o.orderId == orderId);
            
            if (order == null || order.state != OrderState.Active)
            {
                ordersToRemove.Add(orderId);
                Destroy(orderUIEntry);
            }
            else
            {
                UpdateAcceptedOrderUIDisplay(orderUIEntry, order);
            }
        }

        foreach (int orderId in ordersToRemove)
        {
            acceptedOrderUIEntries.Remove(orderId);
        }

        foreach (DeliveryOrder order in allOrders)
        {
            if (order.state == OrderState.Active && !acceptedOrderUIEntries.ContainsKey(order.orderId))
            {
                RefreshAcceptedOrderDisplay();
                break;
            }
        }
    }

    void RefreshAcceptedOrderDisplay()
    {
        ClearActiveOrderDisplay();

        if (DeliveryGameManager.Instance == null || acceptedOrderContainer == null) return;

        GameObject prefab = acceptedOrderEntryPrefab != null ? acceptedOrderEntryPrefab : orderOptionPrefab;
        if (prefab == null) return;

        List<DeliveryOrder> allOrders = DeliveryGameManager.Instance.AllOrders;
        foreach (DeliveryOrder order in allOrders)
        {
            if (order.state != OrderState.Active) continue;

            if (!acceptedOrderUIEntries.ContainsKey(order.orderId))
            {
                GameObject orderUIEntry = Instantiate(prefab, acceptedOrderContainer);
                orderUIEntry.transform.localScale = originalPrefabScale;
                orderUIEntry.name = $"AcceptedOrder_UI_{order.orderId}";
                acceptedOrderUIEntries[order.orderId] = orderUIEntry;
            }

            UpdateAcceptedOrderUIDisplay(acceptedOrderUIEntries[order.orderId], order);
        }
    }

    void UpdateAcceptedOrderUIDisplay(GameObject orderUIEntry, DeliveryOrder order)
    {
        if (orderUIEntry == null) return;

        Transform infoTransform = orderUIEntry.transform.Find("OrderInfo");
        if (infoTransform != null)
        {
            int currentOrderId = order.orderId;
            string foodName = order.food.foodName;
            int foodValue = order.food.foodValue;
            float maxDeliveryTime = order.food.maxDeliveryTime;
            TextMeshProUGUI infoText = infoTransform.GetComponent<TextMeshProUGUI>();
            if (infoText != null)
            {
                infoText.text = $"{foodName} ({order.activeTimer:F1}sec)\n" +
                                $"Score: {foodValue}\nTime Limit: {maxDeliveryTime:F1}min";
            }
        }

        Button yesBtn = orderUIEntry.transform.Find("YesBotton")?.GetComponent<Button>();
        if (yesBtn != null) yesBtn.gameObject.SetActive(false);

        Button noBtn = orderUIEntry.transform.Find("NoBotton")?.GetComponent<Button>();
        if (noBtn != null) noBtn.gameObject.SetActive(false);
    }

    void ClearActiveOrderDisplay()
    {
        foreach (var kvp in acceptedOrderUIEntries)
        {
            if (kvp.Value != null)
            {
                Destroy(kvp.Value);
            }
        }
        acceptedOrderUIEntries.Clear();
    }

    void ShowHoverPreviewForOrder(GameObject orderEntry)
    {
        if (orderEntry == null || orderEntry == lastHoveredOrderObject) return;

        HideHoverPreviewForOrder(lastHoveredOrderObject);
        lastHoveredOrderObject = orderEntry;

        Transform previewTransform = orderEntry.transform.Find("PreviewObject");
        if (previewTransform != null)
        {
            previewTransform.gameObject.SetActive(true);
        }
    }

    void HideHoverPreviewForOrder(GameObject orderEntry)
    {
        if (orderEntry == null) return;
        Transform previewTransform = orderEntry.transform.Find("PreviewObject");
        if (previewTransform != null)
        {
            previewTransform.gameObject.SetActive(false);
        }

        if (orderEntry == lastHoveredOrderObject)
        {
            lastHoveredOrderObject = null;
        }
    }

    GameObject FindOrderEntryRoot(Transform child)
    {
        if (child == null) return null;
        Transform current = child;

        while (current.parent != null && current.parent != contentContainer && current.parent != acceptedOrderContainer)
        {
            current = current.parent;
        }

        if (current.parent == contentContainer || current.parent == acceptedOrderContainer)
        {
            return current.gameObject;
        }

        return child.gameObject;
    }

    void OnOrderChoiceClicked(int orderId, bool isAccepted)
    {
        if (DeliveryGameManager.Instance == null) return;

        DeliveryGameManager.Instance.ClearOrderLocationPreview();

        if (isAccepted)
        {
            Debug.Log($"【UI確認】玩家接受了訂單編號: {orderId}");
            DeliveryGameManager.Instance.AcceptOrder(orderId);
        }
        else
        {
            Debug.Log($"【UI拒絕】玩家拒絕了訂單編號: {orderId}");
            DeliveryOrder dataOrder = DeliveryGameManager.Instance.AllOrders.Find(o => o.orderId == orderId);
            if (dataOrder != null)
            {
                DeliveryGameManager.Instance.DiscardOrder(dataOrder);
            }
        }
    }

    #endregion

    UnityEngine.Vector3 GetOriginalScale(Button button)
    {
        if (buttonOriginalScales.TryGetValue(button, out Vector3 originalScale)) return originalScale;
        return button.transform.localScale;
    }

    void CleanupButtonScales(GameObject orderObj)
    {
        if (orderObj == null) return;
        Button[] buttons = orderObj.GetComponentsInChildren<Button>(true);
        foreach (Button btn in buttons)
        {
            if (buttonOriginalScales.ContainsKey(btn)) buttonOriginalScales.Remove(btn);
        }
    }

    private void UpdateIndependentScore()
    {
        if (DeliveryGameManager.Instance == null) return;

        int currentScore = DeliveryGameManager.Instance.Score;

        if (scoreTextMeshPro != null)
        {
            scoreTextMeshPro.text = $"Score: {currentScore}";
        }

        if (scoreStandardText != null)
        {
            scoreStandardText.text = $"Score: {currentScore}";
        }
    }

    public void OnOrderCardHoverEnter(int orderId)
    {
        if (DeliveryGameManager.Instance != null)
        {
            DeliveryGameManager.Instance.ShowOrderLocationPreview(orderId);
        }
    }

    public void OnOrderCardHoverExit()
    {
        if (DeliveryGameManager.Instance != null)
        {
            DeliveryGameManager.Instance.ClearOrderLocationPreview();
        }
    }

    #region 隨身聽音樂播放器核心功能

    private void AutoPlayMusicOnStart()
    {
        if (bgmAudioSource == null || playlist.Count == 0) return;

        // 預設載入清單中的第一首歌曲
        bgmAudioSource.clip = playlist[currentSongIndex];
        bgmAudioSource.Play();
        
        // 將 UI 圖案改為「播放中狀態 (|| 暫停符號)」
        UpdatePlayButtonIcon(false);
        UpdateSongNameUIDisplay();
        Debug.Log($"[隨身聽] 遊戲開始，自動播放第 1 首音樂: {bgmAudioSource.clip.name}");
    }

    private void TogglePlayStatus()
    {
        if (bgmAudioSource == null || playlist.Count == 0) return;

        if (bgmAudioSource.clip == null)
        {
            bgmAudioSource.clip = playlist[currentSongIndex];
        }

        if (bgmAudioSource.isPlaying)
        {
            bgmAudioSource.Pause();
            UpdatePlayButtonIcon(false); 
            Debug.Log("音樂已暫停");
        }
        else
        {
            bgmAudioSource.Play();
            UpdatePlayButtonIcon(true);
            UpdateSongNameUIDisplay();
            Debug.Log($"正在播放第 {currentSongIndex + 1} 首音樂: {bgmAudioSource.clip.name}");
        }
    }

    private void ChangeSong(int direction)
    {
        if (bgmAudioSource == null || playlist.Count == 0) return;

        currentSongIndex += direction;
        if (currentSongIndex >= playlist.Count) currentSongIndex = 0;
        if (currentSongIndex < 0) currentSongIndex = playlist.Count - 1;

        bgmAudioSource.clip = playlist[currentSongIndex];
        bgmAudioSource.Play();

        UpdatePlayButtonIcon(false);
        UpdateSongNameUIDisplay();
        Debug.Log($"已切換歌曲，目前播放第 {currentSongIndex + 1} 首: {bgmAudioSource.clip.name}");
    }

    private void UpdatePlayButtonIcon(bool isPlaying)
    {
        if (playButtonImage == null) return;

        if (isPlaying)
        {
            if (playSprite != null) playButtonImage.sprite = playSprite;
        }
        else
        {
            if (pauseSprite != null) playButtonImage.sprite = pauseSprite;
        }
    }
    private void UpdateSongNameUIDisplay()
    {
        if (songNameText == null) return;

        if (playlistNames != null && currentSongIndex >= 0 && currentSongIndex < playlistNames.Count && !string.IsNullOrEmpty(playlistNames[currentSongIndex]))
        {
            songNameText.text = playlistNames[currentSongIndex];
        }
        else if (playlist.Count > 0 && playlist[currentSongIndex] != null)
        {
            // 後備方案：檔名
            songNameText.text = playlist[currentSongIndex].name;
        }
        else
        {
            songNameText.text = "No Audio Track";
        }
    }

    private void VolumeChanged(float VolumeChange)
    {
        if (bgmAudioSource == null) return;
        
        float newVolume = Mathf.Clamp(bgmAudioSource.volume + VolumeChange, 0f, 1f);
        bgmAudioSource.volume = newVolume;
        Debug.Log($"[隨身聽] 音量已調整為: {bgmAudioSource.volume * 100f:F0}%");
    }

    #endregion
}