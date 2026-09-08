/// <summary>
/// 1 for a colonist with no job, 0 for a worker with one. Gates the Build, Craft,
/// Repair and Forage actions: jobless colonists are the colony's utility labor,
/// and a worker with a job never downs tools to build (the player can unassign
/// them if they want to). Pair with <see cref="SpecialtyAllows"/> for the trade.
/// Zero-cost, so it sits first in the consideration list and early-outs the
/// whole action for job holders before any scan runs.
/// </summary>
public class IsJobless : Consideration
{
    public IsJobless(ResponseCurve curve) : base(curve) { }

    public override float ScoreRaw(AIBlackboard bb) => bb.hasJob ? 0f : 1f;
}
