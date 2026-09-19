using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// Defensive support building: extends enemy detection around it and buffs the damage of
/// warriors fighting nearby. Contributes no housing and holds no garrison.
/// </summary>
public class Watchtower : MonoBehaviour, ITargetable, IBuildingIdentity
{
    public BuildingType BuildingType => BuildingType.Watchtower;

    // Owner (lap step 1 commit 5). Set by Spawn.Owned right after Instantiate
    // (commit 6); the player's when nothing set it. Read in Start or later, never Awake.
    Faction owner;
    public Faction Faction { get => owner ?? (owner = Factions.Player); set => owner = value; }
    [Header("Health")]
    public float maxHealth = 200f;
    private Health healthComponent;
    public Health CachedHealth => healthComponent;

    [Header("Building Placement")]
    public float noBuildRadius = 3.0f;

    [Header("Detection")]
    [Tooltip("Early warning system - reveals enemies at greater distance")]
    public float detectionRadius = 20f;

    [Header("Damage Buff Aura")]
    [Tooltip("Warriors within this radius get a damage buff")]
    public float buffRadius = 8f;
    [Tooltip("Damage multiplier for warriors in range (1.25 = 25% buff)")]
    public float damageMultiplier = 1.25f;

    /// <summary>
    /// True while the Watchtower is a pure vision building (2026-09-10). Flipped
    /// to false by the archer-tower upgrade path, not by a setting.
    /// </summary>
    public const bool AuraDisabled = true;

    public static IReadOnlyList<Watchtower> ActiveList => ActiveRegistry<Watchtower>.List;

    void Awake() { ActiveRegistry<Watchtower>.Register(this); }

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

        // The tallest thing the colony builds, so it hides more than anything else and
        // opens a window for any unit behind it (2026-09-08).
        OccluderCutout.AttachTo(gameObject);
        // The tower's second job (2026-09-09): it sees far further than anything else.
        if (Faction.IsPlayer) VisionSource.Attach(gameObject, VisionSource.WatchtowerRadius);
        else FogVisibility.Attach(gameObject, FogVisibility.Rule.Visible);   // another colony's: shown only on watched ground, sees nothing for the player

        healthComponent.destroyOnDeath = true;
        healthComponent.destroyDelay = 1f;
        healthComponent.showHealthText = true;
        healthComponent.showObjectName = true;
        healthComponent.hideWhenFull = true;

        // Disable NavMeshObstacle carving (like other buildings - enemies can surround)
        NavMeshObstacle obstacle = GetComponent<NavMeshObstacle>();
        if (obstacle != null)
        {
            obstacle.carving = false;  // Enemies can surround it from all sides
        }
    }

    /// <summary>
    /// Returns the best damage multiplier for a unit at the given position.
    /// Checks all watchtowers and returns the highest multiplier if in range, or 1.0 if not.
    /// </summary>
    public static float GetDamageMultiplier(Vector3 position)
    {
        float bestMultiplier = 1f;

        // Vision only (2026-09-10): the tower sees and warns, it does not fight.
        // The aura fields stay serialized because the same numbers become the
        // archer-tower upgrade (Watchtower -> archer tower -> cannon tower) when
        // that path exists; until then no warrior hits harder for standing here.
#pragma warning disable 0162   // the const gate makes the scan unreachable on purpose
        if (AuraDisabled) return bestMultiplier;

        for (int i = 0; i < ActiveList.Count; i++)
        {
            Watchtower tower = ActiveList[i];
            if (tower == null) continue;
            if (tower.healthComponent != null && !tower.healthComponent.IsAlive) continue;

            float distance = Vector3.Distance(position, tower.transform.position);
            if (distance <= tower.buffRadius && tower.damageMultiplier > bestMultiplier)
            {
                bestMultiplier = tower.damageMultiplier;
            }
        }

        return bestMultiplier;
#pragma warning restore 0162
    }

    /// <summary>
    /// Left-click opens the selected-building card (2026-09-18): tier, health, what it
    /// does, and the Upgrade / Demolish pair. Left button only - right-click is the
    /// castaway's command gesture and must never open a panel. Player-owned only:
    /// a rival's building opens nothing, the way its campfire and colonists do not.
    /// </summary>
    void OnMouseDown()
    {
        if (GameStartController.IntroInProgress) return;
        if (PauseController.BlockGameplayInput || PointerBlock.OverHud) return;
        if (!Faction.IsPlayer) return;
        SelectedBuildingHUD.Show(gameObject);
    }

    void OnDestroy()
    {
        ActiveRegistry<Watchtower>.Unregister(this);
    }

    // Visual feedback in Scene view
    void OnDrawGizmosSelected()
    {
        // Draw no-build radius
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, noBuildRadius);

        // Draw detection radius
        Gizmos.color = new Color(1f, 1f, 0f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        // Draw buff radius
        Gizmos.color = new Color(0.3f, 1f, 0.3f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, buffRadius);
    }
}
