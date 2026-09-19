using UnityEngine;

/// <summary>
/// Everything the build system needs to know about one building type: what it costs, what
/// to spawn at each stage (ghost, construction site, finished building), how it may be
/// placed, and its health. One asset per BuildingType, looked up through BuildingDatabase.
/// </summary>
[CreateAssetMenu(fileName = "BuildingData", menuName = "RTS/Building Data")]
public class BuildingData : ScriptableObject
{
    [Header("Building Type")]
    public BuildingType buildingType;
    public string buildingName;
    [Tooltip("One line for the build palette's tooltip and the selected-building card.")]
    [TextArea(2, 3)]
    public string description;
    [Tooltip("Which heading this sits under in the build palette.")]
    public BuildCategory category = BuildCategory.Production;

    // --- Tiers (2026-09-18) ---------------------------------------------------
    // A tier is data: the Tent is tier 1 and points at the Hut, which is tier 2 and
    // not placeable. Nothing branches on the type to know this.
    [Header("Tier")]
    [Tooltip("Shown as \"Level N\" on the selected-building card.")]
    public int tier = 1;
    [Tooltip("The next tier up, or none when this is the top. A direct reference rather than a BuildingType, so \"no upgrade\" needs no sentinel value.")]
    public BuildingData upgradesTo;
    [Tooltip("False for a tier that is only ever reached by upgrading (the Hut). The build palette lists placeable types only.")]
    public bool placeable = true;

    // --- Research gate (2026-09-18) -------------------------------------------
    // Was three hardcoded ifs in BuildPlacement.SelectBuilding. Still exactly one
    // Knowledge.Has read at the site that decides - it is just parameterised now, so
    // the palette can grey a tile and name the research from the same field.
    [Header("Research Gate")]
    public bool requiresUnlock = false;
    public Unlocks.Kind requiredUnlock = Unlocks.Kind.Construction;

    [Header("Resource Costs")]
    public int woodCost;
    public int foodCost;
    public int stoneCost;
    // Metal (2026-09-04, Slice 6): the Shipyard is the first building that costs it.
    // Every placement, refund and repair reads the four-resource overloads now.
    public int metalCost;

    [Header("Prefab References")]
    public GameObject ghostPrefab;              // Translucent preview that follows the cursor
    public GameObject constructionSitePrefab;   // Spawned on confirm, becomes the finished building
    public GameObject finishedBuildingPrefab;

    [Header("Placement Settings")]
    public Vector3 buildingSize = new Vector3(2f, 1.5f, 2f);  // Footprint for the overlap test
    public float noBuildRadius = 3.5f;          // Clearance other buildings may not be placed inside
    [Tooltip("Visual-only radius for the red no-build border. Does not affect actual placement validation.")]
    public float visualNoBuildRadius = 3.5f;
    // Offset above the ground the building is placed at. Base-pivot art wants 0; the
    // legacy 0.75 default suits the old centre-pivot primitives.
    public float placementHeight = 0.75f;
    // Shore rule (2026-09-04, Slice 6): the ghost is only valid within
    // GhostPlacer.ShoreRadius of the water. The Shipyard.
    public bool requiresShore = false;
    // Seconds of one builder's labour before LaborFactor; 0 = the construction
    // site prefab's own buildTime (huts and the Workshop share one site prefab,
    // so a slow build needs its own number here).
    public float buildTimeOverride = 0f;

    [Header("Gameplay Stats")]
    public float maxHealth = 100f;
    public bool blocksNavMesh = false;  // true for walls, false for others

    [Header("Wall Behavior")]
    public bool isWall = false;  // true for WoodenWall/StoneWall - changes placement rules

    // ------------------------------------------------------------------
    // Shared reads (2026-09-18). The palette, the selected-building card, the
    // placement gate and the upgrade all ask the same three questions, so they
    // ask them here rather than each spelling out the four cost fields.
    // ------------------------------------------------------------------

    /// <summary>True when this colony has researched whatever this building needs.</summary>
    public bool UnlockedFor(Faction faction)
    {
        if (!requiresUnlock) return true;
        return faction != null && faction.Knowledge.Has(requiredUnlock);
    }

    /// <summary>The research that would unlock it, for a player-facing "Needs X" line.</summary>
    public string RequiredResearchTitle => requiresUnlock ? Unlocks.ResearchTitleFor(requiredUnlock) : null;

    public bool AffordableBy(Faction faction)
    {
        return faction != null && faction.Resources.CanAfford(woodCost, foodCost, stoneCost, metalCost);
    }

    public bool Charge(Faction faction)
    {
        return faction != null && faction.Resources.SpendResources(woodCost, foodCost, stoneCost, metalCost);
    }

    /// <summary>"20W 10F" - only the resources this building actually costs, multiplied for wall lines.</summary>
    public string CostLine(int multiplier = 1)
    {
        var sb = new System.Text.StringBuilder(24);
        Append(sb, woodCost * multiplier, 'W');
        Append(sb, foodCost * multiplier, 'F');
        Append(sb, stoneCost * multiplier, 'S');
        Append(sb, metalCost * multiplier, 'M');
        return sb.Length == 0 ? "Free" : sb.ToString();
    }

    static void Append(System.Text.StringBuilder sb, int amount, char suffix)
    {
        if (amount <= 0) return;
        if (sb.Length > 0) sb.Append(' ');
        sb.Append(amount).Append(suffix);
    }
}
