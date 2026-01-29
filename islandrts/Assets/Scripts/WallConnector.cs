using UnityEngine;

public class WallConnector : MonoBehaviour
{
    [Header("Connection State")]
    public bool connectedNorth = false;
    public bool connectedSouth = false;
    public bool connectedEast = false;
    public bool connectedWest = false;

    [Header("Visual Settings")]
    [Tooltip("The mesh renderer to update based on connections")]
    public Renderer wallRenderer;

    [Header("Debug")]
    public bool showConnectionGizmos = true;

    private float gridCellSize = 1f;  // Match BuildPlacement grid size
    private Vector3 gridPosition;

    void Start()
    {
        // Get the grid-snapped position
        gridPosition = GridSnap.SnapXZ(transform.position, gridCellSize);
        transform.position = new Vector3(gridPosition.x, transform.position.y, gridPosition.z);

        // Find wall renderer if not assigned
        if (wallRenderer == null)
        {
            wallRenderer = GetComponent<Renderer>();
        }

        // Wait one frame for all walls to spawn, then update connections
        Invoke(nameof(UpdateConnections), 0.1f);
    }

    /// <summary>
    /// Scan adjacent grid positions for other walls and update connections
    /// </summary>
    public void UpdateConnections()
    {
        // Check 4 adjacent grid positions
        connectedNorth = HasWallAt(gridPosition + Vector3.forward);
        connectedSouth = HasWallAt(gridPosition + Vector3.back);
        connectedEast = HasWallAt(gridPosition + Vector3.right);
        connectedWest = HasWallAt(gridPosition + Vector3.left);

        // Update visual appearance based on connections
        UpdateVisualMesh();

        Debug.Log($"WallConnector: Connections - N:{connectedNorth} S:{connectedSouth} E:{connectedEast} W:{connectedWest}");
    }

    /// <summary>
    /// Check if there's a wall at the specified grid position
    /// </summary>
    private bool HasWallAt(Vector3 position)
    {
        // Find all Wall components in scene
        Wall[] allWalls = FindObjectsByType<Wall>(FindObjectsSortMode.None);

        foreach (Wall wall in allWalls)
        {
            if (wall.gameObject == gameObject) continue;  // Skip self

            Vector3 wallGridPos = GridSnap.SnapXZ(wall.transform.position, gridCellSize);
            float distance = Vector3.Distance(
                new Vector3(position.x, 0, position.z),
                new Vector3(wallGridPos.x, 0, wallGridPos.z)
            );

            if (distance < 0.1f)  // Close enough to be considered adjacent
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Update the wall's visual appearance based on connection state
    /// </summary>
    private void UpdateVisualMesh()
    {
        // Determine connection pattern
        int connectionCount = 0;
        if (connectedNorth) connectionCount++;
        if (connectedSouth) connectionCount++;
        if (connectedEast) connectionCount++;
        if (connectedWest) connectionCount++;

        // For Phase 6A: Simple rotation based on primary connection axis
        if (connectedNorth && connectedSouth)
        {
            // North-South wall (default rotation)
            transform.rotation = Quaternion.Euler(0, 0, 0);
        }
        else if (connectedEast && connectedWest)
        {
            // East-West wall (rotate 90 degrees)
            transform.rotation = Quaternion.Euler(0, 90, 0);
        }
        else if (connectedNorth || connectedSouth)
        {
            // North or South connection only (default rotation)
            transform.rotation = Quaternion.Euler(0, 0, 0);
        }
        else if (connectedEast || connectedWest)
        {
            // East or West connection only (rotate 90 degrees)
            transform.rotation = Quaternion.Euler(0, 90, 0);
        }

        // TODO Phase 6B: Handle corner pieces (L-shapes) and T-junctions
        // For now, walls always align to their strongest connection axis
    }

    /// <summary>
    /// Notify adjacent walls to update their connections (called when this wall is destroyed)
    /// </summary>
    public void NotifyAdjacentWalls()
    {
        // Find all walls in adjacent positions
        Vector3[] adjacentPositions = new Vector3[]
        {
            gridPosition + Vector3.forward,  // North
            gridPosition + Vector3.back,     // South
            gridPosition + Vector3.right,    // East
            gridPosition + Vector3.left      // West
        };

        Wall[] allWalls = FindObjectsByType<Wall>(FindObjectsSortMode.None);

        foreach (Vector3 adjPos in adjacentPositions)
        {
            foreach (Wall wall in allWalls)
            {
                if (wall.gameObject == gameObject) continue;  // Skip self

                Vector3 wallGridPos = GridSnap.SnapXZ(wall.transform.position, gridCellSize);
                float distance = Vector3.Distance(
                    new Vector3(adjPos.x, 0, adjPos.z),
                    new Vector3(wallGridPos.x, 0, wallGridPos.z)
                );

                if (distance < 0.1f)
                {
                    // Found adjacent wall - tell it to update
                    WallConnector connector = wall.GetComponent<WallConnector>();
                    if (connector != null)
                    {
                        connector.UpdateConnections();
                    }
                }
            }
        }
    }

    void OnDestroy()
    {
        // Notify adjacent walls when this wall is destroyed
        NotifyAdjacentWalls();
    }

    // Visual debugging in Scene view
    void OnDrawGizmos()
    {
        if (!showConnectionGizmos) return;

        Vector3 center = transform.position + Vector3.up * 1f;

        // Draw connection lines
        if (connectedNorth)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(center, center + Vector3.forward * 0.5f);
        }
        if (connectedSouth)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(center, center + Vector3.back * 0.5f);
        }
        if (connectedEast)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(center, center + Vector3.right * 0.5f);
        }
        if (connectedWest)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(center, center + Vector3.left * 0.5f);
        }
    }
}
