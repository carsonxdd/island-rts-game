using UnityEngine;

/// <summary>
/// The game-rule settings a player picks before a run: how often raids come and
/// how big they are, how hard raiders hit, how long the nights are, what the
/// colony starts with, and how many days until rescue.
///
/// Locked for the duration of a run, on purpose. Waves already spawned would not
/// retroactively change if this were live-editable, and "survived five nights"
/// stops meaning anything if the player can soften night four from the pause
/// menu. So <see cref="Active"/> is a snapshot taken when NEW GAME is pressed;
/// the pause menu shows it read-only.
///
/// A static snapshot is what carries it across the scene load — the same reason
/// the sim harness's <c>SimOverrides.Active</c> is a static. There is no
/// DontDestroyOnLoad object involved, so nothing stale can survive a restart
/// except the value the player deliberately chose.
///
/// Every knob is a multiplier applied at the point of effect (the pattern
/// CraftedUpgrades and GameSettings use), never a push into scene objects.
/// </summary>
public static class Difficulty
{
    public enum Level { Peaceful, Relaxed, Normal, Hard, Brutal, Custom }

    /// <summary>
    /// One difficulty's rules. A class rather than a struct because the Custom
    /// preset is edited in place by the options screen.
    /// </summary>
    public class Preset
    {
        public string name;
        public string blurb;

        public float enemyCount = 1f;        // multiplies the raid size
        public float raidFrequency = 1f;     // multiplies the nightly raid chance (RaidDirector)
        public float enemyHealth = 1f;
        public float enemyDamage = 1f;
        public float nightLength = 1f;       // longer night = more time under attack
        public float startingResources = 1f;
        public int daysToSurvive = 30;       // the rescue ship arrives at the dawn after this day

        public Preset Clone()
        {
            return new Preset
            {
                name = name, blurb = blurb,
                enemyCount = enemyCount, raidFrequency = raidFrequency,
                enemyHealth = enemyHealth, enemyDamage = enemyDamage,
                nightLength = nightLength, startingResources = startingResources,
                daysToSurvive = daysToSurvive,
            };
        }
    }

    // ---- presets ----------------------------------------------------------
    //
    // Tuned against the balance harness's Eco baseline rather than guessed:
    // Normal is exactly the shipped numbers (all 1.0), and each step moves
    // raid size hardest because campfire_hp_min in the sim runs is far more
    // sensitive to how many enemies arrive at once than to what each one hits
    // for. Peaceful is deliberately winnable while ignoring defence entirely.
    //
    // Length is NOT a difficulty lever (2026-09-02): a 50-day Brutal run would
    // be a chore, not a challenge. Hard and Brutal keep the 30-day calendar and
    // raid more often instead; the gentle presets are shorter because a player
    // who picked them wants a lighter evening.

    private static readonly Preset[] Presets =
    {
        new Preset
        {
            name = "Peaceful", daysToSurvive = 20,
            blurb = "For building and exploring. Raids are rare and token, and the colony starts flush.",
            enemyCount = 0.5f, raidFrequency = 0.6f, enemyHealth = 0.7f, enemyDamage = 0.55f,
            nightLength = 0.85f, startingResources = 1.5f,
        },
        new Preset
        {
            name = "Relaxed", daysToSurvive = 20,
            blurb = "A forgiving run. Mistakes cost you a hut, not the colony.",
            enemyCount = 0.75f, raidFrequency = 0.8f, enemyHealth = 0.85f, enemyDamage = 0.8f,
            nightLength = 0.95f, startingResources = 1.25f,
        },
        new Preset
        {
            name = "Normal", daysToSurvive = 30,
            blurb = "The intended balance. Thirty days to rescue, no handicaps either way.",
        },
        new Preset
        {
            name = "Hard", daysToSurvive = 30,
            blurb = "Bigger raids, more of them, and tighter resources.",
            enemyCount = 1.3f, raidFrequency = 1.25f, enemyHealth = 1.15f, enemyDamage = 1.2f,
            nightLength = 1.1f, startingResources = 0.8f,
        },
        new Preset
        {
            name = "Brutal", daysToSurvive = 30,
            blurb = "Walls are not optional. Raids come most nights, and the late ones are swarms.",
            enemyCount = 1.7f, raidFrequency = 1.5f, enemyHealth = 1.35f, enemyDamage = 1.45f,
            nightLength = 1.25f, startingResources = 0.6f,
        },
    };

    /// <summary>The Custom preset's knobs, persisted so a player's own rules survive a restart.</summary>
    public static readonly Preset CustomPreset = new Preset
    {
        name = "Custom",
        blurb = "Your own rules. Everything below is yours to set.",
    };

    public static Preset Get(Level level)
    {
        if (level == Level.Custom) return CustomPreset;
        int i = Mathf.Clamp((int)level, 0, Presets.Length - 1);
        return Presets[i];
    }

    public static string[] LevelNames
    {
        get
        {
            string[] names = new string[Presets.Length + 1];
            for (int i = 0; i < Presets.Length; i++) names[i] = Presets[i].name;
            names[Presets.Length] = CustomPreset.name;
            return names;
        }
    }

    // ---- the active run ---------------------------------------------------

    /// <summary>What the player picked on the menu. Persisted; also the default for the next run.</summary>
    public static Level Selected = Level.Normal;

    private static Preset activeRun;

    /// <summary>
    /// The rules in force right now.
    ///
    /// Falls back to the selected preset when no run has been started through
    /// the menu — that covers pressing Play straight into MainIsland from the
    /// editor, which is how most testing actually happens.
    ///
    /// A balance sweep always reads Normal: the harness varies its own knobs
    /// through SimConfig, and a difficulty saved in the developer's PlayerPrefs
    /// silently skewing every simulated run is exactly the kind of invisible
    /// variable the harness exists to eliminate.
    /// </summary>
    public static Preset Active
    {
        get
        {
            if (SimHooks.Simulating) return Get(Level.Normal);
            return activeRun ?? Get(Selected);
        }
    }

    /// <summary>Freezes the current selection as the run's rules. Called when a new game starts.</summary>
    public static void BeginRun()
    {
        activeRun = Get(Selected).Clone();
    }

    /// <summary>Name to show in the pause menu's status line.</summary>
    public static string ActiveName => Active.name;

    // ---- convenience accessors (point-of-effect reads) ---------------------

    public static float EnemyCountMultiplier => Active.enemyCount;
    public static float RaidFrequencyMultiplier => Active.raidFrequency;
    public static float EnemyHealthMultiplier => Active.enemyHealth;
    public static float EnemyDamageMultiplier => Active.enemyDamage;
    public static float NightLengthMultiplier => Active.nightLength;
    public static float StartingResourceMultiplier => Active.startingResources;
    public static int DaysToSurvive => Mathf.Max(1, Active.daysToSurvive);

    // ---- persistence ------------------------------------------------------
    //
    // Lives here rather than in GameSettings because these are run rules, not
    // preferences: GameSettings.ResetToDefaults must not silently re-roll the
    // difficulty of a game in progress.

    private const string KeyLevel = "diff.level";
    private const string KeyPrefix = "diff.custom.";

    public static void Load()
    {
        Selected = (Level)PlayerPrefs.GetInt(KeyLevel, (int)Level.Normal);
        if (Selected < Level.Peaceful || Selected > Level.Custom) Selected = Level.Normal;

        CustomPreset.enemyCount = PlayerPrefs.GetFloat(KeyPrefix + "count", 1f);
        CustomPreset.raidFrequency = PlayerPrefs.GetFloat(KeyPrefix + "raids", 1f);
        CustomPreset.enemyHealth = PlayerPrefs.GetFloat(KeyPrefix + "hp", 1f);
        CustomPreset.enemyDamage = PlayerPrefs.GetFloat(KeyPrefix + "dmg", 1f);
        CustomPreset.nightLength = PlayerPrefs.GetFloat(KeyPrefix + "night", 1f);
        CustomPreset.startingResources = PlayerPrefs.GetFloat(KeyPrefix + "res", 1f);
        // New key ("days"), not the old "nights": a Custom preset saved under the
        // wave rules would otherwise come back as a 5-day run.
        CustomPreset.daysToSurvive = PlayerPrefs.GetInt(KeyPrefix + "days", 30);
    }

    public static void Save()
    {
        PlayerPrefs.SetInt(KeyLevel, (int)Selected);

        PlayerPrefs.SetFloat(KeyPrefix + "count", CustomPreset.enemyCount);
        PlayerPrefs.SetFloat(KeyPrefix + "raids", CustomPreset.raidFrequency);
        PlayerPrefs.SetFloat(KeyPrefix + "hp", CustomPreset.enemyHealth);
        PlayerPrefs.SetFloat(KeyPrefix + "dmg", CustomPreset.enemyDamage);
        PlayerPrefs.SetFloat(KeyPrefix + "night", CustomPreset.nightLength);
        PlayerPrefs.SetFloat(KeyPrefix + "res", CustomPreset.startingResources);
        PlayerPrefs.SetInt(KeyPrefix + "days", CustomPreset.daysToSurvive);

        PlayerPrefs.Save();
    }
}
