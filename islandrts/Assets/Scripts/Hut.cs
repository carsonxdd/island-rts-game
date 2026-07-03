using UnityEngine;
using System.Collections.Generic;

public class Hut : MonoBehaviour
{
    public static IReadOnlyList<Hut> ActiveList => ActiveRegistry<Hut>.List;

    // Static event: fires when any hut dies. Enemies subscribe to ForceReeval
    // so they retarget immediately instead of waiting ~0.25s for the next brain tick.
    public static event System.Action OnAnyHutDestroyed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { OnAnyHutDestroyed = null; }

    void Awake() { ActiveRegistry<Hut>.Register(this); }

    [Header("Health")]
    public float maxHealth = 100f;  // Hut health (less than campfire)
    private Health healthComponent;
    public Health CachedHealth => healthComponent;

    [Header("Building Placement")]
    public float noBuildRadius = 3.5f;  // Creates 7x7 square no-build zone (3 grid cell buffer)

    [Header("Population & Housing")]
    public int workerCapacity = 2;  // Huts provide 2 worker slots

    private bool housingReleased = false;  // Guards against double RemoveHousing (death + destroy)

    void Start()
    {
        // Setup Health component
        healthComponent = GetComponent<Health>();
        if (healthComponent == null)
        {
            healthComponent = gameObject.AddComponent<Health>();
        }
        healthComponent.maxHealth = maxHealth;
        healthComponent.currentHealth = maxHealth;
        healthComponent.destroyOnDeath = true;  // Huts are destroyed when killed
        healthComponent.destroyDelay = 1f;  // Small delay before destruction
        healthComponent.showHealthText = true;
        healthComponent.showObjectName = true;
        healthComponent.hideWhenFull = true;
        healthComponent.onDeath.AddListener(OnHutDestroyed);

        // Enable NavMeshObstacle carving so workers path around the hut
        UnityEngine.AI.NavMeshObstacle obstacle = GetComponent<UnityEngine.AI.NavMeshObstacle>();
        if (obstacle != null)
        {
            obstacle.carving = true;  // Carve NavMesh so agents path around
            obstacle.carveOnlyStationary = true;  // Only carve when not moving (performance)
        }

        // Register housing capacity with PopulationManager
        if (PopulationManager.Instance != null)
        {
            PopulationManager.Instance.AddHousing(workerCapacity);
        }
    }

    void OnHutDestroyed()
    {
        // Immediately release the NavMesh carve and disable colliders so stacked
        // enemies can path out during the 1s fade-out instead of being trapped
        // inside the corpse's carved footprint. Fixes 3-4s retarget freeze.
        UnityEngine.AI.NavMeshObstacle obstacle = GetComponent<UnityEngine.AI.NavMeshObstacle>();
        if (obstacle != null) obstacle.enabled = false;
        Collider[] cols = GetComponentsInChildren<Collider>();
        for (int i = 0; i < cols.Length; i++) cols[i].enabled = false;

        // Notify enemies so they retarget instantly (destroyDelay keeps this object
        // in ActiveList for ~1s, but FindNearestBuilding already filters by IsAlive).
        OnAnyHutDestroyed?.Invoke();

        ReleaseHousing();

        // Check if workers are now homeless
        if (PopulationManager.Instance != null && PopulationManager.Instance.HasHomelessWorkers())
        {
            int homelessCount = PopulationManager.Instance.GetHomelessCount();
            Debug.LogWarning($"Hut: {homelessCount} workers are now HOMELESS! Build more huts.");
        }

        // Could add visual effects, resource drops, etc. here
    }

    /// <summary>
    /// Removes this hut's housing from the PopulationManager exactly once,
    /// whether the hut died in combat (OnHutDestroyed) or was demolished /
    /// destroyed directly (OnDestroy).
    /// </summary>
    void ReleaseHousing()
    {
        if (housingReleased) return;
        housingReleased = true;

        if (PopulationManager.Instance != null)
        {
            PopulationManager.Instance.RemoveHousing(workerCapacity);
        }
    }

    void OnDestroy()
    {
        ActiveRegistry<Hut>.Unregister(this);
        ReleaseHousing();
    }

    // Visual helper in Scene view
    void OnDrawGizmosSelected()
    {
        // Draw no-build radius
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, noBuildRadius);
    }
}
