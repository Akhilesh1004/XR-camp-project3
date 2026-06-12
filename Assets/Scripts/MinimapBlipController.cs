using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
[ExecuteAlways]
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
    public bool showOnlyCurrentActiveOrder = true;
    public bool showWaitingOrders = false;
    public bool showPickupBlips = true;
    public bool showDestinationBlips = true;

    [Header("Visual")]
    public Sprite pickupBlipSprite;
    public Sprite destinationBlipSprite;
    public bool preserveBlipSpriteAspect = true;
    public Vector2 pickupBlipSize = new Vector2(7f, 7f);
    public Vector2 destinationBlipSize = new Vector2(8f, 8f);
    public float pickupBlipRotation = 0f;
    public float destinationBlipRotation = 45f;
    public Vector2 edgeBlipScale = new Vector2(1.15f, 1.15f);
    public Color pickupTint = Color.white;
    public Color destinationTint = Color.white;
    public Color[] orderColors =
    {
        new Color(0.25f, 0.65f, 1f, 1f),
        new Color(0.2f, 1f, 0.45f, 1f),
        new Color(1f, 0.35f, 0.25f, 1f)
    };

    [Header("Distance")]
    public bool showDistanceLabels = true;
    public Vector2 distanceLabelSize = new Vector2(42f, 12f);
    public Vector2 pickupDistanceLabelOffset = new Vector2(0f, -10f);
    public Vector2 destinationDistanceLabelOffset = new Vector2(0f, 10f);
    public float distanceFontSize = 7f;
    public Color distanceTextColor = Color.white;
    public string distanceFormat = "{0:0}m";

    [Header("Layout Guide")]
    public bool showLayoutGuide = false;
    public bool showMarkerClampGuide = true;
    public Color mapBoundaryGuideColor = new Color(1f, 1f, 1f, 0.75f);
    public Color markerClampGuideColor = new Color(1f, 0.85f, 0.1f, 0.75f);
    public float layoutGuideLineThickness = 1f;

    private readonly Dictionary<int, BlipPair> blipsByOrderId =
        new Dictionary<int, BlipPair>();

    private RectTransform[] mapBoundaryGuideLines;
    private RectTransform[] markerClampGuideLines;
    private float nextUpdateTime;

    void Awake()
    {
        if (mapRect == null)
        {
            mapRect = GetComponent<RectTransform>();
        }

        if (Application.isPlaying)
        {
            FindPlayerIfNeeded();
        }
    }

    void OnEnable()
    {
        nextUpdateTime = 0f;
        UpdateLayoutGuide();
    }

    void OnDisable()
    {
        ClearAllBlips();
        DestroyLayoutGuide();
    }

    void LateUpdate()
    {
        if (!Application.isPlaying)
        {
            if (mapRect == null)
            {
                mapRect = GetComponent<RectTransform>();
            }

            UpdateLayoutGuide();
            return;
        }

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

        UpdateLayoutGuide();
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
                pickupBlipRotation,
                pickupBlipSprite,
                pair.pickupDistanceLabel,
                pickupDistanceLabelOffset
            );

            UpdateBlip(
                pair.destination,
                order.destinationPoint,
                showDestinationBlips,
                order.colorIndex,
                destinationTint,
                destinationBlipSize,
                destinationBlipRotation,
                destinationBlipSprite,
                pair.destinationDistanceLabel,
                destinationDistanceLabelOffset
            );
        }

        RemoveHiddenOrders(visibleOrderIds);
    }

    bool ShouldShowOrder(DeliveryOrder order)
    {
        if (order.state == OrderState.Active)
        {
            if (!showActiveOrders)
            {
                return false;
            }

            return !showOnlyCurrentActiveOrder ||
                   DeliveryGameManager.Instance.CurrentActiveOrder == order;
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
            pickup = CreateBlip($"MinimapBlip_Pickup_{order.orderId}", pickupBlipSize, pickupBlipRotation),
            destination = CreateBlip($"MinimapBlip_Destination_{order.orderId}", destinationBlipSize, destinationBlipRotation),
            pickupDistanceLabel = CreateDistanceLabel($"MinimapDistance_Pickup_{order.orderId}"),
            destinationDistanceLabel = CreateDistanceLabel($"MinimapDistance_Destination_{order.orderId}")
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
        image.preserveAspect = preserveBlipSpriteAspect;

        return rect;
    }

    TextMeshProUGUI CreateDistanceLabel(string name)
    {
        GameObject labelObject = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI)
        );

        labelObject.layer = gameObject.layer;

        RectTransform rect = labelObject.GetComponent<RectTransform>();
        rect.SetParent(mapRect, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = distanceLabelSize;
        rect.localScale = Vector3.one;

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.raycastTarget = false;
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = distanceFontSize;
        label.color = distanceTextColor;
        label.text = "";

        return label;
    }

    void UpdateBlip(
        RectTransform blip,
        Transform target,
        bool shouldShow,
        int colorIndex,
        Color tint,
        Vector2 baseSize,
        float baseRotation,
        Sprite sprite,
        TextMeshProUGUI distanceLabel,
        Vector2 distanceLabelOffset)
    {
        if (blip == null)
        {
            return;
        }

        bool visible = shouldShow && target != null;
        blip.gameObject.SetActive(visible);
        SetDistanceLabelVisible(distanceLabel, false);

        if (!visible)
        {
            return;
        }

        Vector2 normalizedPosition = GetNormalizedMapPosition(target.position, out bool isOutsideRange);
        Rect rect = mapRect.rect;
        float halfWidth = Mathf.Max(1f, rect.width * 0.5f - edgePadding);
        float halfHeight = Mathf.Max(1f, rect.height * 0.5f - edgePadding);

        Vector2 anchoredPosition = new Vector2(
            normalizedPosition.x * halfWidth,
            normalizedPosition.y * halfHeight
        );

        blip.anchoredPosition = anchoredPosition;
        blip.sizeDelta = baseSize;
        blip.localScale = isOutsideRange
            ? new Vector3(edgeBlipScale.x, edgeBlipScale.y, 1f)
            : Vector3.one;
        blip.localRotation = Quaternion.Euler(0f, 0f, baseRotation);

        Image image = blip.GetComponent<Image>();

        if (image != null)
        {
            image.sprite = sprite;
            image.preserveAspect = preserveBlipSpriteAspect;
            image.color = GetOrderColor(colorIndex) * tint;
        }

        UpdateDistanceLabel(
            distanceLabel,
            target.position,
            anchoredPosition,
            distanceLabelOffset,
            halfWidth,
            halfHeight
        );
    }

    void UpdateDistanceLabel(
        TextMeshProUGUI label,
        Vector3 targetPosition,
        Vector2 blipAnchoredPosition,
        Vector2 offset,
        float halfWidth,
        float halfHeight)
    {
        if (label == null || !showDistanceLabels || player == null)
        {
            SetDistanceLabelVisible(label, false);
            return;
        }

        RectTransform rect = label.rectTransform;
        Vector2 labelPosition = blipAnchoredPosition + offset;
        float padding = Mathf.Max(edgePadding, 1f);

        labelPosition.x = Mathf.Clamp(
            labelPosition.x,
            -halfWidth + padding,
            halfWidth - padding
        );

        labelPosition.y = Mathf.Clamp(
            labelPosition.y,
            -halfHeight + padding,
            halfHeight - padding
        );

        rect.anchoredPosition = labelPosition;
        rect.sizeDelta = distanceLabelSize;
        label.fontSize = distanceFontSize;
        label.color = distanceTextColor;
        label.text = FormatDistance(Vector3.Distance(player.position, targetPosition));
        SetDistanceLabelVisible(label, true);
    }

    string FormatDistance(float distance)
    {
        if (string.IsNullOrEmpty(distanceFormat))
        {
            return Mathf.RoundToInt(distance).ToString() + "m";
        }

        try
        {
            return string.Format(distanceFormat, distance);
        }
        catch (System.FormatException)
        {
            return Mathf.RoundToInt(distance).ToString() + "m";
        }
    }

    void SetDistanceLabelVisible(TextMeshProUGUI label, bool visible)
    {
        if (label != null && label.gameObject.activeSelf != visible)
        {
            label.gameObject.SetActive(visible);
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
            DestroyDistanceLabel(pair.pickupDistanceLabel);
            DestroyDistanceLabel(pair.destinationDistanceLabel);
            blipsByOrderId.Remove(orderId);
        }
    }

    void UpdateLayoutGuide()
    {
        if (mapRect == null || !gameObject.scene.IsValid())
        {
            return;
        }

        if (!showLayoutGuide)
        {
            SetGuideLinesActive(mapBoundaryGuideLines, false);
            SetGuideLinesActive(markerClampGuideLines, false);
            return;
        }

        EnsureLayoutGuideCreated();

        Rect rect = mapRect.rect;
        Vector2 mapSize = new Vector2(Mathf.Abs(rect.width), Mathf.Abs(rect.height));
        UpdateGuideRectangle(mapBoundaryGuideLines, mapSize, mapBoundaryGuideColor);

        bool showClampGuide = showMarkerClampGuide && edgePadding > 0f;
        SetGuideLinesActive(markerClampGuideLines, showClampGuide);

        if (showClampGuide)
        {
            Vector2 clampSize = new Vector2(
                Mathf.Max(0f, mapSize.x - edgePadding * 2f),
                Mathf.Max(0f, mapSize.y - edgePadding * 2f)
            );

            UpdateGuideRectangle(markerClampGuideLines, clampSize, markerClampGuideColor);
        }
    }

    void EnsureLayoutGuideCreated()
    {
        if (NeedsGuideLines(mapBoundaryGuideLines))
        {
            mapBoundaryGuideLines = CreateGuideLines("MinimapGuide_Boundary");
        }

        if (NeedsGuideLines(markerClampGuideLines))
        {
            markerClampGuideLines = CreateGuideLines("MinimapGuide_Clamp");
        }
    }

    bool NeedsGuideLines(RectTransform[] lines)
    {
        if (lines == null || lines.Length != 4)
        {
            return true;
        }

        foreach (RectTransform line in lines)
        {
            if (line == null)
            {
                return true;
            }
        }

        return false;
    }

    RectTransform[] CreateGuideLines(string namePrefix)
    {
        RectTransform[] lines = new RectTransform[4];

        for (int i = 0; i < lines.Length; i++)
        {
            GameObject lineObject = new GameObject(
                $"{namePrefix}_{i}",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image)
            );

            lineObject.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            lineObject.layer = gameObject.layer;

            RectTransform line = lineObject.GetComponent<RectTransform>();
            line.SetParent(mapRect, false);
            line.anchorMin = new Vector2(0.5f, 0.5f);
            line.anchorMax = new Vector2(0.5f, 0.5f);
            line.pivot = new Vector2(0.5f, 0.5f);
            line.localScale = Vector3.one;

            Image image = lineObject.GetComponent<Image>();
            image.raycastTarget = false;

            lines[i] = line;
        }

        return lines;
    }

    void UpdateGuideRectangle(RectTransform[] lines, Vector2 size, Color color)
    {
        if (lines == null || lines.Length != 4)
        {
            return;
        }

        float thickness = Mathf.Max(0.25f, layoutGuideLineThickness);
        float halfWidth = size.x * 0.5f;
        float halfHeight = size.y * 0.5f;

        SetGuideLine(lines[0], new Vector2(0f, halfHeight), new Vector2(size.x, thickness), color);
        SetGuideLine(lines[1], new Vector2(0f, -halfHeight), new Vector2(size.x, thickness), color);
        SetGuideLine(lines[2], new Vector2(-halfWidth, 0f), new Vector2(thickness, size.y), color);
        SetGuideLine(lines[3], new Vector2(halfWidth, 0f), new Vector2(thickness, size.y), color);
    }

    void SetGuideLine(RectTransform line, Vector2 anchoredPosition, Vector2 size, Color color)
    {
        if (line == null)
        {
            return;
        }

        line.gameObject.SetActive(true);
        line.anchoredPosition = anchoredPosition;
        line.sizeDelta = size;
        line.localRotation = Quaternion.identity;
        line.SetAsLastSibling();

        Image image = line.GetComponent<Image>();

        if (image != null)
        {
            image.color = color;
        }
    }

    void SetGuideLinesActive(RectTransform[] lines, bool active)
    {
        if (lines == null)
        {
            return;
        }

        foreach (RectTransform line in lines)
        {
            if (line != null && line.gameObject.activeSelf != active)
            {
                line.gameObject.SetActive(active);
            }
        }
    }

    void DestroyLayoutGuide()
    {
        DestroyGuideLines(mapBoundaryGuideLines);
        DestroyGuideLines(markerClampGuideLines);
        mapBoundaryGuideLines = null;
        markerClampGuideLines = null;
    }

    void DestroyGuideLines(RectTransform[] lines)
    {
        if (lines == null)
        {
            return;
        }

        foreach (RectTransform line in lines)
        {
            if (line != null)
            {
                DestroyObject(line.gameObject);
            }
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
            DestroyDistanceLabel(pair.pickupDistanceLabel);
            DestroyDistanceLabel(pair.destinationDistanceLabel);
        }

        blipsByOrderId.Clear();
    }

    void DestroyBlip(RectTransform blip)
    {
        if (blip != null)
        {
            DestroyObject(blip.gameObject);
        }
    }

    void DestroyDistanceLabel(TextMeshProUGUI label)
    {
        if (label != null)
        {
            DestroyObject(label.gameObject);
        }
    }

    void DestroyObject(Object objectToDestroy)
    {
        if (objectToDestroy == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(objectToDestroy);
        }
        else
        {
            DestroyImmediate(objectToDestroy);
        }
    }

    private struct BlipPair
    {
        public RectTransform pickup;
        public RectTransform destination;
        public TextMeshProUGUI pickupDistanceLabel;
        public TextMeshProUGUI destinationDistanceLabel;
    }
}
