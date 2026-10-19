using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

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

    public BaseBuilding Campfire => faction.Campfire;

    // ---- counts the policies and metrics read -----------------------------

    public int HutCount => TargetingUtil.CountOwned(Hut.ActiveList, faction);
    public int WallCount => TargetingUtil.CountOwned(Wall.ActiveList, faction) + TargetingUtil.CountOwned(Gate.ActiveList, faction);
    public int TowerCount => TargetingUtil.CountOwned(Watchtower.ActiveList, faction);
    public int WorkshopCount => TargetingUtil.CountOwned(Workshop.ActiveList, faction);
    public int ShipyardCount => TargetingUtil.CountOwned(Shipyard.ActiveList, faction);
    public int StorehouseCount => TargetingUtil.CountOwned(Storehouse.ActiveList, faction);
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
        if (Campfire == null) return false;
        return PlaceBuildingAround(type, Campfire.transform.position, startRadius, maxRadius);
    }

    /// <summary>
    /// The same spiral around any point (2026-09-16): a Storehouse goes up beside
    /// the forest the colonists are working, not beside the fire. Ring rules and
    /// clearance apply exactly as at the fire.
    /// </summary>
    public bool PlaceBuildingAround(BuildingType type, Vector3 origin, float startRadius, float maxRadius)
    {
        BuildingData data = BuildingDatabase.Instance != null
            ? BuildingDatabase.Instance.GetBuildingData(type) : null;
        if (data == null || data.constructionSitePrefab == null) return false;
        if (Campfire == null) return false;
        if (!faction.Resources.CanAfford(data.woodCost, data.foodCost, data.stoneCost, data.metalCost)) return false;

        // 8 candidate spots per lap, laps 1.5m apart, offset per lap so later
        // laps don't sit in the shadow of a blocked earlier one.
        for (float radius = startRadius; radius <= maxRadius; radius += 1.5f)
        {
            float lapOffset = (radius - startRadius) * 11f;
            int steps = Mathf.Max(8, Mathf.CeilToInt(Mathf.PI * 2f * radius / 3f));   // a candidate every ~3 m of arc (a fixed 8 left a thin annulus with 2-3 spots once spacing applied, 2026-10-19)
            for (int i = 0; i < steps; i++)
            {
                float angle = (i * (360f / steps) + lapOffset) * Mathf.Deg2Rad;
                Vector3 pos = origin + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                pos = GridSnap.SnapXZ(pos, 1f);
                pos.y = GroundY(pos);

                if (!IsClear(pos, data.buildingSize, data.noBuildRadius)) continue;
                if (BlocksRing(pos)) continue;

                SpawnSite(data, type, pos, flatten: true);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Orders the colony's wall ring (<see cref="EnsureRingPlan"/>): a closed loop that
    /// follows the FOOT of any steep ground instead of a fixed square, with a
    /// two-cell opening where each compass ray from the fire meets it (filled later
    /// by <see cref="GateOpenings"/>, which walls both cells and converts them into
    /// ONE three-wide gate). The openings are load-bearing: a sealed carving ring
    /// would trap every worker inside it. Every call walks the whole plan, so a gap
    /// left by a destroyed wall, unaffordable wood or unexplored ground is filled
    /// the next time round. Returns how many sites were placed (0 if unaffordable
    /// or complete).
    /// </summary>
    public int PlaceWallRing(BuildingType wallType, int halfExtent, int maxSites)
    {
        BuildingData data = BuildingDatabase.Instance != null
            ? BuildingDatabase.Instance.GetBuildingData(wallType) : null;
        if (data == null || data.constructionSitePrefab == null) return 0;
        if (Campfire == null || WallGrid.Instance == null) return 0;

        // Re-plan and re-examine every abandoned cell periodically; ground changes
        // under a colony (pads flatten, nodes deplete) and the plan follows it.
        if (Time.time >= nextHoleSweep)
        {
            holeCells.Clear();
            ringPlanned = false;
            nextHoleSweep = Time.time + HoleRetrySeconds;
        }
        EnsureRingPlan(halfExtent);

        int placed = 0;
        for (int i = 0; i < ringCells.Count && placed < maxSites; i++)
        {
            Vector2Int cell = ringCells[i];
            CellResult r = TryPlaceWallCell(data, wallType, cell);
            if (r == CellResult.Placed) placed++;
            else if (r == CellResult.Unbuildable) holeCells.Add(cell);
        }
        return placed;
    }

    // ---- the ring plan (2026-10-19) ------------------------------------------
    // The old ring was a square of cells at a fixed Chebyshev distance, notched
    // inward around anything unbuildable; on a mountainside that either climbed the
    // slope (IsBuildable only asks slope < 0.55) or left holes. The plan is instead
    // the boundary of the FLAT ground around the fire: flood-fill the flat cells
    // connected to the fire inside the old square bound, and the walled cells are
    // the region's edge cells that touch ground a raider could cross. Steep ground
    // is walkable (the bake climbs 45 degrees), so the wall hugs its foot and must
    // close; only sea and cliff (RaidersCanCross false) seal a stretch for free.
    private readonly List<Vector2Int> ringCells = new List<Vector2Int>();
    private readonly HashSet<Vector2Int> ringSet = new HashSet<Vector2Int>();
    private readonly List<Vector2Int> openingFirst = new List<Vector2Int>();
    private readonly List<Vector2Int> openingAlong = new List<Vector2Int>();
    private bool ringPlanned;
    private int plannedHalf;
    private Vector2Int plannedCentre;

    private static readonly Vector2Int[] Dir4 =
        { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

    private bool FlatCell(Vector2Int cell)
    {
        Vector3 p = WallGrid.Instance.GridToWorld(cell);
        p.y = GroundY(p);
        return TerrainGrid.Instance == null || (TerrainGrid.Instance.IsBuildable(p) && RingGroundOk(p));
    }

    private void EnsureRingPlan(int half)
    {
        Vector2Int centre = WallGrid.Instance.WorldToGrid(Campfire.transform.position);
        if (ringPlanned && plannedHalf == half && plannedCentre == centre) return;
        ringPlanned = true; plannedHalf = half; plannedCentre = centre;
        ringCells.Clear(); ringSet.Clear(); openingFirst.Clear(); openingAlong.Clear();

        // 1. Flat ground inside the square bound, smoothed so a one-cell finger of
        //    flat ground does not become a spur of wall (a cell needs 6 of its 3x3 flat).
        int side = half * 2 + 3;
        var flat = new Dictionary<Vector2Int, bool>(side * side);
        for (int dx = -half - 1; dx <= half + 1; dx++)
            for (int dy = -half - 1; dy <= half + 1; dy++)
            {
                var c = new Vector2Int(centre.x + dx, centre.y + dy);
                flat[c] = FlatCell(c);
            }
        var region = new HashSet<Vector2Int>();
        var stack = new Stack<Vector2Int>();
        region.Add(centre); stack.Push(centre);   // the fire's own pad is flat by construction
        while (stack.Count > 0)
        {
            Vector2Int c = stack.Pop();
            for (int d = 0; d < 4; d++)
            {
                Vector2Int n = c + Dir4[d];
                if (region.Contains(n)) continue;
                if (Mathf.Max(Mathf.Abs(n.x - centre.x), Mathf.Abs(n.y - centre.y)) > half) continue;
                if (!SolidFlat(flat, n)) continue;
                region.Add(n); stack.Push(n);
            }
        }

        // 1b. Shave the stair-steps: drop edge cells with fewer than 6 of their 3x3
        //     block in the region (removal only, so the loop never climbs a slope),
        //     twice, then keep what is still connected to the fire.
        for (int pass = 0; pass < 2; pass++)
        {
            var drop = new List<Vector2Int>();
            foreach (Vector2Int c in region)
            {
                if (c == centre) continue;
                int n = 0;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if (region.Contains(new Vector2Int(c.x + dx, c.y + dy))) n++;
                if (n < 6) drop.Add(c);
            }
            for (int i = 0; i < drop.Count; i++) region.Remove(drop[i]);
        }
        var connected = new HashSet<Vector2Int> { centre };
        stack.Push(centre);
        while (stack.Count > 0)
        {
            Vector2Int c = stack.Pop();
            for (int d = 0; d < 4; d++)
            {
                Vector2Int n = c + Dir4[d];
                if (region.Contains(n) && connected.Add(n)) stack.Push(n);
            }
        }
        region = connected;

        // 2. Wall the region's edge cells that touch crossable ground. An edge cell is
        //    one with ANY of its 8 neighbours outside the region, which keeps the loop
        //    4-connected (no diagonal links to seal).
        foreach (Vector2Int c in region)
        {
            bool needsWall = false;
            for (int dx = -1; dx <= 1 && !needsWall; dx++)
                for (int dy = -1; dy <= 1 && !needsWall; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    Vector2Int n = new Vector2Int(c.x + dx, c.y + dy);
                    if (region.Contains(n)) continue;
                    Vector3 np = WallGrid.Instance.GridToWorld(n);
                    np.y = GroundY(np);
                    if (RaidersCanCross(np)) needsWall = true;   // else sea / cliff: sealed by the island
                }
            if (needsWall) ringSet.Add(c);
        }

        // 3. A two-cell opening where each compass ray from the fire meets the loop.
        for (int d = 0; d < 4; d++)
        {
            Vector2Int last = centre;
            while (region.Contains(last + Dir4[d])) last += Dir4[d];
            if (!ringSet.Contains(last)) continue;       // sealed there by sea or cliff
            Vector2Int along = d < 2 ? new Vector2Int(0, 1) : new Vector2Int(1, 0);
            Vector2Int first = last;
            if (!ringSet.Contains(first + along))
            {
                if (ringSet.Contains(first - along)) first = first - along;
                else along = Vector2Int.zero;             // a lone cell: gated alone
            }
            openingFirst.Add(first);
            openingAlong.Add(along);
        }

        // 4. The cells to wall: the loop minus the openings, in a fixed order.
        foreach (Vector2Int c in ringSet)
        {
            bool opening = false;
            for (int i = 0; i < openingFirst.Count; i++)
                if (c == openingFirst[i] || c == openingFirst[i] + openingAlong[i]) { opening = true; break; }
            if (!opening) ringCells.Add(c);
        }
        ringCells.Sort((a, b) => (a.x != b.x) ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
    }

    /// <summary>Flat, and at least 6 of its 3x3 block flat: the smoothing in step 1.</summary>
    private static bool SolidFlat(Dictionary<Vector2Int, bool> flat, Vector2Int c)
    {
        bool f;
        if (!flat.TryGetValue(c, out f) || !f) return false;
        int n = 0;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (flat.TryGetValue(new Vector2Int(c.x + dx, c.y + dy), out f) && f) n++;
        return n >= 6;
    }

    /// <summary>
    /// A ring cell nothing can walk is sealed by the island itself (2026-09-16):
    /// water, a cliff face, a cut-off outcrop. Passable = the flood fill reaches it
    /// AND the NavMesh has a surface inside the cell.
    /// </summary>
    private static bool RaidersCanCross(Vector3 pos)
    {
        if (TerrainGrid.Instance != null && !TerrainGrid.Instance.IsReachable(pos)) return false;
        NavMeshHit hit;
        return NavMesh.SamplePosition(pos, out hit, 0.6f, NavMesh.AllAreas);
    }

    /// <summary>
    /// True when a non-wall building at <paramref name="pos"/> would sit on the
    /// ring's line or in a gate corridor (2026-09-10; follows the planned loop since
    /// 2026-10-19). Huts placed on the spiral once landed IN the openings of a ring,
    /// which sealed it, locked the militia outside and let the raid land inside.
    /// The band is two cells either side of the line; a corridor is seven cells wide
    /// and eleven deep around each opening.
    /// </summary>
    private bool BlocksRing(Vector3 pos)
    {
        if (RingHalf <= 0 || Campfire == null || WallGrid.Instance == null) return false;
        EnsureRingPlan(RingHalf);
        Vector2Int g = WallGrid.Instance.WorldToGrid(pos);
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                if (ringSet.Contains(new Vector2Int(g.x + dx, g.y + dy))) return true;
        for (int i = 0; i < openingFirst.Count; i++)
        {
            Vector2Int o = openingFirst[i];
            if (Mathf.Abs(g.x - o.x) <= 4 && Mathf.Abs(g.y - o.y) <= 4) return true;
        }
        return false;
    }

    /// <summary>
    /// Fills the ring's openings with gates, one step per opening per call: an empty
    /// opening cell gets a wall site (a gate is only ever converted from a finished
    /// wall, like the player's G key), a site is left to finish, and once both cells
    /// stand as walls the opening is converted (5 wood) through the same
    /// <see cref="Wall.UpgradeToGate"/> as the G key - ONE gate up to three wide.
    /// A side holding a gate is done. Returns how many cells it acted on this call
    /// (0 = nothing to do or unaffordable).
    /// </summary>
    public int GateOpenings(BuildingType wallType, int halfExtent)
    {
        BuildingData data = BuildingDatabase.Instance != null
            ? BuildingDatabase.Instance.GetBuildingData(wallType) : null;
        if (data == null || data.constructionSitePrefab == null) return 0;
        if (Campfire == null || WallGrid.Instance == null) return 0;

        EnsureRingPlan(halfExtent);
        int acted = 0;
        for (int i = 0; i < openingFirst.Count; i++)
            acted += GateOpening(data, wallType, openingFirst[i], openingAlong[i]);
        return acted;
    }


    /// <summary>One side's opening: the cell at <paramref name="first"/> and the next one <paramref name="along"/> the side.</summary>
    private int GateOpening(BuildingData data, BuildingType wallType, Vector2Int first, Vector2Int along)
    {
        WallGrid grid = WallGrid.Instance;
        Vector2Int second = first + along;
        MonoBehaviour a = grid.GetWallAt(first), b = grid.GetWallAt(second);
        if (a is Gate || b is Gate) return 0;          // gated

        // Wall both cells first. A cell the island refuses is left open and the other
        // is gated alone; one still unaffordable or unexplored is waited for.
        int placed = 0;
        bool waiting = false;
        if (a == null) Fill(data, wallType, first, ref placed, ref waiting);
        if (b == null && second != first) Fill(data, wallType, second, ref placed, ref waiting);
        if (placed > 0 || waiting) return placed;

        Wall wa = a as Wall, wb = b as Wall;
        if ((a != null && wa == null) || (b != null && wb == null)) return 0;   // a site still building

        // Convert from a wall of mine that joins nothing on a slant (the G key's rule).
        Wall pick = null;
        if (wa != null && wa.Faction == faction && !grid.HasDiagonalLink(first)) pick = wa;
        else if (wb != null && wb.Faction == faction && !grid.HasDiagonalLink(second)) pick = wb;
        if (pick == null) return 0;                    // a neighbour's wall on my ring line is theirs to gate

        if (!faction.Resources.CanAfford(5, 0, 0)) return 0;
        faction.Resources.SpendResources(5, 0, 0);     // BuildPlacement's G cost
        Gate gate = pick.UpgradeToGate();
        return gate != null ? gate.Width : 0;
    }

    private void Fill(BuildingData data, BuildingType wallType, Vector2Int cell, ref int placed, ref bool waiting)
    {
        CellResult r = TryPlaceWallCell(data, wallType, cell);
        if (r == CellResult.Placed) placed++;
        else if (r == CellResult.Unaffordable || r == CellResult.Unexplored) waiting = true;
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
        if (!RingGroundOk(pos)) return CellResult.Unbuildable;
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
                if (!IsClear(pos, data.buildingSize, data.noBuildRadius)) continue;
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

    /// <summary>
    /// A governed wall ring stays on the camp's own ground (2026-10-19). IsBuildable
    /// only asks for slope under 0.55, so a ring happily climbed a ridge and the
    /// wall ran over the mountain in the site shots. A ring cell must be gentle
    /// AND within <see cref="RingMaxRise"/> of the fire's height; a cell that fails
    /// is notched around inward like any other unbuildable one.
    /// </summary>
    private const float RingMaxSlope = 0.3f;
    private const float RingMaxRise = 2.5f;

    private bool RingGroundOk(Vector3 pos)
    {
        if (TerrainGrid.Instance == null || Campfire == null) return true;
        if (TerrainGrid.Instance.SlopeAt(pos) >= RingMaxSlope) return false;
        return Mathf.Abs(TerrainGrid.Instance.SampleHeight(pos) - GroundY(Campfire.transform.position)) <= RingMaxRise;
    }

    /// <summary>
    /// True when <paramref name="pos"/> sits inside another building's no-build
    /// square (or its own, if larger) - the rule GhostPlacer.IsTooCloseToExistingBuilding
    /// holds the player to, which this builder never applied: huts landed touching
    /// (2026-10-19). Walls are exempt (radius 0).
    /// </summary>
    private static bool TooClose(Vector3 pos, float ownRadius)
    {
        const float buffer = 0.1f;
        for (int i = 0; i < BaseBuilding.ActiveList.Count; i++) if (Near(BaseBuilding.ActiveList[i], pos, BaseBuilding.ActiveList[i] != null ? BaseBuilding.ActiveList[i].noBuildRadius : 0f, ownRadius, buffer)) return true;
        for (int i = 0; i < ConstructionSite.ActiveList.Count; i++) if (Near(ConstructionSite.ActiveList[i], pos, ConstructionSite.ActiveList[i] != null ? ConstructionSite.ActiveList[i].noBuildRadius : 0f, ownRadius, buffer)) return true;
        for (int i = 0; i < Hut.ActiveList.Count; i++) if (Near(Hut.ActiveList[i], pos, Hut.ActiveList[i] != null ? Hut.ActiveList[i].noBuildRadius : 0f, ownRadius, buffer)) return true;
        for (int i = 0; i < Workshop.ActiveList.Count; i++) if (Near(Workshop.ActiveList[i], pos, Workshop.ActiveList[i] != null ? Workshop.ActiveList[i].noBuildRadius : 0f, ownRadius, buffer)) return true;
        for (int i = 0; i < Shipyard.ActiveList.Count; i++) if (Near(Shipyard.ActiveList[i], pos, Shipyard.ActiveList[i] != null ? Shipyard.ActiveList[i].noBuildRadius : 0f, ownRadius, buffer)) return true;
        for (int i = 0; i < Storehouse.ActiveList.Count; i++) if (Near(Storehouse.ActiveList[i], pos, Storehouse.ActiveList[i] != null ? Storehouse.ActiveList[i].noBuildRadius : 0f, ownRadius, buffer)) return true;
        for (int i = 0; i < Watchtower.ActiveList.Count; i++) if (Near(Watchtower.ActiveList[i], pos, Watchtower.ActiveList[i] != null ? Watchtower.ActiveList[i].noBuildRadius : 0f, ownRadius, buffer)) return true;
        return false;
    }

    private static bool Near(Component other, Vector3 pos, float otherRadius, float ownRadius, float buffer)
    {
        if (other == null) return false;
        float r = Mathf.Max(otherRadius, ownRadius) + buffer;
        Vector3 p = other.transform.position;
        return Mathf.Abs(pos.x - p.x) < r && Mathf.Abs(pos.z - p.z) < r;
    }

    private bool IsClear(Vector3 pos, Vector3 size, float noBuildRadius)
    {
        if (TooClose(pos, noBuildRadius)) return false;
        return IsClear(pos, size);
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
