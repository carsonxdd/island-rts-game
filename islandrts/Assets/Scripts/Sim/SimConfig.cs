#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One simulated game. Everything the harness is allowed to vary between runs
/// lives here, so a sweep is "a list of these" and nothing else.
///
/// Serialized with <see cref="JsonUtility"/>, which means: public fields only,
/// no properties, no nullable types, no dictionaries. Numeric overrides use a
/// sentinel of -1 for "leave the scene/prefab value alone" rather than 0, since
/// 0 is a legal value for most of them.
/// </summary>
[System.Serializable]
public class SimConfig
{
    /// <summary>Row label in the CSV. Auto-filled from index if left blank.</summary>
    public string id = "";

    /// <summary>Turtle | Rush | Eco — see <see cref="SimPolicy.Create"/>.</summary>
    public string strategy = "Eco";

    /// <summary>Seeds UnityEngine.Random for the whole run (spawn jitter, AI stagger, ORCA priorities).</summary>
    public int seed = 1;

    /// <summary>Island seed. -1 keeps the scene's TerrainGrid seed (fixed-island T1 behaviour).</summary>
    public int terrainSeed = -1;

    /// <summary>Run ends in a win if this many nights are survived.</summary>
    public int nightsToSurvive = 5;

    /// <summary>Hard wall-clock-independent stop: abandon the run after this much game time.</summary>
    public float maxGameSeconds = 2400f;

    // --- Balance knobs (-1 = don't override) ------------------------------

    [Header("Economy")]
    public int startingWood = -1;
    public int startingFood = -1;
    public int startingStone = -1;
    public float workerGatherRate = -1f;      // ResourceNode gather units/sec
    public int workerCarryCapacity = -1;

    [Header("Enemies")]
    public int baseEnemiesPerNight = -1;
    public float enemyIncreasePerNight = -1f;
    public float enemyHealth = -1f;
    public float enemyDamage = -1f;
    public float enemyMoveSpeed = -1f;
    public float enemyAttackCooldown = -1f;
    /// <summary>How far an enemy will divert to fight a warrior instead of pushing on to buildings.</summary>
    public float enemyWarriorDetectionRange = -1f;

    [Header("Wave shape")]
    /// <summary>
    /// Seconds between individual enemy spawns. The shipping 1.0 trickles a
    /// 13-enemy wave in over 13s, letting warriors defeat it in detail; a low
    /// value makes the wave arrive as one body.
    /// </summary>
    public float spawnInterval = -1f;
    public float spawnDelay = -1f;
    /// <summary>Ring radius enemies spawn on. Larger = more of the night spent commuting.</summary>
    public float spawnDistance = -1f;

    [Header("Warriors")]
    public float warriorHealth = -1f;
    public float warriorDamage = -1f;
    public float warriorMoveSpeed = -1f;
    public float warriorAttackCooldown = -1f;
    public int warriorCostWood = -1;
    public int warriorCostFood = -1;
    public int maxWarriors = -1;
    /// <summary>
    /// How far a warrior ranges to intercept. The shipping 50 means fights
    /// happen out in the field, which is why the campfire never takes damage.
    /// </summary>
    public float warriorSearchRadius = -1f;
    public float warriorPatrolRadius = -1f;
    /// <summary>
    /// HP/sec warriors regain at the campfire between waves. The shipping 5
    /// fully resets them every night, making cross-night attrition impossible.
    /// </summary>
    public float warriorHealRate = -1f;

    [Header("Buildings")]
    /// <summary>Hut HP. At 100 vs 6.67 enemy DPS a wave erases every hut in seconds.</summary>
    public float hutHealth = -1f;
    public float campfireHealth = -1f;
    public float watchtowerHealth = -1f;
    public float watchtowerDamageMultiplier = -1f;
    public float watchtowerBuffRadius = -1f;

    [Header("Day/Night")]
    public float dayLengthSeconds = -1f;
    public float nightLengthSeconds = -1f;

    public string Label(int index)
    {
        return string.IsNullOrEmpty(id) ? $"run{index:D4}" : id;
    }
}

/// <summary>
/// A sweep job file: the list of runs plus process-wide options.
/// </summary>
[System.Serializable]
public class SimSweep
{
    /// <summary>Directory for runs.csv / nights.csv. Relative paths resolve against the project root.</summary>
    public string outputDir = "SimLogs";

    /// <summary>
    /// Fixed game-time step per frame. 1/60 keeps the frame-based AI eval and
    /// NavMesh throttles behaving exactly as they do at 60fps, while letting the
    /// loop run at whatever wall-clock speed the CPU allows. See SIMULATION.md.
    /// </summary>
    public float captureDeltaTime = 1f / 60f;

    /// <summary>Repeat the whole config list this many times, incrementing seeds.</summary>
    public int repeats = 1;

    /// <summary>
    /// Real-seconds failsafe per run. The per-run <c>maxGameSeconds</c> stop is
    /// measured in GAME time, so anything that freezes the game clock (a stray
    /// Time.timeScale = 0, a paused DayNightCycle) would hang a sweep forever
    /// without this. A run at ~25x realtime needs well under a minute, so the
    /// default is loose enough never to fire on a healthy run.
    /// </summary>
    public float maxWallSecondsPerRun = 300f;

    public List<SimConfig> runs = new List<SimConfig>();

    public static SimSweep Parse(string json)
    {
        SimSweep sweep = JsonUtility.FromJson<SimSweep>(json);
        if (sweep == null || sweep.runs == null || sweep.runs.Count == 0) return null;
        if (sweep.repeats < 1) sweep.repeats = 1;
        if (sweep.captureDeltaTime <= 0f) sweep.captureDeltaTime = 1f / 60f;
        if (sweep.maxWallSecondsPerRun <= 0f) sweep.maxWallSecondsPerRun = 300f;
        return sweep;
    }

    /// <summary>Flattens repeats into a concrete run list, seeds offset per repeat.</summary>
    public List<SimConfig> Expand()
    {
        List<SimConfig> all = new List<SimConfig>(runs.Count * repeats);
        for (int r = 0; r < repeats; r++)
        {
            for (int i = 0; i < runs.Count; i++)
            {
                SimConfig src = runs[i];
                SimConfig copy = JsonUtility.FromJson<SimConfig>(JsonUtility.ToJson(src));
                copy.seed = src.seed + r;
                copy.id = repeats > 1
                    ? $"{src.Label(i)}_s{copy.seed}"
                    : src.Label(i);
                all.Add(copy);
            }
        }
        return all;
    }
}
#endif
