using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;

/// <summary>
/// The BENCH half of the campfire panel, and the whole panel for a Workshop
/// (2026-09-17): a MAKE / LEARN filter over one scrolling list of one-line rows
/// (title, cost coloured by affordability, the button), the character's line
/// under it, and the QUEUE docked at the bottom with a Remove per entry and the
/// Send button. Craft, Research and Queue used to be three tabs of one workflow.
/// </summary>
/// <remarks>
/// Owned and driven by <see cref="WorkerAssignmentUI"/>; the title bar's COLONY
/// button swaps back to <see cref="ColonyPanel"/> in the same corner. Display
/// only — the station owns its queue, the catalogs own the definitions, the
/// character does the queueing (<c>TryQueueCraft</c> / <c>TryQueueResearch</c>).
/// Every label is dirty-checked; the queue pool is fixed so the panel never
/// refits while open.
/// </remarks>
public class BenchPanel
{
    public const float Width = 600f;
    const float ListHeight = 330f;
    const int QueueRowsShown = 5;
    const float QueueRowHeight = 26f;
    const string PanelPosKey = "ui.benchPanel";
    static readonly Vector2 DefaultPanelPos = new Vector2(16f, 16f);
    const int FilterMake = 0, FilterLearn = 1;

    static readonly string LockHex = ColorUtility.ToHtmlStringRGBA(new Color(0.62f, 0.60f, 0.57f, 0.9f));

    private class CraftRow
    {
        public CraftingCatalog.Recipe recipe;
        public GameObject root;
        public TextMeshProUGUI label, cost, oneLabel;
        public Button one, five;
        public int stateLast = -1, queuedLast = -1;
        public bool listed = true;
    }

    private class ResearchRow
    {
        public ResearchCatalog.ResearchDef def;
        public GameObject root;
        public TextMeshProUGUI cost, buttonLabel;
        public Button button;
        public int stateLast = -1;
        public bool listed = true;
    }

    private class QueueRow
    {
        public GameObject root;
        public TextMeshProUGUI text;
        public Button remove;
        public string textLast;
    }

    private readonly WorkerAssignmentUI owner;
    private BaseBuilding fire;
    private CraftStation station;

    public RectTransform Panel { get; private set; }
    private DraggablePanel drag;
    private TextMeshProUGUI titleText;
    private Button colonyButton;
    private Button[] filterButtons;
    private int filter = FilterMake;

    private readonly List<CraftRow> craftRows = new List<CraftRow>();
    private readonly List<ResearchRow> researchRows = new List<ResearchRow>();
    private readonly List<QueueRow> queueRows = new List<QueueRow>();
    private TextMeshProUGUI listStatus, queueStatus;
    private Button sendButton;
    private TextMeshProUGUI sendLabel;
    private string listStatusLast, queueStatusLast, sendLabelLast;
    private int queueVersionShown = -1;

    public BenchPanel(WorkerAssignmentUI owner) { this.owner = owner; }

    public bool IsOpen => Panel != null && Panel.gameObject.activeSelf;
    public CraftStation Station => station;

    // ------------------------------------------------------------------
    // Open / close
    // ------------------------------------------------------------------

    /// <param name="stationOnly">A Workshop: no colony to swap to from here.</param>
    public void Open(BaseBuilding building, CraftStation st, bool stationOnly)
    {
        if (Panel == null) return;
        fire = building;
        station = st;
        titleText.text = stationOnly && st != null ? st.displayName.ToUpperInvariant() : "CAMPFIRE BENCH";
        colonyButton.gameObject.SetActive(!stationOnly);

        for (int i = 0; i < craftRows.Count; i++) { craftRows[i].stateLast = -1; craftRows[i].queuedLast = -1; }
        for (int i = 0; i < researchRows.Count; i++) researchRows[i].stateLast = -1;
        for (int i = 0; i < queueRows.Count; i++) queueRows[i].textLast = null;
        listStatusLast = queueStatusLast = sendLabelLast = null;
        queueVersionShown = -1;

        Panel.gameObject.SetActive(true);
        ApplyFilter();
        drag.Clamp();
        UpdateDisplay();
    }

    public void Close()
    {
        if (Panel != null) Panel.gameObject.SetActive(false);
        fire = null;
        station = null;
    }

    /// <summary>Per frame while open: the bench may have burned under it.</summary>
    public bool Tick()
    {
        if (!IsOpen) return false;
        if (fire == null || station == null || !station.IsAlive) { Close(); return false; }
        UpdateDisplay();
        return true;
    }

    // ------------------------------------------------------------------
    // Construction
    // ------------------------------------------------------------------

    public void Build(Transform canvas)
    {
        Panel = MenuBuilder.Panel(canvas, "BenchPanel", Width, 100f);
        Panel.anchorMin = Panel.anchorMax = Vector2.zero;
        Panel.pivot = Vector2.zero;
        Panel.anchoredPosition = DefaultPanelPos;

        VerticalLayoutGroup col = MenuBuilder.Column(Panel, 2f);

        RectTransform title = MenuBuilder.SettingRow(col.transform, "CAMPFIRE BENCH", out RectTransform titleSlot, 40f);
        drag = DraggablePanel.Attach(title, Panel, PanelPosKey, DefaultPanelPos);
        titleText = title.GetComponentInChildren<TextMeshProUGUI>();
        titleText.color = MenuStyle.TextAccent;
        titleText.fontSize = MenuStyle.HeadingSize - 4f;
        titleText.characterSpacing = 3f;
        titleText.textWrappingMode = TextWrappingModes.NoWrap;
        colonyButton = MenuBuilder.SlotButton(titleSlot, "‹  COLONY", () => owner.SwapToColony(), new Vector2(0.44f, 0.1f), new Vector2(0.82f, 0.9f));
        SmallLabel(colonyButton);
        Tooltip.Attach((RectTransform)colonyButton.transform, "Back to the colony: jobs, people, priorities, defence and stock.");
        Button close = MenuBuilder.SlotButton(titleSlot, "X", () => owner.ClosePanel(), new Vector2(0.86f, 0.1f), new Vector2(1f, 0.9f));
        SmallLabel(close);
        MenuBuilder.Divider(col.transform);

        // MAKE / LEARN over one list
        MenuBuilder.Spacer(col.transform, 4f);
        filterButtons = MenuBuilder.SegmentButtons(col.transform, new[] { "MAKE", "LEARN" }, 34f, MenuStyle.SmallSize + 1f, OnFilter, 6f);
        MenuBuilder.TintTabs(filterButtons, filter);
        Tooltip.Attach((RectTransform)filterButtons[0].transform, "Repeatable recipes at this bench. Costs come from your character's hands and the stockpile together and are paid when the item is finished.");
        Tooltip.Attach((RectTransform)filterButtons[1].transform, "Research once and the whole colony knows it. Each entry opens jobs, buildings or recipes and hands your character its tool.");

        VerticalLayoutGroup list = MenuBuilder.ScrollColumn(col.transform, 2f, ListHeight);
        var recipes = CraftingCatalog.All;
        for (int i = 0; i < recipes.Length; i++)
        {
            CraftRow row = new CraftRow { recipe = recipes[i] };
            RectTransform rt = MenuBuilder.SettingRow(list.transform, recipes[i].title, out RectTransform slot, MenuStyle.CompactRowHeight);
            row.root = rt.gameObject;
            row.label = rt.GetComponentInChildren<TextMeshProUGUI>();
            row.label.textWrappingMode = TextWrappingModes.NoWrap;
            row.label.overflowMode = TextOverflowModes.Ellipsis;
            row.cost = CostLabel(slot, 0f, 0.50f);
            CraftingCatalog.Recipe captured = recipes[i];
            row.one = MenuBuilder.SlotButton(slot, "Craft", () => OnCraft(captured, 1), new Vector2(0.53f, 0.06f), new Vector2(0.78f, 0.94f));
            row.oneLabel = row.one.GetComponentInChildren<TextMeshProUGUI>();
            row.five = MenuBuilder.SlotButton(slot, "×5", () => OnCraft(captured, 5), new Vector2(0.81f, 0.06f), new Vector2(1f, 0.94f));
            SmallLabel(row.one);
            SmallLabel(row.five);
            if (recipes[i].oncePerRun) row.five.gameObject.SetActive(false);
            Tooltip.Attach(rt, recipes[i].description);
            craftRows.Add(row);
        }
        var defs = ResearchCatalog.All;
        for (int i = 0; i < defs.Length; i++)
        {
            ResearchRow row = new ResearchRow { def = defs[i] };
            RectTransform rt = MenuBuilder.SettingRow(list.transform, defs[i].title, out RectTransform slot, MenuStyle.CompactRowHeight);
            row.root = rt.gameObject;
            TextMeshProUGUI label = rt.GetComponentInChildren<TextMeshProUGUI>();
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            row.cost = CostLabel(slot, 0f, 0.56f);
            ResearchCatalog.ResearchDef captured = defs[i];
            row.button = MenuBuilder.SlotButton(slot, "Research", () => OnResearch(captured), new Vector2(0.60f, 0.06f), new Vector2(1f, 0.94f));
            row.buttonLabel = row.button.GetComponentInChildren<TextMeshProUGUI>();
            SmallLabel(row.button);
            Tooltip.Attach(rt, defs[i].description);
            researchRows.Add(row);
        }

        listStatus = MenuBuilder.RowDescription(col.transform, "");
        listStatus.textWrappingMode = TextWrappingModes.NoWrap;
        listStatus.overflowMode = TextOverflowModes.Ellipsis;

        // QUEUE, docked: a fixed pool, blank rows stay blank so the panel never refits
        MenuBuilder.SectionHeader(col.transform, "Queue");
        for (int i = 0; i < QueueRowsShown; i++)
        {
            QueueRow row = new QueueRow();
            GameObject go = new GameObject("QueueRow", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(col.transform, false);
            go.GetComponent<LayoutElement>().preferredHeight = QueueRowHeight;
            RectTransform rt = go.GetComponent<RectTransform>();
            row.root = go;
            row.text = MenuBuilder.Label(rt, "", MenuStyle.SmallSize, MenuStyle.TextPrimary, TextAlignmentOptions.MidlineLeft);
            row.text.textWrappingMode = TextWrappingModes.NoWrap;
            row.text.overflowMode = TextOverflowModes.Ellipsis;
            RectTransform trt = row.text.rectTransform;
            trt.anchorMin = new Vector2(0f, 0f);
            trt.anchorMax = new Vector2(0.80f, 1f);
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            int index = i;
            row.remove = MenuBuilder.SlotButton(rt, "Remove", () => OnRemove(index), new Vector2(0.84f, 0.06f), new Vector2(1f, 0.94f));
            SmallLabel(row.remove);
            row.remove.gameObject.SetActive(false);
            queueRows.Add(row);
        }
        Tooltip.Attach((RectTransform)queueRows[0].root.transform, "What this bench is working through, top first. Several pairs of hands share a bench, so later entries can move too. Remove hands back nothing: costs are only paid on completion.");

        queueStatus = MenuBuilder.RowDescription(col.transform, "");
        queueStatus.textWrappingMode = TextWrappingModes.NoWrap;
        queueStatus.overflowMode = TextOverflowModes.Ellipsis;
        sendButton = MenuBuilder.MenuButton(col.transform, "Send your character to the bench", OnSend);
        sendButton.GetComponent<LayoutElement>().preferredHeight = 38f;
        sendLabel = sendButton.GetComponentInChildren<TextMeshProUGUI>();
        sendLabel.fontSize = MenuStyle.SmallSize + 2f;
        Tooltip.Attach((RectTransform)sendButton.transform, "A queue nobody stands at does not move. An idle colonist comes on their own; your character can go now, and works faster.");

        MenuBuilder.Spacer(col.transform, 2f);
        MenuBuilder.FitPanelHeight(Panel, col);
        drag.Clamp();
        Panel.gameObject.SetActive(false);
    }

    static TextMeshProUGUI CostLabel(RectTransform slot, float x0, float x1)
    {
        TextMeshProUGUI t = MenuBuilder.Label(slot, "", MenuStyle.SmallSize, MenuStyle.TextMuted, TextAlignmentOptions.MidlineLeft);
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        RectTransform rt = t.rectTransform;
        rt.anchorMin = new Vector2(x0, 0f);
        rt.anchorMax = new Vector2(x1, 1f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return t;
    }

    static void SmallLabel(Button b)
    {
        TextMeshProUGUI t = b.GetComponentInChildren<TextMeshProUGUI>();
        if (t == null) return;
        t.fontSize = MenuStyle.SmallSize;
        t.textWrappingMode = TextWrappingModes.NoWrap;
    }

    /// <summary>Which rows show: the filter, and what this bench lists at all (its speed table and tier).</summary>
    void ApplyFilter()
    {
        MenuBuilder.TintTabs(filterButtons, filter);
        bool make = filter == FilterMake;
        for (int i = 0; i < craftRows.Count; i++)
        {
            CraftRow row = craftRows[i];
            row.listed = station != null && station.Lists(row.recipe);
            bool show = make && row.listed;
            if (row.root.activeSelf != show) row.root.SetActive(show);
        }
        for (int i = 0; i < researchRows.Count; i++)
        {
            ResearchRow row = researchRows[i];
            row.listed = station != null && station.Lists(row.def);
            bool show = !make && row.listed;
            if (row.root.activeSelf != show) row.root.SetActive(show);
        }
        Tooltip.HideNow();
    }

    // ------------------------------------------------------------------
    // Refresh
    // ------------------------------------------------------------------

    void UpdateDisplay()
    {
        PlayerCharacter pc = PlayerCharacter.Instance;
        if (filter == FilterMake) UpdateCraft(pc); else UpdateResearch(pc);
        UpdateQueue(pc);
    }

    void UpdateCraft(PlayerCharacter pc)
    {
        Inventory hands = pc != null ? pc.Inventory : null;
        Inventory stock = fire.Stockpile;
        bool canQueue = pc != null && !pc.IsKnockedOut;

        for (int i = 0; i < craftRows.Count; i++)
        {
            CraftRow row = craftRows[i];
            if (!row.listed) continue;
            CraftingCatalog.Recipe r = row.recipe;

            int queued = station.Queued(r);
            // 0 locked (research first), 1 made (once per run), 2 queued once-tool, 3 unaffordable, 4 affordable
            int state;
            if (!r.UnlockedFor(Factions.Player.Knowledge)) state = 0;
            else if (r.oncePerRun && r.made) state = 1;
            else if (r.oncePerRun && queued > 0) state = 2;
            else state = r.CanAfford(hands, stock) ? 4 : 3;

            if (state != row.stateLast)
            {
                row.stateLast = state;
                switch (state)
                {
                    case 0:
                        row.cost.text = "needs " + r.RequiredTitle;
                        row.cost.color = MenuStyle.TextAccent;
                        row.oneLabel.text = "Craft";
                        break;
                    case 1:
                        row.cost.text = r.CostText;
                        row.cost.color = MenuStyle.TextMuted;
                        row.oneLabel.text = "Made";
                        break;
                    case 2:
                        row.cost.text = r.CostText;
                        row.cost.color = MenuStyle.TextMuted;
                        row.oneLabel.text = "Queued";
                        break;
                    default:
                        row.cost.text = r.CostText;
                        row.cost.color = state == 4 ? MenuStyle.TextPrimary : MenuStyle.TextDanger;
                        row.oneLabel.text = "Craft";
                        break;
                }
            }

            if (queued != row.queuedLast)
            {
                row.queuedLast = queued;
                row.label.text = queued > 0 && !r.oncePerRun
                    ? r.title + "  <size=78%><color=#" + LockHex + ">×" + queued + "</color></size>"
                    : r.title;
            }

            bool open = state >= 3 && canQueue;
            row.one.interactable = open;
            row.five.interactable = open;
        }

        SetListStatus(StationStatus(pc, "Costs are paid when each item is finished; a short entry waits for what is missing."), pc);
    }

    void UpdateResearch(PlayerCharacter pc)
    {
        Inventory hands = pc != null ? pc.Inventory : null;
        Inventory stock = fire.Stockpile;
        bool canQueue = pc != null && !pc.IsKnockedOut;

        for (int i = 0; i < researchRows.Count; i++)
        {
            ResearchRow row = researchRows[i];
            if (!row.listed) continue;
            ResearchCatalog.ResearchDef d = row.def;

            // 0 done, 1 prerequisite missing, 2 queued, 3 unaffordable, 4 affordable
            int state;
            if (Factions.Player.Knowledge.IsDone(d)) state = 0;
            else if (!Factions.Player.Knowledge.IsAvailable(d)) state = 1;
            else if (CraftStation.IsQueuedAnywhere(d, Factions.Player)) state = 2;
            else state = d.CanAfford(hands, stock) ? 4 : 3;

            if (state != row.stateLast)
            {
                row.stateLast = state;
                switch (state)
                {
                    case 0:
                        row.cost.text = "done";
                        row.cost.color = MenuStyle.TextMuted;
                        row.buttonLabel.text = "Done";
                        break;
                    case 1:
                        row.cost.text = "needs " + Factions.Player.Knowledge.PrerequisiteTitle(d);
                        row.cost.color = MenuStyle.TextAccent;
                        row.buttonLabel.text = "Research";
                        break;
                    case 2:
                        row.cost.text = d.CostText;
                        row.cost.color = MenuStyle.TextMuted;
                        row.buttonLabel.text = "Queued";
                        break;
                    default:
                        row.cost.text = d.CostText;
                        row.cost.color = state == 4 ? MenuStyle.TextPrimary : MenuStyle.TextDanger;
                        row.buttonLabel.text = "Research";
                        break;
                }
            }

            row.button.interactable = state >= 3 && canQueue;
        }

        SetListStatus(StationStatus(pc, "Research once and the whole colony knows it. Costs are paid on completion."), pc);
    }

    void SetListStatus(string status, PlayerCharacter pc)
    {
        if (status == listStatusLast) return;
        listStatusLast = status;
        listStatus.text = status;
        listStatus.color = pc != null && pc.WorkingStation == station ? MenuStyle.TextAccent : MenuStyle.TextMuted;
    }

    void UpdateQueue(PlayerCharacter pc)
    {
        IReadOnlyList<CraftStation.QueueEntry> q = station.Queue;
        int shown = Mathf.Min(q.Count, queueRows.Count);
        if (station.Version != queueVersionShown)
        {
            queueVersionShown = station.Version;
            for (int i = 0; i < queueRows.Count; i++)
            {
                bool on = i < shown;
                if (queueRows[i].remove.gameObject.activeSelf != on) queueRows[i].remove.gameObject.SetActive(on);
                queueRows[i].textLast = null;
                if (!on) queueRows[i].text.text = "";
            }
        }

        for (int i = 0; i < shown; i++)
        {
            CraftStation.QueueEntry e = q[i];
            int pct = Mathf.FloorToInt(e.Progress01 * 100f);
            string text;
            bool last = i == shown - 1 && q.Count > shown;
            if (last)
                text = "… and " + (q.Count - shown + 1) + " more";
            else if (i == 0)
                text = (e.IsResearch ? "Researching " : "Crafting ") + e.Title + (e.remaining > 1 ? " ×" + e.remaining : "") + "  " + pct + "%";
            else
                text = e.Title + (e.remaining > 1 ? " ×" + e.remaining : "") + (pct > 0 ? "  " + pct + "%" : "");
            if (text != queueRows[i].textLast)
            {
                queueRows[i].textLast = text;
                queueRows[i].text.text = text;
                queueRows[i].text.color = i == 0 ? MenuStyle.TextAccent : MenuStyle.TextPrimary;
            }
            if (last && queueRows[i].remove.gameObject.activeSelf) queueRows[i].remove.gameObject.SetActive(false);
        }

        string status;
        Color color = MenuStyle.TextMuted;
        bool wantSend = false;
        if (q.Count == 0)
        {
            status = "Nothing queued — MAKE and LEARN add work here.";
        }
        else if (station.Status.Length > 0)
        {
            status = station.Status + (station.IsWorked ? "" : "  ·  no one at the bench");
            color = MenuStyle.TextDanger;
            wantSend = pc != null && pc.WorkingStation != station;
        }
        else if (station.IsWorked)
        {
            int hands = station.LaborerCount;
            bool you = station.PlayerAtBench;
            int colonists = you ? hands - 1 : hands;
            status = you
                ? (colonists == 0 ? "Your character is at the bench."
                    : colonists == 1 ? "Your character and a colonist are at the bench."
                    : "Your character and " + colonists + " colonists are at the bench.")
                : (colonists == 1 ? "A colonist is at the bench." : colonists + " colonists are at the bench.");
            color = MenuStyle.TextAccent;
        }
        else if (pc != null && pc.WalkingToStation == station)
        {
            status = "Your character is on the way.";
        }
        else if (station.ClaimCount > 0)
        {
            status = station.ClaimCount == 1 ? "A colonist is on the way." : station.ClaimCount + " colonists are on the way.";
        }
        else
        {
            status = "No one at the bench — an idle colonist will come, or send your character.";
            color = MenuStyle.TextAccent;
            wantSend = pc != null;
        }

        if (status != queueStatusLast)
        {
            queueStatusLast = status;
            queueStatus.text = status;
            queueStatus.color = color;
        }

        // The Send button is always there (a fixed height means no refit); it just greys out
        bool sendActive = wantSend && pc != null && !pc.IsKnockedOut && pc.WalkingToStation != station;
        string label = pc == null ? "No character to send"
            : pc.IsKnockedOut ? "Your character is knocked out"
            : pc.WorkingStation == station ? "Your character is at the bench"
            : pc.WalkingToStation == station ? "Your character is on the way"
            : "Send your character to the bench";
        if (label != sendLabelLast)
        {
            sendLabelLast = label;
            sendLabel.text = label;
        }
        if (sendButton.interactable != sendActive) sendButton.interactable = sendActive;
    }

    /// <summary>The line under the list: what the character is doing about this bench.</summary>
    string StationStatus(PlayerCharacter pc, string idleHint)
    {
        if (pc == null) return "No character to work the bench.";
        if (pc.IsKnockedOut) return "Your character is knocked out.";
        if (pc.WorkingStation == station)
        {
            CraftStation.QueueEntry e = station.EntryOf(pc);
            if (e == null) return "Your character is at the bench.";
            if (station.Status.Length > 0) return station.Status;
            return (e.IsResearch ? "Researching " : "Crafting ") + e.Title + "…  " + Mathf.RoundToInt(station.Progress01Of(pc) * 100f) + "%";
        }
        if (pc.WalkingToStation == station) return "Walking to the bench…";
        if (station.HasWork) return "Queued " + station.Queue.Count + " — nobody at the bench yet.";
        return idleHint;
    }

    // ------------------------------------------------------------------
    // Actions
    // ------------------------------------------------------------------

    void Click() { if (AudioManager.Instance != null) AudioManager.Instance.PlayButtonClick(); }

    void OnFilter(int index)
    {
        if (index == filter) return;
        filter = index;
        ApplyFilter();
        if (filter == FilterLearn) DevQuests.Signal("bench:learn");
        Click();
        if (IsOpen && station != null) UpdateDisplay();
    }

    void OnCraft(CraftingCatalog.Recipe recipe, int count)
    {
        if (station == null || PlayerCharacter.Instance == null) return;
        if (PlayerCharacter.Instance.TryQueueCraft(recipe, station, count)) Click();
        UpdateDisplay();
    }

    void OnResearch(ResearchCatalog.ResearchDef def)
    {
        if (station == null || PlayerCharacter.Instance == null) return;
        if (PlayerCharacter.Instance.TryQueueResearch(def, station)) Click();
        UpdateDisplay();
    }

    void OnRemove(int index)
    {
        if (station == null) return;
        station.RemoveAt(index);
        Click();
        UpdateDisplay();
    }

    void OnSend()
    {
        if (station == null || PlayerCharacter.Instance == null) return;
        PlayerCharacter.Instance.WorkAt(station);
        Click();
        UpdateDisplay();
    }
}
