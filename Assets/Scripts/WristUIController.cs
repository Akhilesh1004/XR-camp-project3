using UnityEngine;
using Debug = UnityEngine.Debug;

public class WristUIController : MonoBehaviour
{
    [Header("參考物件")]
    public CanvasGroup uiCanvasGroup;
    public OVRInput.Controller controller = OVRInput.Controller.LTouch;
    public OVRInput.Button Button = OVRInput.Button.Four;
    void Start()
    {
        if (uiCanvasGroup != null) SetUIVisibility(true);
    }

    void Update()
    {
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
    }

    void SetUIVisibility(bool visible)
    {
        uiCanvasGroup.alpha = visible ? 1f : 0f;
        uiCanvasGroup.interactable = visible;
        uiCanvasGroup.blocksRaycasts = visible;
    }
}