using UnityEngine;
using UnityEngine.AI;

public class Watchtower : MonoBehaviour
{
    [Header("Health")]
    public float maxHealth = 200f;
    private Health healthComponent;

    [Header("Building Placement")]
    public float noBuildRadius = 3.0f;

    [Header("Detection (Future Phase 6B)")]
    [Tooltip("Early warning system - reveals enemies at greater distance")]
    public float detectionRadius = 20f;

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
        healthComponent.destroyOnDeath = true;
        healthComponent.destroyDelay = 1f;
        healthComponent.showHealthText = true;
        healthComponent.showObjectName = true;
        healthComponent.onDeath.AddListener(OnWatchtowerDestroyed);

        // Disable NavMeshObstacle carving (like other buildings - enemies can surround)
        NavMeshObstacle obstacle = GetComponent<NavMeshObstacle>();
        if (obstacle != null)
        {
            obstacle.carving = false;  // Enemies can surround it from all sides
            Debug.Log("Watchtower: NavMeshObstacle carving disabled for combat");
        }

        Debug.Log($"Watchtower: Initialized with {maxHealth} HP and {detectionRadius}m detection radius at {transform.position}");
    }

    void OnWatchtowerDestroyed()
    {
        Debug.Log($"Watchtower: Destroyed at {transform.position}");

        // Play destruction sound if AudioManager exists
        if (AudioManager.Instance != null)
        {
            // AudioManager.Instance.PlayWatchtowerDestroyed();  // TODO: Add sound
        }
    }

    // Visual feedback in Scene view
    void OnDrawGizmosSelected()
    {
        // Draw no-build radius
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, noBuildRadius);

        // Draw detection radius (future feature visualization)
        Gizmos.color = new Color(1f, 1f, 0f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
}
