/// <summary>
/// Abstract base for one scoring input in the Utility AI system.
/// Each Consideration produces a 0-1 raw score, then applies a ResponseCurve
/// to shape it into a utility value.
/// </summary>
public abstract class Consideration
{
    public ResponseCurve curve;

#if UNITY_EDITOR
    /// <summary>
    /// Last computed score (after curve). Used by AIDebugOverlay for visualization.
    /// </summary>
    public float lastScore;
#endif

    protected Consideration(ResponseCurve curve)
    {
        this.curve = curve;
    }

    /// <summary>
    /// Return a raw 0-1 value representing this input.
    /// e.g. health percent, distance normalized, time of day, etc.
    /// </summary>
    public abstract float ScoreRaw(AIBlackboard bb);

    /// <summary>
    /// Final score: raw value shaped by the response curve. Always 0-1.
    /// </summary>
    public float Score(AIBlackboard bb)
    {
        float result = curve.Evaluate(ScoreRaw(bb));
#if UNITY_EDITOR
        lastScore = result;
#endif
        return result;
    }
}
