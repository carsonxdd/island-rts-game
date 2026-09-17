#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

/// <summary>
/// The caption under the VISUAL balance sim's window: which run this is, what
/// day it is, and the same numbers days.csv is about to record.
///
/// IMGUI on purpose. The project's uGUI has real rules (computed panel heights,
/// raycast targets, layout rebuilds — see CLAUDE.md) and none of them are worth
/// paying for a dev-only readout that nothing clicks. It also keeps the overlay
/// completely outside the game's own HUD, so what you see drawn here can never be
/// confused with what the player would see.
///
/// Values are pushed by <see cref="SimRunner"/> once a second, at the same moment
/// the policy ticks, rather than pulled per frame: the overlay then shows exactly
/// the state the policy acted on.
/// </summary>
public class SimVisualOverlay : MonoBehaviour
{
    /// <summary>One second's worth of run state, as the policy saw it.</summary>
    public struct Frame
    {
        public string runId;
        public string strategy;
        public int seed;
        public int runIndex, runCount;
        public int day, daysToSurvive;
        public bool raidTonight;
        public int colonists, workers, warriors, enemies;
        /// <summary>The levy (2026-09-16): colonists in a warrior body right now, and weapons on the rack for the next alarm.</summary>
        public int mustered, spareWeapons;
        public float wood, food, stone, metal;
        public float campfireHp, campfireHpMax;
        public int hunger;
        /// <summary>What the policy is trying to reach this second, what it last did, what the castaway is doing (2026-09-10).</summary>
        public string goal, intent, castaway;
        public int nextRaidSize;
        /// <summary>
        /// The first rival colony (2026-09-16): landed at all, its personality,
        /// the opinion WORD and attitude the player would see, its militia, its
        /// fire, and whose expedition party is at sea ("theirs" / "ours" / "").
        /// </summary>
        public bool rivalLanded;
        public string rivalStrategy, rivalOpinion, rivalAttitude, rivalParty;
        public int rivalWarriors, rivalFirePct;
    }

    private static Frame frame;
    private static SimVisualOverlay instance;

    private GUIStyle line;
    private GUIStyle head;
    private Texture2D backdrop;

    private static readonly string[] HungerNames = { "Fed", "HUNGRY", "STARVING" };

    /// <summary>Create the overlay. No-ops unless a visual run is driving.</summary>
    public static void Ensure()
    {
        if (instance != null || !SimHooks.Visual) return;
        GameObject go = new GameObject("~SimVisualOverlay");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SimVisualOverlay>();
    }

    /// <summary>Push the second's state. Cheap enough to call unconditionally.</summary>
    public static void Push(Frame f)
    {
        frame = f;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (backdrop != null) Destroy(backdrop);
    }

    private void EnsureStyles()
    {
        if (line != null) return;

        backdrop = new Texture2D(1, 1);
        backdrop.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.62f));
        backdrop.Apply();

        head = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        line = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            normal = { textColor = new Color(0.86f, 0.9f, 0.95f) }
        };
    }

    private void OnGUI()
    {
        EnsureStyles();

        const float pad = 8f;
        float w = Mathf.Min(360f, Screen.width - pad * 2f);
        float h = frame.rivalLanded ? 168f : 150f;
        Rect box = new Rect(pad, Screen.height - h - pad, w, h);

        GUI.DrawTexture(box, backdrop);
        GUILayout.BeginArea(new Rect(box.x + 8f, box.y + 6f, box.width - 16f, box.height - 12f));

        string title = string.IsNullOrEmpty(frame.runId)
            ? "sim starting…"
            : $"{frame.strategy.ToUpperInvariant()} · {frame.runId} · run {frame.runIndex + 1}/{frame.runCount}";
        GUILayout.Label(title, head);

        string spectator = instance != null && CameraController.Instance != null
            ? ShotLabel()
            : "";

        GUILayout.Label(
            $"Day {frame.day}/{frame.daysToSurvive}" +
            (frame.raidTonight ? "   RAID TONIGHT" : "") +
            (frame.hunger > 0 ? "   " + HungerNames[Mathf.Clamp(frame.hunger, 0, 2)] : "") +
            (spectator.Length > 0 ? "   [" + spectator + "]" : ""), line);

        GUILayout.Label(
            $"W {frame.wood:0}  F {frame.food:0}  S {frame.stone:0}  M {frame.metal:0}" +
            $"   fire {Percent(frame.campfireHp, frame.campfireHpMax)}", line);

        GUILayout.Label(
            $"colonists {frame.colonists}   workers {frame.workers}   " +
            $"warriors {frame.warriors}   levy {frame.mustered} up / {frame.spareWeapons} spare   raiders {frame.enemies}   next raid ~{frame.nextRaidSize}", line);

        // What the run is working on (2026-09-10): the policy's goal for this
        // second, the last move it made, and the castaway's own errand.
        GUILayout.Label("goal: " + (frame.goal ?? ""), line);
        GUILayout.Label("last: " + (frame.intent ?? "") + "   castaway: " + (frame.castaway ?? ""), line);

        // The neighbour (2026-09-16): one line, only once a rival has landed.
        if (frame.rivalLanded)
        {
            GUILayout.Label(
                $"rival: {frame.rivalStrategy}   {frame.rivalOpinion} ({frame.rivalAttitude})" +
                $"   warriors {frame.rivalWarriors}   fire {frame.rivalFirePct}%" +
                (string.IsNullOrEmpty(frame.rivalParty) ? "" : "   party at sea: " + frame.rivalParty), line);
        }

        GUILayout.EndArea();
    }

    private static string Percent(float value, float max)
    {
        if (max <= 0f) return "--";
        return Mathf.RoundToInt(100f * Mathf.Clamp01(value / max)) + "%";
    }

    private static string ShotLabel()
    {
        SimSpectatorCamera cam = CameraController.Instance.GetComponent<SimSpectatorCamera>();
        return cam != null ? cam.CurrentShot.ToString() : "";
    }
}
#endif
