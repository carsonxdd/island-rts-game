#if UNITY_EDITOR
using UnityEngine;

/// <summary>
/// IMGUI debug panel for visualizing Utility AI scores in the editor.
/// Toggle with F3. Click a unit to inspect its AIBrain scores.
/// Entirely #if UNITY_EDITOR wrapped — zero cost in builds.
/// </summary>
public class AIDebugOverlay : MonoBehaviour
{
    private static AIDebugOverlay instance;

    // Selected unit
    private AIBrain selectedBrain;
    private string selectedUnitName = "";
    private bool isVisible = false;

    // Cached GUI resources (created once)
    private GUIStyle headerStyle;
    private GUIStyle labelStyle;
    private GUIStyle smallLabelStyle;
    private GUIStyle activeActionStyle;
    private GUIStyle historyStyle;
    private Texture2D greenTex;
    private Texture2D grayTex;
    private Texture2D yellowTex;
    private Texture2D cyanTex;
    private bool stylesInitialized = false;

    // Panel dimensions
    private const float PanelWidth = 320f;
    private const float PanelPadding = 10f;
    private const float BarHeight = 16f;
    private const float BarMaxWidth = 200f;

    public static void EnsureExists()
    {
        if (instance != null) return;
        var go = new GameObject("[AIDebugOverlay]");
        instance = go.AddComponent<AIDebugOverlay>();
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        if (greenTex != null) Destroy(greenTex);
        if (grayTex != null) Destroy(grayTex);
        if (yellowTex != null) Destroy(yellowTex);
        if (cyanTex != null) Destroy(cyanTex);
    }

    void Update()
    {
        // Toggle overlay with F3
        if (Input.GetKeyDown(KeyCode.F3))
        {
            isVisible = !isVisible;
        }

        if (!isVisible) return;

        // Click to select a unit
        if (Input.GetMouseButtonDown(0))
        {
            // Don't steal clicks from the panel area
            float panelX = Screen.width - PanelWidth - PanelPadding;
            if (Input.mousePosition.x < panelX)
            {
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                RaycastHit hit;
                if (Physics.Raycast(ray, out hit, 200f))
                {
                    AIBrain brain = hit.collider.GetComponentInParent<AIBrain>();
                    if (brain != null)
                    {
                        selectedBrain = brain;
                        selectedUnitName = brain.gameObject.name;
                    }
                }
            }
        }

        // Clear selection if selected unit was destroyed
        if (selectedBrain == null)
        {
            selectedUnitName = "";
        }
    }

    void InitStyles()
    {
        if (stylesInitialized) return;
        stylesInitialized = true;

        greenTex = MakeTex(1, 1, new Color(0.2f, 0.8f, 0.2f, 0.9f));
        grayTex = MakeTex(1, 1, new Color(0.4f, 0.4f, 0.4f, 0.9f));
        yellowTex = MakeTex(1, 1, new Color(0.9f, 0.8f, 0.2f, 0.9f));
        cyanTex = MakeTex(1, 1, new Color(0.3f, 0.7f, 0.9f, 0.9f));

        headerStyle = new GUIStyle();
        headerStyle.fontSize = 14;
        headerStyle.fontStyle = FontStyle.Bold;
        headerStyle.normal.textColor = Color.white;
        headerStyle.alignment = TextAnchor.MiddleLeft;

        labelStyle = new GUIStyle();
        labelStyle.fontSize = 12;
        labelStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
        labelStyle.alignment = TextAnchor.MiddleLeft;

        smallLabelStyle = new GUIStyle();
        smallLabelStyle.fontSize = 10;
        smallLabelStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
        smallLabelStyle.alignment = TextAnchor.MiddleLeft;

        activeActionStyle = new GUIStyle();
        activeActionStyle.fontSize = 12;
        activeActionStyle.fontStyle = FontStyle.Bold;
        activeActionStyle.normal.textColor = new Color(0.3f, 1f, 0.3f);
        activeActionStyle.alignment = TextAnchor.MiddleLeft;

        historyStyle = new GUIStyle();
        historyStyle.fontSize = 10;
        historyStyle.normal.textColor = new Color(0.6f, 0.6f, 0.8f);
        historyStyle.alignment = TextAnchor.MiddleLeft;
    }

    Texture2D MakeTex(int width, int height, Color color)
    {
        var tex = new Texture2D(width, height);
        var pixels = new Color[width * height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    void OnGUI()
    {
        if (!isVisible || !Application.isPlaying) return;

        InitStyles();

        float panelX = Screen.width - PanelWidth - PanelPadding;
        float y = PanelPadding;

        // Background
        GUI.Box(new Rect(panelX - 5, y - 5, PanelWidth + 10, Screen.height - PanelPadding * 2 + 10), "");

        // Title
        GUI.Label(new Rect(panelX, y, PanelWidth, 20), "AI Debug (F3 to toggle)", headerStyle);
        y += 24;

        if (selectedBrain == null)
        {
            GUI.Label(new Rect(panelX, y, PanelWidth, 20), "Click a unit to inspect", labelStyle);
            return;
        }

        // --- Header: unit name + current action ---
        GUI.Label(new Rect(panelX, y, PanelWidth, 20), selectedUnitName, headerStyle);
        y += 20;

        string currentAction = selectedBrain.GetCurrentActionName();
        GUI.Label(new Rect(panelX, y, PanelWidth, 18), "Action: " + currentAction, activeActionStyle);
        y += 22;

        // Divider
        GUI.Box(new Rect(panelX, y, PanelWidth - 10, 1), "");
        y += 6;

        // --- Action scores bar chart ---
        GUI.Label(new Rect(panelX, y, PanelWidth, 18), "Action Scores:", labelStyle);
        y += 20;

        int activeIdx = selectedBrain.debugCurrentActionIndex;

        for (int i = 0; i < selectedBrain.actionCount; i++)
        {
            bool isActive = (i == activeIdx);
            string name = selectedBrain.actionNames[i];
            float score = selectedBrain.lastActionScores[i];

            // Action name + score
            GUIStyle nameStyle = isActive ? activeActionStyle : labelStyle;
            string prefix = isActive ? "> " : "  ";
            GUI.Label(new Rect(panelX, y, PanelWidth, 16), prefix + name + "  " + score.ToString("F3"), nameStyle);
            y += 16;

            // Score bar
            float barWidth = Mathf.Max(1f, score * BarMaxWidth);
            Texture2D barTex = isActive ? greenTex : grayTex;
            GUI.DrawTexture(new Rect(panelX + 16, y, barWidth, BarHeight - 4), barTex);

            // Bar background (full width outline)
            GUI.Box(new Rect(panelX + 16, y, BarMaxWidth, BarHeight - 4), "");
            y += BarHeight;

            // Consideration breakdown (indented, only for active action)
            if (isActive && selectedBrain.considerationCounts[i] > 0)
            {
                for (int c = 0; c < selectedBrain.considerationCounts[i]; c++)
                {
                    string cName = selectedBrain.considerationNames[i][c];
                    float cScore = selectedBrain.lastConsiderationScores[i][c];

                    GUI.Label(new Rect(panelX + 24, y, PanelWidth - 24, 14),
                        cName + ": " + cScore.ToString("F3"), smallLabelStyle);
                    y += 14;

                    // Small consideration bar
                    float cBarWidth = Mathf.Max(1f, cScore * (BarMaxWidth - 30));
                    GUI.DrawTexture(new Rect(panelX + 32, y, cBarWidth, 6), cyanTex);
                    y += 8;
                }
            }

            y += 2;
        }

        // Divider
        y += 4;
        GUI.Box(new Rect(panelX, y, PanelWidth - 10, 1), "");
        y += 6;

        // --- Action history ---
        GUI.Label(new Rect(panelX, y, PanelWidth, 18), "Recent Actions:", labelStyle);
        y += 20;

        if (selectedBrain.historyCount > 0)
        {
            float now = Time.time;
            for (int i = 0; i < selectedBrain.historyCount; i++)
            {
                // Read from most recent to oldest
                int idx = (selectedBrain.historyHead - 1 - i + 10) % 10;
                if (idx < 0) idx += 10;

                var entry = selectedBrain.actionHistory[idx];
                float ago = now - entry.timestamp;

                // Only show entries from last 30 seconds
                if (ago > 30f) continue;

                string timeStr = ago < 1f ? "<1s" : ago.ToString("F0") + "s";
                GUI.Label(new Rect(panelX + 8, y, PanelWidth - 8, 14),
                    timeStr + " ago: " + entry.actionName + " (" + entry.score.ToString("F2") + ")",
                    historyStyle);
                y += 14;
            }
        }
        else
        {
            GUI.Label(new Rect(panelX + 8, y, PanelWidth - 8, 14), "(no switches yet)", historyStyle);
        }
    }
}
#endif
