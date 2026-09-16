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
            ITargetable nearest = TargetingUtil.FindNearestHostileCombatant(bb.transform.position, 0f, bb.faction, out dist);

            bb.scannedNearestEnemy = nearest != null ? nearest.transform : null;
            bb.scannedNearestEnemyDist = dist;
            bb.enemyScanFrame = Time.frameCount;
        }

        bool inRange = bb.scannedNearestEnemy != null &&
                       (maxRange <= 0f || bb.scannedNearestEnemyDist < maxRange);

        // A landing party with nobody left to fight still has a camp to take
        // (2026-09-16, the conquest test): the nearest building of the colony it
        // landed on counts as presence, or Engage scores 0 before its own scan runs.
        if (!inRange && bb.warrior != null)
        {
            Faction siegeOf = Siege.TargetOf(bb.warrior);
            if (siegeOf != null)
            {
                float d;
                ITargetable b = Siege.FindNearestBuilding(bb.transform.position, siegeOf, out d);
                if (b != null && (maxRange <= 0f || d < maxRange))
                {
                    bb.scannedNearestEnemy = b.transform;
                    bb.scannedNearestEnemyDist = d;
                    inRange = true;
                }
            }
        }

        // Cache for other systems (EngageEnemyExecutor, InterceptExecutor)
        bb.nearestEnemy = inRange ? bb.scannedNearestEnemy : null;
        bb.nearestEnemyDistance = inRange ? bb.scannedNearestEnemyDist : float.MaxValue;

        return inRange ? 1f : 0f;
    }
}
