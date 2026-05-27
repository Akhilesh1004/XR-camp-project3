using System.Collections.Generic;
using UnityEngine;
using Debug = UnityEngine.Debug;
using UnityEngine.UI;
using TMPro;

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
    [Tooltip("正常狀態下的縮放倍率，乘上按鈕原本大小")]
    public float normalScale = 1f;
    [Tooltip("縮放動畫速度")]
    public float lerpSpeed = 10f;

    [Header("動態生成設定")]
    public GameObject orderOptionPrefab;
    public Transform contentContainer;
    public Transform acceptedOrderContainer;
    public GameObject acceptedOrderEntryPrefab;

    private Dictionary<Button, Vector3> buttonOriginalScales = new Dictionary<Button, Vector3>();
    private Button lastHoveredButton;
    private GameObject lastHoveredOrderObject;

    // 用來儲存目前已經在 UI 上生成的「等待接受訂單」項目，方便追蹤、更新時間或單獨刪除
    private Dictionary<int, GameObject> activeUIOrderEntries = new Dictionary<int, GameObject>();
    // 用來儲存已接受訂單的 UI 項目 (支持多個同時接受的訂單)
    private Dictionary<int, GameObject> acceptedOrderUIEntries = new Dictionary<int, GameObject>();

    void Start()
    {
        if (uiCanvasGroup != null) SetUIVisibility(StartVisible);
        
        // 初始時先清空容器（安全起見）
        ClearUIList();
    }

    void Update()
    {
        if (uiCanvasGroup == null || handTransform == null) return;

        // 切換 UI 顯示狀態
        if (OVRInput.GetDown(Button))
        {
            Debug.Log("Toggle UI visibility");
            SetUIVisibility(uiCanvasGroup.alpha < 0.1f);
        }

        // ➔ 【整合新邏輯】：如果 UI 正開啟，且遊戲進行中，動態更新 UI 上的倒數計時文字
        if (uiCanvasGroup.alpha > 0.9f && DeliveryGameManager.Instance != null && DeliveryGameManager.Instance.GameActive)
        {
            UpdateUIDowncountTimers();
            UpdateActiveOrderTimer();
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
                    // customCursor.position = hit.point;
                    Vector3 exactHitPoint = hit.point;
                    customCursor.position = exactHitPoint + (rayOrigin - exactHitPoint).normalized * 0.005f;
                    customCursor.rotation = Quaternion.LookRotation(-hit.normal, uiCanvasGroup.transform.up);
                }

                Button targetButton = hit.collider.GetComponent<Button>();

                if (targetButton != null && targetButton.interactable)
                {
                    currentHoveredButton = targetButton;

                    Vector3 targetOriginalScale = GetOriginalScale(targetButton);
                    targetButton.transform.localScale = Vector3.Lerp(
                        targetButton.transform.localScale, 
                        targetOriginalScale * hoveredScale, 
                        Time.deltaTime * lerpSpeed
                    );

                    GameObject hoveredOrderEntry = FindOrderEntryRoot(targetButton.transform);
                    ShowHoverPreviewForOrder(hoveredOrderEntry);

                    if (OVRInput.GetDown(OVRInput.Button.One))
                    {
                        Debug.Log($"點擊了按鈕: {hit.collider.name}");
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
            }
        }

        // 處理按鈕縮放復原邏輯
        if (lastHoveredButton != null && lastHoveredButton != currentHoveredButton)
        {
            lastHoveredButton.transform.localScale = GetOriginalScale(lastHoveredButton) * normalScale;
        }

        if (lastHoveredButton != null && lastHoveredButton != currentHoveredButton)
        {
            Vector3 targetNormalScale = GetOriginalScale(lastHoveredButton) * normalScale;
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

        // 每次打開 UI 時，主動向 GameManager 同步最新訂單狀態
        if (visible)
        {
            RefreshOrderList();
        }
    }

    #region 訂單動態生成與更新核心 (與 GameManager 串接)

    // 清空目前畫面上所有的 UI 訂單項目
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

    // 由 DeliveryGameManager 主動呼叫，用來完整重新整理待接單列表
    public void RefreshOrderList()
    {
        if (orderOptionPrefab == null || contentContainer == null || DeliveryGameManager.Instance == null) return;

        // 1. 先徹底清理舊 UI 物件
        ClearUIList();

        // 2. 獲取當前 GameManager 中所有正在「等待接受」的訂單
        List<DeliveryOrder> currentOrders = DeliveryGameManager.Instance.AllOrders;

        foreach (DeliveryOrder order in currentOrders)
        {
            // 只有「等待接受」狀態的訂單才需要顯示在手腕 UI 選單上
            if (order.state != OrderState.WaitingAccept) continue;

            int currentOrderId = order.orderId;
            string foodName = order.food.foodName;

            // 生成整個 OrderOption 項目
            GameObject newOrderObj = Instantiate(orderOptionPrefab, contentContainer);
            newOrderObj.name = $"Order_UI_{currentOrderId}";
            newOrderObj.transform.localPosition = new Vector3(newOrderObj.transform.localPosition.x, newOrderObj.transform.localPosition.y, 0f);

            // 【找字 1】修改標題文字，加上倒數描述
            Transform infoTransform = newOrderObj.transform.Find("OrderInfo");
            if (infoTransform != null)
            {
                TextMeshProUGUI infoText = infoTransform.GetComponent<TextMeshProUGUI>();
                if (infoText != null) 
                {
                    // 先給初始文字，格式如: "Burger (15.0s)"
                    infoText.text = $"{foodName} ({order.waitingTimer:F1}s)";
                }
            }

            // 【找字 2】"Yes" 按鈕
            Transform yesTextTransform = newOrderObj.transform.Find("YesBotton/Text (TMP)");
            if (yesTextTransform != null)
            {
                TextMeshProUGUI yesText = yesTextTransform.GetComponent<TextMeshProUGUI>();
                if (yesText != null) yesText.text = "Yes";
            }

            // 【找字 3】"No" 按鈕
            Transform noTextTransform = newOrderObj.transform.Find("NoBotton/Text (TMP)");
            if (noTextTransform != null)
            {
                TextMeshProUGUI noText = noTextTransform.GetComponent<TextMeshProUGUI>();
                if (noText != null) noText.text = "No";
            }

            // 【事件綁定】Yes 按鈕事件：呼叫 GameManager 接受訂單
            Button yesBtn = newOrderObj.transform.Find("YesBotton")?.GetComponent<Button>();
            if (yesBtn != null)
            {
                if (!buttonOriginalScales.ContainsKey(yesBtn)) buttonOriginalScales[yesBtn] = yesBtn.transform.localScale;
                yesBtn.onClick.RemoveAllListeners();
                yesBtn.onClick.AddListener(() => OnOrderChoiceClicked(currentOrderId, true));
            }

            // 【事件綁定】No 按鈕事件：呼叫 GameManager 拒絕/丟棄訂單
            Button noBtn = newOrderObj.transform.Find("NoBotton")?.GetComponent<Button>();
            if (noBtn != null)
            {
                if (!buttonOriginalScales.ContainsKey(noBtn)) buttonOriginalScales[noBtn] = noBtn.transform.localScale;
                noBtn.onClick.RemoveAllListeners();
                noBtn.onClick.AddListener(() => OnOrderChoiceClicked(currentOrderId, false));
            }

            // 記錄到 Dict 方便追蹤
            activeUIOrderEntries.Add(currentOrderId, newOrderObj);
        }

        RefreshAcceptedOrderDisplay();
    }

    // 每幀更新 UI 的倒數計時文字
    void UpdateUIDowncountTimers()
    {
        if (DeliveryGameManager.Instance == null) return;

        foreach (var kvp in activeUIOrderEntries)
        {
            int orderId = kvp.Key;
            GameObject uiObj = kvp.Value;

            if (uiObj == null) continue;

            // 尋找資料層對應的訂單資料
            DeliveryOrder dataOrder = DeliveryGameManager.Instance.AllOrders.Find(o => o.orderId == orderId);
            if (dataOrder != null && dataOrder.state == OrderState.WaitingAccept)
            {
                Transform infoTransform = uiObj.transform.Find("OrderInfo");
                if (infoTransform != null)
                {
                    TextMeshProUGUI infoText = infoTransform.GetComponent<TextMeshProUGUI>();
                    if (infoText != null)
                    {
                        // 即時同步顯示 15 秒剩餘時間
                        infoText.text = $"{dataOrder.food.foodName} ({Mathf.Max(0, dataOrder.waitingTimer):F1}s)";
                    }
                }
            }
        }
    }

    void UpdateActiveOrderTimer()
    {
        if (DeliveryGameManager.Instance == null) return;

        // 獲取所有已接受的訂單，並更新它們的 UI
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

            // 尋找對應的訂單資料
            DeliveryOrder order = allOrders.Find(o => o.orderId == orderId);
            
            // 如果訂單不在「已接受」狀態，移除它的 UI
            if (order == null || order.state != OrderState.Active)
            {
                ordersToRemove.Add(orderId);
                Destroy(orderUIEntry);
            }
            else
            {
                // 更新該訂單的 UI 顯示
                UpdateAcceptedOrderUIDisplay(orderUIEntry, order);
            }
        }

        // 清理已移除的訂單
        foreach (int orderId in ordersToRemove)
        {
            acceptedOrderUIEntries.Remove(orderId);
        }

        // 如果有新的已接受訂單，添加它們的 UI
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

        // 遍歷所有訂單，找出已接受狀態的訂單
        List<DeliveryOrder> allOrders = DeliveryGameManager.Instance.AllOrders;
        foreach (DeliveryOrder order in allOrders)
        {
            // 只有已接受狀態的訂單才需要顯示在已接受容器上
            if (order.state != OrderState.Active) continue;

            // 檢查是否已經生成過此訂單的 UI
            if (!acceptedOrderUIEntries.ContainsKey(order.orderId))
            {
                GameObject orderUIEntry = Instantiate(prefab, acceptedOrderContainer);
                orderUIEntry.name = $"AcceptedOrder_UI_{order.orderId}";
                acceptedOrderUIEntries[order.orderId] = orderUIEntry;
            }

            // 更新訂單 UI 顯示
            UpdateAcceptedOrderUIDisplay(acceptedOrderUIEntries[order.orderId], order);
        }
    }

    // 更新已接受訂單的 UI 顯示
    void UpdateAcceptedOrderUIDisplay(GameObject orderUIEntry, DeliveryOrder order)
    {
        if (orderUIEntry == null) return;

        Transform infoTransform = orderUIEntry.transform.Find("OrderInfo");
        if (infoTransform != null)
        {
            TextMeshProUGUI infoText = infoTransform.GetComponent<TextMeshProUGUI>();
            if (infoText != null)
            {
                infoText.text = $"{order.food.foodName} ({order.activeTimer:F1}s)";
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

    // UI 按鈕真正的 onClick 靈魂
    void OnOrderChoiceClicked(int orderId, bool isAccepted)
    {
        if (DeliveryGameManager.Instance == null) return;

        if (isAccepted)
        {
            Debug.Log($"【UI確認】玩家接受了訂單編號: {orderId}");
            // 呼叫 GameManager 開始計算外送與在世界上生成物件
            DeliveryGameManager.Instance.AcceptOrder(orderId);
        }
        else
        {
            Debug.Log($"【UI拒絕】玩家拒絕了訂單編號: {orderId}");
            // 尋找訂單資料並移除
            DeliveryOrder dataOrder = DeliveryGameManager.Instance.AllOrders.Find(o => o.orderId == orderId);
            if (dataOrder != null)
            {
                DeliveryGameManager.Instance.DiscardOrder(dataOrder);
            }
        }
    }

    #endregion

    Vector3 GetOriginalScale(Button button)
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
}