using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The colony HUD: one continuous bar along the top-left holding five equal
/// entries — wood, food, stone, metal (stock + how many workers are on it)
/// and housing — separated by thin dividers. Built entirely in code on the
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
        public TextMeshProUGUI value;
        public TextMeshProUGUI workers;
        public int lastValue = -1;
        public int lastWorkers = -1;
    }

    private readonly List<Chip> chips = new List<Chip>();
    private TextMeshProUGUI popValue;
    private TextMeshProUGUI popLabel;
    private int lastPopWorkers = -1, lastPopCapacity = -1;

    // ---- metrics (reference pixels) ----
    const float EntryWidth = 150f;      // fits "Stone · 12 workers" at CaptionSize with room to spare
    const float EntryHeight = 54f;
    const float TextInset = 14f;        // shared left edge of amount and caption
    const float AmountSize = 24f;
    const float CaptionSize = 13f;
    const float SwatchWidth = 4f;

    private static readonly Color WoodColor = new Color(0.66f, 0.45f, 0.26f);
    private static readonly Color FoodColor = new Color(0.78f, 0.25f, 0.22f);
    private static readonly Color StoneColor = new Color(0.55f, 0.58f, 0.62f);
    private static readonly Color MetalColor = new Color(0.62f, 0.72f, 0.82f);
    private static readonly Color PopColor = new Color(0.92f, 0.80f, 0.45f);

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
        UpdateUI();
    }

    void Update()
    {
        // Only update UI periodically (not every frame for performance)
        timeSinceUpdate += Time.unscaledDeltaTime;

        if (timeSinceUpdate >= updateInterval)
        {
            timeSinceUpdate = 0f;
            UpdateUI();
        }
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
        RectTransform brt = bar.GetComponent<RectTransform>();
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
        pop.GetComponent<Button>().onClick.AddListener(OpenCampfire);
    }

    Chip MakeChip(Transform parent, ResourceNode.ResourceType type, string label)
    {
        Chip chip = new Chip { type = type };
        RectTransform rt = Entry(parent, label, ColorFor(type), out chip.value, out chip.workers, label);
        rt.GetComponent<Button>().onClick.AddListener(OpenCampfire);
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
        out TextMeshProUGUI value, out TextMeshProUGUI caption, string captionText)
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
            int currentWorkers = PopulationManager.Instance.GetCurrentWorkers();
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
