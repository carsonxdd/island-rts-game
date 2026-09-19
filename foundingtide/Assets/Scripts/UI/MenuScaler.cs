using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Applies the UI Scale setting to every canvas the menus build.
///
/// A CanvasScaler in ScaleWithScreenSize mode sizes UI as a fraction of the
/// reference resolution, so <em>dividing</em> the reference by the scale factor
/// is what makes the UI larger — 1920/1.25 means the same panel now covers a
/// bigger share of the screen.
///
/// Canvases register themselves on creation instead of being found by a scan,
/// because a scan would also catch the gameplay HUD (built in the scene, tuned
/// against its own reference resolution) and resize things the setting does not
/// claim to touch. Registrations are pruned lazily — a scene load destroys the
/// canvases without telling anyone.
/// </summary>
public static class MenuScaler
{
    private static readonly List<CanvasScaler> scalers = new List<CanvasScaler>();

    /// <summary>The reference resolution every menu canvas is authored against.</summary>
    public static readonly Vector2 BaseReference = new Vector2(1920f, 1080f);

    public static void Register(CanvasScaler scaler)
    {
        if (scaler == null) return;
        if (!scalers.Contains(scaler)) scalers.Add(scaler);
        ApplyTo(scaler, GameSettings.UIScale);
    }

    public static void Apply(float scale)
    {
        for (int i = scalers.Count - 1; i >= 0; i--)
        {
            // Unity's destroyed-object null, not a real null — the == operator
            // is what detects it, so don't switch this to `is null`.
            if (scalers[i] == null) { scalers.RemoveAt(i); continue; }
            ApplyTo(scalers[i], scale);
        }
    }

    private static void ApplyTo(CanvasScaler scaler, float scale)
    {
        float s = Mathf.Clamp(scale, 0.7f, 1.6f);
        scaler.referenceResolution = BaseReference / s;
    }
}
