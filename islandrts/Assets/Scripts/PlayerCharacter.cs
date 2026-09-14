using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The player's own character: the castaway who wades ashore in the opening and
/// stays under direct control for the whole run (2026-09-02).
///
/// Right-click is a smart command (see <see cref="HandleCommandClick"/>): a
/// ground pickup is fetched into the character's <see cref="Inventory"/>, the
/// campfire is walked to and everything is deposited there (resources into the
/// pool, materials into the campfire stockpile), a station is walked to and
/// worked (the character is the labor that moves its queue — see
/// <see cref="CraftStation"/>), anything else is a move.
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
    const float DepositEdgeDistance = 2.4f;  // from the campfire collider edge - generous, the fire is a big warm target
    const float DepositClickRadius = 3.5f;   // ground clicks this close to the fire edge count as "deposit"
    const float FlashSeconds = 2.2f;
    const float StallSeconds = 0.5f;         // standing still with no path = arrived as close as we can get
    const float StallReach = 2.5f;           // ...and that counts if the target is within this

    // Command raycast: ground (Default), buildings, the pickup click layer, and the
    // resource-node click layer (2026-09-03: hand-harvesting bushes, trees and rocks)
    const int CommandMask = 1 | (1 << 6) | GroundPickup.ClickMask | ResourceNode.ClickMask;

    // Hand-harvest: slower than a worker with a tool, and generous enough that
    // clearing one bush is a few seconds rather than a chore.
    // Building by hand (2026-09-10): the castaway is labour on a construction
    // site exactly like a jobless colonist, so a colony that has put every
    // colonist on a job can still raise its first hut. Reach is a shade more
    // generous than BuildExecutor's 1.2 because the player aims with a mouse.
    const float BuildEdgeDistance = 1.7f;
    const float BuildDriftSlack = 0.9f;
    /// <summary>Seconds of labour per second standing at a site - one colonist's worth.</summary>
    const float BuildLaborRate = 1f;

    const float HarvestPerSecond = 1.2f;
    /// <summary>Resource units harvested by hand per material that comes off with them.</summary>
    const float HarvestByproductEvery = 3f;

    // Destination retry: AINavHelper.TrySetDestination returns Unity's real
    // SetDestination result — a false means the NavMesh rejected the point
    // (recalc in progress, throttle) and we must retry next frame, never
    // pretend success (the "ghost moving" gotcha).
    private Vector3 pendingDestination;
    private bool hasPendingDestination;

    // The one thing the character is walking to do
    enum TaskKind { None, Collect, Deposit, Work, Harvest, Build }
    private TaskKind task;
    private GroundPickup taskPickup;
    private BaseBuilding taskFire;
    private Collider taskFireCollider;
    private CraftStation taskStation;
    private ResourceNode taskNode;
    private ConstructionSite taskSite;
    private Collider taskSiteCollider;
    private float stallTimer;

    // A ground click beside the fire (2026-09-13): walk to the clicked spot and
    // empty the hands on the way past, instead of parking at the fire's edge.
    private bool taskWalkOn;
    private bool taskDeposited;

    // A plain walk, so the queue knows when it is over
    private bool walking;
    private float walkStartedAt;

    // Queued orders (2026-09-13): Shift + right-click appends instead of replacing.
    // Whatever the character is doing runs to its end (a collect, a deposit, a
    // node emptied or the hands filled, a queue run dry, a site finished, a walk
    // arrived, or a "can't reach") and the next order starts by itself. A plain
    // right-click, or any command the UI or the sim issues, clears the queue.
    enum OrderKind { Move, Collect, Harvest, Build, Deposit, DepositNear, Work }
    struct Order
    {
        public OrderKind kind;
        public GroundPickup pickup;
        public ResourceNode node;
        public ConstructionSite site;
        public BaseBuilding fire;
        public CraftStation station;
        public Vector3 point;
    }
    private readonly List<Order> orders = new List<Order>(MaxOrders);
    private const int MaxOrders = 12;
    private bool dispatching;   // an order is being started from the queue: do not clear it

    /// <summary>Orders waiting behind the current one (the HUD shows "+N").</summary>
    public int QueuedOrders => orders.Count;

    // Harvesting a node by hand (standing at it). The node owns its own depletion;
    // this side owns the fractional accumulators that turn units into whole items.
    private ResourceNode harvestNode;
    private float harvestProgress;
    private float harvestByproduct;
    private float nextHarvestPulse;

    // Working a station's queue (standing at its bench). The station owns the
    // progress and charges costs on completion, so walking away loses nothing.
    // Building a construction site (standing at it). The site owns the progress;
    // this side only hands it labour while the character stands there.
    private ConstructionSite buildSite;
    private Collider buildSiteCollider;
    private int buildPercentShown = -1;

    private CraftStation workStation;
    private string workTitleShown;
    private int workPercentShown = -1;
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
    private int composedQueued = -1;
    private string activityShown = "";     // activity plus a "+N" when orders wait

    private Camera mainCam;

    /// <summary>What the character carries. The HUD and the deposit logic read it; pickups write it.</summary>
    public Inventory Inventory => inventory;

    /// <summary>True while knocked out — no input, no interactions.</summary>
    public bool IsKnockedOut => knockedOut;

    /// <summary>The station the character is standing at and working, or null.</summary>
    public CraftStation WorkingStation => workStation;

    /// <summary>The construction site the character is standing at and building, or null.</summary>
    public ConstructionSite BuildingSite => buildSite;

    /// <summary>The station the character is walking to in order to work, or null.</summary>
    public CraftStation WalkingToStation => task == TaskKind.Work ? taskStation : null;

    /// <summary>Walking somewhere with a purpose (fetching, depositing, heading to a bench).</summary>
    public bool HasTask => task != TaskKind.None;

    /// <summary>Anything under way that a queued order must wait behind.</summary>
    bool Busy => task != TaskKind.None || walking || harvestNode != null || workStation != null || buildSite != null;

    /// <summary>The tool shown in the character's hand (visual only).</summary>
    public ItemDef HeldTool => heldItem != null ? heldItem.Current : null;

    /// <summary>What the character is doing, shown under the name and on the HUD, with a "+N" while orders wait. Empty hides the line.</summary>
    public string Activity => activityShown;

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

        // The castaway sees from the first frame of the landing: the cove is the first
        // patch of the island the fog gives up (2026-09-09).
        VisionSource.Attach(gameObject, VisionSource.PlayerRadius);

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
        CombatHUD.Ensure();   // shows itself once the first warrior exists
        Minimap.Ensure();
        PlayerClickMarker.Attach(agent);   // the click ring and the path trail (no-op headless)
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
        UpdateWalk();
        UpdateHarvest();
        UpdateWork();
        UpdateBuild();
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

    /// <summary>
    /// Right-click under the mouse: fetch a pickup, deposit at the fire, or walk
    /// there. With the queue key held (Shift) the order waits behind the current one.
    /// </summary>
    public void HandleCommandClick()
    {
        if (Minimap.PointerOver) return;   // a click on the map moves the camera, not the castaway
        if (mainCam == null) mainCam = Camera.main;
        if (mainCam == null) return;

        bool queue = KeyBindings.Held(KeyBindings.Action.QueueCommand);

        Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, 1000f, CommandMask, QueryTriggerInteraction.Ignore))
        {
            CommandAt(hit.collider, hit.point, queue);
            return;
        }

        // Off the world: plane at sea level (legacy flat world, off-map clicks)
        Plane ground = new Plane(Vector3.up, Vector3.zero);
        float dist;
        if (ground.Raycast(ray, out dist)) Issue(new Order { kind = OrderKind.Move, point = ray.GetPoint(dist) }, queue);
    }

    /// <summary>
    /// Interpret a right-click on <paramref name="hitCollider"/> at <paramref name="point"/>.
    /// Public so the opening sequence can route its clicks here. With
    /// <paramref name="queue"/> the order waits behind whatever is under way.
    /// </summary>
    public void CommandAt(Collider hitCollider, Vector3 point, bool queue = false)
    {
        if (knockedOut) return;
        Issue(Resolve(hitCollider, point), queue);
    }

    /// <summary>What a click on <paramref name="hitCollider"/> at <paramref name="point"/> means.</summary>
    Order Resolve(Collider hitCollider, Vector3 point)
    {
        Order o = new Order { point = point, kind = OrderKind.Move };
        if (hitCollider != null)
        {
            o.pickup = hitCollider.GetComponentInParent<GroundPickup>();
            if (o.pickup != null) { o.kind = OrderKind.Collect; return o; }

            o.node = hitCollider.GetComponentInParent<ResourceNode>();
            if (o.node != null) { o.kind = OrderKind.Harvest; return o; }

            // Before the BaseBuilding test: a site is not a finished building yet
            o.site = hitCollider.GetComponentInParent<ConstructionSite>();
            if (o.site != null) { o.kind = OrderKind.Build; return o; }

            o.fire = hitCollider.GetComponentInParent<BaseBuilding>();
            if (o.fire != null) { o.kind = OrderKind.Deposit; return o; }

            // Any other station (the Workshop): walk over and work its queue
            o.station = hitCollider.GetComponentInParent<CraftStation>();
            if (o.station != null) { o.kind = OrderKind.Work; return o; }
        }

        // A click on the ground right beside the fire is a deposit too
        // (2026-09-03). The campfire's collider is a 2x2 box under a wide,
        // flickering silhouette, so aiming at the fire itself was fiddly, and
        // missing it walked the character past the thing they were carrying to.
        // Since 2026-09-13 it is a deposit ON THE WAY to the clicked spot: the
        // character keeps walking to where you pointed and empties their hands
        // as they pass the fire, and with nothing to drop it is just a walk.
        BaseBuilding near = Factions.Player.Campfire;
        if (near != null)
        {
            float d = TargetingUtil.EdgeDistance(point, near.transform, near.GetComponent<Collider>());
            if (d <= DepositClickRadius) { o.fire = near; o.kind = OrderKind.DepositNear; }
        }
        return o;
    }

    /// <summary>Start an order now, or park it behind the current one.</summary>
    void Issue(Order o, bool queue)
    {
        if (knockedOut) return;
        PlayerClickMarker.Flash(o.point);

        if (queue && Busy)
        {
            if (orders.Count < MaxOrders) orders.Add(o);
            DevQuests.Signal("queue:add");
            RefreshLabel();
            return;
        }

        orders.Clear();
        Execute(o);
    }

    void Execute(Order o)
    {
        switch (o.kind)
        {
            case OrderKind.Collect: CommandCollect(o.pickup); break;
            case OrderKind.Harvest: CommandHarvest(o.node); break;
            case OrderKind.Build: CommandBuild(o.site); break;
            case OrderKind.Deposit: CommandDeposit(o.fire); break;
            case OrderKind.DepositNear: CommandDepositNear(o.fire, o.point); break;
            case OrderKind.Work: WorkAt(o.station); break;
            default: CommandMove(o.point); break;
        }
    }

    /// <summary>
    /// The current order is over (done, or given up): start the next queued one.
    /// Every place that ends an order calls this, and only those places — the
    /// Stop* methods are also what a NEW command calls first, so they must not.
    /// </summary>
    void Continue()
    {
        if (orders.Count == 0 || knockedOut) return;
        Order next = orders[0];
        orders.RemoveAt(0);
        DevQuests.Signal("queue:run");
        bool was = dispatching;   // an order that fails at once continues from inside Execute
        dispatching = true;
        try { Execute(next); }
        finally { dispatching = was; }
        RefreshLabel();
    }

    /// <summary>A command from outside the queue (a click, the UI, the sim) replaces whatever was queued.</summary>
    void NewOrder()
    {
        if (!dispatching && orders.Count > 0) orders.Clear();
    }

    /// <summary>Walk to a ground pickup and take it. Public for the balance sim's player driver.</summary>
    public void CommandCollect(GroundPickup pickup)
    {
        if (knockedOut || pickup == null) return;
        NewOrder();
        StopWork();
        StopHarvest();
        StopBuild();
        ClearTask();

        if (pickup.IsClaimedByOther(this))
        {
            SetActivity("Someone is fetching that", FlashSeconds);
            Continue();
            return;
        }
        if (inventory.SpaceFor(pickup.Item) <= 0)
        {
            SetActivity("Hands full — deposit at the fire", FlashSeconds);
            Continue();
            return;
        }

        task = TaskKind.Collect;
        taskPickup = pickup;
        pickup.claimedBy = this;
        stallTimer = 0f;
        SetActivity("Fetching " + pickup.Item.displayName);
        MoveTo(pickup.transform.position);
    }

    /// <summary>Walk to the fire and deposit everything in hand (then work its queue). Public for the sim's player driver.</summary>
    public void CommandDeposit(BaseBuilding fire)
    {
        if (knockedOut || fire == null) return;
        NewOrder();
        StopWork();
        StopHarvest();
        StopBuild();
        ClearTask();

        task = TaskKind.Deposit;
        taskFire = fire;
        taskFireCollider = fire.GetComponent<Collider>();
        stallTimer = 0f;
        SetActivity(inventory.IsEmpty ? "Going to the fire" : "Carrying to the fire");

        // Carve-safe approach point, never the centre (the campfire carves the NavMesh)
        MoveTo(TargetingUtil.GetApproachPoint(transform.position, fire.transform, taskFireCollider));
    }

    /// <summary>
    /// A ground click beside the fire (2026-09-13): walk to <paramref name="point"/>
    /// and empty the hands on the way past the fire's edge, or on arrival if the
    /// path never comes that close. With nothing to drop it is a plain walk — the
    /// character no longer parks facing the fire because you clicked near it.
    /// </summary>
    public void CommandDepositNear(BaseBuilding fire, Vector3 point)
    {
        if (knockedOut || fire == null) return;
        if (!HasDepositable())
        {
            CommandMove(point);
            return;
        }

        NewOrder();
        StopWork();
        StopHarvest();
        StopBuild();
        ClearTask();

        task = TaskKind.Deposit;
        taskWalkOn = true;
        taskDeposited = false;
        taskFire = fire;
        taskFireCollider = fire.GetComponent<Collider>();
        stallTimer = 0f;
        SetActivity("Carrying to the fire");
        MoveTo(point);
    }

    /// <summary>Anything in hand the fire takes (everything but a tool).</summary>
    bool HasDepositable()
    {
        for (int i = 0; i < inventory.SlotCount; i++)
        {
            Inventory.Slot slot = inventory[i];
            if (!slot.IsEmpty && slot.item.kind != ItemKind.Tool) return true;
        }
        return false;
    }

    /// <summary>A plain walk. Ends (and lets the queue continue) on arrival or when the NavMesh gives up.</summary>
    public void CommandMove(Vector3 point)
    {
        if (knockedOut) return;
        NewOrder();
        StopWork();
        StopHarvest();
        StopBuild();
        ClearTask();
        MoveTo(point);
        walking = hasPendingDestination || (agent != null && agent.isOnNavMesh && (agent.pathPending || agent.hasPath));
        walkStartedAt = Time.time;
        if (!walking) Continue();   // nowhere to walk (off the mesh): on to the next order
    }

    void UpdateWalk()
    {
        if (!walking) return;
        if (Time.time - walkStartedAt < 0.15f) return;   // the path request is still in flight
        if (!Arrived() && !Stalled()) return;
        walking = false;
        Continue();
    }

    /// <summary>The agent has reached its destination and has nothing queued.</summary>
    bool Arrived()
    {
        if (hasPendingDestination) return false;
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return true;
        if (agent.pathPending) return false;
        if (!agent.hasPath) return true;
        return agent.remainingDistance <= agent.stoppingDistance + 0.1f;
    }

    /// <summary>
    /// Walk to a resource node and work it by hand (2026-09-03). A bush gives food and
    /// the sticks that come off it, a tree wood and sticks, a rock stone and chunks -
    /// which is how the character gets crafting materials without waiting for the ground
    /// to trickle them in. Colonists with jobs are unaffected; this is the player's own
    /// pair of hands.
    /// </summary>
    /// <summary>
    /// The tool this node cannot be worked without, or null when hands will do.
    /// Food is deliberately free: berries are the one thing a castaway can gather
    /// on day one, so no research ever stands between the colony and its first meal.
    /// </summary>
    public static ItemDef ToolFor(ResourceNode node)
    {
        if (node == null) return null;
        switch (node.resourceType)
        {
            case ResourceNode.ResourceType.Wood: return ItemCatalog.StoneAxe;
            case ResourceNode.ResourceType.Stone: return ItemCatalog.StonePick;
            case ResourceNode.ResourceType.Metal: return ItemCatalog.MetalPick;
            default: return null;   // Food - bare hands
        }
    }

    public void CommandHarvest(ResourceNode node)
    {
        if (knockedOut || node == null) return;
        NewOrder();
        StopWork();
        StopHarvest();
        StopBuild();
        ClearTask();

        if (!node.HasResources())
        {
            SetActivity("Nothing left there", FlashSeconds);
            Continue();
            return;
        }

        // Bare hands strip a berry bush, and nothing else. A trunk needs an axe
        // and stone needs a pick, so the opening is: fetch the loose sticks and
        // chunks off the ground, deposit them, research, craft the tool - and
        // only then does the island's timber open up (2026-09-03).
        ItemDef tool = ToolFor(node);
        if (tool != null && inventory.Count(tool) <= 0)
        {
            SetActivity("Need a " + tool.displayName.ToLowerInvariant() + " for that", FlashSeconds);
            Continue();
            return;
        }

        if (inventory.SpaceFor(node.PrimaryItem) <= 0)
        {
            SetActivity("Hands full - deposit at the fire", FlashSeconds);
            Continue();
            return;
        }

        if (WithinHarvestReach(node))
        {
            BeginHarvest(node);
            return;
        }

        task = TaskKind.Harvest;
        taskNode = node;
        stallTimer = 0f;
        SetActivity("Going to the " + node.PrimaryItem.displayName.ToLowerInvariant());
        MoveTo(node.GetGatherPoint(transform.position));
    }

    /// <summary>
    /// Standing close enough to work a node. Measured to the node centre against its own
    /// gather ring, not to the collider edge: the node's box is a click hitbox sized off
    /// the art, while the ring is where the carve actually lets anyone stand.
    /// </summary>
    bool WithinHarvestReach(ResourceNode node)
    {
        Vector3 d = node.transform.position - transform.position;
        d.y = 0f;
        float reach = node.GatherRingRadius + 1.1f;
        return d.sqrMagnitude <= reach * reach;
    }

    void BeginHarvest(ResourceNode node)
    {
        harvestNode = node;
        harvestProgress = 0f;
        harvestByproduct = 0f;
        nextHarvestPulse = 0f;
        if (agent != null && agent.isOnNavMesh) agent.ResetPath();
        hasPendingDestination = false;
        SetActivity("Gathering " + node.PrimaryItem.displayName.ToLowerInvariant());
    }

    /// <summary>Stop working a node. The node keeps whatever is left in it.</summary>
    public void StopHarvest()
    {
        if (harvestNode == null) return;
        harvestNode = null;
        SetActivity("");
    }

    void UpdateHarvest()
    {
        if (harvestNode == null) return;

        if (!harvestNode.HasResources())
        {
            StopHarvest();
            SetActivity("Nothing left there", FlashSeconds);
            Continue();
            return;
        }
        if (!WithinHarvestReach(harvestNode))
        {
            // Knocked back, or the node shrank away from us - walk in again
            ResourceNode node = harvestNode;
            StopHarvest();
            CommandHarvest(node);
            return;
        }

        float units = harvestNode.GatherResources(HarvestPerSecond * Time.deltaTime, shedByproducts: false);
        if (units <= 0f) { StopHarvest(); Continue(); return; }

        // Feedback on the same beat a worker's chopping gives the node
        if (Time.time >= nextHarvestPulse)
        {
            nextHarvestPulse = Time.time + 0.35f;
            harvestNode.TriggerShakePulse();
        }

        harvestProgress += units;
        harvestByproduct += units;

        while (harvestProgress >= 1f)
        {
            harvestProgress -= 1f;
            if (inventory.Add(harvestNode.PrimaryItem, 1) <= 0)
            {
                StopHarvest();
                SetActivity("Hands full - deposit at the fire", FlashSeconds);
                Continue();   // typically the queued trip to the fire
                return;
            }
        }

        if (harvestByproduct >= HarvestByproductEvery)
        {
            harvestByproduct -= HarvestByproductEvery;
            // No room for the material is not a reason to stop taking the resource —
            // it falls at your feet instead, the same way a worker sheds one, so a
            // full inventory costs you the walk home and not the stick (2026-09-03).
            if (inventory.Add(harvestNode.ByproductItem, 1) <= 0) harvestNode.ShedOneByproduct();
        }
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
                    Continue();
                }
                else if (Stalled())
                {
                    ClearTask();
                    SetActivity("Can't reach that", FlashSeconds);
                    Continue();
                }
                break;
            }

            case TaskKind.Deposit:
            {
                if (taskFire == null) { ClearTask(); Continue(); return; }

                float edge = TargetingUtil.EdgeDistance(transform.position, taskFire.transform, taskFireCollider);

                if (taskWalkOn)
                {
                    // Passing the fire: empty the hands without breaking stride
                    if (!taskDeposited && edge <= DepositEdgeDistance)
                    {
                        taskDeposited = true;
                        DepositAll(taskFire);
                        DevQuests.Signal("deposit:walk_on");
                    }
                    if (Arrived() || Stalled())
                    {
                        BaseBuilding fire = taskFire;
                        bool done = taskDeposited;
                        ClearTask();
                        if (!done) DepositAll(fire);   // the click was within reach of the edge, so arrival counts
                        Continue();
                    }
                    break;
                }

                if (edge <= DepositEdgeDistance || (Stalled() && edge <= StallReach))
                {
                    if (agent != null && agent.isOnNavMesh) agent.ResetPath();
                    BaseBuilding fire = taskFire;
                    ClearTask();
                    DepositAll(fire);

                    // Already standing at the fire: if its bench has work, get on
                    // with it — unless more orders are waiting, which come first
                    if (orders.Count > 0) Continue();
                    else if (fire.Station != null && fire.Station.HasWork) BeginWork(fire.Station);

                    // Deliberately does NOT open the campfire panel (2026-09-03).
                    // The two gestures are separate: right-click is "go do that",
                    // left-click on the fire itself is "show me the fire". Opening a
                    // panel every time the character walked home to empty their hands
                    // covered the world with UI the player had not asked for.
                }
                else if (Stalled())
                {
                    ClearTask();
                    SetActivity("Can't reach the fire", FlashSeconds);
                    Continue();
                }
                break;
            }

            case TaskKind.Work:
            {
                if (taskStation == null || !taskStation.IsAlive) { ClearTask(); Continue(); return; }

                float edge = TargetingUtil.EdgeDistance(transform.position, taskStation.transform, taskStation.ApproachCollider);
                if (edge <= DepositEdgeDistance || (Stalled() && edge <= StallReach))
                {
                    if (agent != null && agent.isOnNavMesh) agent.ResetPath();
                    CraftStation station = taskStation;
                    ClearTask();
                    BeginWork(station);
                }
                else if (Stalled())
                {
                    ClearTask();
                    SetActivity("Can't reach the " + TaskStationName(), FlashSeconds);
                    Continue();
                }
                break;
            }

            case TaskKind.Build:
            {
                if (taskSite == null || taskSite.IsComplete) { ClearTask(); Continue(); return; }   // finished without us

                float siteEdge = TargetingUtil.EdgeDistance(transform.position, taskSite.transform, taskSiteCollider);
                if (siteEdge <= BuildEdgeDistance || (Stalled() && siteEdge <= StallReach))
                {
                    ConstructionSite arrived = taskSite;
                    ClearTask();
                    BeginBuild(arrived);
                }
                else if (Stalled())
                {
                    ClearTask();
                    SetActivity("Can't reach that site", FlashSeconds);
                    Continue();
                }
                break;
            }

            case TaskKind.Harvest:
            {
                if (taskNode == null) { ClearTask(); Continue(); return; }   // depleted while we walked

                bool close = WithinHarvestReach(taskNode);
                if (close || (Stalled() && Vector3.Distance(transform.position, taskNode.transform.position) <= StallReach + taskNode.GatherRingRadius))
                {
                    ResourceNode node = taskNode;
                    ClearTask();
                    BeginHarvest(node);
                }
                else if (Stalled())
                {
                    ClearTask();
                    SetActivity("Can't reach that", FlashSeconds);
                    Continue();
                }
                break;
            }
        }
    }

    string TaskStationName() => taskStation != null ? taskStation.displayName.ToLowerInvariant() : "bench";

    // ------------------------------------------------------------------
    // Building (2026-09-10): the character is labour on a construction site
    // ------------------------------------------------------------------

    /// <summary>
    /// Walk to a construction site and raise it. Public for the balance sim's
    /// player driver.
    ///
    /// A site advances only while somebody works it, and until now that somebody
    /// had to be a jobless colonist - so a colony that had put all three starting
    /// colonists on gathering jobs could never finish its first hut, never gain
    /// housing, and never get a jobless colonist back. The castaway's own hands
    /// are the way out of that corner. Deliberately does NOT take one of the
    /// site's builder slots: those are the colonists' queue.
    /// </summary>
    public void CommandBuild(ConstructionSite site)
    {
        if (knockedOut || site == null) return;
        NewOrder();
        StopWork();
        StopHarvest();
        StopBuild();
        ClearTask();

        if (site.IsComplete)
        {
            SetActivity("That one is finished", FlashSeconds);
            Continue();
            return;
        }

        Collider col = site.GetComponent<Collider>();
        if (TargetingUtil.EdgeDistance(transform.position, site.transform, col) <= BuildEdgeDistance)
        {
            BeginBuild(site);
            return;
        }

        task = TaskKind.Build;
        taskSite = site;
        taskSiteCollider = col;
        stallTimer = 0f;
        SetActivity("Going to build");
        // Sites carve the NavMesh, so the destination is the carve-safe approach point
        MoveTo(TargetingUtil.GetApproachPoint(transform.position, site.transform, col));
    }

    void BeginBuild(ConstructionSite site)
    {
        buildSite = site;
        buildSiteCollider = site.GetComponent<Collider>();
        buildPercentShown = -1;
        if (agent != null && agent.isOnNavMesh) agent.ResetPath();
        hasPendingDestination = false;
        DevQuests.Signal("build:player");
    }

    /// <summary>Leave the site. It keeps the progress it has, the same as a colonist walking off.</summary>
    public void StopBuild()
    {
        if (buildSite == null) return;
        buildSite = null;
        buildSiteCollider = null;
        buildPercentShown = -1;
        SetActivity("");
    }

    void UpdateBuild()
    {
        if (buildSite == null) return;

        if (buildSite.IsComplete)
        {
            string finished = buildSite.buildingType.ToString();
            StopBuild();
            SetActivity(finished + " finished", FlashSeconds);
            Continue();
            return;
        }

        // Shoved off the site (a crowd, a raider): walk back in, labour pauses meanwhile
        float edge = TargetingUtil.EdgeDistance(transform.position, buildSite.transform, buildSiteCollider);
        if (edge > BuildEdgeDistance + BuildDriftSlack)
        {
            ConstructionSite site = buildSite;
            StopBuild();
            CommandBuild(site);
            return;
        }

        // The site destroys itself the moment it completes, so remember what it
        // was before handing it the frame's labour (a destroyed object reads null).
        BuildingType raising = buildSite.buildingType;
        buildSite.AddLabor(BuildLaborRate * Time.deltaTime);
        if (buildSite == null || buildSite.IsComplete)
        {
            buildSite = null;
            buildSiteCollider = null;
            buildPercentShown = -1;
            SetActivity(raising + " finished", FlashSeconds);
            Continue();
            return;
        }

        int pct = Mathf.Min(99, Mathf.FloorToInt(buildSite.progress * 100f));
        if (pct != buildPercentShown)
        {
            buildPercentShown = pct;
            SetActivity("Building " + buildSite.buildingType + "  " + pct + "%");
        }
    }

    // ------------------------------------------------------------------
    // Working a station (2026-09-03): the character is labor on its queue
    // ------------------------------------------------------------------

    /// <summary>
    /// Queue <paramref name="count"/> of a recipe at a station and go work there
    /// (if not already). False, with a HUD flash, when the station refuses it.
    /// Affordability is not a condition: the entry waits at the bench for what
    /// is missing, and the panel says what that is.
    /// </summary>
    public bool TryQueueCraft(CraftingCatalog.Recipe recipe, CraftStation station, int count = 1)
    {
        if (recipe == null || station == null || knockedOut) return false;
        if (recipe.oncePerRun && recipe.made)
        {
            SetActivity("Already made", FlashSeconds);
            return false;
        }
        if (!station.Enqueue(recipe, count))
        {
            SetActivity("Can't make that here", FlashSeconds);
            return false;
        }
        if (workStation != station && WalkingToStation != station) WorkAt(station);
        return true;
    }

    /// <summary>Queue a research entry at a station and go work there (if not already).</summary>
    public bool TryQueueResearch(ResearchCatalog.ResearchDef def, CraftStation station)
    {
        if (def == null || station == null || knockedOut) return false;
        if (!station.Enqueue(def))
        {
            SetActivity(Factions.Player.Knowledge.IsDone(def) ? "Already known" : "Can't research that here", FlashSeconds);
            return false;
        }
        if (workStation != station && WalkingToStation != station) WorkAt(station);
        return true;
    }

    /// <summary>
    /// Go to a station and work its queue: start at once if already within
    /// reach of its edge, otherwise walk over and start on arrival.
    /// </summary>
    public void WorkAt(CraftStation station)
    {
        if (station == null || knockedOut) return;
        if (workStation == station) return;

        NewOrder();
        StopWork();
        StopHarvest();
        StopBuild();
        ClearTask();

        float edge = TargetingUtil.EdgeDistance(transform.position, station.transform, station.ApproachCollider);
        if (edge <= DepositEdgeDistance)
        {
            BeginWork(station);
            return;
        }

        task = TaskKind.Work;
        taskStation = station;
        stallTimer = 0f;
        SetActivity("Going to the " + TaskStationName());
        MoveTo(TargetingUtil.GetApproachPoint(transform.position, station.transform, station.ApproachCollider));
    }

    void BeginWork(CraftStation station)
    {
        workStation = station;
        workTitleShown = null;
        workPercentShown = -1;
        if (agent != null && agent.isOnNavMesh) agent.ResetPath();
        hasPendingDestination = false;
        SetActivity("At the " + station.displayName.ToLowerInvariant());
    }

    void UpdateWork()
    {
        if (workStation == null) return;

        if (!workStation.IsAlive)
        {
            StopWork();
            SetActivity("The bench is gone", FlashSeconds);
            Continue();
            return;
        }

        CraftStation.QueueEntry entry = workStation.Active;
        if (entry == null)
        {
            // Queue ran dry: stand down (the panel's Queue tab says so too)
            StopWork();
            SetActivity("Nothing left to make", FlashSeconds);
            Continue();
            return;
        }

        if (!workStation.AddLabor(Time.deltaTime, this, inventory))
        {
            if (workTitleShown != "busy")
            {
                workTitleShown = "busy";
                SetActivity("Someone is at the bench");
            }
            return;
        }

        // Our own repeat (other hands may be on other entries); it may have completed inside AddLabor
        entry = workStation.EntryOf(this);
        if (entry == null) return;

        // Only rebuild the label when the title or the whole-percent changes
        int pct = Mathf.Min(99, Mathf.FloorToInt(workStation.Progress01Of(this) * 100f));
        string status = workStation.Status;
        if (status.Length > 0)
        {
            if (workTitleShown != status)
            {
                workTitleShown = status;
                workPercentShown = -1;
                SetActivity(status);
            }
        }
        else if (pct != workPercentShown || workTitleShown != entry.Title)
        {
            workTitleShown = entry.Title;
            workPercentShown = pct;
            SetActivity((entry.IsResearch ? "Researching " : "Crafting ") + entry.Title + "  " + pct + "%");
        }
    }

    /// <summary>Leave the bench. The queue keeps its progress; nothing has been paid.</summary>
    public void StopWork()
    {
        if (workStation == null) return;
        workStation = null;
        workTitleShown = null;
        workPercentShown = -1;
        SetActivity("");
    }

    /// <summary>
    /// A station hands the character what it just made for them: into the hands
    /// (a tool is also equipped), overflow into the campfire stockpile.
    /// </summary>
    public void ReceiveCrafted(ItemDef item, int count)
    {
        if (item == null || count <= 0) return;
        int left = count - inventory.Add(item, count);
        if (left > 0)
        {
            BaseBuilding fire = Factions.Player.Campfire;
            if (fire != null) fire.Stockpile.Add(item, left);
        }
        if (heldItem != null && item.kind == ItemKind.Tool) heldItem.Equip(item);
        SetActivity("Made " + item.displayName, FlashSeconds);
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
    /// and equipment into the campfire stockpile (what fits — the rest stays in
    /// hand). Tools are the character's own and stay.
    /// </summary>
    public void DepositAll(BaseBuilding fire)
    {
        DevQuests.Signal("deposit");
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
                Factions.Player.Resources.Add(slot.item.resourceType, slot.count);
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
        taskStation = null;
        taskNode = null;
        taskSite = null;
        taskSiteCollider = null;
        taskWalkOn = false;
        taskDeposited = false;
        walking = false;
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
        StopWork();
        StopHarvest();
        StopBuild();

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
        DevQuests.Signal(Factions.Player.Stance == GuardStance.Mode.Follow ? "knockout:follow" : "knockout");
        reviveAt = Time.time + knockoutSeconds;
        hasPendingDestination = false;
        orders.Clear();   // a beating cancels the plan
        StopWork();
        StopHarvest();
        StopBuild();
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

    static BaseBuilding AliveCampfire() => Factions.Player.Campfire;

    // ------------------------------------------------------------------
    // Label
    // ------------------------------------------------------------------

    void RefreshLabel()
    {
        string name = PlayerProfile.Name;
        int queued = orders.Count;
        if (name != composedName || activity != composedActivity || queued != composedQueued)
        {
            composedName = name;
            composedActivity = activity;
            composedQueued = queued;
            activityShown = queued == 0 ? activity
                : activity.Length == 0 ? "+" + queued + " queued"
                : activity + "  (+" + queued + ")";
            composedLabel = activityShown.Length == 0
                ? name
                : name + "\n<size=70%><color=#FFFFFFCC>" + activityShown + "</color></size>";
        }

        if (floatingText != null) floatingText.SetText(composedLabel, NameColor);
    }
}
