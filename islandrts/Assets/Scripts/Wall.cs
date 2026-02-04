using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

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

    // Static event fired when ANY wall is destroyed — enemies subscribe for breach detection
    public static event System.Action OnAnyWallDestroyed;

    // Static registry for O(1) lookup instead of FindObjectsByType
    private static readonly List<Wall> activeList = new List<Wall>();
    public static IReadOnlyList<Wall> ActiveList => activeList;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { activeList.Clear(); }

    void Awake() { activeList.Add(this); }

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
        // Notify all enemies that a wall was destroyed (breach detection)
        OnAnyWallDestroyed?.Invoke();
    }

    void OnDestroy()
    {
        activeList.Remove(this);
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

    /// <summary>
    /// Convert this wall into a gate. Costs 5 wood.
    /// Called by BuildPlacement when player presses G on a wall.
    /// </summary>
    public void UpgradeToGate()
    {
        // Record state before destruction
        Vector3 pos = transform.position;
        Quaternion rot = transform.rotation;
        bool stone = isStoneWall;
        float healthRatio = healthComponent != null ? healthComponent.currentHealth / healthComponent.maxHealth : 1f;

        // Unregister this wall from the grid before spawning gate
        UnregisterFromGrid();

        // Create gate GameObject at same position
        GameObject gateObj = new GameObject(stone ? "StoneGate" : "WoodenGate");
        gateObj.transform.position = pos;
        gateObj.transform.rotation = rot;
        gateObj.layer = gameObject.layer;

        // Add required components
        Gate gate = gateObj.AddComponent<Gate>();
        gate.isStoneGate = stone;
        gate.maxHealth = stone ? 150f : 75f;

        // Add NavMeshObstacle for gate (Gate.Start() will configure carving,
        // but we add it here so it's ready before Start runs)
        NavMeshObstacle gateObstacle = gateObj.AddComponent<NavMeshObstacle>();
        gateObstacle.shape = NavMeshObstacleShape.Box;
        gateObstacle.size = new Vector3(1f, 2f, 1f);
        gateObstacle.center = new Vector3(0f, 1f, 0f);
        gateObstacle.carving = true;
        gateObstacle.carveOnlyStationary = true;
        gateObstacle.carvingTimeToStationary = 0.1f;

        // Add WallConnector for mesh generation
        gateObj.AddComponent<MeshFilter>();
        MeshRenderer mr = gateObj.AddComponent<MeshRenderer>();
        mr.material = new Material(Shader.Find("Sprites/Default"));
        mr.material.color = stone ? new Color(0.5f, 0.6f, 0.5f) : new Color(0.45f, 0.35f, 0.2f);
        gateObj.AddComponent<WallConnector>();

        // Set health proportionally after Gate.Start() runs (defer with a frame delay)
        // Gate.Start() initializes health, so we need to apply ratio after
        Gate gateComponent = gateObj.GetComponent<Gate>();
        gateComponent.StartCoroutine(ApplyHealthRatioNextFrame(gateObj, healthRatio));

        Debug.Log($"Wall: Upgraded to {(stone ? "Stone" : "Wooden")} Gate at {pos}");

        // Destroy original wall
        Destroy(gameObject);
    }

    private static System.Collections.IEnumerator ApplyHealthRatioNextFrame(GameObject gateObj, float ratio)
    {
        yield return null; // Wait one frame for Start() to run
        if (gateObj != null)
        {
            Health h = gateObj.GetComponent<Health>();
            if (h != null)
            {
                h.currentHealth = h.maxHealth * ratio;
            }
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
