#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Frame-by-frame stutter recorder. Self-bootstraps in Play mode (no scene
/// wiring) and streams a CSV to &lt;project&gt;/PerfLogs/perf_latest.log so the
/// session can be read back and analyzed during or after a playtest.
///
/// Captured per frame: real frame time, GC allocation + collections, AI
/// evaluation counts/cost, NavMesh command throttling, NavMesh surface rebuilds
/// and carve changes, combat-VFX object creation, and — the actual visible
/// symptom — how many agents WANTED to move that frame but had ~zero velocity
/// ("blocked"), plus how many were waiting on a path.
///
/// Hotkeys: F6 marks the log at the instant the player sees a stutter (the most
/// valuable signal there is — it timestamps the human observation against the
/// data). F7 stops/resumes recording.
///
/// Editor / dev-build only, like DebugMenu and AIDebugOverlay.
/// </summary>
[DefaultExecutionOrder(10000)]
public class PerfLogger : MonoBehaviour
{
    // Frames slower than this get a detailed SPIKE line with a ranked breakdown.
    private const float SpikeMs = 24f;      // ~40fps — slower than this is felt
    private const float BadSpikeMs = 50f;
    private const float FlushInterval = 0.5f;
    private const int RingSize = 240;       // ~4s of history at 60fps

    private static PerfLogger instance;

    private StreamWriter writer;
    private string logPath;
    private bool recording = true;
    private float flushTimer;

    // Rolling window for summaries
    private readonly float[] msRing = new float[RingSize];
    private readonly float[] sortScratch = new float[RingSize];
    private int ringHead;
    private int ringCount;

    private float summaryTimer;
    private int summaryFrames;
    private float summaryMsSum, summaryMsMax;
    private int summarySpikes;
    private long summaryGcBytes;

    private int lastGcCollections;
    private long lastGcTotal;

    private int spikeCount;
    private float worstMs;
    private int worstFrame;

    private readonly StringBuilder sb = new StringBuilder(768);

    // --- Profiler counters (invalid ones report -1 and are simply ignored) ---
    private ProfilerRecorder recGcAlloc;
    private ProfilerRecorder recMainThread;
    private ProfilerRecorder recBehaviourUpdate;
    private ProfilerRecorder recCameraRender;
    private ProfilerRecorder recPhysics;
    private ProfilerRecorder recDrawCalls;
    private ProfilerRecorder recSetPass;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (instance != null) return;
        var go = new GameObject("[PerfLogger]");
        instance = go.AddComponent<PerfLogger>();
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;

        try
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "PerfLogs"));
            Directory.CreateDirectory(dir);
            logPath = Path.Combine(dir, "perf_latest.log");
            // Truncate on each Play session: a stable path is what makes the file
            // easy to read back without hunting for the newest timestamp.
            writer = new StreamWriter(new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite));
            writer.AutoFlush = false;
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError("PerfLogger: could not open log file — " + e.Message);
            enabled = false;
            return;
        }

        StartRecorders();
        WriteHeader();
        UnityEngine.Debug.Log("PerfLogger recording to " + logPath + "  (F6 = mark a stutter, F7 = stop/resume)");
    }

    void StartRecorders()
    {
        recGcAlloc         = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        recMainThread      = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread");
        recBehaviourUpdate = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Behaviour.Update");
        recCameraRender    = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Camera.Render");
        recPhysics         = ProfilerRecorder.StartNew(ProfilerCategory.Physics, "Physics.Processing");
        recDrawCalls       = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        recSetPass         = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
    }

    /// <summary>
    /// Write the builder's chars straight to the stream. sb.ToString() would
    /// allocate a fresh ~300-byte string EVERY frame — a recorder that adds its
    /// own GC pressure taints the very measurement it exists to take.
    /// </summary>
    void WriteLine(StringBuilder s)
    {
        for (int i = 0; i < s.Length; i++) writer.Write(s[i]);
        writer.Write('\n');
    }

    static long Val(ProfilerRecorder r) { return r.Valid ? r.LastValue : -1L; }
    static double Ms(ProfilerRecorder r) { return r.Valid ? r.LastValue / 1e6 : -1.0; }

    void WriteHeader()
    {
        var c = CultureInfo.InvariantCulture;
        writer.WriteLine("# Island RTS perf log");
        writer.WriteLine("# started=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", c));
        writer.WriteLine("# unity=" + Application.unityVersion + " platform=" + Application.platform + " editor=" + Application.isEditor);
        writer.WriteLine("# scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        writer.WriteLine("# vSync=" + QualitySettings.vSyncCount + " targetFrameRate=" + Application.targetFrameRate
                         + " fixedDelta=" + Time.fixedDeltaTime.ToString("0.####", c)
                         + " maxDelta=" + Time.maximumDeltaTime.ToString("0.####", c));
        writer.WriteLine("# navPathIterPerFrame=" + NavMesh.pathfindingIterationsPerFrame
                         + " avoidancePredictionTime=" + NavMesh.avoidancePredictionTime.ToString("0.###", c));
        writer.WriteLine("# quality=" + QualitySettings.names[QualitySettings.GetQualityLevel()]
                         + " shadowDistance=" + QualitySettings.shadowDistance.ToString("0.#", c));
        writer.WriteLine("# recorders: gcAlloc=" + recGcAlloc.Valid + " mainThread=" + recMainThread.Valid
                         + " behaviourUpdate=" + recBehaviourUpdate.Valid + " cameraRender=" + recCameraRender.Valid
                         + " physics=" + recPhysics.Valid + " drawCalls=" + recDrawCalls.Valid);
        writer.WriteLine("#");
        writer.WriteLine("# F lines = one per frame. E lines = events (spikes, user F6 marks). S lines = 1s summaries.");
        writer.WriteLine("# ms is REAL (unscaled) frame time. blocked = agents with a path and a real desired");
        writer.WriteLine("#   velocity but ~zero actual velocity: the freeze you SEE. pathPend = waiting on a path.");
        writer.WriteLine("#");
        writer.WriteLine("F,frame,t,ms,gcAllocB,gcColl,evals,evalFwd,evalDefer,evalMs,execMs,"
                         + "sdTry,sdOk,sdThrot,sdRej,cpTry,cpOk,cpThrot,"
                         + "navUpd,terrRebuild,carve,vfx,vfxMs,spawn,death,"
                         + "agents,pathPend,blocked,slow,avgSpeed,workers,warriors,enemies,nodes,"
                         + "mainMs,behavMs,renderMs,physMs,drawCalls,setPass,timeScale");
        writer.Flush();
    }

    void Update()
    {
        // Hotkeys in Update so a mark lands on the frame the key was pressed.
        if (Input.GetKeyDown(KeyCode.F6))
        {
            Event("MARK", "user reported a stutter here");
            UnityEngine.Debug.Log("PerfLogger: marked frame " + Time.frameCount);
        }
        if (Input.GetKeyDown(KeyCode.F7))
        {
            recording = !recording;
            Event("RECORDING", recording ? "resumed" : "paused");
            UnityEngine.Debug.Log("PerfLogger: recording " + (recording ? "resumed" : "paused"));
        }
    }

    void LateUpdate()
    {
        if (writer == null) return;

        float ms = Time.unscaledDeltaTime * 1000f;

        // --- GC ---
        int coll = GC.CollectionCount(0);
        int collDelta = coll - lastGcCollections;
        lastGcCollections = coll;
        long gcAlloc = Val(recGcAlloc);
        if (gcAlloc < 0)
        {
            long total = GC.GetTotalMemory(false);
            gcAlloc = Math.Max(0, total - lastGcTotal);
            lastGcTotal = total;
        }

        // --- Agent scan: the "is anything actually frozen" measurement ---
        int agents = 0, pathPend = 0, blocked = 0, slow = 0;
        float speedSum = 0f;
        ScanAgents(Worker.ActiveList, ref agents, ref pathPend, ref blocked, ref slow, ref speedSum);
        ScanAgents(Warrior.ActiveList, ref agents, ref pathPend, ref blocked, ref slow, ref speedSum);
        ScanAgents(Enemy.ActiveList, ref agents, ref pathPend, ref blocked, ref slow, ref speedSum);
        float avgSpeed = agents > 0 ? speedSum / agents : 0f;

        int[] k = PerfCounters.Frame;
        double evalMs = PerfCounters.EvalTicks * PerfCounters.TicksToMs;
        double execMs = PerfCounters.ExecTicks * PerfCounters.TicksToMs;
        double vfxMs = PerfCounters.VfxTicks * PerfCounters.TicksToMs;

        if (recording)
        {
            var c = CultureInfo.InvariantCulture;
            sb.Length = 0;
            sb.Append("F,").Append(Time.frameCount).Append(',')
              .Append(Time.unscaledTime.ToString("0.000", c)).Append(',')
              .Append(ms.ToString("0.00", c)).Append(',')
              .Append(gcAlloc).Append(',').Append(collDelta).Append(',')
              .Append(k[(int)PerfCounters.K.Eval]).Append(',')
              .Append(k[(int)PerfCounters.K.EvalForced]).Append(',')
              .Append(k[(int)PerfCounters.K.EvalDeferred]).Append(',')
              .Append(evalMs.ToString("0.00", c)).Append(',')
              .Append(execMs.ToString("0.00", c)).Append(',')
              .Append(k[(int)PerfCounters.K.SetDestTry]).Append(',')
              .Append(k[(int)PerfCounters.K.SetDestOk]).Append(',')
              .Append(k[(int)PerfCounters.K.SetDestThrottled]).Append(',')
              .Append(k[(int)PerfCounters.K.SetDestRejected]).Append(',')
              .Append(k[(int)PerfCounters.K.CalcPathTry]).Append(',')
              .Append(k[(int)PerfCounters.K.CalcPathOk]).Append(',')
              .Append(k[(int)PerfCounters.K.CalcPathThrottled]).Append(',')
              .Append(k[(int)PerfCounters.K.NavMeshUpdate]).Append(',')
              .Append(k[(int)PerfCounters.K.TerrainRebuild]).Append(',')
              .Append(k[(int)PerfCounters.K.CarveChange]).Append(',')
              .Append(k[(int)PerfCounters.K.VfxSpawn]).Append(',')
              .Append(vfxMs.ToString("0.00", c)).Append(',')
              .Append(k[(int)PerfCounters.K.UnitSpawn]).Append(',')
              .Append(k[(int)PerfCounters.K.UnitDeath]).Append(',')
              .Append(agents).Append(',').Append(pathPend).Append(',').Append(blocked).Append(',').Append(slow).Append(',')
              .Append(avgSpeed.ToString("0.00", c)).Append(',')
              .Append(Worker.ActiveList.Count).Append(',')
              .Append(Warrior.ActiveList.Count).Append(',')
              .Append(Enemy.ActiveList.Count).Append(',')
              .Append(ResourceNode.ActiveList.Count).Append(',')
              .Append(Ms(recMainThread).ToString("0.00", c)).Append(',')
              .Append(Ms(recBehaviourUpdate).ToString("0.00", c)).Append(',')
              .Append(Ms(recCameraRender).ToString("0.00", c)).Append(',')
              .Append(Ms(recPhysics).ToString("0.00", c)).Append(',')
              .Append(Val(recDrawCalls)).Append(',')
              .Append(Val(recSetPass)).Append(',')
              .Append(Time.timeScale.ToString("0.##", c));
            WriteLine(sb);

            if (ms > SpikeMs) WriteSpike(ms, gcAlloc, collDelta, evalMs, execMs, vfxMs, blocked, pathPend);
        }

        // --- Rolling stats ---
        msRing[ringHead] = ms;
        ringHead = (ringHead + 1) % RingSize;
        if (ringCount < RingSize) ringCount++;

        summaryFrames++;
        summaryMsSum += ms;
        if (ms > summaryMsMax) summaryMsMax = ms;
        if (ms > SpikeMs) { summarySpikes++; spikeCount++; }
        summaryGcBytes += Math.Max(0, gcAlloc);
        if (ms > worstMs) { worstMs = ms; worstFrame = Time.frameCount; }

        summaryTimer += Time.unscaledDeltaTime;
        if (summaryTimer >= 1f)
        {
            WriteSummary();
            summaryTimer = 0f;
            summaryFrames = 0;
            summaryMsSum = 0f;
            summaryMsMax = 0f;
            summarySpikes = 0;
            summaryGcBytes = 0;
        }

        PerfCounters.ResetFrame();

        flushTimer += Time.unscaledDeltaTime;
        if (flushTimer >= FlushInterval)
        {
            flushTimer = 0f;
            writer.Flush();   // keeps the file readable mid-playtest
        }
    }

    static void ScanAgents<T>(System.Collections.Generic.IReadOnlyList<T> list,
        ref int agents, ref int pathPend, ref int blocked, ref int slow, ref float speedSum)
        where T : UnitBase<T>
    {
        for (int i = 0; i < list.Count; i++)
        {
            T u = list[i];
            if (u == null) continue;
            NavMeshAgent a = u.CachedAgent;
            if (a == null || !a.enabled || !a.isOnNavMesh) continue;

            agents++;
            if (a.pathPending) pathPend++;

            float speed = a.velocity.magnitude;
            speedSum += speed;

            // "Wants to move" = has a path and steering produced a real desired
            // velocity. If actual velocity is ~0 anyway the unit is visibly frozen:
            // carve recalc, avoidance deadlock, or a lost path.
            if (!a.isStopped && a.hasPath && a.desiredVelocity.sqrMagnitude > 0.25f)
            {
                if (speed < 0.05f) blocked++;
                else if (speed < a.speed * 0.4f) slow++;
            }
        }
    }

    void WriteSpike(float ms, long gcAlloc, int collDelta, double evalMs, double execMs, double vfxMs,
        int blocked, int pathPend)
    {
        var c = CultureInfo.InvariantCulture;
        int[] k = PerfCounters.Frame;

        sb.Length = 0;
        sb.Append("E,").Append(Time.frameCount).Append(',')
          .Append(Time.unscaledTime.ToString("0.000", c)).Append(',')
          .Append(ms >= BadSpikeMs ? "BADSPIKE," : "SPIKE,")
          .Append(ms.ToString("0.0", c)).Append("ms | ");

        // Ranked "likely culprit" notes, so the log reads without cross-referencing
        // every column by hand.
        if (collDelta > 0) sb.Append("GC-COLLECT x").Append(collDelta).Append(" | ");
        if (gcAlloc > 200000) sb.Append("alloc ").Append(gcAlloc / 1024).Append("KB | ");
        if (k[(int)PerfCounters.K.NavMeshUpdate] > 0) sb.Append("NAVMESH-REBUILD | ");
        if (k[(int)PerfCounters.K.TerrainRebuild] > 0) sb.Append("terrain-chunks x").Append(k[(int)PerfCounters.K.TerrainRebuild]).Append(" | ");
        if (k[(int)PerfCounters.K.CarveChange] > 0) sb.Append("carve-change x").Append(k[(int)PerfCounters.K.CarveChange]).Append(" | ");
        if (vfxMs > 2.0) sb.Append("vfx ").Append(vfxMs.ToString("0.0", c)).Append("ms x").Append(k[(int)PerfCounters.K.VfxSpawn]).Append(" | ");
        if (evalMs > 2.0) sb.Append("ai-eval ").Append(evalMs.ToString("0.0", c)).Append("ms x").Append(k[(int)PerfCounters.K.Eval]).Append(" | ");
        if (execMs > 2.0) sb.Append("ai-exec ").Append(execMs.ToString("0.0", c)).Append("ms | ");
        if (k[(int)PerfCounters.K.UnitSpawn] > 0) sb.Append("spawn x").Append(k[(int)PerfCounters.K.UnitSpawn]).Append(" | ");
        if (k[(int)PerfCounters.K.SetDestOk] > 8) sb.Append("setDest x").Append(k[(int)PerfCounters.K.SetDestOk]).Append(" | ");
        if (blocked > 0) sb.Append("blocked ").Append(blocked).Append(" | ");
        if (pathPend > 0) sb.Append("pathPending ").Append(pathPend).Append(" | ");
        sb.Append("mainMs=").Append(Ms(recMainThread).ToString("0.0", c))
          .Append(" behavMs=").Append(Ms(recBehaviourUpdate).ToString("0.0", c))
          .Append(" renderMs=").Append(Ms(recCameraRender).ToString("0.0", c));

        WriteLine(sb);
    }

    void WriteSummary()
    {
        if (summaryFrames == 0) return;
        var c = CultureInfo.InvariantCulture;

        float median = Percentile(0.5f);
        float p99 = Percentile(0.99f);

        sb.Length = 0;
        sb.Append("S,").Append(Time.frameCount).Append(',')
          .Append(Time.unscaledTime.ToString("0.0", c))
          .Append(",frames=").Append(summaryFrames)
          .Append(",avgMs=").Append((summaryMsSum / summaryFrames).ToString("0.00", c))
          .Append(",medianMs=").Append(median.ToString("0.00", c))
          .Append(",p99Ms=").Append(p99.ToString("0.00", c))
          .Append(",maxMs=").Append(summaryMsMax.ToString("0.00", c))
          .Append(",spikes=").Append(summarySpikes)
          .Append(",gcKB=").Append(summaryGcBytes / 1024)
          .Append(",units=").Append(Worker.ActiveList.Count + Warrior.ActiveList.Count + Enemy.ActiveList.Count);
        WriteLine(sb);
    }

    float Percentile(float p)
    {
        if (ringCount == 0) return 0f;
        Array.Copy(msRing, sortScratch, ringCount);
        Array.Sort(sortScratch, 0, ringCount);
        int idx = Mathf.Clamp(Mathf.RoundToInt(p * (ringCount - 1)), 0, ringCount - 1);
        return sortScratch[idx];
    }

    /// <summary>Write a one-off annotated event line. Safe to call from any system.</summary>
    public static void Event(string tag, string detail)
    {
        if (instance == null || instance.writer == null) return;
        instance.writer.WriteLine("E," + Time.frameCount + ","
            + Time.unscaledTime.ToString("0.000", CultureInfo.InvariantCulture) + "," + tag + "," + detail);
        instance.writer.Flush();
    }

    void OnApplicationQuit() { Close(); }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        Close();
        recGcAlloc.Dispose();
        recMainThread.Dispose();
        recBehaviourUpdate.Dispose();
        recCameraRender.Dispose();
        recPhysics.Dispose();
        recDrawCalls.Dispose();
        recSetPass.Dispose();
    }

    void Close()
    {
        if (writer == null) return;
        writer.WriteLine("#");
        writer.WriteLine("# session end: frames over " + SpikeMs + "ms = " + spikeCount
                         + ", worst = " + worstMs.ToString("0.0", CultureInfo.InvariantCulture)
                         + "ms at frame " + worstFrame);
        writer.Flush();
        writer.Dispose();
        writer = null;
    }
}
#endif
