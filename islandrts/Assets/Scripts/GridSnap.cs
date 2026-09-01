using UnityEngine;

/// <summary>
/// The one place world positions are quantised to the build grid. Everything that places,
/// previews or looks up a building goes through here, so they all agree on which cell a
/// position belongs to.
/// </summary>
public static class GridSnap
{
    /// <summary>
    /// Snaps X and Z to the nearest cell centre, leaving Y untouched (height comes from the
    /// terrain, not the grid).
    /// </summary>
    public static Vector3 SnapXZ(Vector3 worldPos, float cellSize)
    {
        // Use Floor(x + 0.5) instead of Round to avoid banker's rounding
        // Mathf.Round uses MidpointRounding.ToEven which inconsistently rounds .5 values
        // (e.g., Round(2.5)=2 but Round(3.5)=4), causing placement at no-build borders
        // to work on some buildings but not others depending on position
        float x = Mathf.Floor(worldPos.x / cellSize + 0.5f) * cellSize;
        float z = Mathf.Floor(worldPos.z / cellSize + 0.5f) * cellSize;
        return new Vector3(x, worldPos.y, z);
    }
}
