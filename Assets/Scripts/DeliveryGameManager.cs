using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public enum OrderState
{
    InQueue,      // 排程中（等待輪到它）
    WaitingAccept,// 等待玩家接受（15秒倒數）
    Active,       // 玩家已接受（開始外送倒數）
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
    public float activeTimer = 0f;    // 接受後的外送【倒數】時間（從 N 分鐘開始往下降）

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
        public int foodValue = 100;       // 食物價值
        [Tooltip("外送限時 N (分鐘)")]
        public float maxDeliveryTime = 2f; // 外送限時 N 分鐘
        public DeliveryCargo cargoPrefab;
        [Tooltip("此食物專屬的取餐點（點位與食物綁定）")]
        public Transform restaurantPickupPoint; 
    }

    public static DeliveryGameManager Instance { get; private set; }

    [Header("玩家")]
    public PlayerDeliveryCarrier playerCarrier;

    [Header("遊戲時間")]
    public float gameDuration = 180f;
    public int goalScore = 100;
    public bool startOnAwake = false;
    public MainSceneController mainSceneController;

    [Header("新訂單生成設定")]
    [Tooltip("每隔多少秒自動派發一筆新訂單到等待區")]
    public float orderSpawnInterval = 120f; 
    public int maxWaitingOrders = 3; // 畫面/UI上最多同時顯示幾筆等待接受的訂單

    [Header("計分與懲罰")]
    public int brokenFoodPenalty = 50;
    public int wrongFoodPenalty = 50;

    [Header("目的地")]
    public Transform[] destinationPoints;

    [Header("餐點設定")]
    public FoodOption[] foodOptions;
    public DeliveryCargo fallbackCargoPrefab;
    [Tooltip("當食物未綁定地點時的後備取餐點")]
    public Transform fallbackPickupPoint;

    [Header("Marker / Zone Prefab")]
    public GameObject pickupMarkerPrefab;
    public GameObject destinationMarkerPrefab;
    public DeliveryDestinationZone destinationZonePrefab;

    [Header("Minimap Marker Prefab (三種顏色)")]
    public GameObject[] minimapMarkerPrefabs = new GameObject[3];
    public GameObject[] minimapDestinationPrefabs = new GameObject[3];
    
    [Header("訂單預覽(Preview)設定")]
    [Tooltip("懸停或查看訂單時，在取餐點（餐廳）生成的預覽特效或標記")]
    public GameObject pickupPreviewPrefab;
    [Tooltip("懸停或查看訂單時，在目的地生成的預覽特效或標記")]
    public GameObject destinationPreviewPrefab;
    public float MiniMapHeightOffset = 20f;
    // 用來記錄目前正在場景上顯示的預覽實體
    private GameObject currentPickupPreviewInstance;
    private GameObject currentDestinationPreviewInstance;

    [Header("搶無人機餐點 / 送錯餐點設定")]
    public bool allowPickupAnyCargo = true;
    public bool correctCargoByFoodName = true;

    [Header("UI")]
    public Text timerText;
    public Text scoreText;
    public Text orderText; 
    public Text cargoHealthText;
    public Text messageText;

    [Header("取餐標記的垂直高度偏移量（公尺）")]
    public float pickupMarkerHeightOffset = 1.5f;

    // 核心資料結構
    private List<DeliveryOrder> allOrders = new List<DeliveryOrder>();
    private int orderIdCounter = 0;
    private float orderSpawnTimer;
    
    private List<int> usedColorIndices = new List<int>();

    private float remainingTime;
    private int score = 0;
    private bool gameActive = false;

    private DeliveryOrder currentActiveOrder;

    public int Score => score;
    public bool GameActive => gameActive;
    public List<DeliveryOrder> AllOrders => allOrders;
    public DeliveryOrder CurrentActiveOrder => currentActiveOrder;

    [Header("中央音效系統")]
    [Tooltip("請將掛在 GameManager 物件上的 AudioSource 拖入此處")]
    public AudioSource globalAudioSource;

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

        remainingTime -= Time.deltaTime;
        if (remainingTime <= 0f)
        {
            remainingTime = 0f;
            EndGame();
            return;
        }

        orderSpawnTimer -= Time.deltaTime;
        if (orderSpawnTimer <= 0f)
        {
            orderSpawnTimer = orderSpawnInterval;
            TryQueueNewOrder();
        }

        UpdateOrdersLifecycle();
        UpdateUI();
    }

    public void StartGame()
    {
        score = 0;
        remainingTime = gameDuration;
        orderSpawnTimer = 2f; 
        orderIdCounter = 0;
        gameActive = true;
        currentActiveOrder = null;
        usedColorIndices.Clear();

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

        if (score >= goalScore)
        {
            mainSceneController.TriggerSceneTransition(true);
        }
        else
        {
            mainSceneController.TriggerSceneTransition(false);
        }
    }

    #region 訂單生成與生命週期

    void TryQueueNewOrder()
    {
        if (foodOptions == null || foodOptions.Length == 0 || destinationPoints == null || destinationPoints.Length == 0)
        {
            SetMessage("Setup missing (Food options or Destination points).");
            return;
        }

        FoodOption selectedFood = null;
        Transform selectedDestination = null;
        Transform selectedPickup = null;
        
        bool isUnique = false;
        int maxAttempts = 30; // 稍微提高最大嘗試次數，確保能篩選出空閒地點
        int attempts = 0;

        while (!isUnique && attempts < maxAttempts)
        {
            attempts++;
            selectedFood = ChooseRandomFood();
            selectedDestination = ChooseRandomPoint(destinationPoints);
            selectedPickup = (selectedFood.restaurantPickupPoint != null) ? selectedFood.restaurantPickupPoint : fallbackPickupPoint;

            if (selectedPickup == null) continue;

            // ➔ 【條件 1】：檢查是否已有「相同食物且相同目的地」的未結算訂單（避免一模一樣的單）
            bool duplicateExists = allOrders.Exists(o => 
                o.food.foodName == selectedFood.foodName && 
                o.destinationPoint == selectedDestination &&
                o.state != OrderState.Completed && 
                o.state != OrderState.Discarded
            );

            if (duplicateExists) continue;

            // ➔ 【條件 2】：檢查該取餐點（Pickup Point）目前是否正被其他「等待接單」或「進行中」的訂單佔用
            bool pickupPointOccupied = allOrders.Exists(o =>
                o.pickupPoint == selectedPickup &&
                (o.state == OrderState.WaitingAccept || o.state == OrderState.Active)
            );

            // 只有在目的地不重複，且取餐點完全沒被佔用時，才算是一筆有效的獨特新訂單
            if (!pickupPointOccupied)
            {
                isUnique = true;
            }
        }

        // 如果場景上所有店家的取餐點都被佔滿了，本次暫不生成訂單，留到下個週期再試
        if (!isUnique)
        {
            Debug.LogWarning("所有取餐點目前都有進行中的訂單，暫緩生成新訂單。");
            return;
        }

        int waitingCount = allOrders.FindAll(o => o.state == OrderState.WaitingAccept).Count;
        
        orderIdCounter++;
        DeliveryOrder newOrder = new DeliveryOrder
        {
            orderId = orderIdCounter,
            food = selectedFood,
            pickupPoint = selectedPickup,
            destinationPoint = selectedDestination,
            activeTimer = selectedFood.maxDeliveryTime * 60f 
        };

        // 雖然這個地點目前是空的，但如果玩家手腕畫面的「待接單格子」滿了 (maxWaitingOrders)，就得先乖乖在 InQueue 排隊
        if (waitingCount < maxWaitingOrders)
        {
            newOrder.state = OrderState.WaitingAccept;
            SetMessage("New Order Available: " + newOrder.food.foodName);
            if (OrderNotificationController.Instance != null)
                OrderNotificationController.Instance.TriggerNotification();
        }
        else
        {
            newOrder.state = OrderState.InQueue;
        }

        allOrders.Add(newOrder);
        NotifyUIOrderListChanged(); 
    }

    void UpdateOrdersLifecycle()
    {
        for (int i = 0; i < allOrders.Count; i++)
        {
            DeliveryOrder order = allOrders[i];

            if (order.state == OrderState.WaitingAccept)
            {
                order.waitingTimer -= Time.deltaTime;
                if (order.waitingTimer <= 0f)
                {
                    DiscardOrder(order);
                    i--; 
                }
            }
            else if (order.state == OrderState.Active)
            {
                order.activeTimer -= Time.deltaTime;
                if (order.activeTimer <= 0f)
                {
                    order.activeTimer = 0f;
                    SetMessage($"Order {order.orderId} Timeout! Delivery Failed.");
                    DiscardOrder(order);
                    i--; 
                }
            }
        }

        // 當有舊訂單消失，釋放出 WaitingAccept 的格子時
        int currentWaiting = allOrders.FindAll(o => o.state == OrderState.WaitingAccept).Count;
        if (currentWaiting < maxWaitingOrders)
        {
            // 從 InQueue 佇列中尋找下一筆排隊的訂單
            // 這裡會自動遞補，且因為進入 InQueue 前已經驗證過當時地點沒被佔用，因此可以直接遞補
            DeliveryOrder nextInQueue = allOrders.Find(o => o.state == OrderState.InQueue);
            if (nextInQueue != null)
            {
                // 【完美防呆】：遞補時再次確認該地點現在有沒有被別的 Active 訂單霸佔（例如別家店逾時釋放，但這家店剛好有人在送）
                bool isPointStillFree = !allOrders.Exists(o => o.pickupPoint == nextInQueue.pickupPoint && (o.state == OrderState.WaitingAccept || o.state == OrderState.Active));
                
                if (isPointStillFree)
                {
                    nextInQueue.state = OrderState.WaitingAccept;
                    SetMessage("New Order Available: " + nextInQueue.food.foodName);
                    NotifyUIOrderListChanged();
                }
            }
        }
    }

    #endregion

    #region 玩家互動介面 (UI 呼叫用)

    public void AcceptOrder(int orderId)
    {
        if (!gameActive) return;

        DeliveryOrder order = allOrders.Find(o => o.orderId == orderId);
        if (order == null || order.state != OrderState.WaitingAccept) return;

        order.state = OrderState.Active;
        currentActiveOrder = order; 
        order.colorIndex = GetAvailableColorIndex();

        SpawnOrderWorldObjects(order);

        SetMessage("Accepted Order: " + order.food.foodName);
        NotifyUIOrderListChanged();
    }

    public void DiscardOrder(DeliveryOrder order)
    {
        if (order == null) return;
        
        order.state = OrderState.Discarded;
        ClearSingleOrderObjects(order);
        
        if (currentActiveOrder == order) currentActiveOrder = null;
        
        allOrders.Remove(order); 
        NotifyUIOrderListChanged();
    }

    #endregion

    #region 世界物件生成與清理

    void SpawnOrderWorldObjects(DeliveryOrder order)
    {
        if (order.pickupPoint == null || order.destinationPoint == null) return;

        DeliveryCargo prefab = (order.food.cargoPrefab != null) ? order.food.cargoPrefab : fallbackCargoPrefab;
        if (prefab != null)
        {
            order.spawnedCargo = Instantiate(prefab, order.pickupPoint.position, order.pickupPoint.rotation);
            order.spawnedCargo.InitializeForOrder(order.food.foodName, order.food.maxHealth, order.orderId);
        }

        if (pickupMarkerPrefab != null)
        {
            Vector3 spawnPosition = order.pickupPoint.position + new Vector3(0f, pickupMarkerHeightOffset, 0f);
            order.pickupMarker = Instantiate(pickupMarkerPrefab, spawnPosition, order.pickupPoint.rotation);
        }

        // if (destinationMarkerPrefab != null)
        // {
        //     order.destinationMarker = Instantiate(destinationMarkerPrefab, order.destinationPoint.position, order.destinationPoint.rotation);
        // }

        if (destinationZonePrefab != null)
        {
            Vector3 spawnPosition = order.destinationPoint.position + new Vector3(0f, 0f, 0f);
            order.destinationZone = Instantiate(destinationZonePrefab, spawnPosition, order.destinationPoint.rotation);
            order.destinationZone.Initialize(order.orderId);
        }
        
        SpawnMinimapMarkers(order);
    }
    
    void SpawnMinimapMarkers(DeliveryOrder order)
    {
        if (order.colorIndex < 0 || order.colorIndex >= minimapMarkerPrefabs.Length) return;
        
        GameObject prefabM = minimapMarkerPrefabs[order.colorIndex];
        GameObject prefabD = minimapDestinationPrefabs[order.colorIndex];
        if (prefabM == null || prefabD == null) return;
        
        Vector3 spawnPosition = order.pickupPoint.position + new Vector3(0f, MiniMapHeightOffset, 0f);
        order.pickupMinimapMarker = Instantiate(prefabM, spawnPosition, Quaternion.Euler(0f, 180f, 0f));
        order.pickupMinimapMarker.name = $"MinimapMarker_Pickup_{order.orderId}";
        
        spawnPosition = order.destinationPoint.position + new Vector3(0f, MiniMapHeightOffset, 0f);
        order.destinationMinimapMarker = Instantiate(prefabD, spawnPosition,  Quaternion.Euler(0f, 180f, 0f));
        order.destinationMinimapMarker.name = $"MinimapMarker_Destination_{order.orderId}";
    }

    void ClearSingleOrderObjects(DeliveryOrder order)
    {
        if (order.spawnedCargo != null) Destroy(order.spawnedCargo.gameObject);
        if (order.pickupMarker != null) Destroy(order.pickupMarker);
        if (order.destinationMarker != null) Destroy(order.destinationMarker);
        if (order.destinationZone != null) Destroy(order.destinationZone.gameObject);
        
        if (order.pickupMinimapMarker != null) Destroy(order.pickupMinimapMarker);
        if (order.destinationMinimapMarker != null) Destroy(order.destinationMinimapMarker);
        
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

        return currentActiveOrder != null && IsCargoCorrectForOrder(cargo, currentActiveOrder);
    }

    public bool CanCompleteDelivery(DeliveryCargo cargo, int destinationOrderId)
    {
        if (!gameActive || cargo == null) return false;
        
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

        DeliveryOrder order = allOrders.Find(o => o.orderId == cargo.OrderId);
        
        if (order == null && correctCargoByFoodName)
        {
            order = allOrders.Find(o => o.state == OrderState.Active && o.food.foodName == cargo.FoodName);
        }

        if (order == null)
        {
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
                float maxHp = order.food.maxHealth;
                float maxTimeInSeconds = order.food.maxDeliveryTime * 60f;

                float hpRatio = (maxHp > 0) ? (hp / maxHp) : 0f;
                float timeRatio = (maxTimeInSeconds > 0) ? (order.activeTimer / maxTimeInSeconds) : 0f;

                float totalRatio = (hpRatio * 0.7f) + (timeRatio * 0.3f);
                int earnedPoints = Mathf.RoundToInt(totalRatio * order.food.foodValue);

                score += earnedPoints;
                SetMessage($"Success! {order.food.foodName} | HP:{hpRatio:P0} TimeLeft:{timeRatio:P0} | Earned: +{earnedPoints} pts");
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

    #region 外部呼叫接口 (提供給手機、網路或外部工具連線使用)

    // ➔ 【核心修正】：必須在這裡重新定義這個結構，否則編譯器會找不到型態
    [System.Serializable]
    public struct ExternalOrderRequest
    {
        public string foodName;         // 例如: "Burger", "Pizza"
        public int destinationIndex;    // 目的地的陣列索引 (0, 1, 2...)
    }

    public bool AddOrderFromExternal(ExternalOrderRequest request)
    {
        if (!gameActive) return false;

        FoodOption targetFood = System.Array.Find(foodOptions, f => f.foodName.Equals(request.foodName, System.StringComparison.OrdinalIgnoreCase));
        if (targetFood == null) return false;

        if (destinationPoints == null || request.destinationIndex < 0 || request.destinationIndex >= destinationPoints.Length) return false;
        Transform targetDestination = destinationPoints[request.destinationIndex];

        Transform targetPickup = (targetFood.restaurantPickupPoint != null) ? targetFood.restaurantPickupPoint : fallbackPickupPoint;
        if (targetPickup == null) return false;

        // 外部呼叫時也一併遵守這兩條安全過濾機制
        bool duplicateExists = allOrders.Exists(o => 
            o.food.foodName == targetFood.foodName && 
            o.destinationPoint == targetDestination &&
            o.state != OrderState.Completed && 
            o.state != OrderState.Discarded
        );
        if (duplicateExists) return false;

        bool pickupPointOccupied = allOrders.Exists(o =>
            o.pickupPoint == targetPickup &&
            (o.state == OrderState.WaitingAccept || o.state == OrderState.Active)
        );
        if (pickupPointOccupied) return false;

        int waitingCount = allOrders.FindAll(o => o.state == OrderState.WaitingAccept).Count;
        orderIdCounter++;

        DeliveryOrder newOrder = new DeliveryOrder
        {
            orderId = orderIdCounter,
            food = targetFood,
            pickupPoint = targetPickup,
            destinationPoint = targetDestination,
            activeTimer = targetFood.maxDeliveryTime * 60f 
        };

        if (waitingCount < maxWaitingOrders)
        {
            newOrder.state = OrderState.WaitingAccept;
            SetMessage("External Order Arrived: " + newOrder.food.foodName);
            if (OrderNotificationController.Instance != null)
                OrderNotificationController.Instance.TriggerNotification();
        }
        else
        {
            newOrder.state = OrderState.InQueue;
        }

        allOrders.Add(newOrder);
        NotifyUIOrderListChanged();
        UpdateUI();
        return true;
    }

    #endregion

    #region UI 刷新與輔助

    void UpdateUI()
    {
        if (timerText != null)
        {
            int seconds = Mathf.CeilToInt(remainingTime);
            timerText.text = $"Time: {seconds / 60:00}:{seconds % 60:00}";
        }

        if (scoreText != null) 
        {
            scoreText.text = "Score: " + score;
        }

        if (orderText != null)
        {
            if (currentActiveOrder != null)
            {
                int currentCountdown = Mathf.CeilToInt(currentActiveOrder.activeTimer);
                orderText.text = $"Active: {currentActiveOrder.food.foodName}\n" +
                                 $"Value: ${currentActiveOrder.food.foodValue} | " +
                                 $"Countdown: {currentCountdown / 60:00}:{currentCountdown % 60:00} / (Limit: {currentActiveOrder.food.maxDeliveryTime:F1}m)";
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

    void NotifyUIOrderListChanged()
    {
        WristUIController wristUI = FindFirstObjectByType<WristUIController>();
        if (wristUI == null) wristUI = FindObjectOfType<WristUIController>();
        
        if (wristUI != null) wristUI.RefreshOrderList();
    }

    FoodOption ChooseRandomFood()
    {
        if (foodOptions != null && foodOptions.Length > 0)
        {
            FoodOption food = foodOptions[Random.Range(0, foodOptions.Length)];
            if (food != null) return food;
        }
        FoodOption fallback = new FoodOption { foodName = "Meal", maxHealth = 100, foodValue = 100, maxDeliveryTime = 2f, cargoPrefab = fallbackCargoPrefab };
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

    int GetAvailableColorIndex()
    {
        for (int i = 0; i < 3; i++)
        {
            if (!usedColorIndices.Contains(i))
            {
                usedColorIndices.Add(i);
                return i;
            }
        }
        return usedColorIndices.Count > 0 ? usedColorIndices[0] : 0;
    }

    #endregion

    #region 音效

    public void PlaySound(AudioClip clip, float volumeScale = 1f)
    {
        if (globalAudioSource != null && clip != null)
        {
            globalAudioSource.PlayOneShot(clip, volumeScale);
        }
    }

    #endregion

    #region 訂單位置預覽功能 (提供給 WristUIController 呼叫)

    /// <summary>
    /// 顯示指定訂單的起點與終點預覽標記
    /// </summary>
    /// <param name="orderId">要預覽的訂單 ID</param>
    public void ShowOrderLocationPreview(int orderId)
    {
        // 先清理舊的預覽，避免重複生成
        ClearOrderLocationPreview();

        // 尋找對應的訂單
        DeliveryOrder order = allOrders.Find(o => o.orderId == orderId);
        if (order == null) return;

        // 生成取餐點預覽
        if (pickupPreviewPrefab != null && order.pickupPoint != null)
        {
            // 配合你原本的起點高度偏移量，讓預覽也對齊相同高度
            Vector3 spawnPosition = order.pickupPoint.position + new Vector3(0f, MiniMapHeightOffset, 0f);
            currentPickupPreviewInstance = Instantiate(pickupPreviewPrefab, spawnPosition, Quaternion.Euler(0f, 180f, 0f));
        }

        // 生成目的地預覽
        if (destinationPreviewPrefab != null && order.destinationPoint != null)
        {
            Vector3 spawnPosition = order.destinationPoint.position + new Vector3(0f, MiniMapHeightOffset, 0f);
            currentDestinationPreviewInstance = Instantiate(destinationPreviewPrefab, spawnPosition, Quaternion.Euler(0f, 180f, 0f));
        }
    }

    /// <summary>
    /// 清除目前場景上的所有訂單預覽標記
    /// </summary>
    public void ClearOrderLocationPreview()
    {
        if (currentPickupPreviewInstance != null)
        {
            Destroy(currentPickupPreviewInstance);
            currentPickupPreviewInstance = null;
        }

        if (currentDestinationPreviewInstance != null)
        {
            Destroy(currentDestinationPreviewInstance);
            currentDestinationPreviewInstance = null;
        }
    }

    #endregion
}