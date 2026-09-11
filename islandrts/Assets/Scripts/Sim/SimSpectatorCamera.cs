#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

/// <summary>
/// The camera director for the VISUAL balance sim (SimRunner's -simvisual mode).
///
/// A sweep answers "did Turtle survive day 20"; watching answers "why". That
/// only works if the view is already pointed at the thing that mattered, so this
/// is a director rather than a camera: every half second it scores a handful of
/// candidate shots and holds the winner.
///
/// Shots, most urgent first:
///   Campfire  — the fire lost HP in the last few seconds. Nothing outranks it.
///   Landing   — raiders just came ashore (EnemySpawner.OnRaidLanded).
///   Battle    — the densest cluster of hostile bodies near friendly ones.
///   Raiders   — enemies alive but not yet in contact; watch them walk in.
///   Colony    — the default: framed on the campfire, zoomed to fit the base.
///
/// It reads nothing but positions and health, drives nothing but the camera, and
/// is never created outside <see cref="SimHooks.Visual"/> — so it cannot change
/// what a run decides. That is the whole point of the Simulating/Headless split
/// in <see cref="SimHooks"/>: a visual run has to be a watchable version of the
/// headless run, not a different game.
/// </summary>
/// <remarks>
/// It does not take the camera over wholesale. CameraController keeps running its
/// zoom smoothing and its per-frame clip-plane fit (a fixed near clip starves the
/// ground of shadow texels — see CLAUDE.md), and only the INPUT half is
/// suppressed via <see cref="CameraController.SuppressInput"/>. Framing goes
/// through CenterOn each frame on a SmoothDamp'd focus point, which is what makes
/// a cut glide instead of teleport.
/// </remarks>
public class SimSpectatorCamera : MonoBehaviour
{
    // --- shot kinds, in priority order -------------------------------------

    public enum Shot { Colony, Raiders, Battle, Landing, Campfire }

    /// <summary>
    /// Orthographic size per shot. Tighter = more of a fight, less of an island.
    /// Pulled in about a third on 2026-09-10: a lab cell is a third of the screen
    /// wide, and at the old sizes a battle was a scatter of specks. The floor is
    /// CameraController.minOrthoSize 5, so there is room to go tighter still.
    /// </summary>
    private static readonly float[] ShotZoom = { 18f, 15f, 9f, 12f, 8f };

    /// <summary>Score per shot when its trigger is live. Ordering, not a curve.</summary>
    private static readonly float[] ShotScore = { 1f, 30f, 55f, 80f, 110f };

    // --- tuning ------------------------------------------------------------

    /// <summary>How often the director re-scores. Cheap, but there is no reason to do it per frame.</summary>
    private const float PickInterval = 0.5f;

    /// <summary>A shot is held at least this long, so the view never strobes between two fights.</summary>
    private const float MinShotSeconds = 4f;

    /// <summary>Past the minimum hold, a rival shot still has to beat the current one by this much.</summary>
    private const float SwitchMargin = 1.25f;

    /// <summary>Seconds a campfire HP drop keeps the camera on the fire.</summary>
    private const float DamageMemory = 5f;

    /// <summary>Seconds a raid landing stays the most interesting thing on the island.</summary>
    private const float LandingMemory = 7f;

    /// <summary>Radius a battle cluster is measured in.</summary>
    private const float ClusterRadius = 18f;

    /// <summary>How fast the focus point chases the shot's target.</summary>
    private const float FocusSmoothTime = 0.55f;

    // --- state -------------------------------------------------------------

    private CameraController rig;

    private Shot current = Shot.Colony;
    private float shotStarted;
    private float nextPick;

    private Vector3 focus;
    private Vector3 focusVelocity;
    private bool primed;

    private float lastCampfireHp = -1f;
    private float lastDamageTime = -999f;
    private Vector3 lastLanding;
    private float lastLandingTime = -999f;

    /// <summary>What the director is currently watching. The overlay prints it.</summary>
    public Shot CurrentShot => current;

    // --- lifecycle ---------------------------------------------------------

    /// <summary>Attach to the camera rig. Safe to call every scene load; no-ops off the visual path.</summary>
    public static SimSpectatorCamera Ensure()
    {
        if (!SimHooks.Visual) return null;

        CameraController rig = CameraController.Instance;
        if (rig == null) return null;

        SimSpectatorCamera cam = rig.GetComponent<SimSpectatorCamera>();
        if (cam == null) cam = rig.gameObject.AddComponent<SimSpectatorCamera>();
        return cam;
    }

    private void Awake()
    {
        rig = GetComponent<CameraController>();
        CameraController.SuppressInput = true;
        EnemySpawner.OnRaidLanded += OnRaidLanded;
    }

    private void OnDestroy()
    {
        EnemySpawner.OnRaidLanded -= OnRaidLanded;
        CameraController.SuppressInput = false;
    }

    private void OnRaidLanded(Vector3 where)
    {
        lastLanding = where;
        lastLandingTime = Time.time;
    }

    private void LateUpdate()
    {
        if (rig == null) return;

        WatchCampfire();

        if (Time.time >= nextPick)
        {
            nextPick = Time.time + PickInterval;
            Pick();
        }

        Vector3 target = TargetFor(current);

        // The first frame SNAPS. Smoothing from the field's default zero would
        // glide the opening shot in from the world origin, which on a 150 m map
        // is a long, silly pan across open sea.
        if (!primed) { focus = target; primed = true; }

        focus = Vector3.SmoothDamp(focus, target, ref focusVelocity, FocusSmoothTime,
                                   Mathf.Infinity, Time.unscaledDeltaTime);
        rig.CenterOn(focus);
        rig.SetZoom(ShotZoom[(int)current]);
    }

    // --- the director ------------------------------------------------------

    /// <summary>
    /// Health has no static damage event, and adding one for a dev tool would put
    /// a per-hit callback in everyone's build. Polling the fire's HP each frame
    /// costs nothing and cannot be seen by the run.
    /// </summary>
    private void WatchCampfire()
    {
        BaseBuilding fire = Factions.Player.Campfire;
        if (fire == null) { lastCampfireHp = -1f; return; }

        float hp = fire.GetCurrentHealth();
        if (lastCampfireHp >= 0f && hp < lastCampfireHp - 0.01f) lastDamageTime = Time.time;
        lastCampfireHp = hp;
    }

    private void Pick()
    {
        Shot best = Shot.Colony;
        float bestScore = ShotScore[(int)Shot.Colony];

        for (int s = (int)Shot.Raiders; s <= (int)Shot.Campfire; s++)
        {
            if (!Live((Shot)s)) continue;
            if (ShotScore[s] > bestScore)
            {
                bestScore = ShotScore[s];
                best = (Shot)s;
            }
        }

        if (best == current) return;

        // Hold: a shot gets its minimum seconds before anything can take it.
        float held = Time.time - shotStarted;
        if (held < MinShotSeconds) return;

        // Past that, a rival has to be meaningfully more interesting, or the
        // camera ping-pongs between two fights scoring within a hair of each
        // other. The margin only applies while the CURRENT shot is still live,
        // though: comparing a dead shot's score would strand the camera on the
        // campfire for the rest of the run, since nothing outscores it.
        if (Live(current) && bestScore < ShotScore[(int)current] * SwitchMargin) return;

        current = best;
        shotStarted = Time.time;
    }

    private bool Live(Shot shot)
    {
        switch (shot)
        {
            case Shot.Campfire:
                return Factions.Player.Campfire != null && Time.time - lastDamageTime < DamageMemory;
            case Shot.Landing:
                return Time.time - lastLandingTime < LandingMemory;
            case Shot.Battle:
                return ClusterCentre(out _) >= 3;
            case Shot.Raiders:
                return Enemy.ActiveList.Count > 0;
            default:
                return true;
        }
    }

    private Vector3 TargetFor(Shot shot)
    {
        switch (shot)
        {
            case Shot.Campfire:
            {
                BaseBuilding fire = Factions.Player.Campfire;
                if (fire != null) return fire.transform.position;
                break;
            }
            case Shot.Landing:
                return lastLanding;
            case Shot.Battle:
            {
                Vector3 centre;
                if (ClusterCentre(out centre) >= 3) return centre;
                break;
            }
            case Shot.Raiders:
            {
                var enemies = Enemy.ActiveList;
                if (enemies.Count > 0)
                {
                    Vector3 sum = Vector3.zero;
                    for (int i = 0; i < enemies.Count; i++) sum += enemies[i].transform.position;
                    return sum / enemies.Count;
                }
                break;
            }
        }

        return ColonyCentre();
    }

    /// <summary>
    /// The densest fight: for each raider, how many bodies (raiders and the
    /// player's warriors) sit within <see cref="ClusterRadius"/>. Returns the best
    /// count and its centroid. O(enemies × (enemies + warriors)), which at raid
    /// sizes of ~22 is nothing, and it only runs twice a second.
    /// </summary>
    private int ClusterCentre(out Vector3 centre)
    {
        centre = Vector3.zero;

        var enemies = Enemy.ActiveList;
        var warriors = Warrior.ActiveList;
        if (enemies.Count == 0) return 0;

        float sqrRadius = ClusterRadius * ClusterRadius;
        int bestCount = 0;

        for (int i = 0; i < enemies.Count; i++)
        {
            Vector3 seed = enemies[i].transform.position;
            Vector3 sum = Vector3.zero;
            int count = 0;

            for (int e = 0; e < enemies.Count; e++)
            {
                Vector3 p = enemies[e].transform.position;
                if ((p - seed).sqrMagnitude > sqrRadius) continue;
                sum += p;
                count++;
            }
            for (int w = 0; w < warriors.Count; w++)
            {
                Vector3 p = warriors[w].transform.position;
                if ((p - seed).sqrMagnitude > sqrRadius) continue;
                sum += p;
                count++;
            }

            if (count > bestCount)
            {
                bestCount = count;
                centre = sum / count;
            }
        }

        return bestCount;
    }

    /// <summary>
    /// The colony's default framing: the campfire, nudged toward whatever the
    /// player's colonists are actually doing, so an economy sprawling toward one
    /// coast does not sit half off screen.
    /// </summary>
    private Vector3 ColonyCentre()
    {
        BaseBuilding fire = Factions.Player.Campfire;
        Vector3 anchor = fire != null ? fire.transform.position : transform.position;

        var workers = Worker.ActiveList;
        if (workers.Count == 0) return anchor;

        Faction player = Factions.Player;
        Vector3 sum = Vector3.zero;
        int count = 0;
        for (int i = 0; i < workers.Count; i++)
        {
            if (workers[i].Faction != player) continue;
            sum += workers[i].transform.position;
            count++;
        }
        if (count == 0) return anchor;

        // Two parts fire to one part crowd: the base stays the subject, the crowd
        // only leans the frame.
        return Vector3.Lerp(anchor, sum / count, 0.33f);
    }
}
#endif
