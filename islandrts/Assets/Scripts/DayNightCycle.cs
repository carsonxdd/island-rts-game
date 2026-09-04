using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The game clock and the sun. Advances time of day, announces the day/night transitions
/// that drive enemy raids, and lerps the scene lighting between the day and night presets.
/// </summary>
/// <remarks>
/// Day and night each cover half of the 0..1 time parameter but run at their own rate, so
/// the configured lengths are honoured even though they differ.
///
/// Lighting is driven entirely from the two LightingPreset assets: the values set in the
/// Lighting window are scene-view fallback only and are overwritten on the first frame.
/// The directional light sweeps across the sky by day and is parked at a fixed moon pose
/// all night, blended through the dawn and dusk windows so neither switch pops.
/// </remarks>
public class DayNightCycle : MonoBehaviour
{
    [Header("Time Settings")]
    public float dayLengthInSeconds = 100f;    // scene: 100. A 30-day run is 75 real minutes (2026-09-02; was 120/60)
    public float nightLengthInSeconds = 50f;   // scene: 50
    [Range(0f, 1f)]
    public float currentTimeOfDay = 0.25f;     // 0 = midnight, 0.5 = noon, 1 = midnight

    [Header("Lighting")]
    public Light sunLight;                     // Main directional light (sun)

    [Header("Lighting Presets")]
    public LightingPreset dayPreset;
    public LightingPreset nightPreset;

    [Header("Sun Rotation")]
    [Tooltip("Sun elevation at sunrise/sunset (degrees above horizon). The sun sweeps from this angle at dawn, over the top at noon, back down at dusk — never at grazing elevation, so shadows stay readable and don't race.")]
    public float minSunElevation = 25f;

    [Header("Moon (Night Light)")]
    [Tooltip("Elevation of the moon light above the horizon during night (degrees). The directional light is held at this pose all night.")]
    public float moonElevation = 45f;
    [Tooltip("Yaw of the moon light so night shadows fall a different direction than day shadows.")]
    public float moonYaw = 210f;

    [Header("Dawn/Dusk")]
    [Range(0.02f, 0.15f)]
    [Tooltip("Width of the dawn/dusk lighting blend as a fraction of the full cycle. 0.05 = the old fast transition; 0.10 = gentle ~24s sunrise on a 120s day.")]
    public float transitionWidth = 0.10f;

    [Header("Day/Night Phases")]
    public bool isNight = false;
    public int currentDay = 1;

    [Header("Clock Control")]
    [Tooltip("While true, time of day is frozen (lighting still updates every frame). The opening sequence holds this until the campfire is placed.")]
    public bool clockPaused = false;

    private bool wasNight = false;  // Edge detection for the night/day start events

    // Cached OnGUI state to avoid per-frame allocations
    private GUIStyle cachedGuiStyle;
    private string cachedDebugInfo = "";
    private int lastGuiDay = -1;
    private bool lastGuiNight = false;
    private float lastGuiTime = -1f;

    // Events for other systems to subscribe to
    public delegate void DayNightEvent();
    public static event DayNightEvent OnNightStart;
    public static event DayNightEvent OnDayStart;

    void Start()
    {
        // Find the sun light if not assigned
        if (sunLight == null)
        {
            sunLight = FindAnyObjectByType<Light>();
            if (sunLight == null || sunLight.type != LightType.Directional)
            {
                Debug.LogWarning("DayNightCycle: No directional light found! Assign sun light manually.");
            }
        }

        // Force ambient to Gradient (Trilight) mode so the preset's sky/equator/ground
        // colors are what RenderSettings actually consumes. Otherwise Unity may be
        // sampling the skybox or a flat ambient color and our writes are ignored.
        RenderSettings.ambientMode = AmbientMode.Trilight;

        if (dayPreset == null || nightPreset == null)
        {
            Debug.LogError("DayNightCycle: Day/Night LightingPreset references are missing. Assign them in the Inspector.");
        }
    }

    void Update()
    {
        // Update time of day (frozen while the opening sequence holds the clock).
        // Day (t 0.25-0.75) and night (t 0.75-0.25) each cover HALF the 0..1 parameter
        // but have independent real-time lengths, so the advance rate depends on which
        // phase we're in. (The old code used one constant rate — both phases actually
        // lasted (day+night)/2 seconds and the configured lengths were ignored.)
        if (!clockPaused)
        {
            bool nightNow = currentTimeOfDay < 0.25f || currentTimeOfDay >= 0.75f;
            // A harder difficulty stretches the night — more time under attack
            // per wave — without touching the day, so the economy phase a player
            // gets to plan in stays the same length at every difficulty.
            float phaseLength = nightNow
                ? nightLengthInSeconds * Difficulty.NightLengthMultiplier
                : dayLengthInSeconds;
            currentTimeOfDay += (0.5f / Mathf.Max(phaseLength, 1f)) * Time.deltaTime;

            // Wrap around at end of day
            if (currentTimeOfDay >= 1f)
            {
                currentTimeOfDay = 0f;
                currentDay++;
            }
        }

        // Update lighting based on time of day
        UpdateSunLighting();

        // Check for day/night transitions
        CheckDayNightTransition();
    }

    void UpdateSunLighting()
    {
        if (sunLight == null) return;

        // Day is 0.25 to 0.75, Night is 0.75 to 0.25 (wrapping).
        // dayProgress ramps 0->1 over the dawn window and 1->0 over the dusk window.
        // NOTE: AIWorldState keeps its own dayProgress ramp (fixed 0.05 windows) for AI
        // behavior — this one is visual-only; widening it doesn't retune the AI.
        float blend = Mathf.Max(transitionWidth, 0.001f);
        float dawnEnd = 0.25f + blend;
        float duskStart = 0.75f - blend;
        float dayProgress;

        if (currentTimeOfDay < 0.25f)
        {
            dayProgress = 0f;                                              // night (midnight to dawn)
        }
        else if (currentTimeOfDay < dawnEnd)
        {
            dayProgress = (currentTimeOfDay - 0.25f) / blend;              // dawn transition
        }
        else if (currentTimeOfDay < duskStart)
        {
            dayProgress = 1f;                                              // full day
        }
        else if (currentTimeOfDay < 0.75f)
        {
            dayProgress = 1f - ((currentTimeOfDay - duskStart) / blend);   // dusk transition
        }
        else
        {
            dayProgress = 0f;                                              // night (dusk to midnight)
        }

        // Sun sweep: rises at minSunElevation, passes overhead at noon, sets at
        // 180 - minSunElevation. Clamping the ends keeps the sun off grazing angles
        // where shadows are extremely long and visibly race across the ground.
        // At night the light is held at a fixed moon pose instead (a below-horizon
        // directional light contributes nothing), blending through dawn/dusk so
        // there's no pop.
        float u = Mathf.InverseLerp(0.25f, 0.75f, currentTimeOfDay);
        float sunAngle = Mathf.Lerp(minSunElevation, 180f - minSunElevation, u);
        Quaternion sunRotation = Quaternion.Euler(sunAngle, 0f, 0f);
        Quaternion moonRotation = Quaternion.Euler(moonElevation, moonYaw, 0f);
        sunLight.transform.rotation = Quaternion.Slerp(moonRotation, sunRotation, dayProgress);

        // Lerp all preset values from night -> day based on dayProgress.
        if (dayPreset != null && nightPreset != null)
        {
            sunLight.color = Color.Lerp(nightPreset.sunColor, dayPreset.sunColor, dayProgress);
            sunLight.intensity = Mathf.Lerp(nightPreset.sunIntensity, dayPreset.sunIntensity, dayProgress);
            sunLight.shadowStrength = Mathf.Lerp(nightPreset.shadowStrength, dayPreset.shadowStrength, dayProgress);

            RenderSettings.ambientSkyColor = Color.Lerp(nightPreset.ambientSky, dayPreset.ambientSky, dayProgress);
            RenderSettings.ambientEquatorColor = Color.Lerp(nightPreset.ambientEquator, dayPreset.ambientEquator, dayProgress);
            RenderSettings.ambientGroundColor = Color.Lerp(nightPreset.ambientGround, dayPreset.ambientGround, dayProgress);
            RenderSettings.ambientIntensity = Mathf.Lerp(nightPreset.ambientIntensity, dayPreset.ambientIntensity, dayProgress);
        }

        // Update isNight flag
        isNight = currentTimeOfDay < 0.25f || currentTimeOfDay > 0.75f;
    }

    void CheckDayNightTransition()
    {
        // Check if we just transitioned to night
        if (isNight && !wasNight)
        {
            Debug.Log($"DayNightCycle: Night {currentDay} begins.");

            // Play night ambience only (no music until combat starts)
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayNightAmbience();
            }

            OnNightStart?.Invoke();  // Notify listeners (enemy spawner, etc.)
        }
        // Check if we just transitioned to day
        else if (!isNight && wasNight)
        {
            Debug.Log($"DayNightCycle: Day {currentDay} begins.");

            // Play day music
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayDayMusic();
            }

            OnDayStart?.Invoke();  // Notify listeners
        }

        wasNight = isNight;
    }

    // Public methods for other systems
    public bool IsNightTime()
    {
        return isNight;
    }

    public float GetTimeOfDay()
    {
        return currentTimeOfDay;
    }

    public int GetCurrentDay()
    {
        return currentDay;
    }

#if UNITY_EDITOR
    // Debug helper — cached style and string to avoid GC allocations.
    // Editor-only: builds should get a real HUD element instead (Phase 11).
    void OnGUI()
    {
        if (!Application.isPlaying) return;

        // Create style once
        if (cachedGuiStyle == null)
        {
            cachedGuiStyle = new GUIStyle();
            cachedGuiStyle.fontSize = 18;
            cachedGuiStyle.fontStyle = FontStyle.Bold;
            cachedGuiStyle.alignment = TextAnchor.MiddleCenter;
        }

        // Only rebuild string when displayed values change
        // Round time to 2 decimal places to reduce rebuilds
        float roundedTime = Mathf.Round(currentTimeOfDay * 100f) / 100f;
        if (currentDay != lastGuiDay || isNight != lastGuiNight || roundedTime != lastGuiTime)
        {
            lastGuiDay = currentDay;
            lastGuiNight = isNight;
            lastGuiTime = roundedTime;
            cachedGuiStyle.normal.textColor = isNight ? Color.cyan : Color.yellow;
            string timeString = isNight ? "NIGHT" : "DAY";
            cachedDebugInfo = $"Day {currentDay} - {timeString} (Time: {roundedTime:F2})";
        }

        // Center at top of screen
        float width = 400;
        float height = 40;
        float x = (Screen.width - width) / 2;
        float y = -3;

        GUI.Label(new Rect(x, y, width, height), cachedDebugInfo, cachedGuiStyle);
    }
#endif
}
