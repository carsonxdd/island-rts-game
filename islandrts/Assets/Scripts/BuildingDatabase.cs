using UnityEngine;

public class BuildingDatabase : MonoBehaviour
{
    public static BuildingDatabase Instance { get; private set; }

    [Header("Building Configurations")]
    [Tooltip("Assign BuildingData ScriptableObjects here (one for each building type)")]
    public BuildingData[] buildings;

    void Awake()
    {
        // Singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        Debug.Log($"BuildingDatabase: Initialized with {buildings.Length} building types");
    }

    /// <summary>
    /// Get building data for a specific building type
    /// </summary>
    public BuildingData GetBuildingData(BuildingType type)
    {
        foreach (BuildingData data in buildings)
        {
            if (data != null && data.buildingType == type)
            {
                return data;
            }
        }

        Debug.LogError($"BuildingDatabase: No BuildingData found for type {type}!");
        return null;
    }

    /// <summary>
    /// Check if player can afford a specific building type
    /// </summary>
    public bool CanAfford(BuildingType type)
    {
        BuildingData data = GetBuildingData(type);
        if (data == null) return false;

        return ResourceManager.Instance.CanAfford(data.woodCost, data.foodCost, data.stoneCost);
    }
}
