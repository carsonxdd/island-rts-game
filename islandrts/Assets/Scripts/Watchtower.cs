using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public class Watchtower : MonoBehaviour
{
    [Header("Health")]
    public float maxHealth = 200f;
    private Health healthComponent;

    [Header("Building Placement")]
    public float noBuildRadius = 3.0f;

    [Header("Detection")]
    [Tooltip("Early warning system - reveals enemies at greater distance")]
    public float detectionRadius = 20f;

    [Header("Damage Buff Aura")]
    [Tooltip("Warriors within this radius get a damage buff")]
    public float buffRadius = 8f;
    [Tooltip("Damage multiplier for warriors in range (1.25 = 25% buff)")]
    public float damageMultiplier = 1.25f;

    // Static registry for O(1) lookup instead of FindObjectsByType
    private static readonly List<Watchtower> activeList = new List<Watchtower>();
    public static IReadOnlyList<Watchtower> ActiveList => activeList;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { activeList.Clear(); }

    void Awake() { activeList.Add(this); }

    void Start()
    {
        // Setup Health component
        healthComponent = GetComponent<Health>();
        if (healthComponent == null)
        {
            healthComponent = gameObject.AddComponent<Health>();
        }
        healthComponent.maxHealth = maxHealth;
        healthComponent.currentHealth = maxHealth;
        healthComponent.destroyOnDeath = true;
        healthComponent.destroyDelay = 1f;
        healthComponent.showHealthText = true;
        healthComponent.showObjectName = true;
        healthComponent.onDeath.AddListener(OnWatchtowerDestroyed);

        // Disable NavMeshObstacle carving (like other buildings - enemies can surround)
        NavMeshObstacle obstacle = GetComponent<NavMeshObstacle>();
        if (obstacle != null)
        {
            obstacle.carving = false;  // Enemies can surround it from all sides
            Debug.Log("Watchtower: NavMeshObstacle carving disabled for combat");
        }

        Debug.Log($"Watchtower: Initialized with {maxHealth} HP and {detectionRadius}m detection radius at {transform.position}");
    }

    /// <summary>
    /// Returns the best damage multiplier for a unit at the given position.
    /// Checks all watchtowers and returns the highest multiplier if in range, or 1.0 if not.
    /// </summary>
    public static float GetDamageMultiplier(Vector3 position)
    {
        float bestMultiplier = 1f;

        for (int i = 0; i < activeList.Count; i++)
        {
            Watchtower tower = activeList[i];
            if (tower == null) continue;
            Health towerHealth = tower.GetComponent<Health>();
            if (towerHealth != null && !towerHealth.IsAlive) continue;

            float distance = Vector3.Distance(position, tower.transform.position);
            if (distance <= tower.buffRadius && tower.damageMultiplier > bestMultiplier)
            {
                bestMultiplier = tower.damageMultiplier;
            }
        }

        return bestMultiplier;
    }

    void OnWatchtowerDestroyed()
    {
        Debug.Log($"Watchtower: Destroyed at {transform.position}");

        // Play destruction sound if AudioManager exists
        if (AudioManager.Instance != null)
        {
            // AudioManager.Instance.PlayWatchtowerDestroyed();  // TODO: Add sound
        }
    }

    void OnDestroy()
    {
        activeList.Remove(this);
    }

    // Visual feedback in Scene view
    void OnDrawGizmosSelected()
    {
        // Draw no-build radius
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, noBuildRadius);

        // Draw detection radius
        Gizmos.color = new Color(1f, 1f, 0f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        // Draw buff radius
        Gizmos.color = new Color(0.3f, 1f, 0.3f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, buffRadius);
    }
}
