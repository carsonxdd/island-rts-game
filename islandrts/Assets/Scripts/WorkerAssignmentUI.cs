using UnityEngine;

/// <summary>
/// The campfire panel's coordinator (2026-09-17). The old five-tab panel is two
/// panels that share one corner: <see cref="ColonyPanel"/> (jobs, people,
/// priorities, defence, stock) and <see cref="BenchPanel"/> (make, learn, the
/// queue). A left-click on the fire or a jobless colonist opens COLONY; a
/// Workshop opens BENCH for that bench; each title bar has a button that swaps
/// to the other. Only one is open at a time, Esc closes whichever it is.
/// </summary>
/// <remarks>
/// Built entirely in code on the menu system's widgets — no scene wiring. Stays
/// a scene component (the opening sequence setup wires it to the campfire), and
/// keeps the static entry points every caller uses: <see cref="OpenPanel"/>,
/// <see cref="OpenColonists"/>, <see cref="OpenStation"/>, <see cref="ClosePanel"/>,
/// <see cref="IsOpen"/> (PauseController lets Esc close it instead of pausing).
/// Never built under the balance sim.
/// </remarks>
public class WorkerAssignmentUI : MonoBehaviour
{
    public static WorkerAssignmentUI Instance { get; private set; }

    /// <summary>True while either panel is showing — PauseController lets Esc close it instead of pausing.</summary>
    public static bool IsOpen => Instance != null && Instance.colony != null && (Instance.colony.IsOpen || Instance.bench.IsOpen);

    private ColonyPanel colony;
    private BenchPanel bench;
    private bool built;
    private BaseBuilding fire;   // the campfire the open panel is about (the swap needs it)

    void Awake()
    {
        Instance = this;
        // Made here, not in a field initializer: they need this, and a MonoBehaviour has no constructor of its own
        colony = new ColonyPanel(this);
        bench = new BenchPanel(this);
    }

    void Start()
    {
        if (SimHooks.Headless) { enabled = false; return; }
        Build();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Build()
    {
        if (built) return;
        built = true;
        // Root-level canvas on purpose (a canvas nested in a canvas ignores its own scaler)
        Canvas canvas = MenuBuilder.CreateCanvas("CampfireCanvas", 60);
        colony.Build(canvas.transform);
        bench.Build(canvas.transform);
    }

    // ------------------------------------------------------------------
    // Open / close
    // ------------------------------------------------------------------

    /// <summary>
    /// Opens the COLONY panel for the campfire. Called from BaseBuilding when
    /// the player clicks it, from the HUD, and after the character deposits.
    /// </summary>
    public void OpenPanel(BaseBuilding building)
    {
        if (building == null || SimHooks.Headless) return;
        Build();
        fire = building;
        bench.Close();
        colony.Open(building);
        DevQuests.Signal("panel:colony");
    }

    /// <summary>A left-click on a jobless colonist lands on the COLONY panel (2026-09-07).</summary>
    public void OpenColonists(BaseBuilding building)
    {
        OpenPanel(building);
    }

    /// <summary>
    /// Opens the BENCH panel for a bench. The campfire's own bench opens the
    /// campfire's BENCH (with the swap back to COLONY); any other station (the
    /// Workshop) opens BENCH alone, for that station.
    /// </summary>
    public void OpenStation(CraftStation st)
    {
        if (st == null || SimHooks.Headless) return;
        BaseBuilding campfire = Factions.Player.Campfire;
        if (campfire == null) return;
        Build();
        fire = campfire;
        colony.Close();
        bench.Open(campfire, st, stationOnly: st != campfire.Station);
        DevQuests.Signal("panel:bench");
    }

    /// <summary>The COLONY title bar's BENCH button.</summary>
    public void SwapToBench()
    {
        if (fire == null || fire.Station == null) return;
        Tooltip.HideNow();
        colony.Close();
        bench.Open(fire, fire.Station, stationOnly: false);
        Click();
        DevQuests.Signal("panel:swap");
    }

    /// <summary>The BENCH title bar's COLONY button.</summary>
    public void SwapToColony()
    {
        if (fire == null) return;
        Tooltip.HideNow();
        bench.Close();
        colony.Open(fire);
        Click();
        DevQuests.Signal("panel:swap");
    }

    public void ClosePanel()
    {
        Tooltip.HideNow();
        colony.Close();
        bench.Close();
        fire = null;
        Click();
    }

    static void Click()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlayButtonClick();
    }

    void Update()
    {
        if (!built) return;
        bool colonyOpen = colony.IsOpen, benchOpen = bench.IsOpen;
        if (!colonyOpen && !benchOpen) return;

        if (fire == null)   // destroyed while open
        {
            colony.Close();
            bench.Close();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            ClosePanel();
            return;
        }

        if (colonyOpen) colony.Tick();
        if (benchOpen) bench.Tick();
    }
}
