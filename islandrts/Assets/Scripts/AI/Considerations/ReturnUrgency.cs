using UnityEngine;

/// <summary>
/// Compound consideration for the Return-to-Base action.
/// Combines inventory fullness with external pressure (enemies, nightfall)
/// and drop-off efficiency so workers make smart return decisions.
///
/// Score = max(baseScore, enemyBoost, nightBoost, efficiencyBoost)
///   baseScore       = carryRatio ^ fullExponent       (only near-full triggers)
///   enemyBoost      = carryRatio * threatLevel         (enemies nearby)
///   nightBoost      = carryRatio * (1 - dayProgress)   (dusk approaching)
///   efficiencyBoost = carryRatio * (1 - distBase/distResource)
///                     when carry > 50% and base is closer than next resource
///
/// All are proportional to carryRatio, so empty workers never return.
/// </summary>
public class ReturnUrgency : Consideration
{
    private readonly float fullExponent;
    private readonly float maxThreat;

    /// <param name="fullExponent">Power curve exponent for the "full inventory" base score (higher = steeper)</param>
    /// <param name="maxThreat">Enemy count at which threat is maxed (1.0)</param>
    public ReturnUrgency(float fullExponent, float maxThreat, ResponseCurve curve) : base(curve)
    {
        this.fullExponent = fullExponent;
        this.maxThreat = maxThreat;
    }

    public override float ScoreRaw(AIBlackboard bb)
    {
        if (bb.carryCapacity <= 0f) return 0f;

        float carryRatio = Mathf.Clamp01(bb.carryAmount / bb.carryCapacity);

        // No resources carried → nothing to return
        if (carryRatio < 0.01f) return 0f;

        // 0) No resources available — return with whatever we have
        //    Without this, carry^15 at partial fill ≈ 0 and workers get stuck
        //    in Gather at base unable to deliver.
        //    Use 0.5 floor when carry is meaningful (>10%) to reliably beat
        //    Gather's momentum bonus + commitment threshold.
        if (bb.bestResource == null)
        {
            return carryRatio > 0.1f ? Mathf.Max(carryRatio, 0.5f) : carryRatio;
        }

        // 1) Base: steep curve, only kicks in when inventory is nearly full
        float baseScore = Mathf.Pow(carryRatio, fullExponent);

        // 2) Enemy pressure: return early when enemies are nearby
        float enemyBoost = 0f;
        if (AIWorldState.Instance != null)
        {
            int nearbyEnemies = AIWorldState.Instance.GetNearbyEnemyCount(bb.transform.position);
            float threatLevel = Mathf.Clamp01(nearbyEnemies / maxThreat);
            enemyBoost = carryRatio * threatLevel;
        }

        // 3) Night pressure: return early when dusk/night is approaching
        float nightBoost = 0f;
        if (AIWorldState.Instance != null)
        {
            float nightPressure = 1f - AIWorldState.Instance.dayProgress;
            nightBoost = carryRatio * nightPressure;
        }

        // 4) Drop-off efficiency: if base is closer than the next resource and
        //    worker has significant carry, it's smarter to drop off first than
        //    run to a far node, gather 1, and run all the way back.
        float efficiencyBoost = 0f;
        if (carryRatio > 0.5f && bb.baseBuilding != null && bb.bestResource != null)
        {
            float distToBase = Vector3.Distance(bb.transform.position, bb.baseBuilding.transform.position);
            float distToResource = Vector3.Distance(bb.transform.position, bb.bestResource.transform.position);

            if (distToBase < distToResource && distToResource > 1f)
            {
                // Scales with how much closer base is vs resource
                // e.g. base 5m, resource 25m: 0.8 * (1 - 5/25) = 0.64
                // e.g. base 5m, resource 8m:  0.8 * (1 - 5/8)  = 0.30
                efficiencyBoost = carryRatio * (1f - distToBase / distToResource);
            }
        }

        // Take the strongest signal
        return Mathf.Max(baseScore, Mathf.Max(enemyBoost, Mathf.Max(nightBoost, efficiencyBoost)));
    }
}
