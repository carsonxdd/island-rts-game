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

    public override float ScoreRaw(AIBlackboard bb)
    {
        if (!GuardStance.Permits(role, bb.faction)) return 0f;
        // A spearman holding a gate line stays on it (2026-09-17): DefendWall would walk
        // it to a wall segment it cannot fight through, and off the gate the raiders
        // are coming at. An archer goes — it shoots over the wall.
        if (role == GuardStance.Role.DefendWall && !bb.isRanged && GuardStance.Effective(bb.faction) == GuardStance.Mode.Defensive
            && ((bb.hasHoldPost && bb.holdLineRadius < float.MaxValue) || HoldLine.CurrentGate(bb.faction) != null)) return 0f;
        return 1f;
    }
}
