using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// Phases of the opening sequence. Colony == normal gameplay (everything
/// that existed before the opening sequence was added).
/// </summary>
public enum GamePhase
{
    Landing,          // player's character in the shallows, right-click walks them ashore
    PlacingCampfire,  // B pressed: campfire ghost follows the mouse
    Settling,         // campfire placed, character walking to it
    Colony            // normal gameplay
}

/// <summary>
/// Owns the opening sequence: the game starts with the player's own character
/// wading ashore from the shipwreck. A name popup asks who they are, the player
/// walks them onto dry land (right-click), places the campfire near them (B,
/// free, one-time), and the character walks to the fire — then normal gameplay
/// begins (build mode enabled, day/night clock running).
///
/// The character stays under the player's control for the whole run
/// (2026-09-02); they are never converted into a colonist. The colony's first
/// colonist comes ashore on its own through PopulationManager's arrival timer
/// once the fire is lit.
///
/// Startup contract with the rest of the game:
///  - DayNightCycle.clockPaused is held true until the campfire is placed,
///    so night 1 (and enemy spawns) can never arrive during the intro.
///  - BuildPlacement is disabled until the colony starts (B belongs to the
///    campfire placer during the intro).
///  - Systems that look for the campfire at Start (GameManager,
///    ResourceSpawner, AIWorldState) tolerate its absence; this controller
///    wires GameManager.campfire and BaseBuilding.workerUI at placement.
///
/// skipIntro replicates the classic start: campfire spawned at
/// skipIntroCampfirePosition in Awake, no popup, clock running — and the
/// player's character standing beside the fire.
/// </summary>
public class GameStartController : MonoBehaviour
{
    public static GameStartController Instance { get; private set; }

    /// <summary>Current phase; Colony when no controller exists (old scenes, tests).</summary>
    public static GamePhase Phase => Instance != null ? Instance.phase : GamePhase.Colony;

    /// <summary>True while the opening sequence still owns the game (no campfire yet).</summary>
    public static bool IntroInProgress => Instance != null && Instance.phase != GamePhase.Colony;

    /// <summary>Fired once when the campfire is lit and normal gameplay begins.</summary>
    public static event System.Action OnColonyStarted;

    [Header("Skip (classic start, for playtesting)")]
    [Tooltip("Skip the intro entirely: spawn the campfire immediately at the position below, no name popup. Replicates the pre-opening-sequence game start (the player's character spawns beside the fire).")]
    public bool skipIntro = false;
    public Vector3 skipIntroCampfirePosition = Vector3.zero;

    [Header("Prefabs (wired by the Opening Sequence setup tool)")]
    public GameObject campfirePrefab;
    public GameObject campfireGhostPrefab;
    [FormerlySerializedAs("survivorPrefab")]
    public GameObject playerPrefab;

    [Header("Scene References")]
    public WorkerAssignmentUI workerUI;
    public Transform survivorSpawnPoint;

    [Header("Campfire Placement Rules")]
    [Tooltip("The campfire must be built within this XZ distance of the player's character — they build it where they stand.")]
    public float maxPlaceDistance = 6f;
    [Tooltip("Minimum XZ clearance from resource nodes (replaces ResourceSpawner's keep-away-from-campfire rule, which can't run before the campfire exists).")]
    public float minResourceClearance = 3f;
    [Tooltip("|x| and |z| beyond this are beach/wading water — no campfire there.")]
    public float dryLandExtent = 63f;
    public float cellSize = 1f;
    public Color validColor = new Color(0.5f, 1f, 0.5f, 0.5f);
    public Color invalidColor = new Color(1f, 0.3f, 0.3f, 0.5f);

    [Header("Settling")]
    [Tooltip("The character counts as arrived at the campfire at this collider-edge distance.")]
    public float settleEdgeDistance = 2f;
    [Tooltip("Failsafe: start the colony after this many seconds even if the character never arrives.")]
    public float settleTimeoutSeconds = 8f;

    // --- Runtime state ---
    private GamePhase phase = GamePhase.Landing;
    private PlayerCharacter player;
    private BaseBuilding placedCampfire;
    private Collider placedCampfireCollider;
    private GameObject ghost;
    private Material[] ghostMaterials;
    private Vector3 ghostTarget;
    private bool ghostValid;
    private float settleDeadline;

    private Camera mainCam;
    private BuildPlacement buildPlacement;
    private DayNightCycle dayNight;

    // Hint UI (created at runtime so the scene needs no extra wiring)
    private GameObject hintCanvasObj;
    private TextMeshProUGUI hintText;
    private float hintFadeStart = -1f;
    private const float HintFadeDuration = 1.5f;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Phase must be correct before other scripts' Start() runs —
        // ResourceSpawner reads IntroInProgress to pick its spawn center.
        if (skipIntro)
        {
            // Classic start: campfire exists from frame 0 (spawned in Awake so
            // every Start()-time lookup — GameManager, ResourceSpawner — finds it).
            SpawnCampfire(skipIntroCampfirePosition);
            phase = GamePhase.Colony;
        }
        else
        {
            phase = GamePhase.Landing;
        }
    }

    void Start()
    {
        buildPlacement = FindAnyObjectByType<BuildPlacement>();
        dayNight = FindAnyObjectByType<DayNightCycle>();
        mainCam = Camera.main;

        if (phase == GamePhase.Colony)
        {
            // skipIntro path: no opening to run, but the player still needs a body
            SpawnPlayerAtCampfire();
            return;
        }

        // Hold the world still until the campfire is lit
        if (buildPlacement != null) buildPlacement.enabled = false;
        if (dayNight != null) dayNight.clockPaused = true;

        SpawnPlayer();
        FrameCameraOnPlayer();
        CreateHintUI();

        // Who are you? Modal until answered; a Restart already has a name and
        // skips straight to the landing. Never under the sim (no player, no UI).
        if (!SimHooks.Simulating && !PlayerProfile.HasActive)
        {
            MenuScreens.Ensure().ShowNameEntry(OnNamed);
        }
        else
        {
            OnNamed();
        }
    }

    void OnNamed()
    {
        SetHint("Welcome ashore, " + PlayerProfile.Name + ".\nRight-click: move · right-click sticks and stones to gather them    B: build a campfire (near you)");
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        UpdateHintFade();

        switch (phase)
        {
            case GamePhase.Landing:
                UpdateLanding();
                break;
            case GamePhase.PlacingCampfire:
                UpdatePlacing();
                break;
            case GamePhase.Settling:
                UpdateSettling();
                break;
                // Colony: controller is passive
        }
    }

    // ------------------------------------------------------------------
    // Landing
    // ------------------------------------------------------------------

    void UpdateLanding()
    {
        if (player == null) return;
        // The name popup (and the pause menu) own input while open
        if (PauseController.BlockGameplayInput) return;

        // Same smart command as in the colony phase, so sticks and stones on the
        // landing beach can be gathered before the fire exists
        if (Input.GetMouseButtonDown(1))
        {
            player.HandleCommandClick();
        }

        if (KeyBindings.Down(KeyBindings.Action.BuildMode))
        {
            EnterPlacement();
        }
    }

    // ------------------------------------------------------------------
    // Campfire placement
    // ------------------------------------------------------------------

    void EnterPlacement()
    {
        if (campfireGhostPrefab == null)
        {
            Debug.LogError("GameStartController: campfireGhostPrefab not assigned! Run Tools > Island RTS > Opening Sequence > Setup Opening Scene.");
            return;
        }

        ghost = Instantiate(campfireGhostPrefab);
        // CampfireGhost.prefab carries no collider today, but park it on Ignore Raycast
        // anyway: RaycastGround queries the Default layer, so a collider added here later
        // would make the ghost occlude its own placement ray (see BuildPlacement.SetupGhost).
        ghost.layer = 2;
        ghostTarget = player != null ? player.transform.position : Vector3.zero;
        ghost.transform.position = ghostTarget;
        ghostMaterials = RendererTint.Collect(ghost.GetComponent<Renderer>());

        phase = GamePhase.PlacingCampfire;
        SetHint("Choose a spot for the campfire — it must be near you.\nClick: place    Esc / right-click: cancel");
    }

    void UpdatePlacing()
    {
        if (PauseController.BlockGameplayInput) return;

        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
        {
            CancelPlacement();
            return;
        }

        // Ghost follows the mouse, grid-snapped, no lag (same feel as GhostPlacer)
        Vector3 point;
        if (RaycastGround(out point))
        {
            ghostTarget = GridSnap.SnapXZ(point, cellSize);
            // Base-pivot art: sits on the ground (terrain height when the island exists)
            ghostTarget.y = TerrainGrid.Instance != null ? TerrainGrid.Instance.SampleHeight(ghostTarget) : 0f;
        }

        ghost.transform.position = ghostTarget;

        ghostValid = IsValidCampfireSpot(ghostTarget);
        RendererTint.SetColor(ghostMaterials, ghostValid ? validColor : invalidColor);

        if (Input.GetMouseButtonDown(0) && ghostValid)
        {
            ConfirmPlacement();
        }
    }

    void CancelPlacement()
    {
        if (ghost != null) Destroy(ghost);
        ghost = null;
        ghostMaterials = null;
        phase = GamePhase.Landing;
        SetHint("Right-click: move    B: build a campfire (near you)");
    }

    bool IsValidCampfireSpot(Vector3 pos)
    {
        // Dry land only. With terrain: above water on gentle ground; legacy
        // flat world: inside the square the ocean frame surrounds
        if (TerrainGrid.Instance != null)
        {
            if (!TerrainGrid.Instance.IsBuildable(pos)) return false;
        }
        else if (Mathf.Abs(pos.x) > dryLandExtent || Mathf.Abs(pos.z) > dryLandExtent)
        {
            return false;
        }

        // Must be near the character — they build it where they stand
        if (player != null)
        {
            float dx = pos.x - player.transform.position.x;
            float dz = pos.z - player.transform.position.z;
            if (dx * dx + dz * dz > maxPlaceDistance * maxPlaceDistance)
                return false;
        }

        // Must be on (or very near) the NavMesh
        NavMeshHit hit;
        if (!NavMesh.SamplePosition(pos, out hit, 1f, NavMesh.AllAreas))
            return false;

        // Clear of resource nodes (they spawned before the campfire existed,
        // so the campfire keeps its own distance instead of the reverse)
        float clearanceSqr = minResourceClearance * minResourceClearance;
        var nodes = ResourceNode.ActiveList;
        for (int i = 0; i < nodes.Count; i++)
        {
            ResourceNode node = nodes[i];
            if (node == null) continue;
            float dx = pos.x - node.transform.position.x;
            float dz = pos.z - node.transform.position.z;
            if (dx * dx + dz * dz < clearanceSqr)
                return false;
        }

        return true;
    }

    void ConfirmPlacement()
    {
        Destroy(ghost);
        ghost = null;
        ghostMaterials = null;

        SpawnCampfire(ghostTarget);

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayBuildingPlaced();
        }

        // Send the character to the fire. The campfire is a carving obstacle, so
        // the destination must be the carve-safe approach point, not the center.
        if (player != null && placedCampfire != null)
        {
            Vector3 approach = TargetingUtil.GetApproachPoint(
                player.transform.position, placedCampfire.transform, placedCampfireCollider);
            player.MoveTo(approach);
        }

        settleDeadline = Time.time + settleTimeoutSeconds;
        phase = GamePhase.Settling;
        SetHint("A fire at last. Warmth. A beginning.");
    }

    // ------------------------------------------------------------------
    // Settling → Colony
    // ------------------------------------------------------------------

    void UpdateSettling()
    {
        bool arrived = false;

        if (player == null || placedCampfire == null)
        {
            arrived = true;  // nothing to wait for
        }
        else
        {
            // Edge distance, never center distance, against a carving obstacle
            float edge = TargetingUtil.EdgeDistance(
                player.transform.position, placedCampfire.transform, placedCampfireCollider);
            if (edge <= settleEdgeDistance) arrived = true;
        }

        if (arrived || Time.time >= settleDeadline)
        {
            StartColony();
        }
    }

    void StartColony()
    {
        phase = GamePhase.Colony;

        if (dayNight != null) dayNight.clockPaused = false;
        if (buildPlacement != null) buildPlacement.enabled = true;

        // The character stays yours. Colonists come ashore on their own while
        // the campfire has room (PopulationManager's arrival timer opens the
        // moment IntroInProgress drops).
        SetHint("The fire is lit, " + PlayerProfile.Name + ". Survivors will come ashore while it has room.\n"
            + "Right-click the fire to deposit what you carry · click it to give jobs    B: build    Space: find yourself");
        hintFadeStart = Time.time + 12f;

        OnColonyStarted?.Invoke();
        Debug.Log("GameStartController: Campfire lit — the colony begins.");
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>
    /// Debug-menu hook: instantly finish the opening — campfire placed (at
    /// the character if they're on dry land, else the skipIntro position) and
    /// the colony started. No-op once the colony is running.
    /// </summary>
    public void DebugForceColonyStart()
    {
        if (phase == GamePhase.Colony) return;
        StartCoroutine(DebugForceColonyRoutine());
    }

    IEnumerator DebugForceColonyRoutine()
    {
        // Still on the name popup? Take the last name and dismiss it. The sim
        // never shows the popup and must never write PlayerPrefs.
        if (!SimHooks.Simulating && !PlayerProfile.HasActive)
        {
            PlayerProfile.BeginRun(PlayerProfile.LastName);
        }
        if (MenuScreens.Instance != null && MenuScreens.Instance.Current == MenuScreens.Screen.NameEntry)
        {
            MenuScreens.Instance.Close();
        }

        // Drop any in-progress placement ghost
        if (ghost != null)
        {
            Destroy(ghost);
            ghost = null;
            ghostMaterials = null;
        }

        if (placedCampfire == null)  // Settling phase already has one
        {
            Vector3 pos = skipIntroCampfirePosition;
            if (player != null)
            {
                Vector3 sp = player.transform.position;
                bool onGoodGround = TerrainGrid.Instance != null
                    ? TerrainGrid.Instance.IsBuildable(sp)
                    : (Mathf.Abs(sp.x) <= dryLandExtent && Mathf.Abs(sp.z) <= dryLandExtent);
                if (onGoodGround)
                {
                    pos = GridSnap.SnapXZ(sp, cellSize) + new Vector3(2f, 0f, 0f);
                }
            }
            SpawnCampfire(pos);

            // Park in Settling (stops Landing/Placing input); the deadline is
            // pushed out so this routine — not UpdateSettling's timeout —
            // finishes the job. If the character happens to already be at the
            // fire, UpdateSettling starting the colony first is fine too.
            phase = GamePhase.Settling;
            settleDeadline = float.MaxValue;

            // BaseBuilding.Start (housing registration) must run before
            // anything assigns jobs, or the first assignment silently fails
            yield return null;
        }

        if (phase != GamePhase.Colony) StartColony();
    }
#endif

    // ------------------------------------------------------------------
    // Campfire spawning (shared by intro placement and skipIntro)
    // ------------------------------------------------------------------

    void SpawnCampfire(Vector3 position)
    {
        if (campfirePrefab == null)
        {
            Debug.LogError("GameStartController: campfirePrefab not assigned! Run Tools > Island RTS > Opening Sequence > Setup Opening Scene.");
            return;
        }

        // Terrain T2: level a pad for the fire (no-op on the already-flat origin disc)
        if (TerrainGrid.Instance != null)
        {
            TerrainGrid.Instance.FlattenArea(position, 2.2f, 1.6f);
        }
        position.y = TerrainGrid.Instance != null ? TerrainGrid.Instance.SampleHeight(position) : 0f;
        GameObject fire = Instantiate(campfirePrefab, position, Quaternion.identity);
        fire.name = "Campfire";

        placedCampfire = fire.GetComponent<BaseBuilding>();
        placedCampfireCollider = fire.GetComponent<Collider>();

        if (placedCampfire == null)
        {
            Debug.LogError("GameStartController: Campfire prefab has no BaseBuilding component! Re-run the Opening Sequence setup tool (it applies the scene campfire's components into the prefab).");
            return;
        }

        // Scene-only wiring the prefab can't carry
        placedCampfire.workerUI = workerUI;
        if (workerUI == null)
        {
            Debug.LogWarning("GameStartController: workerUI not assigned — clicking the campfire won't open the assignment panel. Re-run the setup tool.");
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.campfire = placedCampfire;
        }
        // else: GameManager.Start's FindAnyObjectByType fallback will find it
        // (skipIntro spawns in Awake, before GameManager.Start runs).
    }

    // ------------------------------------------------------------------
    // Player character + camera
    // ------------------------------------------------------------------

    void SpawnPlayer()
    {
        // The cove moves with the island size; the authored spawn point is
        // one metre east of the cove centre on the standard map
        Vector3 spawnPos = TerrainGrid.Instance != null
            ? TerrainGrid.Instance.CoveCenter + new Vector3(1f, 0f, 0f)
            : (survivorSpawnPoint != null ? survivorSpawnPoint.position : new Vector3(-69f, 0f, 3f));
        SpawnPlayerAt(spawnPos, Quaternion.Euler(0f, 90f, 0f));
    }

    /// <summary>skipIntro: the character starts beside the fire, facing it.</summary>
    void SpawnPlayerAtCampfire()
    {
        Vector3 pos = placedCampfire != null
            ? placedCampfire.GetValidSpawnPosition()
            : skipIntroCampfirePosition + new Vector3(2f, 0f, 0f);
        SpawnPlayerAt(pos, Quaternion.identity);
    }

    void SpawnPlayerAt(Vector3 spawnPos, Quaternion rotation)
    {
        if (playerPrefab == null)
        {
            Debug.LogError("GameStartController: playerPrefab not assigned! Run Tools > Island RTS > Opening Sequence > Setup Opening Scene.");
            return;
        }

        NavMeshHit hit;
        if (NavMesh.SamplePosition(spawnPos, out hit, 6f, NavMesh.AllAreas))
        {
            spawnPos = hit.position;
        }

        GameObject obj = Instantiate(playerPrefab, spawnPos, rotation);
        obj.name = "Player";
        player = obj.GetComponent<PlayerCharacter>();
        if (player == null)
        {
            Debug.LogError("GameStartController: player prefab has no PlayerCharacter component — re-run the Opening Sequence setup tool.");
        }
    }

    /// <summary>Open on the character. CameraController.CenterOn is rotation-agnostic and CameraShake-safe.</summary>
    void FrameCameraOnPlayer()
    {
        if (player == null) return;

        if (CameraController.Instance != null)
        {
            CameraController.Instance.CenterOn(player.transform.position);
            return;
        }

        // No controller in this scene: shift the raw camera the same way
        if (mainCam == null) return;
        Ray centerRay = mainCam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Plane ground = new Plane(Vector3.up, Vector3.zero);
        float dist;
        if (ground.Raycast(centerRay, out dist))
        {
            Vector3 viewCenter = centerRay.GetPoint(dist);
            Vector3 delta = player.transform.position - viewCenter;
            delta.y = 0f;
            mainCam.transform.position += delta;
        }
    }

    bool RaycastGround(out Vector3 point)
    {
        point = Vector3.zero;
        if (mainCam == null) return false;

        Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);

        // Physics first so clicks land on the actual terrain surface (a math
        // plane at y=0 would offset clicks on hills); Default layer = ground
        RaycastHit hitInfo;
        if (Physics.Raycast(ray, out hitInfo, 1000f, 1))
        {
            point = hitInfo.point;
            return true;
        }

        // Fallback: plane at sea level (off-map clicks, legacy flat world)
        Plane ground = new Plane(Vector3.up, Vector3.zero);
        float dist;
        if (ground.Raycast(ray, out dist))
        {
            point = ray.GetPoint(dist);
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------
    // Hint UI (runtime-created overlay, zero scene wiring)
    // ------------------------------------------------------------------

    void CreateHintUI()
    {
        if (SimHooks.Simulating) return;

        hintCanvasObj = new GameObject("IntroHintCanvas");
        Canvas canvas = hintCanvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the player's inventory strip (PlayerHUD builds its canvas at 45) - the
        // hint used to sort under it AND sit at the same height, so the opening
        // instructions were half-hidden behind the hotbar.
        canvas.sortingOrder = 70;

        CanvasScaler scaler = hintCanvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        GameObject textObj = new GameObject("HintText");
        textObj.transform.SetParent(hintCanvasObj.transform, false);

        hintText = textObj.AddComponent<TextMeshProUGUI>();
        hintText.fontSize = 26f;
        hintText.alignment = TextAlignmentOptions.Center;
        hintText.color = new Color(1f, 0.96f, 0.85f, 1f);
        hintText.raycastTarget = false;

        RectTransform rt = hintText.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        // Clear of the inventory strip: that bar is ~100 reference px tall sitting 14 up
        // from the bottom edge, so the hint starts above it rather than behind it.
        rt.anchoredPosition = new Vector2(0f, 150f);
        rt.sizeDelta = new Vector2(1400f, 110f);
    }

    void SetHint(string text)
    {
        if (hintText != null)
        {
            hintText.text = text;
            hintText.alpha = 1f;
        }
    }

    void UpdateHintFade()
    {
        if (hintCanvasObj == null || hintFadeStart < 0f) return;

        float t = Time.time - hintFadeStart;
        if (t < 0f) return;

        float alpha = 1f - t / HintFadeDuration;
        if (alpha <= 0f)
        {
            Destroy(hintCanvasObj);
            hintCanvasObj = null;
            hintText = null;
            hintFadeStart = -1f;
        }
        else if (hintText != null)
        {
            hintText.alpha = alpha;
        }
    }
}
