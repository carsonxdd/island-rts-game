using UnityEngine;

/// <summary>
/// Score based on distance to a target, normalized by a max range.
/// Returns 1.0 when at the target, 0.0 when at or beyond maxRange.
/// Use with InverseLinear curve to prefer closer targets.
/// </summary>
public class DistanceTo : Consideration
{
    public enum TargetType { CurrentTarget, Base, NearestEnemy, WallUnderAttack, BestResource }

    private readonly TargetType targetType;
    private readonly float maxRange;

    public DistanceTo(TargetType targetType, float maxRange, ResponseCurve curve) : base(curve)
    {
        this.targetType = targetType;
        this.maxRange = maxRange;
    }

    public override float ScoreRaw(AIBlackboard bb)
    {
        Vector3 targetPos;
        switch (targetType)
        {
            case TargetType.CurrentTarget:
                if (bb.currentTarget == null) return 0f;
                targetPos = bb.currentTarget.position;
                break;
            case TargetType.Base:
                if (bb.baseBuilding == null) return 0f;
                targetPos = bb.baseBuilding.transform.position;
                break;
            case TargetType.NearestEnemy:
                if (bb.nearestEnemy == null) return 0f;
                targetPos = bb.nearestEnemy.position;
                break;
            case TargetType.WallUnderAttack:
                if (bb.wallUnderAttack == null) return 0f;
                targetPos = bb.wallUnderAttack.position;
                break;
            case TargetType.BestResource:
                if (bb.bestResource == null) return 0f;
                targetPos = bb.bestResource.transform.position;
                break;
            default:
                return 0f;
        }

        float distance = Vector3.Distance(bb.transform.position, targetPos);
        // Normalized: 1.0 = at target, 0.0 = at maxRange
        return Mathf.Clamp01(1f - distance / maxRange);
    }
}
