using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A bench with a queue (2026-09-03): the campfire and the Workshop each carry
/// one. Recipes and research are queued front to back and worked one entry at a
/// time; progress per second is <c>speed[category] × labor</c>, where labor is 1
/// while <i>someone</i> is at the bench (the player's character now, a Crafter
/// colonist from Slice 3) and 0 otherwise — a queue nobody stands at does not
/// move. One laborer per station; a second is told the bench is busy.
///
/// Costs are charged when an entry completes (<see cref="WorkDef.Pay"/>). If they
/// cannot be met at that moment the entry holds at 100% and <see cref="Status"/>
/// says what is missing; the next tick that can pay finishes it. Output goes to
/// the campfire stockpile whichever station made it — one colony store — except a
/// tool, which goes into the player's hands when the player is the laborer.
///
/// Runtime-added by <c>BaseBuilding.Awake</c> / <c>Workshop.Awake</c> (the
/// RaidDirector pattern), so its public fields are the LIVE values and no prefab
/// carries a stale copy. Slice 3 changes the Workshop's speed table.
/// </summary>
public class CraftStation : MonoBehaviour
{
    public static IReadOnlyList<CraftStation> ActiveList => ActiveRegistry<CraftStation>.List;

    /// <summary>Seconds without labor before the panel says nobody is at the bench.</summary>
    public const float IdleAfter = 0.5f;

    public sealed class QueueEntry
    {
        public CraftingCatalog.Recipe recipe;
        public ResearchCatalog.ResearchDef research;
        /// <summary>Repeats left (recipes); always 1 for research.</summary>
        public int remaining;
        /// <summary>Seconds of scaled labor on the current repeat.</summary>
        public float progress;

        public WorkDef Def => recipe != null ? (WorkDef)recipe : research;
        public string Title => Def.title;
        public bool IsResearch => research != null;
        public float Progress01 => Mathf.Clamp01(progress / Mathf.Max(0.01f, Def.seconds));
    }

    [Tooltip("Which research tier this bench lists.")]
    public ResearchCatalog.Station tier = ResearchCatalog.Station.Campfire;

    [Tooltip("Progress multiplier per WorkCategory (Tool, Weapon, Construction, Research). 0 = not listed here.")]
    public float[] speeds = { 1f, 1f, 1f, 1f };

    public string displayName = "Campfire";

    private readonly List<QueueEntry> queue = new List<QueueEntry>();
    private object laborer;
    private float lastLaborTime = float.NegativeInfinity;
    private string status = "";

    private Collider cachedCollider;
    private ITargetable targetable;   // the building this bench sits on, for its Health

    /// <summary>Front to back; index 0 is being worked.</summary>
    public IReadOnlyList<QueueEntry> Queue => queue;

    /// <summary>Bumped on every structural change (add, remove, complete); the panel rebuilds its rows on it.</summary>
    public int Version { get; private set; }

    public bool HasWork => queue.Count > 0;
    public QueueEntry Active => queue.Count > 0 ? queue[0] : null;

    /// <summary>Someone has added labor in the last <see cref="IdleAfter"/> seconds.</summary>
    public bool IsWorked => Time.time - lastLaborTime < IdleAfter;

    /// <summary>Who is at the bench right now, or null.</summary>
    public object Laborer => IsWorked ? laborer : null;

    /// <summary>"Waiting for 2 Stick" while the front entry is held for materials; empty otherwise.</summary>
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
            BaseBuilding fire = BaseBuilding.FindAlive();
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
        if (r == null || count <= 0 || !Lists(r) || !r.Unlocked) return false;
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

    /// <summary>Queue a research entry. False when not listed here, not available, done, or already queued at any station.</summary>
    public bool Enqueue(ResearchCatalog.ResearchDef d)
    {
        if (d == null || !Lists(d) || !ResearchCatalog.IsAvailable(d)) return false;
        if (IsQueuedAnywhere(d)) return false;

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

    public static bool IsQueuedAnywhere(ResearchCatalog.ResearchDef d)
    {
        var list = ActiveList;
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null && list[i].IsQueued(d)) return true;
        return false;
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= queue.Count) return;
        queue.RemoveAt(index);
        if (index == 0) status = "";
        Version++;
    }

    public void Clear()
    {
        if (queue.Count == 0) return;
        queue.Clear();
        status = "";
        Version++;
    }

    // ------------------------------------------------------------------
    // Labor
    // ------------------------------------------------------------------

    /// <summary>
    /// <paramref name="who"/> works the bench for <paramref name="dt"/> seconds.
    /// Returns false when there is nothing to do or someone else is already at
    /// the bench. <paramref name="hands"/> is the laborer's inventory (may be
    /// null) — items in it count toward, and are taken for, the costs.
    /// </summary>
    public bool AddLabor(float dt, object who, Inventory hands)
    {
        if (queue.Count == 0) return false;
        if (laborer != who && IsWorked) return false;   // bench busy

        laborer = who;
        lastLaborTime = Time.time;

        QueueEntry e = queue[0];

        // Research finished elsewhere (or a tool made elsewhere) while it waited here
        if (e.research != null && e.research.done) { queue.RemoveAt(0); status = ""; Version++; return true; }
        if (e.recipe != null && e.recipe.oncePerRun && e.recipe.made) { queue.RemoveAt(0); status = ""; Version++; return true; }

        e.progress += dt * Speed(e.Def.Category);
        if (e.progress >= e.Def.seconds) TryComplete(e, who, hands);
        return true;
    }

    void TryComplete(QueueEntry e, object who, Inventory hands)
    {
        WorkDef def = e.Def;
        Inventory stock = Stockpile;

        if (!def.Pay(hands, stock))
        {
            // Hold at 100% until the missing part turns up
            e.progress = def.seconds;
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
            ResearchCatalog.Complete(e.research);
            queue.RemoveAt(0);
        }
        else
        {
            CraftingCatalog.Recipe r = e.recipe;
            Deliver(r.output, r.outputCount, who, stock);
            if (r.oncePerRun) r.made = true;

            e.remaining--;
            e.progress = 0f;
            if (e.remaining <= 0 || (r.oncePerRun && r.made)) queue.RemoveAt(0);
        }

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
