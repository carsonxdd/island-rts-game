using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// A doorway in the wall line. Friendly units walk through it, enemies have to break it.
/// Gates are never placed directly - the player converts a finished wall into one.
/// </summary>
/// <remarks>
/// Unlike a wall, a gate does not carve the NavMesh, which is what lets workers path
/// through the colony's perimeter. That also means an enemy pathing through the gap would
/// simply walk past it, so a trigger volume catches enemies inside the doorway and tells
/// them to attack this gate instead.
/// Gates are deliberately half the HP of the wall they were converted from.
/// </remarks>
public class Gate : MonoBehaviour, ITargetable
{
    [Header("Gate Type")]
    public bool isStoneGate = false;  // false = wooden gate, true = stone gate

    [Header("Health")]
    public float maxHealth = 75f;  // Gates are weaker: Wooden=75, Stone=150 (half of wall HP)
    private Health healthComponent;
    public Health CachedHealth => healthComponent;

    [Header("Building Placement")]
    public float noBuildRadius = 0f;  // Gates have no no-build zone, like walls

    [Header("Trigger Settings")]
    public float triggerSize = 1.5f;  // Size of the trigger collider that catches enemies

    private Vector2Int gridPos;
    private bool registered = false;
    private BoxCollider triggerCollider;

    // Static event fired when ANY gate is destroyed — enemies subscribe for breach detection
    public static event System.Action OnAnyGateDestroyed;

    public static IReadOnlyList<Gate> ActiveList => ActiveRegistry<Gate>.List;

    void Awake() { ActiveRegistry<Gate>.Register(this); }

    void Start()
    {
        // Setup Health component
        healthComponent = GetComponent<Health>();
        if (healthComponent == null)
        {
            healthComponent = gameObject.AddComponent<Health>();
        }
        maxHealth = isStoneGate ? 150f : 75f;
        healthComponent.maxHealth = maxHealth;
        healthComponent.currentHealth = maxHealth;
        healthComponent.destroyOnDeath = true;
        healthComponent.destroyDelay = 0.5f;
        healthComponent.showHealthText = true;
        healthComponent.showObjectName = true;
        healthComponent.hideWhenFull = true;
        healthComponent.onDeath.AddListener(OnGateDestroyed);

        // Remove any NavMeshObstacle - gates should NOT block pathfinding
        // Adjacent walls carve 0.9x0.9, leaving a walkable gap at the gate
        NavMeshObstacle existingObstacle = GetComponent<NavMeshObstacle>();
        if (existingObstacle != null)
        {
            Destroy(existingObstacle);
        }

        // Setup trigger collider to catch enemies walking through
        SetupTriggerCollider();

        // Register with WallGrid (neighbors connect to gates like walls)
        gridPos = WallGrid.Instance.WorldToGrid(transform.position);
        WallGrid.Instance.Register(gridPos, this);
        registered = true;
    }

    void SetupTriggerCollider()
    {
        // Convert any existing colliders to triggers so units can pass through physically
        Collider[] existingColliders = GetComponentsInChildren<Collider>();
        foreach (Collider col in existingColliders)
        {
            col.isTrigger = true;
        }

        // Add our enemy-detection trigger collider
        triggerCollider = gameObject.AddComponent<BoxCollider>();
        triggerCollider.isTrigger = true;
        triggerCollider.size = new Vector3(triggerSize, 2f, triggerSize);
        triggerCollider.center = new Vector3(0f, 1f, 0f);
    }

    void OnTriggerEnter(Collider other)
    {
        // Check if it's an enemy - force them to attack the gate
        Enemy enemy = other.GetComponent<Enemy>();
        if (enemy != null)
        {
            enemy.ForceAttackGate(this);
        }
    }

    void OnGateDestroyed()
    {
        UnregisterFromGrid();

        // Immediately disable trigger + any residual colliders so enemies walking
        // over the dying gate during its 0.5s fade-out aren't re-forced onto it
        // by OnTriggerEnter, and can retarget freely. (Gates have no
        // NavMeshObstacle — removed in Start — so only colliders to clear.)
        Collider[] cols = GetComponentsInChildren<Collider>();
        for (int i = 0; i < cols.Length; i++) cols[i].enabled = false;

        // Notify all enemies that a gate was destroyed (breach detection)
        OnAnyGateDestroyed?.Invoke();
    }

    void OnDestroy()
    {
        ActiveRegistry<Gate>.Unregister(this);
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

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 0.3f, 0.3f);
        Gizmos.DrawWireCube(transform.position + Vector3.up, new Vector3(triggerSize, 2f, triggerSize));
    }
}
