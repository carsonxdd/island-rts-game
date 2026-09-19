using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;

/// <summary>
/// The COLONY half of the campfire panel (2026-09-17): one column, no tabs, no
/// folds. A summary line, WORK (the four jobs and the three specialists as
/// compact counters), PEOPLE (a fixed scrolling window over the roster),
/// PRIORITIES (four mini sliders on one row), DEFENCE (warriors, the weapon the
/// next recruit takes, and the levy in one hint line) and STOCK (the campfire
/// stockpile on one line). Help lives in tooltips, not in the flow.
/// </summary>
/// <remarks>
/// Owned and driven by <see cref="WorkerAssignmentUI"/>, which also owns the
/// BENCH half (<see cref="BenchPanel"/>) — the title bar's BENCH button swaps
/// between the two in the same corner. Display only, every label dirty-checked,
/// nothing allocates while idle. The stance, formation and bell are NOT here:
/// the combat box in the bottom-right owns them once the colony has a warrior
/// or a spare weapon. Stock lines are also the HUD chips' breakdowns.
/// </remarks>
public class ColonyPanel
{
    public const float Width = 600f;
    const float PeopleWindowHeight = 100f;
    const int PeopleRowsShown = 24;
    const float PeopleRefreshSeconds = 0.5f;
    const string PanelPosKey = "ui.colonyPanel";
    static readonly Vector2 DefaultPanelPos = new Vector2(16f, 16f);

    static readonly string LockHex = ColorUtility.ToHtmlStringRGBA(new Color(0.62f, 0.60f, 0.57f, 0.9f));
    static readonly string DangerHex = ColorUtility.ToHtmlStringRGBA(MenuStyle.TextDanger);

    private class CounterRow
    {
        public string name;
        public TextMeshProUGUI label;
        public TextMeshProUGUI count;
        public Button minus, plus;
        public int last = -1;
        public int lockedLast = -1;   // -1 unknown, 0 open, 1 locked
    }

    private class JobRow : CounterRow { public ResourceNode.ResourceType type; }
    private class SpecialtyRow : CounterRow { public Worker.Specialty specialty; }

    private readonly WorkerAssignmentUI owner;
    private BaseBuilding fire;

    public RectTransform Panel { get; private set; }
    private DraggablePanel drag;
    private VerticalLayoutGroup column;
    private TextMeshProUGUI titleText;

    private TextMeshProUGUI summary;
    private readonly List<JobRow> jobs = new List<JobRow>();
    private readonly List<SpecialtyRow> specialties = new List<SpecialtyRow>();

    private readonly List<TextMeshProUGUI> peopleRows = new List<TextMeshProUGUI>();
    private readonly List<string> peopleLast = new List<string>();
    private int peopleActive = -1;
    private float peopleNextRefresh;
    private static readonly System.Text.StringBuilder line = new System.Text.StringBuilder(160);

    private TextMeshProUGUI warriorCount, defenceHint;
    private Button warriorMinus, warriorPlus;
    private Button[] weaponButtons;
    private TextMeshProUGUI[] weaponLabels;
    private readonly int[] weaponStockShown = new int[8];
    private int weaponIndexShown = -1;

    private TextMeshProUGUI stockLine;
    private readonly int[] stockShown = new int[16];
    private int stockHeldShown = -1, stockCapShown = -1;

    private int lastWarriors = -1, lastColonists = -1, lastIdle = -1, lastArrival = -1, lastHousingCap = -1;
    private int hintKeyLast = int.MinValue;
    private bool lastCanRecruit;

    public ColonyPanel(WorkerAssignmentUI owner) { this.owner = owner; }

    public bool IsOpen => Panel != null && Panel.gameObject.activeSelf;

    // ------------------------------------------------------------------
    // Open / close
    // ------------------------------------------------------------------

    public void Open(BaseBuilding building)
    {
        if (Panel == null) return;
        fire = building;
        titleText.text = "CAMPFIRE";

        for (int i = 0; i < jobs.Count; i++) { jobs[i].last = -1; jobs[i].lockedLast = -1; }
        for (int i = 0; i < specialties.Count; i++) { specialties[i].last = -1; specialties[i].lockedLast = -1; }
        for (int i = 0; i < weaponStockShown.Length; i++) weaponStockShown[i] = -1;
        for (int i = 0; i < stockShown.Length; i++) stockShown[i] = -1;
        weaponIndexShown = -1;
        stockHeldShown = stockCapShown = -1;
        lastWarriors = lastColonists = lastIdle = lastArrival = lastHousingCap = -1;
        hintKeyLast = int.MinValue;
        peopleActive = -1;
        peopleNextRefresh = 0f;

        Panel.gameObject.SetActive(true);
        drag.Clamp();   // the window may have been resized since the last open
        UpdateDisplay();
    }

    public void Close()
    {
        if (Panel != null) Panel.gameObject.SetActive(false);
        fire = null;
    }

    /// <summary>Per frame while open: the fire may have died under it.</summary>
    public bool Tick()
    {
        if (!IsOpen) return false;
        if (fire == null) { Close(); return false; }
        UpdateDisplay();
        return true;
    }

    // ------------------------------------------------------------------
    // Construction
    // ------------------------------------------------------------------

    public void Build(Transform canvas)
    {
        Panel = MenuBuilder.Panel(canvas, "ColonyPanel", Width, 100f);
        Panel.anchorMin = Panel.anchorMax = Vector2.zero;
        Panel.pivot = Vector2.zero;
        Panel.anchoredPosition = DefaultPanelPos;

        column = MenuBuilder.Column(Panel, 2f);
        VerticalLayoutGroup col = column;

        // Title row: the drag handle, with BENCH (swap) and X on its right
        RectTransform title = MenuBuilder.SettingRow(col.transform, "CAMPFIRE", out RectTransform titleSlot, 40f);
        drag = DraggablePanel.Attach(title, Panel, PanelPosKey, DefaultPanelPos);
        titleText = title.GetComponentInChildren<TextMeshProUGUI>();
        titleText.color = MenuStyle.TextAccent;
        titleText.fontSize = MenuStyle.HeadingSize - 4f;
        titleText.characterSpacing = 3f;
        Button bench = MenuBuilder.SlotButton(titleSlot, "BENCH  ›", () => owner.SwapToBench(), new Vector2(0.44f, 0.1f), new Vector2(0.82f, 0.9f));
        SmallLabel(bench);
        Tooltip.Attach((RectTransform)bench.transform, "The campfire's bench: craft, research and the queue.");
        Button close = MenuBuilder.SlotButton(titleSlot, "X", () => owner.ClosePanel(), new Vector2(0.86f, 0.1f), new Vector2(1f, 0.9f));
        SmallLabel(close);
        MenuBuilder.Divider(col.transform);

        summary = MenuBuilder.RowDescription(col.transform, "");
        summary.textWrappingMode = TextWrappingModes.NoWrap;
        summary.overflowMode = TextOverflowModes.Ellipsis;
        Tooltip.Attach(summary.rectTransform, "Colonists, how many are idle, and when the next survivor lands. Survivors come ashore while there is a free bed.");

        // WORK: the four gathering jobs, then the three specialist trades
        MenuBuilder.SectionHeader(col.transform, "Work");
        jobs.Add(MakeJobRow(col.transform, ResourceNode.ResourceType.Wood, "Wood cutters"));
        jobs.Add(MakeJobRow(col.transform, ResourceNode.ResourceType.Food, "Foragers"));
        jobs.Add(MakeJobRow(col.transform, ResourceNode.ResourceType.Stone, "Quarriers"));
        jobs.Add(MakeJobRow(col.transform, ResourceNode.ResourceType.Metal, "Miners"));
        specialties.Add(MakeSpecialtyRow(col.transform, Worker.Specialty.Builder, "Builders"));
        specialties.Add(MakeSpecialtyRow(col.transform, Worker.Specialty.Crafter, "Crafters"));
        specialties.Add(MakeSpecialtyRow(col.transform, Worker.Specialty.Repairer, "Repairers"));

        // PEOPLE: a fixed window that scrolls; the pool is toggled to the roster size
        MenuBuilder.SectionHeader(col.transform, "People");
        VerticalLayoutGroup people = MenuBuilder.ScrollColumn(col.transform, 0f, PeopleWindowHeight, 0f);
        for (int i = 0; i < PeopleRowsShown; i++)
        {
            TextMeshProUGUI row = MenuBuilder.RowDescription(people.transform, "");
            row.textWrappingMode = TextWrappingModes.NoWrap;
            row.overflowMode = TextOverflowModes.Ellipsis;
            row.gameObject.SetActive(false);
            peopleRows.Add(row);
            peopleLast.Add(null);
        }
        Tooltip.Attach((RectTransform)people.transform.parent, "Name, trait, role and what they are up to. Everyone sleeps from midnight; a trait shifts the hours. Left-click a jobless colonist in the world to open this panel.");

        // PRIORITIES: four mini sliders on one row
        MenuBuilder.SectionHeader(col.transform, "Priorities");
        RectTransform prow = MakePriorityRow(col.transform);
        Tooltip.Attach(prow, "What an idle colonist reaches for first, all else equal: build, then craft, then repair, then tidy the beach. Zero switches that work off.");

        // DEFENCE: warriors, the weapon the next recruit takes, one hint line
        MenuBuilder.SectionHeader(col.transform, "Defence");
        RectTransform wrow = MenuBuilder.SettingRow(col.transform, "Warriors", out RectTransform wslot, MenuStyle.CompactRowHeight);
        CounterControls(wslot, OnWarriorMinus, OnWarriorPlus, out warriorCount, out warriorMinus, out warriorPlus);
        Tooltip.Attach(wrow, "+ arms an idle colonist with the weapon below for 15 food. − dismisses one: the weapon goes back to the stockpile. Stance, formation and the bell are in the militia box, bottom-right.");

        ItemDef[] weapons = ItemCatalog.Weapons;
        string[] names = new string[weapons.Length];
        for (int i = 0; i < weapons.Length; i++) names[i] = weapons[i].displayName;
        weaponButtons = MenuBuilder.SegmentRow(col.transform, "Arm with", names, OnWeaponPick);
        // Three weapon names need more than the row's default 52% control slot: shift the split left
        RectTransform armSlot = (RectTransform)weaponButtons[0].transform.parent.parent;
        armSlot.anchorMin = new Vector2(0.22f, 0f);
        RectTransform armLabel = (RectTransform)armSlot.parent.GetChild(armSlot.GetSiblingIndex() - 1);
        armLabel.anchorMax = new Vector2(0.20f, 1f);
        weaponLabels = new TextMeshProUGUI[weaponButtons.Length];
        for (int i = 0; i < weaponButtons.Length; i++) weaponLabels[i] = weaponButtons[i].GetComponentInChildren<TextMeshProUGUI>();
        Tooltip.Attach((RectTransform)weaponButtons[0].transform.parent.parent.parent, "The weapon the next recruit takes, with how many are in stock. A bow makes an archer. Spare weapons arm the levy when the bell rings or raiders reach the fire.");

        defenceHint = MenuBuilder.RowDescription(col.transform, "");
        defenceHint.textWrappingMode = TextWrappingModes.NoWrap;
        defenceHint.overflowMode = TextOverflowModes.Ellipsis;

        // STOCK: the campfire stockpile on one line
        MenuBuilder.SectionHeader(col.transform, "Stock");
        stockLine = MenuBuilder.RowDescription(col.transform, "");
        stockLine.textWrappingMode = TextWrappingModes.NoWrap;
        stockLine.overflowMode = TextOverflowModes.Ellipsis;
        Tooltip.Attach(stockLine.rectTransform, "What your character and your colonists bring in, and what the benches make. Room grows with Storage Pits and each Storehouse. Right-click the fire with your character to deposit. The HUD chips break it down by kind.");

        MenuBuilder.Spacer(col.transform, 2f);
        MenuBuilder.FitPanelHeight(Panel, col);
        drag.Clamp();
        Panel.gameObject.SetActive(false);
    }

    static void SmallLabel(Button b)
    {
        TextMeshProUGUI t = b.GetComponentInChildren<TextMeshProUGUI>();
        if (t == null) return;
        t.fontSize = MenuStyle.SmallSize;
        t.textWrappingMode = TextWrappingModes.NoWrap;
    }

    JobRow MakeJobRow(Transform parent, ResourceNode.ResourceType type, string label)
    {
        JobRow row = new JobRow { type = type, name = label };
        RectTransform rt = MenuBuilder.SettingRow(parent, label, out RectTransform slot, MenuStyle.CompactRowHeight);
        row.label = rt.GetComponentInChildren<TextMeshProUGUI>();

        // Colour swatch in front of the label, matching the HUD chip
        RectTransform sw = MenuBuilder.SimpleImage(rt, "Swatch", ResourceUI.ColorFor(type));
        sw.anchorMin = new Vector2(0f, 0.25f);
        sw.anchorMax = new Vector2(0f, 0.75f);
        sw.pivot = new Vector2(0f, 0.5f);
        sw.anchoredPosition = new Vector2(-14f, 0f);
        sw.sizeDelta = new Vector2(8f, 0f);

        CounterControls(slot, () => OnJobMinus(type), () => OnJobPlus(type), out row.count, out row.minus, out row.plus);
        Tooltip.Attach(rt, "+ gives an idle colonist this job, − sends one back to idle. The job opens with its research.");
        return row;
    }

    SpecialtyRow MakeSpecialtyRow(Transform parent, Worker.Specialty specialty, string label)
    {
        SpecialtyRow row = new SpecialtyRow { specialty = specialty, name = label };
        RectTransform rt = MenuBuilder.SettingRow(parent, label, out RectTransform slot, MenuStyle.CompactRowHeight);
        row.label = rt.GetComponentInChildren<TextMeshProUGUI>();
        CounterControls(slot, () => OnSpecialtyMinus(specialty), () => OnSpecialtyPlus(specialty), out row.count, out row.minus, out row.plus);
        Tooltip.Attach(rt, "Idle colonists build, then craft, then repair, then tidy on their own. A specialist does only this trade.");
        return row;
    }

    /// <summary>[ − ]  count  [ + ] inside a row's control slot.</summary>
    static void CounterControls(RectTransform slot, Action onMinus, Action onPlus,
        out TextMeshProUGUI count, out Button minus, out Button plus)
    {
        minus = MenuBuilder.SlotButton(slot, "−", onMinus, new Vector2(0.20f, 0.06f), new Vector2(0.40f, 0.94f));
        plus = MenuBuilder.SlotButton(slot, "+", onPlus, new Vector2(0.72f, 0.06f), new Vector2(0.92f, 0.94f));
        SmallLabel(minus);
        SmallLabel(plus);

        count = MenuBuilder.Label(slot, "0", MenuStyle.BodySize, MenuStyle.TextAccent);
        RectTransform crt = count.rectTransform;
        crt.anchorMin = new Vector2(0.42f, 0f);
        crt.anchorMax = new Vector2(0.70f, 1f);
        crt.offsetMin = Vector2.zero;
        crt.offsetMax = Vector2.zero;
    }

    /// <summary>Four captioned mini sliders across one compact row.</summary>
    RectTransform MakePriorityRow(Transform parent)
    {
        GameObject go = new GameObject("Priorities", typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = MenuStyle.CompactRowHeight;
        RectTransform rt = go.GetComponent<RectTransform>();

        LaborPriorities p = Factions.Player.Priorities;
        MiniSlider(rt, 0, "Build", p.Build, v => { Factions.Player.Priorities.Build = v; DevQuests.Signal("priority"); });
        MiniSlider(rt, 1, "Craft", p.Craft, v => { Factions.Player.Priorities.Craft = v; DevQuests.Signal("priority"); });
        MiniSlider(rt, 2, "Repair", p.Repair, v => { Factions.Player.Priorities.Repair = v; DevQuests.Signal("priority"); });
        MiniSlider(rt, 3, "Tidy", p.Forage, v =>
        {
            if (Factions.Player.Priorities.Forage <= 0f && v > 0f) DevQuests.Signal("priority:forage_back");   // was off, back on
            Factions.Player.Priorities.Forage = v;
            DevQuests.Signal("priority");
        });
        return rt;
    }

    static void MiniSlider(RectTransform row, int cell, string caption, float value, Action<float> onChange)
    {
        float x0 = cell * 0.25f, x1 = x0 + 0.25f;
        TextMeshProUGUI t = MenuBuilder.Label(row, caption, MenuStyle.SmallSize, MenuStyle.TextPrimary, TextAlignmentOptions.MidlineLeft);
        t.textWrappingMode = TextWrappingModes.NoWrap;
        RectTransform trt = t.rectTransform;
        trt.anchorMin = new Vector2(x0, 0f);
        trt.anchorMax = new Vector2(x0 + 0.09f, 1f);
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;

        Slider s = MenuBuilder.BuildSlider(row, value, 0f, 1f, new Vector2(x0 + 0.10f, 0.5f), new Vector2(x1 - 0.02f, 0.5f), 7f);
        s.onValueChanged.AddListener(v => onChange?.Invoke(v));
    }

    // ------------------------------------------------------------------
    // Refresh
    // ------------------------------------------------------------------

    void UpdateDisplay()
    {
        Population pm = Factions.Player.Population;
        int housingCap = pm != null ? pm.GetHousingCapacity() : 0;
        int colonists = pm != null ? pm.GetColonistCount() : fire.GetTotalWorkers();
        int idle = pm != null ? pm.GetIdleCount() : 0;
        bool canAssign = idle > 0;   // a job needs an idle colonist — assignment never creates people

        for (int i = 0; i < jobs.Count; i++)
        {
            JobRow row = jobs[i];
            UpdateCounter(row, WorkersOn(row.type), !Factions.Player.Knowledge.HasJob(row.type),
                Unlocks.ForJob(row.type), canAssign);
        }
        for (int i = 0; i < specialties.Count; i++)
        {
            SpecialtyRow row = specialties[i];
            Unlocks.Kind gate = BaseBuilding.UnlockFor(row.specialty);
            bool locked = !Factions.Player.Knowledge.Has(gate);
            if (locked && row.lockedLast != 1) DevQuests.Signal("specialist:locked");   // the row names its research
            UpdateCounter(row, fire.CountSpecialty(row.specialty), locked, gate, canAssign);
        }

        // "7 colonists · 2 idle · beds 7 / 8 · next survivor in 12s"
        int arrival = pm != null ? Mathf.CeilToInt(pm.SecondsToNextArrival) : -1;
        int hunger = pm != null ? (int)pm.Hunger : 0;
        int arrivalKey = arrival * 4 + hunger;
        if (colonists != lastColonists || idle != lastIdle || arrivalKey != lastArrival || housingCap != lastHousingCap)
        {
            lastColonists = colonists;
            lastIdle = idle;
            lastArrival = arrivalKey;
            lastHousingCap = housingCap;
            line.Length = 0;
            line.Append(colonists).Append(colonists == 1 ? " colonist" : " colonists").Append("  ·  ").Append(idle).Append(" idle")
                .Append("  ·  beds ").Append(colonists).Append(" / ").Append(housingCap);
            if (hunger == (int)Population.HungerState.Starving) line.Append("  ·  <color=#").Append(DangerHex).Append(">STARVING</color>");
            else if (hunger == (int)Population.HungerState.Hungry) line.Append("  ·  <color=#").Append(DangerHex).Append(">HUNGRY</color>");
            else if (arrival >= 0) line.Append("  ·  next survivor in ").Append(arrival).Append("s");
            else if (colonists >= housingCap) line.Append("  ·  build a hut for more survivors");
            else line.Append("  ·  survivors land by day");
            summary.text = line.ToString();
            summary.color = colonists >= housingCap ? MenuStyle.TextAccent : (idle > 0 ? MenuStyle.TextPrimary : MenuStyle.TextMuted);
        }

        UpdatePeople(pm);
        UpdateDefence(pm);
        UpdateStock();
    }

    void UpdateCounter(CounterRow row, int n, bool locked, Unlocks.Kind gate, bool canAssign)
    {
        if (n != row.last)
        {
            row.last = n;
            row.count.text = n.ToString();
        }
        int lockState = locked ? 1 : 0;
        if (lockState != row.lockedLast && row.label != null)
        {
            row.lockedLast = lockState;
            row.label.text = locked
                ? row.name + "  <size=78%><color=#" + LockHex + ">research " + Unlocks.ResearchTitleFor(gate) + "</color></size>"
                : row.name;
        }
        row.minus.interactable = n > 0;
        row.plus.interactable = canAssign && !locked;
    }

    /// <summary>
    /// The People rows: shown to the roster size, text rebuilt every
    /// <see cref="PeopleRefreshSeconds"/> from the roster entry's Persona, the
    /// unit's role and its Activity. The window scrolls, so no refit is needed.
    /// </summary>
    void UpdatePeople(Population pm)
    {
        IReadOnlyList<Population.Colonist> roster = pm != null ? pm.Roster : null;
        int living = 0;
        if (roster != null)
        {
            for (int i = 0; i < roster.Count; i++) if (roster[i].unit != null) living++;
        }
        int shown = Mathf.Min(living, peopleRows.Count);
        if (shown != peopleActive)
        {
            peopleActive = shown;
            for (int i = 0; i < peopleRows.Count; i++)
            {
                bool on = i < shown;
                if (peopleRows[i].gameObject.activeSelf != on) peopleRows[i].gameObject.SetActive(on);
                peopleLast[i] = null;
            }
            peopleNextRefresh = 0f;
        }
        if (shown == 0 || Time.unscaledTime < peopleNextRefresh) return;
        peopleNextRefresh = Time.unscaledTime + PeopleRefreshSeconds;

        int row = 0;
        for (int i = 0; i < roster.Count && row < shown; i++)
        {
            Population.Colonist c = roster[i];
            if (c.unit == null) continue;
            bool last = row == shown - 1 && living > shown;
            line.Length = 0;
            if (last)
            {
                line.Append("… and ").Append(living - shown + 1).Append(" more");
            }
            else
            {
                Persona p = c.persona;
                line.Append(p != null ? p.Name : c.unit.name);
                if (p != null) line.Append("  ·  ").Append(p.TraitName);
                Worker w = c.unit as Worker;
                Warrior war = c.unit as Warrior;
                if (w != null)
                {
                    line.Append("  ·  ").Append(w.RoleTitle());
                    string doing = w.CurrentActivity;
                    if (!string.IsNullOrEmpty(doing)) line.Append("  ·  ").Append(doing);
                }
                else if (war != null)
                {
                    line.Append("  ·  ").Append(war.weapon != null && war.weapon.equipment != null && war.weapon.equipment.ranged ? "Archer" : "Warrior");
                    if (war.levied) line.Append(" (levy)");
                    string doing = war.CurrentActivity;
                    if (!string.IsNullOrEmpty(doing)) line.Append("  ·  ").Append(doing);
                }
            }
            string text = line.ToString();
            if (text != peopleLast[row])
            {
                peopleLast[row] = text;
                peopleRows[row].text = text;
                peopleRows[row].color = MenuStyle.TextPrimary;
            }
            row++;
        }
    }

    void UpdateDefence(Population pm)
    {
        int warriors = fire.GetWarriorCount();
        if (warriors != lastWarriors)
        {
            lastWarriors = warriors;
            warriorCount.text = fire.maxWarriors > 0 ? warriors + " / " + fire.maxWarriors : warriors.ToString();
        }
        bool canRecruit = fire.CanRecruitWarrior();
        if (canRecruit != lastCanRecruit || warriorPlus.interactable != canRecruit)
        {
            lastCanRecruit = canRecruit;
            warriorPlus.interactable = canRecruit;
        }
        warriorMinus.interactable = warriors > 0;

        // The Arm-with segments: each shows its stock count, the chosen one is lit
        ItemDef[] weapons = ItemCatalog.Weapons;
        ItemDef selected = fire.SelectedWeapon;
        Inventory stock = fire.Stockpile;
        int selectedIndex = Array.IndexOf(weapons, selected);
        for (int i = 0; i < weaponButtons.Length && i < weapons.Length; i++)
        {
            int n = stock.Count(weapons[i]);
            if (n != weaponStockShown[i])
            {
                weaponStockShown[i] = n;
                weaponLabels[i].text = weapons[i].displayName + "  <color=#" + LockHex + ">" + n + "</color>";
            }
        }
        if (selectedIndex != weaponIndexShown)
        {
            weaponIndexShown = selectedIndex;
            MenuBuilder.TintTabs(weaponButtons, selectedIndex);
        }

        // One hint line: the research gate, else the cost and the levy
        bool militia = Factions.Player.Knowledge.Has(Unlocks.Kind.Militia);
        Militia levy = Factions.Player.Militia;
        int spare = levy.SpareWeapons, mustered = levy.Mustered, claimed = levy.Claimed;
        int hintKey = militia ? (spare * 1024 + mustered * 32 + claimed) : -1;
        if (hintKey != hintKeyLast)
        {
            hintKeyLast = hintKey;
            if (!militia)
            {
                defenceHint.text = "Research " + Unlocks.ResearchTitleFor(Unlocks.Kind.Militia) + " at the bench to arm warriors";
                defenceHint.color = MenuStyle.TextAccent;
            }
            else
            {
                line.Length = 0;
                line.Append(fire.warriorCost_Food).Append(" food + an idle colonist per warrior  ·  levy: ");
                if (mustered == 0 && claimed == 0) line.Append(spare).Append(spare == 1 ? " spare weapon" : " spare weapons");
                else
                {
                    line.Append(mustered).Append(" armed");
                    if (claimed > 0) line.Append(", ").Append(claimed).Append(" on the way");
                }
                defenceHint.text = line.ToString();
                defenceHint.color = mustered > 0 ? MenuStyle.TextAccent : MenuStyle.TextMuted;
            }
        }
    }

    void UpdateStock()
    {
        Inventory stock = fire.Stockpile;
        ItemDef[] items = ItemCatalog.Stockpiled;
        int held = stock.TotalCount, cap = stock.Capacity;
        bool changed = held != stockHeldShown || cap != stockCapShown;
        for (int i = 0; i < items.Length && i < stockShown.Length; i++)
        {
            int n = stock.Count(items[i]);
            if (n != stockShown[i]) { stockShown[i] = n; changed = true; }
        }
        if (!changed) return;
        stockHeldShown = held;
        stockCapShown = cap;

        line.Length = 0;
        line.Append("Room ").Append(held).Append(" / ").Append(cap);
        bool any = false;
        for (int i = 0; i < items.Length && i < stockShown.Length; i++)
        {
            if (stockShown[i] <= 0) continue;
            any = true;
            line.Append("  ·  ").Append(stockShown[i]).Append(' ').Append(items[i].displayName);
        }
        if (!any) line.Append("  ·  nothing stored yet");
        stockLine.text = line.ToString();
        stockLine.color = held >= cap ? MenuStyle.TextDanger : (any ? MenuStyle.TextPrimary : MenuStyle.TextMuted);
    }

    int WorkersOn(ResourceNode.ResourceType t)
    {
        switch (t)
        {
            case ResourceNode.ResourceType.Food: return fire.foodWorkers;
            case ResourceNode.ResourceType.Stone: return fire.stoneWorkers;
            case ResourceNode.ResourceType.Metal: return fire.metalWorkers;
            default: return fire.woodWorkers;
        }
    }

    // ------------------------------------------------------------------
    // Actions
    // ------------------------------------------------------------------

    void Click() { if (AudioManager.Instance != null) AudioManager.Instance.PlayButtonClick(); }

    void OnJobPlus(ResourceNode.ResourceType type)
    {
        if (fire == null) return;
        fire.AssignWorker(type);   // checks the research and the idle pool
        if (AudioManager.Instance != null) AudioManager.Instance.PlayWorkerAssigned();
        UpdateDisplay();
    }

    void OnJobMinus(ResourceNode.ResourceType type)
    {
        if (fire == null) return;
        fire.UnassignWorker(type);
        Click();
        UpdateDisplay();
    }

    void OnSpecialtyPlus(Worker.Specialty s)
    {
        if (fire == null) return;
        if (fire.AssignSpecialist(s) && AudioManager.Instance != null) AudioManager.Instance.PlayWorkerAssigned();
        UpdateDisplay();
    }

    void OnSpecialtyMinus(Worker.Specialty s)
    {
        if (fire == null) return;
        fire.UnassignSpecialist(s);
        Click();
        UpdateDisplay();
    }

    void OnWarriorPlus()
    {
        if (fire == null || !fire.CanRecruitWarrior()) return;
        fire.SpawnWarrior();
        Click();
        UpdateDisplay();
    }

    void OnWarriorMinus()
    {
        if (fire == null) return;
        fire.RemoveWarrior();
        Click();
        UpdateDisplay();
    }

    void OnWeaponPick(int index)
    {
        if (fire == null || index < 0 || index >= ItemCatalog.Weapons.Length) return;
        fire.SelectWeapon(ItemCatalog.Weapons[index]);
        Click();
        UpdateDisplay();
    }
}
