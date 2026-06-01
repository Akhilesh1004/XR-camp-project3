using UnityEngine;
using UnityEngine.UI;

public class VRInstructionPauseMenu : MonoBehaviour
{
    [Header("Player View")]
    public Transform headTransform;

    [Header("Instruction Window")]
    public Canvas instructionCanvas;
    public Image instructionImage;
    public Text pageText;

    [Header("Image Pages")]
    public Sprite[] pageSprites;

    [Header("Window Position")]
    public float distanceFromFace = 1.2f;
    public float verticalOffset = -0.05f;
    public bool followHeadWhileOpen = true;
    public float followSmooth = 12f;

    [Header("Input")]
    public OVRInput.Controller menuController = OVRInput.Controller.LTouch;
    public OVRInput.Button menuButton = OVRInput.Button.PrimaryThumbstick;
    public OVRInput.Axis2D pageAxis = OVRInput.Axis2D.PrimaryThumbstick;

    public float pageSwitchThreshold = 0.65f;
    public float pageSwitchCooldown = 0.25f;

    [Header("Game Start")]
    public bool openOnStart = true;
    public bool startDeliveryGameOnFirstClose = true;
    public DeliveryGameManager deliveryGameManager;
    public WristUIController wristUIController;

    [Header("Pause")]
    public bool pauseGameWhenOpen = true;
    public bool pauseAudioWhenOpen = false;

    private bool isOpen = false;
    private bool hasStartedGame = false;

    private int currentPage = 0;
    private float lastPageSwitchTime = -999f;

    private float previousTimeScale = 1f;

    void Awake()
    {
        if (deliveryGameManager == null)
        {
            deliveryGameManager = FindFirstObjectByType<DeliveryGameManager>();
        }
        if (wristUIController == null)
        {
            wristUIController = FindFirstObjectByType<WristUIController>();
        }
    }

    void Start()
    {
        if (headTransform == null && Camera.main != null)
        {
            headTransform = Camera.main.transform;
        }

        if (instructionCanvas != null)
        {
            instructionCanvas.gameObject.SetActive(false);
        }

        if (openOnStart)
        {
            OpenMenu();
        }
        else
        {
            hasStartedGame = true;

            if (deliveryGameManager != null && startDeliveryGameOnFirstClose && wristUIController != null)
            {
                deliveryGameManager.StartGame();
                wristUIController.StartGame();
            }
        }
    }

    void Update()
    {
        if (OVRInput.GetDown(menuButton, menuController))
        {
            ToggleMenu();
        }

        if (isOpen)
        {
            HandlePageInput();

            if (followHeadWhileOpen)
            {
                UpdateWindowPosition();
            }
        }
    }

    void ToggleMenu()
    {
        if (isOpen)
        {
            CloseMenu();
        }
        else
        {
            OpenMenu();
        }
    }

    public void OpenMenu()
    {
        isOpen = true;

        if (instructionCanvas != null)
        {
            instructionCanvas.gameObject.SetActive(true);
        }

        int count = GetPageCount();

        if (count > 0)
        {
            currentPage = Mathf.Clamp(currentPage, 0, count - 1);
        }
        else
        {
            currentPage = 0;
        }

        UpdateWindowPositionImmediate();
        RefreshPage();

        if (pauseGameWhenOpen)
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        if (pauseAudioWhenOpen)
        {
            AudioListener.pause = true;
        }
    }

    public void CloseMenu()
    {
        isOpen = false;

        if (instructionCanvas != null)
        {
            instructionCanvas.gameObject.SetActive(false);
        }

        if (pauseGameWhenOpen)
        {
            Time.timeScale = previousTimeScale <= 0f ? 1f : previousTimeScale;
        }

        if (pauseAudioWhenOpen)
        {
            AudioListener.pause = false;
        }

        if (!hasStartedGame)
        {
            hasStartedGame = true;

            if (startDeliveryGameOnFirstClose && deliveryGameManager != null && wristUIController != null)
            {
                deliveryGameManager.StartGame();
                wristUIController.StartGame();
            }
        }
    }

    void HandlePageInput()
    {
        if (Time.unscaledTime - lastPageSwitchTime < pageSwitchCooldown)
        {
            return;
        }

        Vector2 axis = OVRInput.Get(pageAxis, menuController);

        if (axis.x > pageSwitchThreshold)
        {
            NextPage();
            lastPageSwitchTime = Time.unscaledTime;
        }
        else if (axis.x < -pageSwitchThreshold)
        {
            PreviousPage();
            lastPageSwitchTime = Time.unscaledTime;
        }
    }

    public void NextPage()
    {
        int count = GetPageCount();

        if (count <= 0)
        {
            return;
        }

        currentPage++;

        if (currentPage >= count)
        {
            currentPage = count - 1;
        }

        RefreshPage();
    }

    public void PreviousPage()
    {
        int count = GetPageCount();

        if (count <= 0)
        {
            return;
        }

        currentPage--;

        if (currentPage < 0)
        {
            currentPage = 0;
        }

        RefreshPage();
    }

    int GetPageCount()
    {
        if (pageSprites == null)
        {
            return 0;
        }

        return pageSprites.Length;
    }

    void RefreshPage()
    {
        int count = GetPageCount();

        if (count <= 0)
        {
            if (instructionImage != null)
            {
                instructionImage.sprite = null;
                instructionImage.enabled = false;
            }

            if (pageText != null)
            {
                pageText.text = "0 / 0";
            }

            return;
        }

        currentPage = Mathf.Clamp(currentPage, 0, count - 1);

        if (instructionImage != null)
        {
            instructionImage.sprite = pageSprites[currentPage];
            instructionImage.enabled = pageSprites[currentPage] != null;
            instructionImage.preserveAspect = true;
        }

        if (pageText != null)
        {
            pageText.text = (currentPage + 1).ToString() + " / " + count.ToString();
        }
    }

    void UpdateWindowPosition()
    {
        if (headTransform == null || instructionCanvas == null)
        {
            return;
        }

        Vector3 targetPosition = GetTargetWindowPosition();
        Quaternion targetRotation = GetTargetWindowRotation(targetPosition);

        instructionCanvas.transform.position = Vector3.Lerp(
            instructionCanvas.transform.position,
            targetPosition,
            Time.unscaledDeltaTime * followSmooth
        );

        instructionCanvas.transform.rotation = Quaternion.Slerp(
            instructionCanvas.transform.rotation,
            targetRotation,
            Time.unscaledDeltaTime * followSmooth
        );
    }

    void UpdateWindowPositionImmediate()
    {
        if (headTransform == null || instructionCanvas == null)
        {
            return;
        }

        Vector3 targetPosition = GetTargetWindowPosition();

        instructionCanvas.transform.position = targetPosition;
        instructionCanvas.transform.rotation = GetTargetWindowRotation(targetPosition);
    }

    Vector3 GetTargetWindowPosition()
    {
        Vector3 forward = headTransform.forward;
        Vector3 up = Vector3.up;

        return headTransform.position +
               forward * distanceFromFace +
               up * verticalOffset;
    }

    Quaternion GetTargetWindowRotation(Vector3 targetPosition)
    {
        if (headTransform == null)
        {
            return Quaternion.identity;
        }

        Vector3 direction = targetPosition - headTransform.position;

        if (direction.sqrMagnitude < 0.001f)
        {
            direction = headTransform.forward;
        }

        return Quaternion.LookRotation(direction.normalized, Vector3.up);
    }
}