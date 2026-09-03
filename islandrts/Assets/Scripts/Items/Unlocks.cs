using System;
using UnityEngine;

/// <summary>
/// What the colony knows how to do (2026-09-02). Each flag is opened by
/// crafting a tool at the campfire once — knowledge, not supply: after the
/// first Stone Axe every colonist may take the Wood job.
///
/// Read at the point of effect, never pushed (the <c>CraftedUpgrades</c> and
/// <c>Difficulty</c> pattern): <c>BaseBuilding.AssignWorker</c>,
/// <c>BaseBuilding.CanRecruitWarrior</c>, <c>BuildPlacement.StartPlacement</c>
/// and the Build / Repair considerations each ask <see cref="Has"/> at the
/// moment they decide. Everything is granted under the balance sim, where no
/// player can craft (same rule as <c>Difficulty.Active</c> returning Normal).
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
    }

    public static readonly int Count = Enum.GetValues(typeof(Kind)).Length;

    private static readonly bool[] granted = new bool[Count];

    /// <summary>Fires after a grant. The campfire panel re-labels its locked rows on it.</summary>
    public static event Action OnChanged;

    public static bool Has(Kind kind)
    {
        if (SimHooks.Simulating) return true;
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

    public static bool AllGranted
    {
        get
        {
            for (int i = 0; i < granted.Length; i++)
                if (!granted[i]) return false;
            return true;
        }
    }

    public static void Grant(Kind kind)
    {
        if (granted[(int)kind]) return;
        granted[(int)kind] = true;
        OnChanged?.Invoke();
    }

    /// <summary>Everything at once — the F4 cheat.</summary>
    public static void GrantAll()
    {
        bool changed = false;
        for (int i = 0; i < granted.Length; i++)
        {
            if (!granted[i]) { granted[i] = true; changed = true; }
        }
        if (changed) OnChanged?.Invoke();
    }

    /// <summary>Player-facing name of the recipe that opens <paramref name="kind"/> ("Stone Axe"), for lock hints.</summary>
    public static string RecipeTitleFor(Kind kind)
    {
        var recipes = CraftingCatalog.CampfireRecipes;
        for (int i = 0; i < recipes.Length; i++)
        {
            var unlocks = recipes[i].unlocks;
            for (int u = 0; u < unlocks.Length; u++)
                if (unlocks[u] == kind) return recipes[i].title;
        }
        return "a tool";
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        for (int i = 0; i < granted.Length; i++) granted[i] = false;
        OnChanged = null;
    }
}
