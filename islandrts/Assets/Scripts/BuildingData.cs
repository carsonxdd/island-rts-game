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
}
