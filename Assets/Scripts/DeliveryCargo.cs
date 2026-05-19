using UnityEngine;

public class DeliveryCargo : MonoBehaviour
{
    [Header("餐點資料")]
    public string foodName = "Burger";
    public int maxHealth = 100;

    [SerializeField]
    private int currentHealth = 100;

    [Header("外送狀態")]
    public bool canBeDelivered = true;

    [Tooltip("被玩家拿著或收起時，是否關閉 Collider，避免爬牆 / 擺盪時卡牆")]
    public bool disableCollidersWhileCarried = true;

    private int orderId = -1;
    private bool isCarried = false;

    private Rigidbody rb;
    private Collider[] colliders;

    public int CurrentHealth
    {
        get { return currentHealth; }
    }

    public int MaxHealth
    {
        get { return maxHealth; }
    }

    public int OrderId
    {
        get { return orderId; }
    }

    public bool IsCarried
    {
        get { return isCarried; }
    }

    public string FoodName
    {
        get { return foodName; }
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        colliders = GetComponentsInChildren<Collider>();

        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
    }

    public void InitializeForOrder(string newFoodName, int newMaxHealth, int newOrderId)
    {
        foodName = newFoodName;
        maxHealth = Mathf.Max(1, newMaxHealth);
        currentHealth = maxHealth;
        orderId = newOrderId;
        canBeDelivered = true;
    }

    public void InitializeAsFreeCargo(string newFoodName, int newMaxHealth)
    {
        foodName = newFoodName;
        maxHealth = Mathf.Max(1, newMaxHealth);
        currentHealth = maxHealth;
        orderId = -1;
        canBeDelivered = true;
    }

    public void TakeDamage(int damage)
    {
        if (damage <= 0)
        {
            return;
        }

        currentHealth -= damage;

        if (currentHealth < 0)
        {
            currentHealth = 0;
        }
    }

    public void HealToFull()
    {
        currentHealth = maxHealth;
    }

    public void AttachTo(Transform holdAnchor)
    {
        if (holdAnchor == null)
        {
            return;
        }

        isCarried = true;

        transform.SetParent(holdAnchor, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (disableCollidersWhileCarried)
        {
            SetCollidersEnabled(false);
        }
    }

    public void Drop(Vector3 worldPosition, Vector3 throwVelocity)
    {
        isCarried = false;

        transform.SetParent(null, true);
        transform.position = worldPosition;

        SetCollidersEnabled(true);

        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.velocity = throwVelocity;
            rb.angularVelocity = Vector3.zero;
        }
    }

    public void DetachWithoutPhysics()
    {
        isCarried = false;
        transform.SetParent(null, true);
        SetCollidersEnabled(true);
    }

    void SetCollidersEnabled(bool enabled)
    {
        if (colliders == null)
        {
            return;
        }

        foreach (Collider col in colliders)
        {
            if (col != null)
            {
                col.enabled = enabled;
            }
        }
    }
}