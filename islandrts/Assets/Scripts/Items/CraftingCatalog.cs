using UnityEngine;

/// <summary>
/// What can be made at a station (2026-09-02; repeatable since the research
/// split of 2026-09-03). A recipe is listed once its <see cref="Recipe.requires"/>
/// research is done, queued <i>n</i> at a time, and each completion charges
/// its own costs (items from the laborer's hands and the campfire stockpile,
/// resources from the pool).
///
/// Output: equipment and materials go to the campfire stockpile whichever
/// station made them (one colony store). The player's TOOLS are not recipes
/// any more (2026-09-03): a research that teaches a job hands over its tool as
/// it completes (<see cref="ResearchCatalog.ResearchDef.tool"/>), so learning
/// and equipping are one step instead of two entries on two tabs. The
/// <see cref="Recipe.oncePerRun"/> machinery stays for future one-off crafts.
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
