using UnityEngine;
using System.Collections.Generic;

public class BuildPlacement : MonoBehaviour
{
    [Header("Building Selection")]
    public BuildingType selectedBuildingType = BuildingType.Hut;
    public BuildingSelectionUI selectionUI;  // Optional: Visual feedback for building selection

    [Header("Building Prefabs (Legacy - kept for backward compatibility)")]
    public GameObject hutGhostPrefab;  // Legacy: Use BuildingDatabase instead
    public GameObject constructionSitePrefab;  // Shared construction site prefab
    public float ghostBuildingNoBuildRadius = 3.5f;  // Will be dynamically set from BuildingData

    [Header("Building Cost (Legacy - kept for backward compatibility)")]
    public int woodCost = 20;  // Legacy: Use BuildingDatabase instead
    public int foodCost = 10;  // Legacy: Use BuildingDatabase instead
    public int stoneCost = 0;  // Legacy: Use BuildingDatabase instead

    [Header("Grid Settings")]
    public float cellSize = 1f;  // Match your grid size
    public float placementHeight = 0.75f;  // Half the height of the hut
    public float movementSpeed = 10f;  // How fast ghost follows mouse (lower = slower/smoother)

    [Header("Collision Detection")]
    public Vector3 buildingSize = new Vector3(2f, 1.5f, 2f);  // Size of the hut for collision check
    public LayerMask buildingsLayer;  // Set to "Buildings" layer

    [Header("Visual Feedback")]
    public Color validColor = new Color(0.5f, 1f, 0.5f, 0.5f);    // Green = valid
    public Color invalidColor = new Color(1f, 0.3f, 0.3f, 0.5f);  // Red = invalid
    public bool showNoBuildZones = true;  // Show red circles during build mode
    public bool showNoBuildFills = false;  // Show filled squares (disable to only show borders)
    public bool mergeOverlappingZones = true;  // Merge overlapping zones into single continuous outline
    public Color noBuildZoneColor = new Color(1f, 0.2f, 0.2f, 0.3f);  // Semi-transparent red
    public Material noBuildZoneMaterial;  // Optional material for zone circles (if null, will create default)

    [Header("Controls")]
    public KeyCode startBuildKey = KeyCode.B;

    [Header("Raycast Settings")]
    public LayerMask groundLayer;  // Set to "Default" layer for ground plane

    // Private state
    private GameObject currentGhost;
    private Camera mainCam;
    private bool isPlacing = false;
    private bool isValidPlacement = false;
    private Renderer ghostRenderer;
    private Vector3 targetPosition;  // Where the ghost wants to move to
    private List<GameObject> noBuildZoneVisuals = new List<GameObject>();  // Visual circles for no-build zones
    private GameObject ghostNoBuildZone;  // No-build zone preview for the ghost building

    void Start()
    {
        mainCam = Camera.main;
        if (mainCam == null)
        {
            Debug.LogError("BuildPlacement: No Main Camera found!");
        }
    }

    void Update()
    {
        // Start placement mode when B is pressed
        if (Input.GetKeyDown(startBuildKey) && !isPlacing)
        {
            StartPlacement();
        }

        // If we're in placement mode
        if (isPlacing && currentGhost != null)
        {
            // Building type selection hotkeys (1-4)
            if (Input.GetKeyDown(KeyCode.Alpha1)) SelectBuilding(BuildingType.Hut);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SelectBuilding(BuildingType.WoodenWall);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SelectBuilding(BuildingType.StoneWall);
            if (Input.GetKeyDown(KeyCode.Alpha4)) SelectBuilding(BuildingType.Watchtower);

            MoveGhostToMouse();

            // Confirm placement with left-click
            if (Input.GetMouseButtonDown(0))
            {
                ConfirmPlacement();
            }

            // Cancel with Escape or right-click
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                CancelPlacement();
            }
        }
    }

    void StartPlacement()
    {
        // Check if BuildingDatabase exists
        if (BuildingDatabase.Instance == null)
        {
            Debug.LogError("BuildPlacement: BuildingDatabase not found in scene!");
            return;
        }

        // Get building data for the selected type
        BuildingData data = BuildingDatabase.Instance.GetBuildingData(selectedBuildingType);
        if (data == null || data.ghostPrefab == null)
        {
            Debug.LogError($"BuildPlacement: No ghost prefab for building type {selectedBuildingType}!");
            return;
        }

        // Spawn the ghost building
        currentGhost = Instantiate(data.ghostPrefab);
        ghostRenderer = currentGhost.GetComponent<Renderer>();

        if (ghostRenderer == null)
        {
            Debug.LogError($"BuildPlacement: Ghost prefab for {selectedBuildingType} has no Renderer component!");
        }

        // Update building properties from data
        buildingSize = data.buildingSize;
        placementHeight = data.placementHeight;
        ghostBuildingNoBuildRadius = data.noBuildRadius;

        // Initialize target position
        targetPosition = currentGhost.transform.position;

        isPlacing = true;
        isValidPlacement = false;

        // Create visual no-build zones for existing buildings
        if (showNoBuildZones)
        {
            CreateNoBuildZoneVisuals();
        }

        // Create no-build zone preview for the ghost building
        CreateGhostNoBuildZone();

        // Update UI if exists
        if (selectionUI != null)
        {
            bool canAfford = ResourceManager.Instance.CanAfford(data.woodCost, data.foodCost, data.stoneCost);
            selectionUI.UpdateDisplay(data, canAfford);
            selectionUI.Show();
        }

        Debug.Log($"BuildPlacement: Started with {data.buildingName}! Press 1-4 to switch buildings. Left-click to place, ESC to cancel.");
    }

    void MoveGhostToMouse()
    {
        // Raycast from camera to mouse position
        Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        Vector3 snappedPos = Vector3.zero;
        bool foundPosition = false;

        // Cast ray to find ground
        if (Physics.Raycast(ray, out hit, 1000f, groundLayer))
        {
            // Get hit position and snap to grid
            snappedPos = GridSnap.SnapXZ(hit.point, cellSize);
            snappedPos.y = placementHeight;  // Set to proper height
            foundPosition = true;
        }
        else
        {
            // Fallback: use plane at y=0
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            float distance;
            if (groundPlane.Raycast(ray, out distance))
            {
                Vector3 worldPos = ray.GetPoint(distance);
                snappedPos = GridSnap.SnapXZ(worldPos, cellSize);
                snappedPos.y = placementHeight;
                foundPosition = true;
            }
        }

        if (foundPosition)
        {
            // Update target position (where we want to go)
            targetPosition = snappedPos;

            // Smoothly interpolate ghost position towards target
            currentGhost.transform.position = Vector3.Lerp(
                currentGhost.transform.position,
                targetPosition,
                movementSpeed * Time.deltaTime
            );

            // Update ghost no-build zone position to follow the ghost
            if (ghostNoBuildZone != null)
            {
                ghostNoBuildZone.transform.position = new Vector3(
                    currentGhost.transform.position.x,
                    0.05f,
                    currentGhost.transform.position.z
                );
            }

            // Check for collisions at TARGET position (not current ghost position)
            // This ensures we check where it's going, not where it currently is
            bool hasCollision = Physics.CheckBox(
                targetPosition,
                buildingSize * 0.5f,  // Half extents
                Quaternion.identity,
                buildingsLayer
            );

            // Check if too close to any existing buildings (no-build zones)
            bool tooCloseToBuilding = IsTooCloseToExistingBuilding(targetPosition);

            // Update validity and color (must pass both checks)
            isValidPlacement = !hasCollision && !tooCloseToBuilding;

            if (ghostRenderer != null)
            {
                // Create new material instance to change color
                if (isValidPlacement)
                {
                    ghostRenderer.material.color = validColor;
                }
                else
                {
                    ghostRenderer.material.color = invalidColor;
                }
            }
        }
    }

    void ConfirmPlacement()
    {
        // Only allow placement if position is valid
        if (!isValidPlacement)
        {
            Debug.Log("BuildPlacement: Cannot place here - overlapping with another building!");
            return;
        }

        // Get building data
        if (BuildingDatabase.Instance == null)
        {
            Debug.LogError("BuildPlacement: BuildingDatabase not found!");
            return;
        }

        BuildingData data = BuildingDatabase.Instance.GetBuildingData(selectedBuildingType);
        if (data == null || data.constructionSitePrefab == null)
        {
            Debug.LogError($"BuildPlacement: No construction site prefab for {selectedBuildingType}!");
            return;
        }

        // Check if player has enough resources
        if (ResourceManager.Instance == null)
        {
            Debug.LogError("BuildPlacement: No ResourceManager found in scene!");
            return;
        }

        if (!ResourceManager.Instance.CanAfford(data.woodCost, data.foodCost, data.stoneCost))
        {
            Debug.Log($"BuildPlacement: Not enough resources! Need {data.woodCost}W {data.foodCost}F {data.stoneCost}S");
            return;
        }

        // Deduct resources
        ResourceManager.Instance.SpendResources(data.woodCost, data.foodCost, data.stoneCost);

        // Use target position (where ghost is moving to) not current position
        Vector3 buildPosition = targetPosition;
        Debug.Log($"BuildPlacement: Spawning ConstructionSite for {data.buildingName} at {buildPosition}");

        // Spawn the construction site
        GameObject constructionSite = Instantiate(
            data.constructionSitePrefab,
            buildPosition,
            Quaternion.identity
        );

        // Make sure it's on the Buildings layer for collision detection
        constructionSite.layer = LayerMask.NameToLayer("Buildings");

        // Configure construction site with building type
        ConstructionSite siteComponent = constructionSite.GetComponent<ConstructionSite>();
        if (siteComponent != null)
        {
            siteComponent.SetBuildingType(selectedBuildingType);
        }

        // Play building placed sound
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayBuildingPlaced();
        }

        // Destroy the ghost
        Destroy(currentGhost);

        // Destroy no-build zone visuals
        DestroyNoBuildZoneVisuals();

        // Destroy ghost no-build zone
        if (ghostNoBuildZone != null)
        {
            Destroy(ghostNoBuildZone);
            ghostNoBuildZone = null;
        }

        // Hide UI if exists
        if (selectionUI != null)
        {
            selectionUI.Hide();
        }

        // End placement mode
        currentGhost = null;
        ghostRenderer = null;
        isPlacing = false;

        Debug.Log($"BuildPlacement: {data.buildingName} construction started! Press B to place another.");
    }

    void CancelPlacement()
    {
        Debug.Log("BuildPlacement: Cancelled.");

        // Destroy the ghost
        if (currentGhost != null)
        {
            Destroy(currentGhost);
            currentGhost = null;
        }

        // Destroy no-build zone visuals
        DestroyNoBuildZoneVisuals();

        // Destroy ghost no-build zone
        if (ghostNoBuildZone != null)
        {
            Destroy(ghostNoBuildZone);
            ghostNoBuildZone = null;
        }

        // Hide UI if exists
        if (selectionUI != null)
        {
            selectionUI.Hide();
        }

        isPlacing = false;
    }

    /// <summary>
    /// Switch to a different building type during placement mode
    /// </summary>
    void SelectBuilding(BuildingType type)
    {
        if (!isPlacing)
        {
            Debug.LogWarning("BuildPlacement: Cannot select building - not in placement mode!");
            return;
        }

        // Check if BuildingDatabase exists
        if (BuildingDatabase.Instance == null)
        {
            Debug.LogError("BuildPlacement: BuildingDatabase not found in scene!");
            return;
        }

        // Get building data for the selected type
        BuildingData data = BuildingDatabase.Instance.GetBuildingData(type);
        if (data == null)
        {
            Debug.LogError($"BuildPlacement: No BuildingData found for {type}!");
            return;
        }

        // Destroy old ghost
        if (currentGhost != null)
        {
            Destroy(currentGhost);
        }

        // Destroy old ghost no-build zone
        if (ghostNoBuildZone != null)
        {
            Destroy(ghostNoBuildZone);
        }

        // Update selected building type
        selectedBuildingType = type;

        // Spawn new ghost from building data
        currentGhost = Instantiate(data.ghostPrefab);
        ghostRenderer = currentGhost.GetComponent<Renderer>();

        if (ghostRenderer == null)
        {
            Debug.LogError($"BuildPlacement: Ghost prefab for {type} has no Renderer component!");
        }

        // Update building size and placement height for collision detection
        buildingSize = data.buildingSize;
        placementHeight = data.placementHeight;
        ghostBuildingNoBuildRadius = data.noBuildRadius;

        // Initialize target position
        targetPosition = currentGhost.transform.position;

        // Create new ghost no-build zone
        CreateGhostNoBuildZone();

        // Update UI if exists
        if (selectionUI != null)
        {
            bool canAfford = ResourceManager.Instance.CanAfford(data.woodCost, data.foodCost, data.stoneCost);
            selectionUI.UpdateDisplay(data, canAfford);
        }

        Debug.Log($"BuildPlacement: Selected {data.buildingName} (Cost: {data.woodCost}W {data.foodCost}F {data.stoneCost}S)");
    }

    // Check if position is too close to any existing building's no-build zone
    // Uses SQUARE bounds instead of circular to match visual display
    bool IsTooCloseToExistingBuilding(Vector3 position)
    {
        // Small buffer to account for floating-point precision with grid snapping
        float gridBuffer = cellSize * 0.1f;

        // Check all BaseBuilding objects (Campfire, etc.)
        BaseBuilding[] baseBuildings = FindObjectsByType<BaseBuilding>(FindObjectsSortMode.None);
        foreach (BaseBuilding building in baseBuildings)
        {
            if (building == null) continue;

            // Check if inside square bounds (check X and Z separately)
            float deltaX = Mathf.Abs(position.x - building.transform.position.x);
            float deltaZ = Mathf.Abs(position.z - building.transform.position.z);

            // Add buffer to ensure boundary cells are blocked despite floating point errors
            if (deltaX < building.noBuildRadius + gridBuffer && deltaZ < building.noBuildRadius + gridBuffer)
            {
                return true;  // Too close!
            }
        }

        // Check all ConstructionSite objects
        ConstructionSite[] constructionSites = FindObjectsByType<ConstructionSite>(FindObjectsSortMode.None);
        foreach (ConstructionSite site in constructionSites)
        {
            if (site == null) continue;

            float deltaX = Mathf.Abs(position.x - site.transform.position.x);
            float deltaZ = Mathf.Abs(position.z - site.transform.position.z);

            if (deltaX < site.noBuildRadius + gridBuffer && deltaZ < site.noBuildRadius + gridBuffer)
            {
                return true;  // Too close!
            }
        }

        // Check all Hut objects (finished buildings)
        Hut[] huts = FindObjectsByType<Hut>(FindObjectsSortMode.None);
        foreach (Hut hut in huts)
        {
            if (hut == null) continue;

            float deltaX = Mathf.Abs(position.x - hut.transform.position.x);
            float deltaZ = Mathf.Abs(position.z - hut.transform.position.z);

            if (deltaX < hut.noBuildRadius + gridBuffer && deltaZ < hut.noBuildRadius + gridBuffer)
            {
                return true;  // Too close!
            }
        }

        // Check all Wall objects (finished walls)
        Wall[] walls = FindObjectsByType<Wall>(FindObjectsSortMode.None);
        foreach (Wall wall in walls)
        {
            if (wall == null) continue;

            float deltaX = Mathf.Abs(position.x - wall.transform.position.x);
            float deltaZ = Mathf.Abs(position.z - wall.transform.position.z);

            if (deltaX < wall.noBuildRadius + gridBuffer && deltaZ < wall.noBuildRadius + gridBuffer)
            {
                return true;  // Too close!
            }
        }

        // Check all Watchtower objects (finished towers)
        Watchtower[] watchtowers = FindObjectsByType<Watchtower>(FindObjectsSortMode.None);
        foreach (Watchtower tower in watchtowers)
        {
            if (tower == null) continue;

            float deltaX = Mathf.Abs(position.x - tower.transform.position.x);
            float deltaZ = Mathf.Abs(position.z - tower.transform.position.z);

            if (deltaX < tower.noBuildRadius + gridBuffer && deltaZ < tower.noBuildRadius + gridBuffer)
            {
                return true;  // Too close!
            }
        }

        return false;  // All good!
    }

    // Create visual zones showing no-build areas around existing buildings
    void CreateNoBuildZoneVisuals()
    {
        // Clear any existing visuals first
        DestroyNoBuildZoneVisuals();

        if (mergeOverlappingZones)
        {
            // Create merged continuous outline
            CreateMergedNoBuildZones();
        }
        else
        {
            // Create individual zones (old behavior)
            CreateIndividualNoBuildZones();
        }
    }

    // Create individual zone visuals for each building (original behavior)
    void CreateIndividualNoBuildZones()
    {
        // Create circles for all BaseBuilding objects
        BaseBuilding[] baseBuildings = FindObjectsByType<BaseBuilding>(FindObjectsSortMode.None);
        foreach (BaseBuilding building in baseBuildings)
        {
            if (building == null) continue;
            CreateCircleVisual(building.transform.position, building.noBuildRadius);
        }

        // Create circles for all ConstructionSite objects
        ConstructionSite[] constructionSites = FindObjectsByType<ConstructionSite>(FindObjectsSortMode.None);
        foreach (ConstructionSite site in constructionSites)
        {
            if (site == null) continue;
            CreateCircleVisual(site.transform.position, site.noBuildRadius);
        }

        // Create circles for all Hut objects (finished buildings)
        Hut[] huts = FindObjectsByType<Hut>(FindObjectsSortMode.None);
        foreach (Hut hut in huts)
        {
            if (hut == null) continue;
            CreateCircleVisual(hut.transform.position, hut.noBuildRadius);
        }

        // Create circles for all Wall objects
        Wall[] walls = FindObjectsByType<Wall>(FindObjectsSortMode.None);
        foreach (Wall wall in walls)
        {
            if (wall == null) continue;
            CreateCircleVisual(wall.transform.position, wall.noBuildRadius);
        }

        // Create circles for all Watchtower objects
        Watchtower[] watchtowers = FindObjectsByType<Watchtower>(FindObjectsSortMode.None);
        foreach (Watchtower tower in watchtowers)
        {
            if (tower == null) continue;
            CreateCircleVisual(tower.transform.position, tower.noBuildRadius);
        }
    }

    // Create merged zones by filling grid cells then outlining the filled region
    void CreateMergedNoBuildZones()
    {
        // Collect all building zones
        List<ZoneData> zones = new List<ZoneData>();

        BaseBuilding[] baseBuildings = FindObjectsByType<BaseBuilding>(FindObjectsSortMode.None);
        foreach (BaseBuilding building in baseBuildings)
        {
            if (building == null) continue;
            zones.Add(new ZoneData { position = building.transform.position, radius = building.noBuildRadius });
        }

        ConstructionSite[] constructionSites = FindObjectsByType<ConstructionSite>(FindObjectsSortMode.None);
        foreach (ConstructionSite site in constructionSites)
        {
            if (site == null) continue;
            zones.Add(new ZoneData { position = site.transform.position, radius = site.noBuildRadius });
        }

        Hut[] huts = FindObjectsByType<Hut>(FindObjectsSortMode.None);
        foreach (Hut hut in huts)
        {
            if (hut == null) continue;
            zones.Add(new ZoneData { position = hut.transform.position, radius = hut.noBuildRadius });
        }

        Wall[] walls = FindObjectsByType<Wall>(FindObjectsSortMode.None);
        foreach (Wall wall in walls)
        {
            if (wall == null) continue;
            zones.Add(new ZoneData { position = wall.transform.position, radius = wall.noBuildRadius });
        }

        Watchtower[] watchtowers = FindObjectsByType<Watchtower>(FindObjectsSortMode.None);
        foreach (Watchtower tower in watchtowers)
        {
            if (tower == null) continue;
            zones.Add(new ZoneData { position = tower.transform.position, radius = tower.noBuildRadius });
        }

        if (zones.Count == 0) return;

        // Find bounds in grid coordinates
        int minGridX = int.MaxValue, maxGridX = int.MinValue;
        int minGridZ = int.MaxValue, maxGridZ = int.MinValue;

        foreach (var zone in zones)
        {
            int centerGridX = Mathf.FloorToInt(zone.position.x / cellSize);
            int centerGridZ = Mathf.FloorToInt(zone.position.z / cellSize);
            int cellsToExtend = Mathf.FloorToInt(zone.radius / cellSize);

            minGridX = Mathf.Min(minGridX, centerGridX - cellsToExtend);
            maxGridX = Mathf.Max(maxGridX, centerGridX + cellsToExtend);
            minGridZ = Mathf.Min(minGridZ, centerGridZ - cellsToExtend);
            maxGridZ = Mathf.Max(maxGridZ, centerGridZ + cellsToExtend);
        }

        // Create a HashSet of filled grid cells
        HashSet<string> filledCells = new HashSet<string>();

        foreach (var zone in zones)
        {
            // Find the grid cell that contains the zone center
            // Use RoundToInt instead of FloorToInt for symmetric cell filling
            int centerGridX = Mathf.RoundToInt(zone.position.x / cellSize);
            int centerGridZ = Mathf.RoundToInt(zone.position.z / cellSize);

            // Calculate how many cells to extend in each direction
            // For radius 2.5: we want 2 cells on each side (total 5 cells)
            int cellsToExtend = Mathf.FloorToInt(zone.radius / cellSize);

            // Fill cells symmetrically around center
            for (int x = centerGridX - cellsToExtend; x <= centerGridX + cellsToExtend; x++)
            {
                for (int z = centerGridZ - cellsToExtend; z <= centerGridZ + cellsToExtend; z++)
                {
                    filledCells.Add($"{x},{z}");
                }
            }
        }

        // Now draw edges around the filled region
        DrawPerimeterEdgesFromGrid(filledCells, minGridX, maxGridX, minGridZ, maxGridZ);
    }

    // Helper class for grid-based edge data
    class GridEdgeData
    {
        public Vector3 worldP1;  // World coordinates for drawing
        public Vector3 worldP2;
        public int count;
    }

    // Helper struct for zone data
    struct ZoneData
    {
        public Vector3 position;
        public float radius;
    }

    // Helper struct for edge segments
    struct EdgeSegment
    {
        public Vector3 p1;
        public Vector3 p2;

        public EdgeSegment(Vector3 point1, Vector3 point2)
        {
            // Normalize edge direction so we can detect duplicates
            if (point1.x < point2.x || (point1.x == point2.x && point1.z < point2.z))
            {
                p1 = point1;
                p2 = point2;
            }
            else
            {
                p1 = point2;
                p2 = point1;
            }
        }

        public override bool Equals(object obj)
        {
            if (!(obj is EdgeSegment)) return false;
            EdgeSegment other = (EdgeSegment)obj;
            return p1 == other.p1 && p2 == other.p2;
        }

        public override int GetHashCode()
        {
            return p1.GetHashCode() ^ p2.GetHashCode();
        }
    }

    // Draw perimeter edges around all filled cells
    void DrawPerimeterEdgesFromGrid(HashSet<string> filledCells, int minX, int maxX, int minZ, int maxZ)
    {
        // For each filled cell, check its 4 edges
        // If a neighbor is NOT filled, draw that edge
        Dictionary<string, GridEdgeData> uniqueEdges = new Dictionary<string, GridEdgeData>();

        foreach (string cellKey in filledCells)
        {
            string[] parts = cellKey.Split(',');
            int x = int.Parse(parts[0]);
            int z = int.Parse(parts[1]);

            // Check each of the 4 neighbors
            // Bottom edge (neighbor at z-1)
            if (!filledCells.Contains($"{x},{z - 1}"))
            {
                AddGridEdge(uniqueEdges, x, z, x + 1, z);  // Bottom edge of this cell
            }

            // Right edge (neighbor at x+1)
            if (!filledCells.Contains($"{x + 1},{z}"))
            {
                AddGridEdge(uniqueEdges, x + 1, z, x + 1, z + 1);  // Right edge of this cell
            }

            // Top edge (neighbor at z+1)
            if (!filledCells.Contains($"{x},{z + 1}"))
            {
                AddGridEdge(uniqueEdges, x, z + 1, x + 1, z + 1);  // Top edge of this cell
            }

            // Left edge (neighbor at x-1)
            if (!filledCells.Contains($"{x - 1},{z}"))
            {
                AddGridEdge(uniqueEdges, x, z, x, z + 1);  // Left edge of this cell
            }
        }

        // Draw all unique edges
        foreach (var edge in uniqueEdges.Values)
        {
            DrawEdgeSegment(edge.worldP1, edge.worldP2);
        }
    }

    // Add an edge to the dictionary (using grid coordinates for the key)
    void AddGridEdge(Dictionary<string, GridEdgeData> edges, int gridX1, int gridZ1, int gridX2, int gridZ2)
    {
        // Normalize edge direction
        int x1, z1, x2, z2;
        if (gridX1 < gridX2 || (gridX1 == gridX2 && gridZ1 < gridZ2))
        {
            x1 = gridX1; z1 = gridZ1;
            x2 = gridX2; z2 = gridZ2;
        }
        else
        {
            x1 = gridX2; z1 = gridZ2;
            x2 = gridX1; z2 = gridZ1;
        }

        string key = $"{x1},{z1}|{x2},{z2}";

        if (!edges.ContainsKey(key))
        {
            // Center the grid coordinates first, then convert to world coordinates
            // Grid edges span asymmetrically (e.g., -2 to 3), so we add 0.5 to center them
            // Then apply offset to align with the functional no-build zone boundary
            float centeringOffset = 0.5f;
            float offset = cellSize;
            Vector3 worldP1 = new Vector3((x1 + centeringOffset) * cellSize - offset, 0, (z1 + centeringOffset) * cellSize - offset);
            Vector3 worldP2 = new Vector3((x2 + centeringOffset) * cellSize - offset, 0, (z2 + centeringOffset) * cellSize - offset);

            edges[key] = new GridEdgeData { worldP1 = worldP1, worldP2 = worldP2, count = 1 };
        }
    }


    // Draw a single edge segment
    void DrawEdgeSegment(Vector3 p1, Vector3 p2)
    {
        GameObject segmentObj = new GameObject("EdgeSegment");
        LineRenderer lineRenderer = segmentObj.AddComponent<LineRenderer>();

        lineRenderer.positionCount = 2;
        lineRenderer.useWorldSpace = true;

        // Set line properties
        lineRenderer.startWidth = 0.15f;
        lineRenderer.endWidth = 0.15f;
        Color borderColor = new Color(1f, 0f, 0f, 0.8f);
        lineRenderer.startColor = borderColor;
        lineRenderer.endColor = borderColor;

        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.material.color = borderColor;

        // Set positions
        p1.y = 0.05f;
        p2.y = 0.05f;
        lineRenderer.SetPosition(0, p1);
        lineRenderer.SetPosition(1, p2);

        noBuildZoneVisuals.Add(segmentObj);
    }

    // Destroy all no-build zone visuals
    void DestroyNoBuildZoneVisuals()
    {
        foreach (GameObject visual in noBuildZoneVisuals)
        {
            if (visual != null)
            {
                Destroy(visual);
            }
        }
        noBuildZoneVisuals.Clear();
    }

    // Create a single square zone visual at a position with given radius (used as half-width)
    void CreateCircleVisual(Vector3 center, float radius)
    {
        GameObject zoneObj = new GameObject("NoBuildZone");
        zoneObj.transform.position = new Vector3(center.x, 0.05f, center.z);

        // Create the filled square (quad) if enabled
        if (showNoBuildFills)
        {
            GameObject fillObj = new GameObject("Fill");
            fillObj.transform.SetParent(zoneObj.transform);
            fillObj.transform.localPosition = Vector3.zero;
            fillObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // Lay flat on ground

            MeshFilter meshFilter = fillObj.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = fillObj.AddComponent<MeshRenderer>();

            // Create a quad mesh
            Mesh mesh = new Mesh();
            float size = radius * 2f;
            mesh.vertices = new Vector3[]
            {
                new Vector3(-radius, -radius, 0),
                new Vector3(radius, -radius, 0),
                new Vector3(radius, radius, 0),
                new Vector3(-radius, radius, 0)
            };
            mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
            mesh.uv = new Vector2[]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(1, 1),
                new Vector2(0, 1)
            };
            mesh.RecalculateNormals();
            meshFilter.mesh = mesh;

            // Create semi-transparent red material for the fill
            Material fillMaterial;
            if (noBuildZoneMaterial != null)
            {
                fillMaterial = noBuildZoneMaterial;
            }
            else
            {
                fillMaterial = new Material(Shader.Find("Standard"));
                fillMaterial.color = noBuildZoneColor;
                fillMaterial.SetFloat("_Mode", 3); // Transparent mode
                fillMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                fillMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                fillMaterial.SetInt("_ZWrite", 0);
                fillMaterial.DisableKeyword("_ALPHATEST_ON");
                fillMaterial.EnableKeyword("_ALPHABLEND_ON");
                fillMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                fillMaterial.renderQueue = 3000;
            }
            meshRenderer.material = fillMaterial;
        }

        // Create the border using LineRenderer
        GameObject borderObj = new GameObject("Border");
        borderObj.transform.SetParent(zoneObj.transform);
        borderObj.transform.localPosition = new Vector3(0, 0.01f, 0); // Slightly above fill

        LineRenderer lineRenderer = borderObj.AddComponent<LineRenderer>();
        lineRenderer.positionCount = 5; // Square needs 5 points to close
        lineRenderer.loop = true;
        lineRenderer.useWorldSpace = false;

        // Set border width and color (brighter red for border)
        lineRenderer.startWidth = 0.15f;
        lineRenderer.endWidth = 0.15f;
        Color borderColor = new Color(1f, 0f, 0f, 0.8f); // Brighter red for border
        lineRenderer.startColor = borderColor;
        lineRenderer.endColor = borderColor;

        // Use default material for border
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.material.color = borderColor;

        // Set square corner positions
        lineRenderer.SetPosition(0, new Vector3(-radius, 0, -radius));
        lineRenderer.SetPosition(1, new Vector3(radius, 0, -radius));
        lineRenderer.SetPosition(2, new Vector3(radius, 0, radius));
        lineRenderer.SetPosition(3, new Vector3(-radius, 0, radius));
        lineRenderer.SetPosition(4, new Vector3(-radius, 0, -radius)); // Close the square

        // Add to our tracking list
        noBuildZoneVisuals.Add(zoneObj);
    }

    // Create no-build zone preview for the ghost building (follows the ghost)
    void CreateGhostNoBuildZone()
    {
        if (ghostNoBuildZone != null)
        {
            Destroy(ghostNoBuildZone);
        }

        GameObject zoneObj = new GameObject("GhostNoBuildZone");
        zoneObj.transform.position = new Vector3(currentGhost.transform.position.x, 0.05f, currentGhost.transform.position.z);

        float radius = ghostBuildingNoBuildRadius;

        // Create the filled square (quad) if enabled
        if (showNoBuildFills)
        {
            GameObject fillObj = new GameObject("Fill");
            fillObj.transform.SetParent(zoneObj.transform);
            fillObj.transform.localPosition = Vector3.zero;
            fillObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            MeshFilter meshFilter = fillObj.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = fillObj.AddComponent<MeshRenderer>();

            // Create a quad mesh
            Mesh mesh = new Mesh();
            mesh.vertices = new Vector3[]
            {
                new Vector3(-radius, -radius, 0),
                new Vector3(radius, -radius, 0),
                new Vector3(radius, radius, 0),
                new Vector3(-radius, radius, 0)
            };
            mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
            mesh.uv = new Vector2[]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(1, 1),
                new Vector2(0, 1)
            };
            mesh.RecalculateNormals();
            meshFilter.mesh = mesh;

            // Create semi-transparent blue/cyan material for ghost preview (different from existing buildings)
            Material fillMaterial = new Material(Shader.Find("Standard"));
            Color ghostZoneColor = new Color(0.3f, 0.7f, 1f, 0.25f);  // Light blue, more transparent
            fillMaterial.color = ghostZoneColor;
            fillMaterial.SetFloat("_Mode", 3);
            fillMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            fillMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            fillMaterial.SetInt("_ZWrite", 0);
            fillMaterial.DisableKeyword("_ALPHATEST_ON");
            fillMaterial.EnableKeyword("_ALPHABLEND_ON");
            fillMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            fillMaterial.renderQueue = 3000;
            meshRenderer.material = fillMaterial;
        }

        // Create the border using LineRenderer
        GameObject borderObj = new GameObject("Border");
        borderObj.transform.SetParent(zoneObj.transform);
        borderObj.transform.localPosition = new Vector3(0, 0.01f, 0);

        LineRenderer lineRenderer = borderObj.AddComponent<LineRenderer>();
        lineRenderer.positionCount = 5;
        lineRenderer.loop = true;
        lineRenderer.useWorldSpace = false;

        // Cyan border for ghost preview
        lineRenderer.startWidth = 0.15f;
        lineRenderer.endWidth = 0.15f;
        Color borderColor = new Color(0.3f, 0.9f, 1f, 0.6f);  // Bright cyan
        lineRenderer.startColor = borderColor;
        lineRenderer.endColor = borderColor;

        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.material.color = borderColor;

        // Set square corner positions
        lineRenderer.SetPosition(0, new Vector3(-radius, 0, -radius));
        lineRenderer.SetPosition(1, new Vector3(radius, 0, -radius));
        lineRenderer.SetPosition(2, new Vector3(radius, 0, radius));
        lineRenderer.SetPosition(3, new Vector3(-radius, 0, radius));
        lineRenderer.SetPosition(4, new Vector3(-radius, 0, -radius));

        ghostNoBuildZone = zoneObj;
    }
}
