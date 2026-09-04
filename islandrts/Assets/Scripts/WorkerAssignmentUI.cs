using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;

/// <summary>
/// The campfire panel — and, since the research split (2026-09-03), the station
/// panel for every bench. Tabs: <b>Colonists</b> (jobs, warriors, housing),
/// <b>Stockpile</b> (the campfire inventory), <b>Craft</b> (repeatable recipes at
/// this bench), <b>Research</b> (this bench's tier of the tech tree) and
/// <b>Queue</b> (what the bench is working through, and who is at it). Clicking
/// the campfire shows all five; clicking a Workshop opens the same panel with only
/// the three station tabs, for that station.
/// </summary>
/// <remarks>
/// Built entirely in code on the menu system's widgets (2026-09-01) — no scene
/// wiring. Display only — the campfire owns the roster, the station owns its
/// queue, the catalogs own the definitions. Buttons disable themselves when an
/// action cannot happen, and every label is dirty-checked so an open panel costs
/// nothing while idle. Esc closes it (PauseController defers to <see cref="IsOpen"/>),
/// as does clicking X. Never built under the balance sim.
/// </remarks>
public class WorkerAssignmentUI : MonoBehaviour
{
    public static WorkerAssignmentUI Instance { get; private set; }

    /// <summary>True while the panel is showing — PauseController lets Esc close it instead of pausing.</summary>
    public static bool IsOpen => Instance != null && Instance.panel != null && Instance.panel.gameObject.activeSelf;

    const int TabColonists = 0, TabStockpile = 1, TabCraft = 2, TabResearch = 3, TabQueue = 4;
    const int QueueRowsShown = 8;
    const float StationScrollHeight = 470f;

    private BaseBuilding baseBuilding;   // the campfire: roster, stockpile
    private CraftStation station;        // the bench the station tabs show
    private bool stationOnly;            // a Workshop: no Colonists / Stockpile tabs
    private RectTransform panel;
    private DraggablePanel drag;
    private TextMeshProUGUI titleText;

    /// <summary>Bottom-left corner, in reference pixels, when the player has never moved it.</summary>
    static readonly Vector2 DefaultPanelPos = new Vector2(16f, 16f);
    const string PanelPosKey = "ui.campfirePanel";

    private class JobRow
    {
        public ResourceNode.ResourceType type;
        public string name;
        public TextMeshProUGUI label;
        public TextMeshProUGUI count;
        public Button minus, plus;
        public int last = -1;
        public int lockedLast = -1;   // -1 unknown, 0 open, 1 locked
    }

    // Craft tab: one row group per recipe (title + buttons, effect, cost)
    private class CraftRow
    {
        public CraftingCatalog.Recipe recipe;
        public GameObject root;
        public TextMeshProUGUI label;
        public Button one, five;
        public TextMeshProUGUI oneLabel;
        public TextMeshProUGUI cost;
        public int stateLast = -1;
        public int queuedLast = -1;
        public bool visible = true;
    }

    // Research tab: one row group per entry
    private class ResearchRow
    {
        public ResearchCatalog.ResearchDef def;
        public GameObject root;
        public Button button;
        public TextMeshProUGUI buttonLabel;
        public TextMeshProUGUI cost;
        public int stateLast = -1;
        public bool visible = true;
    }

    // Queue tab: a fixed pool of rows reassigned to whatever is queued
    private class QueueRow
    {
        public GameObject root;
        public TextMeshProUGUI text;
        public Button remove;
        public string textLast;
    }

    private class StockRow
    {
        public ItemDef item;
        public TextMeshProUGUI count;
        public int last = -1;
    }

    static readonly string LockHex = ColorUtility.ToHtmlStringRGBA(new Color(0.62f, 0.60f, 0.57f, 0.9f));

    private readonly List<JobRow> jobs = new List<JobRow>();
    private readonly List<StockRow> stockRows = new List<StockRow>();
    private TextMeshProUGUI stockpileRoom;
    private int stockpileHeldShown = -1, stockpileCapShown = -1;
    private readonly List<CraftRow> craftRows = new List<CraftRow>();
    private readonly List<ResearchRow> researchRows = new List<ResearchRow>();
    private readonly List<QueueRow> queueRows = new List<QueueRow>();

    private TextMeshProUGUI warriorCount, warriorCost, housingText, colonistText;
    private Button warriorMinus, warriorPlus;
    private TextMeshProUGUI craftStatus, researchStatus, queueStatus;
    private Button sendButton;
    private string craftStatusLast, researchStatusLast, queueStatusLast;
    private int queueVersionShown = -1;
    private int queueRowsActive = -1;

    private VerticalLayoutGroup mainColumn;
    private Button[] tabs;
    private VerticalLayoutGroup colonistsBody, stockpileBody, craftBody, researchBody, queueBody;
    private int activeTab;

    private int lastWarriors = -1, lastHousingUsed = -1, lastHousingCap = -1;
    private int lastColonists = -1, lastIdle = -1, lastArrival = -1;
    private int warriorLineLast = int.MinValue;
    private bool lastCanRecruit;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (SimHooks.Simulating) { enabled = false; return; }
        Build();
        panel.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------
    // Open / close
    // ------------------------------------------------------------------

    /// <summary>
    /// Opens the panel for the campfire (all five tabs). Called from BaseBuilding
    /// when the player clicks it, from the HUD, and after the character deposits.
    /// </summary>
    public void OpenPanel(BaseBuilding building)
    {
        if (building == null) return;
        Open(building, building.Station, stationOnly: false);
    }

    /// <summary>
    /// Opens the panel for a bench. The campfire's own bench opens the full
    /// panel; any other station (the Workshop) shows only Craft · Research · Queue.
    /// </summary>
    public void OpenStation(CraftStation st)
    {
        if (st == null) return;
        BaseBuilding fire = BaseBuilding.FindAlive();
        if (fire == null) return;
        if (st == fire.Station) { OpenPanel(fire); return; }
        Open(fire, st, stationOnly: true);
    }

    void Open(BaseBuilding fire, CraftStation st, bool stationOnly)
    {
        if (SimHooks.Simulating) return;
        if (panel == null) Build();

        bool changed = baseBuilding != fire || station != st || this.stationOnly != stationOnly;
        baseBuilding = fire;
        station = st;
        this.stationOnly = stationOnly;

        titleText.text = stationOnly && st != null ? st.displayName.ToUpperInvariant() : "CAMPFIRE";
        tabs[TabColonists].gameObject.SetActive(!stationOnly);
        tabs[TabStockpile].gameObject.SetActive(!stationOnly);

        // Invalidate dirty caches so everything refreshes for this building / bench
        for (int i = 0; i < jobs.Count; i++) { jobs[i].last = -1; jobs[i].lockedLast = -1; }
        for (int i = 0; i < stockRows.Count; i++) stockRows[i].last = -1;
        for (int i = 0; i < craftRows.Count; i++) { craftRows[i].stateLast = -1; craftRows[i].queuedLast = -1; }
        for (int i = 0; i < researchRows.Count; i++) researchRows[i].stateLast = -1;
        for (int i = 0; i < queueRows.Count; i++) queueRows[i].textLast = null;
        lastWarriors = lastHousingUsed = lastHousingCap = -1;
        lastColonists = lastIdle = lastArrival = -1;
        warriorLineLast = int.MinValue;
        craftStatusLast = researchStatusLast = queueStatusLast = null;
        queueVersionShown = -1;
        queueRowsActive = -1;

        panel.gameObject.SetActive(true);
        RefreshVisibility();

        int tab = activeTab;
        if (stationOnly && tab < TabCraft) tab = TabCraft;
        if (changed || tab != activeTab) SwitchTab(tab, silent: true);
        else
        {
            MenuBuilder.FitPanelHeight(panel, mainColumn);
            drag.Clamp();   // window may have been resized since the last open
        }
        UpdateDisplay();
    }

    public void ClosePanel()
    {
        if (panel != null) panel.gameObject.SetActive(false);
        baseBuilding = null;
        station = null;

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayButtonClick();
        }
    }

    void Update()
    {
        if (panel == null || !panel.gameObject.activeSelf) return;

        if (baseBuilding == null || station == null || !station.IsAlive)   // destroyed while open
        {
            panel.gameObject.SetActive(false);
            baseBuilding = null;
            station = null;
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            ClosePanel();
            return;
        }

        UpdateDisplay();
    }

    // ------------------------------------------------------------------
    // Construction
    // ------------------------------------------------------------------

    void Build()
    {
        // Root-level canvas on purpose (a canvas nested in a canvas ignores its own scaler)
        Canvas canvas = MenuBuilder.CreateCanvas("CampfireCanvas", 60);

        panel = MenuBuilder.Panel(canvas.transform, "CampfirePanel", 640f, 100f);
        // Bottom-left by default (2026-09-01), and draggable by its title bar:
        // DraggablePanel needs the panel anchored + pivoted bottom-left so the
        // anchored position is the corner it clamps and saves
        panel.anchorMin = panel.anchorMax = Vector2.zero;
        panel.pivot = Vector2.zero;
        panel.anchoredPosition = DefaultPanelPos;

        VerticalLayoutGroup col = MenuBuilder.Column(panel, 4f);

        // Title row with an X — also the drag handle
        RectTransform title = MenuBuilder.SettingRow(col.transform, "CAMPFIRE", out RectTransform titleSlot, 40f);
        drag = DraggablePanel.Attach(title, panel, PanelPosKey, DefaultPanelPos);
        titleText = title.GetComponentInChildren<TextMeshProUGUI>();
        titleText.color = MenuStyle.TextAccent;
        titleText.fontSize = MenuStyle.HeadingSize - 4f;
        titleText.characterSpacing = 3f;
        SmallButton(titleSlot, "X", ClosePanel, new Vector2(0.84f, 0.1f), new Vector2(1f, 0.9f));
        MenuBuilder.Divider(col.transform);

        mainColumn = col;
        tabs = MenuBuilder.TabRow(col.transform, new[] { "Colonists", "Stockpile", "Craft", "Research", "Queue" }, 0, i => SwitchTab(i, silent: false));

        // Each tab is a nested column with no padding of its own (the outer column
        // already pads the panel); only one is active at a time.
        RectOffset none = new RectOffset(0, 0, 0, 0);
        colonistsBody = MenuBuilder.Column(col.transform, 4f, none);
        stockpileBody = MenuBuilder.Column(col.transform, 4f, none);
        craftBody = MenuBuilder.Column(col.transform, 4f, none);
        researchBody = MenuBuilder.Column(col.transform, 4f, none);
        queueBody = MenuBuilder.Column(col.transform, 4f, none);

        BuildColonistsTab(colonistsBody.transform);
        BuildStockpileTab(stockpileBody.transform);
        BuildCraftTab(craftBody.transform);
        BuildResearchTab(researchBody.transform);
        BuildQueueTab(queueBody.transform);

        stockpileBody.gameObject.SetActive(false);
        craftBody.gameObject.SetActive(false);
        researchBody.gameObject.SetActive(false);
        queueBody.gameObject.SetActive(false);

        MenuBuilder.Spacer(col.transform, 4f);
        MenuBuilder.FitPanelHeight(panel, col);
        drag.Clamp();   // a saved position from a larger window must not leave it off-screen
    }

    void BuildColonistsTab(Transform body)
    {
        MenuBuilder.SectionHeader(body, "Colonists");
        colonistText = MenuBuilder.RowDescription(body, "");
        housingText = MenuBuilder.RowDescription(body, "Housing 0 / 0");

        MenuBuilder.SectionHeader(body, "Jobs");
        jobs.Add(MakeJobRow(body, ResourceNode.ResourceType.Wood, "Wood cutters"));
        jobs.Add(MakeJobRow(body, ResourceNode.ResourceType.Food, "Foragers"));
        jobs.Add(MakeJobRow(body, ResourceNode.ResourceType.Stone, "Quarriers"));
        jobs.Add(MakeJobRow(body, ResourceNode.ResourceType.Metal, "Miners"));
        MenuBuilder.RowDescription(body, "Idle colonists build and repair. + gives one a job, − sends them back.");

        MenuBuilder.SectionHeader(body, "Defence");
        MenuBuilder.SettingRow(body, "Warriors", out RectTransform wslot);
        CounterControls(wslot, OnWarriorMinusClicked, OnWarriorPlusClicked, out warriorCount, out warriorMinus, out warriorPlus);
        warriorCost = MenuBuilder.RowDescription(body, "");
    }

    /// <summary>One count row per stockpiled item, in catalog order.</summary>
    void BuildStockpileTab(Transform body)
    {
        MenuBuilder.SectionHeader(body, "Campfire stockpile");
        MenuBuilder.RowDescription(body, "What your character and your colonists bring in, and everything the benches make.");
        stockpileRoom = MenuBuilder.ValueRow(body, "Room", "0 / 0");

        ItemDef[] items = ItemCatalog.Stockpiled;
        for (int i = 0; i < items.Length; i++)
        {
            StockRow row = new StockRow { item = items[i] };
            row.count = MenuBuilder.ValueRow(body, items[i].displayName, "0");
            stockRows.Add(row);
        }

        MenuBuilder.RowDescription(body, "Right-click the fire with your character to deposit what they carry.");
    }

    /// <summary>
    /// One row group per recipe: title + Craft / ×5 buttons, then the effect and
    /// the cost line (coloured by affordability). Costs come from the character's
    /// hands and the stockpile together and are paid when the item is finished;
    /// the buttons queue at this bench and send the character to work it.
    /// </summary>
    void BuildCraftTab(Transform body)
    {
        MenuBuilder.SectionHeader(body, "Craft");
        VerticalLayoutGroup list = MenuBuilder.ScrollColumn(body, 2f, StationScrollHeight);
        RectOffset none = new RectOffset(0, 0, 0, 0);

        var recipes = CraftingCatalog.All;
        for (int i = 0; i < recipes.Length; i++)
        {
            CraftRow row = new CraftRow { recipe = recipes[i] };
            VerticalLayoutGroup group = MenuBuilder.Column(list.transform, 2f, none);
            row.root = group.gameObject;

            RectTransform rt = MenuBuilder.SettingRow(group.transform, recipes[i].title, out RectTransform slot);
            row.label = rt.GetComponentInChildren<TextMeshProUGUI>();
            CraftingCatalog.Recipe captured = recipes[i];
            row.one = SmallButton(slot, "Craft", () => OnCraftClicked(captured, 1), new Vector2(0.44f, 0.1f), new Vector2(0.74f, 0.9f));
            row.oneLabel = row.one.GetComponentInChildren<TextMeshProUGUI>();
            row.five = SmallButton(slot, "×5", () => OnCraftClicked(captured, 5), new Vector2(0.77f, 0.1f), new Vector2(1f, 0.9f));
            if (recipes[i].oncePerRun) row.five.gameObject.SetActive(false);

            MenuBuilder.RowDescription(group.transform, recipes[i].description);
            row.cost = MenuBuilder.RowDescription(group.transform, recipes[i].CostText);
            craftRows.Add(row);
        }

        craftStatus = MenuBuilder.RowDescription(body, "");
    }

    /// <summary>One row group per research entry of this bench's tier.</summary>
    void BuildResearchTab(Transform body)
    {
        MenuBuilder.SectionHeader(body, "Research");
        VerticalLayoutGroup list = MenuBuilder.ScrollColumn(body, 2f, StationScrollHeight);
        RectOffset none = new RectOffset(0, 0, 0, 0);

        var defs = ResearchCatalog.All;
        for (int i = 0; i < defs.Length; i++)
        {
            ResearchRow row = new ResearchRow { def = defs[i] };
            VerticalLayoutGroup group = MenuBuilder.Column(list.transform, 2f, none);
            row.root = group.gameObject;

            MenuBuilder.SettingRow(group.transform, defs[i].title, out RectTransform slot);
            ResearchCatalog.ResearchDef captured = defs[i];
            row.button = SmallButton(slot, "Research", () => OnResearchClicked(captured), new Vector2(0.50f, 0.1f), new Vector2(1f, 0.9f));
            row.buttonLabel = row.button.GetComponentInChildren<TextMeshProUGUI>();

            MenuBuilder.RowDescription(group.transform, defs[i].description);
            row.cost = MenuBuilder.RowDescription(group.transform, defs[i].CostText);
            researchRows.Add(row);
        }

        researchStatus = MenuBuilder.RowDescription(body, "");
    }

    /// <summary>A pool of queue rows plus the "who is at the bench" line and a Send button.</summary>
    void BuildQueueTab(Transform body)
    {
        MenuBuilder.SectionHeader(body, "Queue");

        for (int i = 0; i < QueueRowsShown; i++)
        {
            QueueRow row = new QueueRow();
            RectTransform rt = MenuBuilder.SettingRow(body, "", out RectTransform slot);
            row.root = rt.gameObject;
            row.text = rt.GetComponentInChildren<TextMeshProUGUI>();
            int index = i;
            row.remove = SmallButton(slot, "Remove", () => OnRemoveClicked(index), new Vector2(0.56f, 0.1f), new Vector2(1f, 0.9f));
            row.root.SetActive(false);
            queueRows.Add(row);
        }

        queueStatus = MenuBuilder.RowDescription(body, "");
        sendButton = MenuBuilder.MenuButton(body, "Send your character to the bench", OnSendClicked);
    }

    // ------------------------------------------------------------------
    // Tabs and rows
    // ------------------------------------------------------------------

    void SwitchTab(int index, bool silent)
    {
        if (stationOnly && index < TabCraft) index = TabCraft;
        activeTab = index;
        colonistsBody.gameObject.SetActive(index == TabColonists);
        stockpileBody.gameObject.SetActive(index == TabStockpile);
        craftBody.gameObject.SetActive(index == TabCraft);
        researchBody.gameObject.SetActive(index == TabResearch);
        queueBody.gameObject.SetActive(index == TabQueue);
        MenuBuilder.TintTabs(tabs, index);

        // The bodies differ in height: refit the panel and keep it on screen
        MenuBuilder.FitPanelHeight(panel, mainColumn);
        drag.Clamp();

        if (!silent && AudioManager.Instance != null) AudioManager.Instance.PlayButtonClick();
        UpdateDisplay();
    }

    /// <summary>Which recipes and research entries this bench lists at all (its speed table and tier).</summary>
    void RefreshVisibility()
    {
        if (station == null) return;
        for (int i = 0; i < craftRows.Count; i++)
        {
            bool show = station.Lists(craftRows[i].recipe);
            if (show != craftRows[i].visible)
            {
                craftRows[i].visible = show;
                craftRows[i].root.SetActive(show);
            }
        }
        for (int i = 0; i < researchRows.Count; i++)
        {
            bool show = station.Lists(researchRows[i].def);
            if (show != researchRows[i].visible)
            {
                researchRows[i].visible = show;
                researchRows[i].root.SetActive(show);
            }
        }
    }

    JobRow MakeJobRow(Transform parent, ResourceNode.ResourceType type, string label)
    {
        JobRow row = new JobRow { type = type, name = label };
        RectTransform rt = MenuBuilder.SettingRow(parent, label, out RectTransform slot);
        row.label = rt.GetComponentInChildren<TextMeshProUGUI>();   // the row's caption, before any controls are added

        // Colour swatch in front of the label, matching the HUD chip
        RectTransform sw = MenuBuilder.SimpleImage(rt, "Swatch", ResourceUI.ColorFor(type));
        sw.anchorMin = new Vector2(0f, 0.25f);
        sw.anchorMax = new Vector2(0f, 0.75f);
        sw.pivot = new Vector2(0f, 0.5f);
        sw.anchoredPosition = new Vector2(-14f, 0f);
        sw.sizeDelta = new Vector2(8f, 0f);

        CounterControls(slot, () => OnMinusClicked(type), () => OnPlusClicked(type), out row.count, out row.minus, out row.plus);
        return row;
    }

    /// <summary>[ − ]  count  [ + ] inside a row's control slot.</summary>
    static void CounterControls(RectTransform slot, Action onMinus, Action onPlus,
        out TextMeshProUGUI count, out Button minus, out Button plus)
    {
        minus = SmallButton(slot, "−", onMinus, new Vector2(0.20f, 0.1f), new Vector2(0.40f, 0.9f));
        plus = SmallButton(slot, "+", onPlus, new Vector2(0.72f, 0.1f), new Vector2(0.92f, 0.9f));

        count = MenuBuilder.Label(slot, "0", MenuStyle.BodySize, MenuStyle.TextAccent);
        RectTransform crt = count.rectTransform;
        crt.anchorMin = new Vector2(0.42f, 0f);
        crt.anchorMax = new Vector2(0.70f, 1f);
        crt.offsetMin = Vector2.zero;
        crt.offsetMax = Vector2.zero;
    }

    static Button SmallButton(RectTransform parent, string text, Action onClick, Vector2 anchorMin, Vector2 anchorMax)
    {
        Button b = MenuBuilder.MenuButton(parent, text, onClick);
        UnityEngine.Object.Destroy(b.GetComponent<LayoutElement>());   // anchored, not laid out
        RectTransform rt = b.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return b;
    }

    // ------------------------------------------------------------------
    // Refresh
    // ------------------------------------------------------------------

    void UpdateDisplay()
    {
        if (baseBuilding == null || station == null) return;

        switch (activeTab)
        {
            case TabStockpile: UpdateStockpileTab(); return;
            case TabCraft: UpdateCraftTab(); return;
            case TabResearch: UpdateResearchTab(); return;
            case TabQueue: UpdateQueueTab(); return;
        }

        UpdateColonistsTab();
    }

    void UpdateStockpileTab()
    {
        Inventory stock = baseBuilding.Stockpile;

        // The cap is what tells a player their haulers are about to start
        // wasting trips; it grows with Storage Pits and Racks and Baskets.
        int held = stock.TotalCount;
        int cap = stock.Capacity;
        if (held != stockpileHeldShown || cap != stockpileCapShown)
        {
            stockpileHeldShown = held;
            stockpileCapShown = cap;
            stockpileRoom.text = held + " / " + cap;
            stockpileRoom.color = held >= cap ? MenuStyle.TextDanger : MenuStyle.TextAccent;
        }

        for (int i = 0; i < stockRows.Count; i++)
        {
            StockRow row = stockRows[i];
            int n = stock.Count(row.item);
            if (n == row.last) continue;
            row.last = n;
            row.count.text = n.ToString();
            row.count.color = n > 0 ? MenuStyle.TextAccent : MenuStyle.TextMuted;
        }
    }

    void UpdateColonistsTab()
    {
        PopulationManager pm = PopulationManager.Instance;
        int housingCap = pm != null ? pm.GetHousingCapacity() : 0;
        int colonists = pm != null ? pm.GetColonistCount() : baseBuilding.GetTotalWorkers();
        int idle = pm != null ? pm.GetIdleCount() : 0;
        bool canAssign = idle > 0;   // a job needs an idle colonist — assignment never creates people

        for (int i = 0; i < jobs.Count; i++)
        {
            JobRow row = jobs[i];
            int n = WorkersOn(row.type);
            if (n != row.last)
            {
                row.last = n;
                row.count.text = n.ToString();
            }

            // A job the colony has not learned yet says which research opens it
            bool locked = !Unlocks.HasJob(row.type);
            int lockState = locked ? 1 : 0;
            if (lockState != row.lockedLast && row.label != null)
            {
                row.lockedLast = lockState;
                row.label.text = locked
                    ? row.name + "  <size=78%><color=#" + LockHex + ">research " + Unlocks.ResearchTitleFor(Unlocks.ForJob(row.type)) + "</color></size>"
                    : row.name;
            }

            row.minus.interactable = n > 0;
            row.plus.interactable = canAssign && !locked;
        }

        // Warriors need Spearcraft, then a spear in the stockpile
        bool militia = Unlocks.Has(Unlocks.Kind.Militia);
        int weapons = baseBuilding.WeaponsInStock();
        int warriorLine = militia ? weapons : -1;
        if (warriorLine != warriorLineLast)
        {
            warriorLineLast = warriorLine;
            if (!militia)
            {
                warriorCost.text = "Research " + Unlocks.ResearchTitleFor(Unlocks.Kind.Militia) + " at the fire to arm warriors";
                warriorCost.color = MenuStyle.TextAccent;
            }
            else
            {
                warriorCost.text = baseBuilding.warriorCost_Food + " food  ·  one idle colonist  ·  a spear from the stockpile ("
                    + weapons + " in stock)";
                warriorCost.color = weapons > 0 ? MenuStyle.TextMuted : MenuStyle.TextAccent;
            }
        }

        // "5 colonists · 2 idle · next survivor in 12s"
        int arrival = pm != null ? Mathf.CeilToInt(pm.SecondsToNextArrival) : -1;
        if (colonists != lastColonists || idle != lastIdle || arrival != lastArrival)
        {
            lastColonists = colonists;
            lastIdle = idle;
            lastArrival = arrival;
            string line = colonists + (colonists == 1 ? " colonist" : " colonists") + "  ·  " + idle + " idle";
            if (arrival >= 0) line += "  ·  next survivor in " + arrival + "s";
            else if (colonists < housingCap) line += "  ·  survivors land by day";
            colonistText.text = line;
            colonistText.color = idle > 0 ? MenuStyle.TextPrimary : MenuStyle.TextMuted;
        }

        if (colonists != lastHousingUsed || housingCap != lastHousingCap)
        {
            lastHousingUsed = colonists;
            lastHousingCap = housingCap;
            housingText.text = "Housing " + colonists + " / " + housingCap
                + (colonists >= housingCap ? "  —  build a hut and more survivors will come ashore" : "");
            housingText.color = colonists >= housingCap ? MenuStyle.TextAccent : MenuStyle.TextMuted;
        }

        int warriors = baseBuilding.GetWarriorCount();
        if (warriors != lastWarriors)
        {
            lastWarriors = warriors;
            warriorCount.text = warriors + " / " + baseBuilding.maxWarriors;
        }

        bool canRecruit = baseBuilding.CanRecruitWarrior();
        if (canRecruit != lastCanRecruit || warriorPlus.interactable != canRecruit)
        {
            lastCanRecruit = canRecruit;
            warriorPlus.interactable = canRecruit;
        }
        warriorMinus.interactable = warriors > 0;
    }

    void UpdateCraftTab()
    {
        PlayerCharacter pc = PlayerCharacter.Instance;
        Inventory hands = pc != null ? pc.Inventory : null;
        Inventory stock = baseBuilding.Stockpile;
        bool canQueue = pc != null && !pc.IsKnockedOut;

        for (int i = 0; i < craftRows.Count; i++)
        {
            CraftRow row = craftRows[i];
            if (!row.visible) continue;
            CraftingCatalog.Recipe r = row.recipe;

            int queued = station.Queued(r);
            // 0 locked (research first), 1 made (once per run), 2 queued once-tool, 3 unaffordable, 4 affordable
            int state;
            if (!r.Unlocked) state = 0;
            else if (r.oncePerRun && r.made) state = 1;
            else if (r.oncePerRun && queued > 0) state = 2;
            else state = r.CanAfford(hands, stock) ? 4 : 3;

            if (state != row.stateLast)
            {
                row.stateLast = state;
                switch (state)
                {
                    case 0:
                        row.cost.text = "Research " + r.RequiredTitle + " first";
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
                    ? r.title + "  <size=78%><color=#" + LockHex + ">×" + queued + " queued</color></size>"
                    : r.title;
            }

            bool open = state >= 3 && canQueue;
            row.one.interactable = open;
            row.five.interactable = open;
        }

        string status = StationStatus(pc, "Costs are paid when each item is finished; a short entry waits for what is missing.");
        if (status != craftStatusLast)
        {
            craftStatusLast = status;
            craftStatus.text = status;
            craftStatus.color = pc != null && pc.WorkingStation == station ? MenuStyle.TextAccent : MenuStyle.TextMuted;
        }
    }

    void UpdateResearchTab()
    {
        PlayerCharacter pc = PlayerCharacter.Instance;
        Inventory hands = pc != null ? pc.Inventory : null;
        Inventory stock = baseBuilding.Stockpile;
        bool canQueue = pc != null && !pc.IsKnockedOut;

        for (int i = 0; i < researchRows.Count; i++)
        {
            ResearchRow row = researchRows[i];
            if (!row.visible) continue;
            ResearchCatalog.ResearchDef d = row.def;

            // 0 done, 1 prerequisite missing, 2 queued, 3 unaffordable, 4 affordable
            int state;
            if (d.done) state = 0;
            else if (!ResearchCatalog.IsAvailable(d)) state = 1;
            else if (CraftStation.IsQueuedAnywhere(d)) state = 2;
            else state = d.CanAfford(hands, stock) ? 4 : 3;

            if (state != row.stateLast)
            {
                row.stateLast = state;
                switch (state)
                {
                    case 0:
                        row.cost.text = d.CostText;
                        row.cost.color = MenuStyle.TextMuted;
                        row.buttonLabel.text = "Done";
                        break;
                    case 1:
                        row.cost.text = "Needs " + ResearchCatalog.PrerequisiteTitle(d);
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

        string status = StationStatus(pc, "Research once and the whole colony knows it. Costs are paid on completion.");
        if (status != researchStatusLast)
        {
            researchStatusLast = status;
            researchStatus.text = status;
            researchStatus.color = pc != null && pc.WorkingStation == station ? MenuStyle.TextAccent : MenuStyle.TextMuted;
        }
    }

    void UpdateQueueTab()
    {
        PlayerCharacter pc = PlayerCharacter.Instance;
        IReadOnlyList<CraftStation.QueueEntry> q = station.Queue;

        int shown = Mathf.Min(q.Count, queueRows.Count);
        if (shown != queueRowsActive || station.Version != queueVersionShown)
        {
            queueVersionShown = station.Version;
            bool refit = shown != queueRowsActive;
            queueRowsActive = shown;
            for (int i = 0; i < queueRows.Count; i++)
            {
                bool on = i < shown;
                if (queueRows[i].root.activeSelf != on) queueRows[i].root.SetActive(on);
                queueRows[i].textLast = null;
            }
            if (refit)
            {
                MenuBuilder.FitPanelHeight(panel, mainColumn);
                drag.Clamp();
            }
        }

        for (int i = 0; i < shown; i++)
        {
            CraftStation.QueueEntry e = q[i];
            string text;
            if (i == 0)
            {
                int pct = Mathf.FloorToInt(e.Progress01 * 100f);
                text = (e.IsResearch ? "Researching " : "Crafting ") + e.Title
                    + (e.remaining > 1 ? " ×" + e.remaining : "") + "  " + pct + "%";
            }
            else
            {
                text = e.Title + (e.remaining > 1 ? " ×" + e.remaining : "");
            }
            if (text != queueRows[i].textLast)
            {
                queueRows[i].textLast = text;
                queueRows[i].text.text = text;
                queueRows[i].text.color = i == 0 ? MenuStyle.TextAccent : MenuStyle.TextPrimary;
            }
        }

        string status;
        Color color = MenuStyle.TextMuted;
        bool showSend = false;
        if (q.Count == 0)
        {
            status = "Nothing queued — the Craft and Research tabs add work here.";
        }
        else if (station.Status.Length > 0)
        {
            status = station.Status + (station.IsWorked ? "" : "  ·  no one at the bench");
            color = MenuStyle.TextDanger;
            showSend = pc != null && pc.WorkingStation != station;
        }
        else if (station.IsWorked)
        {
            status = station.Laborer == (object)pc ? "Your character is at the bench." : "Someone is at the bench.";
            color = MenuStyle.TextAccent;
        }
        else if (pc != null && pc.WalkingToStation == station)
        {
            status = "Your character is on the way.";
        }
        else
        {
            status = "No one at the bench — nothing moves until someone stands here.";
            color = MenuStyle.TextAccent;
            showSend = pc != null;
        }

        if (status != queueStatusLast)
        {
            queueStatusLast = status;
            queueStatus.text = status;
            queueStatus.color = color;
        }
        bool sendActive = showSend && pc != null && !pc.IsKnockedOut && pc.WalkingToStation != station;
        if (sendButton.gameObject.activeSelf != sendActive)
        {
            sendButton.gameObject.SetActive(sendActive);
            MenuBuilder.FitPanelHeight(panel, mainColumn);
            drag.Clamp();
        }
    }

    /// <summary>The line under the Craft / Research lists: what the character is doing about this bench.</summary>
    string StationStatus(PlayerCharacter pc, string idleHint)
    {
        if (pc == null) return "No character to work the bench.";
        if (pc.IsKnockedOut) return "Your character is knocked out.";
        if (pc.WorkingStation == station)
        {
            CraftStation.QueueEntry e = station.Active;
            if (e == null) return "Your character is at the bench.";
            if (station.Status.Length > 0) return station.Status;
            return (e.IsResearch ? "Researching " : "Crafting ") + e.Title + "…  " + Mathf.RoundToInt(e.Progress01 * 100f) + "%";
        }
        if (pc.WalkingToStation == station) return "Walking to the bench…";
        if (station.HasWork) return "Queued " + station.Queue.Count + " — nobody at the bench. Queue more, or see the Queue tab.";
        return idleHint;
    }

    int WorkersOn(ResourceNode.ResourceType t)
    {
        switch (t)
        {
            case ResourceNode.ResourceType.Food: return baseBuilding.foodWorkers;
            case ResourceNode.ResourceType.Stone: return baseBuilding.stoneWorkers;
            case ResourceNode.ResourceType.Metal: return baseBuilding.metalWorkers;
            default: return baseBuilding.woodWorkers;
        }
    }

    // ------------------------------------------------------------------
    // Actions
    // ------------------------------------------------------------------

    void OnCraftClicked(CraftingCatalog.Recipe recipe, int count)
    {
        if (station == null || PlayerCharacter.Instance == null) return;
        if (PlayerCharacter.Instance.TryQueueCraft(recipe, station, count))
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlayButtonClick();
        }
        UpdateDisplay();
    }

    void OnResearchClicked(ResearchCatalog.ResearchDef def)
    {
        if (station == null || PlayerCharacter.Instance == null) return;
        if (PlayerCharacter.Instance.TryQueueResearch(def, station))
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlayButtonClick();
        }
        UpdateDisplay();
    }

    void OnRemoveClicked(int index)
    {
        if (station == null) return;
        station.RemoveAt(index);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayButtonClick();
        UpdateDisplay();
    }

    void OnSendClicked()
    {
        if (station == null || PlayerCharacter.Instance == null) return;
        PlayerCharacter.Instance.WorkAt(station);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayButtonClick();
        UpdateDisplay();
    }

    void OnPlusClicked(ResourceNode.ResourceType resourceType)
    {
        if (baseBuilding == null) return;

        // Add worker through BaseBuilding (it checks the research and the idle pool)
        baseBuilding.AssignWorker(resourceType);

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayWorkerAssigned();
        }

        UpdateDisplay();
    }

    void OnMinusClicked(ResourceNode.ResourceType resourceType)
    {
        if (baseBuilding == null) return;

        baseBuilding.UnassignWorker(resourceType);

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayButtonClick();
        }

        UpdateDisplay();
    }

    void OnWarriorPlusClicked()
    {
        if (baseBuilding == null) return;

        if (!baseBuilding.CanRecruitWarrior()) return;

        baseBuilding.SpawnWarrior();

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayButtonClick();
        }

        UpdateDisplay();
    }

    void OnWarriorMinusClicked()
    {
        if (baseBuilding == null) return;

        baseBuilding.RemoveWarrior();

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayButtonClick();
        }

        UpdateDisplay();
    }
}
