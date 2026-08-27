using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Workshop crafting panel — built entirely at runtime (no scene wiring, same
/// pattern as GameStartController's hint overlay). One panel serves whichever
/// workshop was clicked: a dark side panel with one row per recipe (title,
/// effect, cost, Craft button) and a live progress line while a craft runs.
/// Esc or the X button closes it.
/// </summary>
public class CraftingUI : MonoBehaviour
{
    private static CraftingUI instance;

    public static Workshop CurrentWorkshop => instance != null ? instance.workshop : null;

    private Workshop workshop;
    private GameObject panel;
    private TextMeshProUGUI statusText;

    private readonly List<Button> craftButtons = new List<Button>();
    private readonly List<TextMeshProUGUI> buttonLabels = new List<TextMeshProUGUI>();

    private string lastStatus;

    public static void Open(Workshop shop)
    {
        if (shop == null) return;
        if (instance == null)
        {
            GameObject go = new GameObject("[CraftingUI]");
            instance = go.AddComponent<CraftingUI>();
            instance.BuildUI();
        }
        instance.workshop = shop;
        instance.panel.SetActive(true);
        instance.RefreshAll();

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayButtonClick();
    }

    public static void Close()
    {
        if (instance != null && instance.panel != null)
        {
            instance.panel.SetActive(false);
            instance.workshop = null;
        }
    }

    void Update()
    {
        if (panel == null || !panel.activeSelf) return;

        if (workshop == null)  // destroyed while open
        {
            Close();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
            return;
        }

        RefreshAll();
    }

    // ------------------------------------------------------------------
    // Refresh
    // ------------------------------------------------------------------

    void RefreshAll()
    {
        var recipes = CraftedUpgrades.Recipes;
        var rm = ResourceManager.Instance;

        for (int i = 0; i < recipes.Length && i < craftButtons.Count; i++)
        {
            var recipe = recipes[i];
            bool busy = workshop != null && workshop.ActiveRecipe != null;
            bool affordable = rm != null && rm.CanAfford(recipe.woodCost, recipe.foodCost, recipe.stoneCost);

            craftButtons[i].interactable = !recipe.crafted && !busy && affordable;
            string label = recipe.crafted ? "Crafted" : "Craft";
            if (buttonLabels[i].text != label) buttonLabels[i].text = label;
        }

        string status;
        if (workshop != null && workshop.ActiveRecipe != null)
        {
            status = "Crafting " + workshop.ActiveRecipe.title + "...  "
                + Mathf.RoundToInt(workshop.ActiveProgress01 * 100f) + "%";
        }
        else
        {
            status = "Select an upgrade to craft.";
        }

        if (status != lastStatus)
        {
            lastStatus = status;
            statusText.text = status;
        }
    }

    // ------------------------------------------------------------------
    // Construction (runtime uGUI, no scene assets)
    // ------------------------------------------------------------------

    void BuildUI()
    {
        GameObject canvasObj = new GameObject("CraftingCanvas");
        canvasObj.transform.SetParent(transform, false);
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        canvasObj.AddComponent<GraphicRaycaster>();

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        // Panel — right side, vertically centered
        panel = new GameObject("Panel");
        panel.transform.SetParent(canvasObj.transform, false);
        Image bg = panel.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.07f, 0.06f, 0.92f);

        RectTransform prt = panel.GetComponent<RectTransform>();
        prt.anchorMin = new Vector2(1f, 0.5f);
        prt.anchorMax = new Vector2(1f, 0.5f);
        prt.pivot = new Vector2(1f, 0.5f);
        prt.anchoredPosition = new Vector2(-24f, 0f);

        var recipes = CraftedUpgrades.Recipes;
        float rowH = 92f;
        float headerH = 54f;
        float statusH = 40f;
        float pad = 14f;
        prt.sizeDelta = new Vector2(390f, headerH + recipes.Length * rowH + statusH + pad * 2f);

        // Header
        var header = MakeText(panel.transform, "WORKSHOP", 26f, new Color(1f, 0.85f, 0.4f));
        SetRect(header.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -pad), new Vector2(-70f, headerH - pad));
        header.alignment = TextAlignmentOptions.MidlineLeft;
        header.margin = new Vector4(18f, 0f, 0f, 0f);

        // Close button (X)
        Button closeBtn = MakeButton(panel.transform, "X", out TextMeshProUGUI closeLabel);
        closeLabel.fontSize = 20f;
        RectTransform cbrt = closeBtn.GetComponent<RectTransform>();
        SetRect(cbrt, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
            new Vector2(-12f, -12f), new Vector2(36f, 36f));
        closeBtn.onClick.AddListener(() =>
        {
            Close();
            if (AudioManager.Instance != null) AudioManager.Instance.PlayButtonClick();
        });

        // Recipe rows
        for (int i = 0; i < recipes.Length; i++)
        {
            var recipe = recipes[i];

            GameObject row = new GameObject("Row_" + recipe.id);
            row.transform.SetParent(panel.transform, false);
            RectTransform rrt = row.AddComponent<RectTransform>();
            SetRect(rrt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -(headerH + i * rowH)), new Vector2(-2f * pad, rowH - 8f));

            Image rowBg = row.AddComponent<Image>();
            rowBg.color = new Color(1f, 1f, 1f, 0.05f);

            var title = MakeText(row.transform, recipe.title, 19f, Color.white);
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -4f), new Vector2(-110f, 26f));
            title.alignment = TextAlignmentOptions.TopLeft;
            title.margin = new Vector4(10f, 0f, 0f, 0f);

            string costLine = BuildCostLine(recipe);
            var desc = MakeText(row.transform, recipe.description + "\n" + costLine, 14f,
                new Color(0.8f, 0.8f, 0.75f));
            SetRect(desc.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -14f), new Vector2(-110f, -32f));
            desc.alignment = TextAlignmentOptions.TopLeft;
            desc.margin = new Vector4(10f, 0f, 0f, 0f);

            Button btn = MakeButton(row.transform, "Craft", out TextMeshProUGUI btnLabel);
            RectTransform brt = btn.GetComponent<RectTransform>();
            SetRect(brt, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-8f, 0f), new Vector2(88f, 40f));

            var captured = recipe;
            btn.onClick.AddListener(() =>
            {
                if (workshop != null && workshop.TryStartCraft(captured))
                {
                    if (AudioManager.Instance != null) AudioManager.Instance.PlayButtonClick();
                }
            });

            craftButtons.Add(btn);
            buttonLabels.Add(btnLabel);
        }

        // Status line at the bottom
        statusText = MakeText(panel.transform, "", 15f, new Color(0.75f, 0.9f, 1f));
        SetRect(statusText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
            new Vector2(0f, pad), new Vector2(-2f * pad, statusH - 8f));
        statusText.alignment = TextAlignmentOptions.MidlineLeft;
        statusText.margin = new Vector4(10f, 0f, 0f, 0f);

        panel.SetActive(false);
    }

    static string BuildCostLine(CraftedUpgrades.Recipe recipe)
    {
        var parts = new List<string>(3);
        if (recipe.woodCost > 0) parts.Add(recipe.woodCost + " Wood");
        if (recipe.foodCost > 0) parts.Add(recipe.foodCost + " Food");
        if (recipe.stoneCost > 0) parts.Add(recipe.stoneCost + " Stone");
        return "Cost: " + string.Join(", ", parts);
    }

    static TextMeshProUGUI MakeText(Transform parent, string text, float size, Color color)
    {
        GameObject go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.raycastTarget = false;
        return tmp;
    }

    static Button MakeButton(Transform parent, string label, out TextMeshProUGUI labelText)
    {
        GameObject go = new GameObject("Button_" + label);
        go.transform.SetParent(parent, false);

        Image img = go.AddComponent<Image>();
        img.color = new Color(0.25f, 0.42f, 0.24f, 1f);

        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        ColorBlock colors = btn.colors;
        colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.6f);
        btn.colors = colors;

        labelText = MakeText(go.transform, label, 16f, Color.white);
        labelText.alignment = TextAlignmentOptions.Center;
        RectTransform lrt = labelText.rectTransform;
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;

        return btn;
    }

    static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 anchoredPos, Vector2 sizeDelta)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;
    }
}
