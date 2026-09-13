using UnityEngine;

/// <summary>
/// The world choices a player makes on the New Game screen: island size,
/// terrain style, and an optional seed. Same shape as <see cref="Difficulty"/>:
/// a persisted <see cref="Selected"/> set that the menu edits, and a
/// <see cref="Snapshot"/> frozen by <see cref="BeginRun"/> so nothing the
/// player changes on the menu afterwards can leak into a run in progress.
///
/// <see cref="Active"/> falls back to the selection when no run was started
/// through the menu (pressing Play straight into MainIsland), and to the
/// standard island under the balance sim, where a developer's saved menu
/// choices must never skew a sweep.
/// </summary>
public static class IslandOptions
{
    public enum Size { Small, Medium, Large }
    public static readonly string[] SizeNames = { "Small", "Medium", "Large" };
    public static readonly string[] SizeBlurbs =
    {
        "A tight 110 m island. Short walks, little room — every wall counts.",
        "The standard 150 m island.",
        "A sprawling 190 m island. Longer supply lines, more to defend, more to find.",
    };

    /// <summary>Island vertices per side (1 m spacing) for each size. The map adds TerrainGrid.OceanMargin of sea on every side.</summary>
    public static int VertsFor(Size size)
    {
        switch (size)
        {
            case Size.Small: return 111;
            case Size.Large: return 191;
            default: return 151;
        }
    }

    /// <summary>How many rival colonies may wash up during the run (2026-09-11). 0 is the default.</summary>
    public const int MaxRivals = 2;

    public static readonly string[] RivalCountNames = { "None", "One", "Two" };
    public static readonly string[] RivalCountBlurbs =
    {
        "You have the island to yourself. Only the night raiders come.",
        "A second ship breaks up on a far shore about a third of the way through. They gather their own patch and can be talked to or fought.",
        "Two more colonies, landing a few days apart on shores of their own. A crowded island.",
    };

    public struct Snapshot
    {
        public Size size;
        public IslandSettings.Style style;
        /// <summary>0 = random.</summary>
        public int seed;
        /// <summary>Rival colonies scheduled to land this run; 0 = none, the default.</summary>
        public int rivalCount;
    }

    // ---- selection (what the menu edits) ---------------------------------

    public static Size SelectedSize = Size.Medium;
    public static IslandSettings.Style SelectedStyle = IslandSettings.Style.Terraced;
    /// <summary>The seed field as typed; empty or non-numeric = random.</summary>
    public static string SelectedSeedText = "";
    /// <summary>Rival colonies, 0..<see cref="MaxRivals"/>. Off by default so existing balance and the sweep baselines stay comparable.</summary>
    public static int SelectedRivalCount = 0;

    public static int SelectedSeed
    {
        get
        {
            int v;
            if (int.TryParse(SelectedSeedText.Trim(), out v)) return v == 0 ? 1 : Mathf.Abs(v);
            // A word is fine too — hash it so "sunrise" is a repeatable island
            string t = SelectedSeedText.Trim();
            if (t.Length == 0) return 0;
            unchecked
            {
                int h = 23;
                for (int i = 0; i < t.Length; i++) h = h * 31 + t[i];
                h &= 0x7FFFFFFF;
                return h == 0 ? 1 : h;
            }
        }
    }

    // ---- the active run ---------------------------------------------------

    private static Snapshot? activeRun;

    public static Snapshot Active
    {
        get
        {
            if (SimHooks.Simulating)
            {
                // The sweep names its island (2026-09-11); anything else is the standard one.
                int s = SimHooks.IndexOfName(SizeNames, SimHooks.IslandSize);
                int t = SimHooks.IndexOfName(IslandSettings.StyleNames, SimHooks.IslandStyle);
                return new Snapshot
                {
                    size = s >= 0 ? (Size)s : Size.Medium,
                    style = t >= 0 ? (IslandSettings.Style)t : IslandSettings.Style.Terraced,
                    seed = 0,
                    rivalCount = Mathf.Clamp(SimHooks.RivalCount, 0, MaxRivals),
                };
            }
            if (activeRun.HasValue) return activeRun.Value;
            return new Snapshot { size = SelectedSize, style = SelectedStyle, seed = SelectedSeed, rivalCount = SelectedRivalCount };
        }
    }

    /// <summary>Freezes the selection as the run's world. Called when a new game starts.</summary>
    public static void BeginRun()
    {
        activeRun = new Snapshot { size = SelectedSize, style = SelectedStyle, seed = SelectedSeed, rivalCount = SelectedRivalCount };
    }

    /// <summary>One-line summary for status displays: "Medium · Terraced".</summary>
    public static string ActiveName => SizeNames[(int)Active.size] + " · " + IslandSettings.StyleNames[(int)Active.style];

    // ---- persistence ------------------------------------------------------

    private const string KeySize = "island.size";
    private const string KeyStyle = "island.style";
    private const string KeySeed = "island.seed";
    private const string KeyRivals = "island.rivals";

    public static void Load()
    {
        SelectedSize = (Size)Mathf.Clamp(PlayerPrefs.GetInt(KeySize, (int)Size.Medium), 0, SizeNames.Length - 1);
        SelectedStyle = (IslandSettings.Style)Mathf.Clamp(PlayerPrefs.GetInt(KeyStyle, (int)IslandSettings.Style.Terraced), 0, IslandSettings.StyleNames.Length - 1);
        SelectedSeedText = PlayerPrefs.GetString(KeySeed, "");
        SelectedRivalCount = Mathf.Clamp(PlayerPrefs.GetInt(KeyRivals, 0), 0, MaxRivals);
    }

    public static void Save()
    {
        PlayerPrefs.SetInt(KeySize, (int)SelectedSize);
        PlayerPrefs.SetInt(KeyStyle, (int)SelectedStyle);
        PlayerPrefs.SetString(KeySeed, SelectedSeedText ?? "");
        PlayerPrefs.SetInt(KeyRivals, SelectedRivalCount);
        PlayerPrefs.Save();
    }
}
