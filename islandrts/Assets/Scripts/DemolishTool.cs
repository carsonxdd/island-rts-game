using UnityEngine;

/// <summary>
/// Demolish mode (Delete/X key): highlight the building under the cursor and
/// demolish on click for a 50% resource refund. Campfire is protected.
/// Plain helper owned by BuildPlacement — not a MonoBehaviour, so the scene
/// object stays unchanged.
/// </summary>
public class DemolishTool
{
    private readonly BuildPlacement owner;

    private bool isActive = false;
    private GameObject demolishHighlight;  // Red highlight on targeted building
    private static readonly Color demolishColor = new Color(1f, 0.2f, 0.2f, 0.4f);

    public bool IsActive => isActive;

    public DemolishTool(BuildPlacement owner)
    {
        this.owner = owner;
    }

    public void Enter()
    {
        isActive = true;
    }

    public void Exit()
    {
        isActive = false;
        if (demolishHighlight != null)
        {
            Object.Destroy(demolishHighlight);
            demolishHighlight = null;
        }
    }

    /// <summary>
    /// Per-frame update while demolish mode is active.
    /// </summary>
    public void Tick()
    {
        // ESC or right-click exits demolish mode
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
        {
            Exit();
            return;
        }

        // Raycast to find building under cursor
        if (owner.mainCam == null) return;
        Ray ray = owner.mainCam.ScreenPointToRay(Input.mousePosition);
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
                Object.Destroy(demolishHighlight);
                demolishHighlight = null;
            }
            return;
        }

        // Create or reposition highlight
        if (demolishHighlight == null)
        {
            demolishHighlight = GameObject.CreatePrimitive(PrimitiveType.Cube);
            demolishHighlight.name = "DemolishHighlight";
            Object.Destroy(demolishHighlight.GetComponent<Collider>());
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
        Object.Destroy(buildingObj);

        // Clear highlight
        if (demolishHighlight != null)
        {
            Object.Destroy(demolishHighlight);
            demolishHighlight = null;
        }
    }
}
