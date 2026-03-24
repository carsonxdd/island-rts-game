using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Enemy-specific consideration: checks if the path to the campfire is blocked by walls.
/// Returns 1.0 if path is blocked (walls need breaching), 0.0 if path is clear.
/// Also checks if any walls/gates exist to breach.
/// </summary>
public class PathBlocked : Consideration
{
    private readonly AIBlackboard cachedBB;
    private NavMeshPath reusablePath;
    private float checkTimer = 0f;
    private float checkInterval = 2f;
    private bool lastResult = false;

    public PathBlocked(AIBlackboard bb) : base(ResponseCurve.Linear(1f, 0f))
    {
        cachedBB = bb;
        reusablePath = new NavMeshPath();
        checkTimer = Random.Range(0f, checkInterval);
    }

    public override float ScoreRaw(AIBlackboard bb)
    {
        // No walls/gates = nothing to breach
        if (Wall.ActiveList.Count == 0 && Gate.ActiveList.Count == 0)
            return 0f;

        // Only check path periodically (expensive)
        checkTimer += Time.deltaTime;
        if (checkTimer >= checkInterval)
        {
            checkTimer = 0f;

            BaseBuilding campfire = BaseBuilding.ActiveList.Count > 0 ? BaseBuilding.ActiveList[0] : null;
            if (campfire == null)
            {
                lastResult = false;
                return 0f;
            }

            // Try to calculate path to campfire
            if (AINavHelper.TryCalculatePath(
                bb.transform.position, campfire.transform.position,
                NavMesh.AllAreas, reusablePath))
            {
                // Path is blocked if partial (walls in the way) or invalid
                lastResult = reusablePath.status != NavMeshPathStatus.PathComplete;
            }
            // If throttled, keep last result
        }

        return lastResult ? 1f : 0f;
    }
}
