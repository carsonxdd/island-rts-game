using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// One shared tooltip for every code-built panel (2026-09-17). A row asks for
/// one with <see cref="Attach"/>; the text shows in a small box beside the
/// pointer after a short hover and hides on exit, on click, or when the row
/// goes inactive under the pointer (a panel closing sends no pointer-exit).
/// This is where the campfire panel's help lines went: the muted line under
/// every control cost 20 px each and was read once.
/// </summary>
/// <remarks>
/// The host canvas sorts above every panel (95) and NOTHING on it raycasts, so
/// a tooltip can never swallow a click or block the camera's edge pan. The
/// surface it attaches to is a transparent raycastable image behind the row's
/// children — the same rule as every other code-built click surface — placed
/// first so buttons on the row still win the raycast, and carrying a hover-only
/// <see cref="TooltipSurface"/> so a surface over a BUTTON (a palette tile, the
/// swap buttons) lets the click through. Never built under the sim.
/// </remarks>
public class Tooltip : MonoBehaviour
{
    const float ShowDelay = 0.35f;
    const float MaxWidth = 340f;
    const float Pad = 10f;
    const float Offset = 18f;
    const int SortOrder = 95;

    private static Tooltip instance;

    private RectTransform canvasRect;
    private RectTransform box;
    private TextMeshProUGUI label;
    private RectTransform over;        // the surface under the pointer, if any
    private string pending;
    private float showAt;
    private bool shown;
    private bool signalled;

    /// <summary>
    /// Gives <paramref name="target"/> a hover surface that shows <paramref name="text"/>.
    /// A null or empty text attaches nothing.
    /// </summary>
    public static void Attach(RectTransform target, string text)
    {
        if (target == null || string.IsNullOrEmpty(text) || SimHooks.Headless) return;
        Ensure();

        RectTransform surface = MenuBuilder.SimpleImage(target, "TooltipSurface", new Color(1f, 1f, 1f, 0.004f), raycast: true);
        MenuBuilder.Stretch(surface);
        surface.SetAsFirstSibling();

        // NOT an EventTrigger (2026-09-18): it implements every pointer interface,
        // including IPointerClickHandler, so a surface over a BUTTON swallowed the
        // button's own click. This handler answers hover only, so a click walks
        // straight past it to whatever is underneath.
        surface.gameObject.AddComponent<TooltipSurface>().Bind(surface, text);
    }

    /// <summary>Hover entered a surface — <see cref="TooltipSurface"/> only.</summary>
    internal static void Show(RectTransform surface, string text)
    {
        if (instance != null) instance.Enter(surface, text);
    }

    /// <summary>Hover left a surface — <see cref="TooltipSurface"/> only.</summary>
    internal static void Hide(RectTransform surface)
    {
        if (instance != null) instance.Exit(surface);
    }

    /// <summary>Hide whatever is showing (a panel closing, a tab switching).</summary>
    public static void HideNow()
    {
        if (instance != null) instance.Exit(instance.over);
    }

    static void Ensure()
    {
        if (instance != null) return;
        GameObject go = new GameObject("[Tooltip]");
        instance = go.AddComponent<Tooltip>();
        instance.Build();
    }

    void OnDestroy() { if (instance == this) instance = null; }

    void Build()
    {
        Canvas canvas = MenuBuilder.CreateCanvas("TooltipCanvas", SortOrder);
        canvas.transform.SetParent(transform, false);
        canvasRect = (RectTransform)canvas.transform;
        // The canvas must not take clicks: its raycaster is what would make it eligible
        GraphicRaycaster ray = canvas.GetComponent<GraphicRaycaster>();
        if (ray != null) ray.enabled = false;

        GameObject boxGo = new GameObject("Box", typeof(RectTransform), typeof(Image), typeof(ContentSizeFitter), typeof(VerticalLayoutGroup));
        boxGo.transform.SetParent(canvas.transform, false);
        box = boxGo.GetComponent<RectTransform>();
        box.anchorMin = box.anchorMax = Vector2.zero;
        box.pivot = new Vector2(0f, 0f);

        Image bg = boxGo.GetComponent<Image>();
        bg.color = new Color(MenuStyle.PanelFill.r, MenuStyle.PanelFill.g, MenuStyle.PanelFill.b, 0.97f);
        bg.raycastTarget = false;
        MenuBuilder.AddBorder(box, MenuStyle.PanelBorder, 1f);

        VerticalLayoutGroup col = boxGo.GetComponent<VerticalLayoutGroup>();
        col.padding = new RectOffset((int)Pad, (int)Pad, (int)(Pad * 0.7f), (int)(Pad * 0.7f));
        col.childControlWidth = true;
        col.childControlHeight = true;
        col.childForceExpandWidth = false;
        col.childForceExpandHeight = false;

        ContentSizeFitter fit = boxGo.GetComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        label = MenuBuilder.Label(boxGo.transform, "", MenuStyle.SmallSize, MenuStyle.TextPrimary, TextAlignmentOptions.TopLeft);
        label.raycastTarget = false;
        LayoutElement le = label.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = MaxWidth;   // wraps at this width; a short line stays short through the fitter below
        label.textWrappingMode = TextWrappingModes.Normal;

        box.gameObject.SetActive(false);
    }

    void Enter(RectTransform surface, string text)
    {
        over = surface;
        pending = text;
        showAt = Time.unscaledTime + ShowDelay;
    }

    void Exit(RectTransform surface)
    {
        if (surface != over && surface != null) return;
        over = null;
        pending = null;
        if (shown)
        {
            shown = false;
            box.gameObject.SetActive(false);
        }
    }

    void LateUpdate()
    {
        if (over == null) return;
        // The row went away under the pointer (its panel closed): no exit event comes
        if (!over.gameObject.activeInHierarchy) { Exit(over); return; }
        // Hide on click, POLLED rather than handled: a pointer-down handler on the
        // surface would take the press away from the button underneath it
        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) { Exit(over); return; }

        if (!shown)
        {
            if (Time.unscaledTime < showAt) return;
            shown = true;
            label.text = pending;
            // A short text must not pad out to MaxWidth: size the label to its own line
            float wanted = Mathf.Min(MaxWidth, label.GetPreferredValues(pending, MaxWidth, 0f).x + 2f);
            label.GetComponent<LayoutElement>().preferredWidth = wanted;
            box.gameObject.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(box);
            if (!signalled) { signalled = true; DevQuests.Signal("tooltip"); }
        }

        // Follow the pointer, kept inside the canvas
        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, Input.mousePosition, null, out local)) return;
        Vector2 size = canvasRect.rect.size;
        Vector2 pos = local + size * 0.5f + new Vector2(Offset, Offset);
        Vector2 boxSize = box.rect.size;
        if (pos.x + boxSize.x > size.x) pos.x = local.x + size.x * 0.5f - Offset - boxSize.x;
        if (pos.y + boxSize.y > size.y) pos.y = local.y + size.y * 0.5f - Offset - boxSize.y;
        pos.x = Mathf.Max(0f, pos.x);
        pos.y = Mathf.Max(0f, pos.y);
        box.anchoredPosition = pos;
    }
}
