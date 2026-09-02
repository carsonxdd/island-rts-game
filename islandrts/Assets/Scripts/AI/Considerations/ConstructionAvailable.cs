using UnityEngine;

/// <summary>
/// Scores the nearest construction site that still has room for another builder,
/// and caches it in <c>bb.bestSite</c> for <see cref="BuildExecutor"/>. Any site on
/// the island is worth walking to (floor of <see cref="MinScore"/>), nearer ones
/// score higher so a colonist picks the close one. 0 when there is nothing to build,
/// which early-outs the action — the executor also hands control back the moment
/// its site completes.
/// </summary>
public class ConstructionAvailable : Consideration
{
    private const float AttractRange = 150f;
    private const float MinScore = 0.15f;

    public ConstructionAvailable(ResponseCurve curve) : base(curve) { }

    public override float ScoreRaw(AIBlackboard bb)
    {
        bb.bestSite = null;

        ConstructionSite best = null;
        float bestSqr = float.MaxValue;
        Vector3 myPos = bb.transform.position;

        var list = ConstructionSite.ActiveList;
        for (int i = 0; i < list.Count; i++)
        {
            ConstructionSite site = list[i];
            if (site == null || site.IsComplete) continue;

            float sqr = (site.transform.position - myPos).sqrMagnitude;
            if (sqr >= bestSqr) continue;                 // prune before the list walk below
            if (!site.HasBuilderRoom(bb.worker)) continue;

            bestSqr = sqr;
            best = site;
        }

        bb.bestSite = best;
        if (best == null) return 0f;

        return Mathf.Max(MinScore, 1f - Mathf.Sqrt(bestSqr) / AttractRange);
    }
}
