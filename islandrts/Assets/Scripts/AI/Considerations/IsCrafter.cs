/// <summary>
/// 1 for a colonist on the Crafter job, 0 for anyone else (2026-09-04, Slice 3).
/// Gates the Craft action the way <see cref="IsJobless"/> gates Build and Repair:
/// zero-cost, first in the list, so every other worker early-outs the action
/// before the station scan runs.
/// </summary>
public class IsCrafter : Consideration
{
    public IsCrafter(ResponseCurve curve) : base(curve) { }

    public override float ScoreRaw(AIBlackboard bb) => bb.isCrafter ? 1f : 0f;
}
