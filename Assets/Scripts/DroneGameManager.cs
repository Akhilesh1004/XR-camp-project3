using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

public class DroneGameManager : MonoBehaviour
{
    [Header("DroneNPC Prefab")]
    public DroneNPC dronePrefab;

    [Header("3D Grid")]
    public DroneWaypointGraph grid;

    [Header("場上數量")]
    public int targetDroneCount = 110;
    public bool spawnOnStart = true;
    public float respawnDelay = 3f;

    [Header("Spawn Throttle")]
    [Tooltip("一次只補一台，避免開場多台 Drone 同一幀 A* 導致 FPS spike")]
    public float spawnInterval = 0.08f;

    [Header("Object Pool")]
    public int initialPoolSize = 110;
    public bool allowPoolExpansion = true;

    [Header("Performance")]
    public float activeListCleanupInterval = 1f;

    [Header("生成設定")]
    public string playerTag = "Player";
    public bool avoidSpawnNearPlayer = true;
    public float minSpawnDistanceFromPlayer = 100f;

    [Header("Spawn Visibility")]
    [Tooltip("補 Drone 時避開相機視野，避免 object pool 啟用時直接 pop-in。")]
    public bool avoidVisibleSpawn = true;
    public int spawnPositionMaxAttempts = 12;
    public float preventVisibleSpawnDistance = 420f;
    public float spawnViewportPadding = 0.08f;

    [Header("Large World Local Population")]
    [Tooltip("只維持玩家附近固定數量的 active Drone。遠距巡邏 Drone 會回到 pool，追擊與警戒 Drone 不會被回收。")]
    public bool restrictPopulationToPlayerArea = false;
    public float localSpawnMinDistance = 80f;
    public float localSpawnMaxDistance = 300f;
    public float localRecycleDistance = 420f;
    public float localPopulationRefreshInterval = 0.5f;
    public int maxLocalRecyclesPerRefresh = 4;

    [Header("Fast Player Coverage")]
    [Tooltip("每次補 Drone 最多啟用幾台。提高可讓快速移動後前方較快補滿，但會增加該幀初始化成本。")]
    public int maxSpawnsPerFrame = 1;

    [Tooltip("玩家快速移動時，優先在玩家前方補 Drone，而不是完全隨機補在周圍。")]
    public bool biasLocalSpawnAheadOfPlayer = true;
    public float frontSpawnSpeedThreshold = 6f;
    public float frontSpawnDistance = 210f;
    public float frontSpawnRadius = 150f;
    public int frontSpawnAttempts = 10;
    public bool allowFastForwardSpawnInsideView = true;
    public float fastForwardVisibleSpawnMinDistance = 220f;

    [Tooltip("玩家快速移動時，回收後方較遠的 patrol Drone，讓 pool 名額可補到前方。")]
    public bool recycleBehindFastPlayer = true;
    public float behindRecycleDistance = 150f;
    public float behindRecycleDotThreshold = -0.15f;
    public float fastLocalPopulationRefreshInterval = 0.2f;
    public float playerVelocitySampleInterval = 0.12f;

    [Header("Visual Ring")]
    [Tooltip("外圈只顯示低成本 Drone，不啟用 DroneNPC AI / A* / 攻擊。用來讓遠方看起來仍有 Drone 流量。")]
    public bool enableVisualRing = false;
    public GameObject visualDronePrefab;
    public int visualDroneCount = 48;
    public float visualRingMinDistance = 300f;
    public float visualRingMaxDistance = 520f;
    public float visualRingRecycleDistance = 620f;
    public float visualRingSpawnInterval = 0.04f;
    public int visualRingMaxSpawnsPerFrame = 2;
    public int visualRingSpawnAttempts = 16;
    public bool visualRingBiasAheadOfFastPlayer = true;
    public float visualRingFrontDistance = 380f;
    public float visualRingFrontRadius = 180f;
    public float visualRingMoveSpeedMin = 3.5f;
    public float visualRingMoveSpeedMax = 7f;
    public float visualRingDestinationRadius = 140f;
    public float visualRingDestinationRefreshInterval = 8f;
    [Tooltip("false 時 visual-only Drone 用便宜巡航目標，不查 grid。建議 false，避免 visual ring 在大 grid 上造成 FPS spike。")]
    public bool visualRingUseGridDestinations = false;
    public float visualRingRelocateCheckInterval = 0.45f;
    public float visualRingBehindRecycleDistance = 320f;
    public float visualRingBehindRecycleDotThreshold = -0.2f;
    public bool visualRingDisableAnimators = true;
    public bool visualRingDisableColliders = true;
    public bool visualRingDisableAudio = true;

    private readonly List<DroneNPC> activeDrones = new List<DroneNPC>();
    private readonly Queue<DroneNPC> pooledDrones = new Queue<DroneNPC>();
    private readonly List<VisualDroneInstance> activeVisualDrones = new List<VisualDroneInstance>();
    private readonly Queue<VisualDroneInstance> pooledVisualDrones = new Queue<VisualDroneInstance>();

    private int pendingRespawnCount = 0;
    private Transform player;
    private Camera spawnVisibilityCamera;
    private float nextSpawnTime = 0f;
    private float nextActiveListCleanupTime = 0f;
    private float nextLocalPopulationRefreshTime = 0f;
    private float nextVisualRingSpawnTime = 0f;
    private float nextVisualRingRelocateCheckTime = 0f;
    private Vector3 lastPlayerPosition;
    private Vector3 sampledPlayerVelocity = Vector3.zero;
    private float lastPlayerVelocitySampleTime = 0f;
    private float nextPlayerVelocitySampleTime = 0f;
    private bool hasPlayerVelocitySample = false;

    private static readonly ProfilerMarker SpawnDroneMarker =
        new ProfilerMarker("Drone.Manager.SpawnActiveDrone");
    private static readonly ProfilerMarker VisualRingUpdateMarker =
        new ProfilerMarker("Drone.VisualRing.Update");
    private static readonly ProfilerMarker VisualRingSpawnMarker =
        new ProfilerMarker("Drone.VisualRing.Spawn");
    private static readonly ProfilerMarker VisualRingDestinationMarker =
        new ProfilerMarker("Drone.VisualRing.PickDestination");
    private static readonly ProfilerMarker VisualRingPositionMarker =
        new ProfilerMarker("Drone.VisualRing.PickSpawnPosition");

    private class VisualDroneInstance
    {
        public GameObject gameObject;
        public Transform transform;
        public Renderer[] renderers;
        public Animator[] animators;
        public AudioSource[] audioSources;
        public Collider[] colliders;
        public Rigidbody[] rigidbodies;
        public Vector3 destination;
        public float speed;
        public float nextDestinationTime;
    }

    void Awake()
    {
        FindPlayer();
        PrewarmPool();
        PrewarmVisualRingPool();
    }

    void Start()
    {
        nextSpawnTime = Time.time + Random.Range(0.1f, 0.4f);
    }

    void Update()
    {
        CleanupInactiveDronesThrottled();
        UpdatePlayerVelocitySample();
        RecycleDistantPatrolDronesThrottled();

        if (!spawnOnStart)
        {
            return;
        }

        if (grid == null || !grid.IsReady)
        {
            return;
        }

        UpdateVisualRing();
        TrySpawnOneIfNeeded();
    }

    void RecycleDistantPatrolDronesThrottled()
    {
        if (!restrictPopulationToPlayerArea ||
            Time.time < nextLocalPopulationRefreshTime)
        {
            return;
        }

        nextLocalPopulationRefreshTime =
            Time.time +
            GetLocalPopulationRefreshInterval();

        if (player == null)
        {
            FindPlayer();
        }

        if (player == null)
        {
            return;
        }

        float recycleDistance = Mathf.Max(localSpawnMaxDistance, localRecycleDistance);
        float recycleDistanceSqr = recycleDistance * recycleDistance;
        int maxRecycles = Mathf.Max(1, maxLocalRecyclesPerRefresh);
        int recycled = 0;

        for (int i = activeDrones.Count - 1; i >= 0; i--)
        {
            DroneNPC drone = activeDrones[i];

            if (drone == null || !drone.CanRecycleForLocalPopulation)
            {
                continue;
            }

            Vector3 toDrone = drone.transform.position - player.position;
            bool shouldRecycle =
                toDrone.sqrMagnitude > recycleDistanceSqr ||
                ShouldRecycleBehindFastPlayer(toDrone);

            if (!shouldRecycle)
            {
                continue;
            }

            activeDrones.RemoveAt(i);
            ReturnDroneToPool(drone);
            recycled++;

            if (recycled >= maxRecycles)
            {
                break;
            }
        }
    }

    float GetLocalPopulationRefreshInterval()
    {
        if (IsPlayerMovingFast())
        {
            return Mathf.Max(0.05f, fastLocalPopulationRefreshInterval);
        }

        return Mathf.Max(0.1f, localPopulationRefreshInterval);
    }

    void CleanupInactiveDronesThrottled()
    {
        if (Time.time < nextActiveListCleanupTime)
        {
            return;
        }

        nextActiveListCleanupTime = Time.time + Mathf.Max(0.1f, activeListCleanupInterval);

        for (int i = activeDrones.Count - 1; i >= 0; i--)
        {
            DroneNPC drone = activeDrones[i];

            if (drone == null || !drone.gameObject.activeSelf)
            {
                activeDrones.RemoveAt(i);
            }
        }
    }

    void TrySpawnOneIfNeeded()
    {
        if (Time.time < nextSpawnTime)
        {
            return;
        }

        int expectedCount = activeDrones.Count + pendingRespawnCount;

        if (expectedCount >= targetDroneCount)
        {
            return;
        }

        int spawnedCount = 0;
        int maxSpawns = Mathf.Max(1, maxSpawnsPerFrame);

        while (expectedCount < targetDroneCount && spawnedCount < maxSpawns)
        {
            bool spawned = SpawnOneDrone();

            if (!spawned)
            {
                nextSpawnTime = Time.time + Mathf.Max(0.5f, spawnInterval);
                return;
            }

            spawnedCount++;
            expectedCount++;
        }

        nextSpawnTime = Time.time + Mathf.Max(0.01f, spawnInterval);
    }

    void FindPlayer()
    {
        GameObject playerObject = GameObject.FindGameObjectWithTag(playerTag);

        if (playerObject != null)
        {
            player = playerObject.transform;
        }
    }

    void PrewarmPool()
    {
        if (dronePrefab == null)
        {
            Debug.LogWarning("DroneGameManager: dronePrefab 沒有設定");
            return;
        }

        int count = Mathf.Max(initialPoolSize, targetDroneCount);

        for (int i = 0; i < count; i++)
        {
            DroneNPC drone = Instantiate(dronePrefab, transform);
            drone.gameObject.SetActive(false);
            pooledDrones.Enqueue(drone);
        }
    }

    void PrewarmVisualRingPool()
    {
        if (!enableVisualRing)
        {
            return;
        }

        int count = Mathf.Max(0, visualDroneCount);

        for (int i = 0; i < count; i++)
        {
            VisualDroneInstance visualDrone = CreateVisualDroneInstance();

            if (visualDrone != null)
            {
                pooledVisualDrones.Enqueue(visualDrone);
            }
        }
    }

    VisualDroneInstance CreateVisualDroneInstance()
    {
        GameObject prefab = visualDronePrefab != null
            ? visualDronePrefab
            : (dronePrefab != null ? dronePrefab.gameObject : null);

        if (prefab == null)
        {
            return null;
        }

        GameObject obj = Instantiate(prefab, transform);
        obj.name = prefab.name + "_VisualRing";

        DroneNPC[] droneScripts = obj.GetComponentsInChildren<DroneNPC>(true);

        for (int i = 0; i < droneScripts.Length; i++)
        {
            if (droneScripts[i] != null)
            {
                droneScripts[i].enabled = false;
            }
        }

        VisualDroneInstance visualDrone = new VisualDroneInstance
        {
            gameObject = obj,
            transform = obj.transform,
            renderers = obj.GetComponentsInChildren<Renderer>(true),
            animators = obj.GetComponentsInChildren<Animator>(true),
            audioSources = obj.GetComponentsInChildren<AudioSource>(true),
            colliders = obj.GetComponentsInChildren<Collider>(true),
            rigidbodies = obj.GetComponentsInChildren<Rigidbody>(true)
        };

        ConfigureVisualDroneInstance(visualDrone);
        obj.SetActive(false);
        return visualDrone;
    }

    void ConfigureVisualDroneInstance(VisualDroneInstance visualDrone)
    {
        if (visualDrone == null)
        {
            return;
        }

        if (visualDrone.renderers != null)
        {
            for (int i = 0; i < visualDrone.renderers.Length; i++)
            {
                Renderer renderer = visualDrone.renderers[i];

                if (renderer == null)
                {
                    continue;
                }

                renderer.enabled = true;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
        }

        if (visualRingDisableAnimators && visualDrone.animators != null)
        {
            for (int i = 0; i < visualDrone.animators.Length; i++)
            {
                Animator animator = visualDrone.animators[i];

                if (animator != null)
                {
                    animator.enabled = false;
                }
            }
        }

        if (visualRingDisableAudio && visualDrone.audioSources != null)
        {
            for (int i = 0; i < visualDrone.audioSources.Length; i++)
            {
                AudioSource audioSource = visualDrone.audioSources[i];

                if (audioSource == null)
                {
                    continue;
                }

                audioSource.Stop();
                audioSource.enabled = false;
            }
        }

        if (visualRingDisableColliders && visualDrone.colliders != null)
        {
            for (int i = 0; i < visualDrone.colliders.Length; i++)
            {
                Collider collider = visualDrone.colliders[i];

                if (collider != null)
                {
                    collider.enabled = false;
                }
            }
        }

        if (visualDrone.rigidbodies != null)
        {
            for (int i = 0; i < visualDrone.rigidbodies.Length; i++)
            {
                Rigidbody rigidbody = visualDrone.rigidbodies[i];

                if (rigidbody == null)
                {
                    continue;
                }

                rigidbody.isKinematic = true;
                rigidbody.detectCollisions = false;
            }
        }
    }

    bool SpawnOneDrone()
    {
        using (SpawnDroneMarker.Auto())
        {
            if (dronePrefab == null || grid == null || !grid.IsReady)
            {
                return false;
            }

            if (!TryGetSpawnPosition(out Vector3 spawnPosition))
            {
                return false;
            }

            DroneNPC drone = GetDroneFromPool();

            if (drone == null)
            {
                return false;
            }

            Quaternion spawnRotation = Quaternion.Euler(
                0f,
                Random.Range(0f, 360f),
                0f
            );

            drone.Initialize(
                this,
                spawnPosition,
                spawnRotation,
                grid
            );

            drone.gameObject.SetActive(true);
            activeDrones.Add(drone);

            return true;
        }
    }

    bool TryGetSpawnPosition(out Vector3 spawnPosition)
    {
        int attempts = avoidVisibleSpawn
            ? Mathf.Max(1, spawnPositionMaxAttempts)
            : 1;

        for (int i = 0; i < attempts; i++)
        {
            if (!TryGetSpawnPositionCandidate(out spawnPosition))
            {
                return false;
            }

            if (!avoidVisibleSpawn ||
                !IsLikelyVisibleSpawnPosition(spawnPosition) ||
                ShouldAllowFastForwardVisibleSpawn(spawnPosition))
            {
                return true;
            }
        }

        spawnPosition = Vector3.zero;
        return false;
    }

    bool TryGetSpawnPositionCandidate(out Vector3 spawnPosition)
    {
        spawnPosition = Vector3.zero;

        if (restrictPopulationToPlayerArea)
        {
            if (player == null)
            {
                FindPlayer();
            }

            if (player == null)
            {
                return false;
            }

            if (TryGetForwardBiasedLocalSpawnPosition(out spawnPosition))
            {
                return true;
            }

            if (!grid.TryGetRandomWalkablePointInRange(
                player.position,
                localSpawnMinDistance,
                localSpawnMaxDistance,
                out spawnPosition))
            {
                return false;
            }
        }
        else if (avoidSpawnNearPlayer)
        {
            if (player == null)
            {
                FindPlayer();
            }

            if (player != null)
            {
                if (!grid.TryGetRandomWalkablePointFarFrom(
                    player.position,
                    minSpawnDistanceFromPlayer,
                    out spawnPosition))
                {
                    return false;
                }
            }
            else
            {
                if (!grid.TryGetRandomWalkablePoint(out spawnPosition))
                {
                    return false;
                }
            }
        }
        else
        {
            if (!grid.TryGetRandomWalkablePoint(out spawnPosition))
            {
                return false;
            }
        }

        return true;
    }

    bool TryGetForwardBiasedLocalSpawnPosition(out Vector3 spawnPosition)
    {
        spawnPosition = Vector3.zero;

        if (!biasLocalSpawnAheadOfPlayer ||
            !IsPlayerMovingFast() ||
            player == null ||
            grid == null)
        {
            return false;
        }

        Vector3 forward = GetPlayerMovementForward();

        if (forward.sqrMagnitude < 0.001f)
        {
            return false;
        }

        forward.Normalize();

        Vector3 sampleCenter =
            player.position +
            forward * Mathf.Max(localSpawnMinDistance, frontSpawnDistance);
        float sampleRadius = Mathf.Max(1f, frontSpawnRadius);
        float minDistanceSqr = localSpawnMinDistance * localSpawnMinDistance;
        float maxDistanceSqr = localSpawnMaxDistance * localSpawnMaxDistance;
        int attempts = Mathf.Max(1, frontSpawnAttempts);

        for (int i = 0; i < attempts; i++)
        {
            if (!grid.TryGetRandomWalkablePointNear(
                sampleCenter,
                sampleRadius,
                out Vector3 candidate))
            {
                return false;
            }

            Vector3 toCandidate = candidate - player.position;
            float sqr = toCandidate.sqrMagnitude;

            if (sqr < minDistanceSqr || sqr > maxDistanceSqr)
            {
                continue;
            }

            Vector3 flatToCandidate = Vector3.ProjectOnPlane(toCandidate, Vector3.up);

            if (flatToCandidate.sqrMagnitude < 0.001f)
            {
                continue;
            }

            float dot = Vector3.Dot(forward, flatToCandidate.normalized);

            if (dot < 0f)
            {
                continue;
            }

            spawnPosition = candidate;
            return true;
        }

        return false;
    }

    bool ShouldRecycleBehindFastPlayer(Vector3 toDrone)
    {
        if (!recycleBehindFastPlayer || !IsPlayerMovingFast())
        {
            return false;
        }

        Vector3 forward = GetPlayerMovementForward();
        Vector3 flatToDrone = Vector3.ProjectOnPlane(toDrone, Vector3.up);

        if (forward.sqrMagnitude < 0.001f ||
            flatToDrone.sqrMagnitude <
            behindRecycleDistance * behindRecycleDistance)
        {
            return false;
        }

        float dot = Vector3.Dot(forward.normalized, flatToDrone.normalized);
        return dot <= behindRecycleDotThreshold;
    }

    bool ShouldAllowFastForwardVisibleSpawn(Vector3 spawnPosition)
    {
        if (!allowFastForwardSpawnInsideView ||
            !restrictPopulationToPlayerArea ||
            !IsPlayerMovingFast() ||
            player == null)
        {
            return false;
        }

        Vector3 forward = GetPlayerMovementForward();
        Vector3 toSpawn = spawnPosition - player.position;
        Vector3 flatToSpawn = Vector3.ProjectOnPlane(toSpawn, Vector3.up);

        if (forward.sqrMagnitude < 0.001f ||
            flatToSpawn.sqrMagnitude <
            fastForwardVisibleSpawnMinDistance * fastForwardVisibleSpawnMinDistance)
        {
            return false;
        }

        return Vector3.Dot(forward.normalized, flatToSpawn.normalized) >= 0.45f;
    }

    bool IsPlayerMovingFast()
    {
        float threshold = Mathf.Max(0.1f, frontSpawnSpeedThreshold);
        return sampledPlayerVelocity.sqrMagnitude >= threshold * threshold;
    }

    Vector3 GetPlayerMovementForward()
    {
        Vector3 velocityForward = Vector3.ProjectOnPlane(sampledPlayerVelocity, Vector3.up);

        if (velocityForward.sqrMagnitude >= 0.001f)
        {
            return velocityForward.normalized;
        }

        if (spawnVisibilityCamera == null)
        {
            spawnVisibilityCamera = Camera.main;
        }

        if (spawnVisibilityCamera != null)
        {
            Vector3 cameraForward =
                Vector3.ProjectOnPlane(spawnVisibilityCamera.transform.forward, Vector3.up);

            if (cameraForward.sqrMagnitude >= 0.001f)
            {
                return cameraForward.normalized;
            }
        }

        if (player == null)
        {
            return Vector3.forward;
        }

        Vector3 playerForward = Vector3.ProjectOnPlane(player.forward, Vector3.up);
        return playerForward.sqrMagnitude >= 0.001f
            ? playerForward.normalized
            : Vector3.forward;
    }

    void UpdatePlayerVelocitySample()
    {
        if (!restrictPopulationToPlayerArea)
        {
            return;
        }

        if (player == null)
        {
            FindPlayer();
        }

        if (player == null)
        {
            hasPlayerVelocitySample = false;
            sampledPlayerVelocity = Vector3.zero;
            return;
        }

        float now = Time.time;

        if (!hasPlayerVelocitySample)
        {
            lastPlayerPosition = player.position;
            lastPlayerVelocitySampleTime = now;
            nextPlayerVelocitySampleTime =
                now + Mathf.Max(0.02f, playerVelocitySampleInterval);
            hasPlayerVelocitySample = true;
            sampledPlayerVelocity = Vector3.zero;
            return;
        }

        if (now < nextPlayerVelocitySampleTime)
        {
            return;
        }

        float dt = Mathf.Max(0.001f, now - lastPlayerVelocitySampleTime);
        Vector3 currentPosition = player.position;
        Vector3 velocity = (currentPosition - lastPlayerPosition) / dt;
        sampledPlayerVelocity = Vector3.Lerp(sampledPlayerVelocity, velocity, 0.45f);
        lastPlayerPosition = currentPosition;
        lastPlayerVelocitySampleTime = now;
        nextPlayerVelocitySampleTime =
            now + Mathf.Max(0.02f, playerVelocitySampleInterval);
    }

    void UpdateVisualRing()
    {
        using (VisualRingUpdateMarker.Auto())
        {
            if (!enableVisualRing || !spawnOnStart)
            {
                DisableAllVisualDrones();
                return;
            }

            if (player == null)
            {
                FindPlayer();
            }

            if (player == null)
            {
                return;
            }

            MoveVisualRingDrones(Time.deltaTime);
            RecycleVisualRingDronesThrottled();
            SpawnVisualRingDronesThrottled();
        }
    }

    void MoveVisualRingDrones(float dt)
    {
        if (dt <= 0f)
        {
            return;
        }

        for (int i = activeVisualDrones.Count - 1; i >= 0; i--)
        {
            VisualDroneInstance visualDrone = activeVisualDrones[i];

            if (!IsValidActiveVisualDrone(visualDrone))
            {
                activeVisualDrones.RemoveAt(i);
                continue;
            }

            Vector3 position = visualDrone.transform.position;
            Vector3 toDestination = visualDrone.destination - position;

            if (toDestination.sqrMagnitude <= 16f ||
                Time.time >= visualDrone.nextDestinationTime)
            {
                AssignVisualRingDestination(visualDrone);
                toDestination = visualDrone.destination - position;
            }

            if (toDestination.sqrMagnitude <= 0.01f)
            {
                continue;
            }

            Vector3 direction = toDestination.normalized;
            float speed = Mathf.Max(0f, visualDrone.speed);
            visualDrone.transform.position =
                Vector3.MoveTowards(position, visualDrone.destination, speed * dt);

            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
                visualDrone.transform.rotation = Quaternion.Slerp(
                    visualDrone.transform.rotation,
                    targetRotation,
                    dt * 2.5f
                );
            }
        }
    }

    void RecycleVisualRingDronesThrottled()
    {
        if (Time.time < nextVisualRingRelocateCheckTime)
        {
            return;
        }

        float interval = Mathf.Max(0.05f, visualRingRelocateCheckInterval);
        nextVisualRingRelocateCheckTime =
            Time.time +
            interval +
            Random.Range(0f, interval * 0.25f);

        int targetCount = Mathf.Max(0, visualDroneCount);

        for (int i = activeVisualDrones.Count - 1; i >= 0; i--)
        {
            VisualDroneInstance visualDrone = activeVisualDrones[i];

            if (!IsValidActiveVisualDrone(visualDrone))
            {
                activeVisualDrones.RemoveAt(i);
                continue;
            }

            if (activeVisualDrones.Count > targetCount ||
                ShouldRecycleVisualDrone(visualDrone))
            {
                activeVisualDrones.RemoveAt(i);
                ReturnVisualDroneToPool(visualDrone);
            }
        }
    }

    void SpawnVisualRingDronesThrottled()
    {
        using (VisualRingSpawnMarker.Auto())
        {
            int targetCount = Mathf.Max(0, visualDroneCount);

            if (activeVisualDrones.Count >= targetCount ||
                Time.time < nextVisualRingSpawnTime)
            {
                return;
            }

            int maxSpawns = Mathf.Max(1, visualRingMaxSpawnsPerFrame);
            int spawned = 0;

            while (activeVisualDrones.Count < targetCount && spawned < maxSpawns)
            {
                if (!TryGetVisualRingPosition(out Vector3 spawnPosition))
                {
                    break;
                }

                VisualDroneInstance visualDrone = GetVisualDroneFromPool();

                if (visualDrone == null)
                {
                    break;
                }

                ActivateVisualDrone(visualDrone, spawnPosition);
                spawned++;
            }

            nextVisualRingSpawnTime =
                Time.time +
                Mathf.Max(0.01f, visualRingSpawnInterval);
        }
    }

    bool ShouldRecycleVisualDrone(VisualDroneInstance visualDrone)
    {
        if (visualDrone == null || player == null)
        {
            return true;
        }

        Vector3 toDrone = visualDrone.transform.position - player.position;
        float distanceSqr = toDrone.sqrMagnitude;
        float minDistance = Mathf.Max(1f, visualRingMinDistance * 0.85f);
        float recycleDistance = Mathf.Max(visualRingMaxDistance, visualRingRecycleDistance);

        if (distanceSqr < minDistance * minDistance ||
            distanceSqr > recycleDistance * recycleDistance)
        {
            return true;
        }

        if (!IsPlayerMovingFast())
        {
            return false;
        }

        Vector3 forward = GetPlayerMovementForward();
        Vector3 flatToDrone = Vector3.ProjectOnPlane(toDrone, Vector3.up);

        if (forward.sqrMagnitude < 0.001f ||
            flatToDrone.sqrMagnitude <
            visualRingBehindRecycleDistance * visualRingBehindRecycleDistance)
        {
            return false;
        }

        float dot = Vector3.Dot(forward.normalized, flatToDrone.normalized);
        return dot <= visualRingBehindRecycleDotThreshold;
    }

    bool TryGetVisualRingPosition(out Vector3 position)
    {
        using (VisualRingPositionMarker.Auto())
        {
            position = Vector3.zero;

            if (grid == null || player == null)
            {
                return false;
            }

            if (visualRingBiasAheadOfFastPlayer &&
                IsPlayerMovingFast() &&
                TryGetForwardBiasedVisualRingPosition(out position))
            {
                return true;
            }

            int attempts = Mathf.Max(1, visualRingSpawnAttempts);

            for (int i = 0; i < attempts; i++)
            {
                if (!grid.TryGetRandomWalkablePointInRangeFast(
                    player.position,
                    visualRingMinDistance,
                    visualRingMaxDistance,
                    visualRingSpawnAttempts,
                    out Vector3 candidate))
                {
                    return false;
                }

                if (IsValidVisualRingPosition(candidate))
                {
                    position = candidate;
                    return true;
                }
            }

            return false;
        }
    }

    bool TryGetForwardBiasedVisualRingPosition(out Vector3 position)
    {
        position = Vector3.zero;

        Vector3 forward = GetPlayerMovementForward();

        if (forward.sqrMagnitude < 0.001f)
        {
            return false;
        }

        forward.Normalize();

        Vector3 center =
            player.position +
            forward * Mathf.Max(visualRingMinDistance, visualRingFrontDistance);
        float radius = Mathf.Max(1f, visualRingFrontRadius);
        int attempts = Mathf.Max(1, visualRingSpawnAttempts);

        for (int i = 0; i < attempts; i++)
        {
            if (!grid.TryGetRandomWalkablePointNearFast(
                center,
                radius,
                visualRingSpawnAttempts,
                out Vector3 candidate))
            {
                return false;
            }

            if (!IsValidVisualRingPosition(candidate))
            {
                continue;
            }

            Vector3 flatToCandidate =
                Vector3.ProjectOnPlane(candidate - player.position, Vector3.up);

            if (flatToCandidate.sqrMagnitude < 0.001f ||
                Vector3.Dot(forward, flatToCandidate.normalized) < 0f)
            {
                continue;
            }

            position = candidate;
            return true;
        }

        return false;
    }

    bool IsValidVisualRingPosition(Vector3 position)
    {
        if (player == null)
        {
            return false;
        }

        Vector3 toPosition = position - player.position;
        float distanceSqr = toPosition.sqrMagnitude;
        float minDistance = Mathf.Max(1f, visualRingMinDistance);
        float maxDistance = Mathf.Max(minDistance, visualRingMaxDistance);

        return distanceSqr >= minDistance * minDistance &&
               distanceSqr <= maxDistance * maxDistance;
    }

    void ActivateVisualDrone(VisualDroneInstance visualDrone, Vector3 position)
    {
        if (visualDrone == null)
        {
            return;
        }

        visualDrone.transform.position = position;
        visualDrone.transform.rotation =
            Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        visualDrone.speed = Random.Range(
            Mathf.Max(0f, visualRingMoveSpeedMin),
            Mathf.Max(visualRingMoveSpeedMin, visualRingMoveSpeedMax)
        );

        ConfigureVisualDroneInstance(visualDrone);
        AssignVisualRingDestination(visualDrone);
        visualDrone.gameObject.SetActive(true);
        activeVisualDrones.Add(visualDrone);
    }

    void AssignVisualRingDestination(VisualDroneInstance visualDrone)
    {
        if (visualDrone == null)
        {
            return;
        }

        if (!TryGetVisualRingDestination(visualDrone, out Vector3 destination))
        {
            destination = GetCheapVisualRingDestination(visualDrone);
        }

        visualDrone.destination = destination;
        float interval = Mathf.Max(1f, visualRingDestinationRefreshInterval);
        visualDrone.nextDestinationTime =
            Time.time +
            interval +
            Random.Range(0f, interval * 0.5f);
    }

    bool TryGetVisualRingDestination(
        VisualDroneInstance visualDrone,
        out Vector3 destination
    )
    {
        using (VisualRingDestinationMarker.Auto())
        {
            destination = Vector3.zero;

            if (grid == null || player == null || visualDrone == null)
            {
                return false;
            }

            if (!visualRingUseGridDestinations)
            {
                destination = GetCheapVisualRingDestination(visualDrone);
                return true;
            }

            destination = visualDrone.transform.position;

            Vector3 center =
                visualDrone.transform.position +
                visualDrone.transform.forward *
                Mathf.Max(16f, visualRingDestinationRadius * 0.35f);
            float radius = Mathf.Max(8f, visualRingDestinationRadius);

            for (int i = 0; i < 4; i++)
            {
                if (!grid.TryGetRandomWalkablePointNearFast(
                    center,
                    radius,
                    visualRingSpawnAttempts,
                    out Vector3 candidate))
                {
                    return false;
                }

                if (IsValidVisualRingPosition(candidate))
                {
                    destination = candidate;
                    return true;
                }
            }

            return TryGetVisualRingPosition(out destination);
        }
    }

    Vector3 GetCheapVisualRingDestination(VisualDroneInstance visualDrone)
    {
        if (visualDrone == null || visualDrone.transform == null)
        {
            return Vector3.zero;
        }

        Vector3 position = visualDrone.transform.position;
        Vector3 direction = Vector3.ProjectOnPlane(visualDrone.transform.forward, Vector3.up);

        if (direction.sqrMagnitude < 0.001f)
        {
            direction = Random.insideUnitSphere;
            direction.y = 0f;
        }

        if (direction.sqrMagnitude < 0.001f)
        {
            direction = Vector3.forward;
        }

        direction.Normalize();
        direction = Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f) * direction;

        float distance = Random.Range(
            Mathf.Max(12f, visualRingDestinationRadius * 0.45f),
            Mathf.Max(24f, visualRingDestinationRadius)
        );
        Vector3 destination = position + direction * distance;
        destination.y = position.y + Random.Range(-8f, 8f);

        if (player == null)
        {
            return destination;
        }

        Vector3 fromPlayer = destination - player.position;
        Vector3 flatFromPlayer = Vector3.ProjectOnPlane(fromPlayer, Vector3.up);

        if (flatFromPlayer.sqrMagnitude < 0.001f)
        {
            flatFromPlayer = direction;
        }

        float minDistance = Mathf.Max(1f, visualRingMinDistance);
        float maxDistance = Mathf.Max(minDistance, visualRingMaxDistance);
        float flatDistance = flatFromPlayer.magnitude;

        if (flatDistance < minDistance)
        {
            flatFromPlayer = flatFromPlayer.normalized * minDistance;
        }
        else if (flatDistance > maxDistance)
        {
            flatFromPlayer = flatFromPlayer.normalized * maxDistance;
        }

        destination.x = player.position.x + flatFromPlayer.x;
        destination.z = player.position.z + flatFromPlayer.z;
        return destination;
    }

    VisualDroneInstance GetVisualDroneFromPool()
    {
        if (pooledVisualDrones.Count > 0)
        {
            return pooledVisualDrones.Dequeue();
        }

        return CreateVisualDroneInstance();
    }

    void ReturnVisualDroneToPool(VisualDroneInstance visualDrone)
    {
        if (visualDrone == null)
        {
            return;
        }

        visualDrone.gameObject.SetActive(false);
        visualDrone.transform.SetParent(transform);
        pooledVisualDrones.Enqueue(visualDrone);
    }

    void DisableAllVisualDrones()
    {
        for (int i = activeVisualDrones.Count - 1; i >= 0; i--)
        {
            VisualDroneInstance visualDrone = activeVisualDrones[i];

            if (visualDrone != null && visualDrone.gameObject != null)
            {
                ReturnVisualDroneToPool(visualDrone);
            }
        }

        activeVisualDrones.Clear();
    }

    bool IsValidActiveVisualDrone(VisualDroneInstance visualDrone)
    {
        return visualDrone != null &&
               visualDrone.gameObject != null &&
               visualDrone.transform != null &&
               visualDrone.gameObject.activeSelf;
    }

    bool IsLikelyVisibleSpawnPosition(Vector3 position)
    {
        if (spawnVisibilityCamera == null)
        {
            spawnVisibilityCamera = Camera.main;
        }

        if (spawnVisibilityCamera == null)
        {
            return false;
        }

        float distance = Mathf.Max(1f, preventVisibleSpawnDistance);

        if ((position - spawnVisibilityCamera.transform.position).sqrMagnitude >
            distance * distance)
        {
            return false;
        }

        Vector3 viewport = spawnVisibilityCamera.WorldToViewportPoint(position);
        float padding = Mathf.Max(0f, spawnViewportPadding);

        return viewport.z > 0f &&
               viewport.x >= -padding &&
               viewport.x <= 1f + padding &&
               viewport.y >= -padding &&
               viewport.y <= 1f + padding;
    }

    DroneNPC GetDroneFromPool()
    {
        if (pooledDrones.Count > 0)
        {
            return pooledDrones.Dequeue();
        }

        if (!allowPoolExpansion)
        {
            return null;
        }

        DroneNPC drone = Instantiate(dronePrefab, transform);
        drone.gameObject.SetActive(false);
        return drone;
    }

    public void NotifyDroneExploded(DroneNPC drone)
    {
        if (drone == null)
        {
            return;
        }

        activeDrones.Remove(drone);
        ReturnDroneToPool(drone);
        pendingRespawnCount++;
        nextSpawnTime = Mathf.Max(nextSpawnTime, Time.time + respawnDelay);
        Invoke(nameof(ReleasePendingRespawn), respawnDelay);
    }

    void ReleasePendingRespawn()
    {
        pendingRespawnCount = Mathf.Max(0, pendingRespawnCount - 1);
    }

    void ReturnDroneToPool(DroneNPC drone)
    {
        if (drone == null)
        {
            return;
        }

        drone.PrepareForPool();
        drone.gameObject.SetActive(false);
        drone.transform.SetParent(transform);
        pooledDrones.Enqueue(drone);
    }
}
