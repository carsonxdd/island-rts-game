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

    void Awake()
    {
        // Singleton setup
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    // Housing capacity is owned entirely by the buildings: every BaseBuilding/Hut
    // calls AddHousing in its Start and RemoveHousing on destruction. No scene
    // rescan here — a Start-order-dependent recalculation used to double-count.

    /// <summary>
    /// Adds a building's housing capacity to the total.
    /// Call this when a building is constructed.
    /// </summary>
    public void AddHousing(int capacity)
    {
        housingCapacity += capacity;
    }

    /// <summary>
    /// Removes a building's housing capacity from the total.
    /// Call this when a building is destroyed.
    /// </summary>
    public void RemoveHousing(int capacity)
    {
        housingCapacity -= capacity;
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
    }

    /// <summary>
    /// Decrement worker count when a worker is removed/killed.
    /// </summary>
    public void RemoveWorker()
    {
        currentWorkers--;
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
