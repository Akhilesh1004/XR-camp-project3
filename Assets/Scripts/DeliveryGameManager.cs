using UnityEngine;
using UnityEngine.UI;

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

    [Header("計分")]
    public int brokenFoodPenalty = 50;
    public int wrongFoodPenalty = 50;

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

    [Header("搶無人機餐點 / 送錯餐點設定")]
    [Tooltip("true = 玩家可以撿起任何 DeliveryCargo。送達時才判斷是否正確")]
    public bool allowPickupAnyCargo = true;

    [Tooltip("true = 只要 foodName 跟本次訂單相同，就算正確餐點")]
    public bool correctCargoByFoodName = true;

    [Header("UI")]
    public Text timerText;
    public Text scoreText;
    public Text orderText;
    public Text cargoHealthText;
    public Text messageText;

    private float remainingTime;
    private int score = 0;
    private int currentOrderId = 0;

    private bool gameActive = false;

    private FoodOption currentFood;
    private Transform currentPickupPoint;
    private Transform currentDestinationPoint;

    private DeliveryCargo activePickupCargo;
    private GameObject activePickupMarker;
    private GameObject activeDestinationMarker;
    private DeliveryDestinationZone activeDestinationZone;

    public int Score
    {
        get { return score; }
    }

    public int CurrentOrderId
    {
        get { return currentOrderId; }
    }

    public bool GameActive
    {
        get { return gameActive; }
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (startOnAwake)
        {
            StartGame();
        }
        else
        {
            remainingTime = gameDuration;
            UpdateUI(null);
        }
    }

    void Update()
    {
        if (!gameActive)
        {
            UpdateUI(null);
            return;
        }

        remainingTime -= Time.deltaTime;

        if (remainingTime <= 0f)
        {
            remainingTime = 0f;
            EndGame();
        }

        UpdateUI(null);
    }

    public void StartGame()
    {
        score = 0;
        remainingTime = gameDuration;
        gameActive = true;

        ClearCurrentOrderObjects();
        CreateNewOrder();

        SetMessage("Delivery Start!");
        UpdateUI(null);
    }

    public void EndGame()
    {
        gameActive = false;

        ClearCurrentOrderObjects();

        if (playerCarrier != null)
        {
            playerCarrier.RemoveCarriedCargoWithoutScoring();
        }

        SetMessage("Time Up! Final Score: " + score);
        UpdateUI(null);
    }

    void CreateNewOrder()
    {
        if (!gameActive)
        {
            return;
        }

        if (pickupPoints == null || pickupPoints.Length == 0)
        {
            SetMessage("No pickup points set.");
            return;
        }

        if (destinationPoints == null || destinationPoints.Length == 0)
        {
            SetMessage("No destination points set.");
            return;
        }

        currentOrderId++;

        currentFood = ChooseRandomFood();
        currentPickupPoint = ChooseRandomPoint(pickupPoints);
        currentDestinationPoint = ChooseRandomPoint(destinationPoints);

        SpawnPickupCargo();
        SpawnDestination();

        SetMessage("New Order: " + currentFood.foodName);
        UpdateUI(null);
    }

    FoodOption ChooseRandomFood()
    {
        if (foodOptions != null && foodOptions.Length > 0)
        {
            FoodOption food = foodOptions[Random.Range(0, foodOptions.Length)];

            if (food != null)
            {
                return food;
            }
        }

        FoodOption fallback = new FoodOption();
        fallback.foodName = "Meal";
        fallback.maxHealth = 100;
        fallback.cargoPrefab = fallbackCargoPrefab;
        return fallback;
    }

    Transform ChooseRandomPoint(Transform[] points)
    {
        if (points == null || points.Length == 0)
        {
            return null;
        }

        for (int i = 0; i < 20; i++)
        {
            Transform candidate = points[Random.Range(0, points.Length)];

            if (candidate != null)
            {
                return candidate;
            }
        }

        return points[0];
    }

    void SpawnPickupCargo()
    {
        if (currentPickupPoint == null)
        {
            return;
        }

        DeliveryCargo prefab = null;

        if (currentFood != null && currentFood.cargoPrefab != null)
        {
            prefab = currentFood.cargoPrefab;
        }
        else
        {
            prefab = fallbackCargoPrefab;
        }

        if (prefab != null)
        {
            activePickupCargo = Instantiate(
                prefab,
                currentPickupPoint.position,
                currentPickupPoint.rotation
            );

            activePickupCargo.InitializeForOrder(
                currentFood.foodName,
                currentFood.maxHealth,
                currentOrderId
            );
        }

        if (pickupMarkerPrefab != null)
        {
            activePickupMarker = Instantiate(
                pickupMarkerPrefab,
                currentPickupPoint.position,
                currentPickupPoint.rotation
            );
        }
    }

    void SpawnDestination()
    {
        if (currentDestinationPoint == null)
        {
            return;
        }

        if (destinationMarkerPrefab != null)
        {
            activeDestinationMarker = Instantiate(
                destinationMarkerPrefab,
                currentDestinationPoint.position,
                currentDestinationPoint.rotation
            );
        }

        if (destinationZonePrefab != null)
        {
            activeDestinationZone = Instantiate(
                destinationZonePrefab,
                currentDestinationPoint.position,
                currentDestinationPoint.rotation
            );

            activeDestinationZone.Initialize(currentOrderId);
        }
    }

    public bool CanPickupCargo(DeliveryCargo cargo)
    {
        if (!gameActive)
        {
            return false;
        }

        if (cargo == null)
        {
            return false;
        }

        if (!cargo.canBeDelivered)
        {
            return false;
        }

        if (allowPickupAnyCargo)
        {
            return true;
        }

        return IsCargoCorrectForCurrentOrder(cargo);
    }

    public bool CanCompleteDelivery(DeliveryCargo cargo, int destinationOrderId)
    {
        if (!gameActive)
        {
            return false;
        }

        if (cargo == null)
        {
            return false;
        }

        if (destinationOrderId != currentOrderId)
        {
            return false;
        }

        return true;
    }

    public bool IsCargoCorrectForCurrentOrder(DeliveryCargo cargo)
    {
        if (cargo == null)
        {
            return false;
        }

        if (cargo.OrderId == currentOrderId)
        {
            return true;
        }

        if (correctCargoByFoodName &&
            currentFood != null &&
            cargo.FoodName == currentFood.foodName)
        {
            return true;
        }

        return false;
    }

    public void NotifyCargoPicked(DeliveryCargo cargo)
    {
        if (cargo == null)
        {
            return;
        }

        SetMessage("Picked Up: " + cargo.FoodName);
        UpdateUI(cargo);
    }

    public void NotifyCargoStored(DeliveryCargo cargo)
    {
        if (cargo == null)
        {
            return;
        }

        SetMessage("Stored: " + cargo.FoodName);
        UpdateUI(cargo);
    }

    public void NotifyCargoTakenOut(DeliveryCargo cargo)
    {
        if (cargo == null)
        {
            return;
        }

        SetMessage("Taken Out: " + cargo.FoodName);
        UpdateUI(cargo);
    }

    public void NotifyCargoDropped(DeliveryCargo cargo)
    {
        SetMessage("Cargo Dropped");
        UpdateUI(cargo);
    }

    public void NotifyCargoHealthChanged(DeliveryCargo cargo)
    {
        if (cargo == null)
        {
            return;
        }

        SetMessage("Cargo Damaged! HP: " + cargo.CurrentHealth);
        UpdateUI(cargo);
    }

    public void NotifyCargoMessage(string message)
    {
        SetMessage(message);
        UpdateUI(null);
    }

    public void CompleteDelivery(DeliveryCargo cargo, PlayerDeliveryCarrier carrier)
    {
        if (!gameActive)
        {
            return;
        }

        if (cargo == null)
        {
            return;
        }

        bool isCorrectCargo = IsCargoCorrectForCurrentOrder(cargo);
        int hp = cargo.CurrentHealth;

        if (!isCorrectCargo)
        {
            score -= wrongFoodPenalty;

            string requiredName = currentFood != null
                ? currentFood.foodName
                : "Unknown";

            SetMessage(
                "Wrong Food! Need " +
                requiredName +
                ", but delivered " +
                cargo.FoodName +
                ". -" +
                wrongFoodPenalty +
                " pts"
            );
        }
        else if (hp <= 0)
        {
            score -= brokenFoodPenalty;

            SetMessage(
                "Food Destroyed! -" +
                brokenFoodPenalty +
                " pts"
            );
        }
        else
        {
            score += hp;

            SetMessage(
                "Delivery Success! " +
                cargo.FoodName +
                " HP: " +
                hp +
                ", +" +
                hp +
                " pts"
            );
        }

        Destroy(cargo.gameObject);

        ClearCurrentOrderObjects();
        CreateNewOrder();

        UpdateUI(null);
    }

    void ClearCurrentOrderObjects()
    {
        if (activePickupCargo != null)
        {
            Destroy(activePickupCargo.gameObject);
            activePickupCargo = null;
        }

        if (activePickupMarker != null)
        {
            Destroy(activePickupMarker);
            activePickupMarker = null;
        }

        if (activeDestinationMarker != null)
        {
            Destroy(activeDestinationMarker);
            activeDestinationMarker = null;
        }

        if (activeDestinationZone != null)
        {
            Destroy(activeDestinationZone.gameObject);
            activeDestinationZone = null;
        }
    }

    void UpdateUI(DeliveryCargo carriedCargo)
    {
        if (timerText != null)
        {
            int seconds = Mathf.CeilToInt(remainingTime);
            int min = seconds / 60;
            int sec = seconds % 60;

            timerText.text = min.ToString("00") + ":" + sec.ToString("00");
        }

        if (scoreText != null)
        {
            scoreText.text = "Score: " + score;
        }

        if (orderText != null)
        {
            if (currentFood != null &&
                currentPickupPoint != null &&
                currentDestinationPoint != null)
            {
                orderText.text = "Order: " + currentFood.foodName;
            }
            else
            {
                orderText.text = "Order: -";
            }
        }

        if (cargoHealthText != null)
        {
            DeliveryCargo displayCargo = carriedCargo;

            if (displayCargo == null &&
                playerCarrier != null)
            {
                displayCargo = playerCarrier.CarriedCargo;
            }

            if (displayCargo != null)
            {
                string storageText = "";

                if (playerCarrier != null &&
                    playerCarrier.IsCargoStored)
                {
                    storageText = " (Stored)";
                }

                cargoHealthText.text =
                    "Food HP: " +
                    displayCargo.CurrentHealth +
                    " / " +
                    displayCargo.MaxHealth +
                    storageText;
            }
            else
            {
                cargoHealthText.text = "Food HP: -";
            }
        }
    }

    void SetMessage(string msg)
    {
        if (messageText != null)
        {
            messageText.text = msg;
        }
    }
}