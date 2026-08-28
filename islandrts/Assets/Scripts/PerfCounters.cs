using System.Diagnostics;

/// <summary>
/// Per-frame instrumentation counters, written by the systems that can spike a
/// frame (AI evaluation, NavMesh commands, combat VFX, NavMesh rebuilds) and
/// drained once per frame by <see cref="PerfLogger"/>.
///
/// Deliberately NOT #if-wrapped: the hooks are a single array increment, so the
/// call sites stay readable and cost nothing measurable in a release build.
/// PerfLogger itself (the file writing, the agent scan) IS editor/dev only.
/// </summary>
public static class PerfCounters
{
    public enum K
    {
        SetDestTry,        // AINavHelper.TrySetDestination called
        SetDestOk,         // ...and Unity accepted the destination
        SetDestThrottled,  // ...refused by the 20/frame cap
        SetDestRejected,   // ...Unity returned false (unmappable / mid-recalc)
        CalcPathTry,
        CalcPathOk,
        CalcPathThrottled, // refused by the 2/frame cap
        Eval,              // AIBrain evaluations that actually ran
        EvalForced,        // ...of which were ForceReeval
        EvalDeferred,      // brains that wanted to think but were over budget
        NavMeshUpdate,     // TerrainGrid.UpdateNavMeshAsync (full surface refresh)
        TerrainRebuild,    // chunk mesh rebuilds (FlattenArea)
        CarveChange,       // a carving NavMeshObstacle appeared/disappeared
        VfxSpawn,          // CombatEffects created a ParticleSystem/TMP GameObject
        UnitSpawn,
        UnitDeath,
        Count
    }

    public static readonly int[] Frame = new int[(int)K.Count];

    /// <summary>Nanoseconds-ish accumulators (Stopwatch ticks) for this frame.</summary>
    public static long EvalTicks;
    public static long ExecTicks;
    public static long VfxTicks;

    public static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

    public static void Hit(K k) { Frame[(int)k]++; }

    public static void ResetFrame()
    {
        for (int i = 0; i < Frame.Length; i++) Frame[i] = 0;
        EvalTicks = 0;
        ExecTicks = 0;
        VfxTicks = 0;
    }

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { ResetFrame(); }
}
