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
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

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
        // pass, so drive the two input steps directly rather than waiting a
        // frame (the panel would visibly resize on the frame after it opened).
        col.CalculateLayoutInputHorizontal();
        col.CalculateLayoutInputVertical();

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

    public static Slider SliderRow(Transform parent, string label, float value, Action<float> onChange)
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
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.SetValueWithoutNotify(value);

        TextMeshProUGUI readout = Label(slot, Mathf.RoundToInt(value * 100f) + "%",
            MenuStyle.SmallSize, MenuStyle.TextMuted, TextAlignmentOptions.MidlineRight);
        RectTransform rrt = readout.rectTransform;
        rrt.anchorMin = new Vector2(0.80f, 0f);
        rrt.anchorMax = new Vector2(1f, 1f);
        rrt.offsetMin = Vector2.zero;
        rrt.offsetMax = Vector2.zero;

        slider.onValueChanged.AddListener(v =>
        {
            readout.text = Mathf.RoundToInt(v * 100f) + "%";
            onChange?.Invoke(v);
        });
        return slider;
    }

    public static Toggle ToggleRow(Transform parent, string label, bool value, Action<bool> onChange)
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
        return toggle;
    }

    /// <summary>Left/right stepper over a list of choices — avoids TMP_Dropdown's prefab requirements.</summary>
    public static void StepperRow(Transform parent, string label, string[] choices, int index, Action<int> onChange)
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
