using UnityEngine;

/// <summary>
/// Handles camera shake effects for combat impacts
/// Attach to main camera
/// </summary>
public class CameraShake : MonoBehaviour
{
    public static CameraShake Instance { get; private set; }

    [Header("Shake Settings")]
    public bool enableShake = true;
    public float shakeDuration = 0.2f;
    public float shakeIntensity = 0.1f;
    public float shakeDecay = 1f; // How quickly shake fades

    [Header("Shake Types")]
    public float lightShakeIntensity = 0.05f;   // UI clicks, minor hits
    public float mediumShakeIntensity = 0.1f;   // Combat hits
    public float heavyShakeIntensity = 0.25f;   // Deaths, explosions

    // Private
    private Vector3 originalPosition;
    private float currentShakeDuration = 0f;
    private float currentShakeIntensity = 0f;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(this);
        }
    }

    void Start()
    {
        // Store original camera position
        originalPosition = transform.localPosition;
    }

    void LateUpdate()
    {
        if (!enableShake || currentShakeDuration <= 0)
        {
            return;
        }

        // Decrease shake duration
        currentShakeDuration -= Time.deltaTime;

        if (currentShakeDuration > 0)
        {
            // Calculate shake amount
            float shakeAmount = currentShakeIntensity * (currentShakeDuration / shakeDuration);

            // Random shake offset
            Vector3 shakeOffset = new Vector3(
                Random.Range(-1f, 1f) * shakeAmount,
                Random.Range(-1f, 1f) * shakeAmount,
                0f
            );

            // Apply shake
            transform.localPosition = originalPosition + shakeOffset;
        }
        else
        {
            // Reset to original position
            currentShakeDuration = 0f;
            transform.localPosition = originalPosition;
        }
    }

    /// <summary>
    /// Trigger a custom shake effect
    /// </summary>
    public void Shake(float intensity, float duration)
    {
        if (!enableShake) return;

        // Store current position when shake starts (not the original Start position)
        if (currentShakeDuration <= 0)
        {
            originalPosition = transform.localPosition;
        }

        currentShakeIntensity = intensity;
        currentShakeDuration = duration;
    }

    /// <summary>
    /// Light shake (UI, minor events)
    /// </summary>
    public void ShakeLight()
    {
        Shake(lightShakeIntensity, shakeDuration * 0.5f);
    }

    /// <summary>
    /// Medium shake (combat hits)
    /// </summary>
    public void ShakeMedium()
    {
        Shake(mediumShakeIntensity, shakeDuration);
    }

    /// <summary>
    /// Heavy shake (deaths, explosions)
    /// </summary>
    public void ShakeHeavy()
    {
        Shake(heavyShakeIntensity, shakeDuration * 1.5f);
    }

    /// <summary>
    /// Reset camera to original position
    /// </summary>
    public void ResetPosition()
    {
        currentShakeDuration = 0f;
        transform.localPosition = originalPosition;
    }

    /// <summary>
    /// Update the stored original position (call if camera moves)
    /// </summary>
    public void UpdateOriginalPosition()
    {
        originalPosition = transform.localPosition;
    }
}
