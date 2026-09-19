using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where a Defensive colony holds its line (2026-09-17): inside the gate the
/// raiders are coming at. One choice per colony, so every warrior's Intercept
/// rally is a slot in the SAME line, and the choice keeps its gate until another
/// is clearly nearer the raiders (<see cref="SwitchMargin"/>) — a centroid that
/// drifts between two gates used to swing the whole formation with it.
/// Colonies with walls but no gate, or no walls, fall back to Intercept's own
/// perimeter rally; this class only answers when there is a gate to hold.
/// </summary>
public static class HoldLine
{
    /// <summary>The front rank stands this far inside the gate, toward the fire.</summary>
    public const float GateStandoff = 4f;
    /// <summary>A different gate takes the line only when its distance to the raiders is under this fraction of the held gate's.</summary>
    public const float SwitchMargin = 0.7f;
    /// <summary>The held gate is re-examined this often per colony.</summary>
    public const float ReplanSeconds = 2f;

    sealed class Choice
    {
        public Gate gate;
        public float time = float.NegativeInfinity;
    }

    static readonly Dictionary<int, Choice> byFaction = new Dictionary<int, Choice>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { byFaction.Clear(); }

    static bool Alive(Gate g) => g != null && g.CachedHealth != null && g.CachedHealth.IsAlive;

    /// <summary>
    /// The gate the colony holds against raiders massed at <paramref name="enemyCentroid"/>,
    /// or null with no gate of its own standing. Zero-GC after the first call per colony.
    /// </summary>
    public static Gate HeldGate(Faction f, Vector3 enemyCentroid)
    {
        if (f == null) return null;
        Choice c;
        if (!byFaction.TryGetValue(f.Id, out c)) { c = new Choice(); byFaction[f.Id] = c; }
        if (Time.time - c.time < ReplanSeconds && Alive(c.gate)) return c.gate;
        c.time = Time.time;

        Gate best = null;
        float bestSqr = float.MaxValue;
        var gates = Gate.ActiveList;
        for (int i = 0; i < gates.Count; i++)
        {
            Gate g = gates[i];
            if (g == null || g.Faction != f || !Alive(g)) continue;
            float sqr = (g.transform.position - enemyCentroid).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = g; }
        }

        if (Alive(c.gate) && best != c.gate)
        {
            float heldSqr = (c.gate.transform.position - enemyCentroid).sqrMagnitude;
            if (bestSqr > heldSqr * (SwitchMargin * SwitchMargin)) best = c.gate;   // not clearly nearer: keep the line
        }
        c.gate = best;
        return best;
    }

    /// <summary>The gate a colony holds right now, without re-examining it (telemetry and the F3 overlay). Null when none is held or it fell.</summary>
    public static Gate CurrentGate(Faction f)
    {
        Choice c;
        if (f == null || !byFaction.TryGetValue(f.Id, out c)) return null;
        return Alive(c.gate) ? c.gate : null;
    }

    /// <summary>
    /// The line's centre and outward facing for a gate: <see cref="GateStandoff"/>
    /// inside it toward <paramref name="fire"/>, facing out through it. Also the
    /// gate's distance from the fire, the "inside the wall" radius the stance reads.
    /// </summary>
    public static void LineAt(Gate gate, BaseBuilding fire, out Vector3 centre, out Vector3 facing, out float gateRadius)
    {
        Vector3 g = gate.transform.position;
        Vector3 inward = fire.transform.position - g;
        inward.y = 0f;
        gateRadius = inward.magnitude;
        if (gateRadius < 0.01f) inward = Vector3.forward; else inward /= gateRadius;
        float standoff = Mathf.Min(GateStandoff, gateRadius * 0.5f);   // a gate hard by the fire: halfway
        centre = g + inward * standoff;
        facing = -inward;
    }
}
