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

    // Dirty checking to avoid string rebuilds when values haven't changed
    private int lastWood = -1, lastFood = -1, lastStone = -1;
    private int lastPopWorkers = -1, lastPopCapacity = -1;

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

        // Only rebuild text when values actually change (avoids string allocations)
        int wood = ResourceManager.Instance.GetWood();
        int food = ResourceManager.Instance.GetFood();
        int stone = ResourceManager.Instance.GetStone();

        if (woodText != null && wood != lastWood)
        {
            lastWood = wood;
            woodText.text = $"Wood: {wood}";
        }

        if (foodText != null && food != lastFood)
        {
            lastFood = food;
            foodText.text = $"Food: {food}";
        }

        if (stoneText != null && stone != lastStone)
        {
            lastStone = stone;
            stoneText.text = $"Stone: {stone}";
        }

        // Update population display
        if (populationText != null && PopulationManager.Instance != null)
        {
            int currentWorkers = PopulationManager.Instance.GetCurrentWorkers();
            int housingCapacity = PopulationManager.Instance.GetHousingCapacity();

            if (currentWorkers != lastPopWorkers || housingCapacity != lastPopCapacity)
            {
                lastPopWorkers = currentWorkers;
                lastPopCapacity = housingCapacity;
                populationText.text = $"Workers: {currentWorkers}/{housingCapacity}";

                // Color code based on housing status
                if (PopulationManager.Instance.HasHomelessWorkers())
                {
                    populationText.color = Color.red;
                }
                else if (currentWorkers >= housingCapacity)
                {
                    populationText.color = Color.yellow;
                }
                else
                {
                    populationText.color = Color.white;
                }
            }
        }
    }

}
