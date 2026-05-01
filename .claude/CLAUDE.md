# Island RTS Game - Claude Working Memory

## Critical Rules

- **Always ask clarifying questions before implementing changes.** Polish > speed. Avoid introducing new bugs. Ask 2-4 targeted questions before writing code for gameplay, UX, or bugfix changes.
- All considerations in the Utility AI are **multiplicative** — a 0.3 from any single consideration kills the whole action. A yShift > 0 prevents early-out and can let momentum keep dead actions alive.
- Momentum bonus is **additive** after multiplication. Combined with the 20% commitment threshold, even small momentum can make an action impossible to exit. Test exit conditions.
- When adding new AI actions: verify the full transition table (enter conditions, exit conditions, what competing actions score at each state).

---

## Tech Stack

- **Engine:** Unity 6000.0.25f1
- **Language:** C# (.NET)
- **Render Pipeline:** Universal Render Pipeline (URP) 17.0.3
- **Pathfinding:** Unity AI Navigation 2.0.9 (NavMesh)
- **UI:** TextMeshPro + Unity UI (uGUI 2.0.0)
- **Input:** Unity Input System 1.11.2
- **IDE:** Visual Studio Code
- **VCS:** Git + GitHub

---

## Project Structure

```
V:/islandrtsgame/                    # Repository root
├── .claude/                         # Claude Code config + this file
├── .git/
├── README.md                        # Developer/contributor guide
├── AUDIO_SETUP.md                   # Audio system setup docs
├── VISUAL_EFFECTS_SETUP.md          # VFX setup docs
├── Utility_AI_Implementation_Plan.md
└── islandrts/                       # Unity project root
    ├── Assets/
    │   ├── Scripts/                 # All C# source
    │   │   ├── AI/                  # Utility AI system
    │   │   │   ├── Core/            # AIBrain, ActionOption, Consideration, ResponseCurve, AIBlackboard
    │   │   │   ├── WorldState/      # AIWorldState (enemy density grid, day progress)
    │   │   │   ├── Considerations/  # Scoring inputs (HealthPercent, ThreatNearby, etc.)
    │   │   │   ├── Executors/
    │   │   │   │   ├── Worker/      # Gather, Return, Flee, Idle
    │   │   │   │   ├── Warrior/     # Engage, Intercept, DefendWall, Patrol, Retreat, HealAtCampfire
    │   │   │   │   └── Enemy/       # EnemyAttack (single action, priority-based targeting)
    │   │   │   ├── Shared/          # AINavHelper (throttling), StuckResolver
    │   │   │   └── Debug/           # AIDebugOverlay (F3 in editor)
    │   │   └── *.cs                 # 40 root-level scripts (game systems)
    │   ├── Prefabs/                 # Worker, Warrior, Enemy, buildings, resources
    │   ├── Materials/
    │   ├── Audio/
    │   ├── Scenes/                  # SampleScene.unity (main scene)
    │   └── Settings/                # URP render pipeline configs
    ├── ProjectSettings/
    └── islandrts.sln                # VS solution
```

### Key Scripts (Root Level)

| Script | Purpose |
|--------|---------|
| `GameManager.cs` | Victory/defeat (survive 5 nights), statistics |
| `ResourceManager.cs` | Singleton: wood/food/stone pool |
| `PopulationManager.cs` | Worker housing capacity tracking |
| `DayNightCycle.cs` | 120s day / 60s night, lerps `LightingPreset` SOs, static events |
| `LightingPreset.cs` | ScriptableObject: sun + ambient gradient values for one moment of the cycle |
| `BaseBuilding.cs` | Campfire: worker/warrior spawning, housing |
| `Health.cs` | Universal HP, damage events, floating text display |
| `BuildPlacement.cs` | Ghost placement, wall line drawing, gate conversion, demolish mode |
| `WallGrid.cs` | O(1) dictionary for wall/gate grid lookups |
| `WallConnector.cs` | Procedural wall meshes (6 shapes + 6 gate variants) |
| `Worker.cs` | Worker unit + Utility AI brain setup |
| `Warrior.cs` | Warrior unit + Utility AI brain setup |
| `Enemy.cs` | Enemy unit + Utility AI brain setup |
| `ActiveRegistry<T>.cs` | Generic static registry for O(1) entity tracking |
| `CameraController.cs` | WASD pan, Q/E rotate, scroll zoom (orthographic) |
| `CameraShake.cs` | Combat shake, pure offset approach (no stored position) |
| `AudioManager.cs` | Singleton: music, SFX, ambient, crossfades |

---

## Architecture

### Utility AI System

All unit decision-making uses a scoring-based Utility AI (no state machines):

```
AIBrain (per unit)
  ├── ActionOption[] (e.g., Gather, Return, Flee, Idle for workers)
  │   ├── Consideration[] (scoring inputs, each 0-1)
  │   │   └── ResponseCurve (Linear, InverseLinear, Exponential, Logistic, Constant)
  │   └── ActionExecutor (OnEnter/OnUpdate/OnExit behavior)
  └── AIBlackboard (per-unit data cache, zero GC)
```

**Evaluation flow:**
1. Every 0.25-0.35s (randomized per unit), AIBrain scores all actions
2. Each action's score = basePriority × (consideration1 × consideration2 × ...) + momentumBonus (if current)
3. Early-out at 0.001 — any consideration near zero kills the action
4. Best action must beat current by 20% (commitment threshold) to switch
5. Max 5 evaluations per frame globally
6. `ForceReeval()` bypasses both timer and frame throttle

**Worker actions:** Gather, Return, Idle, Flee
**Warrior actions:** Engage, Intercept, DefendWall, Patrol, Retreat, Heal
**Enemy actions:** Attack (single action; targeting handled by imperative priority function inside the executor, not by competing ActionOptions — see Phase 6.22)

### Singleton Pattern
Used by: ResourceManager, AudioManager, WallGrid, AIWorldState, PopulationManager, GameManager, CombatEffects, CameraShake, BuildingDatabase

### ActiveRegistry Pattern
`ActiveRegistry<T>` provides O(1) static lists for: Worker, Warrior, Enemy, BaseBuilding, Hut, Wall, Gate, Watchtower, ResourceNode. Units register in Awake, unregister in OnDestroy.

### Performance Conventions
- Zero GC allocations in hot paths (Update, AI evaluation)
- NavMesh throttling: max 20 SetDestination/frame, 2 CalculatePath/frame (AINavHelper)
- Enemy density grid (AIWorldState): CELL_SIZE=10, 3x3 lookup ≈ 30x30 units
- Dirty checking on UI text (ResourceUI, FloatingText, Health)
- Audio clip preloading at startup (eliminates 184ms synchronous disk loads)
- Staggered AI timers prevent synchronized evaluation spikes
- Frame-staggered stuck detection (spread across 5 frames)

### Building System
- `BuildingData` ScriptableObjects define costs, prefabs, placement rules
- `BuildingDatabase` singleton maps `BuildingType` enum to data
- Wall placement: click-start + click-end line drawing (L-shaped or Bresenham staircase)
- Gate conversion: G key uses WallGrid lookup (not raycast) for reliable detection
- Demolish: Delete/X key, 50% resource refund, campfire protected
- `WallGrid` tracks walls/gates/construction sites in O(1) dictionary
- `WallConnector` generates procedural meshes based on 4-bit neighbor bitmask

### Day/Night & Combat
- Day: 120s, Night: 60s (configurable in DayNightCycle)
- Enemies spawn at night, scale with night number (3 + nightNum)
- Victory: survive 5 nights
- Static events: `DayNightCycle.OnNightStart`, `OnDayStart`
- `Health.onDeath` / `Health.onDamaged` UnityEvents drive combat responses

---

## Utility AI Gotchas

- `bb.nearestEnemy` must be populated by `EnemyPresence` consideration **before** other considerations read it — put it first in the array
- `ReturnUrgency` uses `max()` of 4 signals (not multiplicative) to combine carry/threat/night/efficiency
- `IsTargetAlive` must handle Unity destroyed-object null: re-fetch Health if null, don't return true
- Momentum bonus prevents flip-flopping but can also prevent action switches; always test exit conditions
- `ForceReeval()` on AIBlackboard's brain reference lets executors trigger instant re-evaluation
- Worker Flee: ThreatNearby yShift must be 0, otherwise momentum keeps dead flee action alive with 0 enemies
- Warrior Heal: must use zero momentum and a curve that scores exactly 0 at full HP, otherwise commitment threshold blocks Patrol from taking over
- CameraShake uses pure offset (undo-then-apply each LateUpdate) — never stores a "home" position, so it doesn't fight CameraController's WASD movement
- Enemy/Wall attack destinations use `Collider.ClosestPoint(enemyPos)`, not `target.position`. Center-point destinations land inside the NavMesh carve and the agent stops short of `attackRange` — looks like a freeze. Cache the collider on target assignment (`bb.currentTargetCollider`) and also use it for the distance-based attack-range check
- After using `ClosestPoint` on a carving obstacle's collider, **snap the result to the NavMesh via `NavMesh.SamplePosition`** before passing to `SetDestination`. ClosestPoint sits on the carve boundary — right after an adjacent obstacle uncarves (e.g. another hut dies), the NavMesh is briefly in recalc and `SetDestination` silently rejects boundary points. Symptom: enemy freezes ~3s until StuckResolver saves it. Fix lives in `EnemyAttackExecutor.GetApproachPoint`
- `AINavHelper.TrySetDestination` returns Unity's actual `NavMeshAgent.SetDestination` result — do NOT ignore the return. A false return means Unity rejected the destination; caller must retry next frame. Pretending success causes "ghost moving" state where `isStopped=false` but `pathPending=false` and velocity stays zero
- When using priority-ordered imperative target selection (as in `EnemyAttackExecutor.PickTarget`), test reachability against a `NavMesh.SamplePosition`-anchored point near the target, NOT the target's center. Carving obstacles make center-based `CalculatePath` always return `PathPartial`, causing every candidate to be rejected
- Re-roll `NavMeshAgent.avoidancePriority` whenever an enemy retargets from a dead target. Multiple enemies sharing a target will have similar ORCA priorities and mutually yield (stuck dance) on disperse otherwise. `Random.Range(30, 70)` per retarget breaks ties cleanly
- For enemies, replacing four competing ActionOptions (AttackWarrior/AttackBuilding/BreachWall/AttackCampfire) with ONE action + priority function eliminated a whole class of stutter bugs where sibling actions + momentum + commitment threshold fought on target death. Generalize this pattern when competing actions share most of their logic and differ mainly in "what target?"
- Singletons in single-scene games should NOT use `DontDestroyOnLoad` — it causes stale state (worker counts, audio cooldowns, etc.) to survive scene reloads on restart. Only add it back if you introduce a main menu scene
- `Worker.OnDestroy` must call `PopulationManager.Instance?.RemoveWorker()` — the PopulationManager doesn't auto-detect deaths

### Visual / Art Gotchas

Phase 10 spec is in `PHASE_10_VISUAL_OVERHAUL.md`. Stage 1 (post-processing + lighting presets) is shipped; remaining stages are planned.

- **Don't chase the menu mockup composition.** The Castaway Colony main menu image uses macro DOF and a low/cinematic camera; the gameplay camera will never frame the world that way. Match palette, water quality, and silhouette readability — not composition or DOF.
- **No DOF in gameplay.** Depth of field works against RTS clarity. Skip it in the Global Volume even though it's tempting from beauty shots.
- **Water must stay real-time.** Don't mark water as Static, don't include it in the lightmap bake. Vertex displacement (Gerstner/sine) is dynamic geometry by definition.
- **Per-triangle normals are the "low-poly water" look.** Without per-triangle (flat-shaded) normals you get smooth-shaded water that visually breaks the stylized aesthetic. Either recalc normals in the fragment shader or use a low-density mesh with hard edges.
- **NavMesh re-bake after mesh swaps if collider bounds change.** Phase 10 swaps meshes/materials on existing prefabs. If the new mesh's collider footprint differs, carving regions move and pathing breaks until the bake is refreshed.
- **Keep gameplay prefab GameObject hierarchy stable during art swaps.** `Health`, `AIBrain`, `NavMeshAgent`, `ActiveRegistry<T>` registrations, and event subscriptions all reference these GameObjects. Swap meshes/materials only — don't reparent, rename, or replace the root GameObject.
- **Test stylized lighting at the RTS camera angle, not in a beauty-shot view.** What looks great at a 30° tilt or scene-view fly-through can look flat or muddy from the actual gameplay camera height. Validate in Play mode at the real camera transform.
- **`LightingPreset` SO is the runtime source of truth for sun + ambient.** `DayNightCycle.cs` takes a `dayPreset` and `nightPreset` reference and lerps all values between them via `dayProgress`. The values you set in `Window > Rendering > Lighting > Environment` are now **Scene-view fallback only** — they're overridden the first frame `DayNightCycle.Start()` runs. Don't bypass the preset with one-off Lerps; extend the SO type instead.
- **`DayNightCycle.Start()` forces `RenderSettings.ambientMode = AmbientMode.Trilight`.** Whatever ambient mode you pick in the Lighting Settings window is overridden at runtime. Don't waste time tuning Skybox or Flat ambient — change `LightingPreset.cs` instead if you need a different blending shape.
- **Bloom should fire only on HDR-emissive hero assets.** Don't lower Bloom Threshold below 1.0 to "make the campfire glow" — that makes anything moderately bright bloom (UI text, bright resources, etc., all gain a halo). Instead give hero materials HDR Emission with intensity ~3 so they exceed threshold while normal scene surfaces stay below it. Threshold 1.0 + emissive hero = clean bloom.
- **Orthographic + flat ground = exponential fog whitewashes.** Orthographic projection over a flat playfield gives no depth gradient for `Mode: Exponential` fog to work against, so the fog reads as a uniform white veil over everything (close objects fogged the same as far ones). If fog returns in Stage 4, use **Linear** with Start ~30 / End ~80 so only far edges fade.

## Key Conventions

- `CachedHealth` property on all units avoids per-frame GetComponent
- Wall/Gate events: `OnAnyWallDestroyed` / `OnAnyGateDestroyed` static events
- WallGrid: `WorldToGrid()`, `HasWallAt()`, `GetWallAt()` (not GetAt)
- ResourceNode scoring: `distance + (claimCount * 5f)`
- Wall scoring: `dist * (1 + attackers * 0.5f)`, gates at `0.3x` distance
- Public sound methods: `StartGatheringSoundPublic()`, `PlayAttackSoundPublic()`, etc.
- `StuckResolver` is a shared component (Worker, Warrior, Enemy all use it)

---

## Logging Conventions

The console is kept intentionally quiet so real problems are visible. A full trim pass (Phase 6.22) cut logging from 212 calls → 65. **Before adding any `Debug.Log`, check it against the keep-list below. If it doesn't fit, don't add it.**

### What we log (the full keep-list)

**1. All `Debug.LogError`** — always OK. Missing prefabs, null refs, setup failures. Should never fire in a healthy build; when they do, they indicate a real bug.

**2. `Debug.LogWarning` for recoverable misconfigurations only:**
- `Hut.cs` — homeless workers after hut loss
- `ResourceSpawner.cs` — campfire not found, prefab missing, spawn failures
- `ResourceManager.cs` — duplicate singleton / instance being destroyed
- `BaseBuilding.cs` — no Renderers for hover, NavMesh spawn fallback, no UI assigned
- `DayNightCycle.cs` — no directional light found
- `AudioManager.cs` — combat music missing / null clip
- `HealthBar.cs` — no Health component
- `GameManager.cs` — no defeat/victory screen assigned
- `BuildPlacement.cs` — cannot select building (not in placement mode)
- `BuildingSelectionUI.cs` — null BuildingData

**3. `Debug.Log` for key lifecycle events ONLY:**
- `DayNightCycle` — "Night N begins" / "Day N begins" (the day/night transition pair at lines 144/157 only — not the Start() banner)
- `EnemySpawner` — "Spawning N enemies for night N..." (wave summary, one line per night — not per-enemy)
- `GameManager` — init line, "Survived night N" progress, VICTORY banner (3 lines), DEFEAT banner (3 lines)
- `BaseBuilding` — CAMPFIRE DESTROYED banner (3 lines)
- `ResourceManager` — init line with starting amounts (one line, line 36)

### What we do NOT log (the never-list)

If you catch yourself adding any of these, delete instead:
- **Per-unit spawn/init/defeated** (Enemy, Warrior, Worker, Hut, Gate, Watchtower spawning or dying)
- **Per-damage / per-heal / per-death** on `Health` (thousands of these per combat)
- **Per-resource tick** on `ResourceManager` (+Wood, -Wood, Not enough, Spent) — UI shows this
- **Per-button-click** confirmations (`WorkerAssignmentUI`, `VictoryDefeatUI`, etc.)
- **Audio chatter** (clip started, crossfade, queued, stopped)
- **Per-placement events** (L-path mode toggled, wall line started, selected building, rotated, placed N walls) — too frequent in build mode
- **Init banners** for helper components (BuildingDatabase, CombatEffects, HealthBar created, NavMesh obstacle setup)
- **PopulationManager** per-worker / per-housing add/remove
- **NavMeshAgent configured** dumps on unit spawn

### Rule of thumb

Ask: "would this log spam the console during a normal 5-minute play session?" If yes, it's noise — use the AIDebugOverlay (F3) for live state inspection, not `Debug.Log`. Reserve logs for rare events (once per night, once per game, or one-off failures).

---

## How to Run

1. Open `islandrts/` folder in Unity Hub (Unity 6000.0.25f1)
2. Open scene: `Assets/Scenes/SampleScene.unity`
3. Press Play
4. Click campfire to assign workers, press B to build, recruit warriors before nightfall

## Controls

| Key | Action |
|-----|--------|
| WASD / Arrows | Pan camera |
| Q / E | Rotate camera |
| Mouse Wheel | Zoom |
| B | Enter build mode |
| 1-4 | Select building type (Hut, Wood Wall, Stone Wall, Watchtower) |
| G | Convert wall to gate (in build mode, hover over wall) |
| R | Toggle L-path direction (wall mode) / Rotate building |
| Shift | Bresenham staircase wall path |
| Delete / X | Demolish mode (50% refund) |
| F3 | AI debug overlay (editor only) |
| Click campfire | Worker assignment UI |

---

## Current State (Phase 6.22)

**What's built and working:**
- Full resource economy (wood, food, stone) with autonomous worker AI
- Building system: Hut, Wooden/Stone Wall, Gate, Watchtower, Campfire
- Utility AI for all units (Workers, Warriors, Enemies) — no state machines
- Day/night cycle with escalating enemy raids
- Victory (survive 5 nights) / defeat (campfire destroyed)
- 3D spatial audio, combat VFX, screen shake
- Wall system with procedural meshes and smart connections
- Gate conversion (G key) and building demolish (Delete key)
- Warrior healing at campfire between waves
- Worker flee from enemies (day or night)
- Zero GC in hot paths, staggered AI evaluation

**What's next (future phases):**
- Phase 7: Building upgrades (campfire -> fortress), workshop, storage
- Phase 8: Worker night hide behavior, archer units
- Phase 9: Player character (Admiral), crafting, tech tree
- Phase 10: Visual overhaul — Stage 1 (post-processing + lighting presets) shipped; Stages 2-5 (asset swap, water shader, lighting bake) ahead
- Phase 11: Content polish, save/load, main menu

---

## Balancing Reference

| Unit | HP | Damage | Attack Speed | DPS |
|------|----|--------|-------------|-----|
| Warrior | 75 | 15 | 1.2s | 12.5 |
| Enemy | 50 | 10 | 1.5s | 6.67 |

| Building | Cost | HP |
|----------|------|----|
| Hut | 20W 10F | 100 |
| Wooden Wall | 15W 5S | 150 |
| Stone Wall | 10W 20S | 300 |
| Watchtower | 25W 15S | 200 |
| Warrior | 10W 15F | 75 |

- Starting resources: 100W, 50F, 0S
- Worker carry: 5 resources, 1/sec gather rate
- Enemies per night: 3 + nightNumber
- Warrior heals at campfire: 5 HP/sec (between waves only)
- Gate HP: half of corresponding wall (wooden=75, stone=150)

---

## Phase History

### Phase 1-5.5 (Foundation)
Core systems, building placement, resource gathering, worker AI state machines, day/night cycle, enemy spawning, health system, warrior recruitment, victory/defeat, combat VFX, 3D spatial audio.

### Phase 6.1-6.4 (Defensive Expansion)
Walls, gates, watchtowers, housing/population system, wall grid with O(1) lookups, procedural wall meshes, AI bug fixes, pathfinding improvements, static entity registries.

### Phase 6.5-6.6 (Performance)
GC allocation elimination, zero per-frame heap allocations, AudioManager pooled cooldowns, Camera.main caching, CachedHealth properties, dirty checking UI.

### Phase 6.7-6.10 (Polish)
Simplified gate system (trigger-based), worker refactor (commit-based resource selection, claim system), staggered updates, audio preloading, simplified stuck detection.

### Phase 6.11-6.14 (Utility AI)
Complete Utility AI system replacing all state machines. Scoring-based decisions for Workers, Warriors, Enemies. ReturnUrgency compound scoring, delivery fixes, momentum/hysteresis tuning.

### Phase 6.15-6.16 (AI Cleanup)
AI momentum fixes, stuck state fixes, old state machine removal, shared utility extraction (StuckResolver, AINavHelper, FloatingText, ActiveRegistry).

### Phase 6.17 (AI Tuning)
Building HP text hidden at full, warrior exterior patrol, night gathering balance, enemy periodic retargeting (2s rescan), proactive warrior detection, desynchronized AI evaluation.

### Phase 6.18 (Worker Flee Overhaul)
Workers flee away from enemies (not toward huts). Removed TimeOfDay gate — enemies dangerous day or night. Gather hard-suppressed by threat. Dynamic flee direction recalculated every 0.5s.

### Phase 6.19 (QoL)
Grid-based gate conversion (WallGrid lookup replaces raycast). Building demolish system (Delete/X, 50% refund). Camera shake stutter fix (pure offset, no stored position).

### Phase 6.20 (Warrior Heal)
Warriors heal at campfire between waves (5 HP/sec). Fixed Retreat stuck state (ThreatNearby yShift 0, reduced momentum). Heal uses InverseLinear(2,0) with zero momentum for clean full-HP exit.

### Phase 6.21 (Enemy Attack + Restart Fix)
Enemies now target nearest point on building collider (`Collider.ClosestPoint`) instead of center. Fixes enemies piling on one side of huts, freezing outside NavMesh carve, and not tripping attack-range check. `AttackTargetExecutor` + `BreachWallExecutor` both use new `GetApproachPoint`/`GetEdgeDistance` helpers. Enemy `stoppingDistance` reduced to 0.5 (was `attackRange - 1 = 3`) since edge-distance check handles stop logic. Enemy `attackRange` bumped 3.5 → 4. `StuckResolver.stuckCheckInterval` tightened 3s → 1.5s (triggers in ~3s now).

Removed `DontDestroyOnLoad` from PopulationManager, ResourceManager, AudioManager, BuildingDatabase, CombatEffects, AIDebugOverlay — prevents stale state across scene reloads. Added `PopulationManager.RemoveWorker()` call in `Worker.OnDestroy` so dead workers free housing slots (was a bug *within* a playthrough too).

Cached `currentTargetCollider` on `AIBlackboard` for ClosestPoint without per-frame GetComponent.

### Phase 6.22 (Enemy Targeting Refactor)
Replaced the 4-way enemy ActionOption competition (AttackWarrior / AttackBuilding / BreachWall / AttackCampfire) with a **single `EnemyAttack` action** whose executor owns target selection via an imperative priority function. Eliminated a class of stutter bugs where sibling actions + momentum + 20% commitment threshold fought on every target death.

**Target priority (`EnemyAttackExecutor.PickTarget`):**
1. Gate-trigger override (`bb.forcedTarget` with 1.5s expiry — set by `Enemy.ForceAttackGate`)
2. Nearest live warrior in `warriorDetectionRange`
3. Campfire proximity commit (within 5m of campfire → finish the job)
4. Nearest reachable hut/watchtower (no range filter — huts are always preferred over distant campfire)
5. Nearest wall/gate (gates at 0.3× distance preference)
6. Campfire fallback (no huts alive)

**Retarget triggers — only two:** 1s timer in executor; `bb.currentTarget` died (detected each `OnUpdate` via `IsTargetAlive`).

**Three critical bugs found + fixed during the refactor:**

1. **Reachability test rejected every hut.** `CalculatePath` to `hut.position` or collider `ClosestPoint` returns `PathPartial`/`PathInvalid` because huts have `NavMeshObstacle.carving=true` (center sits in carved hole; ClosestPoint sits on hole boundary). Fix: path-test to a point from `NavMesh.SamplePosition(hut.position, 3f)` — guaranteed walkable.

2. **`AINavHelper.TrySetDestination` ignored Unity's return value.** Always returned `true` even when Unity's `NavMeshAgent.SetDestination` returned `false` (destination unmappable during NavMesh recalc). Caller set `isStopped=false` without a path being queued → "ghost moving" state, enemy frozen until StuckResolver rescued it ~3s later. Fix: propagate actual return.

3. **`GetApproachPoint` returned raw `ClosestPoint` on collider**. Same hole-boundary issue — when *adjacent* huts uncarved, boundary points were briefly rejected. Fix: snap `ClosestPoint` output through `NavMesh.SamplePosition(raw, 2f)` before passing to `SetDestination`.

Also: re-roll `agent.avoidancePriority = Random.Range(30, 70)` whenever an enemy retargets from a dead target, so enemies sharing a dying hut don't mutually yield (ORCA stuck-dance) on disperse.

**Deleted files:** `AttackTargetExecutor.cs`, `BreachWallExecutor.cs`, `CampfireProximity.cs`, `EnemyHasTarget.cs`, `PathBlocked.cs` (all enemy-specific and now unused).

**Removed from `AIBlackboard`:** `unreachableTargets` dict (blacklist no longer needed — reachability tested at pick-time), `isAttackingWall` flag (no longer has meaning with single action), `buildingEngagementRange` (dropped — huts always preferred). Added `forcedTarget` / `forcedTargetExpiry` for gate-trigger override.

**Removed from `Enemy.cs`:** `OnAnyWallDestroyed` / `OnAnyGateDestroyed` / `OnAnyHutDestroyed` event subscriptions (retarget on current-target death handles all cases naturally). `onDamaged` ForceReeval also removed (priority function already picks warriors first when in range; damage-retargeting caused ping-pong between attackers).

Net effect: enemies now chew through huts en route to the campfire instead of jogging past them, retarget cleanly the same frame a hut dies, and disperse immediately from shared targets with no 2-4s freeze.

### Phase 10 (In Progress): Visual Overhaul

**Status:** Stage 1 shipped (post-processing + lighting presets). Stages 2-5 planned. Full spec in `PHASE_10_VISUAL_OVERHAUL.md` at repo root — that file is the source of truth; this entry is a summary.

**Goal:** Replace primitive geometry and default lighting with a cohesive stylized low-poly aesthetic in the Bad North / Townscaper / Islanders / Synty POLYGON Pirates family. Visual reference is the Castaway Colony main menu mockup (sunset palette, low-poly islands, stylized water, soft DOF on background) — but tuned for top-down RTS framing: no DOF, no macro lensing, readable silhouettes from gameplay camera height.

**Tech foundation already in place:** Unity 6000.0.25f1 + URP 17.0.3 (Volume system, Shader Graph), `DayNightCycle.cs` ready to drive sun rotation, sun color, ambient gradient, fog, and water shader properties between day/night `LightingPreset` ScriptableObjects.

**Five stages:**

1. **Post-Processing Pass (~1 evening, can be done anytime).** Global Volume in `SampleScene` with Bloom (~0.5 / threshold 1.0), Color Adjustments (post-exposure +0.2, contrast +10, slight saturation), White Balance (temperature +15), ACES tonemapping, Vignette (0.25 / 0.4). **No DOF** — works against RTS clarity. Lower sun angle (15–25° elevation), warm gold sun color, gradient ambient (warm sky / neutral equator / cool ground) for the warm-light-cool-shadow split. `DayNightCycle` lerps between day/night `LightingPreset` SOs.

2. **Asset Replacement (Hybrid Strategy).** Bought pack for filler/environment (Synty POLYGON Pirates / POLYGON Tropical $30–60, or free Quaternius CC0 / KayKit alternatives) — palms, rocks, bushes, grass, props. Custom-modeled in Blender for hero/identity assets — campfire, Worker, Warrior, Enemy, Hut, Wooden/Stone Wall, Gate, Watchtower, shipwreck. Imphenzia low-poly modeling series as reference. Workflow: create `Assets/Art/` tree, replace one prefab category at a time, swap meshes/materials only (never touch `Health`, `AIBrain`, `NavMeshAgent`, registry components), re-validate NavMesh after each category swap.

3. **Stylized Water Shader (Shader Graph).** Build as a side project during Phase 7–8 downtime to de-risk the hardest visual piece. Components: vertex displacement (Gerstner waves or stacked sine, amplitude 0.05–0.15 — calm tropical, not open ocean); depth-based color blend (camera depth texture, shallow turquoise → deep blue); shoreline foam (thin band where depth difference is small, modulated by scrolling noise + threshold); quantized sun specular (Blinn-Phong with smoothstep for hard-edge stylized highlight); per-triangle normal recalc for flat-shaded "low-poly water" look. References: Daniel Ilett URP stylized water tutorial, NedMakesGames Shader Graph series, Stylized Water 2 (Asset Store) as reference. **Time budget: 1 weekend rough + 1 weekend polish — do not exceed.**

4. **Lighting Bake & Final Polish.** Mark static geometry (terrain, rocks, trees, buildings) as Static; Mixed lighting mode; bake lightmaps for soft indirect bounce. Dynamic units and wave-displaced water stay real-time. Add URP exponential distance fog for atmospheric depth. Tune shadow distance / cascades for RTS camera range. Test SSAO — keep only if it helps stylized reads rather than muddying them.

5. **Recommended Sequencing.** Stage 1 anytime (free morale win). Stage 3 water shader during Phase 7–8 downtime. Pre–Phase 10: buy Synty pack, build single test scene to validate look. Phase 10 proper: Stage 2 asset replacement (categorical), Stage 4 lighting bake, polish.

**Success criteria:** game looks visually cohesive at gameplay camera angle (top-down RTS); water has depth-blended color, foam, and sun specular at minimum; day/night lerps sun + sky + fog + water shader smoothly; ≥60 fps with bake + post-processing active; hero assets bespoke, environment bought-pack; a new player describes the aesthetic in the same family as Bad North / Townscaper / Islanders.

**Anti-patterns to avoid** are captured in the "Visual / Art Gotchas" subsection above — most critical: don't chase the menu mockup's DOF/composition (only its palette, water, and silhouettes), don't bake the water, and keep gameplay prefab hierarchies stable during mesh/material swaps.

### Phase 10 Stage 1 (Complete): Post-Processing + Lighting Foundation

URP Global Volume on `SampleScene` with five overrides — Bloom, Color Adjustments, White Balance, ACES Tonemapping, Vignette. Warm directional light (rotation X≈20°, gold color, intensity 1.5). Ambient gradient (warm sky / neutral equator / cool ground).

**New file:** `Assets/Scripts/LightingPreset.cs` — `[CreateAssetMenu]` ScriptableObject holding sun color + intensity and ambient sky/equator/ground colors + intensity for one moment in the day/night cycle.

**`DayNightCycle.cs` refactor:** removed the four old inspector fields (`dayColor`, `nightColor`, `dayIntensity`, `nightIntensity`) — replaced by two `LightingPreset` references (`dayPreset`, `nightPreset`). `UpdateSunLighting()` lerps sun color, sun intensity, and `RenderSettings.ambientSkyColor` / `ambientEquatorColor` / `ambientGroundColor` / `ambientIntensity` between presets via the existing `dayProgress` curve. `Start()` forces `RenderSettings.ambientMode = AmbientMode.Trilight` so the gradient values are what `RenderSettings` actually consumes; `Debug.LogError` if either preset reference is missing.

Two SO assets created in scene: `DayPreset.asset` (warm gold sun + warm-yellow / neutral / cool-blue ambient) and `NightPreset.asset` (cool blue sun + dark-navy / dark-blue / near-black ambient). User tunes values in the Project window; scene Inspector references them on `DayNightCycle`.

Campfire material now has HDR Emission (intensity ~3) so it blooms at Bloom Threshold = 1.0 — preferred over dropping threshold globally, since only true HDR-emissive hero assets should bloom.

Fog disabled. Orthographic + flat ground meant `Mode: Exponential` was reading as a uniform white veil (no depth gradient to work against). Revisit in Stage 4 with **Linear** fog (Start ~30 / End ~80) tuned to far edges only.

**Outstanding (intentionally deferred):**
- Post-FX values currently conservative (Sat 2, Contrast 6, Temp 10, Vignette 0.12) vs spec values (5 / 10 / 15 / 0.25). Stylistic choice — can push closer to spec for more drama later.
- Fog hooks not in `LightingPreset` SO yet — wait until Stage 4 so we know what shape they need.
- Water shader properties not in `LightingPreset` yet — Stage 3 dependency.
- Sun rotation (`sunriseAngle` / `sunsetAngle`) intentionally NOT in the preset; rotation is a continuous time-of-day mechanic, the preset is for discrete day-state vs night-state values.
