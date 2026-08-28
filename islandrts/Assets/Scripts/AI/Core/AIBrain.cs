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

    // --- Global per-frame evaluation budget ---
    //
    // The budget SCALES WITH POPULATION so each unit's think rate stays constant as
    // the colony grows. The old fixed cap of 5/frame was a hard ceiling at roughly
    //   5 evals/frame * 60fps / (1 / 0.3s per unit) = ~90 units
    // beyond which brains silently starved and units got sluggish.
    //
    // activeBrains * deltaTime / MinEvalInterval is exactly the number of evaluations
    // per frame needed to keep every brain on schedule. MaxEvalsPerFrame is a safety
    // ceiling so a pathological population can't tank the frame outright.
    private const float MinEvalInterval = 0.25f;
    private const int MinEvalsPerFrame = 5;
    private const int MaxEvalsPerFrame = 64;

    private static int evalFrame = -1;
    private static int evalCount = 0;
    private static int frameBudget = MinEvalsPerFrame;
    private static int activeBrains = 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        evalFrame = -1;
        evalCount = 0;
        frameBudget = MinEvalsPerFrame;
        activeBrains = 0;
    }

    /// <summary>
    /// Roll the per-frame budget over. Called by the first brain to want an
    /// evaluation each frame.
    /// </summary>
    static void BeginFrame()
    {
        if (Time.frameCount == evalFrame) return;
        evalFrame = Time.frameCount;
        evalCount = 0;

        int needed = Mathf.CeilToInt(activeBrains * Time.deltaTime / MinEvalInterval);
        frameBudget = Mathf.Clamp(needed, MinEvalsPerFrame, MaxEvalsPerFrame);
    }

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

        // Population drives the per-frame budget. Guarded so a re-Initialize on the
        // same brain can't double-count.
        if (!isInitialized) activeBrains++;

        isInitialized = true;
    }

    void OnDestroy()
    {
        if (isInitialized)
        {
            isInitialized = false;
            activeBrains--;
        }
    }

    void Update()
    {
        if (!isInitialized) return;

        // --- Staggered evaluation ---
        evalTimer -= Time.deltaTime;
        if (evalTimer <= 0f || forceNextEval)
        {
            BeginFrame();

            // Forced evaluations (external events: target died, damage taken, an enemy
            // died nearby) may exceed the normal budget so they stay responsive — but
            // only up to the hard ceiling. Previously they bypassed the throttle
            // entirely AND didn't consume budget, so one enemy dying in the base made
            // every worker in a 30u radius evaluate on the same frame. That spike grew
            // linearly with population and is the classic source of combat stutter.
            int cap = forceNextEval ? MaxEvalsPerFrame : frameBudget;

            if (evalCount < cap)
            {
                evalCount++;
                PerfCounters.Hit(PerfCounters.K.Eval);
                if (forceNextEval) PerfCounters.Hit(PerfCounters.K.EvalForced);
                forceNextEval = false;
                // Only reset the timer once the evaluation ACTUALLY runs. The old code
                // reset it before the throttle check, so a throttled brain silently
                // dropped that evaluation and waited another full interval instead of
                // retrying. Leaving the timer at/below zero makes it retry next frame.
                evalTimer = evalInterval;

                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                EvaluateActions();
                PerfCounters.EvalTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
            }
            else
            {
                PerfCounters.Hit(PerfCounters.K.EvalDeferred);
            }
            // Over budget: evalTimer stays <= 0 (and forceNextEval stays set), so this
            // brain retries next frame rather than losing the evaluation.
        }

        // --- Execute current action every frame ---
        if (currentActionIndex >= 0 && currentActionIndex < actions.Length)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            actions[currentActionIndex].executor.OnUpdate(blackboard);
            PerfCounters.ExecTicks += System.Diagnostics.Stopwatch.GetTimestamp() - t0;

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
