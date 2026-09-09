using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Something of the colony's that sees: a radius around this transform that
/// <see cref="FogOfWar"/> stamps into its grid a few times a second. Units and buildings
/// add one in Start with a constant radius (<see cref="Attach"/>), the way housing
/// providers register themselves, so the fog never rescans the scene.
/// </summary>
/// <remarks>
/// Registered in OnEnable / unregistered in OnDisable rather than Awake / OnDestroy on
/// purpose: a garrisoned worker is a deactivated object and must stop seeing while it is
/// inside the hut, and the hut sees for it. Plain radius, no line of sight against the
/// terrain — a hill does not block sight and the Watchtower's reach is its whole point.
/// </remarks>
public class VisionSource : MonoBehaviour
{
    public static IReadOnlyList<VisionSource> ActiveList => ActiveRegistry<VisionSource>.List;

    // Sight radii in metres (2026-09-09), the one place they are tuned. Physical
    // distances, deliberately NOT SizeScaled: a bigger island takes longer to explore.
    public const float UnitRadius = 12f;        // worker, warrior
    public const float PlayerRadius = 14f;      // the castaway
    public const float CampfireRadius = 18f;
    public const float HutRadius = 10f;         // hut, Workshop, Shipyard
    public const float WatchtowerRadius = 35f;  // the tower's second job

    [Tooltip("Sight radius in metres. Set by the owner's Start; the value on a prefab is ignored.")]
    public float radius = 12f;

    /// <summary>Add (or update) a vision source on <paramref name="go"/>. Idempotent.</summary>
    public static VisionSource Attach(GameObject go, float radius)
    {
        if (go == null) return null;
        VisionSource v = go.GetComponent<VisionSource>();
        if (v == null) v = go.AddComponent<VisionSource>();
        v.radius = radius;
        return v;
    }

    void OnEnable() { ActiveRegistry<VisionSource>.Register(this); }
    void OnDisable() { ActiveRegistry<VisionSource>.Unregister(this); }
}
