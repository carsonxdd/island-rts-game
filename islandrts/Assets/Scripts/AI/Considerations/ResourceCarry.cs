using UnityEngine;

/// <summary>
/// Score from carry amount relative to capacity.
/// Returns carryAmount / carryCapacity (0 = empty, 1 = full).
/// </summary>
public class ResourceCarry : Consideration
{
    public ResourceCarry(ResponseCurve curve) : base(curve) { }

    public override float ScoreRaw(AIBlackboard bb)
    {
        if (bb.carryCapacity <= 0f) return 0f;
        return Mathf.Clamp01(bb.carryAmount / bb.carryCapacity);
    }
}
