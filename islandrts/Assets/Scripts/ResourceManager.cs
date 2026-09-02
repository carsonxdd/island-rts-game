using UnityEngine;

/// <summary>
/// The colony's single shared pool of wood, food, stone and metal. Everything that earns
/// or spends resources goes through this singleton.
/// </summary>
/// <remarks>
/// Deliberately has no DontDestroyOnLoad: this is a single-scene game, and surviving a
/// scene reload would carry a finished run's resources into the next one.
/// Spending is always check-then-subtract in a single call - the Spend methods return
/// false and change nothing when the colony cannot afford the cost, so no caller ever has
/// to undo a partial spend.
///
/// Metal (2026-09-01) is gathered from ore nodes by Metal workers. Nothing costs it yet;
/// the three-cost overloads of CanAfford / SpendResources stay so every existing caller
/// keeps compiling, and the four-cost overloads are what new content should use.
/// </remarks>
public class ResourceManager : MonoBehaviour
{
    public static ResourceManager Instance { get; private set; }

    // Inspector defaults for a new colony, before the difficulty multiplier is applied.
    [Header("Starting Resources")]
    public int startingWood = 100;
    public int startingFood = 50;
    public int startingStone = 0;
    public int startingMetal = 0;

    // The live pool. Public because the debug menu and the balance sweep write it directly.
    [Header("Current Resources")]
    public int wood = 0;
    public int food = 0;
    public int stone = 0;
    public int metal = 0;

    void Awake()
    {
        // Singleton setup - ensure only one exists
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"ResourceManager: Duplicate found! Destroying {gameObject.name}. Keeping {Instance.gameObject.name}");
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // Initialize resources, scaled by the run's difficulty. Applied here
        // rather than pushed later because several systems read the pool in
        // their own Start — a colony that briefly had the unscaled amount could
        // afford a building it should not have.
        float scale = Difficulty.StartingResourceMultiplier;
        wood = Mathf.RoundToInt(startingWood * scale);
        food = Mathf.RoundToInt(startingFood * scale);
        stone = Mathf.RoundToInt(startingStone * scale);
        metal = Mathf.RoundToInt(startingMetal * scale);

        Debug.Log($"ResourceManager: Initialized with Wood={wood}, Food={food}, Stone={stone}, Metal={metal}");
    }

    void OnDestroy()
    {
        // Clear Instance if this was the active one
        if (Instance == this)
        {
            Debug.LogWarning("ResourceManager: Main instance is being destroyed! This should not happen during gameplay!");
            Instance = null;
        }
    }

    /// <summary>Deposits gathered resources. Uncapped by design.</summary>
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

    /// <summary>Spends wood if the colony has it; returns false and changes nothing otherwise.</summary>
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

    /// <summary>True if all costs can be paid. For enabling UI - SpendResources does
    /// its own check, so there is no need to call this first.</summary>
    public bool CanAfford(int woodCost, int foodCost, int stoneCost)
    {
        return CanAfford(woodCost, foodCost, stoneCost, 0);
    }

    public bool CanAfford(int woodCost, int foodCost, int stoneCost, int metalCost)
    {
        return wood >= woodCost && food >= foodCost && stone >= stoneCost && metal >= metalCost;
    }

    /// <summary>
    /// Pays a full building cost. All or nothing: returns false and leaves the pool
    /// untouched unless every one of the costs is affordable.
    /// </summary>
    public bool SpendResources(int woodCost, int foodCost, int stoneCost)
    {
        return SpendResources(woodCost, foodCost, stoneCost, 0);
    }

    public bool SpendResources(int woodCost, int foodCost, int stoneCost, int metalCost)
    {
        if (!CanAfford(woodCost, foodCost, stoneCost, metalCost))
        {
            return false;
        }

        wood -= woodCost;
        food -= foodCost;
        stone -= stoneCost;
        metal -= metalCost;

        return true;
    }

    // Get current resources (for UI)
    public int GetWood() => wood;
    public int GetFood() => food;
    public int GetStone() => stone;
    public int GetMetal() => metal;
}
