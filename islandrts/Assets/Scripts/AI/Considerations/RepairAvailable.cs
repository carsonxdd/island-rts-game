using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Scores the nearest damaged friendly building the colony can afford to repair,
/// and caches it (transform, Health, cost type) on the blackboard for
/// <see cref="RepairExecutor"/>. Walks every building registry — huts, towers, the
/// workshop, walls, gates and the campfire — with a squared-distance prune, so the
/// per-building cost lookup only runs once, for the winner. 0 when nothing is
/// damaged or the pool cannot cover the next unit of the repair, which early-outs
/// the action; ThreatNearby beside it keeps colonists off walls that are being chewed.
/// </summary>
public class RepairAvailable : Consideration
{
    private const float AttractRange = 150f;
    private const float MinScore = 0.15f;
    private const float DamageThreshold = 0.5f;   // HP below max before a building counts as damaged

    public RepairAvailable(ResponseCurve curve) : base(curve) { }

    // Per-scan scratch (one instance per brain — no allocation)
    private Transform best;
    private Health bestHealth;
    private BuildingType bestType;
    private float bestSqr;
    private Vector3 myPos;

    public override float ScoreRaw(AIBlackboard bb)
    {
        bb.bestRepair = null;
        bb.bestRepairHealth = null;

        best = null;
        bestHealth = null;
        bestSqr = float.MaxValue;
        myPos = bb.transform.position;

        Scan(Hut.ActiveList, BuildingType.Hut);
        Scan(Watchtower.ActiveList, BuildingType.Watchtower);
        Scan(Workshop.ActiveList, BuildingType.Workshop);
        Scan(BaseBuilding.ActiveList, BuildingType.Hut);   // the campfire is priced like a hut

        var walls = Wall.ActiveList;
        for (int i = 0; i < walls.Count; i++)
        {
            Wall w = walls[i];
            if (w == null) continue;
            Consider(w.transform, w.CachedHealth, RepairCosts.TypeOf(w));
        }
        var gates = Gate.ActiveList;
        for (int i = 0; i < gates.Count; i++)
        {
            Gate g = gates[i];
            if (g == null) continue;
            Consider(g.transform, g.CachedHealth, RepairCosts.TypeOf(g));
        }

        if (best == null) return 0f;

        RepairCosts.PerHp cost;
        if (!RepairCosts.TryGetPerHp(bestType, bestHealth.maxHealth, out cost)) return 0f;
        if (cost.Any && !RepairCosts.CanAffordAny(cost)) return 0f;

        bb.bestRepair = best;
        bb.bestRepairHealth = bestHealth;
        bb.bestRepairType = bestType;

        return Mathf.Max(MinScore, 1f - Mathf.Sqrt(bestSqr) / AttractRange);
    }

    void Scan<T>(IReadOnlyList<T> list, BuildingType type) where T : ITargetable
    {
        for (int i = 0; i < list.Count; i++)
        {
            T entry = list[i];
            if (entry == null) continue;
            Consider(entry.transform, entry.CachedHealth, type);
        }
    }

    void Consider(Transform t, Health h, BuildingType type)
    {
        if (t == null || h == null || !h.IsAlive) return;
        if (h.currentHealth >= h.maxHealth - DamageThreshold) return;

        float sqr = (t.position - myPos).sqrMagnitude;
        if (sqr >= bestSqr) return;

        bestSqr = sqr;
        best = t;
        bestHealth = h;
        bestType = type;
    }
}
