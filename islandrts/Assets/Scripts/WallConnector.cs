using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Shape-driven wall visuals. Uses WallGrid neighbor mask to select
/// the correct procedural mesh (isolated/endcap/straight/corner/T/cross)
/// and rotation.
/// </summary>
public class WallConnector : MonoBehaviour
{
    public enum WallShape
    {
        Isolated,   // 0 neighbors
        Endcap,     // 1 neighbor
        Straight,   // 2 opposite neighbors
        Corner,     // 2 adjacent neighbors
        TJunction,  // 3 neighbors
        Cross       // 4 neighbors
    }

    [Header("Debug")]
    public bool showConnectionGizmos = true;
    public WallShape currentShape = WallShape.Isolated;

    private Vector2Int gridPos;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private bool isStoneWall = false;
    private bool initialized = false;

    // Cached procedural meshes (shared across all walls of same type)
    private static Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();

    // Wall dimensions
    private const float WALL_THICKNESS = 0.3f;
    private const float WOODEN_HEIGHT = 1.2f;
    private const float STONE_HEIGHT = 2.0f;
    private const float PILLAR_SIZE = 0.4f;
    private const float Y_OFFSET = 0.02f; // Slight raise to avoid ground z-fighting

    private float wallHeight;

    void Start()
    {
        Initialize();
    }

    private void Initialize()
    {
        if (initialized) return;
        initialized = true;

        Wall wall = GetComponent<Wall>();
        isStoneWall = wall != null && wall.isStoneWall;
        wallHeight = isStoneWall ? STONE_HEIGHT : WOODEN_HEIGHT;

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

        // Set Y slightly above 0 to avoid ground z-fighting
        transform.position = new Vector3(transform.position.x, Y_OFFSET, transform.position.z);

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

        // Snap to grid
        gridPos = WallGrid.Instance.WorldToGrid(transform.position);
        Vector3 snapped = WallGrid.Instance.GridToWorld(gridPos, Y_OFFSET);
        transform.position = snapped;

        // Set initial isolated shape
        meshFilter.mesh = GetOrCreateMesh(WallShape.Isolated, isStoneWall);
    }

    /// <summary>
    /// Called by WallGrid.RefreshTileAndNeighbors to update this wall's mesh and rotation.
    /// </summary>
    public void RefreshShape()
    {
        if (!initialized) Initialize();

        int mask = WallGrid.Instance.GetNeighborMask(gridPos);
        WallShape shape;
        float yRotation;
        GetShapeAndRotation(mask, out shape, out yRotation);
        currentShape = shape;

        if (meshFilter != null)
        {
            meshFilter.mesh = GetOrCreateMesh(shape, isStoneWall);
        }

        transform.localScale = Vector3.one;
        transform.rotation = Quaternion.Euler(0f, yRotation, 0f);
    }

    // =============================================
    // Static API for ghost previews
    // =============================================

    /// <summary>
    /// Compute the shape and Y-rotation for a given neighbor bitmask.
    /// Can be called by BuildPlacement for ghost previews.
    /// </summary>
    public static void GetShapeAndRotation(int mask, out WallShape shape, out float yRotation)
    {
        int neighborCount = CountBitsStatic(mask);
        yRotation = 0f;

        switch (neighborCount)
        {
            case 0:
                shape = WallShape.Isolated;
                break;
            case 1:
                shape = WallShape.Endcap;
                yRotation = GetEndcapRotationStatic(mask);
                break;
            case 2:
                if (IsOppositeStatic(mask))
                {
                    shape = WallShape.Straight;
                    yRotation = GetStraightRotationStatic(mask);
                }
                else
                {
                    shape = WallShape.Corner;
                    yRotation = GetCornerRotationStatic(mask);
                }
                break;
            case 3:
                shape = WallShape.TJunction;
                yRotation = GetTJunctionRotationStatic(mask);
                break;
            case 4:
                shape = WallShape.Cross;
                break;
            default:
                shape = WallShape.Isolated;
                break;
        }
    }

    /// <summary>
    /// Get or create a cached procedural mesh for a given shape. Static so
    /// BuildPlacement can call it for ghost previews.
    /// </summary>
    public static Mesh GetOrCreateMesh(WallShape shape, bool isStone)
    {
        string key = shape.ToString() + (isStone ? "_stone" : "_wood");
        Mesh mesh;
        if (meshCache.TryGetValue(key, out mesh) && mesh != null)
            return mesh;

        float h = isStone ? STONE_HEIGHT : WOODEN_HEIGHT;
        float t = WALL_THICKNESS;

        switch (shape)
        {
            case WallShape.Isolated:
                mesh = CreateBox(PILLAR_SIZE, h, PILLAR_SIZE);
                break;
            case WallShape.Endcap:
                mesh = CreateEndcapMesh(t, h);
                break;
            case WallShape.Straight:
                mesh = CreateStraightMesh(t, h);
                break;
            case WallShape.Corner:
                mesh = CreateCornerMesh(t, h);
                break;
            case WallShape.TJunction:
                mesh = CreateTJunctionMesh(t, h);
                break;
            case WallShape.Cross:
                mesh = CreateCrossMesh(t, h);
                break;
            default:
                mesh = CreateBox(PILLAR_SIZE, h, PILLAR_SIZE);
                break;
        }

        mesh.name = key;
        meshCache[key] = mesh;
        return mesh;
    }

    /// <summary>
    /// Compute neighbor mask for a grid position using both the WallGrid
    /// and a set of additional ghost positions. Used for ghost previews.
    /// </summary>
    public static int GetPreviewNeighborMask(Vector2Int pos, HashSet<Vector2Int> ghostPositions)
    {
        int mask = 0;
        Vector2Int[] offsets = { new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0) };
        int[] bits = { WallGrid.NORTH, WallGrid.EAST, WallGrid.SOUTH, WallGrid.WEST };

        for (int i = 0; i < 4; i++)
        {
            Vector2Int neighbor = pos + offsets[i];
            if (ghostPositions.Contains(neighbor))
            {
                mask |= bits[i];
            }
            else if (WallGrid.Instance != null && WallGrid.Instance.HasWallAt(neighbor))
            {
                mask |= bits[i];
            }
        }
        return mask;
    }

    // =============================================
    // Static Rotation Helpers
    // =============================================

    private static float GetEndcapRotationStatic(int mask)
    {
        if ((mask & WallGrid.NORTH) != 0) return 0f;
        if ((mask & WallGrid.EAST) != 0)  return 90f;
        if ((mask & WallGrid.SOUTH) != 0) return 180f;
        if ((mask & WallGrid.WEST) != 0)  return 270f;
        return 0f;
    }

    private static float GetStraightRotationStatic(int mask)
    {
        if ((mask & WallGrid.NORTH) != 0 && (mask & WallGrid.SOUTH) != 0) return 0f;
        return 90f;
    }

    private static float GetCornerRotationStatic(int mask)
    {
        if ((mask & WallGrid.NORTH) != 0 && (mask & WallGrid.EAST) != 0)  return 0f;
        if ((mask & WallGrid.EAST) != 0  && (mask & WallGrid.SOUTH) != 0) return 90f;
        if ((mask & WallGrid.SOUTH) != 0 && (mask & WallGrid.WEST) != 0)  return 180f;
        if ((mask & WallGrid.WEST) != 0  && (mask & WallGrid.NORTH) != 0) return 270f;
        return 0f;
    }

    private static float GetTJunctionRotationStatic(int mask)
    {
        if ((mask & WallGrid.SOUTH) == 0) return 0f;
        if ((mask & WallGrid.WEST) == 0)  return 90f;
        if ((mask & WallGrid.NORTH) == 0) return 180f;
        if ((mask & WallGrid.EAST) == 0)  return 270f;
        return 0f;
    }

    private static bool IsOppositeStatic(int mask)
    {
        return mask == (WallGrid.NORTH | WallGrid.SOUTH) ||
               mask == (WallGrid.EAST | WallGrid.WEST);
    }

    private static int CountBitsStatic(int n)
    {
        int count = 0;
        while (n != 0) { count += n & 1; n >>= 1; }
        return count;
    }

    // =============================================
    // Procedural Mesh Generation
    // =============================================

    /// <summary>
    /// Create a 5-face box (no bottom face) with bottom at y=0, centered on XZ.
    /// </summary>
    private static Mesh CreateBox(float sizeX, float sizeY, float sizeZ)
    {
        return CreateBoxAt(sizeX, sizeY, sizeZ, 0f, 0f);
    }

    /// <summary>
    /// Create a 5-face box (no bottom) at given XZ offset, bottom at y=0.
    /// </summary>
    private static Mesh CreateBoxAt(float sizeX, float sizeY, float sizeZ, float cx, float cz)
    {
        Mesh mesh = new Mesh();

        float hx = sizeX * 0.5f;
        float hz = sizeZ * 0.5f;
        float top = sizeY;

        // 5 faces x 4 verts = 20 vertices
        Vector3[] vertices = new Vector3[20];
        Vector3[] normals = new Vector3[20];
        Vector2[] uvs = new Vector2[20];

        // Front face (+Z)
        vertices[0]  = new Vector3(cx - hx, 0,   cz + hz);
        vertices[1]  = new Vector3(cx + hx, 0,   cz + hz);
        vertices[2]  = new Vector3(cx + hx, top, cz + hz);
        vertices[3]  = new Vector3(cx - hx, top, cz + hz);
        // Back face (-Z)
        vertices[4]  = new Vector3(cx + hx, 0,   cz - hz);
        vertices[5]  = new Vector3(cx - hx, 0,   cz - hz);
        vertices[6]  = new Vector3(cx - hx, top, cz - hz);
        vertices[7]  = new Vector3(cx + hx, top, cz - hz);
        // Top face (+Y)
        vertices[8]  = new Vector3(cx - hx, top, cz + hz);
        vertices[9]  = new Vector3(cx + hx, top, cz + hz);
        vertices[10] = new Vector3(cx + hx, top, cz - hz);
        vertices[11] = new Vector3(cx - hx, top, cz - hz);
        // Right face (+X)
        vertices[12] = new Vector3(cx + hx, 0,   cz + hz);
        vertices[13] = new Vector3(cx + hx, 0,   cz - hz);
        vertices[14] = new Vector3(cx + hx, top, cz - hz);
        vertices[15] = new Vector3(cx + hx, top, cz + hz);
        // Left face (-X)
        vertices[16] = new Vector3(cx - hx, 0,   cz - hz);
        vertices[17] = new Vector3(cx - hx, 0,   cz + hz);
        vertices[18] = new Vector3(cx - hx, top, cz + hz);
        vertices[19] = new Vector3(cx - hx, top, cz - hz);

        for (int i = 0;  i < 4;  i++) normals[i]  = Vector3.forward;
        for (int i = 4;  i < 8;  i++) normals[i]  = Vector3.back;
        for (int i = 8;  i < 12; i++) normals[i]  = Vector3.up;
        for (int i = 12; i < 16; i++) normals[i]  = Vector3.right;
        for (int i = 16; i < 20; i++) normals[i]  = Vector3.left;

        for (int i = 0; i < 5; i++)
        {
            int b = i * 4;
            uvs[b]     = new Vector2(0, 0);
            uvs[b + 1] = new Vector2(1, 0);
            uvs[b + 2] = new Vector2(1, 1);
            uvs[b + 3] = new Vector2(0, 1);
        }

        // Clockwise winding for Unity front faces
        int[] triangles = new int[30]; // 5 faces x 6 indices
        for (int i = 0; i < 5; i++)
        {
            int b = i * 4;
            int t = i * 6;
            triangles[t]     = b;
            triangles[t + 1] = b + 1;
            triangles[t + 2] = b + 2;
            triangles[t + 3] = b;
            triangles[t + 4] = b + 2;
            triangles[t + 5] = b + 3;
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();

        return mesh;
    }

    /// <summary>
    /// Endcap: pillar at center + half-bar toward +Z.
    /// </summary>
    private static Mesh CreateEndcapMesh(float thickness, float height)
    {
        float halfT = thickness * 0.5f;
        // Pillar at center
        Mesh pillar = CreateBoxAt(PILLAR_SIZE, height, PILLAR_SIZE, 0f, 0f);
        // Bar from pillar edge to +0.5 Z
        float barLen = 0.5f - PILLAR_SIZE * 0.5f;
        float barCZ = PILLAR_SIZE * 0.5f + barLen * 0.5f;
        Mesh bar = CreateBoxAt(thickness, height, barLen, 0f, barCZ);
        return CombineMeshes(pillar, bar);
    }

    /// <summary>
    /// Corner mesh: pillar + arm toward +Z + arm toward +X.
    /// No overlapping geometry.
    /// </summary>
    private static Mesh CreateCornerMesh(float thickness, float height)
    {
        // Center pillar
        Mesh pillar = CreateBoxAt(PILLAR_SIZE, height, PILLAR_SIZE, 0f, 0f);
        // Arm toward +Z (North)
        float armLen = 0.5f - PILLAR_SIZE * 0.5f;
        float armCZ = PILLAR_SIZE * 0.5f + armLen * 0.5f;
        Mesh armN = CreateBoxAt(thickness, height, armLen, 0f, armCZ);
        // Arm toward +X (East)
        float armCX = PILLAR_SIZE * 0.5f + armLen * 0.5f;
        Mesh armE = CreateBoxAt(armLen, height, thickness, armCX, 0f);

        return CombineMeshes(CombineMeshes(pillar, armN), armE);
    }

    /// <summary>
    /// T-junction: pillar + arms toward +Z, +X, -X.
    /// Default orientation: missing South (N+E+W).
    /// </summary>
    private static Mesh CreateTJunctionMesh(float thickness, float height)
    {
        // Center pillar
        Mesh pillar = CreateBoxAt(PILLAR_SIZE, height, PILLAR_SIZE, 0f, 0f);
        float armLen = 0.5f - PILLAR_SIZE * 0.5f;
        float offset = PILLAR_SIZE * 0.5f + armLen * 0.5f;
        // Arm +Z (North)
        Mesh armN = CreateBoxAt(thickness, height, armLen, 0f, offset);
        // Arm +X (East)
        Mesh armE = CreateBoxAt(armLen, height, thickness, offset, 0f);
        // Arm -X (West)
        Mesh armW = CreateBoxAt(armLen, height, thickness, -offset, 0f);

        return CombineMeshes(CombineMeshes(CombineMeshes(pillar, armN), armE), armW);
    }

    /// <summary>
    /// Straight: pillar + arms toward +Z and -Z.
    /// Default orientation: N-S.
    /// </summary>
    private static Mesh CreateStraightMesh(float thickness, float height)
    {
        Mesh pillar = CreateBoxAt(PILLAR_SIZE, height, PILLAR_SIZE, 0f, 0f);
        float armLen = 0.5f - PILLAR_SIZE * 0.5f;
        float offset = PILLAR_SIZE * 0.5f + armLen * 0.5f;
        Mesh armN = CreateBoxAt(thickness, height, armLen, 0f, offset);
        Mesh armS = CreateBoxAt(thickness, height, armLen, 0f, -offset);
        return CombineMeshes(CombineMeshes(pillar, armN), armS);
    }

    /// <summary>
    /// Cross: pillar + arms in all 4 directions.
    /// </summary>
    private static Mesh CreateCrossMesh(float thickness, float height)
    {
        Mesh pillar = CreateBoxAt(PILLAR_SIZE, height, PILLAR_SIZE, 0f, 0f);
        float armLen = 0.5f - PILLAR_SIZE * 0.5f;
        float offset = PILLAR_SIZE * 0.5f + armLen * 0.5f;
        Mesh armN = CreateBoxAt(thickness, height, armLen, 0f, offset);
        Mesh armS = CreateBoxAt(thickness, height, armLen, 0f, -offset);
        Mesh armE = CreateBoxAt(armLen, height, thickness, offset, 0f);
        Mesh armW = CreateBoxAt(armLen, height, thickness, -offset, 0f);

        Mesh ns = CombineMeshes(CombineMeshes(pillar, armN), armS);
        return CombineMeshes(CombineMeshes(ns, armE), armW);
    }

    /// <summary>
    /// Combine two meshes into one.
    /// </summary>
    private static Mesh CombineMeshes(Mesh a, Mesh b)
    {
        int vertCountA = a.vertexCount;
        int vertCountB = b.vertexCount;

        Vector3[] verts = new Vector3[vertCountA + vertCountB];
        Vector3[] norms = new Vector3[vertCountA + vertCountB];
        Vector2[] uvArr = new Vector2[vertCountA + vertCountB];

        System.Array.Copy(a.vertices, 0, verts, 0, vertCountA);
        System.Array.Copy(b.vertices, 0, verts, vertCountA, vertCountB);
        System.Array.Copy(a.normals, 0, norms, 0, vertCountA);
        System.Array.Copy(b.normals, 0, norms, vertCountA, vertCountB);
        System.Array.Copy(a.uv, 0, uvArr, 0, vertCountA);
        System.Array.Copy(b.uv, 0, uvArr, vertCountA, vertCountB);

        int[] trisA = a.triangles;
        int[] trisB = b.triangles;
        int[] tris = new int[trisA.Length + trisB.Length];
        System.Array.Copy(trisA, 0, tris, 0, trisA.Length);
        for (int i = 0; i < trisB.Length; i++)
        {
            tris[trisA.Length + i] = trisB[i] + vertCountA;
        }

        Mesh mesh = new Mesh();
        mesh.vertices = verts;
        mesh.normals = norms;
        mesh.uv = uvArr;
        mesh.triangles = tris;
        mesh.RecalculateBounds();

        return mesh;
    }

    // =============================================
    // Gizmos
    // =============================================

    void OnDrawGizmos()
    {
        if (!showConnectionGizmos) return;
        if (WallGrid.Instance == null) return;

        Vector3 center = transform.position + Vector3.up * 1f;
        int mask = WallGrid.Instance.GetNeighborMask(WallGrid.Instance.WorldToGrid(transform.position));

        Gizmos.color = Color.green;
        if ((mask & WallGrid.NORTH) != 0) Gizmos.DrawLine(center, center + Vector3.forward * 0.5f);
        if ((mask & WallGrid.SOUTH) != 0) Gizmos.DrawLine(center, center + Vector3.back * 0.5f);
        if ((mask & WallGrid.EAST) != 0)  Gizmos.DrawLine(center, center + Vector3.right * 0.5f);
        if ((mask & WallGrid.WEST) != 0)  Gizmos.DrawLine(center, center + Vector3.left * 0.5f);
    }
}
