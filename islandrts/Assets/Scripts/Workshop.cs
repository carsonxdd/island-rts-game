using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Crafting building (2026-08-26): click to open the CraftingUI and craft
/// one-time global upgrades (see CraftedUpgrades). One craft runs at a time;
/// resources are spent up front. Buildable via the normal placement flow
/// (key 5 in build mode) — assets and BuildingData are created by
/// Tools &gt; Island RTS &gt; Session Content &gt; Setup Pickups + Workshop.
/// </summary>
public class Workshop : MonoBehaviour, ITargetable
{
    public static IReadOnlyList<Workshop> ActiveList => ActiveRegistry<Workshop>.List;

    void Awake() { ActiveRegistry<Workshop>.Register(this); }

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

    // Active craft
    private CraftedUpgrades.Recipe activeRecipe;
    private float craftProgress;  // seconds accumulated

    public CraftedUpgrades.Recipe ActiveRecipe => activeRecipe;
    public float ActiveProgress01 =>
        activeRecipe == null ? 0f : Mathf.Clamp01(craftProgress / activeRecipe.craftSeconds);

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

    void Update()
    {
        if (activeRecipe == null) return;

        craftProgress += Time.deltaTime;
        if (craftProgress >= activeRecipe.craftSeconds)
        {
            activeRecipe.crafted = true;
            activeRecipe.apply?.Invoke();
            activeRecipe = null;
            craftProgress = 0f;

            if (AudioManager.Instance != null)
                AudioManager.Instance.PlayBuildingPlaced();
        }
    }

    /// <summary>
    /// Try to begin crafting (spends resources up front).
    /// False when busy, already crafted, or unaffordable.
    /// </summary>
    public bool TryStartCraft(CraftedUpgrades.Recipe recipe)
    {
        if (recipe == null || recipe.crafted || activeRecipe != null) return false;
        if (ResourceManager.Instance == null) return false;
        if (!ResourceManager.Instance.SpendResources(recipe.woodCost, recipe.foodCost, recipe.stoneCost))
            return false;

        activeRecipe = recipe;
        craftProgress = 0f;
        return true;
    }

    void OnMouseEnter() { RendererTint.SetColor(buildingMaterials, hoverColor); }
    void OnMouseExit() { RendererTint.RestoreColors(buildingMaterials, originalColors); }

    void OnMouseDown()
    {
        // Don't open the panel while placing buildings / demolishing
        if (GameStartController.IntroInProgress) return;
        CraftingUI.Open(this);
    }

    void OnDestroy()
    {
        ActiveRegistry<Workshop>.Unregister(this);
        if (CraftingUI.CurrentWorkshop == this) CraftingUI.Close();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, noBuildRadius);
    }
}
