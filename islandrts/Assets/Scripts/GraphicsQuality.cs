using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The ONE place that writes the live URP asset and the sun's shadow mode from
/// the player's graphics settings (2026-09-08). <see cref="GameSettings"/> holds
/// the values; this pushes them.
/// </summary>
/// <remarks>
/// The URP asset is a ScriptableObject on disk, so every write here would
/// persist to the .asset from a Play session in the editor. The first touch
/// caches the asset's authored values and <c>Application.quitting</c> (which
/// fires on leaving Play mode too) puts them back — the same reason
/// CameraController used to cache the shadow distance.
///
/// Shadow distance is a collaboration: <see cref="CameraController"/> knows how
/// deep the view is and reports it through <see cref="SetViewShadowDistance"/>;
/// this scales it by the Shadow Distance setting and QUANTISES it. The old code
/// re-fitted the cascades on every half-metre of zoom or tilt, and each re-fit
/// moves the shadow map's texel grid — the "shadows crawl while I pan" report.
/// A 10 m step with hysteresis makes that a rare, soft pop instead.
/// </remarks>
public static class GraphicsQuality
{
    /// <summary>The scene's sun. DayNightCycle registers it in Start; null on the main menu.</summary>
    public static Light Sun;

    /// <summary>Metres per shadow-distance step. Bigger = fewer cascade re-fits, coarser coverage.</summary>
    public const float DistanceStep = 10f;

    static UniversalRenderPipelineAsset touched;
    static float originalDistance, originalCascade2Split, originalRenderScale;
    static int originalResolution, originalCascades, originalMsaa;
    static bool restoreHooked;

    static float viewShadowDistance = -1f;
    static float appliedDistance = -1f;

    /// <summary>Shadow-map resolution per <see cref="GameSettings.ShadowLevel"/>; Off keeps the last size (the light is what turns off).</summary>
    static readonly int[] Resolutions = { 1024, 1024, 2048, 4096, 4096 };
    /// <summary>
    /// Cascades per level. The RTS camera sees a shallow band of depth, so one
    /// cascade at 4096 already lands a texel every few centimetres; the second
    /// on Ultra halves that again at the price of a blended seam mid-screen.
    /// </summary>
    static readonly int[] Cascades = { 1, 1, 1, 1, 2 };

    /// <summary>Push every graphics setting into the live pipeline. Safe to call per frame — every write is change-guarded.</summary>
    public static void Apply()
    {
        if (SimHooks.Simulating) return;

        var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (asset != null)
        {
            Remember(asset);

            int level = (int)GameSettings.Shadows;
            level = Mathf.Clamp(level, 0, Resolutions.Length - 1);
            if (asset.mainLightShadowmapResolution != Resolutions[level]) asset.mainLightShadowmapResolution = Resolutions[level];
            if (asset.shadowCascadeCount != Cascades[level]) asset.shadowCascadeCount = Cascades[level];
            if (Cascades[level] == 2 && Mathf.Abs(asset.cascade2Split - 0.5f) > 0.001f) asset.cascade2Split = 0.5f;

            int msaa = Mathf.Max(1, GameSettings.AntiAliasing);   // URP reads 1 as "off"
            if (asset.msaaSampleCount != msaa) asset.msaaSampleCount = msaa;

            if (Mathf.Abs(asset.renderScale - GameSettings.RenderScale) > 0.001f) asset.renderScale = GameSettings.RenderScale;

            ApplyShadowDistance();
        }

        ApplyLight();
    }

    /// <summary>CameraController reports the depth its view spans (already margined) every frame.</summary>
    public static void SetViewShadowDistance(float metres)
    {
        viewShadowDistance = metres;
        ApplyShadowDistance();
    }

    static void ApplyShadowDistance()
    {
        if (touched == null || viewShadowDistance < 0f) return;

        float want = viewShadowDistance * GameSettings.ShadowDistanceScale;
        float stepped = Mathf.Ceil(want / DistanceStep) * DistanceStep;
        // Hysteresis: only move when the raw request has crossed most of a step
        // away from what is applied, so a zoom hovering on a boundary does not
        // flicker between two cascade fits.
        if (appliedDistance > 0f && Mathf.Abs(want - appliedDistance) < DistanceStep * 0.75f) return;
        if (Mathf.Abs(stepped - appliedDistance) < 0.5f) return;

        appliedDistance = stepped;
        touched.shadowDistance = stepped;
    }

    static void ApplyLight()
    {
        if (Sun == null) return;

        LightShadows mode = GameSettings.Shadows == GameSettings.ShadowLevel.Off ? LightShadows.None
            : GameSettings.SoftShadows ? LightShadows.Soft : LightShadows.Hard;
        if (Sun.shadows != mode) Sun.shadows = mode;

        // Soft filter width follows the level so Low is cheap and Ultra is silky.
        SoftShadowQuality q = GameSettings.Shadows >= GameSettings.ShadowLevel.Ultra ? SoftShadowQuality.High
            : GameSettings.Shadows >= GameSettings.ShadowLevel.High ? SoftShadowQuality.Medium
            : SoftShadowQuality.Low;
        var data = Sun.GetUniversalAdditionalLightData();
        if (data != null && data.softShadowQuality != q) data.softShadowQuality = q;
    }

    static void Remember(UniversalRenderPipelineAsset asset)
    {
        if (asset == touched) return;
        Restore();                       // quality level switched: hand the old asset back first
        touched = asset;
        originalDistance = asset.shadowDistance;
        originalResolution = asset.mainLightShadowmapResolution;
        originalCascades = asset.shadowCascadeCount;
        originalCascade2Split = asset.cascade2Split;
        originalMsaa = asset.msaaSampleCount;
        originalRenderScale = asset.renderScale;
        appliedDistance = -1f;

        if (!restoreHooked)
        {
            restoreHooked = true;
            Application.quitting += Restore;
        }
    }

    /// <summary>Put the asset's authored values back (editor: keeps a Play session out of the .asset on disk).</summary>
    public static void Restore()
    {
        if (touched == null) return;
        touched.shadowDistance = originalDistance;
        touched.mainLightShadowmapResolution = originalResolution;
        touched.shadowCascadeCount = originalCascades;
        touched.cascade2Split = originalCascade2Split;
        touched.msaaSampleCount = originalMsaa;
        touched.renderScale = originalRenderScale;
        touched = null;
        appliedDistance = -1f;
    }
}
