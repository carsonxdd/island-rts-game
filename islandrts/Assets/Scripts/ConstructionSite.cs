using UnityEngine;
using UnityEngine.AI;

public class ConstructionSite : MonoBehaviour
{
    [Header("Construction Settings")]
    public float buildTime = 5f;  // Seconds to auto-complete
    public GameObject finishedBuildingPrefab;  // What to spawn when done

    [Header("Progress")]
    [Range(0f, 1f)]
    public float progress = 0f;  // 0 = just started, 1 = complete

    [Header("Building Placement")]
    public float noBuildRadius = 2.5f;  // Creates 5x5 square no-build zone (1 grid cell buffer)

    private float timeElapsed = 0f;
    private bool isComplete = false;

    void Start()
    {
        // Make sure NavMesh Obstacle is enabled for runtime pathfinding
        NavMeshObstacle obstacle = GetComponent<NavMeshObstacle>();
        if (obstacle != null)
        {
            obstacle.enabled = true;
            obstacle.carving = true;
            Debug.Log("ConstructionSite: NavMesh Obstacle enabled!");
        }
    }

    void Update()
    {
        if (isComplete) return;

        // Auto-build over time
        timeElapsed += Time.deltaTime;
        progress = Mathf.Clamp01(timeElapsed / buildTime);

        // Check if construction is complete
        if (progress >= 1f)
        {
            Complete();
        }
    }

    void Complete()
    {
        isComplete = true;
        Debug.Log($"ConstructionSite: Building completed at {transform.position}!");

        // Spawn the finished building
        if (finishedBuildingPrefab != null)
        {
            GameObject finishedBuilding = Instantiate(
                finishedBuildingPrefab,
                transform.position,
                transform.rotation
            );

            // Copy layer to finished building
            finishedBuilding.layer = gameObject.layer;

            // Enable NavMesh Obstacle on finished building for pathfinding
            NavMeshObstacle obstacle = finishedBuilding.GetComponent<NavMeshObstacle>();
            if (obstacle != null)
            {
                obstacle.enabled = true;
                obstacle.carving = true;
                Debug.Log("ConstructionSite: Finished building NavMesh Obstacle enabled!");
            }

            Debug.Log("ConstructionSite: Spawned finished building!");
        }
        else
        {
            Debug.LogWarning("ConstructionSite: No finished building prefab assigned!");
        }

        // Destroy the construction site
        Destroy(gameObject);
    }

    // Method for workers to add progress (we'll use this later)
    public void AddProgress(float amount)
    {
        if (isComplete) return;

        progress += amount;
        progress = Mathf.Clamp01(progress);

        if (progress >= 1f)
        {
            Complete();
        }
    }

    // Visual feedback in Scene view
    void OnDrawGizmos()
    {
        // Draw progress bar above construction site
        Gizmos.color = Color.yellow;
        Vector3 pos = transform.position + Vector3.up * 2f;
        Gizmos.DrawWireCube(pos, new Vector3(2f, 0.2f, 0.1f));

        // Fill bar based on progress
        Gizmos.color = Color.green;
        float fillWidth = 2f * progress;
        Gizmos.DrawCube(pos - Vector3.right * (2f - fillWidth) * 0.5f, new Vector3(fillWidth, 0.2f, 0.1f));
    }

    void OnDrawGizmosSelected()
    {
        // Draw no-build radius
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, noBuildRadius);
    }
}
