using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Core Utility AI controller. Attached to each unit (Worker, Warrior, Enemy).
/// Staggered evaluation picks the highest-scoring ActionOption and drives its executor.
/// Zero GC per frame: no allocations in hot paths.
/// </summary>
public class AIBrain : MonoBehaviour
{
    // --- Evaluation throttle ---
    private float evalInterval;
    private float evalTimer;

    // Global per-frame throttle: max 5 evaluations per frame across all brains
    private static int evalFrame = -1;
    private static int evalCount = 0;

    // --- Commitment threshold ---
    private float commitmentThreshold = 0.2f;
    private float currentActionScore = 0f;
    private bool forceNextEval = false;

    // --- Actions ---
    private ActionOption[] actions;
    private int currentActionIndex = -1;

    // --- Blackboard ---
    public AIBlackboard blackboard { get; private set; }

    private bool isInitialized = false;

#if UNITY_EDITOR
    // --- Debug data (editor only) ---
    public float[] lastActionScores { get; private set; }
    public string[] actionNames { get; private set; }
    public int actionCount { get; private set; }
    public int debugCurrentActionIndex => currentActionIndex;

    // Consideration scores per action: [actionIndex][considerationIndex]
    public float[][] lastConsiderationScores { get; private set; }
    public string[][] considerationNames { get; private set; }
    public int[] considerationCounts { get; private set; }

    // Action history (circular buffer)
    public struct ActionHistoryEntry
    {
        public string actionName;
        public float score;
        public float timestamp;
    }
    public ActionHistoryEntry[] actionHistory { get; private set; }
    public int historyHead { get; private set; }
    public int historyCount { get; private set; }
    private const int HistorySize = 10;
#endif

    /// <summary>
    /// Initialize the brain with its action options and blackboard.
    /// Called by the unit's setup code (Worker, Warrior, Enemy).
    /// </summary>
    public void Initialize(ActionOption[] actionOptions, AIBlackboard bb)
    {
        actions = actionOptions;
        blackboard = bb;

        // Ensure AIWorldState singleton exists
        AIWorldState.EnsureExists();

#if UNITY_EDITOR
        // Ensure debug overlay exists
        AIDebugOverlay.EnsureExists();

        // Pre-allocate debug arrays
        actionCount = actions.Length;
        lastActionScores = new float[actionCount];
        actionNames = new string[actionCount];
        lastConsiderationScores = new float[actionCount][];
        considerationNames = new string[actionCount][];
        considerationCounts = new int[actionCount];

        for (int i = 0; i < actionCount; i++)
        {
            actionNames[i] = actions[i].name;
            int cCount = actions[i].considerations.Length;
            considerationCounts[i] = cCount;
            lastConsiderationScores[i] = new float[cCount];
            considerationNames[i] = new string[cCount];
            for (int c = 0; c < cCount; c++)
            {
                considerationNames[i][c] = actions[i].considerations[c].GetType().Name;
            }
        }

        actionHistory = new ActionHistoryEntry[HistorySize];
        historyHead = 0;
        historyCount = 0;
#endif

        // Randomize eval interval per unit to prevent timer phase-locking
        evalInterval = Random.Range(0.25f, 0.35f);

        // Stagger evaluation timers so not all units evaluate on the same frame
        evalTimer = Random.Range(0f, evalInterval);

        isInitialized = true;
    }

    void Update()
    {
        if (!isInitialized) return;

        // --- Staggered evaluation ---
        evalTimer -= Time.deltaTime;
        if (evalTimer <= 0f)
        {
            evalTimer = evalInterval;

            // Per-frame throttle
            if (Time.frameCount != evalFrame)
            {
                evalFrame = Time.frameCount;
                evalCount = 0;
            }

            if (evalCount < 5 || forceNextEval)
            {
                if (!forceNextEval) evalCount++;
                forceNextEval = false;
                EvaluateActions();
            }
        }

        // --- Execute current action every frame ---
        if (currentActionIndex >= 0 && currentActionIndex < actions.Length)
        {
            actions[currentActionIndex].executor.OnUpdate(blackboard);
            blackboard.stateDisplayName = actions[currentActionIndex].executor.DisplayName;
        }
    }

    void EvaluateActions()
    {
        if (actions == null || actions.Length == 0) return;

        int bestIndex = -1;
        float bestScore = -1f;

        for (int i = 0; i < actions.Length; i++)
        {
            bool isCurrent = (i == currentActionIndex);
            float score = actions[i].ComputeScore(blackboard, isCurrent);

            // Track current action's score for commitment threshold
            if (isCurrent) currentActionScore = score;

#if UNITY_EDITOR
            lastActionScores[i] = score;
            // Read back each consideration's lastScore
            for (int c = 0; c < actions[i].considerations.Length; c++)
            {
                lastConsiderationScores[i][c] = actions[i].considerations[c].lastScore;
            }
#endif

            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        // Switch action if a different one won — with commitment threshold
        if (bestIndex != currentActionIndex && bestIndex >= 0)
        {
            // Only switch if new action beats current by commitment threshold,
            // or if there is no current action
            if (currentActionIndex < 0 || bestScore > currentActionScore * (1f + commitmentThreshold))
            {
                // Exit current
                if (currentActionIndex >= 0 && currentActionIndex < actions.Length)
                {
                    actions[currentActionIndex].executor.OnExit(blackboard);
                }

#if UNITY_EDITOR
                // Record action switch in history
                actionHistory[historyHead] = new ActionHistoryEntry
                {
                    actionName = actions[bestIndex].name,
                    score = bestScore,
                    timestamp = Time.time
                };
                historyHead = (historyHead + 1) % HistorySize;
                if (historyCount < HistorySize) historyCount++;
#endif

                // Enter new
                currentActionIndex = bestIndex;
                actions[currentActionIndex].executor.OnEnter(blackboard);
            }
        }
    }

    /// <summary>
    /// Force re-evaluation on next frame (used when external events change state).
    /// Bypasses both the per-unit timer and the per-frame throttle.
    /// </summary>
    public void ForceReeval()
    {
        evalTimer = 0f;
        forceNextEval = true;
    }

    /// <summary>
    /// Get the display name of the current action (for state text).
    /// </summary>
    public string GetCurrentActionName()
    {
        if (currentActionIndex >= 0 && currentActionIndex < actions.Length)
            return actions[currentActionIndex].name;
        return "None";
    }
}
