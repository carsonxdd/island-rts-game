using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The colony HUD: a bar of resource chips along the top-left (wood, food,
/// stone, metal — each with its stock and how many workers are on it) and a
/// housing chip. Built entirely in code on the menu system's widgets, so it
/// needs no scene wiring and picks up the UI Scale setting; the old
/// hand-built ResourcePanel is removed by the legacy cleanup step.
///
/// Every chip is a button that opens the campfire's assignment panel, so the
/// player's main economic lever is one click from the numbers they are
/// reacting to. Refreshed on a slow interval and dirty-checked, so an
/// unchanged number never costs a string allocation.
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

    private static readonly Color WoodColor = new Color(0.66f, 0.45f, 0.26f);
    private static readonly Color FoodColor = new Color(0.78f, 0.25f, 0.22f);
    private static readonly Color StoneColor = new Color(0.55f, 0.58f, 0.62f);
    private static readonly Color MetalColor = new Color(0.62f, 0.72f, 0.82f);
    private static readonly Color PopColor = new Color(0.92f, 0.80f, 0.45f);

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

        // Bar container: top-left, sized by its content
        GameObject bar = new GameObject("ResourceBar", typeof(RectTransform), typeof(Image),
            typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        bar.transform.SetParent(canvas.transform, false);
        RectTransform brt = bar.GetComponent<RectTransform>();
        brt.anchorMin = brt.anchorMax = new Vector2(0f, 1f);
        brt.pivot = new Vector2(0f, 1f);
        brt.anchoredPosition = new Vector2(16f, -16f);

        Image bg = bar.GetComponent<Image>();
        bg.color = new Color(MenuStyle.PanelFill.r, MenuStyle.PanelFill.g, MenuStyle.PanelFill.b, 0.82f);
        bg.raycastTarget = false;

        HorizontalLayoutGroup row = bar.GetComponent<HorizontalLayoutGroup>();
        row.spacing = 8f;
        row.padding = new RectOffset(8, 8, 6, 6);
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
        row.childAlignment = TextAnchor.MiddleLeft;

        ContentSizeFitter fit = bar.GetComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        MenuBuilder.AddBorder(brt, MenuStyle.Divider, 1f);

        chips.Add(MakeChip(bar.transform, ResourceNode.ResourceType.Wood, "Wood"));
        chips.Add(MakeChip(bar.transform, ResourceNode.ResourceType.Food, "Food"));
        chips.Add(MakeChip(bar.transform, ResourceNode.ResourceType.Stone, "Stone"));
        chips.Add(MakeChip(bar.transform, ResourceNode.ResourceType.Metal, "Metal"));

        // Housing chip
        RectTransform pop = ChipShell(bar.transform, "Housing", PopColor, out popValue, out popLabel, "Housing");
        pop.GetComponent<Button>().onClick.AddListener(OpenCampfire);
    }

    Chip MakeChip(Transform parent, ResourceNode.ResourceType type, string label)
    {
        Chip chip = new Chip { type = type };
        RectTransform rt = ChipShell(parent, label, ColorFor(type), out chip.value, out chip.workers, label);
        rt.GetComponent<Button>().onClick.AddListener(OpenCampfire);
        return chip;
    }

    /// <summary>
    /// One chip: a coloured swatch, a big value, and a small caption line
    /// underneath (the resource name and its worker count). The whole chip is
    /// a button — the Image is the raycast surface.
    /// </summary>
    RectTransform ChipShell(Transform parent, string name, Color swatch,
        out TextMeshProUGUI value, out TextMeshProUGUI caption, string captionText)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        LayoutElement le = go.GetComponent<LayoutElement>();
        le.preferredWidth = 128f;
        le.preferredHeight = 50f;

        Image img = go.GetComponent<Image>();
        img.color = MenuStyle.ButtonFill;
        img.raycastTarget = true;

        Button btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1f);
        cb.pressedColor = new Color(1.8f, 1.6f, 1.2f, 1f);
        cb.fadeDuration = 0.08f;
        btn.colors = cb;

        // Swatch strip on the left edge
        RectTransform sw = MenuBuilder.SimpleImage(go.transform, "Swatch", swatch);
        sw.anchorMin = new Vector2(0f, 0f);
        sw.anchorMax = new Vector2(0f, 1f);
        sw.pivot = new Vector2(0f, 0.5f);
        sw.anchoredPosition = Vector2.zero;
        sw.sizeDelta = new Vector2(6f, 0f);

        value = MenuBuilder.Label(go.transform, "0", MenuStyle.ButtonSize, MenuStyle.TextPrimary, TextAlignmentOptions.MidlineLeft);
        RectTransform vrt = value.rectTransform;
        vrt.anchorMin = new Vector2(0f, 0.42f);
        vrt.anchorMax = new Vector2(1f, 1f);
        vrt.offsetMin = new Vector2(14f, 0f);
        vrt.offsetMax = new Vector2(-6f, -2f);
        value.fontStyle = FontStyles.Bold;

        caption = MenuBuilder.Label(go.transform, captionText, MenuStyle.SmallSize, MenuStyle.TextMuted, TextAlignmentOptions.MidlineLeft);
        RectTransform crt = caption.rectTransform;
        crt.anchorMin = new Vector2(0f, 0f);
        crt.anchorMax = new Vector2(1f, 0.42f);
        crt.offsetMin = new Vector2(14f, 2f);
        crt.offsetMax = new Vector2(-6f, 0f);

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
                c.workers.text = c.type + (w == 1 ? "  ·  1 worker" : "  ·  " + w + " workers");
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
                    popLabel.text = "Housing  ·  homeless!";
                }
                else if (currentWorkers >= housingCapacity)
                {
                    popValue.color = MenuStyle.TextAccent;
                    popLabel.text = "Housing  ·  full";
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
