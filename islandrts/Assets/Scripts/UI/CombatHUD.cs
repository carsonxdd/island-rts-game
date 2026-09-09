using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The militia's orders on the main screen (2026-09-07): a small box in the
/// bottom-right corner, on the same row as the character strip, with the three
/// stance buttons (Defensive · Offensive · Follow, the active one lit) and the
/// five formation buttons under them. Appears once the colony has a warrior —
/// there is nothing to command before that — and hides again if it loses them
/// all. Code-built on the menu widgets like <see cref="PlayerHUD"/>; never
/// built under the balance sim.
/// </summary>
/// <remarks>
/// It is a second face on the same statics the campfire panel's steppers set
/// (<see cref="GuardStance.Set"/> / <see cref="Formation.Set"/>); both sides
/// dirty-check the statics every frame, so a change made anywhere shows
/// everywhere at once. The three stance hotkeys (<c>KeyBindings</c>, "Militia"
/// group) are read here, gated on gameplay input and on the Controls screen not
/// capturing a key, and only while the box is showing.
/// </remarks>
public class CombatHUD : MonoBehaviour
{
    private static CombatHUD instance;

    const float StanceHeight = 36f;
    const float StanceWidth = 104f;
    const float FormationHeight = 28f;
    const float FormationWidth = 62f;

    private RectTransform root;
    private TextMeshProUGUI countText;
    private TextMeshProUGUI keyHint;
    private Button[] stanceButtons;
    private Button[] formationButtons;

    private int lastStance = -1;
    private int lastFormation = -1;
    private int lastCount = -1;

    /// <summary>Create the box for this scene if it does not exist yet. Skipped under the sim.</summary>
    public static void Ensure()
    {
        if (instance != null || SimHooks.Simulating) return;
        GameObject go = new GameObject("[CombatHUD]");
        instance = go.AddComponent<CombatHUD>();
    }

    void Awake() { instance = this; }

    void OnDestroy() { if (instance == this) instance = null; }

    void Update()
    {
        if (root == null) Build();

        bool show = Warrior.ActiveList.Count > 0;
        if (root.gameObject.activeSelf != show)
        {
            root.gameObject.SetActive(show);
            if (show) RefreshKeyHint();   // a rebind while hidden lands on the next show
            DevQuests.Signal(show ? "combat_box:shown" : "combat_box:hidden");
        }
        if (!show) return;

        // Hotkeys: gameplay input only, and never while the Controls screen waits for a key
        bool capturing = MenuScreens.Instance != null && MenuScreens.Instance.IsCapturingKey;
        if (!PauseController.BlockGameplayInput && !capturing)
        {
            if (KeyBindings.Down(KeyBindings.Action.StanceDefensive)) GuardStance.Set(GuardStance.Mode.Defensive);
            else if (KeyBindings.Down(KeyBindings.Action.StanceOffensive)) GuardStance.Set(GuardStance.Mode.Offensive);
            else if (KeyBindings.Down(KeyBindings.Action.StanceFollow))
            {
                GuardStance.Set(GuardStance.Mode.Follow);
                DevQuests.Signal("stance_key:follow");
            }
        }

        int stance = (int)GuardStance.Active;
        if (stance != lastStance)
        {
            lastStance = stance;
            MenuBuilder.TintTabs(stanceButtons, stance);
        }

        int formation = (int)Formation.Active;
        if (formation != lastFormation)
        {
            lastFormation = formation;
            MenuBuilder.TintTabs(formationButtons, formation);
        }

        int count = Warrior.ActiveList.Count;
        if (count != lastCount)
        {
            lastCount = count;
            countText.text = count == 1 ? "1 warrior" : count + " warriors";
        }
    }

    void RefreshKeyHint()
    {
        keyHint.text =
            KeyBindings.Name(KeyBindings.Get(KeyBindings.Action.StanceDefensive).primary) + " · " +
            KeyBindings.Name(KeyBindings.Get(KeyBindings.Action.StanceOffensive).primary) + " · " +
            KeyBindings.Name(KeyBindings.Get(KeyBindings.Action.StanceFollow).primary);
    }

    // ------------------------------------------------------------------
    // Construction
    // ------------------------------------------------------------------

    void Build()
    {
        Canvas canvas = MenuBuilder.CreateCanvas("CombatHUDCanvas", 45);
        canvas.transform.SetParent(transform, false);

        // Bottom-right box, sized to its content, on the character strip's row
        GameObject box = new GameObject("MilitiaBox", typeof(RectTransform), typeof(Image),
            typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        box.transform.SetParent(canvas.transform, false);
        root = box.GetComponent<RectTransform>();
        root.anchorMin = root.anchorMax = new Vector2(1f, 0f);
        root.pivot = new Vector2(1f, 0f);
        root.anchoredPosition = new Vector2(-16f, 14f);

        Image bg = box.GetComponent<Image>();
        bg.color = new Color(MenuStyle.PanelFill.r, MenuStyle.PanelFill.g, MenuStyle.PanelFill.b, 0.84f);
        bg.raycastTarget = true;   // the box is a control: swallow clicks on its background

        VerticalLayoutGroup col = box.GetComponent<VerticalLayoutGroup>();
        col.padding = new RectOffset(12, 12, 6, 8);
        col.spacing = 5f;
        col.childControlWidth = true;
        col.childControlHeight = true;
        col.childForceExpandWidth = true;
        col.childForceExpandHeight = false;
        col.childAlignment = TextAnchor.UpperCenter;

        ContentSizeFitter fit = box.GetComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Line 1: MILITIA · N warriors · key hint
        GameObject top = new GameObject("TopLine", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        top.transform.SetParent(box.transform, false);
        top.GetComponent<LayoutElement>().preferredHeight = 22f;
        HorizontalLayoutGroup topRow = top.GetComponent<HorizontalLayoutGroup>();
        topRow.spacing = 10f;
        topRow.childControlWidth = true;
        topRow.childControlHeight = true;
        topRow.childForceExpandWidth = false;
        topRow.childForceExpandHeight = true;
        topRow.childAlignment = TextAnchor.MiddleLeft;

        TextMeshProUGUI title = MenuBuilder.Label(top.transform, "MILITIA", MenuStyle.SmallSize, MenuStyle.TextAccent, TextAlignmentOptions.MidlineLeft);
        title.characterSpacing = 4f;
        title.textWrappingMode = TextWrappingModes.NoWrap;
        title.overflowMode = TextOverflowModes.Overflow;

        countText = MenuBuilder.Label(top.transform, "", MenuStyle.SmallSize, MenuStyle.TextMuted, TextAlignmentOptions.MidlineLeft);
        countText.textWrappingMode = TextWrappingModes.NoWrap;
        countText.overflowMode = TextOverflowModes.Overflow;
        countText.gameObject.AddComponent<LayoutElement>().minWidth = 80f;

        keyHint = MenuBuilder.Label(top.transform, "", MenuStyle.SmallSize - 2f, MenuStyle.TextMuted, TextAlignmentOptions.MidlineRight);
        keyHint.textWrappingMode = TextWrappingModes.NoWrap;
        keyHint.overflowMode = TextOverflowModes.Overflow;
        keyHint.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        // Line 2: the stances
        stanceButtons = ButtonRow(box.transform, GuardStance.Names, StanceWidth, StanceHeight, MenuStyle.SmallSize + 1f,
            i => { GuardStance.Set((GuardStance.Mode)i); DevQuests.Signal("combat_box:stance"); });

        // Line 3: the formations
        formationButtons = ButtonRow(box.transform, Formation.Names, FormationWidth, FormationHeight, MenuStyle.SmallSize - 1f,
            i => Formation.Set((Formation.Kind)i));

        RefreshKeyHint();
        lastStance = lastFormation = lastCount = -1;
    }

    /// <summary>A row of fixed-size buttons; the caller tints the active one with <see cref="MenuBuilder.TintTabs"/>.</summary>
    static Button[] ButtonRow(Transform parent, string[] names, float width, float height, float fontSize, System.Action<int> onPick)
    {
        GameObject rowGo = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        rowGo.transform.SetParent(parent, false);
        rowGo.GetComponent<LayoutElement>().preferredHeight = height;
        HorizontalLayoutGroup row = rowGo.GetComponent<HorizontalLayoutGroup>();
        row.spacing = 6f;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = true;
        row.childAlignment = TextAnchor.MiddleCenter;

        Button[] buttons = new Button[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            int idx = i;
            Button b = MenuBuilder.MenuButton(rowGo.transform, names[i], () => onPick(idx));
            LayoutElement le = b.GetComponent<LayoutElement>();
            le.preferredHeight = le.minHeight = height;
            le.preferredWidth = le.minWidth = width;
            TextMeshProUGUI label = b.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.fontSize = fontSize;
                label.textWrappingMode = TextWrappingModes.NoWrap;
            }
            buttons[i] = b;
        }
        return buttons;
    }
}
