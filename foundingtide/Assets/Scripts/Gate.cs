using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

/// <summary>
/// A doorway in the wall line. Friendly units walk through it, enemies have to break it.
/// Gates are never placed directly - the player converts a finished wall into one.
/// </summary>
/// <remarks>
/// Unlike a wall, a gate does not carve the NavMesh, which is what lets workers path
/// through the colony's perimeter. That also means an enemy pathing through the gap would
/// simply walk past it, so a trigger volume catches enemies inside the doorway and tells
/// them to attack this gate instead.
/// A gate is up to <see cref="MaxWidth"/> cells wide (2026-09-28): ONE object, one health
/// bar and one door over a straight run of cells, its transform on the MIDDLE cell (the
/// held line reads <c>transform.position</c> as the gate's centre). Every cell of the span
/// is registered in <see cref="WallGrid"/> as this gate. Each cell is worth half the HP of
/// the wall it was converted from, so a three-wide gate is three cells' worth.
/// </remarks>
public class Gate : MonoBehaviour, ITargetable, IBuildingIdentity
{
    public BuildingType BuildingType => isStoneGate ? BuildingType.StoneGate : BuildingType.WoodenGate;

    // Owner (lap step 1 commit 5). Set by Spawn.Owned right after Instantiate
    // (commit 6); the player's when nothing set it. Read in Start or later, never Awake.
    Faction owner;
    public Faction Faction { get => owner ?? (owner = Factions.Player); set => owner = value; }
    [Header("Gate Type")]
    public bool isStoneGate = false;  // false = wooden gate, true = stone gate

    [Header("Health")]
    public float maxHealth = 75f;  // written in Start: CellHealth x Width
    private Health healthComponent;
    public Health CachedHealth => healthComponent;

    [Header("Building Placement")]
    public float noBuildRadius = 0f;  // Gates have no no-build zone, like walls

    [Header("Trigger Settings")]
    public float triggerSize = 1.5f;  // Depth of the trigger that catches enemies; its length is the span plus this

    /// <summary>The widest gate one G press makes.</summary>
    public const int MaxWidth = 3;

    /// <summary>HP per cell of gate: half the wall it was converted from.</summary>
    public static float CellHealth(bool stone) => stone ? 150f : 75f;

    // The span, in cells along one axis, relative to the anchor cell the transform sits
    // on: lo..hi, lo <= 0 <= hi. Written by SetSpan before the gate claims its cells; a
    // gate nobody configured is one cell on the X axis.
    private Vector2Int anchorCell;
    private bool alongX = true;
    private int spanLo, spanHi;
    private bool spanSet;
    private bool registered = false;
    private BoxCollider triggerCollider;

    /// <summary>Cells this gate spans (1..<see cref="MaxWidth"/>).</summary>
    public int Width => spanHi - spanLo + 1;
    /// <summary>True when the span runs along world X, false along Z.</summary>
    public bool AlongX => alongX;
    public int SpanLo => spanLo;
    public int SpanHi => spanHi;

    // Static event fired when ANY gate is destroyed — enemies subscribe for breach detection
    public static event System.Action OnAnyGateDestroyed;

    public static IReadOnlyList<Gate> ActiveList => ActiveRegistry<Gate>.List;

    void Awake() { ActiveRegistry<Gate>.Register(this); }

    /// <summary>
    /// The run this gate covers: <paramref name="lo"/>..<paramref name="hi"/> cells from
    /// <paramref name="anchor"/> along X or Z. Call before <see cref="ClaimCells"/> (the
    /// conversion does both in one frame, so the opening is never empty in the grid).
    /// </summary>
    public void SetSpan(Vector2Int anchor, bool runsAlongX, int lo, int hi)
    {
        anchorCell = anchor;
        alongX = runsAlongX;
        spanLo = Mathf.Min(0, lo);
        spanHi = Mathf.Max(0, hi);
        spanSet = true;
    }

    /// <summary>The grid cell at offset <paramref name="k"/> along the span.</summary>
    public Vector2Int CellAt(int k)
    {
        return alongX ? new Vector2Int(anchorCell.x + k, anchorCell.y) : new Vector2Int(anchorCell.x, anchorCell.y + k);
    }

    /// <summary>
    /// Squared XZ distance from <paramref name="point"/> to the nearest cell centre line of
    /// the span - "how far from this gate" for a gate wider than a point. Cells are 1 m.
    /// </summary>
    public float SqrDistanceXZ(Vector3 point)
    {
        Vector3 c = transform.position;
        float dx = point.x - c.x, dz = point.z - c.z;
        float along = alongX ? dx : dz;
        float across = alongX ? dz : dx;
        float off = along - Mathf.Clamp(along, spanLo, spanHi);
        return off * off + across * across;
    }

    /// <summary>
    /// Registers every cell of the span in <see cref="WallGrid"/> as this gate. Idempotent.
    /// </summary>
    public void ClaimCells()
    {
        if (registered || WallGrid.Instance == null) return;
        if (!spanSet) SetSpan(WallGrid.Instance.WorldToGrid(transform.position), true, 0, 0);
        registered = true;
        for (int k = spanLo; k <= spanHi; k++)
            WallGrid.Instance.Register(CellAt(k), this);
    }

    void Start()
    {
        // Shape and position first, so the renderers the fog and the cutout collect are
        // the final ones (see WallConnector.EnsureInitialized).
        WallConnector connector = GetComponent<WallConnector>();
        if (connector != null) connector.EnsureInitialized();

        // A wall hides a unit's legs from the tilted RTS camera, so it opens a window
        // for any unit behind it like every other occluder (2026-09-08). The old fade
        // excluded walls because a line of ghosting cells read worse than the problem; a
        // cutout has no such cost.
        OccluderCutout.AttachTo(gameObject);
        // Another colony's gate is shown only on watched ground, like its huts (2026-09-28:
        // rival walls and gates drew through the fog).
        if (!Faction.IsPlayer) FogVisibility.Attach(gameObject, FogVisibility.Rule.Visible);

        // Setup Health component
        healthComponent = GetComponent<Health>();
        if (healthComponent == null)
        {
            healthComponent = gameObject.AddComponent<Health>();
        }
        ClaimCells();   // before the HP: Width needs the span
        maxHealth = CellHealth(isStoneGate) * Width;
        healthComponent.maxHealth = maxHealth;
        healthComponent.currentHealth = maxHealth;
        healthComponent.destroyOnDeath = true;
        healthComponent.destroyDelay = 0.5f;
        healthComponent.showHealthText = true;
        healthComponent.showObjectName = true;
        healthComponent.hideWhenFull = true;
        healthComponent.onDeath.AddListener(OnGateDestroyed);

        // Remove any NavMeshObstacle - gates should NOT block pathfinding
        // Adjacent walls carve 0.9x0.9, leaving a walkable gap at the gate
        NavMeshObstacle existingObstacle = GetComponent<NavMeshObstacle>();
        if (existingObstacle != null)
        {
            Destroy(existingObstacle);
        }

        // Setup trigger collider to catch enemies walking through
        SetupTriggerCollider();
    }

    void SetupTriggerCollider()
    {
        // Convert any existing colliders to triggers so units can pass through physically
        Collider[] existingColliders = GetComponentsInChildren<Collider>();
        foreach (Collider col in existingColliders)
        {
            col.isTrigger = true;
        }

        // Our enemy-detection trigger covers the whole span. It is also the gate's click
        // box (demolish, the building card) and the collider every approach point and
        // edge distance measures to.
        triggerCollider = gameObject.AddComponent<BoxCollider>();
        triggerCollider.isTrigger = true;
        triggerCollider.size = TriggerSize();
        triggerCollider.center = TriggerCentre();
    }

    Vector3 TriggerSize()
    {
        float length = (Width - 1) + triggerSize;
        return alongX ? new Vector3(length, 2f, triggerSize) : new Vector3(triggerSize, 2f, length);
    }

    Vector3 TriggerCentre()
    {
        float off = (spanLo + spanHi) * 0.5f;
        return alongX ? new Vector3(off, 1f, 0f) : new Vector3(0f, 1f, off);
    }

    void OnTriggerEnter(Collider other)
    {
        // Check if it's an enemy - force them to attack the gate
        Enemy enemy = other.GetComponent<Enemy>();
        if (enemy != null)
        {
            enemy.ForceAttackGate(this);
        }
    }

    /// <summary>
    /// Left-click opens the selected-building card (2026-09-28): the gate's width, health
    /// and its demolish refund. Same rules as a hut's: left button only, player-owned only.
    /// </summary>
    void OnMouseDown()
    {
        if (GameStartController.IntroInProgress) return;
        if (PauseController.BlockGameplayInput || PointerBlock.OverHud) return;
        if (!Faction.IsPlayer) return;
        SelectedBuildingHUD.Show(gameObject);
    }

    void OnGateDestroyed()
    {
        UnregisterFromGrid();

        // Immediately disable trigger + any residual colliders so enemies walking
        // over the dying gate during its 0.5s fade-out aren't re-forced onto it
        // by OnTriggerEnter, and can retarget freely. (Gates have no
        // NavMeshObstacle — removed in Start — so only colliders to clear.)
        Collider[] cols = GetComponentsInChildren<Collider>();
        for (int i = 0; i < cols.Length; i++) cols[i].enabled = false;

        // Notify all enemies that a gate was destroyed (breach detection)
        OnAnyGateDestroyed?.Invoke();
    }

    void OnDestroy()
    {
        ActiveRegistry<Gate>.Unregister(this);
        UnregisterFromGrid();
    }

    private void UnregisterFromGrid()
    {
        if (registered && WallGrid.Instance != null)
        {
            registered = false;
            for (int k = spanLo; k <= spanHi; k++)
                WallGrid.Instance.Unregister(CellAt(k), this);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 0.3f, 0.3f);
        Gizmos.DrawWireCube(transform.position + TriggerCentre(), TriggerSize());
    }
}
