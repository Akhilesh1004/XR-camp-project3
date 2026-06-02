using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BuildingSolidColliderGenerator
{
    private const string BuildingLayerName = "Building";
    private const string LegacyBlockerName = "__DroneSolidBuildingBlocker";
    private const string BlockerNamePrefix = "__DroneSolidBox_";
    private const float MinColliderSize = 0.05f;
    private const float DefaultBoxWorldSize = 2.5f;
    private const int DefaultMaxCellsPerAxis = 24;
    private const float ExtraFineBoxWorldSize = 1.25f;
    private const int ExtraFineMaxCellsPerAxis = 48;

    private struct GenerationSettings
    {
        public string label;
        public float boxWorldSize;
        public int maxCellsPerAxis;
        public bool mergeAdjacentCells;
    }

    private static GenerationSettings DefaultSettings => new GenerationSettings
    {
        label = "Fine",
        boxWorldSize = DefaultBoxWorldSize,
        maxCellsPerAxis = DefaultMaxCellsPerAxis,
        mergeAdjacentCells = false
    };

    private static GenerationSettings ExtraFineSettings => new GenerationSettings
    {
        label = "Extra Fine",
        boxWorldSize = ExtraFineBoxWorldSize,
        maxCellsPerAxis = ExtraFineMaxCellsPerAxis,
        mergeAdjacentCells = false
    };

    [MenuItem("Tools/Drone/Building Colliders/Generate Per MeshRenderer BoxColliders")]
    [MenuItem("Tools/Drone/Building Colliders/Generate Solid BoxColliders")]
    public static void GenerateForAllBuildingObjects()
    {
        GenerateForAllBuildingObjects(DefaultSettings);
    }

    [MenuItem("Tools/Drone/Building Colliders/Generate Extra Fine BoxColliders")]
    public static void GenerateExtraFineForAllBuildingObjects()
    {
        GenerateForAllBuildingObjects(ExtraFineSettings);
    }

    private static void GenerateForAllBuildingObjects(GenerationSettings settings)
    {
        int buildingLayer = LayerMask.NameToLayer(BuildingLayerName);

        if (buildingLayer < 0)
        {
            EditorUtility.DisplayDialog(
                "Building Layer Missing",
                "找不到名為 Building 的 Layer。請先在 Project Settings > Tags and Layers 建立 Building Layer。",
                "OK"
            );
            return;
        }

        List<GameObject> targets = FindBuildingTargets(buildingLayer);

        int created = 0;
        int skipped = 0;

        foreach (GameObject target in targets)
        {
            int before = created;

            if (!TryGenerateBlockers(target, buildingLayer, settings, ref created) || created == before)
            {
                skipped++;
            }
        }

        EditorUtility.DisplayDialog(
            settings.label + " Building BoxColliders",
            "完成依照子 MeshRenderer 產生 " + settings.label + " Building BoxCollider\n\n" +
            "Collider 僅供 Drone Grid 建圖使用，平時保持停用以降低 Physics 負擔。\n\n" +
            "Building Targets: " + targets.Count + "\n" +
            "Created BoxColliders: " + created + "\n" +
            "Skipped Targets: " + skipped,
            "OK"
        );
    }

    [MenuItem("Tools/Drone/Building Colliders/Generate For Selected Roots")]
    public static void GenerateForSelectedRoots()
    {
        GenerateForSelectedRoots(DefaultSettings);
    }

    [MenuItem("Tools/Drone/Building Colliders/Generate Extra Fine For Selected Roots")]
    public static void GenerateExtraFineForSelectedRoots()
    {
        GenerateForSelectedRoots(ExtraFineSettings);
    }

    private static void GenerateForSelectedRoots(GenerationSettings settings)
    {
        int buildingLayer = LayerMask.NameToLayer(BuildingLayerName);

        if (buildingLayer < 0)
        {
            EditorUtility.DisplayDialog(
                "Building Layer Missing",
                "找不到名為 Building 的 Layer。請先在 Project Settings > Tags and Layers 建立 Building Layer。",
                "OK"
            );
            return;
        }

        List<GameObject> targets = FindBuildingTargetsFromSelection(buildingLayer);
        int created = 0;
        int skipped = 0;

        foreach (GameObject target in targets)
        {
            int before = created;

            if (!TryGenerateBlockers(target, buildingLayer, settings, ref created) || created == before)
            {
                skipped++;
            }
        }

        EditorUtility.DisplayDialog(
            "Selected " + settings.label + " BoxColliders",
            "完成處理選取範圍，模式: " + settings.label + "\n\n" +
            "Collider 僅供 Drone Grid 建圖使用，平時保持停用以降低 Physics 負擔。\n\n" +
            "Selected Roots: " + Selection.gameObjects.Length + "\n" +
            "Building Targets: " + targets.Count + "\n" +
            "Created BoxColliders: " + created + "\n" +
            "Skipped Targets: " + skipped,
            "OK"
        );
    }

    [MenuItem("Tools/Drone/Building Colliders/Clear Generated BoxColliders")]
    public static void ClearGeneratedBlockers()
    {
        List<GameObject> blockers = FindGeneratedBlockers();

        foreach (GameObject blocker in blockers)
        {
            if (blocker == null)
            {
                continue;
            }

            Scene scene = blocker.scene;
            Undo.DestroyObjectImmediate(blocker);

            if (scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }
        }

        EditorUtility.DisplayDialog(
            "Clear Generated BoxColliders",
            "已清除工具產生的 BoxCollider 物件: " + blockers.Count,
            "OK"
        );
    }

    private static List<GameObject> FindBuildingTargets(int buildingLayer)
    {
        HashSet<GameObject> uniqueTargets = new HashSet<GameObject>();
        List<GameObject> targets = new List<GameObject>();

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);

            if (!scene.isLoaded)
            {
                continue;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                CollectBuildingTargets(root.transform, buildingLayer, uniqueTargets);
            }
        }

        targets.AddRange(uniqueTargets);
        SortByHierarchyPath(targets);
        return targets;
    }

    private static List<GameObject> FindBuildingTargetsFromSelection(int buildingLayer)
    {
        HashSet<GameObject> uniqueTargets = new HashSet<GameObject>();
        List<GameObject> targets = new List<GameObject>();

        foreach (GameObject selected in Selection.gameObjects)
        {
            if (selected == null || EditorUtility.IsPersistent(selected))
            {
                continue;
            }

            CollectBuildingTargets(selected.transform, buildingLayer, uniqueTargets);
        }

        targets.AddRange(uniqueTargets);
        SortByHierarchyPath(targets);
        return targets;
    }

    private static void CollectBuildingTargets(
        Transform current,
        int buildingLayer,
        HashSet<GameObject> uniqueTargets
    )
    {
        if (current == null || IsGeneratedBlocker(current))
        {
            return;
        }

        if (current.gameObject.layer == buildingLayer)
        {
            GameObject target = GetGenerationRoot(current, buildingLayer);

            if (target != null && HasSourceMeshRenderers(target.transform, buildingLayer))
            {
                uniqueTargets.Add(target);
            }
        }

        for (int i = 0; i < current.childCount; i++)
        {
            CollectBuildingTargets(current.GetChild(i), buildingLayer, uniqueTargets);
        }
    }

    private static GameObject GetGenerationRoot(Transform source, int buildingLayer)
    {
        GameObject prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(source.gameObject);

        if (IsValidSceneObject(prefabRoot))
        {
            return prefabRoot;
        }

        Transform root = source;

        while (root.parent != null &&
               root.parent.gameObject.layer == buildingLayer &&
               !IsGeneratedBlocker(root.parent))
        {
            root = root.parent;
        }

        return root.gameObject;
    }

    private static bool TryGenerateBlockers(
        GameObject target,
        int buildingLayer,
        GenerationSettings settings,
        ref int created
    )
    {
        if (target == null)
        {
            return false;
        }

        List<MeshRenderer> sourceRenderers = GetSourceMeshRenderers(target.transform, buildingLayer);

        if (sourceRenderers.Count == 0)
        {
            return false;
        }

        RemoveGeneratedBlockersUnder(target.transform);

        foreach (MeshRenderer renderer in sourceRenderers)
        {
            created += CreateBlockersForRenderer(renderer, buildingLayer, settings);
        }

        if (target.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(target.scene);
        }

        return true;
    }

    private static int CreateBlockersForRenderer(
        MeshRenderer renderer,
        int buildingLayer,
        GenerationSettings settings
    )
    {
        if (renderer == null || IsInsideGeneratedBlocker(renderer.transform))
        {
            return 0;
        }

        MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();

        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            return CreateSingleBlockerForRenderer(renderer, buildingLayer);
        }

        try
        {
            return CreateFineBlockersForMeshRenderer(renderer, meshFilter.sharedMesh, buildingLayer, settings);
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "Failed to create fine drone blockers for " +
                GetHierarchyPath(renderer.transform) +
                ". Falling back to one BoxCollider. " +
                exception.Message,
                renderer
            );

            return CreateSingleBlockerForRenderer(renderer, buildingLayer);
        }
    }

    private static int CreateFineBlockersForMeshRenderer(
        MeshRenderer renderer,
        Mesh mesh,
        int buildingLayer,
        GenerationSettings settings
    )
    {
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;
        Bounds localBounds = mesh.bounds;

        if (vertices == null ||
            vertices.Length == 0 ||
            triangles == null ||
            triangles.Length < 3 ||
            localBounds.size.sqrMagnitude <= 0.001f)
        {
            return CreateSingleBlockerForRenderer(renderer, buildingLayer);
        }

        int xCells = GetFineCellCount(localBounds.size.x, renderer.transform.lossyScale.x, settings);
        int zCells = GetFineCellCount(localBounds.size.z, renderer.transform.lossyScale.z, settings);

        bool[,] occupied = new bool[xCells, zCells];
        float[,] minY = new float[xCells, zCells];
        float[,] maxY = new float[xCells, zCells];

        for (int x = 0; x < xCells; x++)
        {
            for (int z = 0; z < zCells; z++)
            {
                minY[x, z] = float.PositiveInfinity;
                maxY[x, z] = float.NegativeInfinity;
            }
        }

        Vector3 boundsMin = localBounds.min;
        Vector3 boundsMax = localBounds.max;
        float cellSizeX = localBounds.size.x / xCells;
        float cellSizeZ = localBounds.size.z / zCells;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = vertices[triangles[i]];
            Vector3 b = vertices[triangles[i + 1]];
            Vector3 c = vertices[triangles[i + 2]];

            float triMinX = Mathf.Min(a.x, Mathf.Min(b.x, c.x));
            float triMaxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
            float triMinY = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
            float triMaxY = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
            float triMinZ = Mathf.Min(a.z, Mathf.Min(b.z, c.z));
            float triMaxZ = Mathf.Max(a.z, Mathf.Max(b.z, c.z));

            int startX = GetCellIndex(triMinX, boundsMin.x, cellSizeX, xCells);
            int endX = GetCellIndex(triMaxX, boundsMin.x, cellSizeX, xCells);
            int startZ = GetCellIndex(triMinZ, boundsMin.z, cellSizeZ, zCells);
            int endZ = GetCellIndex(triMaxZ, boundsMin.z, cellSizeZ, zCells);

            for (int x = startX; x <= endX; x++)
            {
                float cellMinX = boundsMin.x + x * cellSizeX;
                float cellMaxX = x == xCells - 1 ? boundsMax.x : cellMinX + cellSizeX;

                for (int z = startZ; z <= endZ; z++)
                {
                    float cellMinZ = boundsMin.z + z * cellSizeZ;
                    float cellMaxZ = z == zCells - 1 ? boundsMax.z : cellMinZ + cellSizeZ;

                    if (!TriangleOverlapsCellXZ(a, b, c, cellMinX, cellMaxX, cellMinZ, cellMaxZ))
                    {
                        continue;
                    }

                    occupied[x, z] = true;
                    minY[x, z] = Mathf.Min(minY[x, z], triMinY);
                    maxY[x, z] = Mathf.Max(maxY[x, z], triMaxY);
                }
            }
        }

        int created = settings.mergeAdjacentCells
            ? CreateMergedCellBlockers(
                renderer,
                buildingLayer,
                occupied,
                minY,
                maxY,
                boundsMin,
                boundsMax,
                cellSizeX,
                cellSizeZ,
                xCells,
                zCells
            )
            : CreateIndividualCellBlockers(
                renderer,
                buildingLayer,
                occupied,
                minY,
                maxY,
                boundsMin,
                boundsMax,
                cellSizeX,
                cellSizeZ,
                xCells,
                zCells
            );

        if (created == 0)
        {
            return CreateSingleBlockerForRenderer(renderer, buildingLayer);
        }

        return created;
    }

    private static int CreateIndividualCellBlockers(
        MeshRenderer renderer,
        int buildingLayer,
        bool[,] occupied,
        float[,] minY,
        float[,] maxY,
        Vector3 boundsMin,
        Vector3 boundsMax,
        float cellSizeX,
        float cellSizeZ,
        int xCells,
        int zCells
    )
    {
        int created = 0;

        for (int z = 0; z < zCells; z++)
        {
            float localMinZ = boundsMin.z + z * cellSizeZ;
            float localMaxZ = z == zCells - 1 ? boundsMax.z : localMinZ + cellSizeZ;

            for (int x = 0; x < xCells; x++)
            {
                if (!occupied[x, z])
                {
                    continue;
                }

                float localMinX = boundsMin.x + x * cellSizeX;
                float localMaxX = x == xCells - 1 ? boundsMax.x : localMinX + cellSizeX;

                Vector3 center = new Vector3(
                    (localMinX + localMaxX) * 0.5f,
                    (minY[x, z] + maxY[x, z]) * 0.5f,
                    (localMinZ + localMaxZ) * 0.5f
                );

                Vector3 size = new Vector3(
                    localMaxX - localMinX,
                    maxY[x, z] - minY[x, z],
                    localMaxZ - localMinZ
                );

                CreateBlockerBox(renderer, buildingLayer, created, center, size);
                created++;
            }
        }

        return created;
    }

    private static int CreateMergedCellBlockers(
        MeshRenderer renderer,
        int buildingLayer,
        bool[,] occupied,
        float[,] minY,
        float[,] maxY,
        Vector3 boundsMin,
        Vector3 boundsMax,
        float cellSizeX,
        float cellSizeZ,
        int xCells,
        int zCells
    )
    {
        int created = 0;

        for (int z = 0; z < zCells; z++)
        {
            int runStart = -1;
            float runMinY = float.PositiveInfinity;
            float runMaxY = float.NegativeInfinity;

            for (int x = 0; x <= xCells; x++)
            {
                bool hasCell = x < xCells && occupied[x, z];

                if (hasCell)
                {
                    if (runStart < 0)
                    {
                        runStart = x;
                        runMinY = minY[x, z];
                        runMaxY = maxY[x, z];
                    }
                    else
                    {
                        runMinY = Mathf.Min(runMinY, minY[x, z]);
                        runMaxY = Mathf.Max(runMaxY, maxY[x, z]);
                    }

                    continue;
                }

                if (runStart < 0)
                {
                    continue;
                }

                int runEnd = x - 1;
                float localMinX = boundsMin.x + runStart * cellSizeX;
                float localMaxX = runEnd == xCells - 1 ? boundsMax.x : boundsMin.x + (runEnd + 1) * cellSizeX;
                float localMinZ = boundsMin.z + z * cellSizeZ;
                float localMaxZ = z == zCells - 1 ? boundsMax.z : localMinZ + cellSizeZ;

                Vector3 center = new Vector3(
                    (localMinX + localMaxX) * 0.5f,
                    (runMinY + runMaxY) * 0.5f,
                    (localMinZ + localMaxZ) * 0.5f
                );

                Vector3 size = new Vector3(
                    localMaxX - localMinX,
                    runMaxY - runMinY,
                    localMaxZ - localMinZ
                );

                CreateBlockerBox(renderer, buildingLayer, created, center, size);
                created++;

                runStart = -1;
                runMinY = float.PositiveInfinity;
                runMaxY = float.NegativeInfinity;
            }
        }

        return created;
    }

    private static int CreateSingleBlockerForRenderer(MeshRenderer renderer, int buildingLayer)
    {
        Bounds localBounds;
        MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();

        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            localBounds = meshFilter.sharedMesh.bounds;
        }
        else
        {
            localBounds = renderer.localBounds;
        }

        CreateBlockerBox(renderer, buildingLayer, 0, localBounds.center, localBounds.size);
        return 1;
    }

    private static void CreateBlockerBox(
        MeshRenderer renderer,
        int buildingLayer,
        int index,
        Vector3 center,
        Vector3 size
    )
    {
        size.x = Mathf.Max(size.x, MinColliderSize);
        size.y = Mathf.Max(size.y, MinColliderSize);
        size.z = Mathf.Max(size.z, MinColliderSize);

        GameObject blocker = new GameObject(BlockerNamePrefix + renderer.gameObject.name + "_" + index.ToString("00"));
        Undo.RegisterCreatedObjectUndo(blocker, "Create Drone MeshRenderer BoxCollider");
        Undo.SetTransformParent(blocker.transform, renderer.transform, "Parent Drone MeshRenderer BoxCollider");

        blocker.layer = buildingLayer;
        blocker.hideFlags = HideFlags.None;
        blocker.transform.localPosition = Vector3.zero;
        blocker.transform.localRotation = Quaternion.identity;
        blocker.transform.localScale = Vector3.one;

        BoxCollider boxCollider = Undo.AddComponent<BoxCollider>(blocker);
        boxCollider.center = center;
        boxCollider.size = size;
        boxCollider.isTrigger = false;
        boxCollider.enabled = false;

        EditorUtility.SetDirty(blocker);
        EditorUtility.SetDirty(boxCollider);
    }

    private static int GetFineCellCount(
        float localSize,
        float lossyScale,
        GenerationSettings settings
    )
    {
        float worldSize = Mathf.Abs(localSize * lossyScale);

        if (worldSize <= 0.001f)
        {
            return 1;
        }

        return Mathf.Clamp(
            Mathf.CeilToInt(worldSize / Mathf.Max(0.1f, settings.boxWorldSize)),
            1,
            Mathf.Max(1, settings.maxCellsPerAxis)
        );
    }

    private static int GetCellIndex(float value, float min, float cellSize, int cellCount)
    {
        if (cellSize <= 0.001f)
        {
            return 0;
        }

        return Mathf.Clamp(Mathf.FloorToInt((value - min) / cellSize), 0, cellCount - 1);
    }

    private static bool TriangleOverlapsCellXZ(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        float cellMinX,
        float cellMaxX,
        float cellMinZ,
        float cellMaxZ
    )
    {
        float triMinX = Mathf.Min(a.x, Mathf.Min(b.x, c.x));
        float triMaxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
        float triMinZ = Mathf.Min(a.z, Mathf.Min(b.z, c.z));
        float triMaxZ = Mathf.Max(a.z, Mathf.Max(b.z, c.z));

        if (triMaxX < cellMinX ||
            triMinX > cellMaxX ||
            triMaxZ < cellMinZ ||
            triMinZ > cellMaxZ)
        {
            return false;
        }

        Vector2 p0 = new Vector2(a.x, a.z);
        Vector2 p1 = new Vector2(b.x, b.z);
        Vector2 p2 = new Vector2(c.x, c.z);

        if (PointInRect(p0, cellMinX, cellMaxX, cellMinZ, cellMaxZ) ||
            PointInRect(p1, cellMinX, cellMaxX, cellMinZ, cellMaxZ) ||
            PointInRect(p2, cellMinX, cellMaxX, cellMinZ, cellMaxZ))
        {
            return true;
        }

        Vector2 r0 = new Vector2(cellMinX, cellMinZ);
        Vector2 r1 = new Vector2(cellMaxX, cellMinZ);
        Vector2 r2 = new Vector2(cellMaxX, cellMaxZ);
        Vector2 r3 = new Vector2(cellMinX, cellMaxZ);

        if (PointInTriangle(r0, p0, p1, p2) ||
            PointInTriangle(r1, p0, p1, p2) ||
            PointInTriangle(r2, p0, p1, p2) ||
            PointInTriangle(r3, p0, p1, p2))
        {
            return true;
        }

        return SegmentIntersectsRect(p0, p1, r0, r1, r2, r3) ||
               SegmentIntersectsRect(p1, p2, r0, r1, r2, r3) ||
               SegmentIntersectsRect(p2, p0, r0, r1, r2, r3);
    }

    private static bool PointInRect(
        Vector2 point,
        float minX,
        float maxX,
        float minZ,
        float maxZ
    )
    {
        return point.x >= minX &&
               point.x <= maxX &&
               point.y >= minZ &&
               point.y <= maxZ;
    }

    private static bool PointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
    {
        float area = Cross(b - a, c - a);

        if (Mathf.Abs(area) <= 0.00001f)
        {
            return false;
        }

        float s = Cross(c - a, point - a) / area;
        float t = Cross(point - a, b - a) / area;
        float u = 1f - s - t;

        const float epsilon = -0.0001f;
        return s >= epsilon && t >= epsilon && u >= epsilon;
    }

    private static bool SegmentIntersectsRect(
        Vector2 a,
        Vector2 b,
        Vector2 r0,
        Vector2 r1,
        Vector2 r2,
        Vector2 r3
    )
    {
        return SegmentsIntersect(a, b, r0, r1) ||
               SegmentsIntersect(a, b, r1, r2) ||
               SegmentsIntersect(a, b, r2, r3) ||
               SegmentsIntersect(a, b, r3, r0);
    }

    private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float abC = Cross(b - a, c - a);
        float abD = Cross(b - a, d - a);
        float cdA = Cross(d - c, a - c);
        float cdB = Cross(d - c, b - c);

        if (((abC > 0f && abD < 0f) || (abC < 0f && abD > 0f)) &&
            ((cdA > 0f && cdB < 0f) || (cdA < 0f && cdB > 0f)))
        {
            return true;
        }

        const float epsilon = 0.00001f;
        return Mathf.Abs(abC) <= epsilon && PointOnSegment(c, a, b) ||
               Mathf.Abs(abD) <= epsilon && PointOnSegment(d, a, b) ||
               Mathf.Abs(cdA) <= epsilon && PointOnSegment(a, c, d) ||
               Mathf.Abs(cdB) <= epsilon && PointOnSegment(b, c, d);
    }

    private static bool PointOnSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        const float epsilon = 0.0001f;

        return point.x >= Mathf.Min(a.x, b.x) - epsilon &&
               point.x <= Mathf.Max(a.x, b.x) + epsilon &&
               point.y >= Mathf.Min(a.y, b.y) - epsilon &&
               point.y <= Mathf.Max(a.y, b.y) + epsilon;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static List<MeshRenderer> GetSourceMeshRenderers(Transform target, int buildingLayer)
    {
        List<MeshRenderer> sourceRenderers = new List<MeshRenderer>();
        MeshRenderer[] renderers = target.GetComponentsInChildren<MeshRenderer>(true);
        HashSet<Renderer> lowerLODRenderers = GetLowerLODRenderers(target);

        foreach (MeshRenderer renderer in renderers)
        {
            if (renderer == null ||
                IsInsideGeneratedBlocker(renderer.transform) ||
                lowerLODRenderers.Contains(renderer))
            {
                continue;
            }

            if (!HasBuildingLayerInHierarchy(renderer.transform, target, buildingLayer))
            {
                continue;
            }

            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();

            if (meshFilter == null && renderer.localBounds.size.sqrMagnitude <= 0.001f)
            {
                continue;
            }

            sourceRenderers.Add(renderer);
        }

        return sourceRenderers;
    }

    private static bool HasSourceMeshRenderers(Transform target, int buildingLayer)
    {
        return GetSourceMeshRenderers(target, buildingLayer).Count > 0;
    }

    private static HashSet<Renderer> GetLowerLODRenderers(Transform target)
    {
        HashSet<Renderer> lowerLODRenderers = new HashSet<Renderer>();
        LODGroup[] lodGroups = target.GetComponentsInChildren<LODGroup>(true);

        foreach (LODGroup lodGroup in lodGroups)
        {
            if (lodGroup == null || IsInsideGeneratedBlocker(lodGroup.transform))
            {
                continue;
            }

            LOD[] lods = lodGroup.GetLODs();

            for (int i = 1; i < lods.Length; i++)
            {
                Renderer[] renderers = lods[i].renderers;

                for (int j = 0; j < renderers.Length; j++)
                {
                    if (renderers[j] != null)
                    {
                        lowerLODRenderers.Add(renderers[j]);
                    }
                }
            }
        }

        return lowerLODRenderers;
    }

    private static bool HasBuildingLayerInHierarchy(
        Transform source,
        Transform stopAt,
        int buildingLayer
    )
    {
        Transform current = source;

        while (current != null)
        {
            if (current.gameObject.layer == buildingLayer)
            {
                return true;
            }

            if (current == stopAt)
            {
                break;
            }

            current = current.parent;
        }

        return false;
    }

    private static void RemoveGeneratedBlockersUnder(Transform target)
    {
        List<GameObject> blockers = new List<GameObject>();
        CollectGeneratedBlockers(target, blockers);

        foreach (GameObject blocker in blockers)
        {
            if (blocker != null)
            {
                Undo.DestroyObjectImmediate(blocker);
            }
        }
    }

    private static List<GameObject> FindGeneratedBlockers()
    {
        List<GameObject> blockers = new List<GameObject>();

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);

            if (!scene.isLoaded)
            {
                continue;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                CollectGeneratedBlockers(root.transform, blockers);
            }
        }

        return blockers;
    }

    private static void CollectGeneratedBlockers(Transform current, List<GameObject> blockers)
    {
        if (current == null)
        {
            return;
        }

        if (IsGeneratedBlocker(current))
        {
            blockers.Add(current.gameObject);
            return;
        }

        for (int i = 0; i < current.childCount; i++)
        {
            CollectGeneratedBlockers(current.GetChild(i), blockers);
        }
    }

    private static bool IsInsideGeneratedBlocker(Transform transform)
    {
        while (transform != null)
        {
            if (IsGeneratedBlocker(transform))
            {
                return true;
            }

            transform = transform.parent;
        }

        return false;
    }

    private static bool IsGeneratedBlocker(Transform transform)
    {
        return transform != null &&
               (transform.name == LegacyBlockerName ||
                transform.name.StartsWith(BlockerNamePrefix, StringComparison.Ordinal));
    }

    private static bool IsValidSceneObject(GameObject gameObject)
    {
        return gameObject != null &&
               gameObject.scene.IsValid() &&
               gameObject.scene.isLoaded &&
               !EditorUtility.IsPersistent(gameObject);
    }

    private static void SortByHierarchyPath(List<GameObject> gameObjects)
    {
        gameObjects.Sort((a, b) => string.Compare(
            GetHierarchyPath(a.transform),
            GetHierarchyPath(b.transform),
            StringComparison.Ordinal
        ));
    }

    private static string GetHierarchyPath(Transform transform)
    {
        if (transform == null)
        {
            return string.Empty;
        }

        string path = transform.name;

        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }

        return path;
    }
}
