/// <summary>
/// Something a faction owns: every unit, building, construction site and craft
/// station (2026-09-09, lap step 1). Resource nodes and pickups are unowned and
/// do not implement it. <see cref="Spawn.Owned"/> sets the owner right after
/// <c>Instantiate</c>, so the value is reliable from <c>Start</c> on and NOT in
/// <c>Awake</c>. Joins <see cref="ITargetable"/> once every implementer carries a
/// faction (step 1, commit 5); until then it is opt-in.
/// </summary>
public interface IOwned
{
    Faction Faction { get; set; }
}
