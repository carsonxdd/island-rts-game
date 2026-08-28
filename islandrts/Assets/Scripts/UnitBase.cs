using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// Shared base for the three unit types (Worker : UnitBase&lt;Worker&gt;, etc.).
/// Owns ActiveRegistry registration, cached component access, floating state
/// text creation, and small setup helpers shared verbatim by the subclasses.
/// Unit behavior (AI action setup, executors, per-unit audio) stays in the
/// subclasses. CRTP generic so each subclass gets its own registry list.
/// </summary>
public abstract class UnitBase<T> : MonoBehaviour, ITargetable where T : UnitBase<T>
{
    public static IReadOnlyList<T> ActiveList => ActiveRegistry<T>.List;

    [Header("State Display")]
    public bool showStateText = true;
    public float textHeightOffset = 2.5f;

    protected AIBrain aiBrain;
    protected NavMeshAgent agent;
    protected Health healthComponent;
    protected FloatingText floatingText;
    protected AudioSource combatAudioSource;

    public NavMeshAgent CachedAgent => agent;

    public Health CachedHealth
    {
        get
        {
            if (healthComponent == null) healthComponent = GetComponent<Health>();
            return healthComponent;
        }
    }

    protected virtual void Awake()
    {
        ActiveRegistry<T>.Register((T)this);
        PerfCounters.Hit(PerfCounters.K.UnitSpawn);
    }

    protected virtual void OnDestroy()
    {
        ActiveRegistry<T>.Unregister((T)this);
        PerfCounters.Hit(PerfCounters.K.UnitDeath);
    }

    /// <summary>Fetch the NavMeshAgent into <see cref="agent"/>; logs an error and returns false if missing.</summary>
    protected bool FetchAgent()
    {
        agent = GetComponent<NavMeshAgent>();
        if (agent == null)
        {
            Debug.LogError(GetType().Name + ": No NavMeshAgent found!");
            return false;
        }
        return true;
    }

    /// <summary>Fetch-or-add the Health component, set stats, and hook the death callback.</summary>
    protected void SetupHealth(float maxHealth, UnityEngine.Events.UnityAction onDeath)
    {
        healthComponent = GetComponent<Health>();
        if (healthComponent == null)
        {
            healthComponent = gameObject.AddComponent<Health>();
        }
        healthComponent.maxHealth = maxHealth;
        healthComponent.currentHealth = maxHealth;
        healthComponent.destroyOnDeath = true;
        healthComponent.onDeath.AddListener(onDeath);
    }

    /// <summary>Create the floating state text if <see cref="showStateText"/> is enabled.</summary>
    protected void CreateStateText(float fontSize, string initialText, Color initialColor)
    {
        if (!showStateText) return;
        floatingText = gameObject.AddComponent<FloatingText>();
        floatingText.heightOffset = textHeightOffset;
        floatingText.fontSize = fontSize;
        floatingText.initialText = initialText;
        floatingText.initialColor = initialColor;
    }

    /// <summary>Current AI action display name from the blackboard, or <paramref name="fallback"/> if unavailable.</summary>
    protected string StateDisplayName(string fallback)
    {
        string displayName = aiBrain != null && aiBrain.blackboard != null ? aiBrain.blackboard.stateDisplayName : fallback;
        return displayName != null ? displayName : fallback;
    }

    /// <summary>Add and initialize a StuckResolver for this unit. Caller assigns onStuckReset.</summary>
    protected StuckResolver CreateStuckResolver()
    {
        var stuckResolver = gameObject.AddComponent<StuckResolver>();
        stuckResolver.Initialize(agent, ActiveRegistry<T>.IndexOf((T)this));
        return stuckResolver;
    }

    /// <summary>Create the 3D spatial combat audio source (Warrior/Enemy).</summary>
    protected void SetupCombatAudio(float volume)
    {
        combatAudioSource = AudioHelper.CreateSpatialAudioSource(gameObject, volume, 8f, 35f);
    }

    /// <summary>Play a one-shot on the combat audio source, tolerating missing source/clip.</summary>
    protected void PlayCombatClip(AudioClip clip, float volume)
    {
        if (combatAudioSource != null && clip != null)
        {
            combatAudioSource.PlayOneShot(clip, volume);
        }
    }
}
