using UnityEngine;
using TMPro;

public class ResourceUI : MonoBehaviour
{
    [Header("UI Text References")]
    public TMP_Text woodText;
    public TMP_Text foodText;
    public TMP_Text stoneText;

    [Header("Update Settings")]
    public float updateInterval = 0.1f;  // Update UI every 0.1 seconds

    private float timeSinceUpdate = 0f;

    void Update()
    {
        // Only update UI periodically (not every frame for performance)
        timeSinceUpdate += Time.deltaTime;

        if (timeSinceUpdate >= updateInterval)
        {
            timeSinceUpdate = 0f;
            UpdateUI();
        }
    }

    void UpdateUI()
    {
        if (ResourceManager.Instance == null)
        {
            Debug.LogWarning("ResourceUI: No ResourceManager found!");
            return;
        }

        // Update text displays
        if (woodText != null)
        {
            woodText.text = $"Wood: {ResourceManager.Instance.GetWood()}";
        }

        if (foodText != null)
        {
            foodText.text = $"Food: {ResourceManager.Instance.GetFood()}";
        }

        if (stoneText != null)
        {
            stoneText.text = $"Stone: {ResourceManager.Instance.GetStone()}";
        }
    }

    // Force immediate update (useful when resources change)
    public void ForceUpdate()
    {
        UpdateUI();
    }
}
