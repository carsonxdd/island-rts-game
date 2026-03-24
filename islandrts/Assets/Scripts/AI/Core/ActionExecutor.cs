/// <summary>
/// Abstract base for action execution. Implements a simple state machine
/// with OnEnter (setup), OnUpdate (per-frame logic), and OnExit (cleanup).
/// </summary>
public abstract class ActionExecutor
{
    /// <summary>Display name for state text UI.</summary>
    public abstract string DisplayName { get; }

    /// <summary>Called once when this action becomes the active action.</summary>
    public abstract void OnEnter(AIBlackboard bb);

    /// <summary>Called every frame while this action is active.</summary>
    public abstract void OnUpdate(AIBlackboard bb);

    /// <summary>Called once when this action is replaced by another action.</summary>
    public abstract void OnExit(AIBlackboard bb);
}
