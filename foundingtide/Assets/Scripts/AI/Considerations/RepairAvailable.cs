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
    private Faction mine;

    public override float ScoreRaw(AIBlackboard bb)
    {
        // Repair is construction knowledge too: locked until the Mallet is crafted
        if (!bb.faction.Knowledge.Has(Unlocks.Kind.Construction)) return 0f;

        bb.bestRepair = null;
        bb.bestRepairHealth = null;

        best = null;
        bestHealth = null;
        bestSqr = float.MaxValue;
        myPos = bb.transform.position;
        mine = bb.faction;

        // Each building names its own type (2026-09-18) - a Tent and a Hut share the
        // Hut component, so a hardcoded type here would repair a Tent at Hut prices.
        Scan(Hut.ActiveList);
        Scan(Watchtower.ActiveList);
        Scan(Workshop.ActiveList);
        Scan(Storehouse.ActiveList);
        Scan(BaseBuilding.ActiveList, BuildingType.Hut);   // the campfire has no BuildingData; priced like a hut

        var walls = Wall.ActiveList;
        for (int i = 0; i < walls.Count; i++)
        {
            Wall w = walls[i];
            if (w == null || w.Faction != mine) continue;
            Consider(w.transform, w.CachedHealth, RepairCosts.TypeOf(w));
        }
        var gates = Gate.ActiveList;
        for (int i = 0; i < gates.Count; i++)
        {
            Gate g = gates[i];
            if (g == null || g.Faction != mine) continue;
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

    /// <summary>Scan a list whose members carry their own BuildingType.</summary>
    void Scan<T>(IReadOnlyList<T> list) where T : class, ITargetable, IBuildingIdentity
    {
        for (int i = 0; i < list.Count; i++)
        {
            T entry = list[i];
            if (entry == null || entry.Faction != mine) continue;
            Consider(entry.transform, entry.CachedHealth, entry.BuildingType);
        }
    }

    /// <summary>Scan a list priced as one fixed type - the campfire, which has no BuildingData.</summary>
    void Scan<T>(IReadOnlyList<T> list, BuildingType type) where T : ITargetable
    {
        for (int i = 0; i < list.Count; i++)
        {
            T entry = list[i];
            if (entry == null || entry.Faction != mine) continue;
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
