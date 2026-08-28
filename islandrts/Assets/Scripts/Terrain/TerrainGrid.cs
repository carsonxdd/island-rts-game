using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using System.Collections.Generic;

/// <summary>
/// Runtime island terrain (Terrain System T1): a chunked flat-shaded
/// heightmap mesh replacing the old flat Ground plane. Singleton, sibling of
/// WallGrid in spirit.
///
/// Runs everything in Awake at [DefaultExecutionOrder(-100)] — generate
/// heights, build chunk meshes + colliders, spawn the water plane, mark deep
/// water NotWalkable, and synchronously build the NavMeshSurface — so every
/// existing Start()-time system (spawners, campfire, units) finds a finished
/// world and a live NavMesh exactly like it used to. Startup ordering is the
/// #1 break risk in the plan; this is the mitigation.
///
/// Visuals: no custom shader. The mesh is flat-shaded (verts duplicated per
/// triangle) and each TRIANGLE is assigned to one of three submeshes —
/// sand / grass / rock — using the existing LP materials, so height/slope
/// banding comes out as crisp low-poly facets that match the art style.
///
/// Sea level is y = 0. Land rises above, seabed dips below. Deep water
/// (below −0.4) is NotWalkable; the −0.4..0 band is the walkable wading
/// band (survivor landing now, shoreline enemy spawns in T3).
///
/// T2 (not here yet): FlattenArea under placed buildings + dirty-chunk
/// rebuilds + async NavMesh updates. IslandGenerator.FlattenDisc is the
/// core op it will build on.
/// </summary>
[DefaultExecutionOrder(-100)]
public class TerrainGrid : MonoBehaviour
{
    public static TerrainGrid Instance { get; private set; }

    // Field dimensions: 101×101 verts at 1 m spacing = the classic 100×100 world
    public const int VertsPerSide = 151;
    public const float Spacing = 1f;
    private const float Half = (VertsPerSide - 1) * Spacing * 0.5f;

    private const int ChunkQuads = 16;      // 16×16 quads per chunk
    private const float DeepWaterY = -0.4f; // below this is NotWalkable
    private const int NotWalkableArea = 1;  // built-in NavMesh area index

    [Header("Generation")]
    [Tooltip("Fixed seed for T1 — every run gets the same island. T4 randomizes this per run.")]
    public int seed = 20260825;

    [Header("Materials (wired by Tools > Island RTS > Terrain setup)")]
    public Material sandMaterial;
    public Material grassMaterial;
    public Material rockMaterial;
    public Material waterMaterial;

    [Header("Buildability")]
    [Tooltip("Minimum ground height for building placement — keeps buildings off the beach waterline.")]
    public float minBuildHeight = 0.15f;
    [Tooltip("Maximum slope (rise/run) for building placement — steeper reads as cliff.")]
    public float maxBuildSlope = 0.55f;

    private float[,] heights;
    private NavMeshSurface surface;
    private Transform chunksRoot;
    private GameObject[,] chunkObjects;   // [cx, cz] — kept for T2 dirty-chunk rebuilds
    private int chunkGridCount;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        float startTime = Time.realtimeSinceStartup;

        heights = IslandGenerator.Generate(VertsPerSide, Spacing, seed);
        BuildAllChunks();
        CreateWaterPlane();
        CreateDeepWaterVolume();
        BuildNavMesh();

        Debug.Log($"TerrainGrid: island generated (seed {seed}) + NavMesh built in "
            + $"{(Time.realtimeSinceStartup - startTime) * 1000f:F0} ms");
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------
    // Sampling API — the one way anything asks "how high is the ground"
    // ------------------------------------------------------------------

    /// <summary>Bilinear terrain height at a world position (clamped at the map border).</summary>
    public float SampleHeight(Vector3 worldPos)
    {
        return SampleField(heights, worldPos.x, worldPos.z);
    }

    /// <summary>Slope (rise/run gradient magnitude) at a world position.</summary>
    public float SlopeAt(Vector3 worldPos)
    {
        const float e = 0.5f;
        float dhdx = (SampleField(heights, worldPos.x + e, worldPos.z) - SampleField(heights, worldPos.x - e, worldPos.z)) / (2f * e);
        float dhdz = (SampleField(heights, worldPos.x, worldPos.z + e) - SampleField(heights, worldPos.x, worldPos.z - e)) / (2f * e);
        return Mathf.Sqrt(dhdx * dhdx + dhdz * dhdz);
    }

    /// <summary>Above sea level?</summary>
    public bool IsLand(Vector3 worldPos) => SampleHeight(worldPos) > 0f;

    /// <summary>Inside the walkable wading band (−0.4..0)?</summary>
    public bool IsShallow(Vector3 worldPos)
    {
        float h = SampleHeight(worldPos);
        return h > DeepWaterY && h <= 0f;
    }

    /// <summary>Can a building footprint center sit here? Dry land, gentle slope.</summary>
    public bool IsBuildable(Vector3 worldPos)
    {
        return SampleHeight(worldPos) > minBuildHeight && SlopeAt(worldPos) < maxBuildSlope;
    }

    /// <summary>
    /// Static bilinear sample of any heightfield laid out like ours (verts
    /// centered on the origin at <see cref="Spacing"/>). Shared with the
    /// editor tools, which generate the same field to snap scene props.
    /// </summary>
    public static float SampleField(float[,] field, float worldX, float worldZ)
    {
        float fx = Mathf.Clamp((worldX + Half) / Spacing, 0f, VertsPerSide - 1.0001f);
        float fz = Mathf.Clamp((worldZ + Half) / Spacing, 0f, VertsPerSide - 1.0001f);
        int x0 = (int)fx;
        int z0 = (int)fz;
        float tx = fx - x0;
        float tz = fz - z0;

        float h00 = field[x0, z0];
        float h10 = field[x0 + 1, z0];
        float h01 = field[x0, z0 + 1];
        float h11 = field[x0 + 1, z0 + 1];
        return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
    }

    // ------------------------------------------------------------------
    // Chunk meshing
    // ------------------------------------------------------------------

    void BuildAllChunks()
    {
        // Rebuild-from-scratch (restart safety if the object ever survives)
        Transform existing = transform.Find("Chunks");
        if (existing != null) Destroy(existing.gameObject);

        chunksRoot = new GameObject("Chunks").transform;
        chunksRoot.SetParent(transform, false);
        chunksRoot.gameObject.layer = gameObject.layer;  // Default — BuildPlacement.groundLayer

        int quadsPerSide = VertsPerSide - 1;
        chunkGridCount = Mathf.CeilToInt(quadsPerSide / (float)ChunkQuads);
        chunkObjects = new GameObject[chunkGridCount, chunkGridCount];

        for (int cz = 0; cz < chunkGridCount; cz++)
        {
            for (int cx = 0; cx < chunkGridCount; cx++)
            {
                BuildChunk(cx, cz, quadsPerSide);
            }
        }
    }

    Mesh BuildChunkMesh(int cx, int cz, int quadsPerSide)
    {
        int qx0 = cx * ChunkQuads;
        int qz0 = cz * ChunkQuads;
        int qx1 = Mathf.Min(qx0 + ChunkQuads, quadsPerSide);
        int qz1 = Mathf.Min(qz0 + ChunkQuads, quadsPerSide);

        var verts = new List<Vector3>((qx1 - qx0) * (qz1 - qz0) * 6);
        var sandTris = new List<int>();
        var grassTris = new List<int>();
        var rockTris = new List<int>();

        for (int qz = qz0; qz < qz1; qz++)
        {
            for (int qx = qx0; qx < qx1; qx++)
            {
                float x0 = qx * Spacing - Half;
                float z0 = qz * Spacing - Half;
                float x1 = x0 + Spacing;
                float z1 = z0 + Spacing;

                Vector3 v00 = new Vector3(x0, heights[qx, qz], z0);
                Vector3 v10 = new Vector3(x1, heights[qx + 1, qz], z0);
                Vector3 v01 = new Vector3(x0, heights[qx, qz + 1], z1);
                Vector3 v11 = new Vector3(x1, heights[qx + 1, qz + 1], z1);

                // Alternate the quad diagonal in a checker pattern so long
                // slopes don't get a uniform diagonal grain
                if (((qx + qz) & 1) == 0)
                {
                    AddTriangle(verts, sandTris, grassTris, rockTris, v00, v01, v11);
                    AddTriangle(verts, sandTris, grassTris, rockTris, v00, v11, v10);
                }
                else
                {
                    AddTriangle(verts, sandTris, grassTris, rockTris, v00, v01, v10);
                    AddTriangle(verts, sandTris, grassTris, rockTris, v10, v01, v11);
                }
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = $"TerrainChunk_{cx}_{cz}";
        mesh.SetVertices(verts);
        mesh.subMeshCount = 3;
        mesh.SetTriangles(sandTris, 0);
        mesh.SetTriangles(grassTris, 1);
        mesh.SetTriangles(rockTris, 2);
        mesh.RecalculateNormals();   // duplicated verts per tri → hard flat-shaded facets
        mesh.RecalculateBounds();
        return mesh;
    }

    void BuildChunk(int cx, int cz, int quadsPerSide)
    {
        Mesh mesh = BuildChunkMesh(cx, cz, quadsPerSide);

        GameObject chunk = new GameObject($"Chunk_{cx}_{cz}");
        chunk.transform.SetParent(chunksRoot, false);
        chunk.layer = chunksRoot.gameObject.layer;

        MeshFilter mf = chunk.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        MeshRenderer mr = chunk.AddComponent<MeshRenderer>();
        mr.sharedMaterials = new[] { sandMaterial, grassMaterial, rockMaterial };

        MeshCollider mc = chunk.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;

        chunkObjects[cx, cz] = chunk;
    }

    /// <summary>
    /// Rebuild one chunk's mesh from the (edited) heightfield in place —
    /// renderer and collider both pick up the new mesh; the old one is freed.
    /// </summary>
    void RebuildChunk(int cx, int cz)
    {
        GameObject chunk = chunkObjects != null ? chunkObjects[cx, cz] : null;
        if (chunk == null) return;

        MeshFilter mf = chunk.GetComponent<MeshFilter>();
        MeshCollider mc = chunk.GetComponent<MeshCollider>();
        Mesh old = mf != null ? mf.sharedMesh : null;

        PerfCounters.Hit(PerfCounters.K.TerrainRebuild);
        Mesh fresh = BuildChunkMesh(cx, cz, VertsPerSide - 1);
        if (mf != null) mf.sharedMesh = fresh;
        if (mc != null) mc.sharedMesh = fresh;
        if (old != null) Destroy(old);
    }

    /// <summary>
    /// Terrain T2: level a disc of ground to the height at its center — full
    /// weight inside <paramref name="radius"/>, blending out over
    /// <paramref name="blend"/> meters. Rebuilds only the touched chunks and
    /// kicks an async NavMesh refresh. Called when a building is placed so
    /// structures sit flush on a level pad instead of clipping into slopes.
    /// </summary>
    public void FlattenArea(Vector3 center, float radius, float blend)
    {
        if (heights == null || chunkObjects == null) return;

        float target = SampleHeight(center);
        IslandGenerator.FlattenDisc(heights, VertsPerSide, Spacing, center.x, center.z, radius, blend, target);

        // Chunk range touched by the disc (+1 vert margin: a quad's verts feed
        // the neighboring chunk's triangles at the seam)
        float reach = radius + blend + Spacing;
        int vx0 = Mathf.Max(0, Mathf.FloorToInt((center.x - reach + Half) / Spacing));
        int vx1 = Mathf.Min(VertsPerSide - 1, Mathf.CeilToInt((center.x + reach + Half) / Spacing));
        int vz0 = Mathf.Max(0, Mathf.FloorToInt((center.z - reach + Half) / Spacing));
        int vz1 = Mathf.Min(VertsPerSide - 1, Mathf.CeilToInt((center.z + reach + Half) / Spacing));

        int cx0 = Mathf.Clamp((vx0 - 1) / ChunkQuads, 0, chunkGridCount - 1);
        int cx1 = Mathf.Clamp(vx1 / ChunkQuads, 0, chunkGridCount - 1);
        int cz0 = Mathf.Clamp((vz0 - 1) / ChunkQuads, 0, chunkGridCount - 1);
        int cz1 = Mathf.Clamp(vz1 / ChunkQuads, 0, chunkGridCount - 1);

        for (int cz = cz0; cz <= cz1; cz++)
            for (int cx = cx0; cx <= cx1; cx++)
                RebuildChunk(cx, cz);

        UpdateNavMeshAsync();
    }

    /// <summary>
    /// Emit one triangle into the vertex list and classify it into a
    /// material band: rock on steep land faces, sand low (including all
    /// seabed), grass above the beach line.
    /// </summary>
    void AddTriangle(List<Vector3> verts, List<int> sand, List<int> grass, List<int> rock,
        Vector3 a, Vector3 b, Vector3 c)
    {
        int i = verts.Count;
        verts.Add(a); verts.Add(b); verts.Add(c);

        float avgH = (a.y + b.y + c.y) / 3f;
        Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
        bool steep = normal.y < 0.80f;  // ≈ >37° face

        List<int> band;
        if (steep && avgH > 0f) band = rock;
        else if (avgH < 0.45f) band = sand;   // beach ring + all seabed
        else band = grass;

        band.Add(i); band.Add(i + 1); band.Add(i + 2);
    }

    // ------------------------------------------------------------------
    // Water + NavMesh
    // ------------------------------------------------------------------

    void CreateWaterPlane()
    {
        if (waterMaterial == null)
        {
            Debug.LogError("TerrainGrid: waterMaterial not assigned — no water plane. Run Tools > Island RTS > Terrain > Setup Terrain Scene.");
            return;
        }

        // Own root, NOT under the terrain (the NavMeshSurface collects
        // children). No collider: ground raycasts must pass through to the
        // seabed chunks. Never static — Phase 10 Stage 3 mounts the
        // displacement shader here.
        GameObject water = GameObject.CreatePrimitive(PrimitiveType.Plane);
        water.name = "_WaterPlane";
        Destroy(water.GetComponent<Collider>());
        water.transform.position = Vector3.zero;                 // sea level y = 0
        water.transform.localScale = new Vector3(48f, 1f, 48f);  // 320×320 m, past the camera horizon
        MeshRenderer mr = water.GetComponent<MeshRenderer>();
        mr.sharedMaterial = waterMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    void CreateDeepWaterVolume()
    {
        // Everything below −0.4 is NotWalkable; the −0.4..0 band stays the
        // walkable wading band. Must exist before BuildNavMesh and be a
        // child (the surface collects children).
        GameObject volGo = new GameObject("DeepWaterNotWalkable");
        volGo.transform.SetParent(transform, false);
        NavMeshModifierVolume vol = volGo.AddComponent<NavMeshModifierVolume>();
        vol.size = new Vector3(500f, 4f, 500f);
        vol.center = new Vector3(0f, DeepWaterY - 2f, 0f);  // covers −4.4..−0.4
        vol.area = NotWalkableArea;
    }

    void BuildNavMesh()
    {
        surface = GetComponent<NavMeshSurface>();
        if (surface == null) surface = gameObject.AddComponent<NavMeshSurface>();

        // Children-only + physics colliders: exactly the chunk colliders and
        // the modifier volume — never buildings, resource nodes, or water
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();
    }

    /// <summary>
    /// T2 hook: full async NavMesh refresh after terrain deformation. Not
    /// called in T1 (terrain is static after generation).
    /// </summary>
    public void UpdateNavMeshAsync()
    {
        if (surface != null && surface.navMeshData != null)
        {
            PerfCounters.Hit(PerfCounters.K.NavMeshUpdate);
            surface.UpdateNavMesh(surface.navMeshData);
        }
    }
}
