using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public class Gate : MonoBehaviour
{
    [Header("Gate Type")]
    public bool isStoneGate = false;  // false = wooden gate, true = stone gate
    public bool isGate = true;  // Tag so workers/warriors can identify gates vs walls

    [Header("Health")]
    public float maxHealth = 75f;  // Gates are weaker: Wooden=75, Stone=150 (half of wall HP)
    private Health healthComponent;

    [Header("Building Placement")]
    public float noBuildRadius = 0f;  // Gates have no no-build zone, like walls

    // NavMesh area index for gate passages — friendlies can traverse, enemies cannot
    public static readonly int GateAreaIndex = 3;

    private Vector2Int gridPos;
    private bool registered = false;
    private NavMeshObstacle navObstacle;

    // Static event fired when ANY gate is destroyed — enemies subscribe for breach detection
    public static event System.Action OnAnyGateDestroyed;

    // Static registry for O(1) lookup instead of FindObjectsByType
    private static readonly List<Gate> activeList = new List<Gate>();
    public static IReadOnlyList<Gate> ActiveList => activeList;

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
        maxHealth = isStoneGate ? 150f : 75f;
        healthComponent.maxHealth = maxHealth;
        healthComponent.currentHealth = maxHealth;
        healthComponent.destroyOnDeath = true;
        healthComponent.destroyDelay = 0.5f;
        healthComponent.showHealthText = true;
        healthComponent.showObjectName = true;
        healthComponent.onDeath.AddListener(OnGateDestroyed);

        // Gates block enemies with NavMeshObstacle carving (same as walls)
        // Friendly units traverse via OffMeshLinks (setup in SetupGateLinks)
        navObstacle = GetComponent<NavMeshObstacle>();
        if (navObstacle == null)
        {
            navObstacle = gameObject.AddComponent<NavMeshObstacle>();
            navObstacle.shape = NavMeshObstacleShape.Box;
            navObstacle.size = new Vector3(1f, 2f, 1f);
            navObstacle.center = new Vector3(0f, 1f, 0f);
        }
        navObstacle.carving = true;
        navObstacle.carveOnlyStationary = true;
        navObstacle.carvingTimeToStationary = 0.1f;

        // Setup OffMeshLinks so friendly units (warriors/workers) can pass through
        SetupGateLinks();

        // Register with WallGrid (neighbors connect to gates like walls)
        gridPos = WallGrid.Instance.WorldToGrid(transform.position);
        WallGrid.Instance.Register(gridPos, this);
        registered = true;

        Debug.Log($"Gate: Initialized {(isStoneGate ? "Stone" : "Wooden")} Gate with {maxHealth} HP at {transform.position}");
    }

    /// <summary>
    /// Create OffMeshLinks in both X and Z axes so that friendly units can
    /// traverse through the gate regardless of wall orientation. Only the
    /// link whose endpoints land on valid NavMesh (not carved by adjacent
    /// walls) will be used by the pathfinder.
    /// </summary>
    void SetupGateLinks()
    {
        float linkOffset = 1.0f; // Must be outside the NavMeshObstacle carve zone (box half-extent = 0.5)

        // X-axis link (perpendicular passage for N-S oriented walls)
        CreateGateLink("GateLinkX",
            new Vector3(linkOffset, 0f, 0f),
            new Vector3(-linkOffset, 0f, 0f));

        // Z-axis link (perpendicular passage for E-W oriented walls)
        CreateGateLink("GateLinkZ",
            new Vector3(0f, 0f, linkOffset),
            new Vector3(0f, 0f, -linkOffset));
    }

    void CreateGateLink(string linkName, Vector3 localStart, Vector3 localEnd)
    {
        GameObject linkObj = new GameObject(linkName);
        linkObj.transform.parent = transform;
        linkObj.transform.localPosition = Vector3.zero;
        linkObj.transform.localRotation = Quaternion.identity;

        GameObject startPt = new GameObject("Start");
        startPt.transform.parent = linkObj.transform;
        startPt.transform.localPosition = localStart;

        GameObject endPt = new GameObject("End");
        endPt.transform.parent = linkObj.transform;
        endPt.transform.localPosition = localEnd;

        OffMeshLink link = linkObj.AddComponent<OffMeshLink>();
        link.startTransform = startPt.transform;
        link.endTransform = endPt.transform;
        link.area = GateAreaIndex;
        link.biDirectional = true;
        link.autoUpdatePositions = true;
        link.costOverride = -1f;
        link.activated = true;
    }

    void OnGateDestroyed()
    {
        UnregisterFromGrid();
        // Notify all enemies that a gate was destroyed (breach detection)
        OnAnyGateDestroyed?.Invoke();
        Debug.Log($"Gate: Destroyed at {transform.position}");
    }

    void OnDestroy()
    {
        activeList.Remove(this);
        UnregisterFromGrid();
    }

    private void UnregisterFromGrid()
    {
        if (registered && WallGrid.Instance != null)
        {
            WallGrid.Instance.Unregister(gridPos);
            registered = false;
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 0.3f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, 1f);
    }
}
