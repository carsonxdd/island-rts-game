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
    public bool hideWhenFull = true;
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

        Debug.Log($"HealthBar: Created for {gameObject.name}");
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

        // Hide/show logic
        bool shouldShow = showHealthBar && healthComponent.IsAlive;

        if (hideWhenFull && healthPercent >= 0.99f)
        {
            shouldShow = false;
        }

        if (hideWhenDead && !healthComponent.IsAlive)
        {
            shouldShow = false;
        }

        if (barContainer != null && shouldShow != lastShouldShow)
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
