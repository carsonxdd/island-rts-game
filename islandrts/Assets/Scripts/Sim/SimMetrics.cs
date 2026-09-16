#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Two CSVs, because balance questions come in two shapes.
///
/// runs.csv  — one row per game. "Is Rush beatable to day 30?" "Which seeds
///             lose?" This is what you sort and filter.
/// days.csv  — one row per calendar day per game (dusk to dawn). "Where does
///             the economy curve cross the raid curve?" This is what you plot.
///             Since raids stopped being nightly (2026-09-02) every row carries
///             a <c>raid</c> flag; <c>campfire_hp_min</c> only means anything
///             on rows where it is 1.
///
/// Written incrementally and flushed after every row, so a sweep that crashes on
/// run 80 of 100 still leaves 79 usable rows on disk.
/// </summary>
public class SimMetrics
{
    public const string RunsFile = "runs.csv";
    public const string DaysFile = "days.csv";

    /// <summary>Snapshot of one calendar day's night, captured at dusk and again at dawn.</summary>
    public class DayRow
    {
        public int day;
        public bool raid;                            // did the director land raiders this night
        public int raidSize;                         // what it committed to at the dawn roll
        public float wood, food, stone;              // at dusk
        public float woodDawn, foodDawn, stoneDawn;  // at the following dawn
        public int workers, warriors, huts, walls, towers;
        public int enemiesSpawned;
        public int workersDawn, warriorsDawn, hutsDawn, wallsDawn, towersDawn;
        public float campfireHpStart, campfireHpMin, campfireHpDawn;
        public int enemiesKilledTotal;               // cumulative at dawn
        public int hungerDawn;                       // 0 fed, 1 hungry, 2 starving (2026-09-04)
        public int leftTotal;                        // colonists who walked out, cumulative at dawn
        public int archersDawn;                      // bow-armed warriors among warriorsDawn (2026-09-04)
        // The third lab of 2026-09-10 could only INFER why a wiped colony never
        // re-armed (12 idle colonists, 0 warriors, 6,000 wood at the defeat):
        public int idleDawn;                         // jobless colonists at the fire
        public int weaponsDawn;                      // weapons in the stockpile
        public int sticksDawn, chunksDawn;           // the spear kit in the stockpile
        public string queueDawn = "";                // the campfire station's "Waiting for 2 Stick", else empty
        public int warriorsLost;                     // warriors that died between dusk and dawn
        public int ringHoles;                        // ring cells the sim builder could not wall or notch
        // Stone chunks lying on the island at dawn (2026-09-11). A colony cannot
        // MAKE a chunk without the Stone Pick that Quarrying grants, and Quarrying
        // costs three chunks, so a run that finds none is unwinnable. The lab could
        // only see the empty stockpile; this says whether the island had any.
        public int chunksLoose;
        // Rival colonies (2026-09-11, lap step 3). Zero on every run that did not
        // ask for one, which is every baseline sweep.
        public int rivalArrivalDay;   // calendar day the first rival landed; 0 = none yet
        public int rivalContact;      // 1 once the player's fog has touched a rival colony
        public int rivalOpinion;      // 0 hostile, 1 neutral, 2 allied - the Attitude, until slice B's scalar
        public int rivalWarriors;     // militia across every landed rival
        public int raidAtRival;       // 1 when tonight's raid was rolled onto a rival's shore (2026-09-16)
        public float rivalOpinionPts; // the hidden opinion with the first rival, -100..100
        public int rivalLandings;     // landings on the player's shore so far this run
        public int rivalRelief;       // relief parties that came to the player so far this run
        // The first rival's own night (2026-09-16, sim update for factions): its
        // collapse should read as its cause the way the player's does. -1 fire =
        // no rival landed yet; 0 = its fire is down.
        public int rivalFirePct = -1;    // the first rival's campfire HP at dawn, 0..100
        public int rivalHuts;            // its huts at dawn
        public int rivalColonists;       // its roster at dawn (warriors included)
        public float rivalFoodDawn;      // its food at dawn
        public bool survived;
    }

    public string configId;
    public string strategy;
    public int seed;
    public int daysToSurvive;

    public readonly List<DayRow> days = new List<DayRow>();

    public string outcome = "incomplete";   // victory | defeat | timeout | error
    public int dayReached;
    public int raids;                        // raids that landed
    public float gameSeconds;
    public float wallClockSeconds;
    public int frames;
    public int totalEnemiesKilled;
    public int peakWorkers, peakWarriors;
    public float finalWood, finalFood, finalStone;
    public int colonistsLeft;                // starved out over the run (2026-09-04)
    public string note = "";
    /// <summary>The first landed rival's governor policy name, empty on a run with none (2026-09-16).</summary>
    public string rivalStrategy = "";
    /// <summary>
    /// How the first rival colony ended (2026-09-16): <c>none</c> (never landed),
    /// <c>alive</c>, <c>fell</c> (its campfire destroyed — only raiders can, a
    /// warrior cannot hit a building) or <c>deserted</c> (fire standing, nobody
    /// left). <see cref="rivalFellDay"/> is the calendar day the fire went out, 0 otherwise.
    /// </summary>
    public string rivalFate = "none";
    public int rivalFellDay;

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
                "config_id,strategy,seed,outcome,day_reached,days_to_survive,raids," +
                "enemies_killed,peak_workers,peak_warriors," +
                "final_wood,final_food,final_stone,colonists_left," +
                "game_seconds,wall_seconds,frames,note,rival_strategy,rival_fate,rival_fell_day\n");
        }

        string days = Path.Combine(dir, DaysFile);
        if (!File.Exists(days))
        {
            File.WriteAllText(days,
                "config_id,strategy,seed,day,raid,raid_size,survived," +
                "wood_dusk,food_dusk,stone_dusk,wood_dawn,food_dawn,stone_dawn," +
                "workers_dusk,warriors_dusk,huts_dusk,walls_dusk,towers_dusk," +
                "workers_dawn,warriors_dawn,huts_dawn,walls_dawn,towers_dawn," +
                "enemies_spawned,enemies_killed_total," +
                "campfire_hp_dusk,campfire_hp_min,campfire_hp_dawn," +
                "hunger_dawn,left_total,archers_dawn," +
                "idle_dawn,weapons_dawn,sticks_dawn,chunks_dawn,queue_dawn,warriors_lost,ring_holes,chunks_loose," +
                "rival_arrival_day,rival_contact,rival_opinion,rival_warriors," +
                "raid_at_rival,rival_opinion_pts,rival_landings,rival_relief," +
                "rival_fire_pct,rival_huts,rival_colonists,rival_food_dawn\n");
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
          .Append(dayReached).Append(',')
          .Append(daysToSurvive).Append(',')
          .Append(raids).Append(',')
          .Append(totalEnemiesKilled).Append(',')
          .Append(peakWorkers).Append(',')
          .Append(peakWarriors).Append(',')
          .Append(F(finalWood)).Append(',')
          .Append(F(finalFood)).Append(',')
          .Append(F(finalStone)).Append(',')
          .Append(colonistsLeft).Append(',')
          .Append(F(gameSeconds)).Append(',')
          .Append(F(wallClockSeconds)).Append(',')
          .Append(frames).Append(',')
          .Append(Csv(note)).Append(',')
          .Append(Csv(rivalStrategy)).Append(',')
          .Append(Csv(rivalFate)).Append(',')
          .Append(rivalFellDay).Append('\n');
        File.AppendAllText(Path.Combine(dir, RunsFile), sb.ToString());

        sb.Clear();
        for (int i = 0; i < days.Count; i++)
        {
            DayRow n = days[i];
            sb.Append(Csv(configId)).Append(',')
              .Append(Csv(strategy)).Append(',')
              .Append(seed).Append(',')
              .Append(n.day).Append(',')
              .Append(n.raid ? 1 : 0).Append(',')
              .Append(n.raidSize).Append(',')
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
              .Append(F(n.campfireHpDawn)).Append(',')
              .Append(n.hungerDawn).Append(',')
              .Append(n.leftTotal).Append(',')
              .Append(n.archersDawn).Append(',')
              .Append(n.idleDawn).Append(',')
              .Append(n.weaponsDawn).Append(',')
              .Append(n.sticksDawn).Append(',')
              .Append(n.chunksDawn).Append(',')
              .Append(Csv(n.queueDawn ?? "")).Append(',')
              .Append(n.warriorsLost).Append(',')
              .Append(n.ringHoles).Append(',')
              .Append(n.chunksLoose).Append(',')
              .Append(n.rivalArrivalDay).Append(',')
              .Append(n.rivalContact).Append(',')
              .Append(n.rivalOpinion).Append(',')
              .Append(n.rivalWarriors).Append(',')
              .Append(n.raidAtRival).Append(',')
              .Append(F(n.rivalOpinionPts)).Append(',')
              .Append(n.rivalLandings).Append(',')
              .Append(n.rivalRelief).Append(',')
              .Append(n.rivalFirePct).Append(',')
              .Append(n.rivalHuts).Append(',')
              .Append(n.rivalColonists).Append(',')
              .Append(F(n.rivalFoodDawn)).Append('\n');
        }
        if (sb.Length > 0) File.AppendAllText(Path.Combine(dir, DaysFile), sb.ToString());
    }

    /// <summary>One-line console summary, the thing you actually watch scroll past.</summary>
    public string Summary()
    {
        return $"[Sim] {configId} ({strategy}, seed {seed}) -> {outcome} " +
               $"day {dayReached}/{daysToSurvive} | raids {raids} | kills {totalEnemiesKilled} " +
               $"| peak {peakWorkers}w/{peakWarriors}s " +
               $"| res {finalWood:F0}/{finalFood:F0}/{finalStone:F0} " +
               $"| {gameSeconds:F0}s game in {wallClockSeconds:F1}s wall " +
               $"({(wallClockSeconds > 0.01f ? gameSeconds / wallClockSeconds : 0f):F1}x)" +
               (rivalFate == "none" ? "" : $" | rival {rivalStrategy} {rivalFate}" + (rivalFellDay > 0 ? $" d{rivalFellDay}" : "")) +
               (string.IsNullOrEmpty(note) ? "" : $" | {note}");
    }
}
#endif
