using UnityEngine;
using UnityEngine.Events;
using TMPro;

public class Health : MonoBehaviour
{
    [Header("Health Settings")]
    public float maxHealth = 100f;
    public float currentHealth;

    [Header("Destruction")]
    public bool destroyOnDeath = true;
    public float destroyDelay = 0f;

    [Header("Health Display")]
    public bool showHealthText = true;
    public float healthTextHeight = 2.5f;
    public bool showObjectName = true;

    [Header("Events")]
    public UnityEvent onDeath;  // Triggered when health reaches 0
    public UnityEvent onDamaged; // Triggered when taking damage

    // Private
    private TextMeshPro healthText;
    private GameObject healthTextObject;

    // Public property to check if alive
    public bool IsAlive => currentHealth > 0;

    void Awake()
    {
        // Initialize events
        if (onDeath == null)
            onDeath = new UnityEvent();
        if (onDamaged == null)
            onDamaged = new UnityEvent();
    }

    void Start()
    {
        // Initialize health to max
        currentHealth = maxHealth;
        Debug.Log($"Health: {gameObject.name} initialized with {maxHealth} HP");

        // Create health text display
        if (showHealthText)
        {
            CreateHealthText();
        }
    }

    void Update()
    {
        // Update health text display
        if (showHealthText && healthText != null)
        {
            UpdateHealthText();
        }
    }

    /// <summary>
    /// Apply damage to this object
    /// </summary>
    public void TakeDamage(float damageAmount)
    {
        if (!IsAlive)
        {
            return; // Already dead, ignore
        }

        currentHealth -= damageAmount;
        currentHealth = Mathf.Max(currentHealth, 0); // Clamp to 0

        Debug.Log($"Health: {gameObject.name} took {damageAmount} damage. Health: {currentHealth}/{maxHealth}");

        // Trigger damaged event
        onDamaged?.Invoke();

        // Check for death
        if (currentHealth <= 0)
        {
            Die();
        }
    }

    /// <summary>
    /// Heal this object
    /// </summary>
    public void Heal(float healAmount)
    {
        if (!IsAlive)
        {
            return; // Can't heal the dead
        }

        currentHealth += healAmount;
        currentHealth = Mathf.Min(currentHealth, maxHealth); // Clamp to max

        Debug.Log($"Health: {gameObject.name} healed {healAmount}. Health: {currentHealth}/{maxHealth}");
    }

    /// <summary>
    /// Get health as a percentage (0 to 1)
    /// </summary>
    public float GetHealthPercentage()
    {
        return maxHealth > 0 ? currentHealth / maxHealth : 0;
    }

    void Die()
    {
        Debug.Log($"Health: {gameObject.name} has died!");

        // Trigger death event
        onDeath?.Invoke();

        // Destroy if configured
        if (destroyOnDeath)
        {
            Destroy(gameObject, destroyDelay);
        }
    }

    void CreateHealthText()
    {
        // Create a child GameObject for the text
        healthTextObject = new GameObject("HealthText");
        healthTextObject.transform.parent = transform;
        healthTextObject.transform.localPosition = new Vector3(0, healthTextHeight, 0);

        // Add TextMeshPro component
        healthText = healthTextObject.AddComponent<TextMeshPro>();
        healthText.fontSize = 2.5f;
        healthText.alignment = TextAlignmentOptions.Center;
        healthText.color = Color.green;

        // Set sorting to render on top
        healthText.GetComponent<MeshRenderer>().sortingOrder = 100;

        UpdateHealthText();  // Set initial text
    }

    void UpdateHealthText()
    {
        if (healthText == null)
            return;

        // Update text content
        string objectName = showObjectName ? $"{gameObject.name}\n" : "";
        healthText.text = $"{objectName}{currentHealth:F0} / {maxHealth:F0} HP";

        // Color based on health percentage
        float healthPercent = GetHealthPercentage();
        if (!IsAlive)
        {
            healthText.text = showObjectName ? $"{gameObject.name}\nDESTROYED!" : "DESTROYED!";
            healthText.color = Color.red;
            healthText.fontSize = 3f;
        }
        else if (healthPercent > 0.6f)
        {
            healthText.color = Color.green;
        }
        else if (healthPercent > 0.3f)
        {
            healthText.color = Color.yellow;
        }
        else
        {
            healthText.color = Color.red;
        }

        // Billboard effect - always face camera
        if (Camera.main != null)
        {
            healthTextObject.transform.LookAt(Camera.main.transform);
            healthTextObject.transform.Rotate(0, 180, 0);  // Flip to face camera correctly
        }
    }

    // Visual debug in inspector
    void OnValidate()
    {
        // Ensure max health is positive
        if (maxHealth <= 0)
        {
            maxHealth = 1f;
        }
    }
}
