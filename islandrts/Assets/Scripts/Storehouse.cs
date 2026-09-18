using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// The Storehouse (2026-09-16): a drop-off point and a little more room. A
/// colonist returning a load walks to the NEAREST drop-off of its colony
/// (<see cref="Dropoff.Nearest"/>) — this or the campfire — so a Storehouse
/// beside a far forest turns a sixty-metre walk per five wood into a short one.
/// The castaway right-clicks it to empty their hands too.
///
/// ONE colony store, by decision: whatever is dropped here lands in the
/// faction pool and the CAMPFIRE stockpile at once. It holds no inventory of
/// its own and nothing is hauled between the two, so the campfire stays the
/// crafting place and every stockpile read stays where it is. Each standing
/// Storehouse adds <see cref="RoomBonus"/> to that stockpile's capacity
/// (<c>BaseBuilding.StockpileCapacity</c>, read live).
///
/// Unlocked by Storage Pits (<see cref="Unlocks.Kind.Storage"/>); build key 7.
/// Raiders treat it like a hut (a reachable building tier target), so a
/// Storehouse out by the trees is a thing to defend. Assets and BuildingData
/// come from Tools &gt; Island RTS &gt; Session Content &gt; Setup Pickups + Workshop.
/// </summary>
public class Storehouse : MonoBehaviour, ITargetable, IMaterialSet, IDropoff
{
    // Owner (lap step 1). Set by Spawn.Owned right after Instantiate; the
    // player's when nothing set it. Read in Start or later, never Awake.
    Faction owner;
    public Faction Faction { get => owner ?? (owner = Factions.Player); set => owner = value; }
    public static IReadOnlyList<Storehouse> ActiveList => ActiveRegistry<Storehouse>.List;

    /// <summary>Stockpile room each standing Storehouse adds to the colony store.</summary>
    public const int RoomBonus = 20;

    [Header("Health")]
    public float maxHealth = 120f;
    private Health healthComponent;
    public Health CachedHealth => healthComponent;
    public bool IsAlive => healthComponent == null || healthComponent.IsAlive;

    [Header("Building Placement")]
    public float noBuildRadius = 3.0f;

    [Header("Hover Effect")]
    [Tooltip("Glow strength while the mouse is over the Storehouse (see HoverGlow).")]
    public float hoverGlow = 2.2f;

    private Material[] buildingMaterials;
    private HoverGlow glow;
    private Collider approachCollider;
    private DropoffRing ring;

    public Collider ApproachCollider => approachCollider != null ? approachCollider : (approachCollider = GetComponent<Collider>());
    private DropoffRing Ring => ring ?? (ring = new DropoffRing(transform, () => ApproachCollider));

    public int ClaimDropoffSlot(Worker worker, Vector3 from) => Ring.Claim(worker, from);
    public Vector3 DropoffPoint(int slot) => Ring.Point(slot);
    public void ReleaseDropoffSlot(Worker worker) => Ring.Release(worker);

    void Awake()
    {
        ActiveRegistry<Storehouse>.Register(this);
    }

    void Start()
    {
        healthComponent = GetComponent<Health>();
        if (healthComponent == null) healthComponent = gameObject.AddComponent<Health>();
        healthComponent.maxHealth = maxHealth;
        healthComponent.currentHealth = maxHealth;
        healthComponent.destroyOnDeath = true;
        healthComponent.destroyDelay = 1f;
        healthComponent.showHealthText = true;
        healthComponent.showObjectName = true;
        healthComponent.hideWhenFull = true;

        // Carve so units path around it; the drop-off points are approach points
        // on its edge, the campfire pattern (a centre target never "arrives").
        UnityEngine.AI.NavMeshObstacle obstacle = GetComponent<UnityEngine.AI.NavMeshObstacle>();
        if (obstacle != null)
        {
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;
        }

        EnsureMaterials();
        OccluderCutout.AttachTo(gameObject);
        if (Faction.IsPlayer) VisionSource.Attach(gameObject, VisionSource.HutRadius);
        else FogVisibility.Attach(gameObject, FogVisibility.Rule.Visible);   // another colony's: shown only on watched ground
    }

    /// <summary>
    /// The instanced material copies for every renderer slot, created on first use
    /// and shared with the occluder cutout (one collector per object).
    /// </summary>
    public Material[] EnsureMaterials()
    {
        if (buildingMaterials == null)
        {
            buildingMaterials = RendererTint.Collect(GetComponentsInChildren<Renderer>());
            glow = HoverGlow.Attach(gameObject, buildingMaterials, 0f, hoverGlow);
        }
        return buildingMaterials;
    }

    void OnMouseEnter() { if (glow != null) glow.SetHovered(true); }
    void OnMouseExit() { if (glow != null) glow.SetHovered(false); }

    void OnDestroy()
    {
        ActiveRegistry<Storehouse>.Unregister(this);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, noBuildRadius);
    }
}
