/// <summary>
/// Whether a HUD surface is under the mouse and should swallow this click (2026-09-18).
/// </summary>
/// <remarks>
/// uGUI stops nothing: a ground raycast, an <c>OnMouseDown</c> and the camera's edge pan all
/// fire straight through a HUD box. Every click surface that must eat gameplay clicks
/// therefore publishes a static rect test against <c>Input.mousePosition</c> - never
/// <c>EventSystem.IsPointerOverGameObject</c>, which the HUD makes true almost everywhere -
/// and every gameplay click site consults it.
///
/// Until 2026-09-18 that meant each site reading <see cref="Minimap.PointerOver"/> by name,
/// which was fine with one surface and a trap with three: adding the build palette would
/// have meant a second condition at eleven call sites, and missing one is a building placed
/// underneath the bar you clicked. The sites read this instead, so a new surface is one line
/// here rather than a sweep.
/// </remarks>
public static class PointerBlock
{
    /// <summary>True while any HUD surface owns the pixel the mouse is on.</summary>
    public static bool OverHud =>
        Minimap.PointerOver
        || BuildPaletteHUD.PointerOver
        || SelectedBuildingHUD.PointerOver;
}
