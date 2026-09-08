/// <summary>
/// 1 when the colony's <see cref="GuardStance"/> runs this action, else 0
/// (2026-09-07). Zero-cost and no yShift, so it sits first in the list and
/// early-outs Intercept / DefendWall / Patrol / Follow before any scan — and a
/// stance change on the panel kills the running action at the next tick
/// despite its momentum.
/// </summary>
public class StanceAllows : Consideration
{
    private readonly GuardStance.Role role;

    public StanceAllows(GuardStance.Role role, ResponseCurve curve) : base(curve)
    {
        this.role = role;
    }

    public override float ScoreRaw(AIBlackboard bb) => GuardStance.Permits(role) ? 1f : 0f;
}
