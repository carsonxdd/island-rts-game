using UnityEngine;

/// <summary>
/// Scores based on how close the nearest enemy is, normalized by a range.
/// Returns 1.0 when enemy is at the unit's position, 0.0 when at or beyond maxRange.
/// Reads bb.nearestEnemy (populated by EnemyPresence consideration — must evaluate first).
/// </summary>
public class EnemyProximity : Consideration
{
    private readonly float maxRange;

    public EnemyProximity(float maxRange, ResponseCurve curve) : base(curve)
    {
        this.maxRange = maxRange;
    }

    public override float ScoreRaw(AIBlackboard bb)
    {
        if (bb.nearestEnemy == null) return 0f;
        return Mathf.Clamp01(1f - bb.nearestEnemyDistance / maxRange);
    }
}
