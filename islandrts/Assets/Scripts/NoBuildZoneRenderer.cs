using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// No-build zone visuals during build mode: red outlines around existing
/// buildings (merged or individual) plus the blue preview zone that follows
/// the ghost building. Plain helper owned by BuildPlacement — not a
/// MonoBehaviour, so the scene object stays unchanged.
/// </summary>
public class NoBuildZoneRenderer
{
    private readonly BuildPlacement owner;

    private readonly List<GameObject> noBuildZoneVisuals = new List<GameObject>();  // Visual circles for no-build zones
    private GameObject ghostNoBuildZone;  // No-build zone preview for the ghost building

    public NoBuildZoneRenderer(BuildPlacement owner)
    {
        this.owner = owner;
    }

    // Create visual zones showing no-build areas around existing buildings
    public void CreateZoneVisuals()
    {
        // Clear any existing visuals first
        DestroyZoneVisuals();

        if (owner.mergeOverlappingZones)
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

    public void DestroyZoneVisuals()
    {
        foreach (GameObject visual in noBuildZoneVisuals)
        {
            if (visual != null)
            {
                Object.Destroy(visual);
            }
        }
        noBuildZoneVisuals.Clear();
    }

    /// <summary>
    /// Create the no-build zone preview that follows the ghost building.
    /// </summary>
    public void CreateGhostZone(Vector3 ghostPosition, float radius)
    {
        DestroyGhostZone();

        GameObject zoneObj = new GameObject("GhostNoBuildZone");
        zoneObj.transform.position = new Vector3(ghostPosition.x, 0.05f, ghostPosition.z);

        if (owner.showNoBuildFills)
        {
            GameObject fillObj = new GameObject("Fill");
            fillObj.transform.SetParent(zoneObj.transform);
            fillObj.transform.localPosition = Vector3.zero;
            fillObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            MeshFilter meshFilter = fillObj.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = fillObj.AddComponent<MeshRenderer>();

            meshFilter.mesh = CreateQuadMesh(radius);

            Material fillMaterial = new Material(Shader.Find("Standard"));
            Color ghostZoneColor = new Color(0.3f, 0.7f, 1f, 0.25f);
            fillMaterial.color = ghostZoneColor;
            ConfigureTransparentMaterial(fillMaterial);
            meshRenderer.material = fillMaterial;
        }

        Color ghostBorderColor = new Color(0.3f, 0.9f, 1f, 0.6f);
        CreateZoneBorder(zoneObj, radius, ghostBorderColor);

        ghostNoBuildZone = zoneObj;
    }

    /// <summary>
    /// Keep the ghost zone preview aligned with the ghost building.
    /// </summary>
    public void UpdateGhostZone(Vector3 ghostPosition)
    {
        if (ghostNoBuildZone != null)
        {
            ghostNoBuildZone.transform.position = new Vector3(ghostPosition.x, 0.05f, ghostPosition.z);
        }
    }

    public void DestroyGhostZone()
    {
        if (ghostNoBuildZone != null)
        {
            Object.Destroy(ghostNoBuildZone);
            ghostNoBuildZone = null;
        }
    }

    // Get visual no-build radius for a building type from BuildingData
    float GetVisualNoBuildRadius(BuildingType type, float fallback)
    {
        if (BuildingDatabase.Instance == null) return fallback;
        BuildingData data = BuildingDatabase.Instance.GetBuildingData(type);
        return data != null ? data.visualNoBuildRadius : fallback;
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
            int centerGridX = Mathf.RoundToInt(zone.position.x / owner.cellSize);
            int centerGridZ = Mathf.RoundToInt(zone.position.z / owner.cellSize);
            int cellsToExtend = Mathf.FloorToInt(zone.radius / owner.cellSize);

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
            float offset = owner.cellSize;
            Vector3 worldP1 = new Vector3((x1 + centeringOffset) * owner.cellSize - offset, 0, (z1 + centeringOffset) * owner.cellSize - offset);
            Vector3 worldP2 = new Vector3((x2 + centeringOffset) * owner.cellSize - offset, 0, (z2 + centeringOffset) * owner.cellSize - offset);

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

    void CreateCircleVisual(Vector3 center, float radius)
    {
        GameObject zoneObj = new GameObject("NoBuildZone");
        zoneObj.transform.position = new Vector3(center.x, 0.05f, center.z);

        if (owner.showNoBuildFills)
        {
            GameObject fillObj = new GameObject("Fill");
            fillObj.transform.SetParent(zoneObj.transform);
            fillObj.transform.localPosition = Vector3.zero;
            fillObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            MeshFilter meshFilter = fillObj.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = fillObj.AddComponent<MeshRenderer>();

            meshFilter.mesh = CreateQuadMesh(radius);

            Material fillMaterial;
            if (owner.noBuildZoneMaterial != null)
            {
                fillMaterial = owner.noBuildZoneMaterial;
            }
            else
            {
                fillMaterial = new Material(Shader.Find("Standard"));
                fillMaterial.color = owner.noBuildZoneColor;
                ConfigureTransparentMaterial(fillMaterial);
            }
            meshRenderer.material = fillMaterial;
        }

        Color borderColor = new Color(1f, 0f, 0f, 0.8f);
        CreateZoneBorder(zoneObj, radius, borderColor);

        noBuildZoneVisuals.Add(zoneObj);
    }

    // Square outline (LineRenderer) child shared by building zones and the ghost zone
    void CreateZoneBorder(GameObject zoneObj, float radius, Color borderColor)
    {
        GameObject borderObj = new GameObject("Border");
        borderObj.transform.SetParent(zoneObj.transform);
        borderObj.transform.localPosition = new Vector3(0, 0.01f, 0);

        LineRenderer lineRenderer = borderObj.AddComponent<LineRenderer>();
        lineRenderer.positionCount = 5;
        lineRenderer.loop = true;
        lineRenderer.useWorldSpace = false;

        lineRenderer.startWidth = 0.15f;
        lineRenderer.endWidth = 0.15f;
        lineRenderer.startColor = borderColor;
        lineRenderer.endColor = borderColor;

        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.material.color = borderColor;

        lineRenderer.SetPosition(0, new Vector3(-radius, 0, -radius));
        lineRenderer.SetPosition(1, new Vector3(radius, 0, -radius));
        lineRenderer.SetPosition(2, new Vector3(radius, 0, radius));
        lineRenderer.SetPosition(3, new Vector3(-radius, 0, radius));
        lineRenderer.SetPosition(4, new Vector3(-radius, 0, -radius));
    }

    // Flat quad mesh used for zone fills
    Mesh CreateQuadMesh(float radius)
    {
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
        return mesh;
    }

    // Standard-shader transparent blend setup for zone fill materials
    void ConfigureTransparentMaterial(Material mat)
    {
        mat.SetFloat("_Mode", 3);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
    }
}
