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

    // ---- the ring the policy is building (2026-09-10) ---------------------
    // Ring cells the builder could neither wall nor notch around; a hole is a
    // gap raiders walk through, and the third lab's Turtle lost every run whose
    // ring had one (seed 1042: 2 holes, 4711: 8) and won the one with none.
    private static readonly HashSet<Vector2Int> holeCells = new HashSet<Vector2Int>();
    public static int RingHoles => holeCells.Count;
    /// <summary>Half-extent of the ring the policy builds, 0 = none; keeps huts off its line and out of its gate corridors.</summary>
    public static int RingHalf { get; private set; }
    public static void SetRing(int halfExtent) => RingHalf = halfExtent;

    /// <summary>Per-run state; SimRunner calls it before every run.</summary>
    public static void ResetRun()
    {
        holeCells.Clear();
        RingHalf = 0;
    }

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
                if (BlocksRing(pos)) continue;

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
            Vector2Int cell = cells[i];
            if (holeCells.Contains(cell)) continue;
            CellResult r = TryPlaceWallCell(data, wallType, cell);
            if (r == CellResult.Placed) { placed++; continue; }
            if (r != CellResult.Unbuildable) continue;   // exists, unaffordable, or unexplored (retried later)
            placed += Notch(data, wallType, cell, center, halfExtent, maxSites - placed);
        }
        return placed;
    }

    /// <summary>
    /// A ring cell that cannot take a wall (water, a slope, a node) is bypassed
    /// one cell inward with a three-cell U (2026-09-10): the inward cell plus its
    /// two neighbours along the side, so the ring stays orthogonally connected.
    /// A corner, or a notch cell that is itself unbuildable, is a hole and is
    /// counted; an unexplored notch cell is left for a later call.
    /// </summary>
    private static int Notch(BuildingData data, BuildingType wallType, Vector2Int cell,
                             Vector2Int center, int halfExtent, int budget)
    {
        int dx = cell.x - center.x, dy = cell.y - center.y;
        bool onRow = Mathf.Abs(dy) == halfExtent;
        bool onCol = Mathf.Abs(dx) == halfExtent;
        if (onRow && onCol) { holeCells.Add(cell); return 0; }   // corner

        Vector2Int inward = onRow ? new Vector2Int(0, dy > 0 ? -1 : 1) : new Vector2Int(dx > 0 ? -1 : 1, 0);
        Vector2Int tangent = onRow ? new Vector2Int(1, 0) : new Vector2Int(0, 1);
        Vector2Int n0 = cell + inward, n1 = n0 + tangent, n2 = n0 - tangent;

        // All three must be walled, buildable or (for now) unexplored before any is ordered.
        Vector2Int[] notch = { n0, n1, n2 };
        for (int i = 0; i < notch.Length; i++)
        {
            if (WallGrid.Instance.HasWallAt(notch[i])) continue;
            Vector3 pos = WallGrid.Instance.GridToWorld(notch[i]);
            pos.y = GroundY(pos);
            if (TerrainGrid.Instance != null && !TerrainGrid.Instance.IsBuildable(pos)) { holeCells.Add(cell); return 0; }
            if (FogOfWar.Instance != null && !FogOfWar.Instance.IsExplored(pos)) return 0;
        }

        int placed = 0;
        for (int i = 0; i < notch.Length && placed < budget; i++)
            if (TryPlaceWallCell(data, wallType, notch[i]) == CellResult.Placed) placed++;
        return placed;
    }

    /// <summary>
    /// True when a non-wall building at <paramref name="pos"/> would sit on the
    /// ring's line or in a gate corridor (2026-09-10). Watched in the third lab:
    /// huts placed on the 7-16 m spiral landed IN the openings of a 9-cell ring,
    /// which sealed it, locked the militia outside and let the raid land inside.
    /// The band is two cells either side of the line; a corridor is five cells
    /// deep on both sides of each opening.
    /// </summary>
    private static bool BlocksRing(Vector3 pos)
    {
        if (RingHalf <= 0 || Campfire == null || WallGrid.Instance == null) return false;
        Vector2Int c = WallGrid.Instance.WorldToGrid(Campfire.transform.position);
        Vector2Int g = WallGrid.Instance.WorldToGrid(pos);
        int dx = Mathf.Abs(g.x - c.x), dy = Mathf.Abs(g.y - c.y);
        int cheb = Mathf.Max(dx, dy);
        if (cheb >= RingHalf - 2 && cheb <= RingHalf + 2) return true;
        // Openings sit at offsets 0 and 1 along each side (IsOpeningOffset).
        int ox = g.x - c.x, oy = g.y - c.y;
        bool nearX = ox >= -2 && ox <= 3, nearY = oy >= -2 && oy <= 3;
        int deep = RingHalf + 5, shallow = RingHalf - 5;
        if (nearX && dy >= shallow && dy <= deep) return true;
        if (nearY && dx >= shallow && dx <= deep) return true;
        return false;
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
        if (occupant == null) return TryPlaceWallCell(data, wallType, cell) == CellResult.Placed ? 1 : 0;

        Wall wall = occupant as Wall;
        if (wall == null) return 0;   // a site still building, or already a gate
        if (!Factions.Player.Resources.CanAfford(5, 0, 0)) return 0;
        Factions.Player.Resources.SpendResources(5, 0, 0);   // BuildPlacement's G cost
        wall.UpgradeToGate();
        return 1;
    }

    private enum CellResult { Placed, Occupied, Unaffordable, Unbuildable, Unexplored }

    /// <summary>One wall site at a grid cell, the WallLinePlacer.CellBlocked tests included.</summary>
    private static CellResult TryPlaceWallCell(BuildingData data, BuildingType wallType, Vector2Int cell)
    {
        if (WallGrid.Instance.HasWallAt(cell)) return CellResult.Occupied;
        if (!Factions.Player.Resources.CanAfford(data.woodCost, data.foodCost, data.stoneCost)) return CellResult.Unaffordable;

        Vector3 pos = WallGrid.Instance.GridToWorld(cell);
        pos.y = GroundY(pos);
        if (TerrainGrid.Instance != null && !TerrainGrid.Instance.IsBuildable(pos)) return CellResult.Unbuildable;
        if (FogOfWar.Instance != null && !FogOfWar.Instance.IsExplored(pos)) return CellResult.Unexplored;   // WallLinePlacer.CellBlocked

        // Walls deliberately do NOT flatten — they follow the terrain per cell.
        Spawn(data, wallType, pos, flatten: false);
        return CellResult.Placed;
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
                if (BlocksRing(pos)) continue;

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
