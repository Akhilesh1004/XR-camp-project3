using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public enum OrderState
{
    InQueue,      // 排程中（等待輪到它）
    WaitingAccept,// 等待玩家接受（15秒倒數）
    Active,       // 玩家已接受（開始外送計時）
    Completed,    // 已完成
    Discarded     // 已超時或被玩家丟棄
}

[System.Serializable]
public class DeliveryOrder
{
    public int orderId;
    public DeliveryGameManager.FoodOption food;
    public Transform pickupPoint;
    public Transform destinationPoint;
    public OrderState state = OrderState.InQueue;

    // 時間相關
    public float waitingTimer = 15f; // 等待接受的倒數時間
    public float activeTimer = 0f;    // 接受後花費的時間（從0開始往上加）

    // 產生的實體引用
    public DeliveryCargo spawnedCargo;
    public GameObject pickupMarker;
    public GameObject destinationMarker;
    public DeliveryDestinationZone destinationZone;
    
    // Minimap 相關
    public int colorIndex = -1; // 0, 1, 2 分別代表三種不同顏色；-1 表示未分配
    public GameObject pickupMinimapMarker;
    public GameObject destinationMinimapMarker;
}

public class DeliveryGameManager : MonoBehaviour
{
    [System.Serializable]
    public class FoodOption
    {
        public string foodName = "Burger";
        public int maxHealth = 100;
        public DeliveryCargo cargoPrefab;
    }

    public static DeliveryGameManager Instance { get; private set; }

    [Header("玩家")]
    public PlayerDeliveryCarrier playerCarrier;

    [Header("遊戲時間")]
    public float gameDuration = 180f;
    public bool startOnAwake = false;

    [Header("新訂單生成設定")]
    [Tooltip("每隔多少秒自動派發一筆新訂單到等待區")]
    public float orderSpawnInterval = 20f; 
    public int maxWaitingOrders = 3; // 畫面/UI上最多同時顯示幾筆等待接受的訂單

    [Header("計分")]
    public int brokenFoodPenalty = 50;
    public int wrongFoodPenalty = 50;
    [Tooltip("每外送一秒扣除的分數（可調整外送時間懲罰）")]
    public float timePenaltyPerSecond = 1f; 

    [Header("取餐點 / 目的地")]
    public Transform[] pickupPoints;
    public Transform[] destinationPoints;

    [Header("餐點設定")]
    public FoodOption[] foodOptions;
    public DeliveryCargo fallbackCargoPrefab;

    [Header("Marker / Zone Prefab")]
    public GameObject pickupMarkerPrefab;
    public GameObject destinationMarkerPrefab;
    public DeliveryDestinationZone destinationZonePrefab;

    [Header("Minimap Marker Prefab (三種顏色)")]
    [Tooltip("三組不同顏色的 Minimap 標記 Prefab，分別對應顏色 0、1、2")]
    public GameObject[] minimapMarkerPrefabs = new GameObject[3];
    public GameObject[] minimapDestinationPrefabs = new GameObject[3];

    [Header("搶無人機餐點 / 送錯餐點設定")]
    public bool allowPickupAnyCargo = true;
    public bool correctCargoByFoodName = true;

    [Header("UI")]
    public Text timerText;
    public Text scoreText;
    public Text orderText; // 現在可能需要顯示當前外送中的訂單資訊
    public Text cargoHealthText;
    public Text messageText;

    // 核心資料結構
    private List<DeliveryOrder> allOrders = new List<DeliveryOrder>();
    private int orderIdCounter = 0;
    private float orderSpawnTimer;
    
    // 追蹤目前已使用的顏色索引（用來確保同時出現的訂單顏色不同）
    private List<int> usedColorIndices = new List<int>();

    private float remainingTime;
    private int score = 0;
    private bool gameActive = false;

    // 玩家目前正在執行的外送訂單（同一時間通常只有一筆，或依你設計而定）
    private DeliveryOrder currentActiveOrder;

    // 提供給外部 UI 讀取的公開屬性
    public int Score => score;
    public bool GameActive => gameActive;
    public List<DeliveryOrder> AllOrders => allOrders;
    public DeliveryOrder CurrentActiveOrder => currentActiveOrder;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (startOnAwake) StartGame();
        else
        {
            remainingTime = gameDuration;
            UpdateUI();
        }
    }

    void Update()
    {
        if (!gameActive)
        {
            UpdateUI();
            return;
        }

        // 1. 遊戲主時間倒數
        remainingTime -= Time.deltaTime;
        if (remainingTime <= 0f)
        {
            remainingTime = 0f;
            EndGame();
            return;
        }

        // 2. 定期生成新訂單並放入佇列
        orderSpawnTimer -= Time.deltaTime;
        if (orderSpawnTimer <= 0f)
        {
            orderSpawnTimer = orderSpawnInterval;
            TryQueueNewOrder();
        }

        // 3. 更新所有訂單的時間與生命週期
        UpdateOrdersLifecycle();

        UpdateUI();
    }

    public void StartGame()
    {
        score = 0;
        remainingTime = gameDuration;
        orderSpawnTimer = 2f; // 遊戲開始 2 秒後立刻來第一單
        orderIdCounter = 0;
        gameActive = true;
        currentActiveOrder = null;
        usedColorIndices.Clear(); // 重置已使用的顏色索引

        ClearAllOrdersObjects();
        allOrders.Clear();

        SetMessage("Delivery Start!");
        UpdateUI();
    }

    public void EndGame()
    {
        gameActive = false;
        ClearAllOrdersObjects();

        if (playerCarrier != null)
        {
            playerCarrier.RemoveCarriedCargoWithoutScoring();
        }

        SetMessage("Time Up! Final Score: " + score);
        UpdateUI();
    }

    #region 訂單生成與生命週期

    void TryQueueNewOrder()
    {
        if (pickupPoints == null || pickupPoints.Length == 0 || destinationPoints == null || destinationPoints.Length == 0)
        {
            SetMessage("Points missing.");
            return;
        }

        // 計算目前處於 "等待被接受" 的訂單數量
        int waitingCount = allOrders.FindAll(o => o.state == OrderState.WaitingAccept).Count;
        
        orderIdCounter++;
        DeliveryOrder newOrder = new DeliveryOrder
        {
            orderId = orderIdCounter,
            food = ChooseRandomFood(),
            pickupPoint = ChooseRandomPoint(pickupPoints),
            destinationPoint = ChooseRandomPoint(destinationPoints)
        };

        // 如果等待區還沒滿，直接進入等待接受階段；滿了就先待在 Queue 中
        if (waitingCount < maxWaitingOrders)
        {
            newOrder.state = OrderState.WaitingAccept;
            SetMessage("New Order Available: " + newOrder.food.foodName);
        }
        else
        {
            newOrder.state = OrderState.InQueue;
        }

        allOrders.Add(newOrder);
        // 通知你的 UI 系統更新訂單列表
        NotifyUIOrderListChanged(); 
    }

    void UpdateOrdersLifecycle()
    {
        List<DeliveryOrder> toRemove = new List<DeliveryOrder>();

        for (int i = 0; i < allOrders.Count; i++)
        {
            DeliveryOrder order = allOrders[i];

            if (order.state == OrderState.WaitingAccept)
            {
                // 15秒倒數
                order.waitingTimer -= Time.deltaTime;
                if (order.waitingTimer <= 0f)
                {
                    DiscardOrder(order);
                }
            }
            else if (order.state == OrderState.Active)
            {
                // 外送時間增加（可加到負數，時間越長扣越多分）
                order.activeTimer += Time.deltaTime;
            }
        }

        // 檢查是否有 InQueue 的訂單可以補進 Waiting 區
        int currentWaiting = allOrders.FindAll(o => o.state == OrderState.WaitingAccept).Count;
        if (currentWaiting < maxWaitingOrders)
        {
            DeliveryOrder nextInQueue = allOrders.Find(o => o.state == OrderState.InQueue);
            if (nextInQueue != null)
            {
                nextInQueue.state = OrderState.WaitingAccept;
                SetMessage("New Order Available: " + nextInQueue.food.foodName);
                NotifyUIOrderListChanged();
            }
        }
    }

    #endregion

    #region 玩家互動介面 (UI 呼叫用)

    // 提供給你的 UI 按鈕呼叫：接受訂單
    public void AcceptOrder(int orderId)
    {
        if (!gameActive) return;

        DeliveryOrder order = allOrders.Find(o => o.orderId == orderId);
        if (order == null || order.state != OrderState.WaitingAccept) return;

        // 如果你希望玩家一次只能送一單，可以把這行取消註解：
        // if (currentActiveOrder != null) { SetMessage("You already have an active delivery!"); return; }

        order.state = OrderState.Active;
        currentActiveOrder = order; // 設定為當前主要追蹤訂單
        
        // 為訂單分配顏色
        order.colorIndex = GetAvailableColorIndex();

        // 接受後才在世界上生成實體物件！
        SpawnOrderWorldObjects(order);

        SetMessage("Accepted Order: " + order.food.foodName);
        NotifyUIOrderListChanged();
    }

    // 提供給你的 UI 按鈕呼叫：拒絕/丟棄訂單
    public void DiscardOrder(DeliveryOrder order)
    {
        if (order == null) return;
        
        order.state = OrderState.Discarded;
        ClearSingleOrderObjects(order);
        
        if (currentActiveOrder == order) currentActiveOrder = null;
        
        allOrders.Remove(order); // 從清單移除
        SetMessage("Order " + order.orderId + " Discarded.");
        NotifyUIOrderListChanged();
    }

    #endregion

    #region 世界物件生成與清理

    void SpawnOrderWorldObjects(DeliveryOrder order)
    {
        if (order.pickupPoint == null || order.destinationPoint == null) return;

        // 1. 生成餐點 Cargo
        DeliveryCargo prefab = (order.food.cargoPrefab != null) ? order.food.cargoPrefab : fallbackCargoPrefab;
        if (prefab != null)
        {
            order.spawnedCargo = Instantiate(prefab, order.pickupPoint.position, order.pickupPoint.rotation);
            order.spawnedCargo.InitializeForOrder(order.food.foodName, order.food.maxHealth, order.orderId);
        }

        // 2. 生成取餐 Marker
        if (pickupMarkerPrefab != null)
        {
            order.pickupMarker = Instantiate(pickupMarkerPrefab, order.pickupPoint.position, order.pickupPoint.rotation);
        }

        // 3. 生成目的地 Marker
        if (destinationMarkerPrefab != null)
        {
            order.destinationMarker = Instantiate(destinationMarkerPrefab, order.destinationPoint.position, order.destinationPoint.rotation);
        }

        // 4. 生成目的地 Zone
        if (destinationZonePrefab != null)
        {
            order.destinationZone = Instantiate(destinationZonePrefab, order.destinationPoint.position, order.destinationPoint.rotation);
            order.destinationZone.Initialize(order.orderId);
        }
        
        // 5. 生成 Minimap Marker（根據分配的顏色索引）
        SpawnMinimapMarkers(order);
    }
    
    void SpawnMinimapMarkers(DeliveryOrder order)
    {
        if (order.colorIndex < 0 || order.colorIndex >= minimapMarkerPrefabs.Length) return;
        
        GameObject prefabM = minimapMarkerPrefabs[order.colorIndex];
        GameObject prefabD = minimapDestinationPrefabs[order.colorIndex];
        if (prefabM == null || prefabD == null) return;
        
        // 在取餐點生成 Minimap 標記
        order.pickupMinimapMarker = Instantiate(prefabM, order.pickupPoint.position, order.pickupPoint.rotation);
        order.pickupMinimapMarker.name = $"MinimapMarker_Pickup_{order.orderId}";
        
        // 在目的地生成 Minimap 標記
        order.destinationMinimapMarker = Instantiate(prefabD, order.destinationPoint.position, order.destinationPoint.rotation);
        order.destinationMinimapMarker.name = $"MinimapMarker_Destination_{order.orderId}";
    }

    void ClearSingleOrderObjects(DeliveryOrder order)
    {
        if (order.spawnedCargo != null) Destroy(order.spawnedCargo.gameObject);
        if (order.pickupMarker != null) Destroy(order.pickupMarker);
        if (order.destinationMarker != null) Destroy(order.destinationMarker);
        if (order.destinationZone != null) Destroy(order.destinationZone.gameObject);
        
        // 清除 Minimap 標記
        if (order.pickupMinimapMarker != null) Destroy(order.pickupMinimapMarker);
        if (order.destinationMinimapMarker != null) Destroy(order.destinationMinimapMarker);
        
        // 釋放使用的顏色索引
        if (order.colorIndex >= 0 && usedColorIndices.Contains(order.colorIndex))
        {
            usedColorIndices.Remove(order.colorIndex);
        }
    }

    void ClearAllOrdersObjects()
    {
        foreach (var order in allOrders)
        {
            ClearSingleOrderObjects(order);
        }
    }

    #endregion

    #region 外送判斷與完成

    public bool CanPickupCargo(DeliveryCargo cargo)
    {
        if (!gameActive || cargo == null || !cargo.canBeDelivered) return false;
        if (allowPickupAnyCargo) return true;

        // 檢查是否是玩家已接受的訂單
        return currentActiveOrder != null && IsCargoCorrectForOrder(cargo, currentActiveOrder);
    }

    public bool CanCompleteDelivery(DeliveryCargo cargo, int destinationOrderId)
    {
        if (!gameActive || cargo == null) return false;
        
        // 尋找世界上對應這筆 destinationZone 的訂單
        DeliveryOrder order = allOrders.Find(o => o.orderId == destinationOrderId);
        if (order == null || order.state != OrderState.Active) return false;

        return true;
    }

    public bool IsCargoCorrectForOrder(DeliveryCargo cargo, DeliveryOrder order)
    {
        if (cargo == null || order == null) return false;
        if (cargo.OrderId == order.orderId) return true;

        if (correctCargoByFoodName && cargo.FoodName == order.food.foodName) return true;

        return false;
    }

    public void CompleteDelivery(DeliveryCargo cargo, PlayerDeliveryCarrier carrier)
    {
        if (!gameActive || cargo == null) return;

        // 尋找此餐點對應的 Active 訂單
        DeliveryOrder order = allOrders.Find(o => o.orderId == cargo.OrderId);
        
        // 如果找不到精確 ID，且允許用名字識別，就找名字相符的 Active 訂單
        if (order == null && correctCargoByFoodName)
        {
            order = allOrders.Find(o => o.state == OrderState.Active && o.food.foodName == cargo.FoodName);
        }

        if (order == null)
        {
            // 完全送錯
            score -= wrongFoodPenalty;
            SetMessage("Wrong Food Delivered! -" + wrongFoodPenalty + " pts");
        }
        else
        {
            int hp = cargo.CurrentHealth;
            if (hp <= 0)
            {
                score -= brokenFoodPenalty;
                SetMessage("Food Destroyed! -" + brokenFoodPenalty + " pts");
            }
            else
            {
                // 分數計算：血量(HP) - (花費時間 * 時間懲罰)
                // 時間越多，扣分越多。可以扣到負數
                int timePenalty = Mathf.FloorToInt(order.activeTimer * timePenaltyPerSecond);
                int finalPoints = hp - timePenalty;

                score += finalPoints;
                SetMessage($"Success! {order.food.foodName} HP:{hp} Time Penalty:-{timePenalty}. Final: {(finalPoints >= 0 ? "+" : "")}{finalPoints} pts");
            }

            order.state = OrderState.Completed;
            ClearSingleOrderObjects(order);
            if (currentActiveOrder == order) currentActiveOrder = null;
            allOrders.Remove(order);
        }

        Destroy(cargo.gameObject);
        NotifyUIOrderListChanged();
        UpdateUI();
    }

    #endregion

    #region 玩家狀態通知

    public void NotifyCargoPicked(DeliveryCargo cargo) { SetMessage("Picked Up: " + cargo.FoodName); UpdateUI(); }
    public void NotifyCargoStored(DeliveryCargo cargo) { SetMessage("Stored: " + cargo.FoodName); UpdateUI(); }
    public void NotifyCargoTakenOut(DeliveryCargo cargo) { SetMessage("Taken Out: " + cargo.FoodName); UpdateUI(); }
    public void NotifyCargoDropped(DeliveryCargo cargo) { SetMessage("Cargo Dropped"); UpdateUI(); }
    public void NotifyCargoHealthChanged(DeliveryCargo cargo) { SetMessage("Cargo Damaged! HP: " + cargo.CurrentHealth); UpdateUI(); }
    public void NotifyCargoMessage(string message) { SetMessage(message); UpdateUI(); }

    #endregion

    #region UI 刷新與輔助

    void UpdateUI()
    {
        if (timerText != null)
        {
            int seconds = Mathf.CeilToInt(remainingTime);
            timerText.text = $"{seconds / 60:00}:{seconds % 60:00}";
        }

        if (scoreText != null) scoreText.text = "Score: " + score;

        if (orderText != null)
        {
            if (currentActiveOrder != null)
            {
                // 顯示當前外送進度與時間
                orderText.text = $"Active: {currentActiveOrder.food.foodName} ({currentActiveOrder.activeTimer:F1}s)";
            }
            else
            {
                orderText.text = "No Active Order";
            }
        }

        if (cargoHealthText != null)
        {
            DeliveryCargo displayCargo = (playerCarrier != null) ? playerCarrier.CarriedCargo : null;
            if (displayCargo != null)
            {
                string storageText = (playerCarrier != null && playerCarrier.IsCargoStored) ? " (Stored)" : "";
                cargoHealthText.text = $"Food HP: {displayCargo.CurrentHealth} / {displayCargo.MaxHealth}{storageText}";
            }
            else
            {
                cargoHealthText.text = "Food HP: -";
            }
        }
    }

    void SetMessage(string msg)
    {
        if (messageText != null) messageText.text = msg;
    }

    // 當訂單佇列有變動時（新增、超時、接受），呼叫此處通知你的UI
    void NotifyUIOrderListChanged()
    {
        // TODO: 在這裡呼叫你的 UI 腳本，讓它重新繪製待接單列表
        // 例如： MyUIManager.Instance.RefreshOrderList(allOrders);
        // ➔ 【整合：尋找場景中的 WristUIController 並刷新】
        WristUIController wristUI = FindFirstObjectByType<WristUIController>();
        if (wristUI == null) wristUI = FindObjectOfType<WristUIController>();
        
        if (wristUI != null)
        {
            wristUI.RefreshOrderList();
        }
    }

    FoodOption ChooseRandomFood()
    {
        if (foodOptions != null && foodOptions.Length > 0)
        {
            FoodOption food = foodOptions[Random.Range(0, foodOptions.Length)];
            if (food != null) return food;
        }
        FoodOption fallback = new FoodOption { foodName = "Meal", maxHealth = 100, cargoPrefab = fallbackCargoPrefab };
        return fallback;
    }

    Transform ChooseRandomPoint(Transform[] points)
    {
        if (points == null || points.Length == 0) return null;
        for (int i = 0; i < 20; i++)
        {
            Transform candidate = points[Random.Range(0, points.Length)];
            if (candidate != null) return candidate;
        }
        return points[0];
    }

    // 為新接受的訂單分配未使用的顏色索引（確保同時進行的訂單顏色不重複）
    int GetAvailableColorIndex()
    {
        // 遍歷三種顏色索引，找出未被使用的
        for (int i = 0; i < 3; i++)
        {
            if (!usedColorIndices.Contains(i))
            {
                usedColorIndices.Add(i);
                return i;
            }
        }
        // 如果三種顏色都被使用了，就循環回到第一種（不應該發生，除非同時超過 3 筆訂單）
        return usedColorIndices.Count > 0 ? usedColorIndices[0] : 0;
    }

    #endregion
}