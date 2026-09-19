/// <summary>
/// One faction's pool of wood, food, stone and metal (2026-09-09, lap step 1
/// commit 2 — moved off the <c>ResourceManager</c> singleton so a rival colony
/// can have its own). Plain class, no scene object; reach it through
/// <see cref="Faction.Resources"/> (<c>Factions.Player.Resources</c> for the
/// player's, <c>bb.faction.Resources</c> inside an executor).
/// </summary>
/// <remarks>
/// Spending is check-then-subtract in one call: the Spend methods return false
/// and change nothing when the pool cannot cover the cost, so no caller ever
/// undoes a partial spend. Deposits are uncapped by design. The three-cost
/// overloads stay for the wall callers; new content uses the four-cost ones.
/// </remarks>
public sealed class ResourcePool
{
    // Public fields, not properties: the debug menu, the sim and the UI read and write them directly.
    public int wood;
    public int food;
    public int stone;
    public int metal;

    public void Set(int wood, int food, int stone, int metal)
    {
        this.wood = wood;
        this.food = food;
        this.stone = stone;
        this.metal = metal;
    }

    public void AddWood(int amount) { wood += amount; }
    public void AddFood(int amount) { food += amount; }
    public void AddStone(int amount) { stone += amount; }
    public void AddMetal(int amount) { metal += amount; }

    /// <summary>Deposit by type — what the delivery executor calls.</summary>
    public void Add(ResourceNode.ResourceType type, int amount)
    {
        switch (type)
        {
            case ResourceNode.ResourceType.Wood: wood += amount; break;
            case ResourceNode.ResourceType.Food: food += amount; break;
            case ResourceNode.ResourceType.Stone: stone += amount; break;
            case ResourceNode.ResourceType.Metal: metal += amount; break;
        }
    }

    public int Get(ResourceNode.ResourceType type)
    {
        switch (type)
        {
            case ResourceNode.ResourceType.Wood: return wood;
            case ResourceNode.ResourceType.Food: return food;
            case ResourceNode.ResourceType.Stone: return stone;
            case ResourceNode.ResourceType.Metal: return metal;
        }
        return 0;
    }

    public bool SpendWood(int amount)
    {
        if (wood < amount) return false;
        wood -= amount;
        return true;
    }

    public bool SpendFood(int amount)
    {
        if (food < amount) return false;
        food -= amount;
        return true;
    }

    public bool SpendStone(int amount)
    {
        if (stone < amount) return false;
        stone -= amount;
        return true;
    }

    public bool SpendMetal(int amount)
    {
        if (metal < amount) return false;
        metal -= amount;
        return true;
    }

    /// <summary>True if every cost can be paid. For enabling UI — the Spend methods check for themselves.</summary>
    public bool CanAfford(int woodCost, int foodCost, int stoneCost)
    {
        return CanAfford(woodCost, foodCost, stoneCost, 0);
    }

    public bool CanAfford(int woodCost, int foodCost, int stoneCost, int metalCost)
    {
        return wood >= woodCost && food >= foodCost && stone >= stoneCost && metal >= metalCost;
    }

    /// <summary>Pays a full cost. All or nothing: false and untouched unless every part is affordable.</summary>
    public bool SpendResources(int woodCost, int foodCost, int stoneCost)
    {
        return SpendResources(woodCost, foodCost, stoneCost, 0);
    }

    public bool SpendResources(int woodCost, int foodCost, int stoneCost, int metalCost)
    {
        if (!CanAfford(woodCost, foodCost, stoneCost, metalCost)) return false;
        wood -= woodCost;
        food -= foodCost;
        stone -= stoneCost;
        metal -= metalCost;
        return true;
    }

    public int GetWood() => wood;
    public int GetFood() => food;
    public int GetStone() => stone;
    public int GetMetal() => metal;
}
