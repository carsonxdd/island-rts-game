using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The card for the building you clicked (2026-09-18): what it is, what tier it is, how
/// healthy it is, what it does for the colony, and the two things you can do to it -
/// upgrade it, or pull it down. Before this, no finished building except the campfire
/// answered a click at all.
/// </summary>
/// <remarks>
/// Bottom-LEFT on purpose: the build palette owns the bottom centre, so the two can be open
/// at once without overlapping. Like the palette it pulls and dirty-checks rather than
/// being pushed to, and like every other click surface it publishes
/// <see cref="PointerOver"/> so a click on its buttons does not also land on the world
/// behind it (see <see cref="PointerBlock"/>).
///
/// It decides nothing itself. Whether Upgrade is live, and the sentence it is greyed out
/// with, both come from <see cref="BuildingUpgrade.CanUpgrade"/>; the refund comes from
/// <see cref="DemolishTool"/>. Re-deriving either here is how the two would drift apart.
/// </remarks>
public class SelectedBuildingHUD : MonoBehaviour
{
    private static SelectedBuildingHUD instance;

    /// <summary>True while the mouse is over the card. Checked through <see cref="PointerBlock"/>.</summary>
    public static bool PointerOver { get; private set; }

    /// <summary>True while a building is selected - <see cref="PauseController"/> asks, so Esc closes this before it pauses.</summary>
    public static bool IsOpen => instance != null && instance.target != null;

    const float CardWidth = 300f;

    private GameObject target;
    private Health targetHealth;
    private BuildingType targetType;

    private RectTransform root;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI healthText;
    private RectTransform healthFill;
    private TextMeshProUGUI statText;
    private TextMeshProUGUI warningText;
    private Button upgradeButton;
    private TextMeshProUGUI upgradeLabel;
    private Button demolishButton;
    private TextMeshProUGUI demolishLabel;

    private int lastHealth = -1;
    private string lastUpgradeLabel;
    private bool lastUpgradeLive;

    public static void Ensure()
    {
        if (instance != null || SimHooks.Headless) return;
        GameObject go = new GameObject("[SelectedBuildingHUD]");
        instance = go.AddComponent<SelectedBuildingHUD>();
    }

    void Awake() { instance = this; }

    void OnDestroy() { if (instance == this) instance = null; }

    void OnDisable() { PointerOver = false; }

    /// <summary>Open the card on a building. Player-owned, identified buildings only.</summary>
    public static void Show(GameObject building)
    {
        if (SimHooks.Headless || building == null) return;
        Ensure();
        if (instance != null) instance.Open(building);
    }

    /// <summary>Close the card. Safe to call when nothing is selected.</summary>
    public static void Close()
    {
        if (instance != null) instance.Clear();
    }

    void Open(GameObject building)
    {
        IBuildingIdentity identity = building.GetComponent<IBuildingIdentity>();
        if (identity == null) return;

        target = building;
        targetType = identity.BuildingType;
        targetHealth = building.GetComponent<Health>();
        lastHealth = -1;
        lastUpgradeLabel = null;

        if (root == null) Build();
        root.gameObject.SetActive(true);
        RefreshTitle();
        DevQuests.Signal("building_card:open");
    }

    void Clear()
    {
        target = null;
        targetHealth = null;
        PointerOver = false;
        if (root != null) root.gameObject.SetActive(false);
        Tooltip.HideNow();
    }

    void Update()
    {
        if (target == null)
        {
            if (root != null && root.gameObject.activeSelf) Clear();
            return;
        }

        // The building died, was demolished, or became a construction site under us.
        if (targetHealth != null && !targetHealth.IsAlive) { Clear(); return; }

        PointerOver = root != null
            && root.gameObject.activeSelf
            && RectTransformUtility.RectangleContainsScreenPoint(root, Input.mousePosition, null);

        RefreshHealth();
        RefreshButtons();
    }

    // ------------------------------------------------------------------
    // Per-frame dirty checks
    // ------------------------------------------------------------------

    void RefreshTitle()
    {
        BuildingData data = BuildingUpgrade.DataOf(target);
        string name = data != null ? data.buildingName : targetType.ToString();
        int tier = data != null ? data.tier : 1;
        titleText.text = name + "   <size=80%><color=#9B978F>Level " + tier + "</color></size>";
        statText.text = StatLine();

        string warning = BuildingUpgrade.WarningFor(target);
        warningText.text = warning ?? "";
        warningText.gameObject.SetActive(!string.IsNullOrEmpty(warning));

        demolishLabel.text = "Demolish   <size=85%><color=#9B978F>+" + DemolishTool.RefundLine(targetType) + "</color></size>";
    }

    void RefreshHealth()
    {
        if (targetHealth == null) return;
        int hp = Mathf.CeilToInt(targetHealth.currentHealth);
        if (hp == lastHealth) return;
        lastHealth = hp;

        float fraction = targetHealth.maxHealth > 0f
            ? Mathf.Clamp01(targetHealth.currentHealth / targetHealth.maxHealth)
            : 0f;
        healthText.text = hp + " / " + Mathf.CeilToInt(targetHealth.maxHealth) + " HP";
        healthFill.anchorMax = new Vector2(fraction, 1f);
        healthFill.GetComponent<Image>().color = fraction > 0.6f
            ? new Color(0.45f, 0.75f, 0.42f, 1f)
            : fraction > 0.3f ? new Color(0.88f, 0.76f, 0.35f, 1f) : MenuStyle.TextDanger;
    }

    void RefreshButtons()
    {
        BuildingData next;
        string reason;
        bool can = BuildingUpgrade.CanUpgrade(target, out next, out reason);

        string label = can
            ? "Upgrade to " + next.buildingName + "   <size=85%><color=#9B978F>" + next.CostLine() + "</color></size>"
            : reason;

        if (label == lastUpgradeLabel && can == lastUpgradeLive) return;
        lastUpgradeLabel = label;
        lastUpgradeLive = can;

        upgradeLabel.text = label;
        upgradeLabel.color = can ? MenuStyle.TextPrimary : MenuStyle.TextMuted;
        upgradeButton.interactable = can;
        ((Image)upgradeButton.targetGraphic).color = can ? MenuStyle.ButtonFill : MenuStyle.ButtonDisabled;
    }

    /// <summary>What this building does for the colony, in one line.</summary>
    string StatLine()
    {
        Hut housing = target.GetComponent<Hut>();
        if (housing != null)
        {
            int beds = housing.HousingCapacity;
            return beds == 1 ? "Sleeps 1 colonist" : "Sleeps " + beds + " colonists";
        }

        if (target.GetComponent<Storehouse>() != null)
            return "Drop-off point · +" + Storehouse.RoomBonus + " stockpile room";

        if (target.GetComponent<Workshop>() != null)
            return "Workbench · makes tools and weapons at double speed";

        if (target.GetComponent<Watchtower>() != null)
            return "Watches the ground around it";

        if (target.GetComponent<Shipyard>() != null)
            return "The way off the island";

        BuildingData data = BuildingUpgrade.DataOf(target);
        return data != null ? data.description : "";
    }

    // ------------------------------------------------------------------
    // Actions
    // ------------------------------------------------------------------

    void OnUpgrade()
    {
        GameObject building = target;
        if (building == null) return;
        // The building is destroyed by the upgrade, so the card closes either way.
        BuildingUpgrade.TryUpgrade(building);
        Clear();
    }

    void OnDemolish()
    {
        GameObject building = target;
        BuildingType type = targetType;
        Clear();
        if (building != null) DemolishTool.Demolish(building, type);
    }

    // ------------------------------------------------------------------
    // Construction
    // ------------------------------------------------------------------

    void Build()
    {
        Canvas canvas = MenuBuilder.CreateCanvas("SelectedBuildingCanvas", 50);
        canvas.transform.SetParent(transform, false);

        GameObject box = new GameObject("BuildingCard", typeof(RectTransform), typeof(Image),
            typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        box.transform.SetParent(canvas.transform, false);
        root = box.GetComponent<RectTransform>();
        root.anchorMin = root.anchorMax = new Vector2(0f, 0f);
        root.pivot = new Vector2(0f, 0f);
        root.anchoredPosition = new Vector2(16f, 104f);   // clear of the PlayerHUD strip

        Image bg = box.GetComponent<Image>();
        bg.color = new Color(MenuStyle.PanelFill.r, MenuStyle.PanelFill.g, MenuStyle.PanelFill.b, 0.92f);
        bg.raycastTarget = true;   // the card is a control: swallow clicks on its background

        VerticalLayoutGroup col = box.GetComponent<VerticalLayoutGroup>();
        col.padding = new RectOffset(14, 14, 10, 12);
        col.spacing = 6f;
        col.childControlWidth = true;
        col.childControlHeight = true;
        col.childForceExpandWidth = true;
        col.childForceExpandHeight = false;
        col.childAlignment = TextAnchor.UpperLeft;

        ContentSizeFitter fit = box.GetComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        box.GetComponent<RectTransform>().sizeDelta = new Vector2(CardWidth, 0f);

        titleText = MenuBuilder.Label(box.transform, "", MenuStyle.BodySize, MenuStyle.TextPrimary, TextAlignmentOptions.MidlineLeft);
        titleText.textWrappingMode = TextWrappingModes.NoWrap;
        titleText.overflowMode = TextOverflowModes.Ellipsis;
        titleText.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

        BuildHealthBar(box.transform);

        statText = MenuBuilder.Label(box.transform, "", MenuStyle.SmallSize, MenuStyle.TextMuted, TextAlignmentOptions.TopLeft);
        statText.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

        warningText = MenuBuilder.Label(box.transform, "", MenuStyle.SmallSize - 1f, MenuStyle.TextDanger, TextAlignmentOptions.TopLeft);
        warningText.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

        upgradeButton = MenuBuilder.MenuButton(box.transform, "", OnUpgrade);
        LayoutElement upgradeLayout = upgradeButton.GetComponent<LayoutElement>();
        upgradeLayout.preferredHeight = upgradeLayout.minHeight = 36f;
        upgradeLabel = upgradeButton.GetComponentInChildren<TextMeshProUGUI>();
        upgradeLabel.fontSize = MenuStyle.SmallSize + 1f;
        upgradeLabel.textWrappingMode = TextWrappingModes.NoWrap;
        upgradeLabel.overflowMode = TextOverflowModes.Ellipsis;

        demolishButton = MenuBuilder.MenuButton(box.transform, "", OnDemolish);
        LayoutElement demolishLayout = demolishButton.GetComponent<LayoutElement>();
        demolishLayout.preferredHeight = demolishLayout.minHeight = 30f;
        demolishLabel = demolishButton.GetComponentInChildren<TextMeshProUGUI>();
        demolishLabel.fontSize = MenuStyle.SmallSize;
        demolishLabel.color = MenuStyle.TextDanger;
        demolishLabel.textWrappingMode = TextWrappingModes.NoWrap;
        demolishLabel.overflowMode = TextOverflowModes.Ellipsis;

        root.gameObject.SetActive(false);
    }

    void BuildHealthBar(Transform parent)
    {
        GameObject row = new GameObject("Health", typeof(RectTransform), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        row.GetComponent<LayoutElement>().preferredHeight = 20f;

        RectTransform track = MenuBuilder.SimpleImage(row.transform, "Track", new Color(0f, 0f, 0f, 0.45f));
        track.anchorMin = new Vector2(0f, 0.15f);
        track.anchorMax = new Vector2(1f, 0.85f);
        track.offsetMin = Vector2.zero;
        track.offsetMax = Vector2.zero;

        healthFill = MenuBuilder.SimpleImage(track, "Fill", new Color(0.45f, 0.75f, 0.42f, 1f));
        healthFill.anchorMin = Vector2.zero;
        healthFill.anchorMax = new Vector2(1f, 1f);
        healthFill.offsetMin = Vector2.zero;
        healthFill.offsetMax = Vector2.zero;

        healthText = MenuBuilder.Label(track, "", MenuStyle.SmallSize - 2f, MenuStyle.TextPrimary, TextAlignmentOptions.Center);
        RectTransform textRect = healthText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        healthText.textWrappingMode = TextWrappingModes.NoWrap;
        healthText.overflowMode = TextOverflowModes.Overflow;
    }
}
