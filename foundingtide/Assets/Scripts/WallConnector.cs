using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Gives a wall its shape. Reads the eight-bit link mask from WallGrid and builds a post with
/// one arm toward each linked neighbour, so a line of walls joins up on its own, a broken one
/// re-caps itself, and a freehand line reads as one slanted wall instead of a staircase.
/// </summary>
/// <remarks>
/// Replaced the six fixed shapes (isolated, endcap, straight, corner, T, cross) plus rotation
/// on 2026-09-22: diagonal links make 256 masks, and "a post and its arms" covers all of them
/// with one builder and no rotation, so a wall's transform always stays axis-aligned (the
/// carve box and the square colliders are what the NavMesh and the gate trigger see).
/// Meshes are cached statically per (mask, stone) - one mesh per variant for the whole
/// game, not one per wall.
///
/// A gate is not drawn from links (2026-09-28): it is a doorway over its whole span - a jamb
/// at each end, where the neighbouring walls' arms meet it, and a lintel above head height -
/// cached per (span, axis, stone).
///
/// Because this writes the mesh onto the root MeshFilter at runtime, a wall cannot be given
/// hand-authored art by swapping its mesh: anything assigned is overwritten on the next
/// refresh. Walls can only be re-materialed.
/// </remarks>
public class WallConnector : MonoBehaviour
{
    [Header("Debug")]
    public bool showConnectionGizmos = true;
    public int currentLinks;

    private Vector2Int gridPos;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private bool isStoneWall = false;
    private Gate gate;
    private bool initialized = false;

    // Cached procedural meshes (shared across all walls / gates of the same variant)
    private static readonly Dictionary<int, Mesh> meshCache = new Dictionary<int, Mesh>();
    private static readonly Dictionary<int, Mesh> gateMeshCache = new Dictionary<int, Mesh>();

    // Wall dimensions
    private const float WALL_THICKNESS = 0.3f;
    private const float WOODEN_HEIGHT = 1.2f;
    private const float STONE_HEIGHT = 2.0f;
    private const float PILLAR_SIZE = 0.4f;
    private const float Y_OFFSET = 0.02f; // Slight raise to avoid ground z-fighting

    // Gate frame: jambs taller than the wall and a lintel over the heads of the units
    // walking under it (a colonist is ~1.2 m, a raider ~1.4 m).
    private const float GATE_MIN_FRAME = 1.9f;
    private const float GATE_FRAME_RATIO = 1.1f;  // frame height vs the wall it stands in
    private const float GATE_JAMB_DEPTH = 0.25f;  // along the run, so a 1-wide gate still opens 0.5 m
    private const float GATE_LINTEL = 0.18f;

    // A slanted arm starts inside the post's footprint; dropping its top a hair under the
    // post's keeps the two top faces from being coplanar where they overlap (z-fighting
    // under the tilted RTS camera).
    private const float DIAGONAL_TOP_DROP = 0.01f;

    void Start()
    {
        Initialize();
    }

    /// <summary>
    /// Snap, reset and mesh this piece now rather than in its own Start. The wall and the
    /// gate call it first thing in their Start (so the fog and the cutout collect the final
    /// renderers), and a finishing site calls it the frame the wall is spawned (so the
    /// carve box never moves after it appears - see ConstructionSite.Complete).
    /// </summary>
    public void EnsureInitialized()
    {
        Initialize();
    }

    /// <summary>
    /// Where a wall piece in the cell containing <paramref name="pos"/> stands: the cell
    /// centre, at the terrain height there plus a hair. A site finishing into a wall spawns
    /// the wall HERE, so Initialize finds nothing to move.
    /// </summary>
    public static Vector3 SnapToCell(Vector3 pos)
    {
        Vector3 snapped = WallGrid.Instance.GridToWorld(WallGrid.Instance.WorldToGrid(pos), Y_OFFSET);
        if (TerrainGrid.Instance != null)
        {
            snapped.y = TerrainGrid.Instance.SampleHeight(snapped) + Y_OFFSET;
        }
        return snapped;
    }

    private void Initialize()
    {
        if (initialized) return;
        initialized = true;

        Wall wall = GetComponent<Wall>();
        gate = GetComponent<Gate>();
        isStoneWall = (wall != null && wall.isStoneWall) || (gate != null && gate.isStoneGate);

        // Disable ALL child renderers so the original prefab mesh doesn't show
        Renderer[] childRenderers = GetComponentsInChildren<Renderer>();
        foreach (Renderer r in childRenderers)
        {
            if (r.gameObject != gameObject)
            {
                r.enabled = false;
            }
        }

        // Reset localScale — the procedural mesh has correct dimensions baked in. Written
        // only when it differs: a transform change restarts a carve (2026-09-28).
        if (transform.localScale != Vector3.one) transform.localScale = Vector3.one;

        // Set up MeshFilter on root
        meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null)
            meshFilter = gameObject.AddComponent<MeshFilter>();

        // Set up MeshRenderer on root
        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null)
            meshRenderer = gameObject.AddComponent<MeshRenderer>();

        // Ensure a material is assigned. Only a piece built by hand with no material lands
        // here (the gate conversion copies its wall's), and it draws unfogged - say so.
        if (meshRenderer.sharedMaterial == null)
        {
            Debug.LogWarning("WallConnector: " + name + " has no material - drawing an unfogged fallback.");
            meshRenderer.material = new Material(Shader.Find("Sprites/Default"));
            meshRenderer.material.color = isStoneWall ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.55f, 0.35f, 0.15f);
        }

        // Snap to grid (y = terrain height at the snapped cell + lift, 0 on the flat world).
        // A wall spawned by a finishing site already stands here and must not be "moved"
        // onto its own spot: a moved carving obstacle re-carves its NavMesh tile (the wall
        // lag, 2026-09-28).
        Vector3 snapped = SnapToCell(transform.position);
        gridPos = WallGrid.Instance.WorldToGrid(snapped);
        if ((transform.position - snapped).sqrMagnitude > 1e-8f) transform.position = snapped;
        if (transform.rotation != Quaternion.identity) transform.rotation = Quaternion.identity;

        // Bare post (or the whole doorway) until the grid tells us our neighbours
        meshFilter.sharedMesh = gate != null
            ? GetOrCreateGateMesh(gate.SpanLo, gate.SpanHi, gate.AlongX, isStoneWall)
            : GetOrCreateMesh(0, isStoneWall);
    }

    /// <summary>
    /// Called by WallGrid.RefreshTileAndNeighbors to update this wall's mesh.
    /// </summary>
    public void RefreshShape()
    {
        if (!initialized) Initialize();

        if (meshFilter != null)
        {
            if (gate != null)
            {
                // A gate's doorway spans its own cells; its neighbours draw their arms up to it.
                currentLinks = 0;
                meshFilter.sharedMesh = GetOrCreateGateMesh(gate.SpanLo, gate.SpanHi, gate.AlongX, isStoneWall);
            }
            else
            {
                currentLinks = WallGrid.Instance.GetLinkMask(gridPos);
                meshFilter.sharedMesh = GetOrCreateMesh(currentLinks, isStoneWall);
            }
        }

        if (transform.localScale != Vector3.one) transform.localScale = Vector3.one;
        if (transform.rotation != Quaternion.identity) transform.rotation = Quaternion.identity;
    }

    // =============================================
    // Static API (also used by the ghost preview)
    // =============================================

    /// <summary>
    /// The cached wall mesh for a link mask (WallGrid bits: four cardinal, four diagonal).
    /// Always drawn unrotated.
    /// </summary>
    public static Mesh GetOrCreateMesh(int links, bool isStone)
    {
        int key = (links & 0xFF) | (isStone ? 0x100 : 0);

        Mesh mesh;
        if (meshCache.TryGetValue(key, out mesh) && mesh != null)
            return mesh;

        mesh = BuildMesh(links, isStone);
        mesh.name = "Wall_" + links + (isStone ? "_stone" : "_wood");
        meshCache[key] = mesh;
        return mesh;
    }

    /// <summary>
    /// The cached doorway for a gate spanning <paramref name="lo"/>..<paramref name="hi"/>
    /// cells from its anchor (each within -4..4) along X or Z.
    /// </summary>
    public static Mesh GetOrCreateGateMesh(int lo, int hi, bool alongX, bool isStone)
    {
        int key = (lo + 4) | ((hi + 4) << 4) | (alongX ? 0x100 : 0) | (isStone ? 0x200 : 0);

        Mesh mesh;
        if (gateMeshCache.TryGetValue(key, out mesh) && mesh != null)
            return mesh;

        mesh = BuildGateMesh(lo, hi, alongX, isStone);
        mesh.name = "Gate_" + (hi - lo + 1) + (alongX ? "x" : "z") + lo + (isStone ? "_stone" : "_wood");
        gateMeshCache[key] = mesh;
        return mesh;
    }

    // =============================================
    // Procedural Mesh Generation
    // =============================================

    /// <summary>
    /// A post at the cell centre plus one arm per link. A cardinal arm runs from the post's
    /// edge to the cell edge, where the neighbour's arm meets it; a diagonal arm runs to the
    /// shared cell CORNER, where the diagonal neighbour's arm meets it end to end.
    /// </summary>
    private static Mesh BuildMesh(int links, bool isStone)
    {
        float h = isStone ? STONE_HEIGHT : WOODEN_HEIGHT;
        float halfPost = PILLAR_SIZE * 0.5f;

        var b = new BoxBuilder();
        b.AddBox(Vector2.zero, Vector2.up, halfPost, halfPost, 0f, h, false);

        for (int i = 0; i < 4; i++)
        {
            if ((links & WallGrid.NeighborBits[i]) == 0) continue;
            Vector2Int o = WallGrid.NeighborOffsets[i];
            Vector2 dir = new Vector2(o.x, o.y);
            float len = 0.5f - halfPost;
            b.AddBox(dir * (halfPost + len * 0.5f), dir, len * 0.5f, WALL_THICKNESS * 0.5f,
                0f, h, false);
        }

        for (int i = 0; i < 4; i++)
        {
            if ((links & WallGrid.DiagonalLinkBits[i]) == 0) continue;
            Vector2Int o = WallGrid.DiagonalOffsets[i];
            Vector2 dir = new Vector2(o.x, o.y).normalized;
            float len = Mathf.Sqrt(0.5f) - halfPost;   // post edge → cell corner
            b.AddBox(dir * (halfPost + len * 0.5f), dir, len * 0.5f, WALL_THICKNESS * 0.5f,
                0f, h - DIAGONAL_TOP_DROP, false);
        }

        return b.ToMesh();
    }

    /// <summary>
    /// A doorway over cells lo..hi: a jamb standing at each END of the span (the cell edge,
    /// where a neighbouring wall's arm stops) and a lintel across the top. Nothing in
    /// between, so the opening reads as wide as it walks.
    /// </summary>
    private static Mesh BuildGateMesh(int lo, int hi, bool alongX, bool isStone)
    {
        float h = isStone ? STONE_HEIGHT : WOODEN_HEIGHT;
        float top = Mathf.Max(GATE_MIN_FRAME, h * GATE_FRAME_RATIO);
        Vector2 axis = alongX ? Vector2.right : Vector2.up;
        float e0 = lo - 0.5f, e1 = hi + 0.5f;
        float halfJamb = GATE_JAMB_DEPTH * 0.5f;

        var b = new BoxBuilder();
        b.AddBox(axis * (e0 + halfJamb), axis, halfJamb, PILLAR_SIZE * 0.5f, 0f, top, false);
        b.AddBox(axis * (e1 - halfJamb), axis, halfJamb, PILLAR_SIZE * 0.5f, 0f, top, false);
        // The lintel runs between the jambs, a hair under their tops so no faces z-fight;
        // its underside is seen from below the camera's tilt, so it gets a bottom face.
        b.AddBox(axis * ((e0 + e1) * 0.5f), axis, (e1 - e0) * 0.5f - GATE_JAMB_DEPTH, WALL_THICKNESS * 0.5f,
            top - GATE_LINTEL, top - DIAGONAL_TOP_DROP, true);
        return b.ToMesh();
    }

    /// <summary>
    /// Accumulates oriented boxes into one vertex/index list (flat-shaded, four verts per face)
    /// so a whole post-and-arms mesh is one allocation instead of a chain of combines.
    /// </summary>
    private class BoxBuilder
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> norms = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int> tris = new List<int>();

        /// <summary>
        /// A box centred at <paramref name="c"/> (XZ), its long axis along the unit
        /// <paramref name="along"/>. No bottom face unless asked (a gate lintel's underside
        /// is seen through the doorway).
        /// </summary>
        public void AddBox(Vector2 c, Vector2 along, float halfLen, float halfWidth,
            float yMin, float yMax, bool bottomFace)
        {
            Vector3 a = new Vector3(along.x, 0f, along.y) * halfLen;
            Vector3 w = new Vector3(along.y, 0f, -along.x) * halfWidth;
            Vector3 o = new Vector3(c.x, 0f, c.y);
            Vector3 lo = Vector3.up * yMin, hi = Vector3.up * yMax;

            // The four footprint corners; Quad winds each face from its outward normal,
            // so their order around the box does not matter.
            Vector3 p0 = o - a - w, p1 = o + a - w, p2 = o + a + w, p3 = o - a + w;

            Vector3 na = a.normalized, nw = w.normalized;
            Quad(p1 + lo, p2 + lo, p2 + hi, p1 + hi, na);    // far end
            Quad(p3 + lo, p0 + lo, p0 + hi, p3 + hi, -na);   // near end
            Quad(p2 + lo, p3 + lo, p3 + hi, p2 + hi, nw);    // side
            Quad(p0 + lo, p1 + lo, p1 + hi, p0 + hi, -nw);   // other side
            Quad(p0 + hi, p1 + hi, p2 + hi, p3 + hi, Vector3.up);
            if (bottomFace) Quad(p0 + lo, p1 + lo, p2 + lo, p3 + lo, Vector3.down);
        }

        void Quad(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 n)
        {
            // Unity's front face: cross(v1 - v0, v2 - v0) points out of the face.
            if (Vector3.Dot(Vector3.Cross(v1 - v0, v2 - v0), n) < 0f)
            {
                Vector3 t = v1; v1 = v3; v3 = t;
            }

            int b = verts.Count;
            verts.Add(v0); verts.Add(v1); verts.Add(v2); verts.Add(v3);
            norms.Add(n); norms.Add(n); norms.Add(n); norms.Add(n);
            uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0));
            uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(0, 1));
            tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
        }

        public Mesh ToMesh()
        {
            Mesh mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    // =============================================
    // Gizmos
    // =============================================

    void OnDrawGizmos()
    {
        if (!showConnectionGizmos) return;
        if (WallGrid.Instance == null) return;

        Vector3 center = transform.position + Vector3.up * 1f;
        int mask = WallGrid.Instance.GetLinkMask(WallGrid.Instance.WorldToGrid(transform.position));

        Gizmos.color = Color.green;
        for (int i = 0; i < 4; i++)
        {
            Vector2Int o = WallGrid.NeighborOffsets[i];
            if ((mask & WallGrid.NeighborBits[i]) != 0)
                Gizmos.DrawLine(center, center + new Vector3(o.x, 0f, o.y) * 0.5f);
            Vector2Int d = WallGrid.DiagonalOffsets[i];
            if ((mask & WallGrid.DiagonalLinkBits[i]) != 0)
                Gizmos.DrawLine(center, center + new Vector3(d.x, 0f, d.y) * 0.5f);
        }
    }
}
