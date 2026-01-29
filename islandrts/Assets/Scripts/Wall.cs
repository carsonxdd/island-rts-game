using UnityEngine;
using UnityEngine.AI;

public class Wall : MonoBehaviour
{
    [Header("Wall Type")]
    public bool isStoneWall = false;  // false = wooden wall, true = stone wall

    [Header("Health")]
    public float maxHealth = 150f;  // Wooden=150, Stone=300
    private Health healthComponent;

    [Header("Building Placement")]
    public float noBuildRadius = 1.5f;  // Walls have smaller no-build zones

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
        healthComponent.destroyDelay = 0.5f;
        healthComponent.showHealthText = true;
        healthComponent.showObjectName = true;
        healthComponent.onDeath.AddListener(OnWallDestroyed);

        // CRITICAL: Enable NavMeshObstacle carving for walls to block enemy pathfinding
        NavMeshObstacle obstacle = GetComponent<NavMeshObstacle>();
        if (obstacle != null)
        {
            obstacle.carving = true;  // FORCE ENEMIES TO ATTACK WALLS
            obstacle.carveOnlyStationary = true;
            obstacle.carvingTimeToStationary = 0.1f;
            Debug.Log($"Wall: NavMeshObstacle carving ENABLED - enemies must destroy to pass through");
        }
        else
        {
            Debug.LogWarning("Wall: No NavMeshObstacle component found! Enemies will path through walls.");
        }

        Debug.Log($"Wall: Initialized {(isStoneWall ? "Stone" : "Wooden")} wall with {maxHealth} HP at {transform.position}");
    }

    void OnWallDestroyed()
    {
        Debug.Log($"Wall: {(isStoneWall ? "Stone" : "Wooden")} wall destroyed at {transform.position}");

        // Play destruction sound if AudioManager exists
        if (AudioManager.Instance != null)
        {
            // AudioManager.Instance.PlayWallDestroyed();  // TODO: Add sound
        }

        // Notify adjacent walls to update connections
        WallConnector connector = GetComponent<WallConnector>();
        if (connector != null)
        {
            connector.NotifyAdjacentWalls();
        }
    }

    // Visual feedback in Scene view
    void OnDrawGizmosSelected()
    {
        // Draw no-build radius
        Gizmos.color = new Color(0.5f, 0.5f, 1f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, noBuildRadius);
    }
}
