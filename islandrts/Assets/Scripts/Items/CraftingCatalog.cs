using UnityEngine;

/// <summary>
/// What can be made at the campfire (2026-09-02), as static definitions like
/// <see cref="ItemCatalog"/>. Each recipe costs items (from the character's
/// inventory and the campfire stockpile together, inventory first) and/or
/// pooled resources, takes a few seconds of the character standing at the
/// fire, produces a tool into the character's hands, and grants
/// <see cref="Unlocks"/> on the first completion.
///
/// The Workshop's global upgrades are still <c>CraftedUpgrades.Recipes</c>;
/// folding them in under <see cref="Station.Workshop"/> is a later slice.
/// </summary>
public static class CraftingCatalog
{
    public enum Station { Campfire, Workshop }

    public struct ItemCost
    {
        public ItemDef item;
        public int count;
        public ItemCost(ItemDef item, int count) { this.item = item; this.count = count; }
    }

    public sealed class Recipe
    {
        public string id;
        public string title;
        public string description;
        public Station station;
        public ItemCost[] itemCosts;
        public int woodCost, foodCost, stoneCost, metalCost;
        public float seconds;
        public ItemDef output;
        public int outputCount = 1;
        public Unlocks.Kind[] unlocks;
        public bool crafted;

        private string costText;

        /// <summary>"2 Stick · 1 Stone chunk · 5 Wood" — built once, the costs never change.</summary>
        public string CostText
        {
            get
            {
                if (costText == null)
                {
                    var sb = new System.Text.StringBuilder();
                    for (int i = 0; i < itemCosts.Length; i++)
                    {
                        if (sb.Length > 0) sb.Append(" · ");
                        sb.Append(itemCosts[i].count).Append(' ').Append(itemCosts[i].item.displayName);
                    }
                    AppendResource(sb, woodCost, "Wood");
                    AppendResource(sb, foodCost, "Food");
                    AppendResource(sb, stoneCost, "Stone");
                    AppendResource(sb, metalCost, "Metal");
                    costText = sb.ToString();
                }
                return costText;
            }
        }

        static void AppendResource(System.Text.StringBuilder sb, int n, string name)
        {
            if (n <= 0) return;
            if (sb.Length > 0) sb.Append(" · ");
            sb.Append(n).Append(' ').Append(name);
        }
    }

    public static readonly Recipe[] CampfireRecipes =
    {
        new Recipe
        {
            id = "stone_axe", title = "Stone Axe", station = Station.Campfire,
            description = "Colonists can be sent to cut wood",
            itemCosts = new[] { new ItemCost(ItemCatalog.Stick, 2), new ItemCost(ItemCatalog.StoneChunk, 1) },
            seconds = 6f, output = ItemCatalog.StoneAxe,
            unlocks = new[] { Unlocks.Kind.WoodJob },
        },
        new Recipe
        {
            id = "fishing_spear", title = "Fishing Spear", station = Station.Campfire,
            description = "Colonists can be sent to forage food",
            itemCosts = new[] { new ItemCost(ItemCatalog.Stick, 2), new ItemCost(ItemCatalog.StoneChunk, 1) },
            seconds = 6f, output = ItemCatalog.FishingSpear,
            unlocks = new[] { Unlocks.Kind.FoodJob },
        },
        new Recipe
        {
            id = "stone_pick", title = "Stone Pick", station = Station.Campfire,
            description = "Colonists can be sent to quarry stone",
            itemCosts = new[] { new ItemCost(ItemCatalog.Stick, 2), new ItemCost(ItemCatalog.StoneChunk, 2) },
            seconds = 8f, output = ItemCatalog.StonePick,
            unlocks = new[] { Unlocks.Kind.StoneJob },
        },
        new Recipe
        {
            id = "mallet", title = "Mallet", station = Station.Campfire,
            description = "Opens build mode; idle colonists build and repair",
            itemCosts = new[] { new ItemCost(ItemCatalog.Stick, 3), new ItemCost(ItemCatalog.StoneChunk, 1) },
            seconds = 8f, output = ItemCatalog.Mallet,
            unlocks = new[] { Unlocks.Kind.Construction },
        },
        new Recipe
        {
            id = "wooden_spear", title = "Wooden Spear", station = Station.Campfire,
            description = "Idle colonists can be armed as warriors",
            itemCosts = new[] { new ItemCost(ItemCatalog.Stick, 3), new ItemCost(ItemCatalog.StoneChunk, 1) },
            woodCost = 5,
            seconds = 10f, output = ItemCatalog.WoodenSpear,
            unlocks = new[] { Unlocks.Kind.Militia },
        },
        new Recipe
        {
            id = "metal_pick", title = "Metal Pick", station = Station.Campfire,
            description = "Colonists can be sent to mine ore",
            itemCosts = new[] { new ItemCost(ItemCatalog.Stick, 2), new ItemCost(ItemCatalog.StoneChunk, 2) },
            stoneCost = 15,
            seconds = 12f, output = ItemCatalog.MetalPick,
            unlocks = new[] { Unlocks.Kind.MetalJob },
        },
    };

    public static Recipe Find(string id)
    {
        for (int i = 0; i < CampfireRecipes.Length; i++)
            if (CampfireRecipes[i].id == id) return CampfireRecipes[i];
        return null;
    }

    /// <summary>
    /// Can the costs be met from <paramref name="hands"/> plus <paramref name="stock"/>
    /// plus the resource pool? Either inventory may be null.
    /// </summary>
    public static bool CanAfford(Recipe r, Inventory hands, Inventory stock)
    {
        for (int i = 0; i < r.itemCosts.Length; i++)
        {
            int have = (hands != null ? hands.Count(r.itemCosts[i].item) : 0)
                     + (stock != null ? stock.Count(r.itemCosts[i].item) : 0);
            if (have < r.itemCosts[i].count) return false;
        }

        if (r.woodCost > 0 || r.foodCost > 0 || r.stoneCost > 0 || r.metalCost > 0)
        {
            if (ResourceManager.Instance == null) return false;
            if (!ResourceManager.Instance.CanAfford(r.woodCost, r.foodCost, r.stoneCost, r.metalCost)) return false;
        }
        return true;
    }

    /// <summary>
    /// Take the costs, hands first then stockpile. All or nothing: returns false
    /// and changes nothing when <see cref="CanAfford"/> would be false.
    /// </summary>
    public static bool Pay(Recipe r, Inventory hands, Inventory stock)
    {
        if (!CanAfford(r, hands, stock)) return false;

        if (r.woodCost > 0 || r.foodCost > 0 || r.stoneCost > 0 || r.metalCost > 0)
        {
            if (!ResourceManager.Instance.SpendResources(r.woodCost, r.foodCost, r.stoneCost, r.metalCost))
                return false;
        }

        for (int i = 0; i < r.itemCosts.Length; i++)
        {
            int remaining = r.itemCosts[i].count;
            if (hands != null) remaining -= hands.Remove(r.itemCosts[i].item, remaining);
            if (remaining > 0 && stock != null) remaining -= stock.Remove(r.itemCosts[i].item, remaining);
        }
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        for (int i = 0; i < CampfireRecipes.Length; i++) CampfireRecipes[i].crafted = false;
    }
}
