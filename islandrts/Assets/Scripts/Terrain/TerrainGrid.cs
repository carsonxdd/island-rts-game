using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using System.Collections.Generic;

/// <summary>
/// Runtime island terrain: a chunked flat-shaded heightmap mesh replacing the
/// old flat Ground plane. Singleton, sibling of WallGrid in spirit.
///
/// Runs everything in Awake at [DefaultExecutionOrder(-100)] — generate the
/// island, build chunk meshes + colliders, spawn the water mesh, mark deep
/// water NotWalkable, and synchronously build the NavMeshSurface — so every
/// existing Start()-time system (spawners, campfire, units) finds a finished
/// world and a live NavMesh exactly like it used to. Startup ordering is the
/// #1 break risk in the plan; this is the mitigation.
///
/// Per-run choices come from <see cref="IslandOptions"/> (island size,
/// terrain style, optional seed — picked on the New Game screen). Map size
/// sets <see cref="VertsPerSide"/> before generation, so every distance in
/// the game that used to assume a 150 m map reads <see cref="SizeScale"/>.
///
/// Seed: a NEW GAME gets a fresh random island; RESTART replays the same one
/// (<see cref="RunSeed"/> survives the scene reload — it is a static, not a
/// component — and <c>MenuFlow.NewGame</c> clears it). Balance sweeps never
/// get a random island: under <c>SimHooks.Simulating</c> the inspector seed
/// (or the sweep's <c>terrainSeed</c> override) is used.
///
/// Visuals: no custom terrain shader. The mesh is flat-shaded (verts
/// duplicated per triangle) and each TRIANGLE is assigned to one
/// <see cref="Surface"/> submesh — wet sand, sand, three grass tones, rock,
/// cliff — using LP materials. Bands are classified from the SMOOTH field
/// (bilinear height + gradient at the triangle centre), not from the
/// triangle's own normal, so a slope reads as one flowing band instead of a
/// checkerboard of alternating facets.
///
/// Sea level is y = 0. Land rises above, seabed dips below. Deep water
/// (below <see cref="DeepWaterY"/>) is NotWalkable; the −0.4..0 band is the
/// walkable wading band. Ponds carve below the deep line, so they are
/// impassable too.
///
/// Buildable = dry, gentle AND reachable from the campfire site: the
/// generator's flood fill marks cut-off outcrops, and nothing may be built
/// or spawned on one.
/// </summary>
[DefaultExecutionOrder(-100)]
public class TerrainGrid : MonoBehaviour
{
    public static TerrainGrid Instance { get; private set; }

    /// <summary>
    /// Seed of the island the current run is played on. 0 = none chosen yet
    /// (the next load picks a fresh random one). Set by Awake, cleared by
    /// MenuFlow.NewGame, deliberately kept across MenuFlow.Restart.
    /// </summary>
    public static int RunSeed;

    /// <summary>
    /// Vertices per side of the current map (1 m spacing). Set from the run's
    /// island size before generation; 151 = the standard 150 m map. Static so
    /// editor tools and spawners can read it without an instance.
    /// </summary>
    public static int VertsPerSide { get; private set; } = 151;
    public const float Spacing = 1f;
    private static float Half => (VertsPerSide - 1) * Spacing * 0.5f;

    /// <summary>Map half-extent divided by the standard 75 m: the factor every 150 m-map distance scales by.</summary>
    public static float SizeScale => Half / 75f;

    private const int ChunkQuads = 16;      // 16×16 quads per chunk
    public const float DeepWaterY = -0.4f;  // below this is NotWalkable
    private const int NotWalkableArea = 1;  // built-in NavMesh area index

    /// <summary>Material bands, in submesh order. Add here + in Classify + in the palette.</summary>
    public enum Surface { SandWet, Sand, GrassGreen, GrassDark, GrassDry, RockMid, RockDark }
    public static readonly int SurfaceCount = System.Enum.GetValues(typeof(Surface)).Length;

    [Header("Generation")]
    [Tooltip("All generator knobs. Null = the built-in defaults (IslandSettings.CreateDefault).")]
    public IslandSettings settings;
    [Tooltip("Pick a fresh random seed for every NEW GAME. Off = always the seed below (the old fixed-island behaviour).")]
    public bool randomizeSeed = true;
    [Tooltip("Seed used when randomizeSeed is off, and always under the balance sim (deterministic sweeps).")]
    public int seed = 20260825;

    [Header("Materials (wired by Tools > Island RTS > Terrain setup)")]
    [Tooltip("One material per Surface enum entry, in enum order.")]
    public Material[] surfaceMaterials;
    public Material waterMaterial;

    [Header("Buildability")]
    [Tooltip("Minimum ground height for building placement — keeps buildings off the beach waterline.")]
    public float minBuildHeight = 0.15f;
    [Tooltip("Maximum slope (rise/run) for building placement — steeper reads as cliff.")]
    public float maxBuildSlope = 0.55f;

    private IslandField field;
    private IslandSettings activeSettings;   // the asset with the run's style applied
    private float[,] heights;
    private NavMeshSurface surface;
    private Transform chunksRoot;
    private GameObject[,] chunkObjects;   // [cx, cz] — kept for T2 dirty-chunk rebuilds
    private int chunkGridCount;

    /// <summary>The generated field (heights, tone, reachability, scaled anchors) — read-only use by scatter and tools.</summary>
    public IslandField Field => field;
    /// <summary>The settings actually used this run (style applied). Never the asset itself.</summary>
    public IslandSettings ActiveSettings => activeSettings != null ? activeSettings : IslandSettings.Resolve(settings);
    public IslandOptions.Size ActiveSize { get; private set; } = IslandOptions.Size.Medium;
    public IslandSettings.Style ActiveStyle { get; private set; } = IslandSettings.Style.Terraced;

    /// <summary>Landing cove centre on this map (y at the cove shelf height).</summary>
    public Vector3 CoveCenter => field != null ? new Vector3(field.coveCenter.x, -0.25f, field.coveCenter.y) : new Vector3(-70f, -0.25f, 3f);
    /// <summary>Campfire flat centre on this map.</summary>
    public Vector3 CampfireSite => field != null ? new Vector3(field.campfireSite.x, SampleHeight(new Vector3(field.campfireSite.x, 0f, field.campfireSite.y)), field.campfireSite.y) : Vector3.zero;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        IslandOptions.Snapshot run = IslandOptions.Active;
        ActiveSize = run.size;
        ActiveStyle = run.style;
        VertsPerSide = IslandOptions.VertsFor(run.size);
        activeSettings = IslandSettings.Resolve(settings).WithStyle(run.style);

        int chosen = ChooseSeed(run);

        float startTime = Time.realtimeSinceStartup;

        field = IslandGenerator.Generate(VertsPerSide, Spacing, chosen, activeSettings);
        heights = field.heights;
        seed = field.seed;
        if (!SimHooks.Simulating) RunSeed = field.seed;

        BuildAllChunks();
        CreateWaterPlane();
        CreateDeepWaterVolume();
        BuildNavMesh();
        PlaceShipwreck();

        Debug.Log("TerrainGrid: " + run.size + " " + run.style + " island " + field.Report + " + NavMesh built in "
            + $"{(Time.realtimeSinceStartup - startTime) * 1000f:F0} ms");
    }

    int ChooseSeed(IslandOptions.Snapshot run)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // A balance sweep may want a different island per run. This has to
        // happen here, not in a sceneLoaded callback: the whole world plus the
        // NavMesh is built below, in Awake, by design.
        if (SimOverrides.Active != null && SimOverrides.Active.terrainSeed >= 0)
        {
            return SimOverrides.Active.terrainSeed;
        }
#endif
        // Sweeps must be statistically comparable across runs — never random
        if (SimHooks.Simulating) return seed;
        if (RunSeed != 0) return RunSeed;
        if (run.seed != 0) return run.seed;   // typed on the New Game screen
        if (!randomizeSeed) return seed;

        int fresh = unchecked((int)(System.DateTime.Now.Ticks ^ ((long)System.Environment.TickCount << 8))) & 0x7FFFFFFF;
        return fresh == 0 ? 1 : fresh;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        // A texture created in code is not collected with the scene
        if (depthMap != null) Destroy(depthMap);
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
        return SlopeAt(worldPos.x, worldPos.z);
    }

    float SlopeAt(float x, float z)
    {
        const float e = 0.5f;
        float dhdx = (SampleField(heights, x + e, z) - SampleField(heights, x - e, z)) / (2f * e);
        float dhdz = (SampleField(heights, x, z + e) - SampleField(heights, x, z - e)) / (2f * e);
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

    /// <summary>
    /// Can an agent walk here from the campfire site? False on cut-off
    /// outcrops and in deep water. Nearest-vertex lookup on the generator's
    /// flood-fill mask.
    /// </summary>
    public bool IsReachable(Vector3 worldPos)
    {
        if (field == null) return true;
        int xi = Mathf.Clamp(Mathf.RoundToInt((worldPos.x + Half) / Spacing), 0, VertsPerSide - 1);
        int zi = Mathf.Clamp(Mathf.RoundToInt((worldPos.z + Half) / Spacing), 0, VertsPerSide - 1);
        return field.reachable[xi, zi];
    }

    /// <summary>Can a building footprint center sit here? Dry land, gentle slope, reachable.</summary>
    public bool IsBuildable(Vector3 worldPos)
    {
        return SampleHeight(worldPos) > minBuildHeight
            && SlopeAt(worldPos) < maxBuildSlope
            && IsReachable(worldPos);
    }

    /// <summary>
    /// Grass tone 0..1 at a world position (0 = dark valley grass, 1 = dry
    /// plateau meadow) — the same value the material bands use, so scatter
    /// and resource placement can agree with what the ground looks like.
    /// </summary>
    public float ToneAt(Vector3 worldPos)
    {
        return ToneAt(worldPos.x, worldPos.z, SampleHeight(worldPos));
    }

    float ToneAt(float x, float z, float h)
    {
        if (field == null) return 0.5f;
        IslandSettings s = ActiveSettings;
        float noise = SampleField(field.tone, x, z);
        float heightNorm = Mathf.Clamp01(h / (s.baseHeight + s.hillAmplitude));
        return Mathf.Lerp(noise, heightNorm, s.toneHeightWeight);
    }

    /// <summary>
    /// Static bilinear sample of any heightfield laid out like ours (verts
    /// centered on the origin at <see cref="Spacing"/>). Shared with the
    /// editor tools, which generate the same field to snap scene props. The
    /// field's own size sets the extent, so it works for any island size.
    /// </summary>
    public static float SampleField(float[,] field, float worldX, float worldZ)
    {
        int n = field.GetLength(0);
        float half = (n - 1) * Spacing * 0.5f;
        float fx = Mathf.Clamp((worldX + half) / Spacing, 0f, n - 1.0001f);
        float fz = Mathf.Clamp((worldZ + half) / Spacing, 0f, n - 1.0001f);
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
        var tris = new List<int>[SurfaceCount];
        for (int i = 0; i < SurfaceCount; i++) tris[i] = new List<int>();

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
                    AddTriangle(verts, tris, v00, v01, v11);
                    AddTriangle(verts, tris, v00, v11, v10);
                }
                else
                {
                    AddTriangle(verts, tris, v00, v01, v10);
                    AddTriangle(verts, tris, v10, v01, v11);
                }
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = $"TerrainChunk_{cx}_{cz}";
        mesh.SetVertices(verts);
        mesh.subMeshCount = SurfaceCount;
        for (int i = 0; i < SurfaceCount; i++) mesh.SetTriangles(tris[i], i);
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
        mr.sharedMaterials = ResolveMaterials();

        MeshCollider mc = chunk.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;

        chunkObjects[cx, cz] = chunk;
    }

    Material[] resolvedMaterials;

    /// <summary>
    /// One material per Surface. A missing slot falls back to the nearest
    /// wired one so a half-configured scene still renders (and logs once)
    /// instead of showing magenta.
    /// </summary>
    Material[] ResolveMaterials()
    {
        if (resolvedMaterials != null) return resolvedMaterials;

        resolvedMaterials = new Material[SurfaceCount];
        Material any = null;
        if (surfaceMaterials != null)
            for (int i = 0; i < surfaceMaterials.Length && any == null; i++) any = surfaceMaterials[i];

        bool missing = false;
        for (int i = 0; i < SurfaceCount; i++)
        {
            Material m = surfaceMaterials != null && i < surfaceMaterials.Length ? surfaceMaterials[i] : null;
            if (m == null) { missing = true; m = any; }
            resolvedMaterials[i] = m;
        }
        if (missing)
        {
            Debug.LogError("TerrainGrid: surfaceMaterials incomplete (" + SurfaceCount + " expected, one per Surface). "
                + "Run Tools > Island RTS > Terrain > Setup Terrain Scene.");
        }
        return resolvedMaterials;
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

        PushWaterProperties();   // a flattened pad near the shore changes the water column
        UpdateNavMeshAsync();
    }

    /// <summary>
    /// Emit one triangle into the vertex list and classify it into a
    /// material band (see <see cref="Classify"/>).
    /// </summary>
    void AddTriangle(List<Vector3> verts, List<int>[] tris, Vector3 a, Vector3 b, Vector3 c)
    {
        int i = verts.Count;
        verts.Add(a); verts.Add(b); verts.Add(c);

        float cxp = (a.x + b.x + c.x) / 3f;
        float czp = (a.z + b.z + c.z) / 3f;
        float avgH = (a.y + b.y + c.y) / 3f;

        List<int> band = tris[(int)Classify(cxp, czp, avgH)];
        band.Add(i); band.Add(i + 1); band.Add(i + 2);
    }

    /// <summary>
    /// Surface band for one triangle, from the smooth field at its centre.
    ///
    /// Using the field's bilinear gradient rather than the triangle's own
    /// normal is what makes bands FLOW: two triangles of one quad have
    /// different normals but the same smooth slope, so a hillside is one band
    /// instead of a rock/grass checkerboard. Cliff faces (rise/run &gt; 1)
    /// are dark rock, steep faces (&gt; 0.62) mid rock; below that, height
    /// bands — wet sand up through the waterline, dry sand on the beach,
    /// then grass by tone (noise blended with height so valleys run dark and
    /// plateau tops dry).
    /// </summary>
    Surface Classify(float x, float z, float avgH)
    {
        IslandSettings s = ActiveSettings;
        float jitter = field != null ? SampleField(field.band, x, z) * s.bandJitter : 0f;
        float h = avgH + jitter;
        float slope = SlopeAt(x, z);

        if (avgH > -1.0f)
        {
            if (slope > 1.0f) return Surface.RockDark;
            if (slope > 0.62f && avgH > 0.1f) return Surface.RockMid;
        }

        if (h < 0.2f) return Surface.SandWet;   // waterline + wading band + all seabed
        if (h < 0.65f) return Surface.Sand;     // beach ring

        float tone = ToneAt(x, z, avgH);
        if (tone < 0.36f) return Surface.GrassDark;
        if (tone > 0.66f) return Surface.GrassDry;
        return Surface.GrassGreen;
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
        // seabed chunks. Never static — the stylized water shader displaces
        // vertices, so the mesh is a real grid rather than Unity's 10×10
        // plane primitive.
        GameObject water = new GameObject("_WaterPlane");
        water.transform.position = Vector3.zero;   // sea level y = 0
        MeshFilter mf = water.AddComponent<MeshFilter>();
        mf.sharedMesh = BuildWaterMesh(Mathf.Max(480f, Half * 6f), WaterGridStep);
        waterRenderer = water.AddComponent<MeshRenderer>();
        waterRenderer.sharedMaterial = waterMaterial;
        waterRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        waterRenderer.receiveShadows = false;

        PushWaterProperties();
    }

    // ------------------------------------------------------------------
    // Water depth map
    //
    // How deep the water is at a pixel used to be read from the camera depth
    // texture. That is a measurement along the VIEW RAY of whatever happens to
    // be behind the water, which made the sea's colour depend on camera tilt
    // and — far worse — drew a hard straight line across the ocean at the edge
    // of the terrain, where the seabed geometry simply stops and the buffer
    // holds nothing (2026-09-03). The island's own heightfield is the actual
    // answer and it is already in memory: bake it into a one-channel texture
    // and the shader knows the exact water column at every point, with no
    // camera dependence, no depth-texture dependence, and a border that clamps
    // to open-ocean depth so there is nothing left to cut.
    // ------------------------------------------------------------------

    /// <summary>Metres of water encoded by a full-white texel. Deeper than this is all "deep".</summary>
    const float DepthEncodeRange = 4f;

    MeshRenderer waterRenderer;
    Texture2D depthMap;

    static readonly int HeightMapId = Shader.PropertyToID("_HeightMap");
    static readonly int MapParamsId = Shader.PropertyToID("_MapParams");

    /// <summary>Rebuild the depth map and hand the water renderer its properties.</summary>
    void PushWaterProperties()
    {
        if (waterRenderer == null) return;
        BuildDepthMap();

        // A property block, never the shared material — writing the asset in
        // Play mode dirties it on disk in the editor.
        var block = new MaterialPropertyBlock();
        block.SetFloat(WaterGridStepId, WaterGridStep);
        if (depthMap != null)
        {
            block.SetTexture(HeightMapId, depthMap);
            // uv = worldXZ * x + y, landing on texel CENTRES (hence the +0.5)
            int n = VertsPerSide;
            float a = 1f / (Spacing * n);
            float b = (Half / Spacing + 0.5f) / n;
            block.SetVector(MapParamsId, new Vector4(a, b, DepthEncodeRange, 0f));
        }
        waterRenderer.SetPropertyBlock(block);
    }

    /// <summary>One texel per terrain vertex: water column in metres, scaled into 0..1.</summary>
    void BuildDepthMap()
    {
        if (heights == null) return;

        int n = VertsPerSide;
        if (depthMap == null)
        {
            // Linear, no mips, clamped: a sample past the map edge must read the
            // border texel (open ocean), which is what removes the seam.
            depthMap = new Texture2D(n, n, TextureFormat.RFloat, false, true)
            {
                name = "IslandWaterDepth",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
        }

        if (depthPixels == null || depthPixels.Length != n * n) depthPixels = new float[n * n];
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
                depthPixels[z * n + x] = Mathf.Clamp01(-heights[x, z] / DepthEncodeRange);

        depthMap.SetPixelData(depthPixels, 0);
        depthMap.Apply(false, false);
    }

    float[] depthPixels;

    /// <summary>
    /// Water grid spacing in metres. The shader's wavelengths must stay ≥ ~6× this
    /// or the waves alias against the grid into slow diagonal stripes (the 3 m grid
    /// with a 4 m wave did exactly that, 2026-09-01). 1.5 m on the 480 m plane is
    /// ~103k verts / ~205k tris — cheap, and the facets read at gameplay zoom.
    /// </summary>
    const float WaterGridStep = 1.5f;
    static readonly int WaterGridStepId = Shader.PropertyToID("_GridStep");

    /// <summary>
    /// A flat XZ grid centred on the origin, <paramref name="size"/> m across at
    /// <paramref name="step"/> m spacing. The quad count is forced even so the
    /// centred vertices land on whole multiples of <paramref name="step"/> — the
    /// water shader relies on that to find each pixel's triangle from world XZ.
    /// </summary>
    static Mesh BuildWaterMesh(float size, float step)
    {
        int quads = Mathf.CeilToInt(size / step);
        if ((quads & 1) == 1) quads++;
        int n = quads + 1;
        float half = quads * step * 0.5f;
        Vector3[] verts = new Vector3[n * n];
        Vector2[] uvs = new Vector2[n * n];
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                verts[z * n + x] = new Vector3(x * step - half, 0f, z * step - half);
                uvs[z * n + x] = new Vector2(x * step, z * step);   // world-metre UVs
            }

        int[] tris = new int[(n - 1) * (n - 1) * 6];
        int t = 0;
        for (int z = 0; z < n - 1; z++)
            for (int x = 0; x < n - 1; x++)
            {
                int i = z * n + x;
                tris[t++] = i; tris[t++] = i + n; tris[t++] = i + 1;
                tris[t++] = i + 1; tris[t++] = i + n; tris[t++] = i + n + 1;
            }

        Mesh mesh = new Mesh();
        mesh.name = "WaterGrid";
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        // Vertices move in the shader; keep the bounds generous so waves at
        // the screen edge never cull the whole plane
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(size, 4f, size));
        return mesh;
    }

    void CreateDeepWaterVolume()
    {
        // Everything below −0.4 is NotWalkable; the −0.4..0 band stays the
        // walkable wading band. Must exist before BuildNavMesh and be a
        // child (the surface collects children).
        GameObject volGo = new GameObject("DeepWaterNotWalkable");
        volGo.transform.SetParent(transform, false);
        NavMeshModifierVolume vol = volGo.AddComponent<NavMeshModifierVolume>();
        float size = Mathf.Max(500f, Half * 6f);
        vol.size = new Vector3(size, 4f, size);
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
    /// The shipwreck is a scene object authored beside the cove on the 150 m
    /// map. On another island size the cove moves with the map, so the wreck
    /// keeps its authored offset from the cove. The standard map is left
    /// exactly where the editor tool snapped it.
    /// </summary>
    void PlaceShipwreck()
    {
        if (field == null || Mathf.Approximately(field.sizeScale, 1f)) return;
        GameObject wreck = GameObject.Find("_Shipwreck");
        if (wreck == null) return;

        IslandSettings s = ActiveSettings;
        Vector3 authoredCove = new Vector3(s.coveCenter.x, wreck.transform.position.y, s.coveCenter.y);
        Vector3 offset = wreck.transform.position - authoredCove;
        wreck.transform.position = new Vector3(field.coveCenter.x, 0f, field.coveCenter.y) + offset;
    }

    /// <summary>
    /// T2 hook: full async NavMesh refresh after terrain deformation.
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
