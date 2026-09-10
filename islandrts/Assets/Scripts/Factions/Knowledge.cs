using System;

/// <summary>
/// What one faction knows (2026-09-09, lap step 1 commit 4): the research it has
/// completed, the <see cref="Unlocks.Kind"/> flags that research granted, and
/// the multipliers the Workshop-tier research applies. On
/// <see cref="Faction.Knowledge"/>; the catalogs (<see cref="ResearchCatalog"/>,
/// <see cref="Unlocks"/>) stay static definitions with no state.
/// </summary>
/// <remarks>
/// Read at the point of effect, never pushed (the <c>Difficulty</c> pattern):
/// <c>BaseBuilding.AssignWorker</c>, <c>CanRecruitWarrior</c>,
/// <c>BuildPlacement.StartPlacement</c> and the Build / Repair / Craft
/// considerations each ask <see cref="Has"/> at the moment they decide, on the
/// faction that is deciding (<c>bb.faction.Knowledge</c> in the AI,
/// <c>Factions.Player.Knowledge</c> in the UI). NOT granted under the balance
/// sim: the sim researches like a player.
/// </remarks>
public sealed class Knowledge
{
    readonly bool[] unlocks = new bool[Unlocks.Count];
    readonly bool[] researched = new bool[ResearchCatalog.All.Length];

    // The Workshop-tier multipliers, applied by a research entry's `apply`
    // (2026-08-26; fed by ResearchCatalog since 2026-09-03). Read at the point of
    // effect — gathering (GatherExecutor), construction (ConstructionSite) — so
    // an upgrade applies to every existing and future unit the moment its
    // research completes.
    public float GatherRateMult = 1f;
    public float BuildSpeedMult = 1f;
    /// <summary>Extra room in the campfire stockpile, on top of its base capacity.</summary>
    public int StockpileRoom;

    /// <summary>Fires after any unlock is granted or research completes.</summary>
    public event Action OnChanged;

    // ---- unlocks ----------------------------------------------------------

    public bool Has(Unlocks.Kind kind) => unlocks[(int)kind];

    public bool HasJob(ResourceNode.ResourceType type) => Has(Unlocks.ForJob(type));

    public void Grant(Unlocks.Kind kind)
    {
        if (unlocks[(int)kind]) return;
        unlocks[(int)kind] = true;
        OnChanged?.Invoke();
    }

    /// <summary>Every flag at once — the F4 cheat (research rows stay as they are; use <see cref="CompleteAll"/> for those).</summary>
    public void GrantAll()
    {
        bool changed = false;
        for (int i = 0; i < unlocks.Length; i++)
        {
            if (!unlocks[i]) { unlocks[i] = true; changed = true; }
        }
        if (changed) OnChanged?.Invoke();
    }

    // ---- research ---------------------------------------------------------

    public readonly Faction faction;

    public Knowledge(Faction faction) { this.faction = faction; }

    public bool IsDone(ResearchCatalog.ResearchDef d) => d != null && researched[d.index];

    /// <summary>True when <paramref name="id"/> is done — or is not a research id at all (an empty requirement is no requirement).</summary>
    public bool IsDone(string id)
    {
        if (string.IsNullOrEmpty(id)) return true;
        ResearchCatalog.ResearchDef d = ResearchCatalog.Find(id);
        return d == null || researched[d.index];
    }

    /// <summary>Not yet done and every prerequisite is.</summary>
    public bool IsAvailable(ResearchCatalog.ResearchDef d)
    {
        if (d == null || researched[d.index]) return false;
        for (int i = 0; i < d.prerequisites.Length; i++)
            if (!IsDone(d.prerequisites[i])) return false;
        return true;
    }

    /// <summary>Title of the first prerequisite still outstanding, or null when there is none.</summary>
    public string PrerequisiteTitle(ResearchCatalog.ResearchDef d)
    {
        for (int i = 0; i < d.prerequisites.Length; i++)
        {
            if (IsDone(d.prerequisites[i])) continue;
            ResearchCatalog.ResearchDef p = ResearchCatalog.Find(d.prerequisites[i]);
            return p != null ? p.title : d.prerequisites[i];
        }
        return null;
    }

    public bool AllDone
    {
        get
        {
            for (int i = 0; i < researched.Length; i++)
                if (!researched[i]) return false;
            return true;
        }
    }

    /// <summary>Mark done, grant its flags, run its effect. Idempotent.</summary>
    public void Complete(ResearchCatalog.ResearchDef d)
    {
        if (d == null || researched[d.index]) return;
        researched[d.index] = true;
        if (faction.IsPlayer)
        {
            DevQuests.Signal("research:" + d.id);
            UnityEngine.Debug.Log("Researched " + d.title + " — " + d.description);   // once per entry per run
        }
        for (int i = 0; i < d.grants.Length; i++) Grant(d.grants[i]);
        d.apply?.Invoke(this);
        OnChanged?.Invoke();
    }

    /// <summary>Everything at once — the F4 cheat.</summary>
    public void CompleteAll()
    {
        for (int i = 0; i < ResearchCatalog.All.Length; i++) Complete(ResearchCatalog.All[i]);
    }
}
