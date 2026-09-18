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
        /// <summary>
        /// The tool this research puts in the player's hands when it completes
        /// (2026-09-03). Learning to cut wood and making the axe were two entries
        /// on two tabs, and the second was easy to miss — one entry teaches AND
        /// equips now, and the tool recipes are gone from the Craft tab.
        /// </summary>
        public ItemDef tool;
        /// <summary>Extra effect on completion (the Workshop-tier multipliers), applied to the completing faction's Knowledge.</summary>
        public Action<Knowledge> apply;
        /// <summary>Position in <see cref="All"/>; the per-faction done flag lives at this index in <see cref="Knowledge"/>.</summary>
        public int index;

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
            description = "Makes you a Stone Axe; colonists can be sent to cut wood",
            itemCosts = new[] { new ItemCost(ItemCatalog.Stick, 3), new ItemCost(ItemCatalog.StoneChunk, 2) },
            seconds = 10f,
            grants = new[] { Unlocks.Kind.WoodJob },
            tool = ItemCatalog.StoneAxe,
        },
        new ResearchDef
        {
            id = "foraging", title = "Foraging", tier = 1, station = Station.Campfire,
            description = "Makes you a Fishing Spear; colonists can be sent to forage food",
            itemCosts = new[] { new ItemCost(ItemCatalog.Stick, 3), new ItemCost(ItemCatalog.StoneChunk, 2) },
            seconds = 10f,
            grants = new[] { Unlocks.Kind.FoodJob },
            tool = ItemCatalog.FishingSpear,
        },
        new ResearchDef
        {
            id = "quarrying", title = "Quarrying", tier = 1, station = Station.Campfire,
            description = "Makes you a Stone Pick; colonists can be sent to quarry stone",
            itemCosts = new[] { new ItemCost(ItemCatalog.Stick, 3), new ItemCost(ItemCatalog.StoneChunk, 3) },
            seconds = 12f,
            grants = new[] { Unlocks.Kind.StoneJob },
            tool = ItemCatalog.StonePick,
        },
        new ResearchDef
        {
            id = "construction", title = "Construction", tier = 2, station = Station.Campfire,
            description = "Makes you a Mallet; opens build mode, and idle colonists build and repair",
            itemCosts = new[] { new ItemCost(ItemCatalog.Stick, 4), new ItemCost(ItemCatalog.StoneChunk, 2) },
            seconds = 12f,
            prerequisites = new[] { "woodcutting" },
            grants = new[] { Unlocks.Kind.Construction },
            tool = ItemCatalog.Mallet,
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
            description = "Idle colonists work the benches; the Workshop can be built and crafts faster",
            woodCost = 10, stoneCost = 5,
            seconds = 10f,
            prerequisites = new[] { "construction" },
            grants = new[] { Unlocks.Kind.Crafting },
        },
        new ResearchDef
        {
            id = "mining", title = "Mining", tier = 3, station = Station.Campfire,
            description = "Makes you a Metal Pick; colonists can be sent to mine ore",
            itemCosts = new[] { new ItemCost(ItemCatalog.Stick, 3), new ItemCost(ItemCatalog.StoneChunk, 3) },
            stoneCost = 15,
            seconds = 16f,
            prerequisites = new[] { "quarrying" },
            grants = new[] { Unlocks.Kind.MetalJob },
            tool = ItemCatalog.MetalPick,
        },

        // Storage: the campfire stockpile starts small on purpose — a colony
        // that wants to hoard has to invest in it. Since 2026-09-16 the same
        // research opens the Storehouse: a drop-off point colonists deliver to
        // (the nearest one wins) that adds a little room of its own.
        new ResearchDef
        {
            id = "storage_pits", title = "Storage Pits", tier = 2, station = Station.Campfire,
            description = "+40 room in the campfire stockpile; the Storehouse can be built (a drop-off point near far work)",
            itemCosts = new[] { new ItemCost(ItemCatalog.Stick, 4) },
            woodCost = 15,
            seconds = 10f,
            prerequisites = new[] { "construction" },
            grants = new[] { Unlocks.Kind.Storage },
            apply = k => k.StockpileRoom += 40,
        },

        // --- Workshop tier ---------------------------------------------------
        new ResearchDef
        {
            id = "sharp_tools", title = "Sharpened Tools", tier = 4, station = Station.Workshop,
            description = "+30% gather speed for all workers",
            woodCost = 25, stoneCost = 15,
            seconds = 10f,
            prerequisites = new[] { "crafting" },
            apply = k => k.GatherRateMult = 1.3f,
        },
        new ResearchDef
        {
            id = "scaffolds", title = "Sturdy Scaffolds", tier = 4, station = Station.Workshop,
            description = "+50% construction speed",
            woodCost = 30, stoneCost = 10,
            seconds = 10f,
            prerequisites = new[] { "construction" },
            apply = k => k.BuildSpeedMult = 1.5f,
        },
        new ResearchDef
        {
            id = "racks", title = "Racks and Baskets", tier = 4, station = Station.Workshop,
            description = "+80 room in the campfire stockpile",
            woodCost = 30, stoneCost = 10,
            seconds = 12f,
            prerequisites = new[] { "storage_pits" },
            apply = k => k.StockpileRoom += 80,
        },
        // Metal's first use (2026-09-04, Slice 3): the Iron Spear recipe.
        new ResearchDef
        {
            id = "iron_work", title = "Iron Work", tier = 4, station = Station.Workshop,
            description = "Iron Spears can be made: 35 damage against the Wooden Spear's 25",
            woodCost = 20, stoneCost = 25, metalCost = 10,
            seconds = 14f,
            prerequisites = new[] { "mining" },
            grants = new[] { Unlocks.Kind.IronWork },
        },
        // Archers (2026-09-04, Slice 5): the Bow recipe; a bow-armed recruit is an archer.
        new ResearchDef
        {
            id = "bowyery", title = "Bowyery", tier = 4, station = Station.Workshop,
            description = "Bows can be made; a colonist armed with one is an archer who shoots over walls",
            woodCost = 20, foodCost = 5,
            seconds = 14f,
            prerequisites = new[] { "spearcraft" },
            grants = new[] { Unlocks.Kind.Archery },
        },
        // The escape (2026-09-04, Slice 6): the Shipyard building, and with it the early ending.
        new ResearchDef
        {
            id = "shipwright", title = "Shipwright", tier = 5, station = Station.Workshop,
            description = "The Shipyard can be built on a beach; a finished ship sails you home early",
            woodCost = 40, stoneCost = 30, metalCost = 10,
            seconds = 20f,
            prerequisites = new[] { "iron_work" },
            grants = new[] { Unlocks.Kind.Shipwright },
        },
    };

    static ResearchCatalog()
    {
        for (int i = 0; i < All.Length; i++) All[i].index = i;
    }

    public static ResearchDef Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < All.Length; i++)
            if (All[i].id == id) return All[i];
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
}
