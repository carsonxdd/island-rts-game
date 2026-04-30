using UnityEngine;
using UnityEngine.UI;
using TMPro;  // TextMeshPro for text

/// <summary>
/// Controls the Worker Assignment UI panel
/// Shows worker counts and +/- buttons for assigning workers to resource types
/// </summary>
public class WorkerAssignmentUI : MonoBehaviour
{
    [Header("UI References - Drag from Hierarchy")]
    public GameObject uiPanel;  // The main panel that shows/hides
    public Button closeButton;  // X button to close panel

    [Header("Wood Worker UI")]
    public TextMeshProUGUI woodCountText;  // Shows "Wood: 2"
    public Button woodPlusButton;   // + button for wood workers
    public Button woodMinusButton;  // - button for wood workers

    [Header("Food Worker UI")]
    public TextMeshProUGUI foodCountText;  // Shows "Food: 1"
    public Button foodPlusButton;   // + button for food workers
    public Button foodMinusButton;  // - button for food workers

    [Header("Stone Worker UI")]
    public TextMeshProUGUI stoneCountText;  // Shows "Stone: 0"
    public Button stonePlusButton;   // + button for stone workers
    public Button stoneMinusButton;  // - button for stone workers

    [Header("Warrior UI")]
    public TextMeshProUGUI warriorCountText;  // Shows "Warriors: 2"
    public Button warriorPlusButton;   // + button for warriors
    public Button warriorMinusButton;  // - button for warriors
    public TextMeshProUGUI warriorCostText;  // Shows cost "Cost: 10 Wood, 15 Food"

    [Header("Total Workers")]
    public TextMeshProUGUI totalWorkersCountText;  // Shows just "3 / 10" (not the label)

    // Reference to the Campfire building
    private BaseBuilding baseBuilding;

    void Start()
    {
        // Hide the panel when game starts
        if (uiPanel != null)
        {
            uiPanel.SetActive(false);
        }

        // Connect button clicks to functions
        SetupButtons();
    }

    void SetupButtons()
    {
        // Close button
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(ClosePanel);
        }

        // Wood buttons
        if (woodPlusButton != null)
        {
            woodPlusButton.onClick.AddListener(() => OnPlusClicked(ResourceNode.ResourceType.Wood));
        }
        if (woodMinusButton != null)
        {
            woodMinusButton.onClick.AddListener(() => OnMinusClicked(ResourceNode.ResourceType.Wood));
        }

        // Food buttons
        if (foodPlusButton != null)
        {
            foodPlusButton.onClick.AddListener(() => OnPlusClicked(ResourceNode.ResourceType.Food));
        }
        if (foodMinusButton != null)
        {
            foodMinusButton.onClick.AddListener(() => OnMinusClicked(ResourceNode.ResourceType.Food));
        }

        // Stone buttons
        if (stonePlusButton != null)
        {
            stonePlusButton.onClick.AddListener(() => OnPlusClicked(ResourceNode.ResourceType.Stone));
        }
        if (stoneMinusButton != null)
        {
            stoneMinusButton.onClick.AddListener(() => OnMinusClicked(ResourceNode.ResourceType.Stone));
        }

        // Warrior buttons
        if (warriorPlusButton != null)
        {
            warriorPlusButton.onClick.AddListener(OnWarriorPlusClicked);
        }
        if (warriorMinusButton != null)
        {
            warriorMinusButton.onClick.AddListener(OnWarriorMinusClicked);
        }
    }

    /// <summary>
    /// Opens the UI panel and connects it to a specific base building
    /// Called from BaseBuilding.cs when player clicks the Campfire
    /// </summary>
    public void OpenPanel(BaseBuilding building)
    {
        baseBuilding = building;

        if (uiPanel != null)
        {
            uiPanel.SetActive(true);
            UpdateDisplay();  // Refresh the numbers
        }
    }

    /// <summary>
    /// Closes the UI panel
    /// </summary>
    public void ClosePanel()
    {
        if (uiPanel != null)
        {
            uiPanel.SetActive(false);
        }

        // Play button click sound
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayButtonClick();
        }
    }

    void Update()
    {
        // Keep UI updated in real-time while panel is open
        if (uiPanel != null && uiPanel.activeSelf && baseBuilding != null)
        {
            UpdateDisplay();
        }
    }

    /// <summary>
    /// Updates all the text displays with current worker counts
    /// </summary>
    void UpdateDisplay()
    {
        if (baseBuilding == null) return;

        // Update wood count
        if (woodCountText != null)
        {
            woodCountText.text = $"Wood: {baseBuilding.woodWorkers}";
        }

        // Update food count
        if (foodCountText != null)
        {
            foodCountText.text = $"Food: {baseBuilding.foodWorkers}";
        }

        // Update stone count
        if (stoneCountText != null)
        {
            stoneCountText.text = $"Stone: {baseBuilding.stoneWorkers}";
        }

        // Update total workers count (just the numbers, not the label)
        if (totalWorkersCountText != null)
        {
            int total = baseBuilding.GetTotalWorkers();
            int max = PopulationManager.Instance != null ? PopulationManager.Instance.GetHousingCapacity() : 10;
            totalWorkersCountText.text = $"{total} / {max}";
        }

        // Update warrior count
        if (warriorCountText != null)
        {
            int current = baseBuilding.GetWarriorCount();
            int max = baseBuilding.maxWarriors;
            warriorCountText.text = $"Warriors: {current} / {max}";
        }

        // Update warrior cost text
        if (warriorCostText != null)
        {
            warriorCostText.text = $"Cost: {baseBuilding.warriorCost_Wood} Wood, {baseBuilding.warriorCost_Food} Food";
        }
    }

    /// <summary>
    /// Called when + button is clicked
    /// Assigns a new worker to the resource type
    /// </summary>
    void OnPlusClicked(ResourceNode.ResourceType resourceType)
    {
        if (baseBuilding == null)
        {
            Debug.LogError("WorkerAssignmentUI: No baseBuilding reference!");
            return;
        }

        // Add worker through BaseBuilding (it will check housing capacity internally)
        baseBuilding.AssignWorker(resourceType);

        // Play worker assigned sound
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayWorkerAssigned();
        }

        // Update the UI display
        UpdateDisplay();
    }

    /// <summary>
    /// Called when - button is clicked
    /// Removes a worker from the resource type
    /// </summary>
    void OnMinusClicked(ResourceNode.ResourceType resourceType)
    {
        if (baseBuilding == null)
        {
            Debug.LogError("WorkerAssignmentUI: No baseBuilding reference!");
            return;
        }

        // Remove worker through BaseBuilding
        baseBuilding.UnassignWorker(resourceType);

        // Play button click sound
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayButtonClick();
        }

        // Update the UI display
        UpdateDisplay();
    }

    /// <summary>
    /// Called when warrior + button is clicked
    /// Recruits a new warrior
    /// </summary>
    void OnWarriorPlusClicked()
    {
        if (baseBuilding == null)
        {
            Debug.LogError("WorkerAssignmentUI: No baseBuilding reference!");
            return;
        }

        // Check if at max warriors
        if (baseBuilding.GetWarriorCount() >= baseBuilding.maxWarriors)
        {
            return;
        }

        // Check resources
        if (ResourceManager.Instance.wood < baseBuilding.warriorCost_Wood ||
            ResourceManager.Instance.food < baseBuilding.warriorCost_Food)
        {
            return;
        }

        // Spawn warrior through BaseBuilding
        baseBuilding.SpawnWarrior();

        // Play button click sound
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayButtonClick();
        }

        // Update the UI display
        UpdateDisplay();
    }

    /// <summary>
    /// Called when warrior - button is clicked
    /// Removes a warrior
    /// </summary>
    void OnWarriorMinusClicked()
    {
        if (baseBuilding == null)
        {
            Debug.LogError("WorkerAssignmentUI: No baseBuilding reference!");
            return;
        }

        // Remove warrior through BaseBuilding
        baseBuilding.RemoveWarrior();

        // Play button click sound
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayButtonClick();
        }

        // Update the UI display
        UpdateDisplay();
    }
}
