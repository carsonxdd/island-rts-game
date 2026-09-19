/// <summary>
/// 1 for a mustered levy once the colony's alarm has been quiet for
/// <see cref="Militia.StandDownSeconds"/> (2026-09-16), else 0. Zero-cost gate for
/// the warrior's StandDown action; a full-time warrior scores 0 here forever, and
/// a levy away with an expedition waits until they are home.
/// </summary>
public class StandDownDue : Consideration
{
    public StandDownDue(ResponseCurve curve) : base(curve) { }

    public override float ScoreRaw(AIBlackboard bb)
    {
        if (!bb.levied || bb.faction == null) return 0f;
        if (bb.warrior != null && bb.warrior.OnExpedition) return 0f;
        return bb.faction.Militia.StandDownDue ? 1f : 0f;
    }
}
