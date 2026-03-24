using UnityEngine;

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
    public GameObject ghostPrefab;
    public GameObject constructionSitePrefab;
    public GameObject finishedBuildingPrefab;

    [Header("Placement Settings")]
    public Vector3 buildingSize = new Vector3(2f, 1.5f, 2f);
    public float noBuildRadius = 3.5f;
    [Tooltip("Visual-only radius for the red no-build border. Does not affect actual placement validation.")]
    public float visualNoBuildRadius = 3.5f;
    public float placementHeight = 0.75f;

    [Header("Gameplay Stats")]
    public float maxHealth = 100f;
    public bool blocksNavMesh = false;  // true for walls, false for others

    [Header("Wall Behavior")]
    public bool isWall = false;  // true for WoodenWall/StoneWall - changes placement rules
}
