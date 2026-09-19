using UnityEngine;

/// <summary>
/// Scores the nearest living bench with something queued that this crafter may
/// hold, and caches it in <c>bb.targetStation</c> for <see cref="CraftExecutor"/>
/// (2026-09-04, Slice 3). Any bench on the island is worth the walk (floor of
/// <see cref="MinScore"/>), nearer ones score higher. 0 with nothing to work —
/// no yShift, so momentum cannot keep the action alive once the queues run dry.
///
/// The Crafting research gates it for the PLAYER's colony: before the colony
/// knows crafting there is no Crafter job to hold, and the flag check is free.
/// A rival colony has no castaway to stand at its bench, so its jobless
/// colonists work it from the first day (2026-09-16, lap step 3 slice B) —
/// otherwise a governed rival could never research anything at all.
/// </summary>
public class StationWorkAvailable : Consideration
{
    private const float AttractRange = 150f;
    private const float MinScore = 0.15f;

    public StationWorkAvailable(ResponseCurve curve) : base(curve) { }

    public override float ScoreRaw(AIBlackboard bb)
    {
        bb.targetStation = null;
        if (bb.faction.IsPlayer && !bb.faction.Knowledge.Has(Unlocks.Kind.Crafting)) return 0f;

        CraftStation best = null;
        float bestSqr = float.MaxValue;
        Vector3 myPos = bb.transform.position;

        var list = CraftStation.ActiveList;
        for (int i = 0; i < list.Count; i++)
        {
            CraftStation s = list[i];
            if (s == null || !s.HasWork || s.Faction != bb.faction) continue;

            float sqr = (s.transform.position - myPos).sqrMagnitude;
            if (sqr >= bestSqr) continue;                 // prune before the claim / alive checks
            if (!s.IsAlive || !s.CanClaim(bb.worker)) continue;

            bestSqr = sqr;
            best = s;
        }

        bb.targetStation = best;
        if (best == null) return 0f;

        return Mathf.Max(MinScore, 1f - Mathf.Sqrt(bestSqr) / AttractRange);
    }
}
