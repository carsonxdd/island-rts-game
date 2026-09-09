using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// The Shipyard (2026-09-04, Slice 6): the escape ship's slipway, buildable on
/// a beach once Shipwright is researched (key 6). Left-clicking it asks whether
/// to set sail; YES hands the run to <see cref="GameManager.TriggerEscape"/>,
/// which plays a short departure beat and ends the run as an escape.
/// </summary>
/// <remarks>
/// The <see cref="Workshop"/> shape: a targetable, carving building with a
/// hover glow and its own registry. Placement is the normal build flow with
/// <c>BuildingData.requiresShore</c>; the beach rule lives in the ghost, not here.
/// Raiders target it like a hut or a Workshop, and it counts eight prosperity —
/// a colony that can afford a ship is a colony worth raiding.
/// </remarks>
public class Shipyard : MonoBehaviour, ITargetable, IMaterialSet
{
    public static IReadOnlyList<Shipyard> ActiveList => ActiveRegistry<Shipyard>.List;

    [Header("Health")]
    public float maxHealth = 300f;
    private Health healthComponent;
    public Health CachedHealth => healthComponent;

    [Header("Building Placement")]
    public float noBuildRadius = 5f;

    [Header("Hover Effect")]
    [Tooltip("Glow strength while the mouse is over the Shipyard (see HoverGlow).")]
    public float hoverGlow = 2.2f;

    [Header("Escape")]
    [Tooltip("The ship that slides out to sea on Set Sail (art prefab; wired by Setup Pickups + Workshop).")]
    public GameObject shipArtPrefab;

    /// <summary>Unit vector from the slipway toward open water, found in Start from the heightfield.</summary>
    public Vector3 LaunchDirection { get; private set; } = Vector3.right;

    private Material[] buildingMaterials;
    private HoverGlow glow;

    void Awake()
    {
        ActiveRegistry<Shipyard>.Register(this);
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

        UnityEngine.AI.NavMeshObstacle obstacle = GetComponent<UnityEngine.AI.NavMeshObstacle>();
        if (obstacle != null)
        {
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;
        }

        if (TerrainGrid.Instance != null)
            LaunchDirection = TerrainGrid.Instance.DirectionToWater(transform.position, GhostPlacer.ShoreRadius + 2f);

        EnsureMaterials();
        OcclusionFade.AttachTo(gameObject, Workshop.BuildingTightness);
    }

    /// <summary>
    /// The instanced material copies for every renderer slot, created on first use and
    /// shared with the occlusion fade so the hover glow and the fade write to the same
    /// instances.
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
        if (SimHooks.Simulating) return;
        if (GameManager.Instance == null || GameManager.Instance.isGameOver) return;

        int day = GameManager.Instance.currentDay;
        MenuScreens.Ensure().AskSetSail(day, SetSail);
    }

    /// <summary>Leave the island now. The confirm-free entry the sim and the F4 menu use.</summary>
    public void SetSail()
    {
        if (GameManager.Instance != null) GameManager.Instance.TriggerEscape(this);
    }

    void OnDestroy()
    {
        ActiveRegistry<Shipyard>.Unregister(this);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, noBuildRadius);
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, transform.position + LaunchDirection * 8f);
    }
}
