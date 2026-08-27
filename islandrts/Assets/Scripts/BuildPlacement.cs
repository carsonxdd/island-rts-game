using UnityEngine;

/// <summary>
/// Build-mode coordinator: owns the serialized config, the shared ghost
/// object, and per-frame mode dispatch. The actual work lives in four plain
/// helper classes — WallLinePlacer (click-start/click-end wall lines),
/// GhostPlacer (single-building placement), DemolishTool (Delete/X demolish
/// mode), and NoBuildZoneRenderer (zone visuals). Helpers are plain C#
/// classes, not MonoBehaviours, so the scene GameObject setup is unchanged.
/// </summary>
public class BuildPlacement : MonoBehaviour
{
    [Header("Building Selection")]
    public BuildingType selectedBuildingType = BuildingType.Hut;
    public BuildingSelectionUI selectionUI;  // Optional: Visual feedback for building selection

    internal float ghostBuildingVisualNoBuildRadius = 3.5f;  // Visual-only radius for ghost border (set from BuildingData)

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

    // Shared runtime state (internal so the helpers can access it; internal
    // fields are not serialized, so the Inspector is unchanged)
    internal Camera mainCam;
    internal GameObject currentGhost;
    internal Renderer ghostRenderer;
    // Every material slot on the ghost, instanced once when the ghost is created. The
    // low-poly building meshes are multi-submesh, so tinting `ghostRenderer.material`
    // would only recolor slot 0; reading `.materials` per frame would allocate.
    internal Material[] ghostMaterials;
    internal bool isPlacing = false;

    // Mode helpers
    internal WallLinePlacer wallPlacer;
    internal GhostPlacer ghostPlacer;
    internal DemolishTool demolishTool;
    internal NoBuildZoneRenderer zoneRenderer;

    void Start()
    {
        mainCam = Camera.main;
        if (mainCam == null)
        {
            Debug.LogError("BuildPlacement: No Main Camera found!");
        }

        wallPlacer = new WallLinePlacer(this);
        ghostPlacer = new GhostPlacer(this);
        demolishTool = new DemolishTool(this);
        zoneRenderer = new NoBuildZoneRenderer(this);
    }

    void Update()
    {
        // Delete key: toggle demolish mode (works outside build mode)
        if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.X))
        {
            if (demolishTool.IsActive)
            {
                demolishTool.Exit();
            }
            else
            {
                // Cancel build mode if active, then enter demolish
                if (isPlacing) CancelPlacement();
                demolishTool.Enter();
            }
            return;
        }

        // Demolish mode update
        if (demolishTool.IsActive)
        {
            demolishTool.Tick();
            return;
        }

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
            if (Input.GetKeyDown(KeyCode.Alpha5)) SelectBuilding(BuildingType.Workshop);

            // G key: convert wall under cursor to gate (grid-based detection)
            if (Input.GetKeyDown(KeyCode.G))
            {
                TryConvertWallToGate();
            }

            if (IsWallType(selectedBuildingType))
            {
                // Wall placement uses click-start + click-end line drawing
                wallPlacer.Tick();
            }
            else
            {
                // Non-wall placement: single click to place
                ghostPlacer.Tick();
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
        if (IsWallType(selectedBuildingType))
        {
            currentGhost = wallPlacer.CreateWallCursorGhost(data);
        }
        else
        {
            currentGhost = Instantiate(data.ghostPrefab);
        }
        CacheGhostRenderer();

        // Update building properties from data
        buildingSize = data.buildingSize;
        placementHeight = data.placementHeight;
        ghostBuildingVisualNoBuildRadius = data.visualNoBuildRadius;

        // Initialize ghost placer state (target position, rotation reset)
        ghostPlacer.ResetForNewGhost(currentGhost.transform.position);

        isPlacing = true;
        wallPlacer.ResetLineState();  // Reset wall line state

        // Create visual no-build zones for existing buildings (not when placing walls)
        // and the no-build zone preview for the ghost building
        if (!IsWallType(selectedBuildingType))
        {
            if (showNoBuildZones)
            {
                zoneRenderer.CreateZoneVisuals();
            }
            zoneRenderer.CreateGhostZone(currentGhost.transform.position, ghostBuildingVisualNoBuildRadius);
        }

        // Update UI if exists
        if (selectionUI != null)
        {
            bool canAfford = ResourceManager.Instance.CanAfford(data.woodCost, data.foodCost, data.stoneCost);
            selectionUI.UpdateDisplay(data, canAfford);
            selectionUI.Show();
        }
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

        // Clean up wall line state if switching types
        wallPlacer.ResetLineState();

        // Destroy old ghost
        if (currentGhost != null)
        {
            Destroy(currentGhost);
        }

        // Destroy old ghost no-build zone and zone visuals
        zoneRenderer.DestroyGhostZone();
        zoneRenderer.DestroyZoneVisuals();

        // Update selected building type
        selectedBuildingType = type;

        // Spawn new ghost from building data
        if (IsWallType(type))
        {
            currentGhost = wallPlacer.CreateWallCursorGhost(data);
        }
        else
        {
            currentGhost = Instantiate(data.ghostPrefab);
        }
        CacheGhostRenderer();

        // Update building size and placement height for collision detection
        buildingSize = data.buildingSize;
        placementHeight = data.placementHeight;
        ghostBuildingVisualNoBuildRadius = data.visualNoBuildRadius;

        // Re-target the ghost placer (rotation is kept across type switches)
        ghostPlacer.SetTargetPosition(currentGhost.transform.position);

        // Create no-build zone visuals and ghost zone (not for walls)
        if (!IsWallType(type))
        {
            if (showNoBuildZones)
            {
                zoneRenderer.CreateZoneVisuals();
            }
            zoneRenderer.CreateGhostZone(currentGhost.transform.position, ghostBuildingVisualNoBuildRadius);
        }

        // Update UI if exists
        if (selectionUI != null)
        {
            bool canAfford = ResourceManager.Instance.CanAfford(data.woodCost, data.foodCost, data.stoneCost);
            selectionUI.UpdateDisplay(data, canAfford);
        }
    }

    internal void CancelPlacement()
    {
        // Clean up wall line ghosts if any
        wallPlacer.ResetLineState();

        // Destroy the ghost
        if (currentGhost != null)
        {
            Destroy(currentGhost);
            currentGhost = null;
        }

        // Destroy no-build zone visuals and ghost zone
        zoneRenderer.DestroyZoneVisuals();
        zoneRenderer.DestroyGhostZone();

        // Hide UI if exists
        if (selectionUI != null)
        {
            selectionUI.Hide();
        }

        isPlacing = false;
    }

    /// <summary>
    /// Convert the wall at the mouse's grid position to a gate (costs 5 wood).
    /// Uses WallGrid lookup instead of raycast so it works regardless of camera angle.
    /// </summary>
    void TryConvertWallToGate()
    {
        if (WallGrid.Instance == null) return;

        // Get the grid cell under the mouse cursor
        Vector3 snapped;
        if (!GetSnappedMousePosition(out snapped)) return;

        Vector2Int gridPos = WallGrid.Instance.WorldToGrid(snapped);

        // Look up what's at this grid position
        MonoBehaviour occupant = WallGrid.Instance.GetWallAt(gridPos);
        if (occupant == null)
        {
            return;
        }

        // Must be a Wall (not already a Gate or ConstructionSite)
        Wall wall = occupant as Wall;
        if (wall == null)
        {
            return;
        }

        // Check cost: 5 wood
        if (ResourceManager.Instance == null) return;

        if (!ResourceManager.Instance.CanAfford(5, 0, 0))
        {
            return;
        }

        ResourceManager.Instance.SpendResources(5, 0, 0);

        // Play sound
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayBuildingPlaced();
        }

        wall.UpgradeToGate();
    }

    /// <summary>
    /// Cache the ghost's renderer and instance its material slots. Called once per ghost
    /// spawn so the per-frame validity tint is a plain array walk with no allocation.
    /// </summary>
    private void CacheGhostRenderer()
    {
        ghostRenderer = currentGhost.GetComponent<Renderer>();

        if (ghostRenderer == null)
        {
            ghostRenderer = currentGhost.GetComponent<MeshRenderer>();
        }

        ghostMaterials = RendererTint.Collect(ghostRenderer);
    }

    /// <summary>
    /// Tint the whole ghost (every submesh slot) to indicate placement validity.
    /// </summary>
    internal void SetGhostColor(Color color)
    {
        RendererTint.SetColor(ghostMaterials, color);
    }

    /// <summary>
    /// Raycast to ground and return grid-snapped position.
    /// </summary>
    internal bool GetSnappedMousePosition(out Vector3 snapped)
    {
        snapped = Vector3.zero;

        Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, 1000f, groundLayer))
        {
            snapped = GridSnap.SnapXZ(hit.point, cellSize);
            snapped.y = GroundYAt(snapped) + placementHeight;
            return true;
        }

        // Fallback: plane at y=0
        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
        float distance;
        if (groundPlane.Raycast(ray, out distance))
        {
            Vector3 worldPos = ray.GetPoint(distance);
            snapped = GridSnap.SnapXZ(worldPos, cellSize);
            snapped.y = GroundYAt(snapped) + placementHeight;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Terrain height at a position (0 on the legacy flat world).
    /// placementHeight semantics are now "offset above the ground here".
    /// </summary>
    internal float GroundYAt(Vector3 pos)
    {
        return TerrainGrid.Instance != null ? TerrainGrid.Instance.SampleHeight(pos) : 0f;
    }

    // Check if a building type is a wall using BuildingData.isWall flag
    internal bool IsWallType(BuildingType type)
    {
        if (BuildingDatabase.Instance == null) return false;
        BuildingData data = BuildingDatabase.Instance.GetBuildingData(type);
        return data != null && data.isWall;
    }
}
