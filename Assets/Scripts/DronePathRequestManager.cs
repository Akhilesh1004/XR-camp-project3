using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

public class DronePathRequestManager : MonoBehaviour
{
    public static DronePathRequestManager Instance { get; private set; }

    [Header("Path Request Budget")]
    [Tooltip("每幀最多跑幾次 A*。100 台無人機建議 1。")]
    public int maxRequestsPerFrame = 1;

    [Tooltip("每幀尋路預算，毫秒。注意單次 A* 仍然可能超過這個時間。")]
    public float maxMillisecondsPerFrame = 2f;

    [Header("Path Cache")]
    public bool enablePathCache = true;

    [Tooltip("快取座標量化大小。越大越容易命中快取，但路線越粗略。")]
    public float cacheCellSize = 32f;

    [Tooltip("快取保留秒數。")]
    public float cacheLifeTime = 10f;

    [Tooltip("最多保留幾條路徑。")]
    public int maxCacheEntries = 1024;

    private struct PathRequest
    {
        public DroneWaypointGraph grid;
        public Vector3 from;
        public Vector3 to;
        public int variant;
        public bool highPriority;
        public Action<bool, List<Vector3>> callback;
    }

    private class CacheEntry
    {
        public List<Vector3> path;
        public float expireTime;
    }

    private readonly Queue<PathRequest> highPriorityQueue = new Queue<PathRequest>();
    private readonly Queue<PathRequest> normalQueue = new Queue<PathRequest>();
    private readonly Dictionary<string, CacheEntry> pathCache = new Dictionary<string, CacheEntry>();
    private readonly List<string> keysToRemove = new List<string>();

    private readonly Stopwatch stopwatch = new Stopwatch();

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
        if (request.grid == null)
        {
            request.callback(false, null);
            return;
        }

        if (enablePathCache && TryReturnCachedPath(request))
        {
            return;
        }

        bool found = request.grid.TryFindPathPositions(
            request.from,
            request.to,
            out List<Vector3> path,
            request.variant,
            false,
            false
        );

        if (found && path != null)
        {
            StoreCache(request, path);
            request.callback(true, new List<Vector3>(path));
        }
        else
        {
            request.callback(false, null);
        }
    }

    bool TryReturnCachedPath(PathRequest request)
    {
        if (!enablePathCache)
        {
            return false;
        }

        string key = MakeCacheKey(request);

        if (!pathCache.TryGetValue(key, out CacheEntry entry))
        {
            return false;
        }

        if (Time.time > entry.expireTime || entry.path == null)
        {
            pathCache.Remove(key);
            return false;
        }

        request.callback(true, new List<Vector3>(entry.path));
        return true;
    }

    void StoreCache(PathRequest request, List<Vector3> path)
    {
        if (!enablePathCache || path == null || path.Count == 0)
        {
            return;
        }

        if (pathCache.Count >= maxCacheEntries)
        {
            RemoveExpiredOrOldestCacheEntry();
        }

        string key = MakeCacheKey(request);

        pathCache[key] = new CacheEntry
        {
            path = new List<Vector3>(path),
            expireTime = Time.time + cacheLifeTime
        };
    }

    void RemoveExpiredOrOldestCacheEntry()
    {
        string oldestKey = null;
        float oldestExpire = float.MaxValue;

        foreach (KeyValuePair<string, CacheEntry> pair in pathCache)
        {
            if (pair.Value == null || Time.time > pair.Value.expireTime)
            {
                oldestKey = pair.Key;
                break;
            }

            if (pair.Value.expireTime < oldestExpire)
            {
                oldestExpire = pair.Value.expireTime;
                oldestKey = pair.Key;
            }
        }

        if (!string.IsNullOrEmpty(oldestKey))
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

        foreach (KeyValuePair<string, CacheEntry> pair in pathCache)
        {
            if (pair.Value == null || Time.time > pair.Value.expireTime)
            {
                keysToRemove.Add(pair.Key);
            }
        }

        for (int i = 0; i < keysToRemove.Count; i++)
        {
            pathCache.Remove(keysToRemove[i]);
        }
    }

    string MakeCacheKey(PathRequest request)
    {
        int gridId = request.grid != null ? request.grid.GetInstanceID() : 0;

        Vector3Int a = Quantize(request.from);
        Vector3Int b = Quantize(request.to);

        // Ignore exact variant to improve cache hit rate for large crowds.
        int variantBucket = Mathf.Abs(request.variant) % 2;

        return gridId + "|" +
               a.x + "," + a.y + "," + a.z + "|" +
               b.x + "," + b.y + "," + b.z + "|" +
               variantBucket;
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
