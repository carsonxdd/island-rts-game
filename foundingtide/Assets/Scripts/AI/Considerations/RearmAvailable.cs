/// <summary>
/// 1 when the campfire stockpile holds a better weapon of this warrior's kind
/// than the one in hand, 0 otherwise (2026-09-04, Slice 3). Zero cost — a few
/// stockpile counts, no scan — and no yShift, so the Rearm action early-outs the
/// moment there is nothing better to fetch.
/// </summary>
public class RearmAvailable : Consideration
{
    public RearmAvailable(ResponseCurve curve) : base(curve) { }

    public override float ScoreRaw(AIBlackboard bb)
    {
        BaseBuilding fire = bb.baseBuilding;
        if (fire == null || bb.warrior == null) return 0f;
        if (fire.CachedHealth != null && !fire.CachedHealth.IsAlive) return 0f;
        return ItemCatalog.BetterWeaponInStock(bb.warrior.weapon, fire.Stockpile) != null ? 1f : 0f;
    }
}
