#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Two CSVs, because balance questions come in two shapes.
///
/// runs.csv   — one row per game. "Is Rush beatable at night 5?" "Which seeds
///              lose?" This is what you sort and filter.
/// nights.csv — one row per night per game. "Where does the economy curve cross
///              the enemy curve?" This is what you plot.
///
/// Written incrementally and flushed after every row, so a sweep that crashes on
/// run 80 of 100 still leaves 79 usable rows on disk.
/// </summary>
public class SimMetrics
{
    public const string RunsFile = "runs.csv";
    public const string NightsFile = "nights.csv";

    /// <summary>Snapshot of one night, captured at dusk and again at dawn.</summary>
    public class NightRow
    {
        public int night;
        public float wood, food, stone;              // at night start
        public float woodDawn, foodDawn, stoneDawn;  // at the following dawn
        public int workers, warriors, huts, walls, towers;
        public int enemiesSpawned;
        public int workersDawn, warriorsDawn, hutsDawn, wallsDawn, towersDawn;
        public float campfireHpStart, campfireHpMin, campfireHpDawn;
        public int enemiesKilledTotal;               // cumulative at dawn
        public bool survived;
    }

    public string configId;
    public string strategy;
    public int seed;
    public int nightsToSurvive;

    public readonly List<NightRow> nights = new List<NightRow>();

    public string outcome = "incomplete";   // victory | defeat | timeout | error
    public int nightReached;
    public float gameSeconds;
    public float wallClockSeconds;
    public int frames;
    public int totalEnemiesKilled;
    public int peakWorkers, peakWarriors;
    public float finalWood, finalFood, finalStone;
    public string note = "";

    private readonly StringBuilder sb = new StringBuilder(256);

    // ---- CSV plumbing ----------------------------------------------------

    private static string Csv(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    public static void EnsureHeaders(string dir)
    {
        Directory.CreateDirectory(dir);

        string runs = Path.Combine(dir, RunsFile);
        if (!File.Exists(runs))
        {
            File.WriteAllText(runs,
                "config_id,strategy,seed,outcome,night_reached,nights_to_survive," +
                "enemies_killed,peak_workers,peak_warriors," +
                "final_wood,final_food,final_stone," +
                "game_seconds,wall_seconds,frames,note\n");
        }

        string nights = Path.Combine(dir, NightsFile);
        if (!File.Exists(nights))
        {
            File.WriteAllText(nights,
                "config_id,strategy,seed,night,survived," +
                "wood_dusk,food_dusk,stone_dusk,wood_dawn,food_dawn,stone_dawn," +
                "workers_dusk,warriors_dusk,huts_dusk,walls_dusk,towers_dusk," +
                "workers_dawn,warriors_dawn,huts_dawn,walls_dawn,towers_dawn," +
                "enemies_spawned,enemies_killed_total," +
                "campfire_hp_dusk,campfire_hp_min,campfire_hp_dawn\n");
        }
    }

    public void Append(string dir)
    {
        EnsureHeaders(dir);

        sb.Clear();
        sb.Append(Csv(configId)).Append(',')
          .Append(Csv(strategy)).Append(',')
          .Append(seed).Append(',')
          .Append(Csv(outcome)).Append(',')
          .Append(nightReached).Append(',')
          .Append(nightsToSurvive).Append(',')
          .Append(totalEnemiesKilled).Append(',')
          .Append(peakWorkers).Append(',')
          .Append(peakWarriors).Append(',')
          .Append(F(finalWood)).Append(',')
          .Append(F(finalFood)).Append(',')
          .Append(F(finalStone)).Append(',')
          .Append(F(gameSeconds)).Append(',')
          .Append(F(wallClockSeconds)).Append(',')
          .Append(frames).Append(',')
          .Append(Csv(note)).Append('\n');
        File.AppendAllText(Path.Combine(dir, RunsFile), sb.ToString());

        sb.Clear();
        for (int i = 0; i < nights.Count; i++)
        {
            NightRow n = nights[i];
            sb.Append(Csv(configId)).Append(',')
              .Append(Csv(strategy)).Append(',')
              .Append(seed).Append(',')
              .Append(n.night).Append(',')
              .Append(n.survived ? 1 : 0).Append(',')
              .Append(F(n.wood)).Append(',').Append(F(n.food)).Append(',').Append(F(n.stone)).Append(',')
              .Append(F(n.woodDawn)).Append(',').Append(F(n.foodDawn)).Append(',').Append(F(n.stoneDawn)).Append(',')
              .Append(n.workers).Append(',').Append(n.warriors).Append(',')
              .Append(n.huts).Append(',').Append(n.walls).Append(',').Append(n.towers).Append(',')
              .Append(n.workersDawn).Append(',').Append(n.warriorsDawn).Append(',')
              .Append(n.hutsDawn).Append(',').Append(n.wallsDawn).Append(',').Append(n.towersDawn).Append(',')
              .Append(n.enemiesSpawned).Append(',')
              .Append(n.enemiesKilledTotal).Append(',')
              .Append(F(n.campfireHpStart)).Append(',')
              .Append(F(n.campfireHpMin)).Append(',')
              .Append(F(n.campfireHpDawn)).Append('\n');
        }
        if (sb.Length > 0) File.AppendAllText(Path.Combine(dir, NightsFile), sb.ToString());
    }

    /// <summary>One-line console summary, the thing you actually watch scroll past.</summary>
    public string Summary()
    {
        return $"[Sim] {configId} ({strategy}, seed {seed}) -> {outcome} " +
               $"night {nightReached}/{nightsToSurvive} | kills {totalEnemiesKilled} " +
               $"| peak {peakWorkers}w/{peakWarriors}s " +
               $"| res {finalWood:F0}/{finalFood:F0}/{finalStone:F0} " +
               $"| {gameSeconds:F0}s game in {wallClockSeconds:F1}s wall " +
               $"({(wallClockSeconds > 0.01f ? gameSeconds / wallClockSeconds : 0f):F1}x)" +
               (string.IsNullOrEmpty(note) ? "" : $" | {note}");
    }
}
#endif
