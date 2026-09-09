using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The sky's cloud cover (2026-09-08): a condition rolled each dawn, drifting
/// puffs drawn as soft noise quads at altitude, and the shade they cast on the
/// island, rendered into the sun's light cookie so it darkens terrain, units
/// and buildings alike with no hard edge.
/// </summary>
/// <remarks>
/// Runtime-added by <see cref="DayNightCycle"/> (never in the scene: the public
/// fields here are the live values). Four conditions:
/// <list type="bullet">
/// <item>Sunny — nothing.</item>
/// <item>Slightly cloudy — two to four puffs.</item>
/// <item>Partly cloudy — about ten, plus a whisper of high overcast.</item>
/// <item>Cloudy — a broken overcast sheet over everything, dimmed sun, softer
/// shadows; light still gets through in the thin parts.</item>
/// </list>
/// A change blends over <see cref="transitionSeconds"/>: puffs fade in and out,
/// the overcast and the three lighting multipliers slide. The multipliers are
/// statics <see cref="DayNightCycle"/> reads every frame — 1 when there is no
/// system (main menu, sim).
///
/// The cookie is the trick that keeps the shade and the puff the same shape:
/// CloudCookie.shader evaluates the SAME coverage function CloudPuff.shader
/// draws, in light space, so the ground shadow lands where the sun's angle puts
/// it and follows the sun across the day. Off under the sim (no camera) and
/// when the Clouds setting is Off.
/// </remarks>
public class CloudSystem : MonoBehaviour
{
    public enum Condition { Sunny, SlightlyCloudy, PartlyCloudy, Cloudy }
    public static readonly string[] ConditionNames = { "Sunny", "Slightly cloudy", "Partly cloudy", "Cloudy" };
    static readonly string[] SignalKeys = { "sky:sunny", "sky:slightly", "sky:partly", "sky:cloudy" };

    public static CloudSystem Instance { get; private set; }

    /// <summary>Sun intensity × this (1 = clear sky). Read by DayNightCycle every frame.</summary>
    public static float SunMultiplier = 1f;
    public static float AmbientMultiplier = 1f;
    public static float ShadowStrengthMultiplier = 1f;

    const int MaxPuffs = 16;

    [Header("Sky")]
    [Tooltip("Altitude of the cloud layer. Above the tallest scenery, low enough that CameraController's near clip can keep it in frame at full zoom-out.")]
    public float cloudHeight = 30f;
    [Tooltip("Wind, metres per second in world XZ. Puffs drift with it and wrap back in upwind.")]
    public Vector2 wind = new Vector2(1.4f, 0.6f);
    [Tooltip("Seconds for a dawn's new condition to fully arrive (puff fades, overcast, lighting).")]
    public float transitionSeconds = 30f;
    [Tooltip("Roll weights per condition (Sunny, Slightly, Partly, Cloudy).")]
    public float[] conditionWeights = { 0.4f, 0.3f, 0.2f, 0.1f };

    [Header("Look")]
    public float puffRadiusMin = 10f;
    public float puffRadiusMax = 22f;
    [Tooltip("How dark the deepest cloud shade gets: 0.55 = 45% sun left under the thickest part. Keep it gentle — the transition is what sells it.")]
    [Range(0f, 1f)] public float shadowDensity = 0.55f;
    [Range(0f, 1f)] public float puffAlpha = 0.8f;
    [Range(0f, 1f)] public float sheetAlpha = 0.45f;
    public int cookieResolution = 512;

    // Per-condition targets, indexed by Condition.
    static readonly int[] PuffCounts = { 0, 3, 10, 6 };
    static readonly float[] OvercastTargets = { 0f, 0f, 0.12f, 0.75f };
    static readonly float[] SunTargets = { 1f, 0.97f, 0.9f, 0.55f };
    static readonly float[] AmbientTargets = { 1f, 1f, 0.96f, 0.82f };
    static readonly float[] ShadowTargets = { 1f, 1f, 0.9f, 0.5f };

    public Condition Current { get; private set; } = Condition.Sunny;

    class Puff
    {
        public Vector2 pos;
        public float radius, seed, opacity, targetOpacity, windScale, fadeSeconds;
        public Transform quad;
        public MeshRenderer renderer;
        public bool Active => targetOpacity > 0f || opacity > 0.001f;
    }

    readonly Puff[] puffs = new Puff[MaxPuffs];
    readonly Vector4[] cloudArray = new Vector4[MaxPuffs];
    readonly Vector4[] seedArray = new Vector4[MaxPuffs];
    MaterialPropertyBlock block;

    Light sun;
    Material puffMaterial, cookieMaterial;
    Mesh quadMesh;
    RenderTexture cookie;
    Transform sheet;
    MeshRenderer sheetRenderer;
    float overcast, fieldRadius, cookieSize, clock;
    Vector2 drift;
    bool cookieBound;

    static readonly int CloudId = Shader.PropertyToID("_Cloud");
    static readonly int SeedId = Shader.PropertyToID("_Seed");
    static readonly int IsSheetId = Shader.PropertyToID("_IsSheet");
    static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    static readonly int CloudTimeId = Shader.PropertyToID("_CloudTime");
    static readonly int CloudDriftId = Shader.PropertyToID("_CloudDrift");
    static readonly int CloudOvercastId = Shader.PropertyToID("_CloudOvercast");
    static readonly int SunTintId = Shader.PropertyToID("_CloudSunTint");
    static readonly int LightToWorldId = Shader.PropertyToID("_LightToWorld");
    static readonly int CookieSizeId = Shader.PropertyToID("_CookieSize");
    static readonly int CloudHeightId = Shader.PropertyToID("_CloudHeight");
    static readonly int ShadowDensityId = Shader.PropertyToID("_ShadowDensity");
    static readonly int CloudCountId = Shader.PropertyToID("_CloudCount");
    static readonly int CloudsId = Shader.PropertyToID("_Clouds");
    private bool wasOff;   // playtest only: the Clouds setting just went Off
    static readonly int CloudSeedsId = Shader.PropertyToID("_CloudSeeds");

    /// <summary>Create the system for this scene's sun. No-op under the sim or if one exists.</summary>
    public static void EnsureExists(Light sunLight)
    {
        if (Instance != null || SimHooks.Simulating || sunLight == null) return;
        var go = new GameObject("CloudSystem");
        var cs = go.AddComponent<CloudSystem>();
        cs.sun = sunLight;
    }

    void Awake()
    {
        Instance = this;
        SunMultiplier = AmbientMultiplier = ShadowStrengthMultiplier = 1f;
    }

    void Start()
    {
        puffMaterial = Resources.Load<Material>("Clouds/Mat_CloudPuff");
        cookieMaterial = Resources.Load<Material>("Clouds/Mat_CloudCookie");
        if (puffMaterial == null || cookieMaterial == null)
        {
            Debug.LogWarning("CloudSystem: Resources/Clouds materials missing — no clouds this run.");
            enabled = false;
            return;
        }

        fieldRadius = TerrainGrid.SizeScale * 75f + 15f;
        cookieSize = 2f * (fieldRadius + cloudHeight) + 20f;
        block = new MaterialPropertyBlock();
        quadMesh = BuildQuad();

        for (int i = 0; i < MaxPuffs; i++)
        {
            var p = new Puff();
            p.quad = MakeQuad("CloudPuff " + i, out p.renderer);
            puffs[i] = p;
        }
        sheet = MakeQuad("CloudSheet", out sheetRenderer);
        float sheetHalf = fieldRadius + cloudHeight + 30f;
        sheet.position = new Vector3(0f, cloudHeight + 0.5f, 0f);
        sheet.localScale = new Vector3(sheetHalf, 1f, sheetHalf);
        block.Clear();
        block.SetFloat(IsSheetId, 1f);
        block.SetFloat(AlphaId, sheetAlpha);
        sheetRenderer.SetPropertyBlock(block);

        cookie = new RenderTexture(cookieResolution, cookieResolution, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear)
        {
            name = "CloudCookie",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            useMipMap = false,
        };
        cookie.Create();

        var data = sun.GetUniversalAdditionalLightData();
        data.lightCookieSize = new Vector2(cookieSize, cookieSize);
        data.lightCookieOffset = Vector2.zero;

        DayNightCycle.OnDayStart += RollForToday;
        RollForToday();   // day 1 (the dawn event has already fired, or the clock started in daylight)
        // First-day clouds are already overhead rather than fading in from nothing.
        SnapToTargets();
    }

    void OnDestroy()
    {
        DayNightCycle.OnDayStart -= RollForToday;
        if (sun != null && cookieBound) sun.cookie = null;
        if (cookie != null) cookie.Release();
        SunMultiplier = AmbientMultiplier = ShadowStrengthMultiplier = 1f;
        if (Instance == this) Instance = null;
    }

    // ---- condition --------------------------------------------------------

    void RollForToday()
    {
        float total = 0f;
        for (int i = 0; i < conditionWeights.Length; i++) total += Mathf.Max(0f, conditionWeights[i]);
        float r = Random.value * Mathf.Max(total, 0.0001f);
        int pick = 0;
        for (int i = 0; i < conditionWeights.Length; i++)
        {
            r -= Mathf.Max(0f, conditionWeights[i]);
            if (r <= 0f) { pick = i; break; }
        }
        Set((Condition)Mathf.Clamp(pick, 0, 3));
    }

    /// <summary>Change the sky (the F4 menu). Blends in over the usual transition.</summary>
    public void Set(Condition c)
    {
        Current = c;
        int want = PuffCounts[(int)c];
        int active = 0;
        for (int i = 0; i < MaxPuffs; i++) if (puffs[i].targetOpacity > 0f) active++;

        for (int i = 0; i < MaxPuffs && active < want; i++)
        {
            Puff p = puffs[i];
            if (p.targetOpacity > 0f) continue;
            Spawn(p, false);
            active++;
        }
        for (int i = MaxPuffs - 1; i >= 0 && active > want; i--)
        {
            Puff p = puffs[i];
            if (p.targetOpacity <= 0f) continue;
            p.targetOpacity = 0f;
            active--;
        }
        DevQuests.Signal(SignalKeys[(int)c]);
    }

    void SnapToTargets()
    {
        int c = (int)Current;
        overcast = OvercastTargets[c];
        SunMultiplier = SunTargets[c];
        AmbientMultiplier = AmbientTargets[c];
        ShadowStrengthMultiplier = ShadowTargets[c];
        for (int i = 0; i < MaxPuffs; i++) puffs[i].opacity = puffs[i].targetOpacity;
    }

    void Spawn(Puff p, bool upwind)
    {
        p.radius = Random.Range(puffRadiusMin, puffRadiusMax);
        p.seed = Random.Range(0f, 1000f);
        p.windScale = Random.Range(0.75f, 1.25f);
        p.targetOpacity = 1f;
        p.opacity = 0f;
        // A re-entry off the upwind edge fades in over 8 s; a condition change
        // fades at the transition rate with everything else.
        p.fadeSeconds = upwind ? 8f : Mathf.Max(transitionSeconds, 0.1f);
        Vector2 dir = wind.sqrMagnitude > 0.0001f ? wind.normalized : Vector2.right;
        Vector2 perp = new Vector2(-dir.y, dir.x);
        if (upwind)
        {
            p.pos = -dir * (fieldRadius + p.radius) + perp * Random.Range(-fieldRadius, fieldRadius);
        }
        else
        {
            p.pos = Random.insideUnitCircle * fieldRadius;
        }
    }

    // ---- per frame --------------------------------------------------------

    void Update()
    {
        GameSettings.CloudMode mode = GameSettings.Clouds;
        float dt = Time.deltaTime;
        clock += dt;

        if (mode == GameSettings.CloudMode.Off)
        {
            if (!wasOff) DevQuests.Signal("sky:off");
            wasOff = true;
            SunMultiplier = AmbientMultiplier = ShadowStrengthMultiplier = 1f;
            HideAll();
            BindCookie(false);
            return;
        }
        wasOff = false;

        int c = (int)Current;
        float rate = dt / Mathf.Max(transitionSeconds, 0.1f);
        overcast = Mathf.MoveTowards(overcast, OvercastTargets[c], rate);
        SunMultiplier = Mathf.MoveTowards(SunMultiplier, SunTargets[c], rate);
        AmbientMultiplier = Mathf.MoveTowards(AmbientMultiplier, AmbientTargets[c], rate);
        ShadowStrengthMultiplier = Mathf.MoveTowards(ShadowStrengthMultiplier, ShadowTargets[c], rate);

        drift += wind * dt;
        Vector2 dir = wind.sqrMagnitude > 0.0001f ? wind.normalized : Vector2.right;
        bool showPuffs = mode == GameSettings.CloudMode.Full;
        bool anything = overcast > 0.002f;
        int count = 0;

        for (int i = 0; i < MaxPuffs; i++)
        {
            Puff p = puffs[i];
            if (!p.Active)
            {
                if (p.renderer.enabled) p.renderer.enabled = false;
                continue;
            }

            p.pos += wind * (p.windScale * dt);
            // Gone past the downwind edge: come back in from the upwind side as a new cloud.
            if (p.targetOpacity > 0f && Vector2.Dot(p.pos, dir) > fieldRadius + p.radius) Spawn(p, true);

            float fadeRate = p.targetOpacity > 0f ? dt / p.fadeSeconds : rate;
            p.opacity = Mathf.MoveTowards(p.opacity, p.targetOpacity, fadeRate);

            cloudArray[count] = new Vector4(p.pos.x, p.pos.y, p.radius, p.opacity);
            seedArray[count] = new Vector4(p.seed, 0f, 0f, 0f);
            count++;
            anything = true;

            bool visible = showPuffs && p.opacity > 0.005f;
            if (p.renderer.enabled != visible) p.renderer.enabled = visible;
            if (visible)
            {
                p.quad.position = new Vector3(p.pos.x, cloudHeight, p.pos.y);
                p.quad.localScale = new Vector3(p.radius, 1f, p.radius);
                block.Clear();
                block.SetVector(CloudId, cloudArray[count - 1]);
                block.SetFloat(SeedId, p.seed);
                block.SetFloat(AlphaId, puffAlpha);
                p.renderer.SetPropertyBlock(block);
            }
        }

        bool sheetVisible = showPuffs && overcast > 0.005f;
        if (sheetRenderer.enabled != sheetVisible) sheetRenderer.enabled = sheetVisible;

        Shader.SetGlobalFloat(CloudTimeId, clock);
        Shader.SetGlobalVector(CloudDriftId, drift);
        Shader.SetGlobalFloat(CloudOvercastId, overcast);
        Shader.SetGlobalColor(SunTintId, sun != null ? sun.color * Mathf.Clamp01(sun.intensity / 1.5f + 0.35f) : Color.white);

        if (!anything || sun == null)
        {
            BindCookie(false);
            return;
        }

        cookieMaterial.SetMatrix(LightToWorldId, sun.transform.localToWorldMatrix);
        cookieMaterial.SetFloat(CookieSizeId, cookieSize);
        cookieMaterial.SetFloat(CloudHeightId, cloudHeight);
        cookieMaterial.SetFloat(ShadowDensityId, shadowDensity);
        cookieMaterial.SetInteger(CloudCountId, count);   // the shader's _CloudCount is a real int
        cookieMaterial.SetVectorArray(CloudsId, cloudArray);
        cookieMaterial.SetVectorArray(CloudSeedsId, seedArray);
        Graphics.Blit(null, cookie, cookieMaterial);
        BindCookie(true);
    }

    void BindCookie(bool on)
    {
        if (on == cookieBound || sun == null) return;
        cookieBound = on;
        sun.cookie = on ? cookie : null;
    }

    void HideAll()
    {
        for (int i = 0; i < MaxPuffs; i++)
            if (puffs[i].renderer.enabled) puffs[i].renderer.enabled = false;
        if (sheetRenderer.enabled) sheetRenderer.enabled = false;
    }

    // ---- geometry ---------------------------------------------------------

    Transform MakeQuad(string name, out MeshRenderer renderer)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = quadMesh;
        renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = puffMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.enabled = false;
        return go.transform;
    }

    /// <summary>A unit quad in XZ (±1), facing up; scaled per cloud by the transform.</summary>
    static Mesh BuildQuad()
    {
        var m = new Mesh { name = "CloudQuad" };
        m.vertices = new[]
        {
            new Vector3(-1f, 0f, -1f), new Vector3(1f, 0f, -1f),
            new Vector3(1f, 0f, 1f), new Vector3(-1f, 0f, 1f),
        };
        m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        m.RecalculateBounds();
        return m;
    }
}
