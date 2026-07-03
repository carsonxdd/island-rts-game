using UnityEngine;
using System.Collections.Generic;

public class BuildingDatabase : MonoBehaviour
{
    public static BuildingDatabase Instance { get; private set; }

    [Header("Building Configurations")]
    [Tooltip("Assign BuildingData ScriptableObjects here (one for each building type)")]
    public BuildingData[] buildings;

    // O(1) lookup — GetBuildingData is called from per-frame placement/UI paths
    private Dictionary<BuildingType, BuildingData> lookup;

    void Awake()
    {
        // Singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        lookup = new Dictionary<BuildingType, BuildingData>();
        foreach (BuildingData data in buildings)
        {
            if (data != null)
            {
                lookup[data.buildingType] = data;
            }
        }
    }

    /// <summary>
    /// Get building data for a specific building type
    /// </summary>
    public BuildingData GetBuildingData(BuildingType type)
    {
        if (lookup != null && lookup.TryGetValue(type, out BuildingData data))
        {
            return data;
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
