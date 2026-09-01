using UnityEngine;

/// <summary>
/// Creates a visual health bar above a unit using simple quads
/// Automatically syncs with Health component
/// </summary>
public class HealthBar : MonoBehaviour
{
    [Header("Settings")]
    public bool showHealthBar = true;
    public float heightOffset = 2.5f;
    public float barWidth = 1f;
    public float barHeight = 0.15f;

    [Header("Colors")]
    public Color highHealthColor = Color.green;
    public Color mediumHealthColor = Color.yellow;
    public Color lowHealthColor = Color.red;
    public Color backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.8f);

    [Header("Behavior")]
    // hideWhenFull used to live here. It is now GameSettings.HealthBarMode:
    // every prefab set the flag identically and no player could reach it, so it
    // was a preference wearing a prefab field's clothes. Stale serialized
    // values in the prefabs are ignored and drop out on the next save.
    public bool hideWhenDead = true;

    // Private
    private Health healthComponent;
    private GameObject barContainer;
    private GameObject backgroundBar;
    private GameObject healthFillBar;
    private MeshRenderer backgroundRenderer;
    private MeshRenderer fillRenderer;
    private Camera cachedCamera;
    private float lastHealthPercent = -1f;
    private bool lastShouldShow = true;

    void Start()
    {
        // Nothing renders in a headless balance run; skip the two quads.
        if (SimHooks.Simulating) { enabled = false; return; }

        // Get Health component
        healthComponent = GetComponent<Health>();
        if (healthComponent == null)
        {
            Debug.LogWarning("HealthBar: No Health component found!");
            enabled = false;
            return;
        }

        cachedCamera = Camera.main;

        if (showHealthBar)
        {
            CreateHealthBar();
        }
    }

    void Update()
    {
        if (healthFillBar == null || healthComponent == null)
            return;

        // Billboard rotation every frame (zero allocations)
        if (cachedCamera != null && barContainer != null && barContainer.activeSelf)
        {
            barContainer.transform.LookAt(cachedCamera.transform);
            barContainer.transform.Rotate(0, 180, 0);
        }

        // Only update bar content when health actually changes
        float currentPercent = healthComponent.GetHealthPercentage();
        if (currentPercent != lastHealthPercent)
        {
            lastHealthPercent = currentPercent;
            UpdateHealthBar();
        }
        else
        {
            // The Health Bars setting can change without any health changing.
            RefreshVisibility();
        }
    }

    void CreateHealthBar()
    {
        // Create container
        barContainer = new GameObject("HealthBarContainer");
        barContainer.transform.SetParent(transform);
        barContainer.transform.localPosition = Vector3.up * heightOffset;

        // Create background quad
        backgroundBar = GameObject.CreatePrimitive(PrimitiveType.Quad);
        backgroundBar.name = "BackgroundBar";
        backgroundBar.transform.SetParent(barContainer.transform);
        backgroundBar.transform.localPosition = Vector3.zero;
        backgroundBar.transform.localScale = new Vector3(barWidth, barHeight, 1f);

        backgroundRenderer = backgroundBar.GetComponent<MeshRenderer>();
        backgroundRenderer.material = new Material(Shader.Find("Sprites/Default"));
        backgroundRenderer.material.color = backgroundColor;
        backgroundRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        backgroundRenderer.receiveShadows = false;

        // Remove collider
        Destroy(backgroundBar.GetComponent<Collider>());

        // Create health fill quad
        healthFillBar = GameObject.CreatePrimitive(PrimitiveType.Quad);
        healthFillBar.name = "HealthFillBar";
        healthFillBar.transform.SetParent(barContainer.transform);
        healthFillBar.transform.localPosition = new Vector3(0, 0, -0.01f); // Slightly in front
        healthFillBar.transform.localScale = new Vector3(barWidth, barHeight, 1f);

        fillRenderer = healthFillBar.GetComponent<MeshRenderer>();
        fillRenderer.material = new Material(Shader.Find("Sprites/Default"));
        fillRenderer.material.color = highHealthColor;
        fillRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        fillRenderer.receiveShadows = false;
        fillRenderer.sortingOrder = 1; // Render on top of background

        // Remove collider
        Destroy(healthFillBar.GetComponent<Collider>());
    }

    void UpdateHealthBar()
    {
        if (healthComponent == null || healthFillBar == null)
            return;

        float healthPercent = lastHealthPercent;

        // Update fill bar scale (shrink from left to right)
        Vector3 fillScale = healthFillBar.transform.localScale;
        fillScale.x = barWidth * healthPercent;
        healthFillBar.transform.localScale = fillScale;

        // Offset position to keep bar left-aligned
        float offset = (barWidth - fillScale.x) / 2f;
        healthFillBar.transform.localPosition = new Vector3(-offset, 0, -0.01f);

        // Update color based on health percentage
        if (healthPercent > 0.6f)
        {
            fillRenderer.material.color = highHealthColor;
        }
        else if (healthPercent > 0.3f)
        {
            fillRenderer.material.color = mediumHealthColor;
        }
        else
        {
            fillRenderer.material.color = lowHealthColor;
        }

        RefreshVisibility();
    }

    /// <summary>
    /// Whether the bar should currently be on screen.
    ///
    /// Called every frame rather than only when health changes, because the
    /// player can change the Health Bars setting from the pause menu — a bar
    /// that only re-evaluated on damage would stay wrong until something hit
    /// the unit. It is a handful of boolean comparisons and one cached
    /// percentage; nothing here allocates or searches.
    /// </summary>
    private void RefreshVisibility()
    {
        if (barContainer == null || healthComponent == null) return;

        bool alive = healthComponent.IsAlive;
        bool shouldShow = showHealthBar && alive;

        if (hideWhenDead && !alive) shouldShow = false;

        // GameSettings.HealthBarMode owns the "hide at full health" rule now —
        // it replaced the old per-prefab hideWhenFull flag, which every prefab
        // set the same way and no player could reach.
        if (shouldShow)
        {
            switch (GameSettings.HealthBarMode)
            {
                case GameSettings.HealthBars.Never:
                    shouldShow = false;
                    break;
                case GameSettings.HealthBars.WhenDamaged:
                    if (healthComponent.GetHealthPercentage() >= 0.99f) shouldShow = false;
                    break;
            }
        }

        if (shouldShow != lastShouldShow)
        {
            lastShouldShow = shouldShow;
            barContainer.SetActive(shouldShow);
        }
    }

    void OnDestroy()
    {
        if (barContainer != null)
        {
            Destroy(barContainer);
        }
    }
}
