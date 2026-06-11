using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class MinimapBlipController : MonoBehaviour
{
    [Header("References")]
    public RectTransform mapRect;
    public Transform player;
    public string playerTag = "Player";

    [Header("Update")]
    public float updatesPerSecond = 10f;

    [Header("Range")]
    [Tooltip("World units from player to the minimap edge.")]
    public float mapWorldRange = 180f;
    public float edgePadding = 4f;
    public bool rotateMapWithPlayer = false;

    [Header("Orders")]
    public bool showActiveOrders = true;
    public bool showWaitingOrders = false;
    public bool showPickupBlips = true;
    public bool showDestinationBlips = true;

    [Header("Visual")]
    public Vector2 pickupBlipSize = new Vector2(7f, 7f);
    public Vector2 destinationBlipSize = new Vector2(8f, 8f);
    public Vector2 edgeBlipScale = new Vector2(1.15f, 1.15f);
    public Color pickupTint = Color.white;
    public Color destinationTint = Color.white;
    public Color[] orderColors =
    {
        new Color(0.25f, 0.65f, 1f, 1f),
        new Color(0.2f, 1f, 0.45f, 1f),
        new Color(1f, 0.35f, 0.25f, 1f)
    };

    private readonly Dictionary<int, BlipPair> blipsByOrderId =
        new Dictionary<int, BlipPair>();

    private float nextUpdateTime;

    void Awake()
    {
        if (mapRect == null)
        {
            mapRect = GetComponent<RectTransform>();
        }

        FindPlayerIfNeeded();
    }

    void OnEnable()
    {
        nextUpdateTime = 0f;
    }

    void OnDisable()
    {
        ClearAllBlips();
    }

    void LateUpdate()
    {
        float interval = 1f / Mathf.Max(0.1f, updatesPerSecond);

        if (Time.unscaledTime < nextUpdateTime)
        {
            return;
        }

        nextUpdateTime = Time.unscaledTime + interval;
        RefreshBlips();
    }

    void RefreshBlips()
    {
        if (mapRect == null)
        {
            mapRect = GetComponent<RectTransform>();
        }

        FindPlayerIfNeeded();

        if (mapRect == null || player == null || DeliveryGameManager.Instance == null)
        {
            ClearAllBlips();
            return;
        }

        List<DeliveryOrder> orders = DeliveryGameManager.Instance.AllOrders;
        HashSet<int> visibleOrderIds = new HashSet<int>();

        foreach (DeliveryOrder order in orders)
        {
            if (order == null || !ShouldShowOrder(order))
            {
                continue;
            }

            visibleOrderIds.Add(order.orderId);

            if (!blipsByOrderId.TryGetValue(order.orderId, out BlipPair pair))
            {
                pair = CreateBlipPair(order);
                blipsByOrderId.Add(order.orderId, pair);
            }

            UpdateBlip(
                pair.pickup,
                order.pickupPoint,
                showPickupBlips,
                order.colorIndex,
                pickupTint,
                pickupBlipSize,
                0f
            );

            UpdateBlip(
                pair.destination,
                order.destinationPoint,
                showDestinationBlips,
                order.colorIndex,
                destinationTint,
                destinationBlipSize,
                45f
            );
        }

        RemoveHiddenOrders(visibleOrderIds);
    }

    bool ShouldShowOrder(DeliveryOrder order)
    {
        if (order.state == OrderState.Active)
        {
            return showActiveOrders;
        }

        if (order.state == OrderState.WaitingAccept)
        {
            return showWaitingOrders;
        }

        return false;
    }

    BlipPair CreateBlipPair(DeliveryOrder order)
    {
        BlipPair pair = new BlipPair
        {
            pickup = CreateBlip($"MinimapBlip_Pickup_{order.orderId}", pickupBlipSize, 0f),
            destination = CreateBlip($"MinimapBlip_Destination_{order.orderId}", destinationBlipSize, 45f)
        };

        return pair;
    }

    RectTransform CreateBlip(string name, Vector2 size, float zRotation)
    {
        GameObject blipObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        blipObject.layer = gameObject.layer;

        RectTransform rect = blipObject.GetComponent<RectTransform>();
        rect.SetParent(mapRect, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.localRotation = Quaternion.Euler(0f, 0f, zRotation);
        rect.localScale = Vector3.one;

        Image image = blipObject.GetComponent<Image>();
        image.raycastTarget = false;
        image.color = Color.white;

        return rect;
    }

    void UpdateBlip(
        RectTransform blip,
        Transform target,
        bool shouldShow,
        int colorIndex,
        Color tint,
        Vector2 baseSize,
        float baseRotation)
    {
        if (blip == null)
        {
            return;
        }

        bool visible = shouldShow && target != null;
        blip.gameObject.SetActive(visible);

        if (!visible)
        {
            return;
        }

        Vector2 normalizedPosition = GetNormalizedMapPosition(target.position, out bool isOutsideRange);
        Rect rect = mapRect.rect;
        float halfWidth = Mathf.Max(1f, rect.width * 0.5f - edgePadding);
        float halfHeight = Mathf.Max(1f, rect.height * 0.5f - edgePadding);

        blip.anchoredPosition = new Vector2(
            normalizedPosition.x * halfWidth,
            normalizedPosition.y * halfHeight
        );

        blip.sizeDelta = baseSize;
        blip.localScale = isOutsideRange
            ? new Vector3(edgeBlipScale.x, edgeBlipScale.y, 1f)
            : Vector3.one;
        blip.localRotation = Quaternion.Euler(0f, 0f, baseRotation);

        Image image = blip.GetComponent<Image>();

        if (image != null)
        {
            image.color = GetOrderColor(colorIndex) * tint;
        }
    }

    Vector2 GetNormalizedMapPosition(Vector3 worldPosition, out bool isOutsideRange)
    {
        Vector3 delta = worldPosition - player.position;
        Vector2 mapOffset;

        if (rotateMapWithPlayer)
        {
            Vector3 localDelta = Quaternion.Euler(0f, -player.eulerAngles.y, 0f) * delta;
            mapOffset = new Vector2(localDelta.x, localDelta.z);
        }
        else
        {
            mapOffset = new Vector2(delta.x, delta.z);
        }

        float range = Mathf.Max(1f, mapWorldRange);
        Vector2 normalized = mapOffset / range;
        float edgeScale = Mathf.Max(Mathf.Abs(normalized.x), Mathf.Abs(normalized.y));
        isOutsideRange = edgeScale > 1f;

        if (isOutsideRange)
        {
            normalized /= edgeScale;
        }

        return normalized;
    }

    Color GetOrderColor(int colorIndex)
    {
        if (orderColors != null &&
            colorIndex >= 0 &&
            colorIndex < orderColors.Length)
        {
            return orderColors[colorIndex];
        }

        return Color.white;
    }

    void RemoveHiddenOrders(HashSet<int> visibleOrderIds)
    {
        List<int> ordersToRemove = null;

        foreach (int orderId in blipsByOrderId.Keys)
        {
            if (visibleOrderIds.Contains(orderId))
            {
                continue;
            }

            if (ordersToRemove == null)
            {
                ordersToRemove = new List<int>();
            }

            ordersToRemove.Add(orderId);
        }

        if (ordersToRemove == null)
        {
            return;
        }

        foreach (int orderId in ordersToRemove)
        {
            if (!blipsByOrderId.TryGetValue(orderId, out BlipPair pair))
            {
                continue;
            }

            DestroyBlip(pair.pickup);
            DestroyBlip(pair.destination);
            blipsByOrderId.Remove(orderId);
        }
    }

    void FindPlayerIfNeeded()
    {
        if (player != null)
        {
            return;
        }

        GameObject playerObject = GameObject.FindGameObjectWithTag(playerTag);

        if (playerObject != null)
        {
            player = playerObject.transform;
        }
    }

    void ClearAllBlips()
    {
        foreach (BlipPair pair in blipsByOrderId.Values)
        {
            DestroyBlip(pair.pickup);
            DestroyBlip(pair.destination);
        }

        blipsByOrderId.Clear();
    }

    void DestroyBlip(RectTransform blip)
    {
        if (blip != null)
        {
            Destroy(blip.gameObject);
        }
    }

    private struct BlipPair
    {
        public RectTransform pickup;
        public RectTransform destination;
    }
}
