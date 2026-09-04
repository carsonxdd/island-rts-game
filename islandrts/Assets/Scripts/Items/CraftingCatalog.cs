using UnityEngine;

/// <summary>
/// What can be made at a station (2026-09-02; repeatable since the research
/// split of 2026-09-03). A recipe is listed once its <see cref="Recipe.requires"/>
/// research is done, queued <i>n</i> at a time, and each completion charges
/// its own costs (items from the laborer's hands and the campfire stockpile,
/// resources from the pool).
///
/// Output: equipment and materials go to the campfire stockpile whichever
/// station made them (one colony store); a tool goes into the player's hands
/// when the player is the one at the bench. Tools are <see cref="Recipe.oncePerRun"/>
/// — they are the character's own kit and purely visual, so a second one has
/// no use.
///
/// Research is the other kind of station work — see <see cref="ResearchCatalog"/>.
/// </summary>
public static class CraftingCatalog
{
    public sealed class Recipe : WorkDef
    {
        public WorkCategory category;
        /// <summary>Research id that lists this recipe; empty = always listed.</summary>
        public string requires;
        public ItemDef output;
        public int outputCount = 1;
        /// <summary>Craftable once per run (the player's tools).</summary>
        public bool oncePerRun;
        /// <summary>Set after the first completion of a <see cref="oncePerRun"/> recipe.</summary>
        public bool made;

        public override WorkCategory Category => category;

        public bool Unlocked => ResearchCatalog.IsDone(requires);

        /// <summary>Title of the research that lists it, for lock hints.</summary>
        public string RequiredTitle
        {
            get
            {
                ResearchCatalog.ResearchDef d = ResearchCatalog.Find(requires);
                return d != null ? d.title : requires;
            }
        }
    }

    static readonly ItemCost[] Kit2_1 = { new ItemCost(ItemCatalog.Stick, 2), new ItemCost(ItemCatalog.StoneChunk, 1) };
    static readonly ItemCost[] Kit2_2 = { new ItemCost(ItemCatalog.Stick, 2), new ItemCost(ItemCatalog.StoneChunk, 2) };
    static readonly ItemCost[] Kit3_1 = { new ItemCost(ItemCatalog.Stick, 3), new ItemCost(ItemCatalog.StoneChunk, 1) };

    public static readonly Recipe[] All =
    {
        // --- Weapons (repeatable; the colony's supply line) -------------------
        new Recipe
        {
            id = "wooden_spear", title = "Wooden Spear", category = WorkCategory.Weapon,
            description = "Arms one warrior. Goes to the stockpile",
            itemCosts = Kit3_1, woodCost = 5,
            seconds = 10f, output = ItemCatalog.WoodenSpear,
            requires = "spearcraft",
        },

        // --- The player's tools (once per run, visual) ------------------------
        new Recipe
        {
            id = "stone_axe", title = "Stone Axe", category = WorkCategory.Tool,
            description = "A tool for your own hands",
            itemCosts = Kit2_1,
            seconds = 6f, output = ItemCatalog.StoneAxe, oncePerRun = true,
            requires = "woodcutting",
        },
        new Recipe
        {
            id = "fishing_spear", title = "Fishing Spear", category = WorkCategory.Tool,
            description = "A tool for your own hands",
            itemCosts = Kit2_1,
            seconds = 6f, output = ItemCatalog.FishingSpear, oncePerRun = true,
            requires = "foraging",
        },
        new Recipe
        {
            id = "stone_pick", title = "Stone Pick", category = WorkCategory.Tool,
            description = "A tool for your own hands",
            itemCosts = Kit2_2,
            seconds = 8f, output = ItemCatalog.StonePick, oncePerRun = true,
            requires = "quarrying",
        },
        new Recipe
        {
            id = "mallet", title = "Mallet", category = WorkCategory.Tool,
            description = "A tool for your own hands",
            itemCosts = Kit3_1,
            seconds = 8f, output = ItemCatalog.Mallet, oncePerRun = true,
            requires = "construction",
        },
        new Recipe
        {
            id = "metal_pick", title = "Metal Pick", category = WorkCategory.Tool,
            description = "A tool for your own hands",
            itemCosts = Kit2_2, stoneCost = 15,
            seconds = 12f, output = ItemCatalog.MetalPick, oncePerRun = true,
            requires = "mining",
        },
    };

    public static Recipe Find(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < All.Length; i++)
            if (All[i].id == id) return All[i];
        return null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        for (int i = 0; i < All.Length; i++) All[i].made = false;
    }
}
