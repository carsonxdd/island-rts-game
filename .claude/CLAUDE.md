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
| `PopulationManager.cs` | The colony roster: who lives here (workers, idle colonists, warriors), which `IHousing` they sleep in, and the survivor-arrival timer |
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
| `CameraController.cs` | Smoothed WASD pan (zoom-scaled), Q/E rotate, eased scroll zoom, middle-mouse free-look tilt (30°–60°) + rotate orbiting the view-center ground point (orthographic, unscaled time) |
| `CameraShake.cs` | Combat shake, pure offset approach (no stored position) |
| `AudioManager.cs` | Singleton: music, SFX, ambient, crossfades |
| `GameStartController.cs` | Opening sequence: survivor landing → campfire placement → colony start |
| `PlayerCharacter.cs` | The player's own named character for the whole run (right-click move, HP with knock-out instead of death, regen at the fire, name label); replaced `Survivor.cs` 2026-09-02 |
| `PlayerProfile.cs` | The run's character name: last-used value in PlayerPrefs, run snapshot set by the name popup (`Difficulty` pattern) |
| `Items/ItemCatalog.cs` | Static item definitions: materials (stick, stone chunk), resources-in-hand, tools; the layer above the four pooled resources |
| `Items/Inventory.cs` | Fixed-slot stack container (plain C#, zero-alloc) — the character's hands and the campfire stockpile |
| `Items/CraftingCatalog.cs` | Campfire recipes: item + resource costs, seconds, output tool, unlocks; `CanAfford` / `Pay` across hands + stockpile + pool |
| `Items/Unlocks.cs` | What the colony knows how to do (jobs, construction, militia); read at the point of effect, everything granted under the sim |
| `Items/HeldItem.cs` | The tool in the character's hand (art under a HandSocket; visual only) |
| `UI/PlayerHUD.cs` | Bottom-centre strip: name, six inventory slots, activity line |
| `DebugMenu.cs` | F4 cheat menu (editor + dev builds only): resources, quick-start colony, time, combat cheats |
| `Terrain/TerrainGrid.cs` | Island terrain: chunked flat-shaded heightmap in 7 material bands, `SampleHeight`/`IsBuildable`/`IsReachable` API, runtime NavMeshSurface, random seed per run (`RunSeed`) |
| `Terrain/IslandGenerator.cs` | Pure seeded island pipeline (shape → relief → terraces → ponds → seabed → detail → anchors → validation + ramp repair); returns an `IslandField` |
| `Terrain/IslandSettings.cs` | ScriptableObject with every generator knob (`Assets/Settings/IslandSettings.asset`); `CreateDefault()` is the code fallback |
| `Terrain/PropScatter.cs` | Runtime environment set dressing from `ScatterSettings` terrain rules (height / slope / grass tone) |
| `Terrain/IslandOptions.cs` | New Game world choices (island size, terrain style, seed): persisted selection + run snapshot, the `Difficulty` pattern |
| `ResourceSpawner.cs` | Habitat-driven node placement (forests in dark lowland, bushes on meadows, stone on high ground, ore on plateaus / cliff feet); counts scale with map area |
| `ResourceUI.cs` / `WorkerAssignmentUI.cs` | Code-built HUD chips (wood / food / stone / metal / housing) and the campfire assignment panel, on `MenuBuilder` |

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

**Worker actions:** Gather, Return, Pickup (ground sticks/stones), Idle, Flee (garrison in nearest hut)
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
- **A placement ghost must never be visible to the placement raycast (2026-08-26).** `HutGhost`/`WatchTowerGhost` shipped with a full-size `BoxCollider` on layer 0 — which is exactly what `groundLayer` is set to (`m_Bits: 1`) — so `GetSnappedMousePosition`'s ray hit the ghost's *roof* instead of the terrain. On the tilted camera the ghost settled with its roof under the cursor, parking its base roughly one building-height **down-screen** from the mouse (hut ≈ 2.6 m, tower ≈ 4 m); `WorkshopGhost`/`CampfireGhost` have no collider and so felt correct, which is how the bug was isolated. `BuildPlacement.SetupGhost` now disables every collider on a spawned ghost and moves it to Ignore Raycast (layer 2); `GameStartController` does the same for the campfire ghost. Ghost colliders serve nothing — validity uses `Physics.CheckBox` against `buildingsLayer`.
- **Placement ghosts snap, they don't lerp.** `GhostPlacer` used to `Vector3.Lerp` toward the snapped cell at `movementSpeed * Time.deltaTime` (framerate-dependent), so the ghost was *drawn* trailing the cursor while validity was *evaluated* at the unlagged target — the green/red tint could disagree with the silhouette on screen. Grid snap already quantizes motion to whole cells, so there is nothing to smooth. `BuildPlacement.movementSpeed` and `GameStartController.ghostFollowSpeed` were deleted (stale `movementSpeed: 10` / `ghostFollowSpeed: 10` keys in `MainIsland.unity` are ignored and drop out on the next scene save).

### Day/Night & Combat
- Day: 120s, Night: 60s (configurable in DayNightCycle). Clock speed is phase-based — day and night each cover half the 0..1 time parameter at their own rate. (Fixed 2026-08-25: the old constant `timeSpeed = 1/(day+night)` made both phases actually last 90s and silently ignored the configured lengths.)
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
- **Never `ResetPath()` just to force a new destination.** `ResetPath` drops the path AND zeroes velocity that same frame, so the agent stands still until the new path is computed; `SetDestination` alone swaps the path in place and the agent keeps walking the old one meanwhile. `EnemyAttackExecutor.IssueMove` used to `ResetPath` on every forced retarget — with a whole wave retargeting on the same frame it read as a synchronized group freeze (2026-08-26). Reserve `ResetPath` for genuinely stopping a unit (e.g. warrior entering attack range). The forced move is now tracked by a `moveQueued` flag that survives a throttled/rejected `TrySetDestination` and retries next frame.
- **Stagger every per-unit retarget/recalc timer in `OnEnter`** (`Random.Range(0f, interval)`), the way `EngageEnemyExecutor` does. An un-staggered timer (the enemy executor's was a flat `0f`) lets a whole wave re-path in one frame, and Unity's path-request queue then drains over several frames — a visible simultaneous stall even at a healthy framerate.
- **The AI evaluation budget scales with population (2026-08-26).** `AIBrain` used a fixed 5 evals/frame, which was a hard ceiling at ~90 units (5 × 60fps ÷ 3.3 evals/unit/sec); past that, brains starved and units visibly lagged. It is now `Clamp(activeBrains × deltaTime / MinEvalInterval, 5, 64)` — exactly the rate needed to keep every brain on schedule. `activeBrains` is counted in `Initialize`/`OnDestroy`.
- **A throttled evaluation must be DEFERRED, never dropped.** The old `AIBrain.Update` reset `evalTimer = evalInterval` *before* the budget check, so a brain that lost the race silently skipped that evaluation and waited another full interval. The timer is now only reset when `EvaluateActions()` actually runs; otherwise it stays ≤ 0 and retries next frame.
- **`ForceReeval()` is budgeted now, not a bypass.** It used to skip the throttle entirely *and* not consume budget — so one enemy dying inside the base made every worker within 30u (`Worker.OnEnemyDiedUtilityAI`) evaluate on the same frame, a spike that grew linearly with population. Forced evals may now exceed the normal budget up to `MaxEvalsPerFrame` and are deferred (not dropped) beyond it. **Any new `ForceReeval` broadcast on a static death/damage event is a population-scaled spike — prefer a radius filter and accept a frame of latency.**
- **Order consideration checks cheapest-first, and prune against the running best.** `ResourceAvailability` ran its `distance > searchRadius` cull LAST, so `HasWorkerRoom()` (compacts a claim list; can fire 8 `NavMesh.SamplePosition` calls on a cache miss) executed for every same-type node island-wide — ~440 nodes per worker, ~3× a second. Now: type check → squared-distance cull → `distance >= bestScore` prune (the claim penalty is never negative, so a further node cannot win) → the expensive availability checks.
- **`ResponseCurve.Constant` discards its input**, so pairing it with a scanning consideration burns the entire scan for nothing. Worker Idle used `ResourceAvailability(Constant(0.1f))` — a second full node scan per evaluation whose result was thrown away. Use `ConstantScore` for a fixed utility floor. (Its only side effect, caching `bb.bestResource`, is already done by Gather's `ResourceAvailability`, which is that action's first consideration and so always runs.)
- For enemies, replacing four competing ActionOptions (AttackWarrior/AttackBuilding/BreachWall/AttackCampfire) with ONE action + priority function eliminated a whole class of stutter bugs where sibling actions + momentum + commitment threshold fought on target death. Generalize this pattern when competing actions share most of their logic and differ mainly in "what target?"
- Singletons in single-scene games should NOT use `DontDestroyOnLoad` — it causes stale state (worker counts, audio cooldowns, etc.) to survive scene reloads on restart. Only add it back if you introduce a main menu scene
- Worker bookkeeping has ONE owner: `Worker.OnDestroy` → `BaseBuilding.NotifyWorkerRemoved(this)`, which removes from `activeWorkers` (roster membership is the idempotence guard) and calls `PopulationManager.RemoveColonist(worker)`; warriors leave through `Warrior.Die` → `NotifyWarriorKilled` → `RemoveColonist`. Job counts (`woodWorkers` etc.) are **computed** from the roster since 2026-09-02 — never add a counter back. `UnassignWorker` clears the job; it no longer destroys anyone
- Housing capacity has ONE owner: buildings implement `IHousing` and call `PopulationManager.RegisterHousing(this)` in Start / `UnregisterHousing` via a flag-guarded release from both death and `OnDestroy` (so demolish counts too). Capacity is summed from live providers — PopulationManager must NOT rescan the scene at Start (the old `RecalculateHousingCapacity` double-counted depending on Start order)

- `AIBlackboard.SetTarget` returns true only when the target actually **changed** — it deliberately does NOT reset `isInAttackRange` or issue movement; each executor decides what to reset on a change (enemies drop attack state, warriors keep their hysteresis timestamps). Don't add side effects to it
- A worker's `deliveryDistance` and warrior heal/attack ranges are **edge distances** (collider `ClosestPoint`), not center distances. If you add a new interaction with any building, use `TargetingUtil.EdgeDistance` — a center-based threshold smaller than the building's half-extent can never trip

- **Resource nodes CARVE the NavMesh now (2026-08-26).** Non-carving obstacles never affected pathfinding — paths ran straight through trees, agents rubbed/slowed on the avoidance obstacle, and enemies chasing warriors froze dead behind trunks. Radii are trunk-tight (tree/bush 0.45, rock 0.5). Consequences: `ResourceNode.GatherRingRadius = obstacle.radius + 0.55` (carve hole = radius + ~0.5 bake erosion; ring must sit at the hole edge), and depletion shrink scales the **Model child, never the root** — root scale drives the obstacle, and scaling it would re-carve the NavMesh every gather tick (the original reason carving was disabled). Model baseline is captured lazily at first shrink so it composes with TreeVariance's Start-time jitter.
- **Worker Pickup action (sticks/stones):** `PickupAvailability` caches `bb.bestPickup` and fades with distance (0 beyond 22u) so pickups only outbid Gather when genuinely close; ThreatNearby hard-suppresses it like Gather; pickups carry a `claimedBy` worker so two workers never chase one stick. `CollectPickupExecutor` releases the claim on exit/stuck-reset. Food workers score 0 (no food pickups).
- **Flee = garrison (2026-08-26):** FleeToHutExecutor paths to the nearest hut's carve-safe approach point (the old code targeted hut CENTERS — silently rejected because huts carve), and at edge-arrival calls `Worker.SetGarrisoned(true)` (renderers + agent + collider off). Only FleeToHutExecutor may call SetGarrisoned, and its OnExit always restores before any other executor runs. A destroyed shelter pops the worker out immediately. No huts → crowd at the campfire edge; no campfire → legacy run-away.
- **`TerrainGrid.FlattenArea(center, radius, blend)` (T2, pulled forward):** levels the ground to the height at `center`, rebuilds only touched chunks, kicks `UpdateNavMeshAsync`. Called by GhostPlacer.ConfirmPlacement (1.8/1.4), GameStartController.SpawnCampfire (2.2/1.6), and the F4 quick-start hut ring. Ghost previews at the center height = exactly where the pad ends up, so ghost and placed building heights always match. Walls deliberately do NOT flatten (they follow terrain per-cell).
- **`GridOverlay` draws only buildable cells, draped on the terrain (2026-08-26).** It used to draw a flat 50×50 square of `LineRenderer`s at y=0 — buried under an island that rises to ~3.5 m, so it was only visible out over the water. It now walks every cell centre, keeps the ones passing `TerrainGrid.IsBuildable`, and emits their boundaries into ONE `MeshTopology.Lines` mesh (a LineRenderer per line would be tens of thousands of GameObjects at island scale; `IndexFormat.UInt32` is required). Two things to preserve: boundaries sit on the **half**-offsets, because `GridSnap.SnapXZ` puts a building's *center* on a whole cell coordinate — the old overlay's lines ran through the centers, half a cell out of phase with placement; and the no-`TerrainGrid` branch still draws the legacy flat square so an un-set-up scene renders.
- **The grid toggle is F2, and must never be G.** `GridToggleHotkey` was on G, which `BuildPlacement` also uses for wall→gate conversion — both handlers ran on the same frame. It now also auto-shows during build mode (`buildPlacement.isPlacing`), with `manual`/`suppressed` flags layering the user's explicit toggle over the auto behavior; `suppressed` clears when build mode ends so the next B starts fresh.

### Runtime uGUI Gotchas (menus, crafting panel — anything built in code)

- **A `VerticalLayoutGroup` with `childControlHeight = false` IGNORES `LayoutElement.preferredHeight`.** It falls back to each child's raw `sizeDelta`, which for a code-created `RectTransform` is nothing like the intended height. `MenuBuilder.Column` shipped with `childControlHeight = false` while every element sized itself via `LayoutElement` — so all seven menu screens laid out at wrong heights and spilled out of their panels (2026-08-28). If you size children with `LayoutElement`, the group must control that axis.
- **A control's `targetGraphic` must have `raycastTarget = true` or the control is silently inert.** `MenuBuilder.SimpleImage` defaults it to false (correct for decoration), and the slider background and toggle box were built with it — neither had a single raycastable graphic, so the EventSystem never delivered them a pointer event and both were unclickable while looking fine. `SimpleImage(..., raycast: true)` for anything that IS the click surface. Buttons escaped this only because they use a bare `Image`, which defaults to true.
- **Panel heights are computed, not typed.** `MenuBuilder.FitPanelHeight(panel, column)` sizes a panel to its content at the end of `MenuScreens.Rebuild`. Hand-typed heights drift the moment a row is added — the Controls screen needed ~780px and was declared 640, dropping its last rows and the Back button outside the panel.
- **`[RuntimeInitializeOnLoadMethod]` fires ONCE per launch, not per scene load.** `PauseController` bootstrapped that way with no `DontDestroyOnLoad` (correct, per the stale-singleton rule), so it died with the menu scene on NEW GAME and Esc did nothing for the rest of the session. It now also subscribes to `SceneManager.sceneLoaded` and recreates itself per single-mode load. Any self-bootstrapping system that must exist in every scene needs both halves.
- **Settings that must survive a scene load are read at the point of effect, not pushed by `GameSettings.Apply()`.** Camera speed, edge pan, screen shake and the grid default are read live by `CameraController` / `CameraShake` / `GridToggleHotkey` (same pattern as `CraftedUpgrades`), so `Apply` never has to go find the new scene's components. They were all dead settings before this — saved to PlayerPrefs and read by nothing.
- **`GameSettings.Apply()` runs every frame of a slider drag**, so anything expensive in it must be change-guarded. `QualitySettings.SetQualityLevel(..., applyExpensiveChanges: true)` was being called per frame.
- **`KeyBindings.cs` is the single source of truth for every gameplay key (2026-08-30).** No script may hold a `KeyCode` field or literal any more — `BuildPlacement.startBuildKey`, `CameraController.rotateLeftKey/rotateRightKey` and `GridToggleHotkey.toggleKey` were all deleted, and `CameraController` no longer reads the `"Horizontal"`/`"Vertical"` input axes (those are fixed in the project settings, so pan would have been the one action visible on the Controls screen that couldn't be rebound). Two slots per action, so WASD+arrows and Delete+X need no special case. **Escape and the mouse buttons are reserved and must stay that way** — Escape is a back-out gesture five systems consume in order (`PauseController.ModeActive`), not a binding; `KeyBindings.IsReserved` also covers the debug keys.
- **A rebind prompt has to out-rank `PauseController`'s Escape handler.** `PauseController` is `[DefaultExecutionOrder(-50)]`, so it sees Escape *before* `MenuScreens` does and would back out of the Controls screen instead of cancelling the capture. It checks `MenuScreens.Instance.IsCapturingKey` first. Any future modal that wants Escape needs the same treatment.
- **Difficulty is a run snapshot, not a setting.** `Difficulty.BeginRun()` clones the selected preset in `MenuFlow.NewGame` **before** the scene loads, because `ResourceManager` applies the starting-resource multiplier in its own `Awake`. `MenuFlow.Restart` deliberately does NOT re-snapshot. Multipliers are read at the point of effect (`Enemy.Start`, `EnemySpawner.CalculateEnemyCount`, `DayNightCycle.Update`, `GameManager.Start`), never pushed.
- **Difficulty must never leak into a balance sweep.** `Difficulty.Active` returns the Normal preset whenever `SimHooks.Simulating`, or a difficulty saved in the developer's PlayerPrefs would silently skew every simulated run. `GameManager.Start` additionally guards `nightsToSurvive` behind `!SimHooks.Simulating` — the sweep sets that from `sceneLoaded`, which runs *before* Start, so an unguarded assignment would clobber the config's value back to 5.
- **`sceneLoaded` is ahead of every `Start` but behind every `Awake`.** `SimRunner.ConfigureScene` set `ResourceManager.startingWood/Food/Stone` there, but `ResourceManager` copies those into the live pool in `Awake` — so the three starting-resource knobs were **silently ignored by every sweep** until 2026-08-30. It now writes `rm.wood/food/stone` as well. Check which callback a component actually consumes a value in before "configuring" it.
- **A `VerticalLayoutGroup` row and its description are siblings, not nested.** `MenuBuilder.RowDescription` adds the muted explainer line to the *column*, right after the row, so a description can never squeeze the control it belongs to. Keep them to one line: the height is a fixed 20px, because TMP cannot report a wrapped height until after a layout pass and the panel is sized in the same frame it is built.
- **Restoring a `ScrollRect` position needs a forced layout first.** `ScrollRect` clamps `verticalNormalizedPosition` against the content height, and a `ContentSizeFitter` has not computed that height on the frame the rows are created — setting it any earlier silently resolves to 1 (the top). `MenuScreens.RestoreScroll` calls `Canvas.ForceUpdateCanvases()` + `LayoutRebuilder.ForceRebuildLayoutImmediate` first. Without it, every rebind scrolled the Controls list back to the top and the row the player just clicked left the screen.
- **A scroll viewport clips with `RectMask2D`, never a stencil `Mask` (2026-09-01).** `MenuBuilder.ScrollColumn` originally used `Mask` with `showMaskGraphic = false` over an `Image` at alpha `0.001`. A hidden `Mask` renders its graphic through an **alpha-clipped** material, and a uGUI vertex colour is quantised to a byte on its way into the mesh — `0.001 × 255` truncates to **0**, so `clip(a - 0.001)` discarded every mask pixel, the stencil was never written, and **every row inside the viewport was culled**. Symptom: the Options screen showed its title, its four tabs and its bottom buttons with a 380px void where the settings were, and the entire Controls keybinding list was blank. `RectMask2D` clips by rectangle with no graphic and no stencil, so the alpha is irrelevant.
- **The viewport `Image` is still required even under `RectMask2D` — for the raycast only.** The scroll wheel and drags reach the `ScrollRect` only through a raycastable graphic (same class as the slider background and toggle box: an invisible surface that IS the interaction surface must be raycastable). `Color.clear` is safe now: uGUI raycasts ignore alpha unless `alphaHitTestMinimumThreshold` is set. Children still win the raycast, so buttons inside the region keep working.
- **A setting read once at spawn can't be toggled mid-run.** Unit state labels are gated inside `FloatingText.LateUpdate` rather than at `CreateStateText` time, and `HealthBar.RefreshVisibility` is called every frame instead of only on health change, precisely so the Interface options apply to entities already on the field. If a new setting affects something created at spawn, decide deliberately which of the two it is.
- **Victory and defeat are `MenuScreens.Screen.GameOver` now, not scene panels (2026-08-31).** The old `VictoryDefeatUI` was hand-built uGUI under the gameplay Canvas whose Quit button called `Application.Quit` — in the editor that just stopped Play, and there was **no route back to the main menu from either screen at all**. `GameManager` no longer holds `victoryScreen`/`defeatScreen` refs; it calls `MenuScreens.Ensure().ShowGameOver(victory)`. `Tools > Island RTS > Menus > Remove Legacy Victory-Defeat Panels` deletes the dead scene objects (it walks the hierarchy rather than using `GameObject.Find`, which skips inactive objects — and those panels are inactive by definition).
- **The game-over screen must have no Back.** `MenuScreens.Back()` early-returns on `Screen.GameOver`. Dismissing it would strand the player on a frozen world with no UI: the game sits at `timeScale 0` and `PauseController.SetPaused(false)` deliberately refuses to touch timeScale while `isGameOver` is set.
- **`GameManager.ContinuePlaying` must clear `isGameOver` BEFORE closing the menu**, for that same reason — closing routes through `SetPaused(false)`, which no-ops while the flag is set, and the player would resume into a frozen game.
- **Anything that builds UI on game over needs a `SimHooks.Simulating` guard.** A sweep ends a game every few seconds; `MenuScreens.Ensure()` would build a canvas, an EventSystem and a screen of TMP labels each time in a headless process, and would then block the harness through `PauseController.BlockGameplayInput`. `GameManager.ShowEndScreen` is the guard.
- **`HealthBar.hideWhenFull` was deleted** — every prefab set it identically and no player could reach it, so it was a preference wearing a prefab field's clothes. It is `GameSettings.HealthBarMode` now (Always / When damaged / Never). `Health.hideWhenFull` is a *different* field (building HP text) and is untouched.

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
- **Ortho camera needs a NEGATIVE near clip (`nearClip` -100, set by `CameraController.Awake`).** When tilted low / zoomed out, ground at the bottom of the view sits up to ~20u BEHIND the camera plane — a positive near clip slices entities open ("split" cross-sections) and culls them entirely. Negative near is the standard ortho-RTS fix; depth is linear in ortho so precision is unaffected. The scene camera's serialized `near clip plane: 0.3` is dead data — Awake overrides it.
- **The negative near clip makes depth-based screen effects garbage at the bottom of the screen — SSAO is OFF because of it (2026-08-28).** With `nearClip -100`, the ground along the bottom edge of a zoomed-out view has NEGATIVE view-space depth. URP's `ScreenSpaceAmbientOcclusion` (Source: Depth) reconstructs view position from the depth buffer and produces garbage for those pixels, painting the bottom ~quarter of the screen dark — visible only past the zoom level where ground first falls behind the camera plane. Disabled in `Settings/PC_Renderer.asset` (`m_Active: 0`; Mobile_Renderer never had it), which also matches the Phase 10 Stage 4 note that SSAO stays only if it helps stylized reads — at Intensity 0.4 / Radius 0.3 on flat-shaded low-poly it contributed almost nothing. **Any future depth-sampling screen effect (SSAO, SSR, screen-space fog, depth-based outlines) will hit the same wall.** The real fix, if one is ever wanted, is to dolly the camera backwards along its forward axis as ortho size grows and use a small positive near clip (visually identical in ortho) — but the AudioListener rides on the camera, so it would have to move to a child that stays put.
- **URP shadow distance is driven from zoom by `CameraController.UpdateShadowDistance`, not by the asset's fixed value.** `PC_RPAsset`'s `m_ShadowDistance: 50` covers a zoomed-in view; at `maxOrthoSize 24` with a 30° tilt the furthest visible ground sits ~72u down the view axis, so most of the screen rendered with no shadows at all (the symptom was "distant trees have no shadows"). The controller now sets `shadowDistance = clamp(((camY + orthoSize * cos p) / sin p) * 1.25, 50, 160)` every frame. **It caches and restores the asset's original value in `OnDisable`** — the URP asset is a ScriptableObject on disk, and a Play-mode write to it persists and shows up as a git diff. Same reason it re-caches when `GraphicsSettings.currentRenderPipeline` changes (quality-level switch).
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
- **`MainIsland` overrides the campfire's `NavMeshObstacle` extents per-instance** (0.6/0.5/0.6). Prefab-level carve edits never reach the scene's campfire — change it on the scene instance or not at all. **Superseded by Opening Sequence Stage 1:** the setup tool applies the scene overrides (added `BaseBuilding`, carve extents, trigger flags) INTO `Campfire.prefab` and deletes the scene instance — after running it, the campfire is runtime-spawned and the prefab is the single source of truth.

## Key Conventions

- `CachedHealth` property on all units avoids per-frame GetComponent
- Wall/Gate events: `OnAnyWallDestroyed` / `OnAnyGateDestroyed` static events
- WallGrid: `WorldToGrid()`, `HasWallAt()`, `GetWallAt()` (not GetAt)
- ResourceNode scoring: `distance + (claimCount * 5f)`
- Wall scoring: `dist * (1 + attackers * 0.5f)`, gates at `0.3x` distance
- Public sound methods: `StartGatheringSoundPublic()`, `PlayAttackSoundPublic()`, etc.
- `StuckResolver` is a shared component (Worker, Warrior, Enemy all use it)
- **A `public float` on a unit script is DEAD DATA in the script -- the prefab value wins.** `Warrior.moveSpeed` read `3.5f` in code with the comment "faster than enemies" while `Warrior.prefab` serialized **2.5**, and `Enemy.moveSpeed` read `2f` while `Enemy.prefab` serialized **3** -- so warriors were the slowest unit on the field and could never close on the enemies they chased (found 2026-08-27, movement snap pass). Conversely, values assigned in `Start()` (`acceleration`, `angularSpeed`, `radius`, `baseOffset`) make the PREFAB the dead data. When tuning movement, change both and keep the comment honest, or you will tune a number nothing reads.
- Singleton/unique-object lookup is **`FindAnyObjectByType<T>()`**. `FindFirstObjectByType<T>()` is obsolete as of Unity 6000.5 (it relies on instance-ID ordering) and `FindObjectOfType` / `FindObjectsOfType` were obsoleted before that. `FindObjectsByType` for multi-object scans remains banned outright — use `X.ActiveList` (see ActiveRegistry Pattern)
- Nearest-alive target scans go through **`TargetingUtil.FindNearest`** (every unit/building implements `ITargetable`); target set/clear/alive-check through **`bb.SetTarget` / `bb.ClearTarget` / `bb.IsTargetAlive`**; carve-safe destinations and range checks through **`TargetingUtil.GetApproachPoint` / `EdgeDistance`**. Don't hand-roll new scans or approach-point code (Phase 6.25)
- Worker spacing knobs: `Worker.AgentRadius` (avoidance radius — the thing that actually spaces workers), `Worker.GatherStopDistance`, `ResourceNode.GatherRingRadius` (standing ring), and `gatherDistance` (arrival tolerance, floored at `AgentRadius + 0.25` by GatherExecutor). `deliveryDistance` is measured from the campfire collider **edge**

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
4. Opening: name your castaway in the popup, right-click to walk them ashore, press B to place the campfire (free, one-time). The character stays yours for the whole run; colonists come ashore on their own
5. Click campfire to assign workers, press B to build, recruit warriors before nightfall

## Controls

| Key | Action |
|-----|--------|
| WASD / Arrows | Pan camera |
| Q / E | Rotate camera |
| Mouse Wheel | Zoom |
| Middle Mouse (drag) | Tilt (vertical) / rotate (horizontal) camera |
| B | Enter build mode |
| 1-5 | Select building type (Hut, Wood Wall, Stone Wall, Watchtower, Workshop) |
| G | Convert wall to gate (in build mode, hover over wall) |
| R | Toggle L-path direction (wall mode) / Rotate building |
| Shift | Bresenham staircase wall path |
| Delete / X | Demolish mode (50% refund) |
| F2 | Build grid overlay (also auto-shows during build mode) |
| F3 | AI debug overlay (editor only) |
| F4 | Debug menu (editor + dev builds only) |
| Click campfire | Worker assignment UI |
| Right-click | Smart command for your character: fetch a pickup / deposit at the campfire / walk there |
| Space | Centre the camera on your character |
| B (opening) | Place the campfire (Esc / right-click cancels) |

Every gameplay key above is a **default**, not a fixed binding — `KeyBindings.cs` owns the map and *Options → Controls & Keybindings* rebinds it (two slots per action). Esc, the mouse buttons and F3/F4/F6/F7 are reserved and cannot be rebound.

**Full controls + the consolidated playtest checklist live in `docs/CONTROLS_AND_CHECKLIST.md`** (GitHub-readable). Keep both in sync when a binding changes.

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
- **⚠️ The editor setup steps must be run before anything is testable.** One menu item does all of them in dependency order: **`Tools > Island RTS > Setup Everything (In Order)`** (`Assets/Editor/FullSetup.cs`). It leaves `MainMenu` open, which is the real entry point; `Tools > Island RTS > Open Game Scene (MainIsland)` jumps straight to gameplay. The individual steps, in the order the master tool runs them (each is idempotent): 1) `Low-Poly Templates > Generate All Assets` 2) `Plumb Everything` 3) `Opening Sequence > Setup Opening Scene` 4) `Low-Poly Templates > Build Scatter Settings` (prop table → asset; props are scattered at runtime now) 5) `Terrain > Setup Terrain Scene` (creates the settings assets, wires the 7 band materials, removes the legacy scene scatter, snaps the wreck to the cove) 6) `Session Content > Setup Pickups + Workshop` 7) `Menus > Remove Legacy Victory-Defeat Panels` 8) `Menus > Setup Main Menu Scene`. **Order is load-bearing:** the opening tool consumes the generated art library, the terrain tool deletes the ocean frame the opening tool built and re-snaps the scattered props, and the menu tool replaces the open scene so it can only go last. See the Session 2026-08-26 entry in Phase History for the playtest checklist.
- **Terrain T1 needs its editor setup run + playtest:** run `Tools > Island RTS > Terrain > Setup Terrain Scene (T1)` (AFTER the Opening Sequence setup — it removes the Ground plane, the baked NavMesh, and the _Ocean frame, then snaps the wreck/scatter props to the generated island). See the Terrain T1 entry in Phase History.
- **Opening Sequence Stage 1 needs its editor setup run + playtest:** in Unity run `Tools > Island RTS > Opening Sequence > Setup Opening Scene`, then Play (see the Opening Sequence Stage 1 entry in Phase History). Until the menu item is run, the scene is untouched and the game starts the classic way.
- Playtest Phase 6.24 refactors + the Unity 6000.5.9f1 upgrade + Phase 6.25 targeting/spacing changes (see checklists in Phase History — the 6.25 list stacks on 6.24's).
- Run `Tools > Island RTS > Low-Poly Templates > Generate All Assets` (regenerates the Stage 2c simplified shapes), then `Plumb Everything` + `Scatter Environment Props`, re-bake the NavMesh, then playtest Phase 10 Stage 2a/2b/2c. **Run the Low-Poly steps BEFORE the Opening Sequence setup** — the setup tool consumes the art library (campfire mesh, Worker art prefab, LP materials).

**What's next (future phases):**
- Phase 7 (REVISED 2026-08-25): Jobless generalist colonists — colonists spawn without a job, wander, collect ground pickups (sticks/rocks carried to the campfire), and build/repair when sites exist; assigning a gathering job specializes them (replaces the dedicated Builder unit — see the revision note in the Phase 7 sketch). Plus building upgrades (hut -> house, campfire -> fortress), workshop, storage
- Phase 8: Worker night hide behavior, archer units
- Phase 9: Player character (Admiral), crafting, tech tree
- Phase 10: Visual overhaul — Stage 1 (post-processing + lighting presets) shipped; Stages 2-5 (asset swap, water shader, lighting bake) ahead
- Phase 11: Content polish, save/load, main menu
- Terrain System: **T1 (static shaped island + runtime NavMesh) implemented 2026-08-25**, pending setup run + playtest; T2 (placement flattening), T3 (waterline gameplay/shoreline spawns), T4 (random seed per run) ahead — see `TERRAIN_SYSTEM_PLAN.md`

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
| Workshop | 30W 20S | 150 |
| Warrior | 10W 15F | 75 |

- Starting resources: 100W, 50F, 0S, 0M (metal: gathered from ore nodes by miners; nothing costs it yet)
- Worker carry: 5 resources, 1/sec gather rate
- Enemies per night: `5 + (night - 1) * 2.25`, rounded → 5 / 7 / 10 / 12 / 14. Spawned 0.4s apart, so a wave arrives as one body rather than a trickle that warriors defeat in detail
- Warrior heals at campfire: 1.5 HP/sec (between waves only) — deliberately slow so damage carries across nights
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

### Phase 6.25 (Targeting Unification + Worker Spacing — ⚠️ PENDING PLAYTEST)

Refactored the three hand-rolled targeting implementations onto shared code, made workers pack tighter, and made every node/campfire interaction distance derived instead of hand-tuned (2026-08-24). Compile-verified vs 6000.5.9f1 Roslyn: 0 errors, 0 new warnings.

**New file `AI/Shared/TargetingUtil.cs`:**
- `ITargetable` interface (`CachedHealth` + `transform`) — implemented by `UnitBase<T>` (all units) and Hut, Watchtower, Wall, Gate, BaseBuilding. Entries whose Health hasn't been set up yet (spawned same frame) are skipped by scans — visible within one brain tick.
- `TargetingUtil.FindNearest<T>(list, from, maxRange, out dist)` — the one nearest-alive registry scan (sqrMagnitude, zero GC; maxRange ≤ 0 = unlimited). Replaces the bespoke scans in EnemyAttackExecutor (warriors/huts/towers/walls/gates), EngageEnemyExecutor, and EnemyPresence.
- `TargetingUtil.GetApproachPoint(from, target, collider)` / `EdgeDistance(from, target, collider)` — the ClosestPoint→SamplePosition carve-safe pattern, now in ONE place. Used by EnemyAttackExecutor, HealAtCampfireExecutor, and (new) ReturnToBaseExecutor.

**AIBlackboard target bookkeeping (one implementation):** `SetTarget(t, name)` (caches Health + Collider, returns true only on change — callers decide what to reset), `ClearTarget()`, `IsTargetAlive()` (the re-fetch-on-null version), `TargetEdgeDistance()` (float.MaxValue with no target). Enemy and warrior executors both use these; the warrior's duplicate IsTargetAlive/ClearTarget and the enemy's private SetTarget are gone.

**Warrior robustness fixes (small behavior deltas, all safer-direction):**
- Engage attack-range checks now use collider **edge** distance (`bb.TargetEdgeDistance`) instead of enemy center — consistent with enemies; effective reach grows by the enemy capsule radius (0.2).
- Engage's per-executor `Dictionary<Enemy, NavMeshAgent>` deleted — uses `Enemy.CachedAgent`.
- Engage's null-Health enemies are now skipped (was "assume alive"), matching every other scan.
- Retreat routed through `AINavHelper.TrySetDestination` (was raw `SetDestination` — the last raw call site); Retreat/DefendWall/Intercept/Engage all honor the bool return with a retry-next-frame flag, closing their ghost-moving windows.

**Worker spacing:** `Worker.AgentRadius = 0.3f` const (was 0.5 — the NavMeshAgent avoidance radius is what spaces workers; the CapsuleCollider is click-hitbox only). `ResourceNode.WorkerSlotArc` now derives from it (`AgentRadius * 2 + 0.25` = 0.85, was hardcoded 1.25), so per-node capacity scales up automatically (~2-3 more workers per tree). Worker.prefab NavMeshAgent radius updated to match, and **duplicate CapsuleColliders were removed from the Worker AND Warrior prefab roots** (each carried two identical capsules — plumber slip; Enemy.prefab was clean).

**Node interaction distance (anti-orbit guarantee):** `gatherDistance` (arrival tolerance) 1.0 → 0.6 in script + prefab, and GatherExecutor floors it at `AgentRadius + 0.25` — the tolerance always exceeds how far ORCA can hold a worker off its on-NavMesh gather point, so a worker can never be asked to reach an unreachable spot and circle a node. Arrival now accepts EITHER within-tolerance of the gather point OR within `GatherRingRadius + tolerance` of the node center (worker pushed to a different ring spot still counts). `ResourceNode.GatherRingRadius` is public — the single shared standing-ring definition.

**Campfire delivery is edge-based:** `deliveryDistance` semantics changed from "distance to campfire center" to "distance to campfire collider **edge**" (script default 3.5 → 1.5; prefab already said 1.5, which under center-based semantics was *inside* the 2×2 collider — deliveries were only succeeding via the timer fallbacks). ReturnToBaseExecutor now uses `TargetingUtil.EdgeDistance` for all five delivery checks and `GetApproachPoint` for the dropoff destination (replacing the ring + 8-direction fallback). All timer fallbacks kept.

**Playtest checklist (stacks on the 6.24 list):** workers pack tighter around nodes without orbiting or shoving; ~6-8 fit around a tree; workers walk right up to the campfire and deliver promptly (no 3s fallback pauses — watch carry counts hit 0 next to the fire, not 2m away); warriors still engage/disengage cleanly at range (edge-distance change); enemies unchanged (behavior-preserving refactor); Retreat/Intercept/DefendWall still move immediately when chosen.

**Post-playtest fix (2026-08-24 evening):** the user's first Play session (still on pre-6.25 binaries) logged repeated NREs from all three unit types: `StuckResolver.UpdateMoving()` fires the unit's `onStuckReset` callback **mid-call**, which nulls `bb.targetResource` (worker) / `bb.currentTarget` (warrior, enemy) — and the executor code immediately after the call dereferenced them. Fixed by honoring `UpdateMoving()`'s bool return in GatherExecutor / EngageEnemyExecutor / EnemyAttackExecutor: on a stuck reset, `return` for that tick (the callback already ForceReeval'd). **Gotcha: any executor that calls `UpdateMoving()` must either early-return when it reports a reset or re-null-check every blackboard field the unit's onStuckReset callback touches.** The warrior/enemy cases were also independently defused by `TargetEdgeDistance()` returning float.MaxValue on a null target.

### Phase 6.26 (Worker Crowd Interaction — ⚠️ PENDING PLAYTEST)

Fixes worker-vs-worker shoving/jams (campfire traffic, standers displaced) via state-based ORCA roles (2026-08-24):

- **Avoidance role follows worker state.** `Worker.SetStationaryAvoidance` (priority 10 = max-importance; lower number = more important in Unity) whenever a worker stands still — gathering, idle, sheltering at a hut in flee. A stander has no path so it *can't* yield; making it max-importance turns it into "furniture" movers route around instead of shoving. `Worker.RollMovingAvoidance` (`Random.Range(30, 70)`) on every moving errand: Gather OnEnter/`TryPickupNewResource`/`StartHeadingToBase`, Return OnEnter, Flee OnEnter, Idle OnExit. **Gotcha: every executor that stops a worker must set stationary priority, and every one that moves it must re-roll — a stale priority-10 mover plows through everything.**
- **Rubber band on gather spots.** `GatherExecutor` anchors the worker's position when it registers at a node; if the crowd nudges it > 0.5 (`RubberBandSlack`) off the anchor during `Gathering`, it walks back (gathering continues — the ring stays in range) and `ResetPath()`s again within 0.15 (`RubberBandArrive`). Displaced gatherers no longer drift away from their spot permanently.
- **Worker avoidance quality Med → High** (`Worker.Start`). The old "reduced for performance with many walls" rationale was wrong — walls are carving obstacles, not avoidance agents; ORCA cost scales with *agent* count and ~10 workers is cheap. High predicts crossings earlier, killing most of the head-on side-step dance.

Playtest (stacks on 6.24/6.25): deliverers flow around idlers at the campfire instead of jamming; gatherers hold their spots and spring back if bumped; two workers meeting head-on pass cleanly; fleeing workers still pile into huts without deadlock.

### Phase 7 (Planned): Builders — Design Sketch

Decided 2026-08-24 (user picked all three recommended options). Builders join Phase 7 alongside building upgrades / workshop / storage.

> ⚠️ **REVISED 2026-08-25 (during Opening Sequence planning):** decision 1 is replaced — there is **no dedicated Builder unit**. Colonists spawn **jobless** and act as generalists: they wander, pick up small ground items (sticks/rocks — future slice, delivered to the campfire like normal gathering), and automatically build/repair when construction sites or damaged buildings exist. Assigning a colonist to a gathering job specializes them; unassigned = builder/collector. Decisions 2 (construction requires labor) and 3 (repair costs resources) stand, executed by jobless colonists. The implementation-shape notes below still apply to the jobless-colonist AI (Build/Repair/Flee/Idle actions, edge-distance interactions, AINavHelper, crowd avoidance roles).

**Core decisions (as originally locked — item 1 superseded by the revision above):**
1. ~~**Dedicated Builder unit**~~ — ~~fourth `UnitBase<Builder>` (CRTP keeps its own `ActiveRegistry`), recruited at the campfire like warriors. NOT a worker job assignment.~~ → **Jobless generalist colonists** (see revision note).
2. **Construction requires a builder.** `ConstructionSite.progress` advances only while ≥1 builder is working it — the current 5s auto-complete becomes builder labor. Placement becomes logistics; mid-siege walls need protecting.
3. **Repair costs a resource fraction** — proportional to HP restored, ~25% of build cost for a full repair, drawn incrementally as HP ticks up; repair pauses (doesn't cancel) when the pool runs dry.

**Implementation shape (leverage existing patterns):**
- AI action set: **Build** (nearest `ConstructionSite` via its existing ActiveRegistry), **Repair** (scan building registries for `Health < max`), **Flee** (reuse worker flee logic — builders are civilians), **Idle**. All the Utility AI gotchas apply: multiplicative considerations, momentum exit conditions, full transition table before shipping.
- All building interactions are **edge-distance** (`TargetingUtil.GetApproachPoint` / `EdgeDistance`) — construction sites and damaged buildings sit on/next to carving obstacles, same as every other building interaction.
- Movement through `AINavHelper.TrySetDestination` honoring the bool; state-based avoidance priority (stationary-while-building = `SetStationaryAvoidance`-style, re-roll on errand) — same crowd pattern as workers (Phase 6.26). Consider promoting the worker helpers to `UnitBase` at that point.
- Housing: builders occupy population like workers (single-owner bookkeeping — register through the same `NotifyWorkerRemoved`-style single path, mind the Phase 6.23 lessons).
- Art: meeple + tool accessory (hammer? tool belt) via `LowPolyAssetGenerator` + a `LowPolyPlumber` table row — an asset not in the plumber table is invisible in-game.
- UI: recruit button beside the warrior's; construction sites show "awaiting builder" state when no builder is en route.

**Balance starting points (tune in playtest):** cost ~15W 10F, HP ~50 (civilian), build rate such that a wall takes ~8-10s of labor (was 5s auto), repair ~5 HP/sec. Multiple builders on one site: linear stack capped at 2-3.

**Open questions (settle at implementation):** do builders keep working at night or auto-flee; do enemies target builders like workers (currently enemies prefer warriors/buildings); is there a builder cap; does demolish-refund interact with partially-repaired HP.

**Soft-lock guard:** with zero builders alive, sites wait indefinitely (they don't decay); demolish still refunds 50%. Surface a warning banner if sites exist but no builder does.

### Terrain System (Planned): Dynamic Island Terrain — Design Sketch

Decided 2026-08-25 (user picked all four recommended options). Full spec in `TERRAIN_SYSTEM_PLAN.md` at repo root — that file is the source of truth; this is a summary.

**Locked decisions:** (1) random island per run (seeded generation at game start); (2) gentle tactical — hills walkable, water impassable, occasional steep slopes as chokepoints, island connectivity guaranteed; (3) enemies spawn in a shallow wading band offshore and emerge from the water; (4) placement smoothing makes almost all land placeable — reject only water-overlap/cliff footprints.

**Architecture:** custom chunked heightmap mesh (NOT Unity Terrain — smooth shading fights the Phase 10 flat-shaded look). `TerrainGrid` singleton: height field at 1m spacing, sea level y=0, 16×16-quad flat-shaded chunks with vertex-color height/slope bands (beaches free), MeshCollider per chunk, `SampleHeight`/`FlattenArea` API, dirty-chunk rebuilds only. `IslandGenerator`: radial falloff × domain-warped noise, amplitude budget ~4m (orthographic occlusion readability), campfire site flattened at generation, validity via real `NavMesh.CalculatePath` tests + seed reroll. NavMesh moves from the scene bake (must be DELETED — double-navmesh otherwise) to a runtime `NavMeshSurface` with async `UpdateNavMesh` after each flatten; wall lines batch N placements into one update. Water = flat plane at y=0 (Stage 3 shader mounts there later); deep water NotWalkable via `NavMeshModifierVolume` below y=−0.4, the −0.4..0 band is the walkable wading band.

**Biggest break risk:** startup ordering — everything currently assumes a NavMesh exists at frame 0; terrain gen + NavMesh build must complete before campfire/spawners/units. The AI layer itself is already height-safe (`SamplePosition` everywhere).

**Staging:** T1 fixed-seed shaped world + runtime NavMesh; T2 flatten-on-placement; T3 waterline gameplay + shoreline spawns; T4 random seed per run + terrain-aware campfire/resources. Each stage playable.

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

**Moonlight follow-up (2026-08-24 — ⚠️ pending playtest):** night was near-black for two stacked reasons: (1) the sun sweep points **below the horizon** all night (angle 180–270 / -90–0), so `NightPreset.sunIntensity` never reached the ground — night was 100% ambient; (2) the ambient values were near-black. Fix: `DayNightCycle` now holds the directional light at a fixed **moon pose** (`moonElevation` 45 / `moonYaw` 210, inspector-tunable) whenever `dayProgress < 1`, `Slerp`-blended through the existing dawn/dusk windows so there's no pop. `LightingPreset` gained `shadowStrength` (lerped onto `sunLight.shadowStrength`): Day 1.0, Night 0.5 for soft moon shadows. `NightPreset` retuned for readable-gameplay night: sun `(0.55, 0.65, 0.9)` @ 0.5, ambient sky `(0.12, 0.16, 0.32)` / equator `(0.07, 0.09, 0.18)` / ground `(0.03, 0.04, 0.09)` @ 0.8. Gotcha: when adding a serialized field to `LightingPreset`, write it into **both** .asset files explicitly — a missing YAML key deserializes as 0, not the C# default (a DayPreset missing `shadowStrength: 1` would kill daytime shadows). Playtest: night should read as cool blue moonlight with soft shadows falling opposite the day direction, dawn/dusk light swing smooth, campfire bloom still pops.

**Outstanding (intentionally deferred):**
- Post-FX values currently conservative (Sat 2, Contrast 6, Temp 10, Vignette 0.12) vs spec values (5 / 10 / 15 / 0.25). Stylistic choice — can push closer to spec for more drama later.
- Fog hooks not in `LightingPreset` SO yet — wait until Stage 4 so we know what shape they need.
- Water shader properties not in `LightingPreset` yet — Stage 3 dependency.
- Sun rotation (`minSunElevation`, replacing the old `sunriseAngle`/`sunsetAngle` sweep 2026-08-25) intentionally NOT in the preset; rotation is a continuous time-of-day mechanic, the preset is for discrete day-state vs night-state values. The sun now sweeps `minSunElevation` (25°) → overhead → `180 − minSunElevation`, never at grazing elevation — long shadows racing across the ground at dawn was the visible symptom. `transitionWidth` (0.10, inspector-tunable) sets the dawn/dusk blend width; AIWorldState keeps its own fixed 0.05 dayProgress windows (AI behavior deliberately untouched by the visual widening).

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

### Opening Sequence Stage 1 (Survivor Landing — ⚠️ NEEDS EDITOR SETUP RUN + PLAYTEST)

The game now opens with the story beat instead of a pre-placed base (2026-08-25): a lone survivor stands in the shallows beside his shipwreck, the player right-clicks him ashore, presses **B** to place the campfire (free, one-time, must be within 6u of the survivor), and the survivor walks to the fire and settles in as the colony's first worker (auto-assigned Wood via the normal `AssignWorker` path). Then normal gameplay begins. Playable from second one — no cutscene; a cinematic camera pass can layer on later. Compile-verified vs 6000.5.9f1 Roslyn (93 scripts, with and without `UNITY_EDITOR`): **0 errors, 0 warnings.**

**User decisions (locked via 4-option questions):** opening sequence first (before pickups/upgrades); playable from the water (no scripted cutscene); jobless colonists replace the dedicated Phase 7 Builder (see the Phase 7 revision note); ground pickups will be carried to the campfire (future slice).

**New runtime files:**
- `GameStartController.cs` — phase machine `Landing → PlacingCampfire → Settling → Colony` (`GamePhase` enum). Statics: `Phase` (returns `Colony` when no controller exists — old scenes keep working), `IntroInProgress`, `OnColonyStarted` event. During the intro it disables `BuildPlacement`, holds `DayNightCycle.clockPaused = true` (night 1 can never arrive early), spawns the survivor, shifts the camera (XZ view-center delta — rotation-agnostic, CameraShake-safe), and shows a runtime-created TMP hint overlay (no scene wiring). Campfire placement is a bespoke mini-placer (grid-snap + lerp follow + `RendererTint` validity tint, same feel as `GhostPlacer`) — the campfire is deliberately NOT in `BuildingType`/`BuildingDatabase`, so the build menu can never produce a second one. Validity: within `maxPlaceDistance` (6) of the survivor, |x|,|z| ≤ `dryLandExtent` (42), on the NavMesh, ≥3u from every ResourceNode. On placement it wires `BaseBuilding.workerUI` + `GameManager.campfire` (scene-only refs a prefab can't carry), sends the survivor to the fire via `TargetingUtil.GetApproachPoint` and waits on `EdgeDistance` (carve-safe, 8s failsafe), then unpauses the clock, re-enables BuildPlacement, assigns the first worker, destroys the survivor. `skipIntro` toggle replicates the classic start exactly (campfire spawned in `Awake` at a configurable position so every `Start()`-time lookup finds it, no survivor, clock running).
- `Survivor.cs` — minimal click-move castaway: NavMeshAgent (worker locomotion values), `MoveTo` with `NavMesh.SamplePosition` snap (4u — clicks in the shallows land on walkable ground), destinations through `AINavHelper.TrySetDestination` honoring the bool with retry-next-frame. Deliberately NO Health / AI / registry — nothing targets or counts him.

**Edits:** `DayNightCycle` gains `clockPaused` (freezes time accumulation; lighting still updates every frame — intro is lit at dawn, `currentTimeOfDay` 0.25). `ResourceSpawner` quietly uses origin as spawn center when `GameStartController.IntroInProgress` (campfire intentionally absent — the placer keeps clearance from nodes instead of the reverse; the old warning still fires when there's no controller at all).

**New editor tool `Assets/Editor/OpeningSequenceSetup.cs`** — `Tools > Island RTS > Opening Sequence > Setup Opening Scene`, idempotent, nothing changes until it's run:
1. **Campfire → runtime prefab.** Records the scene campfire's `workerUI` ref, nulls it, `PrefabUtility.ApplyPrefabInstance` (bakes the scene-added `BaseBuilding` + NavMeshObstacle carve extents 0.6/0.5/0.6 + trigger flags INTO `Campfire.prefab`), deletes the scene instance. This retires the old "scene overrides the campfire carve" gotcha — the prefab is now the single source of truth.
2. Builds `CampfireGhost.prefab` (art mesh on ROOT renderer + one `Mat_Ghostbuilding` per submesh — house ghost pattern) and `Survivor.prefab` (agent + `Survivor` + nested art `Units/Worker.prefab` as `Model` child).
3. Creates `Mat_Water` (URP Lit transparent blue — placeholder until the Stage 3 water shader) and an `_Ocean` frame: 4 collider-less quads at y=0.12, inner edge ±44 — the outer ~6u of the 100×100 ground reads as a shallow wading band. NOT static (water stays real-time).
4. Builds `_Shipwreck` at (-47, 0, 3) (west shore, half in the wading band): broken hull + mast + sail from primitives with LP materials, plus Crate/Barrel/DriftwoodLog art prefab instances. No colliders (never blocks pathing/clicks/bake), scatter-style static flags.
5. Creates the wired `GameStart` object (+`SurvivorSpawn` child at (-45, 0, 2)), clears the dangling `GameManager.campfire` scene ref, saves the scene.

**Startup contract (the load-bearing bit):** everything that used to assume a campfire at frame 0 now tolerates its absence — `GameManager.Update` already null-guarded (controller assigns on placement), `AIWorldState.UpdateCampfireState` polls `BaseBuilding.ActiveList` every frame (picks the spawned one up automatically), `ResourceSpawner` handled above, `EnemySpawner`/warrior-heal are event/registry driven and can't fire before the clock runs. **If a future system caches the campfire in `Start()`, it must subscribe to `GameStartController.OnColonyStarted` or poll the registry.**

**Gotcha (compile tooling):** the Roslyn verify recipe breaks on the editor install path containing a space (`D:\Programs\unity editor\...`) — `-r:` lines in the .rsp must be quoted or csc parses the post-space fragment as a source file (CS2001 spam).

**Playtest checklist (after running the setup menu item — run `Low-Poly Templates > Generate All Assets` + `Plumb Everything` FIRST, the setup tool consumes the art library):** camera opens framed on the survivor at the west-shore wreck standing in shallow water; right-click moves him (into and out of the wading band); day/night clock frozen at dawn until placement (check the Day/Time debug label holds); B shows the campfire ghost — red in water/far from survivor/on a resource node, green on clear dry land near him; Esc/right-click cancels; click places → placement sound, survivor walks to the fire, ~1s later he despawns and a wood worker spawns (population 1/3), clock starts, hints fade; clicking the campfire opens the assignment panel; B now opens the normal build menu; night 1 raid targets the placed campfire; restart (defeat → restart button) replays the intro cleanly. Also verify the classic path: tick `skipIntro` on `GameStart` → campfire at origin from frame 0, no survivor, no regressions. Known cosmetics: the survivor uses unmodified Worker art; the wreck is primitives + props (a bespoke LP shipwreck asset can replace it later).

**Follow-up slices agreed with the user (in order):** ground pickups (sticks/rocks scattered on the island, small wood/stone value, carried to the campfire), jobless-colonist system (wander + collect pickups + build/repair — the Phase 7 revision), hut → house upgrades (framework for building tiers).

### Debug Menu (F4 — editor + dev builds)

Playtest cheat menu added 2026-08-25 (compile-verified both with `UNITY_EDITOR` and with no defines: 0 errors, 0 new warnings). Whole file `#if UNITY_EDITOR || DEVELOPMENT_BUILD` — release builds ship without it. Self-bootstraps via `[RuntimeInitializeOnLoadMethod]` (no scene object to wire) and is the ONE deliberate exception to the no-`DontDestroyOnLoad` rule: it holds no game state — every action looks up live singletons/registries at click time, so nothing stale survives a restart (and steppers persisting is a feature). IMGUI/GUILayout on the LEFT edge (F3 AI overlay owns the right); GUILayout's per-frame GC only costs while the menu is open.

**Sections:** live status (day/time/phase/speed, pop/housing, warriors, enemies); resources (+100/+1000 per type, +1000 all, zero all — writes `ResourceManager`'s public fields directly for zeroing); **quick-start colony** — steppers for huts / wood / food / stone workers / warriors (defaults 2/4/2/1/3), one button grants +1000 each resource, force-finishes the intro if it's running, rings huts around the campfire, then assigns workers and recruits warriors; time (skip to night `t=0.76` / skip to day `t=0.26`, clock-paused toggle, 1x/2x/4x `Time.timeScale` — speed disabled while game over so it can't fight the `timeScale=0` pause); cheats (spawn enemy wave — disabled until a campfire exists so the wave has a target; kill all enemies via `TakeDamage(999999)` so the real death path runs; heal everything friendly via the `ITargetable.CachedHealth` registries; finish all construction via `AddProgress(1f)`).

**Debug hooks added (both `#if`-wrapped):** `GameStartController.DebugForceColonyStart()` — instantly finishes the opening (campfire at the survivor's position if he's on dry land, else the skipIntro position), parking in `Settling` with `settleDeadline = float.MaxValue` and yielding one frame before `StartColony()`; `EnemySpawner.DebugSpawnWave()` — runs `StartSpawning()` with `currentNight` temporarily floored at 1, restored immediately (count is computed synchronously; the Invokes only stagger instantiation), so real night scaling is unaffected.

**Gotchas encoded in the flow (frame-yield ordering):** `BaseBuilding.Start` registers the campfire's housing and `Hut.Start` registers hut housing — `AssignWorker` silently no-ops without housing, so the quick-start coroutine yields a frame after spawning the campfire AND after spawning huts before assigning workers. Skip-to-day must increment `currentDay` when crossing the midnight wrap (`t > 0.75`) but not from early morning (`t < 0.25`), or the survived-nights count drifts. Runtime-spawned huts need `layer = "Buildings"` (normally set by BuildPlacement/ConstructionSite).

**Skip-to-night note:** a wave spawned by "Skip to Night" or `DebugSpawnWave` despawns at the next OnDayStart, like any night wave.

Playtest with everything else: F4 opens/closes; quick-start from inside the intro lands a full working base (campfire + huts + workers gathering + warriors patrolling); spawn wave → warriors engage; kill-all clears the wave and stats count the kills; 4x speed doesn't break AI (eval throttles are frame-based — watch for units evaluating "slower" relative to game time at 4x).

### Terrain System T1 (Shaped Island — ⚠️ NEEDS EDITOR SETUP RUN + PLAYTEST)

Stage T1 of `TERRAIN_SYSTEM_PLAN.md` implemented 2026-08-25 (compile-verified both configs: 0 errors, 0 warnings). The flat 100×100 Ground plane becomes a procedurally generated island. Fixed seed (T1) — every run is the same island until T4.

**New runtime files (`Assets/Scripts/Terrain/`):**
- `IslandGenerator.cs` — pure/deterministic heightfield: radial falloff × domain-warped coastline (±8 m wobble) × 3 Perlin octaves, amplitude ≤3.5 m (ortho-camera readability budget), seabed to −2. Public world anchors baked in post-noise via `FlattenDisc`: a flat disc at the origin (campfire site / classic start) and a **landing cove** at the shipwreck (−46, 2): shallow −0.25 shelf + 0.6 beach ramp inland, so the opening sequence works on the shaped world. `FlattenDisc` is the core op T2's `FlattenArea` will build on. Being pure statics, the editor tool generates the SAME field to snap scene props — that's why `Date`/scene state must never leak into generation.
- `TerrainGrid.cs` — `[DefaultExecutionOrder(-100)]` singleton; does EVERYTHING in `Awake` (generate → chunk meshes → water plane → deep-water volume → `NavMeshSurface.BuildNavMesh()`), so every existing `Start()`-time system finds a finished world + live NavMesh — the plan's #1 break risk (startup ordering) is closed without touching any other script's ordering. 101×101 verts at 1 m; 16×16-quad chunks with per-triangle duplicated verts (hard flat shading), checkerboard-alternating quad diagonals, MeshCollider per chunk, on the Default layer so `BuildPlacement.groundLayer` raycasts work unmodified. **No custom shader:** each triangle is binned into one of 3 submeshes — LP_Sand (beach + all seabed, h<0.45), LP_GrassGreen, LP_RockMid (land faces steeper than ~37°) — crisp low-poly facet banding from existing materials. API: `SampleHeight` (bilinear), `SlopeAt`, `IsLand`, `IsShallow`, `IsBuildable` (h>0.15 ∧ slope<0.55), static `SampleField` (shared with editor tools), `UpdateNavMeshAsync()` (T2 hook). Water: 320×320 plane at y=0, Mat_Water, collider-less, own root (never a NavMeshSurface child), never static. Deep water: runtime `NavMeshModifierVolume` below y=−0.4 → NotWalkable; −0.4..0 is the walkable wading band. **This is a T3 item pulled into T1 deliberately** — without it the NavMesh covers the seabed and units walk across the ocean. `NavMeshSurface` is configured `CollectObjects.Children` + `PhysicsColliders` so ONLY chunk colliders + the modifier volume feed the bake (never buildings/resource nodes/water).

**New editor tool `Assets/Editor/TerrainSetup.cs`** — `Tools > Island RTS > Terrain > Setup Terrain Scene (T1)`, idempotent, run AFTER the Opening Sequence setup: deletes the Ground plane (its old NavMeshSurface rides along) + the baked `NavMesh-Ground.asset` (leftover bake = double-navmesh ghost of the flat world) + the `_Ocean` quad frame (real water plane is runtime now); creates the wired `Terrain` object; snaps the `_Shipwreck` ROOT onto the cove shelf and every `_LowPolyScatter` prop onto the island surface (absolute y from the same generated field → idempotent; underwater props are deleted and counted). `OpeningSequenceSetup.BuildOcean` now skips itself when a TerrainGrid exists, so re-running the opening setup can't resurrect the frame.

**Flat-y=0 call sites made terrain-aware (all guarded `TerrainGrid.Instance == null` → exact legacy behavior, so the un-setup scene still runs):**
- `BuildPlacement` — new `GroundYAt` helper; `GetSnappedMousePosition` y = terrain + placementHeight (semantics now "offset above the ground here"; hut/tower data already 0). Math-plane fallback kept for off-map rays.
- `GhostPlacer` — validity += `IsBuildable` (no huts in the water / on cliffs).
- `WallLinePlacer` — cursor/line ghosts at terrain + 0.02; new `CellBlocked` (occupied ∨ !IsBuildable) drives ghost tint, validCount, cost, and the confirm filter; sites spawn at terrain + placementHeight.
- `WallConnector` — self-snap y is now terrain + Y_OFFSET (sampled at the snapped cell).
- `EnemySpawner` — ring positions get terrain height + `NavMesh.SamplePosition(8f)` snap.
- `ResourceSpawner` — `GroundY`/`IsTerrainOk` (h>0.15 ∧ slope<0.55) helpers; every candidate path (clusters, scattered, generic, both respawn paths, cluster centers) sets y from terrain and rejects water/cliffs.
- `GameStartController` — campfire ghost/spawn y from terrain; `IsValidCampfireSpot` and the debug force-start use `IsBuildable` instead of the ±42 dry-land box; `RaycastGround` raycasts Default-layer physics first (a y=0 math plane offsets clicks on hills), plane fallback kept.
- `DebugMenu` — quick-start hut ring uses `IsBuildable` + terrain y.
- `BaseBuilding.GetValidSpawnPosition` — **removed the legacy `pos.y = 1f`** after `NavMesh.SamplePosition`: hit.position IS the right height; +1 was a flat-world/center-pivot relic that floated base-pivot units and would bury them under terrain above 1 m.

**T1 known-accepted rough edges (T2 fixes):** buildings on slopes clip into/overhang the ground slightly (no `FlattenArea` yet — validity just rejects steep spots); wall lines follow terrain per-cell with small steps; `GridOverlay` (off by default) still renders flat. Watch for: worker/warrior interaction ranges are 3D distances, so slopes inflate them slightly (tolerances have margin); campfire placement is limited to gentle ground — the cove ramp and origin flat guarantee valid spots on the fixed seed.

**Playtest checklist (after running the Terrain setup menu item):** console shows one "TerrainGrid: island generated (seed …) + NavMesh built in … ms" line and no errors; the world is an island — beach ring, rolling green interior, rock facets on steep bits, ocean to the horizon; survivor starts wading in the cove and walks up the beach ramp; campfire ghost red in water/on slopes, green on the flats; colony loop (gather/deliver/build/walls/demolish) works on hills; night wave spawns on land at the ring and pathes to the campfire; units never walk on water beyond the wading band; F4 quick-start colony lands huts on buildable ground; restart regenerates the identical island; ~60 fps held (chunk meshing is ~40k tris total).


### Session 2026-08-26 (Island 150×150 + Node Carving + T2 Flatten + Garrison Flee + Pickups + Workshop — ⚠️ NEEDS EDITOR SETUP RE-RUNS + PLAYTEST)

User-directed session (4-option questions locked: red lines/ghost = terrain clipping; flee = garrison in huts; island = 1.5×; crafting = workshop + recipes). Compile-verified vs 6000.5.9f1 Roslyn, runtime pass AND editor pass: **0 errors, 0 new warnings**.

**Island 150×150 (was 100×100).** `TerrainGrid.VertsPerSide` 151, `IslandGenerator.IslandRadius` 72, campfire flat disc 8/10, cove/ramp anchors (-70,3)/(-58,3), wreck (-71,0,4), survivor spawn (-69,0,3), water plane 480², deep-water volume 500², legacy WaterInner/Outer 66/105, dryLandExtent 63 everywhere. Scene values updated in MainIsland.unity: ResourceSpawner area ±70 with trees 220 / bushes 110 / rocks 110, clusters 9 (r9), scattered 35; EnemySpawner.spawnDistance 45; camera maxOrthoSize 24. LowPolyScatter: bands scaled to radius 70, counts ~1.7×, and grounding switched from physics raycast to **sampling the IslandGenerator heightfield** (chunk colliders only exist in Play mode — the raycast found nothing once the Ground plane was deleted; seed read from the scene TerrainGrid). Fixed a latent TerrainSetup bug: scatter snapping moved the per-prefab GROUP roots, not the props (props live one level down).

**Pathing: resource nodes carve now.** See the new Utility AI gotcha — this is the fix for units slowing/rubbing on trees and enemies freezing behind trunks while chasing warriors. Trunk-tight radii (0.45/0.45/0.5), `carveOnlyStationary`, GatherRingRadius = radius + 0.55, depletion shrink moved to the Model child. `GetGatherPoint` already SamplePosition-snaps, so ring points self-heal to the carve hole edge.

**T2 flatten-on-placement pulled forward.** `TerrainGrid` now keeps its chunk objects, exposes `FlattenArea(center, radius, blend)` (FlattenDisc on the heightfield → rebuild touched chunks → `UpdateNavMesh` async). Wired into single-building placement, campfire spawn, and the F4 quick-start ring. Fixes buildings/ghosts clipping into slopes and ghost-vs-placed height mismatch. Walls still follow terrain per-cell (deliberate).

**No-build red lines follow the terrain.** NoBuildZoneRenderer subdivides merged-perimeter edges (~1m steps) and drapes every point at `GroundYAt + 0.08`; individual/ghost zone borders are world-space draped squares (8 samples/side), re-draped as the ghost moves.

**Flee → garrison in huts** (user choice). FleeToHutExecutor rewritten; `Worker.SetGarrisoned` added. Old flee had a real bug: hut destinations targeted carved centers. Details in the new gotcha.

**Ground pickups (sticks + stones).** `GroundPickup` (registry, claimedBy, instant-carry Collect), `PickupSpawner` (26 sticks / 16 stones on land + NavMesh, trickle respawn every 18s), `PickupAvailability` consideration + `CollectPickupExecutor`, new "Pickup" worker action (basePriority 1.1, momentum 0.15). Stick = +3 wood carry, stone chunk = +3 stone; wood/stone workers only.

**Workshop + crafting.** `BuildingType.Workshop` (build key 5), `Workshop.cs` (ITargetable + registry; enemies target it via the extended `FindNearestReachableBuilding`), `CraftedUpgrades` (static one-time upgrades read at point-of-effect: Sharpened Tools +30% gather, Sturdy Scaffolds +50% build speed, Forged Blades +30% warrior damage), `CraftingUI` (runtime-built uGUI side panel, zero scene wiring, opened by clicking the workshop). Hooks: GatherExecutor rate, ConstructionSite.Update, EngageEnemyExecutor damage. 30W 20S, HP 150. `NewContentSetup.cs` editor tool builds Stick/StonePickup/Workshop/WorkshopGhost prefabs + WorkshopData.asset (borrows Hut's construction-site prefab), registers it in the scene BuildingDatabase, and creates the wired `_PickupSpawner`.

**Trees: taller + shade variants.** BroadleafTree heights 4.5–5.8 (was 3.4–3.8), five variants (Tree, B olive, C, D deep-green, E olive) via two new canopy palette trios (FrondOlive*/FrondDeep*). TreeVariance now stores **art prefab** variants and copies mesh + materials (mesh-only swap couldn't change palettes); plumber wires `VariantPrefabs` and the tree click hitbox grew to 2.6×5.0.

**Editor run order (nothing applies until these run, in order):** `Tools > Island RTS > Setup Everything (In Order)` runs the whole chain — Generate All Assets → Plumb Everything → Setup Opening Scene → Scatter Environment Props → Setup Terrain Scene (T1) → Setup Pickups + Workshop → Setup Main Menu Scene — then leaves MainMenu open for Play.

**Playtest checklist:** island noticeably bigger with nodes spread out; forests mix five tree silhouettes/shades and read taller; units path AROUND trees/rocks at full speed (no rubbing slow-down); enemies chasing warriors never wedge behind trunks; workers still pack around nodes and deliver promptly (carve changed the gather ring — watch for orbiting/unreachable-node give-ups); buildings flatten a pad and sit flush on slopes, ghost matches placed height; red no-build lines hug the hills; flee: workers sprint to a hut and vanish inside, pop out when enemies die, hut destroyed mid-hide pops them out; wood/stone workers detour to nearby sticks/stones and deliver them; key 5 places the Workshop, clicking it opens the crafting panel, each recipe crafts once and its effect visibly applies; enemy waves (ring 45) arrive from the shore and will chew the workshop like huts; F4 quick-start works on the big island; ~60 fps (terrain is ~2.25× the triangles; 440 carving obstacles are static after the initial carve).
### Balance Simulation Harness (2026-08-27)

Headless autoplay so balance questions get answered by data instead of by playing a hundred 15-minute games. Full guide: **`docs/SIMULATION.md`**. Compile-verified vs 6000.5.9f1 Roslyn in all three configs (editor / DEVELOPMENT_BUILD player / release player): **0 errors, 0 new warnings**.

Runs the real game with a scripted *player* — worker assignment, build orders, recruitment — and nothing else. Pathing, gathering, combat, fleeing and enemy targeting stay the game's own Utility AI, which is the whole reason the output is worth reading. Three strategies (`SimPolicy.cs`): **Turtle** (wall ring + gates), **Rush** (warriors, minimal economy), **Eco** (the baseline). Writes `runs.csv` (one row per game) and `nights.csv` (one row per night — `campfire_hp_min` is the column that actually tells you whether a night was close).

**New files:** `Assets/Scripts/Sim/{SimRunner,SimPolicy,SimBuilder,SimConfig,SimOverrides,SimMetrics,SimHooks}.cs`, `Assets/Editor/Sim/SimTools.cs`, `tools/run-sim.ps1`, `SimSweeps/example.json`.

**Guarded hooks added to existing scripts** (all `#if UNITY_EDITOR || DEVELOPMENT_BUILD` or a single `SimHooks.Simulating` check): `Worker.Start` / `Warrior.Start` / `Enemy.Start` / `TerrainGrid.Awake` (knob overrides); `CombatEffects.Awake` / `Health.Start` / `HealthBar.Start` / `UnitBase.CreateStateText` (cosmetic suppression).

**Gotchas this encodes — read before touching the harness:**

- **NEVER speed a sim up with `Time.timeScale`.** The AI eval budget (`AIBrain`) and NavMesh throttles (`AINavHelper`) are FRAME-based, so `timeScale` gives every brain proportionally fewer decisions per game-second — units lose fights they would normally win and the harness reports that AI degradation as *balance*. `SimRunner` sets **`Time.captureDeltaTime = 1/60`** instead: game time advances a fixed step per frame, so the run still gets 60 frames per game-second while the loop runs flat out (~3-10x realtime). Anything measuring `Time.unscaledTime` / `realtimeSinceStartup` diverges under this — the harness uses `Time.time` deliberately.
- **Unit knobs cannot be applied by patching a prefab or an instance.** A `public float` on a unit script is dead data (the prefab wins), and unit `Start`s copy the value into the AI blackboard immediately, so an after-the-fact patch is too late; mutating the prefab asset at runtime dirties it on disk in the editor. Hence `SimOverrides.Apply(this)` at the top of each unit's `Start`.
- **`TerrainGrid` builds the world AND the NavMesh in `Awake`**, so `terrainSeed` cannot be applied from a `sceneLoaded` callback — that override lives inside `TerrainGrid.Awake` itself. Every other scene-level knob IS applied from `sceneLoaded`, which is the one window after all `Awake`s and before any `Start` (`ResourceManager.Start` applies `startingWood`, etc.).
- **A wall ring must have gaps.** Walls carve the NavMesh; a sealed ring traps every worker inside it. `SimBuilder.PlaceWallRing` leaves one cell open mid-way along each side, and Turtle converts two finished walls to gates afterwards.
- **`-1` is the "don't override" sentinel in `SimConfig`, not 0** — 0 is a legal value for most knobs.
- `SimBuilder` mirrors `GhostPlacer.ConfirmPlacement` / `WallLinePlacer.ConfirmWallLine` step for step (afford → spend → T2 flatten → construction site → Buildings layer → `SetBuildingType`). If either confirm path changes, this has to change with it or simulated builds stop costing what real ones cost.
- `SimTools` passes `MainIsland` to `BuildPipeline` explicitly rather than reading `EditorBuildSettings` — that scene list still points at the leftover SampleScene.
- `SimRunner` is the **second** deliberate exception to the no-`DontDestroyOnLoad` rule (`DebugMenu` is the first): it must outlive the scene reload between runs. It holds only sweep bookkeeping, never game state.

**⚠️ THE HARNESS IS NOT RUN-TO-RUN DETERMINISTIC (found 2026-08-28).** The same config with the same seeds, run in two different sweeps, produced 92% and 67% win rates with **5 of 12 individual seeds flipping outcome**. `SimConfig.seed` seeds `UnityEngine.Random` only; it cannot control async `NavMesh.UpdateNavMeshAsync` rebuilds (kicked by every `TerrainGrid.FlattenArea` on a building placement) completing after a wall-clock-dependent number of frames, nor the job system's agent update order — and CPU contention differs with `-Parallel` sharding. Consequences: **a seed makes runs statistically comparable, not reproducible**; at n=12 the win-rate confidence interval is roughly ±13pp, so any difference smaller than that is noise (this is why the warrior heal-rate rows tested non-monotonic); and continuous per-night measures like `campfire_hp_min` are far more sensitive than the binary win/loss, so read those first. Never report a win-rate difference of one or two runs as a finding.

**What this does NOT cover** (still manual, ~40% of the playtest checklists): anything visual (ghost tint, bloom, health-bar heights, draped no-build lines, art, death fades), anything input-driven (wall dragging, R/G/demolish, camera, hover, crafting panel), feel/stutter (use `PerfLogger` F6), and the opening sequence itself (the harness force-skips it via `DebugForceColonyStart`).

**Not yet run.** The harness compiles but no sweep has been executed — the first run needs `Tools > Island RTS > Simulation > Build Headless Sim Player` in Unity, then `.\tools\run-sim.ps1`.

**First-sweep findings (2026-08-27) — three harness bugs the first real runs exposed, all fixed:**

- **Reset `Time.timeScale = 1f` at the start of every simulated run.** `GameManager.TriggerVictory` / `TriggerDefeat` pause the game with `Time.timeScale = 0f`, and timeScale is a **global that survives a scene load**. Without the reset, run 1 ends and every run after it spins at 100% CPU forever with a frozen game clock — and because the per-run `maxGameSeconds` stop is measured in GAME time, it can never fire either. Symptom: exactly one row in `runs.csv`, a player process burning a full core, and a log whose last line is "Campfire lit — the colony begins". `SimRunner` now also carries a REAL-seconds failsafe (`SimSweep.maxWallSecondsPerRun`, default 300) so no future clock freeze can eat a sweep.
- **`AddComponent<T>()` runs `Awake` immediately**, before the caller can assign the component's fields. `SimRunner.Bootstrap` NREd on `sweep.captureDeltaTime` and died at startup, leaving a sweep that produced only CSV headers. The GameObject is now created inactive, populated, then activated.
- **Run 0's scene knobs raced the components that read them.** `sceneLoaded` is the correct window for runs 1..n (after every `Awake`, before any `Start`), but run 0's scene is already loaded before the harness exists. `SimRunner` is now `[DefaultExecutionOrder(-1000)]` so its `Start` precedes `GameManager.Start` / `ResourceManager.Start`, and run 0's unit/terrain knobs are set during `Bootstrap` — before the first `Awake`, which is where `TerrainGrid` builds the island and the NavMesh.

**Two operational gotchas from the same session:**

- **A script-only Unity player build does NOT touch the .exe timestamp** — it rewrites `<player>_Data/Managed/Assembly-CSharp.dll`. Check the DLL when deciding whether a rebuild landed; the exe can be hours stale while the code inside it is current.
- **Never let two sim player processes share one `outputDir`.** `SimMetrics.Append` uses `File.AppendAllText`, so concurrent runs interleave and corrupt rows. `tools/run-sim.ps1 -Parallel N` shards the run list into N temp sweep files, each with its own `outputDir` and `-logFile`, then merges the CSVs. It does NOT launch N copies of the same sweep.

**Measured throughput:** ~23–25x realtime per process on this machine (a 5-night run is ~900s of game time in ~40s wall). The earlier "3–10x" estimate in `docs/SIMULATION.md` was conservative.

### Settings Depth Pass (2026-08-30 — ⚠️ PENDING PLAYTEST)

Options went from 12 settings across 3 tabs to 22 across 4, plus real key rebinding and a difficulty system. Compile-verified vs 6000.5.9f1 Roslyn in all three configs (editor / DEVELOPMENT_BUILD player / release player): **0 errors, 0 warnings**. Playtest checklist: the "Settings, keybinding & difficulty" section of `docs/CONTROLS_AND_CHECKLIST.md`.

**New files:** `UI/KeyBindings.cs` (17 rebindable actions, two slots each, PlayerPrefs-backed), `UI/Difficulty.cs` (6 presets plus an editable Custom), `UI/MenuScaler.cs` (UI Scale applied to registered menu canvases).

**New settings.** Audio: mute-when-unfocused. Video: display mode (fullscreen / borderless / windowed), a real resolution list (Unity's `Screen.resolutions` collapsed by size, highest refresh kept, largest first), frame-rate cap. Camera (new tab): zoom speed, rotation speed, invert tilt, and screen shake promoted from a bool to a 0–1.5x slider. Interface (new tab): UI scale, health-bar mode (Always / When damaged / Never), unit state labels, pause-when-unfocused.

**Options UX.** Every row can carry a one-line muted description; tab bodies are a fixed 380px scroll region with the scroll position preserved across rebuilds; sliders now take a range and a formatter, so volumes read `80%` and multipliers read `1.25x`; leaving the screen is the save point.

**Rebinding.** The Controls screen became the keybinding editor: two clickable slots per action, click to arm, next key takes it, Esc cancels, and binding a key already in use steals it from the old action (promoting that action's alternate into the emptied main slot). A "Fixed" section lists Esc and the mouse buttons for reference, without slots.

**Difficulty.** Peaceful / Relaxed / Normal / Hard / Brutal / Custom, picked on a new NEW GAME screen and locked for the run; the pause menu shows it read-only. Knobs: raid size, enemy health, enemy damage, night length, starting resources, nights to survive. Normal is exactly the shipped numbers, so it is a no-op by construction.

**Bug found on the way:** `SimRunner.ConfigureScene` set `ResourceManager.startingWood/Food/Stone` from `sceneLoaded`, but `ResourceManager` consumes them in `Awake` — those three sweep knobs had never done anything. Fixed by writing the live pool as well. Any past sweep that varied starting resources needs re-running.

The load-bearing details are all in the Runtime uGUI Gotchas section above.

### End Screens on the Menu System (2026-08-31 — ⚠️ PENDING PLAYTEST)

Victory and defeat were the last screens still built by hand in the scene. They are now `MenuScreens.Screen.GameOver` — one screen with two dressings (title, subtitle and accent colour swap; the stats block and buttons are shared), on the same widgets as the pause and options menus. Compile-verified in all three configs: **0 errors, 0 warnings**.

**The actual bug this fixes:** neither end screen could return to the main menu. The old Quit button called `GameManager.QuitGame` → `Application.Quit`, which in the editor just stopped Play, and it bypassed `MenuFlow` entirely so settings were never saved. Buttons are now **Restart · Main Menu · Quit to Desktop**, all through `MenuFlow`, plus **Keep Playing** on victory only.

The stats block gained the run's difficulty and the peak colony size, and defeat now shows nights survived correctly (the night you lost doesn't count — that was a bare `- 1` in a format string before).

**Deleted:** `VictoryDefeatUI.cs`, and `GameManager.victoryScreen` / `defeatScreen` / `RestartGame()` / `QuitGame()`. **New:** `Assets/Editor/LegacyEndScreenCleanup.cs`, which removes the dead scene panels and is now step 7 of 8 in `Setup Everything (In Order)`.

### Island Generator v2 (2026-09-01 — ⚠️ NEEDS EDITOR SETUP RE-RUN + PLAYTEST)

Random island per run with terraces, cliffs, ponds, rocky-vs-sandy shores, seven terrain material bands and runtime prop scatter. This is T3's connectivity validation and T4's random seed + runtime scatter from `TERRAIN_SYSTEM_PLAN.md`, done together. Compile-verified vs 6000.5.9f1 Roslyn in all three configs: **0 errors, 0 new warnings**. The generator was also run outside Unity (console harness with a Perlin shim, 24 seeds + a 12-seed all-cliff stress run): every seed validated on the first attempt in ~20 ms, and the stress run exercised the ramp repair (9 ramps carved, all islands connected). Playtest checklist: the "Island generator v2" section of `docs/CONTROLS_AND_CHECKLIST.md`.

**New files:** `Terrain/IslandSettings.cs` (SO, every knob), `Terrain/ScatterSettings.cs` (SO, prop rules), `Terrain/PropScatter.cs` (runtime scatter), `Editor/TerrainPreview.cs` (`Terrain > Preview Island Seeds` — 12 seeds to PNG without Play mode; magenta = land the validator could not reach). **Rewritten:** `IslandGenerator.cs` (layered pipeline returning an `IslandField`: heights + tone + band-jitter + reachable mask), `TerrainGrid.cs`, `Editor/TerrainSetup.cs`, `Editor/LowPoly/LowPolyScatter.cs` (now builds `Assets/Settings/ScatterSettings.asset` from its table instead of scattering into the scene).

**Gotchas this encodes:**

- **Two settings assets, two opposite policies.** `IslandSettings.asset` is created ONCE from `IslandSettings.CreateDefault()` and never overwritten by the setup tool — it is the tuning surface, so **a default changed in code does not reach an existing asset** (delete the asset or edit it). `ScatterSettings.asset` is the reverse: `Build Scatter Settings` REWRITES it from the code table every run, so lasting scatter tuning goes in `LowPolyScatter.Table`, not the asset.
- **Seed flow:** `TerrainGrid.RunSeed` (static) is set in `Awake` and survives the scene reload; `MenuFlow.NewGame` clears it (fresh island), `MenuFlow.Restart` deliberately does not (same island — same rule as `Difficulty`). Under `SimHooks.Simulating` the inspector `seed` (or the sweep's `terrainSeed`) is always used, never a random one — sweeps must stay comparable. F4 shows the seed; the load log line carries the full validation report.
- **A cliff has to land inside ONE 1 m cell.** The NavMesh agent climbs 0.75 m steps and 45° slopes, so a 1.5 m terrace step whose transition spans two cells becomes two 0.75 m half-steps and is climbable — the first draft's "cliffs" leaked everywhere (100% reachable even in the all-cliff stress run). `cliffSharpness` 0.04 puts the whole step between adjacent vertices; the flood fill (`maxWalkableStep` 0.9, 4-connected) is the same rule. Any future change to sampling spacing or agent climb must revisit both numbers.
- **Buildable now means dry ∧ gentle ∧ REACHABLE.** `TerrainGrid.IsReachable` reads the validator's flood-fill mask from the campfire site; `IsBuildable`, `ResourceSpawner.IsTerrainOk`, `PickupSpawner` and `EnemySpawner` all consult it. Cut-off outcrops under `minRegionCells` (40) are left unreachable on purpose (decor only); bigger ones get a `CarveRamp` (closest boundary pair, extended to `rampSlope` 0.4) up to `maxRamps`, then the seed is rerolled (`seed + attempt × 7919`, deterministic), and after `maxAttempts` the best-scoring attempt is kept so generation always terminates.
- **The anchors are what make random seeds safe for the opening.** The ellipse's long axis is swung ≤ `axisSwing` (35°) around the cove direction so the landing side is never the crushed axis; a land WEDGE (`corridorHalfWidthCove` 13 → `corridorHalfWidth` 5) from the cove ramp to the campfire site is `max`-ed into the land mask, has terraces and ponds suppressed and half hill amplitude, so the survivor's walk never meets a cliff; the cove shelf/ramp discs are flattened to fixed heights, which is why `TerrainSetup` can snap the wreck with any seed. A `borderFalloff` (66 → 73 m) keeps warped coastlines off the map edge.
- **Ponds are for low ground only** (`pondMaxGround` 3 m, wide `pondShore`): a pond noise blob on a 5 m plateau produced a crater with an unwalkable rim ring.
- **Adding a terrain material band is a triple:** `TerrainGrid.Surface` enum entry + a line in `Classify` + `TerrainSetup.SurfaceMaterialKeys` + a `LowPolyPalette` entry (`SandWet` / `GrassDark` / `GrassDry` were added). The setup tool refuses to build if the key count and the enum disagree. `surfaceMaterials` is an array in enum order; the old `sandMaterial`/`grassMaterial`/`rockMaterial` fields are gone.
- **Scatter rules are terrain-based, never radial** — the island is a different shape every run. Props go under an own `_Scatter` root (never under `Terrain`: its `NavMeshSurface` collects children), are placed in `Start` (TerrainGrid is done in `Awake`), use their own `System.Random` seeded from the island seed, and are `StaticBatchingUtility.Combine`d. Skipped under the sim.
- **Compile-checking the generator outside Unity works** because it is pure: a ~60-line shim of `Mathf`/`Vector2`/`ScriptableObject` + a `TerrainGrid` stub lets `dotnet build` run it (scratchpad harness, 2026-09-01). `Mathf.PerlinNoise` is replaced by a textbook Perlin, so the harness's islands are not the game's islands — statistics and pipeline logic transfer, exact shapes do not. Use `Preview Island Seeds` in the editor for the real thing.

### World Options, Metal, Purposeful Nodes, HUD + Water (2026-09-01 evening — ⚠️ NEEDS EDITOR SETUP RE-RUN + PLAYTEST)

The user's first playtest of the generator v2 asked for: generation settings, less foliage and fewer stone nodes, a metal resource, redone resource/assignment UIs, nodes spread out with purpose, non-grey water, and smoother, more gradual terrain with more plateaus and flowing materials. All delivered; compile-verified vs 6000.5.9f1 Roslyn in all three configs: **0 errors, 0 new warnings**. The harness re-ran clean on the smoother defaults (12 seeds + 8 all-cliff, every one valid first attempt).

**Editor run order:** `Setup Everything (In Order)` again. Step 1 generates the `OreNode` art + `OreRock`/`OreMetal` materials, step 2's plumber **creates `Assets/Prefabs/OreNode.prefab` by copying `RockNode.prefab`** (the `CopyFrom`/`NodeType` columns on its table) and mounts the art, step 5 refreshes `IslandSettings.asset` (v1 → v2) and re-points `Mat_Water` at the stylized shader, step 6 wires `oreNodePrefab` + the sparse counts into the scene `ResourceSpawner`, step 7 deletes the legacy `ResourcePanel` / `WorkerAssignmentPanel` scene objects. Checklist: "World options, metal, HUD, water" in `docs/CONTROLS_AND_CHECKLIST.md`.

**New files:** `Terrain/IslandOptions.cs` (size / style / seed selection + run snapshot, the `Difficulty` pattern), `Shaders/StylizedWater.shader`. **Rewritten:** `ResourceUI.cs` (HUD chips), `WorkerAssignmentUI.cs` (campfire panel), `ResourceSpawner.cs` (habitats), `ResourceManager.cs` (metal). `TerrainGrid.VertsPerSide` is a static property now, not a const.

**Gotchas this encodes:**

- **Every 150 m-map distance must scale with `TerrainGrid.SizeScale`** (map half-extent ÷ 75). Small / Medium / Large islands are 111 / 151 / 191 verts. `IslandSettings` distances and anchors are authored for the 150 m map and the generator scales them (the scaled anchors live on the `IslandField`, and `TerrainGrid.CoveCenter` / `CampfireSite` expose them); `EnemySpawner.spawnDistance`, `PickupSpawner`'s radius band, `ResourceSpawner`'s area and cluster radius, and `PropScatter` counts (by area) all read it. `TerrainGrid.PlaceShipwreck` moves the scene wreck with the cove on non-standard sizes; `GameStartController` spawns the survivor one metre east of `CoveCenter`. **A new spawner or placement rule with a literal distance is wrong on two of the three sizes.**
- **`IslandSettings.asset` now carries a `version`;** the setup tool rewrites it from `CreateDefault()` when the code's `CurrentVersion` is newer (any hand tuning is replaced — bump the version only for a deliberate defaults pass). Terrain styles (`Rolling` / `Terraced` / `Rugged`) are applied by `IslandSettings.WithStyle`, which returns a runtime **clone** — `TerrainGrid.ActiveSettings` is what the run used; never read `settings` (the asset) for run-time values.
- **Bands are classified from the smooth field, not the triangle normal.** `TerrainGrid.Classify` used per-triangle normals, so the two triangles of one quad on a hillside landed in different bands — a rock/grass checkerboard. It now samples the bilinear gradient at the centroid (`SlopeAt`), and grass tone is `lerp(noise, height / maxHeight, toneHeightWeight)` so valleys run dark and plateau tops dry instead of random speckle. Same rule in `TerrainPreview`.
- **Node placement is habitat-driven** (`ResourceSpawner.Habitat`: height / slope / tone bands, plus an "or steeper than X" clause for rocks and ore at cliff feet), with the rule **relaxed after most attempts fail** so a Rolling island still gets ore. Counts are sparse on purpose (150 trees / 60 bushes / 55 rocks / 24 ore for the 150 m map, 5 forests of r12) and are SCENE values — `NewContentSetup.WireResourceSpawner` writes them, a code-default edit does not.
- **Metal is a fourth `ResourceType` (appended — the enum is serialized as an int, never reorder).** `ResourceManager.Add(type, n)` / `Get(type)` exist now; the four-cost `CanAfford` / `SpendResources` overloads are what new costs should use (three-cost ones forward with 0). Nothing costs metal yet. Ore nodes carve like rocks; miners share the stone gather sound; no metal pickups (`PickupAvailability` scores 0 for anything but wood/stone workers).
- **The HUD and campfire panel are code-built on `MenuBuilder`** (canvases at sort 40 / 60, registered with `MenuScaler`). `WorkerAssignmentUI.Instance` is the fallback when a campfire's `workerUI` scene ref is null, `WorkerAssignmentUI.IsOpen` is consulted by `PauseController.ModeActive` so Esc closes the panel instead of pausing, and both `BaseBuilding.OnMouseDown` and the HUD chips refuse to open it while `PauseController.BlockGameplayInput`. `MenuBuilder.InputRow` (a code-built `TMP_InputField`) exists for the seed field.
- **`MenuBuilder.Label` WRAPS by default — a single-line caption must set `TextWrappingModes.NoWrap` (2026-09-01).** The HUD's `Wood · 0 workers` line wrapped to a third line inside a 128px chip because the label inherited `Normal` wrapping; the rework (`ResourceUI.Entry`) uses `NoWrap` + `Overflow`, a fixed 150px entry width (`LayoutElement.minWidth` too, so the `HorizontalLayoutGroup` can't shrink it), and a rich-text `<color>` tag to dim the worker-count half of one string rather than a second label. The bar is one Image with 1px `Divider` children between entries — no per-entry borders. **A transparent-at-rest button still needs an opaque-white `Image`:** the `ColorBlock` tint MULTIPLIES the graphic colour, so an Image with alpha 0 can never show a hover; the entry's Image is white and `normalColor` carries the alpha 0.
- **Draggable UI = `DraggablePanel.Attach(handle, panel, prefsKey, default)`** (`UI/DraggablePanel.cs`, used by the campfire panel's title row). The panel must be anchored + pivoted bottom-left (anchored position = the corner that is clamped and saved to PlayerPrefs); the handle gets a near-invisible raycastable `DragSurface` behind its children because drag events only reach something the raycast can hit; the clamp reads the canvas rect, which is 0×0 on the frame the canvas is built, so `Clamp()` returns early then and is called again in `OpenPanel`.
- **Water reads grey when a mirror-smooth transparent Lit material reflects a grey sky** — that was the whole placeholder problem. `StylizedWater.shader` is hand-written URP HLSL: depth-texture colour blend (needs `m_RequireDepthTexture`, on for PC), noise-broken shoreline foam, sine displacement on a real 1.5 m grid mesh (`TerrainGrid.BuildWaterMesh` — the 10×10 plane primitive had no vertices to move), derivative-based flat normals, main-light lighting with a soft sheen, and per-facet tone jitter + glints. **Ortho depth is reconstructed linearly** (`lerp(near, far, 1 − raw)` under reversed-Z) and only the scene-minus-water DIFFERENCE is used, so the negative near clip that breaks SSAO does not break this. `OpeningSequenceSetup.ApplyWaterLook` re-points an existing `Mat_Water` at the shader; the Lit fallback is deliberately low-smoothness.
- **An orthographic camera has no specular highlight SPOT — a hard-stepped specular flips the whole sea on and off (2026-09-01).** The view direction is identical for every pixel, so on a flat plane `dot(normal, halfVector)` is one number for the entire ocean, set only by camera tilt/heading. The first water shader's `smoothstep`-cut specular therefore switched the *whole* sea light at some angles, and every facet the wave tilted out of the cutoff drew as a dark tile. Compounding it, the 3 m grid was sampling a 4.1 m wave, which aliased into slow diagonal beat stripes across the sea. Rules now encoded in the shader header: the sheen is a low-power, low-strength `pow()` with no cutoff; sparkle is **per-facet and angle-independent** (`FacetValue` identifies the grid triangle from world XZ — `TerrainGrid` forces an even quad count so vertices land on multiples of `_GridStep`, and pushes the step via a `MaterialPropertyBlock`, never the shared asset); wavelengths stay ≥ ~6× the grid step. **Tuned values live in `Mat_Water.mat`, not the shader defaults** — a serialized property overrides the default, so a retune has to edit the asset (`ApplyWaterLook` returns early once the shader is assigned and sets nothing).
- **Shaders cannot be compile-checked by the Roslyn recipe.** The water shader is unverified until Unity imports it — if it errors, the material falls back to magenta, not grey; check the console for `StylizedWater` first.

### Tree Occlusion Fade, Gatherable Palms, Plain Metal Node (2026-09-02 — ⚠️ NEEDS EDITOR SETUP RE-RUN + PLAYTEST)

Three playtest asks from the island-generator-v2 session: workers vanished behind canopies, only the spawner's broadleaf trees could be chopped while the scattered palms were scenery, and the new metal node's bright veins read as clutter beside the stone node's crystals. Compile-verified vs 6000.5.9f1 Roslyn in all three configs (editor / DEVELOPMENT_BUILD player / release player): **0 errors, 0 new warnings** (the five `DayNightCycle` editor-GUI field warnings are pre-existing).

**Editor run order:** `Setup Everything (In Order)`. Step 1 regenerates the metal node mesh, step 2's plumber remounts it on `OreNode.prefab`, step 4 rewrites `ScatterSettings.asset` so the palm rules come back marked gatherable. Nothing here needs a scene edit and no prefab gains a new component.

**New files:** `Scripts/OcclusionFade.cs` (the per-tree fade), `Scripts/OcclusionFadeManager.cs` (which trees are hiding a unit right now).

**Gotchas this encodes:**

- **The occlusion test is screen-space, not a raycast.** A camera-to-unit ray would also hit terrain, buildings and the trees' own click hitboxes (those sit on the Default layer), and would cost one raycast per unit per frame. The manager instead projects each tree to the screen-space segment base→top, widens it by the tree's half-width, and asks whether any unit point falls inside it while being further from the camera — which under the orthographic projection is a plain depth compare. One pass over the tree list answers it for every unit at once, at 10 Hz; the fade itself lerps per frame, so the low tick rate is invisible.
- **The fade must reuse `ResourceNode`'s material instances.** Reading `renderer.materials` INSTANTIATES, so collecting a second time would leave the hover highlight and the fade writing to different copies of the same material and whichever wrote last would win at random. `ResourceNode.EnsureNodeMaterials()` is the single collector, and it is lazy so it does not depend on Start order.
- **URP has no runtime API for opaque↔transparent.** `OcclusionFade.SetSurface` writes `_Surface` / `_SrcBlend` / `_DstBlend` / `_ZWrite`, toggles the `_SURFACE_TYPE_TRANSPARENT` keyword, and sets `renderQueue` (back to **-1**, meaning "whatever the shader declares", when restoring). It runs only when the fade crosses in or out of full opacity, never per frame, and a fully opaque tree early-outs of `Update` entirely.
- **`OcclusionFade` is added at runtime by `ResourceNode.Start` for Wood nodes,** not by the plumber, precisely so it also covers trees `PropScatter` builds. The manager self-bootstraps from `OcclusionFade.Awake` (no scene wiring, and no `DontDestroyOnLoad` — it holds no state) and is skipped under the sim.
- **A scatter rule can be gatherable now, and gatherable props are NOT decor.** `ScatterSettings.Rule.gatherable` makes `PropScatter.SpawnNode` build the node the way the plumber builds `Tree.prefab`: an empty root carrying `ResourceNode` and a `BoxCollider` measured from the art's renderer bounds, with the art on a child named exactly **"Model"**. That name is load-bearing — depletion shrink and the gather-beat wobble both target that child, because the root drives the carving `NavMeshObstacle` and moving it would re-carve the NavMesh on every gather tick. Yaw and size jitter go on the Model for the same reason. Gatherable props live under their own `_Scatter_Nodes` root and are **never static-batched** (they shrink, and are destroyed when emptied), and they are rejected on unreachable ground — a node on a cut-off outcrop is a node no worker can ever reach.
- **`PropScatter` no longer returns early under the sim.** It now skips only the decor rules; the gatherable ones run, because palms are a meaningful share of the island's wood and a sweep that cannot chop them is not measuring the real economy. **Sweeps from before this change are not comparable to sweeps after it.**
- **Depleting a scattered palm respawns a broadleaf tree,** because `ResourceSpawner.NotifyResourceDepleted` owns respawns and only knows its own prefabs. Accepted; the shoreline slowly mixes.
- **The metal node is a plain boulder now** (`Shapes_Resources.MetalOreRock`) and the stone node keeps its crystals — that contrast is what tells them apart at a glance. The `OreRock` / `OreMetal` palette entries are left in place but unused; their generated materials are simply no longer referenced by any mesh.

**Playtest:** a worker walking behind any tree fades it to about 30% and it comes back as soon as the worker clears it, with no popping and no tree left stuck transparent; several units behind one tree behave the same; hovering a faded tree still highlights it, since highlight and fade share materials; every palm on the shore can be chopped and shrinks as it depletes, and workers path around palms the way they do around broadleaf trees; ore nodes look like ordinary boulders and still yield metal.

### Salvage: Crates and Barrels Give Supplies (2026-09-02 — ⚠️ NEEDS EDITOR SETUP RE-RUN + PLAYTEST)

The shipwreck cargo and the crates and barrels washed up along the shore were pure decor. They are `GroundPickup`s now — a worker of the matching job walks over, hauls one home, and it lands in the pool like any delivery. Compile-verified vs 6000.5.9f1 Roslyn in all three configs (editor / DEVELOPMENT_BUILD player / release player): **0 errors, 0 new warnings** (the five `DayNightCycle` editor-GUI field warnings are pre-existing).

**Editor run order:** `Setup Everything (In Order)`. Step 3 rebuilds `_Shipwreck` with salvage cargo, step 4 rewrites `ScatterSettings.asset` so the shore crates and barrels come back marked salvage. No prefab or scene wiring beyond that; nothing new to generate.

**Contents:** crate = 6 food, barrel = 5 wood, at the wreck and on the shore alike. Driftwood stays decor. Food is what the opening is thinnest on, and the barrel's staves give wood workers a reason to walk the beach too.

**Gotchas this encodes:**

- **Salvage is finite; spawner pickups are not.** `PickupSpawner` trickle-respawns what it placed, so `GroundPickup.spawnerOwned` (set by the spawner, false on salvage) gates the `NotifyCollected` callback. Without it, collecting a crate would decrement the spawner's stick or stone budget and make it trickle a replacement for something it never placed.
- **Salvage overfills the carry on purpose.** `GroundPickup.Collect` clamps to remaining capacity, and a worker carries 5 — so an unclamped 6-food crate would silently evaporate most of its contents. `allowOverfill` grants the whole amount instead. This is safe for the AI because every consideration reading the carry (`ResourceCarry`, `ReturnUrgency`) clamps the ratio to 0–1, so an overloaded worker simply reads as full and heads home.
- **`PickupAvailability` no longer gates on job type.** The old wood/stone-only early-out would have made every food crate invisible. Any job qualifies now; a job with no matching pickup on the island (metal) finds nothing in the scan and scores 0, which is the same answer with one fewer special case.
- **`ScatterSettings.Rule.salvage` is the third prop mode**, beside `gatherable` and plain decor. `PropScatter.SpawnSalvage` mirrors the pickup prefabs the session-content tool builds: gameplay component on an empty root, art on a **"Model"** child, and **no collider** — a pickup is an AI target, never a click target. It also **samples the NavMesh and snaps** before placing, and returns false when there is none, because the flotsam bands reach into the wading band where the NavMesh edge is ragged. Salvage shares the gameplay `_Scatter_Nodes` root, so it is never static-batched (a collected crate is destroyed) and it is rejected on unreachable ground.
- **The sim runs the salvage rules too** now, alongside the gatherable ones — salvage is real economy a simulated colony can collect. **Sweeps from before this change are not comparable to sweeps after it**, the same caveat the gatherable palms carry.
- The salvage amounts and modes live in `LowPolyScatter.Table` (rewritten into the asset every run) for the shore, and in `OpeningSequenceSetup.BuildShipwreck` for the wreck. Tuning one does not touch the other.

**Playtest:** two crates and a barrel sit in the wreck at the landing site and more of both line the shore; assigning a food worker sends someone to a crate and the food arrives at the campfire (6 per crate, even though a worker carries 5); wood workers do the same with barrels; nothing walks to a crate while enemies are near (threat suppresses Pickup like Gather); a collected crate never comes back, while sticks and stones still trickle in at their usual rate; no crate ends up in water or on an outcrop where no worker can reach it.

### Colonist Pool, Arrivals and Builders (2026-09-02 — ⚠️ PENDING PLAYTEST, UNCOMMITTED)

The Phase 7 "jobless colonist" plan, delivered. People are a pool now: housing brings survivors ashore, the campfire panel hands idle colonists jobs instead of spawning workers, idle colonists build and repair, and warriors are armed from the same pool. The 5s auto-build is gone. Compile-verified vs 6000.5.9f1 Roslyn in all three configs (editor / DEVELOPMENT_BUILD player / release player): **0 errors, 0 new warnings**. No editor setup step and no scene or prefab edit is needed. Checklist: "Colonist pool, arrivals, builders" in `docs/CONTROLS_AND_CHECKLIST.md`.

**User decisions (4-option questions):** arrivals = *both* (a survivor lands at the cove and walks to the hut with room); colonists are free, housing is the only limit; warriors convert an idle colonist; auto-build removed outright, sites wait for a builder.

**New files:** `IHousing.cs`, `RepairCosts.cs`, `AI/Considerations/{IsJobless,ConstructionAvailable,RepairAvailable}.cs`, `AI/Executors/Worker/{BuildExecutor,RepairExecutor}.cs`. **Rewritten:** `PopulationManager.cs` (roster + providers + arrival timer), `IdleExecutor.cs` (walks home, then stands).

**How it fits together:**
- `PopulationManager` holds `Colonist{unit, home}` records and the list of `IHousing` providers (`BaseBuilding`, `Hut`). Capacity is summed from live providers; occupancy per building is counted from the roster. While the roster is short of capacity, by day, one survivor lands at `TerrainGrid.CoveCenter` every `arrivalInterval` (20s) via `BaseBuilding.SpawnColonist(pos, home)` and is homed to the first provider with a free slot (campfire first, then huts in build order). Intro, night and game-over hold the timer.
- `Worker.hasJob` + `SetJob(type)` / `ClearJob()`. `BaseBuilding.AssignWorker` = `SetJob` on the idle colonist nearest the fire (returns false when nobody is idle — the panel greys `+` on the same condition); `UnassignWorker` = `ClearJob`. `woodWorkers` etc. are computed properties over the roster.
- New worker actions **Build** (1.0) and **Repair** (0.9), each gated by `IsJobless` (zero-cost, first in the list so job holders early-out before any scan), a scan consideration that caches the target on the blackboard, and `ThreatNearby` (hard suppression, so builders flee like workers). `ConstructionSite.AddLabor(dt)` is the only way progress advances; one builder finishes in `buildTime × LaborFactor` (5s × 2 = 10s), up to `MaxBuilders` (3) stack linearly, and a site nobody has touched for 0.5s reads "Awaiting builder". `AddProgress` stays for the F4 cheat.
- Warriors: `SpawnWarrior` needs an idle colonist plus the usual 10W 15F, spawns the warrior where the colonist stands and `PopulationManager.ReplaceUnit`s the roster entry before destroying the worker body; `RemoveWarrior` (dismiss) does the reverse, no refund. Warriors therefore occupy housing and a dead warrior frees a slot.
- The opening's survivor becomes an idle colonist (was: a wood worker). F4 quick-start lands colonists beside the fire with `SpawnArrival(atCampfire: true)`, arms warriors, then assigns jobs; a new F4 cheat lands a survivor at the cove on demand.

**Gotchas this encodes:**
- **Assignment REQUIRES a `PopulationManager`.** It used to be an optional cap; now `AssignWorker`, arrivals and recruitment all route through the roster, so `BaseBuilding.Awake` / `Hut.Awake` call `PopulationManager.EnsureExists()` (scene object normally; created on demand otherwise, no `DontDestroyOnLoad`).
- **`ReplaceUnit` before `Destroy`.** Converting a worker to a warrior (or back) swaps the roster entry first, so the old body's `OnDestroy → NotifyWorkerRemoved → RemoveColonist` finds nothing and cannot double-count. Roster membership is the guard, same rule as before.
- **What is in the hands is `bb.carryType`, not the job.** `GatherExecutor` sets it when it registers at a node, `GroundPickup.Collect` sets it to the pickup's type, `ReturnToBaseExecutor` delivers it. `ResourceAvailability` / `PickupAvailability` score 0 while the worker carries a different type than its job, and `ReturnUrgency`'s no-node branch then sends them home — so a job change mid-trip delivers the old load first and never mixes types.
- **A job change never reaches a running executor by side effect.** `Worker.OnJobChanged` releases the node claim, nulls `bb.targetResource`, stops the sound and `ForceReeval`s; `GatherExecutor` already treats a null node as "pick another" in both phases, which is the same path the stuck reset uses.
- **Build/Repair have no yShift anywhere,** so a finished site or full building scores exactly 0 and early-outs despite momentum; both executors also `ForceReeval` when they run out of targets, and `ConstructionAvailable` skips sites without builder room so idle crowds spread across sites.
- **Idle walks home now.** `IdleExecutor` paths to the home provider's carve-safe approach point when further than 6u from its edge and stops 3.5u out (so idlers never block the campfire delivery edge); homeless or near home → stand as before. This is what walks a fresh arrival in from the cove — it is not a separate action.
- **`ConstructionSite.LaborFactor` is a const, not a field**, because the prefab's serialized `buildTime` (5) is what actually gets read (dead-data rule). `PopulationManager.arrivalInterval` is a field with a `> 0` fallback for the same reason: the scene object predates it.
- **Repair pricing:** 25% of build cost per full repair, charged one whole unit at a time from fractional per-resource debt; the debt is only committed when `SpendResources` succeeds, so pausing on an empty pool loses nothing. The campfire has no `BuildingData` and is priced like a hut. `RepairAvailable` returns 0 when the next unit is unaffordable, so nobody walks to a repair they cannot start.
- **Sim policies keep one idle colonist while any site exists** (`SimPolicy.HireWorker`), because sites no longer finish on their own. **Sweeps from before this change are not comparable to sweeps after it.**

### Player Character, Slice A: Name Popup + Persistent Character (2026-09-02 — ⚠️ NEEDS EDITOR SETUP RE-RUN + PLAYTEST, UNCOMMITTED)

First slice of `CRAFTING_AND_PLAYER_CHARACTER_PLAN.md` (repo root — the plan and its locked decisions live there). The castaway is now the player's own named character for the whole run; placing the campfire no longer turns them into a colonist. Compile-verified vs 6000.5.9f1 Roslyn in all three configs: **0 errors, 0 new warnings**. Playtest checklist: "Player character & name popup" in `docs/CONTROLS_AND_CHECKLIST.md`.

**Editor run order:** `Setup Everything (In Order)`. Step 1 generates the `Castaway` unit art (Worker body, red bandana, blue sash), step 3 builds `Assets/Prefabs/PlayerCharacter.prefab` (agent + `PlayerCharacter` + `HealthBar` + Castaway art on a `Model` child), deletes the legacy `Survivor.prefab`, and wires `GameStartController.playerPrefab` (`[FormerlySerializedAs("survivorPrefab")]`, so the scene value survives until the tool runs).

**New files:** `PlayerProfile.cs`, `PlayerCharacter.cs`. **Deleted:** `Survivor.cs`. **Edited:** `GameStartController` (popup, no conversion, `KeyBindings` for B), `MenuScreens` (`Screen.NameEntry`, `ShowNameEntry`, `Current`), `MenuFlow.NewGame` (`PlayerProfile.ClearRun`), `FloatingText.alwaysShow`, `KeyBindings` (`CenterOnCharacter`, Space), `CameraController` (`Instance`, `CenterOn`), `OcclusionFadeManager`, `OpeningSequenceSetup`, `Shapes_Units`.

**Gotchas this encodes:**

- **The name popup is modal the same way the game-over screen is.** `MenuScreens.Back()` early-returns on `Screen.NameEntry`, and `ShowNameEntry` clears the back-stack. It never needs `timeScale 0`: the intro already holds the clock, and `PauseController.BlockGameplayInput` (true while any screen is open) is what stops the right-click from moving the character underneath it — `UpdateLanding` / `UpdatePlacing` now check it, which they never did (the pause menu used to leak clicks through to the survivor).
- **`PlayerProfile.Name` never returns empty and the sim never writes PlayerPrefs.** The popup is skipped under `SimHooks.Simulating` and on a Restart (`HasActive`); `DebugForceColonyStart` takes the last-used name instead of showing it, but only outside the sim, because `BeginRun` persists the name and a sweep must not overwrite the developer's.
- **The player is not a colonist.** Never in the `PopulationManager` roster, no housing, no job, no `AIBrain`, and the enemy priority list never scans `PlayerCharacter.ActiveList`. The first colonist now arrives from the housing timer (~20 s after the fire); F4 quick-start and the sim policies still get their people through `SpawnArrival`, so nothing there changed — but **a sweep now starts with zero colonists instead of one, so sweeps before and after this slice are not comparable.**
- **Death is a knock-out.** `SetupHealth` defaults `destroyOnDeath` to true; the player sets it false right after, so `Health.Die` fires the effect and `onDeath` and then leaves the object alone. `Health.Heal` refuses the dead, so the revive writes `currentHealth` directly. Health is set up in `Awake`, not `Start`, because the prefab's `HealthBar.Start` looks it up and component Start order is not something to lean on.
- **The name label must not follow the state-label setting.** `FloatingText.alwaysShow` bypasses `GameSettings.UnitStateText` for this one label; the composed "name + activity" string is rebuilt only when either part changes, so the per-frame path allocates nothing.
- **`CameraController.CenterOn` intersects the view ray at the target's own height**, not y=0 — on a hill the old y=0 plane landed the character a few metres downhill of screen centre. `GameStartController.FrameCameraOnPlayer` routes through it (plane fallback kept for a scene without the controller).
- **No click collider on the player prefab, on purpose.** Every ground raycast is Default-layer; a capsule would park placement ghosts on top of the player. Slice B's `Pickups` layer is the pattern for anything clickable that must stay invisible to placement.

### Player Character, Slices B + C: Hands, Stockpile, Campfire Crafting, Unlocks (2026-09-02 — ⚠️ NEEDS EDITOR SETUP RE-RUN + PLAYTEST, UNCOMMITTED)

The character hand-collects sticks, stones and salvage, carries them in a six-slot inventory, deposits at the fire (resources to the pool, materials to a campfire stockpile), and crafts tools there; the first craft of each tool unlocks a colony activity (the four jobs, construction, warriors). Compile-verified vs 6000.5.9f1 Roslyn in all three configs: **0 errors, 0 new warnings**. Checklist: "Hands, stockpile & campfire crafting" in `docs/CONTROLS_AND_CHECKLIST.md`.

**Editor run order:** `Setup Everything (In Order)`. Step 1 generates the new `Tools` art category (six grip-down tool meshes), step 3 adds `HeldItem` + a `HandSocket` to `PlayerCharacter.prefab` and wires the id → tool-art table, step 6 names layer 7 `Pickups` in the TagManager and rewrites `Stick` / `StonePickup` with their item ids.

**New files:** `Items/{ItemCatalog, Inventory, CraftingCatalog, Unlocks, HeldItem}.cs`, `UI/PlayerHUD.cs`, `Editor/LowPoly/Shapes_Tools.cs`. **Edited:** `GroundPickup` (click collider, `CollectAsItem`, `claimedBy` widened to `MonoBehaviour`), `PickupSpawner` (cove cluster), `PlayerCharacter` (commands, tasks, crafting), `BaseBuilding` (`Stockpile`, two gates), `WorkerAssignmentUI` (tabs), `MenuBuilder.TabRow` (moved out of `MenuScreens`), `BuildPlacement`, `ConstructionAvailable`, `RepairAvailable`, `DebugMenu`, `NewContentSetup`, `LowPolyAssetDef` / `LowPolyAssetGenerator`.

**Gotchas this encodes:**

- **Pickup click colliders live on layer 7 (`GroundPickup.ClickLayer`), added in `Awake`, never on Default.** Every ground raycast in the game is mask `Default` (`BuildPlacement.groundLayer`, `GameStartController.RaycastGround`), so a collider on Default would park ghosts on sticks — the ghost-collider gotcha again. Adding it at runtime means scatter salvage and wreck cargo get it too, with no prefab edit. The player's command raycast is the ONLY thing that queries the layer. The layer is used by index; the name in the TagManager is cosmetic.
- **A pickup is worth different things to different collectors.** Workers still get `amount` of `resourceType` straight into the carry (`Collect(AIBlackboard)`, unchanged); the character gets `itemId` × `itemAmount` (a stick is one Stick, not three wood), or the resource itself in hand when `itemId` is empty (a crate is Food × 6). One object, two `Collect` methods — don't unify them.
- **Items and resources are two layers, and `ItemKind` says which.** Resource items exist only in hand; `DepositAll` turns them into `ResourceManager` amounts, materials go to the campfire `Stockpile`, tools are skipped (they stay in the hands — the KNOWLEDGE is what the colony gained, per the locked decision). `ItemCatalog.Stockpiled` is therefore materials only.
- **Crafting charges on completion, not on start.** `TryQueueCraft` only checks affordability; `CompleteCraft` re-checks and pays (hands first, then stockpile, resources from the pool). So walking away, a knock-out, or a colonist spending the wood mid-craft all cost nothing — the last case fails at the end with "Missing materials". Any new command (`CommandAt`, `MoveTo`) calls `CancelCraft` first.
- **Every gate is one `Unlocks.Has(...)` at the site that already decides the action**: `BaseBuilding.AssignWorker` / `CanRecruitWarrior`, `BuildPlacement.StartPlacement`, and the first line of `ConstructionAvailable` / `RepairAvailable` (before their scans, no yShift, so Build/Repair early-out). The UI reads the same flag to grey and label; nothing is duplicated. **`Unlocks.Has` returns true under `SimHooks.Simulating`**, exactly like `Difficulty.Active` returning Normal — sweeps must not stall at zero workers. **Sweeps before and after this slice are still not comparable** (the sim now starts with 0 colonists, see Slice A).
- **Locked UI must name its recipe.** Job rows append "needs Stone Axe" to their caption, the warriors row swaps its cost line for "Craft a Wooden Spear…", and B before the Mallet flashes the recipe on the character's HUD line via `PlayerCharacter.SetActivity(text, seconds)`. `Unlocks.RecipeTitleFor` looks the title up from the catalog so the strings can never drift from the recipes.
- **Arrival at a task target has a stall fallback.** `PlayerCharacter.Stalled()` (no pending destination, no path or remaining ≤ stopping distance, velocity ~0 for 0.5 s) counts as "as close as the NavMesh allows": within 2.5 u the pickup / fire interaction still happens, beyond it the task drops with "Can't reach that". Without this a pickup on the ragged shore edge left the character reading "Fetching Stick" forever.
- **The campfire panel's tabs are three nested `Column`s with zero padding** inside the main column; `SwitchTab` toggles them and calls `FitPanelHeight` + `drag.Clamp()`, because the bodies differ in height and a taller tab would otherwise push the panel off the bottom edge. `MenuBuilder.TabRow` returns the buttons so the panel re-tints instead of rebuilding.
- **Activity strings are composed only on change.** The floating label ("name + activity") and the crafting percent are rebuilt when the value changes, never per frame; the HUD repaints slots only on `Inventory.OnChanged`.
- **`HeldItem` is visual only.** Equipping happens on craft completion; nothing reads the held tool to decide what the character can do — `Unlocks` does. The `Tools` art category needed the enum value, the `AddToolsImpl` partial, a menu item and the showcase row; `EnsureFolders` picks the folder up from the enum.
