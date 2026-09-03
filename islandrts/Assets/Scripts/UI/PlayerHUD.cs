using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The player character's strip at the bottom of the screen (2026-09-02):
/// name, the six inventory slots, and the current activity. Code-built on the
/// menu widgets, no scene wiring — it creates itself the first frame a
/// PlayerCharacter exists. Slots repaint only when the inventory reports a
/// change, the activity line only when its text differs.
/// </summary>
public class PlayerHUD : MonoBehaviour
{
    private static PlayerHUD instance;

    const float SlotSize = 54f;
    const float SlotGap = 6f;

    private class SlotView
    {
        public Image frame;
        public Image fill;
        public TextMeshProUGUI glyph;
        public TextMeshProUGUI count;
    }

    private PlayerCharacter player;
    private RectTransform root;
    private TextMeshProUGUI nameText;
    private TextMeshProUGUI activityText;
    private SlotView[] slots;

    private bool inventoryDirty = true;
    private string lastActivity;
    private string lastName;

    /// <summary>Create the HUD for this scene if it does not exist yet. Skipped under the sim.</summary>
    public static void Ensure()
    {
        if (instance != null || SimHooks.Simulating) return;
        GameObject go = new GameObject("[PlayerHUD]");
        instance = go.AddComponent<PlayerHUD>();
    }

    void Awake()
    {
        instance = this;
    }

    void OnDestroy()
    {
        if (player != null) player.Inventory.OnChanged -= MarkDirty;
        if (instance == this) instance = null;
    }

    void Update()
    {
        PlayerCharacter current = PlayerCharacter.Instance;
        if (current != player)
        {
            if (player != null) player.Inventory.OnChanged -= MarkDirty;
            player = current;
            if (player != null) player.Inventory.OnChanged += MarkDirty;
            inventoryDirty = true;
        }

        if (root == null) Build();

        bool show = player != null;
        if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
        if (!show) return;

        string name = PlayerProfile.Name;
        if (name != lastName)
        {
            lastName = name;
            nameText.text = name;
        }

        string activity = player.Activity;
        if (activity != lastActivity)
        {
            lastActivity = activity;
            activityText.text = activity;
        }

        if (inventoryDirty)
        {
            inventoryDirty = false;
            RepaintSlots();
        }
    }

    void MarkDirty() { inventoryDirty = true; }

    // ------------------------------------------------------------------
    // Construction
    // ------------------------------------------------------------------

    void Build()
    {
        Canvas canvas = MenuBuilder.CreateCanvas("PlayerHUDCanvas", 45);
        canvas.transform.SetParent(transform, false);

        // Bottom-centre strip, sized to its content
        GameObject bar = new GameObject("PlayerBar", typeof(RectTransform), typeof(Image),
            typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        bar.transform.SetParent(canvas.transform, false);
        root = bar.GetComponent<RectTransform>();
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0f);
        root.pivot = new Vector2(0.5f, 0f);
        root.anchoredPosition = new Vector2(0f, 14f);

        Image bg = bar.GetComponent<Image>();
        bg.color = new Color(MenuStyle.PanelFill.r, MenuStyle.PanelFill.g, MenuStyle.PanelFill.b, 0.84f);
        bg.raycastTarget = false;

        VerticalLayoutGroup col = bar.GetComponent<VerticalLayoutGroup>();
        col.padding = new RectOffset(12, 12, 6, 8);
        col.spacing = 4f;
        col.childControlWidth = true;
        col.childControlHeight = true;
        col.childForceExpandWidth = true;
        col.childForceExpandHeight = false;
        col.childAlignment = TextAnchor.UpperCenter;

        ContentSizeFitter fit = bar.GetComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Line 1: name (accent) — the activity sits on the same line, to the right
        GameObject top = new GameObject("TopLine", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        top.transform.SetParent(bar.transform, false);
        top.GetComponent<LayoutElement>().preferredHeight = 22f;
        HorizontalLayoutGroup topRow = top.GetComponent<HorizontalLayoutGroup>();
        topRow.spacing = 12f;
        topRow.childControlWidth = true;
        topRow.childControlHeight = true;
        topRow.childForceExpandWidth = false;
        topRow.childForceExpandHeight = true;
        topRow.childAlignment = TextAnchor.MiddleLeft;

        nameText = MenuBuilder.Label(top.transform, "", MenuStyle.SmallSize + 2f, MenuStyle.TextAccent, TextAlignmentOptions.MidlineLeft);
        nameText.fontStyle = FontStyles.Bold;
        nameText.textWrappingMode = TextWrappingModes.NoWrap;
        nameText.overflowMode = TextOverflowModes.Overflow;
        nameText.gameObject.AddComponent<LayoutElement>().minWidth = 90f;

        activityText = MenuBuilder.Label(top.transform, "", MenuStyle.SmallSize, MenuStyle.TextMuted, TextAlignmentOptions.MidlineLeft);
        activityText.textWrappingMode = TextWrappingModes.NoWrap;
        activityText.overflowMode = TextOverflowModes.Overflow;
        activityText.gameObject.AddComponent<LayoutElement>().minWidth = 160f;

        // Line 2: the slots
        GameObject slotRowGo = new GameObject("Slots", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        slotRowGo.transform.SetParent(bar.transform, false);
        slotRowGo.GetComponent<LayoutElement>().preferredHeight = SlotSize;
        HorizontalLayoutGroup slotRow = slotRowGo.GetComponent<HorizontalLayoutGroup>();
        slotRow.spacing = SlotGap;
        slotRow.childControlWidth = true;
        slotRow.childControlHeight = true;
        slotRow.childForceExpandWidth = false;
        slotRow.childForceExpandHeight = false;
        slotRow.childAlignment = TextAnchor.MiddleCenter;

        slots = new SlotView[PlayerCharacter.InventorySlots];
        for (int i = 0; i < slots.Length; i++) slots[i] = MakeSlot(slotRowGo.transform, i);
    }

    SlotView MakeSlot(Transform parent, int index)
    {
        SlotView v = new SlotView();

        GameObject go = new GameObject("Slot" + index, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        LayoutElement le = go.GetComponent<LayoutElement>();
        le.preferredWidth = le.minWidth = SlotSize;
        le.preferredHeight = le.minHeight = SlotSize;

        v.frame = go.GetComponent<Image>();
        v.frame.color = MenuStyle.ButtonFill;
        v.frame.raycastTarget = false;
        MenuBuilder.AddBorder(go.GetComponent<RectTransform>(), MenuStyle.Divider, 1f);

        // Item colour swatch across the bottom edge
        RectTransform fill = MenuBuilder.SimpleImage(go.transform, "Fill", Color.clear);
        fill.anchorMin = new Vector2(0f, 0f);
        fill.anchorMax = new Vector2(1f, 0f);
        fill.pivot = new Vector2(0.5f, 0f);
        fill.anchoredPosition = Vector2.zero;
        fill.sizeDelta = new Vector2(0f, 4f);
        v.fill = fill.GetComponent<Image>();

        v.glyph = MenuBuilder.Label(go.transform, "", MenuStyle.BodySize, MenuStyle.TextPrimary, TextAlignmentOptions.Center);
        MenuBuilder.Stretch(v.glyph.rectTransform);
        v.glyph.rectTransform.offsetMax = new Vector2(0f, -4f);
        v.glyph.fontStyle = FontStyles.Bold;
        v.glyph.textWrappingMode = TextWrappingModes.NoWrap;

        v.count = MenuBuilder.Label(go.transform, "", MenuStyle.SmallSize - 2f, MenuStyle.TextAccent, TextAlignmentOptions.BottomRight);
        MenuBuilder.Stretch(v.count.rectTransform);
        v.count.rectTransform.offsetMin = new Vector2(0f, 5f);
        v.count.rectTransform.offsetMax = new Vector2(-4f, 0f);
        v.count.textWrappingMode = TextWrappingModes.NoWrap;

        return v;
    }

    void RepaintSlots()
    {
        if (player == null || slots == null) return;
        Inventory inv = player.Inventory;

        for (int i = 0; i < slots.Length; i++)
        {
            SlotView v = slots[i];
            Inventory.Slot s = i < inv.SlotCount ? inv[i] : default;

            if (s.IsEmpty)
            {
                if (v.glyph.text.Length != 0) v.glyph.text = "";
                if (v.count.text.Length != 0) v.count.text = "";
                v.fill.color = Color.clear;
                continue;
            }

            if (v.glyph.text != s.item.glyph) v.glyph.text = s.item.glyph;
            string n = s.count.ToString();
            if (v.count.text != n) v.count.text = n;
            v.fill.color = s.item.color;
        }
    }
}
