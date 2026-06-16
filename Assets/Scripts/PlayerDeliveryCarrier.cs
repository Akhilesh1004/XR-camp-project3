using UnityEngine;

public class PlayerDeliveryCarrier : MonoBehaviour
{
    [Header("攜帶設定")]
    [Tooltip("餐點拿出來時的位置，例如胸前、手上、腰前")]
    public Transform holdAnchor;

    [Tooltip("餐點收起來時的位置。可以放在玩家底下 HiddenCargoStorage")]
    public Transform storageAnchor;

    [Tooltip("收起餐點時是否直接隱藏模型")]
    public bool hideCargoWhenStored = true;

    [Header("拿取顯示位置")]
    [Tooltip("餐點拿在手上時，相對 holdAnchor 的位置。用來避免模型卡在手心或控制器裡")]
    public Vector3 heldLocalPosition = new Vector3(0f, 0.08f, 0.12f);

    [Tooltip("餐點拿在手上時，相對 holdAnchor 的旋轉")]
    public Vector3 heldLocalEulerAngles = Vector3.zero;

    [Tooltip("餐點拿在手上時的縮放倍率。丟下或解除攜帶時會恢復原本大小")]
    [Range(0.05f, 1f)]
    public float heldScaleMultiplier = 0.35f;

    [Tooltip("餐點收起來時，相對 storageAnchor 的位置")]
    public Vector3 storedLocalPosition = Vector3.zero;

    [Tooltip("餐點收起來時，相對 storageAnchor 的旋轉")]
    public Vector3 storedLocalEulerAngles = Vector3.zero;

    [Tooltip("餐點收起來時的縮放倍率。通常跟 heldScaleMultiplier 一樣即可")]
    [Range(0.05f, 1f)]
    public float storedScaleMultiplier = 0.35f;

    [Tooltip("可以撿餐點的 Layer")]
    public LayerMask cargoLayer;

    public float pickupRadius = 1.2f;

    [Header("輸入設定")]
    [Tooltip("右手中指鍵：沒有餐點時撿餐點；有餐點且拿在外面時丟下餐點")]
    public OVRInput.Controller pickupDropController = OVRInput.Controller.RTouch;

    public OVRInput.Button pickupDropButton = OVRInput.Button.PrimaryHandTrigger;

    [Tooltip("右手蘑菇頭按下：收起 / 拿出餐點")]
    public OVRInput.Controller storageController = OVRInput.Controller.RTouch;

    public OVRInput.Button toggleStorageButton = OVRInput.Button.PrimaryThumbstick;

    [Header("自動撿取")]
    public bool autoPickupOnTouch = false;

    [Header("丟下設定")]
    [Tooltip("是否允許按右手中指鍵丟下餐點")]
    public bool allowDrop = true;

    [Tooltip("餐點收起來時是否禁止丟下。建議 true")]
    public bool preventDropWhenStored = true;

    public Transform dropPoint;
    public float dropForwardSpeed = 1.5f;

    private DeliveryCargo carriedCargo;
    private bool isCargoStored = false;

    public bool HasCargo
    {
        get { return carriedCargo != null; }
    }

    public bool IsCargoStored
    {
        get { return isCargoStored; }
    }

    public DeliveryCargo CarriedCargo
    {
        get { return carriedCargo; }
    }

    void Update()
    {
        if (OVRInput.GetDown(pickupDropButton, pickupDropController))
        {
            HandlePickupDropButton();
        }

        if (carriedCargo != null &&
            OVRInput.GetDown(toggleStorageButton, storageController))
        {
            ToggleCargoStorage();
        }
    }

    void LateUpdate()
    {
        if (carriedCargo != null)
        {
            carriedCargo.SnapToCarriedAnchor();
        }
    }

    void HandlePickupDropButton()
    {
        if (carriedCargo == null)
        {
            TryPickupNearbyCargo();
            return;
        }

        if (isCargoStored && preventDropWhenStored)
        {
            if (DeliveryGameManager.Instance != null)
            {
                DeliveryGameManager.Instance.NotifyCargoMessage(
                    "Cargo is stored. Press right thumbstick to take it out."
                );
            }

            return;
        }

        if (allowDrop)
        {
            DropCargo();
        }
    }

    public bool TryPickupCargo(DeliveryCargo cargo)
    {
        if (cargo == null)
        {
            return false;
        }

        if (carriedCargo != null)
        {
            return false;
        }

        if (!cargo.canBeDelivered)
        {
            return false;
        }

        if (DeliveryGameManager.Instance != null)
        {
            if (!DeliveryGameManager.Instance.CanPickupCargo(cargo))
            {
                return false;
            }
        }

        carriedCargo = cargo;

        isCargoStored = false;

        Transform anchor = holdAnchor != null ? holdAnchor : transform;

        AttachCargoToAnchor(
            carriedCargo,
            anchor,
            heldLocalPosition,
            heldLocalEulerAngles,
            heldScaleMultiplier
        );
        SetCargoVisible(true);

        if (DeliveryGameManager.Instance != null)
        {
            DeliveryGameManager.Instance.NotifyCargoPicked(carriedCargo);
        }

        return true;
    }

    public void TryPickupNearbyCargo()
    {
        if (carriedCargo != null)
        {
            return;
        }

        Collider[] hits = Physics.OverlapSphere(
            transform.position,
            pickupRadius,
            cargoLayer,
            QueryTriggerInteraction.Collide
        );

        DeliveryCargo bestCargo = null;
        float bestDistance = float.MaxValue;

        foreach (Collider hit in hits)
        {
            DeliveryCargo cargo = hit.GetComponentInParent<DeliveryCargo>();

            if (cargo == null)
            {
                continue;
            }

            if (cargo.IsCarried)
            {
                continue;
            }

            if (DeliveryGameManager.Instance != null &&
                !DeliveryGameManager.Instance.CanPickupCargo(cargo))
            {
                continue;
            }

            float distance = Vector3.Distance(
                transform.position,
                cargo.transform.position
            );

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestCargo = cargo;
            }
        }

        if (bestCargo != null)
        {
            TryPickupCargo(bestCargo);
        }
    }

    public void ToggleCargoStorage()
    {
        if (carriedCargo == null)
        {
            return;
        }

        if (isCargoStored)
        {
            TakeOutCargo();
        }
        else
        {
            StoreCargo();
        }
    }

    public void StoreCargo()
    {
        if (carriedCargo == null)
        {
            return;
        }

        isCargoStored = true;

        Transform anchor = storageAnchor != null
            ? storageAnchor
            : transform;

        AttachCargoToAnchor(
            carriedCargo,
            anchor,
            storedLocalPosition,
            storedLocalEulerAngles,
            storedScaleMultiplier
        );

        if (hideCargoWhenStored)
        {
            SetCargoVisible(false);
        }
        else
        {
            SetCargoVisible(true);
        }

        if (DeliveryGameManager.Instance != null)
        {
            DeliveryGameManager.Instance.NotifyCargoStored(carriedCargo);
        }
    }

    public void TakeOutCargo()
    {
        if (carriedCargo == null)
        {
            return;
        }

        isCargoStored = false;

        Transform anchor = holdAnchor != null
            ? holdAnchor
            : transform;

        AttachCargoToAnchor(
            carriedCargo,
            anchor,
            heldLocalPosition,
            heldLocalEulerAngles,
            heldScaleMultiplier
        );
        SetCargoVisible(true);

        if (DeliveryGameManager.Instance != null)
        {
            DeliveryGameManager.Instance.NotifyCargoTakenOut(carriedCargo);
        }
    }

    void SetCargoVisible(bool visible)
    {
        if (visible && carriedCargo != null)
        {
            carriedCargo.SetCarriedVisible(true);
            return;
        }

        if (carriedCargo != null)
        {
            carriedCargo.SetCarriedVisible(false);
        }
    }

    void AttachCargoToAnchor(
        DeliveryCargo cargo,
        Transform anchor,
        Vector3 localPosition,
        Vector3 localEulerAngles,
        float scaleMultiplier)
    {
        if (cargo == null || anchor == null)
        {
            return;
        }

        cargo.AttachTo(
            anchor,
            localPosition,
            Quaternion.Euler(localEulerAngles),
            scaleMultiplier
        );
    }

    public void DamageCarriedCargo(int damage)
    {
        if (carriedCargo == null)
        {
            return;
        }

        carriedCargo.TakeDamage(damage);

        if (DeliveryGameManager.Instance != null)
        {
            DeliveryGameManager.Instance.NotifyCargoHealthChanged(carriedCargo);
        }
    }

    public void CompleteCurrentDelivery(int destinationOrderId = -1)
    {
        if (carriedCargo == null)
        {
            return;
        }

        DeliveryCargo deliveredCargo = carriedCargo;
        bool wasCargoStored = isCargoStored;

        carriedCargo = null;
        isCargoStored = false;

        if (DeliveryGameManager.Instance != null)
        {
            bool completed = DeliveryGameManager.Instance.CompleteDelivery(
                deliveredCargo,
                this,
                destinationOrderId
            );

            if (!completed)
            {
                carriedCargo = deliveredCargo;
                isCargoStored = wasCargoStored;
                DeliveryGameManager.Instance.NotifyCargoMessage(
                    "Wrong Food Delivered! No score change."
                );
            }

            return;
        }

        Destroy(deliveredCargo.gameObject);
    }

    public void ClearCargoIfMatch(DeliveryCargo cargo)
    {
        if (carriedCargo == cargo)
        {
            carriedCargo = null;
            isCargoStored = false;
        }
    }

    public void RemoveCarriedCargoWithoutScoring()
    {
        if (carriedCargo == null)
        {
            return;
        }

        DeliveryCargo cargo = carriedCargo;

        carriedCargo = null;
        isCargoStored = false;

        Destroy(cargo.gameObject);
    }

    public void DropCargo()
    {
        if (carriedCargo == null)
        {
            return;
        }

        if (isCargoStored && preventDropWhenStored)
        {
            return;
        }

        DeliveryCargo cargo = carriedCargo;

        SetCargoVisible(true);

        carriedCargo = null;
        isCargoStored = false;

        Transform point = dropPoint != null
            ? dropPoint
            : transform;

        Vector3 velocity = point.forward * dropForwardSpeed;

        cargo.Drop(point.position, velocity);

        if (DeliveryGameManager.Instance != null)
        {
            DeliveryGameManager.Instance.NotifyCargoDropped(cargo);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (!autoPickupOnTouch)
        {
            return;
        }

        if (carriedCargo != null)
        {
            return;
        }

        DeliveryCargo cargo = other.GetComponentInParent<DeliveryCargo>();

        if (cargo != null)
        {
            TryPickupCargo(cargo);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, pickupRadius);
    }
}
