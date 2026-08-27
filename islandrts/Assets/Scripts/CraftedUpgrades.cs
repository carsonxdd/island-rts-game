using UnityEngine;

/// <summary>
/// Global one-time crafted upgrades (Workshop building). Static multipliers are
/// read at the point of effect — gathering (GatherExecutor), construction
/// (ConstructionSite), warrior attacks (EngageEnemyExecutor) — so an upgrade
/// applies to every existing and future unit the moment its craft completes.
///
/// Statics reset on play via [RuntimeInitializeOnLoadMethod] (domain-reload
/// safety, same pattern as the other static events in this codebase).
/// </summary>
public static class CraftedUpgrades
{
    public class Recipe
    {
        public string id;
        public string title;
        public string description;
        public int woodCost;
        public int foodCost;
        public int stoneCost;
        public float craftSeconds;
        public bool crafted;
        public System.Action apply;
    }

    public static float GatherRateMult { get; private set; } = 1f;
    public static float BuildSpeedMult { get; private set; } = 1f;
    public static float WarriorDamageMult { get; private set; } = 1f;

    public static readonly Recipe[] Recipes =
    {
        new Recipe
        {
            id = "sharp_tools", title = "Sharpened Tools",
            description = "+30% gather speed for all workers",
            woodCost = 25, foodCost = 0, stoneCost = 15, craftSeconds = 10f,
            apply = () => GatherRateMult = 1.3f
        },
        new Recipe
        {
            id = "scaffolds", title = "Sturdy Scaffolds",
            description = "+50% construction speed",
            woodCost = 30, foodCost = 0, stoneCost = 10, craftSeconds = 10f,
            apply = () => BuildSpeedMult = 1.5f
        },
        new Recipe
        {
            id = "forged_blades", title = "Forged Blades",
            description = "+30% warrior damage",
            woodCost = 20, foodCost = 10, stoneCost = 25, craftSeconds = 14f,
            apply = () => WarriorDamageMult = 1.3f
        },
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        GatherRateMult = 1f;
        BuildSpeedMult = 1f;
        WarriorDamageMult = 1f;
        for (int i = 0; i < Recipes.Length; i++) Recipes[i].crafted = false;
    }
}
