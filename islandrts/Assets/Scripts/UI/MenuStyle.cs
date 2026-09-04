using UnityEngine;

/// <summary>
/// The single place the menu look is defined.
///
/// Everything the menus draw — panel fills, borders, button states, text sizes,
/// spacing — comes from here, so restyling the whole game's menus is editing
/// this one file rather than hunting through five screens. That is the point:
/// the current look is deliberately a plain wireframe (flat fills, 2px borders,
/// no art), and an artist replaces it by changing these values and dropping
/// sprites into <see cref="PanelSprite"/> / <see cref="ButtonSprite"/>.
///
/// See docs/MENU_WIREFRAMES.md for the screen layouts these produce.
/// </summary>
public static class MenuStyle
{
    // ---- palette ---------------------------------------------------------
    // Warm dark neutrals, picked to sit under the game's sunset palette
    // without competing with it. Alpha < 1 on backdrops so the world reads
    // through a paused screen.

    public static readonly Color Backdrop = new Color(0.04f, 0.05f, 0.08f, 0.72f);
    public static readonly Color PanelFill = new Color(0.10f, 0.11f, 0.14f, 0.96f);
    public static readonly Color PanelBorder = new Color(0.85f, 0.72f, 0.45f, 1f);   // warm gold
    public static readonly Color Divider = new Color(0.85f, 0.72f, 0.45f, 0.25f);

    public static readonly Color TextPrimary = new Color(0.95f, 0.93f, 0.88f, 1f);
    public static readonly Color TextMuted = new Color(0.62f, 0.60f, 0.57f, 1f);
    public static readonly Color TextAccent = new Color(0.95f, 0.80f, 0.45f, 1f);
    public static readonly Color TextDanger = new Color(0.92f, 0.45f, 0.38f, 1f);

    public static readonly Color ButtonFill = new Color(0.16f, 0.17f, 0.21f, 1f);
    public static readonly Color ButtonHover = new Color(0.24f, 0.25f, 0.30f, 1f);
    public static readonly Color ButtonPressed = new Color(0.30f, 0.26f, 0.18f, 1f);
    public static readonly Color ButtonDisabled = new Color(0.13f, 0.13f, 0.15f, 1f);

    // ---- typography ------------------------------------------------------

    public const float TitleSize = 54f;
    public const float HeadingSize = 26f;
    public const float BodySize = 19f;
    public const float ButtonSize = 22f;
    public const float SmallSize = 15f;

    // ---- metrics ---------------------------------------------------------

    public const float BorderWidth = 2f;
    public const float PanelPadding = 28f;
    public const float ButtonHeight = 52f;
    public const float ButtonSpacing = 10f;
    public const float RowHeight = 48f;
    public const float MenuWidth = 460f;
    public const float OptionsWidth = 720f;

    /// <summary>
    /// Art hooks. Left null the menus draw flat wireframe boxes; assign a
    /// 9-sliced sprite and every panel/button picks it up with no code change.
    /// </summary>
    public static Sprite PanelSprite;
    public static Sprite ButtonSprite;
}
