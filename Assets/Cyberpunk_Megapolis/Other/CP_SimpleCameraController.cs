using UnityEngine;

namespace AE_Camera
{
    public class AE_SimpleCameraController : MonoBehaviour
    {
        [System.Serializable]
        private class CameraState
        {
            public float yaw;
            public float pitch;
            public float roll;
            public Vector3 position;

            public void SetFromTransform(Transform targetTransform)
            {
                Vector3 euler = targetTransform.eulerAngles;
                pitch = euler.x;
                yaw = euler.y;
                roll = euler.z;
                position = targetTransform.position;
            }

            public void Translate(Vector3 localTranslation)
            {
                Vector3 worldTranslation = Quaternion.Euler(pitch, yaw, roll) * localTranslation;
                position += worldTranslation;
            }

            public void LerpTowards(CameraState target, float positionLerpFactor, float rotationLerpFactor)
            {
                yaw = Mathf.Lerp(yaw, target.yaw, rotationLerpFactor);
                pitch = Mathf.Lerp(pitch, target.pitch, rotationLerpFactor);
                roll = Mathf.Lerp(roll, target.roll, rotationLerpFactor);

                position.x = Mathf.Lerp(position.x, target.position.x, positionLerpFactor);
                position.y = Mathf.Lerp(position.y, target.position.y, positionLerpFactor);
                position.z = Mathf.Lerp(position.z, target.position.z, positionLerpFactor);
            }

            public void ApplyToTransform(Transform targetTransform)
            {
                targetTransform.position = position;
                targetTransform.rotation = Quaternion.Euler(pitch, yaw, roll);
            }
        }

        [Header("Movement")]
        [SerializeField, Min(0.01f)] private float moveSpeed = 10f;
        [SerializeField, Min(1f)] private float sprintMultiplier = 4f;
        [SerializeField, Min(0.001f)] private float positionLerpTime = 0.2f;

        [Header("Rotation")]
        [SerializeField] private AnimationCurve mouseSensitivityCurve = new AnimationCurve(
            new Keyframe(0f, 0.5f, 0f, 5f),
            new Keyframe(1f, 2.5f, 0f, 0f)
        );
        [SerializeField, Min(0.001f)] private float rotationLerpTime = 0.01f;
        [SerializeField] private bool invertY = false;

        [Header("Options")]
        [SerializeField] private bool quitOnEscape = true;

        private readonly CameraState targetCameraState = new CameraState();
        private readonly CameraState interpolatedCameraState = new CameraState();

        // Runtime-fixed values.
        private float runtimeMoveSpeed;
        private float runtimeSprintMultiplier;
        private float runtimePositionLerpTime;
        private float runtimeRotationLerpTime;
        private bool runtimeInvertY;
        private bool runtimeQuitOnEscape;
        private AnimationCurve runtimeMouseSensitivityCurve;

        private void Awake()
        {
            CacheRuntimeSettings();
        }

        private void OnEnable()
        {
            CacheRuntimeSettings();

            targetCameraState.SetFromTransform(transform);
            interpolatedCameraState.SetFromTransform(transform);
        }

        private void OnDisable()
        {
            UnlockCursor();
        }

        private void CacheRuntimeSettings()
        {
            runtimeMoveSpeed = Mathf.Max(0.01f, moveSpeed);
            runtimeSprintMultiplier = Mathf.Max(1f, sprintMultiplier);
            runtimePositionLerpTime = Mathf.Max(0.001f, positionLerpTime);
            runtimeRotationLerpTime = Mathf.Max(0.001f, rotationLerpTime);
            runtimeInvertY = invertY;
            runtimeQuitOnEscape = quitOnEscape;
            runtimeMouseSensitivityCurve = new AnimationCurve(mouseSensitivityCurve.keys);
        }

        private void Update()
        {
            HandleEscape();
            HandleCursor();
            HandleRotation();
            HandleTranslation();
            ApplyInterpolation();
        }

        private void HandleEscape()
        {
            if (!runtimeQuitOnEscape)
                return;

            if (!Input.GetKeyDown(KeyCode.Escape))
                return;

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void HandleCursor()
        {
            if (Input.GetMouseButtonDown(1))
            {
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
            }

            if (Input.GetMouseButtonUp(1))
            {
                UnlockCursor();
            }
        }

        private void HandleRotation()
        {
            if (!Input.GetMouseButton(1))
                return;

            float mouseX = Input.GetAxisRaw("Mouse X");
            float mouseY = Input.GetAxisRaw("Mouse Y");

            float ySign = runtimeInvertY ? 1f : -1f;
            Vector2 mouseDelta = new Vector2(mouseX, mouseY * ySign);

            float sensitivity = runtimeMouseSensitivityCurve.Evaluate(mouseDelta.magnitude);

            targetCameraState.yaw += mouseDelta.x * sensitivity;
            targetCameraState.pitch += mouseDelta.y * sensitivity;
        }

        private void HandleTranslation()
        {
            Vector3 inputDirection = GetMovementInput();

            if (inputDirection.sqrMagnitude > 1f)
                inputDirection.Normalize();

            float currentSpeed = runtimeMoveSpeed;

            if (Input.GetKey(KeyCode.LeftShift))
                currentSpeed *= runtimeSprintMultiplier;

            Vector3 translation = inputDirection * currentSpeed * Time.deltaTime;
            targetCameraState.Translate(translation);
        }

        private Vector3 GetMovementInput()
        {
            Vector3 direction = Vector3.zero;

            if (Input.GetKey(KeyCode.W))
                direction += Vector3.forward;

            if (Input.GetKey(KeyCode.S))
                direction += Vector3.back;

            if (Input.GetKey(KeyCode.A))
                direction += Vector3.left;

            if (Input.GetKey(KeyCode.D))
                direction += Vector3.right;

            if (Input.GetKey(KeyCode.E))
                direction += Vector3.up;

            if (Input.GetKey(KeyCode.Q))
                direction += Vector3.down;

            return direction;
        }

        private void ApplyInterpolation()
        {
            float positionLerpFactor = GetLerpFactor(runtimePositionLerpTime);
            float rotationLerpFactor = GetLerpFactor(runtimeRotationLerpTime);

            interpolatedCameraState.LerpTowards(targetCameraState, positionLerpFactor, rotationLerpFactor);
            interpolatedCameraState.ApplyToTransform(transform);
        }

        private float GetLerpFactor(float lerpTime)
        {
            return 1f - Mathf.Exp((Mathf.Log(0.01f) / lerpTime) * Time.deltaTime);
        }

        private void UnlockCursor()
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            moveSpeed = Mathf.Max(0.01f, moveSpeed);
            sprintMultiplier = Mathf.Max(1f, sprintMultiplier);
            positionLerpTime = Mathf.Max(0.001f, positionLerpTime);
            rotationLerpTime = Mathf.Max(0.001f, rotationLerpTime);

            if (mouseSensitivityCurve == null || mouseSensitivityCurve.length == 0)
            {
                mouseSensitivityCurve = new AnimationCurve(
                    new Keyframe(0f, 0.5f, 0f, 5f),
                    new Keyframe(1f, 2.5f, 0f, 0f)
                );
            }
        }
#endif
    }
}