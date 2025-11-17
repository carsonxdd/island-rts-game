using UnityEngine;
using System.Collections.Generic;

public class BaseBuilding : MonoBehaviour
{
    [Header("UI Reference")]
    public WorkerAssignmentUI workerUI;  // Drag the UI object here in Inspector

    [Header("Worker Management")]
    public GameObject workerPrefab;
    public int maxWorkers = 10;

    [Header("Worker Assignments")]
    public int woodWorkers = 0;
    public int foodWorkers = 0;
    public int stoneWorkers = 0;

    [Header("Spawn Settings")]
    public float spawnRadius = 2f;  // How far from campfire workers spawn

    [Header("Building Placement")]
    public float noBuildRadius = 2.5f;  // Creates 5x5 square no-build zone around campfire

    [Header("Hover Effect")]
    public Color normalColor = Color.white;
    public Color hoverColor = new Color(1f, 1f, 0.7f, 1f);  // Slight yellow tint

    // Track all spawned workers
    private List<Worker> activeWorkers = new List<Worker>();
    private Renderer[] buildingRenderers;  // Changed to array to handle multiple parts
    private Color[] originalColors;        // Store original colors for each part

    void Start()
    {
        Debug.Log("BaseBuilding: Campfire initialized!");

        // Get ALL renderers (checks this object AND all children)
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
        int totalWorkers = woodWorkers + foodWorkers + stoneWorkers;

        if (totalWorkers >= maxWorkers)
        {
            Debug.Log("BaseBuilding: Max workers reached!");
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

        // Random position around campfire - use OUTSIDE the obstacle radius
        // Spawn in a ring shape outside the building
        float angle = Random.Range(0f, 360f);
        float distance = Random.Range(spawnRadius + 0.2f, spawnRadius + 0.8f);  // Just outside obstacle

        Vector3 offset = new Vector3(
            Mathf.Cos(angle * Mathf.Deg2Rad) * distance,
            0f,
            Mathf.Sin(angle * Mathf.Deg2Rad) * distance
        );

        Vector3 spawnPos = transform.position + offset;
        spawnPos.y = 1f;  // Worker height

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

            Debug.Log($"BaseBuilding: Spawned {resourceType} worker at {spawnPos}");
        }
        else
        {
            Debug.LogError("BaseBuilding: Worker prefab doesn't have Worker component!");
        }
    }

    // Get total number of workers
    public int GetTotalWorkers()
    {
        return woodWorkers + foodWorkers + stoneWorkers;
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
