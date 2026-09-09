using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The HUD quest tracker (2026-09-07): the next three open playtest quests,
/// top-right, like an RPG's objective list. Created in the game scene by a
/// launch hook plus a sceneLoaded subscription (the PauseController pattern —
/// the hook fires once per launch, the object dies with its scene). Editor and
/// development builds only, never under the balance sim, and hidden when the
/// DEV tab's toggle is off. A quest that ticks itself flashes at the top for a
/// few seconds so the tester sees the game noticed.
///
/// Text only, no raycast, sort order 46: above the bottom strip, under every
/// menu backdrop.
/// </summary>
public class DevQuestTracker : MonoBehaviour
{
    private const int Shown = 3;
    private const float FlashSeconds = 4f;
    private const int SortOrder = 46;

    private static DevQuestTracker instance;

    private TextMeshProUGUI text;
    private string shown;
    private DevQuests.Quest flashed;
    private float flashUntil;
    private readonly List<DevQuests.Quest> open = new List<DevQuests.Quest>();

    private static readonly string AccentHex = ColorUtility.ToHtmlStringRGBA(MenuStyle.TextAccent);
    private static readonly string MutedHex = ColorUtility.ToHtmlStringRGBA(MenuStyle.TextMuted);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!DevQuests.Enabled || SimHooks.Simulating) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        CreateForActiveScene();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (SimHooks.Simulating || mode != LoadSceneMode.Single) return;
        CreateForActiveScene();
    }

    private static void CreateForActiveScene()
    {
        if (instance != null) return;
        if (SceneManager.GetActiveScene().name != MenuFlow.GameSceneName) return;
        GameObject go = new GameObject("~DevQuestTracker");
        instance = go.AddComponent<DevQuestTracker>();
    }

    void Awake()
    {
        Canvas canvas = MenuBuilder.CreateCanvas("DevQuestTrackerCanvas", SortOrder);
        canvas.transform.SetParent(transform, false);

        text = MenuBuilder.Label(canvas.transform, "", MenuStyle.SmallSize, MenuStyle.TextPrimary, TextAlignmentOptions.TopRight);
        text.richText = true;
        RectTransform rt = text.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-16f, -16f);
        rt.sizeDelta = new Vector2(380f, 160f);

        DevQuests.OnChanged += Refresh;
        DevQuests.OnAutoCompleted += Flash;
        Refresh();
    }

    void OnDestroy()
    {
        DevQuests.OnChanged -= Refresh;
        DevQuests.OnAutoCompleted -= Flash;
        if (instance == this) instance = null;
    }

    void Update()
    {
        if (flashed != null && Time.unscaledTime > flashUntil)
        {
            flashed = null;
            Refresh();
        }
        if (Time.time >= nextWatch)
        {
            nextWatch = Time.time + WatchInterval;
            Watch();
        }
    }

    // ---- state watcher (2026-09-09) ----------------------------------------
    // A few quests are about a STATE, not an event ("four cutters over two
    // trees"). Each check runs only while its quest is open (DevQuests.IsOpen is
    // one dictionary lookup) and walks the ActiveLists with no allocation, twice
    // a second. Once the quest ticks the check is skipped for good.

    private const float WatchInterval = 0.5f;
    private float nextWatch;

    private void Watch()
    {
        if (DevQuests.IsOpen("crowd:split")) WatchCuttersSplit();
        if (DevQuests.IsOpen("dropoff:spread")) WatchDropoffSpread();
        if (DevQuests.IsOpen("patrol:spread")) WatchPatrolSpread();
    }

    /// <summary>Four or more wood workers at nodes, over at least two trees.</summary>
    private static void WatchCuttersSplit()
    {
        var nodes = ResourceNode.ActiveList;
        int workers = 0, trees = 0;
        for (int i = 0; i < nodes.Count; i++)
        {
            ResourceNode n = nodes[i];
            if (n == null || n.resourceType != ResourceNode.ResourceType.Wood) continue;
            int at = n.GetWorkerCount();
            if (at <= 0) continue;
            workers += at;
            trees++;
        }
        if (workers >= 4 && trees >= 2) DevQuests.Signal("crowd:split");
    }

    /// <summary>Two workers hold different drop-off bearings on the fire at once.</summary>
    private static void WatchDropoffSpread()
    {
        var workers = Worker.ActiveList;
        int claimed = 0;
        for (int i = 0; i < workers.Count; i++)
            if (workers[i] != null && workers[i].dropoffSlot >= 0) claimed++;   // slots are unique per worker
        if (claimed >= 2) DevQuests.Signal("dropoff:spread");
    }

    /// <summary>Six warriors, no walls, and none within 4 u of the fire's edge.</summary>
    private static void WatchPatrolSpread()
    {
        if (Warrior.ActiveList.Count < 6 || Wall.ActiveList.Count > 0) return;
        BaseBuilding fire = BaseBuilding.FindAlive();
        if (fire == null || fire.HousingCollider == null) return;
        var warriors = Warrior.ActiveList;
        for (int i = 0; i < warriors.Count; i++)
        {
            Warrior w = warriors[i];
            if (w == null) continue;
            if (TargetingUtil.EdgeDistance(w.transform.position, fire.transform, fire.HousingCollider) < 4f) return;
        }
        DevQuests.Signal("patrol:spread");
    }

    private void Flash(DevQuests.Quest q)
    {
        flashed = q;
        flashUntil = Time.unscaledTime + FlashSeconds;
        Refresh();
    }

    private void Refresh()
    {
        if (text == null) return;
        bool show = DevQuests.ShowTracker;
        if (text.gameObject.activeSelf != show) text.gameObject.SetActive(show);
        if (!show) return;

        var sb = new System.Text.StringBuilder(256);
        sb.Append("<color=#").Append(AccentHex).Append(">PLAYTEST</color>  <color=#").Append(MutedHex).Append('>')
          .Append(DevQuests.DoneCount).Append(" / ").Append(DevQuests.TotalCount).Append("</color>");

        if (flashed != null)
            sb.Append("\n<color=#").Append(AccentHex).Append(">[x] ").Append(flashed.text).Append("</color>");

        DevQuests.NextOpen(open, Shown);
        for (int i = 0; i < open.Count; i++)
        {
            sb.Append("\n[ ] ").Append(open[i].text);
            if (open[i].IsAuto) sb.Append(" <color=#").Append(MutedHex).Append("><size=80%>auto</size></color>");
        }
        if (open.Count == 0 && flashed == null)
            sb.Append("\n<color=#").Append(MutedHex).Append(">All quests done. Esc > Information > DEV to submit.</color>");

        string s = sb.ToString();
        if (s == shown) return;
        shown = s;
        text.text = s;
    }
}
