using UnityEngine;
using UnityEngine.AI;
using TMPro;

public class ConstructionSite : MonoBehaviour
{
    [Header("Construction Settings")]
    public float buildTime = 5f;  // Seconds to auto-complete
    public GameObject finishedBuildingPrefab;  // Legacy: What to spawn when done (use BuildingDatabase instead)
    public float targetHealth = 100f;  // Health the finished building will have

    [Header("Building Type (Set by BuildPlacement)")]
    public BuildingType buildingType = BuildingType.Hut;  // Which building type to construct

    [Header("Progress")]
    [Range(0f, 1f)]
    public float progress = 0f;  // 0 = just started, 1 = complete

    [Header("Building Placement")]
    public float noBuildRadius = 3.5f;  // Creates 7x7 square no-build zone (3 grid cell buffer)

    [Header("Progress Display")]
    public bool showProgressText = true;
    public float progressTextHeight = 2.5f;

    private float timeElapsed = 0f;
    private bool isComplete = false;
    private TextMeshPro progressText;
    private GameObject progressTextObject;
    private Camera cachedCamera;
    private int lastDisplayedProgressHP = -1;

    // WallGrid registration
    private Vector2Int gridPos;
    private bool registeredWithGrid = false;

    void Start()
    {
        cachedCamera = Camera.main;

        // Make sure NavMesh Obstacle is enabled for runtime pathfinding
        NavMeshObstacle obstacle = GetComponent<NavMeshObstacle>();
        if (obstacle != null)
        {
            obstacle.enabled = true;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;

            // For walls, use smaller carve size so gates have walkable gaps
            bool isWallType = false;
            if (BuildingDatabase.Instance != null)
            {
                BuildingData data = BuildingDatabase.Instance.GetBuildingData(buildingType);
                isWallType = data != null && data.isWall;
            }
            if (isWallType)
            {
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.size = new Vector3(0.9f, 2f, 0.9f);
                obstacle.center = new Vector3(0f, 1f, 0f);
            }
        }

        // Create progress text display
        if (showProgressText)
        {
            CreateProgressText();
        }

        // Register with WallGrid if this is a wall type
        RegisterIfWall();
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

        // Unregister from WallGrid before destroying (the finished Wall will re-register)
        UnregisterFromGrid();

        // Get building data from database
        if (BuildingDatabase.Instance == null)
        {
            Destroy(gameObject);
            return;
        }

        BuildingData data = BuildingDatabase.Instance.GetBuildingData(buildingType);
        if (data == null || data.finishedBuildingPrefab == null)
        {
            Destroy(gameObject);
            return;
        }

        // Spawn the finished building
        GameObject finishedBuilding = Instantiate(
            data.finishedBuildingPrefab,
            transform.position,
            transform.rotation
        );

        // Copy layer to finished building
        finishedBuilding.layer = gameObject.layer;

        // Destroy the construction site
        Destroy(gameObject);
    }

    /// <summary>
    /// Set the building type to construct (called by BuildPlacement)
    /// </summary>
    public void SetBuildingType(BuildingType type)
    {
        buildingType = type;

        // Update target health from building data
        if (BuildingDatabase.Instance != null)
        {
            BuildingData data = BuildingDatabase.Instance.GetBuildingData(type);
            if (data != null)
            {
                targetHealth = data.maxHealth;
            }
        }

        // Register with WallGrid if this is a wall type (may be called after Start)
        RegisterIfWall();
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

    private void RegisterIfWall()
    {
        if (registeredWithGrid) return;

        bool isWall = false;
        if (BuildingDatabase.Instance != null)
        {
            BuildingData data = BuildingDatabase.Instance.GetBuildingData(buildingType);
            isWall = data != null && data.isWall;
        }

        if (isWall)
        {
            gridPos = WallGrid.Instance.WorldToGrid(transform.position);
            WallGrid.Instance.Register(gridPos, this);
            registeredWithGrid = true;
        }
    }

    private void UnregisterFromGrid()
    {
        if (registeredWithGrid && WallGrid.Instance != null)
        {
            WallGrid.Instance.Unregister(gridPos);
            registeredWithGrid = false;
        }
    }

    void OnDestroy()
    {
        UnregisterFromGrid();
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
    }

    void UpdateProgressText()
    {
        if (progressText == null)
            return;

        // Billboard effect - always face camera (every frame, zero allocations)
        if (cachedCamera != null)
        {
            progressTextObject.transform.LookAt(cachedCamera.transform);
            progressTextObject.transform.Rotate(0, 180, 0);
        }

        // Only rebuild text when the displayed HP integer changes
        int currentHP = Mathf.RoundToInt(progress * targetHealth);
        if (currentHP == lastDisplayedProgressHP) return;
        lastDisplayedProgressHP = currentHP;

        // Update text content
        if (progress >= 1f)
        {
            progressText.text = "Complete!";
            progressText.color = Color.green;
        }
        else
        {
            progressText.text = $"Building...\n{currentHP} / {targetHealth:F0} HP";

            // Color based on progress
            if (progress >= 0.66f)
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
