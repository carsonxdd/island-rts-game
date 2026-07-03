using UnityEngine;

/// <summary>
/// Score from nearby enemy count using the density grid.
/// Normalized by a configurable max (e.g., 5 enemies = max threat).
/// Returns 0 when no enemies nearby, 1 when at or above maxThreat.
/// </summary>
public class ThreatNearby : Consideration
{
    private readonly float maxThreat;

    /// <param name="maxThreat">Enemy count at which threat is maxed (1.0)</param>
    public ThreatNearby(float maxThreat, ResponseCurve curve) : base(curve)
    {
        this.maxThreat = maxThreat;
    }

    public override float ScoreRaw(AIBlackboard bb)
    {
        if (AIWorldState.Instance == null) return 0f;

        int nearbyEnemies = AIWorldState.Instance.GetNearbyEnemyCount(bb.transform.position);
        return Mathf.Clamp01(nearbyEnemies / maxThreat);
    }
}
