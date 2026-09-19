using UnityEngine;

/// <summary>
/// Upgrading a standing building to its next tier (2026-09-18). The Tent into the Hut is
/// the first, and everything here is generic so the second is a BuildingData field rather
/// than new code.
/// </summary>
/// <remarks>
/// An upgrade is deliberately NOT a swap: it pays the target tier's cost and leaves an
/// ordinary <see cref="ConstructionSite"/> standing on the same pad, which jobless
/// colonists and the castaway work through <c>AddLabor</c> like any other build. That
/// keeps one construction path in the game, and it keeps the labour economy honest -
/// there is no way to convert resources into a finished building without someone's hands.
///
/// The consequence a player can feel: a hut's beds are gone while it goes up. Housing is
/// released by the old building's OnDestroy and registered again by the new one's Start,
/// so the colonists inside are homeless for the build and sleep by the fire. That
/// self-heals (Population.RegisterHousing moves the homeless in, and a fire-side sleeper
/// polls HomeOf and walks to a new hut), which is exactly why upgrading mid-raid is a
/// mistake the player is allowed to make. <see cref="WarningFor"/> says so; nothing
/// forbids it.
///
/// This class is the ONE owner of the rules. The selected-building card asks
/// <see cref="CanUpgrade"/> to decide whether its button is live and what to grey it with;
/// nothing re-derives affordability or the research gate itself.
/// </remarks>
public static class BuildingUpgrade
{
    /// <summary>
    /// The building's own data, via its <see cref="IBuildingIdentity"/>. Null when the
    /// object is not an identified building (a campfire, say) or has no asset.
    /// </summary>
    public static BuildingData DataOf(GameObject building)
    {
        if (building == null || BuildingDatabase.Instance == null) return null;
        IBuildingIdentity identity = building.GetComponent<IBuildingIdentity>();
        if (identity == null) return null;
        return BuildingDatabase.Instance.GetBuildingData(identity.BuildingType);
    }

    /// <summary>
    /// Can this building be upgraded right now, and if not, what does the player need to
    /// hear? <paramref name="reason"/> is player-facing prose, not a log line.
    /// </summary>
    public static bool CanUpgrade(GameObject building, out BuildingData next, out string reason)
    {
        next = null;
        reason = null;

        BuildingData current = DataOf(building);
        if (current == null)
        {
            reason = "Cannot be upgraded";
            return false;
        }

        next = current.upgradesTo;
        if (next == null)
        {
            reason = "Fully upgraded";
            return false;
        }

        // Any colony may upgrade its own building - a rival's governor calls this too.
        // "Is it MINE?" is the click site's question, not this one's: the card only ever
        // opens on a player-owned building, so asking IsPlayer here would only lock the
        // governors out of their own tents.
        Faction faction = OwnerOf(building);
        if (faction == null || faction.IsRaiders)
        {
            reason = "Cannot be upgraded";
            next = null;
            return false;
        }

        // A site is already a build in progress; upgrading one would discard its labour.
        if (building.GetComponent<ConstructionSite>() != null)
        {
            reason = "Still under construction";
            next = null;
            return false;
        }

        if (!next.UnlockedFor(faction))
        {
            reason = "Needs " + next.RequiredResearchTitle;
            return false;
        }

        if (!next.AffordableBy(faction))
        {
            reason = "Needs " + next.CostLine();
            return false;
        }

        return true;
    }

    /// <summary>
    /// A caution to show beside a live Upgrade button, or null when there is nothing to
    /// say. Housing is the case that matters: the beds go away for the length of the
    /// build, which is fine by day and expensive with raiders ashore.
    /// </summary>
    public static string WarningFor(GameObject building)
    {
        if (building == null) return null;

        Hut housing = building.GetComponent<Hut>();
        if (housing == null || housing.HousingCapacity <= 0) return null;

        bool raidersAshore = Enemy.ActiveList.Count > 0;
        return raidersAshore
            ? "Raiders ashore - the beds go while it builds"
            : "The beds go while it builds";
    }

    /// <summary>
    /// Pay for the next tier and put a construction site in this building's place.
    /// False when <see cref="CanUpgrade"/> would have said no, or the charge failed.
    /// </summary>
    public static bool TryUpgrade(GameObject building)
    {
        BuildingData next;
        string reason;
        if (!CanUpgrade(building, out next, out reason)) return false;
        if (next.constructionSitePrefab == null)
        {
            Debug.LogError("BuildingUpgrade: " + next.buildingName + " has no construction site prefab.");
            return false;
        }

        Faction faction = OwnerOf(building);
        if (!next.Charge(faction)) return false;

        // The pad under a standing building is already flat and already baked, so the
        // site inherits the pose as it stands. Re-flattening here would kick an async
        // NavMesh rebuild for a pad that has not moved.
        Vector3 pos = building.transform.position;
        Quaternion rot = building.transform.rotation;
        int layer = building.layer;

        // Destroy first: the old building's OnDestroy is the one owner of releasing its
        // housing and unregistering it, and it must run before the new site claims the
        // cell for placement checks.
        Object.Destroy(building);

        GameObject site = Spawn.Owned(next.constructionSitePrefab, pos, rot, faction);
        site.layer = layer;

        ConstructionSite component = site.GetComponent<ConstructionSite>();
        if (component != null)
        {
            component.SetBuildingType(next.buildingType);
            component.isUpgrade = true;
        }

        if (AudioManager.Instance != null) AudioManager.Instance.PlayBuildingPlaced();
        DevQuests.Signal("build:upgrade");
        DevQuests.Signal("build:upgrade:" + next.buildingType.ToString().ToLowerInvariant());
        return true;
    }

    static Faction OwnerOf(GameObject building)
    {
        if (building == null) return null;
        IOwned owned = building.GetComponent<IOwned>();
        return owned != null ? owned.Faction : null;
    }
}
