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

    public static BaseBuilding Campfire => Factions.Player.Campfire;

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
        if (Campfire == null) return false;
        if (!Factions.Player.Resources.CanAfford(data.woodCost, data.foodCost, data.stoneCost, data.metalCost)) return false;

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
    /// the campfire, leaving a TWO-cell opening mid-way along each side. The
    /// openings are load-bearing: a sealed carving ring would trap every worker
    /// inside it. They are filled later by <see cref="GateOpenings"/>, which walls
    /// each cell and converts it to a gate the tick it finishes — two gates side
    /// by side per side (2026-09-10) so a column of workers or warriors paths
    /// through without queueing on one cell.
    /// Returns how many sites were placed (0 if unaffordable or fully blocked).
    /// </summary>
    public static int PlaceWallRing(BuildingType wallType, int halfExtent, int maxSites)
    {
        BuildingData data = BuildingDatabase.Instance != null
            ? BuildingDatabase.Instance.GetBuildingData(wallType) : null;
        if (data == null || data.constructionSitePrefab == null) return 0;
        if (Campfire == null || WallGrid.Instance == null) return 0;

        Vector2Int center = WallGrid.Instance.WorldToGrid(Campfire.transform.position);
        List<Vector2Int> cells = new List<Vector2Int>();

        for (int d = -halfExtent; d <= halfExtent; d++)
        {
            bool gap = IsOpeningOffset(d);
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
            if (!TryPlaceWallCell(data, wallType, cells[i])) continue;
            placed++;
        }
        return placed;
    }

    /// <summary>The two cells of each side's opening: offsets 0 and 1 along the side.</summary>
    private static bool IsOpeningOffset(int d) => d == 0 || d == 1;

    /// <summary>
    /// Fills the ring's openings with gates, one step per cell per call: an empty
    /// opening cell gets a wall site (a gate is only ever converted from a finished
    /// wall, like the player's G key), a finished wall in one is converted (5 wood),
    /// a site is left to finish, a gate is done. The lab of 2026-09-10 showed why
    /// this exists: the old ConvertGates turned the two NEWEST walls into gates
    /// wherever they stood and left the openings as bare holes, so raiders walked
    /// the ring's gaps and no wall took a hit all lab long. Returns how many
    /// cells it acted on this call (0 = nothing to do or unaffordable).
    /// </summary>
    public static int GateOpenings(BuildingType wallType, int halfExtent)
    {
        BuildingData data = BuildingDatabase.Instance != null
            ? BuildingDatabase.Instance.GetBuildingData(wallType) : null;
        if (data == null || data.constructionSitePrefab == null) return 0;
        if (Campfire == null || WallGrid.Instance == null) return 0;

        Vector2Int center = WallGrid.Instance.WorldToGrid(Campfire.transform.position);
        int acted = 0;
        for (int d = 0; d <= 1; d++)
        {
            acted += GateCell(data, wallType, new Vector2Int(center.x + d, center.y + halfExtent));
            acted += GateCell(data, wallType, new Vector2Int(center.x + d, center.y - halfExtent));
            acted += GateCell(data, wallType, new Vector2Int(center.x + halfExtent, center.y + d));
            acted += GateCell(data, wallType, new Vector2Int(center.x - halfExtent, center.y + d));
        }
        return acted;
    }

    /// <summary>Gates standing. The ring's eight opening cells are the only place the sim makes them.</summary>
    public static int GateCount => Gate.ActiveList.Count;

    private static int GateCell(BuildingData data, BuildingType wallType, Vector2Int cell)
    {
        MonoBehaviour occupant = WallGrid.Instance.GetWallAt(cell);
        if (occupant == null) return TryPlaceWallCell(data, wallType, cell) ? 1 : 0;

        Wall wall = occupant as Wall;
        if (wall == null) return 0;   // a site still building, or already a gate
        if (!Factions.Player.Resources.CanAfford(5, 0, 0)) return 0;
        Factions.Player.Resources.SpendResources(5, 0, 0);   // BuildPlacement's G cost
        wall.UpgradeToGate();
        return 1;
    }

    /// <summary>One wall site at a grid cell, the WallLinePlacer.CellBlocked tests included.</summary>
    private static bool TryPlaceWallCell(BuildingData data, BuildingType wallType, Vector2Int cell)
    {
        if (!Factions.Player.Resources.CanAfford(data.woodCost, data.foodCost, data.stoneCost)) return false;
        if (WallGrid.Instance.HasWallAt(cell)) return false;

        Vector3 pos = WallGrid.Instance.GridToWorld(cell);
        pos.y = GroundY(pos);
        if (TerrainGrid.Instance != null && !TerrainGrid.Instance.IsBuildable(pos)) return false;
        if (FogOfWar.Instance != null && !FogOfWar.Instance.IsExplored(pos)) return false;   // WallLinePlacer.CellBlocked

        // Walls deliberately do NOT flatten — they follow the terrain per cell.
        Spawn(data, wallType, pos, flatten: false);
        return true;
    }

    // ---- shared internals -------------------------------------------------

    /// <summary>
    /// Place a shore building (the Shipyard, 2026-09-04): spiral out from the
    /// campfire in 2 m steps until a buildable cell within GhostPlacer.ShoreRadius
    /// of the water is clear, then the normal confirm mirror. False when none is
    /// found within <paramref name="maxRadius"/> or it is unaffordable.
    /// </summary>
    public static bool PlaceShoreBuilding(BuildingType type, float maxRadius)
    {
        BuildingData data = BuildingDatabase.Instance != null
            ? BuildingDatabase.Instance.GetBuildingData(type) : null;
        if (data == null || data.constructionSitePrefab == null) return false;
        if (Campfire == null || TerrainGrid.Instance == null) return false;
        if (!Factions.Player.Resources.CanAfford(data.woodCost, data.foodCost, data.stoneCost, data.metalCost)) return false;

        Vector3 origin = Campfire.transform.position;
        for (float radius = 6f; radius <= maxRadius; radius += 2f)
        {
            int steps = Mathf.Max(8, Mathf.RoundToInt(radius));
            for (int i = 0; i < steps; i++)
            {
                float angle = i * (Mathf.PI * 2f / steps);
                Vector3 pos = origin + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                pos = GridSnap.SnapXZ(pos, 1f);
                pos.y = GroundY(pos);

                if (!TerrainGrid.Instance.IsNearWater(pos, GhostPlacer.ShoreRadius)) continue;
                if (!IsClear(pos, data.buildingSize)) continue;

                Spawn(data, type, pos, flatten: true);
                return true;
            }
        }
        return false;
    }

    private static void Spawn(BuildingData data, BuildingType type, Vector3 pos, bool flatten)
    {
        Factions.Player.Resources.SpendResources(data.woodCost, data.foodCost, data.stoneCost, data.metalCost);

        if (flatten && TerrainGrid.Instance != null)
        {
            TerrainGrid.Instance.FlattenArea(pos, 1.8f, 1.4f);
            pos.y = TerrainGrid.Instance.SampleHeight(pos);
        }
        pos.y += data.placementHeight;

        GameObject site = global::Spawn.Owned(data.constructionSitePrefab, pos, Quaternion.identity, Factions.Player);   // global:: because this class has its own Spawn method
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
        if (FogOfWar.Instance != null && !FogOfWar.Instance.IsExplored(pos)) return false;   // GhostPlacer's fog gate

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
