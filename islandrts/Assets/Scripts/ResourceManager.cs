using UnityEngine;

/// <summary>
/// The colony's single shared pool of wood, food and stone. Everything that earns or
/// spends resources goes through this singleton.
/// </summary>
/// <remarks>
/// Deliberately has no DontDestroyOnLoad: this is a single-scene game, and surviving a
/// scene reload would carry a finished run's resources into the next one.
/// Spending is always check-then-subtract in a single call - the Spend methods return
/// false and change nothing when the colony cannot afford the cost, so no caller ever has
/// to undo a partial spend.
/// </remarks>
public class ResourceManager : MonoBehaviour
{
    public static ResourceManager Instance { get; private set; }

    // Inspector defaults for a new colony, before the difficulty multiplier is applied.
    [Header("Starting Resources")]
    public int startingWood = 100;
    public int startingFood = 50;
    public int startingStone = 0;

    // The live pool. Public because the debug menu and the balance sweep write it directly.
    [Header("Current Resources")]
    public int wood = 0;
    public int food = 0;
    public int stone = 0;

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

        Debug.Log($"ResourceManager: Initialized with Wood={wood}, Food={food}, Stone={stone}");
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
    public void AddWood(int amount)
    {
        wood += amount;
    }

    public void AddFood(int amount)
    {
        food += amount;
    }

    public void AddStone(int amount)
    {
        stone += amount;
    }

    /// <summary>Spends wood if the colony has it; returns false and changes nothing otherwise.</summary>
    public bool SpendWood(int amount)
    {
        if (wood >= amount)
        {
            wood -= amount;
            return true;
        }
        else
        {
            return false;
        }
    }

    public bool SpendFood(int amount)
    {
        if (food >= amount)
        {
            food -= amount;
            return true;
        }
        else
        {
            return false;
        }
    }

    public bool SpendStone(int amount)
    {
        if (stone >= amount)
        {
            stone -= amount;
            return true;
        }
        else
        {
            return false;
        }
    }

    /// <summary>True if all three costs can be paid. For enabling UI - SpendResources does
    /// its own check, so there is no need to call this first.</summary>
    public bool CanAfford(int woodCost, int foodCost, int stoneCost)
    {
        return wood >= woodCost && food >= foodCost && stone >= stoneCost;
    }

    /// <summary>
    /// Pays a full building cost. All or nothing: returns false and leaves the pool
    /// untouched unless every one of the three costs is affordable.
    /// </summary>
    public bool SpendResources(int woodCost, int foodCost, int stoneCost)
    {
        if (!CanAfford(woodCost, foodCost, stoneCost))
        {
            return false;
        }

        wood -= woodCost;
        food -= foodCost;
        stone -= stoneCost;

        return true;
    }

    // Get current resources (for UI)
    public int GetWood() => wood;
    public int GetFood() => food;
    public int GetStone() => stone;
}
