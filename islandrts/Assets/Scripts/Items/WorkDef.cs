using System.Text;

/// <summary>What kind of work an entry is — a station has one speed per category.</summary>
public enum WorkCategory
{
    Tool,
    Weapon,
    Construction,
    Research,
}

/// <summary>One item cost line of a recipe or research entry.</summary>
public struct ItemCost
{
    public ItemDef item;
    public int count;
    public ItemCost(ItemDef item, int count) { this.item = item; this.count = count; }
}

/// <summary>
/// Anything a <see cref="CraftStation"/> can work on (2026-09-03): a
/// <see cref="CraftingCatalog.Recipe"/> or a <see cref="ResearchCatalog.ResearchDef"/>.
/// Both cost items (from a laborer's hands and the campfire stockpile
/// together, hands first) and/or pooled resources, and both take seconds of
/// labor at a bench. Only the output differs, which is why the queue, the
/// progress rule and the panel rows are shared.
///
/// Costs are charged on completion (<see cref="Pay"/>), never on queueing —
/// walking away, a knock-out, or a colonist spending the wood mid-craft costs
/// nothing; the entry simply waits at the bench for the missing part.
/// </summary>
public abstract class WorkDef
{
    public static readonly ItemCost[] NoItems = new ItemCost[0];

    public string id;
    public string title;
    public string description;
    public ItemCost[] itemCosts = NoItems;
    public int woodCost, foodCost, stoneCost, metalCost;
    public float seconds;

    public abstract WorkCategory Category { get; }

    private string costText;

    /// <summary>"2 Stick · 1 Stone chunk · 5 Wood" — built once, the costs never change.</summary>
    public string CostText
    {
        get
        {
            if (costText == null)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < itemCosts.Length; i++)
                {
                    if (sb.Length > 0) sb.Append(" · ");
                    sb.Append(itemCosts[i].count).Append(' ').Append(itemCosts[i].item.displayName);
                }
                AppendResource(sb, woodCost, "Wood");
                AppendResource(sb, foodCost, "Food");
                AppendResource(sb, stoneCost, "Stone");
                AppendResource(sb, metalCost, "Metal");
                if (sb.Length == 0) sb.Append("Free");
                costText = sb.ToString();
            }
            return costText;
        }
    }

    static void AppendResource(StringBuilder sb, int n, string name)
    {
        if (n <= 0) return;
        if (sb.Length > 0) sb.Append(" · ");
        sb.Append(n).Append(' ').Append(name);
    }

    public bool HasResourceCost => woodCost > 0 || foodCost > 0 || stoneCost > 0 || metalCost > 0;

    /// <summary>
    /// Can the costs be met from <paramref name="hands"/> plus <paramref name="stock"/>
    /// plus the resource pool? Either inventory may be null.
    /// </summary>
    public bool CanAfford(Inventory hands, Inventory stock)
    {
        if (FirstMissingItem(hands, stock) != null) return false;
        if (HasResourceCost)
        {
            if (ResourceManager.Instance == null) return false;
            if (!ResourceManager.Instance.CanAfford(woodCost, foodCost, stoneCost, metalCost)) return false;
        }
        return true;
    }

    /// <summary>The first item cost that is short, or null when every item is in hand or in stock.</summary>
    public ItemDef FirstMissingItem(Inventory hands, Inventory stock)
    {
        for (int i = 0; i < itemCosts.Length; i++)
        {
            int have = (hands != null ? hands.Count(itemCosts[i].item) : 0)
                     + (stock != null ? stock.Count(itemCosts[i].item) : 0);
            if (have < itemCosts[i].count) return itemCosts[i].item;
        }
        return null;
    }

    /// <summary>"2 Stick" / "15 Wood" — the first thing that is short, for a status line.</summary>
    public string MissingText(Inventory hands, Inventory stock)
    {
        for (int i = 0; i < itemCosts.Length; i++)
        {
            int have = (hands != null ? hands.Count(itemCosts[i].item) : 0)
                     + (stock != null ? stock.Count(itemCosts[i].item) : 0);
            if (have < itemCosts[i].count) return (itemCosts[i].count - have) + " " + itemCosts[i].item.displayName;
        }
        ResourceManager rm = ResourceManager.Instance;
        if (rm == null) return "resources";
        if (rm.wood < woodCost) return (woodCost - rm.wood) + " Wood";
        if (rm.food < foodCost) return (foodCost - rm.food) + " Food";
        if (rm.stone < stoneCost) return (stoneCost - rm.stone) + " Stone";
        if (rm.metal < metalCost) return (metalCost - rm.metal) + " Metal";
        return "";
    }

    /// <summary>
    /// Take the costs, hands first then stockpile. All or nothing: returns false
    /// and changes nothing when <see cref="CanAfford"/> would be false.
    /// </summary>
    public bool Pay(Inventory hands, Inventory stock)
    {
        if (!CanAfford(hands, stock)) return false;

        if (HasResourceCost)
        {
            if (!ResourceManager.Instance.SpendResources(woodCost, foodCost, stoneCost, metalCost))
                return false;
        }

        for (int i = 0; i < itemCosts.Length; i++)
        {
            int remaining = itemCosts[i].count;
            if (hands != null) remaining -= hands.Remove(itemCosts[i].item, remaining);
            if (remaining > 0 && stock != null) remaining -= stock.Remove(itemCosts[i].item, remaining);
        }
        return true;
    }
}
