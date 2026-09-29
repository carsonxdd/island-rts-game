using UnityEngine;
using UnityEngine.AI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// A building under construction. Stands in for the real building while it goes up, then
/// replaces itself with the finished prefab.
/// </summary>
/// <remarks>
/// The site is what the player has already paid for, so it is a real obstacle from the
/// moment it appears: it occupies its grid cell, blocks further placement there and can be
/// attacked. Walls and gates register their cell with WallGrid while still being built, so
/// a wall line reserves its cells immediately.
/// </remarks>
public class ConstructionSite : MonoBehaviour, IOwned
{
    // Owner (lap step 1 commit 5). Set by Spawn.Owned right after Instantiate
    // (commit 6); the player's when nothing set it. Read in Start or later, never Awake.
    Faction owner;
    public Faction Faction { get => owner ?? (owner = Factions.Player); set => owner = value; }
    public static IReadOnlyList<ConstructionSite> ActiveList => ActiveRegistry<ConstructionSite>.List;

    void Awake() { ActiveRegistry<ConstructionSite>.Register(this); }

    [Header("Construction Settings")]
    public float buildTime = 5f;               // Seconds to complete, before build-speed upgrades
    public GameObject finishedBuildingPrefab;  // Legacy fallback; the type is normally resolved via BuildingDatabase
    public float targetHealth = 100f;          // Health the finished building will have

    [Header("Building Type (Set by BuildPlacement)")]
    public BuildingType buildingType = BuildingType.Hut;  // Which building type to construct

    // --- Upgrades (2026-09-18) ------------------------------------------------
    // An upgrade is an ordinary site standing where the old building stood, so the
    // same colonists work it through the same AddLabor. Two things differ: the pad is
    // already flat (BuildingUpgrade never re-flattens - a second FlattenArea re-bakes
    // the NavMesh for nothing), and the sign over it says "Upgrading".
    [System.NonSerialized] public bool isUpgrade;

    [Header("Progress")]
    [Range(0f, 1f)]
    public float progress = 0f;  // 0 = just started, 1 = complete

    [Header("Building Placement")]
    public float noBuildRadius = 3.5f;  // Creates 7x7 square no-build zone (3 grid cell buffer)

    [Header("Progress Display")]
    public bool showProgressText = true;
    public float progressTextHeight = 2.5f;

    // --- Labour (2026-09-02) ---
    // A site advances only while a colonist works it. One builder finishes in
    // buildTime * LaborFactor seconds; extra builders stack linearly up to
    // MaxBuilders. LaborFactor is a constant rather than a field because the
    // prefab's serialized buildTime is what actually gets read (dead-data rule).
    public const float LaborFactor = 2f;
    public const int MaxBuilders = 3;

    private readonly List<Worker> builders = new List<Worker>();
    private float lastLaborTime = float.NegativeInfinity;
    private bool lastShownAwaiting;
    private const float AwaitingAfter = 0.5f;   // no labour for this long → "Awaiting builder"

    private float timeElapsed = 0f;
    private bool isComplete = false;
    public bool IsComplete => isComplete;
    private TextMeshPro progressText;
    private GameObject progressTextObject;
    private Camera cachedCamera;
    private int lastDisplayedProgressHP = -1;

    // WallGrid registration
    private Vector2Int gridPos;
    private bool registeredWithGrid = false;

    // The carve (2026-09-28, the wall lag): counted for the F6 logger when it appears
    // and when it goes, each once.
    private NavMeshObstacle obstacle;
    private bool carveCounted;
    private bool carveReleased;

    void Start()
    {
        cachedCamera = Camera.main;

        // The site draws like the building it becomes: through the cutout + fog shader,
        // and - another colony's - only on watched ground. Before 2026-09-28 a rival's
        // wall line and its progress labels (sorting order 100) showed through the fog.
        OccluderCutout.AttachTo(gameObject);
        if (!Faction.IsPlayer) FogVisibility.Attach(gameObject, FogVisibility.Rule.Visible);

        SetupCarve();

        // Create progress text display
        if (showProgressText)
        {
            CreateProgressText();
        }

        // Register with WallGrid if this is a wall type
        RegisterIfWall();
    }

    /// <summary>
    /// Make the obstacle carve. A wall site carves exactly the finished wall's box at the
    /// finished wall's world height (<see cref="Wall.ConfigureCarve"/>), so the hand-over
    /// in <see cref="Complete"/> leaves the NavMesh as it was. Runs from SetBuildingType as
    /// well as Start, so the prefab's big default box never carves for a wall.
    /// </summary>
    private void SetupCarve()
    {
        if (obstacle == null) obstacle = GetComponent<NavMeshObstacle>();
        if (obstacle == null || carveReleased) return;

        if (IsWallType())
        {
            float wallY = WallConnector.SnapToCell(transform.position).y;
            Wall.ConfigureCarve(obstacle, wallY + Wall.CarveCentreY - transform.position.y);
        }
        else
        {
            obstacle.enabled = true;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;
        }

        if (!carveCounted) { carveCounted = true; PerfCounters.Hit(PerfCounters.K.CarveChange); }
    }

    private void ReleaseCarve()
    {
        if (carveReleased) return;
        carveReleased = true;
        if (obstacle == null) obstacle = GetComponent<NavMeshObstacle>();
        if (obstacle != null && obstacle.enabled)
        {
            obstacle.enabled = false;
            if (carveCounted) PerfCounters.Hit(PerfCounters.K.CarveChange);
        }
    }

    private bool IsWallType()
    {
        if (BuildingDatabase.Instance == null) return false;
        BuildingData data = BuildingDatabase.Instance.GetBuildingData(buildingType);
        return data != null && data.isWall;
    }

    void Update()
    {
        if (isComplete) return;

        // No auto-build any more: progress arrives through AddLabor from the
        // colonists working the site. Update only keeps the sign current.
        if (showProgressText && progressText != null)
        {
            UpdateProgressText();
        }
    }

    // ------------------------------------------------------------------
    // Labour
    // ------------------------------------------------------------------

    /// <summary>
    /// One builder's frame of work. The Workshop's "Sturdy Scaffolds" upgrade
    /// multiplies it. Completes the site when the labour adds up.
    /// </summary>
    public void AddLabor(float seconds)
    {
        if (isComplete || seconds <= 0f) return;

        lastLaborTime = Time.time;
        // Sturdy Scaffolds speeds it up; a hungry colony slows it down (2026-09-04)
        timeElapsed += seconds * Faction.Knowledge.BuildSpeedMult * Faction.Population.LaborMultiplier;
        progress = Mathf.Clamp01(timeElapsed / (Mathf.Max(0.01f, buildTime) * LaborFactor));

        if (progress >= 1f)
        {
            Complete();
        }
    }

    /// <summary>True while nobody has worked this site for a moment.</summary>
    public bool AwaitingBuilder => !isComplete && Time.time - lastLaborTime > AwaitingAfter;

    /// <summary>Builders registered on this site (dead entries dropped).</summary>
    public int BuilderCount
    {
        get
        {
            for (int i = builders.Count - 1; i >= 0; i--)
            {
                if (builders[i] == null) builders.RemoveAt(i);
            }
            return builders.Count;
        }
    }

    /// <summary>Room for this colonist: a free slot, or already registered here.</summary>
    public bool HasBuilderRoom(Worker worker)
    {
        if (isComplete) return false;
        if (worker != null && builders.Contains(worker)) return true;
        return BuilderCount < MaxBuilders;
    }

    /// <summary>Claim a builder slot. False when the site is full or finished.</summary>
    public bool RegisterBuilder(Worker worker)
    {
        if (worker == null || !HasBuilderRoom(worker)) return false;
        if (!builders.Contains(worker)) builders.Add(worker);
        return true;
    }

    public void UnregisterBuilder(Worker worker)
    {
        if (worker != null) builders.Remove(worker);
    }

    void Complete()
    {
        isComplete = true;
        DevQuests.Signal("build:" + buildingType.ToString().ToLowerInvariant());

        // The site's carve goes this frame, whatever follows. A wall's carve replaces it
        // in the same frame below, so the tile rebuilds once, not twice with a hole between.
        ReleaseCarve();

        // Get building data from database
        if (BuildingDatabase.Instance == null)
        {
            UnregisterFromGrid();
            Destroy(gameObject);
            return;
        }

        BuildingData data = BuildingDatabase.Instance.GetBuildingData(buildingType);
        if (data == null || data.finishedBuildingPrefab == null)
        {
            UnregisterFromGrid();
            Destroy(gameObject);
            return;
        }

        // Spawn the finished building. A wall is spawned where it will STAND (its cell
        // centre on the ground, not the site's lifted pivot), shaped, carved and given the
        // cell right now: WallConnector used to snap it down in its own Start, and that move
        // restarted the carve of a wall that had only just appeared (the wall lag, 2026-09-28).
        bool isWall = data.isWall;
        Vector3 pos = isWall ? WallConnector.SnapToCell(transform.position) : transform.position;
        Quaternion rot = isWall ? Quaternion.identity : transform.rotation;
        GameObject finishedBuilding = Spawn.Owned(data.finishedBuildingPrefab, pos, rot, Faction);

        // Copy layer to finished building
        finishedBuilding.layer = gameObject.layer;

        Wall wall = isWall ? finishedBuilding.GetComponent<Wall>() : null;
        if (wall != null)
        {
            WallConnector connector = finishedBuilding.GetComponent<WallConnector>();
            if (connector != null) connector.EnsureInitialized();
            wall.ClaimCarveAndCell();   // takes the cell over from this site: no empty frame
        }

        // Frees the cell only if the wall did not just take it
        UnregisterFromGrid();

        // Destroy the construction site
        Destroy(gameObject);
    }

    /// <summary>
    /// Set the building type to construct (called by BuildPlacement)
    /// </summary>
    public void SetBuildingType(BuildingType type)
    {
        buildingType = type;

        // Update target health from building data
        if (BuildingDatabase.Instance != null)
        {
            BuildingData data = BuildingDatabase.Instance.GetBuildingData(type);
            if (data != null)
            {
                targetHealth = data.maxHealth;
                // A slow build (the Shipyard) carries its own time; 0 keeps the
                // site prefab's serialized buildTime (2026-09-04).
                if (data.buildTimeOverride > 0f) buildTime = data.buildTimeOverride;
            }
        }

        // Register with WallGrid if this is a wall type (may be called after Start), and
        // carve the wall's box from the first frame rather than the prefab's default.
        RegisterIfWall();
        SetupCarve();
    }

    // Method for workers to add progress (we'll use this later)
    public void AddProgress(float amount)
    {
        if (isComplete) return;

        progress += amount;
        progress = Mathf.Clamp01(progress);

        if (progress >= 1f)
        {
            Complete();
        }
    }

    private void RegisterIfWall()
    {
        if (registeredWithGrid) return;

        bool isWall = false;
        if (BuildingDatabase.Instance != null)
        {
            BuildingData data = BuildingDatabase.Instance.GetBuildingData(buildingType);
            isWall = data != null && data.isWall;
        }

        if (isWall)
        {
            gridPos = WallGrid.Instance.WorldToGrid(transform.position);
            WallGrid.Instance.Register(gridPos, this);
            registeredWithGrid = true;
        }
    }

    private void UnregisterFromGrid()
    {
        if (registeredWithGrid && WallGrid.Instance != null)
        {
            WallGrid.Instance.Unregister(gridPos, this);
            registeredWithGrid = false;
        }
    }

    void OnDestroy()
    {
        ActiveRegistry<ConstructionSite>.Unregister(this);
        UnregisterFromGrid();
        if (carveCounted && !carveReleased)
        {
            carveReleased = true;
            PerfCounters.Hit(PerfCounters.K.CarveChange);   // demolished or cancelled: the carve goes with it
        }
    }

    void CreateProgressText()
    {
        // Create a child GameObject for the text
        progressTextObject = new GameObject("ProgressText");
        progressTextObject.transform.parent = transform;
        progressTextObject.transform.localPosition = new Vector3(0, progressTextHeight, 0);

        // Add TextMeshPro component
        progressText = progressTextObject.AddComponent<TextMeshPro>();
        progressText.fontSize = 2.5f;
        progressText.alignment = TextAlignmentOptions.Center;
        progressText.color = Color.cyan;

        // Set sorting to render on top
        progressText.GetComponent<MeshRenderer>().sortingOrder = 100;

        UpdateProgressText();  // Set initial text
    }

    void UpdateProgressText()
    {
        if (progressText == null)
            return;

        // Billboard effect - always face camera (every frame, zero allocations)
        if (cachedCamera != null)
        {
            progressTextObject.transform.LookAt(cachedCamera.transform);
            progressTextObject.transform.Rotate(0, 180, 0);
        }

        // Only rebuild text when the displayed HP integer or the awaiting state changes
        int currentHP = Mathf.RoundToInt(progress * targetHealth);
        bool awaiting = AwaitingBuilder;
        if (currentHP == lastDisplayedProgressHP && awaiting == lastShownAwaiting) return;
        lastDisplayedProgressHP = currentHP;
        lastShownAwaiting = awaiting;

        // Update text content
        if (progress >= 1f)
        {
            progressText.text = "Complete!";
            progressText.color = Color.green;
        }
        else if (awaiting)
        {
            // Nobody on it: an idle colonist is what builds things now
            progressText.text = $"Awaiting builder\n{currentHP} / {targetHealth:F0} HP";
            progressText.color = new Color(1f, 0.6f, 0.2f);
        }
        else
        {
            progressText.text = isUpgrade
                ? $"Upgrading...\n{currentHP} / {targetHealth:F0} HP"
                : $"Building...\n{currentHP} / {targetHealth:F0} HP";

            // Color based on progress
            if (progress >= 0.66f)
            {
                progressText.color = Color.green;
            }
            else if (progress >= 0.33f)
            {
                progressText.color = Color.yellow;
            }
            else
            {
                progressText.color = Color.cyan;
            }
        }
    }

    // Visual feedback in Scene view
    void OnDrawGizmos()
    {
        // Draw progress bar above construction site
        Gizmos.color = Color.yellow;
        Vector3 pos = transform.position + Vector3.up * 2f;
        Gizmos.DrawWireCube(pos, new Vector3(2f, 0.2f, 0.1f));

        // Fill bar based on progress
        Gizmos.color = Color.green;
        float fillWidth = 2f * progress;
        Gizmos.DrawCube(pos - Vector3.right * (2f - fillWidth) * 0.5f, new Vector3(fillWidth, 0.2f, 0.1f));
    }

    void OnDrawGizmosSelected()
    {
        // Draw no-build radius
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, noBuildRadius);
    }
}
