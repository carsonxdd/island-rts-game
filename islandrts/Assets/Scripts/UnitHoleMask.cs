using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws one soft disc per unit into a screen-space texture every frame and binds it as
/// the global <c>_UnitHoleMask</c>, which every <see cref="OccluderCutout"/> material
/// samples to open a window around units standing behind it (2026-09-08).
/// </summary>
/// <remarks>
/// Red is coverage (1 at the unit's centre, 0 at the disc edge), green is the view depth
/// of the farthest unit covering that pixel. The occluder's fragment shader compares its
/// own depth against green: nearer means "between the camera and someone", and it clips.
/// The whole "is this tree hiding that worker" question is therefore answered per pixel
/// by the GPU, for every occluder at once, with no silhouette guess on the CPU — the old
/// per-tree segment test is what missed trees.
///
/// Quarter resolution, RGHalf, blitted the way <c>CloudSystem</c> blits its cookie. Up to
/// <see cref="MaxUnits"/> units per frame; the player is listed first so they are never
/// the one dropped on a very busy screen.
///
/// Self-bootstrapping from OccluderCutout.Awake, runtime-added, so its public fields are
/// the live values. NOT DontDestroyOnLoad — it holds no state worth carrying across a
/// scene load, and it rebinds a black mask when it goes so stale materials stay solid.
/// </remarks>
public class UnitHoleMask : MonoBehaviour
{
    /// <summary>Must match MAX_UNITS in UnitHoleMask.shader.</summary>
    public const int MaxUnits = 128;

    [Tooltip("World radius of the window around a unit. About a unit's height, so head and feet both show.")]
    public float holeRadius = 1.1f;
    [Tooltip("Height above the unit's feet the window is centred on, in metres.")]
    public float holeCentreHeight = 0.9f;
    [Tooltip("How far in FRONT of the unit (view depth) a surface must be before it cuts. Stops a trunk level with a unit from flickering.")]
    public float depthMargin = 0.5f;
    [Tooltip("Depth band over which the cut ramps in past the margin, so the hole appears rather than pops.")]
    public float depthSoft = 0.6f;
    [Tooltip("Mask resolution divisor. 4 = quarter resolution; the disc edge is soft anyway.")]
    public int downscale = 4;
    [Tooltip("Flip the mask vertically. Off is right for the URP forward path; toggle if the windows open below the units.")]
    public bool flipY = false;

    private static UnitHoleMask instance;

    private static readonly int MaskId = Shader.PropertyToID("_UnitHoleMask");
    private static readonly int DepthMarginId = Shader.PropertyToID("_UnitHoleDepthMargin");
    private static readonly int DepthSoftId = Shader.PropertyToID("_UnitHoleDepthSoft");
    private static readonly int UnitCountId = Shader.PropertyToID("_UnitCount");
    private static readonly int AspectId = Shader.PropertyToID("_Aspect");
    private static readonly int UnitsId = Shader.PropertyToID("_Units");

    private readonly Vector4[] units = new Vector4[MaxUnits];
    private int count;
    private Camera cam;
    private Material blit;
    private RenderTexture mask;

    /// <summary>Create the mask writer if this scene does not have one yet.</summary>
    public static void Ensure()
    {
        if (instance != null) return;
        if (SimHooks.Simulating) return;  // headless: no camera, and this is pure cosmetics

        GameObject go = new GameObject("_UnitHoleMask");
        instance = go.AddComponent<UnitHoleMask>();
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;

        Shader shader = Resources.Load<Shader>("Shaders/UnitHoleMask");
        if (shader == null)
            Debug.LogWarning("UnitHoleMask: Resources/Shaders/UnitHoleMask missing — occluders stay solid this run.");
        else
            blit = new Material(shader);

        // Nothing cuts until the first blit lands: an unbound global samples grey, and
        // half coverage everywhere would open a hole in every tree on the first frame.
        Shader.SetGlobalTexture(MaskId, Texture2D.blackTexture);
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        Shader.SetGlobalTexture(MaskId, Texture2D.blackTexture);
        if (mask != null) { mask.Release(); Destroy(mask); }
        if (blit != null) Destroy(blit);
    }

    void LateUpdate()
    {
        if (blit == null) return;
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        int w = Mathf.Max(16, cam.pixelWidth / Mathf.Max(1, downscale));
        int h = Mathf.Max(16, cam.pixelHeight / Mathf.Max(1, downscale));
        if (mask == null || mask.width != w || mask.height != h) Rebuild(w, h);

        Collect();

        blit.SetInteger(UnitCountId, count);
        blit.SetFloat(AspectId, (float)cam.pixelWidth / Mathf.Max(1, cam.pixelHeight));
        blit.SetVectorArray(UnitsId, units);   // always the full array: SetVectorArray fixes the size on first call
        Graphics.Blit(null, mask, blit);

        Shader.SetGlobalTexture(MaskId, mask);
        Shader.SetGlobalFloat(DepthMarginId, depthMargin);
        Shader.SetGlobalFloat(DepthSoftId, depthSoft);
    }

    void Rebuild(int w, int h)
    {
        if (mask != null) { mask.Release(); Destroy(mask); }
        mask = new RenderTexture(w, h, 0, RenderTextureFormat.RGHalf, RenderTextureReadWrite.Linear)
        {
            name = "_UnitHoleMask",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
        };
        mask.Create();
    }

    void Collect()
    {
        count = 0;

        // The player's own character is there for the whole run; a knocked-out body is
        // hidden and must not keep a window open. Listed first so a crowded screen never
        // drops them.
        PlayerCharacter player = PlayerCharacter.Instance;
        if (player != null && !player.IsKnockedOut) Add(player.transform.position);

        AddUnits(Worker.ActiveList);
        AddUnits(Warrior.ActiveList);

        // A raider the fog hides must not open a window either — the window would give
        // it away (2026-09-09).
        IReadOnlyList<Enemy> enemies = Enemy.ActiveList;
        for (int i = 0; i < enemies.Count && count < MaxUnits; i++)
        {
            Enemy e = enemies[i];
            if (e == null || !e.gameObject.activeInHierarchy) continue;
            if (e.fog != null && e.fog.Hidden) continue;
            Add(e.transform.position);
        }
    }

    void AddUnits<T>(IReadOnlyList<T> list) where T : MonoBehaviour
    {
        for (int i = 0; i < list.Count && count < MaxUnits; i++)
        {
            T unit = list[i];
            if (unit == null || !unit.gameObject.activeInHierarchy) continue;  // garrisoned workers are hidden
            Add(unit.transform.position);
        }
    }

    void Add(Vector3 feet)
    {
        if (count >= MaxUnits) return;

        Vector3 world = feet + Vector3.up * holeCentreHeight;
        Vector3 screen = cam.WorldToScreenPoint(world);   // z = view depth, the same axis the shader reads
        float pw = cam.pixelWidth, ph = cam.pixelHeight;

        // Disc radius in uv-height units. Orthographic: half the view is orthographicSize
        // metres, so the whole height is 2 × size. Perspective (showcase scene only):
        // the view height at the unit's depth.
        float viewHeight = cam.orthographic
            ? 2f * cam.orthographicSize
            : 2f * Mathf.Max(0.01f, screen.z) * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float radiusUv = holeRadius / Mathf.Max(0.01f, viewHeight);

        // Off-screen by more than a radius: nothing on screen can be hiding it.
        float marginPx = radiusUv * ph;
        if (screen.x < -marginPx || screen.x > pw + marginPx || screen.y < -marginPx || screen.y > ph + marginPx) return;
        if (!cam.orthographic && screen.z <= 0f) return;

        float v = screen.y / ph;
        if (flipY) v = 1f - v;
        units[count++] = new Vector4(screen.x / pw, v, screen.z, radiusUv);
    }
}
