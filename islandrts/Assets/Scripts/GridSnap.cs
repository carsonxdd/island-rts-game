using UnityEngine;

public static class GridSnap
{
    public static Vector3 SnapXZ(Vector3 worldPos, float cellSize)
    {
        float x = Mathf.Round(worldPos.x / cellSize) * cellSize;
        float z = Mathf.Round(worldPos.z / cellSize) * cellSize;
        return new Vector3(x, worldPos.y, z);
    }
}
