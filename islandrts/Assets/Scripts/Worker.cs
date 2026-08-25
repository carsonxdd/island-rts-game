using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

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
    public float deliveryDistance = 1.5f;  // How close to the campfire's collider EDGE to deliver (Phase 6.25: edge-based, was 3.5 from center)

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

    [Header("Current State")]
    public float carryAmount = 0f;  // Resources currently carrying (can be fractional)

    private bool isInitialized = false;

    // Audio - 3D Spatial Sound
    private AudioSource gatheringAudioSource;
    private Coroutine gatheringSoundCoroutine;
    private bool isGatheringSoundActive = false;  // Tracks gathering sound state for coroutine guard

    void Start()
    {
        if (FetchAgent())
        {
            // Configure NavMeshAgent for smooth navigation around obstacles
            agent.stoppingDistance = GatherStopDistance;  // Walk nearly onto the gather point (arrival tolerance is gatherDistance)
            agent.acceleration = 5f;  // Moderate acceleration for smoother movement (reduced from 8)
            agent.angularSpeed = 360f;  // Snappy turning - workers face new headings quickly (was 120, an anti-jitter value; watch for turn jitter)
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;  // Reduced from High for performance with many walls
            agent.avoidancePriority = Random.Range(30, 70);  // Randomized priority to prevent synchronized yielding
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

            // Idle at Base
            new ActionOption("Idle", new Consideration[]
            {
                new ResourceAvailability(ResponseCurve.Constant(0.1f))  // Always-low constant
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
    public void StartGatheringSoundPublic() { StartGatheringSound(); }
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
                // No clip available, wait and try again
                yield return new WaitForSeconds(1f);
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
