using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Runtime uGUI construction helpers — the vocabulary every menu screen is
/// written in (panel, title, button, slider row, toggle row, dropdown row).
///
/// Built in code rather than authored in a scene for the same reason CraftingUI
/// is: zero scene wiring means no prefab to keep in sync, no Inspector
/// references to break, and screens that are diffable in git. The visual layer
/// is entirely <see cref="MenuStyle"/>, so this file is structure only.
/// </summary>
public static class MenuBuilder
{
    /// <summary>Width of a slider knob, in reference pixels.</summary>
    private const float HandleWidth = 16f;

    /// <summary>Full-screen canvas that renders above gameplay UI and survives a paused game.</summary>
    public static Canvas CreateCanvas(string name, int sortOrder = 500)
    {
        GameObject go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortOrder;

        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = MenuScaler.BaseReference;
        scaler.matchWidthOrHeight = 0.5f;
        MenuScaler.Register(scaler);   // picks up the player's UI Scale setting

        EnsureEventSystem();
        return canvas;
    }

    /// <summary>
    /// uGUI is inert without an EventSystem, and the game scene may not have
    /// one (its existing UI is largely non-interactive). Menus must never
    /// depend on the scene having provided it.
    /// </summary>
    public static void EnsureEventSystem()
    {
        if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }

    public static RectTransform FullScreen(Transform parent, string name, Color color)
    {
        GameObject go = new GameObject(name, typeof(Image));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.color = color;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    /// <summary>A bordered box. The border is four thin images, so it stays crisp at any size.</summary>
    public static RectTransform Panel(Transform parent, string name, float width, float height)
    {
        GameObject go = new GameObject(name, typeof(Image));
        go.transform.SetParent(parent, false);

        Image img = go.GetComponent<Image>();
        img.color = MenuStyle.PanelFill;
        if (MenuStyle.PanelSprite != null)
        {
            img.sprite = MenuStyle.PanelSprite;
            img.type = Image.Type.Sliced;
        }

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(width, height);
        rt.anchoredPosition = Vector2.zero;

        if (MenuStyle.PanelSprite == null) AddBorder(rt, MenuStyle.PanelBorder, MenuStyle.BorderWidth);
        return rt;
    }

    /// <summary>Four edge strips forming a hollow rectangle outline.</summary>
    public static void AddBorder(RectTransform target, Color color, float thickness)
    {
        AddEdge(target, "BorderTop", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, thickness), new Vector2(0f, -thickness * 0.5f), color);
        AddEdge(target, "BorderBottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, thickness), new Vector2(0f, thickness * 0.5f), color);
        AddEdge(target, "BorderLeft", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(thickness, 0f), new Vector2(thickness * 0.5f, 0f), color);
        AddEdge(target, "BorderRight", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(thickness, 0f), new Vector2(-thickness * 0.5f, 0f), color);
    }

    private static void AddEdge(RectTransform parent, string name, Vector2 aMin, Vector2 aMax,
                                Vector2 size, Vector2 offset, Color color)
    {
        GameObject go = new GameObject(name, typeof(Image));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.sizeDelta = size;
        rt.anchoredPosition = offset;
    }

    public static TextMeshProUGUI Label(Transform parent, string text, float size, Color color,
                                        TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        GameObject go = new GameObject("Label", typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = align;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        return tmp;
    }

    /// <summary>A stacked vertical column with uniform spacing — the spine of every menu screen.</summary>
    public static VerticalLayoutGroup Column(Transform parent, float spacing, RectOffset padding = null)
    {
        GameObject go = new GameObject("Column", typeof(RectTransform), typeof(VerticalLayoutGroup));
        go.transform.SetParent(parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        VerticalLayoutGroup v = go.GetComponent<VerticalLayoutGroup>();
        v.spacing = spacing;
        v.padding = padding ?? new RectOffset(
            (int)MenuStyle.PanelPadding, (int)MenuStyle.PanelPadding,
            (int)MenuStyle.PanelPadding, (int)MenuStyle.PanelPadding);
        v.childControlWidth = true;
        // childControlHeight MUST stay true: every element in this file sizes
        // itself with LayoutElement.preferredHeight, and a VerticalLayoutGroup
        // ignores LayoutElement entirely when it isn't controlling the axis —
        // it falls back to each child's raw sizeDelta, which for a
        // code-created RectTransform is nothing like the intended height.
        // That is what made every screen lay out at the wrong heights and
        // spill out of its panel.
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        v.childAlignment = TextAnchor.UpperCenter;
        return v;
    }

    /// <summary>
    /// Sizes a panel to exactly fit the column inside it.
    ///
    /// Panel heights used to be hand-typed constants, which drift the moment a
    /// row is added — the Controls screen needed ~780px and was declared at
    /// 640, so its last rows and the Back button fell outside the panel.
    /// Call this once at the end of building a screen instead.
    /// </summary>
    public static void FitPanelHeight(RectTransform panel, VerticalLayoutGroup col,
                                      float min = 120f, float max = 980f)
    {
        // The layout group only computes its preferred height during a layout
        // pass, so force one now rather than waiting a frame (the panel would
        // visibly resize on the frame after it opened).
        //
        // It has to be a full recursive rebuild, not just this group's two
        // CalculateLayoutInput calls (2026-09-03): a NESTED layout group — the
        // campfire panel's tab bodies — reports its preferred height from its
        // own CalculateLayoutInputVertical, which the outer call never runs, so
        // the outer group summed the bodies at zero. The panel came out ~130px
        // tall and the outer group squished every row inside the body to fit:
        // the "campfire panel is squished with no spacing" bug.
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)col.transform);

        float wanted = LayoutUtility.GetPreferredHeight((RectTransform)col.transform);
        panel.sizeDelta = new Vector2(panel.sizeDelta.x, Mathf.Clamp(wanted, min, max));
    }

    public static Button MenuButton(Transform parent, string text, Action onClick,
                                    bool enabled = true, Color? textColor = null)
    {
        GameObject go = new GameObject("Button", typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        Image img = go.GetComponent<Image>();
        img.color = enabled ? MenuStyle.ButtonFill : MenuStyle.ButtonDisabled;
        if (MenuStyle.ButtonSprite != null)
        {
            img.sprite = MenuStyle.ButtonSprite;
            img.type = Image.Type.Sliced;
        }

        go.GetComponent<LayoutElement>().preferredHeight = MenuStyle.ButtonHeight;

        Button btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.interactable = enabled;

        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        // Button colours are multiplied against the image tint, so these are
        // brightness factors rather than absolute colours.
        cb.highlightedColor = new Color(1.5f, 1.5f, 1.5f, 1f);
        cb.pressedColor = new Color(1.9f, 1.7f, 1.2f, 1f);
        cb.disabledColor = Color.white;
        cb.fadeDuration = 0.08f;
        btn.colors = cb;

        if (onClick != null) btn.onClick.AddListener(() => onClick());

        TextMeshProUGUI label = Label(go.transform, text, MenuStyle.ButtonSize,
            enabled ? (textColor ?? MenuStyle.TextPrimary) : MenuStyle.TextMuted);
        RectTransform lrt = label.rectTransform;
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;

        if (MenuStyle.ButtonSprite == null)
            AddBorder(go.GetComponent<RectTransform>(), MenuStyle.Divider, 1f);

        return btn;
    }

    /// <summary>Horizontal row: a left-aligned label and a right-aligned control slot.</summary>
    public static RectTransform SettingRow(Transform parent, string label, out RectTransform controlSlot,
                                           float height = -1f)
    {
        GameObject go = new GameObject("Row", typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = height > 0f ? height : MenuStyle.RowHeight;

        RectTransform rt = go.GetComponent<RectTransform>();

        TextMeshProUGUI text = Label(go.transform, label, MenuStyle.BodySize,
            MenuStyle.TextPrimary, TextAlignmentOptions.MidlineLeft);
        RectTransform trt = text.rectTransform;
        trt.anchorMin = new Vector2(0f, 0f);
        trt.anchorMax = new Vector2(0.45f, 1f);
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;

        GameObject slot = new GameObject("Control", typeof(RectTransform));
        slot.transform.SetParent(go.transform, false);
        controlSlot = slot.GetComponent<RectTransform>();
        controlSlot.anchorMin = new Vector2(0.48f, 0f);
        controlSlot.anchorMax = new Vector2(1f, 1f);
        controlSlot.offsetMin = Vector2.zero;
        controlSlot.offsetMax = Vector2.zero;

        return rt;
    }

    /// <summary>A 0..1 slider with a percentage readout — volumes and the like.</summary>
    public static Slider SliderRow(Transform parent, string label, float value, Action<float> onChange,
                                   string description = null)
    {
        return RangeSliderRow(parent, label, value, 0f, 1f, Percent, onChange, description);
    }

    /// <summary>The default slider readout.</summary>
    public static string Percent(float v) => Mathf.RoundToInt(v * 100f) + "%";

    /// <summary>
    /// A slider over an arbitrary range with its own readout formatter.
    ///
    /// Volumes are 0..1 percentages, but camera speed is a multiplier and a
    /// frame cap is a whole number of frames — rendering those as "62%" tells
    /// the player nothing about what they are actually setting.
    /// </summary>
    public static Slider RangeSliderRow(Transform parent, string label, float value,
                                        float min, float max, Func<float, string> format,
                                        Action<float> onChange, string description = null)
    {
        SettingRow(parent, label, out RectTransform slot);

        GameObject go = new GameObject("Slider", typeof(Slider));
        go.transform.SetParent(slot, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0.78f, 0.5f);
        rt.offsetMin = new Vector2(0f, -9f);
        rt.offsetMax = new Vector2(0f, 9f);

        // The background is the slider's targetGraphic AND its click surface —
        // it must raycast, or the EventSystem never delivers the slider a
        // pointer event and the control is silently inert. Same for the handle.
        RectTransform bg = SimpleImage(go.transform, "Background", MenuStyle.ButtonFill, raycast: true);
        Stretch(bg);

        RectTransform fill = SimpleImage(go.transform, "Fill", MenuStyle.TextAccent);
        Stretch(fill);   // Slider drives the anchors; the offsets must be zero or it overhangs

        // Handle lives in its own container inset by half a handle width, so
        // the knob stays inside the track at both ends.
        GameObject slideArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        slideArea.transform.SetParent(go.transform, false);
        RectTransform areaRt = slideArea.GetComponent<RectTransform>();
        Stretch(areaRt);
        areaRt.offsetMin = new Vector2(HandleWidth * 0.5f, 0f);
        areaRt.offsetMax = new Vector2(-HandleWidth * 0.5f, 0f);

        RectTransform handle = SimpleImage(slideArea.transform, "Handle", MenuStyle.TextPrimary, raycast: true);
        handle.sizeDelta = new Vector2(HandleWidth, 0f);

        Slider slider = go.GetComponent<Slider>();
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.SetValueWithoutNotify(Mathf.Clamp(value, min, max));

        if (format == null) format = Percent;
        TextMeshProUGUI readout = Label(slot, format(slider.value),
            MenuStyle.SmallSize, MenuStyle.TextMuted, TextAlignmentOptions.MidlineRight);
        RectTransform rrt = readout.rectTransform;
        rrt.anchorMin = new Vector2(0.80f, 0f);
        rrt.anchorMax = new Vector2(1f, 1f);
        rrt.offsetMin = Vector2.zero;
        rrt.offsetMax = Vector2.zero;

        slider.onValueChanged.AddListener(v =>
        {
            readout.text = format(v);
            onChange?.Invoke(v);
        });

        if (description != null) RowDescription(parent, description);
        return slider;
    }

    public static Toggle ToggleRow(Transform parent, string label, bool value, Action<bool> onChange,
                                   string description = null)
    {
        SettingRow(parent, label, out RectTransform slot);

        GameObject go = new GameObject("Toggle", typeof(Toggle));
        go.transform.SetParent(slot, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(28f, 28f);

        // Raycastable for the same reason the slider background is: the box is
        // the toggle's targetGraphic and its only click surface.
        RectTransform box = SimpleImage(go.transform, "Box", MenuStyle.ButtonFill, raycast: true);
        Stretch(box);
        AddBorder(box, MenuStyle.PanelBorder, 1f);

        RectTransform check = SimpleImage(go.transform, "Check", MenuStyle.TextAccent);
        check.anchorMin = new Vector2(0.22f, 0.22f);
        check.anchorMax = new Vector2(0.78f, 0.78f);
        check.offsetMin = Vector2.zero;
        check.offsetMax = Vector2.zero;
        check.SetAsLastSibling();   // must draw over the box, not under it

        Toggle toggle = go.GetComponent<Toggle>();
        toggle.targetGraphic = box.GetComponent<Image>();
        toggle.graphic = check.GetComponent<Image>();
        toggle.SetIsOnWithoutNotify(value);
        toggle.onValueChanged.AddListener(v => onChange?.Invoke(v));

        if (description != null) RowDescription(parent, description);
        return toggle;
    }

    /// <summary>Left/right stepper over a list of choices — avoids TMP_Dropdown's prefab requirements.</summary>
    public static void StepperRow(Transform parent, string label, string[] choices, int index,
                                  Action<int> onChange, string description = null)
    {
        SettingRow(parent, label, out RectTransform slot);

        int current = Mathf.Clamp(index, 0, Mathf.Max(0, choices.Length - 1));
        TextMeshProUGUI value = Label(slot, choices.Length > 0 ? choices[current] : "-",
            MenuStyle.BodySize, MenuStyle.TextAccent);
        RectTransform vrt = value.rectTransform;
        vrt.anchorMin = new Vector2(0.18f, 0f);
        vrt.anchorMax = new Vector2(0.82f, 1f);
        vrt.offsetMin = Vector2.zero;
        vrt.offsetMax = Vector2.zero;

        Action<int> step = dir =>
        {
            if (choices.Length == 0) return;
            current = (current + dir + choices.Length) % choices.Length;
            value.text = choices[current];
            onChange?.Invoke(current);
        };

        Arrow(slot, "<", new Vector2(0f, 0f), new Vector2(0.16f, 1f), () => step(-1));
        Arrow(slot, ">", new Vector2(0.84f, 0f), new Vector2(1f, 1f), () => step(1));

        if (description != null) RowDescription(parent, description);
    }

    /// <summary>
    /// The muted line under a setting explaining what it does.
    ///
    /// Sits in the column as its own element rather than inside the row, so a
    /// description can never squeeze the control it belongs to — the row keeps
    /// its full height and the column simply grows. Keep these to one line;
    /// two-line descriptions clip, because the height here is fixed (TMP cannot
    /// report a wrapped height until after a layout pass, and the panel is
    /// sized in the same frame it is built).
    /// </summary>
    public static TextMeshProUGUI RowDescription(Transform parent, string text)
    {
        TextMeshProUGUI t = Label(parent, text, MenuStyle.SmallSize, MenuStyle.TextMuted,
                                  TextAlignmentOptions.MidlineLeft);
        t.gameObject.name = "Description";
        t.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;
        return t;
    }

    /// <summary>A small capitalised heading that groups the rows beneath it.</summary>
    public static TextMeshProUGUI SectionHeader(Transform parent, string text)
    {
        Spacer(parent, 8f);
        TextMeshProUGUI t = Label(parent, text.ToUpperInvariant(), MenuStyle.SmallSize,
                                  MenuStyle.TextAccent, TextAlignmentOptions.MidlineLeft);
        t.gameObject.name = "SectionHeader";
        t.characterSpacing = 4f;
        t.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
        Divider(parent);
        return t;
    }

    /// <summary>A row whose right-hand side is a read-only value rather than a control.</summary>
    public static TextMeshProUGUI ValueRow(Transform parent, string label, string value,
                                           Color? color = null, string description = null)
    {
        SettingRow(parent, label, out RectTransform slot);
        TextMeshProUGUI t = Label(slot, value, MenuStyle.BodySize, color ?? MenuStyle.TextAccent,
                                  TextAlignmentOptions.MidlineLeft);
        Stretch(t.rectTransform);
        if (description != null) RowDescription(parent, description);
        return t;
    }

    /// <summary>
    /// A setting row whose control is two clickable key slots (primary and
    /// alternate). Used by the Controls screen; clicking a slot arms capture.
    /// </summary>
    public static void KeyBindRow(Transform parent, string label,
                                  string primaryText, string secondaryText,
                                  Action onPrimary, Action onSecondary,
                                  bool highlightPrimary = false, bool highlightSecondary = false,
                                  bool modified = false)
    {
        SettingRow(parent, modified ? label + " *" : label, out RectTransform slot, height: 40f);

        KeySlot(slot, primaryText, new Vector2(0f, 0f), new Vector2(0.47f, 1f), onPrimary, highlightPrimary);
        KeySlot(slot, secondaryText, new Vector2(0.53f, 0f), new Vector2(1f, 1f), onSecondary, highlightSecondary);
    }

    private static void KeySlot(Transform parent, string text, Vector2 aMin, Vector2 aMax,
                                Action onClick, bool armed)
    {
        GameObject go = new GameObject("KeySlot", typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        Image img = go.GetComponent<Image>();
        img.color = armed ? MenuStyle.ButtonPressed : MenuStyle.ButtonFill;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.offsetMin = new Vector2(0f, 5f);
        rt.offsetMax = new Vector2(0f, -5f);

        Button btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick?.Invoke());

        ColorBlock cb = btn.colors;
        cb.highlightedColor = new Color(1.5f, 1.5f, 1.5f, 1f);
        cb.fadeDuration = 0.08f;
        btn.colors = cb;

        TextMeshProUGUI t = Label(go.transform, text, MenuStyle.SmallSize,
                                  armed ? MenuStyle.TextAccent : MenuStyle.TextPrimary);
        Stretch(t.rectTransform);
        AddBorder(rt, MenuStyle.Divider, 1f);
    }

    /// <summary>
    /// A fixed-height scrolling region inside a panel, returning the column to
    /// fill. Screens that can outgrow the window (the keybinding list, a long
    /// options tab) use this instead of <see cref="FitPanelHeight"/>, which
    /// would otherwise size a panel taller than the screen.
    ///
    /// The content column anchors to the top and is driven by a
    /// ContentSizeFitter, so it grows downward as rows are added and the
    /// ScrollRect picks the new height up without a manual measure.
    /// </summary>
    public static VerticalLayoutGroup ScrollColumn(Transform parent, float spacing, float height,
                                                   float sidePadding = 4f)
    {
        GameObject viewportGo = new GameObject("Viewport", typeof(Image), typeof(RectMask2D), typeof(ScrollRect), typeof(LayoutElement));
        viewportGo.transform.SetParent(parent, false);
        viewportGo.GetComponent<LayoutElement>().preferredHeight = height;

        // RectMask2D, never a stencil Mask. A Mask with showMaskGraphic = false
        // draws its graphic through an alpha-clipped material, and a UI vertex
        // colour is quantised to a byte on the way into the mesh: an alpha of
        // 0.001 becomes 0, the clip then discards every mask pixel, the stencil
        // is never written, and every row inside the viewport is culled. That is
        // what left the Options tabs and the whole Controls list blank while the
        // headings and buttons around them drew fine. RectMask2D clips by
        // rectangle instead, so the graphic exists only for the raycast.
        //
        // The Image is still required: the scroll wheel and drags reach the
        // ScrollRect only through a raycastable graphic. Fully transparent is
        // fine here — uGUI raycasts ignore alpha unless
        // alphaHitTestMinimumThreshold is set, which it isn't.
        Image vpImage = viewportGo.GetComponent<Image>();
        vpImage.color = Color.clear;
        vpImage.raycastTarget = true;

        RectTransform viewport = viewportGo.GetComponent<RectTransform>();

        GameObject contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentGo.transform.SetParent(viewport, false);

        RectTransform content = contentGo.GetComponent<RectTransform>();
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = new Vector2(0f, 0f);
        content.offsetMax = new Vector2(0f, 0f);

        VerticalLayoutGroup v = contentGo.GetComponent<VerticalLayoutGroup>();
        v.spacing = spacing;
        // Right padding leaves the scrollbar its lane, so no row sits under it.
        v.padding = new RectOffset((int)sidePadding, (int)sidePadding + (int)ScrollbarWidth + 4, 0, 8);
        v.childControlWidth = true;
        v.childControlHeight = true;      // same reason as Column: LayoutElement is how rows size themselves
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        v.childAlignment = TextAnchor.UpperCenter;

        ContentSizeFitter fitter = contentGo.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        ScrollRect scroll = viewportGo.GetComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        // One wheel notch should move a row group, not a few pixels. 28 read as
        // "the list is stuck" on the crafting tab, where a row group is ~90px.
        scroll.scrollSensitivity = 60f;
        scroll.inertia = false;   // a settings list should stop where the player stops it

        // A slim scrollbar down the right edge. Without one there is nothing on
        // screen saying the list continues below the fold. Visibility is
        // Permanent rather than AutoHide because AutoHideAndExpandViewport
        // resizes the viewport, which fights the LayoutElement that sizes it.
        scroll.verticalScrollbar = BuildScrollbar(viewport);
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

        return v;
    }

    /// <summary>Width reserved for a <see cref="ScrollColumn"/>'s scrollbar.</summary>
    public const float ScrollbarWidth = 10f;

    /// <summary>The slim bar down a scroll viewport's right edge (handle included).</summary>
    static Scrollbar BuildScrollbar(RectTransform viewport)
    {
        GameObject go = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
        go.transform.SetParent(viewport, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.sizeDelta = new Vector2(ScrollbarWidth, 0f);
        rt.anchoredPosition = Vector2.zero;

        Image track = go.GetComponent<Image>();
        track.color = new Color(1f, 1f, 1f, 0.06f);
        track.raycastTarget = true;   // the track is the click surface for paging

        GameObject area = new GameObject("SlidingArea", typeof(RectTransform));
        area.transform.SetParent(go.transform, false);
        Stretch(area.GetComponent<RectTransform>());

        GameObject handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
        handleGo.transform.SetParent(area.transform, false);
        Image handle = handleGo.GetComponent<Image>();
        handle.color = Color.white;   // opaque white: the ColorBlock MULTIPLIES this
        handle.raycastTarget = true;
        Stretch(handleGo.GetComponent<RectTransform>());

        Scrollbar bar = go.GetComponent<Scrollbar>();
        bar.direction = Scrollbar.Direction.BottomToTop;
        bar.handleRect = handleGo.GetComponent<RectTransform>();
        bar.targetGraphic = handle;
        ColorBlock colors = bar.colors;
        colors.normalColor = new Color(0.85f, 0.72f, 0.45f, 0.45f);
        colors.highlightedColor = new Color(0.95f, 0.80f, 0.45f, 0.75f);
        colors.pressedColor = new Color(0.95f, 0.80f, 0.45f, 0.95f);
        bar.colors = colors;
        return bar;
    }

    private static void Arrow(Transform parent, string glyph, Vector2 aMin, Vector2 aMax, Action onClick)
    {
        GameObject go = new GameObject("Arrow", typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        Image img = go.GetComponent<Image>();
        img.color = MenuStyle.ButtonFill;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = aMin;
        rt.anchorMax = aMax;
        rt.offsetMin = new Vector2(0f, 6f);
        rt.offsetMax = new Vector2(0f, -6f);

        Button btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick?.Invoke());

        TextMeshProUGUI t = Label(go.transform, glyph, MenuStyle.BodySize, MenuStyle.TextPrimary);
        RectTransform trt = t.rectTransform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;
    }

    public static RectTransform Divider(Transform parent)
    {
        RectTransform rt = SimpleImage(parent, "Divider", MenuStyle.Divider);
        LayoutElement le = rt.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 1f;
        return rt;
    }

    public static RectTransform Spacer(Transform parent, float height)
    {
        GameObject go = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<LayoutElement>().preferredHeight = height;
        return go.GetComponent<RectTransform>();
    }

    /// <summary>
    /// A flat coloured rect. Defaults to <c>raycastTarget = false</c> because
    /// most of these are decoration sitting on top of a control — but anything
    /// that IS the control's click surface must pass <c>raycast: true</c>.
    /// </summary>
    public static RectTransform SimpleImage(Transform parent, string name, Color color, bool raycast = false)
    {
        GameObject go = new GameObject(name, typeof(Image));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = raycast;
        return go.GetComponent<RectTransform>();
    }

    /// <summary>
    /// A setting row whose control is a single-line text field. The field's
    /// background is the click surface, so it is the one raycastable graphic
    /// here (same rule as the slider background and toggle box).
    /// </summary>
    public static TMP_InputField InputRow(Transform parent, string label, string value, string placeholder,
                                          Action<string> onEndEdit, string description = null)
    {
        SettingRow(parent, label, out RectTransform slot);

        RectTransform bg = SimpleImage(slot, "Input", MenuStyle.ButtonFill, raycast: true);
        Stretch(bg);
        bg.offsetMin = new Vector2(0f, 6f);
        bg.offsetMax = new Vector2(0f, -6f);
        AddBorder(bg, MenuStyle.Divider, 1f);

        GameObject areaGo = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
        areaGo.transform.SetParent(bg, false);
        RectTransform area = Stretch(areaGo.GetComponent<RectTransform>());
        area.offsetMin = new Vector2(10f, 0f);
        area.offsetMax = new Vector2(-10f, 0f);

        TextMeshProUGUI text = Label(area, "", MenuStyle.BodySize, MenuStyle.TextAccent, TextAlignmentOptions.MidlineLeft);
        Stretch(text.rectTransform);
        text.textWrappingMode = TextWrappingModes.NoWrap;

        TextMeshProUGUI ph = Label(area, placeholder, MenuStyle.BodySize, MenuStyle.TextMuted, TextAlignmentOptions.MidlineLeft);
        Stretch(ph.rectTransform);
        ph.fontStyle = FontStyles.Italic;

        TMP_InputField field = bg.gameObject.AddComponent<TMP_InputField>();
        field.targetGraphic = bg.GetComponent<Image>();
        field.textViewport = area;
        field.textComponent = text;
        field.placeholder = ph;
        field.characterLimit = 24;
        field.text = value ?? "";
        if (onEndEdit != null) field.onEndEdit.AddListener(v => onEndEdit(v));

        if (description != null) RowDescription(parent, description);
        return field;
    }

    /// <summary>
    /// A row of equal-width tab buttons; the active one is drawn pressed.
    /// Returns the buttons so a caller that keeps the row alive (the campfire
    /// panel) can re-tint them on a switch instead of rebuilding.
    /// </summary>
    public static Button[] TabRow(Transform parent, string[] names, int active, Action<int> onPick)
    {
        GameObject row = new GameObject("Tabs", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        row.GetComponent<LayoutElement>().preferredHeight = 52f;

        HorizontalLayoutGroup h = row.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 6f;
        h.childControlWidth = true;
        h.childForceExpandWidth = true;
        h.childControlHeight = true;
        h.childForceExpandHeight = true;

        Button[] buttons = new Button[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            int idx = i;
            Button b = MenuButton(row.transform, names[i], () => onPick(idx));
            b.GetComponent<LayoutElement>().preferredHeight = 48f;
            buttons[i] = b;
        }
        TintTabs(buttons, active);
        return buttons;
    }

    /// <summary>Re-tint a <see cref="TabRow"/> for a new active index.</summary>
    public static void TintTabs(Button[] tabs, int active)
    {
        for (int i = 0; i < tabs.Length; i++)
        {
            if (tabs[i] == null) continue;
            tabs[i].targetGraphic.color = i == active ? MenuStyle.ButtonPressed : MenuStyle.ButtonFill;
        }
    }

    /// <summary>Fills the parent rect exactly. Code-created RectTransforms do not do this by default.</summary>
    public static RectTransform Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }
}
