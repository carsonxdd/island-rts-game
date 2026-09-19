using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Where an idle unit may STAND (2026-09-13). Idle colonists and patrolling warriors
/// pick spots off the NavMesh and then stop on them; the mesh says nothing about
/// traffic, so before this a stroll spot, a guard post, a night-time "stand at home"
/// or a walk that timed out could park a unit in a gateway - the one cell every
/// hauler, warrior and raider has to pass through.
/// </summary>
/// <remarks>
/// <para>Two rejects, both O(1) per gate: a cell held by <see cref="WallGrid"/> (a
/// wall, a gate or a wall site - standing on the line itself), and anything within
/// <see cref="GateClearance"/> of a gate's centre, which is the corridor on both
/// sides of the opening. A gate carves nothing, so the NavMesh cannot tell a
/// picker it is there.</para>
/// <para>Only the loiter pickers call this. A unit walking THROUGH a gate on an
/// errand is fine; a unit stopping in one is not. Fire clearance stays with each
/// executor's own <c>TooCloseToFire</c> (it needs the fire's collider edge).</para>
/// </remarks>
public static class Loiter
{
    /// <summary>Metres from a gate's centre that count as its corridor. Two cells each side of a 1 m gate cell plus the agent radius.</summary>
    public const float GateClearance = 3f;

    /// <summary>True when a unit may stand here without sitting on a wall line or in a gate corridor.</summary>
    public static bool IsClear(Vector3 point)
    {
        WallGrid grid = WallGrid.Instance;
        if (grid != null && grid.HasWallAt(grid.WorldToGrid(point))) return false;

        float sqr = GateClearance * GateClearance;
        var gates = Gate.ActiveList;
        for (int i = 0; i < gates.Count; i++)
        {
            Gate g = gates[i];
            if (g == null) continue;
            Vector3 d = point - g.transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < sqr) return false;
        }
        return true;
    }

    /// <summary>
    /// A clear NavMesh spot a short step from <paramref name="around"/>: the "step
    /// aside" for a unit that has already stopped somewhere it should not. Tries
    /// eight bearings between 2 m and <paramref name="radius"/>; false when none is
    /// clear (the caller stands where it is).
    /// </summary>
    public static bool TryFindClearNear(Vector3 around, float radius, out Vector3 point)
    {
        point = around;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float dist = Random.Range(2f, radius);
            Vector3 candidate = around + new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(candidate, out hit, 2f, NavMesh.AllAreas)) continue;
            if (!IsClear(hit.position)) continue;
            point = hit.position;
            return true;
        }
        return false;
    }
}
