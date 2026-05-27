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
    public bool startOnAwake = false;

    [Header("新訂單生成設定")]
    [Tooltip("每隔多少秒自動派發一筆新訂單到等待區")]
    public float orderSpawnInterval = 20f; 
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

    [Header("搶無人機餐點 / 送錯餐點設定")]
    public bool allowPickupAnyCargo = true;
    public bool correctCargoByFoodName = true;

    [Header("UI")]
    public Text timerText;
    public Text scoreText;
    public Text orderText; 
    public Text cargoHealthText;
    public Text messageText;

    [Tooltip("取餐標記的垂直高度偏移量（公尺）")]
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
    
    [Tooltip("成功送達時的加分音效")]

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
        int maxAttempts = 20; 
        int attempts = 0;

        while (!isUnique && attempts < maxAttempts)
        {
            attempts++;
            selectedFood = ChooseRandomFood();
            selectedDestination = ChooseRandomPoint(destinationPoints);
            selectedPickup = (selectedFood.restaurantPickupPoint != null) ? selectedFood.restaurantPickupPoint : fallbackPickupPoint;

            if (selectedPickup == null) continue;

            bool duplicateExists = allOrders.Exists(o => 
                o.food.foodName == selectedFood.foodName && 
                o.destinationPoint == selectedDestination &&
                o.state != OrderState.Completed && 
                o.state != OrderState.Discarded
            );

            if (!duplicateExists) isUnique = true;
        }

        if (!isUnique) return;

        int waitingCount = allOrders.FindAll(o => o.state == OrderState.WaitingAccept).Count;
        
        orderIdCounter++;
        DeliveryOrder newOrder = new DeliveryOrder
        {
            orderId = orderIdCounter,
            food = selectedFood,
            pickupPoint = selectedPickup,
            destinationPoint = selectedDestination,
            // 初始外送倒數時間 = N 分鐘 * 60 秒
            activeTimer = selectedFood.maxDeliveryTime * 60f 
        };

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
                    i--; // 元素被移除，索引校正
                }
            }
            else if (order.state == OrderState.Active)
            {
                // 外送時間【倒數】機制
                order.activeTimer -= Time.deltaTime;
                if (order.activeTimer <= 0f)
                {
                    order.activeTimer = 0f;
                    SetMessage($"Order {order.orderId} Timeout! Delivery Failed.");
                    DiscardOrder(order);
                    i--; // 元素被移除，索引校正
                }
            }
        }

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

        if (destinationMarkerPrefab != null)
        {
            order.destinationMarker = Instantiate(destinationMarkerPrefab, order.destinationPoint.position, order.destinationPoint.rotation);
        }

        if (destinationZonePrefab != null)
        {
            order.destinationZone = Instantiate(destinationZonePrefab, order.destinationPoint.position, order.destinationPoint.rotation);
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
        
        order.pickupMinimapMarker = Instantiate(prefabM, order.pickupPoint.position, order.pickupPoint.rotation);
        order.pickupMinimapMarker.name = $"MinimapMarker_Pickup_{order.orderId}";
        
        order.destinationMinimapMarker = Instantiate(prefabD, order.destinationPoint.position, order.destinationPoint.rotation);
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
                // 【全新計分公式實作】
                // (食物血量/食物總血量 * 70% + 剩餘時間 / N * 30%) * 食物價值
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

    [System.Serializable]
    public struct ExternalOrderRequest
    {
        public string foodName;         
        public int destinationIndex;    
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

        bool duplicateExists = allOrders.Exists(o => 
            o.food.foodName == targetFood.foodName && 
            o.destinationPoint == targetDestination &&
            o.state != OrderState.Completed && 
            o.state != OrderState.Discarded
        );

        if (duplicateExists) return false;

        int waitingCount = allOrders.FindAll(o => o.state == OrderState.WaitingAccept).Count;
        orderIdCounter++;

        DeliveryOrder newOrder = new DeliveryOrder
        {
            orderId = orderIdCounter,
            food = targetFood,
            pickupPoint = targetPickup,
            destinationPoint = targetDestination,
            activeTimer = targetFood.maxDeliveryTime * 60f // 設定外部訂單的初始限制倒數
        };

        if (waitingCount < maxWaitingOrders)
        {
            newOrder.state = OrderState.WaitingAccept;
            SetMessage("External Order Arrived: " + newOrder.food.foodName);
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
        // 1. 遊戲剩餘總時間
        if (timerText != null)
        {
            int seconds = Mathf.CeilToInt(remainingTime);
            timerText.text = $"Time: {seconds / 60:00}:{seconds % 60:00}";
        }

        // 2. 目前總得分 (除了 Time 以外必定顯示)
        if (scoreText != null) 
        {
            scoreText.text = "Score: " + score;
        }

        // 3. 正在執行訂單資訊 UI (包含物品金額、倒數時間、限制時間 N)
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

        // 4. 手持物品血量狀態
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
            // PlayOneShot 的好處是音效可以完美重疊，不會互相切斷
            globalAudioSource.PlayOneShot(clip, volumeScale);
        }
    }

    #endregion
}