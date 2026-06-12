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

    [Header("手上對齊")]
    [Tooltip("指定餐點被拿在手上時要對齊 holdAnchor 的點。若未指定，會自動找 CarryAnchor/HoldAnchor/GrabAnchor。")]
    public Transform carryAnchor;

    [Tooltip("開啟時會嘗試用 carryAnchor 或 Renderer bounds 修正手上位置。若要完全固定在 holdAnchor，請保持關閉。")]
    public bool alignVisibleBoundsCenterWhenCarried = false;

    [Tooltip("若 prefab 內有這些名稱的子物件，會優先拿來當手上對齊點。")]
    public string[] autoCarryAnchorNames = { "CarryAnchor", "HoldAnchor", "GrabAnchor" };

    [Tooltip("通常建議關閉，避免餐點曾經掛在 drone 或其他縮放父物件底下時，拿到手上大小被錯誤保留。")]
    public bool preserveWorldScaleWhenCarried = false;

    [Header("餐點外觀切換")]
    [Tooltip("正常狀態外觀。不指定時會自動使用目前 active 的第一個直屬子物件")]
    public GameObject normalVisualRoot;

    [Tooltip("受損狀態外觀。不指定時會自動使用目前 inactive 的第一個直屬子物件")]
    public GameObject damagedVisualRoot;

    [Tooltip("血量低於最大血量的這個比例時，切換到受損外觀")]
    [Range(0.01f, 0.99f)]
    public float damagedVisualHealthRatio = 0.5f;

    private int orderId = -1;
    private bool isCarried = false;
    private bool showingDamagedVisual = false;
    private bool hasCarriedScale = false;
    private Vector3 carriedScale = Vector3.one;
    private float carriedScaleMultiplier = 1f;

    private Transform carriedAnchor;
    private Vector3 carriedLocalPosition;
    private Quaternion carriedLocalRotation = Quaternion.identity;

    private Rigidbody[] rigidbodies;
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
        rigidbodies = GetComponentsInChildren<Rigidbody>(true);
        colliders = GetComponentsInChildren<Collider>(true);

        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        AutoBindVisualRoots();
        AutoBindCarryAnchor();
        RefreshDamageVisual();
    }

    public void InitializeForOrder(string newFoodName, int newMaxHealth, int newOrderId)
    {
        foodName = newFoodName;
        maxHealth = Mathf.Max(1, newMaxHealth);
        currentHealth = maxHealth;
        orderId = newOrderId;
        canBeDelivered = true;
        RefreshDamageVisual();
    }

    public void InitializeAsFreeCargo(string newFoodName, int newMaxHealth)
    {
        foodName = newFoodName;
        maxHealth = Mathf.Max(1, newMaxHealth);
        currentHealth = maxHealth;
        orderId = -1;
        canBeDelivered = true;
        RefreshDamageVisual();
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

        RefreshDamageVisual();
    }

    public void HealToFull()
    {
        currentHealth = maxHealth;
        RefreshDamageVisual();
    }

    public void AttachTo(Transform holdAnchor)
    {
        AttachTo(holdAnchor, Vector3.zero, Quaternion.identity);
    }

    public void AttachTo(
        Transform holdAnchor,
        Vector3 localPosition,
        Quaternion localRotation)
    {
        AttachTo(holdAnchor, localPosition, localRotation, 1f);
    }

    public void AttachTo(
        Transform holdAnchor,
        Vector3 localPosition,
        Quaternion localRotation,
        float scaleMultiplier)
    {
        if (holdAnchor == null)
        {
            return;
        }

        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        if (!isCarried || !hasCarriedScale)
        {
            carriedScale = preserveWorldScaleWhenCarried
                ? transform.lossyScale
                : transform.localScale;
            hasCarriedScale = true;
        }

        isCarried = true;
        carriedAnchor = holdAnchor;
        carriedLocalPosition = alignVisibleBoundsCenterWhenCarried
            ? GetAlignedLocalPosition(localPosition, localRotation)
            : localPosition;
        carriedLocalRotation = localRotation;
        carriedScaleMultiplier = Mathf.Max(0.01f, scaleMultiplier);

        SetRigidbodiesCarried(true, Vector3.zero);

        transform.SetParent(holdAnchor, false);
        transform.localRotation = localRotation;
        transform.localScale = GetCarriedLocalScale(holdAnchor);
        SnapToCarriedAnchor();

        if (disableCollidersWhileCarried)
        {
            SetCollidersEnabled(false);
        }
    }

    public void Drop(Vector3 worldPosition, Vector3 throwVelocity)
    {
        isCarried = false;
        hasCarriedScale = false;
        carriedAnchor = null;

        transform.SetParent(null, true);
        transform.position = worldPosition;
        RestoreReleasedScale();
        gameObject.SetActive(true);
        SetRenderersEnabled(true);

        SetCollidersEnabled(true);
        SetRigidbodiesCarried(false, throwVelocity);
    }

    public void DetachWithoutPhysics()
    {
        isCarried = false;
        hasCarriedScale = false;
        carriedAnchor = null;
        transform.SetParent(null, true);
        RestoreReleasedScale();
        gameObject.SetActive(true);
        SetRenderersEnabled(true);
        SetCollidersEnabled(true);
        SetRigidbodiesCarried(false, Vector3.zero);
    }

    public void SetCarriedVisible(bool visible)
    {
        if (visible && !gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        SetRenderersEnabled(visible);
    }

    public void SnapToCarriedAnchor()
    {
        if (!isCarried || carriedAnchor == null)
        {
            return;
        }

        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        SetRigidbodiesCarried(true, Vector3.zero);

        if (transform.parent != carriedAnchor)
        {
            transform.SetParent(carriedAnchor, false);
        }

        transform.localScale = GetCarriedLocalScale(carriedAnchor);
        transform.SetLocalPositionAndRotation(
            carriedLocalPosition,
            carriedLocalRotation
        );
    }

    void SetRenderersEnabled(bool enabled)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

        foreach (Renderer r in renderers)
        {
            if (r != null)
            {
                r.enabled = enabled;
            }
        }
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

    void SetRigidbodiesCarried(bool carried, Vector3 releaseVelocity)
    {
        if (rigidbodies == null)
        {
            return;
        }

        foreach (Rigidbody body in rigidbodies)
        {
            if (body == null)
            {
                continue;
            }

            body.isKinematic = carried;
            body.useGravity = !carried;
            body.velocity = carried ? Vector3.zero : releaseVelocity;
            body.angularVelocity = Vector3.zero;

            if (carried)
            {
                body.Sleep();
            }
        }
    }

    void AutoBindVisualRoots()
    {
        if (normalVisualRoot != null && damagedVisualRoot != null)
        {
            return;
        }

        Transform firstChild = null;
        Transform secondChild = null;
        Transform firstActiveChild = null;
        Transform firstInactiveChild = null;

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);

            if (child == null)
            {
                continue;
            }

            if (firstChild == null)
            {
                firstChild = child;
            }
            else if (secondChild == null)
            {
                secondChild = child;
            }

            if (child.gameObject.activeSelf)
            {
                if (firstActiveChild == null)
                {
                    firstActiveChild = child;
                }
            }
            else if (firstInactiveChild == null)
            {
                firstInactiveChild = child;
            }
        }

        if (normalVisualRoot == null)
        {
            normalVisualRoot = firstActiveChild != null
                ? firstActiveChild.gameObject
                : firstChild != null
                    ? firstChild.gameObject
                    : null;
        }

        if (damagedVisualRoot == null)
        {
            damagedVisualRoot = firstInactiveChild != null
                ? firstInactiveChild.gameObject
                : secondChild != null
                    ? secondChild.gameObject
                    : null;
        }
    }

    void AutoBindCarryAnchor()
    {
        if (carryAnchor != null ||
            autoCarryAnchorNames == null ||
            autoCarryAnchorNames.Length == 0)
        {
            return;
        }

        Transform[] children = GetComponentsInChildren<Transform>(true);

        foreach (string anchorName in autoCarryAnchorNames)
        {
            if (string.IsNullOrWhiteSpace(anchorName))
            {
                continue;
            }

            foreach (Transform child in children)
            {
                if (child != null && child.name == anchorName)
                {
                    carryAnchor = child;
                    return;
                }
            }
        }
    }

    Vector3 GetAlignedLocalPosition(Vector3 desiredLocalPosition, Quaternion localRotation)
    {
        if (carryAnchor != null && carryAnchor != transform)
        {
            Vector3 carryOffset = transform.InverseTransformPoint(carryAnchor.position);
            return desiredLocalPosition -
                   localRotation * Vector3.Scale(carryOffset, transform.localScale);
        }

        if (!alignVisibleBoundsCenterWhenCarried ||
            !TryGetVisibleRendererBounds(out Bounds bounds))
        {
            return desiredLocalPosition;
        }

        Vector3 centerOffset = transform.InverseTransformPoint(bounds.center);
        return desiredLocalPosition -
               localRotation * Vector3.Scale(centerOffset, transform.localScale);
    }

    bool TryGetVisibleRendererBounds(out Bounds bounds)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        bounds = new Bounds(transform.position, Vector3.zero);

        foreach (Renderer r in renderers)
        {
            if (r == null ||
                !r.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = r.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        return hasBounds;
    }

    void RefreshDamageVisual()
    {
        if (normalVisualRoot == null || damagedVisualRoot == null)
        {
            AutoBindVisualRoots();
        }

        if (normalVisualRoot == null ||
            damagedVisualRoot == null ||
            normalVisualRoot == damagedVisualRoot)
        {
            return;
        }

        bool shouldShowDamaged = currentHealth < maxHealth * damagedVisualHealthRatio;

        if (showingDamagedVisual == shouldShowDamaged &&
            normalVisualRoot.activeSelf == !shouldShowDamaged &&
            damagedVisualRoot.activeSelf == shouldShowDamaged)
        {
            return;
        }

        showingDamagedVisual = shouldShowDamaged;
        normalVisualRoot.SetActive(!shouldShowDamaged);
        damagedVisualRoot.SetActive(shouldShowDamaged);

        if (isCarried && carriedAnchor != null)
        {
            SnapToCarriedAnchor();
        }
    }

    Vector3 GetCarriedLocalScale(Transform parent)
    {
        Vector3 scaledCarriedScale = carriedScale * carriedScaleMultiplier;

        if (!preserveWorldScaleWhenCarried || parent == null)
        {
            return scaledCarriedScale;
        }

        return GetLocalScaleForWorldScale(scaledCarriedScale, parent.lossyScale);
    }

    void RestoreReleasedScale()
    {
        transform.localScale = carriedScale;
        carriedScaleMultiplier = 1f;
    }

    Vector3 GetLocalScaleForWorldScale(Vector3 worldScale, Vector3 parentWorldScale)
    {
        return new Vector3(
            SafeDivide(worldScale.x, parentWorldScale.x),
            SafeDivide(worldScale.y, parentWorldScale.y),
            SafeDivide(worldScale.z, parentWorldScale.z)
        );
    }

    float SafeDivide(float value, float divisor)
    {
        if (Mathf.Approximately(divisor, 0f))
        {
            return value;
        }

        return value / divisor;
    }
}
