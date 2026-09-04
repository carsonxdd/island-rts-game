using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The player-facing patch notes, read from <c>Assets/Resources/Changelog.txt</c>
/// and shown on <see cref="MenuScreens.Screen.Changelog"/>.
///
/// The file is a text asset rather than code so a note can be added without a
/// compile, and it ships inside every build because it lives in Resources.
/// Format, newest entry first:
///
/// <code>
/// # a comment line (ignored)
/// ## 2026-09-04 — Title of the entry
/// - one bullet per change, in player language
/// - a bullet may
///   continue on an indented line
/// </code>
///
/// Anything that is not a heading, a bullet, a continuation or a comment is
/// ignored, so a stray line can never break the screen — the worst outcome of
/// a typo is a missing bullet.
/// </summary>
public static class Changelog
{
    public sealed class Entry
    {
        public string heading;
        public readonly List<string> bullets = new List<string>();
    }

    /// <summary>Resource path (no extension) of the notes file.</summary>
    public const string ResourceName = "Changelog";

    private static List<Entry> entries;

    /// <summary>Every entry, newest first (the order they appear in the file).</summary>
    public static IReadOnlyList<Entry> Entries
    {
        get
        {
            if (entries == null) Load();
            return entries;
        }
    }

    /// <summary>
    /// The date of the newest entry ("2026-09-04"), or null when the newest
    /// heading does not start with one. The main menu's version line shows it.
    /// </summary>
    public static string LatestDate
    {
        get
        {
            IReadOnlyList<Entry> all = Entries;
            if (all.Count == 0) return null;
            string h = all[0].heading;
            if (h.Length < 10) return null;
            for (int i = 0; i < 10; i++)
            {
                char c = h[i];
                bool ok = (i == 4 || i == 7) ? c == '-' : char.IsDigit(c);
                if (!ok) return null;
            }
            return h.Substring(0, 10);
        }
    }

    /// <summary>Drops the cache so the next read re-parses the asset (editor iteration).</summary>
    public static void Reload() => entries = null;

    private static void Load()
    {
        entries = new List<Entry>();
        TextAsset asset = Resources.Load<TextAsset>(ResourceName);
        if (asset == null)
        {
            // Recoverable misconfiguration: the screen shows "No notes yet".
            Debug.LogWarning("Changelog: Resources/" + ResourceName + ".txt not found.");
            return;
        }
        Parse(asset.text, entries);
    }

    /// <summary>Parses the notes format described on the class into <paramref name="into"/>.</summary>
    public static void Parse(string text, List<Entry> into)
    {
        if (string.IsNullOrEmpty(text)) return;

        string[] lines = text.Split('\n');
        Entry current = null;

        for (int i = 0; i < lines.Length; i++)
        {
            string raw = lines[i].TrimEnd('\r', ' ', '\t');
            if (raw.Length == 0) continue;

            string line = raw.TrimStart(' ', '\t');
            bool indented = line.Length < raw.Length;

            if (line.StartsWith("## "))
            {
                current = new Entry { heading = line.Substring(3).Trim() };
                into.Add(current);
                continue;
            }

            if (line.StartsWith("#")) continue;                    // comment

            if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                if (current == null) continue;                     // bullet before any heading
                current.bullets.Add(line.Substring(2).Trim());
                continue;
            }

            // An indented plain line continues the previous bullet.
            if (indented && current != null && current.bullets.Count > 0)
            {
                int last = current.bullets.Count - 1;
                current.bullets[last] = current.bullets[last] + " " + line;
            }
        }
    }
}
