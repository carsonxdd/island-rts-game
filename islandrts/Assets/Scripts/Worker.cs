using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

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
    public ResourceNode.ResourceType assignedResourceType = ResourceNode.ResourceType.Wood;
    public BaseBuilding baseBuilding;  // Reference to campfire

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

    // --- Garrison (flee shelter, 2026-08-26): visually hide inside a hut ---
    // Renderers (art, health bar, floating text), the NavMeshAgent, and the click
    // collider all toggle off while hidden. Enemies never target workers, so this
    // is shelter feel + removes the hidden worker from the crowd sim. Only
    // FleeToHutExecutor calls this; its OnExit always restores before any other
    // executor runs.
    private Renderer[] garrisonHiddenRenderers;
    private bool isGarrisoned;
    public bool IsGarrisoned => isGarrisoned;

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
        bb.assignedResourceType = assignedResourceType;
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
                new ResourceAvailability(ResponseCurve.Linear(1f, 0.1f)),  // Need a resource node
                new CrowdPenalty(4f, ResponseCurve.Linear(0.8f, 0.2f)),  // Spread across nodes (4+ workers = max penalty)
                new ResourceCarry(ResponseCurve.InverseLinear(0.8f, 0.2f)),  // Empty inventory preferred
                new TimeOfDay(false, ResponseCurve.Linear(0.3f, 0.7f)),  // Slight daytime preference, not crippled at night
                new ThreatNearby(1f, ResponseCurve.InverseLinear(1f, 0f))  // 1 enemy nearby → score 0, hard suppression
            }, new GatherExecutor(), basePriority: 1.0f, momentumBonus: 0.15f),

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

            // Collect Pickup — a stick/stone lying nearby is a quick top-up (2026-08-26).
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

            // Idle at Base
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
            }, new FleeToHutExecutor(), basePriority: 1.2f, momentumBonus: 0.2f)
        };

        aiBrain.Initialize(actions, bb);
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
        else if (displayName.Contains("Fleeing"))
            color = Color.red;
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
