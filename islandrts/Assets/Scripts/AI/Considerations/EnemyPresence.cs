using UnityEngine;

/// <summary>
/// Scores based on whether enemies exist and caches the nearest one.
/// Unlike ThreatNearby (which uses the density grid), this does an exact scan
/// and populates bb.nearestEnemy for other systems to use.
/// Returns 1.0 if any living enemy exists, 0.0 if none.
/// </summary>
public class EnemyPresence : Consideration
{
    private readonly float maxRange;

    /// <param name="maxRange">Max distance to consider. 0 = unlimited (any living enemy counts).</param>
    public EnemyPresence(float maxRange, ResponseCurve curve) : base(curve)
    {
        this.maxRange = maxRange;
    }

    public override float ScoreRaw(AIBlackboard bb)
    {
        // Full scan runs once per unit per frame; every EnemyPresence instance
        // sharing this blackboard (Engage/Intercept/Patrol/Heal) reuses the result
        // and only applies its own maxRange cutoff. The scan itself is the shared
        // TargetingUtil nearest-alive scan (Phase 6.25).
        if (bb.enemyScanFrame != Time.frameCount)
        {
            float dist;
            Enemy nearest = TargetingUtil.FindNearest(Enemy.ActiveList, bb.transform.position, 0f, out dist);

            bb.scannedNearestEnemy = nearest != null ? nearest.transform : null;
            bb.scannedNearestEnemyDist = dist;
            bb.enemyScanFrame = Time.frameCount;
        }

        bool inRange = bb.scannedNearestEnemy != null &&
                       (maxRange <= 0f || bb.scannedNearestEnemyDist < maxRange);

        // Cache for other systems (EngageEnemyExecutor, InterceptExecutor)
        bb.nearestEnemy = inRange ? bb.scannedNearestEnemy : null;
        bb.nearestEnemyDistance = inRange ? bb.scannedNearestEnemyDist : float.MaxValue;

        return inRange ? 1f : 0f;
    }
}
