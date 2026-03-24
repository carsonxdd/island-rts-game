/// <summary>
/// Score from whether walls are under attack.
/// Returns 1.0 when walls are under attack (defend!), 0.0 when all is quiet.
/// Caches the nearest wall under attack in the blackboard.
/// </summary>
public class WallIntegrity : Consideration
{
    public WallIntegrity(ResponseCurve curve) : base(curve) { }

    public override float ScoreRaw(AIBlackboard bb)
    {
        if (AIWorldState.Instance == null) return 0f;

        if (!AIWorldState.Instance.AreWallsUnderAttack()) return 0f;

        float distance;
        bb.wallUnderAttack = AIWorldState.Instance.GetNearestWallUnderAttack(
            bb.transform.position, out distance);
        bb.wallUnderAttackDistance = distance;

        if (bb.wallUnderAttack == null) return 0f;

        // Score higher when closer to a wall under attack
        float maxRange = bb.warriorSearchRadius > 0 ? bb.warriorSearchRadius : 50f;
        return UnityEngine.Mathf.Clamp01(1f - distance / maxRange);
    }
}
