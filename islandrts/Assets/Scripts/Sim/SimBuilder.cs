#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Places buildings the way a player would, minus the mouse.
///
/// Deliberately mirrors <c>GhostPlacer.ConfirmPlacement</c> and
/// <c>WallLinePlacer.ConfirmWallLine</c> step for step — affordability check,
/// spend, T2 flatten, instantiate the construction site (never the finished
/// building), Buildings layer, SetBuildingType — so a simulated build costs the
/// same, takes the same build time, and is destructible the same as a real one.
/// If those confirm paths ever change, this has to change with them.
///
/// The one thing it does NOT reproduce is placement validity as the ghost sees
/// it: it uses the same TerrainGrid.IsBuildable + Physics.CheckBox + WallGrid
/// tests, but not the no-build-zone overlap rules, so it can occasionally place
/// closer to a neighbour than a player could.
/// </summary>
public static class SimBuilder
{
    private static readonly Collider[] overlap = new Collider[8];

    public static BaseBuilding Campfire =>
        BaseBuilding.ActiveList.Count > 0 ? BaseBuilding.ActiveList[0] : null;

    // ---- counts the policies and metrics read -----------------------------

    public static int HutCount => Hut.ActiveList.Count;
    public static int WallCount => Wall.ActiveList.Count + Gate.ActiveList.Count;
    public static int TowerCount => Watchtower.ActiveList.Count;

    /// <summary>Construction sites of one type currently in flight (so a policy doesn't double-order).</summary>
    public static int PendingSites(BuildingType type)
    {
        int n = 0;
        var list = ConstructionSite.ActiveList;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != null && list[i].buildingType == type) n++;
        }
        return n;
    }

    // ---- placement --------------------------------------------------------

    /// <summary>
    /// Places one non-wall building at the first workable spot on a ring around
    /// the campfire, walking outward. Returns false if unaffordable or boxed in.
    /// </summary>
    public static bool PlaceBuilding(BuildingType type, float startRadius, float maxRadius)
    {
        BuildingData data = BuildingDatabase.Instance != null
            ? BuildingDatabase.Instance.GetBuildingData(type) : null;
        if (data == null || data.constructionSitePrefab == null) return false;
        if (ResourceManager.Instance == null || Campfire == null) return false;
        if (!ResourceManager.Instance.CanAfford(data.woodCost, data.foodCost, data.stoneCost)) return false;

        Vector3 origin = Campfire.transform.position;

        // 8 candidate spots per lap, laps 1.5m apart, offset per lap so later
        // laps don't sit in the shadow of a blocked earlier one.
        for (float radius = startRadius; radius <= maxRadius; radius += 1.5f)
        {
            float lapOffset = (radius - startRadius) * 11f;
            for (int i = 0; i < 8; i++)
            {
                float angle = (i * 45f + lapOffset) * Mathf.Deg2Rad;
                Vector3 pos = origin + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                pos = GridSnap.SnapXZ(pos, 1f);
                pos.y = GroundY(pos);

                if (!IsClear(pos, data.buildingSize)) continue;

                Spawn(data, type, pos, flatten: true);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Orders a square wall perimeter at <paramref name="halfExtent"/> cells from
    /// the campfire, leaving a one-cell gap mid-way along each side. The gaps are
    /// load-bearing: a sealed carving ring would trap every worker inside it.
    /// Returns how many sites were placed (0 if unaffordable or fully blocked).
    /// </summary>
    public static int PlaceWallRing(BuildingType wallType, int halfExtent, int maxSites)
    {
        BuildingData data = BuildingDatabase.Instance != null
            ? BuildingDatabase.Instance.GetBuildingData(wallType) : null;
        if (data == null || data.constructionSitePrefab == null) return 0;
        if (ResourceManager.Instance == null || Campfire == null || WallGrid.Instance == null) return 0;

        Vector2Int center = WallGrid.Instance.WorldToGrid(Campfire.transform.position);
        List<Vector2Int> cells = new List<Vector2Int>();

        for (int d = -halfExtent; d <= halfExtent; d++)
        {
            bool gap = d == 0;   // one opening per side, dead centre
            if (!gap)
            {
                cells.Add(new Vector2Int(center.x + d, center.y + halfExtent));
                cells.Add(new Vector2Int(center.x + d, center.y - halfExtent));
            }
            if (!gap && Mathf.Abs(d) != halfExtent)
            {
                cells.Add(new Vector2Int(center.x + halfExtent, center.y + d));
                cells.Add(new Vector2Int(center.x - halfExtent, center.y + d));
            }
        }

        int placed = 0;
        for (int i = 0; i < cells.Count && placed < maxSites; i++)
        {
            if (!ResourceManager.Instance.CanAfford(data.woodCost, data.foodCost, data.stoneCost)) break;
            if (WallGrid.Instance.HasWallAt(cells[i])) continue;

            Vector3 pos = WallGrid.Instance.GridToWorld(cells[i]);
            pos.y = GroundY(pos);
            if (TerrainGrid.Instance != null && !TerrainGrid.Instance.IsBuildable(pos)) continue;

            // Walls deliberately do NOT flatten — they follow the terrain per cell.
            Spawn(data, wallType, pos, flatten: false);
            placed++;
        }
        return placed;
    }

    /// <summary>
    /// Upgrades up to <paramref name="count"/> finished walls into gates (5 wood
    /// each), preferring the ones nearest the campfire's cardinal openings.
    /// </summary>
    public static int ConvertGates(int count)
    {
        if (ResourceManager.Instance == null) return 0;
        int done = 0;
        var walls = Wall.ActiveList;
        for (int i = walls.Count - 1; i >= 0 && done < count; i--)
        {
            Wall w = walls[i];
            if (w == null) continue;
            if (!ResourceManager.Instance.CanAfford(5, 0, 0)) break;
            ResourceManager.Instance.SpendResources(5, 0, 0);
            w.UpgradeToGate();
            done++;
        }
        return done;
    }

    // ---- shared internals -------------------------------------------------

    private static void Spawn(BuildingData data, BuildingType type, Vector3 pos, bool flatten)
    {
        ResourceManager.Instance.SpendResources(data.woodCost, data.foodCost, data.stoneCost);

        if (flatten && TerrainGrid.Instance != null)
        {
            TerrainGrid.Instance.FlattenArea(pos, 1.8f, 1.4f);
            pos.y = TerrainGrid.Instance.SampleHeight(pos);
        }
        pos.y += data.placementHeight;

        GameObject site = Object.Instantiate(data.constructionSitePrefab, pos, Quaternion.identity);
        int layer = LayerMask.NameToLayer("Buildings");
        if (layer >= 0) site.layer = layer;

        ConstructionSite comp = site.GetComponent<ConstructionSite>();
        if (comp != null) comp.SetBuildingType(type);
    }

    private static float GroundY(Vector3 pos)
    {
        return TerrainGrid.Instance != null ? TerrainGrid.Instance.SampleHeight(pos) : 0f;
    }

    private static bool IsClear(Vector3 pos, Vector3 size)
    {
        if (TerrainGrid.Instance != null && !TerrainGrid.Instance.IsBuildable(pos)) return false;

        int mask = LayerMask.GetMask("Buildings");
        int hits = Physics.OverlapBoxNonAlloc(
            pos + Vector3.up * (size.y * 0.5f), size * 0.55f, overlap, Quaternion.identity, mask);
        if (hits > 0) return false;

        // Keep off resource nodes — a real player's ghost reads red on them.
        var nodes = ResourceNode.ActiveList;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] == null) continue;
            Vector3 d = nodes[i].transform.position - pos;
            d.y = 0f;
            if (d.sqrMagnitude < 9f) return false;
        }
        return true;
    }
}
#endif
