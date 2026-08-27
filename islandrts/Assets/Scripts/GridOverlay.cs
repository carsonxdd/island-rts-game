using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Build-grid overlay. Draws the cell boundaries of every BUILDABLE terrain cell,
/// draped on the island surface.
///
/// Two things this deliberately no longer does:
///  - it does not draw a flat square at y=0 (the island rises to ~3.5m, so the old
///    grid was buried under the terrain and only visible out over the water);
///  - it does not cover water, beach fringe or cliffs — the drawn cells ARE the
///    placeable area, so the overlay doubles as a placement guide.
///
/// The whole grid is ONE mesh with <see cref="MeshTopology.Lines"/>. The old
/// implementation spawned a LineRenderer per line, which at island scale would be
/// tens of thousands of GameObjects.
/// </summary>
[ExecuteAlways]
public class GridOverlay : MonoBehaviour
{
    [Header("Size & Cells")]
    [Tooltip("Cell size — must match BuildPlacement.cellSize, or the overlay lies about where buildings snap.")]
    public float cellSize = 1f;
    [Tooltip("Fallback half-extent, used ONLY when the scene has no TerrainGrid (un-set-up scene): draws the legacy flat square at y=0.")]
    public int halfExtent = 25;

    [Header("Style")]
    public Color lineColor = new Color(1f, 1f, 1f, 0.22f);
    [Tooltip("Height above the terrain surface, so lines aren't z-fought by the ground.")]
    public float heightOffset = 0.06f;
    public bool show = false;

    // Build snapping rounds to the nearest whole cell, so a building's CENTER lands
    // on a cell coordinate — which puts the cell BOUNDARIES on the half-offsets.
    // The old overlay drew its lines through the centers: half a cell out of phase
    // with placement.
    const float BoundaryOffset = 0.5f;

    Mesh _mesh;
    Material _mat;
    bool _needsRebuild;

    readonly List<Vector3> _verts = new List<Vector3>();
    readonly List<int> _indices = new List<int>();

    void OnEnable() { Build(); }
    void OnValidate() { _needsRebuild = true; }   // can't create objects during OnValidate
    void OnDisable() { Clear(); }

    void Update()
    {
        if (_needsRebuild)
        {
            _needsRebuild = false;
            Build();
        }
    }

    /// <summary>Re-drape and re-cull the grid. Call on toggle, or after terrain changes.</summary>
    public void Rebuild() { Build(); }

    void Clear()
    {
        DestroyChildren();
        DestroySafe(_mesh); _mesh = null;
        DestroySafe(_mat); _mat = null;
    }

    void DestroyChildren()
    {
        // Our own child, plus any orphans from an earlier build or from the legacy
        // LineRenderer implementation (whose children were named "grid_line").
        var doomed = new List<GameObject>();
        foreach (Transform child in transform)
        {
            if (child.name == "grid_mesh" || child.name == "grid_line")
                doomed.Add(child.gameObject);
        }
        for (int i = 0; i < doomed.Count; i++) DestroySafe(doomed[i]);
    }

    void DestroySafe(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }

    void Build()
    {
        if (!isActiveAndEnabled) return;

        Clear();
        if (!show) return;

        _verts.Clear();
        _indices.Clear();

        TerrainGrid terrain = TerrainGrid.Instance;
        if (terrain != null) BuildBuildableCells(terrain);
        else BuildFlatSquare();

        if (_indices.Count == 0) return;

        var go = new GameObject("grid_mesh");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        go.hideFlags = HideFlags.DontSave;

        _mesh = new Mesh { name = "GridOverlay" };
        // An island-scale grid blows past the 16-bit vertex limit.
        _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        _mesh.SetVertices(_verts);
        _mesh.SetIndices(_indices, MeshTopology.Lines, 0);
        _mesh.RecalculateBounds();

        _mat = new Material(Shader.Find("Sprites/Default"));  // simple, transparent
        _mat.color = lineColor;

        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = _mesh;

        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = _mat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    /// <summary>
    /// One pass over every cell centre on the island. A buildable cell contributes
    /// its west and south edges unconditionally, and its east/north edges only when
    /// the neighbour on that side is NOT buildable — so shared interior edges are
    /// emitted exactly once and the outer boundary still closes.
    /// </summary>
    void BuildBuildableCells(TerrainGrid terrain)
    {
        // Keep the outermost boundary inside the heightfield so draped corners
        // never clamp against the map edge.
        float half = (TerrainGrid.VertsPerSide - 1) * TerrainGrid.Spacing * 0.5f;
        int halfCells = Mathf.FloorToInt((half - cellSize) / cellSize);

        for (int cx = -halfCells; cx <= halfCells; cx++)
        {
            for (int cz = -halfCells; cz <= halfCells; cz++)
            {
                float x = cx * cellSize;
                float z = cz * cellSize;
                if (!terrain.IsBuildable(new Vector3(x, 0f, z))) continue;

                float minX = x - BoundaryOffset * cellSize;
                float maxX = x + BoundaryOffset * cellSize;
                float minZ = z - BoundaryOffset * cellSize;
                float maxZ = z + BoundaryOffset * cellSize;

                AddEdge(terrain, minX, minZ, minX, maxZ);   // west
                AddEdge(terrain, minX, minZ, maxX, minZ);   // south

                if (!terrain.IsBuildable(new Vector3(x + cellSize, 0f, z)))
                    AddEdge(terrain, maxX, minZ, maxX, maxZ);   // east
                if (!terrain.IsBuildable(new Vector3(x, 0f, z + cellSize)))
                    AddEdge(terrain, minX, maxZ, maxX, maxZ);   // north
            }
        }
    }

    /// <summary>Legacy flat square — only reached when the scene has no TerrainGrid.</summary>
    void BuildFlatSquare()
    {
        float lo = (-halfExtent + BoundaryOffset) * cellSize;
        float hi = (halfExtent + BoundaryOffset) * cellSize;

        for (int i = -halfExtent; i <= halfExtent; i++)
        {
            float c = (i + BoundaryOffset) * cellSize;
            AddFlatEdge(c, lo, c, hi);
            AddFlatEdge(lo, c, hi, c);
        }
    }

    void AddEdge(TerrainGrid terrain, float x0, float z0, float x1, float z1)
    {
        _indices.Add(_verts.Count);
        _verts.Add(Drape(terrain, x0, z0));
        _indices.Add(_verts.Count);
        _verts.Add(Drape(terrain, x1, z1));
    }

    void AddFlatEdge(float x0, float z0, float x1, float z1)
    {
        _indices.Add(_verts.Count);
        _verts.Add(new Vector3(x0, heightOffset, z0));
        _indices.Add(_verts.Count);
        _verts.Add(new Vector3(x1, heightOffset, z1));
    }

    Vector3 Drape(TerrainGrid terrain, float x, float z)
    {
        return new Vector3(x, terrain.SampleHeight(new Vector3(x, 0f, z)) + heightOffset, z);
    }
}
