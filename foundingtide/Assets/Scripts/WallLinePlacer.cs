using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Wall placement by drawing (2026-09-22, replaced click-start / click-end).
/// Two gestures share one path:
/// - DRAG: press and draw; releasing places the stroke.
/// - CLICK POINTS: a click that does not drag drops a point; each further click adds one
///   (a drag between clicks adds a freehand run); double-click or FinishWallLine places it.
/// A freehand path is smoothed (Douglas-Peucker; clicked points are always kept), then each
/// straight piece is rasterized into cells with an 8-connected line, so a slant becomes a
/// diagonal run that WallConnector draws as one slanted wall. Hold StraightWallPath (Shift)
/// for the old L-shaped path between clicked points (R flips which leg comes first).
/// Plain helper owned by BuildPlacement — not a MonoBehaviour.
/// </summary>
public class WallLinePlacer
{
    private readonly BuildPlacement owner;

    enum Stage { Cursor, Pressing, Waypoints }
    private Stage stage = Stage.Cursor;

    // Drawing tunables, in metres (one cell = 1 m)
    const float DragStartDistance = 1.2f;   // a press that travels this far is a stroke, not a click
    const float SampleSpacing = 0.5f;       // freehand samples are at least this far apart
    const float SimplifyTolerance = 0.7f;   // how far a stroke may wobble before it earns a bend
    const float CloseLoopDistance = 1.5f;   // ending this near the first point closes the ring
    const float CloseLoopMinLength = 6f;    // ...once the path is long enough to be a ring
    const float DoubleClickSeconds = 0.4f;
    const int MaxLineCells = 400;           // a runaway scribble is capped, not placed whole

    // The path: every point the player put down, and which of them were CLICKS (kept by the
    // smoothing and used by the square path) rather than freehand samples.
    private readonly List<Vector3> pathPoints = new List<Vector3>();
    private readonly List<bool> pathClicked = new List<bool>();
    private Vector3 pressStart;
    private bool dragging;          // the current press has become a stroke
    private bool pressHeld;         // a press that began in Waypoints is still held
    private float lastClickTime;
    private Vector2Int lastClickCell;

    // Per-frame working sets, reused so a drag allocates nothing
    private readonly List<Vector3> polyPoints = new List<Vector3>();
    private readonly List<bool> polyClicked = new List<bool>();
    private readonly List<bool> keep = new List<bool>();
    private readonly List<Vector2Int> lineCells = new List<Vector2Int>();
    private readonly HashSet<Vector2Int> lineCellSet = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> validCellSet = new HashSet<Vector2Int>();
    private readonly List<bool> cellBlocked = new List<bool>();
    private bool lastSquare;        // the last preview was the square path
    private bool loopClosed;        // the last preview snapped its end onto its start

    // Ghost pool, one per cell of the line
    private readonly List<GameObject> wallLineGhosts = new List<GameObject>();
    private readonly List<MeshFilter> ghostFilters = new List<MeshFilter>();
    private readonly List<Material> ghostMaterials = new List<Material>();

    // Walls that the line being drawn would actually place (2026-09-18). The build
    // palette pulls this each frame for its running total, rather than the placer
    // pushing a cost string into a UI it should not know about.
    private int lineWallCount;
    public bool IsDrawingLine => stage != Stage.Cursor;
    public int LineWallCount => IsDrawingLine ? lineWallCount : 0;

    // Square path: true = go along X first, then Z; toggled with R
    private bool xFirst = true;

    // Shared ghost colours — light blue, semi-transparent; red where a wall cannot go
    private static readonly Color wallGhostColor = new Color(0.4f, 0.7f, 1f, 0.35f);
    private static readonly Color wallGhostInvalidColor = new Color(1f, 0.3f, 0.3f, 0.35f);

    public WallLinePlacer(BuildPlacement owner)
    {
        this.owner = owner;
    }

    /// <summary>
    /// Per-frame update while a wall type is selected in build mode.
    /// </summary>
    public void Tick()
    {
        Vector3 raw;
        if (!owner.GetMouseGroundPoint(out raw)) return;
        raw.y = 0f;

        // R flips the square path's first leg, before or during a line
        if (KeyBindings.Down(KeyBindings.Action.RotateBuilding)) xFirst = !xFirst;

        bool clickDown = Input.GetMouseButtonDown(0) && !PointerBlock.OverHud;
        bool cancel = Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1);

        switch (stage)
        {
            case Stage.Cursor:
                TickCursor(raw, clickDown, cancel);
                break;
            case Stage.Pressing:
                TickPressing(raw, cancel);
                break;
            case Stage.Waypoints:
                TickWaypoints(raw, clickDown, cancel);
                break;
        }
    }

    // Phase 1: one post follows the mouse, waiting for the first press
    void TickCursor(Vector3 raw, bool clickDown, bool cancel)
    {
        Vector2Int cell = CellOf(raw);
        Vector3 cursorPos = CellCentre(cell);
        cursorPos.y = owner.GroundYAt(cursorPos) + 0.02f;
        owner.currentGhost.transform.position = cursorPos;
        owner.SetGhostColor(CellBlocked(cell) ? wallGhostInvalidColor : wallGhostColor);

        if (clickDown)
        {
            ClearPath();
            AddPoint(raw, true);
            pressStart = raw;
            dragging = false;
            lastClickTime = Time.unscaledTime;
            lastClickCell = cell;
            stage = Stage.Pressing;
            owner.currentGhost.SetActive(false);  // line ghosts take over
            UpdatePreview(raw, false);
            return;
        }

        // Cancel: exit build mode
        if (cancel) owner.CancelPlacement();
    }

    // The first press is held: it becomes a stroke once it travels, a click if it does not
    void TickPressing(Vector3 raw, bool cancel)
    {
        if (cancel) { CancelWallLine(); return; }

        if (!dragging && FlatDistance(raw, pressStart) >= DragStartDistance) dragging = true;
        if (dragging) AddSample(raw);

        UpdatePreview(raw, dragging);

        if (!Input.GetMouseButton(0))
        {
            if (dragging)
            {
                // A stroke places on release. Its end goes into the path first, so a
                // stroke that cannot be afforded stays whole as a clicked line.
                AddPoint(raw, true);
                dragging = false;
                UpdatePreview(raw, false);
                ConfirmWallLine("wall:drawn");
            }
            else
            {
                stage = Stage.Waypoints;       // a click starts a clicked line
            }
        }
    }

    // A clicked line: each click adds a point, the cursor previews the next leg
    void TickWaypoints(Vector3 raw, bool clickDown, bool cancel)
    {
        if (cancel) { CancelWallLine(); return; }

        if (clickDown)
        {
            Vector2Int cell = CellOf(raw);
            bool doubleClick = cell == lastClickCell && Time.unscaledTime - lastClickTime <= DoubleClickSeconds;
            if (doubleClick)
            {
                UpdatePreview(raw, true);
                ConfirmWallLine("wall:double_click");
                return;
            }

            AddPoint(raw, true);
            lastClickTime = Time.unscaledTime;
            lastClickCell = cell;
            pressStart = raw;
            pressHeld = true;
            dragging = false;
        }

        // A drag between clicks draws freehand; its release point is kept like a click
        if (pressHeld)
        {
            if (!dragging && FlatDistance(raw, pressStart) >= DragStartDistance) dragging = true;
            if (dragging) AddSample(raw);
            if (!Input.GetMouseButton(0))
            {
                if (dragging) AddPoint(raw, true);
                pressHeld = false;
                dragging = false;
            }
        }

        UpdatePreview(raw, true);

        if (KeyBindings.Down(KeyBindings.Action.FinishWallLine)) ConfirmWallLine("wall:finish_key");
    }

    /// <summary>
    /// Discard any in-progress line and ghosts (used when leaving build mode
    /// or switching building type).
    /// </summary>
    public void ResetLineState()
    {
        ClearWallLineGhosts();
        ClearPath();
        stage = Stage.Cursor;
        lineWallCount = 0;
    }

    /// <summary>
    /// Create a simple procedural ghost for the wall cursor (a bare post).
    /// Uses the same transparent material as line ghosts.
    /// </summary>
    public GameObject CreateWallCursorGhost(BuildingData data)
    {
        bool isStone = data.buildingType == BuildingType.StoneWall;
        GameObject ghost = new GameObject("WallCursorGhost");
        MeshFilter mf = ghost.AddComponent<MeshFilter>();
        mf.mesh = WallConnector.GetOrCreateMesh(0, isStone, false);
        MeshRenderer mr = ghost.AddComponent<MeshRenderer>();
        mr.material = CreateWallGhostMaterial();
        return ghost;
    }

    // =============================================
    // Path
    // =============================================

    void ClearPath()
    {
        pathPoints.Clear();
        pathClicked.Clear();
        pressHeld = false;
        dragging = false;
    }

    void AddPoint(Vector3 p, bool clicked)
    {
        pathPoints.Add(p);
        pathClicked.Add(clicked);
    }

    void AddSample(Vector3 p)
    {
        if (pathPoints.Count > 0 && FlatDistance(p, pathPoints[pathPoints.Count - 1]) < SampleSpacing) return;
        AddPoint(p, false);
    }

    /// <summary>
    /// Turn the path (plus the cursor, when <paramref name="withCursor"/>) into cells and lay
    /// the ghost line over them.
    /// </summary>
    void UpdatePreview(Vector3 cursor, bool withCursor)
    {
        polyPoints.Clear();
        polyClicked.Clear();
        loopClosed = false;
        for (int i = 0; i < pathPoints.Count; i++)
        {
            polyPoints.Add(pathPoints[i]);
            polyClicked.Add(pathClicked[i]);
        }
        if (withCursor)
        {
            polyPoints.Add(cursor);
            polyClicked.Add(true);
        }
        CloseLoop();

        lineCells.Clear();
        lineCellSet.Clear();
        lastSquare = KeyBindings.Held(KeyBindings.Action.StraightWallPath);
        if (lastSquare) RasterizeSquare();
        else RasterizeSmooth();

        RefreshGhosts();
    }

    /// <summary>A line that ends near its first point snaps its end onto it, so a drawn ring
    /// closes (the end is the cursor, or a stroke's release point).</summary>
    void CloseLoop()
    {
        int n = polyPoints.Count;
        if (n < 3) return;
        Vector3 first = polyPoints[0];
        if (FlatDistance(polyPoints[n - 1], first) > CloseLoopDistance) return;

        float length = 0f;
        for (int i = 1; i < n; i++) length += FlatDistance(polyPoints[i - 1], polyPoints[i]);
        if (length < CloseLoopMinLength) return;

        polyPoints[n - 1] = first;
        loopClosed = true;
    }

    // Freehand: smooth, then an 8-connected line per straight piece
    void RasterizeSmooth()
    {
        int n = polyPoints.Count;
        if (n == 0) return;

        keep.Clear();
        for (int i = 0; i < n; i++) keep.Add(polyClicked[i] || i == 0 || i == n - 1);

        int start = 0;
        for (int i = 1; i < n; i++)
        {
            if (!keep[i]) continue;
            Simplify(start, i);
            start = i;
        }

        Vector2Int prev = CellOf(polyPoints[0]);
        AddCell(prev);
        for (int i = 1; i < n; i++)
        {
            if (!keep[i]) continue;
            Vector2Int next = CellOf(polyPoints[i]);
            AppendLine(prev, next);
            prev = next;
        }
    }

    /// <summary>Douglas-Peucker over polyPoints[a..b]: keep the sample farthest from the
    /// chord while it is farther than the tolerance, then recurse on both halves.</summary>
    void Simplify(int a, int b)
    {
        if (b <= a + 1) return;

        float maxDistance = 0f;
        int farthest = -1;
        for (int i = a + 1; i < b; i++)
        {
            float d = DistanceToSegment(polyPoints[i], polyPoints[a], polyPoints[b]);
            if (d > maxDistance) { maxDistance = d; farthest = i; }
        }

        if (farthest < 0 || maxDistance <= SimplifyTolerance) return;
        keep[farthest] = true;
        Simplify(a, farthest);
        Simplify(farthest, b);
    }

    // Square: an L between each pair of CLICKED points; freehand samples are ignored
    void RasterizeSquare()
    {
        bool have = false;
        Vector2Int prev = default;
        for (int i = 0; i < polyPoints.Count; i++)
        {
            if (!polyClicked[i]) continue;
            Vector2Int next = CellOf(polyPoints[i]);
            if (!have) { AddCell(next); have = true; }
            else AppendLShape(prev, next);
            prev = next;
        }
    }

    /// <summary>8-connected Bresenham: a slant steps diagonally, never around a corner, which
    /// is what WallGrid needs to link the two cells with one slanted arm.</summary>
    void AppendLine(Vector2Int a, Vector2Int b)
    {
        int x = a.x, z = a.y;
        int dx = Mathf.Abs(b.x - x), dz = -Mathf.Abs(b.y - z);
        int sx = x < b.x ? 1 : -1, sz = z < b.y ? 1 : -1;
        int err = dx + dz;

        while (true)
        {
            AddCell(new Vector2Int(x, z));
            if (x == b.x && z == b.y) break;
            int e2 = 2 * err;
            if (e2 >= dz) { err += dz; x += sx; }
            if (e2 <= dx) { err += dx; z += sz; }
        }
    }

    void AppendLShape(Vector2Int a, Vector2Int b)
    {
        int sx = a.x < b.x ? 1 : -1;
        int sz = a.y < b.y ? 1 : -1;
        if (xFirst)
        {
            for (int x = a.x; x != b.x; x += sx) AddCell(new Vector2Int(x, a.y));
            for (int z = a.y; z != b.y; z += sz) AddCell(new Vector2Int(b.x, z));
        }
        else
        {
            for (int z = a.y; z != b.y; z += sz) AddCell(new Vector2Int(a.x, z));
            for (int x = a.x; x != b.x; x += sx) AddCell(new Vector2Int(x, b.y));
        }
        AddCell(b);
    }

    // A path that crosses itself keeps the cell once
    void AddCell(Vector2Int c)
    {
        if (lineCells.Count >= MaxLineCells) return;
        if (lineCellSet.Add(c)) lineCells.Add(c);
    }

    // =============================================
    // Ghosts
    // =============================================

    /// <summary>
    /// Lay one ghost per cell. The link set holds only the cells a wall would actually go
    /// in, so a blocked cell shows as a lone red post and the line around it shows the gap
    /// it will really have.
    /// </summary>
    void RefreshGhosts()
    {
        BuildingData data = BuildingDatabase.Instance != null
            ? BuildingDatabase.Instance.GetBuildingData(owner.selectedBuildingType)
            : null;
        if (data == null) return;
        bool isStone = data.buildingType == BuildingType.StoneWall;

        validCellSet.Clear();
        cellBlocked.Clear();
        for (int i = 0; i < lineCells.Count; i++)
        {
            bool blocked = CellBlocked(lineCells[i]);
            cellBlocked.Add(blocked);
            if (!blocked) validCellSet.Add(lineCells[i]);
        }

        while (wallLineGhosts.Count < lineCells.Count)
        {
            GameObject ghost = new GameObject("WallGhost");
            ghostFilters.Add(ghost.AddComponent<MeshFilter>());
            MeshRenderer mr = ghost.AddComponent<MeshRenderer>();
            Material mat = CreateWallGhostMaterial();
            mr.material = mat;
            ghostMaterials.Add(mat);
            wallLineGhosts.Add(ghost);
        }

        for (int i = 0; i < lineCells.Count; i++)
        {
            GameObject ghost = wallLineGhosts[i];
            ghost.SetActive(true);

            Vector3 pos = CellCentre(lineCells[i]);
            pos.y = owner.GroundYAt(pos) + 0.02f;
            ghost.transform.SetPositionAndRotation(pos, Quaternion.identity);

            bool blocked = cellBlocked[i];
            int links = blocked || WallGrid.Instance == null
                ? 0
                : WallGrid.Instance.ComputeLinkMask(lineCells[i], validCellSet);
            ghostFilters[i].sharedMesh = WallConnector.GetOrCreateMesh(links, isStone, false);
            ghostMaterials[i].color = blocked ? wallGhostInvalidColor : wallGhostColor;
        }

        for (int i = lineCells.Count; i < wallLineGhosts.Count; i++)
        {
            wallLineGhosts[i].SetActive(false);
        }

        // The build palette reads LineWallCount each frame for the running total.
        lineWallCount = validCellSet.Count;
    }

    // =============================================
    // Confirm / cancel
    // =============================================

    /// <summary>
    /// Confirm the line as last previewed: deduct the total cost once, place a construction
    /// site in every valid cell, and return to cursor mode for the next line.
    /// </summary>
    void ConfirmWallLine(string gesture)
    {
        if (BuildingDatabase.Instance == null) return;

        BuildingData data = BuildingDatabase.Instance.GetBuildingData(owner.selectedBuildingType);
        if (data == null || data.constructionSitePrefab == null) return;

        int count = 0;
        for (int i = 0; i < lineCells.Count; i++)
        {
            if (!cellBlocked[i]) count++;
        }

        if (count == 0)
        {
            CancelWallLine();
            return;
        }

        int totalWood = data.woodCost * count;
        int totalFood = data.foodCost * count;
        int totalStone = data.stoneCost * count;

        // Unaffordable: keep the line so the player can shorten it or wait
        if (!Factions.Player.Resources.CanAfford(totalWood, totalFood, totalStone))
        {
            if (stage == Stage.Pressing) stage = Stage.Waypoints;
            return;
        }

        Factions.Player.Resources.SpendResources(totalWood, totalFood, totalStone);

        // Place construction sites (a wall's shape comes from WallGrid once it stands)
        for (int i = 0; i < lineCells.Count; i++)
        {
            if (cellBlocked[i]) continue;
            Vector3 sitePos = CellCentre(lineCells[i]);
            sitePos.y = owner.GroundYAt(sitePos) + owner.placementHeight;
            GameObject constructionSite = Spawn.Owned(data.constructionSitePrefab, sitePos, Quaternion.identity, Factions.Player);

            constructionSite.layer = LayerMask.NameToLayer("Buildings");

            ConstructionSite siteComponent = constructionSite.GetComponent<ConstructionSite>();
            if (siteComponent != null)
            {
                siteComponent.SetBuildingType(owner.selectedBuildingType);
            }
        }

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayBuildingPlaced();
        }

        DevQuests.Signal(gesture);
        if (lastSquare) DevQuests.Signal("wall:square");
        if (loopClosed) DevQuests.Signal("wall:loop");
        if (HasSlant()) DevQuests.Signal("wall:slanted");

        EndLine();
    }

    // Did the placed line join any two cells on a slant? (dev quest proof only)
    bool HasSlant()
    {
        if (WallGrid.Instance == null) return false;
        foreach (Vector2Int cell in validCellSet)
        {
            if ((WallGrid.Instance.ComputeLinkMask(cell, validCellSet) & WallGrid.DiagonalBits) != 0) return true;
        }
        return false;
    }

    /// <summary>
    /// Cancel wall line drawing, return to cursor mode.
    /// </summary>
    void CancelWallLine()
    {
        EndLine();
    }

    void EndLine()
    {
        ClearWallLineGhosts();
        ClearPath();
        stage = Stage.Cursor;
        owner.currentGhost.SetActive(true);
        lineWallCount = 0;
    }

    /// <summary>
    /// Destroy all wall line ghost objects and clear the lists.
    /// </summary>
    void ClearWallLineGhosts()
    {
        foreach (GameObject ghost in wallLineGhosts)
        {
            if (ghost != null) Object.Destroy(ghost);
        }
        foreach (Material mat in ghostMaterials)
        {
            if (mat != null) Object.Destroy(mat);
        }
        wallLineGhosts.Clear();
        ghostFilters.Clear();
        ghostMaterials.Clear();
        lineCells.Clear();
        lineCellSet.Clear();
        cellBlocked.Clear();
    }

    Material CreateWallGhostMaterial()
    {
        Material mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = wallGhostColor;
        return mat;
    }

    // =============================================
    // Cells
    // =============================================

    Vector2Int CellOf(Vector3 world)
    {
        // GridSnap's floor(x + 0.5) first, so a point on a cell border lands where the
        // cursor ghost does (WorldToGrid alone rounds half to even)
        Vector3 snapped = GridSnap.SnapXZ(world, owner.cellSize);
        return new Vector2Int(Mathf.RoundToInt(snapped.x / owner.cellSize), Mathf.RoundToInt(snapped.z / owner.cellSize));
    }

    Vector3 CellCentre(Vector2Int cell)
    {
        return new Vector3(cell.x * owner.cellSize, 0f, cell.y * owner.cellSize);
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector2 ab = new Vector2(b.x - a.x, b.z - a.z);
        Vector2 ap = new Vector2(p.x - a.x, p.z - a.z);
        float len2 = ab.sqrMagnitude;
        if (len2 < 1e-6f) return ap.magnitude;
        float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / len2);
        return (ap - ab * t).magnitude;
    }

    // A cell can't take a wall if it's occupied, (terrain) the ground there is
    // underwater / a cliff face, or (fog, 2026-09-09) nobody has ever seen it
    bool CellBlocked(Vector2Int cell)
    {
        Vector3 position = CellCentre(cell);
        if (HasWallAt(cell, position)) return true;
        if (TerrainGrid.Instance != null && !TerrainGrid.Instance.IsBuildable(position)) return true;
        if (FogOfWar.Instance != null && !FogOfWar.Instance.IsExplored(position))
        {
            DevQuests.Signal("fog:wall");
            return true;
        }
        return false;
    }

    // Check if a wall or construction site already exists at this exact grid position
    bool HasWallAt(Vector2Int cell, Vector3 position)
    {
        if (WallGrid.Instance != null)
        {
            return WallGrid.Instance.HasWallAt(cell);
        }

        // Fallback if WallGrid not yet initialized
        float threshold = owner.cellSize * 0.4f;

        for (int i = 0; i < Wall.ActiveList.Count; i++)
        {
            Wall wall = Wall.ActiveList[i];
            if (wall == null) continue;
            float dx = Mathf.Abs(position.x - wall.transform.position.x);
            float dz = Mathf.Abs(position.z - wall.transform.position.z);
            if (dx < threshold && dz < threshold)
                return true;
        }

        for (int i = 0; i < ConstructionSite.ActiveList.Count; i++)
        {
            ConstructionSite site = ConstructionSite.ActiveList[i];
            if (site == null) continue;
            float dx = Mathf.Abs(position.x - site.transform.position.x);
            float dz = Mathf.Abs(position.z - site.transform.position.z);
            if (dx < threshold && dz < threshold)
                return true;
        }

        return false;
    }
}
