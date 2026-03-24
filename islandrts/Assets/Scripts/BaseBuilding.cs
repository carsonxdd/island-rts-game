using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using TMPro;

public class BaseBuilding : MonoBehaviour
{
    public static IReadOnlyList<BaseBuilding> ActiveList => ActiveRegistry<BaseBuilding>.List;

    void Awake() { ActiveRegistry<BaseBuilding>.Register(this); }

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

    [Header("Worker Assignments")]
    public int woodWorkers = 0;
    public int foodWorkers = 0;
    public int stoneWorkers = 0;

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
    private Renderer[] buildingRenderers;  // Changed to array to handle multiple parts
    private Color[] originalColors;        // Store original colors for each part

    void Start()
    {
        Debug.Log("BaseBuilding: Campfire initialized!");

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

        Debug.Log($"BaseBuilding: Health system initialized - {maxHealth} HP");

        // Register housing capacity with PopulationManager
        if (PopulationManager.Instance != null)
        {
            PopulationManager.Instance.AddHousing(workerCapacity);
            Debug.Log($"BaseBuilding: Registered {workerCapacity} housing slots");
        }

        // Get ALL renderers BEFORE creating health text (checks this object AND all children)
        buildingRenderers = GetComponentsInChildren<Renderer>();

        if (buildingRenderers.Length > 0)
        {
            originalColors = new Color[buildingRenderers.Length];

            // Create unique material instances and save original colors
            for (int i = 0; i < buildingRenderers.Length; i++)
            {
                buildingRenderers[i].material = new Material(buildingRenderers[i].material);
                originalColors[i] = buildingRenderers[i].material.color;
            }

            Debug.Log($"BaseBuilding: Found {buildingRenderers.Length} renderer(s) for hover effect!");
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
        else
        {
            Debug.Log($"BaseBuilding: Collider found: {col.GetType().Name}");
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
        // Mouse is hovering over the building
        if (buildingRenderers != null && buildingRenderers.Length > 0)
        {
            // Change color on ALL parts of the building
            foreach (Renderer renderer in buildingRenderers)
            {
                renderer.material.color = hoverColor;
            }
        }
    }

    void OnMouseExit()
    {
        // Mouse left the building
        if (buildingRenderers != null && buildingRenderers.Length > 0)
        {
            // Restore original colors on ALL parts
            for (int i = 0; i < buildingRenderers.Length; i++)
            {
                buildingRenderers[i].material.color = originalColors[i];
            }
        }
    }

    void OnMouseDown()
    {
        // When clicked, open worker assignment UI
        Debug.Log("BaseBuilding: Clicked! Opening worker UI...");

        if (workerUI != null)
        {
            workerUI.OpenPanel(this);
        }
        else
        {
            Debug.LogWarning("BaseBuilding: No WorkerAssignmentUI assigned!");
        }
    }

    // Assign a worker to a resource type
    public void AssignWorker(ResourceNode.ResourceType resourceType)
    {
        // Check housing capacity via PopulationManager (this is now the only limit)
        if (PopulationManager.Instance != null && !PopulationManager.Instance.HasAvailableHousing())
        {
            Debug.Log($"BaseBuilding: No housing available! Build more huts. ({PopulationManager.Instance.GetCurrentWorkers()}/{PopulationManager.Instance.GetHousingCapacity()})");
            return;
        }

        // Increment the appropriate counter
        switch (resourceType)
        {
            case ResourceNode.ResourceType.Wood:
                woodWorkers++;
                break;
            case ResourceNode.ResourceType.Food:
                foodWorkers++;
                break;
            case ResourceNode.ResourceType.Stone:
                stoneWorkers++;
                break;
        }

        // Spawn the worker
        SpawnWorker(resourceType);
    }

    // Remove a worker from a resource type
    public void UnassignWorker(ResourceNode.ResourceType resourceType)
    {
        // Find a worker of this type
        Worker workerToRemove = null;
        foreach (Worker worker in activeWorkers)
        {
            if (worker != null && worker.assignedResourceType == resourceType)
            {
                workerToRemove = worker;
                break;  // Found one, stop looking
            }
        }

        if (workerToRemove != null)
        {
            // Decrease counter
            switch (resourceType)
            {
                case ResourceNode.ResourceType.Wood:
                    if (woodWorkers > 0) woodWorkers--;
                    break;
                case ResourceNode.ResourceType.Food:
                    if (foodWorkers > 0) foodWorkers--;
                    break;
                case ResourceNode.ResourceType.Stone:
                    if (stoneWorkers > 0) stoneWorkers--;
                    break;
            }

            // Remove from list
            activeWorkers.Remove(workerToRemove);

            // Unregister worker with PopulationManager
            if (PopulationManager.Instance != null)
            {
                PopulationManager.Instance.RemoveWorker();
            }

            // Destroy the worker GameObject
            Destroy(workerToRemove.gameObject);

            Debug.Log($"BaseBuilding: Removed 1 {resourceType} worker");
        }
        else
        {
            Debug.Log($"BaseBuilding: No {resourceType} workers to remove!");
        }
    }

    void SpawnWorker(ResourceNode.ResourceType resourceType)
    {
        if (workerPrefab == null)
        {
            Debug.LogError("BaseBuilding: Worker prefab not assigned!");
            return;
        }

        // Find a valid NavMesh position around the campfire
        Vector3 spawnPos = GetValidSpawnPosition();

        // Spawn worker
        GameObject workerObj = Instantiate(workerPrefab, spawnPos, Quaternion.identity);
        workerObj.name = $"Worker_{resourceType}_{activeWorkers.Count + 1}";

        // Get Worker component and assign it
        Worker worker = workerObj.GetComponent<Worker>();
        if (worker != null)
        {
            worker.assignedResourceType = resourceType;
            worker.baseBuilding = this;
            activeWorkers.Add(worker);

            // Register worker with PopulationManager
            if (PopulationManager.Instance != null)
            {
                PopulationManager.Instance.AddWorker();
            }

            Debug.Log($"BaseBuilding: Spawned {resourceType} worker at {spawnPos}");
        }
        else
        {
            Debug.LogError("BaseBuilding: Worker prefab doesn't have Worker component!");
        }
    }

    /// <summary>
    /// Find a valid spawn position on the NavMesh around the campfire.
    /// Tries random positions, then evenly-spaced directions, then falls back with warning.
    /// </summary>
    Vector3 GetValidSpawnPosition()
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
                Vector3 pos = hit.position;
                pos.y = 1f;
                return pos;
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
                Vector3 pos = hit.position;
                pos.y = 1f;
                return pos;
            }
        }

        // Last resort: offset position with NavMesh validation
        Debug.LogWarning("BaseBuilding: Could not find valid NavMesh spawn position! Using fallback.");
        Vector3 fallback = transform.position + new Vector3(spawnRadius + 1f, 0f, 0f);
        fallback.y = 1f;
        // Bug 3: Validate fallback on NavMesh to prevent off-mesh spawns
        if (NavMesh.SamplePosition(fallback, out hit, 5f, NavMesh.AllAreas))
        {
            Vector3 pos = hit.position;
            pos.y = 1f;
            return pos;
        }
        return fallback;
    }

    // Get total number of workers
    public int GetTotalWorkers()
    {
        return woodWorkers + foodWorkers + stoneWorkers;
    }

    // Spawn a warrior
    public void SpawnWarrior()
    {
        if (currentWarriors >= maxWarriors)
        {
            Debug.Log("BaseBuilding: Max warriors reached!");
            return;
        }

        // Check if we have enough resources
        if (ResourceManager.Instance.wood < warriorCost_Wood || ResourceManager.Instance.food < warriorCost_Food)
        {
            Debug.Log($"BaseBuilding: Not enough resources! Need {warriorCost_Wood} wood and {warriorCost_Food} food");
            return;
        }

        if (warriorPrefab == null)
        {
            Debug.LogError("BaseBuilding: Warrior prefab not assigned!");
            return;
        }

        // Deduct resources
        ResourceManager.Instance.SpendWood(warriorCost_Wood);
        ResourceManager.Instance.SpendFood(warriorCost_Food);

        // Find a valid NavMesh position around the campfire
        Vector3 spawnPos = GetValidSpawnPosition();

        // Spawn warrior
        GameObject warriorObj = Instantiate(warriorPrefab, spawnPos, Quaternion.identity);
        warriorObj.name = $"Warrior_{currentWarriors + 1}";

        // Get Warrior component and assign it
        Warrior warrior = warriorObj.GetComponent<Warrior>();
        if (warrior != null)
        {
            warrior.baseBuilding = this;
            activeWarriors.Add(warrior);
            currentWarriors++;

            Debug.Log($"BaseBuilding: Spawned warrior #{currentWarriors} at {spawnPos}");
        }
        else
        {
            Debug.LogError("BaseBuilding: Warrior prefab doesn't have Warrior component!");
        }
    }

    // Remove a warrior
    public void RemoveWarrior()
    {
        if (currentWarriors <= 0 || activeWarriors.Count == 0)
        {
            Debug.Log("BaseBuilding: No warriors to remove!");
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

        if (warriorToRemove != null)
        {
            activeWarriors.Remove(warriorToRemove);
            currentWarriors--;
            Destroy(warriorToRemove.gameObject);

            Debug.Log($"BaseBuilding: Removed 1 warrior. {currentWarriors} warriors remaining");
        }
    }

    // Called when a warrior is killed
    public void NotifyWarriorKilled(GameObject warrior)
    {
        Warrior warriorComponent = warrior.GetComponent<Warrior>();
        if (warriorComponent != null)
        {
            activeWarriors.Remove(warriorComponent);
            currentWarriors--;
            Debug.Log($"BaseBuilding: Warrior killed. {currentWarriors} warriors remaining");
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

        // Remove housing capacity from PopulationManager
        if (PopulationManager.Instance != null)
        {
            PopulationManager.Instance.RemoveHousing(workerCapacity);
        }

        // Health component will handle the "DESTROYED!" text display

        // Visual feedback - darken the campfire
        if (buildingRenderers != null && buildingRenderers.Length > 0)
        {
            Debug.Log($"BaseBuilding: Turning {buildingRenderers.Length} renderers black...");
            foreach (Renderer renderer in buildingRenderers)
            {
                if (renderer != null && renderer.material != null)
                {
                    renderer.material.color = Color.black;
                    Debug.Log($"BaseBuilding: Renderer {renderer.name} turned black");
                }
            }
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
            Debug.Log("BaseBuilding: Collider disabled");
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
        Debug.Log("BaseBuilding: Component disabled");
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

    void OnDestroy()
    {
        ActiveRegistry<BaseBuilding>.Unregister(this);
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
