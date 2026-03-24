using UnityEngine;

public class DayNightCycle : MonoBehaviour
{
    [Header("Time Settings")]
    public float dayLengthInSeconds = 120f;    // 2 minutes for a full day
    public float nightLengthInSeconds = 60f;   // 1 minute for night
    [Range(0f, 1f)]
    public float currentTimeOfDay = 0.25f;     // 0 = midnight, 0.5 = noon, 1 = midnight

    [Header("Lighting")]
    public Light sunLight;                     // Main directional light (sun)
    public Color dayColor = new Color(1f, 0.95f, 0.8f);      // Warm daylight
    public Color nightColor = new Color(0.2f, 0.2f, 0.5f);   // Cool moonlight
    public float dayIntensity = 1.5f;
    public float nightIntensity = 0.3f;

    [Header("Sun Rotation")]
    public float sunriseAngle = -90f;          // Sun position at sunrise (below horizon)
    public float sunsetAngle = 270f;           // Sun position at sunset (below horizon)

    [Header("Day/Night Phases")]
    public bool isNight = false;
    public int currentDay = 1;

    // Private
    private float timeSpeed;
    private bool wasNight = false;

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
            sunLight = FindFirstObjectByType<Light>();
            if (sunLight != null && sunLight.type == LightType.Directional)
            {
                Debug.Log("DayNightCycle: Found directional light for sun");
            }
            else
            {
                Debug.LogWarning("DayNightCycle: No directional light found! Assign sun light manually.");
            }
        }

        // Calculate time speed based on day/night lengths
        UpdateTimeSpeed();

        Debug.Log($"DayNightCycle: Started! Day length: {dayLengthInSeconds}s, Night length: {nightLengthInSeconds}s");
    }

    void Update()
    {
        // Update time of day
        currentTimeOfDay += timeSpeed * Time.deltaTime;

        // Wrap around at end of day
        if (currentTimeOfDay >= 1f)
        {
            currentTimeOfDay = 0f;
            currentDay++;
            Debug.Log($"DayNightCycle: Day {currentDay} begins!");
        }

        // Update lighting based on time of day
        UpdateSunLighting();

        // Check for day/night transitions
        CheckDayNightTransition();
    }

    void UpdateTimeSpeed()
    {
        // Calculate how fast time should progress
        // We want day and night to have different lengths
        float fullCycleDuration = dayLengthInSeconds + nightLengthInSeconds;
        timeSpeed = 1f / fullCycleDuration;
    }

    void UpdateSunLighting()
    {
        if (sunLight == null) return;

        // Calculate sun rotation (0 = midnight, 0.5 = noon)
        // Rotate sun around X axis to simulate day/night
        float sunAngle = Mathf.Lerp(sunriseAngle, sunsetAngle, currentTimeOfDay);
        sunLight.transform.rotation = Quaternion.Euler(sunAngle, 0f, 0f);

        // Calculate light intensity and color based on time
        // Day is roughly 0.25 to 0.75, Night is 0.75 to 0.25 (wrapping)
        float dayProgress;

        if (currentTimeOfDay < 0.25f)
        {
            // Night (midnight to dawn)
            dayProgress = 0f;
        }
        else if (currentTimeOfDay < 0.3f)
        {
            // Dawn transition
            dayProgress = (currentTimeOfDay - 0.25f) / 0.05f;
        }
        else if (currentTimeOfDay < 0.7f)
        {
            // Full day
            dayProgress = 1f;
        }
        else if (currentTimeOfDay < 0.75f)
        {
            // Dusk transition
            dayProgress = 1f - ((currentTimeOfDay - 0.7f) / 0.05f);
        }
        else
        {
            // Night (dusk to midnight)
            dayProgress = 0f;
        }

        // Interpolate light properties
        sunLight.color = Color.Lerp(nightColor, dayColor, dayProgress);
        sunLight.intensity = Mathf.Lerp(nightIntensity, dayIntensity, dayProgress);

        // Update isNight flag
        isNight = currentTimeOfDay < 0.25f || currentTimeOfDay > 0.75f;
    }

    void CheckDayNightTransition()
    {
        // Check if we just transitioned to night
        if (isNight && !wasNight)
        {
            Debug.Log($"DayNightCycle: Night {currentDay} begins! Enemies incoming...");

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
            Debug.Log($"DayNightCycle: Day {currentDay} begins! Enemies retreat.");

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

    // Debug helper — cached style and string to avoid GC allocations
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
}
