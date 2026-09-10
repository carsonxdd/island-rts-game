using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// The campfire: the colony's heart and the game's single most important object. It spawns
/// and assigns workers, recruits warriors, provides the first housing, is where workers
/// deliver resources and warriors heal, and losing it ends the run.
/// </summary>
/// <remarks>
/// Worker bookkeeping has exactly one owner. A worker leaves the colony through
/// Worker.OnDestroy calling NotifyWorkerRemoved, which drops it from the roster, decrements
/// the right job counter and frees its population slot. Roster membership is the
/// idempotence guard, so it is safe to call more than once - but nothing else may
/// decrement those counters, or a death that is also a demolition counts twice.
///
/// There is no campfire in the scene at startup: the opening sequence spawns it where the
/// player places it, so anything that needs the campfire must poll BaseBuilding.ActiveList
/// or listen for GameStartController.OnColonyStarted rather than caching it in Start.
/// </remarks>
public class BaseBuilding : MonoBehaviour, ITargetable, IHousing
{
    // Owner (lap step 1 commit 5). Set by Spawn.Owned right after Instantiate
    // (commit 6); the player's when nothing set it. Read in Start or later, never Awake.
    Faction owner;
    public Faction Faction { get => owner ?? (owner = Factions.Player); set => owner = value; }
    public static IReadOnlyList<BaseBuilding> ActiveList => ActiveRegistry<BaseBuilding>.List;

    /// <summary>Slots in the campfire's stockpile (materials and equipment; resources go to ResourceManager).</summary>
    public const int StockpileSlots = 16;

    /// <summary>
    /// Items the campfire can hold before it is full, before upgrades
    /// (2026-09-03). A camp stores what fits around the fire; research adds
    /// room (Storage Pits, Racks and Baskets) and a Warehouse building will
    /// add more (see README). Read live, never cached — the same rule every
    /// other upgrade follows.
    /// </summary>
    public const int BaseStockpileCapacity = 60;

    public int StockpileCapacity => BaseStockpileCapacity + Faction.Knowledge.StockpileRoom;

    /// <summary>
    /// The campfire inventory (2026-09-02): sticks and stone chunks the player's
    /// character deposits here, and the weapons every station crafts (one colony
    /// store, 2026-09-03). Not the four pooled resources — those stay in
    /// ResourceManager. Runtime-only, never serialized.
    /// </summary>
    public Inventory Stockpile { get; } = new Inventory(StockpileSlots);

    /// <summary>The campfire's bench (runtime-added in Awake): "anything, but slowly" — every category at 1×.</summary>
    public CraftStation Station { get; private set; }

    // IHousing — the campfire is the colony's first housing (the starting crew's slots)
    public int HousingCapacity => workerCapacity;
    public bool HousingAlive => !housingReleased && (healthComponent == null || healthComponent.IsAlive);
    public Collider HousingCollider => housingCollider;
    private Collider housingCollider;
    private bool housingReleased;

    void Awake()
    {
        ActiveRegistry<BaseBuilding>.Register(this);
        PopulationManager.EnsureExists();   // the roster must exist before Start registers housing

        Stockpile.totalCapacity = () => StockpileCapacity;

        Station = GetComponent<CraftStation>();
        if (Station == null) Station = gameObject.AddComponent<CraftStation>();
        Station.tier = ResearchCatalog.Station.Campfire;
        Station.speeds = new[] { 1f, 1f, 1f, 1f };
        Station.displayName = "Campfire";
    }

    [Header("Health")]
    public float maxHealth = 200f;  // Campfire health
    private Health healthComponent;
    public Health CachedHealth => healthComponent;

    [Header("UI Reference")]
    public WorkerAssignmentUI workerUI;  // Drag the UI object here in Inspector

    [Header("Worker Management")]
    public GameObject workerPrefab;
    public int maxWorkers = 10;

    [Header("Population & Housing")]
    public int workerCapacity = 3;  // Campfire provides 3 worker slots (starting crew)

    // Job counts are derived from the roster, never counted — a counter that is
    // incremented in one place and decremented in another drifts the first time a
    // path is missed (Phase 6.23 found exactly that). ~20 workers, so the scan is free.
    public int woodWorkers => CountJob(ResourceNode.ResourceType.Wood);
    public int foodWorkers => CountJob(ResourceNode.ResourceType.Food);
    public int stoneWorkers => CountJob(ResourceNode.ResourceType.Stone);
    public int metalWorkers => CountJob(ResourceNode.ResourceType.Metal);

    /// <summary>Colonists pinned to crafting (2026-09-04; a specialty since 2026-09-07): they work benches only.</summary>
    public int crafterWorkers => CountSpecialty(Worker.Specialty.Crafter);
    /// <summary>Colonists pinned to building sites only (2026-09-07).</summary>
    public int builderWorkers => CountSpecialty(Worker.Specialty.Builder);
    /// <summary>Colonists pinned to repairs only (2026-09-07).</summary>
    public int repairerWorkers => CountSpecialty(Worker.Specialty.Repairer);

    /// <summary>Jobless colonists pinned to one trade. Utility colonists (Specialty.Any) are the idle pool, not counted here.</summary>
    public int CountSpecialty(Worker.Specialty s)
    {
        if (s == Worker.Specialty.Any) return 0;
        int n = 0;
        for (int i = 0; i < activeWorkers.Count; i++)
        {
            Worker w = activeWorkers[i];
            if (w != null && !w.hasJob && !w.leaving && w.specialty == s) n++;
        }
        return n;
    }

    int CountJob(ResourceNode.ResourceType type)
    {
        int n = 0;
        for (int i = 0; i < activeWorkers.Count; i++)
        {
            Worker w = activeWorkers[i];
            if (w != null && w.hasJob && w.assignedResourceType == type) n++;
        }
        return n;
    }

    [Header("Warrior Management")]
    public GameObject warriorPrefab;
    [Tooltip("0 = no cap (2026-09-07): a warrior already needs a bed and an idle colonist, so housing bounds the militia. A positive value is a hard cap on top — the balance sim's knob.")]
    public int maxWarriors = 0;
    [Tooltip("Food per recruit. The rest of the price is a weapon from the stockpile (2026-09-03) — the old 10 wood is gone.")]
    public int warriorCost_Food = 15;
    public int currentWarriors = 0;

    [Header("Spawn Settings")]
    public float spawnRadius = 2f;  // How far from campfire workers spawn

    [Header("Building Placement")]
    public float noBuildRadius = 2.5f;  // Creates 5x5 square no-build zone around campfire
    [Tooltip("Visual-only radius for the red no-build border. Does not affect actual placement validation.")]
    public float visualNoBuildRadius = 2.5f;

    [Header("Hover Effect")]
    [Tooltip("Glow strength while the mouse is over the campfire (see HoverGlow).")]
    public float hoverGlow = 2.2f;

    // Track all spawned workers and warriors
    private List<Worker> activeWorkers = new List<Worker>();
    private List<Warrior> activeWarriors = new List<Warrior>();
    private Material[] buildingMaterials;  // Every material slot across every renderer
    private HoverGlow glow;                // Hover feedback is an emissive glow, not a tint

    void Start()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        SimOverrides.Apply(this);
#endif
        // Setup Health component
        healthComponent = GetComponent<Health>();
        if (healthComponent == null)
        {
            healthComponent = gameObject.AddComponent<Health>();
        }
        healthComponent.maxHealth = maxHealth;
        healthComponent.currentHealth = maxHealth;
        healthComponent.destroyOnDeath = false;  // Don't destroy campfire, handle game over instead
        healthComponent.hideWhenFull = true;
        healthComponent.onDeath.AddListener(OnCampfireDestroyed);

        // This colony's campfire (commit 5): the faction's slot, cleared when it dies or goes
        if (Faction.Campfire == null) Faction.Campfire = this;

        VisionSource.Attach(gameObject, VisionSource.CampfireRadius);

        // Register the campfire as housing (the starting crew's slots)
        housingCollider = GetComponent<Collider>();
        if (Faction.Population != null)
        {
            Faction.Population.RegisterHousing(this);
        }

        // Get ALL renderers BEFORE creating health text (checks this object AND all children).
        // Collect instances EVERY material slot, not just slot 0 - the low-poly art meshes are
        // multi-submesh, so slot-0-only tinting would leave most of the building unhighlighted.
        Renderer[] buildingRenderers = GetComponentsInChildren<Renderer>();

        if (buildingRenderers.Length > 0)
        {
            buildingMaterials = RendererTint.Collect(buildingRenderers);
            glow = HoverGlow.Attach(gameObject, buildingMaterials, 0f, hoverGlow);
        }
        else
        {
            Debug.LogWarning("BaseBuilding: No Renderers found! Hover effect won't work. Make sure Campfire has a MeshRenderer component.");
        }

        // Check if we have a collider for mouse clicks
        Collider col = GetComponent<Collider>();
        if (col == null)
        {
            Debug.LogError("BaseBuilding: NO COLLIDER FOUND! Add a Box Collider or Capsule Collider to make this clickable!");
        }

        // Enable NavMeshObstacle carving so workers/units path AROUND the campfire
        // instead of trying to walk through it and getting stuck on the collider.
        // Enemies can still attack from the carve edge (attackRange 3.5 > any reasonable carve radius).
        UnityEngine.AI.NavMeshObstacle obstacle = GetComponent<UnityEngine.AI.NavMeshObstacle>();
        if (obstacle != null)
        {
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;
            obstacle.carvingTimeToStationary = 0.1f;
        }
    }

    void OnMouseEnter()
    {
        if (glow != null) glow.SetHovered(true);
    }

    void OnMouseExit()
    {
        if (glow != null) glow.SetHovered(false);
    }

    void OnMouseDown()
    {
        // When clicked, open worker assignment UI (the panel is code-built
        // and self-registering now, so a missing scene reference is fine)
        if (PauseController.BlockGameplayInput || Minimap.PointerOver) return;
        WorkerAssignmentUI ui = workerUI != null ? workerUI : WorkerAssignmentUI.Instance;
        if (ui != null)
        {
            ui.OpenPanel(this);
        }
        else
        {
            Debug.LogWarning("BaseBuilding: No WorkerAssignmentUI in the scene!");
        }
    }

    // ------------------------------------------------------------------
    // Jobs — assignment moves people, it never creates them (2026-09-02)
    // ------------------------------------------------------------------

    /// <summary>
    /// Give the idle colonist nearest the fire this job. False when nobody is idle —
    /// the panel disables its + buttons on the same condition.
    /// </summary>
    public bool AssignWorker(ResourceNode.ResourceType resourceType)
    {
        if (!Faction.Knowledge.HasJob(resourceType)) return false;   // the matching tool has not been crafted yet
        if (Faction.Population == null) return false;
        Worker idle = Faction.Population.FindIdleColonist(transform.position);
        if (idle == null) return false;

        idle.SetJob(resourceType);
        return true;
    }

    /// <summary>Send one worker of this job back to the idle pool (they keep their body and their home).</summary>
    public bool UnassignWorker(ResourceNode.ResourceType resourceType)
    {
        for (int i = 0; i < activeWorkers.Count; i++)
        {
            Worker worker = activeWorkers[i];
            if (worker != null && worker.hasJob && worker.assignedResourceType == resourceType)
            {
                worker.ClearJob();
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The research a specialty needs (2026-09-07): building and repairing are
    /// Construction knowledge, bench work is Crafting. The same gates the AI scans
    /// read, so a specialist can never be pinned to a trade nobody knows yet.
    /// </summary>
    public static Unlocks.Kind UnlockFor(Worker.Specialty s) =>
        s == Worker.Specialty.Crafter ? Unlocks.Kind.Crafting : Unlocks.Kind.Construction;

    /// <summary>
    /// Pin the idle colonist nearest the fire to one trade (2026-09-07; the Crafter
    /// job of 2026-09-04 became one of these). They leave the idle pool and do
    /// nothing but that trade. False when nobody is idle or the trade is unresearched.
    /// </summary>
    public bool AssignSpecialist(Worker.Specialty s)
    {
        if (s == Worker.Specialty.Any) return false;
        if (!Faction.Knowledge.Has(UnlockFor(s))) return false;
        if (Faction.Population == null) return false;
        Worker idle = Faction.Population.FindIdleColonist(transform.position);
        if (idle == null) return false;

        idle.SetSpecialty(s);
        DevQuests.Signal("specialist:" + s.ToString().ToLowerInvariant());
        return true;
    }

    /// <summary>Send one specialist of this trade back to the idle pool.</summary>
    public bool UnassignSpecialist(Worker.Specialty s)
    {
        for (int i = 0; i < activeWorkers.Count; i++)
        {
            Worker worker = activeWorkers[i];
            if (worker != null && !worker.hasJob && worker.specialty == s)
            {
                worker.ClearJob();
                return true;
            }
        }
        return false;
    }

    // --- Drop-off slots (2026-09-08) ---
    // Eight bearings around the fire that colonists walking in to deliver (or to
    // gear up) claim, the same way warriors claim an enemy's attack slots. Before
    // this every returner pathed to the ONE closest point on the collider, so a
    // group coming back from the same forest queued nose-to-tail on one face and
    // the ones at the back stood "Returning to base" until the 8 s fallback fired.
    // A slot is free when its owner is gone, dead, or no longer holding it
    // (Worker.dropoffSlot) — nothing leaks. With every slot taken the nearest is
    // shared; two on one face is still a short queue.

    public const int DropoffSlotCount = 8;
    private Worker[] dropoffOwners;

    /// <summary>
    /// The slot this colonist should deliver at: the one it already holds, else the
    /// free bearing closest to its own line of approach, else the closest regardless.
    /// Stamps <see cref="Worker.dropoffSlot"/>. Pair with <see cref="DropoffPoint"/>.
    /// </summary>
    public int ClaimDropoffSlot(Worker worker, Vector3 from)
    {
        if (dropoffOwners == null) dropoffOwners = new Worker[DropoffSlotCount];

        if (worker != null && worker.dropoffSlot >= 0 && worker.dropoffSlot < DropoffSlotCount
            && dropoffOwners[worker.dropoffSlot] == worker)
            return worker.dropoffSlot;

        Vector3 dir = from - transform.position;
        dir.y = 0f;
        float wanted = dir.sqrMagnitude > 0.001f ? Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg : 0f;

        int bestFree = -1, bestAny = -1;
        float bestFreeDelta = float.MaxValue, bestAnyDelta = float.MaxValue;
        for (int i = 0; i < DropoffSlotCount; i++)
        {
            float delta = Mathf.Abs(Mathf.DeltaAngle(wanted, DropoffSlotAngle(i)));
            if (delta < bestAnyDelta) { bestAnyDelta = delta; bestAny = i; }
            if (DropoffSlotFree(i) && delta < bestFreeDelta) { bestFreeDelta = delta; bestFree = i; }
        }

        int slot = bestFree >= 0 ? bestFree : bestAny;
        if (bestFree >= 0) dropoffOwners[slot] = worker;
        if (worker != null) worker.dropoffSlot = slot;
        return slot;
    }

    /// <summary>Give the slot back (delivered, or left for another errand). Clears <see cref="Worker.dropoffSlot"/>.</summary>
    public void ReleaseDropoffSlot(Worker worker)
    {
        if (worker != null) worker.dropoffSlot = -1;
        if (dropoffOwners == null) return;
        for (int i = 0; i < DropoffSlotCount; i++)
            if (dropoffOwners[i] == worker) dropoffOwners[i] = null;
    }

    /// <summary>
    /// Walkable point at the fire's collider edge along the slot's bearing. The fire
    /// carves the NavMesh, so this goes through the shared ClosestPoint → SamplePosition
    /// approach-point pattern, aimed from a point out along the bearing instead of from
    /// the colonist (which is what put everyone on the same face).
    /// </summary>
    public Vector3 DropoffPoint(int slot)
    {
        float a = DropoffSlotAngle(slot) * Mathf.Deg2Rad;
        Vector3 outside = transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 10f;
        Collider col = housingCollider != null ? housingCollider : GetComponent<Collider>();
        return TargetingUtil.GetApproachPoint(outside, transform, col);
    }

    static float DropoffSlotAngle(int slot) => slot * (360f / DropoffSlotCount) + 22.5f;   // face-centres and corners alike, never a corner exactly

    bool DropoffSlotFree(int i)
    {
        Worker owner = dropoffOwners[i];
        if (owner == null) return true;                        // destroyed, or never claimed
        if (owner.dropoffSlot != i) return true;               // moved on without releasing
        Health h = owner.CachedHealth;
        return h != null && !h.IsAlive;
    }

    /// <summary>
    /// Called from Worker.OnDestroy so every destruction path (killed by enemy,
    /// converted to a warrior, scene teardown) updates the roster and the population
    /// exactly once. Roster membership is the guard; a converted worker's old body
    /// was already dropped from the roster, so its destroy is a no-op here.
    /// </summary>
    public void NotifyWorkerRemoved(Worker worker)
    {
        if (!activeWorkers.Remove(worker))
            return;  // Already processed (or never tracked by this building)

        if (Faction.Population != null)
        {
            Faction.Population.RemoveColonist(worker);
        }
    }

    /// <summary>
    /// A new person joins the colony: jobless, homed to <paramref name="home"/>.
    /// PopulationManager calls this for every survivor that comes ashore, and the
    /// opening sequence for the castaway who settles in. Returns null if the prefab
    /// is missing.
    /// </summary>
    public Worker SpawnColonist(Vector3 position, IHousing home)
    {
        Worker worker = InstantiateColonist(position);
        if (worker == null) return null;

        if (Faction.Population != null)
        {
            Faction.Population.AddColonist(worker, home);
        }
        return worker;
    }

    /// <summary>The body only — roster bookkeeping is the caller's job (AddColonist or ReplaceUnit).</summary>
    Worker InstantiateColonist(Vector3 position)
    {
        if (workerPrefab == null)
        {
            Debug.LogError("BaseBuilding: Worker prefab not assigned!");
            return null;
        }

        GameObject workerObj = Spawn.Owned(workerPrefab, position, Quaternion.identity, Faction);
        workerObj.name = $"Colonist_{activeWorkers.Count + 1}";

        Worker worker = workerObj.GetComponent<Worker>();
        if (worker == null)
        {
            Debug.LogError("BaseBuilding: Worker prefab doesn't have Worker component!");
            Destroy(workerObj);
            return null;
        }

        worker.hasJob = false;
        worker.baseBuilding = this;
        activeWorkers.Add(worker);
        return worker;
    }

    /// <summary>
    /// Find a valid spawn position on the NavMesh around the campfire.
    /// Tries random positions, then evenly-spaced directions, then falls back with warning.
    /// </summary>
    public Vector3 GetValidSpawnPosition()
    {
        NavMeshHit hit;

        // Try 8 random positions slightly farther out
        for (int i = 0; i < 8; i++)
        {
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float distance = spawnRadius + Random.Range(0.5f, 1.5f);

            Vector3 candidate = transform.position + new Vector3(
                Mathf.Cos(angle) * distance,
                0f,
                Mathf.Sin(angle) * distance
            );

            if (NavMesh.SamplePosition(candidate, out hit, 2f, NavMesh.AllAreas))
            {
                // hit.position IS on the NavMesh at the right height — the old
                // "+1" was a flat-world/center-pivot relic that floats (or on
                // terrain, buries) base-pivot units
                return hit.position;
            }
        }

        // Fallback: try 8 evenly-spaced directions at a farther distance
        for (int i = 0; i < 8; i++)
        {
            float angle = i * 45f * Mathf.Deg2Rad;
            float distance = spawnRadius + 2f;

            Vector3 candidate = transform.position + new Vector3(
                Mathf.Cos(angle) * distance,
                0f,
                Mathf.Sin(angle) * distance
            );

            if (NavMesh.SamplePosition(candidate, out hit, 3f, NavMesh.AllAreas))
            {
                // hit.position IS on the NavMesh at the right height — the old
                // "+1" was a flat-world/center-pivot relic that floats (or on
                // terrain, buries) base-pivot units
                return hit.position;
            }
        }

        // Last resort: offset position with NavMesh validation
        Debug.LogWarning("BaseBuilding: Could not find valid NavMesh spawn position! Using fallback.");
        Vector3 fallback = transform.position + new Vector3(spawnRadius + 1f, 0f, 0f);
        // Bug 3: Validate fallback on NavMesh to prevent off-mesh spawns
        if (NavMesh.SamplePosition(fallback, out hit, 5f, NavMesh.AllAreas))
        {
            return hit.position;
        }
        return fallback;
    }

    /// <summary>Everyone assigned: the four gathering jobs plus the three specialties. Utility colonists are the idle count.</summary>
    public int GetTotalWorkers()
    {
        return woodWorkers + foodWorkers + stoneWorkers + metalWorkers + builderWorkers + crafterWorkers + repairerWorkers;
    }

    // ------------------------------------------------------------------
    // Warriors — recruited FROM the idle pool, not spawned (2026-09-02)
    // ------------------------------------------------------------------

    /// <summary>The best weapon in the stockpile (catalog preference order), or null when there is none.</summary>
    public ItemDef FirstWeaponInStock()
    {
        ItemDef[] weapons = ItemCatalog.Weapons;
        for (int i = 0; i < weapons.Length; i++)
            if (Stockpile.Count(weapons[i]) > 0) return weapons[i];
        return null;
    }

    /// <summary>Every weapon of every kind in the stockpile.</summary>
    public int WeaponsInStock()
    {
        int n = 0;
        ItemDef[] weapons = ItemCatalog.Weapons;
        for (int i = 0; i < weapons.Length; i++) n += Stockpile.Count(weapons[i]);
        return n;
    }

    // --- The recruit picker (2026-09-04, Slice 3) ---
    // Which weapon the next recruit is armed with. The Colonists tab cycles it;
    // it starts on the best weapon in stock the first time anyone asks, and a
    // choice sticks until the player changes it (an empty stock just disables
    // the + button — the label says "0 in stock"). Never serialized.
    private ItemDef selectedWeapon;

    /// <summary>The weapon the next recruit takes. Defaults to the best in stock.</summary>
    public ItemDef SelectedWeapon
    {
        get
        {
            if (selectedWeapon == null)
            {
                selectedWeapon = FirstWeaponInStock();
                if (selectedWeapon == null) selectedWeapon = ItemCatalog.Weapons[0];
            }
            return selectedWeapon;
        }
    }

    /// <summary>Step the recruit picker through <see cref="ItemCatalog.Weapons"/> (+1 / -1, wrapping).</summary>
    public void CycleWeapon(int step)
    {
        ItemDef[] weapons = ItemCatalog.Weapons;
        int index = System.Array.IndexOf(weapons, SelectedWeapon);
        if (index < 0) index = 0;
        index = (index + step + weapons.Length) % weapons.Length;
        selectedWeapon = weapons[index];
        DevQuests.Signal("picker");
    }

    /// <summary>True when a recruit could happen right now: Spearcraft known, under the cap if there is one, the chosen weapon in stock, the food, and someone idle.</summary>
    public bool CanRecruitWarrior()
    {
        if (!Faction.Knowledge.Has(Unlocks.Kind.Militia)) return false;   // Spearcraft not researched
        if (maxWarriors > 0 && currentWarriors >= maxWarriors) return false;
        if (Stockpile.Count(SelectedWeapon) <= 0) return false; // nothing to arm them with
        if (Faction.Resources.food < warriorCost_Food) return false;
        Population pm = Faction.Population;
        if (pm == null) return false;
        if (pm.GetIdleCount() > 0) return true;
        // Playtest: everything else is in place and only hands are missing — the
        // greyed + is housing's doing (no beds → no arrivals) or a pinned specialist's.
        if (pm.GetColonistCount() > 0)
        {
            if (!pm.HasAvailableHousing()) DevQuests.Signal("recruit:housing_full");
            else if (DevQuests.IsOpen("recruit:no_idle") && AnyJoblessSpecialist()) DevQuests.Signal("recruit:no_idle");
        }
        return false;
    }

    /// <summary>A colonist with no job who is pinned to a trade, so not idle (playtest signal only).</summary>
    static bool AnyJoblessSpecialist()
    {
        var workers = Worker.ActiveList;
        for (int i = 0; i < workers.Count; i++)
        {
            Worker w = workers[i];
            if (w != null && !w.hasJob && !w.leaving && w.specialty != Worker.Specialty.Any) return true;
        }
        return false;
    }

    /// <summary>
    /// Arm the idle colonist nearest the fire: they keep their roster slot and their home,
    /// the worker body is destroyed and a warrior stands in its place. Costs one weapon
    /// from the stockpile (the one the picker shows) and the food. No-op when nobody
    /// is idle, the cap is reached, or it is unaffordable.
    /// </summary>
    public void SpawnWarrior()
    {
        if (!CanRecruitWarrior()) return;

        if (warriorPrefab == null)
        {
            Debug.LogError("BaseBuilding: Warrior prefab not assigned!");
            return;
        }

        Worker recruit = Faction.Population.FindIdleColonist(transform.position);
        if (recruit == null) return;

        ItemDef weapon = SelectedWeapon;
        if (Stockpile.Remove(weapon, 1) <= 0) return;
        Faction.Resources.SpendFood(warriorCost_Food);

        // Stand the warrior where the colonist stood (a garrisoned colonist is at a
        // hut edge, which is on the NavMesh too)
        Vector3 spawnPos = recruit.transform.position;
        NavMeshHit hit;
        if (NavMesh.SamplePosition(spawnPos, out hit, 2f, NavMesh.AllAreas)) spawnPos = hit.position;

        GameObject warriorObj = Spawn.Owned(warriorPrefab, spawnPos, recruit.transform.rotation, Faction);
        bool archer = weapon.equipment != null && weapon.equipment.ranged;   // a bow makes an archer (2026-09-04)
        warriorObj.name = (archer ? "Archer_" : "Warrior_") + (currentWarriors + 1);

        Warrior warrior = warriorObj.GetComponent<Warrior>();
        if (warrior == null)
        {
            Debug.LogError("BaseBuilding: Warrior prefab doesn't have Warrior component!");
            Destroy(warriorObj);
            return;
        }

        warrior.baseBuilding = this;
        warrior.weapon = weapon;   // before Start, which copies its stats into the unit
        DevQuests.Signal(archer ? "archer" : "warrior");
        activeWarriors.Add(warrior);
        currentWarriors++;
        if (currentWarriors >= 6) DevQuests.Signal("warrior:6");   // past the old cap of five

        // Same person, new body: swap the roster entry BEFORE destroying the old body,
        // so the worker's OnDestroy → NotifyWorkerRemoved finds nothing to remove.
        Faction.Population.ReplaceUnit(recruit, warrior);
        activeWorkers.Remove(recruit);
        Destroy(recruit.gameObject);
    }

    /// <summary>
    /// Dismiss a warrior: they lay down arms and rejoin the idle pool where they stand.
    /// The weapon goes back to the stockpile (lost if it is full); the food is not refunded.
    /// </summary>
    public void RemoveWarrior()
    {
        if (currentWarriors <= 0 || activeWarriors.Count == 0)
        {
            return;
        }

        // Find the first living warrior
        Warrior warriorToRemove = null;
        foreach (Warrior warrior in activeWarriors)
        {
            if (warrior != null)
            {
                warriorToRemove = warrior;
                break;
            }
        }
        if (warriorToRemove == null) return;

        DismissWarrior(warriorToRemove);
    }

    /// <summary>
    /// The one path a living warrior becomes a colonist again (2026-09-04): the
    /// panel's − button and, later, a starving colony's departures. Returns the
    /// new colonist body, or null if the warrior was not this campfire's.
    /// </summary>
    public Worker DismissWarrior(Warrior warrior)
    {
        if (warrior == null || !activeWarriors.Remove(warrior)) return null;
        currentWarriors--;

        if (warrior.weapon != null) Stockpile.Add(warrior.weapon, 1);

        Worker colonist = InstantiateColonist(warrior.transform.position);
        if (colonist != null && Faction.Population != null)
        {
            Faction.Population.ReplaceUnit(warrior, colonist);
        }
        Destroy(warrior.gameObject);
        return colonist;
    }

    /// <summary>
    /// Swap a warrior's weapon for the better one in the stockpile, if there still is
    /// one (2026-09-04): the better weapon comes out, the old one goes back in, and
    /// the stats land through <see cref="Warrior.ApplyWeapon"/>. Called by the Rearm
    /// executor on arrival at the fire; a no-op when nothing better is in stock.
    /// </summary>
    public bool RearmWarrior(Warrior warrior)
    {
        if (warrior == null) return false;
        ItemDef better = ItemCatalog.BetterWeaponInStock(warrior.weapon, Stockpile);
        if (better == null) return false;
        if (Stockpile.Remove(better, 1) <= 0) return false;

        if (warrior.weapon != null) Stockpile.Add(warrior.weapon, 1);   // lost if the stockpile is full
        warrior.ApplyWeapon(better);
        DevQuests.Signal("rearm");
        return true;
    }

    // Called when a warrior is killed — the one path a warrior leaves the roster by
    public void NotifyWarriorKilled(GameObject warrior)
    {
        Warrior warriorComponent = warrior.GetComponent<Warrior>();
        if (warriorComponent != null)
        {
            if (activeWarriors.Remove(warriorComponent)) currentWarriors--;
            if (Faction.Population != null)
            {
                Faction.Population.RemoveColonist(warriorComponent);
            }
        }
    }

    // Get current warrior count
    public int GetWarriorCount()
    {
        return currentWarriors;
    }

    // Called when campfire is destroyed
    void OnCampfireDestroyed()
    {
        Debug.Log("========================================");
        Debug.Log("BaseBuilding: CAMPFIRE DESTROYED! GAME OVER!");
        Debug.Log("========================================");

        ReleaseHousing();
        if (Faction.Campfire == this) Faction.Campfire = null;

        // Health component will handle the "DESTROYED!" text display

        // Visual feedback - darken the campfire
        if (buildingMaterials != null && buildingMaterials.Length > 0)
        {
            RendererTint.SetColor(buildingMaterials, Color.black);
        }
        else
        {
            Debug.LogWarning("BaseBuilding: No renderers found to darken!");
        }

        // Disable collider so it can't be clicked
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.enabled = false;
        }

        // Trigger game over through GameManager
        if (GameManager.Instance != null)
        {
            GameManager.Instance.TriggerDefeat();
        }
        else
        {
            Debug.LogError("BaseBuilding: No GameManager found to trigger defeat!");
        }

        // Disable worker spawning
        enabled = false;
    }

    // Public method to get current health (for UI later)
    public float GetCurrentHealth()
    {
        return healthComponent != null ? healthComponent.currentHealth : maxHealth;
    }

    public float GetHealthPercentage()
    {
        return healthComponent != null ? healthComponent.GetHealthPercentage() : 1f;
    }

    /// <summary>Housing leaves the roster exactly once, whether the fire died or the scene is tearing down.</summary>
    void ReleaseHousing()
    {
        if (housingReleased) return;
        housingReleased = true;
        if (Faction.Population != null)
        {
            Faction.Population.UnregisterHousing(this);
        }
    }

    void OnDestroy()
    {
        ActiveRegistry<BaseBuilding>.Unregister(this);
        ReleaseHousing();
        if (owner != null && owner.Campfire == this) owner.Campfire = null;   // never resolve the owner during teardown
    }

    // Visual helper in Scene view
    void OnDrawGizmosSelected()
    {
        // Draw spawn radius
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, spawnRadius);

        // Draw no-build radius (larger, semi-transparent red)
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, noBuildRadius);
    }
}
