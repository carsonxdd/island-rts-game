using UnityEngine;
using System.Collections.Generic;

public class BuildPlacement : MonoBehaviour
{
    [Header("Building Selection")]
    public BuildingType selectedBuildingType = BuildingType.Hut;
    public BuildingSelectionUI selectionUI;  // Optional: Visual feedback for building selection

    private float ghostBuildingVisualNoBuildRadius = 3.5f;  // Visual-only radius for ghost border (set from BuildingData)

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
    private float currentRotation = 0f;  // Current ghost rotation in degrees
    private List<GameObject> noBuildZoneVisuals = new List<GameObject>();  // Visual circles for no-build zones
    private GameObject ghostNoBuildZone;  // No-build zone preview for the ghost building

    // Wall line drawing state
    private bool isDrawingWallLine = false;
    private Vector3 wallLineStart;
    private List<GameObject> wallLineGhosts = new List<GameObject>();
    private List<Vector3> wallLinePositions = new List<Vector3>();

    // L-shaped path mode
    private bool xFirst = true;  // true = go along X first, then Z; toggled with R

    // Demolish mode (Delete key)
    private bool isDemolishing = false;
    private GameObject demolishHighlight;  // Red highlight on targeted building
    private static readonly Color demolishColor = new Color(1f, 0.2f, 0.2f, 0.4f);

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
        // Delete key: toggle demolish mode (works outside build mode)
        if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.X))
        {
            if (isDemolishing)
            {
                ExitDemolishMode();
            }
            else
            {
                // Cancel build mode if active, then enter demolish
                if (isPlacing) CancelPlacement();
                EnterDemolishMode();
            }
            return;
        }

        // Demolish mode update
        if (isDemolishing)
        {
            UpdateDemolishMode();
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

            // G key: convert wall under cursor to gate (grid-based detection)
            if (Input.GetKeyDown(KeyCode.G))
            {
                TryConvertWallToGate();
            }

            if (IsWallType(selectedBuildingType))
            {
                // Wall placement uses click-start + click-end line drawing
                UpdateWallPlacement();
            }
            else
            {
                // Non-wall placement: single click to place
                if (Input.GetKeyDown(KeyCode.R))
                {
                    RotateGhost();
                }

                MoveGhostToMouse();

                if (Input.GetMouseButtonDown(0))
                {
                    ConfirmPlacement();
                }

                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
                {
                    CancelPlacement();
                }
            }
        }
    }

    // =============================================
    // Wall Line Drawing System
    // =============================================

    /// <summary>
    /// Handle wall placement with click-start + click-end line drawing.
    /// Phase 1: Single ghost follows mouse. Click to set start point.
    /// Phase 2: Ghost line from start to mouse. Click to confirm all walls.
    /// </summary>
    void UpdateWallPlacement()
    {
        Vector3 snapped;
        if (!GetSnappedMousePosition(out snapped)) return;

        if (!isDrawingWallLine)
        {
            // Phase 1: Cursor ghost follows mouse, waiting for first click
            Vector3 cursorPos = snapped;
            cursorPos.y = 0.02f; // Match wall Y offset
            currentGhost.transform.position = cursorPos;

            // R toggles L-path direction (X-first vs Z-first) before starting a line
            if (Input.GetKeyDown(KeyCode.R))
            {
                xFirst = !xFirst;
            }

            // Color ghost based on whether this cell is occupied
            bool occupied = HasWallAtPosition(snapped);
            if (ghostRenderer != null)
            {
                ghostRenderer.material.color = occupied ? wallGhostInvalidColor : wallGhostColor;
            }

            // Show single-wall cost in UI
            BuildingData data = BuildingDatabase.Instance != null
                ? BuildingDatabase.Instance.GetBuildingData(selectedBuildingType)
                : null;
            if (selectionUI != null && data != null)
            {
                bool canAfford = ResourceManager.Instance.CanAfford(data.woodCost, data.foodCost, data.stoneCost);
                selectionUI.UpdateDisplay(data, canAfford);
            }

            // Left click: set start point and begin drawing line
            if (Input.GetMouseButtonDown(0))
            {
                wallLineStart = snapped;
                isDrawingWallLine = true;
                currentGhost.SetActive(false);  // Hide cursor ghost; line ghosts take over
            }

            // Cancel: exit build mode
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                CancelPlacement();
            }
        }
        else
        {
            // Phase 2: Drawing line from start to current mouse position
            // R toggles L-path direction while drawing
            if (Input.GetKeyDown(KeyCode.R))
            {
                xFirst = !xFirst;
            }

            UpdateWallLinePreview(snapped);

            // Left click: confirm and place all walls in the line
            if (Input.GetMouseButtonDown(0))
            {
                ConfirmWallLine();
            }

            // Cancel: discard line, return to cursor mode
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                CancelWallLine();
            }
        }
    }

    /// <summary>
    /// Update the wall line preview: compute grid positions along the line,
    /// spawn/update ghost objects, show total cost.
    /// Default: L-shaped path (R toggles X-first vs Z-first).
    /// Hold Shift: Bresenham staircase path.
    /// Wall shapes are auto-determined by WallGrid neighbors, so no per-wall rotation is needed.
    /// </summary>
    void UpdateWallLinePreview(Vector3 endSnapped)
    {
        // Choose path algorithm based on Shift modifier
        bool useBresenham = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (useBresenham)
        {
            wallLinePositions = GetGridLine(wallLineStart, endSnapped);
        }
        else
        {
            wallLinePositions = GetLShapedLine(wallLineStart, endSnapped, xFirst);
        }

        BuildingData data = BuildingDatabase.Instance != null
            ? BuildingDatabase.Instance.GetBuildingData(selectedBuildingType)
            : null;
        if (data == null || data.ghostPrefab == null) return;

        bool isStone = data.buildingType == BuildingType.StoneWall;

        // Build a HashSet of ghost grid positions for neighbor lookups
        HashSet<Vector2Int> ghostGridPositions = new HashSet<Vector2Int>();
        List<Vector2Int> ghostGridList = new List<Vector2Int>();
        for (int i = 0; i < wallLinePositions.Count; i++)
        {
            Vector2Int gp = WallGrid.Instance.WorldToGrid(wallLinePositions[i]);
            ghostGridPositions.Add(gp);
            ghostGridList.Add(gp);
        }

        // Grow ghost pool if needed — use simple GameObjects with MeshFilter+MeshRenderer
        while (wallLineGhosts.Count < wallLinePositions.Count)
        {
            GameObject ghost = new GameObject("WallGhost");
            ghost.AddComponent<MeshFilter>();
            MeshRenderer mr = ghost.AddComponent<MeshRenderer>();
            mr.material = CreateWallGhostMaterial();
            wallLineGhosts.Add(ghost);
        }

        int validCount = 0;

        // Position, shape, rotate, and color each ghost
        for (int i = 0; i < wallLinePositions.Count; i++)
        {
            GameObject ghost = wallLineGhosts[i];
            ghost.SetActive(true);
            ghost.transform.localScale = Vector3.one;

            // Place at grid position, slightly above ground
            Vector3 pos = wallLinePositions[i];
            pos.y = 0.02f;
            ghost.transform.position = pos;

            bool occupied = HasWallAtPosition(wallLinePositions[i]);
            if (!occupied) validCount++;

            // Compute neighbor mask considering both existing walls and other ghosts in the line
            int mask = WallConnector.GetPreviewNeighborMask(ghostGridList[i], ghostGridPositions);

            // Get shape and rotation
            WallConnector.WallShape shape;
            float yRot;
            WallConnector.GetShapeAndRotation(mask, out shape, out yRot);

            // Apply procedural mesh
            MeshFilter mf = ghost.GetComponent<MeshFilter>();
            if (mf != null)
            {
                mf.mesh = WallConnector.GetOrCreateMesh(shape, isStone);
            }
            ghost.transform.rotation = Quaternion.Euler(0f, yRot, 0f);

            // Color: light blue if valid, red if occupied
            MeshRenderer renderer = ghost.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.material.color = occupied ? wallGhostInvalidColor : wallGhostColor;
            }
        }

        // Hide excess ghosts from pool
        for (int i = wallLinePositions.Count; i < wallLineGhosts.Count; i++)
        {
            wallLineGhosts[i].SetActive(false);
        }

        // Update UI with total cost for the line
        if (selectionUI != null && data != null)
        {
            int totalWood = data.woodCost * validCount;
            int totalFood = data.foodCost * validCount;
            int totalStone = data.stoneCost * validCount;
            bool canAfford = ResourceManager.Instance.CanAfford(totalWood, totalFood, totalStone);
            selectionUI.UpdateWallLineDisplay(data, validCount, canAfford);
        }
    }

    /// <summary>
    /// Compute grid cell positions along a line using Bresenham's algorithm.
    /// Diagonal steps are split into horizontal + vertical so every corner
    /// is filled (no gaps in the staircase).
    /// </summary>
    List<Vector3> GetGridLine(Vector3 start, Vector3 end)
    {
        int x0 = Mathf.RoundToInt(start.x / cellSize);
        int z0 = Mathf.RoundToInt(start.z / cellSize);
        int x1 = Mathf.RoundToInt(end.x / cellSize);
        int z1 = Mathf.RoundToInt(end.z / cellSize);

        List<Vector3> positions = new List<Vector3>();

        int dx = Mathf.Abs(x1 - x0);
        int dz = Mathf.Abs(z1 - z0);
        int sx = x0 < x1 ? 1 : -1;
        int sz = z0 < z1 ? 1 : -1;
        int err = dx - dz;

        while (true)
        {
            positions.Add(new Vector3(x0 * cellSize, placementHeight, z0 * cellSize));

            if (x0 == x1 && z0 == z1) break;

            int e2 = 2 * err;
            bool stepX = e2 > -dz;
            bool stepZ = e2 < dx;

            if (stepX && stepZ)
            {
                // Would be diagonal — split into horizontal step then vertical step
                // so the corner cell is filled (no gap)
                err -= dz; x0 += sx;
                positions.Add(new Vector3(x0 * cellSize, placementHeight, z0 * cellSize));
                err += dx; z0 += sz;
            }
            else
            {
                if (stepX) { err -= dz; x0 += sx; }
                if (stepZ) { err += dx; z0 += sz; }
            }
        }

        return positions;
    }

    /// <summary>
    /// Compute an L-shaped path from start to end.
    /// Goes along the X axis first (if xFirst), then Z axis (or vice versa).
    /// Returns List of grid positions with no duplicates.
    /// </summary>
    List<Vector3> GetLShapedLine(Vector3 start, Vector3 end, bool doXFirst)
    {
        int x0 = Mathf.RoundToInt(start.x / cellSize);
        int z0 = Mathf.RoundToInt(start.z / cellSize);
        int x1 = Mathf.RoundToInt(end.x / cellSize);
        int z1 = Mathf.RoundToInt(end.z / cellSize);

        List<Vector3> positions = new List<Vector3>();

        if (doXFirst)
        {
            // Walk along X first
            int sx = x0 < x1 ? 1 : -1;
            for (int x = x0; x != x1; x += sx)
            {
                positions.Add(new Vector3(x * cellSize, placementHeight, z0 * cellSize));
            }
            // Then walk along Z
            int sz = z0 < z1 ? 1 : -1;
            for (int z = z0; z != z1; z += sz)
            {
                positions.Add(new Vector3(x1 * cellSize, placementHeight, z * cellSize));
            }
            // Add final position
            positions.Add(new Vector3(x1 * cellSize, placementHeight, z1 * cellSize));
        }
        else
        {
            // Walk along Z first
            int sz = z0 < z1 ? 1 : -1;
            for (int z = z0; z != z1; z += sz)
            {
                positions.Add(new Vector3(x0 * cellSize, placementHeight, z * cellSize));
            }
            // Then walk along X
            int sx = x0 < x1 ? 1 : -1;
            for (int x = x0; x != x1; x += sx)
            {
                positions.Add(new Vector3(x * cellSize, placementHeight, z1 * cellSize));
            }
            // Add final position
            positions.Add(new Vector3(x1 * cellSize, placementHeight, z1 * cellSize));
        }

        return positions;
    }

    /// <summary>
    /// Raycast to ground and return grid-snapped position.
    /// </summary>
    bool GetSnappedMousePosition(out Vector3 snapped)
    {
        snapped = Vector3.zero;

        Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, 1000f, groundLayer))
        {
            snapped = GridSnap.SnapXZ(hit.point, cellSize);
            snapped.y = placementHeight;
            return true;
        }

        // Fallback: plane at y=0
        Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
        float distance;
        if (groundPlane.Raycast(ray, out distance))
        {
            Vector3 worldPos = ray.GetPoint(distance);
            snapped = GridSnap.SnapXZ(worldPos, cellSize);
            snapped.y = placementHeight;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Confirm wall line: deduct total cost, place construction sites at all valid positions.
    /// Returns to cursor mode for continuous wall building.
    /// </summary>
    void ConfirmWallLine()
    {
        if (BuildingDatabase.Instance == null || ResourceManager.Instance == null) return;

        BuildingData data = BuildingDatabase.Instance.GetBuildingData(selectedBuildingType);
        if (data == null || data.constructionSitePrefab == null) return;

        // Collect valid (non-occupied) positions
        List<Vector3> validPositions = new List<Vector3>();
        for (int i = 0; i < wallLinePositions.Count; i++)
        {
            if (!HasWallAtPosition(wallLinePositions[i]))
            {
                validPositions.Add(wallLinePositions[i]);
            }
        }

        if (validPositions.Count == 0)
        {
            CancelWallLine();
            return;
        }

        // Check total cost
        int totalWood = data.woodCost * validPositions.Count;
        int totalFood = data.foodCost * validPositions.Count;
        int totalStone = data.stoneCost * validPositions.Count;

        if (!ResourceManager.Instance.CanAfford(totalWood, totalFood, totalStone))
        {
            return;
        }

        // Deduct resources (once for the entire line)
        ResourceManager.Instance.SpendResources(totalWood, totalFood, totalStone);

        // Place construction sites at each valid position (rotation auto-determined by WallGrid)
        for (int i = 0; i < validPositions.Count; i++)
        {
            GameObject constructionSite = Instantiate(
                data.constructionSitePrefab,
                validPositions[i],
                Quaternion.identity
            );

            constructionSite.layer = LayerMask.NameToLayer("Buildings");

            ConstructionSite siteComponent = constructionSite.GetComponent<ConstructionSite>();
            if (siteComponent != null)
            {
                siteComponent.SetBuildingType(selectedBuildingType);
            }
        }

        // Play sound
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayBuildingPlaced();
        }

        // Clean up and return to cursor mode for next line
        ClearWallLineGhosts();
        isDrawingWallLine = false;
        currentGhost.SetActive(true);

        // Restore single-wall cost display
        if (selectionUI != null)
        {
            bool canAfford = ResourceManager.Instance.CanAfford(data.woodCost, data.foodCost, data.stoneCost);
            selectionUI.UpdateDisplay(data, canAfford);
        }
    }

    /// <summary>
    /// Cancel wall line drawing, return to cursor mode.
    /// </summary>
    void CancelWallLine()
    {
        ClearWallLineGhosts();
        isDrawingWallLine = false;
        currentGhost.SetActive(true);

        // Restore single-wall cost display
        BuildingData data = BuildingDatabase.Instance != null
            ? BuildingDatabase.Instance.GetBuildingData(selectedBuildingType)
            : null;
        if (selectionUI != null && data != null)
        {
            bool canAfford = ResourceManager.Instance.CanAfford(data.woodCost, data.foodCost, data.stoneCost);
            selectionUI.UpdateDisplay(data, canAfford);
        }
    }

    /// <summary>
    /// Destroy all wall line ghost objects and clear the lists.
    /// </summary>
    void ClearWallLineGhosts()
    {
        foreach (GameObject ghost in wallLineGhosts)
        {
            if (ghost != null) Destroy(ghost);
        }
        wallLineGhosts.Clear();
        wallLinePositions.Clear();
    }

    /// <summary>
    /// Create a simple procedural ghost for the wall cursor (single isolated shape).
    /// Uses the same transparent material as line ghosts.
    /// </summary>
    // Shared ghost material — light blue, semi-transparent
    private static readonly Color wallGhostColor = new Color(0.4f, 0.7f, 1f, 0.35f);
    private static readonly Color wallGhostInvalidColor = new Color(1f, 0.3f, 0.3f, 0.35f);

    Material CreateWallGhostMaterial()
    {
        Material mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = wallGhostColor;
        return mat;
    }

    GameObject CreateWallCursorGhost(BuildingData data)
    {
        bool isStone = data.buildingType == BuildingType.StoneWall;
        GameObject ghost = new GameObject("WallCursorGhost");
        MeshFilter mf = ghost.AddComponent<MeshFilter>();
        mf.mesh = WallConnector.GetOrCreateMesh(WallConnector.WallShape.Isolated, isStone);
        MeshRenderer mr = ghost.AddComponent<MeshRenderer>();
        mr.material = CreateWallGhostMaterial();
        return ghost;
    }

    // =============================================
    // Wall-to-Gate Conversion
    // =============================================

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

    // =============================================
    // Demolish Mode (Delete/X key)
    // =============================================

    void EnterDemolishMode()
    {
        isDemolishing = true;
    }

    void ExitDemolishMode()
    {
        isDemolishing = false;
        if (demolishHighlight != null)
        {
            Destroy(demolishHighlight);
            demolishHighlight = null;
        }
    }

    void UpdateDemolishMode()
    {
        // ESC or right-click exits demolish mode
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
        {
            ExitDemolishMode();
            return;
        }

        // Raycast to find building under cursor
        if (mainCam == null) return;
        Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        GameObject targetObj = null;
        BuildingType? targetType = null;

        if (Physics.Raycast(ray, out hit, 1000f))
        {
            // Try to identify the building type
            Wall wall = hit.collider.GetComponent<Wall>();
            if (wall == null) wall = hit.collider.GetComponentInParent<Wall>();
            if (wall != null)
            {
                targetObj = wall.gameObject;
                targetType = wall.isStoneWall ? BuildingType.StoneWall : BuildingType.WoodenWall;
            }

            if (targetObj == null)
            {
                Gate gate = hit.collider.GetComponent<Gate>();
                if (gate == null) gate = hit.collider.GetComponentInParent<Gate>();
                if (gate != null)
                {
                    targetObj = gate.gameObject;
                    targetType = gate.isStoneGate ? BuildingType.StoneWall : BuildingType.WoodenWall;
                }
            }

            if (targetObj == null)
            {
                Hut hut = hit.collider.GetComponent<Hut>();
                if (hut == null) hut = hit.collider.GetComponentInParent<Hut>();
                if (hut != null)
                {
                    targetObj = hut.gameObject;
                    targetType = BuildingType.Hut;
                }
            }

            if (targetObj == null)
            {
                Watchtower tower = hit.collider.GetComponent<Watchtower>();
                if (tower == null) tower = hit.collider.GetComponentInParent<Watchtower>();
                if (tower != null)
                {
                    targetObj = tower.gameObject;
                    targetType = BuildingType.Watchtower;
                }
            }

            if (targetObj == null)
            {
                ConstructionSite site = hit.collider.GetComponent<ConstructionSite>();
                if (site == null) site = hit.collider.GetComponentInParent<ConstructionSite>();
                if (site != null)
                {
                    targetObj = site.gameObject;
                    targetType = site.buildingType;
                }
            }

            // Don't allow demolishing the campfire
            if (targetObj == null || targetType == null)
            {
                BaseBuilding bb = hit.collider.GetComponent<BaseBuilding>();
                if (bb == null) bb = hit.collider.GetComponentInParent<BaseBuilding>();
                if (bb != null)
                {
                    targetObj = null; // Block campfire demolish
                }
            }
        }

        // Update highlight
        UpdateDemolishHighlight(targetObj);

        // Left click: demolish
        if (Input.GetMouseButtonDown(0) && targetObj != null && targetType.HasValue)
        {
            DemolishBuilding(targetObj, targetType.Value);
        }
    }

    void UpdateDemolishHighlight(GameObject target)
    {
        if (target == null)
        {
            if (demolishHighlight != null)
            {
                Destroy(demolishHighlight);
                demolishHighlight = null;
            }
            return;
        }

        // Create or reposition highlight
        if (demolishHighlight == null)
        {
            demolishHighlight = GameObject.CreatePrimitive(PrimitiveType.Cube);
            demolishHighlight.name = "DemolishHighlight";
            Destroy(demolishHighlight.GetComponent<Collider>());
            MeshRenderer mr = demolishHighlight.GetComponent<MeshRenderer>();
            mr.material = new Material(Shader.Find("Sprites/Default"));
            mr.material.color = demolishColor;
        }

        // Scale highlight to match target bounds
        Renderer targetRenderer = target.GetComponentInChildren<Renderer>();
        if (targetRenderer != null)
        {
            Bounds bounds = targetRenderer.bounds;
            demolishHighlight.transform.position = bounds.center;
            demolishHighlight.transform.localScale = bounds.size + Vector3.one * 0.2f;
        }
        else
        {
            demolishHighlight.transform.position = target.transform.position + Vector3.up;
            demolishHighlight.transform.localScale = new Vector3(1.2f, 2.2f, 1.2f);
        }
    }

    void DemolishBuilding(GameObject buildingObj, BuildingType type)
    {
        if (ResourceManager.Instance == null || BuildingDatabase.Instance == null) return;

        // Get building data for refund calculation
        BuildingData data = BuildingDatabase.Instance.GetBuildingData(type);
        if (data != null)
        {
            // 50% refund
            int woodRefund = Mathf.FloorToInt(data.woodCost * 0.5f);
            int foodRefund = Mathf.FloorToInt(data.foodCost * 0.5f);
            int stoneRefund = Mathf.FloorToInt(data.stoneCost * 0.5f);

            if (woodRefund > 0) ResourceManager.Instance.AddWood(woodRefund);
            if (foodRefund > 0) ResourceManager.Instance.AddFood(foodRefund);
            if (stoneRefund > 0) ResourceManager.Instance.AddStone(stoneRefund);
        }

        // Play sound
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayBuildingPlaced();
        }

        // Destroy the building (OnDestroy handlers in Wall/Gate/Hut handle grid unregistration)
        Destroy(buildingObj);

        // Clear highlight
        if (demolishHighlight != null)
        {
            Destroy(demolishHighlight);
            demolishHighlight = null;
        }
    }

    // =============================================
    // Standard (Non-Wall) Placement
    // =============================================

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
            currentGhost = CreateWallCursorGhost(data);
        }
        else
        {
            currentGhost = Instantiate(data.ghostPrefab);
        }
        ghostRenderer = currentGhost.GetComponent<Renderer>();

        if (ghostRenderer == null)
        {
            ghostRenderer = currentGhost.GetComponent<MeshRenderer>();
        }

        // Update building properties from data
        buildingSize = data.buildingSize;
        placementHeight = data.placementHeight;
        ghostBuildingVisualNoBuildRadius = data.visualNoBuildRadius;

        // Initialize target position
        targetPosition = currentGhost.transform.position;

        isPlacing = true;
        isValidPlacement = false;
        currentRotation = 0f;  // Reset rotation when entering build mode
        isDrawingWallLine = false;  // Reset wall line state

        // Create visual no-build zones for existing buildings (not when placing walls)
        if (showNoBuildZones && !IsWallType(selectedBuildingType))
        {
            CreateNoBuildZoneVisuals();
        }

        // Create no-build zone preview for the ghost building (not for walls)
        if (!IsWallType(selectedBuildingType))
        {
            CreateGhostNoBuildZone();
        }

        // Update UI if exists
        if (selectionUI != null)
        {
            bool canAfford = ResourceManager.Instance.CanAfford(data.woodCost, data.foodCost, data.stoneCost);
            selectionUI.UpdateDisplay(data, canAfford);
            selectionUI.Show();
        }
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

            // Smooth lerp for non-wall buildings
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

            // Check for collisions at TARGET position
            bool hasCollision = Physics.CheckBox(
                targetPosition,
                buildingSize * 0.5f,  // Half extents
                Quaternion.Euler(0f, currentRotation, 0f),
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
            return;
        }

        // Deduct resources
        ResourceManager.Instance.SpendResources(data.woodCost, data.foodCost, data.stoneCost);

        // Use target position (where ghost is moving to) not current position
        Vector3 buildPosition = targetPosition;

        // Spawn the construction site with the ghost's rotation
        GameObject constructionSite = Instantiate(
            data.constructionSitePrefab,
            buildPosition,
            Quaternion.Euler(0f, currentRotation, 0f)
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

        // NON-WALL: Exit build mode after placing
        Destroy(currentGhost);
        DestroyNoBuildZoneVisuals();

        if (ghostNoBuildZone != null)
        {
            Destroy(ghostNoBuildZone);
            ghostNoBuildZone = null;
        }

        if (selectionUI != null)
        {
            selectionUI.Hide();
        }

        currentGhost = null;
        ghostRenderer = null;
        isPlacing = false;
    }

    void CancelPlacement()
    {
        // Clean up wall line ghosts if any
        ClearWallLineGhosts();
        isDrawingWallLine = false;

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

        // Clean up wall line state if switching types
        ClearWallLineGhosts();
        isDrawingWallLine = false;

        // Destroy old ghost
        if (currentGhost != null)
        {
            Destroy(currentGhost);
        }

        // Destroy old ghost no-build zone
        if (ghostNoBuildZone != null)
        {
            Destroy(ghostNoBuildZone);
            ghostNoBuildZone = null;
        }

        // Destroy old no-build zone visuals
        DestroyNoBuildZoneVisuals();

        // Update selected building type
        selectedBuildingType = type;

        // Spawn new ghost from building data
        if (IsWallType(type))
        {
            currentGhost = CreateWallCursorGhost(data);
        }
        else
        {
            currentGhost = Instantiate(data.ghostPrefab);
        }
        ghostRenderer = currentGhost.GetComponent<Renderer>();
        if (ghostRenderer == null)
        {
            ghostRenderer = currentGhost.GetComponent<MeshRenderer>();
        }

        // Update building size and placement height for collision detection
        buildingSize = data.buildingSize;
        placementHeight = data.placementHeight;
        ghostBuildingVisualNoBuildRadius = data.visualNoBuildRadius;

        // Initialize target position
        targetPosition = currentGhost.transform.position;

        // Create no-build zone visuals and ghost zone (not for walls)
        if (!IsWallType(type))
        {
            if (showNoBuildZones)
            {
                CreateNoBuildZoneVisuals();
            }
            CreateGhostNoBuildZone();
        }

        // Update UI if exists
        if (selectionUI != null)
        {
            bool canAfford = ResourceManager.Instance.CanAfford(data.woodCost, data.foodCost, data.stoneCost);
            selectionUI.UpdateDisplay(data, canAfford);
        }
    }

    /// <summary>
    /// Rotate the ghost building 90 degrees when R is pressed
    /// </summary>
    void RotateGhost()
    {
        currentRotation += 90f;
        if (currentRotation >= 360f) currentRotation = 0f;

        if (currentGhost != null)
        {
            currentGhost.transform.rotation = Quaternion.Euler(0f, currentRotation, 0f);
        }
    }

    // =============================================
    // Collision / Validity Checks
    // =============================================

    // Check if a wall or construction site already exists at this exact grid position
    bool HasWallAtPosition(Vector3 position)
    {
        if (WallGrid.Instance != null)
        {
            Vector2Int gridPos = WallGrid.Instance.WorldToGrid(position);
            return WallGrid.Instance.HasWallAt(gridPos);
        }

        // Fallback if WallGrid not yet initialized
        float threshold = cellSize * 0.4f;

        for (int i = 0; i < Wall.ActiveList.Count; i++)
        {
            Wall wall = Wall.ActiveList[i];
            if (wall == null) continue;
            float dx = Mathf.Abs(position.x - wall.transform.position.x);
            float dz = Mathf.Abs(position.z - wall.transform.position.z);
            if (dx < threshold && dz < threshold)
                return true;
        }

        for (int i = 0; i < ConstructionSite.ActiveList.Count; i++)
        {
            ConstructionSite site = ConstructionSite.ActiveList[i];
            if (site == null) continue;
            float dx = Mathf.Abs(position.x - site.transform.position.x);
            float dz = Mathf.Abs(position.z - site.transform.position.z);
            if (dx < threshold && dz < threshold)
                return true;
        }

        return false;
    }

    // Get visual no-build radius for a building type from BuildingData
    float GetVisualNoBuildRadius(BuildingType type, float fallback)
    {
        if (BuildingDatabase.Instance == null) return fallback;
        BuildingData data = BuildingDatabase.Instance.GetBuildingData(type);
        return data != null ? data.visualNoBuildRadius : fallback;
    }

    // Check if a building type is a wall using BuildingData.isWall flag
    bool IsWallType(BuildingType type)
    {
        if (BuildingDatabase.Instance == null) return false;
        BuildingData data = BuildingDatabase.Instance.GetBuildingData(type);
        return data != null && data.isWall;
    }

    // Check if position is too close to any existing building's no-build zone
    // Walls skip this check entirely
    bool IsTooCloseToExistingBuilding(Vector3 position)
    {
        // Walls skip all no-build zone checks - they can be placed anywhere
        if (IsWallType(selectedBuildingType))
            return false;

        // Small buffer to account for floating-point precision with grid snapping
        float gridBuffer = cellSize * 0.1f;

        // Check all BaseBuilding objects (Campfire, etc.)
        for (int i = 0; i < BaseBuilding.ActiveList.Count; i++)
        {
            BaseBuilding building = BaseBuilding.ActiveList[i];
            if (building == null) continue;

            float deltaX = Mathf.Abs(position.x - building.transform.position.x);
            float deltaZ = Mathf.Abs(position.z - building.transform.position.z);

            if (deltaX < building.noBuildRadius + gridBuffer && deltaZ < building.noBuildRadius + gridBuffer)
            {
                return true;  // Too close!
            }
        }

        // Check all ConstructionSite objects
        for (int i = 0; i < ConstructionSite.ActiveList.Count; i++)
        {
            ConstructionSite site = ConstructionSite.ActiveList[i];
            if (site == null) continue;

            float deltaX = Mathf.Abs(position.x - site.transform.position.x);
            float deltaZ = Mathf.Abs(position.z - site.transform.position.z);

            if (deltaX < site.noBuildRadius + gridBuffer && deltaZ < site.noBuildRadius + gridBuffer)
            {
                return true;  // Too close!
            }
        }

        // Check all Hut objects (finished buildings)
        for (int i = 0; i < Hut.ActiveList.Count; i++)
        {
            Hut hut = Hut.ActiveList[i];
            if (hut == null) continue;

            float deltaX = Mathf.Abs(position.x - hut.transform.position.x);
            float deltaZ = Mathf.Abs(position.z - hut.transform.position.z);

            if (deltaX < hut.noBuildRadius + gridBuffer && deltaZ < hut.noBuildRadius + gridBuffer)
            {
                return true;  // Too close!
            }
        }

        // Walls have noBuildRadius=0 so this check is effectively skipped for them

        // Check all Watchtower objects (finished towers)
        for (int i = 0; i < Watchtower.ActiveList.Count; i++)
        {
            Watchtower tower = Watchtower.ActiveList[i];
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

    // =============================================
    // No-Build Zone Visuals (unchanged)
    // =============================================

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
        // Create circles for all BaseBuilding objects (campfire - uses its own visualNoBuildRadius)
        for (int i = 0; i < BaseBuilding.ActiveList.Count; i++)
        {
            BaseBuilding building = BaseBuilding.ActiveList[i];
            if (building == null) continue;
            CreateCircleVisual(building.transform.position, building.visualNoBuildRadius);
        }

        // Create circles for all ConstructionSite objects (look up from BuildingData)
        for (int i = 0; i < ConstructionSite.ActiveList.Count; i++)
        {
            ConstructionSite site = ConstructionSite.ActiveList[i];
            if (site == null) continue;
            float visualRadius = GetVisualNoBuildRadius(site.buildingType, site.noBuildRadius);
            CreateCircleVisual(site.transform.position, visualRadius);
        }

        // Create circles for all Hut objects (look up from BuildingData)
        for (int i = 0; i < Hut.ActiveList.Count; i++)
        {
            Hut hut = Hut.ActiveList[i];
            if (hut == null) continue;
            float visualRadius = GetVisualNoBuildRadius(BuildingType.Hut, hut.noBuildRadius);
            CreateCircleVisual(hut.transform.position, visualRadius);
        }

        // Walls intentionally have no no-build zone visuals so they can be placed adjacent

        // Create circles for all Watchtower objects (look up from BuildingData)
        for (int i = 0; i < Watchtower.ActiveList.Count; i++)
        {
            Watchtower tower = Watchtower.ActiveList[i];
            if (tower == null) continue;
            float visualRadius = GetVisualNoBuildRadius(BuildingType.Watchtower, tower.noBuildRadius);
            CreateCircleVisual(tower.transform.position, visualRadius);
        }
    }

    // Create merged zones by filling grid cells then outlining the filled region
    void CreateMergedNoBuildZones()
    {
        // Collect all building zones
        List<ZoneData> zones = new List<ZoneData>();

        // Campfire uses its own visualNoBuildRadius (no BuildingData entry)
        for (int i = 0; i < BaseBuilding.ActiveList.Count; i++)
        {
            BaseBuilding building = BaseBuilding.ActiveList[i];
            if (building == null) continue;
            zones.Add(new ZoneData { position = building.transform.position, radius = building.visualNoBuildRadius });
        }

        // All other buildings look up visual radius from BuildingData
        for (int i = 0; i < ConstructionSite.ActiveList.Count; i++)
        {
            ConstructionSite site = ConstructionSite.ActiveList[i];
            if (site == null) continue;
            float visualRadius = GetVisualNoBuildRadius(site.buildingType, site.noBuildRadius);
            zones.Add(new ZoneData { position = site.transform.position, radius = visualRadius });
        }

        for (int i = 0; i < Hut.ActiveList.Count; i++)
        {
            Hut hut = Hut.ActiveList[i];
            if (hut == null) continue;
            float visualRadius = GetVisualNoBuildRadius(BuildingType.Hut, hut.noBuildRadius);
            zones.Add(new ZoneData { position = hut.transform.position, radius = visualRadius });
        }

        // Walls excluded from no-build zones - they can be placed adjacent

        for (int i = 0; i < Watchtower.ActiveList.Count; i++)
        {
            Watchtower tower = Watchtower.ActiveList[i];
            if (tower == null) continue;
            float visualRadius = GetVisualNoBuildRadius(BuildingType.Watchtower, tower.noBuildRadius);
            zones.Add(new ZoneData { position = tower.transform.position, radius = visualRadius });
        }

        if (zones.Count == 0) return;

        // Create a HashSet of filled grid cells
        HashSet<Vector2Int> filledCells = new HashSet<Vector2Int>();

        foreach (var zone in zones)
        {
            int centerGridX = Mathf.RoundToInt(zone.position.x / cellSize);
            int centerGridZ = Mathf.RoundToInt(zone.position.z / cellSize);
            int cellsToExtend = Mathf.FloorToInt(zone.radius / cellSize);

            for (int x = centerGridX - cellsToExtend; x <= centerGridX + cellsToExtend; x++)
            {
                for (int z = centerGridZ - cellsToExtend; z <= centerGridZ + cellsToExtend; z++)
                {
                    filledCells.Add(new Vector2Int(x, z));
                }
            }
        }

        // Now draw edges around the filled region
        DrawPerimeterEdgesFromGrid(filledCells);
    }

    // Helper struct for zone data
    struct ZoneData
    {
        public Vector3 position;
        public float radius;
    }

    // Draw perimeter edges around all filled cells
    void DrawPerimeterEdgesFromGrid(HashSet<Vector2Int> filledCells)
    {
        HashSet<(int x1, int z1, int x2, int z2)> drawnEdges = new HashSet<(int, int, int, int)>();

        foreach (Vector2Int cell in filledCells)
        {
            int x = cell.x;
            int z = cell.y;

            if (!filledCells.Contains(new Vector2Int(x, z - 1)))
            {
                AddGridEdge(drawnEdges, x, z, x + 1, z);
            }
            if (!filledCells.Contains(new Vector2Int(x + 1, z)))
            {
                AddGridEdge(drawnEdges, x + 1, z, x + 1, z + 1);
            }
            if (!filledCells.Contains(new Vector2Int(x, z + 1)))
            {
                AddGridEdge(drawnEdges, x, z + 1, x + 1, z + 1);
            }
            if (!filledCells.Contains(new Vector2Int(x - 1, z)))
            {
                AddGridEdge(drawnEdges, x, z, x, z + 1);
            }
        }
    }

    void AddGridEdge(HashSet<(int x1, int z1, int x2, int z2)> drawnEdges, int gridX1, int gridZ1, int gridX2, int gridZ2)
    {
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

        if (drawnEdges.Add((x1, z1, x2, z2)))
        {
            float centeringOffset = 0.5f;
            float offset = cellSize;
            Vector3 worldP1 = new Vector3((x1 + centeringOffset) * cellSize - offset, 0, (z1 + centeringOffset) * cellSize - offset);
            Vector3 worldP2 = new Vector3((x2 + centeringOffset) * cellSize - offset, 0, (z2 + centeringOffset) * cellSize - offset);

            DrawEdgeSegment(worldP1, worldP2);
        }
    }

    void DrawEdgeSegment(Vector3 p1, Vector3 p2)
    {
        GameObject segmentObj = new GameObject("EdgeSegment");
        LineRenderer lineRenderer = segmentObj.AddComponent<LineRenderer>();

        lineRenderer.positionCount = 2;
        lineRenderer.useWorldSpace = true;

        lineRenderer.startWidth = 0.15f;
        lineRenderer.endWidth = 0.15f;
        Color borderColor = new Color(1f, 0f, 0f, 0.8f);
        lineRenderer.startColor = borderColor;
        lineRenderer.endColor = borderColor;

        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.material.color = borderColor;

        p1.y = 0.05f;
        p2.y = 0.05f;
        lineRenderer.SetPosition(0, p1);
        lineRenderer.SetPosition(1, p2);

        noBuildZoneVisuals.Add(segmentObj);
    }

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

    void CreateCircleVisual(Vector3 center, float radius)
    {
        GameObject zoneObj = new GameObject("NoBuildZone");
        zoneObj.transform.position = new Vector3(center.x, 0.05f, center.z);

        if (showNoBuildFills)
        {
            GameObject fillObj = new GameObject("Fill");
            fillObj.transform.SetParent(zoneObj.transform);
            fillObj.transform.localPosition = Vector3.zero;
            fillObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            MeshFilter meshFilter = fillObj.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = fillObj.AddComponent<MeshRenderer>();

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

            Material fillMaterial;
            if (noBuildZoneMaterial != null)
            {
                fillMaterial = noBuildZoneMaterial;
            }
            else
            {
                fillMaterial = new Material(Shader.Find("Standard"));
                fillMaterial.color = noBuildZoneColor;
                fillMaterial.SetFloat("_Mode", 3);
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

        GameObject borderObj = new GameObject("Border");
        borderObj.transform.SetParent(zoneObj.transform);
        borderObj.transform.localPosition = new Vector3(0, 0.01f, 0);

        LineRenderer lineRenderer = borderObj.AddComponent<LineRenderer>();
        lineRenderer.positionCount = 5;
        lineRenderer.loop = true;
        lineRenderer.useWorldSpace = false;

        lineRenderer.startWidth = 0.15f;
        lineRenderer.endWidth = 0.15f;
        Color borderColor = new Color(1f, 0f, 0f, 0.8f);
        lineRenderer.startColor = borderColor;
        lineRenderer.endColor = borderColor;

        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.material.color = borderColor;

        lineRenderer.SetPosition(0, new Vector3(-radius, 0, -radius));
        lineRenderer.SetPosition(1, new Vector3(radius, 0, -radius));
        lineRenderer.SetPosition(2, new Vector3(radius, 0, radius));
        lineRenderer.SetPosition(3, new Vector3(-radius, 0, radius));
        lineRenderer.SetPosition(4, new Vector3(-radius, 0, -radius));

        noBuildZoneVisuals.Add(zoneObj);
    }

    void CreateGhostNoBuildZone()
    {
        if (ghostNoBuildZone != null)
        {
            Destroy(ghostNoBuildZone);
        }

        GameObject zoneObj = new GameObject("GhostNoBuildZone");
        zoneObj.transform.position = new Vector3(currentGhost.transform.position.x, 0.05f, currentGhost.transform.position.z);

        float radius = ghostBuildingVisualNoBuildRadius;

        if (showNoBuildFills)
        {
            GameObject fillObj = new GameObject("Fill");
            fillObj.transform.SetParent(zoneObj.transform);
            fillObj.transform.localPosition = Vector3.zero;
            fillObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            MeshFilter meshFilter = fillObj.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = fillObj.AddComponent<MeshRenderer>();

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

            Material fillMaterial = new Material(Shader.Find("Standard"));
            Color ghostZoneColor = new Color(0.3f, 0.7f, 1f, 0.25f);
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

        GameObject borderObj = new GameObject("Border");
        borderObj.transform.SetParent(zoneObj.transform);
        borderObj.transform.localPosition = new Vector3(0, 0.01f, 0);

        LineRenderer lineRenderer = borderObj.AddComponent<LineRenderer>();
        lineRenderer.positionCount = 5;
        lineRenderer.loop = true;
        lineRenderer.useWorldSpace = false;

        lineRenderer.startWidth = 0.15f;
        lineRenderer.endWidth = 0.15f;
        Color ghostBorderColor = new Color(0.3f, 0.9f, 1f, 0.6f);
        lineRenderer.startColor = ghostBorderColor;
        lineRenderer.endColor = ghostBorderColor;

        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.material.color = ghostBorderColor;

        lineRenderer.SetPosition(0, new Vector3(-radius, 0, -radius));
        lineRenderer.SetPosition(1, new Vector3(radius, 0, -radius));
        lineRenderer.SetPosition(2, new Vector3(radius, 0, radius));
        lineRenderer.SetPosition(3, new Vector3(-radius, 0, radius));
        lineRenderer.SetPosition(4, new Vector3(-radius, 0, -radius));

        ghostNoBuildZone = zoneObj;
    }
}
