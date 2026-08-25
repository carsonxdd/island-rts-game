# Island RTS Game - Claude Working Memory

## Critical Rules

- **Always ask clarifying questions before implementing changes.** Polish > speed. Avoid introducing new bugs. Ask 2-4 targeted questions before writing code for gameplay, UX, or bugfix changes.
- All considerations in the Utility AI are **multiplicative** — a 0.3 from any single consideration kills the whole action. A yShift > 0 prevents early-out and can let momentum keep dead actions alive.
- Momentum bonus is **additive** after multiplication. Combined with the 20% commitment threshold, even small momentum can make an action impossible to exit. Test exit conditions.
- When adding new AI actions: verify the full transition table (enter conditions, exit conditions, what competing actions score at each state).

---

## Tech Stack

- **Engine:** Unity 6000.5.9f1
- **Language:** C# (.NET)
- **Render Pipeline:** Universal Render Pipeline (URP) 17.5.0
- **Pathfinding:** Unity AI Navigation 2.0.14 (NavMesh)
- **UI:** TextMeshPro + Unity UI (uGUI 2.5.0)
- **Input:** Unity Input System 1.20.0
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
    │   │   └── *.cs                 # 38 root-level scripts (game systems)
    │   ├── Prefabs/                 # Worker, Warrior, Enemy, buildings, resources
    │   ├── Materials/
    │   ├── Audio/
    │   ├── MainIsland.unity         # MAIN GAME SCENE (274 objects)
    │   ├── Scenes/                  # SampleScene.unity — stock Unity scene, unused
    │   ├── Art/                     # Phase 10 low-poly art library + showcase scene
    │   ├── Editor/LowPoly/          # Procedural low-poly asset generator (editor-only)
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
`ActiveRegistry<T>` provides O(1) static lists for: Worker, Warrior, Enemy, BaseBuilding, Hut, Wall, Gate, Watchtower, ResourceNode, ConstructionSite. Units register in Awake, unregister in OnDestroy. `FindObjectsByType` is banned in this codebase — every scan site was converted to registries; use `X.ActiveList` with an index loop.

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
- ANY destination on a carving obstacle (campfire, huts, towers) must use the ClosestPoint→SamplePosition→edge-distance pattern — not just enemy attacks. HealAtCampfireExecutor originally targeted/measured the campfire CENTER: agents stop at the carve boundary, center-distance never drops below the arrival threshold, unit sticks on "moving" forever
- CameraShake uses pure offset (undo-then-apply each LateUpdate) — never stores a "home" position, so it doesn't fight CameraController's WASD movement
- Enemy/Wall attack destinations use `Collider.ClosestPoint(enemyPos)`, not `target.position`. Center-point destinations land inside the NavMesh carve and the agent stops short of `attackRange` — looks like a freeze. Cache the collider on target assignment (`bb.currentTargetCollider`) and also use it for the distance-based attack-range check
- After using `ClosestPoint` on a carving obstacle's collider, **snap the result to the NavMesh via `NavMesh.SamplePosition`** before passing to `SetDestination`. ClosestPoint sits on the carve boundary — right after an adjacent obstacle uncarves (e.g. another hut dies), the NavMesh is briefly in recalc and `SetDestination` silently rejects boundary points. Symptom: enemy freezes ~3s until StuckResolver saves it. Fix lives in `EnemyAttackExecutor.GetApproachPoint`
- `AINavHelper.TrySetDestination` returns Unity's actual `NavMeshAgent.SetDestination` result — do NOT ignore the return. A false return means Unity rejected the destination; caller must retry next frame. Pretending success causes "ghost moving" state where `isStopped=false` but `pathPending=false` and velocity stays zero
- When using priority-ordered imperative target selection (as in `EnemyAttackExecutor.PickTarget`), test reachability against a `NavMesh.SamplePosition`-anchored point near the target, NOT the target's center. Carving obstacles make center-based `CalculatePath` always return `PathPartial`, causing every candidate to be rejected
- Re-roll `NavMeshAgent.avoidancePriority` whenever an enemy retargets from a dead target. Multiple enemies sharing a target will have similar ORCA priorities and mutually yield (stuck dance) on disperse otherwise. `Random.Range(30, 70)` per retarget breaks ties cleanly
- For enemies, replacing four competing ActionOptions (AttackWarrior/AttackBuilding/BreachWall/AttackCampfire) with ONE action + priority function eliminated a whole class of stutter bugs where sibling actions + momentum + commitment threshold fought on target death. Generalize this pattern when competing actions share most of their logic and differ mainly in "what target?"
- Singletons in single-scene games should NOT use `DontDestroyOnLoad` — it causes stale state (worker counts, audio cooldowns, etc.) to survive scene reloads on restart. Only add it back if you introduce a main menu scene
- Worker bookkeeping has ONE owner: `Worker.OnDestroy` → `BaseBuilding.NotifyWorkerRemoved(this)`, which removes from `activeWorkers` (roster membership is the idempotence guard), decrements the wood/food/stone counter, and calls `PopulationManager.RemoveWorker()`. Never decrement population or counters anywhere else — `UnassignWorker` used to do its own bookkeeping *and* Destroy, double-decrementing the population
- Housing capacity has ONE owner: buildings call `AddHousing` in Start and release via `Hut.ReleaseHousing()` (flag-guarded, called from both death and `OnDestroy` so demolish counts too). PopulationManager must NOT rescan the scene at Start — the old `RecalculateHousingCapacity` double-counted depending on Start order

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
- **Generating art ≠ plumbing art.** `LowPolyAssetGenerator` writes a self-contained library into `Assets/Art/` and touches nothing else, by design. `LowPolyPlumber` (`Tools > Island RTS > Low-Poly Templates > Plumb …`) is the other half that mounts it onto the gameplay prefabs. Add every new category to the plumber's table — an art asset that isn't in that table is invisible in-game no matter how good it looks in the showcase scene.
- **Art is base-pivot at scale 1; the old gameplay prefabs were center-pivot primitives squashed by non-uniform root scale.** Never assign an art mesh to an existing root MeshFilter — the root scale distorts it and the pivot sinks/floats it. Mount the art on a `Model` child and reset the root to scale 1 instead.
- **Resetting a unit root's scale un-squashes every child.** `HealthBar` (bar container is a child at `up * heightOffset`, scaled `barWidth`/`barHeight`) and `FloatingText` (TMP child, and its `LookAt` billboard **skews** under non-uniform parent scale) were both silently riding the root's 0.4–0.7 squash. When the root goes to scale 1, retune `heightOffset` / `barWidth` / `barHeight` and the `CreateStateText` font size or the UI jumps ~2x in size.
- **`NavMeshAgent.baseOffset` is pivot-dependent.** Units were at `baseOffset: 1` with center-pivot capsules, so feet floated 0.3–0.4 above the NavMesh. Base-pivot art wants `baseOffset: 0`. Agent `radius`/`height` are world-space and *not* scaled by the transform — leave them alone during art swaps; they're NavMesh-bake concerns.
- **Walls cannot be plumbed by prefab swap.** `WallConnector` generates 6 shapes + 6 gate variants procedurally at runtime and writes them onto the root MeshFilter (`WallConnector.cs:108/139`), so any mesh you assign is overwritten. Walls can only be re-materialed until someone extends the generator to emit all 12 variants and teaches WallConnector to select among them.
- **`renderer.material` is slot 0 only — multi-submesh art broke every tint site in the codebase.** The old primitives had one material per renderer, so `.material.color` worked by accident. Art meshes carry 4–8 submeshes, so slot-0 tinting leaves most of the object untinted. All five sites now go through `RendererTint` (`Collect` once in Start → instanced `Material[]` across every slot and renderer; `SetColor`/`RestoreColors` after): `BaseBuilding` hover + campfire-death darken, `ResourceNode` hover, `BuildPlacement` ghost validity, `FadeOutEffect` death fade. **Never read `.materials` in Update** — it allocates a fresh array every call; collect once and cache.
- **`CombatEffects.FadeOutUnit` needs `GetComponentInChildren`,** since art lives on a `Model` child and the unit root has no Renderer of its own.
- **Ghost prefabs take the art MESH on their root renderer, not a `Model` child.** That keeps `BuildPlacement`'s `currentGhost.GetComponent<Renderer>()` valid and lets every submesh slot be filled with the translucent `Mat_Ghostbuilding` — a nested art prefab would drag its opaque LP materials in and the ghost would render solid. Wall ghosts are exempt: `WallLinePlacer` builds those procedurally, so `WoodenWallGhost`/`StoneWallGhost` prefabs are never instantiated.
- **`ResourceNode.SetupNavMeshObstacle()` overwrites shape/radius/height at runtime**, so serialized `NavMeshObstacle` values on Tree/RockNode/BerryBush prefabs are dead data — editing them does nothing. But obstacle radius/height *are* scaled by the transform, so changing a resource prefab's root scale silently resizes its avoidance volume. Taking `Tree` from 0.5 → 1 moved its effective obstacle from r0.4/h1.0 to the intended r0.8/h2.0.
- **`MainIsland` overrides the campfire's `NavMeshObstacle` extents per-instance** (0.6/0.5/0.6). Prefab-level carve edits never reach the scene's campfire — change it on the scene instance or not at all.

## Key Conventions

- `CachedHealth` property on all units avoids per-frame GetComponent
- Wall/Gate events: `OnAnyWallDestroyed` / `OnAnyGateDestroyed` static events
- WallGrid: `WorldToGrid()`, `HasWallAt()`, `GetWallAt()` (not GetAt)
- ResourceNode scoring: `distance + (claimCount * 5f)`
- Wall scoring: `dist * (1 + attackers * 0.5f)`, gates at `0.3x` distance
- Public sound methods: `StartGatheringSoundPublic()`, `PlayAttackSoundPublic()`, etc.
- `StuckResolver` is a shared component (Worker, Warrior, Enemy all use it)
- Singleton/unique-object lookup is **`FindAnyObjectByType<T>()`**. `FindFirstObjectByType<T>()` is obsolete as of Unity 6000.5 (it relies on instance-ID ordering) and `FindObjectOfType` / `FindObjectsOfType` were obsoleted before that. `FindObjectsByType` for multi-object scans remains banned outright — use `X.ActiveList` (see ActiveRegistry Pattern)

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

1. Open `islandrts/` folder in Unity Hub (Unity 6000.5.9f1)
2. Open scene: `Assets/MainIsland.unity` (**not** `Assets/Scenes/SampleScene.unity` — that is the leftover stock Unity scene, 3 objects, and is not the game)
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

## Current State (Unity 6000.5.9f1 upgrade done — Phase 6.24 still pending playtest)

> ⚠️ Phase 6.24 (four queued refactors) is implemented, compiles clean, and is now **committed** (checkpoint `ae8f632`) — but still **not playtested**. See the Phase 6.24 entry in Phase History for the playtest checklist. Everything below was true as of Phase 6.23 and is unaffected by the refactors (behavior-preserving).
>
> ⚠️ The project was upgraded from Unity 6000.0.25f1 to **6000.5.9f1** (see the Unity 6000.5.9f1 Upgrade entry in Phase History). Scripts compile with zero errors/warnings and the scene imports clean, but **nothing has been run in Play mode on the new version yet**. The Phase 6.24 playtest now doubles as the upgrade playtest.

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

**What's next (immediate):**
- Playtest Phase 6.24 refactors + the Unity 6000.5.9f1 upgrade (see checklist in Phase History).
- Run `Tools > Island RTS > Low-Poly Templates > Generate All Assets` (regenerates the Stage 2c simplified shapes), then `Plumb Everything` + `Scatter Environment Props`, re-bake the NavMesh, then playtest Phase 10 Stage 2a/2b/2c.

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

### Phase 6.23 (Code Health Pass)

Full-codebase review (4 parallel read agents) followed by three fix batches. Compiles with zero errors and zero warnings.

**Bookkeeping bug fixes (single-owner pattern):**
1. `UnassignWorker` double-decremented population (it called `RemoveWorker()` AND destroyed the worker, whose `OnDestroy` decremented again).
2. Workers killed by enemies never left `BaseBuilding.activeWorkers` or their wood/food/stone counter.
3. Housing capacity was Start-order dependent (`PopulationManager.Start` re-summed buildings while buildings also `AddHousing` in their Start).
4. Demolishing a hut leaked its housing (demolish calls `Destroy()` directly, skipping the Health death event where `RemoveHousing` lived).

Fixes: new `BaseBuilding.NotifyWorkerRemoved(Worker)` (roster membership = idempotence guard) called from `Worker.OnDestroy`; `UnassignWorker` just destroys; `RecalculateHousingCapacity` deleted; `Hut.ReleaseHousing()` flag-guarded, called from both death and `OnDestroy`.

**Warrior heal stuck fix:** `HealAtCampfireExecutor` targeted and measured the campfire CENTER — inside its 2x2 NavMesh carve, so agents stalled at the boundary outside `HealRange` on "Moving to Campfire" forever. Now uses `ClosestPoint` → `SamplePosition` → edge-distance (same pattern as `EnemyAttackExecutor`) and routes through `AINavHelper.TrySetDestination` respecting its return.

**Performance:**
- `FindObjectsByType` eliminated codebase-wide (was 4 scans/frame in `BuildPlacement.IsTooCloseToExistingBuilding` while placing, 5 per spawn candidate in `ResourceSpawner`, more in zone visuals). `ConstructionSite` gained an `ActiveRegistry`.
- `EnemyPresence` scan frame-cached on the blackboard (`enemyScanFrame`) — warriors ran the full enemy-list scan ~4x per brain tick (Engage/Intercept/Patrol/Heal); now once. Loop uses `sqrMagnitude`. Range cutoff semantics preserved (strict `<`).
- `WorkerAssignmentUI` + `VictoryDefeatUI` now dirty-check (were rebuilding strings every frame; victory/defeat screens build once on show).
- `BuildingDatabase.GetBuildingData` is a dictionary (was linear scan on per-frame paths). `Enemy.CachedAgent` added; `AIWorldState` uses it instead of per-enemy `GetComponent` every second. Damage numbers cache `Camera.main`. Merged no-build-zone grid uses `Vector2Int`/tuple keys (was `HashSet<string>` with `$"{x},{z}"` + `Split`/`Parse`).

**Dead-code sweep** (~20 members, all grep-verified callerless): AudioManager legacy gather one-shots + volume setters + `StopAmbientSounds`/`FadeOutAmbient`; GameManager resources-gathered stat path + empty stats stubs; `CameraShake.ShakeHeavy`/`ResetPosition` + `heavyShakeIntensity`; write-only blackboard fields (`nearbyEnemyCount`, `bestResourceScore`, `wallUnderAttackDistance`); dead `destinationSet` flags (Intercept/Retreat); `Gate.isGate`; BuildPlacement legacy inspector fields + demolish name tracking; `EnemySpawner.GetActiveEnemyCount`; `ResourceUI.ForceUpdate`; `BuildingSelectionUI.Toggle`; `PopulationManager.showDebugLogs`. Deleted `WorkerSpawnTest.cs` + `NavMeshTest.cs` (unreferenced in scenes). `DayNightCycle.OnGUI` wrapped in `#if UNITY_EDITOR`. `WallGrid` obsolete `FindObjectOfType` → `FindFirstObjectByType`.

**Notable discovery:** the multi-worker gather speed bonus was never implemented — `ResourceNode.gatherRatePerWorker`/`speedMultiplier` fed a discarded local. Deleted (default was "no bonus", so no behavior change). If stacking-worker bonuses are wanted, design deliberately as a balance feature.

**Kept deliberately:** `DistanceTo`'s unused enum branches (coherent parameterized utility); redundant `currentHealth` inits in unit Starts (removing creates Start-order dependency on `Health.Start`); worker executors still bypass `AINavHelper` (behavior-touching — needs its own pass with playtesting).

**Refactors still queued:** all four cleared in Phase 6.24 below (BuildPlacement split, UnitBase extraction, audio crossfade dedup, worker executors through AINavHelper).

### Phase 6.24 (Queued Refactors — ⚠️ PENDING PLAYTEST, NOT COMMITTED)

**Status:** Implemented and compile-verified (0 errors / 0 warnings via Unity 6000.0.25f1's bundled Roslyn), but **not yet playtested and not yet committed** — user needed to restart before testing. Next session: playtest, then commit. Net diff at time of writing: 279 insertions, 1752 deletions across 8 modified files + 5 new files.

Cleared all four "Refactors still queued" from Phase 6.23. No scene/prefab edits were required (deliberate design choice per refactor).

1. **Audio crossfade coroutine dedup.** `AudioManager.CrossfadeMusic` / `FadeOutMusic` / `CrossfadeAmbient` collapsed onto two shared coroutines: `CrossfadeSource(source, clip, targetVol)` (fade out → swap clip → fade in) and `FadeVolume(source, target, duration)`. The `isFadingMusic` flag handling stayed in the thin wrappers, so the "skip a second music crossfade while already fading" behavior is unchanged.

2. **`UnitBase<T>` extraction (conservative — clearly-identical code only).** New `Assets/Scripts/UnitBase.cs`: abstract CRTP generic (`Worker : UnitBase<Worker>`, `Warrior : UnitBase<Warrior>`, `Enemy : UnitBase<Enemy>`) so each subclass keeps its own `ActiveRegistry<T>` list. Pulled up ONLY verbatim-shared members: registry `Awake`/`OnDestroy` register/unregister, `ActiveList`, `showStateText`/`textHeightOffset` fields, `CachedHealth` (now lazy-fetch) / `CachedAgent`, `FetchAgent`, `SetupHealth(maxHP, onDeath)`, `CreateStateText(fontSize, text, color)`, `StateDisplayName(fallback)`, `CreateStuckResolver`, `SetupCombatAudio` / `PlayCombatClip`. Component TYPE NAMES are unchanged → prefabs and `AddComponent<Worker>()` etc. unaffected. Serialized field names unchanged → Inspector values preserved. Subclass `OnDestroy` overrides now call `base.OnDestroy()` first. Cosmetic-only: Worker's `textHeightOffset` moved from its "Visual Feedback" header (default 2) to the base's "State Display" header (default 2.5) — the prefab's serialized value (2) still wins, but if the field ever shows as "empty override" re-confirm it reads 2.

3. **`BuildPlacement` four-way split.** 1729-line class → ~380-line coordinator + four **plain C# helper classes** (NOT MonoBehaviours — so zero scene edits, no component wiring): `WallLinePlacer.cs` (click-start/click-end wall lines, L-path + Bresenham, cursor ghost), `GhostPlacer.cs` (single-building follow/rotate/validity/confirm), `DemolishTool.cs` (Delete/X highlight + 50% refund demolish), `NoBuildZoneRenderer.cs` (merged/individual red zone outlines + blue ghost preview zone). Coordinator keeps all serialized `[Header]` fields, `Update` mode-dispatch, `StartPlacement`/`SelectBuilding`/`CancelPlacement`/`TryConvertWallToGate`/`GetSnappedMousePosition`/`IsWallType`. Shared runtime state (`currentGhost`, `ghostRenderer`, `isPlacing`, `mainCam`, helper refs) is `internal` on `BuildPlacement` — internal fields are not Unity-serialized, so the Inspector is unchanged. Also deduped the copy-pasted quad-mesh / transparent-material / square-border blocks inside the zone renderer.

4. **Worker executors routed through `AINavHelper`** (the behavior-touching one — done last per plan). All 9 raw `bb.agent.SetDestination(...)` calls in `GatherExecutor` / `ReturnToBaseExecutor` / `FleeToHutExecutor` now go through `AINavHelper.TrySetDestination` and **honor its bool return** — `isStopped = false` fires only on success, so the "ghost moving" freeze (isStopped=false with no queued path) can't occur. Each site has a retry: Gather tracks a `destinationQueued` flag and retries each frame in `UpdateMovingToResource`; Return relies on its existing `!hasPath && !pathPending` retry; Flee's `SetFleeDestination` now returns bool and keeps `recalcTimer` at 0 on rejection (retry next frame instead of standing still 0.5s). This is why it needs a playtest — it changes movement command flow for all worker actions.

**New files (Unity will generate .meta on first focus — expected):** `UnitBase.cs`, `WallLinePlacer.cs`, `GhostPlacer.cs`, `DemolishTool.cs`, `NoBuildZoneRenderer.cs`.

**Playtest checklist before committing:** worker gather → return → deliver loop and flee-from-enemies (the AINavHelper change); wall line drawing (L-path, Shift staircase, R toggle, G gate-convert); single-building placement / R rotate / Esc cancel / type-switch (1-4); demolish mode (Delete/X); day↔night music + ambient crossfades. Watch the console for any new errors/warnings on Play. Once clean, commit as Phase 6.24 and (optionally) flip the ⚠️ status note above to "Complete".

### Phase 10 (In Progress): Visual Overhaul

**Status:** Stage 1 shipped (post-processing + lighting presets). Stages 2-5 planned. Full spec in `PHASE_10_VISUAL_OVERHAUL.md` at repo root — that file is the source of truth; this entry is a summary.

**Goal:** Replace primitive geometry and default lighting with a cohesive stylized low-poly aesthetic in the Bad North / Townscaper / Islanders / Synty POLYGON Pirates family. Visual reference is the Castaway Colony main menu mockup (sunset palette, low-poly islands, stylized water, soft DOF on background) — but tuned for top-down RTS framing: no DOF, no macro lensing, readable silhouettes from gameplay camera height.

**Tech foundation already in place:** Unity 6000.5.9f1 + URP 17.5.0 (Volume system, Shader Graph), `DayNightCycle.cs` ready to drive sun rotation, sun color, ambient gradient, fog, and water shader properties between day/night `LightingPreset` ScriptableObjects.

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

### Phase 10 Stage 2a (Units Plumbed — ⚠️ PENDING PLAYTEST): Art Library → Gameplay Prefabs

**The problem this solves.** `LowPolyAssetGenerator` had produced a complete art library (26 meshes, 34 materials, 26 mesh-only prefabs under `Assets/Art/`) that **nothing in the game referenced.** Gameplay prefabs were still Unity primitives: `Worker.prefab` a capsule (`fileID 10208`), `Hut.prefab` a cube (`10202`), each squashed by a non-uniform root scale. The two asset sets never pointed at each other — the art was generated but not *plumbed*.

**Why it isn't a mesh swap.** Art is authored **base-pivot, facing +Z, real world units, scale 1**. Gameplay prefabs are **center-pivot primitives** at scale `0.4/0.6/0.4` (Worker), `0.5/0.7/0.5` (Warrior), `0.45/0.7/0.45` (Enemy), `2/1.5/2` (Hut). Assigning the art mesh to the existing root MeshFilter would squash it *and* sink/float it. Art meshes are also multi-submesh (8 material slots on a unit) vs one on the primitives.

**New file: `Assets/Editor/LowPoly/LowPolyPlumber.cs`.** Two menu items under `Tools > Island RTS > Low-Poly Templates/`. Idempotent (Model child rebuilt from scratch; every value assigned absolutely, never accumulated), table-driven, so future categories are new table rows.

`Plumb Units Into Gameplay Prefabs` — per unit, via `PrefabUtility.LoadPrefabContents` / `SaveAsPrefabAsset`:
1. Destroy the root's `MeshFilter` + `MeshRenderer` (the primitive visual).
2. `root.transform.localScale = Vector3.one`.
3. Add a `Model` child as a **nested prefab instance** of the art prefab (so regenerating art propagates automatically), at local identity.
4. Restate every root `CapsuleCollider` in world units, `direction = 1`, `center = (0, height/2, 0)` for the base pivot.
5. `NavMeshAgent.baseOffset` `1 → 0`. Radius/height/speed **untouched** (pathing + bake concerns).
6. Retune `HealthBar` (`heightOffset`/`barWidth`/`barHeight`) and `textHeightOffset` — reached via `SerializedObject.FindProperty` by name, since `UnitBase<T>` is generic and can't be cast to a non-generic base.

Sizes were already right: the generator's `SizeNote` values match the old primitives' *world* dimensions exactly (Worker 0.40×1.2, Warrior 0.50×1.4, Enemy 0.45×1.4), so silhouette **size** doesn't change — only pivot, scale and shape. Units also stop floating: `baseOffset 1` with a center-pivot capsule had feet 0.3–0.4 above the NavMesh.

`Re-Material Walls (Keep Procedural Meshes)` — points `WoodenWall`/`StoneWall` root renderers at `LP_WoodPlank`/`LP_StoneBlock`. Walls **cannot** be prefab-swapped: `WallConnector` generates 6 shapes + 6 gate variants at runtime and overwrites the root MeshFilter, and the generator only emits one flat segment per material. Gates inherit this (they tint via `.material.color`, and LP materials are URP Lit so `color` → `_BaseColor` works).

**Runtime fixes the child-model move required** (`CombatEffects.cs`):
- `FadeOutUnit` used `unit.GetComponent<Renderer>()` — root-only, so death fades would have silently no-op'd. Now `GetComponentInChildren`.
- `FadeOutEffect` faded only `.material` (slot 0). With 8-submesh art that left 7/8 of the corpse opaque. Now copies and fades `.materials`.
- `CreateStateText` font sizes retuned for the un-squashed root: Worker `3 → 1.8`, Warrior `2 → 1.4`, Enemy `2 → 1.4` (old effective size was `fontSize × rootScale.y`). The TMP child was also being **skewed** by `LookAt` under non-uniform parent scale — that's gone too.

Compile-verified against 6000.5.9f1 (77 runtime + 10 editor scripts): **0 errors, 0 warnings.**

**Superseded by Stage 2b below,** which extended the same tool to buildings, ghosts, resource nodes and environment scatter.

### Phase 10 Stage 2b (Buildings / Resources / Ghosts / Scatter — ⚠️ PENDING PLAYTEST)

Completes the plumbing. `LowPolyPlumber` became table-driven — one `Plumb` record per gameplay prefab with every retune field nullable (`null` = leave it alone) — so future categories are new rows, not new code. Six menu items under `Tools > Island RTS > Low-Poly Templates/`: **Plumb Everything**, Plumb Units, Plumb Buildings (+ Ghosts), Plumb Resource Nodes, Re-Material Walls, plus `LowPolyScatter`'s Scatter / Clear Environment Props.

**Buildings.** `Hut` (root scale `2/1.5/2` → 1, art 2×2 footprint / 2.6 to peak) and `WatchTower` (`2/4/2` → 1, art 2×2 / 4.0 tall) get the unit treatment: Model child, box collider restated in world units on the base pivot, obstacle carve **kept at the same world volume** (hut: extents `1.1/0.825/1.1` at center `0/0.825/0` = the old 2.2×1.65×2.2 lifted out of the ground). `HutData`/`WatchTowerData` `placementHeight` `0.75 → 0`, which is what actually makes base-pivot buildings sit right — it's the Y `BuildPlacement` snaps a placed building to.

Health bars were **not** preserved verbatim: `heightOffset` was multiplied by the root's Y scale, so the hut's bar floated 5.25 world units up and the tower's **12.75**. Now 3.2 / 4.6, just above the new rooflines.

**Campfire** keeps its 4-object hierarchy: `FirePit` and `Wood` are deleted, the Model child is mounted, and `Flame` is **kept but its MeshRenderer disabled** — the art Campfire mesh contains its own HDR-emissive flame (Ember + FireCore prisms clearing the Bloom threshold of 1.0), so leaving the old capsule flame visible would double it. Re-enable that one checkbox to get the original back. Root was already scale 1; its collider becomes `2/1.2/2` at center `0/0.6/0`, preserving the 2×2 clickable footprint.

**Resource nodes.** `RockNode` and `BerryBush` just lose their visual child (`Cube`, `Sphere`) and gain a Model — their roots were already scale 1. `Tree` is the awkward one: it wraps the "Tree for Carson V3" FBX as a nested prefab instance at root scale 0.5, so its renderers are **disabled rather than deleted** and the root goes to scale 1 (the art tree is 3.6 tall; at 0.5 it would render 1.8). `Tree1.prefab` is left alone — zero scene instances, not referenced by `ResourceSpawner`, effectively dead.

**Ghosts.** `HutGhost`/`WatchTowerGhost` take the art **mesh** on their existing root renderer with N copies of `Mat_Ghostbuilding` (one per submesh, or the extra submeshes render magenta). No `Model` child — see the gotcha above for why.

**Environment scatter.** New `Assets/Editor/LowPoly/LowPolyScatter.cs`: seeded (`Seed = 20260823`), deterministic, destroy-and-rebuild under a single `_LowPolyScatter` root, so the scene diff stays regenerable. ~255 props across the 13 environment assets, each with a radial band (palms and flotsam outward, ferns/grass inward), an 11-unit campfire clearing, area-uniform radius sampling (`sqrt(random)`), a spacing check, and a downward raycast that must hit ground — **failing the ray is how props are kept off the water.** Props are marked Batching/GI/Occludee static for Stage 4. Saves and restores Unity's global `Random.state` so a scatter doesn't perturb anything else, and logs how many props couldn't find a spot rather than letting a density cap look like full coverage. Art prefabs carry no colliders, so scattered props never block pathing, intercept clicks, or affect the bake.

**Runtime fixes** — multi-submesh art broke five tint sites that all assumed one material per renderer. New `Assets/Scripts/RendererTint.cs` (`Collect` / `CaptureColors` / `SetColor` / `RestoreColors`) now backs `BaseBuilding` hover + campfire-death darken, `ResourceNode` hover, `BuildPlacement` ghost validity, and `FadeOutEffect`. `BuildPlacement` caches `ghostMaterials` at ghost-spawn and exposes `SetGhostColor`, so the per-frame validity tint stays zero-GC (`GhostPlacer` and `WallLinePlacer` call through it; `GhostPlacer.CancelPlacement` clears the cache).

Compile-verified against 6000.5.9f1 (78 runtime + 11 editor scripts): **0 errors, 0 warnings.**

**Playtest checklist** (nothing is applied until the menu items are run): run **Plumb Everything**, then **Scatter Environment Props**, then **re-bake the NavMesh**. Verify — units/buildings/resources render as low-poly art and sit *on* the ground; hut and watchtower place at the right height (`placementHeight` 0) and their ghosts match the real silhouette and still tint green/red across every submesh; hover highlight tints the *whole* building/node, not one panel; campfire shows exactly one flame and still blooms; workers path around trees (Tree's obstacle doubled); demolish, wall lines and gate conversion still work; death fade covers the whole body. Known cosmetic leftover: wall ghosts preview at `placementHeight` 0.75 while real walls snap to y=0.02 — pre-existing, unrelated to the art swap.

### Phase 10 Stage 2c (Simplification Pass — ⚠️ PENDING REGENERATION + PLAYTEST)

Feedback-driven simplification of the generated art plus two gather-behavior changes (2026-08-24). Compile-verified vs 6000.5.9f1 Roslyn: 0 errors, 0 warnings.

**Art (generator changes only — nothing in-game changes until the menu items are re-run):**
- **Units are template-simple meeples now:** one tapered body block + head + a single identifying accessory (Worker: straw hat; Warrior: helmet + red crest + round shield; Enemy: red band + horns). All limbs, straps, tools, backpacks, pauldrons deleted. Same authored dimensions, so colliders/plumber tables are untouched.
- **Hut:** plain tapered walls + one dark door panel + one window panel per side + a single one-tone pyramid roof (peak stays at 2.6). Foundation, corner posts, plank seams, layered roof, ridge cap deleted.
- **BerryBush:** one solid low-jitter (0.08) mass; 8 chunky berries (0.16 × width) whose centers sit ON the nominal ellipsoid surface. `Bush_Round`/`Bush_Wide` (environment) inherit the single-mass look.
- **RockNode:** one solid boulder (satellite rocks deleted); 4 ore crystals as `TaperedSegment` spikes rooted at 0.35R inside the mass and tipped at 1.25–1.45R outside, so they visibly pierce the surface.
- **Watchtower:** legs + platform + railing + single one-tone pyramid roof on its corner posts. Cross bracing (16 beams), deck planks, ladder, and the second roof tone deleted.
- **Campfire:** 6 ring stones (was 9), 4 teepee logs (was 5 + 5 charred overlays), 2 flame cones (was 3) — still on the HDR-emissive Ember/FireCore keys, so bloom behavior is unchanged. Material key set unchanged too.
- **Wall segments + gate** simplified (4 pointed logs, binding rails deleted; 3 chunky plain stone courses; gate leaves lose their iron bracing). NOTE: these art segments are showcase/reference only until someone extends WallConnector — in-game walls are procedural meshes that only take the LP materials.
- **Tree variance:** `Tree_B` (3.4 tall) and `Tree_C` (3.8 tall) added, and canopy side-blob placement is now seeded so each seed gets its own canopy shape. New runtime `TreeVariance.cs` on Tree.prefab (wired by the plumber's new `VariantMeshes` column + `ApplyVariants` step) picks a variant per instance and applies random yaw + 0.9–1.12 scale jitter to the Model child in Start. Visual-only: root transform, colliders and the runtime NavMeshObstacle are untouched, and all variants share one material key order, which is what makes the `sharedMesh` swap safe.
- **Embedding rule that makes "attached" reliable:** with `Rock` jitter j, put an attachment's center on the *nominal* (unjittered) surface and make its diameter > 2jR — it then always straddles the actual jittered surface. Never place small props at fixed offsets beyond the nominal radius (the old berries at 0.40–0.52 × width floated; the old ore chips at fixed radii detached).

**Gameplay:**
- **Workers stand next to nodes now.** `Worker.GatherStopDistance = 0.35f` const replaces `stoppingDistance = gatherDistance * 0.8f` (set in `Worker.Start`, restored in `ReturnToBaseExecutor.OnExit`); `gatherDistance` is purely the arrival tolerance to the gather point — prefab value 1.5 → 1.2, script default 2.5 → 1.2. Net effect: stop ~1.4 from a tree center / ~1.0 from a bush or rock, instead of ~2.2–3.0 (the old stoppingDistance stacked on top of the offset gather point).
- **Unreachable-node fallback.** `AIBlackboard` gains a 4-slot zero-GC ring (`MarkNodeUnreachable` / `IsNodeUnreachable`, 15s expiry). `GatherExecutor.UpdateMovingToResource` detects a dead-end path — `PathInvalid`, or `PathPartial` with the partial path walked to its end — sustained 0.6s, then `GiveUpOnUnreachableNode`: mark, unclaim, pick another node. `ResourceAvailability` and `TryPickupNewResource` both skip marked nodes, so the worker doesn't march back into the same wall.

- **Turn speed + node crowding (2026-08-24 follow-up).** Worker `angularSpeed` 120 → 360 (120 was an anti-jitter value — watch for turn jitter in playtest). Approach tightened again: gather-ring offset `obstacleRadius * 1.3` → `* 1.1` (now the shared `ResourceNode.GatherRingRadius`), `GatherStopDistance` 0.35 → 0.25, `gatherDistance` 1.2 → 1.0 (prefab too). **Per-node worker capacity:** `ResourceNode.GetMaxWorkers()` = standing-ring circumference / 1.25 per worker, scaled by how many of 8 NavMesh samples around the node are open, cached 5s (building walls changes it). `HasWorkerRoom(worker)` counts registered + claimed workers against the cap (cleans dead entries; a worker already registered/claiming always keeps its slot). Enforced in `ResourceAvailability` (full nodes skipped → workers spill to the next node), `TryPickupNewResource`, and `RegisterWorker` (arrival race falls into the existing empty-node re-target branch). Net: ~6 workers max around a tree, ~4 around a bush/rock, fewer when hemmed in by walls.

**To apply the art:** run `Generate All Assets`, then `Plumb Everything` — the re-plumb is REQUIRED, not optional: submesh/material counts changed (hut 6 → 3 keys, units ~8 → 3–4), and ghost prefabs carry one ghost-material per submesh. Then the Stage 2b playtest checklist, plus: berries attached, rock reads as one object, workers stand close to nodes, and a worker walled off from a node re-targets a different one within ~1s.

### Unity 6000.5.9f1 Upgrade (Engine — ⚠️ NOT YET PLAY-TESTED)

Project converted from Unity **6000.0.25f1 → 6000.5.9f1**. Checkpoint commit `ae8f632` captured the full pre-upgrade tree (Phase 6.24 refactors + Phase 10 low-poly art WIP) in its 6000.0 serialization, so the pre-upgrade state stays recoverable.

**Script compatibility: clean.** All 76 runtime scripts plus the 9 `Assets/Editor/LowPoly/` editor scripts compile against 6000.5.9f1 with **zero errors**. The API surface was already conservative — `UnityEngine`, `UnityEngine.AI`, `TMPro`, `UnityEngine.UI`, one `UnityEngine.Rendering` (for `AmbientMode`). No `ScriptableRenderPass` / `ScriptableRendererFeature` / `OnRenderImage`, no asmdefs, no custom render features, so none of the URP 17.5 RenderGraph churn applies. Legacy `Input.*` still works — `activeInputHandler: 2` (Both) is preserved.

**One real deprecation, fixed:** `FindFirstObjectByType<T>()` became obsolete in 6000.5 ("relies on instance ID ordering"). All 11 call sites swapped to `FindAnyObjectByType<T>()` across `AIWorldState`, `AudioManager`, `DayNightCycle`, `Enemy`, `EnemySpawner`, `GameManager` (x3), `GridToggleHotkey`, `ResourceSpawner`, `WallGrid`. Every site was a singleton/unique-object lookup, so `Any` is semantically identical and marginally faster. Recorded in Key Conventions so it doesn't get reintroduced.

**Packages pinned deliberately** in `manifest.json` to the 6000.5.9f1 defaults *before* the first open, rather than letting the editor silently resolve them — the diff is reviewable that way:

| Package | Was | Now |
|---|---|---|
| URP + Core + ShaderGraph | 17.0.3 | 17.5.0 |
| com.unity.ugui (TextMeshPro) | 2.0.0 | 2.5.0 |
| Input System | 1.11.2 | 1.20.0 |
| AI Navigation | 2.0.9 | 2.0.14 |
| Timeline / VisualScripting | 1.8.7 / 1.9.4 | 1.8.12 / 1.9.12 |
| Test Framework / collab-proxy | 1.4.5 / 2.10.1 | 1.7.0 / 2.13.6 |
| Rider / VisualStudio / multiplayer.center | 3.0.31 / 2.0.22 / 1.0.0 | 3.0.38 / 2.0.26 / 1.0.1 |

Unity confirmed the pin took (`Registered 57 packages` — URP 17.5.0 / ugui 2.5.0 / inputsystem 1.20.0) and rewrote `packages-lock.json`.

**Import was clean.** `Logs/Editor.log` shows zero `error CS`, zero `warning CS`, zero shader errors, and `Loaded scene 'Assets/MainIsland.unity'`. The Global Volume profile, `NavMesh-Ground.asset`, and `UniversalRenderPipelineGlobalSettings.asset` all imported without migration errors. Unity rebuilt `SourceAssetDB` / `ArtifactDB` in the new format (expected — the old ones cannot be upgraded in place). The `[API Updater] Failed to read assembly dependency graph ... Invalid signature` line is benign: a stale 6000.0 cache file that gets recreated.

**Notably, no scenes, prefabs, or materials were re-serialized** by the upgrade — only `ProjectSettings.asset` (PlayerSettings `serializedVersion` 28 → 29: dead holographic/WSA fields dropped, new iOS/Android/Switch/WebGL fields added) and two new settings files Unity 6.5 creates, `ProjectAuditorSettings.asset` and `PhysicsCoreProjectSettings2D.asset`. Assets re-serialize lazily on save, so expect scene/prefab diffs the first time each one is edited.

**One behavior-relevant settings change:** the upgrade replaced the empty `m_BuildTargetVRSettings` with an explicit non-automatic graphics API list for `WindowsStandaloneSupport` — D3D11 then D3D12 (`m_Automatic: 0`). D3D11 is first and is what the editor already uses, so this matches previous behavior, but it is now pinned rather than auto-selected.

**Two things the upgrade changed that needed review (both checked, one reverted):**
- **Emissive materials.** URP 17.5's material upgrader bumped every material's `version: 9 → 10` and rewrote `m_LightmapFlags` on the emissive ones. `Mat_Flame`, `LP_Ember`, and `LP_FireCore` went `1` (RealtimeEmissive) → `3` (`AnyEmissive` = Realtime|Baked) — emission is **preserved**, and their HDR `_EmissionColor` values survived intact (`LP_FireCore` r:3, `LP_Ember` r:2), so they still exceed the Bloom threshold of 1.0. Only two TextMeshPro *example* materials (`Crate - URP`, `Ground - URP`) got flag `7`, which includes `EmissiveIsBlack` — irrelevant to gameplay. Still worth an eyeball on the campfire in Play mode.
- **Version control mode — REVERTED.** The upgrade flipped `VersionControlSettings.asset` from `m_Mode: Visible Meta Files` to `m_Mode: Unity Version Control` (UVCS/Plastic), presumably because `com.unity.collab-proxy` is installed. This project uses Git, so it was reverted on disk. If it reappears in a future diff, set it back via **Edit > Project Settings > Version Control > Mode: Visible Meta Files** — a running editor holds this in memory and can rewrite the file.

Also: Unity 6.5 emits a new-format `islandrts.slnx` solution file. `.gitignore` had `*.sln` but not `*.slnx`; added.

**Gotchas learned:**
- Unity reads `manifest.json` during startup package resolution. Edit it **before** launching the editor, not while it is booting — otherwise you race the resolver and land in a confusing half-resolved state.
- Batchmode (`-batchmode -quit -projectPath`) cannot run against a project another editor instance has open; `Temp/UnityLockfile` is the tell. Check for a running `Unity.exe` first.
- Unity 6 moves the editor log to a **project-relative** `Logs/Editor.log` almost immediately; `%LOCALAPPDATA%\Unity\Editor\Editor.log` only holds the boot preamble and a pointer to it. Read the project-relative one.
- Scripts can be compile-verified against a new editor **without converting the project** — point Roslyn (`Editor/Data/DotNetSdk/sdk/*/Roslyn/bincore/csc.dll`) at `Editor/Data/Managed/UnityEngine/*.dll`. Do NOT also reference `Editor/Data/Managed/UnityEditor.dll` alongside the `UnityEditor.*Module.dll` files — that produces spurious `CS0433` ambiguous-type errors (e.g. `EditorApplication`) which Unity itself never sees, because the real `UnityEditor.dll` is a type-forwarding facade.

**Pre-existing drift spotted (not caused by the upgrade) — docs fixed, build settings still WRONG.** The real gameplay scene is `Assets/MainIsland.unity` (941 KB, 274 GameObjects, actively edited). `Assets/Scenes/SampleScene.unity` is the leftover stock Unity scene — 11 KB, 3 GameObjects, untouched since July 2024. README and this file both pointed at SampleScene and have been corrected.

> ⚠️ **`EditorBuildSettings.asset` still lists only `Assets/Scenes/SampleScene.unity`.** Left alone deliberately — changing which scene ships in a build is a project decision, not a doc fix. **A build made right now would ship the empty scene.** Fix via *File > Build Profiles* (add `MainIsland`, remove/disable `SampleScene`) before building anything.

**Playtest checklist:** the Phase 6.24 checklist now covers both the refactors and the engine upgrade — worker gather/return/deliver and flee; wall line drawing (L-path, Shift staircase, R toggle, G gate-convert); single-building placement / rotate / cancel / type-switch; demolish mode; day-night music + ambient crossfades. On top of that, verify specifically for the upgrade: post-processing still reads correctly (Bloom on the HDR-emissive campfire, no blown-out vignette or tonemap shift from URP 17.5), the day/night `LightingPreset` lerp still drives sun + ambient, NavMesh agents still path (AI Navigation 2.0.14), and TextMeshPro UI still renders (uGUI 2.0.0 → 2.5.0 is the largest single package jump).
