using UnityEngine;
using TMPro;

/// <summary>
/// The build-mode palette: lists the placeable buildings with their costs and highlights
/// the selected one. Display only - the number keys and BuildPlacement own the selection.
/// </summary>
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
            hintText.text = "1-4: Select | R: Rotate | Click: Place";
            hintText.color = defaultColor;
        }
    }

    /// <summary>
    /// Update the UI for wall line drawing mode (shows count and total cost)
    /// </summary>
    public void UpdateWallLineDisplay(BuildingData data, int wallCount, bool canAfford)
    {
        if (data == null) return;

        if (buildingNameText != null)
        {
            buildingNameText.text = $"{data.buildingName} x{wallCount}";
            buildingNameText.color = defaultColor;
        }

        if (buildingCostText != null)
        {
            int totalWood = data.woodCost * wallCount;
            int totalFood = data.foodCost * wallCount;
            int totalStone = data.stoneCost * wallCount;

            string costText = $"Total: {totalWood}W";
            if (totalFood > 0) costText += $" {totalFood}F";
            if (totalStone > 0) costText += $" {totalStone}S";

            buildingCostText.text = costText;
            buildingCostText.color = canAfford ? affordableColor : unaffordableColor;
        }

        if (hintText != null)
        {
            hintText.text = "Click: Confirm | ESC/RMB: Cancel line";
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

}
