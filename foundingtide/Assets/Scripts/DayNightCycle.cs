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
    public float dayLengthInSeconds = 150f;    // scene: 150. A 30-day run is ~112 real minutes (2026-09-16; was 100/50, before that 120/60)
    public float nightLengthInSeconds = 75f;   // scene: 75. Colonists sleep from midnight (the night's midpoint) to dawn
    [Range(0f, 1f)]
    // 0 = midnight, 0.25 = dawn (6 am), 0.5 = noon, 0.75 = dusk, 1 = midnight. A run
    // starts at 8 am (scene: 0.33333): 0.25 sat in the middle of the dawn blend, so
    // the landing looked like the small hours (2026-09-13).
    public float currentTimeOfDay = 1f / 3f;

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

    [Tooltip("A raid night lasts until the last raider is dead (2026-09-07): the clock holds just short of dawn while any is alive. This caps the hold so a raider stuck on a NavMesh seam cannot freeze the run; <= 0 means the 180 s default (the scene predates the field).")]
    public float maxDawnHoldSeconds = 180f;
    const float DefaultMaxDawnHold = 180f;
    const float DawnT = 0.25f;

    /// <summary>True while the clock is being held short of dawn for living raiders (HUD reads it).</summary>
    public bool DawnHeld { get; private set; }
    private float dawnHoldTimer;
    private bool dawnHoldCapLogged;
    private bool heldThisNight;   // playtest only: a night that never held ends "on time"

    private bool wasNight = false;  // Edge detection for the night/day start events

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

        // The sun's shadow mode is a graphics setting (2026-09-08); the cloud
        // layer shades through its light cookie. Both are runtime-added here so
        // the scene stays the single source of the light itself.
        GraphicsQuality.Sun = sunLight;
        GraphicsQuality.Apply();
        CloudSystem.EnsureExists(sunLight);
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
            float next = currentTimeOfDay + (0.5f / Mathf.Max(phaseLength, 1f)) * Time.deltaTime;

            // The night ends when the raid does (2026-09-07): with raiders still alive
            // the clock holds a hair short of dawn — the sky stays dark, the spawner's
            // dawn despawn does not fire, and the day only breaks over their bodies.
            // Capped so a raider that can never be reached cannot hold the run hostage.
            bool crossingDawn = nightNow && currentTimeOfDay < DawnT && next >= DawnT;
            if (crossingDawn && RaidersAlive())
            {
                float cap = maxDawnHoldSeconds > 0f ? maxDawnHoldSeconds : DefaultMaxDawnHold;
                dawnHoldTimer += Time.deltaTime;
                if (dawnHoldTimer < cap)
                {
                    if (!DawnHeld) DevQuests.Signal("dawn:held");
                    DawnHeld = true;
                    heldThisNight = true;
                    next = DawnT - 0.0005f;
                }
                else if (!dawnHoldCapLogged)
                {
                    dawnHoldCapLogged = true;
                    Debug.LogWarning("DayNightCycle: dawn held " + Mathf.RoundToInt(cap) + "s with raiders still alive — releasing the day.\n"
                        + Enemy.DescribeAlive());
                }
            }
            if (next >= DawnT && DawnHeld)
            {
                if (!dawnHoldCapLogged) DevQuests.Signal("dawn:released");   // the last raider fell, not the cap
                DawnHeld = false;   // the last raider fell (or the cap released the day)
                dawnHoldTimer = 0f;
                dawnHoldCapLogged = false;
            }
            currentTimeOfDay = next;

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

    /// <summary>Any living raider on the field. Registry walk, no allocation.</summary>
    static bool RaidersAlive()
    {
        var list = Enemy.ActiveList;
        for (int i = 0; i < list.Count; i++)
        {
            Enemy e = list[i];
            if (e == null) continue;
            Health h = e.CachedHealth;
            if (h != null && h.IsAlive) return true;
        }
        return false;
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
            // Cloud cover dims the sun, flattens the ambient a little and softens
            // the shadows (a cloudy sky is a big diffuse light). The multipliers
            // are 1 with no CloudSystem, so the menu and the sim see the presets.
            sunLight.color = Color.Lerp(nightPreset.sunColor, dayPreset.sunColor, dayProgress);
            sunLight.intensity = Mathf.Lerp(nightPreset.sunIntensity, dayPreset.sunIntensity, dayProgress) * CloudSystem.SunMultiplier;
            sunLight.shadowStrength = Mathf.Lerp(nightPreset.shadowStrength, dayPreset.shadowStrength, dayProgress) * CloudSystem.ShadowStrengthMultiplier;

            RenderSettings.ambientSkyColor = Color.Lerp(nightPreset.ambientSky, dayPreset.ambientSky, dayProgress);
            RenderSettings.ambientEquatorColor = Color.Lerp(nightPreset.ambientEquator, dayPreset.ambientEquator, dayProgress);
            RenderSettings.ambientGroundColor = Color.Lerp(nightPreset.ambientGround, dayPreset.ambientGround, dayProgress);
            RenderSettings.ambientIntensity = Mathf.Lerp(nightPreset.ambientIntensity, dayPreset.ambientIntensity, dayProgress) * CloudSystem.AmbientMultiplier;
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
            if (!heldThisNight) DevQuests.Signal("dawn:quiet");   // no raider held the dawn: the night ended on time
            heldThisNight = false;

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

    /// <summary>
    /// Seconds in one full calendar day at the active difficulty (day + the
    /// stretched night). The unit everything "per day" is measured in —
    /// food consumption (2026-09-04) reads it every frame.
    /// </summary>
    public float CycleSeconds => dayLengthInSeconds + nightLengthInSeconds * Difficulty.NightLengthMultiplier;

    public float GetTimeOfDay()
    {
        return currentTimeOfDay;
    }

    public int GetCurrentDay()
    {
        return currentDay;
    }

}
