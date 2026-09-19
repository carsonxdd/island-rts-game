/// <summary>
/// 1 for a colonist who has given up on the colony (2026-09-04, Slice 4), 0
/// for everyone else. The only consideration on the Leave action, so a leaver
/// scores a flat 2.0 that beats everything including Flee, and nobody else
/// ever evaluates past this zero-cost gate.
/// </summary>
public class IsLeaving : Consideration
{
    public IsLeaving(ResponseCurve curve) : base(curve) { }

    public override float ScoreRaw(AIBlackboard bb) => bb.leaving ? 1f : 0f;
}
