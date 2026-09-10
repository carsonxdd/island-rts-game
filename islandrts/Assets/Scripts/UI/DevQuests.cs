using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Playtest quests (2026-09-07): the things that changed recently, as a list a
/// tester works through in-game. Read from <c>Assets/Resources/DevQuests.txt</c>;
/// shown on the HUD tracker (<see cref="DevQuestTracker"/>) and on the DEV tab
/// of the Information screen, which is also where PASS / FAIL, notes and the
/// report live. Editor and development builds only — <see cref="Enabled"/> is a
/// compile-time constant, so every <see cref="Signal"/> call in gameplay code
/// folds to nothing in a release build.
///
/// Format, newest batch first:
/// <code>
/// # a comment line (ignored)
/// ## 2026-09-07 Utility colonists        a batch: the feature under test
/// - @craft:wooden_spear Craft a spear    a quest with a trigger: ticks itself on that signal
/// - Pinned Builder ignores a bench       a quest without one: the tester ticks it
/// </code>
///
/// Triggers are plain strings raised by <see cref="Signal"/> from the point of
/// effect; the set in use is listed at the top of DevQuests.txt. A new feature
/// adds its batch there and, where a signal does not exist yet, one Signal line
/// where the thing happens. A signal is the proof (2026-09-09): it marks the
/// quest done AND passed, so the tester only judges what no signal can see.
/// Two meta signals come from this class itself: <c>auto</c> the first time any
/// quest ticks itself, <c>persisted</c> when a launch loads a ticked quest back.
/// Quests that need a state check rather than an event ("four cutters over two
/// trees") are polled by <see cref="DevQuestTracker"/> every half second while
/// they are open (<see cref="IsOpen"/>), and raise a plain signal when true.
///
/// Results persist in PlayerPrefs per quest id (batch slug + index) so a tester
/// can spread the list over several runs and launches; RESET clears them. The
/// balance sim never touches PlayerPrefs, so everything here is a no-op under it.
/// </summary>
#pragma warning disable 0162   // Enabled is a compile-time constant: the release branches are meant to be dead
public static class DevQuests
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public const bool Enabled = true;
#else
    public const bool Enabled = false;
#endif

    public enum Result { Untested, Pass, Fail }

    public sealed class Quest
    {
        public string id;
        public string text;
        /// <summary>The signal that completes this quest, or null for a manual one.</summary>
        public string trigger;
        public Batch batch;
        public bool done;
        public Result result;
        public string note = "";

        public bool IsAuto => trigger != null;
    }

    public sealed class Batch
    {
        public string title;
        public readonly List<Quest> quests = new List<Quest>();
    }

    public const string ResourceName = "DevQuests";
    private const string PrefsPrefix = "devq.";

    private static List<Batch> batches;
    private static List<Quest> all;
    private static Dictionary<string, List<Quest>> byTrigger;

    /// <summary>Fires after any quest, note, tracker toggle or reset changes state.</summary>
    public static event Action OnChanged;
    /// <summary>Fires when a signal ticks a quest — the tracker flashes it.</summary>
    public static event Action<Quest> OnAutoCompleted;

    public static IReadOnlyList<Batch> Batches { get { EnsureLoaded(); return batches; } }
    public static IReadOnlyList<Quest> All { get { EnsureLoaded(); return all; } }

    public static int TotalCount => All.Count;

    public static int DoneCount
    {
        get
        {
            int n = 0;
            IReadOnlyList<Quest> list = All;
            for (int i = 0; i < list.Count; i++) if (list[i].done) n++;
            return n;
        }
    }

    /// <summary>Whether the HUD tracker is drawn. Remembered across launches.</summary>
    public static bool ShowTracker
    {
        get => Enabled && PlayerPrefs.GetInt(PrefsPrefix + "tracker", 1) == 1;
        set
        {
            if (!Enabled || SimHooks.Simulating) return;
            PlayerPrefs.SetInt(PrefsPrefix + "tracker", value ? 1 : 0);
            OnChanged?.Invoke();
        }
    }

    /// <summary>Free-text notes for the whole report.</summary>
    public static string Notes
    {
        get => Enabled ? PlayerPrefs.GetString(PrefsPrefix + "notes", "") : "";
        set
        {
            if (!Enabled || SimHooks.Simulating) return;
            PlayerPrefs.SetString(PrefsPrefix + "notes", value ?? "");
        }
    }

    /// <summary>Drops the cache so the next read re-parses the asset (editor iteration).</summary>
    public static void Reload() => batches = null;

    // ---- loading -----------------------------------------------------------

    private static void EnsureLoaded()
    {
        if (batches != null) return;
        batches = new List<Batch>();
        all = new List<Quest>();
        byTrigger = new Dictionary<string, List<Quest>>();
        if (!Enabled) return;

        TextAsset asset = Resources.Load<TextAsset>(ResourceName);
        if (asset == null)
        {
            // Recoverable misconfiguration: the DEV tab shows "No quests".
            Debug.LogWarning("DevQuests: Resources/" + ResourceName + ".txt not found.");
            return;
        }
        Parse(asset.text);
        bool anyLoaded = LoadState();
        if (anyLoaded) Signal("persisted");   // a ticked quest came back from PlayerPrefs (the "survives a fresh launch" quest)
    }

    private static void Parse(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        string[] lines = text.Split('\n');
        Batch current = null;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("## "))
            {
                current = new Batch { title = line.Substring(3).Trim() };
                batches.Add(current);
                continue;
            }
            if (line.StartsWith("#")) continue;
            if (current == null) continue;
            if (!line.StartsWith("- ") && !line.StartsWith("* ")) continue;

            string body = line.Substring(2).Trim();
            string trigger = null;
            if (body.StartsWith("@"))
            {
                int sp = body.IndexOf(' ');
                if (sp < 0) continue;                              // a trigger with no text
                trigger = body.Substring(1, sp - 1).ToLowerInvariant();
                body = body.Substring(sp + 1).Trim();
            }
            if (body.Length == 0) continue;

            Quest q = new Quest
            {
                id = Slug(current.title) + "/" + current.quests.Count,
                text = body,
                trigger = trigger,
                batch = current,
            };
            current.quests.Add(q);
            all.Add(q);
            if (trigger != null)
            {
                if (!byTrigger.TryGetValue(trigger, out List<Quest> list))
                {
                    list = new List<Quest>();
                    byTrigger[trigger] = list;
                }
                list.Add(q);
            }
        }
    }

    private static string Slug(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = char.ToLowerInvariant(s[i]);
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        }
        return sb.ToString();
    }

    /// <summary>Reads every quest's saved state; true when at least one came back done.</summary>
    private static bool LoadState()
    {
        if (SimHooks.Simulating) return false;
        bool any = false;
        for (int i = 0; i < all.Count; i++)
        {
            Quest q = all[i];
            q.done = PlayerPrefs.GetInt(PrefsPrefix + q.id + ".done", 0) == 1;
            q.result = (Result)PlayerPrefs.GetInt(PrefsPrefix + q.id + ".result", 0);
            q.note = PlayerPrefs.GetString(PrefsPrefix + q.id + ".note", "");
            any |= q.done;
        }
        return any;
    }

    private static void Save(Quest q)
    {
        if (SimHooks.Simulating) return;
        PlayerPrefs.SetInt(PrefsPrefix + q.id + ".done", q.done ? 1 : 0);
        PlayerPrefs.SetInt(PrefsPrefix + q.id + ".result", (int)q.result);
        PlayerPrefs.SetString(PrefsPrefix + q.id + ".note", q.note ?? "");
    }

    // ---- state changes -----------------------------------------------------

    /// <summary>
    /// Something happened in the game. Ticks every open quest waiting on
    /// <paramref name="key"/> and marks it PASS (the signal is the proof; a
    /// tester who saw it go wrong flips the verdict on the DEV tab). Cheap: one
    /// dictionary lookup, nothing when the key has no quest, and a constant
    /// false in a release build.
    /// </summary>
    public static void Signal(string key)
    {
        if (!Enabled || SimHooks.Simulating || string.IsNullOrEmpty(key)) return;
        EnsureLoaded();
        if (!byTrigger.TryGetValue(key, out List<Quest> list)) return;

        bool any = false;
        for (int i = 0; i < list.Count; i++)
        {
            Quest q = list[i];
            if (q.done) continue;
            q.done = true;
            if (q.result == Result.Untested) q.result = Result.Pass;
            Save(q);
            any = true;
            OnAutoCompleted?.Invoke(q);
        }
        if (!any) return;
        OnChanged?.Invoke();
        if (key != "auto") Signal("auto");   // the Dev quests batch's own "an auto quest ticks itself"
    }

    /// <summary>
    /// True while some quest waiting on <paramref name="key"/> is still open —
    /// the watcher's gate, so a state check costs nothing once its quest is done.
    /// </summary>
    public static bool IsOpen(string key)
    {
        if (!Enabled || SimHooks.Simulating) return false;
        EnsureLoaded();
        if (!byTrigger.TryGetValue(key, out List<Quest> list)) return false;
        for (int i = 0; i < list.Count; i++)
            if (!list[i].done) return true;
        return false;
    }

    public static void SetDone(Quest q, bool done)
    {
        if (q == null || q.done == done) return;
        q.done = done;
        Save(q);
        OnChanged?.Invoke();
    }

    public static void SetResult(Quest q, Result r)
    {
        if (q == null || q.result == r) return;
        q.result = r;
        if (r != Result.Untested) q.done = true;   // a verdict implies it was looked at
        Save(q);
        OnChanged?.Invoke();
    }

    public static void SetNote(Quest q, string note)
    {
        if (q == null) return;
        q.note = note ?? "";
        Save(q);
    }

    /// <summary>Clears every result, note and the report notes.</summary>
    public static void Reset()
    {
        EnsureLoaded();
        for (int i = 0; i < all.Count; i++)
        {
            Quest q = all[i];
            q.done = false;
            q.result = Result.Untested;
            q.note = "";
            Save(q);
        }
        Notes = "";
        OnChanged?.Invoke();
    }

    /// <summary>The first <paramref name="max"/> open quests in file order, for the tracker.</summary>
    public static void NextOpen(List<Quest> into, int max)
    {
        into.Clear();
        IReadOnlyList<Quest> list = All;
        for (int i = 0; i < list.Count && into.Count < max; i++)
            if (!list[i].done) into.Add(list[i]);
    }

    // ---- the report --------------------------------------------------------

    /// <summary>The markdown report: build, run, every quest with its verdict and note, the notes.</summary>
    public static string Report()
    {
        EnsureLoaded();
        var sb = new StringBuilder();
        DateTime now = DateTime.Now;

        sb.Append("# Playtest report — ").Append(now.ToString("yyyy-MM-dd HH:mm")).Append('\n').Append('\n');

        string latest = Changelog.LatestDate ?? "unknown";
#if UNITY_EDITOR
        string kind = "editor";
#else
        string kind = "development build";
#endif
        sb.Append("**Build:** v").Append(Application.version).Append(" · changelog ").Append(latest).Append(" · Unity ").Append(Application.unityVersion)
          .Append(" · ").Append(kind).Append(" · ").Append(Application.platform).Append('\n').Append('\n');

        AppendRun(sb);

        int pass = 0, fail = 0;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].result == Result.Pass) pass++;
            else if (all[i].result == Result.Fail) fail++;
        }
        sb.Append("**Quests:** ").Append(DoneCount).Append(" / ").Append(all.Count).Append(" done · ")
          .Append(pass).Append(" pass · ").Append(fail).Append(" fail\n\n");

        for (int b = 0; b < batches.Count; b++)
        {
            Batch batch = batches[b];
            sb.Append("## ").Append(batch.title).Append('\n');
            for (int i = 0; i < batch.quests.Count; i++)
            {
                Quest q = batch.quests[i];
                sb.Append("- [").Append(q.done ? 'x' : ' ').Append("] ");
                sb.Append(q.result == Result.Pass ? "PASS  " : q.result == Result.Fail ? "FAIL  " : "----  ");
                sb.Append(q.text);
                if (q.IsAuto) sb.Append(" _(auto)_");
                if (!string.IsNullOrEmpty(q.note)) sb.Append(" — ").Append(q.note);
                sb.Append('\n');
            }
            sb.Append('\n');
        }

        sb.Append("## Notes\n");
        string notes = Notes;
        sb.Append(string.IsNullOrEmpty(notes) ? "(none)" : notes).Append('\n');
        return sb.ToString();
    }

    private static void AppendRun(StringBuilder sb)
    {
        GameManager gm = GameManager.Instance;
        if (gm == null)
        {
            sb.Append("**Run:** not in a game\n\n");
            return;
        }

        DayNightCycle dn = UnityEngine.Object.FindAnyObjectByType<DayNightCycle>();
        sb.Append("**Run:** ").Append(Difficulty.ActiveName).Append(" · ").Append(IslandOptions.ActiveName).Append(" island");
        if (TerrainGrid.Instance != null) sb.Append(" · seed ").Append(TerrainGrid.Instance.seed);
        if (dn != null) sb.Append(" · day ").Append(dn.GetCurrentDay()).Append(dn.IsNightTime() ? " (night)" : " (day)");

        string outcome = !gm.isGameOver ? "in progress" : gm.isEscape ? "ESCAPED" : gm.isVictory ? "VICTORY" : "DEFEAT";
        sb.Append(" · ").Append(outcome);

        Population pm = Factions.Player.Population;
        BaseBuilding fire = Factions.Player.Campfire;
        if (pm != null)
        {
            sb.Append(" · ").Append(pm.GetColonistCount()).Append(" colonists (").Append(pm.GetIdleCount()).Append(" idle)");
            if (fire != null) sb.Append(" · ").Append(fire.GetWarriorCount()).Append(" warriors");
            sb.Append(" · ").Append(pm.Hunger);
        }
        ResourcePool rm = Factions.Player.Resources;
        {
            sb.Append(" · ").Append(rm.GetWood()).Append("W ").Append(rm.GetFood()).Append("F ")
              .Append(rm.GetStone()).Append("S ").Append(rm.GetMetal()).Append('M');
        }
        RaidDirector rd = RaidDirector.Instance;
        if (rd != null) sb.Append(" · ").Append(rd.RaidsSoFar).Append(" raids");
        sb.Append('\n').Append('\n');
    }

    /// <summary>
    /// Where reports are written: <c>Playtests/</c> at the repo root from the
    /// editor, beside the executable from a build. Created on first use.
    /// </summary>
    public static string ReportDirectory()
    {
#if UNITY_EDITOR
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Playtests"));
#else
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Playtests"));
#endif
    }

    /// <summary>
    /// Builds the report, copies it to the clipboard and writes it to disk.
    /// Returns the file path, or null when the write failed (the clipboard copy
    /// still happened).
    /// </summary>
    public static string Submit()
    {
        string report = Report();
        GUIUtility.systemCopyBuffer = report;
        Signal("report");
        try
        {
            string dir = ReportDirectory();
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "playtest_" + DateTime.Now.ToString("yyyy-MM-dd_HHmm") + ".md");
            File.WriteAllText(path, report, new UTF8Encoding(false));
            return path;
        }
        catch (Exception e)
        {
            Debug.LogWarning("DevQuests: could not write the report — " + e.Message);
            return null;
        }
    }
}
