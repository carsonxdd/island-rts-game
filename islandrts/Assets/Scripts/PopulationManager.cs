using UnityEngine;

/// <summary>
/// Manages worker population and housing capacity across all buildings.
/// Similar to ResourceManager, this is a singleton that tracks housing limits.
/// </summary>
public class PopulationManager : MonoBehaviour
{
    // Singleton pattern
    public static PopulationManager Instance { get; private set; }

    [Header("Population Stats")]
    [SerializeField] private int currentWorkers = 0;  // Total workers alive
    [SerializeField] private int housingCapacity = 0;  // Total housing slots available

    [Header("Debug")]
    public bool showDebugLogs = true;

    void Awake()
    {
        // Singleton setup
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (showDebugLogs)
            Debug.Log("PopulationManager: Initialized!");
    }

    void Start()
    {
        // Recalculate housing capacity from all buildings in scene
        RecalculateHousingCapacity();
    }

    /// <summary>
    /// Recalculates total housing capacity by scanning all buildings in the scene.
    /// Call this when buildings are constructed or destroyed.
    /// </summary>
    public void RecalculateHousingCapacity()
    {
        int totalCapacity = 0;

        // Check all BaseBuilding objects (Campfire)
        BaseBuilding[] baseBuildings = FindObjectsByType<BaseBuilding>(FindObjectsSortMode.None);
        foreach (BaseBuilding building in baseBuildings)
        {
            if (building != null && building.enabled)
            {
                totalCapacity += building.workerCapacity;
            }
        }

        // Check all Hut objects (finished buildings)
        Hut[] huts = FindObjectsByType<Hut>(FindObjectsSortMode.None);
        foreach (Hut hut in huts)
        {
            if (hut != null && hut.enabled)
            {
                totalCapacity += hut.workerCapacity;
            }
        }

        housingCapacity = totalCapacity;

        if (showDebugLogs)
            Debug.Log($"PopulationManager: Recalculated housing capacity = {housingCapacity}");
    }

    /// <summary>
    /// Adds a building's housing capacity to the total.
    /// Call this when a building is constructed.
    /// </summary>
    public void AddHousing(int capacity)
    {
        housingCapacity += capacity;

        if (showDebugLogs)
            Debug.Log($"PopulationManager: Added {capacity} housing. Total capacity: {housingCapacity}");
    }

    /// <summary>
    /// Removes a building's housing capacity from the total.
    /// Call this when a building is destroyed.
    /// </summary>
    public void RemoveHousing(int capacity)
    {
        housingCapacity -= capacity;

        if (showDebugLogs)
            Debug.Log($"PopulationManager: Removed {capacity} housing. Total capacity: {housingCapacity}");
    }

    /// <summary>
    /// Checks if there is available housing for a new worker.
    /// </summary>
    public bool HasAvailableHousing()
    {
        return currentWorkers < housingCapacity;
    }

    /// <summary>
    /// Returns how many more workers can be housed.
    /// </summary>
    public int GetAvailableHousing()
    {
        return Mathf.Max(0, housingCapacity - currentWorkers);
    }

    /// <summary>
    /// Increment worker count when a new worker is spawned.
    /// </summary>
    public void AddWorker()
    {
        currentWorkers++;

        if (showDebugLogs)
            Debug.Log($"PopulationManager: Worker added. {currentWorkers}/{housingCapacity}");
    }

    /// <summary>
    /// Decrement worker count when a worker is removed/killed.
    /// </summary>
    public void RemoveWorker()
    {
        currentWorkers--;

        if (showDebugLogs)
            Debug.Log($"PopulationManager: Worker removed. {currentWorkers}/{housingCapacity}");
    }

    /// <summary>
    /// Get current worker count.
    /// </summary>
    public int GetCurrentWorkers()
    {
        return currentWorkers;
    }

    /// <summary>
    /// Get current housing capacity.
    /// </summary>
    public int GetHousingCapacity()
    {
        return housingCapacity;
    }

    /// <summary>
    /// Check if workers are homeless (more workers than housing).
    /// </summary>
    public bool HasHomelessWorkers()
    {
        return currentWorkers > housingCapacity;
    }

    /// <summary>
    /// Get number of homeless workers.
    /// </summary>
    public int GetHomelessCount()
    {
        return Mathf.Max(0, currentWorkers - housingCapacity);
    }
}
