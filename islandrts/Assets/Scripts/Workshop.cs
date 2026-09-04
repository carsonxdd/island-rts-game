using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// The Workshop (2026-08-26): a crafting building. Since the research split
/// (2026-09-03) it is a <see cref="CraftStation"/> like the campfire — click it
/// to open the station panel — and the one that lists the Workshop-tier
/// research (Sharpened Tools, Sturdy Scaffolds). Its speed table is 1× until
/// Slice 3 makes it the fast bench and adds the Crafter job. Buildable via the
/// normal placement flow (key 5) once <i>Crafting</i> is researched — assets and
/// BuildingData are created by Tools &gt; Island RTS &gt; Session Content &gt;
/// Setup Pickups + Workshop.
/// </summary>
public class Workshop : MonoBehaviour, ITargetable
{
    public static IReadOnlyList<Workshop> ActiveList => ActiveRegistry<Workshop>.List;

    [Header("Health")]
    public float maxHealth = 150f;
    private Health healthComponent;
    public Health CachedHealth => healthComponent;

    [Header("Building Placement")]
    public float noBuildRadius = 3.5f;

    [Header("Hover Effect")]
    public Color hoverColor = new Color(1f, 1f, 0.7f, 1f);

    private Material[] buildingMaterials;
    private Color[] originalColors;

    /// <summary>The bench (runtime-added, so the prefab never carries a stale copy).</summary>
    public CraftStation Station { get; private set; }

    void Awake()
    {
        ActiveRegistry<Workshop>.Register(this);

        Station = GetComponent<CraftStation>();
        if (Station == null) Station = gameObject.AddComponent<CraftStation>();
        Station.tier = ResearchCatalog.Station.Workshop;
        Station.speeds = new[] { 1f, 1f, 1f, 1f };   // Slice 3: 2× Tool / Weapon
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

        buildingMaterials = RendererTint.Collect(GetComponentsInChildren<Renderer>());
        originalColors = RendererTint.CaptureColors(buildingMaterials);
    }

    void OnMouseEnter() { RendererTint.SetColor(buildingMaterials, hoverColor); }
    void OnMouseExit() { RendererTint.RestoreColors(buildingMaterials, originalColors); }

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
