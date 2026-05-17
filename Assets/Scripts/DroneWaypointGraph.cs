using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class DroneWaypointGraph : MonoBehaviour
{
    [Header("Waypoints")]
    public Transform[] waypoints;

    [Header("Graph 建立設定")]
    public LayerMask obstacleLayer;

    [Tooltip("兩個 waypoint 超過這個距離就不考慮連線")]
    public float maxEdgeDistance = 60f;

    [Tooltip("每個 waypoint 最多連到幾個鄰居。waypoint 多時建議 4~8")]
    public int maxNeighborsPerWaypoint = 6;

    [Tooltip("檢查兩點之間是否被建築擋住的 SphereCast 半徑")]
    public float edgeCheckRadius = 1f;

    [Tooltip("Play Mode 開始時自動建立 Graph")]
    public bool buildOnStart = true;

    [Header("大量 Waypoint 效能設定")]
    [Tooltip("waypoint 很多時建議打開。會用空間格子先篩選附近 waypoint")]
    public bool useSpatialCandidateSearch = true;

    [Tooltip("空間格子大小。0 代表自動使用 maxEdgeDistance")]
    public float spatialCellSize = 0f;

    [Tooltip("如果 waypoint 很多，不建議打開。打開後 Edit Mode 會在參數改變時自動重建")]
    public bool autoRebuildInEditMode = false;

    [Header("A* 路徑設定")]
    [Tooltip("讓不同 Drone 算出來的路徑有一點差異。0 = 完全最短路徑")]
    [Range(0f, 0.25f)]
    public float pathRandomness = 0.08f;

    [Tooltip("路徑快取，降低多台 Drone 重複算相同路徑的成本")]
    public bool enablePathCache = true;

    [Tooltip("路徑變體數量。越高路線越有變化，但快取數量越多")]
    public int pathVariantCount = 4;

    [Header("Debug Gizmos")]
    public bool drawGraphGizmos = true;

    [Tooltip("不用選到 DroneWaypointGraph 物件也畫出 Graph")]
    public bool alwaysDrawGizmos = true;

    public Color edgeColor = Color.cyan;
    public Color waypointColor = Color.yellow;
    public Color disconnectedWaypointColor = Color.red;

    [Tooltip("畫線時避免雙向邊重複畫兩次")]
    public bool drawEachEdgeOnce = true;

    [Header("Build Info")]
    [SerializeField] private bool graphDirty = true;
    [SerializeField] private int lastBuildWaypointCount = 0;
    [SerializeField] private int lastBuildEdgeCount = 0;
    [SerializeField] private string lastBuildMessage = "Not built yet";

    private class Neighbor
    {
        public int index;
        public float distance;

        public Neighbor(int index, float distance)
        {
            this.index = index;
            this.distance = distance;
        }
    }

    private List<Neighbor>[] graphNeighbors;
    private readonly Dictionary<string, List<int>> pathCache = new Dictionary<string, List<int>>();

    private readonly Dictionary<Vector3Int, List<int>> spatialBuckets =
        new Dictionary<Vector3Int, List<int>>();

    private readonly List<Neighbor> reusableCandidates = new List<Neighbor>();
    private readonly HashSet<int> reusableCandidateSet = new HashSet<int>();

    void OnEnable()
    {
        if (Application.isPlaying && buildOnStart)
        {
            BuildGraph();
        }
    }

    void Start()
    {
        if (Application.isPlaying && buildOnStart)
        {
            BuildGraph();
        }
    }

    void OnValidate()
    {
        maxEdgeDistance = Mathf.Max(0.1f, maxEdgeDistance);
        maxNeighborsPerWaypoint = Mathf.Max(1, maxNeighborsPerWaypoint);
        edgeCheckRadius = Mathf.Max(0f, edgeCheckRadius);
        pathVariantCount = Mathf.Max(1, pathVariantCount);
        spatialCellSize = Mathf.Max(0f, spatialCellSize);

        graphDirty = true;
        pathCache.Clear();

        if (!Application.isPlaying && autoRebuildInEditMode)
        {
            BuildGraph();
        }
    }

    [ContextMenu("Rebuild Graph")]
    public void RebuildGraphFromInspector()
    {
        BuildGraph();
    }

    [ContextMenu("Clear Graph")]
    public void ClearGraphFromInspector()
    {
        graphNeighbors = null;
        pathCache.Clear();
        spatialBuckets.Clear();

        lastBuildWaypointCount = 0;
        lastBuildEdgeCount = 0;
        lastBuildMessage = "Graph cleared";
        graphDirty = true;
    }

    [ContextMenu("Clear Path Cache")]
    public void ClearPathCacheFromInspector()
    {
        pathCache.Clear();
    }

    public void SetWaypoints(Transform[] newWaypoints, bool rebuild = true)
    {
        waypoints = newWaypoints;
        graphDirty = true;
        pathCache.Clear();

        if (rebuild)
        {
            BuildGraph();
        }
    }

    public void BuildGraph()
    {
        pathCache.Clear();
        spatialBuckets.Clear();

        if (waypoints == null || waypoints.Length == 0)
        {
            graphNeighbors = new List<Neighbor>[0];
            lastBuildWaypointCount = 0;
            lastBuildEdgeCount = 0;
            lastBuildMessage = "No waypoints";
            graphDirty = false;
            return;
        }

        graphNeighbors = new List<Neighbor>[waypoints.Length];

        for (int i = 0; i < graphNeighbors.Length; i++)
        {
            graphNeighbors[i] = new List<Neighbor>();
        }

        if (useSpatialCandidateSearch)
        {
            BuildSpatialBuckets();
        }

        int validWaypointCount = 0;

        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] != null)
            {
                validWaypointCount++;
            }
        }

        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null)
            {
                continue;
            }

            reusableCandidates.Clear();

            if (useSpatialCandidateSearch)
            {
                CollectSpatialCandidates(i, reusableCandidates);
            }
            else
            {
                CollectAllCandidates(i, reusableCandidates);
            }

            reusableCandidates.Sort((a, b) => a.distance.CompareTo(b.distance));

            int count = Mathf.Min(maxNeighborsPerWaypoint, reusableCandidates.Count);

            for (int k = 0; k < count; k++)
            {
                int neighborIndex = reusableCandidates[k].index;
                float distance = reusableCandidates[k].distance;

                AddEdge(i, neighborIndex, distance);
                AddEdge(neighborIndex, i, distance);
            }
        }

        int directedEdgeCount = CountDirectedEdges();

        lastBuildWaypointCount = validWaypointCount;
        lastBuildEdgeCount = directedEdgeCount;
        lastBuildMessage =
            "Built graph: " +
            validWaypointCount +
            " waypoints, " +
            directedEdgeCount +
            " directed edges";

        graphDirty = false;
    }

    void BuildSpatialBuckets()
    {
        spatialBuckets.Clear();

        float cellSize = GetEffectiveSpatialCellSize();

        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null)
            {
                continue;
            }

            Vector3Int cell = WorldToCell(waypoints[i].position, cellSize);

            if (!spatialBuckets.TryGetValue(cell, out List<int> list))
            {
                list = new List<int>();
                spatialBuckets[cell] = list;
            }

            list.Add(i);
        }
    }

    void CollectSpatialCandidates(int index, List<Neighbor> candidates)
    {
        reusableCandidateSet.Clear();

        float cellSize = GetEffectiveSpatialCellSize();
        Vector3Int centerCell = WorldToCell(waypoints[index].position, cellSize);

        int searchRange = Mathf.CeilToInt(maxEdgeDistance / cellSize);

        for (int x = -searchRange; x <= searchRange; x++)
        {
            for (int y = -searchRange; y <= searchRange; y++)
            {
                for (int z = -searchRange; z <= searchRange; z++)
                {
                    Vector3Int cell = new Vector3Int(
                        centerCell.x + x,
                        centerCell.y + y,
                        centerCell.z + z
                    );

                    if (!spatialBuckets.TryGetValue(cell, out List<int> bucket))
                    {
                        continue;
                    }

                    for (int i = 0; i < bucket.Count; i++)
                    {
                        int candidateIndex = bucket[i];

                        if (candidateIndex == index)
                        {
                            continue;
                        }

                        if (reusableCandidateSet.Contains(candidateIndex))
                        {
                            continue;
                        }

                        reusableCandidateSet.Add(candidateIndex);
                        TryAddCandidate(index, candidateIndex, candidates);
                    }
                }
            }
        }
    }

    void CollectAllCandidates(int index, List<Neighbor> candidates)
    {
        for (int j = 0; j < waypoints.Length; j++)
        {
            if (j == index)
            {
                continue;
            }

            TryAddCandidate(index, j, candidates);
        }
    }

    void TryAddCandidate(int fromIndex, int toIndex, List<Neighbor> candidates)
    {
        if (!IsValidIndex(fromIndex) || !IsValidIndex(toIndex))
        {
            return;
        }

        float distance = Vector3.Distance(
            waypoints[fromIndex].position,
            waypoints[toIndex].position
        );

        if (distance > maxEdgeDistance)
        {
            return;
        }

        if (!HasClearPath(
            waypoints[fromIndex].position,
            waypoints[toIndex].position
        ))
        {
            return;
        }

        candidates.Add(new Neighbor(toIndex, distance));
    }

    float GetEffectiveSpatialCellSize()
    {
        if (spatialCellSize > 0.01f)
        {
            return spatialCellSize;
        }

        return Mathf.Max(1f, maxEdgeDistance);
    }

    Vector3Int WorldToCell(Vector3 position, float cellSize)
    {
        return new Vector3Int(
            Mathf.FloorToInt(position.x / cellSize),
            Mathf.FloorToInt(position.y / cellSize),
            Mathf.FloorToInt(position.z / cellSize)
        );
    }

    void AddEdge(int from, int to, float distance)
    {
        if (!IsValidIndex(from) || !IsValidIndex(to))
        {
            return;
        }

        List<Neighbor> list = graphNeighbors[from];

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].index == to)
            {
                return;
            }
        }

        list.Add(new Neighbor(to, distance));
    }

    int CountDirectedEdges()
    {
        if (graphNeighbors == null)
        {
            return 0;
        }

        int count = 0;

        for (int i = 0; i < graphNeighbors.Length; i++)
        {
            if (graphNeighbors[i] != null)
            {
                count += graphNeighbors[i].Count;
            }
        }

        return count;
    }

    public bool HasClearPath(Vector3 from, Vector3 to)
    {
        if (obstacleLayer.value == 0)
        {
            return true;
        }

        Vector3 direction = to - from;
        float distance = direction.magnitude;

        if (distance <= 0.01f)
        {
            return true;
        }

        direction.Normalize();

        bool blocked = Physics.SphereCast(
            from,
            edgeCheckRadius,
            direction,
            out RaycastHit hit,
            distance,
            obstacleLayer,
            QueryTriggerInteraction.Ignore
        );

        return !blocked;
    }

    public int GetClosestWaypointIndex(Vector3 position, bool requireClearPath)
    {
        if (waypoints == null || waypoints.Length == 0)
        {
            return -1;
        }

        int bestIndex = -1;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null)
            {
                continue;
            }

            float distance = Vector3.Distance(position, waypoints[i].position);

            if (distance >= bestDistance)
            {
                continue;
            }

            if (requireClearPath && !HasClearPath(position, waypoints[i].position))
            {
                continue;
            }

            bestDistance = distance;
            bestIndex = i;
        }

        if (bestIndex < 0 && requireClearPath)
        {
            return GetClosestWaypointIndex(position, false);
        }

        return bestIndex;
    }

    public bool TryFindPathPositions(
        Vector3 from,
        Vector3 to,
        out List<Vector3> pathPositions,
        int variant = 0,
        bool requireClearStart = false,
        bool requireClearGoal = false
    )
    {
        pathPositions = new List<Vector3>();

        EnsureGraphReady();

        int startIndex = GetClosestWaypointIndex(from, requireClearStart);
        int goalIndex = GetClosestWaypointIndex(to, requireClearGoal);

        if (startIndex < 0 || goalIndex < 0)
        {
            return false;
        }

        List<int> pathIndices = FindPath(startIndex, goalIndex, variant);

        if (pathIndices == null || pathIndices.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < pathIndices.Count; i++)
        {
            int index = pathIndices[i];

            if (IsValidIndex(index))
            {
                pathPositions.Add(waypoints[index].position);
            }
        }

        return pathPositions.Count > 0;
    }

    public List<int> FindPath(int startIndex, int goalIndex, int variant = 0)
    {
        EnsureGraphReady();

        if (!IsValidIndex(startIndex) || !IsValidIndex(goalIndex))
        {
            return null;
        }

        if (startIndex == goalIndex)
        {
            return new List<int> { startIndex };
        }

        int safeVariant = Mathf.Abs(variant);

        if (pathVariantCount > 0)
        {
            safeVariant %= pathVariantCount;
        }

        string cacheKey = startIndex + "_" + goalIndex + "_" + safeVariant;

        if (enablePathCache && pathCache.TryGetValue(cacheKey, out List<int> cached))
        {
            return new List<int>(cached);
        }

        int count = waypoints.Length;

        float[] gScore = new float[count];
        float[] fScore = new float[count];
        int[] cameFrom = new int[count];
        bool[] closed = new bool[count];

        for (int i = 0; i < count; i++)
        {
            gScore[i] = float.MaxValue;
            fScore[i] = float.MaxValue;
            cameFrom[i] = -1;
            closed[i] = false;
        }

        List<int> open = new List<int>();

        gScore[startIndex] = 0f;
        fScore[startIndex] = Heuristic(startIndex, goalIndex);
        open.Add(startIndex);

        while (open.Count > 0)
        {
            int current = GetLowestFScore(open, fScore);

            if (current == goalIndex)
            {
                List<int> path = ReconstructPath(cameFrom, current);

                if (enablePathCache)
                {
                    pathCache[cacheKey] = new List<int>(path);
                }

                return path;
            }

            open.Remove(current);
            closed[current] = true;

            if (graphNeighbors[current] == null)
            {
                continue;
            }

            foreach (Neighbor neighbor in graphNeighbors[current])
            {
                int next = neighbor.index;

                if (!IsValidIndex(next) || closed[next])
                {
                    continue;
                }

                float randomizedCost =
                    neighbor.distance * GetEdgeCostFactor(current, next, safeVariant);

                float tentativeG = gScore[current] + randomizedCost;

                if (!open.Contains(next))
                {
                    open.Add(next);
                }
                else if (tentativeG >= gScore[next])
                {
                    continue;
                }

                cameFrom[next] = current;
                gScore[next] = tentativeG;
                fScore[next] = tentativeG + Heuristic(next, goalIndex);
            }
        }

        return null;
    }

    void EnsureGraphReady()
    {
        if (graphNeighbors == null ||
            graphNeighbors.Length == 0 ||
            graphDirty)
        {
            BuildGraph();
        }
    }

    float GetEdgeCostFactor(int a, int b, int variant)
    {
        if (pathRandomness <= 0f)
        {
            return 1f;
        }

        int hash = a * 73856093 ^ b * 19349663 ^ variant * 83492791;
        hash = Mathf.Abs(hash);

        float normalized = (hash % 10000) / 10000f;
        float randomOffset = Mathf.Lerp(-pathRandomness, pathRandomness, normalized);

        return 1f + randomOffset;
    }

    int GetLowestFScore(List<int> open, float[] fScore)
    {
        int best = open[0];
        float bestScore = fScore[best];

        for (int i = 1; i < open.Count; i++)
        {
            int index = open[i];

            if (fScore[index] < bestScore)
            {
                bestScore = fScore[index];
                best = index;
            }
        }

        return best;
    }

    float Heuristic(int from, int to)
    {
        return Vector3.Distance(
            waypoints[from].position,
            waypoints[to].position
        );
    }

    List<int> ReconstructPath(int[] cameFrom, int current)
    {
        List<int> path = new List<int>();
        path.Add(current);

        while (cameFrom[current] >= 0)
        {
            current = cameFrom[current];
            path.Add(current);
        }

        path.Reverse();
        return path;
    }

    bool IsValidIndex(int index)
    {
        return waypoints != null &&
               index >= 0 &&
               index < waypoints.Length &&
               waypoints[index] != null;
    }

    bool HasAnyNeighbor(int index)
    {
        return graphNeighbors != null &&
               index >= 0 &&
               index < graphNeighbors.Length &&
               graphNeighbors[index] != null &&
               graphNeighbors[index].Count > 0;
    }

    void OnDrawGizmos()
    {
        if (!alwaysDrawGizmos)
        {
            return;
        }

        DrawGraphGizmos();
    }

    void OnDrawGizmosSelected()
    {
        if (alwaysDrawGizmos)
        {
            return;
        }

        DrawGraphGizmos();
    }

    void DrawGraphGizmos()
    {
        if (!drawGraphGizmos || waypoints == null)
        {
            return;
        }

        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null)
            {
                continue;
            }

            Gizmos.color = HasAnyNeighbor(i)
                ? waypointColor
                : disconnectedWaypointColor;

            Gizmos.DrawWireSphere(waypoints[i].position, 0.4f);
        }

        if (graphNeighbors == null)
        {
            return;
        }

        Gizmos.color = edgeColor;

        for (int i = 0; i < graphNeighbors.Length; i++)
        {
            if (!IsValidIndex(i) || graphNeighbors[i] == null)
            {
                continue;
            }

            foreach (Neighbor neighbor in graphNeighbors[i])
            {
                int j = neighbor.index;

                if (!IsValidIndex(j))
                {
                    continue;
                }

                if (drawEachEdgeOnce && j < i)
                {
                    continue;
                }

                Gizmos.DrawLine(
                    waypoints[i].position,
                    waypoints[j].position
                );
            }
        }
    }
}