using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Renders a gallery of island seeds to PNG without entering Play mode, so
/// generator tuning is "edit the IslandSettings asset, run this, look at
/// twelve islands" instead of pressing Play twelve times.
///
/// Colours follow TerrainGrid.Classify (wet sand / sand / three grass tones /
/// rock / cliff) with height shading; deep water is blue, the wading band
/// light blue; land the validator could NOT reach from the campfire is
/// magenta; the campfire site and cove are red dots. The log line per seed
/// is the same report TerrainGrid prints at load.
///
/// Output: &lt;project&gt;/TerrainPreviews/seed_N.png (gitignored).
/// </summary>
public static class TerrainPreview
{
    private const int SeedCount = 12;
    private const int PixelsPerCell = 3;

    [MenuItem("Tools/Island RTS/Terrain/Preview Island Seeds (PNG gallery)", false, 20)]
    public static void RenderGallery()
    {
        IslandSettings settings = AssetDatabase.LoadAssetAtPath<IslandSettings>(TerrainSetup.IslandSettingsPath);
        string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "TerrainPreviews");
        Directory.CreateDirectory(dir);

        StringBuilder log = new StringBuilder();
        log.AppendLine("[Terrain] Preview gallery → " + dir + (settings != null ? " (IslandSettings.asset)" : " (code defaults — no asset yet)"));

        int baseSeed = (int)(System.DateTime.Now.Ticks & 0x7FFFFFF);
        for (int i = 0; i < SeedCount; i++)
        {
            EditorUtility.DisplayProgressBar("Island previews", "Seed " + (i + 1) + " / " + SeedCount, i / (float)SeedCount);
            int seed = baseSeed + i * 7919;
            IslandField field = IslandGenerator.Generate(TerrainGrid.VertsPerSide, TerrainGrid.Spacing, seed, settings);
            string path = Path.Combine(dir, "seed_" + (i + 1) + ".png");
            File.WriteAllBytes(path, Render(field, settings).EncodeToPNG());
            log.AppendLine("  " + Path.GetFileName(path) + ": " + field.Report);
        }
        EditorUtility.ClearProgressBar();
        Debug.Log(log.ToString());
        EditorUtility.RevealInFinder(Path.Combine(dir, "seed_1.png"));
    }

    static Texture2D Render(IslandField f, IslandSettings settingsOrNull)
    {
        IslandSettings s = IslandSettings.Resolve(settingsOrNull);
        int n = f.verts;
        int size = n * PixelsPerCell;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGB24, false);
        Color32[] px = new Color32[size * size];

        for (int py = 0; py < size; py++)
        {
            int z = py / PixelsPerCell;
            for (int pxx = 0; pxx < size; pxx++)
            {
                int x = pxx / PixelsPerCell;
                px[py * size + pxx] = CellColor(f, s, x, z);
            }
        }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    static Color32 CellColor(IslandField f, IslandSettings s, int x, int z)
    {
        int n = f.verts;
        float h = f.heights[x, z];
        float dx = x > 0 && x < n - 1 ? (f.heights[x + 1, z] - f.heights[x - 1, z]) * 0.5f : 0f;
        float dz = z > 0 && z < n - 1 ? (f.heights[x, z + 1] - f.heights[x, z - 1]) * 0.5f : 0f;
        float slope = Mathf.Sqrt(dx * dx + dz * dz);
        float ny = 1f / Mathf.Sqrt(1f + slope * slope);

        Color c;
        if (h <= TerrainGrid.DeepWaterY)
        {
            float d = Mathf.Clamp01(-h / 2.5f);
            c = new Color(0.24f - 0.12f * d, 0.47f - 0.2f * d, 0.78f - 0.23f * d);
        }
        else if (h <= 0f) c = new Color(0.35f, 0.67f, 0.82f);
        else if (slope > 1.0f) c = Hex("#616A70");
        else if (slope > 0.62f && h > 0.1f) c = Hex("#838D93");
        else if (h < 0.2f) c = Hex("#C9B489");
        else if (h < 0.65f) c = Hex("#E4D2A2");
        else
        {
            // Same rule as TerrainGrid.ToneAt: noise blended with height
            float tone = Mathf.Lerp(f.tone[x, z], Mathf.Clamp01(h / (s.baseHeight + s.hillAmplitude)), s.toneHeightWeight);
            c = tone < 0.36f ? Hex("#5E9E44") : tone > 0.66f ? Hex("#B9C25E") : Hex("#86C25A");
            c *= Mathf.Clamp(0.45f + h * 0.11f, 0.45f, 1.2f);
        }

        if (h > 0.15f && !f.reachable[x, z]) c = Color.magenta;

        float wx = x * f.spacing - (n - 1) * f.spacing * 0.5f;
        float wz = z * f.spacing - (n - 1) * f.spacing * 0.5f;
        if ((wx - f.campfireSite.x) * (wx - f.campfireSite.x) + (wz - f.campfireSite.y) * (wz - f.campfireSite.y) < 2.5f
            || (wx - f.coveCenter.x) * (wx - f.coveCenter.x) + (wz - f.coveCenter.y) * (wz - f.coveCenter.y) < 2.5f)
            c = Color.red;

        c.a = 1f;
        return c;
    }

    static Color Hex(string hex)
    {
        Color c;
        ColorUtility.TryParseHtmlString(hex, out c);
        return c;
    }
}
