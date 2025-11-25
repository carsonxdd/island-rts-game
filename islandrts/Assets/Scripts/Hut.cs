using UnityEngine;

public class Hut : MonoBehaviour
{
    [Header("Health")]
    public float maxHealth = 100f;  // Hut health (less than campfire)
    private Health healthComponent;

    [Header("Building Placement")]
    public float noBuildRadius = 2.5f;  // Creates 5x5 square no-build zone (1 grid cell buffer)

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
        healthComponent.onDeath.AddListener(OnHutDestroyed);

        // Disable NavMeshObstacle carving so enemies can attack from all sides
        UnityEngine.AI.NavMeshObstacle obstacle = GetComponent<UnityEngine.AI.NavMeshObstacle>();
        if (obstacle != null)
        {
            obstacle.carving = false;  // Don't carve NavMesh - let enemies get close
            Debug.Log($"Hut: NavMeshObstacle carving disabled for combat");
        }

        Debug.Log($"Hut: Initialized at {transform.position} with {maxHealth} HP and no-build radius {noBuildRadius}");
    }

    void OnHutDestroyed()
    {
        Debug.Log($"Hut: {gameObject.name} has been destroyed!");
        // Could add visual effects, resource drops, etc. here
    }

    // Visual helper in Scene view
    void OnDrawGizmosSelected()
    {
        // Draw no-build radius
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, noBuildRadius);
    }
}
