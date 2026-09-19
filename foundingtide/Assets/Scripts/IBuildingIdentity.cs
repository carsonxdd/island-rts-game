/// <summary>
/// A finished building that knows which BuildingType it is (2026-09-18).
/// </summary>
/// <remarks>
/// ConstructionSite always knew its type; finished buildings did not, so every site that
/// needed one - the demolish refund, the repair price, the no-build zone radius, and now
/// the upgrade - hardcoded it from the component class. That breaks the moment one
/// component serves two tiers, which is exactly what the Tent and the Hut do: both carry
/// the Hut component, and a Tent must not refund or repair at Hut prices.
///
/// Implement it by serializing the type on the prefab, never by returning a literal, so a
/// second tier is a prefab field rather than a new class.
/// </remarks>
public interface IBuildingIdentity
{
    BuildingType BuildingType { get; }
}
