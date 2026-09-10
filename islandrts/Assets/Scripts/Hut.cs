using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Player-built housing. Each hut adds worker slots to the colony's population cap and is
/// a target enemies will chew through on their way to the campfire.
/// </summary>
/// <remarks>
/// Housing has exactly one owner: a hut adds its capacity in Start and gives it back
/// through ReleaseHousing, which is flag-guarded and called from BOTH death and OnDestroy
/// so demolishing counts too. Nothing else may add or remove housing on a hut's behalf.
/// </remarks>
public class Hut : MonoBehaviour, ITargetable, IHousing
{
    // Owner (lap step 1 commit 5). Set by Spawn.Owned right after Instantiate
    // (commit 6); the player's when nothing set it. Read in Start or later, never Awake.
    Faction owner;
    public Faction Faction { get => owner ?? (owner = Factions.Player); set => owner = value; }
    // IHousing — PopulationManager derives the colony's capacity from registered providers
    public int HousingCapacity => workerCapacity;
    public bool HousingAlive => !housingReleased && (healthComponent == null || healthComponent.IsAlive);
    public Collider HousingCollider => housingCollider;
    private Collider housingCollider;

    public static IReadOnlyList<Hut> ActiveList => ActiveRegistry<Hut>.List;

    // Static event: fires when any hut dies. Enemies subscribe to ForceReeval
    // so they retarget immediately instead of waiting ~0.25s for the next brain tick.
    public static event System.Action OnAnyHutDestroyed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { OnAnyHutDestroyed = null; }

    void Awake()
    {
        ActiveRegistry<Hut>.Register(this);
        PopulationManager.EnsureExists();   // the roster must exist before Start registers housing
    }

    [Header("Health")]
    public float maxHealth = 100f;  // Hut health (less than campfire)
    private Health healthComponent;
    public Health CachedHealth => healthComponent;

    [Header("Building Placement")]
    public float noBuildRadius = 3.5f;  // Keeps other buildings a 3-cell buffer away

    [Header("Population & Housing")]
    public int workerCapacity = 2;      // Worker slots this hut contributes to the population cap

    private bool housingReleased = false;  // Guards against double RemoveHousing (death + destroy)

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
        // A hut is a solid box at the RTS camera angle, so it opens a window for a unit
        // standing behind it, same as a tree (2026-09-08). It owns no material set of its
        // own, so the cutout is this object's single collector.
        OccluderCutout.AttachTo(gameObject);
        if (Faction.IsPlayer) VisionSource.Attach(gameObject, VisionSource.HutRadius);
        else FogVisibility.Attach(gameObject, FogVisibility.Rule.Visible);   // another colony's: shown only on watched ground, sees nothing for the player

        healthComponent.destroyOnDeath = true;  // Huts are destroyed when killed
        healthComponent.destroyDelay = 1f;  // Small delay before destruction
        healthComponent.showHealthText = true;
        healthComponent.showObjectName = true;
        healthComponent.hideWhenFull = true;
        healthComponent.onDeath.AddListener(OnHutDestroyed);

        // Enable NavMeshObstacle carving so workers path around the hut
        UnityEngine.AI.NavMeshObstacle obstacle = GetComponent<UnityEngine.AI.NavMeshObstacle>();
        if (obstacle != null)
        {
            obstacle.carving = true;  // Carve NavMesh so agents path around
            obstacle.carveOnlyStationary = true;  // Only carve when not moving (performance)
        }

        // Register this hut as housing with PopulationManager (homeless colonists move in at once)
        housingCollider = GetComponent<Collider>();
        if (Faction.Population != null)
        {
            Faction.Population.RegisterHousing(this);
        }
    }

    /// <summary>
    /// Runs the moment the hut's health hits zero, a second before the object is actually
    /// destroyed, so nothing is left waiting on the corpse.
    /// </summary>
    void OnHutDestroyed()
    {
        // Immediately release the NavMesh carve and disable colliders so stacked
        // enemies can path out during the 1s fade-out instead of being trapped
        // inside the corpse's carved footprint. Fixes 3-4s retarget freeze.
        UnityEngine.AI.NavMeshObstacle obstacle = GetComponent<UnityEngine.AI.NavMeshObstacle>();
        if (obstacle != null) obstacle.enabled = false;
        Collider[] cols = GetComponentsInChildren<Collider>();
        for (int i = 0; i < cols.Length; i++) cols[i].enabled = false;

        // Notify enemies so they retarget instantly (destroyDelay keeps this object
        // in ActiveList for ~1s, but FindNearestBuilding already filters by IsAlive).
        OnAnyHutDestroyed?.Invoke();

        ReleaseHousing();

        // Check if workers are now homeless
        if (Faction.Population != null && Faction.Population.HasHomelessWorkers())
        {
            int homelessCount = Faction.Population.GetHomelessCount();
            Debug.LogWarning($"Hut: {homelessCount} workers are now HOMELESS! Build more huts.");
        }

        // Could add visual effects, resource drops, etc. here
    }

    /// <summary>
    /// Removes this hut's housing from the PopulationManager exactly once,
    /// whether the hut died in combat (OnHutDestroyed) or was demolished /
    /// destroyed directly (OnDestroy).
    /// </summary>
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
        ActiveRegistry<Hut>.Unregister(this);
        ReleaseHousing();
    }

    // Scene-view only: shows the clearance other buildings must respect.
    void OnDrawGizmosSelected()
    {
        // Draw no-build radius
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, noBuildRadius);
    }
}
