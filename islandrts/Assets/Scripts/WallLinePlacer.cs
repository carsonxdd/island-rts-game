using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Wall placement with click-start + click-end line drawing.
/// Phase 1: Single cursor ghost follows mouse. Click to set start point.
/// Phase 2: Ghost line from start to mouse. Click to confirm all walls.
/// Default path is L-shaped (R toggles X-first vs Z-first); hold Shift for a
/// Bresenham staircase. Plain helper owned by BuildPlacement — not a
/// MonoBehaviour, so the scene object stays unchanged.
/// </summary>
public class WallLinePlacer
{
    private readonly BuildPlacement owner;

    // Wall line drawing state
    private bool isDrawingWallLine = false;
    private Vector3 wallLineStart;
    private readonly List<GameObject> wallLineGhosts = new List<GameObject>();
    private List<Vector3> wallLinePositions = new List<Vector3>();

    // L-shaped path mode: true = go along X first, then Z; toggled with R
    private bool xFirst = true;

    // Shared ghost material — light blue, semi-transparent
    private static readonly Color wallGhostColor = new Color(0.4f, 0.7f, 1f, 0.35f);
    private static readonly Color wallGhostInvalidColor = new Color(1f, 0.3f, 0.3f, 0.35f);

    public WallLinePlacer(BuildPlacement owner)
    {
        this.owner = owner;
    }

    /// <summary>
    /// Per-frame update while a wall type is selected in build mode.
    /// </summary>
    public void Tick()
    {
        Vector3 snapped;
        if (!owner.GetSnappedMousePosition(out snapped)) return;

        if (!isDrawingWallLine)
        {
            // Phase 1: Cursor ghost follows mouse, waiting for first click
            Vector3 cursorPos = snapped;
            cursorPos.y = owner.GroundYAt(snapped) + 0.02f; // Wall Y offset above the ground here
            owner.currentGhost.transform.position = cursorPos;

            // R toggles L-path direction (X-first vs Z-first) before starting a line
            if (KeyBindings.Down(KeyBindings.Action.RotateBuilding))
            {
                xFirst = !xFirst;
            }

            // Color ghost based on whether this cell can take a wall
            bool occupied = CellBlocked(snapped);
            owner.SetGhostColor(occupied ? wallGhostInvalidColor : wallGhostColor);

            // Show single-wall cost in UI
            BuildingData data = BuildingDatabase.Instance != null
                ? BuildingDatabase.Instance.GetBuildingData(owner.selectedBuildingType)
                : null;
            if (owner.selectionUI != null && data != null)
            {
                bool canAfford = ResourceManager.Instance.CanAfford(data.woodCost, data.foodCost, data.stoneCost);
                owner.selectionUI.UpdateDisplay(data, canAfford);
            }

            // Left click: set start point and begin drawing line
            if (Input.GetMouseButtonDown(0))
            {
                wallLineStart = snapped;
                isDrawingWallLine = true;
                owner.currentGhost.SetActive(false);  // Hide cursor ghost; line ghosts take over
            }

            // Cancel: exit build mode
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                owner.CancelPlacement();
            }
        }
        else
        {
            // Phase 2: Drawing line from start to current mouse position
            // R toggles L-path direction while drawing
            if (KeyBindings.Down(KeyBindings.Action.RotateBuilding))
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
    /// Discard any in-progress line and ghosts (used when leaving build mode
    /// or switching building type).
    /// </summary>
    public void ResetLineState()
    {
        ClearWallLineGhosts();
        isDrawingWallLine = false;
    }

    /// <summary>
    /// Create a simple procedural ghost for the wall cursor (single isolated shape).
    /// Uses the same transparent material as line ghosts.
    /// </summary>
    public GameObject CreateWallCursorGhost(BuildingData data)
    {
        bool isStone = data.buildingType == BuildingType.StoneWall;
        GameObject ghost = new GameObject("WallCursorGhost");
        MeshFilter mf = ghost.AddComponent<MeshFilter>();
        mf.mesh = WallConnector.GetOrCreateMesh(WallConnector.WallShape.Isolated, isStone);
        MeshRenderer mr = ghost.AddComponent<MeshRenderer>();
        mr.material = CreateWallGhostMaterial();
        return ghost;
    }

    /// <summary>
    /// Update the wall line preview: compute grid positions along the line,
    /// spawn/update ghost objects, show total cost.
    /// Wall shapes are auto-determined by WallGrid neighbors, so no per-wall rotation is needed.
    /// </summary>
    void UpdateWallLinePreview(Vector3 endSnapped)
    {
        // Choose path algorithm based on Shift modifier
        bool useBresenham = KeyBindings.Held(KeyBindings.Action.StaircaseWalls);

        if (useBresenham)
        {
            wallLinePositions = GetGridLine(wallLineStart, endSnapped);
        }
        else
        {
            wallLinePositions = GetLShapedLine(wallLineStart, endSnapped, xFirst);
        }

        BuildingData data = BuildingDatabase.Instance != null
            ? BuildingDatabase.Instance.GetBuildingData(owner.selectedBuildingType)
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

            // Place at grid position, slightly above the ground there
            Vector3 pos = wallLinePositions[i];
            pos.y = owner.GroundYAt(pos) + 0.02f;
            ghost.transform.position = pos;

            bool occupied = CellBlocked(wallLinePositions[i]);
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
        if (owner.selectionUI != null && data != null)
        {
            int totalWood = data.woodCost * validCount;
            int totalFood = data.foodCost * validCount;
            int totalStone = data.stoneCost * validCount;
            bool canAfford = ResourceManager.Instance.CanAfford(totalWood, totalFood, totalStone);
            owner.selectionUI.UpdateWallLineDisplay(data, validCount, canAfford);
        }
    }

    /// <summary>
    /// Compute grid cell positions along a line using Bresenham's algorithm.
    /// Diagonal steps are split into horizontal + vertical so every corner
    /// is filled (no gaps in the staircase).
    /// </summary>
    List<Vector3> GetGridLine(Vector3 start, Vector3 end)
    {
        int x0 = Mathf.RoundToInt(start.x / owner.cellSize);
        int z0 = Mathf.RoundToInt(start.z / owner.cellSize);
        int x1 = Mathf.RoundToInt(end.x / owner.cellSize);
        int z1 = Mathf.RoundToInt(end.z / owner.cellSize);

        List<Vector3> positions = new List<Vector3>();

        int dx = Mathf.Abs(x1 - x0);
        int dz = Mathf.Abs(z1 - z0);
        int sx = x0 < x1 ? 1 : -1;
        int sz = z0 < z1 ? 1 : -1;
        int err = dx - dz;

        while (true)
        {
            positions.Add(new Vector3(x0 * owner.cellSize, owner.placementHeight, z0 * owner.cellSize));

            if (x0 == x1 && z0 == z1) break;

            int e2 = 2 * err;
            bool stepX = e2 > -dz;
            bool stepZ = e2 < dx;

            if (stepX && stepZ)
            {
                // Would be diagonal — split into horizontal step then vertical step
                // so the corner cell is filled (no gap)
                err -= dz; x0 += sx;
                positions.Add(new Vector3(x0 * owner.cellSize, owner.placementHeight, z0 * owner.cellSize));
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
        int x0 = Mathf.RoundToInt(start.x / owner.cellSize);
        int z0 = Mathf.RoundToInt(start.z / owner.cellSize);
        int x1 = Mathf.RoundToInt(end.x / owner.cellSize);
        int z1 = Mathf.RoundToInt(end.z / owner.cellSize);

        List<Vector3> positions = new List<Vector3>();

        if (doXFirst)
        {
            // Walk along X first
            int sx = x0 < x1 ? 1 : -1;
            for (int x = x0; x != x1; x += sx)
            {
                positions.Add(new Vector3(x * owner.cellSize, owner.placementHeight, z0 * owner.cellSize));
            }
            // Then walk along Z
            int sz = z0 < z1 ? 1 : -1;
            for (int z = z0; z != z1; z += sz)
            {
                positions.Add(new Vector3(x1 * owner.cellSize, owner.placementHeight, z * owner.cellSize));
            }
            // Add final position
            positions.Add(new Vector3(x1 * owner.cellSize, owner.placementHeight, z1 * owner.cellSize));
        }
        else
        {
            // Walk along Z first
            int sz = z0 < z1 ? 1 : -1;
            for (int z = z0; z != z1; z += sz)
            {
                positions.Add(new Vector3(x0 * owner.cellSize, owner.placementHeight, z * owner.cellSize));
            }
            // Then walk along X
            int sx = x0 < x1 ? 1 : -1;
            for (int x = x0; x != x1; x += sx)
            {
                positions.Add(new Vector3(x * owner.cellSize, owner.placementHeight, z1 * owner.cellSize));
            }
            // Add final position
            positions.Add(new Vector3(x1 * owner.cellSize, owner.placementHeight, z1 * owner.cellSize));
        }

        return positions;
    }

    /// <summary>
    /// Confirm wall line: deduct total cost, place construction sites at all valid positions.
    /// Returns to cursor mode for continuous wall building.
    /// </summary>
    void ConfirmWallLine()
    {
        if (BuildingDatabase.Instance == null || ResourceManager.Instance == null) return;

        BuildingData data = BuildingDatabase.Instance.GetBuildingData(owner.selectedBuildingType);
        if (data == null || data.constructionSitePrefab == null) return;

        // Collect valid (non-occupied, buildable-ground) positions
        List<Vector3> validPositions = new List<Vector3>();
        for (int i = 0; i < wallLinePositions.Count; i++)
        {
            if (!CellBlocked(wallLinePositions[i]))
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
            Vector3 sitePos = validPositions[i];
            sitePos.y = owner.GroundYAt(sitePos) + owner.placementHeight;
            GameObject constructionSite = Object.Instantiate(
                data.constructionSitePrefab,
                sitePos,
                Quaternion.identity
            );

            constructionSite.layer = LayerMask.NameToLayer("Buildings");

            ConstructionSite siteComponent = constructionSite.GetComponent<ConstructionSite>();
            if (siteComponent != null)
            {
                siteComponent.SetBuildingType(owner.selectedBuildingType);
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
        owner.currentGhost.SetActive(true);

        // Restore single-wall cost display
        if (owner.selectionUI != null)
        {
            bool canAfford = ResourceManager.Instance.CanAfford(data.woodCost, data.foodCost, data.stoneCost);
            owner.selectionUI.UpdateDisplay(data, canAfford);
        }
    }

    /// <summary>
    /// Cancel wall line drawing, return to cursor mode.
    /// </summary>
    void CancelWallLine()
    {
        ClearWallLineGhosts();
        isDrawingWallLine = false;
        owner.currentGhost.SetActive(true);

        // Restore single-wall cost display
        BuildingData data = BuildingDatabase.Instance != null
            ? BuildingDatabase.Instance.GetBuildingData(owner.selectedBuildingType)
            : null;
        if (owner.selectionUI != null && data != null)
        {
            bool canAfford = ResourceManager.Instance.CanAfford(data.woodCost, data.foodCost, data.stoneCost);
            owner.selectionUI.UpdateDisplay(data, canAfford);
        }
    }

    /// <summary>
    /// Destroy all wall line ghost objects and clear the lists.
    /// </summary>
    void ClearWallLineGhosts()
    {
        foreach (GameObject ghost in wallLineGhosts)
        {
            if (ghost != null) Object.Destroy(ghost);
        }
        wallLineGhosts.Clear();
        wallLinePositions.Clear();
    }

    Material CreateWallGhostMaterial()
    {
        Material mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = wallGhostColor;
        return mat;
    }

    // A cell can't take a wall if it's occupied or (terrain) the ground
    // there is underwater / a cliff face
    bool CellBlocked(Vector3 position)
    {
        if (HasWallAtPosition(position)) return true;
        return TerrainGrid.Instance != null && !TerrainGrid.Instance.IsBuildable(position);
    }

    // Check if a wall or construction site already exists at this exact grid position
    bool HasWallAtPosition(Vector3 position)
    {
        if (WallGrid.Instance != null)
        {
            Vector2Int gridPos = WallGrid.Instance.WorldToGrid(position);
            return WallGrid.Instance.HasWallAt(gridPos);
        }

        // Fallback if WallGrid not yet initialized
        float threshold = owner.cellSize * 0.4f;

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
}
