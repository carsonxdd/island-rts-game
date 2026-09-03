using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Every rebindable action in the game, and the keys currently bound to it.
///
/// The game used to read <c>KeyCode</c> literals inline (<c>Input.GetKeyDown(KeyCode.B)</c>)
/// and a handful of <c>public KeyCode</c> inspector fields, which meant a binding
/// lived in whichever script happened to use it — impossible to list, let alone
/// rebind. Every gameplay key now resolves through here.
///
/// Same shape as <see cref="GameSettings"/>: a default, a PlayerPrefs key, and a
/// read at the point of effect. Nothing pushes bindings into scripts, so a rebind
/// takes effect on the next frame with no scene lookup.
///
/// Escape is deliberately NOT in this list. It is not a binding, it is a
/// back-out gesture that five systems consume in a specific order (build ghost,
/// wall line, demolish, crafting panel, campfire placement, then the pause menu
/// — see PauseController). Letting a player move it would break that chain.
/// Debug keys (F3 AI overlay, F4 debug menu, F6/F7 perf logger) are likewise
/// fixed: they do not ship in a release build.
/// </summary>
public static class KeyBindings
{
    public enum Action
    {
        PanUp, PanDown, PanLeft, PanRight,
        RotateCameraLeft, RotateCameraRight,
        BuildMode,
        SelectHut, SelectWoodWall, SelectStoneWall, SelectWatchtower, SelectWorkshop,
        ConvertToGate, RotateBuilding, StaircaseWalls,
        Demolish, ToggleGrid,
        CenterOnCharacter,
    }

    /// <summary>
    /// One action's two slots. A secondary is a real alternate, not a fallback:
    /// WASD and the arrow keys both pan, Delete and X both demolish, and either
    /// Shift staircases a wall line — all of that is expressible without a
    /// special case because every binding has room for two keys.
    /// </summary>
    public struct Binding
    {
        public KeyCode primary;
        public KeyCode secondary;
        public Binding(KeyCode p, KeyCode s = KeyCode.None) { primary = p; secondary = s; }
    }

    /// <summary>Display order and grouping for the Controls screen.</summary>
    public static readonly (string group, Action action, string label)[] Catalog =
    {
        ("Camera", Action.PanUp,              "Pan up"),
        ("Camera", Action.PanDown,            "Pan down"),
        ("Camera", Action.PanLeft,            "Pan left"),
        ("Camera", Action.PanRight,           "Pan right"),
        ("Camera", Action.RotateCameraLeft,   "Rotate left"),
        ("Camera", Action.RotateCameraRight,  "Rotate right"),

        ("Building", Action.BuildMode,        "Build mode"),
        ("Building", Action.SelectHut,        "Select hut"),
        ("Building", Action.SelectWoodWall,   "Select wooden wall"),
        ("Building", Action.SelectStoneWall,  "Select stone wall"),
        ("Building", Action.SelectWatchtower, "Select watchtower"),
        ("Building", Action.SelectWorkshop,   "Select workshop"),
        ("Building", Action.ConvertToGate,    "Convert wall to gate"),
        ("Building", Action.RotateBuilding,   "Rotate / flip wall path"),
        ("Building", Action.StaircaseWalls,   "Staircase wall path"),
        ("Building", Action.Demolish,         "Demolish mode"),
        ("Building", Action.ToggleGrid,       "Toggle build grid"),

        ("Character", Action.CenterOnCharacter, "Centre camera on your character"),
    };

    private static readonly Dictionary<Action, Binding> Defaults = new Dictionary<Action, Binding>
    {
        { Action.PanUp,              new Binding(KeyCode.W, KeyCode.UpArrow) },
        { Action.PanDown,            new Binding(KeyCode.S, KeyCode.DownArrow) },
        { Action.PanLeft,            new Binding(KeyCode.A, KeyCode.LeftArrow) },
        { Action.PanRight,           new Binding(KeyCode.D, KeyCode.RightArrow) },
        { Action.RotateCameraLeft,   new Binding(KeyCode.Q) },
        { Action.RotateCameraRight,  new Binding(KeyCode.E) },
        { Action.BuildMode,          new Binding(KeyCode.B) },
        { Action.SelectHut,          new Binding(KeyCode.Alpha1, KeyCode.Keypad1) },
        { Action.SelectWoodWall,     new Binding(KeyCode.Alpha2, KeyCode.Keypad2) },
        { Action.SelectStoneWall,    new Binding(KeyCode.Alpha3, KeyCode.Keypad3) },
        { Action.SelectWatchtower,   new Binding(KeyCode.Alpha4, KeyCode.Keypad4) },
        { Action.SelectWorkshop,     new Binding(KeyCode.Alpha5, KeyCode.Keypad5) },
        { Action.ConvertToGate,      new Binding(KeyCode.G) },
        { Action.RotateBuilding,     new Binding(KeyCode.R) },
        { Action.StaircaseWalls,     new Binding(KeyCode.LeftShift, KeyCode.RightShift) },
        { Action.Demolish,           new Binding(KeyCode.Delete, KeyCode.X) },
        { Action.ToggleGrid,         new Binding(KeyCode.F2) },
        { Action.CenterOnCharacter,  new Binding(KeyCode.Space) },
    };

    private static readonly Dictionary<Action, Binding> current = new Dictionary<Action, Binding>();
    private static bool loaded;

    /// <summary>
    /// Keys the player may never bind. Escape and the debug keys are reserved
    /// (see the class summary); the mouse buttons are reserved because the game
    /// reads them positionally for select/place/cancel and free-look.
    /// </summary>
    public static bool IsReserved(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.Escape:
            case KeyCode.F3: case KeyCode.F4: case KeyCode.F6: case KeyCode.F7:
            case KeyCode.Mouse0: case KeyCode.Mouse1: case KeyCode.Mouse2:
                return true;
            default:
                return false;
        }
    }

    // ---- persistence ------------------------------------------------------

    private static string Key(Action a, bool secondary) => "key." + a + (secondary ? ".s" : ".p");

    public static void Load()
    {
        if (loaded) return;
        loaded = true;

        foreach (var kv in Defaults)
        {
            KeyCode p = (KeyCode)PlayerPrefs.GetInt(Key(kv.Key, false), (int)kv.Value.primary);
            KeyCode s = (KeyCode)PlayerPrefs.GetInt(Key(kv.Key, true), (int)kv.Value.secondary);

            // A reserved key can only get in here through a hand-edited prefs
            // file or a build where the reserved set grew. Drop it rather than
            // letting it shadow Escape for the rest of the session.
            if (IsReserved(p)) p = kv.Value.primary;
            if (IsReserved(s)) s = KeyCode.None;

            current[kv.Key] = new Binding(p, s);
        }
    }

    public static void Save()
    {
        Load();
        foreach (var kv in current)
        {
            PlayerPrefs.SetInt(Key(kv.Key, false), (int)kv.Value.primary);
            PlayerPrefs.SetInt(Key(kv.Key, true), (int)kv.Value.secondary);
        }
        PlayerPrefs.Save();
    }

    public static void ResetToDefaults()
    {
        loaded = true;
        current.Clear();
        foreach (var kv in Defaults) current[kv.Key] = kv.Value;
        Save();
    }

    public static bool IsDefault(Action action)
    {
        Binding b = Get(action);
        Binding d = Defaults[action];
        return b.primary == d.primary && b.secondary == d.secondary;
    }

    /// <summary>True when any binding differs from its default — drives the Reset button's state.</summary>
    public static bool AnyCustomised()
    {
        foreach (Action a in Defaults.Keys) if (!IsDefault(a)) return true;
        return false;
    }

    // ---- reading ----------------------------------------------------------

    public static Binding Get(Action action)
    {
        Load();
        return current.TryGetValue(action, out Binding b) ? b : Defaults[action];
    }

    /// <summary>Pressed this frame, on either slot.</summary>
    public static bool Down(Action action)
    {
        Binding b = Get(action);
        if (b.primary != KeyCode.None && Input.GetKeyDown(b.primary)) return true;
        return b.secondary != KeyCode.None && Input.GetKeyDown(b.secondary);
    }

    /// <summary>Held this frame, on either slot.</summary>
    public static bool Held(Action action)
    {
        Binding b = Get(action);
        if (b.primary != KeyCode.None && Input.GetKey(b.primary)) return true;
        return b.secondary != KeyCode.None && Input.GetKey(b.secondary);
    }

    /// <summary>+1/-1/0 from a pair of opposed actions — the axis the camera pans on.</summary>
    public static float Axis(Action negative, Action positive)
    {
        float v = 0f;
        if (Held(positive)) v += 1f;
        if (Held(negative)) v -= 1f;
        return v;
    }

    // ---- writing ----------------------------------------------------------

    /// <summary>
    /// Binds a key, clearing it from wherever else it was bound first.
    ///
    /// Silently stealing is the right behaviour here rather than rejecting the
    /// bind: a player remapping several keys in a row will transiently collide
    /// with a key they are about to move anyway, and a modal "that's taken"
    /// error mid-remap is worse than a row that visibly goes blank.
    /// Returns the action the key was taken from, or null.
    /// </summary>
    public static Action? Bind(Action action, bool secondary, KeyCode key)
    {
        Load();
        if (IsReserved(key)) return null;

        Action? stolenFrom = null;
        foreach (Action other in new List<Action>(current.Keys))
        {
            Binding b = current[other];
            bool changed = false;

            if (b.primary == key && !(other == action && !secondary)) { b.primary = KeyCode.None; changed = true; }
            if (b.secondary == key && !(other == action && secondary)) { b.secondary = KeyCode.None; changed = true; }

            if (changed)
            {
                // An action left with only a secondary key is confusing to read
                // on the Controls screen, so promote it into the empty primary.
                if (b.primary == KeyCode.None && b.secondary != KeyCode.None)
                {
                    b.primary = b.secondary;
                    b.secondary = KeyCode.None;
                }
                current[other] = b;
                stolenFrom = other;
            }
        }

        Binding target = current[action];
        if (secondary) target.secondary = key; else target.primary = key;
        current[action] = target;
        return stolenFrom;
    }

    public static void Clear(Action action, bool secondary)
    {
        Load();
        Binding b = current[action];
        if (secondary) b.secondary = KeyCode.None;
        else { b.primary = b.secondary; b.secondary = KeyCode.None; }   // primary is never the empty one
        current[action] = b;
    }

    // ---- display ----------------------------------------------------------

    /// <summary>Player-facing key name. KeyCode.ToString() is programmer output ("Alpha1", "LeftShift").</summary>
    public static string Name(KeyCode key)
    {
        if (key == KeyCode.None) return "—";

        string s = key.ToString();
        if (s.StartsWith("Alpha")) return s.Substring(5);
        if (s.StartsWith("Keypad")) return "Num " + s.Substring(6);

        switch (key)
        {
            case KeyCode.LeftShift: return "L Shift";
            case KeyCode.RightShift: return "R Shift";
            case KeyCode.LeftControl: return "L Ctrl";
            case KeyCode.RightControl: return "R Ctrl";
            case KeyCode.LeftAlt: return "L Alt";
            case KeyCode.RightAlt: return "R Alt";
            case KeyCode.UpArrow: return "Up";
            case KeyCode.DownArrow: return "Down";
            case KeyCode.LeftArrow: return "Left";
            case KeyCode.RightArrow: return "Right";
            case KeyCode.Space: return "Space";
            case KeyCode.Return: return "Enter";
            case KeyCode.Backspace: return "Backspace";
            case KeyCode.Delete: return "Delete";
            case KeyCode.Tab: return "Tab";
            case KeyCode.BackQuote: return "Backtick";
            case KeyCode.Minus: return "-";
            case KeyCode.Equals: return "=";
            case KeyCode.Comma: return ",";
            case KeyCode.Period: return ".";
            case KeyCode.Slash: return "/";
            case KeyCode.Semicolon: return ";";
            case KeyCode.Quote: return "'";
            case KeyCode.LeftBracket: return "[";
            case KeyCode.RightBracket: return "]";
            case KeyCode.Backslash: return "\\";
            default: return s;
        }
    }

    /// <summary>
    /// The key the player just pressed, for the rebind capture state. Walks the
    /// KeyCode enum rather than using Input.inputString, which only reports
    /// printable characters — it would never see F-keys, arrows or modifiers.
    /// </summary>
    public static bool TryCaptureKey(out KeyCode key)
    {
        key = KeyCode.None;
        if (!Input.anyKeyDown) return false;

        for (int i = 0; i < AllKeys.Length; i++)
        {
            if (Input.GetKeyDown(AllKeys[i])) { key = AllKeys[i]; return true; }
        }
        return false;
    }

    /// <summary>
    /// Built once. Enum.GetValues allocates, and the capture path runs every
    /// frame the rebind prompt is open — not a hot path, but a per-frame one,
    /// and the codebase's rule is no per-frame allocation.
    /// </summary>
    private static readonly KeyCode[] AllKeys = BuildKeyList();

    private static KeyCode[] BuildKeyList()
    {
        var list = new List<KeyCode>();
        foreach (KeyCode k in (KeyCode[])Enum.GetValues(typeof(KeyCode)))
        {
            if (k == KeyCode.None) continue;
            if (k >= KeyCode.Mouse0 && k <= KeyCode.Mouse6) continue;
            if (k >= KeyCode.JoystickButton0) continue;      // no gamepad support yet
            list.Add(k);
        }
        return list.ToArray();
    }
}
