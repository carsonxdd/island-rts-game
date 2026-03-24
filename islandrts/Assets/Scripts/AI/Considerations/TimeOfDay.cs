/// <summary>
/// Score from the day/night cycle progress.
/// dayProgress: 0 = full night, 1 = full day.
/// Use with InverseLinear to score higher at night (for flee/shelter behaviors).
/// Use with Linear to score higher during day (for gather behaviors).
/// </summary>
public class TimeOfDay : Consideration
{
    private readonly bool invertForNight;

    /// <param name="invertForNight">If true, returns (1 - dayProgress) so night = high score</param>
    public TimeOfDay(bool invertForNight, ResponseCurve curve) : base(curve)
    {
        this.invertForNight = invertForNight;
    }

    public override float ScoreRaw(AIBlackboard bb)
    {
        if (AIWorldState.Instance == null) return 0.5f;

        float dp = AIWorldState.Instance.dayProgress;
        return invertForNight ? (1f - dp) : dp;
    }
}
