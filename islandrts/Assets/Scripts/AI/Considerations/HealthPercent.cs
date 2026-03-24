/// <summary>
/// Score from own health ratio (0-1). Returns currentHealth/maxHealth.
/// Use with different curves to create behaviors at different health thresholds.
/// </summary>
public class HealthPercent : Consideration
{
    public HealthPercent(ResponseCurve curve) : base(curve) { }

    public override float ScoreRaw(AIBlackboard bb)
    {
        if (bb.health == null) return 1f;
        return bb.health.GetHealthPercentage();
    }
}
