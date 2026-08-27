/// <summary>
/// Zero-cost consideration that always returns a raw 1.0, leaving the ResponseCurve
/// to decide the value. Use with ResponseCurve.Constant(x) for an action that should
/// sit at a fixed baseline utility (e.g. Idle as a floor beneath everything else).
///
/// Exists because ResponseCurve.Constant ignores its input entirely, so pairing it
/// with a scanning consideration burns the whole scan and throws the result away.
/// Worker Idle used ResourceAvailability(Constant(0.1f)) that way — a full
/// ResourceNode.ActiveList walk (~440 nodes) whose output was discarded, doubling
/// the per-worker scan cost. Its only side effect, caching bb.bestResource, is
/// already done by Gather's ResourceAvailability, which is that action's first
/// consideration and therefore always runs.
/// </summary>
public class ConstantScore : Consideration
{
    public ConstantScore(ResponseCurve curve) : base(curve) { }

    public override float ScoreRaw(AIBlackboard bb) => 1f;
}
