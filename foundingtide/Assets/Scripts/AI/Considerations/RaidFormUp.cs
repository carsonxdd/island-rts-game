/// <summary>
/// The dusk form-up gate (2026-09-28): 1 while a raid is coming to this warrior's
/// colony and not yet over — from dusk on a raid night until the landed raid is
/// dead (<see cref="RaidDirector.RaidPendingAt"/>) — with the landing known, under
/// the Defensive stance, and the warrior at home; else 0. Zero-cost (flags and one
/// static clock read), no yShift, so the FormUp action dies the moment the raid
/// is repelled, the stance changes, or dawn re-rolls the night.
/// </summary>
public class RaidFormUp : Consideration
{
    public RaidFormUp(ResponseCurve curve) : base(curve) { }

    public override float ScoreRaw(AIBlackboard bb)
    {
        if (bb.faction == null || bb.warrior == null || bb.warrior.OnExpedition) return 0f;
        if (GuardStance.Effective(bb.faction) != GuardStance.Mode.Defensive) return 0f;
        RaidDirector rd = RaidDirector.Instance;
        if (rd == null || !rd.RaidPendingAt(bb.faction)) return 0f;
        UnityEngine.Vector3 unused;
        return rd.LandingFor(bb.faction, out unused) ? 1f : 0f;
    }
}
