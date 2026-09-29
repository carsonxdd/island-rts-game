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
                    targetType = RepairCosts.WallTypeOf(gate);   // Demolish multiplies by the gate's width
                }
            }

            if (targetObj == null)
            {
                Hut hut = hit.collider.GetComponent<Hut>();
                if (hut == null) hut = hit.collider.GetComponentInParent<Hut>();
                if (hut != null)
                {
                    targetObj = hut.gameObject;
                    targetType = hut.BuildingType;   // Tent or Hut (2026-09-18): the refund follows the tier
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
                Storehouse store = hit.collider.GetComponent<Storehouse>();
                if (store == null) store = hit.collider.GetComponentInParent<Storehouse>();
                if (store != null)
                {
                    targetObj = store.gameObject;
                    targetType = BuildingType.Storehouse;
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

        // Only the player's own buildings (2026-09-28): a rival's wall hidden by the fog
        // still has its collider, and demolishing it refunded the player.
        if (targetObj != null)
        {
            IOwned owned = targetObj.GetComponent<IOwned>();
            if (owned != null && owned.Faction != Factions.Player) targetObj = null;
        }

        // Update highlight
        UpdateDemolishHighlight(targetObj);

        // Left click: demolish
        if (Input.GetMouseButtonDown(0) && !PointerBlock.OverHud && targetObj != null && targetType.HasValue)
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
        Demolish(buildingObj, type);

        // Clear highlight
        if (demolishHighlight != null)
        {
            Object.Destroy(demolishHighlight);
            demolishHighlight = null;
        }
    }

    /// <summary>
    /// Refund half the build cost and destroy the building. Public and static since
    /// 2026-09-18 because the selected-building card demolishes too, and there must be
    /// exactly one refund rule - two would drift the moment a cost changed. A gate refunds
    /// as the walls it replaced (its wall's cost x its width, 2026-09-28) whatever type
    /// the caller names for it.
    /// </summary>
    public static void Demolish(GameObject buildingObj, BuildingType type)
    {
        if (BuildingDatabase.Instance == null) return;

        // Get building data for refund calculation
        int count;
        BuildingData data = RefundData(buildingObj, type, out count);
        if (data != null)
        {
            // 50% refund
            int woodRefund = RefundOf(data.woodCost, count);
            int foodRefund = RefundOf(data.foodCost, count);
            int stoneRefund = RefundOf(data.stoneCost, count);
            int metalRefund = RefundOf(data.metalCost, count);

            if (woodRefund > 0) Factions.Player.Resources.AddWood(woodRefund);
            if (foodRefund > 0) Factions.Player.Resources.AddFood(foodRefund);
            if (stoneRefund > 0) Factions.Player.Resources.AddStone(stoneRefund);
            if (metalRefund > 0) Factions.Player.Resources.AddMetal(metalRefund);
        }

        // Play sound
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayBuildingPlaced();
        }

        // Destroy the building (OnDestroy handlers in Wall/Gate/Hut handle grid unregistration)
        Object.Destroy(buildingObj);
    }

    /// <summary>The 50% refund as a player-facing line, for the card's Demolish button.</summary>
    public static string RefundLine(BuildingType type)
    {
        return RefundLine(null, type);
    }

    /// <summary>The 50% refund of this building as a player-facing line (a gate: × its width).</summary>
    public static string RefundLine(GameObject building, BuildingType type)
    {
        int count;
        BuildingData data = RefundData(building, type, out count);
        if (data == null) return "";

        var sb = new System.Text.StringBuilder(24);
        Refund(sb, data.woodCost, count, 'W');
        Refund(sb, data.foodCost, count, 'F');
        Refund(sb, data.stoneCost, count, 'S');
        Refund(sb, data.metalCost, count, 'M');
        return sb.ToString();
    }

    /// <summary>
    /// The data a refund is priced from and how many of it. A gate has no BuildingData:
    /// it is its wall, <see cref="Gate.Width"/> times over.
    /// </summary>
    static BuildingData RefundData(GameObject building, BuildingType type, out int count)
    {
        count = 1;
        Gate gate = building != null ? building.GetComponent<Gate>() : null;
        if (gate != null)
        {
            type = RepairCosts.WallTypeOf(gate);
            count = gate.Width;
        }
        else if (type == BuildingType.WoodenGate) type = BuildingType.WoodenWall;
        else if (type == BuildingType.StoneGate) type = BuildingType.StoneWall;

        return BuildingDatabase.Instance != null ? BuildingDatabase.Instance.GetBuildingData(type) : null;
    }

    static int RefundOf(int cost, int count) => Mathf.FloorToInt(cost * count * 0.5f);

    static void Refund(System.Text.StringBuilder sb, int cost, int count, char suffix)
    {
        int back = RefundOf(cost, count);
        if (back <= 0) return;
        if (sb.Length > 0) sb.Append(' ');
        sb.Append(back).Append(suffix);
    }
}
