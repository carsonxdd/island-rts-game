using UnityEngine;

/// <summary>
/// The Engage gate (2026-09-07): 1 when some living enemy passes
/// <see cref="GuardStance.Allows"/> for this warrior, 0 otherwise. Replaces the
/// old distance curve on Engage — "close enough to fight" is now the stance's
/// call, not a fixed 50 u falloff. Caches the nearest ALLOWED enemy on
/// <c>bb.nearestEnemy</c> so the executor and the facing code agree with the
/// score. Put <see cref="EnemyPresence"/> before it: that one is frame-cached
/// and early-outs the action for free when nothing is alive at all.
/// </summary>
public class StanceTargetAvailable : Consideration
{
    public StanceTargetAvailable(ResponseCurve curve) : base(curve) { }

    public override float ScoreRaw(AIBlackboard bb)
    {
        Enemy best = null;
        float bestSqr = float.MaxValue;
        Vector3 from = bb.transform.position;

        for (int i = 0; i < Enemy.ActiveList.Count; i++)
        {
            Enemy e = Enemy.ActiveList[i];
            if (e == null) continue;
            Health h = e.CachedHealth;
            if (h == null || !h.IsAlive) continue;

            float sqr = (e.transform.position - from).sqrMagnitude;
            if (sqr >= bestSqr) continue;                          // cheap cull before the stance test
            if (!GuardStance.Allows(e, from, bb.baseBuilding)) continue;

            bestSqr = sqr;
            best = e;
        }

        bb.nearestEnemy = best != null ? best.transform : null;
        bb.nearestEnemyDistance = best != null ? Mathf.Sqrt(bestSqr) : float.MaxValue;
        return best != null ? 1f : 0f;
    }
}
