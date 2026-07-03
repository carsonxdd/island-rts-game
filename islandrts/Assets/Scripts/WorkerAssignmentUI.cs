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

    // Dirty-check caches so UpdateDisplay only rebuilds strings when a value changes
    private int lastWoodWorkers = -1;
    private int lastFoodWorkers = -1;
    private int lastStoneWorkers = -1;
    private int lastTotalWorkers = -1;
    private int lastHousingCapacity = -1;
    private int lastWarriorCount = -1;

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

        // Invalidate dirty caches so everything refreshes for this building
        lastWoodWorkers = lastFoodWorkers = lastStoneWorkers = -1;
        lastTotalWorkers = lastHousingCapacity = lastWarriorCount = -1;

        // Warrior cost never changes while the panel is open — set it once here
        if (warriorCostText != null)
        {
            warriorCostText.text = $"Cost: {building.warriorCost_Wood} Wood, {building.warriorCost_Food} Food";
        }

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
        if (woodCountText != null && baseBuilding.woodWorkers != lastWoodWorkers)
        {
            lastWoodWorkers = baseBuilding.woodWorkers;
            woodCountText.text = $"Wood: {lastWoodWorkers}";
        }

        // Update food count
        if (foodCountText != null && baseBuilding.foodWorkers != lastFoodWorkers)
        {
            lastFoodWorkers = baseBuilding.foodWorkers;
            foodCountText.text = $"Food: {lastFoodWorkers}";
        }

        // Update stone count
        if (stoneCountText != null && baseBuilding.stoneWorkers != lastStoneWorkers)
        {
            lastStoneWorkers = baseBuilding.stoneWorkers;
            stoneCountText.text = $"Stone: {lastStoneWorkers}";
        }

        // Update total workers count (just the numbers, not the label)
        if (totalWorkersCountText != null)
        {
            int total = baseBuilding.GetTotalWorkers();
            int max = PopulationManager.Instance != null ? PopulationManager.Instance.GetHousingCapacity() : 10;
            if (total != lastTotalWorkers || max != lastHousingCapacity)
            {
                lastTotalWorkers = total;
                lastHousingCapacity = max;
                totalWorkersCountText.text = $"{total} / {max}";
            }
        }

        // Update warrior count
        if (warriorCountText != null)
        {
            int current = baseBuilding.GetWarriorCount();
            if (current != lastWarriorCount)
            {
                lastWarriorCount = current;
                warriorCountText.text = $"Warriors: {current} / {baseBuilding.maxWarriors}";
            }
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
