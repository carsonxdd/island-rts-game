#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.IO;
using UnityEngine;

/// <summary>
/// A one-line heartbeat file per sim process, so the launcher can show a live
/// dashboard while six windows play.
///
/// Neither CSV can do this job. `runs.csv` and `days.csv` are both appended at
/// the END of a run (SimMetrics.Append), which is exactly the moment the
/// dashboard no longer needs them — during the twenty minutes a run takes, both
/// files say nothing at all. So a visual run overwrites `status.csv` in its own
/// output directory once a second with the state the policy just acted on.
///
/// Both modes since 2026-09-11: the overnight batch shows the same dashboard
/// for a headless sweep. SimRunner throttles the write to one per REAL second
/// (a headless process runs 15-30 game seconds a second), so eight processes
/// cost eight small writes a second between them.
/// </summary>
public static class SimStatus
{
    public const string File = "status.csv";

    private const string Header =
        "run_id,strategy,seed,run_index,run_count,day,days_to_survive,raid_tonight," +
        "colonists,workers,warriors,enemies,wood,food,stone,metal,fire_pct,hunger,outcome," +
        // The first rival (2026-09-16), appended at the END: the dashboard reads
        // columns by name, so an older status.csv still parses. `rival` is its
        // personality, empty until one lands.
        "rival,rival_opinion,rival_attitude,rival_warriors,rival_fire_pct,rival_party\n";

    /// <summary>
    /// Overwrite this process's status line. <paramref name="outcome"/> is empty
    /// while a run is in progress and carries the result once it has one.
    /// </summary>
    public static void Write(string dir, SimVisualOverlay.Frame f, string outcome)
    {
        if (string.IsNullOrEmpty(dir)) return;

        string body = string.Join(",", new[]
        {
            Escape(f.runId),
            Escape(f.strategy),
            f.seed.ToString(),
            f.runIndex.ToString(),
            f.runCount.ToString(),
            f.day.ToString(),
            f.daysToSurvive.ToString(),
            f.raidTonight ? "1" : "0",
            f.colonists.ToString(),
            f.workers.ToString(),
            f.warriors.ToString(),
            f.enemies.ToString(),
            Mathf.RoundToInt(f.wood).ToString(),
            Mathf.RoundToInt(f.food).ToString(),
            Mathf.RoundToInt(f.stone).ToString(),
            Mathf.RoundToInt(f.metal).ToString(),
            f.campfireHpMax > 0f
                ? Mathf.RoundToInt(100f * Mathf.Clamp01(f.campfireHp / f.campfireHpMax)).ToString()
                : "-1",
            f.hunger.ToString(),
            Escape(outcome),
            f.rivalLanded ? Escape(f.rivalStrategy ?? "?") : "",
            f.rivalLanded ? Escape(f.rivalOpinion) : "",
            f.rivalLanded ? Escape(f.rivalAttitude) : "",
            f.rivalLanded ? f.rivalWarriors.ToString() : "",
            f.rivalLanded ? f.rivalFirePct.ToString() : "",
            f.rivalLanded ? Escape(f.rivalParty) : "",
        });

        // Written to a sibling and swapped in, so a reader polling once a second
        // never catches a half-written line. Best effort throughout: a dashboard
        // that misses a tick is not worth failing a run over.
        string target = Path.Combine(dir, File);
        string temp = target + ".tmp";
        try
        {
            Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(temp, Header + body + "\n");
            if (System.IO.File.Exists(target)) System.IO.File.Replace(temp, target, null);
            else System.IO.File.Move(temp, target);
        }
        catch (IOException) { }
        catch (System.UnauthorizedAccessException) { }
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;
    }
}
#endif
