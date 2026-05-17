using System.Collections.Generic;
using UnityEngine;

public class DroneWaypointGraph : MonoBehaviour
{
    [Header("Waypoints")]
    public Transform[] waypoints;

    [Header("Graph 建立設定")]
    public LayerMask obstacleLayer;
    public float maxEdgeDistance = 60f;
    public int maxNeighborsPerWaypoint = 6;
    public float edgeCheckRadius = 1f;
    public bool buildOnStart = true;

    [Header("A* 路徑設定")]
    [Tooltip("讓不同 Drone 算出來的路徑有一點差異。0 = 完全最短路徑")]
    [Range(0f, 0.25f)]
    public float pathRandomness = 0.08f;

    [Tooltip("路徑快取，降低多台 Drone 重複算相同路徑的成本")]
    public bool enablePathCache = true;

    [Tooltip("路徑變體數量。越高路線越有變化，但快取數量越多")]
    public int pathVariantCount = 4;

    [Header("Debug")]
    public bool drawGraphGizmos = true;
    public Color edgeColor = Color.cyan;
    public Color waypointColor = Color.yellow;

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

    private readonly List<Neighbor>[] neighbors = new List<Neighbor>[0];
    private List<Neighbor>[] graphNeighbors;

    private readonly Dictionary<string, List<int>> pathCache = new Dictionary<string, List<int>>();

    void Start()
    {
        if (buildOnStart)
        {
            BuildGraph();
        }
    }

    public void SetWaypoints(Transform[] newWaypoints, bool rebuild = true)
    {
        waypoints = newWaypoints;

        if (rebuild)
        {
            BuildGraph();
        }
    }

    public void BuildGraph()
    {
        pathCache.Clear();

        if (waypoints == null || waypoints.Length == 0)
        {
            graphNeighbors = new List<Neighbor>[0];
            return;
        }

        graphNeighbors = new List<Neighbor>[waypoints.Length];

        for (int i = 0; i < graphNeighbors.Length; i++)
        {
            graphNeighbors[i] = new List<Neighbor>();
        }

        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null)
            {
                continue;
            }

            List<Neighbor> candidates = new List<Neighbor>();

            for (int j = 0; j < waypoints.Length; j++)
            {
                if (i == j || waypoints[j] == null)
                {
                    continue;
                }

                float distance = Vector3.Distance(
                    waypoints[i].position,
                    waypoints[j].position
                );

                if (distance > maxEdgeDistance)
                {
                    continue;
                }

                if (!HasClearPath(waypoints[i].position, waypoints[j].position))
                {
                    continue;
                }

                candidates.Add(new Neighbor(j, distance));
            }

            candidates.Sort((a, b) => a.distance.CompareTo(b.distance));

            int count = Mathf.Min(maxNeighborsPerWaypoint, candidates.Count);

            for (int k = 0; k < count; k++)
            {
                AddEdge(i, candidates[k].index, candidates[k].distance);
                AddEdge(candidates[k].index, i, candidates[k].distance);
            }
        }
    }

    void AddEdge(int from, int to, float distance)
    {
        if (from < 0 || to < 0)
        {
            return;
        }

        if (from >= graphNeighbors.Length || to >= graphNeighbors.Length)
        {
            return;
        }

        for (int i = 0; i < graphNeighbors[from].Count; i++)
        {
            if (graphNeighbors[from][i].index == to)
            {
                return;
            }
        }

        graphNeighbors[from].Add(new Neighbor(to, distance));
    }

    public bool HasClearPath(Vector3 from, Vector3 to)
    {
        if (obstacleLayer.value == 0)
        {
            return true;
        }

        Vector3 dir = to - from;
        float distance = dir.magnitude;

        if (distance <= 0.01f)
        {
            return true;
        }

        dir.Normalize();

        bool blocked = Physics.SphereCast(
            from,
            edgeCheckRadius,
            dir,
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

            if (index >= 0 && index < waypoints.Length && waypoints[index] != null)
            {
                pathPositions.Add(waypoints[index].position);
            }
        }

        return pathPositions.Count > 0;
    }

    public List<int> FindPath(int startIndex, int goalIndex, int variant = 0)
    {
        if (graphNeighbors == null || graphNeighbors.Length == 0)
        {
            BuildGraph();
        }

        if (!IsValidIndex(startIndex) || !IsValidIndex(goalIndex))
        {
            return null;
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

            foreach (Neighbor neighbor in graphNeighbors[current])
            {
                int next = neighbor.index;

                if (!IsValidIndex(next) || closed[next])
                {
                    continue;
                }

                float randomizedCost = neighbor.distance * GetEdgeCostFactor(
                    current,
                    next,
                    safeVariant
                );

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

    void OnDrawGizmosSelected()
    {
        if (!drawGraphGizmos || waypoints == null)
        {
            return;
        }

        Gizmos.color = waypointColor;

        foreach (Transform waypoint in waypoints)
        {
            if (waypoint != null)
            {
                Gizmos.DrawWireSphere(waypoint.position, 0.4f);
            }
        }

        if (graphNeighbors == null)
        {
            return;
        }

        Gizmos.color = edgeColor;

        for (int i = 0; i < graphNeighbors.Length; i++)
        {
            if (waypoints[i] == null)
            {
                continue;
            }

            foreach (Neighbor neighbor in graphNeighbors[i])
            {
                if (neighbor.index < 0 ||
                    neighbor.index >= waypoints.Length ||
                    waypoints[neighbor.index] == null)
                {
                    continue;
                }

                Gizmos.DrawLine(
                    waypoints[i].position,
                    waypoints[neighbor.index].position
                );
            }
        }
    }
}