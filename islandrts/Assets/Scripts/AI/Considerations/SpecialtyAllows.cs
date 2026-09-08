/// <summary>
/// 1 when a jobless colonist may do <c>trade</c>, 0 otherwise (2026-09-07). A
/// utility colonist (<see cref="Worker.Specialty.Any"/>) may do everything; a
/// specialist only their own trade. Passing <c>Any</c> as the trade gates an
/// action to utility colonists alone (Forage: a specialist stands by instead of
/// tidying the beach). Zero-cost, so it sits with <see cref="IsJobless"/> at the
/// front of the list and early-outs the action before any scan.
/// </summary>
public class SpecialtyAllows : Consideration
{
    private readonly Worker.Specialty trade;

    public SpecialtyAllows(Worker.Specialty trade, ResponseCurve curve) : base(curve)
    {
        this.trade = trade;
    }

    public override float ScoreRaw(AIBlackboard bb) =>
        bb.specialty == Worker.Specialty.Any || bb.specialty == trade ? 1f : 0f;
}
