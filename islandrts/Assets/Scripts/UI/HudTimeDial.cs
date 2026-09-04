using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The sky dial in the resource bar (2026-09-03): a half-circle horizon with the sun
/// travelling left to right across the day and the moon doing the same across the night.
///
/// It replaced the editor-only debug label that read "Day 1 - DAY (Time: 0.30)" over the
/// top of the screen. That number meant nothing to a player, and the one thing they
/// actually need from a clock in this game - how much of the day is left before the
/// raiders land - was the one thing it did not show. An arc shows it at a glance without
/// anyone reading a digit.
/// </summary>
/// <remarks>
/// Drawn entirely from code, like the rest of the HUD: the arc is a row of small dot
/// Images placed once, the disc is one more, and every frame writes only the disc's
/// position and colour. The dot sprite is a texture generated once per session and
/// shared by every dial, so this adds one 32x32 texture to the game.
///
/// The clock's phases are not equal halves of the parameter's rate - day and night each
/// cover half of the 0..1 parameter at their own speed (see <see cref="DayNightCycle"/>) -
/// so a single marker sweeping 0..1 would drift against what the sky is doing. The dial
/// maps each phase to its own sweep instead.
/// </remarks>
public class HudTimeDial : MonoBehaviour
{
    const int ArcDots = 15;
    const float ArcRadius = 26f;
    const float DotSize = 3.5f;
    const float DiscSize = 15f;

    static readonly Color SunColor = new Color(1f, 0.82f, 0.38f);
    static readonly Color MoonColor = new Color(0.80f, 0.86f, 1f);
    static readonly Color ArcDay = new Color(0.95f, 0.80f, 0.45f, 0.34f);
    static readonly Color ArcNight = new Color(0.55f, 0.70f, 0.95f, 0.30f);

    static Sprite dotSprite;

    private RectTransform disc;
    private Image discImage;
    private Image[] arcDots;
    private Image horizon;
    private DayNightCycle dayNight;
    private Vector2 centre;

    /// <summary>
    /// Build a dial filling <paramref name="parent"/>. The caller owns the sizing; the
    /// dial just draws inside whatever rect it is given.
    /// </summary>
    public static HudTimeDial Build(RectTransform parent, float width, float height)
    {
        GameObject go = new GameObject("TimeDial", typeof(RectTransform), typeof(HudTimeDial));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        HudTimeDial dial = go.GetComponent<HudTimeDial>();
        dial.Construct(width, height);
        return dial;
    }

    void Construct(float width, float height)
    {
        // Horizon sits low in the entry so the arc has room above it
        centre = new Vector2(0f, -height * 0.5f + 12f);

        horizon = Dot("Horizon", new Color(0.85f, 0.72f, 0.45f, 0.35f));
        horizon.rectTransform.anchoredPosition = centre;
        horizon.rectTransform.sizeDelta = new Vector2(ArcRadius * 2f + 8f, 1.5f);
        horizon.sprite = null;

        arcDots = new Image[ArcDots];
        for (int i = 0; i < ArcDots; i++)
        {
            arcDots[i] = Dot("Arc" + i, ArcDay);
            float a = Mathf.PI * (1f - i / (float)(ArcDots - 1));
            arcDots[i].rectTransform.anchoredPosition =
                centre + new Vector2(Mathf.Cos(a) * ArcRadius, Mathf.Sin(a) * ArcRadius);
            arcDots[i].rectTransform.sizeDelta = new Vector2(DotSize, DotSize);
        }

        discImage = Dot("Disc", SunColor);
        disc = discImage.rectTransform;
        disc.sizeDelta = new Vector2(DiscSize, DiscSize);
    }

    Image Dot(string name, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(transform, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);

        Image img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        img.sprite = Circle();
        return img;
    }

    void Update()
    {
        if (dayNight == null)
        {
            dayNight = FindAnyObjectByType<DayNightCycle>();
            if (dayNight == null) return;
        }

        // 0 = midnight, 0.25 = dawn, 0.5 = noon, 0.75 = dusk
        float t = Mathf.Repeat(dayNight.currentTimeOfDay, 1f);
        bool night = t < 0.25f || t >= 0.75f;
        float progress = night
            ? Mathf.Repeat(t + 0.25f, 1f) / 0.5f   // dusk -> dawn
            : (t - 0.25f) / 0.5f;                  // dawn -> dusk
        progress = Mathf.Clamp01(progress);

        float angle = Mathf.PI * (1f - progress);
        disc.anchoredPosition = centre + new Vector2(Mathf.Cos(angle) * ArcRadius, Mathf.Sin(angle) * ArcRadius);

        Color body = night ? MoonColor : SunColor;
        if (discImage.color != body) discImage.color = body;

        Color arc = night ? ArcNight : ArcDay;
        if (arcDots[0].color != arc)
        {
            for (int i = 0; i < arcDots.Length; i++) arcDots[i].color = arc;
        }
    }

    /// <summary>
    /// A soft filled circle, generated once and shared. uGUI has no built-in round
    /// sprite, and shipping a texture asset for four dozen 4px dots is not worth an
    /// import step.
    /// </summary>
    static Sprite Circle()
    {
        if (dotSprite != null) return dotSprite;

        const int size = 32;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        float r = size * 0.5f - 0.5f;
        Color32[] px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - r, dy = y - r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                // One pixel of feather so the small dots do not look like squares
                float a = Mathf.Clamp01(r - d);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();

        dotSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        return dotSprite;
    }
}
