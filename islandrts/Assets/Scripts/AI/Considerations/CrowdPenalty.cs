using UnityEngine;

/// <summary>
/// Score penalizing actions that many allies are already doing.
/// Used by enemies to distribute across walls, and by workers to spread across resources.
/// Returns 1.0 when uncrowded, decreasing toward 0 as crowd increases.
/// </summary>
public class CrowdPenalty : Consideration
{
    private readonly float maxCrowd;

    /// <param name="maxCrowd">Ally count at which penalty is maximized</param>
    public CrowdPenalty(float maxCrowd, ResponseCurve curve) : base(curve)
    {
        this.maxCrowd = maxCrowd;
    }

    public override float ScoreRaw(AIBlackboard bb)
    {
        // Everyone committed to the best node: walking there and already working there
        // (claims alone ignored the workers standing at it, 2026-09-08)
        if (bb.bestResource != null)
        {
            int crowd = bb.bestResource.GetWorkerCount();
            return Mathf.Clamp01(1f - crowd / maxCrowd);
        }
        return 1f; // No crowd info available, no penalty
    }
}
