using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The minimap (2026-09-09, fog of war step 6): a north-up picture of the island in the
/// top-right corner, painted from the fog grid. Unexplored ground is near-black,
/// explored ground nobody is watching sits in the same dim shroud the terrain does,
/// watched ground shows its true surface colour. Over it: buildings and walls, your
/// colonists and warriors, the castaway, raiders standing on watched ground, the
/// camera's footprint, and a pulse where the last raid came ashore.
/// </summary>
/// <remarks>
/// One texture, <c>Scale</c> texels per fog cell, rebuilt <c>RefreshHz</c> times a second
/// from a cached surface palette: paint the base colour through the fog, then stamp
/// markers over it. No per-marker objects, so a colony of eighty and a raid of thirty
/// cost the same as an empty island. The camera outline and the landing pulse are the
/// only uGUI objects that move per frame.
///
/// The map answers "where did that raid land" on purpose, fog or not: a landing is heard
/// along the coast (<see cref="EnemySpawner.OnRaidLanded"/>). Everything else obeys the
/// fog — a raider draws only where <see cref="FogOfWar.IsVisible"/> says so, the same
/// rule <see cref="FogVisibility"/> hides its body by.
///
/// Left-click or drag on the map centres the camera there. Gameplay clicks under the
/// map are refused through <see cref="PointerOver"/>: nothing in the HUD stops a ground
/// raycast on its own, and a map this size would otherwise place huts and command the
/// castaway through itself. Runtime-added by <see cref="PlayerCharacter"/> beside the
/// other HUD boxes; never built under the balance sim.
/// </remarks>
public class Minimap : MonoBehaviour
{
    private static Minimap instance;

    /// <summary>Side of the map square in canvas pixels. The tracker sits below it.</summary>
    public const float MapSize = 208f;
    /// <summary>Inset of the panel from the screen corner.</summary>
    public const float Margin = 16f;
    /// <summary>Frame around the map picture inside the panel.</summary>
    const float Frame = 4f;
    /// <summary>Panel side: what anything laid out under the map needs to clear.</summary>
    public const float PanelSize = MapSize + Frame * 2f;
    const int SortOrder = 45;

    /// <summary>Texels per fog cell. 2 m cells at 2 = a colonist dot is one cell wide and two texels tall.</summary>
    const int Scale = 2;
    const float RefreshHz = 8f;
    /// <summary>Seconds the landing pulse rings, then how long the landing mark stays on the map.</summary>
    const float PulseSeconds = 12f;
    const float LandingMarkSeconds = 90f;
    const float PulsePeriod = 1.2f;
    const float PulseMaxRadius = 22f;

    // Palette. Surface colours come from the terrain materials; these are what the
    // materials cannot tell us and what sits on top.
    static readonly Color32 DeepSea = new Color32(18, 42, 74, 255);
    static readonly Color32 ShallowSea = new Color32(52, 112, 142, 255);
    static readonly Color32 Unexplored = new Color32(9, 10, 14, 255);
    static readonly Color32 ColonistDot = new Color32(242, 237, 224, 255);
    static readonly Color32 WarriorDot = new Color32(242, 204, 115, 255);
    static readonly Color32 PlayerDot = new Color32(255, 255, 255, 255);
    static readonly Color32 PlayerRim = new Color32(40, 40, 48, 255);
    static readonly Color32 RaiderDot = new Color32(235, 92, 78, 255);
    static readonly Color32 BuildingFill = new Color32(217, 184, 115, 255);
    static readonly Color32 CampfireFill = new Color32(255, 150, 60, 255);
    static readonly Color32 SiteFill = new Color32(150, 128, 84, 255);
    static readonly Color32 WallFill = new Color32(176, 160, 128, 255);
    static readonly Color32 GateFill = new Color32(205, 175, 110, 255);
    static readonly Color32 LandingMark = new Color32(235, 92, 78, 255);
    static readonly Color ViewOutline = new Color(1f, 1f, 1f, 0.75f);
    static readonly Color PulseColor = new Color(0.92f, 0.36f, 0.30f, 1f);
    const float ShroudLevel = 0.5f;
    const float ShroudGrey = 0.45f;

    /// <summary>True while the mouse is over the map picture. Gameplay clicks check it.</summary>
    public static bool PointerOver { get; private set; }

    private RectTransform panel;
    private RectTransform mapRect;
    private RawImage mapImage;
    private Texture2D texture;
    private Color32[] pixels;
    private Color32[] ground;      // cached surface colour per texel, painted once
    private int n;                 // fog cells per side
    private int res;               // texels per side = n * Scale
    private float half;
    private float cellSize;
    private float nextRefresh;
    private bool dragging;

    private RectTransform[] viewEdges = new RectTransform[4];
    private Image pulse;
    private Vector3 landing;
    private float landedAt = -1000f;

    /// <summary>Create the map for this scene if there is none. Skipped under the sim.</summary>
    public static void Ensure()
    {
        if (instance != null || SimHooks.Headless) return;
        GameObject go = new GameObject("[Minimap]");
        instance = go.AddComponent<Minimap>();
    }

    void Awake() { instance = this; }

    void OnEnable() { EnemySpawner.OnRaidLanded += OnRaidLanded; }

    void OnDisable()
    {
        EnemySpawner.OnRaidLanded -= OnRaidLanded;
        PointerOver = false;
        dragging = false;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        if (texture != null) Destroy(texture);
    }

    // ------------------------------------------------------------------
    // Construction
    // ------------------------------------------------------------------

    void Build()
    {
        FogOfWar fog = FogOfWar.Instance;
        TerrainGrid terrain = TerrainGrid.Instance;
        if (fog == null || terrain == null) return;   // try again next frame

        n = fog.CellsPerSide;
        half = fog.Half;
        cellSize = fog.cellSize;
        res = n * Scale;
        pixels = new Color32[res * res];
        ground = new Color32[res * res];
        PaintGround(terrain);

        texture = new Texture2D(res, res, TextureFormat.RGBA32, false, false)
        {
            name = "MinimapTexture",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        Canvas canvas = MenuBuilder.CreateCanvas("MinimapCanvas", SortOrder);
        canvas.transform.SetParent(transform, false);

        panel = MenuBuilder.Panel(canvas.transform, "MinimapPanel", PanelSize, PanelSize);
        panel.anchorMin = panel.anchorMax = new Vector2(1f, 1f);
        panel.pivot = new Vector2(1f, 1f);
        panel.anchoredPosition = new Vector2(-Margin, -Margin);

        // The picture. RectMask2D clips the camera outline, which runs off the island
        // whenever the view is zoomed out or tilted toward the horizon.
        GameObject mapGo = new GameObject("Map", typeof(RectTransform), typeof(RawImage), typeof(RectMask2D));
        mapGo.transform.SetParent(panel, false);
        mapRect = mapGo.GetComponent<RectTransform>();
        mapRect.anchorMin = mapRect.anchorMax = new Vector2(0.5f, 0.5f);
        mapRect.pivot = new Vector2(0.5f, 0.5f);
        mapRect.sizeDelta = new Vector2(MapSize, MapSize);
        mapRect.anchoredPosition = Vector2.zero;
        mapImage = mapGo.GetComponent<RawImage>();
        mapImage.texture = texture;
        mapImage.raycastTarget = true;   // a click surface, even though clicks are read from Input

        for (int i = 0; i < 4; i++)
        {
            RectTransform edge = MenuBuilder.SimpleImage(mapRect, "ViewEdge" + i, ViewOutline);
            edge.anchorMin = edge.anchorMax = new Vector2(0.5f, 0.5f);
            edge.pivot = new Vector2(0.5f, 0.5f);
            edge.sizeDelta = new Vector2(0f, 1.5f);
            viewEdges[i] = edge;
        }

        GameObject pulseGo = new GameObject("LandingPulse", typeof(RectTransform), typeof(Image));
        pulseGo.transform.SetParent(mapRect, false);
        pulse = pulseGo.GetComponent<Image>();
        pulse.sprite = HudTimeDial.CircleSprite();
        pulse.color = PulseColor;
        pulse.raycastTarget = false;
        pulse.rectTransform.anchorMin = pulse.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        pulse.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        pulseGo.SetActive(false);

        Refresh();
    }

    /// <summary>
    /// Surface colour per texel, once. Sea by depth, land by the band the terrain would
    /// draw it in (<see cref="TerrainGrid.SurfaceAt"/>), so the map matches the ground.
    /// Sampled at the texel's own world position rather than the fog cell's, which is
    /// what keeps the coastline smooth at two texels per cell.
    /// </summary>
    void PaintGround(TerrainGrid terrain)
    {
        Color32[] bands = new Color32[TerrainGrid.SurfaceCount];
        for (int i = 0; i < bands.Length; i++) bands[i] = terrain.SurfaceColor((TerrainGrid.Surface)i);

        Vector3 p = Vector3.zero;
        for (int tz = 0; tz < res; tz++)
        {
            p.z = TexelToWorld(tz);
            for (int tx = 0; tx < res; tx++)
            {
                p.x = TexelToWorld(tx);
                float h = terrain.SampleHeight(p);
                Color32 c;
                if (h <= TerrainGrid.DeepWaterY) c = DeepSea;
                else if (h <= 0f) c = Color32.Lerp(DeepSea, ShallowSea, Mathf.InverseLerp(TerrainGrid.DeepWaterY, 0f, h));
                else c = bands[(int)terrain.SurfaceAt(p)];
                ground[tz * res + tx] = c;
            }
        }
    }

    // ------------------------------------------------------------------
    // Mapping
    // ------------------------------------------------------------------

    // Fog cell x covers texels [x*Scale, x*Scale + Scale); cell x is centred on world
    // x*cellSize - half. So texel t sits at world ((t + 0.5) / Scale - 0.5) * cellSize - half.
    float TexelToWorld(int t) => ((t + 0.5f) / Scale - 0.5f) * cellSize - half;
    int WorldToTexel(float w) => Mathf.RoundToInt(((w + half) / cellSize + 0.5f) * Scale - 0.5f);

    /// <summary>World XZ to a position in the map rect (pivot centre), in canvas pixels.</summary>
    Vector2 WorldToMap(Vector3 w)
    {
        float u = ((w.x + half) / cellSize + 0.5f) / n;
        float v = ((w.z + half) / cellSize + 0.5f) / n;
        return new Vector2((u - 0.5f) * MapSize, (v - 0.5f) * MapSize);
    }

    Vector3 MapToWorld(Vector2 local)
    {
        float u = local.x / MapSize + 0.5f;
        float v = local.y / MapSize + 0.5f;
        float x = (u * n - 0.5f) * cellSize - half;
        float z = (v * n - 0.5f) * cellSize - half;
        return new Vector3(x, 0f, z);
    }

    // ------------------------------------------------------------------
    // Per frame
    // ------------------------------------------------------------------

    void Update()
    {
        if (panel == null)
        {
            Build();
            if (panel == null) return;
        }

        HandlePointer();
        UpdateViewOutline();
        UpdatePulse();

        if (Time.time >= nextRefresh)
        {
            nextRefresh = Time.time + 1f / RefreshHz;
            Refresh();
        }
    }

    void HandlePointer()
    {
        Vector2 mouse = Input.mousePosition;
        bool over = mapRect.gameObject.activeInHierarchy
            && RectTransformUtility.RectangleContainsScreenPoint(mapRect, mouse, null);
        PointerOver = over || dragging;

        if (PauseController.BlockGameplayInput) { dragging = false; return; }

        if (Input.GetMouseButtonDown(0) && over) dragging = true;
        if (!Input.GetMouseButton(0)) dragging = false;
        if (!dragging) return;

        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(mapRect, mouse, null, out local)) return;
        local.x = Mathf.Clamp(local.x, -MapSize * 0.5f, MapSize * 0.5f);
        local.y = Mathf.Clamp(local.y, -MapSize * 0.5f, MapSize * 0.5f);

        Vector3 world = MapToWorld(local);
        TerrainGrid terrain = TerrainGrid.Instance;
        if (terrain != null) world.y = Mathf.Max(0f, terrain.SampleHeight(world));
        if (CameraController.Instance != null)
        {
            CameraController.Instance.CenterOn(world);
            if (Input.GetMouseButtonDown(0)) DevQuests.Signal("minimap:click");
        }
    }

    /// <summary>
    /// The camera's footprint on the sea plane: the four viewport corners cast to y = 0
    /// and joined by thin strips. An ortho, tilted camera sees a parallelogram, and the
    /// far edge can be well off the island; the map's mask clips whatever runs out.
    /// </summary>
    void UpdateViewOutline()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Plane sea = new Plane(Vector3.up, Vector3.zero);
        Vector2 c0 = ViewportCorner(cam, sea, 0f, 0f);
        Vector2 c1 = ViewportCorner(cam, sea, 1f, 0f);
        Vector2 c2 = ViewportCorner(cam, sea, 1f, 1f);
        Vector2 c3 = ViewportCorner(cam, sea, 0f, 1f);
        SetEdge(viewEdges[0], c0, c1);
        SetEdge(viewEdges[1], c1, c2);
        SetEdge(viewEdges[2], c2, c3);
        SetEdge(viewEdges[3], c3, c0);
    }

    Vector2 ViewportCorner(Camera cam, Plane sea, float u, float v)
    {
        Ray ray = cam.ViewportPointToRay(new Vector3(u, v, 0f));
        float d;
        if (!sea.Raycast(ray, out d)) d = 1000f;   // looking at the sky: push the corner far out
        return WorldToMap(ray.GetPoint(d));
    }

    static void SetEdge(RectTransform edge, Vector2 a, Vector2 b)
    {
        Vector2 d = b - a;
        float len = d.magnitude;
        edge.anchoredPosition = (a + b) * 0.5f;
        edge.sizeDelta = new Vector2(len, 1.5f);
        edge.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
    }

    void OnRaidLanded(Vector3 where)
    {
        landing = where;
        landedAt = Time.time;
        if (pulse != null)
        {
            pulse.rectTransform.anchoredPosition = WorldToMap(where);
            pulse.gameObject.SetActive(true);
        }
        DevQuests.Signal("minimap:landing");
    }

    void UpdatePulse()
    {
        if (pulse == null || !pulse.gameObject.activeSelf) return;
        float since = Time.time - landedAt;
        if (since > PulseSeconds) { pulse.gameObject.SetActive(false); return; }
        float t = Mathf.Repeat(since, PulsePeriod) / PulsePeriod;   // 0..1 per ring
        float r = Mathf.Lerp(4f, PulseMaxRadius, t);
        pulse.rectTransform.sizeDelta = new Vector2(r * 2f, r * 2f);
        Color c = PulseColor;
        c.a = (1f - t) * 0.8f;
        pulse.color = c;
    }

    // ------------------------------------------------------------------
    // The picture
    // ------------------------------------------------------------------

    void Refresh()
    {
        FogOfWar fog = FogOfWar.Instance;
        if (fog == null || texture == null) return;

        // Ground through the fog, one fog cell = Scale × Scale texels
        for (int z = 0; z < n; z++)
        {
            for (int x = 0; x < n; x++)
            {
                int state = fog.CellVisible(x, z) ? 2 : fog.CellExplored(x, z) ? 1 : 0;
                for (int dz = 0; dz < Scale; dz++)
                {
                    int row = (z * Scale + dz) * res + x * Scale;
                    for (int dx = 0; dx < Scale; dx++)
                    {
                        int i = row + dx;
                        pixels[i] = state == 2 ? ground[i] : state == 1 ? Shroud(ground[i]) : Unexplored;
                    }
                }
            }
        }

        // Structures first, units over them
        var walls = Wall.ActiveList;
        for (int i = 0; i < walls.Count; i++) StampOwned(fog, walls[i], 1, WallFill);
        var gates = Gate.ActiveList;
        for (int i = 0; i < gates.Count; i++) StampOwned(fog, gates[i], 1, GateFill);
        var sites = ConstructionSite.ActiveList;
        for (int i = 0; i < sites.Count; i++) StampOwned(fog, sites[i], 3, SiteFill);
        var huts = Hut.ActiveList;
        for (int i = 0; i < huts.Count; i++) StampOwned(fog, huts[i], 3, BuildingFill);
        var towers = Watchtower.ActiveList;
        for (int i = 0; i < towers.Count; i++) StampOwned(fog, towers[i], 3, BuildingFill);
        var shops = Workshop.ActiveList;
        for (int i = 0; i < shops.Count; i++) StampOwned(fog, shops[i], 3, BuildingFill);
        var yards = Shipyard.ActiveList;
        for (int i = 0; i < yards.Count; i++) StampOwned(fog, yards[i], 4, BuildingFill);
        var fires = BaseBuilding.ActiveList;
        for (int i = 0; i < fires.Count; i++) StampOwned(fog, fires[i], 4, CampfireFill);

        if (Time.time - landedAt < LandingMarkSeconds) Ring(landing, 3, LandingMark);

        var workers = Worker.ActiveList;
        for (int i = 0; i < workers.Count; i++) StampOwned(fog, workers[i], 2, ColonistDot);
        var warriors = Warrior.ActiveList;
        for (int i = 0; i < warriors.Count; i++) StampOwned(fog, warriors[i], 2, WarriorDot);

        // Raiders only on watched ground: the same question FogVisibility asks for the body
        var raiders = Enemy.ActiveList;
        for (int i = 0; i < raiders.Count; i++)
        {
            Vector3 p = raiders[i].transform.position;
            if (fog.IsVisible(p)) Stamp(p, 2, RaiderDot);
        }

        PlayerCharacter player = PlayerCharacter.Instance;
        if (player != null)
        {
            Stamp(player.transform.position, 4, PlayerRim);
            Stamp(player.transform.position, 2, PlayerDot);
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
    }

    static Color32 Shroud(Color32 c)
    {
        float grey = (c.r * 0.3f + c.g * 0.59f + c.b * 0.11f);
        float r = Mathf.Lerp(c.r, grey, ShroudGrey) * ShroudLevel;
        float g = Mathf.Lerp(c.g, grey, ShroudGrey) * ShroudLevel;
        float b = Mathf.Lerp(c.b, grey, ShroudGrey) * ShroudLevel;
        return new Color32((byte)r, (byte)g, (byte)b, 255);
    }

    /// <summary>A filled square of <paramref name="size"/> texels centred on a world position.</summary>
    /// <summary>The player's things in the map's own colours; another colony's in its faction colour, and only on watched ground (like a raider) - unless it is an ally, whose colony is shared (2026-09-16).</summary>
    void StampOwned<T>(FogOfWar fog, T t, int size, Color32 mine) where T : Component, IOwned
    {
        if (t == null) return;
        Vector3 p = t.transform.position;
        Faction f = t.Faction;
        if (f.IsPlayer) { Stamp(p, size, mine); return; }
        if (fog == null || fog.IsVisible(p) || f.IsAlliedWith(Factions.Player)) Stamp(p, size, (Color32)f.Color);
    }

    void Stamp(Vector3 world, int size, Color32 color)
    {
        int cx = WorldToTexel(world.x);
        int cz = WorldToTexel(world.z);
        int lo = -(size / 2), hi = lo + size - 1;   // size 2 → [-1, 0]; size 3 → [-1, 1]
        for (int dz = lo; dz <= hi; dz++)
        {
            int z = cz + dz;
            if (z < 0 || z >= res) continue;
            int row = z * res;
            for (int dx = lo; dx <= hi; dx++)
            {
                int x = cx + dx;
                if (x < 0 || x >= res) continue;
                pixels[row + x] = color;
            }
        }
    }

    /// <summary>A hollow square outline of half-width <paramref name="r"/> texels.</summary>
    void Ring(Vector3 world, int r, Color32 color)
    {
        int cx = WorldToTexel(world.x);
        int cz = WorldToTexel(world.z);
        for (int dz = -r; dz <= r; dz++)
        {
            int z = cz + dz;
            if (z < 0 || z >= res) continue;
            bool edgeRow = dz == -r || dz == r;
            for (int dx = -r; dx <= r; dx++)
            {
                if (!edgeRow && dx != -r && dx != r) continue;
                int x = cx + dx;
                if (x < 0 || x >= res) continue;
                pixels[z * res + x] = color;
            }
        }
    }
}
