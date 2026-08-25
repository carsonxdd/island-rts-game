using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// Anything a unit can target: exposes a cached Health reference.
/// Implemented by UnitBase&lt;T&gt; (Worker/Warrior/Enemy) and the buildings
/// (Hut, Watchtower, Wall, Gate, BaseBuilding). Component's inherited
/// transform property satisfies the transform member.
/// </summary>
public interface ITargetable
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
/// </summary>
public static class TargetingUtil
{
    /// <summary>
    /// Nearest living entry in an ActiveRegistry list. maxRange &lt;= 0 means
    /// unlimited. Entries with no Health component yet (spawned this frame,
    /// Start not run) are skipped — they become targetable within one brain tick.
    /// Returns null (distance = float.MaxValue) when nothing qualifies.
    /// </summary>
    public static T FindNearest<T>(IReadOnlyList<T> list, Vector3 from, float maxRange, out float distance)
        where T : Component, ITargetable
    {
        T best = null;
        float bestSqr = float.MaxValue;
        float maxSqr = maxRange > 0f ? maxRange * maxRange : float.MaxValue;

        for (int i = 0; i < list.Count; i++)
        {
            T item = list[i];
            if (item == null) continue;
            Health h = item.CachedHealth;
            if (h == null || !h.IsAlive) continue;

            float sqr = (item.transform.position - from).sqrMagnitude;
            if (sqr <= maxSqr && sqr < bestSqr)
            {
                bestSqr = sqr;
                best = item;
            }
        }

        distance = best != null ? Mathf.Sqrt(bestSqr) : float.MaxValue;
        return best;
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
