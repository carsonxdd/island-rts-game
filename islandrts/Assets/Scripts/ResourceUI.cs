using UnityEngine;
using TMPro;

public class ResourceUI : MonoBehaviour
{
    [Header("UI Text References")]
    public TMP_Text woodText;
    public TMP_Text foodText;
    public TMP_Text stoneText;
    public TMP_Text populationText;  // Display worker population

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
        // Wait for ResourceManager to initialize
        if (ResourceManager.Instance == null)
        {
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

        // Update population display
        if (populationText != null && PopulationManager.Instance != null)
        {
            int currentWorkers = PopulationManager.Instance.GetCurrentWorkers();
            int housingCapacity = PopulationManager.Instance.GetHousingCapacity();

            populationText.text = $"Workers: {currentWorkers}/{housingCapacity}";

            // Color code based on housing status
            if (PopulationManager.Instance.HasHomelessWorkers())
            {
                // Red if workers are homeless
                populationText.color = Color.red;
            }
            else if (currentWorkers >= housingCapacity)
            {
                // Yellow if at capacity
                populationText.color = Color.yellow;
            }
            else
            {
                // White if there's room
                populationText.color = Color.white;
            }
        }
    }

    // Force immediate update (useful when resources change)
    public void ForceUpdate()
    {
        UpdateUI();
    }
}
