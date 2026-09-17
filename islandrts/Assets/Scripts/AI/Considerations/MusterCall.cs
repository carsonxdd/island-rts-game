/// <summary>
/// 1 for a levied colonist while the colony's alarm is up (2026-09-16), else 0.
/// Zero-cost gate for the worker's Muster action: two cached bools, no scan. The
/// claim itself is handed out by <see cref="Militia"/> at 2 Hz; a leaver never gets one.
/// </summary>
public class MusterCall : Consideration
{
    public MusterCall(ResponseCurve curve) : base(curve) { }

    public override float ScoreRaw(AIBlackboard bb)
    {
        if (!bb.levied || bb.leaving || bb.faction == null) return 0f;
        return bb.faction.Militia.Alarm ? 1f : 0f;
    }
}
