using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The colony HUD: one continuous bar along the top-left holding six equal
/// entries — wood, food, stone, metal (stock + how many workers are on it),
/// housing, and the calendar (which day of how many, and whether raiders land
/// tonight — the run is a 30-day calendar with announced raids, 2026-09-02) —
/// separated by thin dividers. A one-line banner under the bar flashes
/// "Raiders sighted" for a few seconds at the dawn roll. Built entirely in code on the
/// menu system's widgets, so it needs no scene wiring and picks up the UI
/// Scale setting; the old hand-built ResourcePanel is removed by the legacy
/// cleanup step.
///
/// Every entry is a button that opens the campfire's assignment panel, so the
/// player's main economic lever is one click from the numbers they are
/// reacting to. Refreshed on a slow interval and dirty-checked, so an
/// unchanged number never costs a string allocation.
///
/// Layout rules (2026-09-01 rework): exactly two lines per entry — the big
/// amount on top, "Wood · 0 workers" as ONE unbroken line under it. Both
/// lines share a left edge, every entry is the same fixed width, and the
/// worker count is smaller and dimmer than the amount so the eye lands on
/// the number first. The caption is <c>NoWrap</c>: MenuBuilder.Label wraps
/// by default, which is what used to break "0 workers" onto a third line.
/// </summary>
public class ResourceUI : MonoBehaviour
{
    [Header("Update Settings")]
    public float updateInterval = 0.1f;  // Update UI every 0.1 seconds

    private float timeSinceUpdate = 0f;

    private class Chip
    {
        public ResourceNode.ResourceType type;
        public RectTransform entry;
        public TextMeshProUGUI value;
        public TextMeshProUGUI workers;
        public int lastValue = -1;
        public int lastWorkers = -1;
    }

    private readonly List<Chip> chips = new List<Chip>();
    private TextMeshProUGUI popValue;
    private TextMeshProUGUI popLabel;
    private int lastPopWorkers = -1, lastPopCapacity = -1;

    // Calendar entry + raid banner
    private TextMeshProUGUI calValue;
    private TextMeshProUGUI calLabel;
    private int lastCalKey = -1;
    private DayNightCycle dayNight;
    private TextMeshProUGUI banner;
    private RectTransform barRect;
    private float bannerHideAt = -1f;
    const float BannerSeconds = 7f;

    // ---- metrics (reference pixels) ----
    const float EntryWidth = 150f;      // fits "Stone · 12 workers" at CaptionSize with room to spare
    const float EntryHeight = 54f;
    const float DialWidth = 84f;        // the sky dial's slot: an arc, no text
    const float TextInset = 14f;        // shared left edge of amount and caption
    const float AmountSize = 24f;
    const float CaptionSize = 13f;
    const float SwatchWidth = 4f;

    private static readonly Color WoodColor = new Color(0.66f, 0.45f, 0.26f);
    private static readonly Color FoodColor = new Color(0.78f, 0.25f, 0.22f);
    private static readonly Color StoneColor = new Color(0.55f, 0.58f, 0.62f);
    private static readonly Color MetalColor = new Color(0.62f, 0.72f, 0.82f);
    private static readonly Color PopColor = new Color(0.92f, 0.80f, 0.45f);
    private static readonly Color CalColor = new Color(0.55f, 0.75f, 0.90f);

    /// <summary>Hex of the dimmer tone used for the worker-count half of a caption.</summary>
    private static readonly string DimHex = ColorUtility.ToHtmlStringRGBA(new Color(0.62f, 0.60f, 0.57f, 0.72f));

    public static Color ColorFor(ResourceNode.ResourceType t)
    {
        switch (t)
        {
            case ResourceNode.ResourceType.Food: return FoodColor;
            case ResourceNode.ResourceType.Stone: return StoneColor;
            case ResourceNode.ResourceType.Metal: return MetalColor;
            default: return WoodColor;
        }
    }

    void Start()
    {
        if (SimHooks.Simulating) { enabled = false; return; }
        Build();
        RaidDirector.OnRaidRolled += OnRaidRolled;
        UpdateUI();
    }

    void OnDestroy()
    {
        RaidDirector.OnRaidRolled -= OnRaidRolled;
    }

    void Update()
    {
        // Only update UI periodically (not every frame for performance)
        timeSinceUpdate += Time.unscaledDeltaTime;

        if (timeSinceUpdate >= updateInterval)
        {
            timeSinceUpdate = 0f;
            UpdateUI();
            if (openPanel != Breakdown.None) RefreshBreakdown();
        }

        CloseBreakdownIfClickedAway();

        if (bannerHideAt >= 0f && Time.unscaledTime >= bannerHideAt)
        {
            bannerHideAt = -1f;
            banner.gameObject.SetActive(false);
        }
    }

    /// <summary>The dawn roll came back: flash the warning for a few seconds. The chip carries it all day.</summary>
    void OnRaidRolled(bool raid)
    {
        if (!raid || banner == null) return;
        banner.gameObject.SetActive(true);
        bannerHideAt = Time.unscaledTime + BannerSeconds;
        lastCalKey = -1;   // repaint the chip now rather than on the next tick
        UpdateUI();
    }

    // ------------------------------------------------------------------
    // Construction
    // ------------------------------------------------------------------

    void Build()
    {
        // Root-level canvas on purpose: this component sits under the scene's
        // old Canvas, and a canvas nested in a canvas ignores its own scaler
        Canvas canvas = MenuBuilder.CreateCanvas("HUDCanvas", 40);

        // One continuous bar: top-left, sized by its content. No border —
        // the entries are separated by dividers inside it, not boxed.
        GameObject bar = new GameObject("ResourceBar", typeof(RectTransform), typeof(Image),
            typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        bar.transform.SetParent(canvas.transform, false);
        RectTransform brt = barRect = bar.GetComponent<RectTransform>();
        brt.anchorMin = brt.anchorMax = new Vector2(0f, 1f);
        brt.pivot = new Vector2(0f, 1f);
        brt.anchoredPosition = new Vector2(16f, -16f);

        Image bg = bar.GetComponent<Image>();
        bg.color = new Color(MenuStyle.PanelFill.r, MenuStyle.PanelFill.g, MenuStyle.PanelFill.b, 0.84f);
        bg.raycastTarget = false;

        HorizontalLayoutGroup row = bar.GetComponent<HorizontalLayoutGroup>();
        row.spacing = 0f;
        row.padding = new RectOffset(0, 0, 0, 0);
        row.childControlWidth = true;      // entries take their LayoutElement width → all equal
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = true;
        row.childAlignment = TextAnchor.MiddleLeft;

        ContentSizeFitter fit = bar.GetComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        chips.Add(MakeChip(bar.transform, ResourceNode.ResourceType.Wood, "Wood"));
        VerticalDivider(bar.transform);
        chips.Add(MakeChip(bar.transform, ResourceNode.ResourceType.Food, "Food"));
        VerticalDivider(bar.transform);
        chips.Add(MakeChip(bar.transform, ResourceNode.ResourceType.Stone, "Stone"));
        VerticalDivider(bar.transform);
        chips.Add(MakeChip(bar.transform, ResourceNode.ResourceType.Metal, "Metal"));
        VerticalDivider(bar.transform);

        // Housing entry
        RectTransform pop = Entry(bar.transform, "Housing", PopColor, out popValue, out popLabel, "Housing");
        pop.GetComponent<Button>().onClick.AddListener(() => ToggleBreakdown(Breakdown.Housing, pop));
        VerticalDivider(bar.transform);

        // Calendar entry — not a button: nothing to open, so no hover either
        Entry(bar.transform, "Calendar", CalColor, out calValue, out calLabel, "Day", clickable: false);
        VerticalDivider(bar.transform);

        // Sky dial: sun across the day, moon across the night
        BuildDialEntry(bar.transform);

        // Raid banner: one bold line centred BELOW the bar (the bar is ~900px
        // wide from the left edge, so a top-centre banner would sit on top of
        // its right half), hidden until the dawn roll says raiders are coming.
        banner = MenuBuilder.Label(canvas.transform, "RAIDERS SIGHTED  —  they land tonight",
            26f, MenuStyle.TextDanger, TextAlignmentOptions.Center);
        banner.fontStyle = FontStyles.Bold;
        banner.textWrappingMode = TextWrappingModes.NoWrap;
        banner.overflowMode = TextOverflowModes.Overflow;
        banner.raycastTarget = false;
        RectTransform brt2 = banner.rectTransform;
        brt2.anchorMin = brt2.anchorMax = new Vector2(0.5f, 1f);
        brt2.pivot = new Vector2(0.5f, 1f);
        brt2.anchoredPosition = new Vector2(0f, -(16f + EntryHeight + 14f));
        brt2.sizeDelta = new Vector2(900f, 40f);
        banner.gameObject.SetActive(false);
    }

    Chip MakeChip(Transform parent, ResourceNode.ResourceType type, string label)
    {
        Chip chip = new Chip { type = type };
        RectTransform rt = Entry(parent, label, ColorFor(type), out chip.value, out chip.workers, label);
        chip.entry = rt;
        // Clicking a chip drops its breakdown down rather than opening the campfire:
        // the number the player is reacting to is the number they want split apart.
        rt.GetComponent<Button>().onClick.AddListener(() => ToggleBreakdown((Breakdown)((int)type + 1), rt));
        return chip;
    }

    /// <summary>A 1px vertical rule between two entries, full bar height.</summary>
    static void VerticalDivider(Transform parent)
    {
        RectTransform rt = MenuBuilder.SimpleImage(parent, "Divider", MenuStyle.Divider);
        LayoutElement le = rt.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = 1f;
        le.minWidth = 1f;
    }

    /// <summary>
    /// One bar entry: a thin colour swatch on the left edge, the big amount on
    /// the top line and a single-line caption under it, both starting at the
    /// same x. The whole entry is a button — its Image is the raycast surface,
    /// fully transparent at rest and lit faintly on hover via the tint block
    /// (the tint multiplies the Image colour, so the Image itself has to stay
    /// opaque white for the hover to show at all).
    /// </summary>
    RectTransform Entry(Transform parent, string name, Color swatch,
        out TextMeshProUGUI value, out TextMeshProUGUI caption, string captionText, bool clickable = true)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        LayoutElement le = go.GetComponent<LayoutElement>();
        le.preferredWidth = EntryWidth;
        le.minWidth = EntryWidth;
        le.preferredHeight = EntryHeight;

        Image img = go.GetComponent<Image>();
        img.color = Color.white;
        img.raycastTarget = true;

        Button btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        ColorBlock cb = btn.colors;
        cb.normalColor = new Color(1f, 1f, 1f, 0f);
        cb.highlightedColor = new Color(1f, 1f, 1f, 0.07f);
        cb.pressedColor = new Color(1f, 0.9f, 0.7f, 0.14f);
        cb.selectedColor = new Color(1f, 1f, 1f, 0f);
        cb.fadeDuration = 0.08f;
        btn.colors = cb;
        if (!clickable) btn.transition = Selectable.Transition.None;   // no hover on a read-only entry

        // Swatch strip on the left edge
        RectTransform sw = MenuBuilder.SimpleImage(go.transform, "Swatch", swatch);
        sw.anchorMin = new Vector2(0f, 0f);
        sw.anchorMax = new Vector2(0f, 1f);
        sw.pivot = new Vector2(0f, 0.5f);
        sw.anchoredPosition = Vector2.zero;
        sw.sizeDelta = new Vector2(SwatchWidth, 0f);

        // Top line: the amount. Bottom-aligned within its band so its baseline
        // sits at a fixed height across all five entries.
        value = MenuBuilder.Label(go.transform, "0", AmountSize, MenuStyle.TextPrimary, TextAlignmentOptions.BottomLeft);
        RectTransform vrt = value.rectTransform;
        vrt.anchorMin = new Vector2(0f, 0.44f);
        vrt.anchorMax = new Vector2(1f, 1f);
        vrt.offsetMin = new Vector2(TextInset, 0f);
        vrt.offsetMax = new Vector2(-6f, -4f);
        value.fontStyle = FontStyles.Bold;
        value.textWrappingMode = TextWrappingModes.NoWrap;
        value.overflowMode = TextOverflowModes.Overflow;

        // Bottom line: "Wood · 0 workers", one line, never wrapped. Top-aligned
        // in its band so it hangs off the amount at the same gap everywhere.
        caption = MenuBuilder.Label(go.transform, captionText, CaptionSize, MenuStyle.TextMuted, TextAlignmentOptions.TopLeft);
        RectTransform crt = caption.rectTransform;
        crt.anchorMin = new Vector2(0f, 0f);
        crt.anchorMax = new Vector2(1f, 0.44f);
        crt.offsetMin = new Vector2(TextInset, 6f);
        crt.offsetMax = new Vector2(-6f, 0f);
        caption.textWrappingMode = TextWrappingModes.NoWrap;
        caption.overflowMode = TextOverflowModes.Overflow;
        caption.richText = true;

        return go.GetComponent<RectTransform>();
    }

    /// <summary>The dial's own slot on the bar: no text, just the sky.</summary>
    void BuildDialEntry(Transform parent)
    {
        GameObject go = new GameObject("Sky", typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        LayoutElement le = go.GetComponent<LayoutElement>();
        le.preferredWidth = DialWidth;
        le.minWidth = DialWidth;
        le.preferredHeight = EntryHeight;

        HudTimeDial.Build(go.GetComponent<RectTransform>(), DialWidth, EntryHeight);
    }

    // ------------------------------------------------------------------
    // Breakdown dropdowns (2026-09-03)
    //
    // A chip is a total, and a total hides what it is made of. Clicking one
    // drops a small panel under it listing the pooled amount, everything in the
    // campfire stockpile filed under that chip (sticks and spears under Wood,
    // chunks under Stone - see ItemDef.hudCategory), and how many colonists are
    // on the job. New item types appear here on their own the moment they are
    // added to the catalog with hudListed set; nothing here needs updating for
    // planks or fish.
    //
    // The panel is parented to the ENTRY, not to the bar, so it follows its chip
    // wherever the horizontal layout puts it and needs no x arithmetic.
    // ------------------------------------------------------------------

    enum Breakdown { None, Wood, Food, Stone, Metal, Housing }

    private class BreakRow
    {
        public TextMeshProUGUI value;
        public System.Func<string> compute;
        public string last;
    }

    private Breakdown openPanel = Breakdown.None;
    private RectTransform dropdown;
    private VerticalLayoutGroup dropColumn;
    private readonly List<BreakRow> dropRows = new List<BreakRow>();

    const float DropWidth = 236f;
    const float DropRowHeight = 22f;

    void ToggleBreakdown(Breakdown kind, RectTransform anchor)
    {
        if (PauseController.BlockGameplayInput) return;

        if (openPanel == kind) { CloseBreakdown(); return; }
        openPanel = kind;
        BuildBreakdown(kind, anchor);
    }

    void CloseBreakdown()
    {
        openPanel = Breakdown.None;
        dropRows.Clear();
        if (dropdown != null) { Destroy(dropdown.gameObject); dropdown = null; dropColumn = null; }
    }

    void BuildBreakdown(Breakdown kind, RectTransform anchor)
    {
        if (dropdown != null) Destroy(dropdown.gameObject);
        dropRows.Clear();

        dropdown = MenuBuilder.Panel(anchor, "Breakdown", DropWidth, 100f);
        dropdown.anchorMin = dropdown.anchorMax = new Vector2(0f, 0f);
        dropdown.pivot = new Vector2(0f, 1f);
        dropdown.anchoredPosition = new Vector2(0f, -6f);

        dropColumn = MenuBuilder.Column(dropdown, 2f, new RectOffset(14, 14, 10, 12));

        if (kind == Breakdown.Housing) BuildHousingRows();
        else BuildResourceRows((ResourceNode.ResourceType)((int)kind - 1));

        MenuBuilder.Spacer(dropColumn.transform, 6f);
        MenuBuilder.MenuButton(dropColumn.transform, "Manage colonists", () => { CloseBreakdown(); OpenCampfire(); });

        RefreshBreakdown(true);
        MenuBuilder.FitPanelHeight(dropdown, dropColumn, 80f, 460f);
    }

    void BuildResourceRows(ResourceNode.ResourceType type)
    {
        Header(type.ToString());
        AddRow("In the stores", () => ResourceManager.Instance != null
            ? ResourceManager.Instance.Get(type).ToString() : "0");

        // Everything the campfire stockpile holds under this chip
        ItemDef[] all = ItemCatalog.All;
        for (int i = 0; i < all.Length; i++)
        {
            ItemDef item = all[i];
            if (!item.hudListed || item.hudCategory != type) continue;
            AddRow(item.displayName, () =>
            {
                BaseBuilding fire = BaseBuilding.FindAlive();
                return fire != null ? fire.Stockpile.Count(item).ToString() : "0";
            });
        }

        Header("Colonists");
        AddRow("On this job", () =>
        {
            BaseBuilding fire = BaseBuilding.ActiveList.Count > 0 ? BaseBuilding.ActiveList[0] : null;
            return fire != null ? WorkersOn(fire, type).ToString() : "0";
        });
    }

    void BuildHousingRows()
    {
        Header("Who sleeps where");

        PopulationManager pm = PopulationManager.Instance;
        if (pm == null || pm.HousingProviders.Count == 0)
        {
            AddRow("No shelter yet", () => "-");
            return;
        }

        var providers = pm.HousingProviders;
        for (int i = 0; i < providers.Count; i++)
        {
            IHousing home = providers[i];
            if (home == null) continue;
            string name = HousingName(home, i);
            AddRow(name, () =>
            {
                PopulationManager p = PopulationManager.Instance;
                if (p == null || home == null || !home.HousingAlive) return "-";
                return p.OccupantsOf(home) + " / " + home.HousingCapacity;
            });
        }

        AddRow("Homeless", () => PopulationManager.Instance != null
            ? PopulationManager.Instance.GetHomelessCount().ToString() : "0");
    }

    static string HousingName(IHousing home, int index)
    {
        if (home is BaseBuilding) return "Campfire";
        return "Hut " + index;
    }

    void Header(string text)
    {
        TextMeshProUGUI t = MenuBuilder.Label(dropColumn.transform, text.ToUpperInvariant(),
            MenuStyle.SmallSize, MenuStyle.TextAccent, TextAlignmentOptions.MidlineLeft);
        t.characterSpacing = 3f;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
    }

    /// <summary>
    /// One "name .... value" line. The value is refreshed from its closure on the
    /// normal UI tick and dirty-checked, so an open panel costs nothing while the
    /// numbers hold still.
    /// </summary>
    void AddRow(string label, System.Func<string> compute)
    {
        GameObject go = new GameObject("Row", typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(dropColumn.transform, false);
        go.GetComponent<LayoutElement>().preferredHeight = DropRowHeight;
        RectTransform rt = go.GetComponent<RectTransform>();

        TextMeshProUGUI name = MenuBuilder.Label(rt, label, MenuStyle.SmallSize + 1f,
            MenuStyle.TextMuted, TextAlignmentOptions.MidlineLeft);
        name.textWrappingMode = TextWrappingModes.NoWrap;
        name.overflowMode = TextOverflowModes.Ellipsis;
        MenuBuilder.Stretch(name.rectTransform);

        TextMeshProUGUI value = MenuBuilder.Label(rt, "0", MenuStyle.SmallSize + 2f,
            MenuStyle.TextPrimary, TextAlignmentOptions.MidlineRight);
        value.textWrappingMode = TextWrappingModes.NoWrap;
        MenuBuilder.Stretch(value.rectTransform);

        dropRows.Add(new BreakRow { value = value, compute = compute, last = null });
    }

    void RefreshBreakdown(bool force = false)
    {
        for (int i = 0; i < dropRows.Count; i++)
        {
            BreakRow row = dropRows[i];
            string text = row.compute();
            if (force || text != row.last)
            {
                row.last = text;
                row.value.text = text;
            }
        }
    }

    /// <summary>Clicking anywhere that is not the bar or the open panel closes it.</summary>
    void CloseBreakdownIfClickedAway()
    {
        if (openPanel == Breakdown.None || dropdown == null) return;
        if (!Input.GetMouseButtonDown(0) && !Input.GetMouseButtonDown(1)) return;

        Vector2 p = Input.mousePosition;
        if (RectTransformUtility.RectangleContainsScreenPoint(dropdown, p, null)) return;
        if (barRect != null && RectTransformUtility.RectangleContainsScreenPoint(barRect, p, null)) return;

        CloseBreakdown();
    }

    void OpenCampfire()
    {
        if (PauseController.BlockGameplayInput) return;
        BaseBuilding fire = BaseBuilding.ActiveList.Count > 0 ? BaseBuilding.ActiveList[0] : null;
        if (fire == null) return;
        WorkerAssignmentUI ui = fire.workerUI != null ? fire.workerUI : WorkerAssignmentUI.Instance;
        if (ui != null) ui.OpenPanel(fire);
    }

    // ------------------------------------------------------------------
    // Refresh
    // ------------------------------------------------------------------

    /// <summary>"Wood" in the caption tone, then the worker count a step dimmer.</summary>
    static string Caption(string name, string detail)
    {
        return name + " <color=#" + DimHex + ">· " + detail + "</color>";
    }

    void UpdateUI()
    {
        ResourceManager rm = ResourceManager.Instance;
        if (rm == null) return;

        BaseBuilding fire = BaseBuilding.ActiveList.Count > 0 ? BaseBuilding.ActiveList[0] : null;

        for (int i = 0; i < chips.Count; i++)
        {
            Chip c = chips[i];
            int v = rm.Get(c.type);
            if (v != c.lastValue)
            {
                c.lastValue = v;
                c.value.text = v.ToString();
            }

            int w = fire != null ? WorkersOn(fire, c.type) : 0;
            if (w != c.lastWorkers)
            {
                c.lastWorkers = w;
                c.workers.text = Caption(c.type.ToString(), w == 1 ? "1 worker" : w + " workers");
            }
        }

        if (PopulationManager.Instance != null)
        {
            int currentWorkers = PopulationManager.Instance.GetColonistCount();
            int housingCapacity = PopulationManager.Instance.GetHousingCapacity();

            if (currentWorkers != lastPopWorkers || housingCapacity != lastPopCapacity)
            {
                lastPopWorkers = currentWorkers;
                lastPopCapacity = housingCapacity;
                popValue.text = currentWorkers + " / " + housingCapacity;

                // Colour code based on housing status
                if (PopulationManager.Instance.HasHomelessWorkers())
                {
                    popValue.color = MenuStyle.TextDanger;
                    popLabel.text = Caption("Housing", "homeless!");
                }
                else if (currentWorkers >= housingCapacity)
                {
                    popValue.color = MenuStyle.TextAccent;
                    popLabel.text = Caption("Housing", "full");
                }
                else
                {
                    popValue.color = MenuStyle.TextPrimary;
                    popLabel.text = "Housing";
                }
            }
        }

        UpdateCalendar();
    }

    /// <summary>
    /// "Day 4" over "of 30 · quiet night ahead", turning red with "Raid tonight · 7"
    /// once the dawn roll says so, and "Night 4 · raid underway" while it lands.
    /// Repainted only when day, phase, verdict or size changes.
    /// </summary>
    void UpdateCalendar()
    {
        if (calValue == null) return;
        if (dayNight == null)
        {
            dayNight = FindAnyObjectByType<DayNightCycle>();
            if (dayNight == null) return;
        }

        int day = dayNight.GetCurrentDay();
        int total = GameManager.Instance != null ? GameManager.Instance.daysToSurvive : Difficulty.DaysToSurvive;
        RaidDirector rd = RaidDirector.Instance;
        bool raid = rd != null && rd.RaidTonight;
        int size = raid ? rd.PlannedSize : 0;
        bool night = dayNight.IsNightTime();

        int key = (((day * 128 + total) * 2 + (night ? 1 : 0)) * 2 + (raid ? 1 : 0)) * 64 + Mathf.Min(size, 63);
        if (key == lastCalKey) return;
        lastCalKey = key;

        calValue.text = (night ? "Night " : "Day ") + day;

        if (raid && night)
        {
            calValue.color = MenuStyle.TextDanger;
            calLabel.color = MenuStyle.TextDanger;
            calLabel.text = "Raid underway";
        }
        else if (raid)
        {
            calValue.color = MenuStyle.TextDanger;
            calLabel.color = MenuStyle.TextDanger;
            calLabel.text = Caption("Raid tonight", size + " raiders");
        }
        else
        {
            calValue.color = MenuStyle.TextPrimary;
            calLabel.color = MenuStyle.TextMuted;
            calLabel.text = Caption("of " + total, night ? "quiet" : "quiet night ahead");
        }
    }

    static int WorkersOn(BaseBuilding fire, ResourceNode.ResourceType t)
    {
        switch (t)
        {
            case ResourceNode.ResourceType.Food: return fire.foodWorkers;
            case ResourceNode.ResourceType.Stone: return fire.stoneWorkers;
            case ResourceNode.ResourceType.Metal: return fire.metalWorkers;
            default: return fire.woodWorkers;
        }
    }
}
