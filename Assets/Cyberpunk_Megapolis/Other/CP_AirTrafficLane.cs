using UnityEngine;

public class AirTrafficLane : MonoBehaviour
{
    [Header("Boxes")]
    [SerializeField] private BoxCollider spawnBox;
    [SerializeField] private BoxCollider targetBox;

    [Header("Prefabs")]
    [SerializeField] private GameObject[] vehiclePrefabs;

    [Header("Traffic")]
    [SerializeField] private bool autoStart = true;
    [SerializeField] private int maxActiveVehicles = 10;

    [Header("Speed")]
    [SerializeField] private float minSpeed = 8f;
    [SerializeField] private float maxSpeed = 14f;

    [Header("Scale")]
    [SerializeField] private bool randomScale = false;
    [SerializeField] private float minScaleMultiplier = 0.95f;
    [SerializeField] private float maxScaleMultiplier = 1.05f;

    [Header("Simple Sway")]
    [Tooltip("0 = no side movement.")]
    [SerializeField] private float sideAmplitude = 0.35f;

    [Tooltip("Side sway speed.")]
    [SerializeField] private float sideFrequency = 1.2f;

    [Tooltip("0 = no vertical movement.")]
    [SerializeField] private float verticalAmplitude = 0.2f;

    [Tooltip("Vertical sway speed.")]
    [SerializeField] private float verticalFrequency = 0.9f;

    [Header("Model Orientation")]
    [SerializeField] private float modelYawOffset = -90f;

    [Header("Safety")]
    [SerializeField] private bool forceKinematicRigidbody = true;
    [SerializeField] private bool disableCollidersOnVehicles = false;

    [Header("Optional")]
    [SerializeField] private Transform spawnedParent;

    private class Vehicle
    {
        public GameObject obj;
        public Transform tr;

        public Vector3 start;
        public Vector3 target;
        public Vector3 direction;
        public Vector3 sideDir;
        public Vector3 upDir;

        public float distance;
        public float speed;
        public float duration;

        public double startTime;
        public float timeOffset;

        public float sideAmplitude;
        public float sideFrequency;
        public float verticalAmplitude;
        public float verticalFrequency;
    }

    private Vehicle[] vehicles;
    private bool isRunning;

    private void Start()
    {
        if (autoStart)
            StartTraffic();
    }

    private void LateUpdate()
    {
        if (!isRunning || vehicles == null)
            return;

        double now = Time.timeAsDouble;

        for (int i = 0; i < vehicles.Length; i++)
        {
            Vehicle v = vehicles[i];

            if (v == null || v.tr == null)
                continue;

            float progress = (float)((now - v.startTime) / v.duration);

            if (progress >= 1f)
            {
                ResetVehicleRoute(v, 0f);
                progress = 0f;
            }

            ApplyVehiclePosition(v, progress, now);
        }
    }

    public void StartTraffic()
    {
        if (isRunning)
            return;

        if (!ValidateSetup())
            return;

        CreateVehiclesOnce();

        isRunning = true;
    }

    public void StopTraffic()
    {
        isRunning = false;
    }

    private void CreateVehiclesOnce()
    {
        vehicles = new Vehicle[maxActiveVehicles];

        for (int i = 0; i < maxActiveVehicles; i++)
        {
            GameObject prefab = GetRandomPrefab();

            if (prefab == null)
                continue;

            GameObject obj = Instantiate(prefab, Vector3.zero, Quaternion.identity, spawnedParent);
            obj.name = prefab.name + "_AirTraffic_" + i.ToString("00");

            if (forceKinematicRigidbody)
                MakeRigidbodiesKinematic(obj);

            if (disableCollidersOnVehicles)
                DisableColliders(obj);

            if (randomScale)
            {
                float scale = Random.Range(minScaleMultiplier, maxScaleMultiplier);
                obj.transform.localScale *= scale;
            }

            Vehicle v = new Vehicle();
            v.obj = obj;
            v.tr = obj.transform;

            vehicles[i] = v;

            float startProgress = Random.Range(0f, 0.95f);
            ResetVehicleRoute(v, startProgress);
        }
    }

    private void ResetVehicleRoute(Vehicle v, float startProgress)
    {
        v.start = GetRandomPointInBox(spawnBox);
        v.target = GetRandomPointInBox(targetBox);

        Vector3 route = v.target - v.start;
        v.distance = route.magnitude;

        if (v.distance < 0.01f)
        {
            v.direction = transform.forward;
            v.distance = 1f;
        }
        else
        {
            v.direction = route / v.distance;
        }

        BuildRouteAxes(v.direction, out v.sideDir, out v.upDir);

        v.speed = Random.Range(minSpeed, maxSpeed);
        v.duration = v.distance / v.speed;

        if (v.duration < 0.01f)
            v.duration = 0.01f;

        startProgress = Mathf.Clamp01(startProgress);

        v.startTime = Time.timeAsDouble - v.duration * startProgress;
        v.timeOffset = Random.Range(0f, 1000f);

        v.sideAmplitude = sideAmplitude;
        v.sideFrequency = sideFrequency;

        v.verticalAmplitude = verticalAmplitude;
        v.verticalFrequency = verticalFrequency;

        v.tr.rotation =
            Quaternion.LookRotation(v.direction, v.upDir) *
            Quaternion.Euler(0f, modelYawOffset, 0f);
    }

    private void ApplyVehiclePosition(Vehicle v, float progress, double now)
    {
        progress = Mathf.Clamp01(progress);

        Vector3 basePosition = Vector3.LerpUnclamped(v.start, v.target, progress);

        float t = (float)now + v.timeOffset;

        Vector3 sway = Vector3.zero;

        if (v.sideAmplitude != 0f && v.sideFrequency != 0f)
        {
            sway += v.sideDir * Mathf.Sin(t * v.sideFrequency) * v.sideAmplitude;
        }

        if (v.verticalAmplitude != 0f && v.verticalFrequency != 0f)
        {
            sway += v.upDir * Mathf.Sin(t * v.verticalFrequency) * v.verticalAmplitude;
        }

        v.tr.position = basePosition + sway;

        v.tr.rotation =
            Quaternion.LookRotation(v.direction, v.upDir) *
            Quaternion.Euler(0f, modelYawOffset, 0f);
    }

    private GameObject GetRandomPrefab()
    {
        if (vehiclePrefabs == null || vehiclePrefabs.Length == 0)
            return null;

        for (int i = 0; i < 30; i++)
        {
            GameObject prefab = vehiclePrefabs[Random.Range(0, vehiclePrefabs.Length)];

            if (prefab != null)
                return prefab;
        }

        return null;
    }

    private void BuildRouteAxes(Vector3 routeDir, out Vector3 sideDir, out Vector3 upDir)
    {
        Vector3 worldUp = Vector3.up;

        if (Mathf.Abs(Vector3.Dot(routeDir, worldUp)) > 0.98f)
            worldUp = Vector3.forward;

        sideDir = Vector3.Cross(worldUp, routeDir).normalized;
        upDir = Vector3.Cross(routeDir, sideDir).normalized;

        if (sideDir.sqrMagnitude < 0.0001f)
            sideDir = Vector3.right;

        if (upDir.sqrMagnitude < 0.0001f)
            upDir = Vector3.up;
    }

    private Vector3 GetRandomPointInBox(BoxCollider box)
    {
        Vector3 localPoint = box.center + new Vector3(
            Random.Range(-box.size.x * 0.5f, box.size.x * 0.5f),
            Random.Range(-box.size.y * 0.5f, box.size.y * 0.5f),
            Random.Range(-box.size.z * 0.5f, box.size.z * 0.5f)
        );

        return box.transform.TransformPoint(localPoint);
    }

    private void MakeRigidbodiesKinematic(GameObject obj)
    {
        Rigidbody[] bodies = obj.GetComponentsInChildren<Rigidbody>(true);

        for (int i = 0; i < bodies.Length; i++)
        {
            Rigidbody rb = bodies[i];

            if (rb == null)
                continue;

            rb.isKinematic = true;
            rb.useGravity = false;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    private void DisableColliders(GameObject obj)
    {
        Collider[] colliders = obj.GetComponentsInChildren<Collider>(true);

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }
    }

    private bool ValidateSetup()
    {
        if (spawnBox == null)
        {
            Debug.LogError($"[{name}] Spawn Box is not assigned.");
            return false;
        }

        if (targetBox == null)
        {
            Debug.LogError($"[{name}] Target Box is not assigned.");
            return false;
        }

        if (vehiclePrefabs == null || vehiclePrefabs.Length == 0)
        {
            Debug.LogError($"[{name}] Vehicle Prefabs are not assigned.");
            return false;
        }

        bool hasPrefab = false;

        for (int i = 0; i < vehiclePrefabs.Length; i++)
        {
            if (vehiclePrefabs[i] != null)
            {
                hasPrefab = true;
                break;
            }
        }

        if (!hasPrefab)
        {
            Debug.LogError($"[{name}] Vehicle Prefabs array has no valid prefabs.");
            return false;
        }

        if (maxActiveVehicles < 1)
            maxActiveVehicles = 1;

        if (minSpeed < 0.01f)
            minSpeed = 0.01f;

        if (maxSpeed < minSpeed)
            maxSpeed = minSpeed;

        if (minScaleMultiplier <= 0f)
            minScaleMultiplier = 0.01f;

        if (maxScaleMultiplier < minScaleMultiplier)
            maxScaleMultiplier = minScaleMultiplier;

        return true;
    }

    private void OnDrawGizmosSelected()
    {
        DrawBoxGizmo(spawnBox, new Color(0f, 1f, 0f, 0.25f));
        DrawBoxGizmo(targetBox, new Color(1f, 0f, 0f, 0.25f));
    }

    private void DrawBoxGizmo(BoxCollider box, Color color)
    {
        if (box == null)
            return;

        Matrix4x4 oldMatrix = Gizmos.matrix;

        Gizmos.matrix = box.transform.localToWorldMatrix;
        Gizmos.color = color;
        Gizmos.DrawCube(box.center, box.size);

        Gizmos.color = new Color(color.r, color.g, color.b, 1f);
        Gizmos.DrawWireCube(box.center, box.size);

        Gizmos.matrix = oldMatrix;
    }
}