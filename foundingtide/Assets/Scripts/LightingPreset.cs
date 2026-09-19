using UnityEngine;

/// <summary>
/// Holds the lighting "look" for one moment of the day/night cycle.
/// DayNightCycle lerps between two presets (day, night) based on currentTimeOfDay.
///
/// Phase 10 Stage 1 scope: sun + gradient ambient.
/// Future: fog (Stage 4), water shader properties (Stage 3).
/// </summary>
[CreateAssetMenu(fileName = "LightingPreset", menuName = "Lighting/Lighting Preset")]
public class LightingPreset : ScriptableObject
{
    [Header("Sun (Directional Light)")]
    public Color sunColor = Color.white;
    public float sunIntensity = 1f;

    [Header("Shadows")]
    [Tooltip("Directional light shadow strength at this moment (1 = full sun shadows, ~0.5 = soft moon shadows).")]
    [Range(0f, 1f)] public float shadowStrength = 1f;

    [Header("Ambient (Gradient)")]
    public Color ambientSky = new Color(0.5f, 0.5f, 0.5f);
    public Color ambientEquator = new Color(0.4f, 0.4f, 0.4f);
    public Color ambientGround = new Color(0.3f, 0.3f, 0.3f);
    [Range(0f, 8f)] public float ambientIntensity = 1f;
}
