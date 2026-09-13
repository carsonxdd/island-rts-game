/// <summary>
/// Global switches the balance-simulation harness flips before a run.
///
/// TWO flags, because "the harness is driving" and "nothing is being drawn" are
/// different questions and only one of them is allowed to change a decision:
///
/// <see cref="Simulating"/> is policy. It suppresses things that would make a
/// simulated run diverge from a played one or from another simulated run — the
/// difficulty snapshot, the name popup, the end screen, dev quests, saved
/// PlayerPrefs, mouse-driven UI. Every consumer of it is a deliberate "the
/// harness decides this instead".
///
/// <see cref="Headless"/> is capability. It is true only when the process has no
/// camera and no window, and it guards things that are purely DRAWN — VFX,
/// health bars, floating state text, the fog mask upload, the occluder cutout,
/// the unit hole mask, decor scatter, the HUD. Skipping them saves real CPU in a
/// sweep and changes nothing a CSV can read.
///
/// The split exists so the VISUAL sim (SimRunner's -simvisual mode) can render a
/// run you can actually watch while taking the same decisions as the headless
/// sweep it is meant to explain. If a guard would change what the game DOES it
/// belongs on Simulating; if it only changes what the game LOOKS LIKE it belongs
/// on Headless. Nothing may read Headless to decide anything.
///
/// Always compiled (two static bools are free), but only ever set by
/// <see cref="SimRunner"/>, which is editor/dev-build only.
/// </summary>
public static class SimHooks
{
    /// <summary>True while a balance run is driving the game, headless or visual.</summary>
    public static bool Simulating;

    /// <summary>
    /// True while a balance run is driving the game AND nothing is being
    /// rendered. Read ONLY to skip drawing work, never to decide gameplay.
    /// </summary>
    public static bool Headless;

    /// <summary>True while a balance run is driving a rendered, watchable window.</summary>
    public static bool Visual => Simulating && !Headless;

    /// <summary>
    /// The run's rule-set choices under the sim (2026-09-11), as the names the
    /// menu would show: a <see cref="Difficulty.Level"/> name, an
    /// <see cref="IslandOptions.Size"/> name and an
    /// <see cref="IslandSettings.Style"/> name. Empty = the sweep's defaults
    /// (Normal, Medium, Terraced). Set by <see cref="SimRunner"/> from the
    /// run's <c>SimConfig</c> BEFORE the scene loads, because
    /// <c>ResourceManager.Awake</c> reads the difficulty and
    /// <c>TerrainGrid.Awake</c> the island; read by <see cref="Difficulty.Active"/>
    /// and <see cref="IslandOptions.Active"/> in place of the developer's saved
    /// menu choices, which a sweep must never see. Strings rather than enums so
    /// this file stays free of the sim-only types.
    /// </summary>
    public static string Difficulty = "";
    public static string IslandSize = "";
    public static string IslandStyle = "";

    /// <summary>
    /// Rival colonies the sweep asked for (2026-09-11, lap step 3). An int
    /// rather than a name because 0 is meaningful here - it is the default, and
    /// the whole point of the default is that a sweep with no rivals stays
    /// comparable with every baseline taken before they existed. Set by
    /// <see cref="SimRunner"/> BEFORE the scene loads and read by
    /// <see cref="IslandOptions.Active"/>, because the landing director asks on
    /// its first frame.
    /// </summary>
    public static int RivalCount;

    /// <summary>Case-insensitive lookup of a name in a name table; -1 when empty or unknown.</summary>
    public static int IndexOfName(string[] names, string name)
    {
        if (string.IsNullOrEmpty(name)) return -1;
        for (int i = 0; i < names.Length; i++)
            if (string.Equals(names[i], name.Trim(), System.StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
}
