using UnityEngine;

/// <summary>
/// Single-building (non-wall) placement: smooth ghost follow, R rotation,
/// collision + no-build-zone validity, click-to-confirm. Plain helper owned
/// by BuildPlacement — not a MonoBehaviour, so the scene object stays unchanged.
/// </summary>
public class GhostPlacer
{
    private readonly BuildPlacement owner;

    private Vector3 targetPosition;  // Where the ghost wants to move to
    private bool isValidPlacement = false;
    private float currentRotation = 0f;  // Current ghost rotation in degrees

    public GhostPlacer(BuildPlacement owner)
    {
        this.owner = owner;
    }

    /// <summary>
    /// Per-frame update while a non-wall type is selected in build mode.
    /// </summary>
    public void Tick()
    {
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
            owner.CancelPlacement();
        }
    }

    /// <summary>
    /// Reset state for a freshly spawned ghost when entering build mode.
    /// </summary>
    public void ResetForNewGhost(Vector3 ghostPosition)
    {
        targetPosition = ghostPosition;
        isValidPlacement = false;
        currentRotation = 0f;  // Reset rotation when entering build mode
    }

    /// <summary>
    /// Re-target after swapping the ghost during type selection (rotation is kept).
    /// </summary>
    public void SetTargetPosition(Vector3 ghostPosition)
    {
        targetPosition = ghostPosition;
    }

    void MoveGhostToMouse()
    {
        Vector3 snappedPos;
        if (!owner.GetSnappedMousePosition(out snappedPos)) return;

        // Update target position (where we want to go)
        targetPosition = snappedPos;

        // Smooth lerp for non-wall buildings
        owner.currentGhost.transform.position = Vector3.Lerp(
            owner.currentGhost.transform.position,
            targetPosition,
            owner.movementSpeed * Time.deltaTime
        );

        // Update ghost no-build zone position to follow the ghost
        owner.zoneRenderer.UpdateGhostZone(owner.currentGhost.transform.position);

        // Check for collisions at TARGET position
        bool hasCollision = Physics.CheckBox(
            targetPosition,
            owner.buildingSize * 0.5f,  // Half extents
            Quaternion.Euler(0f, currentRotation, 0f),
            owner.buildingsLayer
        );

        // Check if too close to any existing buildings (no-build zones)
        bool tooCloseToBuilding = IsTooCloseToExistingBuilding(targetPosition);

        // Update validity and color (must pass both checks)
        isValidPlacement = !hasCollision && !tooCloseToBuilding;

        if (owner.ghostRenderer != null)
        {
            owner.ghostRenderer.material.color = isValidPlacement ? owner.validColor : owner.invalidColor;
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

        BuildingData data = BuildingDatabase.Instance.GetBuildingData(owner.selectedBuildingType);
        if (data == null || data.constructionSitePrefab == null)
        {
            Debug.LogError($"BuildPlacement: No construction site prefab for {owner.selectedBuildingType}!");
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
        // Spawn the construction site with the ghost's rotation
        GameObject constructionSite = Object.Instantiate(
            data.constructionSitePrefab,
            targetPosition,
            Quaternion.Euler(0f, currentRotation, 0f)
        );

        // Make sure it's on the Buildings layer for collision detection
        constructionSite.layer = LayerMask.NameToLayer("Buildings");

        // Configure construction site with building type
        ConstructionSite siteComponent = constructionSite.GetComponent<ConstructionSite>();
        if (siteComponent != null)
        {
            siteComponent.SetBuildingType(owner.selectedBuildingType);
        }

        // Play building placed sound
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayBuildingPlaced();
        }

        // NON-WALL: Exit build mode after placing
        Object.Destroy(owner.currentGhost);
        owner.zoneRenderer.DestroyZoneVisuals();
        owner.zoneRenderer.DestroyGhostZone();

        if (owner.selectionUI != null)
        {
            owner.selectionUI.Hide();
        }

        owner.currentGhost = null;
        owner.ghostRenderer = null;
        owner.isPlacing = false;
    }

    /// <summary>
    /// Rotate the ghost building 90 degrees when R is pressed
    /// </summary>
    void RotateGhost()
    {
        currentRotation += 90f;
        if (currentRotation >= 360f) currentRotation = 0f;

        if (owner.currentGhost != null)
        {
            owner.currentGhost.transform.rotation = Quaternion.Euler(0f, currentRotation, 0f);
        }
    }

    // Check if position is too close to any existing building's no-build zone
    // Walls skip this check entirely
    bool IsTooCloseToExistingBuilding(Vector3 position)
    {
        // Walls skip all no-build zone checks - they can be placed anywhere
        if (owner.IsWallType(owner.selectedBuildingType))
            return false;

        // Small buffer to account for floating-point precision with grid snapping
        float gridBuffer = owner.cellSize * 0.1f;

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
}
