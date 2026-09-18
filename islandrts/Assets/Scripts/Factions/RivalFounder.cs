using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Founds a colony that is not the player's (2026-09-11, lap step 3 slice A):
/// a campfire, one hut so it sleeps four, a starting pool, the jobs it knows,
/// four survivors walking up from their own beach and one of them armed.
/// </summary>
/// <remarks>
/// <para>This is the body that used to live in <c>DebugMenu.SpawnRivalRoutine</c>.
/// It moved here so F4 and a real day-ten landing exercise the SAME path - a
/// debug-only founding sequence would drift from the shipped one within a
/// session, and the founding order is the part that is easy to get wrong.</para>
/// <para><b>Every <c>yield return null</c> below is load-bearing</b>, because
/// faction state is written in <c>Start</c>, never <c>Awake</c>:</para>
/// <list type="bullet">
/// <item>after the campfire, so its <c>Start</c> registers housing and assigns
/// <see cref="Faction.Campfire"/>;</item>
/// <item>after the hut, so <c>Hut.Start</c> registers its housing - without it
/// the survivors below find no bed and never land;</item>
/// <item>after the survivors, so their own <c>Start</c> has run before anything
/// converts or employs them.</item>
/// </list>
/// <para>Every owned object goes through <see cref="Spawn.Owned"/>. A bare
/// <c>Instantiate</c> runs <c>Awake</c> with no owner and produces a unit that
/// fights for nobody.</para>
/// </remarks>
public static class RivalFounder
{
    /// <summary>What a founding colony starts with, the same numbers the player's opening gives.</summary>
    public const int StartingWood = 100;
    public const int StartingFood = 50;

    /// <summary>Survivors that come ashore with the wreck. The fire sleeps three; the hut's four beds let the fourth stay and the colony grow.</summary>
    public const int Survivors = 4;

    /// <summary>
    /// Frames to keep trying to seat the founding hut (2026-09-16). The fire pad
    /// was flattened a frame earlier and <c>TerrainGrid.FlattenArea</c> re-bakes
    /// the NavMesh ASYNC, so the first <c>NavMesh.SamplePosition</c> around the
    /// site can fail on timing alone — the headless check of 2026-09-16 seated
    /// the Eco rival's hut and skipped the Turtle rival's on the SAME island and
    /// site, and that colony sat at three people for fifteen days.
    /// </summary>
    public const int HutPlaceTries = 30;

    /// <summary>
    /// The research a founding colony has already done: the three gathering
    /// jobs, the militia and building. The same head start the debug camp had,
    /// spelled as entries so <see cref="Knowledge.IsDone"/> agrees.
    /// </summary>
    public static readonly string[] FoundingResearch = { "woodcutting", "foraging", "quarrying", "spearcraft", "construction" };

    /// <summary>
    /// Builds <paramref name="rival"/>'s colony at <paramref name="campfireSite"/>,
    /// landing its people at <paramref name="coveCenter"/>. Drive it with
    /// <c>StartCoroutine</c>; it gives up quietly when the scene has no campfire
    /// prefab or no terrain, which is the intro and the main menu.
    /// </summary>
    public static IEnumerator Found(Faction rival, Vector3 campfireSite, Vector3 coveCenter)
    {
        GameStartController gsc = GameStartController.Instance;
        TerrainGrid tg = TerrainGrid.Instance;
        if (rival == null || tg == null || gsc == null || gsc.campfirePrefab == null) yield break;

        // Their beach is where their arrivals land from now on, not the player's.
        rival.SetCove(coveCenter);

        rival.Resources.Set(StartingWood, StartingFood, 0, 0);

        // What they land knowing, as research DONE rather than bare unlock flags
        // (2026-09-16): the governor's research list skips a done entry, so a
        // colony that only held the flags would spend its first sticks buying
        // Woodcutting again. Complete grants the same kinds.
        Knowledge k = rival.Knowledge;
        for (int i = 0; i < FoundingResearch.Length; i++) k.Complete(ResearchCatalog.Find(FoundingResearch[i]));

        Vector3 site = campfireSite;
        tg.FlattenArea(site, 2.2f, 1.6f);
        site.y = tg.SampleHeight(site);
        GameObject fireObj = Spawn.Owned(gsc.campfirePrefab, site, Quaternion.identity, rival);
        fireObj.name = "Campfire (" + rival.Name + ")";
        BaseBuilding fire = fireObj.GetComponent<BaseBuilding>();
        if (fire == null) yield break;
        yield return null;   // Start: housing registers, Faction.Campfire is set

        bool hut = false;
        for (int t = 0; t < HutPlaceTries && !hut; t++)
        {
            hut = PlaceHut(rival, site, tg);
            yield return null;   // the NavMesh update lands, then Hut.Start registers its housing
        }
        if (!hut)
        {
            Debug.LogWarning("RivalFounder: no clear spot for " + rival.Name + "'s hut within " + HutPlaceTries
                + " frames of " + site + "; the camp sleeps three until its governor builds one.");
        }

        // Land them at their own cove and let Idle walk them in: a fresh arrival
        // far from home already walks to its home provider's approach point, so
        // the landing needs no executor of its own.
        Population pop = rival.Population;
        for (int i = 0; i < Survivors && pop.SpawnArrival(false) != null; i++) { }
        yield return null;   // the colonists' Start

        // One survivor stays jobless (2026-09-16): the governor's own builder
        // reserve, from the first tick. The overnight batch's rivals sweep had
        // 13 camps whose hut never seated, and every one of them sat at three
        // people and no hut for the whole run - the warrior and two workers filled
        // the fire's three beds, nobody was jobless to build, so no hut, no bed,
        // no arrival, no builder, ever. A rival has no castaway to break that.
        fire.Stockpile.Add(ItemCatalog.WoodenSpear, 1);
        fire.SpawnWarrior();
        if (pop.GetIdleCount() > 1) fire.AssignWorker(ResourceNode.ResourceType.Wood);
        if (pop.GetIdleCount() > 1) fire.AssignWorker(ResourceNode.ResourceType.Food);
        if (pop.GetIdleCount() > 1) fire.AssignWorker(ResourceNode.ResourceType.Stone);

        DevQuests.Signal("faction:rival");
    }

    /// <summary>Laps of the hut search ring: 7, 11, 15 and 19 m (two laps until 2026-09-16 - a beach fire has half of its 7-11 m ring in the sea).</summary>
    const int HutLaps = 4;

    /// <summary>One hut on the first clear spot of a four-lap ring, so the camp can grow past the fire's three beds. True when one was placed.</summary>
    static bool PlaceHut(Faction rival, Vector3 site, TerrainGrid tg)
    {
        BuildingData hutData = BuildingDatabase.Instance != null
            ? BuildingDatabase.Instance.GetBuildingData(BuildingType.Hut) : null;
        if (hutData == null || hutData.finishedBuildingPrefab == null) return true;   // nothing to place, nothing to retry

        int buildingsLayer = LayerMask.NameToLayer("Buildings");
        for (int i = 0; i < 8 * HutLaps; i++)
        {
            float angle = (i * 45f + 22.5f * (i / 8)) * Mathf.Deg2Rad;   // each lap turned half a step
            float radius = 7f + 4f * (i / 8);
            Vector3 pos = site + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            pos = GridSnap.SnapXZ(pos, 1f);
            pos.y = tg.SampleHeight(pos);
            if (!IsClearForBuilding(pos)) continue;
            tg.FlattenArea(pos, 1.8f, 1.4f);
            pos.y = tg.SampleHeight(pos);
            GameObject hut = Spawn.Owned(hutData.finishedBuildingPrefab, pos, Quaternion.identity, rival);
            if (buildingsLayer >= 0) hut.layer = buildingsLayer;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Dry, gentle, reachable, on the NavMesh and clear of what is already
    /// there. Radial checks, not the full placement validator: this seats a
    /// starting camp, it does not replace <c>GhostPlacer</c>.
    /// </summary>
    public static bool IsClearForBuilding(Vector3 pos)
    {
        if (TerrainGrid.Instance != null)
        {
            if (!TerrainGrid.Instance.IsBuildable(pos)) return false;
        }
        else if (Mathf.Abs(pos.x) > 63f || Mathf.Abs(pos.z) > 63f) return false;

        NavMeshHit hit;
        if (!NavMesh.SamplePosition(pos, out hit, 1f, NavMesh.AllAreas)) return false;

        if (!ClearOf(BaseBuilding.ActiveList, pos, 5f)) return false;
        if (!ClearOf(Hut.ActiveList, pos, 4.5f)) return false;
        if (!ClearOf(Watchtower.ActiveList, pos, 4.5f)) return false;
        if (!ClearOf(Storehouse.ActiveList, pos, 4.5f)) return false;
        if (!ClearOf(ConstructionSite.ActiveList, pos, 4f)) return false;
        if (!ClearOf(ResourceNode.ActiveList, pos, 3f)) return false;
        return true;
    }

    static bool ClearOf<T>(System.Collections.Generic.IReadOnlyList<T> list, Vector3 pos, float minDist) where T : MonoBehaviour
    {
        float minSqr = minDist * minDist;
        for (int i = 0; i < list.Count; i++)
        {
            T entry = list[i];
            if (entry == null) continue;
            Vector3 d = entry.transform.position - pos;
            d.y = 0f;
            if (d.sqrMagnitude < minSqr) return false;
        }
        return true;
    }
}
