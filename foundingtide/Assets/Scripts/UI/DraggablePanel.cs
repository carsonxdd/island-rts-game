using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Lets a panel be dragged around the screen by one of its child rects (a
/// title bar), clamped so it can never leave the canvas, with the position
/// remembered in PlayerPrefs. Attach to the HANDLE, not the panel, and give
/// the handle a raycastable graphic — an EventSystem only delivers drag
/// events to something it can hit (the same rule as every other code-built
/// control: an invisible surface that IS the interaction surface still has
/// to raycast). Children of the handle (a close button) still win the
/// raycast, so they keep working.
///
/// The panel must be anchored and pivoted at its parent's bottom-left so the
/// anchored position is the panel's bottom-left corner in canvas units, which
/// is what the clamp and the saved value assume.
/// </summary>
public class DraggablePanel : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private RectTransform panel;
    private RectTransform canvasRect;
    private Canvas canvas;
    private string prefsKey;
    private Vector2 dragStartPanel;
    private Vector2 dragStartPointer;

    /// <summary>
    /// Makes <paramref name="handle"/> drag <paramref name="panel"/>. The handle
    /// gets a transparent raycast surface behind its existing children. The
    /// panel is snapped to its saved position (or <paramref name="defaultPos"/>).
    /// </summary>
    public static DraggablePanel Attach(RectTransform handle, RectTransform panel, string prefsKey, Vector2 defaultPos)
    {
        RectTransform surface = MenuBuilder.SimpleImage(handle, "DragSurface", new Color(1f, 1f, 1f, 0.004f), raycast: true);
        MenuBuilder.Stretch(surface);
        surface.SetAsFirstSibling();

        DraggablePanel d = handle.gameObject.AddComponent<DraggablePanel>();
        d.panel = panel;
        d.prefsKey = prefsKey;
        d.canvas = panel.GetComponentInParent<Canvas>();
        d.canvasRect = d.canvas != null ? (RectTransform)d.canvas.transform : (RectTransform)panel.parent;

        Vector2 pos = new Vector2(
            PlayerPrefs.GetFloat(prefsKey + ".x", defaultPos.x),
            PlayerPrefs.GetFloat(prefsKey + ".y", defaultPos.y));
        panel.anchoredPosition = pos;
        return d;
    }

    /// <summary>Re-clamp after the panel's size is known (call once it has been laid out).</summary>
    public void Clamp()
    {
        panel.anchoredPosition = Clamped(panel.anchoredPosition);
    }

    Vector2 Clamped(Vector2 pos)
    {
        Vector2 canvasSize = canvasRect.rect.size;
        if (canvasSize.x <= 0f || canvasSize.y <= 0f) return pos;   // canvas not laid out yet (build frame)
        Vector2 panelSize = panel.rect.size;
        pos.x = Mathf.Clamp(pos.x, 0f, Mathf.Max(0f, canvasSize.x - panelSize.x));
        pos.y = Mathf.Clamp(pos.y, 0f, Mathf.Max(0f, canvasSize.y - panelSize.y));
        return pos;
    }

    bool PointerInCanvas(PointerEventData e, out Vector2 local)
    {
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, e.position, cam, out local);
    }

    public void OnBeginDrag(PointerEventData e)
    {
        if (!PointerInCanvas(e, out dragStartPointer)) return;
        dragStartPanel = panel.anchoredPosition;
    }

    public void OnDrag(PointerEventData e)
    {
        if (!PointerInCanvas(e, out Vector2 local)) return;
        // Both points are in canvas units, so UI Scale and resolution cancel out
        panel.anchoredPosition = Clamped(dragStartPanel + (local - dragStartPointer));
    }

    public void OnEndDrag(PointerEventData e)
    {
        Vector2 pos = panel.anchoredPosition;
        PlayerPrefs.SetFloat(prefsKey + ".x", pos.x);
        PlayerPrefs.SetFloat(prefsKey + ".y", pos.y);
    }
}
