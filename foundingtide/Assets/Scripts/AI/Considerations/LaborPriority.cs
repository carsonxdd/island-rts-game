/// <summary>
/// The colony-wide weight for a trade (<see cref="LaborPriorities"/>), read live
/// so a slider on the campfire panel steers every idle colonist at once
/// (2026-09-07). Replaces the fixed base priorities the four jobless actions
/// used to carry; those are all 1.0 now and this is where Build > Craft >
/// Repair > Forage comes from. Zero-cost, no yShift: a weight of 0 early-outs
/// the action, which is how the player switches that work off.
/// </summary>
public class LaborPriority : Consideration
{
    private readonly Worker.Specialty trade;

    public LaborPriority(Worker.Specialty trade, ResponseCurve curve) : base(curve)
    {
        this.trade = trade;
    }

    public override float ScoreRaw(AIBlackboard bb) => bb.faction.Priorities.For(trade);
}
