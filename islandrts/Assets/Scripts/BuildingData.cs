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

    [Header("Gameplay Stats")]
    public float maxHealth = 100f;
    public bool blocksNavMesh = false;  // true for walls, false for others

    [Header("Wall Behavior")]
    public bool isWall = false;  // true for WoodenWall/StoneWall - changes placement rules
}
