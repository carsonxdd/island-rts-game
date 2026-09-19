using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// The hover half of a <see cref="Tooltip"/> surface (2026-09-18). Deliberately
/// implements ONLY enter and exit: uGUI's <c>ExecuteHierarchy</c> stops at the
/// first ancestor that can handle an event, so anything implementing
/// <c>IPointerClickHandler</c> here — <c>EventTrigger</c> implements every
/// pointer interface — eats the click of the button this surface sits on. Enter
/// and exit are exempt: the event system walks the whole hovered chain instead
/// of stopping at one handler.
/// </summary>
public class TooltipSurface : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private RectTransform surface;
    private string text;

    public void Bind(RectTransform rt, string tip) { surface = rt; text = tip; }

    public void OnPointerEnter(PointerEventData e) { Tooltip.Show(surface, text); }
    public void OnPointerExit(PointerEventData e) { Tooltip.Hide(surface); }
}
