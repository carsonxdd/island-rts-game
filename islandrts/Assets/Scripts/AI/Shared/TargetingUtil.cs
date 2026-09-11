using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// Anything a unit can target: exposes a cached Health reference and, since lap
/// step 1 commit 5 (2026-09-09), its owner through <see cref="IOwned"/>.
/// Implemented by UnitBase&lt;T&gt; (Worker/Warrior/Enemy) and the buildings
/// (Hut, Watchtower, Wall, Gate, Workshop, Shipyard, BaseBuilding). Component's
/// inherited transform property satisfies the transform member.
/// </summary>
public interface ITargetable : IOwned
{
    Health CachedHealth { get; }
    Transform transform { get; }
}

/// <summary>
/// Shared target-selection and approach-geometry helpers for all unit AI.
/// Phase 6.25: consolidates the nearest-alive scans that were hand-rolled in
/// EnemyAttackExecutor / EngageEnemyExecutor / EnemyPresence, and the
/// ClosestPoint -> SamplePosition approach-point pattern that was duplicated in
/// EnemyAttackExecutor / HealAtCampfireExecutor (and needed by ReturnToBase).
/// All methods are zero-GC.
///
/// Lap step 1 commit 5: the registries stay global (minimap, fog, perf want
/// everything), so ownership is a FILTER on the scan — <see cref="FindNearestOwned{T}"/>
/// for "my huts / my sites" and <see cref="FindNearestHostile{T}"/> for "anything
/// whose faction is Hostile to me". A `Faction` and a relation read, no delegates.
/// </summary>
public static class TargetingUtil
{
    /// <summary>
    /// Nearest living entry in an ActiveRegistry list, any owner. maxRange &lt;= 0 means
    /// unlimited. Entries with no Health component yet (spawned this frame,
    /// Start not run) are skipped — they become targetable within one brain tick.
    /// Returns null (distance = float.MaxValue) when nothing qualifies.
    /// </summary>
    public static T FindNearest<T>(IReadOnlyList<T> list, Vector3 from, float maxRange, out float distance)
        where T : Component, ITargetable
    {
        return Scan(list, from, maxRange, null, Attitude.Neutral, false, out distance);
    }

    /// <summary>Nearest living entry that <paramref name="owner"/> owns.</summary>
    public static T FindNearestOwned<T>(IReadOnlyList<T> list, Vector3 from, float maxRange, Faction owner, out float distance)
        where T : Component, ITargetable
    {
        return Scan(list, from, maxRange, owner, Attitude.Allied, true, out distance);
    }

    /// <summary>
    /// How many living entries <paramref name="owner"/> owns (2026-09-11). The
    /// registries are global by design, so a per-faction COUNT is a filter on the
    /// list like every other ownership question - never a second list. Called once
    /// a dawn by the raid roll and once a second by a governor, so the loop is fine.
    /// </summary>
    public static int CountOwned<T>(IReadOnlyList<T> list, Faction owner)
        where T : Component, ITargetable
    {
        if (owner == null) return 0;
        int n = 0;
        for (int i = 0; i < list.Count; i++)
        {
            T item = list[i];
            if (item != null && item.Faction == owner) n++;
        }
        return n;
    }

    /// <summary>Nearest living entry whose faction is Hostile to <paramref name="me"/>.</summary>
    public static T FindNearestHostile<T>(IReadOnlyList<T> list, Vector3 from, float maxRange, Faction me, out float distance)
        where T : Component, ITargetable
    {
        return Scan(list, from, maxRange, me, Attitude.Hostile, false, out distance);
    }

    static T Scan<T>(IReadOnlyList<T> list, Vector3 from, float maxRange, Faction me, Attitude wanted, bool sameOwner, out float distance)
        where T : Component, ITargetable
    {
        T best = null;
        float bestSqr = float.MaxValue;
        float maxSqr = maxRange > 0f ? maxRange * maxRange : float.MaxValue;

        for (int i = 0; i < list.Count; i++)
        {
            T item = list[i];
            if (item == null) continue;

            float sqr = (item.transform.position - from).sqrMagnitude;
            if (sqr > maxSqr || sqr >= bestSqr) continue;   // distance cull before the relation read and the Health fetch

            if (me != null)
            {
                Faction owner = item.Faction;
                if (sameOwner ? owner != me : Relations.Get(me, owner) != wanted) continue;
            }

            Health h = item.CachedHealth;
            if (h == null || !h.IsAlive) continue;

            bestSqr = sqr;
            best = item;
        }

        distance = best != null ? Mathf.Sqrt(bestSqr) : float.MaxValue;
        return best;
    }

    /// <summary>
    /// The nearest living fighter hostile to <paramref name="me"/>: a raider body
    /// (<see cref="Enemy"/>) or another colony's <see cref="Warrior"/>. What every
    /// warrior scan (Engage, Intercept, Patrol, Heal, the kite check) means by
    /// "an enemy" since commit 5. Returns the component as an <see cref="ITargetable"/>.
    /// </summary>
    public static ITargetable FindNearestHostileCombatant(Vector3 from, float maxRange, Faction me, out float distance)
    {
        float dEnemy, dWarrior;
        Enemy e = FindNearestHostile(Enemy.ActiveList, from, maxRange, me, out dEnemy);
        Warrior w = FindNearestHostile(Warrior.ActiveList, from, maxRange, me, out dWarrior);
        if (w != null && (e == null || dWarrior < dEnemy)) { distance = dWarrior; return w; }
        distance = dEnemy;
        return e;
    }

    /// <summary>
    /// Average position of every living fighter hostile to <paramref name="me"/>
    /// (the Intercept rally's "where the raiders are"). count = 0 when none.
    /// </summary>
    public static Vector3 HostileCentroid(Faction me, out int count)
    {
        Vector3 sum = Vector3.zero;
        count = 0;
        var enemies = Enemy.ActiveList;
        for (int i = 0; i < enemies.Count; i++)
        {
            Enemy e = enemies[i];
            if (e == null || !me.IsHostileTo(e.Faction)) continue;
            Health h = e.CachedHealth;
            if (h != null && !h.IsAlive) continue;
            sum += e.transform.position;
            count++;
        }
        var warriors = Warrior.ActiveList;
        for (int i = 0; i < warriors.Count; i++)
        {
            Warrior w = warriors[i];
            if (w == null || !me.IsHostileTo(w.Faction)) continue;
            Health h = w.CachedHealth;
            if (h != null && !h.IsAlive) continue;
            sum += w.transform.position;
            count++;
        }
        return count > 0 ? sum / count : Vector3.zero;
    }

    /// <summary>
    /// Walkable NavMesh point at the target's collider edge nearest to
    /// <paramref name="from"/>. ClosestPoint on a carving obstacle's collider
    /// sits on the carve boundary, which SetDestination silently rejects while
    /// the NavMesh is mid-recalc — so the raw point is snapped through
    /// NavMesh.SamplePosition first. Falls back to the raw point (caller's
    /// TrySetDestination retry handles a rejected set), then to target.position.
    /// ANY destination on or near a carving obstacle must go through this.
    /// </summary>
    public static Vector3 GetApproachPoint(Vector3 from, Transform target, Collider targetCollider)
    {
        Vector3 raw = targetCollider != null
            ? targetCollider.ClosestPoint(from)
            : target.position;

        NavMeshHit hit;
        if (NavMesh.SamplePosition(raw, out hit, 2f, NavMesh.AllAreas))
            return hit.position;
        return raw;
    }

    /// <summary>
    /// Distance from <paramref name="from"/> to the target's collider edge
    /// (center distance when no collider). Use this for every attack-range /
    /// interaction-range check against buildings — center distance never drops
    /// below the arrival threshold on large or carving targets.
    /// </summary>
    public static float EdgeDistance(Vector3 from, Transform target, Collider targetCollider)
    {
        if (targetCollider == null)
            return Vector3.Distance(from, target.position);
        return Vector3.Distance(from, targetCollider.ClosestPoint(from));
    }
}
