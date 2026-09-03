using UnityEngine;

/// <summary>
/// The player's chosen name for this run. Same shape as <see cref="Difficulty"/>
/// and <see cref="IslandOptions"/>: a persisted last-used value the name popup
/// pre-fills, and a run value frozen by <see cref="BeginRun"/> that a Restart
/// keeps and a New Game clears (so the popup asks again).
///
/// <see cref="Name"/> never returns empty: with no run begun it falls back to the
/// last name used, and under the balance sim — where no popup can ever show —
/// to <see cref="DefaultName"/>.
/// </summary>
public static class PlayerProfile
{
    public const string DefaultName = "Castaway";
    public const int MaxNameLength = 16;

    private const string KeyLastName = "player.name";

    private static string activeName;   // null = no run begun yet

    /// <summary>True once the popup (or a debug/sim path) has named this run.</summary>
    public static bool HasActive => activeName != null;

    /// <summary>The name in force. Never null or empty.</summary>
    public static string Name
    {
        get
        {
            if (activeName != null) return activeName;
            if (SimHooks.Simulating) return DefaultName;
            return LastName;
        }
    }

    /// <summary>What the popup pre-fills: the last name confirmed on this machine.</summary>
    public static string LastName => Sanitize(PlayerPrefs.GetString(KeyLastName, DefaultName));

    /// <summary>Freezes the name for this run and remembers it for next time.</summary>
    public static void BeginRun(string name)
    {
        activeName = Sanitize(name);
        PlayerPrefs.SetString(KeyLastName, activeName);
        PlayerPrefs.Save();
    }

    /// <summary>Forget the run name so the next game asks again. Called by MenuFlow.NewGame.</summary>
    public static void ClearRun()
    {
        activeName = null;
    }

    /// <summary>Trim, collapse inner whitespace, cap the length, and never return empty.</summary>
    public static string Sanitize(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return DefaultName;

        var sb = new System.Text.StringBuilder(raw.Length);
        bool pendingSpace = false;
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (char.IsControl(c)) continue;
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }
            if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
            sb.Append(c);
            if (sb.Length >= MaxNameLength) break;
        }

        return sb.Length == 0 ? DefaultName : sb.ToString();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        activeName = null;
    }
}
