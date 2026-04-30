using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class VictoryDefeatUI : MonoBehaviour
{
    [Header("Victory Screen")]
    public GameObject victoryPanel;
    public TextMeshProUGUI victoryTitleText;
    public TextMeshProUGUI victoryStatsText;
    public Button continueButton;
    public Button quitButtonVictory;

    [Header("Defeat Screen")]
    public GameObject defeatPanel;
    public TextMeshProUGUI defeatTitleText;
    public TextMeshProUGUI defeatStatsText;
    public Button restartButton;
    public Button quitButtonDefeat;

    void Start()
    {
        // Hide both screens at start
        if (victoryPanel != null)
            victoryPanel.SetActive(false);

        if (defeatPanel != null)
            defeatPanel.SetActive(false);

        // Setup button listeners
        SetupButtons();
    }

    void SetupButtons()
    {
        // Victory buttons
        if (continueButton != null)
        {
            continueButton.onClick.AddListener(OnContinueClicked);
        }

        if (quitButtonVictory != null)
        {
            quitButtonVictory.onClick.AddListener(OnQuitClicked);
        }

        // Defeat buttons
        if (restartButton != null)
        {
            restartButton.onClick.AddListener(OnRestartClicked);
        }

        if (quitButtonDefeat != null)
        {
            quitButtonDefeat.onClick.AddListener(OnQuitClicked);
        }
    }

    void Update()
    {
        // Update screens if they're visible
        if (victoryPanel != null && victoryPanel.activeSelf)
        {
            UpdateVictoryScreen();
        }

        if (defeatPanel != null && defeatPanel.activeSelf)
        {
            UpdateDefeatScreen();
        }
    }

    void UpdateVictoryScreen()
    {
        if (GameManager.Instance == null) return;

        // Update title
        if (victoryTitleText != null)
        {
            victoryTitleText.text = "VICTORY!";
        }

        // Update stats
        if (victoryStatsText != null)
        {
            int nightsSurvived = GameManager.Instance.GetNightsSurvived();
            int enemiesKilled = GameManager.Instance.GetEnemiesKilled();
            int maxWorkers = GameManager.Instance.maxWorkers;
            int maxWarriors = GameManager.Instance.maxWarriors;

            // Get current resources
            int wood = 0, food = 0, stone = 0;
            if (ResourceManager.Instance != null)
            {
                wood = (int)ResourceManager.Instance.wood;
                food = (int)ResourceManager.Instance.food;
                stone = (int)ResourceManager.Instance.stone;
            }

            victoryStatsText.text = $@"You survived the pirate raids!

<b>Nights Survived:</b> {nightsSurvived}
<b>Enemies Defeated:</b> {enemiesKilled}

<b>Final Settlement:</b>
Workers: {maxWorkers}
Warriors: {maxWarriors}

<b>Resources Collected:</b>
Wood: {wood}
Food: {food}
Stone: {stone}

Your settlement thrives!";
        }
    }

    void UpdateDefeatScreen()
    {
        if (GameManager.Instance == null) return;

        // Update title
        if (defeatTitleText != null)
        {
            defeatTitleText.text = "DEFEAT";
        }

        // Update stats
        if (defeatStatsText != null)
        {
            int nightsSurvived = GameManager.Instance.GetNightsSurvived();
            int enemiesKilled = GameManager.Instance.GetEnemiesKilled();
            int maxWorkers = GameManager.Instance.maxWorkers;
            int maxWarriors = GameManager.Instance.maxWarriors;

            defeatStatsText.text = $@"Your camp was overrun...

<b>Nights Survived:</b> {nightsSurvived - 1}
<b>Enemies Defeated:</b> {enemiesKilled}

<b>Settlement Stats:</b>
Max Workers: {maxWorkers}
Max Warriors: {maxWarriors}

Better luck next time!";
        }
    }

    void OnContinueClicked()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.ContinuePlaying();
        }
    }

    void OnRestartClicked()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.RestartGame();
        }
    }

    void OnQuitClicked()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.QuitGame();
        }
    }
}
