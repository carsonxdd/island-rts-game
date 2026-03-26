using UnityEngine;

/// <summary>
/// Handles camera shake effects for combat impacts.
/// Attach to main camera.
/// Uses a pure offset approach: reads the current position each frame,
/// adds a shake offset, then removes it next frame — never fights CameraController.
/// </summary>
public class CameraShake : MonoBehaviour
{
    public static CameraShake Instance { get; private set; }

    [Header("Shake Settings")]
    public bool enableShake = true;
    public float shakeDuration = 0.2f;
    public float shakeIntensity = 0.1f;

    [Header("Shake Types")]
    public float lightShakeIntensity = 0.05f;   // UI clicks, minor hits
    public float mediumShakeIntensity = 0.1f;   // Combat hits
    public float heavyShakeIntensity = 0.25f;   // Deaths, explosions

    // Private
    private float currentShakeDuration = 0f;
    private float currentShakeIntensity = 0f;
    private float initialShakeDuration = 0f;  // For decay calculation
    private Vector3 lastShakeOffset = Vector3.zero;  // Offset applied last frame (to undo it)

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

    void LateUpdate()
    {
        // Undo last frame's shake offset first (restores true camera position)
        if (lastShakeOffset != Vector3.zero)
        {
            transform.localPosition -= lastShakeOffset;
            lastShakeOffset = Vector3.zero;
        }

        if (!enableShake || currentShakeDuration <= 0f)
        {
            return;
        }

        // Decrease shake duration
        currentShakeDuration -= Time.deltaTime;

        if (currentShakeDuration > 0f)
        {
            // Calculate shake amount with linear decay
            float shakeAmount = currentShakeIntensity * (currentShakeDuration / initialShakeDuration);

            // Random shake offset
            Vector3 shakeOffset = new Vector3(
                Random.Range(-1f, 1f) * shakeAmount,
                Random.Range(-1f, 1f) * shakeAmount,
                0f
            );

            // Apply offset additively (will be undone next frame)
            transform.localPosition += shakeOffset;
            lastShakeOffset = shakeOffset;
        }
        else
        {
            currentShakeDuration = 0f;
        }
    }

    /// <summary>
    /// Trigger a custom shake effect
    /// </summary>
    public void Shake(float intensity, float duration)
    {
        if (!enableShake) return;

        currentShakeIntensity = intensity;
        currentShakeDuration = duration;
        initialShakeDuration = duration;
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
    /// Reset camera shake immediately
    /// </summary>
    public void ResetPosition()
    {
        if (lastShakeOffset != Vector3.zero)
        {
            transform.localPosition -= lastShakeOffset;
            lastShakeOffset = Vector3.zero;
        }
        currentShakeDuration = 0f;
    }
}
