using UnityEngine;

/// <summary>
/// One possible action a unit can take. Contains an array of Considerations
/// whose scores are multiplied together, plus an ActionExecutor that runs
/// when this action is selected.
/// </summary>
public class ActionOption
{
    public readonly string name;
    public readonly Consideration[] considerations;
    public readonly ActionExecutor executor;
    public readonly float basePriority;
    public readonly float momentumBonus;

    /// <summary>
    /// Create an action option.
    /// </summary>
    /// <param name="name">Debug name for this action</param>
    /// <param name="considerations">Pre-allocated array of scoring inputs</param>
    /// <param name="executor">The executor that runs this action</param>
    /// <param name="basePriority">Base multiplier (default 1.0)</param>
    /// <param name="momentumBonus">Bonus added when this action is already running (hysteresis)</param>
    public ActionOption(string name, Consideration[] considerations, ActionExecutor executor,
        float basePriority = 1f, float momentumBonus = 0.1f)
    {
        this.name = name;
        this.considerations = considerations;
        this.executor = executor;
        this.basePriority = basePriority;
        this.momentumBonus = momentumBonus;
    }

    /// <summary>
    /// Compute the final utility score for this action.
    /// Multiplies all consideration scores together, scales by basePriority,
    /// and adds momentum bonus if this is the currently running action.
    /// </summary>
    public float ComputeScore(AIBlackboard bb, bool isCurrentAction)
    {
        float score = basePriority;

        for (int i = 0; i < considerations.Length; i++)
        {
            float s = considerations[i].Score(bb);
            score *= s;

            // Early-out: if any consideration zeroes out, the whole action is dead
            if (score <= 0.001f) return 0f;
        }

        // Hysteresis: add momentum bonus to prevent rapid flip-flopping
        if (isCurrentAction)
        {
            score += momentumBonus;
        }

        return score;
    }
}
