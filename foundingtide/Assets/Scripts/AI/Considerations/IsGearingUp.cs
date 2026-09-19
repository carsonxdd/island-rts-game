/// <summary>
/// 1 while a colonist owes the campfire a visit after a job change
/// (2026-09-07), else 0. Zero-cost gate for the GearUp action; the flag is set
/// by <c>Worker.OnJobChanged</c> and cleared by <c>GearUpExecutor</c> once the
/// colonist has dropped their load and picked up the tools of the new trade.
/// </summary>
public class IsGearingUp : Consideration
{
    public IsGearingUp(ResponseCurve curve) : base(curve) { }

    public override float ScoreRaw(AIBlackboard bb) => bb.gearingUp ? 1f : 0f;
}
