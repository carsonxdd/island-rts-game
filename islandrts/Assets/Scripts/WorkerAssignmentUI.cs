using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;

/// <summary>
/// The campfire panel: assign workers to wood, food, stone or metal and
/// recruit warriors. This is the player's main lever on the economy.
/// </summary>
/// <remarks>
/// Built entirely in code on the menu system's widgets (2026-09-01) — no
/// scene wiring, one row per job with a coloured swatch, a −/+ pair and a
/// live count, a warriors row with its cost, and a housing line. The old
/// hand-built WorkerAssignmentPanel is removed by the legacy cleanup step.
///
/// Display only — the campfire owns the worker roster and every count shown
/// here. Buttons disable themselves when an action is unaffordable or the
/// population is capped, and every label is dirty-checked so an open panel
/// costs nothing while idle. Esc closes it (PauseController defers to
/// <see cref="IsOpen"/>), as does clicking X.
/// </remarks>
public class WorkerAssignmentUI : MonoBehaviour
{
    public static WorkerAssignmentUI Instance { get; private set; }

    /// <summary>True while the panel is showing — PauseController lets Esc close it instead of pausing.</summary>
    public static bool IsOpen => Instance != null && Instance.panel != null && Instance.panel.gameObject.activeSelf;

    private BaseBuilding baseBuilding;
    private RectTransform panel;
    private DraggablePanel drag;

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

    // Craft tab (2026-09-02): one row per campfire recipe
    private class CraftRow
    {
        public CraftingCatalog.Recipe recipe;
        public Button button;
        public TextMeshProUGUI buttonLabel;
        public TextMeshProUGUI cost;
        public int stateLast = -1;   // 0 unaffordable, 1 affordable, 2 crafted
    }
    private readonly List<CraftRow> craftRows = new List<CraftRow>();
    private VerticalLayoutGroup craftBody;
    private TextMeshProUGUI craftStatus;
    private string craftStatusLast;
    private string warriorCostLine = "";
    private int warriorLockLast = -1;

    static readonly string LockHex = ColorUtility.ToHtmlStringRGBA(new Color(0.62f, 0.60f, 0.57f, 0.9f));

    private readonly List<JobRow> jobs = new List<JobRow>();
    private TextMeshProUGUI warriorCount, warriorCost, housingText, colonistText;

    // Tabs (2026-09-02): Colonists is the old panel; Stockpile shows the campfire
    // inventory the player's character deposits into.
    private VerticalLayoutGroup mainColumn;
    private Button[] tabs;
    private VerticalLayoutGroup colonistsBody, stockpileBody;
    private int activeTab;

    private class StockRow
    {
        public ItemDef item;
        public TextMeshProUGUI count;
        public int last = -1;
    }
    private readonly List<StockRow> stockRows = new List<StockRow>();
    private Button warriorMinus, warriorPlus;
    private int lastWarriors = -1, lastHousingUsed = -1, lastHousingCap = -1;
    private int lastColonists = -1, lastIdle = -1, lastArrival = -1;
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

    /// <summary>
    /// Opens the panel for a specific base building. Called from
    /// BaseBuilding when the player clicks the campfire, and from the HUD.
    /// </summary>
    public void OpenPanel(BaseBuilding building)
    {
        if (panel == null) Build();
        baseBuilding = building;

        // Invalidate dirty caches so everything refreshes for this building
        for (int i = 0; i < jobs.Count; i++) { jobs[i].last = -1; jobs[i].lockedLast = -1; }
        for (int i = 0; i < stockRows.Count; i++) stockRows[i].last = -1;
        for (int i = 0; i < craftRows.Count; i++) craftRows[i].stateLast = -1;
        lastWarriors = lastHousingUsed = lastHousingCap = -1;
        warriorLockLast = -1;
        craftStatusLast = null;

        warriorCostLine = building.warriorCost_Wood + " wood  ·  " + building.warriorCost_Food + " food  ·  one idle colonist";

        panel.gameObject.SetActive(true);
        drag.Clamp();   // window may have been resized since the last open
        UpdateDisplay();
    }

    public void ClosePanel()
    {
        if (panel != null) panel.gameObject.SetActive(false);
        baseBuilding = null;

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayButtonClick();
        }
    }

    void Update()
    {
        if (panel == null || !panel.gameObject.activeSelf) return;

        if (baseBuilding == null)  // campfire destroyed while open
        {
            panel.gameObject.SetActive(false);
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

        panel = MenuBuilder.Panel(canvas.transform, "CampfirePanel", 520f, 100f);
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
        TextMeshProUGUI titleText = title.GetComponentInChildren<TextMeshProUGUI>();
        titleText.color = MenuStyle.TextAccent;
        titleText.fontSize = MenuStyle.HeadingSize - 4f;
        titleText.characterSpacing = 3f;
        SmallButton(titleSlot, "X", ClosePanel, new Vector2(0.84f, 0.1f), new Vector2(1f, 0.9f));
        MenuBuilder.Divider(col.transform);

        mainColumn = col;
        tabs = MenuBuilder.TabRow(col.transform, new[] { "Colonists", "Stockpile", "Craft" }, 0, SwitchTab);

        // Each tab is a nested column with no padding of its own (the outer column
        // already pads the panel); only one is active at a time.
        RectOffset none = new RectOffset(0, 0, 0, 0);
        colonistsBody = MenuBuilder.Column(col.transform, 4f, none);
        stockpileBody = MenuBuilder.Column(col.transform, 4f, none);
        craftBody = MenuBuilder.Column(col.transform, 4f, none);

        Transform body = colonistsBody.transform;
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

        BuildStockpileTab(stockpileBody.transform);
        stockpileBody.gameObject.SetActive(false);

        BuildCraftTab(craftBody.transform);
        craftBody.gameObject.SetActive(false);

        MenuBuilder.Spacer(col.transform, 4f);
        MenuBuilder.FitPanelHeight(panel, col);
        drag.Clamp();   // a saved position from a larger window must not leave it off-screen
    }

    /// <summary>One count row per stockpiled item, in catalog order.</summary>
    void BuildStockpileTab(Transform body)
    {
        MenuBuilder.SectionHeader(body, "Campfire stockpile");
        MenuBuilder.RowDescription(body, "What your character has brought to the fire. Crafting draws from it.");

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
    /// One row per campfire recipe: title + Craft button, then the effect and the
    /// cost line (coloured by affordability). Costs come from the character's
    /// hands and the stockpile together; the button walks the character over.
    /// </summary>
    void BuildCraftTab(Transform body)
    {
        MenuBuilder.SectionHeader(body, "Craft at the fire");

        var recipes = CraftingCatalog.CampfireRecipes;
        for (int i = 0; i < recipes.Length; i++)
        {
            CraftRow row = new CraftRow { recipe = recipes[i] };
            MenuBuilder.SettingRow(body, recipes[i].title, out RectTransform slot);
            row.button = SmallButton(slot, "Craft", () => OnCraftClicked(row.recipe), new Vector2(0.50f, 0.1f), new Vector2(1f, 0.9f));
            row.buttonLabel = row.button.GetComponentInChildren<TextMeshProUGUI>();
            MenuBuilder.RowDescription(body, recipes[i].description);
            row.cost = MenuBuilder.RowDescription(body, recipes[i].CostText);
            craftRows.Add(row);
        }

        craftStatus = MenuBuilder.RowDescription(body, "");
    }

    void OnCraftClicked(CraftingCatalog.Recipe recipe)
    {
        if (baseBuilding == null || PlayerCharacter.Instance == null) return;
        if (PlayerCharacter.Instance.TryQueueCraft(recipe, baseBuilding))
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlayButtonClick();
        }
        UpdateDisplay();
    }

    void SwitchTab(int index)
    {
        if (index == activeTab && colonistsBody.gameObject.activeSelf == (index == 0)) return;
        activeTab = index;
        colonistsBody.gameObject.SetActive(index == 0);
        stockpileBody.gameObject.SetActive(index == 1);
        craftBody.gameObject.SetActive(index == 2);
        MenuBuilder.TintTabs(tabs, index);

        // The two bodies differ in height: refit the panel and keep it on screen
        MenuBuilder.FitPanelHeight(panel, mainColumn);
        drag.Clamp();

        if (AudioManager.Instance != null) AudioManager.Instance.PlayButtonClick();
        UpdateDisplay();
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
        if (baseBuilding == null) return;

        if (activeTab == 1)
        {
            Inventory stock = baseBuilding.Stockpile;
            for (int i = 0; i < stockRows.Count; i++)
            {
                StockRow row = stockRows[i];
                int n = stock.Count(row.item);
                if (n == row.last) continue;
                row.last = n;
                row.count.text = n.ToString();
                row.count.color = n > 0 ? MenuStyle.TextAccent : MenuStyle.TextMuted;
            }
            return;
        }

        if (activeTab == 2)
        {
            UpdateCraftTab();
            return;
        }

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

            // A job the colony has not learned yet says which tool opens it
            bool locked = !Unlocks.HasJob(row.type);
            int lockState = locked ? 1 : 0;
            if (lockState != row.lockedLast && row.label != null)
            {
                row.lockedLast = lockState;
                row.label.text = locked
                    ? row.name + "  <size=78%><color=#" + LockHex + ">needs " + Unlocks.RecipeTitleFor(Unlocks.ForJob(row.type)) + "</color></size>"
                    : row.name;
            }

            row.minus.interactable = n > 0;
            row.plus.interactable = canAssign && !locked;
        }

        // Warriors need the spear crafted first
        int militia = Unlocks.Has(Unlocks.Kind.Militia) ? 1 : 0;
        if (militia != warriorLockLast)
        {
            warriorLockLast = militia;
            warriorCost.text = militia == 1
                ? warriorCostLine
                : "Craft a " + Unlocks.RecipeTitleFor(Unlocks.Kind.Militia) + " at the fire to arm warriors";
            warriorCost.color = militia == 1 ? MenuStyle.TextMuted : MenuStyle.TextAccent;
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
        bool busy = pc == null || pc.IsKnockedOut || pc.ActiveRecipe != null || pc.QueuedRecipe != null;

        for (int i = 0; i < craftRows.Count; i++)
        {
            CraftRow row = craftRows[i];
            bool affordable = !row.recipe.crafted && CraftingCatalog.CanAfford(row.recipe, hands, stock);
            int state = row.recipe.crafted ? 2 : (affordable ? 1 : 0);
            if (state != row.stateLast)
            {
                row.stateLast = state;
                row.cost.color = state == 2 ? MenuStyle.TextMuted : (state == 1 ? MenuStyle.TextPrimary : MenuStyle.TextDanger);
                row.buttonLabel.text = state == 2 ? "Done" : "Craft";
            }
            row.button.interactable = state == 1 && !busy;
        }

        string status;
        if (pc == null) status = "No character to craft with.";
        else if (pc.IsKnockedOut) status = "Your character is knocked out.";
        else if (pc.ActiveRecipe != null) status = "Crafting " + pc.ActiveRecipe.title + "…  " + Mathf.RoundToInt(pc.CraftProgress01 * 100f) + "%";
        else if (pc.QueuedRecipe != null) status = "Walking to the fire to craft " + pc.QueuedRecipe.title + "…";
        else status = "Your character crafts standing at the fire. Costs are taken when the tool is finished.";

        if (status != craftStatusLast)
        {
            craftStatusLast = status;
            craftStatus.text = status;
            craftStatus.color = pc != null && pc.ActiveRecipe != null ? MenuStyle.TextAccent : MenuStyle.TextMuted;
        }
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

    void OnPlusClicked(ResourceNode.ResourceType resourceType)
    {
        if (baseBuilding == null) return;

        // Add worker through BaseBuilding (it checks housing capacity internally)
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
