using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where a Defensive colony holds its line (2026-09-17): at the gate the raiders
/// are coming at. One choice per colony, so every warrior's Intercept rally is a
/// slot in the SAME line, and the choice keeps its gate until another is clearly
/// nearer the raiders (<see cref="SwitchMargin"/>) — a centroid that drifts between
/// two gates used to swing the whole formation with it.
/// Colonies with walls but no gate, or no walls, fall back to Intercept's own
/// perimeter rally; this class only answers when there is a gate to hold.
/// </summary>
/// <remarks>
/// <para><b>Outside the gate since 2026-09-28.</b> The spearmen stand
/// <see cref="OutsideStandoff"/> OUT of the gate, between it and the threat, facing
/// it; the archers form <see cref="GateStandoff"/> INSIDE the gate, spread along the
/// wall, and shoot over it (arrows have no line-of-sight check). The line inside
/// the gate let every raider chew the gate unopposed and then pour through one at a
/// time into a crowd that could not reach it.</para>
/// <para>Before any raider is ashore the threat is the published landing point
/// (<see cref="RaidDirector.LandingFor"/>), so the line forms at dusk on the side
/// the raid will come from; afterwards it is the raiders' centroid, with the same
/// hysteresis. The line's geometry (centres, facing) is computed once per gate
/// choice, so a centroid wandering in front of a held gate never swings it.</para>
/// </remarks>
public static class HoldLine
{
    /// <summary>The archer rank stands this far INSIDE the gate, toward the fire (was the whole line until 2026-09-28; the sim's kill telemetry still reads it).</summary>
    public const float GateStandoff = 4f;
    /// <summary>The spearmen's front rank stands this far OUTSIDE the gate, toward the threat (2026-09-28).</summary>
    public const float OutsideStandoff = 4.5f;
    /// <summary>Half the width of a gate opening (a gate spans three 1 m cells, its transform on the middle one).</summary>
    public const float GateHalfWidth = 1.5f;
    /// <summary>How far the facing bends from straight out of the gate toward the threat (0 = straight out, 1 = half way).</summary>
    public const float FacingBend = 0.6f;
    /// <summary>A different gate takes the line only when its distance to the raiders is under this fraction of the held gate's.</summary>
    public const float SwitchMargin = 0.7f;
    /// <summary>The held gate is re-examined this often per colony.</summary>
    public const float ReplanSeconds = 2f;

    sealed class Choice
    {
        public Gate gate;
        public float time = float.NegativeInfinity;
        // The line for `lineFor`, computed when the gate was chosen
        public Gate lineFor;
        public Vector3 spear, archer, facing;
        public float gateRadius;
    }

    static readonly Dictionary<int, Choice> byFaction = new Dictionary<int, Choice>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { byFaction.Clear(); }

    /// <summary>Forget every colony's held gate (dusk: tonight's line is chosen against tonight's landing).</summary>
    public static void ResetAll()
    {
        foreach (var kv in byFaction)
        {
            kv.Value.gate = null;
            kv.Value.lineFor = null;
            kv.Value.time = float.NegativeInfinity;
        }
    }

    static bool Alive(Gate g) => g != null && g.CachedHealth != null && g.CachedHealth.IsAlive;

    static Choice For(Faction f)
    {
        Choice c;
        if (!byFaction.TryGetValue(f.Id, out c)) { c = new Choice(); byFaction[f.Id] = c; }
        return c;
    }

    /// <summary>
    /// The gate the colony holds against a threat at <paramref name="threat"/> (the
    /// raiders' centroid, or the landing before they are ashore), or null with no gate
    /// of its own standing. Zero-GC after the first call per colony.
    /// </summary>
    public static Gate HeldGate(Faction f, Vector3 threat)
    {
        if (f == null) return null;
        Choice c = For(f);
        if (Time.time - c.time < ReplanSeconds && Alive(c.gate)) return c.gate;
        c.time = Time.time;

        Gate best = null;
        float bestSqr = float.MaxValue;
        var gates = Gate.ActiveList;
        for (int i = 0; i < gates.Count; i++)
        {
            Gate g = gates[i];
            if (g == null || g.Faction != f || !Alive(g)) continue;
            float sqr = (g.transform.position - threat).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = g; }
        }

        if (Alive(c.gate) && best != c.gate)
        {
            float heldSqr = (c.gate.transform.position - threat).sqrMagnitude;
            if (bestSqr > heldSqr * (SwitchMargin * SwitchMargin)) best = c.gate;   // not clearly nearer: keep the line
        }
        c.gate = best;
        return best;
    }

    /// <summary>
    /// <see cref="HeldGate"/>, plus the line at it: the spearmen's centre outside the
    /// gate, the archers' centre inside it, the facing (out of the gate, bent toward
    /// <paramref name="threat"/>), and the gate's distance from <paramref name="fire"/>
    /// (the wall's radius the stance reads). The geometry is computed when the gate
    /// is chosen and kept while it is held.
    /// </summary>
    public static Gate Plan(Faction f, Vector3 threat, BaseBuilding fire,
                            out Vector3 spearCentre, out Vector3 archerCentre, out Vector3 facing, out float gateRadius)
    {
        spearCentre = archerCentre = facing = Vector3.zero;
        gateRadius = float.MaxValue;
        Gate g = HeldGate(f, threat);
        if (g == null || fire == null) return null;
        Choice c = For(f);
        if (c.lineFor != g)
        {
            Compute(g, fire, threat, c);
            c.lineFor = g;
        }
        spearCentre = c.spear;
        archerCentre = c.archer;
        facing = c.facing;
        gateRadius = c.gateRadius;
        return g;
    }

    /// <summary>
    /// The line a colony holds right now, without re-planning: the post a warrior of
    /// this kind would take when it has none of its own yet (a levy body on its first
    /// tick). False with no gate held.
    /// </summary>
    public static bool CurrentLine(Faction f, bool ranged, out Vector3 centre, out float gateRadius)
    {
        centre = Vector3.zero;
        gateRadius = float.MaxValue;
        Choice c;
        if (f == null || !byFaction.TryGetValue(f.Id, out c)) return false;
        if (!Alive(c.gate) || c.lineFor != c.gate) return false;
        centre = ranged ? c.archer : c.spear;
        gateRadius = c.gateRadius;
        return true;
    }

    /// <summary>The gate a colony holds right now, without re-examining it (telemetry and the F3 overlay). Null when none is held or it fell.</summary>
    public static Gate CurrentGate(Faction f)
    {
        Choice c;
        if (f == null || !byFaction.TryGetValue(f.Id, out c)) return null;
        return Alive(c.gate) ? c.gate : null;
    }

    static void Compute(Gate gate, BaseBuilding fire, Vector3 threat, Choice c)
    {
        Vector3 g = gate.transform.position;
        Vector3 outward = g - fire.transform.position;
        outward.y = 0f;
        float radius = outward.magnitude;
        if (radius < 0.01f) outward = Vector3.forward; else outward /= radius;

        Vector3 toThreat = threat - g;
        toThreat.y = 0f;
        Vector3 facing = outward;
        if (toThreat.sqrMagnitude > 0.01f)
        {
            toThreat.Normalize();
            // Bend toward the threat only when it is on the outside: a line facing
            // along the wall would stand with its flank to the gate
            if (Vector3.Dot(toThreat, outward) > 0f) facing = (outward + toThreat * FacingBend).normalized;
        }

        float inside = Mathf.Min(GateStandoff, radius * 0.5f);   // a gate hard by the fire: halfway
        c.spear = g + facing * OutsideStandoff;
        c.archer = g - outward * inside;
        c.facing = facing;
        c.gateRadius = radius;
    }
}
