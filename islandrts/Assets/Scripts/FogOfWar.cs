using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The colony's knowledge of the island (2026-09-09): a coarse grid over the map with two
/// bits per cell, ever-explored and currently-visible, stamped a few times a second from
/// every <see cref="VisionSource"/> and handed to the shaders as the global
/// <c>_FogMask</c> texture. Explored memory fog: unseen ground starts dark and clears for
/// good once one of your people or buildings has seen it; ground nobody is watching
/// right now sits in a dim shroud.
/// </summary>
/// <remarks>
/// Runtime-added by <see cref="TerrainGrid"/> at the end of its Awake, so its public
/// fields are the live values and it always finds a finished island. Gameplay asks it
/// two questions, <see cref="IsExplored"/> and <see cref="IsVisible"/>, both O(1) reads
/// of the raw grid; the smoothed values only exist for the texture. Under the balance
/// sim the grid still runs (the gates that will read it must behave the same headless)
/// but no texture is built.
///
/// Rendering is the cheap half and lives in Resources/Shaders/FogOfWar.hlsl; what hides
/// in the fog is <see cref="FogVisibility"/>. Raiders do NOT read this — their AI keeps
/// knowing where the campfire is on purpose; a symmetric fog is a different game.
/// </remarks>
public class FogOfWar : MonoBehaviour
{
    public static FogOfWar Instance { get; private set; }

    [Tooltip("Metres per fog cell. 2 m is plenty: the shader filters across cells and the CPU smooths the reveal.")]
    public float cellSize = 2f;
    [Tooltip("Seconds between stamps of the vision sources. The smoothing below runs at the same cadence.")]
    public float updateInterval = 0.15f;
    [Tooltip("How fast a cell brightens when it comes into view or is first explored (per second, exponential).")]
    public float revealRate = 8f;
    [Tooltip("How fast a cell drops back into the shroud after the last watcher leaves (per second, exponential).")]
    public float shroudRate = 3f;

    [Header("Look")]
    [Range(0f, 1f), Tooltip("Brightness of ground nobody has ever seen. Near-black, not black: silhouettes of the coast still read.")]
    public float unexploredBrightness = 0.04f;
    [Range(0f, 1f), Tooltip("Brightness of explored ground with no watcher on it.")]
    public float shroudBrightness = 0.45f;
    [Range(0f, 1f), Tooltip("How grey the shroud is. 0 = only darker, 1 = monochrome.")]
    public float shroudDesaturation = 0.55f;

    [Header("Debug")]
    [Tooltip("Everything explored and visible. The F4 menu toggles this; never ship it on.")]
    public bool revealAll;

    private static readonly int MaskId = Shader.PropertyToID("_FogMask");
    private static readonly int ParamsId = Shader.PropertyToID("_FogParams");
    private static readonly int LookId = Shader.PropertyToID("_FogLook");

    private int n;               // cells per side
    private float half;          // map half-extent in metres
    private byte[] explored;     // 1 once any source has covered the cell
    private int[] seenStamp;     // == stamp while a source covers the cell this update
    private int stamp;           // increments per update; 0 = never stamped
    private float[] visSmooth;   // texture-only: eased "visible" 0..1
    private float[] expSmooth;   // texture-only: eased "explored" 0..1
    private float[] edgeFade;    // texture-only: 0 on the outermost cell ring, 1 EdgeFadeCells in
    private byte[] pixels;       // RG8 upload buffer

    /// <summary>
    /// Cells over which the mask fades to unexplored at the map edge (12 m). The
    /// shader reads everything past the map as unexplored, and the water depth map
    /// blends to open ocean over the same distance, so a reveal that reaches the edge
    /// dims out rather than stopping on a straight line.
    /// </summary>
    const int EdgeFadeCells = 6;
    private Texture2D mask;
    private float nextUpdate;
    private float lastUpdate;

    /// <summary>Cells per side of the grid (read by the minimap).</summary>
    public int CellsPerSide => n;
    /// <summary>Metres from the map centre to its edge.</summary>
    public float Half => half;

    /// <summary>Create the fog for this scene if there is none. Called by TerrainGrid once the island exists.</summary>
    public static void EnsureExists()
    {
        if (Instance != null) return;
        var go = new GameObject("FogOfWar");
        go.AddComponent<FogOfWar>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        half = TerrainGrid.Half;
        cellSize = Mathf.Max(0.5f, cellSize);
        n = Mathf.FloorToInt(2f * half / cellSize) + 1;

        int count = n * n;
        explored = new byte[count];
        seenStamp = new int[count];
        visSmooth = new float[count];
        expSmooth = new float[count];

        if (!SimHooks.Headless)
        {
            edgeFade = new float[count];
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    int edge = Mathf.Min(Mathf.Min(x, z), Mathf.Min(n - 1 - x, n - 1 - z));
                    edgeFade[z * n + x] = Mathf.SmoothStep(0f, 1f, edge / (float)EdgeFadeCells);
                }
            }
            pixels = new byte[count * 2];
            mask = new Texture2D(n, n, TextureFormat.RG16, false, true)
            {
                name = "FogOfWarMask",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            mask.SetPixelData(pixels, 0);
            mask.Apply(false, false);
            Shader.SetGlobalTexture(MaskId, mask);
        }

        PushParams();
        lastUpdate = Time.time;
        nextUpdate = Time.time;   // first stamp on the first Update, so the opening frame is already fogged right
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            // Whatever loads next (the main menu, a restart's first frames) draws
            // unfogged until a new fog claims the globals. Only the live instance may
            // do this: a rejected duplicate clearing them would unfog the whole map.
            Shader.SetGlobalVector(ParamsId, Vector4.zero);
            Shader.SetGlobalTexture(MaskId, Texture2D.whiteTexture);
        }
        if (mask != null) Destroy(mask);
    }

    void PushParams()
    {
        // uv = worldXZ * a + b, landing on texel CENTRES — the same mapping the water's
        // depth map uses (TerrainGrid.PushWaterProperties).
        float a = 1f / (cellSize * n);
        float b = (half / cellSize + 0.5f) / n;
        Shader.SetGlobalVector(ParamsId, new Vector4(a, b, 0f, SimHooks.Headless ? 0f : 1f));
        Shader.SetGlobalVector(LookId, new Vector4(unexploredBrightness, shroudBrightness, shroudDesaturation, 0f));
        // Re-bind the mask with the params: anything that clears the global (a scene
        // teardown racing this one) would otherwise leave the map unfogged for good.
        if (mask != null) Shader.SetGlobalTexture(MaskId, mask);
    }

    // ------------------------------------------------------------------
    // Queries — raw grid, never the smoothed texture values
    // ------------------------------------------------------------------

    int IndexOf(Vector3 world)
    {
        // Positions off the map (open ocean, a raider still at sea) clamp to the edge cell,
        // like the shader's saturate — so "unexplored" out there until the coast is.
        int x = Mathf.Clamp(Mathf.RoundToInt((world.x + half) / cellSize), 0, n - 1);
        int z = Mathf.Clamp(Mathf.RoundToInt((world.z + half) / cellSize), 0, n - 1);
        return z * n + x;
    }

    /// <summary>Has anything of the colony's ever seen this ground?</summary>
    public bool IsExplored(Vector3 world)
    {
        if (revealAll) return true;
        return explored[IndexOf(world)] != 0;
    }

    /// <summary>Is something of the colony's looking at this ground right now?</summary>
    public bool IsVisible(Vector3 world)
    {
        if (revealAll) return true;
        return stamp > 0 && seenStamp[IndexOf(world)] == stamp;
    }

    /// <summary>
    /// One line for the F4 menu: the globals the shaders read back, and how much of the
    /// grid is explored. Allocates; debug only.
    /// </summary>
    public string DebugLine()
    {
        Vector4 p = Shader.GetGlobalVector(ParamsId);
        Texture t = Shader.GetGlobalTexture(MaskId);
        int exploredCells = 0;
        for (int i = 0; i < explored.Length; i++) if (explored[i] != 0) exploredCells++;
        return "w=" + p.w.ToString("0.#")
            + " mask=" + (t == null ? "null" : t.name + " " + t.width + "x" + t.height)
            + (t != null && mask != null && t != mask ? " (NOT OURS)" : "")
            + " explored=" + exploredCells + "/" + explored.Length
            + " sources=" + VisionSource.ActiveList.Count
            + " sim=" + SimHooks.Simulating + " headless=" + SimHooks.Headless;
    }

    /// <summary>Explored flag of a cell by grid coordinates (minimap).</summary>
    public bool CellExplored(int x, int z) => revealAll || explored[z * n + x] != 0;
    /// <summary>Visible flag of a cell by grid coordinates (minimap).</summary>
    public bool CellVisible(int x, int z) => revealAll || (stamp > 0 && seenStamp[z * n + x] == stamp);

    // ------------------------------------------------------------------
    // Update — stamp, smooth, upload
    // ------------------------------------------------------------------

    void Update()
    {
        if (Time.time < nextUpdate) return;
        nextUpdate = Time.time + updateInterval;
        float dt = Mathf.Max(0.001f, Time.time - lastUpdate);
        lastUpdate = Time.time;

        Stamp();
        if (!SimHooks.Headless)
        {
            Smooth(dt);
            mask.SetPixelData(pixels, 0);
            mask.Apply(false, false);
            PushParams();   // the look fields are live, and this is three uniforms
        }
    }

    void Stamp()
    {
        stamp++;

        // Reveal-all is a live flag read by the queries and by Smooth; it never writes
        // the grid, so switching it off in the F4 menu gives the real fog back (it used
        // to mark every cell explored, and the whole map stayed in the shroud after).
        if (revealAll) return;

        IReadOnlyList<VisionSource> sources = VisionSource.ActiveList;
        for (int s = 0; s < sources.Count; s++)
        {
            VisionSource src = sources[s];
            if (src == null) continue;
            StampDisc(src.transform.position, src.radius);
        }
    }

    void StampDisc(Vector3 centre, float radius)
    {
        if (radius <= 0f) return;
        float fx = (centre.x + half) / cellSize;
        float fz = (centre.z + half) / cellSize;
        float r = radius / cellSize;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(fx - r));
        int x1 = Mathf.Min(n - 1, Mathf.CeilToInt(fx + r));
        int z0 = Mathf.Max(0, Mathf.FloorToInt(fz - r));
        int z1 = Mathf.Min(n - 1, Mathf.CeilToInt(fz + r));
        float r2 = r * r;

        for (int z = z0; z <= z1; z++)
        {
            float dz = z - fz;
            int row = z * n;
            for (int x = x0; x <= x1; x++)
            {
                float dx = x - fx;
                if (dx * dx + dz * dz > r2) continue;
                int i = row + x;
                seenStamp[i] = stamp;
                explored[i] = 1;
            }
        }
    }

    void Smooth(float dt)
    {
        float kUp = 1f - Mathf.Exp(-revealRate * dt);
        float kDown = 1f - Mathf.Exp(-shroudRate * dt);
        for (int i = 0; i < explored.Length; i++)
        {
            float visTarget = revealAll || seenStamp[i] == stamp ? 1f : 0f;
            float v = visSmooth[i];
            v += (visTarget - v) * (visTarget > v ? kUp : kDown);
            visSmooth[i] = v;

            float expTarget = revealAll || explored[i] != 0 ? 1f : 0f;
            float e = expSmooth[i];
            e += (expTarget - e) * (expTarget > e ? kUp : kDown);
            expSmooth[i] = e;

            // The map-edge fade is texture-only: the grid stays honest for the queries.
            float fade = edgeFade[i];
            pixels[i * 2] = (byte)(e * fade * 255f + 0.5f);
            pixels[i * 2 + 1] = (byte)(v * fade * 255f + 0.5f);
        }
    }
}

/// <summary>
/// Fog-aware copies of shared materials for surfaces that have no material collector of
/// their own — the terrain chunks and the static-batched scatter decor. A copy per
/// distinct source material, on the OccluderCutout shader with the unit windows OFF, so
/// the ground reads the fog mask without cutting holes around anyone. Copies are
/// instances, never the assets: writing an asset's shader in Play mode dirties it on disk.
/// </summary>
public static class FogMaterials
{
    private const string ShaderPath = "Shaders/OccluderCutout";
    private const string LitShaderName = "Universal Render Pipeline/Lit";

    private static readonly Dictionary<Material, Material> copies = new Dictionary<Material, Material>();
    private static Shader shader;
    private static bool looked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { copies.Clear(); shader = null; looked = false; }

    /// <summary>
    /// The fog-aware twin of <paramref name="source"/>. Returns the source itself when it
    /// is not on URP Lit (the water has its own shader and its own fog sampling), when the
    /// fog shader is missing, or under the sim.
    /// </summary>
    public static Material For(Material source)
    {
        if (source == null || SimHooks.Headless) return source;
        if (copies.TryGetValue(source, out Material copy) && copy != null) return copy;

        if (!looked)
        {
            looked = true;
            shader = Resources.Load<Shader>(ShaderPath);
            if (shader == null)
                Debug.LogWarning("FogMaterials: Resources/" + ShaderPath + " missing — the ground ignores the fog this run.");
        }
        if (shader == null || source.shader == null || source.shader.name != LitShaderName) return source;

        copy = new Material(source) { name = source.name + " (fog)", shader = shader };
        copy.DisableKeyword(OccluderCutout.UnitCutoutKeyword);
        copies[source] = copy;
        return copy;
    }

    /// <summary>Swap every renderer under <paramref name="root"/> onto fog-aware copies of its shared materials.</summary>
    public static void Apply(GameObject root)
    {
        if (root == null || SimHooks.Headless) return;
        MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
        for (int r = 0; r < renderers.Length; r++)
        {
            Material[] mats = renderers[r].sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = For(mats[i]);
                if (m != mats[i]) { mats[i] = m; changed = true; }
            }
            if (changed) renderers[r].sharedMaterials = mats;
        }
    }
}
