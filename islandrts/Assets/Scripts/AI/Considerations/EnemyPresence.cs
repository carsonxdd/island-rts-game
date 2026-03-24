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
        Transform nearest = null;
        float nearestDist = maxRange > 0f ? maxRange : float.MaxValue;

        for (int i = 0; i < Enemy.ActiveList.Count; i++)
        {
            Enemy enemy = Enemy.ActiveList[i];
            if (enemy == null) continue;
            Health h = enemy.CachedHealth;
            if (h != null && !h.IsAlive) continue;

            float dist = Vector3.Distance(bb.transform.position, enemy.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = enemy.transform;
            }
        }

        // Cache for other systems (EngageEnemyExecutor, InterceptExecutor)
        bb.nearestEnemy = nearest;
        bb.nearestEnemyDistance = nearest != null ? nearestDist : float.MaxValue;

        return nearest != null ? 1f : 0f;
    }
}
