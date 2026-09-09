using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// The Workshop (2026-08-26): a crafting building. Since the research split
/// (2026-09-03) it is a <see cref="CraftStation"/> like the campfire — click it
/// to open the station panel — and the one that lists the Workshop-tier
/// research (Sharpened Tools, Sturdy Scaffolds, Iron Work). It makes tools and
/// weapons at 2× (2026-09-04) and is where a Crafter colonist works. Buildable via the
/// normal placement flow (key 5) once <i>Crafting</i> is researched — assets and
/// BuildingData are created by Tools &gt; Island RTS &gt; Session Content &gt;
/// Setup Pickups + Workshop.
/// </summary>
public class Workshop : MonoBehaviour, ITargetable, IMaterialSet
{
    public static IReadOnlyList<Workshop> ActiveList => ActiveRegistry<Workshop>.List;

    [Header("Health")]
    public float maxHealth = 150f;
    private Health healthComponent;
    public Health CachedHealth => healthComponent;

    [Header("Building Placement")]
    public float noBuildRadius = 3.5f;

    [Header("Hover Effect")]
    [Tooltip("Glow strength while the mouse is over the Workshop (see HoverGlow).")]
    public float hoverGlow = 2.2f;

    private Material[] buildingMaterials;
    private HoverGlow glow;

    /// <summary>The bench (runtime-added, so the prefab never carries a stale copy).</summary>
    public CraftStation Station { get; private set; }

    void Awake()
    {
        ActiveRegistry<Workshop>.Register(this);

        Station = GetComponent<CraftStation>();
        if (Station == null) Station = gameObject.AddComponent<CraftStation>();
        Station.tier = ResearchCatalog.Station.Workshop;
        // Tool, Weapon, Construction, Research: the fast bench for MAKING things
        // (2026-09-04); research runs at the same 1× the fire manages.
        Station.speeds = new[] { 2f, 2f, 1f, 1f };
        Station.displayName = "Workshop";
    }

    void Start()
    {
        healthComponent = GetComponent<Health>();
        if (healthComponent == null)
        {
            healthComponent = gameObject.AddComponent<Health>();
        }
        healthComponent.maxHealth = maxHealth;
        healthComponent.currentHealth = maxHealth;
        healthComponent.destroyOnDeath = true;
        healthComponent.destroyDelay = 1f;
        healthComponent.showHealthText = true;
        healthComponent.showObjectName = true;
        healthComponent.hideWhenFull = true;

        // Carve so units path around it (same as every other building)
        UnityEngine.AI.NavMeshObstacle obstacle = GetComponent<UnityEngine.AI.NavMeshObstacle>();
        if (obstacle != null)
        {
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;
        }

        EnsureMaterials();
        OccluderCutout.AttachTo(gameObject);
    }

    /// <summary>
    /// The instanced material copies for every renderer slot, created on first use and
    /// shared with the occlusion fade — two collectors on one object would write to
    /// different copies and the hover glow and the fade would fight.
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

    void OnMouseDown()
    {
        if (GameStartController.IntroInProgress) return;
        if (PauseController.BlockGameplayInput) return;
        WorkerAssignmentUI ui = WorkerAssignmentUI.Instance;
        if (ui != null) ui.OpenStation(Station);
    }

    void OnDestroy()
    {
        ActiveRegistry<Workshop>.Unregister(this);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, noBuildRadius);
    }
}
