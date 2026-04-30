using UnityEngine;

// Singleton pattern - only one ResourceManager exists
public class ResourceManager : MonoBehaviour
{
    // Singleton instance
    public static ResourceManager Instance { get; private set; }

    [Header("Starting Resources")]
    public int startingWood = 100;
    public int startingFood = 50;
    public int startingStone = 0;

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

        // Initialize resources
        wood = startingWood;
        food = startingFood;
        stone = startingStone;

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

    // Add resources
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

    // Remove resources (returns true if successful, false if not enough)
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

    // Check if player can afford a cost
    public bool CanAfford(int woodCost, int foodCost, int stoneCost)
    {
        return wood >= woodCost && food >= foodCost && stone >= stoneCost;
    }

    // Spend multiple resources at once (returns true if successful)
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
