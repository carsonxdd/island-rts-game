using UnityEngine;
using System.Collections.Generic;

public class Hut : MonoBehaviour
{
    public static IReadOnlyList<Hut> ActiveList => ActiveRegistry<Hut>.List;

    void Awake() { ActiveRegistry<Hut>.Register(this); }

    [Header("Health")]
    public float maxHealth = 100f;  // Hut health (less than campfire)
    private Health healthComponent;
    public Health CachedHealth => healthComponent;

    [Header("Building Placement")]
    public float noBuildRadius = 3.5f;  // Creates 7x7 square no-build zone (3 grid cell buffer)

    [Header("Population & Housing")]
    public int workerCapacity = 2;  // Huts provide 2 worker slots

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
            Debug.Log($"Hut: NavMeshObstacle carving enabled for pathfinding");
        }

        // Register housing capacity with PopulationManager
        if (PopulationManager.Instance != null)
        {
            PopulationManager.Instance.AddHousing(workerCapacity);
            Debug.Log($"Hut: Registered {workerCapacity} housing slots");
        }

        Debug.Log($"Hut: Initialized at {transform.position} with {maxHealth} HP and no-build radius {noBuildRadius}");
    }

    void OnHutDestroyed()
    {
        Debug.Log($"Hut: {gameObject.name} has been destroyed!");

        // Remove housing capacity from PopulationManager
        if (PopulationManager.Instance != null)
        {
            PopulationManager.Instance.RemoveHousing(workerCapacity);

            // Check if workers are now homeless
            if (PopulationManager.Instance.HasHomelessWorkers())
            {
                int homelessCount = PopulationManager.Instance.GetHomelessCount();
                Debug.LogWarning($"Hut: {homelessCount} workers are now HOMELESS! Build more huts.");
            }
        }

        // Could add visual effects, resource drops, etc. here
    }

    void OnDestroy()
    {
        ActiveRegistry<Hut>.Unregister(this);
    }

    // Visual helper in Scene view
    void OnDrawGizmosSelected()
    {
        // Draw no-build radius
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, noBuildRadius);
    }
}
