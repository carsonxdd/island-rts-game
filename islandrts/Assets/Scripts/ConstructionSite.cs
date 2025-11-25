using UnityEngine;
using UnityEngine.AI;
using TMPro;

public class ConstructionSite : MonoBehaviour
{
    [Header("Construction Settings")]
    public float buildTime = 5f;  // Seconds to auto-complete
    public GameObject finishedBuildingPrefab;  // What to spawn when done
    public float targetHealth = 100f;  // Health the finished building will have

    [Header("Progress")]
    [Range(0f, 1f)]
    public float progress = 0f;  // 0 = just started, 1 = complete

    [Header("Building Placement")]
    public float noBuildRadius = 2.5f;  // Creates 5x5 square no-build zone (1 grid cell buffer)

    [Header("Progress Display")]
    public bool showProgressText = true;
    public float progressTextHeight = 2.5f;

    private float timeElapsed = 0f;
    private bool isComplete = false;
    private TextMeshPro progressText;
    private GameObject progressTextObject;

    void Start()
    {
        // Make sure NavMesh Obstacle is enabled for runtime pathfinding
        NavMeshObstacle obstacle = GetComponent<NavMeshObstacle>();
        if (obstacle != null)
        {
            obstacle.enabled = true;
            obstacle.carving = false;  // Disable carving to prevent path recalculation during construction
            Debug.Log("ConstructionSite: NavMesh Obstacle enabled (no carving)!");
        }

        // Create progress text display
        if (showProgressText)
        {
            CreateProgressText();
        }
    }

    void Update()
    {
        if (isComplete) return;

        // Auto-build over time
        timeElapsed += Time.deltaTime;
        progress = Mathf.Clamp01(timeElapsed / buildTime);

        // Update progress text
        if (showProgressText && progressText != null)
        {
            UpdateProgressText();
        }

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

            // Note: NavMeshObstacle settings are handled by the building's own script (Hut, BaseBuilding, etc.)
            // Those scripts disable carving to prevent path stuttering

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

    void CreateProgressText()
    {
        // Create a child GameObject for the text
        progressTextObject = new GameObject("ProgressText");
        progressTextObject.transform.parent = transform;
        progressTextObject.transform.localPosition = new Vector3(0, progressTextHeight, 0);

        // Add TextMeshPro component
        progressText = progressTextObject.AddComponent<TextMeshPro>();
        progressText.fontSize = 2.5f;
        progressText.alignment = TextAlignmentOptions.Center;
        progressText.color = Color.cyan;

        // Set sorting to render on top
        progressText.GetComponent<MeshRenderer>().sortingOrder = 100;

        UpdateProgressText();  // Set initial text
        Debug.Log("ConstructionSite: Progress text created");
    }

    void UpdateProgressText()
    {
        if (progressText == null)
            return;

        // Calculate current "health" based on progress
        float currentProgress = progress * targetHealth;

        // Update text content
        progressText.text = $"Building...\n{currentProgress:F0} / {targetHealth:F0} HP";

        // Color based on progress
        if (progress >= 1f)
        {
            progressText.text = "Complete!";
            progressText.color = Color.green;
        }
        else if (progress >= 0.66f)
        {
            progressText.color = Color.green;
        }
        else if (progress >= 0.33f)
        {
            progressText.color = Color.yellow;
        }
        else
        {
            progressText.color = Color.cyan;
        }

        // Billboard effect - always face camera
        if (Camera.main != null)
        {
            progressTextObject.transform.LookAt(Camera.main.transform);
            progressTextObject.transform.Rotate(0, 180, 0);  // Flip to face camera correctly
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
