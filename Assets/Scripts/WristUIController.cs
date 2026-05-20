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

    [Header("縮放效果設定")]
    [Tooltip("射線指著按鈕時的縮放比例")]
    public Vector3 hoveredScale = new Vector3(0.9f, 0.9f, 1f); 
    [Tooltip("正常狀態下的縮放比例")]
    public Vector3 normalScale = new Vector3(1f, 1f, 1f);
    [Tooltip("縮放動畫速度")]
    public float lerpSpeed = 10f;
    [Header("動態生成設定")]
    public GameObject buttonPrefab;
    public Transform contentContainer;

    private Button lastHoveredButton;

    void Start()
    {
        if (uiCanvasGroup != null) SetUIVisibility(false);
        SpawnMyButtons();
    }

    void Update()
    {
        if (uiCanvasGroup == null || handTransform == null) return;

        if (uiCanvasGroup == null) return;

        if (Input.GetKeyDown(KeyCode.Space))
        {
            Debug.Log("Space pressed");
            SetUIVisibility(uiCanvasGroup.alpha < 0.1f);
        }

        if (OVRInput.GetDown(Button))
        {
            Debug.Log("Toggle UI visibility");
            SetUIVisibility(uiCanvasGroup.alpha < 0.1f);
        }

        Button currentHoveredButton = null;

        if (uiCanvasGroup.alpha > 0.9f)
        {
            // Debug.Log("UI is visible, checking for button hover");
            // Debug.DrawRay(handTransform.position, handTransform.forward * 5f, Color.red);
            if (Physics.Raycast(handTransform.position, handTransform.forward, out RaycastHit hit))
            {
                Button targetButton = hit.collider.GetComponent<Button>();

                if (targetButton != null && targetButton.interactable)
                {
                    currentHoveredButton = targetButton;

                    targetButton.transform.localScale = Vector3.Lerp(
                        targetButton.transform.localScale, 
                        hoveredScale, 
                        Time.deltaTime * lerpSpeed
                    );

                    if (OVRInput.GetDown(OVRInput.Button.One))
                    {
                        Debug.Log($"點擊了按鈕: {hit.collider.name}");
                        targetButton.onClick.Invoke();
                    }
                }
            }
        }

        if (lastHoveredButton != null && lastHoveredButton != currentHoveredButton)
        {
            lastHoveredButton.transform.localScale = normalScale;
        }

        if (lastHoveredButton != null && lastHoveredButton == currentHoveredButton)
        {
        }
        else if (lastHoveredButton != null)
        {
            lastHoveredButton.transform.localScale = Vector3.Lerp(
                lastHoveredButton.transform.localScale, 
                normalScale, 
                Time.deltaTime * lerpSpeed
            );
            
            if (Vector3.Distance(lastHoveredButton.transform.localScale, normalScale) < 0.01f)
            {
                lastHoveredButton.transform.localScale = normalScale;
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
    }

    void SpawnMyButtons()
    {
        if (buttonPrefab == null || contentContainer == null) return;

        string[] optionNames = { "A", "B", "C" };

        for (int i = 0; i < optionNames.Length; i++)
        {
            string currentName = optionNames[i];

            GameObject newBtnObj = Instantiate(buttonPrefab, contentContainer);
            newBtnObj.name = currentName;

            newBtnObj.transform.localScale = normalScale;
            newBtnObj.transform.localPosition = new Vector3(newBtnObj.transform.localPosition.x, newBtnObj.transform.localPosition.y, 0f);

            TextMeshProUGUI btnText = newBtnObj.GetComponentInChildren<TextMeshProUGUI>();
            if (btnText != null) btnText.text = currentName;
            else Debug.LogWarning($"在按鈕預製件中找不到 Text 組件，無法設定按鈕文字：{currentName}");

            Button btn = newBtnObj.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnButtonClickLogic(currentName, btn));
            }
        }
    }

    void OnButtonClickLogic(string nameOfButton, Button button)
    {
        Debug.Log($"射線成功點擊了動態生成的：{nameOfButton} ！");

        Image buttonImage = button.GetComponent<Image>();
        if (buttonImage != null)
        {
            buttonImage.color = new Color(0.5f, 1f, 0.5f, 1f);
        }

        // if (nameOfButton == "離開選單")
        // {
        //     SetUIVisibility(false);
        // }
    }
}