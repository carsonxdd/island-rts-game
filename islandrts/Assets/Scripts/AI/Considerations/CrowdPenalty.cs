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
        // Use the claim count on the best resource as a crowd proxy
        if (bb.bestResource != null)
        {
            int claimCount = bb.bestResource.GetClaimCount();
            return Mathf.Clamp01(1f - claimCount / maxCrowd);
        }
        return 1f; // No crowd info available, no penalty
    }
}
