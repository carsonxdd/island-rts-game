using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// The player-facing field guide, read from <c>Assets/Resources/Information.txt</c>
/// and shown on <see cref="MenuScreens.Screen.Information"/> (2026-09-07).
///
/// Prose lives in the text asset so a sentence can be fixed without a compile;
/// numbers that live in a catalog are NOT typed into it — a <c>@name</c> line
/// asks the screen to build that table from the live catalog when it opens
/// (<see cref="MenuScreens"/> owns the rendering), so a retuned spear or a new
/// research entry shows up with no edit here. Format:
///
/// <code>
/// # a comment line (ignored)
/// ## TAB NAME            a sub-tab
/// ### Heading            a section header inside the tab
/// plain lines            a paragraph; consecutive lines join, a blank line ends it
/// - a bullet
/// @research              a live table (research, recipes, weapons, buildings, difficulty, colony, raids)
/// </code>
///
/// Like the changelog, anything unrecognised is ignored rather than fatal.
/// </summary>
public static class GameInfo
{
    public enum BlockKind { Heading, Paragraph, Bullet, Table }

    public sealed class Block
    {
        public BlockKind kind;
        /// <summary>The heading, paragraph or bullet text; for a table, its name without the @.</summary>
        public string text;
    }

    public sealed class Tab
    {
        public string title;
        public readonly List<Block> blocks = new List<Block>();
    }

    /// <summary>Resource path (no extension) of the guide.</summary>
    public const string ResourceName = "Information";

    private static List<Tab> tabs;

    /// <summary>Every sub-tab, in file order.</summary>
    public static IReadOnlyList<Tab> Tabs
    {
        get
        {
            if (tabs == null) Load();
            return tabs;
        }
    }

    /// <summary>Drops the cache so the next read re-parses the asset (editor iteration).</summary>
    public static void Reload() => tabs = null;

    private static void Load()
    {
        tabs = new List<Tab>();
        TextAsset asset = Resources.Load<TextAsset>(ResourceName);
        if (asset == null)
        {
            // Recoverable misconfiguration: the screen shows "No information yet".
            Debug.LogWarning("GameInfo: Resources/" + ResourceName + ".txt not found.");
            return;
        }
        Parse(asset.text, tabs);
    }

    /// <summary>Parses the format described on the class into <paramref name="into"/>.</summary>
    public static void Parse(string text, List<Tab> into)
    {
        if (string.IsNullOrEmpty(text)) return;

        string[] lines = text.Split('\n');
        Tab current = null;
        StringBuilder paragraph = null;

        void FlushParagraph()
        {
            if (paragraph == null || paragraph.Length == 0) { paragraph = null; return; }
            if (current != null)
                current.blocks.Add(new Block { kind = BlockKind.Paragraph, text = paragraph.ToString() });
            paragraph = null;
        }

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();

            if (line.Length == 0) { FlushParagraph(); continue; }

            if (line.StartsWith("### "))
            {
                FlushParagraph();
                if (current != null)
                    current.blocks.Add(new Block { kind = BlockKind.Heading, text = line.Substring(4).Trim() });
                continue;
            }

            if (line.StartsWith("## "))
            {
                FlushParagraph();
                current = new Tab { title = line.Substring(3).Trim() };
                into.Add(current);
                continue;
            }

            if (line.StartsWith("#")) continue;                    // comment

            if (current == null) continue;                         // text before any tab

            if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                FlushParagraph();
                current.blocks.Add(new Block { kind = BlockKind.Bullet, text = line.Substring(2).Trim() });
                continue;
            }

            if (line.StartsWith("@") && line.Length > 1 && line.IndexOf(' ') < 0)
            {
                FlushParagraph();
                current.blocks.Add(new Block { kind = BlockKind.Table, text = line.Substring(1).ToLowerInvariant() });
                continue;
            }

            // Plain text: a paragraph line. Consecutive lines are one paragraph.
            if (paragraph == null) paragraph = new StringBuilder();
            else paragraph.Append(' ');
            paragraph.Append(line);
        }
        FlushParagraph();
    }
}
