using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The build palette (2026-09-18): a grouped bar of building tiles across the bottom of the
/// screen, shown only while build mode is open. Replaces <c>BuildingSelectionUI</c>, which
/// was a name-and-cost text panel that had never actually been placed in the scene - the
/// game shipped with the number keys and no palette at all.
/// </summary>
/// <remarks>
/// It PULLS rather than being pushed to. The old UI was updated from eight call sites
/// scattered through <c>BuildPlacement</c> / <c>GhostPlacer</c> / <c>WallLinePlacer</c>;
/// this reads the build state each frame and dirty-checks what it draws, the way
/// <see cref="CombatHUD"/> and <c>ResourceUI</c> do. The placers no longer know a UI exists.
///
/// Its contents come entirely from <see cref="BuildingData"/>: <c>category</c> picks the
/// group, <c>placeable</c> decides whether it appears at all (which is what keeps the Hut
/// out - it is only ever reached by upgrading a Tent), and <c>requiresUnlock</c> greys a
/// tile and names the research. A new building shows up here with no change to this file.
///
/// <see cref="PointerOver"/> is load-bearing, not polish: uGUI stops nothing, so without it
/// a click on a tile also drops a building on the ground underneath it and the camera edge-
/// pans behind it. Same pattern as <see cref="Minimap.PointerOver"/> - a rect test against
/// the mouse, not the EventSystem, which the HUD makes true almost everywhere.
/// </remarks>
public class BuildPaletteHUD : MonoBehaviour
{
    private static BuildPaletteHUD instance;

    /// <summary>True while the mouse is over the bar. Every gameplay click site checks it.</summary>
    public static bool PointerOver { get; private set; }

    const float TileWidth = 116f;
    const float TileHeight = 58f;
    /// <summary>Clear of the PlayerHUD inventory strip, which owns the very bottom of the screen.</summary>
    const float BottomOffset = 104f;

    private RectTransform root;
    private RectTransform bar;
    private TextMeshProUGUI hintText;
    private TextMeshProUGUI lineText;

    private readonly List<Tile> tiles = new List<Tile>();
    private BuildPlacement placement;

    private int lastSelected = -1;
    private int lastAffordMask = -1;
    private int lastUnlockMask = -1;
    private int lastLineCount = -1;
    private string lastNotice;
    private bool hintWall;
    private bool builtRows;

    /// <summary>One building's tile: the button, and the labels that change.</summary>
    private class Tile
    {
        public BuildingType type;
        public BuildingData data;
        public Button button;
        public Image background;
        public TextMeshProUGUI cost;
        public RectTransform rect;
    }

    /// <summary>Create the bar for this scene if it does not exist yet. Skipped under the sim.</summary>
    public static void Ensure()
    {
        if (instance != null || SimHooks.Headless) return;
        GameObject go = new GameObject("[BuildPaletteHUD]");
        instance = go.AddComponent<BuildPaletteHUD>();
    }

    void Awake() { instance = this; }

    void OnDestroy() { if (instance == this) instance = null; }

    void OnDisable() { PointerOver = false; }

    void Update()
    {
        if (placement == null) placement = FindAnyObjectByType<BuildPlacement>();

        bool show = placement != null && placement.isPlacing && !PauseController.BlockGameplayInput;
        if (!show)
        {
            if (root != null && root.gameObject.activeSelf)
            {
                root.gameObject.SetActive(false);
                Tooltip.HideNow();   // a vanished tile sends no pointer-exit
            }
            PointerOver = false;
            return;
        }

        if (root == null) Build();
        if (!builtRows) BuildTiles();

        if (!root.gameObject.activeSelf)
        {
            root.gameObject.SetActive(true);
            RefreshHint();           // a rebind while hidden lands on the next show
            lastSelected = lastAffordMask = lastUnlockMask = lastLineCount = -1;
        }

        PointerOver = RectTransformUtility.RectangleContainsScreenPoint(bar, Input.mousePosition, null);

        RefreshSelection();
        RefreshCosts();
        RefreshLine();
    }

    // ------------------------------------------------------------------
    // Per-frame dirty checks
    // ------------------------------------------------------------------

    void RefreshSelection()
    {
        int selected = -1;
        for (int i = 0; i < tiles.Count; i++)
        {
            if (tiles[i].type == placement.selectedBuildingType) { selected = i; break; }
        }
        if (selected == lastSelected) return;
        lastSelected = selected;
        if (placement.IsWallType(placement.selectedBuildingType) != hintWall) RefreshHint();

        for (int i = 0; i < tiles.Count; i++)
        {
            tiles[i].background.color = i == selected ? MenuStyle.ButtonPressed : MenuStyle.ButtonFill;
        }
    }

    /// <summary>
    /// Cost colour and the lock state, both dirty-checked as bitmasks so a full pass over
    /// the tiles only happens on the frame something actually changed.
    /// </summary>
    void RefreshCosts()
    {
        Faction player = Factions.Player;
        int affordMask = 0;
        int unlockMask = 0;
        for (int i = 0; i < tiles.Count && i < 31; i++)
        {
            if (tiles[i].data.AffordableBy(player)) affordMask |= 1 << i;
            if (tiles[i].data.UnlockedFor(player)) unlockMask |= 1 << i;
        }
        if (affordMask == lastAffordMask && unlockMask == lastUnlockMask) return;
        lastAffordMask = affordMask;
        lastUnlockMask = unlockMask;

        for (int i = 0; i < tiles.Count; i++)
        {
            Tile t = tiles[i];
            bool unlocked = i < 31 ? (unlockMask & (1 << i)) != 0 : t.data.UnlockedFor(player);
            bool afford = i < 31 ? (affordMask & (1 << i)) != 0 : t.data.AffordableBy(player);

            if (!unlocked)
            {
                t.cost.text = "Locked";
                t.cost.color = MenuStyle.TextMuted;
            }
            else
            {
                t.cost.text = t.data.CostLine();
                t.cost.color = afford ? MenuStyle.TextMuted : MenuStyle.TextDanger;
            }
        }
    }

    /// <summary>
    /// The running total while a wall line is being dragged. The placer counts the cells
    /// (<see cref="WallLinePlacer.LineWallCount"/>) and this reads it; nothing is pushed.
    /// </summary>
    void RefreshLine()
    {
        int count = placement.wallPlacer != null ? placement.wallPlacer.LineWallCount : 0;
        string notice = placement.Notice;
        if (count == lastLineCount && ReferenceEquals(notice, lastNotice)) return;
        lastLineCount = count;
        lastNotice = notice;

        if (count <= 0)
        {
            // Nothing being drawn: the line shows the placer's last refusal, if any
            lineText.text = notice ?? "";
            lineText.color = MenuStyle.TextDanger;
            return;
        }

        BuildingData data = BuildingDatabase.Instance != null
            ? BuildingDatabase.Instance.GetBuildingData(placement.selectedBuildingType)
            : null;
        if (data == null) { lineText.text = ""; return; }

        bool afford = Factions.Player.Resources.CanAfford(
            data.woodCost * count, data.foodCost * count, data.stoneCost * count, data.metalCost * count);
        lineText.text = data.buildingName + " x" + count + "   " + data.CostLine(count);
        lineText.color = afford ? MenuStyle.TextAccent : MenuStyle.TextDanger;
    }

    /// <summary>The hint line, built from live bindings so a rebind is reflected verbatim.
    /// A wall has its own line (2026-09-22): the drawing gestures would not fit beside the rest.</summary>
    void RefreshHint()
    {
        hintWall = placement.IsWallType(placement.selectedBuildingType);
        hintText.text = hintWall
            ? "Drag to draw, or click points and " + Key(KeyBindings.Action.FinishWallLine) + " / double-click  ·  " +
              "hold " + Key(KeyBindings.Action.StraightWallPath) + " square path (" +
              Key(KeyBindings.Action.RotateBuilding) + " flips)  ·  " +
              Key(KeyBindings.Action.ConvertToGate) + " wall to gate  ·  Esc cancel"
            : Key(KeyBindings.Action.RotateBuilding) + " rotate  ·  " +
              Key(KeyBindings.Action.ConvertToGate) + " wall to gate  ·  " +
              Key(KeyBindings.Action.QueueCommand) + "+click keep placing  ·  " +
              Key(KeyBindings.Action.Demolish) + " demolish  ·  Esc cancel";
    }

    static string Key(KeyBindings.Action action)
    {
        return KeyBindings.Name(KeyBindings.Get(action).primary);
    }

    // ------------------------------------------------------------------
    // Construction
    // ------------------------------------------------------------------

    void Build()
    {
        Canvas canvas = MenuBuilder.CreateCanvas("BuildPaletteCanvas", 50);
        canvas.transform.SetParent(transform, false);

        GameObject box = new GameObject("BuildPalette", typeof(RectTransform), typeof(Image),
            typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        box.transform.SetParent(canvas.transform, false);
        bar = box.GetComponent<RectTransform>();
        root = bar;
        bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0f);
        bar.pivot = new Vector2(0.5f, 0f);
        bar.anchoredPosition = new Vector2(0f, BottomOffset);

        Image bg = box.GetComponent<Image>();
        bg.color = new Color(MenuStyle.PanelFill.r, MenuStyle.PanelFill.g, MenuStyle.PanelFill.b, 0.88f);
        bg.raycastTarget = true;   // the bar is a control: it swallows clicks on its own background

        VerticalLayoutGroup col = box.GetComponent<VerticalLayoutGroup>();
        col.padding = new RectOffset(14, 14, 8, 8);
        col.spacing = 6f;
        col.childControlWidth = true;
        col.childControlHeight = true;
        col.childForceExpandWidth = true;
        col.childForceExpandHeight = false;
        col.childAlignment = TextAnchor.UpperCenter;

        ContentSizeFitter fit = box.GetComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        root.gameObject.SetActive(false);
    }

    /// <summary>
    /// The tile rows, built once the database exists. Groups and their contents come
    /// straight from the assets, so a new building needs no edit here.
    /// </summary>
    void BuildTiles()
    {
        if (BuildingDatabase.Instance == null) return;
        builtRows = true;

        GameObject groups = new GameObject("Groups", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        groups.transform.SetParent(bar, false);
        HorizontalLayoutGroup groupRow = groups.GetComponent<HorizontalLayoutGroup>();
        groupRow.spacing = 18f;
        groupRow.childControlWidth = true;
        groupRow.childControlHeight = true;
        groupRow.childForceExpandWidth = false;
        groupRow.childForceExpandHeight = false;
        groupRow.childAlignment = TextAnchor.LowerCenter;

        foreach (BuildCategory category in System.Enum.GetValues(typeof(BuildCategory)))
        {
            BuildGroup(groups.transform, category);
        }

        // The wall-line total, and under it the context hints.
        lineText = MenuBuilder.Label(bar, "", MenuStyle.SmallSize + 1f, MenuStyle.TextAccent, TextAlignmentOptions.Center);
        lineText.textWrappingMode = TextWrappingModes.NoWrap;
        lineText.overflowMode = TextOverflowModes.Overflow;
        lineText.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

        hintText = MenuBuilder.Label(bar, "", MenuStyle.SmallSize - 1f, MenuStyle.TextMuted, TextAlignmentOptions.Center);
        hintText.textWrappingMode = TextWrappingModes.NoWrap;
        hintText.overflowMode = TextOverflowModes.Overflow;
        hintText.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

        RefreshHint();
    }

    /// <summary>One heading and the tiles under it. Draws nothing when the group is empty.</summary>
    void BuildGroup(Transform parent, BuildCategory category)
    {
        List<BuildingData> members = new List<BuildingData>();
        BuildingData[] all = BuildingDatabase.Instance.buildings;
        for (int i = 0; i < all.Length; i++)
        {
            BuildingData d = all[i];
            if (d == null || !d.placeable || d.category != category) continue;
            // Gates are converted from finished walls, never placed, and the database
            // carries their data for pricing only.
            if (d.buildingType == BuildingType.WoodenGate || d.buildingType == BuildingType.StoneGate) continue;
            members.Add(d);
        }
        if (members.Count == 0) return;

        GameObject group = new GameObject(category.ToString(), typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        group.transform.SetParent(parent, false);
        VerticalLayoutGroup vertical = group.GetComponent<VerticalLayoutGroup>();
        vertical.spacing = 4f;
        vertical.childControlWidth = true;
        vertical.childControlHeight = true;
        vertical.childForceExpandWidth = true;
        vertical.childForceExpandHeight = false;
        vertical.childAlignment = TextAnchor.UpperCenter;

        TextMeshProUGUI heading = MenuBuilder.Label(group.transform, category.ToString().ToUpperInvariant(),
            MenuStyle.SmallSize - 2f, MenuStyle.TextAccent, TextAlignmentOptions.Center);
        heading.characterSpacing = 3f;
        heading.textWrappingMode = TextWrappingModes.NoWrap;
        heading.overflowMode = TextOverflowModes.Overflow;
        heading.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;

        GameObject row = new GameObject("Tiles", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(group.transform, false);
        row.GetComponent<LayoutElement>().preferredHeight = TileHeight;
        HorizontalLayoutGroup tileRow = row.GetComponent<HorizontalLayoutGroup>();
        tileRow.spacing = 6f;
        tileRow.childControlWidth = true;
        tileRow.childControlHeight = true;
        tileRow.childForceExpandWidth = false;
        tileRow.childForceExpandHeight = true;
        tileRow.childAlignment = TextAnchor.MiddleCenter;

        for (int i = 0; i < members.Count; i++) BuildTile(row.transform, members[i]);
    }

    /// <summary>One building: hotkey badge, name, cost line, and the tooltip behind it.</summary>
    void BuildTile(Transform parent, BuildingData data)
    {
        BuildingType type = data.buildingType;
        Button button = MenuBuilder.MenuButton(parent, "", () => Pick(type));

        // MenuButton gives a single centred label; this tile wants three stacked lines,
        // so the label goes and a column takes its place.
        TextMeshProUGUI stock = button.GetComponentInChildren<TextMeshProUGUI>();
        if (stock != null) Destroy(stock.gameObject);

        LayoutElement le = button.GetComponent<LayoutElement>();
        le.preferredWidth = le.minWidth = TileWidth;
        le.preferredHeight = le.minHeight = TileHeight;

        GameObject column = new GameObject("Lines", typeof(RectTransform), typeof(VerticalLayoutGroup));
        column.transform.SetParent(button.transform, false);
        RectTransform columnRect = column.GetComponent<RectTransform>();
        columnRect.anchorMin = Vector2.zero;
        columnRect.anchorMax = Vector2.one;
        columnRect.offsetMin = new Vector2(4f, 3f);
        columnRect.offsetMax = new Vector2(-4f, -3f);
        VerticalLayoutGroup lines = column.GetComponent<VerticalLayoutGroup>();
        lines.spacing = 0f;
        lines.childControlWidth = true;
        lines.childControlHeight = true;
        lines.childForceExpandWidth = true;
        lines.childForceExpandHeight = false;
        lines.childAlignment = TextAnchor.MiddleCenter;

        KeyBindings.Action hotkey;
        string keyName = TryHotkeyFor(type, out hotkey)
            ? KeyBindings.Name(KeyBindings.Get(hotkey).primary)
            : "";
        TextMeshProUGUI key = MenuBuilder.Label(column.transform, keyName, MenuStyle.SmallSize - 3f,
            MenuStyle.TextAccent, TextAlignmentOptions.Center);
        key.textWrappingMode = TextWrappingModes.NoWrap;
        key.overflowMode = TextOverflowModes.Overflow;
        key.gameObject.AddComponent<LayoutElement>().preferredHeight = 14f;

        TextMeshProUGUI name = MenuBuilder.Label(column.transform, data.buildingName, MenuStyle.SmallSize,
            MenuStyle.TextPrimary, TextAlignmentOptions.Center);
        name.textWrappingMode = TextWrappingModes.NoWrap;
        name.overflowMode = TextOverflowModes.Ellipsis;
        name.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

        TextMeshProUGUI cost = MenuBuilder.Label(column.transform, data.CostLine(), MenuStyle.SmallSize - 2f,
            MenuStyle.TextMuted, TextAlignmentOptions.Center);
        cost.textWrappingMode = TextWrappingModes.NoWrap;
        cost.overflowMode = TextOverflowModes.Overflow;
        cost.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;

        RectTransform rect = button.GetComponent<RectTransform>();
        Tooltip.Attach(rect, TooltipFor(data));

        tiles.Add(new Tile
        {
            type = type,
            data = data,
            button = button,
            background = button.targetGraphic as Image,
            cost = cost,
            rect = rect
        });
    }

    /// <summary>The description, plus what a locked tile is waiting on.</summary>
    static string TooltipFor(BuildingData data)
    {
        string body = string.IsNullOrEmpty(data.description) ? data.buildingName : data.description;
        if (data.requiresUnlock) body += "\nNeeds " + data.RequiredResearchTitle + ".";
        if (data.requiresShore) body += "\nMust be built on the beach.";
        if (data.upgradesTo != null) body += "\nUpgrades to a " + data.upgradesTo.buildingName + ".";
        return body;
    }

    void Pick(BuildingType type)
    {
        if (placement == null) return;
        placement.SelectBuilding(type);
        DevQuests.Signal("build_palette:pick");
    }

    /// <summary>
    /// The number key a type sits on. A building with no binding still gets a tile - it
    /// just shows no badge - so putting one in the palette never requires a key first.
    /// </summary>
    static bool TryHotkeyFor(BuildingType type, out KeyBindings.Action action)
    {
        switch (type)
        {
            case BuildingType.Tent: action = KeyBindings.Action.SelectTent; return true;
            case BuildingType.WoodenWall: action = KeyBindings.Action.SelectWoodWall; return true;
            case BuildingType.StoneWall: action = KeyBindings.Action.SelectStoneWall; return true;
            case BuildingType.Watchtower: action = KeyBindings.Action.SelectWatchtower; return true;
            case BuildingType.Workshop: action = KeyBindings.Action.SelectWorkshop; return true;
            case BuildingType.Shipyard: action = KeyBindings.Action.SelectShipyard; return true;
            case BuildingType.Storehouse: action = KeyBindings.Action.SelectStorehouse; return true;
            default: action = KeyBindings.Action.BuildMode; return false;
        }
    }
}
