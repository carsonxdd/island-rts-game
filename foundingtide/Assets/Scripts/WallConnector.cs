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
/// Meshes are cached statically per (mask, stone, gate) - one mesh per variant for the whole
/// game, not one per wall.
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
    private bool isGate = false;
    private bool initialized = false;

    // Cached procedural meshes (shared across all walls of the same variant)
    private static readonly Dictionary<int, Mesh> meshCache = new Dictionary<int, Mesh>();

    // Wall dimensions
    private const float WALL_THICKNESS = 0.3f;
    private const float WOODEN_HEIGHT = 1.2f;
    private const float STONE_HEIGHT = 2.0f;
    private const float PILLAR_SIZE = 0.4f;
    private const float Y_OFFSET = 0.02f; // Slight raise to avoid ground z-fighting
    private const float GATE_HEIGHT_RATIO = 0.5f; // Gates are half the height of walls
    private const float GATE_ARCH_RATIO = 0.6f;   // A gate arm is only its top 40%: the arch below

    // A slanted arm starts inside the post's footprint; dropping its top a hair under the
    // post's keeps the two top faces from being coplanar where they overlap (z-fighting
    // under the tilted RTS camera).
    private const float DIAGONAL_TOP_DROP = 0.01f;

    void Start()
    {
        Initialize();
    }

    private void Initialize()
    {
        if (initialized) return;
        initialized = true;

        Wall wall = GetComponent<Wall>();
        Gate gate = GetComponent<Gate>();
        isStoneWall = (wall != null && wall.isStoneWall) || (gate != null && gate.isStoneGate);
        isGate = gate != null;

        // Disable ALL child renderers so the original prefab mesh doesn't show
        Renderer[] childRenderers = GetComponentsInChildren<Renderer>();
        foreach (Renderer r in childRenderers)
        {
            if (r.gameObject != gameObject)
            {
                r.enabled = false;
            }
        }

        // Reset localScale — the procedural mesh has correct dimensions baked in
        transform.localScale = Vector3.one;

        // Set up MeshFilter on root
        meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null)
            meshFilter = gameObject.AddComponent<MeshFilter>();

        // Set up MeshRenderer on root
        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null)
            meshRenderer = gameObject.AddComponent<MeshRenderer>();

        // Ensure a material is assigned
        if (meshRenderer.sharedMaterial == null)
        {
            // Use Sprites/Default which is always available; Standard may be stripped
            meshRenderer.material = new Material(Shader.Find("Sprites/Default"));
            meshRenderer.material.color = isStoneWall ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.55f, 0.35f, 0.15f);
        }

        // Snap to grid (y = terrain height at the snapped cell + lift, 0 on the flat world)
        gridPos = WallGrid.Instance.WorldToGrid(transform.position);
        Vector3 snapped = WallGrid.Instance.GridToWorld(gridPos, Y_OFFSET);
        if (TerrainGrid.Instance != null)
        {
            snapped.y = TerrainGrid.Instance.SampleHeight(snapped) + Y_OFFSET;
        }
        transform.position = snapped;
        transform.rotation = Quaternion.identity;

        // Bare post until the grid tells us our neighbours
        meshFilter.mesh = GetOrCreateMesh(0, isStoneWall, isGate);

        // Gates use a distinct color tint
        if (isGate && meshRenderer != null && meshRenderer.sharedMaterial != null)
        {
            meshRenderer.material.color = isStoneWall ? new Color(0.5f, 0.6f, 0.5f) : new Color(0.45f, 0.35f, 0.2f);
        }
    }

    /// <summary>
    /// Called by WallGrid.RefreshTileAndNeighbors to update this wall's mesh.
    /// </summary>
    public void RefreshShape()
    {
        if (!initialized) Initialize();

        currentLinks = WallGrid.Instance.GetLinkMask(gridPos);

        if (meshFilter != null)
        {
            meshFilter.mesh = GetOrCreateMesh(currentLinks, isStoneWall, isGate);
        }

        transform.localScale = Vector3.one;
        transform.rotation = Quaternion.identity;
    }

    // =============================================
    // Static API (also used by the ghost preview)
    // =============================================

    /// <summary>
    /// The cached mesh for a link mask (WallGrid bits: four cardinal, four diagonal). A gate
    /// ignores diagonal bits - WallGrid never gives it any. Always drawn unrotated.
    /// </summary>
    public static Mesh GetOrCreateMesh(int links, bool isStone, bool isGate)
    {
        if (isGate) links &= ~WallGrid.DiagonalBits;
        int key = (links & 0xFF) | (isStone ? 0x100 : 0) | (isGate ? 0x200 : 0);

        Mesh mesh;
        if (meshCache.TryGetValue(key, out mesh) && mesh != null)
            return mesh;

        mesh = BuildMesh(links, isStone, isGate);
        mesh.name = "Wall_" + links + (isStone ? "_stone" : "_wood") + (isGate ? "_gate" : "");
        meshCache[key] = mesh;
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
    private static Mesh BuildMesh(int links, bool isStone, bool isGate)
    {
        float h = isStone ? STONE_HEIGHT : WOODEN_HEIGHT;
        float postTop = isGate ? h * GATE_HEIGHT_RATIO : h;
        float armBottom = isGate ? postTop * GATE_ARCH_RATIO : 0f;
        float halfPost = PILLAR_SIZE * 0.5f;

        var b = new BoxBuilder();
        b.AddBox(Vector2.zero, Vector2.up, halfPost, halfPost, 0f, postTop, false);

        for (int i = 0; i < 4; i++)
        {
            if ((links & WallGrid.NeighborBits[i]) == 0) continue;
            Vector2Int o = WallGrid.NeighborOffsets[i];
            Vector2 dir = new Vector2(o.x, o.y);
            float len = 0.5f - halfPost;
            b.AddBox(dir * (halfPost + len * 0.5f), dir, len * 0.5f, WALL_THICKNESS * 0.5f,
                armBottom, postTop, isGate);
        }

        if (!isGate)
        {
            for (int i = 0; i < 4; i++)
            {
                if ((links & WallGrid.DiagonalLinkBits[i]) == 0) continue;
                Vector2Int o = WallGrid.DiagonalOffsets[i];
                Vector2 dir = new Vector2(o.x, o.y).normalized;
                float len = Mathf.Sqrt(0.5f) - halfPost;   // post edge → cell corner
                b.AddBox(dir * (halfPost + len * 0.5f), dir, len * 0.5f, WALL_THICKNESS * 0.5f,
                    0f, postTop - DIAGONAL_TOP_DROP, false);
            }
        }

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
        /// <paramref name="along"/>. No bottom face unless asked (a gate arm's underside is
        /// seen through the arch).
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
