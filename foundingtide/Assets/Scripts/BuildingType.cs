/// <summary>
/// Every placeable structure. BuildingDatabase maps each value to the BuildingData asset
/// holding its costs, prefabs and placement rules, so adding a building means adding a
/// value here plus its asset - no code branches per type.
/// </summary>
/// <remarks>
/// The campfire is deliberately absent: it is placed once by the opening sequence, and
/// keeping it out of this enum is what makes a second one impossible to build.
/// Gates are not placed directly either - they are converted from finished walls.
/// New values are APPENDED, never inserted: the value is serialized as an int on every
/// BuildingData asset and ConstructionSite prefab, so reordering silently repoints them.
/// </remarks>
public enum BuildingType
{
    /// <summary>
    /// Housing tier 2 (2026-09-18). No longer placed directly - BuildingData.placeable is
    /// false on it and it is reached only by upgrading a Tent.
    /// </summary>
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
    Storehouse,
    /// <summary>Housing tier 1 (2026-09-18). The cheap starter shelter every colony opens with; upgrades to a Hut.</summary>
    Tent
}
