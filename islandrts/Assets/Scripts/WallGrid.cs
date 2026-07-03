using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Central authority for wall occupancy and neighbor lookups.
/// Replaces FindObjectsByType scanning with O(1) dictionary lookups.
/// Auto-creates itself if not in the scene.
/// </summary>
public class WallGrid : MonoBehaviour
{
    private static WallGrid _instance;
    public static WallGrid Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<WallGrid>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("WallGrid");
                    _instance = go.AddComponent<WallGrid>();
                }
            }
            return _instance;
        }
    }

    // Bitmask constants for neighbor directions
    public const int NORTH = 1;  // +Z
    public const int EAST  = 2;  // +X
    public const int SOUTH = 4;  // -Z
    public const int WEST  = 8;  // -X

    private static readonly Vector2Int[] NeighborOffsets = new Vector2Int[]
    {
        new Vector2Int(0, 1),   // North
        new Vector2Int(1, 0),   // East
        new Vector2Int(0, -1),  // South
        new Vector2Int(-1, 0)   // West
    };

    private static readonly int[] NeighborBits = new int[] { NORTH, EAST, SOUTH, WEST };

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

    public void Register(Vector2Int pos, MonoBehaviour occupant)
    {
        grid[pos] = occupant;
        RefreshTileAndNeighbors(pos);
    }

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
