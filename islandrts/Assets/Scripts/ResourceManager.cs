using UnityEngine;

/// <summary>
/// The scene's starting amounts for the player's colony. On Awake it fills
/// <c>Factions.Player.Resources</c> (a <see cref="ResourcePool"/>) from these
/// fields times the run's difficulty multiplier; after that it holds nothing.
/// </summary>
/// <remarks>
/// Until 2026-09-09 this was the singleton that owned the live pool
/// (<c>ResourceManager.Instance</c>, now banned — see CLAUDE.md). The pool
/// moved onto <see cref="Faction"/> in lap step 1 so a rival colony can have its
/// own; this component keeps its name so the scene object and its serialised
/// starting values are untouched. Awake, not Start: several systems read the
/// pool in their own Start, and a colony that briefly had the unscaled amount
/// could afford a building it should not have.
/// </remarks>
public class ResourceManager : MonoBehaviour
{
    [Header("Starting Resources")]
    public int startingWood = 100;
    public int startingFood = 50;
    public int startingStone = 0;
    public int startingMetal = 0;

    void Awake()
    {
        float scale = Difficulty.StartingResourceMultiplier;
        ResourcePool pool = Factions.Player.Resources;
        pool.Set(
            Mathf.RoundToInt(startingWood * scale),
            Mathf.RoundToInt(startingFood * scale),
            Mathf.RoundToInt(startingStone * scale),
            Mathf.RoundToInt(startingMetal * scale));

        Debug.Log($"ResourceManager: Initialized with Wood={pool.wood}, Food={pool.food}, Stone={pool.stone}, Metal={pool.metal}");
    }
}
