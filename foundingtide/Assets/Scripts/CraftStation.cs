using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A bench with a queue (2026-09-03): the campfire and the Workshop each carry
/// one. Recipes and research are queued front to back; progress per second is
/// <c>speed[category] × labor</c>, where labor is 1 per pair of hands at the
/// bench and 0 otherwise — a queue nobody stands at does not move.
///
/// Several pairs of hands share a bench (2026-09-13): every laborer holds a
/// <b>lane</b>, one repeat of one queue entry, so five queued spears with three
/// colonists at the bench make three at once, each at full speed. Lanes on an
/// entry never outnumber its repeats, so research (one repeat) is always
/// one-at-a-time. At most <see cref="MaxLaborers"/> lanes; the player's
/// character always gets one, evicting the stalest colonist if the bench is
/// full, and takes over a colonist's repeat when no repeat is free. An
/// abandoned repeat banks its progress on the entry and the next pair of hands
/// picks it up, so walking away loses nothing.
///
/// Costs are charged when a repeat completes (<see cref="WorkDef.Pay"/>). If they
/// cannot be met at that moment the lane holds at 100% and <see cref="Status"/>
/// says what is missing; the next tick that can pay finishes it. Output goes to
/// the campfire stockpile whichever station made it — one colony store — except a
/// tool, which goes into the player's hands when the player is the laborer.
///
/// Runtime-added by <c>BaseBuilding.Awake</c> / <c>Workshop.Awake</c> (the
/// RaidDirector pattern), so its public fields are the LIVE values and no prefab
/// carries a stale copy.
/// </summary>
public class CraftStation : MonoBehaviour
{
    IOwned host;
    /// <summary>Whose bench this is: the building it sits on (the campfire or a Workshop).</summary>
    public Faction Faction
    {
        get
        {
            if (host == null) { host = GetComponent<BaseBuilding>(); if (host == null) host = GetComponent<Workshop>(); }
            return host != null ? host.Faction : Factions.Player;
        }
    }

    public static IReadOnlyList<CraftStation> ActiveList => ActiveRegistry<CraftStation>.List;

    /// <summary>Seconds without labor before a pair of hands counts as gone from the bench.</summary>
    public const float IdleAfter = 0.5f;

    /// <summary>Pairs of hands one bench holds at once (the player's character included).</summary>
    public const int MaxLaborers = 4;

    public sealed class QueueEntry
    {
        public CraftingCatalog.Recipe recipe;
        public ResearchCatalog.ResearchDef research;
        /// <summary>Repeats left (recipes); always 1 for research.</summary>
        public int remaining;
        /// <summary>Seconds of scaled labor banked on an abandoned repeat; the next lane on this entry inherits it.</summary>
        public float progress;
        /// <summary>Furthest live lane on this entry, for display only.</summary>
        internal float live;

        public WorkDef Def => recipe != null ? (WorkDef)recipe : research;
        public string Title => Def.title;
        public bool IsResearch => research != null;
        public float Progress01 => Mathf.Clamp01(Mathf.Max(progress, live) / Mathf.Max(0.01f, Def.seconds));
    }

    /// <summary>One pair of hands: who, which entry, how far along their own repeat is.</summary>
    sealed class Lane
    {
        public object who;
        public float lastLaborTime;
        public QueueEntry entry;
        public float progress;
    }

    [Tooltip("Which research tier this bench lists.")]
    public ResearchCatalog.Station tier = ResearchCatalog.Station.Campfire;

    [Tooltip("Progress multiplier per WorkCategory (Tool, Weapon, Construction, Research). 0 = not listed here.")]
    public float[] speeds = { 1f, 1f, 1f, 1f };

    public string displayName = "Campfire";

    private readonly List<QueueEntry> queue = new List<QueueEntry>();
    private readonly List<Lane> lanes = new List<Lane>(MaxLaborers + 1);
    private string status = "";

    private Collider cachedCollider;
    private ITargetable targetable;   // the building this bench sits on, for its Health

    /// <summary>Front to back; index 0 is the entry hands take first.</summary>
    public IReadOnlyList<QueueEntry> Queue => queue;

    /// <summary>Bumped on every structural change (add, remove, complete); the panel rebuilds its rows on it.</summary>
    public int Version { get; private set; }

    public bool HasWork => queue.Count > 0;
    public QueueEntry Active => queue.Count > 0 ? queue[0] : null;

    /// <summary>Someone has added labor in the last <see cref="IdleAfter"/> seconds.</summary>
    public bool IsWorked => LaborerCount > 0;

    /// <summary>Pairs of hands at the bench right now.</summary>
    public int LaborerCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < lanes.Count; i++) if (Fresh(lanes[i])) n++;
            return n;
        }
    }

    /// <summary>Who is at the bench right now (the player first, else the first colonist), or null.</summary>
    public object Laborer
    {
        get
        {
            object first = null;
            for (int i = 0; i < lanes.Count; i++)
            {
                if (!Fresh(lanes[i])) continue;
                if (lanes[i].who is PlayerCharacter) return lanes[i].who;
                if (first == null) first = lanes[i].who;
            }
            return first;
        }
    }

    /// <summary>True while the player's character is one of the pairs of hands here.</summary>
    public bool PlayerAtBench => Laborer is PlayerCharacter;

    /// <summary>The entry <paramref name="who"/> is working, or null when they hold no lane.</summary>
    public QueueEntry EntryOf(object who)
    {
        Lane l = FindLane(who);
        return l != null && Fresh(l) ? l.entry : null;
    }

    /// <summary>0..1 of <paramref name="who"/>'s own repeat, 0 when they hold no lane.</summary>
    public float Progress01Of(object who)
    {
        Lane l = FindLane(who);
        if (l == null || l.entry == null) return 0f;
        return Mathf.Clamp01(l.progress / Mathf.Max(0.01f, l.entry.Def.seconds));
    }

    // --- Crafter claims (2026-09-04, Slice 3; several since 2026-09-13) ---
    // The ConstructionSite.RegisterBuilder shape: a colonist claims a seat when it
    // sets out so the seats fill from different benches before anyone queues at
    // one. A claim is about WHO WALKS HERE, not who labors — the player's
    // character never claims and always wins a lane on arrival (see AddLabor).
    // Seats = min(MaxLaborers, repeats queued), so one spear draws one colonist.
    private readonly List<Worker> claimers = new List<Worker>(MaxLaborers);

    /// <summary>Colonists headed to or standing at this bench.</summary>
    public int ClaimCount { get { PruneClaims(); return claimers.Count; } }

    /// <summary>Seats colonists may claim: one per queued repeat, capped at <see cref="MaxLaborers"/>.</summary>
    public int Seats => Mathf.Min(MaxLaborers, TotalRepeats);

    /// <summary>Room for this crafter: a free seat, or already claimed by them.</summary>
    public bool CanClaim(Worker w)
    {
        if (w == null) return false;
        PruneClaims();
        return claimers.Contains(w) || claimers.Count < Seats;
    }

    /// <summary>Claim a seat for a crafter. False when every seat is taken.</summary>
    public bool Claim(Worker w)
    {
        if (!CanClaim(w)) return false;
        if (!claimers.Contains(w)) claimers.Add(w);
        return true;
    }

    /// <summary>Release by worker — safe whether or not they held a seat.</summary>
    public void Release(Worker w)
    {
        if (w != null) claimers.Remove(w);
    }

    void PruneClaims()
    {
        for (int i = claimers.Count - 1; i >= 0; i--)
            if (claimers[i] == null) claimers.RemoveAt(i);
    }

    /// <summary>"Waiting for 2 Stick" while a repeat is held for materials; empty otherwise.</summary>
    public string Status => status;

    /// <summary>The station's own collider — approach points and reach are edge distances against it.</summary>
    public Collider ApproachCollider
    {
        get
        {
            if (cachedCollider == null) cachedCollider = GetComponent<Collider>();
            return cachedCollider;
        }
    }

    /// <summary>The one colony store: the living campfire's stockpile (null with no campfire).</summary>
    public Inventory Stockpile
    {
        get
        {
            BaseBuilding fire = Faction.Campfire;
            return fire != null ? fire.Stockpile : null;
        }
    }

    void Awake()
    {
        ActiveRegistry<CraftStation>.Register(this);
        targetable = GetComponent<ITargetable>();   // exists: the building adds this component from its own Awake
    }
    void OnDestroy() { ActiveRegistry<CraftStation>.Unregister(this); }

    public float Speed(WorkCategory c)
    {
        int i = (int)c;
        return speeds != null && i < speeds.Length ? speeds[i] : 0f;
    }

    /// <summary>Does this bench make this recipe at all (its category has a speed here)?</summary>
    public bool Lists(CraftingCatalog.Recipe r) => r != null && Speed(r.category) > 0f;

    /// <summary>Does this bench teach this entry (same tier)?</summary>
    public bool Lists(ResearchCatalog.ResearchDef d) => d != null && d.station == tier && Speed(WorkCategory.Research) > 0f;

    /// <summary>Every repeat still queued, across all entries.</summary>
    public int TotalRepeats
    {
        get
        {
            int n = 0;
            for (int i = 0; i < queue.Count; i++) n += queue[i].remaining;
            return n;
        }
    }

    // ------------------------------------------------------------------
    // Queueing
    // ------------------------------------------------------------------

    /// <summary>
    /// Queue <paramref name="count"/> of a recipe (merged onto a trailing entry of
    /// the same recipe). False when the bench does not make it, its research is
    /// not done, or a once-per-run tool has already been made or is queued.
    /// Affordability is NOT checked — costs are paid at completion.
    /// </summary>
    public bool Enqueue(CraftingCatalog.Recipe r, int count = 1)
    {
        if (r == null || count <= 0 || !Lists(r) || !r.UnlockedFor(Faction.Knowledge)) return false;
        if (r.oncePerRun)
        {
            if (r.made || Queued(r) > 0) return false;
            count = 1;
        }

        QueueEntry last = queue.Count > 0 ? queue[queue.Count - 1] : null;
        if (last != null && last.recipe == r) last.remaining += count;
        else queue.Add(new QueueEntry { recipe = r, remaining = count });

        Version++;
        return true;
    }

    /// <summary>Queue a research entry. False when not listed here, not available, done, or already queued at any of this colony's stations.</summary>
    public bool Enqueue(ResearchCatalog.ResearchDef d)
    {
        if (d == null || !Lists(d) || !Faction.Knowledge.IsAvailable(d)) return false;
        if (IsQueuedAnywhere(d, Faction)) return false;

        queue.Add(new QueueEntry { research = d, remaining = 1 });
        Version++;
        return true;
    }

    /// <summary>How many repeats of <paramref name="r"/> are waiting here (all entries).</summary>
    public int Queued(CraftingCatalog.Recipe r)
    {
        int n = 0;
        for (int i = 0; i < queue.Count; i++)
            if (queue[i].recipe == r) n += queue[i].remaining;
        return n;
    }

    public bool IsQueued(ResearchCatalog.ResearchDef d)
    {
        for (int i = 0; i < queue.Count; i++)
            if (queue[i].research == d) return true;
        return false;
    }

    /// <summary>
    /// Is <paramref name="d"/> queued at any bench <paramref name="owner"/>
    /// owns? Research is de-duplicated per COLONY (2026-09-16): a rival
    /// researching Quarrying must not block the player's own entry.
    /// </summary>
    public static bool IsQueuedAnywhere(ResearchCatalog.ResearchDef d, Faction owner)
    {
        var list = ActiveList;
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null && list[i].Faction == owner && list[i].IsQueued(d)) return true;
        return false;
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= queue.Count) return;
        DropEntry(queue[index]);
        queue.RemoveAt(index);
        status = "";
        Version++;
    }

    public void Clear()
    {
        if (queue.Count == 0) return;
        for (int i = 0; i < queue.Count; i++) DropEntry(queue[i]);
        queue.Clear();
        status = "";
        Version++;
    }

    /// <summary>An entry is leaving the queue: every lane on it goes back to looking for work.</summary>
    void DropEntry(QueueEntry e)
    {
        for (int i = 0; i < lanes.Count; i++)
        {
            if (lanes[i].entry != e) continue;
            lanes[i].entry = null;
            lanes[i].progress = 0f;
        }
    }

    // ------------------------------------------------------------------
    // Labor
    // ------------------------------------------------------------------

    bool Fresh(Lane l) => Time.time - l.lastLaborTime < IdleAfter;

    Lane FindLane(object who)
    {
        for (int i = 0; i < lanes.Count; i++)
            if (lanes[i].who == who) return lanes[i];
        return null;
    }

    int LanesOn(QueueEntry e)
    {
        int n = 0;
        for (int i = 0; i < lanes.Count; i++)
            if (lanes[i].entry == e) n++;
        return n;
    }

    /// <summary>The first entry with a repeat nobody is working, or null.</summary>
    QueueEntry FreeEntry()
    {
        for (int i = 0; i < queue.Count; i++)
            if (LanesOn(queue[i]) < queue[i].remaining) return queue[i];
        return null;
    }

    /// <summary>Hands gone for <see cref="IdleAfter"/> bank their repeat on its entry and leave the bench.</summary>
    void PruneLanes()
    {
        for (int i = lanes.Count - 1; i >= 0; i--)
        {
            Lane l = lanes[i];
            if (Fresh(l)) continue;
            Bank(l);
            lanes.RemoveAt(i);
        }
    }

    void Bank(Lane l)
    {
        if (l.entry != null && l.progress > l.entry.progress) l.entry.progress = l.progress;
        l.entry = null;
        l.progress = 0f;
    }

    void RefreshLive(QueueEntry e)
    {
        float max = 0f;
        for (int i = 0; i < lanes.Count; i++)
            if (lanes[i].entry == e && lanes[i].progress > max) max = lanes[i].progress;
        e.live = max;
    }

    /// <summary>
    /// <paramref name="who"/> works the bench for <paramref name="dt"/> seconds.
    /// Returns false when there is nothing for these hands: the queue is empty,
    /// every seat is taken, or every queued repeat already has a pair of hands.
    /// <paramref name="hands"/> is the laborer's inventory (may be null) — items
    /// in it count toward, and are taken for, the costs of their own repeat.
    ///
    /// The player's character always wins a place (2026-09-04): with the bench
    /// full they evict the stalest colonist, and with every repeat taken they
    /// take one over, progress and all; the colonist waits beside the bench
    /// until a repeat frees up. Colonists never evict anyone.
    /// </summary>
    public bool AddLabor(float dt, object who, Inventory hands)
    {
        if (queue.Count == 0 || who == null) return false;
        PruneLanes();

        Lane lane = FindLane(who);
        bool isPlayer = who is PlayerCharacter;

        if (lane == null)
        {
            if (lanes.Count >= MaxLaborers)
            {
                if (!isPlayer) return false;   // bench full
                int stalest = StalestColonistLane();
                if (stalest < 0) return false;
                Bank(lanes[stalest]);
                lanes.RemoveAt(stalest);
            }
            // Only join when there is a repeat for these hands — a waiting
            // colonist must not allocate a lane every frame it asks.
            if (FreeEntry() == null && !(isPlayer && StalestColonistLane() >= 0)) return false;
            lane = new Lane { who = who };
            lanes.Add(lane);
            if (LaborerCount >= 2) DevQuests.Signal("craft:shared");
        }

        if (lane.entry == null)
        {
            QueueEntry e = FreeEntry();
            if (e != null)
            {
                lane.entry = e;
                lane.progress = e.progress;   // inherit an abandoned repeat
                e.progress = 0f;
            }
            else if (isPlayer)
            {
                int victim = StalestColonistLane();
                if (victim < 0) return false;
                lane.entry = lanes[victim].entry;
                lane.progress = lanes[victim].progress;
                lanes.RemoveAt(victim);
            }
            else
            {
                lanes.Remove(lane);
                return false;
            }
        }

        lane.lastLaborTime = Time.time;
        QueueEntry entry = lane.entry;
        // An evicted lane can hold no entry (DropEntry nulls it, PruneLanes
        // reaps it later) - the player would inherit null (overnight 2026-09-17).
        if (entry == null) { lanes.Remove(lane); return false; }

        // Research finished elsewhere (or a tool made elsewhere) while it waited here
        if (entry.research != null && Faction.Knowledge.IsDone(entry.research)) { RemoveEntry(entry); return true; }
        if (entry.recipe != null && entry.recipe.oncePerRun && entry.recipe.made) { RemoveEntry(entry); return true; }

        lane.progress += dt * Speed(entry.Def.Category);
        if (lane.progress > entry.live) entry.live = lane.progress;
        if (lane.progress >= entry.Def.seconds) TryComplete(lane, who, hands);
        return true;
    }

    int StalestColonistLane()
    {
        int idx = -1;
        float oldest = float.MaxValue;
        for (int i = 0; i < lanes.Count; i++)
        {
            if (lanes[i].who is PlayerCharacter) continue;
            if (lanes[i].lastLaborTime < oldest) { oldest = lanes[i].lastLaborTime; idx = i; }
        }
        return idx;
    }

    void RemoveEntry(QueueEntry e)
    {
        DropEntry(e);
        queue.Remove(e);
        status = "";
        Version++;
    }

    void TryComplete(Lane lane, object who, Inventory hands)
    {
        QueueEntry e = lane.entry;
        WorkDef def = e.Def;
        Inventory stock = Stockpile;

        if (!def.Pay(hands, stock))
        {
            // Hold at 100% until the missing part turns up
            lane.progress = def.seconds;
            string missing = def.MissingText(hands, stock);
            status = missing.Length > 0 ? "Waiting for " + missing : "Waiting for materials";
            return;
        }
        status = "";

        if (e.research != null)
        {
            // A research that carries a tool equips the player with it as it
            // completes — learning to cut wood and making the axe are one step.
            Deliver(e.research.tool, 1, who, stock);
            Faction.Knowledge.Complete(e.research);
            e.remaining = 0;
        }
        else
        {
            CraftingCatalog.Recipe r = e.recipe;
            Deliver(r.output, r.outputCount, who, stock);
            DevQuests.Signal("craft:" + r.id);
            DevQuests.Signal(who is Worker ? "craft_by_colonist" : "craft_by_player");
            if (r.oncePerRun) { r.made = true; e.remaining = 0; }
            else e.remaining--;
        }

        // These hands look for the next repeat on the next tick
        lane.entry = null;
        lane.progress = 0f;

        if (e.remaining <= 0) { DropEntry(e); queue.Remove(e); }
        else RefreshLive(e);

        Version++;
        if (AudioManager.Instance != null) AudioManager.Instance.PlayBuildingPlaced();
    }

    /// <summary>
    /// A tool goes to the player's hands (they are the character's own kit,
    /// whoever stood at the bench); everything else to the campfire stockpile.
    /// What fits nowhere is lost — a full stockpile is the panel's job to warn about.
    /// </summary>
    static void Deliver(ItemDef item, int count, object who, Inventory stock)
    {
        if (item == null || count <= 0) return;

        PlayerCharacter pc = who as PlayerCharacter;
        if (pc == null) pc = PlayerCharacter.Instance;
        if (item.kind == ItemKind.Tool && pc != null)
        {
            pc.ReceiveCrafted(item, count);
            return;
        }
        if (stock != null) stock.Add(item, count);
    }

    // ------------------------------------------------------------------
    // Lookup
    // ------------------------------------------------------------------

    /// <summary>The nearest living station with something queued, or null.</summary>
    public static CraftStation NearestWithWork(Vector3 from)
    {
        CraftStation best = null;
        float bestSq = float.MaxValue;
        var list = ActiveList;
        for (int i = 0; i < list.Count; i++)
        {
            CraftStation s = list[i];
            if (s == null || !s.HasWork || !s.IsAlive) continue;
            float d = (s.transform.position - from).sqrMagnitude;
            if (d < bestSq) { bestSq = d; best = s; }
        }
        return best;
    }

    /// <summary>False once the building it sits on has died (the component outlives the Health death by a frame).</summary>
    public bool IsAlive
    {
        get
        {
            if (targetable == null) targetable = GetComponent<ITargetable>();
            return targetable == null || targetable.CachedHealth == null || targetable.CachedHealth.IsAlive;
        }
    }
}
