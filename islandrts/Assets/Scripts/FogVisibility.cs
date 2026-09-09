using UnityEngine;

/// <summary>
/// Hides an object while the fog says the player should not see it (2026-09-09): every
/// renderer under it is switched off (art, health bar, state label, all of it) and,
/// optionally, its click collider is moved off the clickable layer so it cannot be
/// hovered or right-clicked through the dark. Raiders use the <see cref="Rule.Visible"/>
/// rule — they show only while something of yours is looking — while resource nodes
/// and pickups use <see cref="Rule.Explored"/>, since a tree does not walk away once
/// found.
/// </summary>
/// <remarks>
/// Renderers are collected in Start and once more on the first LateUpdate, because the
/// health bar and the floating label are built by other components' Start on the spawn
/// frame and Start order across components is undefined. Checks are staggered per
/// object and cost one grid read. Under the sim nothing renders, so it disables itself.
/// Toggling <c>Renderer.enabled</c> rather than the object keeps the AI, the agent and
/// the colliders alive — a hidden raider is still walking.
/// </remarks>
[DisallowMultipleComponent]
public class FogVisibility : MonoBehaviour
{
    public enum Rule { Explored, Visible }

    private const float CheckInterval = 0.12f;
    private const int IgnoreRaycastLayer = 2;

    public Rule rule = Rule.Visible;
    /// <summary>Layer the root sits on while shown; -1 = leave the layer alone.</summary>
    public int clickLayer = -1;

    /// <summary>True while the object is hidden by the fog.</summary>
    public bool Hidden { get; private set; }

    private Renderer[] renderers;
    private bool recollected;
    private float nextCheck;

    /// <summary>Attach to <paramref name="go"/>. <paramref name="clickLayer"/> is the root's normal layer, restored when shown.</summary>
    public static FogVisibility Attach(GameObject go, Rule rule, int clickLayer = -1)
    {
        if (go == null) return null;
        FogVisibility f = go.GetComponent<FogVisibility>();
        if (f == null) f = go.AddComponent<FogVisibility>();
        f.rule = rule;
        f.clickLayer = clickLayer;
        return f;
    }

    void Start()
    {
        if (SimHooks.Simulating) { enabled = false; return; }
        nextCheck = Time.time + Random.Range(0f, CheckInterval);
        Collect();
        Check(force: true);
    }

    void LateUpdate()
    {
        if (!recollected)
        {
            // Every Start of the spawn frame has run by now: pick up the bars and labels.
            recollected = true;
            Collect();
            Apply(Hidden);
        }
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + CheckInterval;
        Check(force: false);
    }

    /// <summary>
    /// Only renderers that are ON when collected are tracked (2026-09-09): a prefab can
    /// carry deliberately disabled art (Tree.prefab keeps the old FBX tree under its
    /// root, renderers off, beside the low-poly Model), and restoring "everything"
    /// switched both trees on at once - the "two trees in one" bug. A recollect first
    /// restores the tracked set so a hidden object does not read as "nothing to track".
    /// </summary>
    void Collect()
    {
        bool wasHidden = Hidden;
        if (renderers != null && wasHidden) Apply(false);
        Renderer[] all = GetComponentsInChildren<Renderer>(true);
        int count = 0;
        for (int i = 0; i < all.Length; i++)
            if (all[i] != null && all[i].enabled) count++;
        renderers = new Renderer[count];
        for (int i = 0, k = 0; i < all.Length; i++)
            if (all[i] != null && all[i].enabled) renderers[k++] = all[i];
        if (wasHidden) Apply(true);
    }

    void Check(bool force)
    {
        FogOfWar fog = FogOfWar.Instance;
        bool show = fog == null
            || (rule == Rule.Visible ? fog.IsVisible(transform.position) : fog.IsExplored(transform.position));
        bool hide = !show;
        if (!force && hide == Hidden) return;
        if (Hidden && !hide)   // playtest: something the fog was hiding just came into the light
            DevQuests.Signal(rule == Rule.Visible ? "fog:raider_revealed" : "fog:node_revealed");
        Apply(hide);
    }

    void Apply(bool hide)
    {
        Hidden = hide;
        if (renderers != null)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r != null) r.enabled = !hide;
            }
        }
        if (clickLayer >= 0)
        {
            // Ignore Raycast: skipped by OnMouse* events and by every masked click
            // raycast, while Collider.ClosestPoint (approach points) keeps working.
            gameObject.layer = hide ? IgnoreRaycastLayer : clickLayer;
        }
    }
}
