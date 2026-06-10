using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;

public class DronePathRequestManager : MonoBehaviour
{
    public static DronePathRequestManager Instance { get; private set; }

    [Header("Path Request Budget")]
    [Tooltip("每幀最多跑幾次 A*。100 台無人機建議 1。")]
    public int maxRequestsPerFrame = 1;

    [Tooltip("每幀尋路預算，毫秒。注意單次 A* 仍然可能超過這個時間。")]
    public float maxMillisecondsPerFrame = 1.5f;
    public float minSecondsBetweenPathSearches = 0.04f;

    [Header("Path Cache")]
    public bool enablePathCache = true;

    [Tooltip("快取座標量化大小。越大越容易命中快取，但路線越粗略。")]
    public float cacheCellSize = 32f;

    [Tooltip("快取保留秒數。")]
    public float cacheLifeTime = 10f;

    [Tooltip("最多保留幾條路徑。")]
    public int maxCacheEntries = 1024;

    [Tooltip("false 會把快取/暫存路徑直接傳給 callback，callback 若要保留路徑必須自行複製。DroneNPC / DroneNPC2 會立即 AddRange，可關閉以減少 GC。")]
    public bool copyPathForCallbacks = false;

    private struct PathRequest
    {
        public DroneWaypointGraph grid;
        public Vector3 from;
        public Vector3 to;
        public int variant;
        public bool highPriority;
        public Action<bool, List<Vector3>> callback;
    }

    private struct CacheEntry
    {
        public List<Vector3> path;
        public float expireTime;
    }

    private struct PathCacheKey : IEquatable<PathCacheKey>
    {
        private readonly int gridId;
        private readonly int graphVersion;
        private readonly int ax;
        private readonly int ay;
        private readonly int az;
        private readonly int bx;
        private readonly int by;
        private readonly int bz;
        private readonly int variantBucket;

        public PathCacheKey(
            int gridId,
            int graphVersion,
            Vector3Int from,
            Vector3Int to,
            int variantBucket
        )
        {
            this.gridId = gridId;
            this.graphVersion = graphVersion;
            ax = from.x;
            ay = from.y;
            az = from.z;
            bx = to.x;
            by = to.y;
            bz = to.z;
            this.variantBucket = variantBucket;
        }

        public bool Equals(PathCacheKey other)
        {
            return gridId == other.gridId &&
                   graphVersion == other.graphVersion &&
                   ax == other.ax &&
                   ay == other.ay &&
                   az == other.az &&
                   bx == other.bx &&
                   by == other.by &&
                   bz == other.bz &&
                   variantBucket == other.variantBucket;
        }

        public override bool Equals(object obj)
        {
            return obj is PathCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = gridId;
                hash = hash * 397 ^ graphVersion;
                hash = hash * 397 ^ ax;
                hash = hash * 397 ^ ay;
                hash = hash * 397 ^ az;
                hash = hash * 397 ^ bx;
                hash = hash * 397 ^ by;
                hash = hash * 397 ^ bz;
                hash = hash * 397 ^ variantBucket;
                return hash;
            }
        }
    }

    private readonly Queue<PathRequest> highPriorityQueue = new Queue<PathRequest>();
    private readonly Queue<PathRequest> normalQueue = new Queue<PathRequest>();
    private readonly Dictionary<PathCacheKey, CacheEntry> pathCache = new Dictionary<PathCacheKey, CacheEntry>();
    private readonly List<PathCacheKey> keysToRemove = new List<PathCacheKey>();
    private readonly List<Vector3> reusablePathResult = new List<Vector3>();

    private readonly Stopwatch stopwatch = new Stopwatch();
    private float nextPathSearchTime = 0f;

    private static readonly ProfilerMarker ProcessRequestMarker =
        new ProfilerMarker("Drone.PathRequest.ProcessRequest");
    private static readonly ProfilerMarker PathSearchMarker =
        new ProfilerMarker("Drone.PathRequest.AStarSearch");
    private static readonly ProfilerMarker StoreCacheMarker =
        new ProfilerMarker("Drone.PathRequest.StoreCache");

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            UnityEngine.Debug.LogWarning("場上有多個 DronePathRequestManager，會使用第一個 Instance。");
        }

        maxRequestsPerFrame = Mathf.Max(1, maxRequestsPerFrame);
        maxMillisecondsPerFrame = Mathf.Max(0.25f, maxMillisecondsPerFrame);
        minSecondsBetweenPathSearches = Mathf.Max(0f, minSecondsBetweenPathSearches);
        cacheCellSize = Mathf.Max(1f, cacheCellSize);
        cacheLifeTime = Mathf.Max(0.5f, cacheLifeTime);
        maxCacheEntries = Mathf.Max(32, maxCacheEntries);
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public static DronePathRequestManager GetOrCreate()
    {
        if (Instance != null)
        {
            return Instance;
        }

        DronePathRequestManager existing = FindObjectOfType<DronePathRequestManager>();

        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }

        GameObject obj = new GameObject("DronePathRequestManager");
        Instance = obj.AddComponent<DronePathRequestManager>();
        return Instance;
    }

    public static bool RequestPath(
        DroneWaypointGraph grid,
        Vector3 from,
        Vector3 to,
        int variant,
        Action<bool, List<Vector3>> callback,
        bool highPriority = false
    )
    {
        if (grid == null || callback == null)
        {
            return false;
        }

        DronePathRequestManager manager = GetOrCreate();

        PathRequest request = new PathRequest
        {
            grid = grid,
            from = from,
            to = to,
            variant = variant,
            callback = callback,
            highPriority = highPriority
        };

        if (manager.TryReturnCachedPath(request))
        {
            return true;
        }

        if (highPriority)
        {
            manager.highPriorityQueue.Enqueue(request);
        }
        else
        {
            manager.normalQueue.Enqueue(request);
        }

        return true;
    }

    void Update()
    {
        if (Time.time < nextPathSearchTime)
        {
            CleanupCache();
            return;
        }

        int processed = 0;
        stopwatch.Restart();

        while (processed < maxRequestsPerFrame && HasPendingRequest())
        {
            if (processed > 0 &&
                stopwatch.Elapsed.TotalMilliseconds >= maxMillisecondsPerFrame)
            {
                break;
            }

            PathRequest request = DequeueRequest();
            ProcessRequest(request);
            processed++;

            if (minSecondsBetweenPathSearches > 0f)
            {
                nextPathSearchTime = Time.time + minSecondsBetweenPathSearches;
                break;
            }
        }

        stopwatch.Stop();
        CleanupCache();
    }

    bool HasPendingRequest()
    {
        return highPriorityQueue.Count > 0 || normalQueue.Count > 0;
    }

    PathRequest DequeueRequest()
    {
        if (highPriorityQueue.Count > 0)
        {
            return highPriorityQueue.Dequeue();
        }

        return normalQueue.Dequeue();
    }

    void ProcessRequest(PathRequest request)
    {
        using (ProcessRequestMarker.Auto())
        {
            if (request.grid == null)
            {
                request.callback(false, null);
                return;
            }

            if (enablePathCache && TryReturnCachedPath(request))
            {
                return;
            }

            bool found;

            using (PathSearchMarker.Auto())
            {
                found = request.grid.TryFindPathPositionsNonAlloc(
                    request.from,
                    request.to,
                    reusablePathResult,
                    request.variant,
                    false,
                    false
                );
            }

            if (found && reusablePathResult.Count > 0)
            {
                StoreCache(request, reusablePathResult);
                ReturnPath(request, reusablePathResult);
            }
            else
            {
                request.callback(false, null);
            }
        }
    }

    bool TryReturnCachedPath(PathRequest request)
    {
        if (!enablePathCache)
        {
            return false;
        }

        PathCacheKey key = MakeCacheKey(request);

        if (!pathCache.TryGetValue(key, out CacheEntry entry))
        {
            return false;
        }

        if (Time.time > entry.expireTime || entry.path == null)
        {
            pathCache.Remove(key);
            return false;
        }

        ReturnPath(request, entry.path);
        return true;
    }

    void ReturnPath(PathRequest request, List<Vector3> path)
    {
        if (copyPathForCallbacks)
        {
            request.callback(true, new List<Vector3>(path));
            return;
        }

        request.callback(true, path);
    }

    void StoreCache(PathRequest request, List<Vector3> path)
    {
        if (!enablePathCache || path == null || path.Count == 0)
        {
            return;
        }

        using (StoreCacheMarker.Auto())
        {
            if (pathCache.Count >= maxCacheEntries)
            {
                RemoveExpiredOrOldestCacheEntry();
            }

            PathCacheKey key = MakeCacheKey(request);

            pathCache[key] = new CacheEntry
            {
                path = new List<Vector3>(path),
                expireTime = Time.time + cacheLifeTime
            };
        }
    }

    void RemoveExpiredOrOldestCacheEntry()
    {
        PathCacheKey oldestKey = default(PathCacheKey);
        bool hasOldestKey = false;
        float oldestExpire = float.MaxValue;

        foreach (KeyValuePair<PathCacheKey, CacheEntry> pair in pathCache)
        {
            if (pair.Value.path == null || Time.time > pair.Value.expireTime)
            {
                oldestKey = pair.Key;
                hasOldestKey = true;
                break;
            }

            if (pair.Value.expireTime < oldestExpire)
            {
                oldestExpire = pair.Value.expireTime;
                oldestKey = pair.Key;
                hasOldestKey = true;
            }
        }

        if (hasOldestKey)
        {
            pathCache.Remove(oldestKey);
        }
    }

    void CleanupCache()
    {
        if (pathCache.Count == 0)
        {
            return;
        }

        // Do not scan full cache every frame.
        if (Time.frameCount % 120 != 0)
        {
            return;
        }

        keysToRemove.Clear();

        foreach (KeyValuePair<PathCacheKey, CacheEntry> pair in pathCache)
        {
            if (pair.Value.path == null || Time.time > pair.Value.expireTime)
            {
                keysToRemove.Add(pair.Key);
            }
        }

        for (int i = 0; i < keysToRemove.Count; i++)
        {
            pathCache.Remove(keysToRemove[i]);
        }
    }

    PathCacheKey MakeCacheKey(PathRequest request)
    {
        int gridId = request.grid != null ? request.grid.GetInstanceID() : 0;
        int graphVersion = request.grid != null ? request.grid.GraphVersion : 0;

        Vector3Int a = Quantize(request.from);
        Vector3Int b = Quantize(request.to);

        // Ignore exact variant to improve cache hit rate for large crowds.
        int variantBucket = request.variant & 1;

        return new PathCacheKey(gridId, graphVersion, a, b, variantBucket);
    }

    Vector3Int Quantize(Vector3 p)
    {
        return new Vector3Int(
            Mathf.RoundToInt(p.x / cacheCellSize),
            Mathf.RoundToInt(p.y / cacheCellSize),
            Mathf.RoundToInt(p.z / cacheCellSize)
        );
    }
}
