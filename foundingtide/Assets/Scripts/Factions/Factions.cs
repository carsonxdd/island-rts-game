using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The static registry of every <see cref="Faction"/> in the loaded scene
/// (2026-09-09, lap step 1): <see cref="Player"/>, <see cref="Raiders"/>, the
/// rivals, lookup by id, and the per-scene reset.
/// </summary>
/// <remarks>
/// <para><b>Lifetime is the scene's.</b> The set is rebuilt lazily on first
/// access after a scene load: every accessor calls <see cref="EnsureForScene"/>,
/// which compares the active scene's handle with the one the set was built for
/// and starts over when it differs. That is deliberate — a <c>sceneLoaded</c>
/// callback runs AFTER every <c>Awake</c>, and the first readers of the player
/// faction (<c>ResourceManager.Awake</c>, once the pool moves) are Awakes. A
/// scene reload gets a new handle even for the same scene, so Restart, New Game
/// and every sim run start clean, and the play-mode reset below covers domain
/// reload being off.</para>
/// <para>Rivals are registered by the debug menu (step 1) and by the world
/// generator (step 5). <see cref="Relations.MaxFactions"/> caps the count.</para>
/// </remarks>
public static class Factions
{
    static readonly List<Faction> all = new List<Faction>(Relations.MaxFactions);
    static readonly Faction[] byId = new Faction[Relations.MaxFactions];
    static Faction player;
    static Faction raiders;
    static ulong builtForScene;   // Scene.handle raw data the current set belongs to; 0 = none

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { ResetAll(); }

    public static readonly Color PlayerColor = new Color(0.95f, 0.78f, 0.30f);   // the campfire's gold
    public static readonly Color RaiderColor = new Color(0.85f, 0.20f, 0.18f);   // the minimap's raider red

    /// <summary>The player's colony. Never null once a scene is loaded.</summary>
    public static Faction Player { get { EnsureForScene(); return player; } }

    /// <summary>The night raiders: a faction with no colony, hostile to all. Never null once a scene is loaded.</summary>
    public static Faction Raiders { get { EnsureForScene(); return raiders; } }

    /// <summary>Every live faction in registration order (Player first, Raiders second). Index loop, no allocation.</summary>
    public static IReadOnlyList<Faction> All { get { EnsureForScene(); return all; } }

    /// <summary>Faction by <see cref="Faction.Id"/>, or null.</summary>
    public static Faction ById(int id)
    {
        return id >= 0 && id < byId.Length ? byId[id] : null;
    }

    /// <summary>
    /// Adds a faction (a rival, normally) and gives it its relations row. Returns
    /// null, with an error, past <see cref="Relations.MaxFactions"/>.
    /// </summary>
    public static Faction Register(string name, Faction.Kind kind, Color color)
    {
        EnsureForScene();
        return RegisterInternal(name, kind, color);
    }

    static Faction RegisterInternal(string name, Faction.Kind kind, Color color)
    {
        int id = -1;
        for (int i = 0; i < byId.Length; i++)
            if (byId[i] == null) { id = i; break; }
        if (id < 0)
        {
            Debug.LogError("Factions: cannot register '" + name + "' — " + Relations.MaxFactions + " factions already exist.");
            return null;
        }

        Faction f = new Faction(id, name, kind, color);
        byId[id] = f;
        all.Add(f);
        Relations.InitRow(f);
        return f;
    }

    /// <summary>
    /// Removes a faction (a debug rival being cleared). The Player and the
    /// Raiders cannot be removed; their objects and state are the scene's.
    /// </summary>
    public static void Unregister(Faction f)
    {
        if (f == null || f == player || f == raiders) return;
        if (byId[f.Id] != f) return;
        byId[f.Id] = null;
        all.Remove(f);
    }

    /// <summary>Forgets every faction and relation. Called by the play-mode reset and by <see cref="EnsureForScene"/> on a new scene.</summary>
    public static void ResetAll()
    {
        all.Clear();
        for (int i = 0; i < byId.Length; i++) byId[i] = null;
        Relations.Clear();
        Diplomacy.Clear();
        Expedition.Clear();
        Loot.Clear();
        Siege.Clear();
        player = null;
        raiders = null;
        builtForScene = 0;
    }

    /// <summary>Builds the Player and Raiders pair for the active scene if the set belongs to another one (or none).</summary>
    static void EnsureForScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        ulong handle = scene.handle.GetRawData();
        if (builtForScene == handle && player != null) return;

        ResetAll();
        builtForScene = handle;
        player = RegisterInternal("Castaways", Faction.Kind.Player, PlayerColor);
        raiders = RegisterInternal("Raiders", Faction.Kind.Raiders, RaiderColor);

        if (scene.name == MenuFlow.GameSceneName) DevQuests.Signal("faction:bootstrap");
    }
}
