using UnityEngine;
using TMPro;

public class BuildingSelectionUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("The main panel containing all UI elements")]
    public GameObject selectionPanel;

    [Tooltip("Text showing the building name (e.g., 'Wooden Wall')")]
    public TextMeshProUGUI buildingNameText;

    [Tooltip("Text showing the cost (e.g., '15W 0F 5S')")]
    public TextMeshProUGUI buildingCostText;

    [Tooltip("Text showing hint about hotkeys (e.g., 'Press 1-4 to select')")]
    public TextMeshProUGUI hintText;

    [Header("Color Settings")]
    public Color affordableColor = Color.green;
    public Color unaffordableColor = Color.red;
    public Color defaultColor = Color.white;

    void Start()
    {
        // Start hidden
        Hide();
    }

    /// <summary>
    /// Update the UI display with building data and affordability
    /// </summary>
    public void UpdateDisplay(BuildingData data, bool canAfford)
    {
        if (data == null)
        {
            Debug.LogWarning("BuildingSelectionUI: Received null BuildingData");
            return;
        }

        // Update building name
        if (buildingNameText != null)
        {
            buildingNameText.text = data.buildingName;
            buildingNameText.color = defaultColor;
        }

        // Update cost text
        if (buildingCostText != null)
        {
            string costText = $"Cost: {data.woodCost}W";

            // Only show food if cost > 0
            if (data.foodCost > 0)
            {
                costText += $" {data.foodCost}F";
            }

            // Only show stone if cost > 0
            if (data.stoneCost > 0)
            {
                costText += $" {data.stoneCost}S";
            }

            buildingCostText.text = costText;
            buildingCostText.color = canAfford ? affordableColor : unaffordableColor;
        }

        // Update hint text if exists
        if (hintText != null)
        {
            hintText.text = "Press 1-4 to select | Click to place";
            hintText.color = defaultColor;
        }
    }

    /// <summary>
    /// Show the selection panel
    /// </summary>
    public void Show()
    {
        if (selectionPanel != null)
        {
            selectionPanel.SetActive(true);
        }
    }

    /// <summary>
    /// Hide the selection panel
    /// </summary>
    public void Hide()
    {
        if (selectionPanel != null)
        {
            selectionPanel.SetActive(false);
        }
    }

    /// <summary>
    /// Toggle the selection panel visibility
    /// </summary>
    public void Toggle()
    {
        if (selectionPanel != null)
        {
            selectionPanel.SetActive(!selectionPanel.activeSelf);
        }
    }
}
