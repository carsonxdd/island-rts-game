/// <summary>
/// Every placeable structure. BuildingDatabase maps each value to the BuildingData asset
/// holding its costs, prefabs and placement rules, so adding a building means adding a
/// value here plus its asset - no code branches per type.
/// </summary>
/// <remarks>
/// The campfire is deliberately absent: it is placed once by the opening sequence, and
/// keeping it out of this enum is what makes a second one impossible to build.
/// Gates are not placed directly either - they are converted from finished walls.
/// </remarks>
public enum BuildingType
{
    Hut,
    WoodenWall,
    StoneWall,
    Watchtower,
    WoodenGate,
    StoneGate,
    Workshop,
    /// <summary>The escape ship's slipway (2026-09-04, Slice 6). Beach-only; clicking it offers to set sail.</summary>
    Shipyard,
    /// <summary>A drop-off point with a little stockpile room (2026-09-16). Unlocked by Storage Pits.</summary>
    Storehouse
}
