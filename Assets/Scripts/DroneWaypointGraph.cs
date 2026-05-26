using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class DroneWaypointGraph : MonoBehaviour
{
    [Header("3D Grid Volume")]
    public Vector3 gridCenter = new Vector3(0f, 80f, 0f);
    public Vector3 gridSize = new Vector3(1200f, 160f, 900f);

    [Header("Grid Resolution")]
    [Tooltip("大場景建議 8~12。越小越精準，但 build / A* 越重。")]
    public float cellSize = 8f;

    [Tooltip("無人機通行半徑。DroneNPC2 BoxCollider 2x1x2 時，建議 2。")]
    public float agentRadius = 2f;

    [Tooltip("直線檢查半徑。0 = 使用 agentRadius。")]
    public float lineCheckRadius = 2f;

    [Header("Obstacle")]
    [Tooltip("只放 Building / Ground / Wall 這類靜態障礙。不要包含 Drone / Player / Bullet。")]
    public LayerMask obstacleLayer;

    [Header("Build Settings")]
    public bool buildOnStart = true;
    public bool autoRebuildInEditMode = false;
    public int nearestWalkableSearchRadius = 10;

    [Header("A* Settings")]
    [Tooltip("效能與穩定優先，預設 false。true 會比較順，但需要防切角。")]
    public bool allowDiagonalMovement = false;

    [Tooltip("避免超大場景搜尋卡死。若常找不到路可調高，但會更吃效能。")]
    public int maxSearchNodes = 30000;

    [Tooltip("穩定優先預設 false。true 會減少節點，但可能靠近牆角。")]
    public bool smoothPath = false;

    [Range(0f, 0.25f)]
    public float pathRandomness = 0.02f;

    [Header("Random Grid Point Settings")]
    [Tooltip("從已經快取的 walkable cells 中，隨機抽樣尋找符合距離條件的點。失敗後會 fallback 掃 walkable cache。")]
    public int randomFilteredPointMaxAttempts = 64;

    [Header("Debug Gizmos")]
    public bool drawGridBounds = true;
    public bool drawGridCells = false;
    public bool drawOnlyBlockedCells = false;
    public int drawCellStep = 8;
    public int maxGizmoCells = 3000;

    public Color boundsColor = Color.white;
    public Color walkableCellColor = new Color(0f, 1f, 1f, 0.15f);
    public Color blockedCellColor = new Color(1f, 0f, 0f, 0.25f);

    [Header("Build Info")]
    [SerializeField] private bool gridDirty = true;
    [SerializeField] private int gridCountX = 0;
    [SerializeField] private int gridCountY = 0;
    [SerializeField] private int gridCountZ = 0;
    [SerializeField] private int totalCellCount = 0;
    [SerializeField] private int walkableCellCount = 0;
    [SerializeField] private string lastBuildMessage = "Not built yet";

    private Vector3 gridMin;
    private bool[] walkableCells;

    private readonly List<int> walkableCellIndices = new List<int>();
    private readonly List<int> reusablePathIndices = new List<int>();
    private readonly List<Vector3> reusableRawPath = new List<Vector3>();
    private readonly List<Vector3> reusableSmoothedPath = new List<Vector3>();
    private readonly List<Vector3Int> neighborOffsets = new List<Vector3Int>();
    private readonly List<float> neighborMoveCosts = new List<float>();

    // A* reusable buffers: avoid per-path large allocations / GC spikes.
    private float[] gScore;
    private int[] cameFrom;
    private int[] openedStamp;
    private int[] closedStamp;
    private int searchId = 0;
    private MinHeap openHeap;

    public int TotalCellCount => totalCellCount;
    public int WalkableCellCount => walkableCellCount;
    public string LastBuildMessage => lastBuildMessage;
    public bool IsReady => walkableCells != null && walkableCells.Length > 0 && !gridDirty;

    void Start()
    {
        // Guarded to prevent double-build when a Manager calls grid APIs before this Start().
        if (Application.isPlaying && buildOnStart && !IsReady)
        {
            BuildGraph();
        }
    }

    void OnValidate()
    {
        cellSize = Mathf.Max(0.2f, cellSize);
        agentRadius = Mathf.Max(0.01f, agentRadius);
        lineCheckRadius = Mathf.Max(0f, lineCheckRadius);
        nearestWalkableSearchRadius = Mathf.Max(1, nearestWalkableSearchRadius);
        maxSearchNodes = Mathf.Max(100, maxSearchNodes);
        randomFilteredPointMaxAttempts = Mathf.Max(1, randomFilteredPointMaxAttempts);
        drawCellStep = Mathf.Max(1, drawCellStep);

        gridDirty = true;

        if (!Application.isPlaying && autoRebuildInEditMode)
        {
            BuildGraph();
        }
    }

    [ContextMenu("Rebuild 3D Grid")]
    public void RebuildGraphFromInspector()
    {
        BuildGraph();
    }

    [ContextMenu("Clear 3D Grid")]
    public void ClearGraphFromInspector()
    {
        walkableCells = null;
        walkableCellIndices.Clear();

        gScore = null;
        cameFrom = null;
        openedStamp = null;
        closedStamp = null;
        openHeap = null;

        gridCountX = 0;
        gridCountY = 0;
        gridCountZ = 0;
        totalCellCount = 0;
        walkableCellCount = 0;

        lastBuildMessage = "Grid cleared";
        gridDirty = true;
    }

    public void BuildGraph()
    {
        gridCountX = Mathf.Max(1, Mathf.CeilToInt(gridSize.x / cellSize));
        gridCountY = Mathf.Max(1, Mathf.CeilToInt(gridSize.y / cellSize));
        gridCountZ = Mathf.Max(1, Mathf.CeilToInt(gridSize.z / cellSize));

        totalCellCount = gridCountX * gridCountY * gridCountZ;

        walkableCells = new bool[totalCellCount];
        walkableCellIndices.Clear();

        gridMin = gridCenter - gridSize * 0.5f;
        walkableCellCount = 0;

        for (int x = 0; x < gridCountX; x++)
        {
            for (int y = 0; y < gridCountY; y++)
            {
                for (int z = 0; z < gridCountZ; z++)
                {
                    int index = ToIndex(x, y, z);
                    Vector3 world = CellToWorld(x, y, z);

                    bool blocked = Physics.CheckSphere(
                        world,
                        agentRadius,
                        obstacleLayer,
                        QueryTriggerInteraction.Ignore
                    );

                    walkableCells[index] = !blocked;

                    if (!blocked)
                    {
                        walkableCellCount++;
                        walkableCellIndices.Add(index);
                    }
                }
            }
        }

        BuildNeighborOffsets();
        AllocateSearchBuffers();

        gridDirty = false;

        lastBuildMessage =
            "Built 3D Grid: " +
            gridCountX + " x " +
            gridCountY + " x " +
            gridCountZ + " = " +
            totalCellCount +
            " cells, walkable = " +
            walkableCellCount;
    }

    void AllocateSearchBuffers()
    {
        gScore = new float[totalCellCount];
        cameFrom = new int[totalCellCount];
        openedStamp = new int[totalCellCount];
        closedStamp = new int[totalCellCount];
        openHeap = new MinHeap(Mathf.Min(totalCellCount, 8192));
        searchId = 0;
    }

    void BuildNeighborOffsets()
    {
        neighborOffsets.Clear();
        neighborMoveCosts.Clear();

        if (allowDiagonalMovement)
        {
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        if (x == 0 && y == 0 && z == 0)
                        {
                            continue;
                        }

                        AddNeighborOffset(x, y, z);
                    }
                }
            }
        }
        else
        {
            AddNeighborOffset(1, 0, 0);
            AddNeighborOffset(-1, 0, 0);
            AddNeighborOffset(0, 1, 0);
            AddNeighborOffset(0, -1, 0);
            AddNeighborOffset(0, 0, 1);
            AddNeighborOffset(0, 0, -1);
        }
    }

    void AddNeighborOffset(int x, int y, int z)
    {
        neighborOffsets.Add(new Vector3Int(x, y, z));
        neighborMoveCosts.Add(
            Mathf.Sqrt(x * x + y * y + z * z) * cellSize
        );
    }

    void EnsureGridReady()
    {
        if (walkableCells == null ||
            walkableCells.Length == 0 ||
            gScore == null ||
            gridDirty)
        {
            BuildGraph();
        }
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

        float radius = lineCheckRadius > 0f ? lineCheckRadius : agentRadius;

        bool blocked = Physics.SphereCast(
            from,
            radius,
            direction,
            out RaycastHit hit,
            distance,
            obstacleLayer,
            QueryTriggerInteraction.Ignore
        );

        return !blocked;
    }

    public bool IsWorldPositionWalkable(Vector3 worldPosition)
    {
        EnsureGridReady();
        return IsWorldPositionWalkableNoBuild(worldPosition);
    }

    public bool HasWalkableGridLine(Vector3 from, Vector3 to, float sampleStep = 0f)
    {
        EnsureGridReady();

        Vector3 delta = to - from;
        float distance = delta.magnitude;

        if (distance <= 0.01f)
        {
            return IsWorldPositionWalkableNoBuild(to);
        }

        float step = sampleStep > 0f ? sampleStep : Mathf.Max(1f, cellSize * 0.5f);
        int samples = Mathf.Max(1, Mathf.CeilToInt(distance / step));

        for (int i = 1; i <= samples; i++)
        {
            Vector3 sample = Vector3.Lerp(from, to, i / (float)samples);

            if (!IsWorldPositionWalkableNoBuild(sample))
            {
                return false;
            }
        }

        return true;
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

        return TryFindPathPositionsNonAlloc(
            from,
            to,
            pathPositions,
            variant,
            requireClearStart,
            requireClearGoal
        );
    }

    public bool TryFindPathPositionsNonAlloc(
        Vector3 from,
        Vector3 to,
        List<Vector3> pathPositions,
        int variant = 0,
        bool requireClearStart = false,
        bool requireClearGoal = false
    )
    {
        if (pathPositions == null)
        {
            return false;
        }

        pathPositions.Clear();

        EnsureGridReady();

        if (HasClearPath(from, to))
        {
            pathPositions.Add(to);
            return true;
        }

        int startIndex = FindNearestWalkableCellIndex(from);
        int goalIndex = FindNearestWalkableCellIndex(to);

        if (startIndex < 0 || goalIndex < 0)
        {
            return false;
        }

        bool found = FindPathCellIndices(
            startIndex,
            goalIndex,
            reusablePathIndices,
            variant
        );

        if (!found || reusablePathIndices.Count == 0)
        {
            return false;
        }

        reusableRawPath.Clear();

        for (int i = 0; i < reusablePathIndices.Count; i++)
        {
            reusableRawPath.Add(IndexToWorld(reusablePathIndices[i]));
        }

        if (smoothPath)
        {
            SmoothPath(reusableRawPath, reusableSmoothedPath);

            for (int i = 0; i < reusableSmoothedPath.Count; i++)
            {
                pathPositions.Add(reusableSmoothedPath[i]);
            }
        }
        else
        {
            for (int i = 0; i < reusableRawPath.Count; i++)
            {
                pathPositions.Add(reusableRawPath[i]);
            }
        }

        if (pathPositions.Count == 0)
        {
            return false;
        }

        Vector3 last = pathPositions[pathPositions.Count - 1];
        float snapDistance = cellSize * 0.5f;

        if ((last - to).sqrMagnitude > snapDistance * snapDistance && HasClearPath(last, to))
        {
            pathPositions.Add(to);
        }

        return true;
    }

    bool FindPathCellIndices(
        int startIndex,
        int goalIndex,
        List<int> result,
        int variant
    )
    {
        result.Clear();

        if (startIndex == goalIndex)
        {
            result.Add(startIndex);
            return true;
        }

        BeginSearch();

        IndexToCell(startIndex, out int startX, out int startY, out int startZ);
        IndexToCell(goalIndex, out int goalX, out int goalY, out int goalZ);

        gScore[startIndex] = 0f;
        cameFrom[startIndex] = -1;
        openedStamp[startIndex] = searchId;
        openHeap.Push(
            startIndex,
            HeuristicCost(startX, startY, startZ, goalX, goalY, goalZ)
        );

        int searchedNodes = 0;

        while (openHeap.Count > 0)
        {
            int current = openHeap.Pop();

            if (closedStamp[current] == searchId)
            {
                continue;
            }

            if (current == goalIndex)
            {
                ReconstructPath(cameFrom, current, result);
                return true;
            }

            closedStamp[current] = searchId;
            searchedNodes++;

            if (searchedNodes > maxSearchNodes)
            {
                return false;
            }

            IndexToCell(current, out int cx, out int cy, out int cz);

            for (int i = 0; i < neighborOffsets.Count; i++)
            {
                Vector3Int offset = neighborOffsets[i];
                float moveCost = neighborMoveCosts[i];

                int nx = cx + offset.x;
                int ny = cy + offset.y;
                int nz = cz + offset.z;

                if (!IsInsideGrid(nx, ny, nz))
                {
                    continue;
                }

                if (!IsMoveAllowedWithoutCornerCutting(cx, cy, cz, offset))
                {
                    continue;
                }

                int neighborIndex = ToIndex(nx, ny, nz);

                if (!walkableCells[neighborIndex])
                {
                    continue;
                }

                if (closedStamp[neighborIndex] == searchId)
                {
                    continue;
                }

                if (openedStamp[neighborIndex] != searchId)
                {
                    openedStamp[neighborIndex] = searchId;
                    gScore[neighborIndex] = float.MaxValue;
                    cameFrom[neighborIndex] = -1;
                }

                moveCost *= GetEdgeCostFactor(current, neighborIndex, variant);

                float tentativeG = gScore[current] + moveCost;

                if (tentativeG >= gScore[neighborIndex])
                {
                    continue;
                }

                cameFrom[neighborIndex] = current;
                gScore[neighborIndex] = tentativeG;

                float fScore =
                    tentativeG +
                    HeuristicCost(nx, ny, nz, goalX, goalY, goalZ);

                openHeap.Push(neighborIndex, fScore);
            }
        }

        return false;
    }

    void BeginSearch()
    {
        searchId++;

        if (searchId == int.MaxValue)
        {
            System.Array.Clear(openedStamp, 0, openedStamp.Length);
            System.Array.Clear(closedStamp, 0, closedStamp.Length);
            searchId = 1;
        }

        openHeap.Clear();
    }

    bool IsMoveAllowedWithoutCornerCutting(
        int cx,
        int cy,
        int cz,
        Vector3Int offset
    )
    {
        if (!allowDiagonalMovement)
        {
            return true;
        }

        int nonZeroCount = 0;

        if (offset.x != 0) nonZeroCount++;
        if (offset.y != 0) nonZeroCount++;
        if (offset.z != 0) nonZeroCount++;

        if (nonZeroCount <= 1)
        {
            return true;
        }

        // Conservative 3D corner-cut prevention.
        for (int x = 0; x <= Mathf.Abs(offset.x); x++)
        {
            for (int y = 0; y <= Mathf.Abs(offset.y); y++)
            {
                for (int z = 0; z <= Mathf.Abs(offset.z); z++)
                {
                    if (x == 0 && y == 0 && z == 0)
                    {
                        continue;
                    }

                    int sx = offset.x == 0 ? 0 : (offset.x > 0 ? x : -x);
                    int sy = offset.y == 0 ? 0 : (offset.y > 0 ? y : -y);
                    int sz = offset.z == 0 ? 0 : (offset.z > 0 ? z : -z);

                    int nx = cx + sx;
                    int ny = cy + sy;
                    int nz = cz + sz;

                    if (!IsInsideGrid(nx, ny, nz))
                    {
                        return false;
                    }

                    int index = ToIndex(nx, ny, nz);

                    if (!walkableCells[index])
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    void ReconstructPath(int[] cameFromArray, int current, List<int> result)
    {
        result.Clear();
        result.Add(current);

        while (cameFromArray[current] >= 0)
        {
            current = cameFromArray[current];
            result.Add(current);
        }

        result.Reverse();
    }

    void SmoothPath(List<Vector3> rawPath, List<Vector3> smoothedPath)
    {
        smoothedPath.Clear();

        if (rawPath == null || rawPath.Count == 0)
        {
            return;
        }

        if (rawPath.Count <= 2)
        {
            for (int i = 0; i < rawPath.Count; i++)
            {
                smoothedPath.Add(rawPath[i]);
            }

            return;
        }

        int currentIndex = 0;
        smoothedPath.Add(rawPath[currentIndex]);

        while (currentIndex < rawPath.Count - 1)
        {
            int bestIndex = currentIndex + 1;

            for (int i = rawPath.Count - 1; i > currentIndex + 1; i--)
            {
                if (HasClearPath(rawPath[currentIndex], rawPath[i]))
                {
                    bestIndex = i;
                    break;
                }
            }

            smoothedPath.Add(rawPath[bestIndex]);
            currentIndex = bestIndex;
        }
    }

    public bool TryGetRandomWalkablePoint(out Vector3 point)
    {
        EnsureGridReady();

        point = Vector3.zero;

        if (walkableCellIndices.Count == 0)
        {
            return false;
        }

        int index = walkableCellIndices[Random.Range(0, walkableCellIndices.Count)];
        point = IndexToWorld(index);
        return true;
    }

    public bool TryGetRandomWalkablePointFarFrom(
        Vector3 origin,
        float minDistance,
        out Vector3 point
    )
    {
        EnsureGridReady();

        point = Vector3.zero;

        if (walkableCellIndices.Count == 0)
        {
            return false;
        }

        float minSqr = minDistance * minDistance;

        for (int i = 0; i < randomFilteredPointMaxAttempts; i++)
        {
            int index = walkableCellIndices[Random.Range(0, walkableCellIndices.Count)];
            Vector3 candidate = IndexToWorld(index);

            if ((candidate - origin).sqrMagnitude >= minSqr)
            {
                point = candidate;
                return true;
            }
        }

        int bestIndex = -1;
        float bestDistance = -1f;

        for (int i = 0; i < walkableCellIndices.Count; i++)
        {
            int index = walkableCellIndices[i];
            Vector3 candidate = IndexToWorld(index);
            float sqr = (candidate - origin).sqrMagnitude;

            if (sqr > bestDistance)
            {
                bestDistance = sqr;
                bestIndex = index;
            }
        }

        if (bestIndex >= 0)
        {
            point = IndexToWorld(bestIndex);
            return true;
        }

        return false;
    }

    public bool TryGetRandomWalkablePointInRange(
        Vector3 origin,
        float minDistance,
        float maxDistance,
        out Vector3 point
    )
    {
        EnsureGridReady();

        point = Vector3.zero;

        if (walkableCellIndices.Count == 0)
        {
            return false;
        }

        float minSqr = minDistance * minDistance;
        float maxSqr = maxDistance > 0f
            ? maxDistance * maxDistance
            : float.MaxValue;

        for (int i = 0; i < randomFilteredPointMaxAttempts; i++)
        {
            int index = walkableCellIndices[Random.Range(0, walkableCellIndices.Count)];
            Vector3 candidate = IndexToWorld(index);
            float sqr = (candidate - origin).sqrMagnitude;

            if (sqr >= minSqr && sqr <= maxSqr)
            {
                point = candidate;
                return true;
            }
        }

        return TryGetRandomWalkablePointFarFrom(origin, minDistance, out point);
    }

    public bool TryGetRandomWalkablePointNear(
        Vector3 origin,
        float maxDistance,
        out Vector3 point
    )
    {
        EnsureGridReady();

        point = Vector3.zero;

        if (walkableCellIndices.Count == 0)
        {
            return false;
        }

        float maxSqr = maxDistance * maxDistance;

        for (int i = 0; i < randomFilteredPointMaxAttempts; i++)
        {
            int index = walkableCellIndices[Random.Range(0, walkableCellIndices.Count)];
            Vector3 candidate = IndexToWorld(index);
            float sqr = (candidate - origin).sqrMagnitude;

            if (sqr <= maxSqr)
            {
                point = candidate;
                return true;
            }
        }

        return TryGetRandomWalkablePoint(out point);
    }

    int FindNearestWalkableCellIndex(Vector3 worldPosition)
    {
        WorldToCell(
            worldPosition,
            out int startX,
            out int startY,
            out int startZ
        );

        startX = Mathf.Clamp(startX, 0, gridCountX - 1);
        startY = Mathf.Clamp(startY, 0, gridCountY - 1);
        startZ = Mathf.Clamp(startZ, 0, gridCountZ - 1);

        int startIndex = ToIndex(startX, startY, startZ);

        if (walkableCells[startIndex])
        {
            return startIndex;
        }

        int bestIndex = -1;
        float bestDistance = float.MaxValue;

        for (int r = 1; r <= nearestWalkableSearchRadius; r++)
        {
            for (int x = startX - r; x <= startX + r; x++)
            {
                for (int y = startY - r; y <= startY + r; y++)
                {
                    for (int z = startZ - r; z <= startZ + r; z++)
                    {
                        if (!IsInsideGrid(x, y, z))
                        {
                            continue;
                        }

                        int index = ToIndex(x, y, z);

                        if (!walkableCells[index])
                        {
                            continue;
                        }

                        Vector3 cellWorld = CellToWorld(x, y, z);
                        float sqr = (cellWorld - worldPosition).sqrMagnitude;

                        if (sqr < bestDistance)
                        {
                            bestDistance = sqr;
                            bestIndex = index;
                        }
                    }
                }
            }

            if (bestIndex >= 0)
            {
                return bestIndex;
            }
        }

        return -1;
    }

    float HeuristicCost(
        int fromX,
        int fromY,
        int fromZ,
        int toX,
        int toY,
        int toZ
    )
    {
        int dx = Mathf.Abs(fromX - toX);
        int dy = Mathf.Abs(fromY - toY);
        int dz = Mathf.Abs(fromZ - toZ);

        float cost = allowDiagonalMovement
            ? Mathf.Sqrt(dx * dx + dy * dy + dz * dz) * cellSize
            : (dx + dy + dz) * cellSize;

        return cost * Mathf.Max(0f, 1f - pathRandomness);
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

    void WorldToCell(Vector3 world, out int x, out int y, out int z)
    {
        Vector3 local = world - gridMin;

        x = Mathf.FloorToInt(local.x / cellSize);
        y = Mathf.FloorToInt(local.y / cellSize);
        z = Mathf.FloorToInt(local.z / cellSize);
    }

    Vector3 CellToWorld(int x, int y, int z)
    {
        return gridMin + new Vector3(
            (x + 0.5f) * cellSize,
            (y + 0.5f) * cellSize,
            (z + 0.5f) * cellSize
        );
    }

    Vector3 IndexToWorld(int index)
    {
        IndexToCell(index, out int x, out int y, out int z);
        return CellToWorld(x, y, z);
    }

    int ToIndex(int x, int y, int z)
    {
        return x + gridCountX * (y + gridCountY * z);
    }

    void IndexToCell(int index, out int x, out int y, out int z)
    {
        z = index / (gridCountX * gridCountY);
        int remain = index - z * gridCountX * gridCountY;
        y = remain / gridCountX;
        x = remain - y * gridCountX;
    }

    bool IsInsideGrid(int x, int y, int z)
    {
        return x >= 0 &&
               y >= 0 &&
               z >= 0 &&
               x < gridCountX &&
               y < gridCountY &&
               z < gridCountZ;
    }

    bool IsWorldPositionWalkableNoBuild(Vector3 worldPosition)
    {
        WorldToCell(worldPosition, out int x, out int y, out int z);

        if (!IsInsideGrid(x, y, z))
        {
            return false;
        }

        return walkableCells[ToIndex(x, y, z)];
    }

    void OnDrawGizmos()
    {
        if (drawGridBounds)
        {
            Gizmos.color = boundsColor;
            Gizmos.DrawWireCube(gridCenter, gridSize);
        }

        if (!drawGridCells ||
            walkableCells == null ||
            walkableCells.Length == 0)
        {
            return;
        }

        int drawn = 0;

        for (int x = 0; x < gridCountX; x += drawCellStep)
        {
            for (int y = 0; y < gridCountY; y += drawCellStep)
            {
                for (int z = 0; z < gridCountZ; z += drawCellStep)
                {
                    if (drawn >= maxGizmoCells)
                    {
                        return;
                    }

                    int index = ToIndex(x, y, z);

                    if (index < 0 || index >= walkableCells.Length)
                    {
                        continue;
                    }

                    bool walkable = walkableCells[index];

                    if (drawOnlyBlockedCells && walkable)
                    {
                        continue;
                    }

                    Vector3 world = CellToWorld(x, y, z);

                    Gizmos.color = walkable ? walkableCellColor : blockedCellColor;
                    Gizmos.DrawWireCube(world, Vector3.one * cellSize * 0.85f);

                    drawn++;
                }
            }
        }
    }

    private class MinHeap
    {
        private readonly List<int> items;
        private readonly List<float> priorities;

        public int Count => items.Count;

        public MinHeap(int capacity)
        {
            items = new List<int>(capacity);
            priorities = new List<float>(capacity);
        }

        public void Clear()
        {
            items.Clear();
            priorities.Clear();
        }

        public void Push(int item, float priority)
        {
            items.Add(item);
            priorities.Add(priority);
            BubbleUp(items.Count - 1);
        }

        public int Pop()
        {
            int result = items[0];

            int lastIndex = items.Count - 1;

            items[0] = items[lastIndex];
            priorities[0] = priorities[lastIndex];

            items.RemoveAt(lastIndex);
            priorities.RemoveAt(lastIndex);

            if (items.Count > 0)
            {
                BubbleDown(0);
            }

            return result;
        }

        void BubbleUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;

                if (priorities[parent] <= priorities[index])
                {
                    break;
                }

                Swap(parent, index);
                index = parent;
            }
        }

        void BubbleDown(int index)
        {
            while (true)
            {
                int left = index * 2 + 1;
                int right = index * 2 + 2;
                int smallest = index;

                if (left < items.Count && priorities[left] < priorities[smallest])
                {
                    smallest = left;
                }

                if (right < items.Count && priorities[right] < priorities[smallest])
                {
                    smallest = right;
                }

                if (smallest == index)
                {
                    break;
                }

                Swap(index, smallest);
                index = smallest;
            }
        }

        void Swap(int a, int b)
        {
            int itemTemp = items[a];
            items[a] = items[b];
            items[b] = itemTemp;

            float priorityTemp = priorities[a];
            priorities[a] = priorities[b];
            priorities[b] = priorityTemp;
        }
    }
}
