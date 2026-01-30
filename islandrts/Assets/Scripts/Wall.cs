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
    public float noBuildRadius = 0f;  // Walls have no no-build zone - they can be placed adjacent

    private Vector2Int gridPos;
    private bool registered = false;

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
        }

        // Register with WallGrid
        gridPos = WallGrid.Instance.WorldToGrid(transform.position);
        WallGrid.Instance.Register(gridPos, this);
        registered = true;
    }

    void OnWallDestroyed()
    {
        UnregisterFromGrid();
    }

    void OnDestroy()
    {
        UnregisterFromGrid();
    }

    private void UnregisterFromGrid()
    {
        if (registered && WallGrid.Instance != null)
        {
            WallGrid.Instance.Unregister(gridPos);
            registered = false;
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
