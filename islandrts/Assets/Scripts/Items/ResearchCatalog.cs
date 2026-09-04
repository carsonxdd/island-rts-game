using System;
using UnityEngine;

/// <summary>
/// The tech tree (2026-09-03, Slice 2 of the research-and-days plan). Each
/// entry is researched ONCE per run at a station bench — exactly like a craft
/// (same queue, same labor rule, same panel row), the only difference being
/// the output: <see cref="Unlocks"/> flags, recipes that name it in
/// <c>requires</c>, and for the Workshop-tier upgrades a global multiplier.
///
/// A station lists the entries of its own tier (<see cref="Station"/>): the
/// campfire teaches the basics, the Workshop the upgrades. Prerequisites are
/// research ids. Static like the item catalog; reset on play.
/// </summary>
public static class ResearchCatalog
{
    public enum Station { Campfire, Workshop }

    public sealed class ResearchDef : WorkDef
    {
        public int tier;
        public Station station;
        public string[] prerequisites = NoPrereqs;
        public Unlocks.Kind[] grants = NoGrants;
        /// <summary>Extra effect on completion (the Workshop-tier multipliers).</summary>
        public Action apply;
        public bool done;

        public override WorkCategory Category => WorkCategory.Research;
    }

    static readonly string[] NoPrereqs = new string[0];
    static readonly Unlocks.Kind[] NoGrants = new Unlocks.Kind[0];

    public static readonly ResearchDef[] All =
    {
        // --- Campfire tier ---------------------------------------------------
        new ResearchDef
        {
            id = "woodcutting", title = "Woodcutting", tier = 1, station = Station.Campfire,
            description = "Colonists can be sent to cut wood",
            itemCosts = new[] { new ItemCost(ItemCatalog.Stick, 2), new ItemCost(ItemCatalog.StoneChunk, 1) },
            seconds = 6f,
            grants = new[] { Unlocks.Kind.WoodJob },
        },
        new ResearchDef
        {
            id = "foraging", title = "Foraging", tier = 1, station = Station.Campfire,
            description = "Colonists can be sent to forage food",
            itemCosts = new[] { new ItemCost(ItemCatalog.Stick, 2), new ItemCost(ItemCatalog.StoneChunk, 1) },
            seconds = 6f,
            grants = new[] { Unlocks.Kind.FoodJob },
        },
        new ResearchDef
        {
            id = "quarrying", title = "Quarrying", tier = 1, station = Station.Campfire,
            description = "Colonists can be sent to quarry stone",
            itemCosts = new[] { new ItemCost(ItemCatalog.Stick, 2), new ItemCost(ItemCatalog.StoneChunk, 2) },
            seconds = 8f,
            grants = new[] { Unlocks.Kind.StoneJob },
        },
        new ResearchDef
        {
            id = "construction", title = "Construction", tier = 2, station = Station.Campfire,
            description = "Opens build mode; idle colonists build and repair",
            itemCosts = new[] { new ItemCost(ItemCatalog.Stick, 3), new ItemCost(ItemCatalog.StoneChunk, 1) },
            seconds = 8f,
            prerequisites = new[] { "woodcutting" },
            grants = new[] { Unlocks.Kind.Construction },
        },
        new ResearchDef
        {
            id = "spearcraft", title = "Spearcraft", tier = 2, station = Station.Campfire,
            description = "Wooden Spears can be made; an idle colonist with a spear is a warrior",
            itemCosts = new[] { new ItemCost(ItemCatalog.Stick, 3), new ItemCost(ItemCatalog.StoneChunk, 1) },
            woodCost = 5,
            seconds = 10f,
            prerequisites = new[] { "woodcutting" },
            grants = new[] { Unlocks.Kind.Militia },
        },
        new ResearchDef
        {
            id = "crafting", title = "Crafting", tier = 3, station = Station.Campfire,
            description = "The Workshop can be built; it researches upgrades and crafts faster",
            woodCost = 10, stoneCost = 5,
            seconds = 10f,
            prerequisites = new[] { "construction" },
            grants = new[] { Unlocks.Kind.Crafting },
        },
        new ResearchDef
        {
            id = "mining", title = "Mining", tier = 3, station = Station.Campfire,
            description = "Colonists can be sent to mine ore",
            itemCosts = new[] { new ItemCost(ItemCatalog.Stick, 2), new ItemCost(ItemCatalog.StoneChunk, 2) },
            stoneCost = 15,
            seconds = 12f,
            prerequisites = new[] { "quarrying" },
            grants = new[] { Unlocks.Kind.MetalJob },
        },

        // --- Workshop tier ---------------------------------------------------
        new ResearchDef
        {
            id = "sharp_tools", title = "Sharpened Tools", tier = 4, station = Station.Workshop,
            description = "+30% gather speed for all workers",
            woodCost = 25, stoneCost = 15,
            seconds = 10f,
            prerequisites = new[] { "crafting" },
            apply = () => CraftedUpgrades.SetGatherRate(1.3f),
        },
        new ResearchDef
        {
            id = "scaffolds", title = "Sturdy Scaffolds", tier = 4, station = Station.Workshop,
            description = "+50% construction speed",
            woodCost = 30, stoneCost = 10,
            seconds = 10f,
            prerequisites = new[] { "construction" },
            apply = () => CraftedUpgrades.SetBuildSpeed(1.5f),
        },
    };

    /// <summary>Fires after any entry completes. The campfire panel re-labels its locked rows on it.</summary>
    public static event Action OnChanged;

    public static ResearchDef Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < All.Length; i++)
            if (All[i].id == id) return All[i];
        return null;
    }

    /// <summary>True when <paramref name="id"/> is done — or is not a research id at all (an empty requirement is no requirement).</summary>
    public static bool IsDone(string id)
    {
        if (string.IsNullOrEmpty(id)) return true;
        ResearchDef d = Find(id);
        return d == null || d.done;
    }

    /// <summary>Not yet done and every prerequisite is.</summary>
    public static bool IsAvailable(ResearchDef d)
    {
        if (d == null || d.done) return false;
        for (int i = 0; i < d.prerequisites.Length; i++)
            if (!IsDone(d.prerequisites[i])) return false;
        return true;
    }

    /// <summary>Title of the first prerequisite still outstanding, or null when there is none.</summary>
    public static string PrerequisiteTitle(ResearchDef d)
    {
        for (int i = 0; i < d.prerequisites.Length; i++)
        {
            if (IsDone(d.prerequisites[i])) continue;
            ResearchDef p = Find(d.prerequisites[i]);
            return p != null ? p.title : d.prerequisites[i];
        }
        return null;
    }

    /// <summary>Title of the entry that grants <paramref name="kind"/>, for lock hints ("Research Woodcutting").</summary>
    public static string TitleGranting(Unlocks.Kind kind)
    {
        for (int i = 0; i < All.Length; i++)
        {
            var grants = All[i].grants;
            for (int g = 0; g < grants.Length; g++)
                if (grants[g] == kind) return All[i].title;
        }
        return null;
    }

    public static bool AllDone
    {
        get
        {
            for (int i = 0; i < All.Length; i++)
                if (!All[i].done) return false;
            return true;
        }
    }

    /// <summary>Mark done, grant its flags, run its effect. Idempotent.</summary>
    public static void Complete(ResearchDef d)
    {
        if (d == null || d.done) return;
        d.done = true;
        for (int i = 0; i < d.grants.Length; i++) Unlocks.Grant(d.grants[i]);
        d.apply?.Invoke();
        OnChanged?.Invoke();
        Debug.Log("Researched " + d.title + " — " + d.description);   // once per entry per run
    }

    /// <summary>Everything at once — the F4 cheat.</summary>
    public static void CompleteAll()
    {
        for (int i = 0; i < All.Length; i++) Complete(All[i]);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        for (int i = 0; i < All.Length; i++) All[i].done = false;
        OnChanged = null;
    }
}
