using UnityEngine;

/// <summary>
/// The Engage gate (2026-09-07): 1 when some living hostile fighter passes
/// <see cref="GuardStance.Allows"/> for this warrior, 0 otherwise. Replaces the
/// old distance curve on Engage — "close enough to fight" is now the stance's
/// call, not a fixed 50 u falloff. Caches the nearest ALLOWED target on
/// <c>bb.nearestEnemy</c> so the executor and the facing code agree with the
/// score. Put <see cref="EnemyPresence"/> before it: that one is frame-cached
/// and early-outs the action for free when nothing hostile is alive at all.
/// Since lap step 1 commit 5 "hostile fighter" means a raider body OR another
/// colony's warrior whose faction is Hostile to this one.
/// </summary>
public class StanceTargetAvailable : Consideration
{
    public StanceTargetAvailable(ResponseCurve curve) : base(curve) { }

    public override float ScoreRaw(AIBlackboard bb)
    {
        ITargetable best = null;
        float bestSqr = float.MaxValue;
        Vector3 from = bb.transform.position;

        // The Unity null check happens on the concrete type: an interface reference to
        // a destroyed component never compares equal to null.
        var enemies = Enemy.ActiveList;
        for (int i = 0; i < enemies.Count; i++) { Enemy e = enemies[i]; if (e != null) Consider(bb, e, from, ref best, ref bestSqr); }
        var warriors = Warrior.ActiveList;
        for (int i = 0; i < warriors.Count; i++) { Warrior w = warriors[i]; if (w != null && w != bb.warrior) Consider(bb, w, from, ref best, ref bestSqr); }

        bb.nearestEnemy = best != null ? best.transform : null;
        bb.nearestEnemyDistance = best != null ? Mathf.Sqrt(bestSqr) : float.MaxValue;
        return best != null ? 1f : 0f;
    }

    static void Consider(AIBlackboard bb, ITargetable t, Vector3 from, ref ITargetable best, ref float bestSqr)
    {
        float sqr = (t.transform.position - from).sqrMagnitude;
        if (sqr >= bestSqr) return;                              // cheap cull before the relation, health and stance tests
        if (!bb.faction.IsHostileTo(t.Faction)) return;
        Health h = t.CachedHealth;
        if (h == null || !h.IsAlive) return;
        if (!GuardStance.Allows(t, from, bb.baseBuilding, bb.faction)) return;

        bestSqr = sqr;
        best = t;
    }
}
