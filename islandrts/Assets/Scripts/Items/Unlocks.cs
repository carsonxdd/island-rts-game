using System;
using UnityEngine;

/// <summary>
/// What the colony knows how to do (2026-09-02). Each flag is granted by
/// completing a <see cref="ResearchCatalog"/> entry at a station bench (since
/// 2026-09-03; it used to be the first craft of a tool) — knowledge, not
/// supply: after Woodcutting every colonist may take the Wood job.
///
/// Read at the point of effect, never pushed (the <c>CraftedUpgrades</c> and
/// <c>Difficulty</c> pattern): <c>BaseBuilding.AssignWorker</c>,
/// <c>BaseBuilding.CanRecruitWarrior</c>, <c>BuildPlacement.StartPlacement</c>
/// and the Build / Repair considerations each ask <see cref="Has"/> at the
/// moment they decide. NOT granted under the balance sim any more: the sim
/// researches like a player (its policies queue research and drive the
/// player character to the bench), or it is not measuring the pivot.
///
/// Statics reset on play via [RuntimeInitializeOnLoadMethod].
/// </summary>
public static class Unlocks
{
    public enum Kind
    {
        WoodJob,
        FoodJob,
        StoneJob,
        MetalJob,
        Construction,
        Militia,
        /// <summary>The Workshop building and (Slice 3) the Crafter job.</summary>
        Crafting,
        /// <summary>Bows and archers (Slice 5).</summary>
        Archery,
        /// <summary>Iron Spears (Slice 3).</summary>
        IronWork,
        /// <summary>The Shipyard and the escape (Slice 6).</summary>
        Shipwright,
    }

    public static readonly int Count = Enum.GetValues(typeof(Kind)).Length;

    private static readonly bool[] granted = new bool[Count];

    /// <summary>Fires after a grant. The campfire panel re-labels its locked rows on it.</summary>
    public static event Action OnChanged;

    public static bool Has(Kind kind)
    {
        return granted[(int)kind];
    }

    /// <summary>The unlock that opens a gathering job.</summary>
    public static Kind ForJob(ResourceNode.ResourceType type)
    {
        switch (type)
        {
            case ResourceNode.ResourceType.Food: return Kind.FoodJob;
            case ResourceNode.ResourceType.Stone: return Kind.StoneJob;
            case ResourceNode.ResourceType.Metal: return Kind.MetalJob;
            default: return Kind.WoodJob;
        }
    }

    public static bool HasJob(ResourceNode.ResourceType type) => Has(ForJob(type));

    public static void Grant(Kind kind)
    {
        if (granted[(int)kind]) return;
        granted[(int)kind] = true;
        OnChanged?.Invoke();
    }

    /// <summary>Everything at once — the F4 cheat (research rows stay as they are; use ResearchCatalog.CompleteAll for those).</summary>
    public static void GrantAll()
    {
        bool changed = false;
        for (int i = 0; i < granted.Length; i++)
        {
            if (!granted[i]) { granted[i] = true; changed = true; }
        }
        if (changed) OnChanged?.Invoke();
    }

    /// <summary>Player-facing name of the research that opens <paramref name="kind"/> ("Woodcutting"), for lock hints.</summary>
    public static string ResearchTitleFor(Kind kind)
    {
        return ResearchCatalog.TitleGranting(kind) ?? kind.ToString();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        for (int i = 0; i < granted.Length; i++) granted[i] = false;
        OnChanged = null;
    }
}
