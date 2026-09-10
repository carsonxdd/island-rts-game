using UnityEngine;
using UnityEngine.AI;
using System.Collections;

/// <summary>
/// The colony's civilian: gathers one assigned resource type, carries it back to the
/// campfire, grabs loose ground pickups on the way, and hides in a hut when enemies come.
/// </summary>
/// <remarks>
/// This class only holds the worker's stats and builds its Utility AI (Gather, Return,
/// Pickup, Idle, Flee); all the actual behaviour lives in those executors.
///
/// Workers spend their lives in crowds, so most of the tuning here is about spacing rather
/// than speed: the agent's avoidance radius, not its collider, is what keeps them apart,
/// and the stationary/moving avoidance roles below are what stop a worker standing at a
/// tree from being shoved off its spot by one walking past.
/// </remarks>
public class Worker : UnitBase<Worker>
{
    [Header("Assignment")]
    // A colonist arrives jobless and becomes a worker when the campfire panel hands
    // them a job (2026-09-02). assignedResourceType only means anything while hasJob;
    // an idle colonist builds, crafts and repairs instead. Only SetJob / ClearJob change these.
    public bool hasJob = false;

    /// <summary>
    /// What a jobless colonist is allowed to do (2026-09-07). <see cref="Any"/> is
    /// the utility colonist: builds, then crafts, then repairs, then forages, whatever
    /// the colony needs. The other three are specialists the panel pins to one trade;
    /// a specialist never does the other two and never forages. Meaningless while
    /// <see cref="hasJob"/>. Only SetSpecialty / SetJob / ClearJob change it.
    /// Serialized for the same dead-data reason as hasJob: the prefab value (Any)
    /// is what a fresh colonist gets.
    /// </summary>
    public enum Specialty { Any, Builder, Crafter, Repairer }
    public Specialty specialty = Specialty.Any;
    public ResourceNode.ResourceType assignedResourceType = ResourceNode.ResourceType.Wood;

    /// <summary>
    /// In the idle pool: no job, no specialty, not leaving. This is who the panel
    /// assigns jobs and specialties from, who becomes a warrior, and who
    /// "N idle" counts. A specialist is jobless but NOT idle.
    /// </summary>
    public bool IsIdle => !hasJob && specialty == Specialty.Any && !leaving;
    public BaseBuilding baseBuilding;  // Reference to campfire

    /// <summary>
    /// Owes the campfire a visit (2026-09-07): every job, specialty or back-to-idle
    /// change sets this, and the GearUp action walks the colonist to the fire,
    /// delivers what they carry and holds them a moment before they go out as the
    /// new unit. Cleared only by GearUpExecutor. The panel's counts read the job
    /// fields, which change at once; this is the body catching up.
    /// </summary>
    [System.NonSerialized] public bool gearingUp;

    /// <summary>
    /// The campfire drop-off slot this colonist holds (2026-09-08), -1 for none. Set by
    /// ReturnToBase / GearUp while walking in, cleared on their exit; BaseBuilding reads
    /// it to tell a live claim from a stale one. See <see cref="BaseBuilding.ClaimDropoffSlot"/>.
    /// </summary>
    [System.NonSerialized] public int dropoffSlot = -1;

    /// <summary>The unit a colonist is about to become, for the gear-up label.</summary>
    public string RoleTitle()
    {
        if (hasJob)
        {
            switch (assignedResourceType)
            {
                case ResourceNode.ResourceType.Wood: return "Wood cutter";
                case ResourceNode.ResourceType.Food: return "Forager";
                case ResourceNode.ResourceType.Stone: return "Quarrier";
                case ResourceNode.ResourceType.Metal: return "Miner";
                default: return assignedResourceType.ToString();
            }
        }
        switch (specialty)
        {
            case Specialty.Builder: return "Builder";
            case Specialty.Crafter: return "Crafter";
            case Specialty.Repairer: return "Repairer";
            default: return "Colonist";
        }
    }

    [Header("Gathering Settings")]
    public float gatherRatePerSecond = 1f;  // How fast worker gathers (resources/sec)
    public float carryCapacity = 5.01f;  // Maximum resources worker can carry (slightly over 5 to avoid floating point issues)
    public float searchRadius = 50f;  // How far to search for resources
    public float gatherDistance = 0.6f;  // Arrival tolerance to the gather point (GatherExecutor floors this at AgentRadius + 0.25 so the target can never be unreachably tight)
    public float deliveryDistance = 1.0f;  // How close to the campfire's collider EDGE to deliver (edge-based since Phase 6.25; tightened 1.5 -> 1.0 so workers walk right up to the fire)

    // How close (remaining path distance) a worker walks toward its gather point before the
    // agent stops. Small so workers stand right next to nodes instead of meters away; the
    // arrival check in GatherExecutor uses gatherDistance as its tolerance on top of this.
    public const float GatherStopDistance = 0.25f;

    // NavMeshAgent avoidance radius. This — not the CapsuleCollider (click hitbox only) —
    // is what keeps workers apart via ORCA local avoidance. 0.3 lets workers pack
    // shoulder-to-shoulder around nodes without visually overlapping the ~0.4-wide
    // meeple body. ResourceNode derives its per-node worker capacity slot arc and
    // GatherExecutor derives its anti-orbit arrival tolerance from this.
    public const float AgentRadius = 0.3f;

    // --- ORCA avoidance roles (worker crowding) ---
    // Lower avoidancePriority = MORE important = others yield to it. A stationary
    // worker (gathering, idle, sheltering at a hut) can't yield — it has no path —
    // so it's made max-importance and movers route around it like furniture. Movers
    // re-roll a random band on every new errand so two meeting workers never tie
    // (same trick enemies use on retarget). Executors call these on state changes.
    public const int StationaryAvoidancePriority = 10;

    public static void SetStationaryAvoidance(NavMeshAgent agent)
    {
        if (agent != null) agent.avoidancePriority = StationaryAvoidancePriority;
    }

    public static void RollMovingAvoidance(NavMeshAgent agent)
    {
        if (agent != null) agent.avoidancePriority = Random.Range(30, 70);
    }

    // --- Jobs (2026-09-02) ---
    // The campfire panel moves people between jobs; it never spawns or destroys
    // them. A job change releases any node claim and forces a re-evaluation, so the
    // brain re-picks with the new type. Whatever is in the worker's hands stays
    // there under bb.carryType and is delivered as that type — Gather/Pickup refuse
    // to mix a second type on top, so the worker heads home first.

    /// <summary>Give this colonist a gathering job (or change the one they have). Drops any specialty.</summary>
    public void SetJob(ResourceNode.ResourceType type)
    {
        bool changed = !hasJob || assignedResourceType != type || specialty != Specialty.Any;
        hasJob = true;
        specialty = Specialty.Any;
        assignedResourceType = type;
        if (changed) OnJobChanged();
    }

    /// <summary>
    /// Pin a jobless colonist to one trade (2026-09-07), or hand them back to the
    /// utility pool with <see cref="Specialty.Any"/>. Takes a job holder off their
    /// job first: a specialist is jobless by definition.
    /// </summary>
    public void SetSpecialty(Specialty s)
    {
        bool changed = hasJob || specialty != s;
        hasJob = false;
        specialty = s;
        if (changed) OnJobChanged();
    }

    /// <summary>Back to the idle pool: utility colonist, and the next candidate for a job or the militia.</summary>
    public void ClearJob()
    {
        if (!hasJob && specialty == Specialty.Any) return;
        hasJob = false;
        specialty = Specialty.Any;
        OnJobChanged();
    }

    // --- Leaving (2026-09-04, Slice 4) ---
    // A starving colonist gives up: no job, out of the idle pool (PopulationManager
    // skips leavers), and the Leave action walks them to the cove and destroys the
    // body there. One-way; only PopulationManager.SendOneAway sets it.
    [System.NonSerialized] public bool leaving;

    /// <summary>Walk out on the colony. The body is destroyed at the cove (the normal removal path).</summary>
    public void Leave()
    {
        if (leaving) return;
        ClearJob();
        leaving = true;
        gearingUp = false;   // ClearJob flagged a trip to the fire; a leaver owes nobody that
        if (aiBrain != null && aiBrain.blackboard != null)
        {
            aiBrain.blackboard.leaving = true;
            aiBrain.blackboard.gearingUp = false;
            aiBrain.ForceReeval();
        }
    }

    void OnJobChanged()
    {
        gearingUp = !leaving;   // a leaver has no trade to gear up for
        if (aiBrain == null || aiBrain.blackboard == null) return;   // brain not built yet — Initialize copies the fields
        AIBlackboard bb = aiBrain.blackboard;
        bb.hasJob = hasJob;
        bb.specialty = specialty;
        bb.assignedResourceType = assignedResourceType;
        bb.gearingUp = gearingUp;
        if (bb.carryAmount <= 0.01f) bb.carryType = assignedResourceType;

        if (bb.targetResource != null)
        {
            bb.targetResource.UnclaimNode(this);
            if (bb.isRegisteredAtNode)
            {
                bb.targetResource.UnregisterWorker(this);
                bb.isRegisteredAtNode = false;
            }
            bb.targetResource = null;
        }
        bb.bestResource = null;
        StopGatheringSound();
        aiBrain.ForceReeval();
    }

    // --- Garrison (flee shelter, 2026-08-26): visually hide inside a hut ---
    // Renderers (art, health bar, floating text), the NavMeshAgent, and the click
    // collider all toggle off while hidden. Enemies never target workers, so this
    // is shelter feel + removes the hidden worker from the crowd sim. Only
    // FleeToHutExecutor calls this; its OnExit always restores before any other
    // executor runs.
    private Renderer[] garrisonHiddenRenderers;
    private bool isGarrisoned;

    public void SetGarrisoned(bool hidden)
    {
        if (isGarrisoned == hidden) return;
        isGarrisoned = hidden;

        if (hidden) garrisonHiddenRenderers = GetComponentsInChildren<Renderer>(false);
        if (garrisonHiddenRenderers != null)
        {
            for (int i = 0; i < garrisonHiddenRenderers.Length; i++)
            {
                if (garrisonHiddenRenderers[i] != null)
                    garrisonHiddenRenderers[i].enabled = !hidden;
            }
        }

        if (agent != null) agent.enabled = !hidden;

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = !hidden;
    }

    [Header("Current State")]
    public float carryAmount = 0f;  // Resources currently carrying (can be fractional)

    private bool isInitialized = false;

    // Audio - 3D Spatial Sound
    private AudioSource gatheringAudioSource;
    private Coroutine gatheringSoundCoroutine;
    private ResourceNode soundNode;  // node to shake on each gathering-sound tick
    private bool isGatheringSoundActive = false;  // Tracks gathering sound state for coroutine guard

    void Start()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Balance-sim knobs, if a sweep is running. Must land before these
        // values are copied into the AI blackboard below.
        SimOverrides.Apply(this);
#endif
        VisionSource.Attach(gameObject, VisionSource.UnitRadius);

        if (FetchAgent())
        {
            // Configure NavMeshAgent for smooth navigation around obstacles
            agent.stoppingDistance = GatherStopDistance;  // Walk nearly onto the gather point (arrival tolerance is gatherDistance)
            agent.acceleration = 18f;  // Snap pass: 3.5 / 18 = ~0.19s spin-up (was 5 = 0.70s). Weight should come from top speed, not from a long ramp -- a long ramp just reads as input lag. Braking uses the same value, so arrivals tighten too
            agent.angularSpeed = 360f;  // Snappy turning - workers face new headings quickly (was 120, an anti-jitter value; watch for turn jitter)
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;  // High predicts crossings early so meeting workers weave instead of side-step dancing. Walls are static obstacles, not avoidance agents — the cost scales with agent count and ~10 workers is cheap
            RollMovingAvoidance(agent);  // Randomized priority band prevents synchronized yielding; executors switch this by state (stationary = max-importance)
            agent.radius = AgentRadius;  // Skinny avoidance radius so workers pack tightly (Phase 6.25, was 0.5)
        }

        // Create floating state text
        // 1.8, not 3: the root used to be scaled 0.4/0.6/0.4 and the text child inherited that
        // squash. Root is scale 1 now that the art is on a Model child, so 3 * 0.6 keeps the
        // on-screen size the players were already used to.
        CreateStateText(1.8f, "Idle", Color.white);

        // Setup 3D spatial audio for gathering sounds
        SetupGatheringAudioSource();

        // Wait a moment for NavMeshAgent to fully initialize
        Invoke(nameof(Initialize), 0.5f);  // Small delay before starting work
    }

    void Initialize()
    {
        isInitialized = true;
        InitializeUtilityAI();
    }

    void InitializeUtilityAI()
    {
        aiBrain = gameObject.AddComponent<AIBrain>();

        // Create blackboard
        var bb = new AIBlackboard();
        bb.transform = transform;
        bb.agent = agent;
        bb.health = CachedHealth;
        bb.baseBuilding = baseBuilding;
        bb.worker = this;
        bb.faction = Factions.Player;   // commit 5 reads the unit's own Faction here
        bb.assignedResourceType = assignedResourceType;
        bb.hasJob = hasJob;
        bb.specialty = specialty;
        bb.leaving = leaving;
        bb.gearingUp = gearingUp;
        bb.carryType = assignedResourceType;
        bb.carryCapacity = carryCapacity;
        bb.gatherDistance = gatherDistance;
        bb.deliveryDistance = deliveryDistance;
        bb.searchRadius = searchRadius;
        bb.gatherRatePerSecond = gatherRatePerSecond;
        bb.carryAmount = carryAmount;

        // Setup StuckResolver
        var stuckResolver = CreateStuckResolver();
        stuckResolver.onStuckReset = () =>
        {
            // Release claims and reset
            if (bb.targetResource != null)
            {
                bb.targetResource.UnclaimNode(this);
                if (bb.isRegisteredAtNode)
                {
                    bb.targetResource.UnregisterWorker(this);
                    bb.isRegisteredAtNode = false;
                }
            }
            bb.targetResource = null;
            aiBrain.ForceReeval();
        };
        bb.stuckResolver = stuckResolver;
        bb.brain = aiBrain;

        // First-damage ForceReeval: immediately re-evaluate on first hit
        bool hasBeenDamaged = false;
        if (bb.health != null)
        {
            bb.health.onDamaged.AddListener(() =>
            {
                if (!hasBeenDamaged)
                {
                    hasBeenDamaged = true;
                    aiBrain.ForceReeval();
                }
            });
        }

        // Ally death: re-evaluate when a nearby warrior dies (workers may need to flee)
        Warrior.OnAnyWarriorDied += OnAllyDiedUtilityAI;

        // Enemy death: re-evaluate so fleeing workers can stop fleeing
        Enemy.OnAnyEnemyDied += OnEnemyDiedUtilityAI;

        // Define actions
        var actions = new ActionOption[]
        {
            // Gather Resource
            // ThreatNearby tanks Gather when enemies are close (1 enemy → 0.2, 2+ → early-out 0)
            new ActionOption("Gather", new Consideration[]
            {
                new ResourceAvailability(ResponseCurve.Linear(1f, 0f)),  // Need a resource node — 0 with none (the 0.1 floor for a far node lives inside; a yShift here had jobless colonists "Gathering" nothing at the fire, 2026-09-08)
                new CrowdPenalty(4f, ResponseCurve.Linear(0.8f, 0.2f)),  // Spread across nodes (4+ workers walking to or at the node = max penalty)
                new ResourceCarry(ResponseCurve.InverseLinear(0.8f, 0.2f)),  // Empty inventory preferred
                new TimeOfDay(false, ResponseCurve.Linear(0.3f, 0.7f)),  // Slight daytime preference, not crippled at night
                new ThreatNearby(1f, ResponseCurve.InverseLinear(1f, 0f))  // 1 enemy nearby → score 0, hard suppression
            }, new GatherExecutor(), basePriority: 1.0f, momentumBonus: 0.15f),

            // Gear Up (2026-09-07) — a job change owes the fire a visit: deliver the
            // old load, pause, then go out as the new unit. 1.5 outranks every
            // errand (Gather/Return/Pickup peak at ~1.15 with momentum, which the
            // 20% switch bar turns into 1.38) but ThreatNearby zeroes it the moment
            // an enemy is in the grid, so Flee (1.2) still wins a raid; the flag
            // survives the flight and the trip resumes. Leave (2.0) beats it. Zero
            // momentum, no yShift: the executor's Finish ends it at once.
            new ActionOption("GearUp", new Consideration[]
            {
                new IsGearingUp(ResponseCurve.Linear(1f, 0f)),                 // Zero-cost gate
                new ThreatNearby(1f, ResponseCurve.InverseLinear(1f, 0f))     // 1 enemy nearby → 0, flee first
            }, new GearUpExecutor(), basePriority: 1.5f, momentumBonus: 0f),

            // Return to Base — compound urgency score:
            //   No pressure: only returns near-full (carry^15: 0.9→0.21, 0.95→0.46, 1.0→1.0)
            //   Enemies nearby: returns early proportional to carry × threat level
            //   Night approaching: returns early proportional to carry × (1-dayProgress)
            //   Empty inventory always scores 0 (nothing to deliver)
            //   Crossover at ~4.6 carry → RoundToInt = 5 (ensures full delivery)
            new ActionOption("Return", new Consideration[]
            {
                new ReturnUrgency(15f, 3f, ResponseCurve.Linear(1f, 0f))
            }, new ReturnToBaseExecutor(), basePriority: 1.0f, momentumBonus: 0.15f),

            // Collect Pickup — a stick, stone chunk or salvage crate lying nearby is a
            // quick top-up (2026-08-26; salvage 2026-09-02).
            // PickupAvailability fades with distance (0 beyond ~22u), so this only outbids
            // Gather when the pickup is genuinely close; ThreatNearby hard-suppresses like
            // Gather; ResourceCarry keeps full workers heading home instead. Zero yShift
            // everywhere so the action early-outs cleanly when no pickup exists.
            new ActionOption("Pickup", new Consideration[]
            {
                new PickupAvailability(ResponseCurve.Linear(1f, 0f)),          // Caches bb.bestPickup; 0 when none
                new ResourceCarry(ResponseCurve.InverseLinear(0.9f, 0.1f)),    // Prefer when hands are empty
                new ThreatNearby(1f, ResponseCurve.InverseLinear(1f, 0f))      // 1 enemy nearby → 0, hard suppression
            }, new CollectPickupExecutor(), basePriority: 1.1f, momentumBonus: 0.15f),

            // --- Jobless labor (2026-09-07): Build > Craft > Repair > Forage by LaborPriorities ---
            // A utility colonist (Specialty.Any) may take all four; a specialist only
            // their own trade (SpecialtyAllows, zero-cost, beside IsJobless so both
            // early-out before any scan). The order comes from LaborPriority, the
            // colony-wide weights the campfire panel's sliders set (defaults 1.0 /
            // 0.95 / 0.9 / 0.85), read live; base priorities are all 1.0. The weights
            // are the tie-break: each scan scores by distance with a 0.15 floor, so a
            // bench next door still beats a site across the island. Every scan is 0
            // with nothing to do and none has a yShift, so momentum cannot keep a
            // finished site or a dry bench alive.

            // Build — the site scan (Construction research gates it inside).
            // ThreatNearby hard-suppresses like Gather.
            new ActionOption("Build", new Consideration[]
            {
                new IsJobless(ResponseCurve.Linear(1f, 0f)),
                new SpecialtyAllows(Specialty.Builder, ResponseCurve.Linear(1f, 0f)),
                new LaborPriority(Specialty.Builder, ResponseCurve.Linear(1f, 0f)),
                new ConstructionAvailable(ResponseCurve.Linear(1f, 0f)),   // Caches bb.bestSite; 0 when none
                new ThreatNearby(1f, ResponseCurve.InverseLinear(1f, 0f))
            }, new BuildExecutor(), basePriority: 1.0f, momentumBonus: 0.15f),

            // Craft — a bench with a queue (2026-09-04, Slice 3; opened to every
            // jobless colonist 2026-09-07). The station scan gates on the Crafting
            // research inside and claims one colonist per bench.
            new ActionOption("Craft", new Consideration[]
            {
                new IsJobless(ResponseCurve.Linear(1f, 0f)),
                new SpecialtyAllows(Specialty.Crafter, ResponseCurve.Linear(1f, 0f)),
                new LaborPriority(Specialty.Crafter, ResponseCurve.Linear(1f, 0f)),
                new StationWorkAvailable(ResponseCurve.Linear(1f, 0f)),   // Caches bb.targetStation; 0 when none
                new ThreatNearby(1f, ResponseCurve.InverseLinear(1f, 0f))
            }, new CraftExecutor(), basePriority: 1.0f, momentumBonus: 0.15f),

            // Repair — weighted below Build and Craft so a colonist finishes new
            // construction and the queue before patching. RepairAvailable is 0 when
            // nothing is damaged OR the pool cannot cover the next unit of the repair.
            new ActionOption("Repair", new Consideration[]
            {
                new IsJobless(ResponseCurve.Linear(1f, 0f)),
                new SpecialtyAllows(Specialty.Repairer, ResponseCurve.Linear(1f, 0f)),
                new LaborPriority(Specialty.Repairer, ResponseCurve.Linear(1f, 0f)),
                new RepairAvailable(ResponseCurve.Linear(1f, 0f)),         // Caches bb.bestRepair; 0 when none
                new ThreatNearby(1f, ResponseCurve.InverseLinear(1f, 0f))
            }, new RepairExecutor(), basePriority: 1.0f, momentumBonus: 0.15f),

            // Forage — a utility colonist carries loose sticks, chunks and salvage near
            // the fire home (2026-09-03). Weighted below the three trades on purpose:
            // real work first, tidying second, and well above Idle so nobody stands
            // next to a stick doing nothing. Specialists never forage (SpecialtyAllows(Any)
            // is 1 only for a utility colonist; LaborPriority(Any) is the Forage weight).
            // Same executor as the job version; only the scan differs
            // (ForageAvailability: any type, but only within 35u of the fire).
            new ActionOption("Forage", new Consideration[]
            {
                new IsJobless(ResponseCurve.Linear(1f, 0f)),
                new SpecialtyAllows(Specialty.Any, ResponseCurve.Linear(1f, 0f)),
                new LaborPriority(Specialty.Any, ResponseCurve.Linear(1f, 0f)),
                new ForageAvailability(ResponseCurve.Linear(1f, 0f)),        // Caches bb.bestPickup; 0 when none
                new ResourceCarry(ResponseCurve.InverseLinear(0.9f, 0.1f)),  // Full hands head home instead
                new ThreatNearby(1f, ResponseCurve.InverseLinear(1f, 0f))    // Civilians flee, they don't forage
            }, new CollectPickupExecutor(), basePriority: 1.0f, momentumBonus: 0.15f),

            // Idle at home (walks to the hut or campfire it is homed to, then waits)
            new ActionOption("Idle", new Consideration[]
            {
                new ConstantScore(ResponseCurve.Constant(0.1f))  // Always-low constant floor
                // (was ResourceAvailability — the Constant curve discards its result,
                //  so it was a wasted full node scan every evaluation. Gather's
                //  ResourceAvailability still caches bb.bestResource for everyone.)
            }, new IdleExecutor(), basePriority: 0.1f, momentumBonus: 0.05f),

            // Flee from Enemies
            // Primary driver: ThreatNearby with aggressive logistic curve
            //   0 enemies → 0 (no flee), 1 enemy → ~0.95, 2+ → ~1.0
            // EnemyPresence populates bb.nearestEnemy (must be first)
            // HealthPercent: high floor (0.7 at full HP, 1.0 at low HP) — modifier not gate
            new ActionOption("Flee", new Consideration[]
            {
                new EnemyPresence(20f, ResponseCurve.Linear(1f, 0f)),         // Populates bb.nearestEnemy; 0 if no enemy in 20u
                new ThreatNearby(1f, ResponseCurve.Logistic(12f, 0.3f)),      // 1 enemy in grid → raw 1.0 → logistic ~0.999
                new HealthPercent(ResponseCurve.InverseLinear(0.3f, 0.7f))    // Full HP=0.7, low HP=1.0 — nudge not gate
            }, new FleeToHutExecutor(), basePriority: 1.2f, momentumBonus: 0.2f),

            // Leave — a starving colonist walks out (2026-09-04, Slice 4). One
            // zero-cost gate, priority 2.0 so it beats Flee; the executor ends in
            // Destroy, so there is no exit to tune.
            new ActionOption("Leave", new Consideration[]
            {
                new IsLeaving(ResponseCurve.Linear(1f, 0f))
            }, new LeaveExecutor(), basePriority: 2.0f, momentumBonus: 0f)
        };

        aiBrain.Initialize(actions, bb);
    }

    /// <summary>
    /// Left-click on a jobless colonist opens the campfire panel on the Colonists
    /// tab (2026-09-07), where the specialist rows and the priority sliders are —
    /// the natural place to ask "what should my idle people be doing?". The click
    /// collider is the capsule (a hitbox only); job holders ignore the click so a
    /// worker in a crowd does not pop the panel. Left button only, like the fire.
    /// </summary>
    void OnMouseDown()
    {
        if (PauseController.BlockGameplayInput || Minimap.PointerOver) return;
        if (hasJob || leaving)
        {
            DevQuests.Signal("worker_click:busy");   // a working colonist opens nothing
            return;
        }
        WorkerAssignmentUI ui = WorkerAssignmentUI.Instance;
        BaseBuilding fire = baseBuilding != null ? baseBuilding : BaseBuilding.FindAlive();
        if (ui == null || fire == null) return;
        ui.OpenColonists(fire);
        DevQuests.Signal("worker_click");
    }

    void Update()
    {
        // Don't do anything until initialized
        if (!isInitialized) return;

        // Update gathering audio volume based on AudioManager SFX slider
        if (gatheringAudioSource != null && AudioManager.Instance != null)
        {
            gatheringAudioSource.volume = 0.2f * AudioManager.Instance.sfxVolume * AudioManager.Instance.masterVolume;
        }

        // Sync carry amount from blackboard
        if (aiBrain != null && aiBrain.blackboard != null)
        {
            carryAmount = aiBrain.blackboard.carryAmount;
        }

        // Update state text
        if (showStateText && floatingText != null)
        {
            UpdateStateText();
        }
    }

    // --- Utility AI ForceReeval handlers ---

    void OnAllyDiedUtilityAI(Vector3 deathPos)
    {
        if (aiBrain != null && transform != null
            && Vector3.Distance(transform.position, deathPos) < 20f)
        {
            aiBrain.ForceReeval();
        }
    }

    void OnEnemyDiedUtilityAI(Vector3 deathPos)
    {
        if (aiBrain != null && transform != null
            && Vector3.Distance(transform.position, deathPos) < 30f)
        {
            aiBrain.ForceReeval();
        }
    }

    // --- Public sound methods for Utility AI executors ---
    // The node whose sound we're playing — pulsed on every audio tick so the node
    // shakes on the beat of the chop/mining sound.
    public void StartGatheringSoundPublic(ResourceNode node) { soundNode = node; StartGatheringSound(); }
    public void StopGatheringSoundPublic() { StopGatheringSound(); }

    // --- Audio ---

    void SetupGatheringAudioSource()
    {
        gatheringAudioSource = AudioHelper.CreateSpatialAudioSource(gameObject, 0.2f, 15f, 50f, 0f);
    }

    void StartGatheringSound()
    {
        // Stop any existing sound coroutine
        if (gatheringSoundCoroutine != null)
        {
            StopCoroutine(gatheringSoundCoroutine);
        }

        isGatheringSoundActive = true;

        // Start the delayed looping coroutine
        gatheringSoundCoroutine = StartCoroutine(PlayGatheringSoundLoop());
    }

    void StopGatheringSound()
    {
        isGatheringSoundActive = false;
        soundNode = null;

        // Stop the looping coroutine
        if (gatheringSoundCoroutine != null)
        {
            StopCoroutine(gatheringSoundCoroutine);
            gatheringSoundCoroutine = null;
        }

        // Stop any currently playing sound IMMEDIATELY (no fade)
        if (gatheringAudioSource != null)
        {
            gatheringAudioSource.Stop();
            gatheringAudioSource.clip = null;  // Clear the clip to ensure it stops
        }
    }

    IEnumerator PlayGatheringSoundLoop()
    {
        while (true)
        {
            // Only play sound if gathering sound is still active
            if (!isGatheringSoundActive)
            {
                yield break;
            }

            // Get the appropriate sound clip from AudioManager
            AudioClip clipToPlay = GetGatheringClip();

            if (clipToPlay != null && gatheringAudioSource != null)
            {
                gatheringAudioSource.clip = clipToPlay;
                gatheringAudioSource.Play();

                // Shake the node on the beat (destroyed-node safe: Unity null check)
                if (soundNode != null) soundNode.TriggerShakePulse();

                // Wait for clip to finish
                yield return new WaitForSeconds(clipToPlay.length);

                // Check again after clip finishes
                if (!isGatheringSoundActive)
                {
                    yield break;
                }

                // Add delay between loops (1-2 seconds)
                yield return new WaitForSeconds(Random.Range(1f, 2f));
            }
            else
            {
                // No clip assigned — still beat the shake so nodes react without audio
                if (soundNode != null) soundNode.TriggerShakePulse();
                yield return new WaitForSeconds(1.5f);
            }
        }
    }

    AudioClip GetGatheringClip()
    {
        if (AudioManager.Instance == null) return null;

        switch (assignedResourceType)
        {
            case ResourceNode.ResourceType.Wood:
                return AudioManager.Instance.gatherWoodSound;
            case ResourceNode.ResourceType.Food:
                return AudioManager.Instance.gatherFoodSound;
            case ResourceNode.ResourceType.Stone:
            case ResourceNode.ResourceType.Metal:   // ore is chipped like rock
                return AudioManager.Instance.gatherStoneSound;
            default:
                return null;
        }
    }

    // --- State text ---

    void UpdateStateText()
    {
        // Get display name from brain
        string displayName = StateDisplayName("Thinking");

        // Include carry info if carrying
        string fullText;
        if (carryAmount > 0.5f)
        {
            fullText = displayName + "\n(" + carryAmount.ToString("F1") + "/" + carryCapacity.ToString("F0") + ")";
        }
        else
        {
            fullText = displayName;
        }

        // Color based on action
        Color color;
        if (displayName.Contains("Collecting") || displayName.Contains("Gathering"))
            color = Color.green;
        else if (displayName.Contains("Moving"))
            color = Color.yellow;
        else if (displayName.Contains("Returning"))
            color = Color.cyan;
        else if (displayName.Contains("Fleeing") || displayName.Contains("Leaving"))
            color = Color.red;
        else if (displayName.Contains("Building") || displayName.Contains("Repairing") || displayName.Contains("Crafting"))
            color = new Color(1f, 0.65f, 0.2f);   // orange: labour
        else if (displayName.Contains("Heading"))
            color = Color.yellow;
        else if (displayName.Contains("Gearing up"))
            color = new Color(0.3f, 0.8f, 1f);   // the warriors' guard blue: kitting out
        else
            color = Color.gray;

        floatingText.SetText(fullText, color);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        // Single cleanup path: the base building removes us from its roster,
        // decrements the assignment counter, and frees the population slot
        if (baseBuilding != null)
            baseBuilding.NotifyWorkerRemoved(this);

        // Unsubscribe from static events to prevent memory leaks
        Warrior.OnAnyWarriorDied -= OnAllyDiedUtilityAI;
        Enemy.OnAnyEnemyDied -= OnEnemyDiedUtilityAI;

        // Stop any gathering sound
        StopGatheringSound();
    }

    // Visual debug in Scene view
    void OnDrawGizmosSelected()
    {
        // Draw search radius
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, searchRadius);
    }
}
