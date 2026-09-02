using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// The campfire: the colony's heart and the game's single most important object. It spawns
/// and assigns workers, recruits warriors, provides the first housing, is where workers
/// deliver resources and warriors heal, and losing it ends the run.
/// </summary>
/// <remarks>
/// Worker bookkeeping has exactly one owner. A worker leaves the colony through
/// Worker.OnDestroy calling NotifyWorkerRemoved, which drops it from the roster, decrements
/// the right job counter and frees its population slot. Roster membership is the
/// idempotence guard, so it is safe to call more than once - but nothing else may
/// decrement those counters, or a death that is also a demolition counts twice.
///
/// There is no campfire in the scene at startup: the opening sequence spawns it where the
/// player places it, so anything that needs the campfire must poll BaseBuilding.ActiveList
/// or listen for GameStartController.OnColonyStarted rather than caching it in Start.
/// </remarks>
public class BaseBuilding : MonoBehaviour, ITargetable, IHousing
{
    public static IReadOnlyList<BaseBuilding> ActiveList => ActiveRegistry<BaseBuilding>.List;

    // IHousing — the campfire is the colony's first housing (the starting crew's slots)
    public int HousingCapacity => workerCapacity;
    public bool HousingAlive => !housingReleased && (healthComponent == null || healthComponent.IsAlive);
    public Collider HousingCollider => housingCollider;
    private Collider housingCollider;
    private bool housingReleased;

    void Awake()
    {
        ActiveRegistry<BaseBuilding>.Register(this);
        PopulationManager.EnsureExists();   // the roster must exist before Start registers housing
    }

    [Header("Health")]
    public float maxHealth = 200f;  // Campfire health
    private Health healthComponent;
    public Health CachedHealth => healthComponent;

    [Header("UI Reference")]
    public WorkerAssignmentUI workerUI;  // Drag the UI object here in Inspector

    [Header("Worker Management")]
    public GameObject workerPrefab;
    public int maxWorkers = 10;

    [Header("Population & Housing")]
    public int workerCapacity = 3;  // Campfire provides 3 worker slots (starting crew)

    // Job counts are derived from the roster, never counted — a counter that is
    // incremented in one place and decremented in another drifts the first time a
    // path is missed (Phase 6.23 found exactly that). ~20 workers, so the scan is free.
    public int woodWorkers => CountJob(ResourceNode.ResourceType.Wood);
    public int foodWorkers => CountJob(ResourceNode.ResourceType.Food);
    public int stoneWorkers => CountJob(ResourceNode.ResourceType.Stone);
    public int metalWorkers => CountJob(ResourceNode.ResourceType.Metal);

    int CountJob(ResourceNode.ResourceType type)
    {
        int n = 0;
        for (int i = 0; i < activeWorkers.Count; i++)
        {
            Worker w = activeWorkers[i];
            if (w != null && w.hasJob && w.assignedResourceType == type) n++;
        }
        return n;
    }

    [Header("Warrior Management")]
    public GameObject warriorPrefab;
    public int maxWarriors = 5;
    public int warriorCost_Wood = 10;
    public int warriorCost_Food = 15;
    public int currentWarriors = 0;

    [Header("Spawn Settings")]
    public float spawnRadius = 2f;  // How far from campfire workers spawn

    [Header("Building Placement")]
    public float noBuildRadius = 2.5f;  // Creates 5x5 square no-build zone around campfire
    [Tooltip("Visual-only radius for the red no-build border. Does not affect actual placement validation.")]
    public float visualNoBuildRadius = 2.5f;

    [Header("Hover Effect")]
    public Color normalColor = Color.white;
    public Color hoverColor = new Color(1f, 1f, 0.7f, 1f);  // Slight yellow tint

    // Track all spawned workers and warriors
    private List<Worker> activeWorkers = new List<Worker>();
    private List<Warrior> activeWarriors = new List<Warrior>();
    private Material[] buildingMaterials;  // Every material slot across every renderer
    private Color[] originalColors;        // Original color per material slot

    void Start()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        SimOverrides.Apply(this);
#endif
        // Setup Health component
        healthComponent = GetComponent<Health>();
        if (healthComponent == null)
        {
            healthComponent = gameObject.AddComponent<Health>();
        }
        healthComponent.maxHealth = maxHealth;
        healthComponent.currentHealth = maxHealth;
        healthComponent.destroyOnDeath = false;  // Don't destroy campfire, handle game over instead
        healthComponent.hideWhenFull = true;
        healthComponent.onDeath.AddListener(OnCampfireDestroyed);

        // Register the campfire as housing (the starting crew's slots)
        housingCollider = GetComponent<Collider>();
        if (PopulationManager.Instance != null)
        {
            PopulationManager.Instance.RegisterHousing(this);
        }

        // Get ALL renderers BEFORE creating health text (checks this object AND all children).
        // Collect instances EVERY material slot, not just slot 0 - the low-poly art meshes are
        // multi-submesh, so slot-0-only tinting would leave most of the building unhighlighted.
        Renderer[] buildingRenderers = GetComponentsInChildren<Renderer>();

        if (buildingRenderers.Length > 0)
        {
            buildingMaterials = RendererTint.Collect(buildingRenderers);
            originalColors = RendererTint.CaptureColors(buildingMaterials);
        }
        else
        {
            Debug.LogWarning("BaseBuilding: No Renderers found! Hover effect won't work. Make sure Campfire has a MeshRenderer component.");
        }

        // Check if we have a collider for mouse clicks
        Collider col = GetComponent<Collider>();
        if (col == null)
        {
            Debug.LogError("BaseBuilding: NO COLLIDER FOUND! Add a Box Collider or Capsule Collider to make this clickable!");
        }

        // Enable NavMeshObstacle carving so workers/units path AROUND the campfire
        // instead of trying to walk through it and getting stuck on the collider.
        // Enemies can still attack from the carve edge (attackRange 3.5 > any reasonable carve radius).
        UnityEngine.AI.NavMeshObstacle obstacle = GetComponent<UnityEngine.AI.NavMeshObstacle>();
        if (obstacle != null)
        {
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;
            obstacle.carvingTimeToStationary = 0.1f;
        }
    }

    void OnMouseEnter()
    {
        // Mouse is hovering over the building - tint every slot of every part
        RendererTint.SetColor(buildingMaterials, hoverColor);
    }

    void OnMouseExit()
    {
        // Mouse left the building - restore every slot
        RendererTint.RestoreColors(buildingMaterials, originalColors);
    }

    void OnMouseDown()
    {
        // When clicked, open worker assignment UI (the panel is code-built
        // and self-registering now, so a missing scene reference is fine)
        if (PauseController.BlockGameplayInput) return;
        WorkerAssignmentUI ui = workerUI != null ? workerUI : WorkerAssignmentUI.Instance;
        if (ui != null)
        {
            ui.OpenPanel(this);
        }
        else
        {
            Debug.LogWarning("BaseBuilding: No WorkerAssignmentUI in the scene!");
        }
    }

    // ------------------------------------------------------------------
    // Jobs — assignment moves people, it never creates them (2026-09-02)
    // ------------------------------------------------------------------

    /// <summary>
    /// Give the idle colonist nearest the fire this job. False when nobody is idle —
    /// the panel disables its + buttons on the same condition.
    /// </summary>
    public bool AssignWorker(ResourceNode.ResourceType resourceType)
    {
        if (PopulationManager.Instance == null) return false;
        Worker idle = PopulationManager.Instance.FindIdleColonist(transform.position);
        if (idle == null) return false;

        idle.SetJob(resourceType);
        return true;
    }

    /// <summary>Send one worker of this job back to the idle pool (they keep their body and their home).</summary>
    public bool UnassignWorker(ResourceNode.ResourceType resourceType)
    {
        for (int i = 0; i < activeWorkers.Count; i++)
        {
            Worker worker = activeWorkers[i];
            if (worker != null && worker.hasJob && worker.assignedResourceType == resourceType)
            {
                worker.ClearJob();
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Called from Worker.OnDestroy so every destruction path (killed by enemy,
    /// converted to a warrior, scene teardown) updates the roster and the population
    /// exactly once. Roster membership is the guard; a converted worker's old body
    /// was already dropped from the roster, so its destroy is a no-op here.
    /// </summary>
    public void NotifyWorkerRemoved(Worker worker)
    {
        if (!activeWorkers.Remove(worker))
            return;  // Already processed (or never tracked by this building)

        if (PopulationManager.Instance != null)
        {
            PopulationManager.Instance.RemoveColonist(worker);
        }
    }

    /// <summary>
    /// A new person joins the colony: jobless, homed to <paramref name="home"/>.
    /// PopulationManager calls this for every survivor that comes ashore, and the
    /// opening sequence for the castaway who settles in. Returns null if the prefab
    /// is missing.
    /// </summary>
    public Worker SpawnColonist(Vector3 position, IHousing home)
    {
        Worker worker = InstantiateColonist(position);
        if (worker == null) return null;

        if (PopulationManager.Instance != null)
        {
            PopulationManager.Instance.AddColonist(worker, home);
        }
        return worker;
    }

    /// <summary>The body only — roster bookkeeping is the caller's job (AddColonist or ReplaceUnit).</summary>
    Worker InstantiateColonist(Vector3 position)
    {
        if (workerPrefab == null)
        {
            Debug.LogError("BaseBuilding: Worker prefab not assigned!");
            return null;
        }

        GameObject workerObj = Instantiate(workerPrefab, position, Quaternion.identity);
        workerObj.name = $"Colonist_{activeWorkers.Count + 1}";

        Worker worker = workerObj.GetComponent<Worker>();
        if (worker == null)
        {
            Debug.LogError("BaseBuilding: Worker prefab doesn't have Worker component!");
            Destroy(workerObj);
            return null;
        }

        worker.hasJob = false;
        worker.baseBuilding = this;
        activeWorkers.Add(worker);
        return worker;
    }

    /// <summary>
    /// Find a valid spawn position on the NavMesh around the campfire.
    /// Tries random positions, then evenly-spaced directions, then falls back with warning.
    /// </summary>
    public Vector3 GetValidSpawnPosition()
    {
        NavMeshHit hit;

        // Try 8 random positions slightly farther out
        for (int i = 0; i < 8; i++)
        {
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float distance = spawnRadius + Random.Range(0.5f, 1.5f);

            Vector3 candidate = transform.position + new Vector3(
                Mathf.Cos(angle) * distance,
                0f,
                Mathf.Sin(angle) * distance
            );

            if (NavMesh.SamplePosition(candidate, out hit, 2f, NavMesh.AllAreas))
            {
                // hit.position IS on the NavMesh at the right height — the old
                // "+1" was a flat-world/center-pivot relic that floats (or on
                // terrain, buries) base-pivot units
                return hit.position;
            }
        }

        // Fallback: try 8 evenly-spaced directions at a farther distance
        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f * Mathf.Deg2Rad;
            float distance = spawnRadius + 2f;

            Vector3 candidate = transform.position + new Vector3(
                Mathf.Cos(angle) * distance,
                0f,
                Mathf.Sin(angle) * distance
            );

            if (NavMesh.SamplePosition(candidate, out hit, 3f, NavMesh.AllAreas))
            {
                // hit.position IS on the NavMesh at the right height — the old
                // "+1" was a flat-world/center-pivot relic that floats (or on
                // terrain, buries) base-pivot units
                return hit.position;
            }
        }

        // Last resort: offset position with NavMesh validation
        Debug.LogWarning("BaseBuilding: Could not find valid NavMesh spawn position! Using fallback.");
        Vector3 fallback = transform.position + new Vector3(spawnRadius + 1f, 0f, 0f);
        // Bug 3: Validate fallback on NavMesh to prevent off-mesh spawns
        if (NavMesh.SamplePosition(fallback, out hit, 5f, NavMesh.AllAreas))
        {
            return hit.position;
        }
        return fallback;
    }

    // Get total number of workers
    public int GetTotalWorkers()
    {
        return woodWorkers + foodWorkers + stoneWorkers + metalWorkers;
    }

    // ------------------------------------------------------------------
    // Warriors — recruited FROM the idle pool, not spawned (2026-09-02)
    // ------------------------------------------------------------------

    /// <summary>True when a recruit could happen right now: under the cap, affordable, and someone is idle.</summary>
    public bool CanRecruitWarrior()
    {
        if (currentWarriors >= maxWarriors) return false;
        if (ResourceManager.Instance == null
            || ResourceManager.Instance.wood < warriorCost_Wood
            || ResourceManager.Instance.food < warriorCost_Food) return false;
        return PopulationManager.Instance != null && PopulationManager.Instance.GetIdleCount() > 0;
    }

    /// <summary>
    /// Arm the idle colonist nearest the fire: they keep their roster slot and their home,
    /// the worker body is destroyed and a warrior stands in its place. Costs the usual
    /// wood and food. No-op when nobody is idle, the cap is reached, or it is unaffordable.
    /// </summary>
    public void SpawnWarrior()
    {
        if (!CanRecruitWarrior()) return;

        if (warriorPrefab == null)
        {
            Debug.LogError("BaseBuilding: Warrior prefab not assigned!");
            return;
        }

        Worker recruit = PopulationManager.Instance.FindIdleColonist(transform.position);
        if (recruit == null) return;

        ResourceManager.Instance.SpendWood(warriorCost_Wood);
        ResourceManager.Instance.SpendFood(warriorCost_Food);

        // Stand the warrior where the colonist stood (a garrisoned colonist is at a
        // hut edge, which is on the NavMesh too)
        Vector3 spawnPos = recruit.transform.position;
        NavMeshHit hit;
        if (NavMesh.SamplePosition(spawnPos, out hit, 2f, NavMesh.AllAreas)) spawnPos = hit.position;

        GameObject warriorObj = Instantiate(warriorPrefab, spawnPos, recruit.transform.rotation);
        warriorObj.name = $"Warrior_{currentWarriors + 1}";

        Warrior warrior = warriorObj.GetComponent<Warrior>();
        if (warrior == null)
        {
            Debug.LogError("BaseBuilding: Warrior prefab doesn't have Warrior component!");
            Destroy(warriorObj);
            return;
        }

        warrior.baseBuilding = this;
        activeWarriors.Add(warrior);
        currentWarriors++;

        // Same person, new body: swap the roster entry BEFORE destroying the old body,
        // so the worker's OnDestroy → NotifyWorkerRemoved finds nothing to remove.
        PopulationManager.Instance.ReplaceUnit(recruit, warrior);
        activeWorkers.Remove(recruit);
        Destroy(recruit.gameObject);
    }

    /// <summary>Dismiss a warrior: they lay down arms and rejoin the idle pool where they stand. No refund.</summary>
    public void RemoveWarrior()
    {
        if (currentWarriors <= 0 || activeWarriors.Count == 0)
        {
            return;
        }

        // Find the first living warrior
        Warrior warriorToRemove = null;
        foreach (Warrior warrior in activeWarriors)
        {
            if (warrior != null)
            {
                warriorToRemove = warrior;
                break;
            }
        }
        if (warriorToRemove == null) return;

        activeWarriors.Remove(warriorToRemove);
        currentWarriors--;

        Worker colonist = InstantiateColonist(warriorToRemove.transform.position);
        if (colonist != null && PopulationManager.Instance != null)
        {
            PopulationManager.Instance.ReplaceUnit(warriorToRemove, colonist);
        }
        Destroy(warriorToRemove.gameObject);
    }

    // Called when a warrior is killed — the one path a warrior leaves the roster by
    public void NotifyWarriorKilled(GameObject warrior)
    {
        Warrior warriorComponent = warrior.GetComponent<Warrior>();
        if (warriorComponent != null)
        {
            if (activeWarriors.Remove(warriorComponent)) currentWarriors--;
            if (PopulationManager.Instance != null)
            {
                PopulationManager.Instance.RemoveColonist(warriorComponent);
            }
        }
    }

    // Get current warrior count
    public int GetWarriorCount()
    {
        return currentWarriors;
    }

    // Called when campfire is destroyed
    void OnCampfireDestroyed()
    {
        Debug.Log("========================================");
        Debug.Log("BaseBuilding: CAMPFIRE DESTROYED! GAME OVER!");
        Debug.Log("========================================");

        ReleaseHousing();

        // Health component will handle the "DESTROYED!" text display

        // Visual feedback - darken the campfire
        if (buildingMaterials != null && buildingMaterials.Length > 0)
        {
            RendererTint.SetColor(buildingMaterials, Color.black);
        }
        else
        {
            Debug.LogWarning("BaseBuilding: No renderers found to darken!");
        }

        // Disable collider so it can't be clicked
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.enabled = false;
        }

        // Trigger game over through GameManager
        if (GameManager.Instance != null)
        {
            GameManager.Instance.TriggerDefeat();
        }
        else
        {
            Debug.LogError("BaseBuilding: No GameManager found to trigger defeat!");
        }

        // Disable worker spawning
        enabled = false;
    }

    // Public method to get current health (for UI later)
    public float GetCurrentHealth()
    {
        return healthComponent != null ? healthComponent.currentHealth : maxHealth;
    }

    public float GetHealthPercentage()
    {
        return healthComponent != null ? healthComponent.GetHealthPercentage() : 1f;
    }

    /// <summary>Housing leaves the roster exactly once, whether the fire died or the scene is tearing down.</summary>
    void ReleaseHousing()
    {
        if (housingReleased) return;
        housingReleased = true;
        if (PopulationManager.Instance != null)
        {
            PopulationManager.Instance.UnregisterHousing(this);
        }
    }

    void OnDestroy()
    {
        ActiveRegistry<BaseBuilding>.Unregister(this);
        ReleaseHousing();
    }

    // Visual helper in Scene view
    void OnDrawGizmosSelected()
    {
        // Draw spawn radius
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, spawnRadius);

        // Draw no-build radius (larger, semi-transparent red)
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, noBuildRadius);
    }
}
