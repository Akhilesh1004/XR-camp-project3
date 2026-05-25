using System.Collections.Generic;
using UnityEngine;
using Debug = UnityEngine.Debug;
using UnityEngine.UI;
using Oculus.Interaction.Feedback;
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

    private Dictionary<Button, Vector3> buttonOriginalScales = new Dictionary<Button, Vector3>();
    private Button lastHoveredButton;

    void Start()
    {
        if (uiCanvasGroup != null) SetUIVisibility(StartVisible);
        SpawnOrderOptions();
    }

    void Update()
    {
        if (uiCanvasGroup == null || handTransform == null) return;

        if (uiCanvasGroup == null) return;

        // if (Input.GetKeyDown(KeyCode.Space))
        // {
        //     Debug.Log("Space pressed");
        //     SetUIVisibility(uiCanvasGroup.alpha < 0.1f);
        // }

        if (OVRInput.GetDown(Button))
        {
            Debug.Log("Toggle UI visibility");
            SetUIVisibility(uiCanvasGroup.alpha < 0.1f);
        }

        Button currentHoveredButton = null;

        if (uiCanvasGroup.alpha > 0.9f)
        {
            Vector3 rayOrigin = handTransform.position;
            Vector3 rayDirection = handTransform.forward;
            float maxRayDistance = 5f;

            if (Physics.Raycast(rayOrigin, rayDirection, out RaycastHit hit, maxRayDistance))
            {
                // ➔ 【整合：更新滑鼠圖標位置與旋轉】
                if (customCursor != null)
                {
                    if (!customCursor.gameObject.activeSelf) customCursor.gameObject.SetActive(true);
                    
                    // 讓滑鼠圖標吸附在射線擊中選單表面的點
                    customCursor.position = hit.point;
                    // 讓滑鼠圖標平行躺在選單表面，不產生歪斜
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

                    if (OVRInput.GetDown(OVRInput.Button.One))
                    {
                        Debug.Log($"點擊了按鈕: {hit.collider.name}");
                        targetButton.onClick.Invoke();
                    }
                }
            }
            else
            {
                // ➔ 【整合：如果射線移開選單，把滑鼠圖標藏起來】
                if (customCursor != null && customCursor.gameObject.activeSelf)
                {
                    customCursor.gameObject.SetActive(false);
                }
            }
        }

        if (lastHoveredButton != null && lastHoveredButton != currentHoveredButton)
        {
            lastHoveredButton.transform.localScale = GetOriginalScale(lastHoveredButton) * normalScale;
        }

        if (lastHoveredButton != null && lastHoveredButton == currentHoveredButton)
        {
        }
        else if (lastHoveredButton != null)
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

        // 面板關閉時，滑鼠圖標也要同步隱形
        if (customCursor != null) customCursor.gameObject.SetActive(visible);
    }

    Vector3 GetOriginalScale(Button button)
    {
        if (buttonOriginalScales.TryGetValue(button, out Vector3 originalScale))
        {
            return originalScale;
        }
        return button.transform.localScale;
    }

    void CleanupButtonScales(GameObject orderObj)
    {
        Button[] buttons = orderObj.GetComponentsInChildren<Button>(true);
        foreach (Button btn in buttons)
        {
            if (buttonOriginalScales.ContainsKey(btn))
            {
                buttonOriginalScales.Remove(btn);
            }
        }
    }

    void SpawnOrderOptions()
    {
        if (orderOptionPrefab == null || contentContainer == null) return;

        string[] orderTitles = { "Order A?", "Order B?" };

        for (int i = 0; i < orderTitles.Length; i++)
        {
            string currentOrderTitle = orderTitles[i];

            // 1. 生成整個 OrderOption 組合包
            GameObject newOrderObj = Instantiate(orderOptionPrefab, contentContainer);
            newOrderObj.name = $"Order_{i}";

            // 修正世界空間 UI 縮放與座標偏移
            // newOrderObj.transform.localScale = Vector3.one;
            newOrderObj.transform.localPosition = new Vector3(newOrderObj.transform.localPosition.x, newOrderObj.transform.localPosition.y, 0f);

            // 2. 【精準找字 1】找到 OrderInfo 修改標題文字
            Transform infoTransform = newOrderObj.transform.Find("OrderInfo");
            if (infoTransform != null)
            {
                TextMeshProUGUI infoText = infoTransform.GetComponent<TextMeshProUGUI>();
                if (infoText != null) infoText.text = currentOrderTitle;
            }

            // 3. 【精準找字 2】找到 YesBotton 底下的文字，並改名為 "確認"
            Transform yesTextTransform = newOrderObj.transform.Find("YesBotton/Text (TMP)");
            if (yesTextTransform != null)
            {
                TextMeshProUGUI yesText = yesTextTransform.GetComponent<TextMeshProUGUI>();
                if (yesText != null) yesText.text = "Yes";
            }

            // 4. 【精準找字 3】找到 NoBotton 底下的文字，並改名為 "拒絕"
            Transform noTextTransform = newOrderObj.transform.Find("NoBotton/Text (TMP)");
            if (noTextTransform != null)
            {
                TextMeshProUGUI noText = noTextTransform.GetComponent<TextMeshProUGUI>();
                if (noText != null) noText.text = "No";
            }

            // 5. 【動態事件綁定】分別為兩個按鈕裝上獨立的 onClick 靈魂
            Button yesBtn = newOrderObj.transform.Find("YesBotton")?.GetComponent<Button>();
            if (yesBtn != null)
            {
                if (!buttonOriginalScales.ContainsKey(yesBtn))
                {
                    buttonOriginalScales[yesBtn] = yesBtn.transform.localScale;
                }
                yesBtn.onClick.RemoveAllListeners();
                yesBtn.onClick.AddListener(() => OnOrderChoiceClicked(currentOrderTitle, true, newOrderObj));
            }

            Button noBtn = newOrderObj.transform.Find("NoBotton")?.GetComponent<Button>();
            if (noBtn != null)
            {
                if (!buttonOriginalScales.ContainsKey(noBtn))
                {
                    buttonOriginalScales[noBtn] = noBtn.transform.localScale;
                }
                noBtn.onClick.RemoveAllListeners();
                noBtn.onClick.AddListener(() => OnOrderChoiceClicked(currentOrderTitle, false, newOrderObj));
            }
        }
    }

    void OnOrderChoiceClicked(string orderName, bool isAccepted, GameObject orderObj)
    {
        if (isAccepted)
        {
            Debug.Log($"【接受點擊】玩家用右手射線 接受 了：{orderName}");
            // 在這裡寫按下確認後要發生的事...
        }
        else
        {
            Debug.Log($"【拒絕點擊】玩家用右手射線 拒絕 了：{orderName}");
            if (orderObj != null)
            {
                CleanupButtonScales(orderObj);
                Destroy(orderObj);
            }
        }
    }
}