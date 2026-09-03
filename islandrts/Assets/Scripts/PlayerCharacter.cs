using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The player's own character: the castaway who wades ashore in the opening and
/// stays under direct control for the whole run (2026-09-02).
///
/// Right-click is a smart command (see <see cref="HandleCommandClick"/>): a
/// ground pickup is fetched into the character's <see cref="Inventory"/>, the
/// campfire is walked to and everything is deposited there (resources into the
/// pool, materials into the campfire stockpile), anything else is a move.
///
/// Deliberately NOT a colonist: never in the PopulationManager roster, takes no
/// housing, holds no job, has no AIBrain. Enemies do not target them (the enemy
/// priority list never scans this registry, exactly as it never scanned workers).
///
/// Has Health, but death is a knock-out, not a loss: at 0 HP the body hides for
/// <see cref="knockoutSeconds"/> and then stands back up beside the campfire at
/// full health. Losing the campfire stays the only defeat. Health regenerates
/// slowly while standing near the fire.
///
/// GameStartController owns the opening-sequence input (right-click during the
/// intro goes through <see cref="CommandAt"/> too, so pickups can be gathered
/// before the fire exists); once the colony starts, this component reads the
/// right mouse button itself.
/// </summary>
public class PlayerCharacter : UnitBase<PlayerCharacter>
{
    public static PlayerCharacter Instance { get; private set; }

    public const int InventorySlots = 6;

    [Header("Character")]
    public float maxHealth = 75f;
    [Tooltip("HP per second while standing within regenRange of the campfire's edge.")]
    public float regenPerSecond = 2f;
    public float regenRange = 3f;
    [Tooltip("Seconds spent knocked out before standing back up at the campfire.")]
    public float knockoutSeconds = 10f;

    [Header("Movement")]
    public float moveSpeed = 3.5f;

    static readonly Color NameColor = new Color(1f, 0.85f, 0.4f);

    const float CollectDistance = 0.9f;      // same reach as CollectPickupExecutor
    const float DepositEdgeDistance = 1.5f;  // from the campfire collider edge
    const float FlashSeconds = 2.2f;
    const float StallSeconds = 0.5f;         // standing still with no path = arrived as close as we can get
    const float StallReach = 2.5f;           // ...and that counts if the target is within this

    // Command raycast: ground (Default), buildings, and the pickup click layer
    const int CommandMask = 1 | (1 << 6) | GroundPickup.ClickMask;

    // Destination retry: AINavHelper.TrySetDestination returns Unity's real
    // SetDestination result — a false means the NavMesh rejected the point
    // (recalc in progress, throttle) and we must retry next frame, never
    // pretend success (the "ghost moving" gotcha).
    private Vector3 pendingDestination;
    private bool hasPendingDestination;

    // The one thing the character is walking to do
    enum TaskKind { None, Collect, Deposit, Craft }
    private TaskKind task;
    private GroundPickup taskPickup;
    private BaseBuilding taskFire;
    private Collider taskFireCollider;
    private CraftingCatalog.Recipe taskRecipe;
    private float stallTimer;

    // Crafting in progress (standing at the fire). Costs are taken on completion,
    // so walking away mid-craft loses nothing.
    private CraftingCatalog.Recipe craftRecipe;
    private BaseBuilding craftFire;
    private float craftProgress;
    private int craftPercentShown = -1;
    private HeldItem heldItem;

    private readonly Inventory inventory = new Inventory(InventorySlots);

    // Knock-out state
    private bool knockedOut;
    private float reviveAt;
    private Renderer[] hiddenRenderers;

    // Label: "Name" on one line, an optional activity below it. Composed only
    // when either part changes so the per-frame path allocates nothing.
    private string activity = "";
    private float activityExpires = -1f;   // > 0: a short flash that clears itself
    private string composedLabel;
    private string composedName;
    private string composedActivity;

    private Camera mainCam;

    /// <summary>What the character carries. The HUD and the deposit logic read it; pickups write it.</summary>
    public Inventory Inventory => inventory;

    /// <summary>True while knocked out — no input, no interactions.</summary>
    public bool IsKnockedOut => knockedOut;

    /// <summary>The recipe being crafted right now, or null.</summary>
    public CraftingCatalog.Recipe ActiveRecipe => craftRecipe;

    /// <summary>The recipe the character is walking to the fire to craft, or null.</summary>
    public CraftingCatalog.Recipe QueuedRecipe => task == TaskKind.Craft ? taskRecipe : null;

    public float CraftProgress01 => craftRecipe == null ? 0f : Mathf.Clamp01(craftProgress / Mathf.Max(0.01f, craftRecipe.seconds));

    /// <summary>The tool shown in the character's hand (visual only).</summary>
    public ItemDef HeldTool => heldItem != null ? heldItem.Current : null;

    /// <summary>What the character is doing, shown under the name and on the HUD. Empty hides the line.</summary>
    public string Activity => activity;

    /// <summary>Set the activity line; with <paramref name="seconds"/> &gt; 0 it clears itself (a "+3 Stick" flash).</summary>
    public void SetActivity(string text, float seconds = 0f)
    {
        activity = text ?? "";
        activityExpires = seconds > 0f ? Time.time + seconds : -1f;
    }

    protected override void Awake()
    {
        base.Awake();
        Instance = this;

        // Health in Awake, not Start: the prefab carries a HealthBar whose Start
        // looks the Health up, and Start order between components on one object
        // is not something to lean on. AddComponent runs Health.Awake at once, so
        // the death event exists by the time the listener is added.
        // Death is a knock-out: SetupHealth defaults destroyOnDeath to true
        // because every unit dies for real; the player stands back up.
        SetupHealth(maxHealth, OnKnockedOut);
        healthComponent.destroyOnDeath = false;
        healthComponent.showHealthText = false;
    }

    void Start()
    {
        if (!FetchAgent()) return;

        // Same locomotion feel as a Worker (see Worker.Start)
        agent.speed = moveSpeed;
        agent.acceleration = 18f;
        agent.angularSpeed = 360f;
        agent.stoppingDistance = 0.2f;
        agent.radius = Worker.AgentRadius;
        agent.baseOffset = 0f;  // base-pivot art: transform origin IS the feet
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;

        CreateStateText(2.2f, PlayerProfile.Name, NameColor);
        if (floatingText != null) floatingText.alwaysShow = true;   // the name never hides with the state-label setting

        mainCam = Camera.main;
        heldItem = GetComponent<HeldItem>();
        PlayerHUD.Ensure();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        ReleaseClaim();
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (activityExpires > 0f && Time.time >= activityExpires) SetActivity("");
        RefreshLabel();

        if (knockedOut)
        {
            if (Time.time >= reviveAt) Revive();
            return;
        }

        if (hasPendingDestination) TryIssueMove();

        UpdateTask();
        UpdateCraft();
        RegenNearCampfire();

        // Right-click commands, once the colony is running. During the intro the
        // GameStartController owns the click (it also has to route B and Esc)
        // and forwards it to CommandAt.
        if (GameStartController.Phase == GamePhase.Colony
            && !PauseController.BlockGameplayInput
            && Input.GetMouseButtonDown(1))
        {
            HandleCommandClick();
        }
    }

    // ------------------------------------------------------------------
    // Commands
    // ------------------------------------------------------------------

    /// <summary>Right-click under the mouse: fetch a pickup, deposit at the fire, or walk there.</summary>
    public void HandleCommandClick()
    {
        if (mainCam == null) mainCam = Camera.main;
        if (mainCam == null) return;

        Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, 1000f, CommandMask, QueryTriggerInteraction.Ignore))
        {
            CommandAt(hit.collider, hit.point);
            return;
        }

        // Off the world: plane at sea level (legacy flat world, off-map clicks)
        Plane ground = new Plane(Vector3.up, Vector3.zero);
        float dist;
        if (ground.Raycast(ray, out dist)) MoveTo(ray.GetPoint(dist));
    }

    /// <summary>
    /// Interpret a right-click on <paramref name="hitCollider"/> at <paramref name="point"/>.
    /// Public so the opening sequence can route its clicks here.
    /// </summary>
    public void CommandAt(Collider hitCollider, Vector3 point)
    {
        if (knockedOut) return;
        CancelCraft();   // any new order interrupts a craft (nothing has been paid yet)

        if (hitCollider != null)
        {
            GroundPickup pickup = hitCollider.GetComponentInParent<GroundPickup>();
            if (pickup != null) { CommandCollect(pickup); return; }

            BaseBuilding fire = hitCollider.GetComponentInParent<BaseBuilding>();
            if (fire != null) { CommandDeposit(fire); return; }
        }

        ClearTask();
        MoveTo(point);
    }

    void CommandCollect(GroundPickup pickup)
    {
        ClearTask();

        if (pickup.IsClaimedByOther(this))
        {
            SetActivity("Someone is fetching that", FlashSeconds);
            return;
        }
        if (inventory.SpaceFor(pickup.Item) <= 0)
        {
            SetActivity("Hands full — deposit at the fire", FlashSeconds);
            return;
        }

        task = TaskKind.Collect;
        taskPickup = pickup;
        pickup.claimedBy = this;
        stallTimer = 0f;
        SetActivity("Fetching " + pickup.Item.displayName);
        MoveTo(pickup.transform.position);
    }

    void CommandDeposit(BaseBuilding fire)
    {
        ClearTask();

        task = TaskKind.Deposit;
        taskFire = fire;
        taskFireCollider = fire.GetComponent<Collider>();
        stallTimer = 0f;
        SetActivity(inventory.IsEmpty ? "Going to the fire" : "Carrying to the fire");

        // Carve-safe approach point, never the centre (the campfire carves the NavMesh)
        MoveTo(TargetingUtil.GetApproachPoint(transform.position, fire.transform, taskFireCollider));
    }

    void UpdateTask()
    {
        switch (task)
        {
            case TaskKind.Collect:
            {
                if (taskPickup == null) { ClearTask(); return; }   // someone else took it

                float dist = Vector3.Distance(transform.position, taskPickup.transform.position);
                if (dist <= CollectDistance || (Stalled() && dist <= StallReach))
                {
                    if (agent != null && agent.isOnNavMesh) agent.ResetPath();
                    GroundPickup p = taskPickup;
                    ClearTask();

                    int taken = p.CollectAsItem(inventory);
                    if (taken > 0) SetActivity("+" + taken + " " + p.Item.displayName, FlashSeconds);
                    else SetActivity("Hands full — deposit at the fire", FlashSeconds);
                }
                else if (Stalled())
                {
                    ClearTask();
                    SetActivity("Can't reach that", FlashSeconds);
                }
                break;
            }

            case TaskKind.Deposit:
            {
                if (taskFire == null) { ClearTask(); return; }

                float edge = TargetingUtil.EdgeDistance(transform.position, taskFire.transform, taskFireCollider);
                if (edge <= DepositEdgeDistance || (Stalled() && edge <= StallReach))
                {
                    if (agent != null && agent.isOnNavMesh) agent.ResetPath();
                    BaseBuilding fire = taskFire;
                    ClearTask();
                    DepositAll(fire);

                    // The click on the fire is also the "open the campfire" gesture
                    if (!PauseController.BlockGameplayInput)
                    {
                        WorkerAssignmentUI ui = fire.workerUI != null ? fire.workerUI : WorkerAssignmentUI.Instance;
                        if (ui != null) ui.OpenPanel(fire);
                    }
                }
                else if (Stalled())
                {
                    ClearTask();
                    SetActivity("Can't reach the fire", FlashSeconds);
                }
                break;
            }

            case TaskKind.Craft:
            {
                if (taskFire == null || taskRecipe == null) { ClearTask(); return; }

                float edge = TargetingUtil.EdgeDistance(transform.position, taskFire.transform, taskFireCollider);
                if (edge <= DepositEdgeDistance || (Stalled() && edge <= StallReach))
                {
                    if (agent != null && agent.isOnNavMesh) agent.ResetPath();
                    BaseBuilding fire = taskFire;
                    CraftingCatalog.Recipe recipe = taskRecipe;
                    ClearTask();
                    BeginCraft(recipe, fire);
                }
                else if (Stalled())
                {
                    ClearTask();
                    SetActivity("Can't reach the fire", FlashSeconds);
                }
                break;
            }
        }
    }

    // ------------------------------------------------------------------
    // Crafting at the fire
    // ------------------------------------------------------------------

    /// <summary>
    /// Craft <paramref name="recipe"/> at <paramref name="fire"/>: start at once
    /// if the character is already there, otherwise walk over and start on
    /// arrival. False (with a HUD flash) when it cannot be afforded or is done.
    /// </summary>
    public bool TryQueueCraft(CraftingCatalog.Recipe recipe, BaseBuilding fire)
    {
        if (recipe == null || fire == null || knockedOut) return false;
        if (recipe.crafted)
        {
            SetActivity("Already made", FlashSeconds);
            return false;
        }
        if (!CraftingCatalog.CanAfford(recipe, inventory, fire.Stockpile))
        {
            SetActivity("Missing materials for " + recipe.title, FlashSeconds);
            return false;
        }

        CancelCraft();
        ClearTask();

        Collider fireCollider = fire.GetComponent<Collider>();
        float edge = TargetingUtil.EdgeDistance(transform.position, fire.transform, fireCollider);
        if (edge <= DepositEdgeDistance)
        {
            BeginCraft(recipe, fire);
            return true;
        }

        task = TaskKind.Craft;
        taskFire = fire;
        taskFireCollider = fireCollider;
        taskRecipe = recipe;
        stallTimer = 0f;
        SetActivity("Going to the fire to craft");
        MoveTo(TargetingUtil.GetApproachPoint(transform.position, fire.transform, fireCollider));
        return true;
    }

    void BeginCraft(CraftingCatalog.Recipe recipe, BaseBuilding fire)
    {
        craftRecipe = recipe;
        craftFire = fire;
        craftProgress = 0f;
        craftPercentShown = -1;
        if (agent != null && agent.isOnNavMesh) agent.ResetPath();
        hasPendingDestination = false;
    }

    void UpdateCraft()
    {
        if (craftRecipe == null) return;

        if (craftFire == null || (craftFire.CachedHealth != null && !craftFire.CachedHealth.IsAlive))
        {
            CancelCraft();
            SetActivity("The fire is gone", FlashSeconds);
            return;
        }

        craftProgress += Time.deltaTime;

        // Only rebuild the label when the whole-percent changes (no per-frame string)
        int pct = Mathf.Min(99, Mathf.FloorToInt(CraftProgress01 * 100f));
        if (pct != craftPercentShown)
        {
            craftPercentShown = pct;
            SetActivity("Crafting " + craftRecipe.title + "  " + pct + "%");
        }

        if (craftProgress >= craftRecipe.seconds) CompleteCraft();
    }

    void CompleteCraft()
    {
        CraftingCatalog.Recipe recipe = craftRecipe;
        BaseBuilding fire = craftFire;
        craftRecipe = null;
        craftFire = null;
        craftProgress = 0f;

        // Re-checked at the end: a colonist may have spent the wood meanwhile
        if (!CraftingCatalog.Pay(recipe, inventory, fire.Stockpile))
        {
            SetActivity("Missing materials for " + recipe.title, FlashSeconds);
            return;
        }

        recipe.crafted = true;

        // The tool goes to the hands; overflow to the stockpile; if neither has
        // room it still exists as knowledge, which is what the colony needed
        if (recipe.output != null)
        {
            int left = recipe.outputCount - inventory.Add(recipe.output, recipe.outputCount);
            if (left > 0) fire.Stockpile.Add(recipe.output, left);
            if (heldItem != null && recipe.output.kind == ItemKind.Tool) heldItem.Equip(recipe.output);
        }

        for (int i = 0; i < recipe.unlocks.Length; i++) Unlocks.Grant(recipe.unlocks[i]);

        SetActivity("Crafted " + recipe.title, FlashSeconds);
        if (AudioManager.Instance != null) AudioManager.Instance.PlayBuildingPlaced();
        Debug.Log("Crafted " + recipe.title + " — " + recipe.description);   // once per recipe per run
    }

    void CancelCraft()
    {
        if (craftRecipe == null) return;
        craftRecipe = null;
        craftFire = null;
        craftProgress = 0f;
        craftPercentShown = -1;
        SetActivity("");
    }

    /// <summary>
    /// Standing still with nothing queued for a moment: the agent has gone as
    /// far as the NavMesh lets it. Lets a pickup on a ragged shore edge still
    /// be taken, and stops "Fetching" from hanging forever on one it cannot reach.
    /// </summary>
    bool Stalled()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return false;
        if (hasPendingDestination || agent.pathPending) { stallTimer = 0f; return false; }
        if (agent.hasPath && agent.remainingDistance > agent.stoppingDistance + 0.1f) { stallTimer = 0f; return false; }
        if (agent.velocity.sqrMagnitude > 0.01f) { stallTimer = 0f; return false; }

        stallTimer += Time.deltaTime;
        return stallTimer >= StallSeconds;
    }

    /// <summary>
    /// Everything in hand goes to the fire: resources into the pool, materials
    /// and tools into the campfire stockpile (what fits — the rest stays in hand).
    /// </summary>
    public void DepositAll(BaseBuilding fire)
    {
        if (fire == null) return;

        int deposited = 0;
        bool stockpileFull = false;

        for (int i = 0; i < inventory.SlotCount; i++)
        {
            Inventory.Slot slot = inventory[i];
            if (slot.IsEmpty) continue;

            if (slot.item.kind == ItemKind.Tool) continue;   // tools stay in your hands

            if (slot.item.kind == ItemKind.Resource)
            {
                if (ResourceManager.Instance == null) continue;
                ResourceManager.Instance.Add(slot.item.resourceType, slot.count);
                inventory.TakeSlot(i);
                deposited += slot.count;
            }
            else
            {
                int put = fire.Stockpile.Add(slot.item, slot.count);
                if (put > 0)
                {
                    inventory.Remove(slot.item, put);
                    deposited += put;
                }
                if (put < slot.count) stockpileFull = true;
            }
        }

        if (stockpileFull) SetActivity("Stockpile full", FlashSeconds);
        else if (deposited > 0) SetActivity("Deposited", FlashSeconds);
        else SetActivity("");
    }

    void ClearTask()
    {
        ReleaseClaim();
        task = TaskKind.None;
        taskPickup = null;
        taskFire = null;
        taskFireCollider = null;
        taskRecipe = null;
        stallTimer = 0f;
        if (activityExpires < 0f) SetActivity("");   // keep a flash, drop a "Fetching…"
    }

    void ReleaseClaim()
    {
        if (taskPickup != null && taskPickup.claimedBy == this) taskPickup.claimedBy = null;
    }

    // ------------------------------------------------------------------
    // Movement
    // ------------------------------------------------------------------

    /// <summary>
    /// Walk somewhere. The point is snapped to the NavMesh (generous 4u radius —
    /// clicks in the shallows land on the nearest walkable spot instead of
    /// being ignored). Does not touch the current task; commands clear it first.
    /// </summary>
    public void MoveTo(Vector3 worldPos)
    {
        if (knockedOut) return;
        CancelCraft();

        NavMeshHit hit;
        if (NavMesh.SamplePosition(worldPos, out hit, 4f, NavMesh.AllAreas))
        {
            pendingDestination = hit.position;
            hasPendingDestination = true;
            stallTimer = 0f;
            TryIssueMove();
        }
    }

    void TryIssueMove()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        if (AINavHelper.TrySetDestination(agent, pendingDestination))
        {
            agent.isStopped = false;
            hasPendingDestination = false;
        }
        // else: rejected — keep the flag, retry next frame
    }

    // ------------------------------------------------------------------
    // Health: regen at the fire, knock-out instead of death
    // ------------------------------------------------------------------

    void RegenNearCampfire()
    {
        if (healthComponent == null || !healthComponent.IsAlive) return;
        if (healthComponent.currentHealth >= healthComponent.maxHealth) return;

        BaseBuilding fire = AliveCampfire();
        if (fire == null) return;

        // Edge distance, never centre distance, against a carving obstacle
        float edge = TargetingUtil.EdgeDistance(transform.position, fire.transform, fire.GetComponent<Collider>());
        if (edge <= regenRange)
        {
            healthComponent.Heal(regenPerSecond * Time.deltaTime);
        }
    }

    void OnKnockedOut()
    {
        if (knockedOut) return;
        knockedOut = true;
        reviveAt = Time.time + knockoutSeconds;
        hasPendingDestination = false;
        CancelCraft();
        ClearTask();
        SetActivity("Knocked out");

        // Hide the body but keep the label (the name + "Knocked out" is how the
        // player finds out what happened). Same approach as Worker.SetGarrisoned.
        hiddenRenderers = GetComponentsInChildren<Renderer>(false);
        for (int i = 0; i < hiddenRenderers.Length; i++)
        {
            if (hiddenRenderers[i] == null) continue;
            if (floatingText != null && hiddenRenderers[i].transform.parent == transform
                && hiddenRenderers[i].gameObject.name == "FloatingText") continue;
            hiddenRenderers[i].enabled = false;
        }

        if (agent != null)
        {
            if (agent.isOnNavMesh) agent.ResetPath();
            agent.enabled = false;
        }

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
    }

    void Revive()
    {
        knockedOut = false;
        SetActivity("");

        // Back on your feet beside the fire (or where you fell, if there is none)
        BaseBuilding fire = AliveCampfire();
        if (fire != null)
        {
            transform.position = fire.GetValidSpawnPosition();
        }

        if (hiddenRenderers != null)
        {
            for (int i = 0; i < hiddenRenderers.Length; i++)
                if (hiddenRenderers[i] != null) hiddenRenderers[i].enabled = true;
            hiddenRenderers = null;
        }

        if (agent != null)
        {
            agent.enabled = true;
            if (agent.isOnNavMesh) agent.Warp(transform.position);
        }

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = true;

        // Health.Heal refuses the dead, so the revive writes the field directly.
        if (healthComponent != null) healthComponent.currentHealth = healthComponent.maxHealth;
    }

    static BaseBuilding AliveCampfire()
    {
        var list = BaseBuilding.ActiveList;
        for (int i = 0; i < list.Count; i++)
        {
            BaseBuilding b = list[i];
            if (b == null || !b.enabled) continue;
            if (b.CachedHealth != null && !b.CachedHealth.IsAlive) continue;
            return b;
        }
        return null;
    }

    // ------------------------------------------------------------------
    // Label
    // ------------------------------------------------------------------

    void RefreshLabel()
    {
        if (floatingText == null) return;

        string name = PlayerProfile.Name;
        if (name != composedName || activity != composedActivity)
        {
            composedName = name;
            composedActivity = activity;
            composedLabel = activity.Length == 0
                ? name
                : name + "\n<size=70%><color=#FFFFFFCC>" + activity + "</color></size>";
        }

        floatingText.SetText(composedLabel, NameColor);
    }
}
