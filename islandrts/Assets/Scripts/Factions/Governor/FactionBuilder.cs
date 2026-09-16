using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Places buildings the way a player would, minus the mouse — for ONE colony.
/// Was the static <c>SimBuilder</c> (sim-only) until 2026-09-16, lap step 3
/// slice B: a rival colony's governor places through the same code now, so it
/// ships in every build and every colony holds its own instance. The ring
/// bookkeeping below (<see cref="RingHalf"/>, the hole set, the sweep timer)
/// is colony state, and colony state in a static is a leak — two colonies
/// sharing one hole set would each condemn the other's ring cells.
///
/// Deliberately mirrors <c>GhostPlacer.ConfirmPlacement</c> and
/// <c>WallLinePlacer.ConfirmWallLine</c> step for step — affordability check,
/// spend, T2 flatten, instantiate the construction site (never the finished
/// building), Buildings layer, SetBuildingType — so a governed build costs the
/// same, takes the same build time, and is destructible the same as a real one.
/// If those confirm paths ever change, this has to change with them (slice B2
/// is the step where they call THIS instead).
///
/// The one thing it does NOT reproduce is placement validity as the ghost sees
/// it: it uses the same TerrainGrid.IsBuildable + Physics.CheckBox + WallGrid
/// tests, but not the no-build-zone overlap rules, so it can occasionally place
/// closer to a neighbour than a player could.
///
/// The fog gates apply to the PLAYER's builder only: rivals stay omniscient by
/// decision (CLAUDE.md, Factions), and a rival that could not wall unexplored
/// ground would never finish a ring on an island nobody of theirs has walked.
/// </summary>
public sealed class FactionBuilder
{
    public readonly Faction faction;

    public FactionBuilder(Faction faction) { this.faction = faction; }

    // Scratch, not state: written and read inside one call.
    private static readonly Collider[] overlap = new Collider[8];
    private static readonly List<Vector2Int> detour = new List<Vector2Int>();

    public BaseBuilding Campfire => faction.Campfire;

    // ---- counts the policies and metrics read -----------------------------

    public int HutCount => TargetingUtil.CountOwned(Hut.ActiveList, faction);
    public int WallCount => TargetingUtil.CountOwned(Wall.ActiveList, faction) + TargetingUtil.CountOwned(Gate.ActiveList, faction);
    public int TowerCount => TargetingUtil.CountOwned(Watchtower.ActiveList, faction);
    public int WorkshopCount => TargetingUtil.CountOwned(Workshop.ActiveList, faction);
    public int ShipyardCount => TargetingUtil.CountOwned(Shipyard.ActiveList, faction);
    /// <summary>Gates standing. The ring's eight opening cells are the only place a governor makes them.</summary>
    public int GateCount => TargetingUtil.CountOwned(Gate.ActiveList, faction);

    // ---- the ring the policy is building (2026-09-10) ---------------------
    // Ring cells the builder could neither wall nor notch around; a hole is a
    // gap raiders walk through, and the third lab's Turtle lost every run whose
    // ring had one (seed 1042: 2 holes, 4711: 8) and won the one with none.
    private readonly HashSet<Vector2Int> holeCells = new HashSet<Vector2Int>();
    public int RingHoles => holeCells.Count;
    /// <summary>
    /// A hole is given up on for this sweep only (2026-09-11). The overnight batch
    /// lost every one of Turtle's 36 baseline runs with ~12 permanent holes in an
    /// 85-wall ring: the set was written once and never revisited, so ground that
    /// was merely unexplored, occupied by a node that later depleted, or flattened
    /// by a neighbouring pad stayed a gap for the whole run. The sweep clears it so
    /// every hole is re-examined, and the count at dawn is the last sweep's verdict.
    /// </summary>
    private const float HoleRetrySeconds = 45f;
    private float nextHoleSweep;
    /// <summary>Half-extent of the ring the policy builds, 0 = none; keeps huts off its line and out of its gate corridors.</summary>
    public int RingHalf { get; private set; }
    public void SetRing(int halfExtent) => RingHalf = halfExtent;

    /// <summary>This colony's construction sites of one type currently in flight (so a policy doesn't double-order).</summary>
    public int PendingSites(BuildingType type)
    {
        int n = 0;
        var list = ConstructionSite.ActiveList;
        for (int i = 0; i < list.Count; i++)
        {
            ConstructionSite s = list[i];
            if (s != null && s.buildingType == type && s.Faction == faction) n++;
        }
        return n;
    }

    // ---- placement --------------------------------------------------------

    /// <summary>
    /// Places one non-wall building at the first workable spot on a ring around
    /// the campfire, walking outward. Returns false if unaffordable or boxed in.
    /// </summary>
    public bool PlaceBuilding(BuildingType type, float startRadius, float maxRadius)
    {
        BuildingData data = BuildingDatabase.Instance != null
            ? BuildingDatabase.Instance.GetBuildingData(type) : null;
        if (data == null || data.constructionSitePrefab == null) return false;
        if (Campfire == null) return false;
        if (!faction.Resources.CanAfford(data.woodCost, data.foodCost, data.stoneCost, data.metalCost)) return false;

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

                SpawnSite(data, type, pos, flatten: true);
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
    public int PlaceWallRing(BuildingType wallType, int halfExtent, int maxSites)
    {
        BuildingData data = BuildingDatabase.Instance != null
            ? BuildingDatabase.Instance.GetBuildingData(wallType) : null;
        if (data == null || data.constructionSitePrefab == null) return 0;
        if (Campfire == null || WallGrid.Instance == null) return 0;

        // Re-examine every abandoned cell periodically; ground changes under a
        // colony (fog lifts, nodes deplete, pads flatten) and a hole left forever
        // is a hole raiders walk through forever.
        if (Time.time >= nextHoleSweep)
        {
            holeCells.Clear();
            nextHoleSweep = Time.time + HoleRetrySeconds;
        }

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
    /// with a detour INWARD, tried at depth 1 first and deepening to
    /// <see cref="MaxDetourDepth"/> (2026-09-11; depth 1 only until then, which
    /// left ~12 permanent holes in every Turtle ring of the overnight batch).
    ///
    /// On a side the detour is a U: the two neighbouring offsets run inward to
    /// the depth and a crossbar closes them there, so the ring stays orthogonally
    /// connected. On a CORNER it is an L cutting across the corner at that depth -
    /// a corner used to be an instant hole, and a square ring has four of them.
    ///
    /// A depth is usable when every one of its cells is already walled, buildable,
    /// or still unexplored; unexplored ground defers the whole cell to a later call
    /// rather than condemning it. Only when no depth works is the cell a hole.
    /// </summary>
    private const int MaxDetourDepth = 4;

    private int Notch(BuildingData data, BuildingType wallType, Vector2Int cell,
                      Vector2Int center, int halfExtent, int budget)
    {
        int dx = cell.x - center.x, dy = cell.y - center.y;
        bool onRow = Mathf.Abs(dy) == halfExtent;
        bool onCol = Mathf.Abs(dx) == halfExtent;

        for (int depth = 1; depth <= MaxDetourDepth && depth < halfExtent; depth++)
        {
            BuildDetour(cell, center, halfExtent, depth, onRow, onCol);
            if (detour.Count == 0) continue;

            bool blocked = false, unexplored = false;
            for (int i = 0; i < detour.Count; i++)
            {
                if (WallGrid.Instance.HasWallAt(detour[i])) continue;
                Vector3 pos = WallGrid.Instance.GridToWorld(detour[i]);
                pos.y = GroundY(pos);
                if (TerrainGrid.Instance != null && !TerrainGrid.Instance.IsBuildable(pos)) { blocked = true; break; }
                if (!Explored(pos)) unexplored = true;
            }
            if (blocked) continue;          // try one cell deeper
            if (unexplored) return 0;       // come back once it is seen

            int placed = 0;
            for (int i = 0; i < detour.Count && placed < budget; i++)
                if (TryPlaceWallCell(data, wallType, detour[i]) == CellResult.Placed) placed++;
            return placed;
        }

        holeCells.Add(cell);
        return 0;
    }

    /// <summary>The cells of one detour attempt, written into <see cref="detour"/> so a call allocates nothing.</summary>
    private static void BuildDetour(Vector2Int cell, Vector2Int center, int halfExtent, int depth,
                                    bool onRow, bool onCol)
    {
        detour.Clear();
        int dx = cell.x - center.x, dy = cell.y - center.y;

        if (onRow && onCol)
        {
            // Corner: an L cutting across it at this depth. One arm ends beside
            // the ring's row, the other beside its column, so both stay connected.
            int sx = dx > 0 ? 1 : -1, sy = dy > 0 ? 1 : -1;
            int inset = halfExtent - depth;
            for (int a = inset; a <= halfExtent - 1; a++)
                detour.Add(new Vector2Int(center.x + sx * a, center.y + sy * inset));
            for (int b = inset; b <= halfExtent - 1; b++)
                detour.Add(new Vector2Int(center.x + sx * inset, center.y + sy * b));
            return;
        }

        Vector2Int inward = onRow ? new Vector2Int(0, dy > 0 ? -1 : 1) : new Vector2Int(dx > 0 ? -1 : 1, 0);
        Vector2Int tangent = onRow ? new Vector2Int(1, 0) : new Vector2Int(0, 1);

        // Two stiles running inward from the neighbouring offsets, joined by a
        // crossbar at the far end.
        for (int j = 1; j <= depth; j++)
        {
            detour.Add(cell + tangent + inward * j);
            detour.Add(cell - tangent + inward * j);
        }
        detour.Add(cell + inward * depth);
    }

    /// <summary>
    /// True when a non-wall building at <paramref name="pos"/> would sit on the
    /// ring's line or in a gate corridor (2026-09-10). Watched in the third lab:
    /// huts placed on the 7-16 m spiral landed IN the openings of a 9-cell ring,
    /// which sealed it, locked the militia outside and let the raid land inside.
    /// The band is two cells either side of the line; a corridor is five cells
    /// deep on both sides of each opening.
    /// </summary>
    private bool BlocksRing(Vector3 pos)
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
    public int GateOpenings(BuildingType wallType, int halfExtent)
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

    private int GateCell(BuildingData data, BuildingType wallType, Vector2Int cell)
    {
        MonoBehaviour occupant = WallGrid.Instance.GetWallAt(cell);
        if (occupant == null) return TryPlaceWallCell(data, wallType, cell) == CellResult.Placed ? 1 : 0;

        Wall wall = occupant as Wall;
        if (wall == null) return 0;                    // a site still building, or already a gate
        if (wall.Faction != faction) return 0;         // a neighbour's wall on my ring line is theirs to gate
        if (!faction.Resources.CanAfford(5, 0, 0)) return 0;
        faction.Resources.SpendResources(5, 0, 0);     // BuildPlacement's G cost
        wall.UpgradeToGate();
        return 1;
    }

    private enum CellResult { Placed, Occupied, Unaffordable, Unbuildable, Unexplored }

    /// <summary>One wall site at a grid cell, the WallLinePlacer.CellBlocked tests included.</summary>
    private CellResult TryPlaceWallCell(BuildingData data, BuildingType wallType, Vector2Int cell)
    {
        if (WallGrid.Instance.HasWallAt(cell)) return CellResult.Occupied;
        if (!faction.Resources.CanAfford(data.woodCost, data.foodCost, data.stoneCost)) return CellResult.Unaffordable;

        Vector3 pos = WallGrid.Instance.GridToWorld(cell);
        pos.y = GroundY(pos);
        if (TerrainGrid.Instance != null && !TerrainGrid.Instance.IsBuildable(pos)) return CellResult.Unbuildable;
        if (!Explored(pos)) return CellResult.Unexplored;   // WallLinePlacer.CellBlocked

        // Walls deliberately do NOT flatten — they follow the terrain per cell.
        SpawnSite(data, wallType, pos, flatten: false);
        return CellResult.Placed;
    }

    // ---- shared internals -------------------------------------------------

    /// <summary>
    /// Place a shore building (the Shipyard, 2026-09-04): spiral out from the
    /// campfire in 2 m steps until a buildable cell within GhostPlacer.ShoreRadius
    /// of the water is clear, then the normal confirm mirror. False when none is
    /// found within <paramref name="maxRadius"/> or it is unaffordable.
    /// </summary>
    public bool PlaceShoreBuilding(BuildingType type, float maxRadius)
    {
        BuildingData data = BuildingDatabase.Instance != null
            ? BuildingDatabase.Instance.GetBuildingData(type) : null;
        if (data == null || data.constructionSitePrefab == null) return false;
        if (Campfire == null || TerrainGrid.Instance == null) return false;
        if (!faction.Resources.CanAfford(data.woodCost, data.foodCost, data.stoneCost, data.metalCost)) return false;

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

                SpawnSite(data, type, pos, flatten: true);
                return true;
            }
        }
        return false;
    }

    private void SpawnSite(BuildingData data, BuildingType type, Vector3 pos, bool flatten)
    {
        faction.Resources.SpendResources(data.woodCost, data.foodCost, data.stoneCost, data.metalCost);

        if (flatten && TerrainGrid.Instance != null)
        {
            TerrainGrid.Instance.FlattenArea(pos, 1.8f, 1.4f);
            pos.y = TerrainGrid.Instance.SampleHeight(pos);
        }
        pos.y += data.placementHeight;

        GameObject site = Spawn.Owned(data.constructionSitePrefab, pos, Quaternion.identity, faction);
        int layer = LayerMask.NameToLayer("Buildings");
        if (layer >= 0) site.layer = layer;

        ConstructionSite comp = site.GetComponent<ConstructionSite>();
        if (comp != null) comp.SetBuildingType(type);

        if (!faction.IsPlayer) DevQuests.Signal("rival:built");
    }

    /// <summary>The fog gate, player-only: a rival sees the whole island (CLAUDE.md, Factions).</summary>
    private bool Explored(Vector3 pos)
    {
        if (!faction.IsPlayer) return true;
        return FogOfWar.Instance == null || FogOfWar.Instance.IsExplored(pos);
    }

    private static float GroundY(Vector3 pos)
    {
        return TerrainGrid.Instance != null ? TerrainGrid.Instance.SampleHeight(pos) : 0f;
    }

    private bool IsClear(Vector3 pos, Vector3 size)
    {
        if (TerrainGrid.Instance != null && !TerrainGrid.Instance.IsBuildable(pos)) return false;
        if (!Explored(pos)) return false;   // GhostPlacer's fog gate

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
