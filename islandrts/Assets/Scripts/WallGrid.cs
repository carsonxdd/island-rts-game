using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// The authority on which grid cells hold a wall, gate or wall construction site. One
/// dictionary keyed by cell, so any "is there a wall here" question is O(1).
/// </summary>
/// <remarks>
/// This is what makes gate conversion reliable: the G key asks the grid what is in the
/// hovered cell rather than raycasting at a procedurally generated mesh. It is also what
/// lets a wall know its neighbours, since a wall's visible shape is chosen from the four
/// cells around it - which is why registering or unregistering a cell also refreshes the
/// shape of that cell and its four neighbours.
/// Auto-creates itself if the scene has no WallGrid.
/// </remarks>
public class WallGrid : MonoBehaviour
{
    private static WallGrid _instance;
    public static WallGrid Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindAnyObjectByType<WallGrid>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("WallGrid");
                    _instance = go.AddComponent<WallGrid>();
                }
            }
            return _instance;
        }
    }

    // Neighbour directions as bit flags. A cell's four-bit mask is what WallConnector
    // turns into a shape (isolated, endcap, straight, corner, T, cross) and a rotation.
    public const int NORTH = 1;  // +Z
    public const int EAST  = 2;  // +X
    public const int SOUTH = 4;  // -Z
    public const int WEST  = 8;  // -X

    // The four cardinal directions, and the bit each one contributes to a neighbour mask.
    // The two arrays are parallel and must stay in the same order. Shared (rather than
    // rebuilt per call) because the ghost preview walks them for every cell of a wall line
    // on every frame of a drag. Treat as read-only.
    public static readonly Vector2Int[] NeighborOffsets = new Vector2Int[]
    {
        new Vector2Int(0, 1),   // North
        new Vector2Int(1, 0),   // East
        new Vector2Int(0, -1),  // South
        new Vector2Int(-1, 0)   // West
    };

    public static readonly int[] NeighborBits = new int[] { NORTH, EAST, SOUTH, WEST };

    private Dictionary<Vector2Int, MonoBehaviour> grid = new Dictionary<Vector2Int, MonoBehaviour>();

    private float cellSize = 1f;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
    }

    void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    /// <summary>
    /// Claims a cell for a wall, gate or construction site, and reshapes the neighbourhood
    /// so the new piece connects to what is around it.
    /// </summary>
    public void Register(Vector2Int pos, MonoBehaviour occupant)
    {
        grid[pos] = occupant;
        RefreshTileAndNeighbors(pos);
    }

    /// <summary>Frees a cell and reshapes its neighbours, so a broken wall leaves endcaps
    /// rather than pieces still pointing at nothing.</summary>
    public void Unregister(Vector2Int pos)
    {
        grid.Remove(pos);
        RefreshTileAndNeighbors(pos);
    }

    public bool HasWallAt(Vector2Int pos)
    {
        return grid.ContainsKey(pos);
    }

    public MonoBehaviour GetWallAt(Vector2Int pos)
    {
        MonoBehaviour occupant;
        grid.TryGetValue(pos, out occupant);
        return occupant;
    }

    /// <summary>
    /// Returns a bitmask of which cardinal neighbors have walls.
    /// N=1, E=2, S=4, W=8
    /// </summary>
    public int GetNeighborMask(Vector2Int pos)
    {
        int mask = 0;
        for (int i = 0; i < 4; i++)
        {
            if (grid.ContainsKey(pos + NeighborOffsets[i]))
                mask |= NeighborBits[i];
        }
        return mask;
    }

    /// <summary>World position to the cell containing it. Height is ignored - the grid is flat.</summary>
    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
        int x = Mathf.RoundToInt(worldPos.x / cellSize);
        int z = Mathf.RoundToInt(worldPos.z / cellSize);
        return new Vector2Int(x, z);
    }

    public Vector3 GridToWorld(Vector2Int gridPos, float y = 0f)
    {
        return new Vector3(gridPos.x * cellSize, y, gridPos.y * cellSize);
    }

    /// <summary>
    /// Refresh the visual shape of the tile at pos and its 4 cardinal neighbors.
    /// </summary>
    public void RefreshTileAndNeighbors(Vector2Int pos)
    {
        RefreshTile(pos);
        for (int i = 0; i < 4; i++)
        {
            RefreshTile(pos + NeighborOffsets[i]);
        }
    }

    /// <summary>
    /// Re-selects one cell's wall shape. Doubles as lazy cleanup: a cell whose occupant has
    /// been destroyed is dropped from the dictionary here.
    /// </summary>
    private void RefreshTile(Vector2Int pos)
    {
        MonoBehaviour occupant;
        if (!grid.TryGetValue(pos, out occupant)) return;
        if (occupant == null)
        {
            grid.Remove(pos);
            return;
        }

        WallConnector connector = occupant.GetComponent<WallConnector>();
        if (connector != null)
        {
            connector.RefreshShape();
        }
    }
}
