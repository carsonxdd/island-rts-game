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
        public TextMeshProUGUI count;
        public Button minus, plus;
        public int last = -1;
    }

    private readonly List<JobRow> jobs = new List<JobRow>();
    private TextMeshProUGUI warriorCount, warriorCost, housingText;
    private Button warriorMinus, warriorPlus;
    private int lastWarriors = -1, lastHousingUsed = -1, lastHousingCap = -1;
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
        for (int i = 0; i < jobs.Count; i++) jobs[i].last = -1;
        lastWarriors = lastHousingUsed = lastHousingCap = -1;

        warriorCost.text = building.warriorCost_Wood + " wood  ·  " + building.warriorCost_Food + " food";

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

        MenuBuilder.SectionHeader(col.transform, "Workers");
        jobs.Add(MakeJobRow(col.transform, ResourceNode.ResourceType.Wood, "Wood cutters"));
        jobs.Add(MakeJobRow(col.transform, ResourceNode.ResourceType.Food, "Foragers"));
        jobs.Add(MakeJobRow(col.transform, ResourceNode.ResourceType.Stone, "Quarriers"));
        jobs.Add(MakeJobRow(col.transform, ResourceNode.ResourceType.Metal, "Miners"));

        housingText = MenuBuilder.RowDescription(col.transform, "Housing 0 / 0");

        MenuBuilder.SectionHeader(col.transform, "Defence");
        MenuBuilder.SettingRow(col.transform, "Warriors", out RectTransform wslot);
        CounterControls(wslot, OnWarriorMinusClicked, OnWarriorPlusClicked, out warriorCount, out warriorMinus, out warriorPlus);
        warriorCost = MenuBuilder.RowDescription(col.transform, "");

        MenuBuilder.Spacer(col.transform, 4f);
        MenuBuilder.FitPanelHeight(panel, col);
        drag.Clamp();   // a saved position from a larger window must not leave it off-screen
    }

    JobRow MakeJobRow(Transform parent, ResourceNode.ResourceType type, string label)
    {
        JobRow row = new JobRow { type = type };
        RectTransform rt = MenuBuilder.SettingRow(parent, label, out RectTransform slot);

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

        int housingCap = PopulationManager.Instance != null ? PopulationManager.Instance.GetHousingCapacity() : 10;
        int housingUsed = PopulationManager.Instance != null ? PopulationManager.Instance.GetCurrentWorkers() : baseBuilding.GetTotalWorkers();
        bool canAssign = PopulationManager.Instance == null || PopulationManager.Instance.HasAvailableHousing();

        for (int i = 0; i < jobs.Count; i++)
        {
            JobRow row = jobs[i];
            int n = WorkersOn(row.type);
            if (n != row.last)
            {
                row.last = n;
                row.count.text = n.ToString();
            }
            row.minus.interactable = n > 0;
            row.plus.interactable = canAssign;
        }

        if (housingUsed != lastHousingUsed || housingCap != lastHousingCap)
        {
            lastHousingUsed = housingUsed;
            lastHousingCap = housingCap;
            housingText.text = "Housing " + housingUsed + " / " + housingCap
                + (housingUsed >= housingCap ? "  —  build a hut for more workers" : "");
            housingText.color = housingUsed >= housingCap ? MenuStyle.TextAccent : MenuStyle.TextMuted;
        }

        int warriors = baseBuilding.GetWarriorCount();
        if (warriors != lastWarriors)
        {
            lastWarriors = warriors;
            warriorCount.text = warriors + " / " + baseBuilding.maxWarriors;
        }

        bool canRecruit = warriors < baseBuilding.maxWarriors
            && ResourceManager.Instance != null
            && ResourceManager.Instance.wood >= baseBuilding.warriorCost_Wood
            && ResourceManager.Instance.food >= baseBuilding.warriorCost_Food;
        if (canRecruit != lastCanRecruit || warriorPlus.interactable != canRecruit)
        {
            lastCanRecruit = canRecruit;
            warriorPlus.interactable = canRecruit;
        }
        warriorMinus.interactable = warriors > 0;
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

        if (baseBuilding.GetWarriorCount() >= baseBuilding.maxWarriors) return;
        if (ResourceManager.Instance.wood < baseBuilding.warriorCost_Wood ||
            ResourceManager.Instance.food < baseBuilding.warriorCost_Food) return;

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
