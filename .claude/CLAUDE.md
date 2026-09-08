# Island RTS Game - Claude Working Memory

## Critical Rules

- **Ask 2-4 targeted clarifying questions before writing code** for gameplay, UX, or bugfix changes. Polish > speed.
- Utility AI considerations are **multiplicative** (any 0.3 kills the action); momentum is **additive** after. With the 20% switch threshold, small momentum can make an action impossible to exit, and `yShift > 0` prevents early-out so momentum keeps dead actions alive. Test exit conditions and the full transition table.
- **Session log goes in `docs/PHASE_HISTORY.md`, not here.** Rules it produces go in the matching gotcha section below, one dated rule-shaped bullet. Keep this file under ~280 lines.
- **Anything a player can see gets an `Assets/Resources/Changelog.txt` entry** (`## yyyy-mm-dd — Title` + `- bullet`, newest first, player-facing). Its newest date is the main menu's "updated" line.
- **Every feature also gets a batch in `Assets/Resources/DevQuests.txt`** (2026-09-07): `## yyyy-mm-dd Feature` + `- @signal Quest` / `- Quest`, ≤ 70 chars each, newest first; add a `DevQuests.Signal("key")` at the point of effect where no signal fits. That is the playtest checklist now (tracker + Esc → Information → DEV → SUBMIT REPORT → `Playtests/*.md` comes back). Delete a batch once its report is in. `docs/CONTROLS_AND_CHECKLIST.md` keeps controls only.

## Tech Stack

Unity **6000.5.9f1** · C# · URP 17.5.0 · AI Navigation 2.0.14 · TextMeshPro + uGUI 2.5.0 · Input System 1.20.0 (legacy `Input.*` still used, `activeInputHandler: Both`) · Git.

## Project Structure

Repo root: `docs/` (PHASE_HISTORY, CONTROLS_AND_CHECKLIST, SIMULATION, MENU_WIREFRAMES), `*_PLAN.md` design docs (TERRAIN_SYSTEM, PHASE_10_VISUAL_OVERHAUL, CRAFTING_AND_PLAYER_CHARACTER, RESEARCH_AND_DAYS, COLONY_EXPANSION — source of truth for locked decisions), `SimSweeps/` + `tools/run-sim.ps1`, and `islandrts/`.

`islandrts/Assets/`: `Scripts/` (root-level systems + `AI/` `Items/` `Terrain/` `UI/` `Sim/` `Shaders/`), `Editor/` (FullSetup master menu, `LowPoly/` generator+plumber+scatter table, OpeningSequenceSetup, TerrainSetup, NewContentSetup, `Sim/SimTools`), `Prefabs/`, `Art/` (generated low-poly library), `Settings/` (URP, IslandSettings.asset, ScatterSettings.asset), `MainIsland.unity` (**the game scene**), `MainMenu.unity` (entry point), and unused stock `Scenes/SampleScene.unity` (⚠️ still the only scene in EditorBuildSettings).

### Key Scripts (only where the name doesn't tell you)

| Script | Purpose |
|--------|---------|
| `DayNightCycle` | 100s day / 50s night (scene values), phase clock, lerps `LightingPreset` SOs, static `OnDayStart`/`OnNightStart`, `clockPaused` |
| `RaidDirector` | Runtime-added to `EnemySpawner` at Awake; rolls at dawn whether raiders land and how many. `EnemySpawner.SpawnRaid(count)` only lands what it is told |
| `PopulationManager` | Roster (`Colonist{unit, home}`), `IHousing` providers, arrival timer, `ReplaceUnit`, `HousingProviders` |
| `BaseBuilding` | The campfire: `AssignWorker`, `SpawnWarrior`, `SpawnColonist`, `Stockpile`, station host, `FindAlive` |
| `UnitBase<T>` | CRTP base for Worker/Warrior/Enemy: registry, `CachedHealth`/Agent, state text, stuck resolver. Jobs are `Worker.hasJob` / `SetJob` / `ClearJob` |
| `PlayerCharacter` | The named castaway: `CommandAt` right-click tasks, six-slot `Inventory`, knock-out not death |
| `BuildPlacement` | Build-mode coordinator over plain-C# helpers `GhostPlacer` / `WallLinePlacer` / `DemolishTool` / `NoBuildZoneRenderer` (not MonoBehaviours) |
| `WallConnector` | Procedural wall meshes at runtime (6 shapes + 6 gate variants); `WallGrid` is the O(1) lookup |
| `Terrain/TerrainGrid` | `[DefaultExecutionOrder(-100)]`; builds island + NavMesh in Awake. `SampleHeight` / `IsBuildable` / `IsReachable` / `FlattenArea` / `SizeScale` / `RunSeed` / `CoveCenter` / `CampfireSite`. `IslandGenerator` is the pure seeded pipeline behind it, `IslandSettings` the knobs |
| `UI/MenuBuilder` | Code-built uGUI widgets; `MenuScreens` = every screen, `MenuFlow` = scene flow |
| `WorkerAssignmentUI` | Campfire/station panel (tabs); `ResourceUI` = HUD chips + breakdowns; `UI/PlayerHUD` = bottom inventory strip |

## Architecture

### Utility AI

No state machines. Per unit: `AIBrain` holds `ActionOption[]`, each with `Consideration[]` (0-1, each with a `ResponseCurve`) and an `ActionExecutor` (OnEnter/OnUpdate/OnExit); `AIBlackboard` is the zero-GC per-unit cache.

Every 0.25–0.35s (randomized per unit) the brain scores `basePriority × Π(considerations) + momentum (if current)`. Early-out at 0.001; best must beat current by 20% to switch. Per-frame budget `Clamp(activeBrains × dt / MinEvalInterval, 5, 64)`; throttled evals are deferred, never dropped, and `ForceReeval()` is budgeted, not a bypass.

**Worker:** Gather, Return, Pickup, then the jobless ladder Build 1.0 > Craft 0.95 > Repair 0.9 > Forage 0.85 (each gated by `IsJobless` + `SpecialtyAllows`), Idle, Flee (garrison in nearest hut), Leave. **Warrior:** Engage, Intercept, DefendWall, Patrol, Retreat, Heal. **Enemy:** one `EnemyAttack` action with an imperative priority target function (gate override → warrior in range → campfire-proximity commit → reachable hut/tower/workshop → wall/gate at 0.3× → campfire).

### Patterns

- **Singletons:** ResourceManager, AudioManager, WallGrid, AIWorldState, PopulationManager, GameManager, CombatEffects, CameraShake, BuildingDatabase. No `DontDestroyOnLoad` (stale state across restarts) except `DebugMenu` and `SimRunner`, which hold no game state.
- **`ActiveRegistry<T>`** gives static O(1) lists for every unit, building, node, site and pickup type. `FindObjectsByType` is **banned** — use `X.ActiveList` with an index loop. Unique lookups use `FindAnyObjectByType<T>()` (`FindFirstObjectByType` is obsolete in 6000.5).
- **Targeting:** everything implements `ITargetable`. Scans go through `TargetingUtil.FindNearest`, target state through `bb.SetTarget` / `ClearTarget` / `IsTargetAlive`, carve-safe destinations and ranges through `TargetingUtil.GetApproachPoint` / `EdgeDistance`. Never hand-roll a scan or an approach point.
- **Performance:** zero GC in Update and AI eval; `AINavHelper` throttles SetDestination (20/frame) and CalculatePath (2/frame); enemy density grid (`AIWorldState`, cell 10); dirty-checked UI text; audio preloaded; staggered per-unit timers.
- **Point-of-effect reads:** difficulty multipliers, settings, `CraftedUpgrades`, `Unlocks.Has` and stockpile capacity are read where they take effect, never pushed. Run-scoped choices (`Difficulty`, `IslandOptions`, `PlayerProfile`, `TerrainGrid.RunSeed`) are snapshotted by `MenuFlow.NewGame` and deliberately kept by `Restart`.
- **Building:** `BuildingData` SOs in `BuildingDatabase` keyed by `BuildingType`. Wall lines are click-start/click-end (L-path, or Shift for Bresenham); G converts wall→gate; Delete/X demolishes at 50% refund, campfire protected. Placement flattens a pad; walls follow terrain per cell. Construction advances only via `ConstructionSite.AddLabor` from jobless colonists — no auto-build. `BuildingData.metalCost` (2026-09-04) is read by every four-resource cost overload (placement, refund, repair; walls keep three-arg calls). `requiresShore` = within `GhostPlacer.ShoreRadius` 6 of water (a beach-band width, NOT `SizeScale`d) via `TerrainGrid.IsNearWater`; `buildTimeOverride` exists because huts and the Workshop share one site prefab. The escape is the victory path with `GameManager.isEscape`; `EscapeInProgress` (static) blocks input during the beat.
- **Calendar:** 30-day run. `RaidDirector` rolls at dawn — `chance = (0.15 + 0.2 × quietNights) × raidFrequency`, never before day 3, forced after 5 quiet nights. Size frozen at roll time: `round((2 + 0.4 × day + 0.08 × prosperity) × enemyCount)`. Victory = dawn after day `daysToSurvive` (Normal 30, Peaceful/Relaxed 20; Hard/Brutal raise `raidFrequency` instead of length).
- **Economy layers:** four pooled resources (`ResourceType`, Metal appended last — **never reorder**) ← items (`ItemCatalog`: materials, resources-in-hand, tools, equipment) ← research (`ResearchCatalog`, one-time, grants `Unlocks.Kind`s and hands over its tool) and repeatable recipes (`CraftingCatalog`, gated by a research id). Stations run the queue only while someone stands at the bench; costs are paid on completion.

---

## Utility AI Gotchas

- Order considerations cheapest-first, pruning against the running best (`ResourceAvailability`: type → sqr-distance cull → `distance >= bestScore` → expensive availability checks). `bb.nearestEnemy` must be populated by `EnemyPresence` **first**; zero-cost gates (`IsJobless`, `Unlocks.Has`) also go first so the action early-outs before any scan.
- `ReturnUrgency` uses `max()` of 4 signals, not multiplication; its no-node branch sends a worker home with a partial or mismatched load.
- `IsTargetAlive` must handle Unity destroyed-object null: re-fetch Health if null, don't return true.
- Momentum stops flip-flopping but also stops switches: Flee's `ThreatNearby` yShift must be 0; Heal uses zero momentum and a curve scoring exactly 0 at full HP; Build/Repair have no yShift so a finished site scores 0 despite momentum.
- **Resource nodes and buildings CARVE the NavMesh.** ANY destination on one goes ClosestPoint → `NavMesh.SamplePosition` → edge-distance, caching the collider on assignment (`bb.currentTargetCollider`). A center-based target sits inside the carve, so agents stop at the boundary and never "arrive" — same reason reachability tests must path to a sampled point, not a center (center → always `PathPartial`).
- Interaction ranges are **edge distances** (`TargetingUtil.EdgeDistance`); a center-based threshold smaller than a building's half-extent can never trip.
- `AINavHelper.TrySetDestination` returns Unity's real result — honor it, retry next frame on false. `isStopped = false` with no queued path is the "ghost moving" freeze.
- **Never `ResetPath()` just to force a new destination** — it zeroes velocity that frame; `SetDestination` swaps the path in place. `ResetPath` is for genuinely stopping.
- **Stagger every per-unit retarget/recalc timer in `OnEnter`** (`Random.Range(0f, interval)`) or a whole wave re-paths in one frame. Likewise a new `ForceReeval` broadcast on a static death/damage event is a population-scaled spike — radius-filter it, accept a frame of latency.
- Re-roll `avoidancePriority = Random.Range(30, 70)` whenever an enemy retargets from a dead target (ORCA stuck-dance otherwise).
- `ResponseCurve.Constant` discards its input — never pair it with a scanning consideration; use `ConstantScore` for a floor.
- Actions sharing logic and differing only in "what target?" should be ONE action + priority function (the enemy refactor); siblings + momentum + threshold fight on every target death.
- `AIBlackboard.SetTarget` returns true only when the target **changed**, and deliberately has no side effects — each executor decides what to reset.
- **Any executor calling `StuckResolver.UpdateMoving()` must early-return when it reports a reset** — the `onStuckReset` callback nulls blackboard fields mid-call.
- **Avoidance role follows worker state:** `Worker.SetStationaryAvoidance` (priority 10) whenever standing still, `RollMovingAvoidance` on every moving errand. A stale priority-10 mover plows through everything. Gatherers rubber-band to their anchor if nudged > 0.5.
- **Flee = garrison:** only `FleeToHutExecutor` may call `Worker.SetGarrisoned`, and its OnExit always restores before another executor runs.
- **Pickup vs Forage:** `PickupAvailability` is for a colonist WITH a job (own resource type, 22u attract range from itself); `ForageAvailability` is the jobless half (any type, within `HomeRadius` 70 of the campfire, `NightRadius` 30 after dusk, read from `AIWorldState.Instance.isNight` — a consideration must never `FindAnyObjectByType`). Both reuse `CollectPickupExecutor`; pickups carry `claimedBy`.
- **What is in the hands is `bb.carryType`, not the job.** Availability scores 0 while carrying a different type; a job change delivers the old load first. `Worker.OnJobChanged` releases the node claim, nulls the target, `ForceReeval`s.
- Worker spacing knobs: `Worker.AgentRadius` (0.3, what actually spaces workers), `GatherStopDistance`, `ResourceNode.GatherRingRadius`, `gatherDistance` (floored at `AgentRadius + 0.25`). Per-node capacity is `ResourceNode.GetMaxWorkers()` from ring circumference and open NavMesh samples.
- Unreachable-node fallback: `bb.MarkNodeUnreachable` ring (15s), set by `GatherExecutor` after 0.6s of dead-end path.
- **Idle walks home** (to the home provider's approach point, stopping 3.5u out so idlers never block the delivery edge) — that is what walks a fresh arrival in from the cove.

### Bookkeeping Gotchas (single owner)

- Worker removal has ONE owner: `Worker.OnDestroy → BaseBuilding.NotifyWorkerRemoved → PopulationManager.RemoveColonist`; roster membership is the idempotence guard. Job counts are **computed** from the roster — never add a counter back.
- Housing has ONE owner: buildings implement `IHousing`, `RegisterHousing` in Start, flag-guarded `UnregisterHousing` from both death and `OnDestroy` (demolish counts). PopulationManager never rescans the scene.
- Assignment, arrivals and recruitment REQUIRE a `PopulationManager` — `BaseBuilding.Awake` / `Hut.Awake` call `PopulationManager.EnsureExists()`.
- **`ReplaceUnit` before `Destroy`** when converting worker ↔ warrior, so the old body's OnDestroy finds nothing to double-count. Warriors occupy housing; dismiss returns the weapon, death loses it.
- **The player is not a colonist:** never in the roster, no housing, no job, no AIBrain, never scanned by enemies, **does not eat**. The first colonist arrives from the housing timer (~20s after the fire).
- **Food (2026-09-04):** `PopulationManager` eats `roster × foodPerColonistPerDay × Difficulty.FoodConsumptionMultiplier` per `DayNightCycle.CycleSeconds` as a fractional debt paid one `SpendFood(1)` at a time; `Hunger` is DERIVED from `starvedSeconds` (never stored), Hungry at 0.25 day (labor ×0.6 via `PopulationManager.LaborMultiplier` in the gather and construction ticks, no arrivals), Starving at 1 day (one departure per day: jobless → worker → warrior via `DismissWarrior`). `Worker.Leave()` → the Leave action (2.0, zero-cost gate) → `Destroy` at the cove = the normal removal path. The scene manager predates the fields: `<= 0 → default`, so the sim's off switch is `foodDisabled`, not 0.
- `Health.Die` with `destroyOnDeath = false` is the player's knock-out; `Health.Heal` refuses the dead, so the revive writes `currentHealth` directly. Player health is set up in `Awake` (the prefab's `HealthBar.Start` looks it up).
- Repair pricing: 25% of build cost per full repair, charged one whole unit at a time from fractional debt, committed only when `SpendResources` succeeds. `RepairAvailable` returns 0 when the next unit is unaffordable.

---

## Runtime uGUI Gotchas (menus, HUD, panels — anything built in code)

- A `VerticalLayoutGroup` with `childControlHeight = false` IGNORES `LayoutElement.preferredHeight`. If children size via `LayoutElement`, the group must control that axis.
- A control's `targetGraphic` needs `raycastTarget = true` or it is silently inert (`MenuBuilder.SimpleImage(..., raycast: true)` for any click surface, including invisible ones: scroll viewport, slider background, toggle box, drag handles).
- **Panel heights are computed** (`MenuBuilder.FitPanelHeight`) and must force a FULL recursive `LayoutRebuilder.ForceRebuildLayoutImmediate` — nested groups report zero before their own layout pass. Restoring a `ScrollRect` position needs `Canvas.ForceUpdateCanvases()` + a forced rebuild first.
- `[RuntimeInitializeOnLoadMethod]` fires ONCE per launch, so a system that must exist in every scene (`PauseController`) also subscribes to `sceneLoaded`. `sceneLoaded` runs after every `Awake` and before every `Start` — check which one a component consumes a value in before "configuring" it there.
- `GameSettings.Apply()` runs every frame of a slider drag — change-guard anything expensive.
- **`KeyBindings.cs` is the single source of truth for every gameplay key.** No `KeyCode` fields or literals anywhere else. Escape, mouse buttons and F3/F4/F6/F7 are reserved. Any modal wanting Escape must be checked by `PauseController` (`[DefaultExecutionOrder(-50)]`) before its own handler, like `MenuScreens.IsCapturingKey`.
- **Difficulty is a run snapshot** (`Difficulty.BeginRun()` in `MenuFlow.NewGame`, before the scene loads because `ResourceManager.Awake` reads it). `Difficulty.Active` returns Normal under `SimHooks.Simulating`.
- `MenuBuilder.RowDescription` is a sibling of its row, fixed 20px, one line (TMP cannot report wrapped height before a layout pass). **Inside a `ScrollColumn` a label MAY wrap with no fixed height** (the changelog bullets): the `ContentSizeFitter` re-measures each pass and the scroll region's fixed height keeps the panel deterministic. Hanging indent is `<indent>` rich text, not a nested layout.
- `MenuBuilder.Label` WRAPS by default — single-line captions set `TextWrappingModes.NoWrap` and a fixed `LayoutElement.minWidth`.
- Scroll viewports clip with `RectMask2D`, never a stencil `Mask` (a hidden Mask's alpha-0.001 graphic quantises to 0 and culls everything). The viewport still needs a `Color.clear` raycastable Image.
- Wheel over a scrolling list belongs to the list: `CameraController.PointerOverScrollView` does an `EventSystem.RaycastAll` and blocks zoom only when a hit has a `ScrollRect` above it. Never use `IsPointerOverGameObject` — the HUD makes it true almost everywhere.
- Every scrolling list gets a permanent slim scrollbar with an **opaque white** handle Image — a `ColorBlock` MULTIPLIES the graphic colour, the same reason a transparent-at-rest button needs an opaque white Image with the alpha in `normalColor`.
- Settings affecting things created at spawn must be read per frame at the effect site (`FloatingText.LateUpdate` state labels, `HealthBar.RefreshVisibility`) or they cannot be toggled mid-run.
- Victory/defeat are `MenuScreens.Screen.GameOver`, the name popup is `Screen.NameEntry`; `MenuScreens.Back()` early-returns on both (dismissing would strand the player). `GameManager.ContinuePlaying` clears `isGameOver` BEFORE closing the menu. Anything building UI on game over needs a `SimHooks.Simulating` guard.
- `PauseController.BlockGameplayInput` is true while any menu screen is open — gameplay clicks (`GameStartController`, `BaseBuilding.OnMouseDown`, HUD chips) must check it. A gameplay panel is not a menu: `WorkerAssignmentUI.IsOpen` is consulted by `PauseController.ModeActive` so Esc closes it instead of pausing.
- Draggable UI = `DraggablePanel.Attach(handle, panel, prefsKey, default)`: panel anchored + pivoted bottom-left, handle gets a raycastable `DragSurface`. The clamp reads a 0×0 canvas rect on the build frame, so call `Clamp()` again on open.
- The campfire/station panel's tabs are nested zero-padding `Column`s; `SwitchTab` refits and re-clamps. `OpenStation(station)` hides the Colonists/Stockpile tabs. The Queue tab is a pool of 8 rows reassigned on `CraftStation.Version`.
- HUD chip breakdown rows come from `ItemCatalog` (`hudListed` + `hudCategory`), so a new item appears under its category with no UI change. The panel is parented to the ENTRY so it follows its chip. Activity strings are composed only on change; slots repaint only on `Inventory.OnChanged`.
- Bottom-of-screen overlays must clear `PlayerHUD` (sort 45, strip height): `IntroHintCanvas` is sort 70 at y 150. The raid banner sits BELOW the resource bar (~900px wide from the left).
- **Right-click never opens a panel.** Right-click on the fire deposits and works the queue; left-click on its collider opens the panel via `BaseBuilding.OnMouseDown` (left button only). Keep the gestures apart.
- **Text-asset screens (Changelog, Information) never type a catalog number (2026-09-07).** Prose in `Resources/*.txt`; `@name` lines in `Information.txt` ask `MenuScreens.RenderInfoTable` for a live table (`BuildingDatabase.Instance` is null on the main menu — say so, don't crash). A screen with sub-tabs must bank the old tab's scroll itself and null `activeScroll` before `Rebuild()`, which banks under the SCREEN key. Six `TabRow` captions across `OptionsWidth` wrap at `ButtonSize` — drop them to `SmallSize + 2`, `NoWrap`.

---

## Visual / Art Gotchas

- Match the menu mockup's palette, water and silhouettes — not its DOF or composition. **No DOF in gameplay.** Test lighting at the real RTS camera angle.
- **Ortho camera has a NEGATIVE near clip** (-100, `CameraController.Awake`) so ground behind the camera plane is not sliced. Consequence: any depth-sampling screen effect (SSAO is OFF in `PC_Renderer.asset`, SSR, depth fog, depth outlines) is garbage at the bottom of the screen. Exponential fog whitewashes under ortho; use Linear (~30/80) if fog returns.
- URP shadow distance is driven from zoom by `CameraController.UpdateShadowDistance`; it caches and restores the asset's value in `OnDisable` (a Play-mode write to the URP asset persists to disk).
- `LightingPreset` SOs are the runtime source of truth for sun + ambient; `DayNightCycle.Start` forces `AmbientMode.Trilight`. A new serialized field must be written into **both** .asset files (a missing YAML key is 0, not the C# default). Night is a fixed moon pose (`moonElevation` / `moonYaw`) Slerp-blended through dawn/dusk.
- Bloom threshold stays 1.0; hero assets glow via HDR emission (~3). Hover feedback is **emission** (`HoverGlow` writes `_EmissionColor` + `_EMISSION`, glow = the slot's own colour pushed toward gold), never `material.color`, so it coexists with `OcclusionFade`'s alpha. Pickups glow only on hover (idle 0 / hover 3.1 @ blend 0.85); nodes hover 1.7 @ 0.4. Node prefabs serialize `hoverGlow` — a code default alone does not reach them.
- **`renderer.material` is slot 0 only; reading `.materials` INSTANTIATES.** Multi-submesh art needs `RendererTint.Collect` once in Start, then `SetColor` / `RestoreColors`. **One collector per object:** `ResourceNode.EnsureNodeMaterials()` is the single collector for a node, and `HoverGlow.Bind` / `OcclusionFade` take those same instances. Never read `.materials` in Update.
- `OcclusionFade` is runtime-added by `ResourceNode.Start` for Wood nodes (covers scatter palms); the manager's test is screen-space at 10 Hz (`SilhouetteTightness` 0.42, `UnitHalfWidth` 0.22, `DepthMargin` 0.6), not a raycast. `SetSurface` (opaque↔transparent keywords, `renderQueue` back to -1) runs only when the fade crosses full opacity.
- **Art is base-pivot at scale 1, mounted on a `Model` child; root scale stays 1.** Never assign an art mesh to a squashed root MeshFilter. Resetting a root's scale un-squashes `HealthBar` / `FloatingText` children — retune offsets. `NavMeshAgent.baseOffset` 0 for base-pivot art; agent radius/height are world-space, leave them.
- **A placement ghost, the player, and resource nodes must never be visible to the Default-layer ground raycast.** Ghost colliders are disabled + layer Ignore Raycast; node click boxes are layer 8 `Nodes`; pickup click boxes are layer 7 `Pickups` (added in `Awake`); the player prefab has no click collider. Ghost prefabs take the art MESH on their root renderer with one `Mat_Ghostbuilding` per submesh (no nested art prefab). Ghosts snap, they do not lerp.
- Walls cannot be plumbed by prefab swap (`WallConnector` overwrites the root MeshFilter at runtime) — re-material only.
- `LowPolyAssetGenerator` writes `Assets/Art/`; `LowPolyPlumber` mounts it on gameplay prefabs from a table — **an art category not in the plumber's table is invisible in-game.** `Tree.prefab` variants are art PREFABS (`TreeVariance` copies mesh + materials). A new unit category needs the enum value, the `Add…Impl` partial, a menu item and the showcase row.
- `GridOverlay` (F2, auto-shows in build mode, never G) draws only buildable cells as ONE `MeshTopology.Lines` mesh draped on the terrain, boundaries on half-offsets.
- `CombatEffects.FadeOutUnit` needs `GetComponentInChildren` (art lives on the Model child).
- `HudTimeDial` replaced `DayNightCycle.OnGUI`; day and night each map to their own half of the 0..1 parameter at their own rate.
- **Resource node scaling:** trunk-tight carve radii 0.45 / 0.5, `GatherRingRadius = obstacle.radius + 0.55`. Depletion shrink and gather wobble scale the **Model child, never the root** (root scale drives the obstacle → re-carve every tick). `SetupNavMeshObstacle()` overwrites serialized obstacle values; root transform scale still scales them.

### Terrain / Water Gotchas

- `TerrainGrid` does EVERYTHING in `Awake` (generate → chunks → water → deep-water NotWalkable volume below −0.4 → `BuildNavMesh()`), so every `Start()` finds a finished world. The surface is `CollectObjects.Children` + `PhysicsColliders` — only chunk colliders and the modifier volume feed the bake, so water and scatter must never be children of `Terrain`. Water is never static.
- **Every 150 m-map distance must scale with `TerrainGrid.SizeScale`** (Small/Medium/Large = 111/151/191 verts). A spawner or placement rule with a literal distance is wrong on two of three sizes.
- Seed flow: `TerrainGrid.RunSeed` set in Awake; `MenuFlow.NewGame` clears it, `Restart` keeps it; under the sim the inspector/sweep seed always wins.
- `IslandSettings.asset` is the tuning surface, rewritten from `CreateDefault()` only when the code's `CurrentVersion` is newer. `ScatterSettings.asset` is the reverse — `Build Scatter Settings` REWRITES it from `LowPolyScatter.Table` every run. Styles are applied by `IslandSettings.WithStyle` → read `TerrainGrid.ActiveSettings`, never the asset.
- **A cliff has to land inside ONE 1 m cell** (`cliffSharpness` 0.04; flood fill `maxWalkableStep` 0.9) — the agent climbs 0.75 m steps, so a two-cell step is climbable. Revisit both if spacing or agent climb changes.
- **Buildable = dry ∧ gentle ∧ REACHABLE** (`IsReachable` from the validator's flood fill). Ramp repair, seed reroll (`seed + attempt × 7919`), best-attempt fallback. Anchors (axis swing, land wedge cove→campfire, fixed-height cove shelf/ramp discs) make random seeds safe for the opening. Ponds only below 3 m.
- Bands are classified from the smooth field gradient at the centroid, not triangle normals (checkerboard otherwise). Adding a band = `Surface` enum + `Classify` line + `TerrainSetup.SurfaceMaterialKeys` + `LowPolyPalette` entry.
- Scatter rules are terrain-based, never radial; own `_Scatter` root; own `System.Random`; decor static-batched. **Gatherable / salvage rules build gameplay objects:** empty root with `ResourceNode` / `GroundPickup`, art on a child named exactly **"Model"** (load-bearing), under `_Scatter_Nodes`, never batched, rejected on unreachable ground, NavMesh-snapped. A depleted scattered palm respawns a broadleaf (accepted).
- `LowPolyScatter` grounds props by sampling the heightfield (chunk colliders only exist in Play mode). `TerrainSetup` snaps props, not their group roots.
- `FlattenArea(center, radius, blend)` rebuilds touched chunks, kicks `UpdateNavMeshAsync`, and re-bakes the water depth map. Ghost previews at the center height = where the pad ends up.
- **Water depth comes from the heightfield, not the camera depth texture** (`_HeightMap` / `_MapParams` on the renderer's property block; clamped, border texels are open-ocean — never Repeat, never let land reach the border). Ortho has no specular spot: sheen is a soft `pow()`, sparkle is per-facet (`FacetValue`, quad count forced even so vertices land on `_GridStep` multiples), wavelengths ≥ 6× grid step. Tuned values live in `Mat_Water.mat`, not shader defaults. Shaders are not Roslyn-checked — a broken one renders magenta.
- `BaseBuilding.GetValidSpawnPosition` has no `+1` after `SamplePosition` (flat-world relic).

---

## Items, Crafting, Research, Player Gotchas

- Two `Collect` methods on `GroundPickup`, don't unify: workers get `amount` of `resourceType` into the carry (plus the material on `bb.carryItem`, banked to the stockpile on delivery); the character gets `itemId × itemAmount` or the resource in hand. `allowOverfill` on salvage (a 6-food crate must not evaporate into a 5-carry). Only what `PickupSpawner` placed is `spawnerOwned` (respawn budget) — salvage and shed byproducts are not.
- `ItemKind` says which layer an item lives in: resources exist only in hand (deposit → pool), materials go to the campfire `Stockpile`, tools stay in the player's hands, equipment is consumed per warrior. `ItemCatalog.Stockpiled` = materials only. One colony store: the Workshop resolves to `BaseBuilding.FindAlive().Stockpile`.
- Stockpile capacity is a delegate (`Inventory.totalCapacity`): base 60 + `CraftedUpgrades.StockpileRoom`; `Add` / `SpaceFor` clamp, overflow is lost.
- **A queue nobody stands at does not move** (`CraftStation.AddLabor`; `IsWorked` = labor in the last 0.5s; one laborer holds the bench). The player's `TaskKind.Work` walks to the approach point; any other command leaves via `StopWork`. **Costs are paid on completion; a short entry WAITS at 100%** ("Waiting for 2 Stick"), never fails. Research is de-duplicated across stations.
- **Jobless = utility labor; a specialist is jobless but NOT idle (2026-09-07).** `Worker.specialty` (`Any` / `Builder` / `Crafter` / `Repairer`) replaces the Crafter job; `Worker.IsIdle` (`!hasJob && Any && !leaving`) is the ONLY idle test (idle count, `FindIdleColonist`, recruits). `SpecialtyAllows(trade)` is a zero-cost gate beside `IsJobless` on Build / Craft / Repair, `SpecialtyAllows(Any)` on Forage (specialists never tidy); the research gates stay inside the scans and `BaseBuilding.UnlockFor` mirrors them for `AssignSpecialist`. The order is `LaborPriorities` (colony-wide 1.0 / 0.95 / 0.9 / 0.85 statics, four sliders on the Colonists tab, read live by the `LaborPriority` consideration; base priorities are 1.0 and a weight of 0 is the off switch) and it only breaks distance ties (every scan floors at 0.15). Left-click on a jobless colonist opens the panel there. The bench has two "mine"s: `CraftStation.Claim/Release` is who WALKS there (any jobless colonist; two spread over two benches), `AddLabor`'s laborer is who WORKS it — the player never claims and always preempts (`AddLabor` refuses a busy bench to everyone but a `PlayerCharacter`; the colonist waits beside it). Workshop speeds `{2, 2, 1, 1}`: making is fast, research is 1× everywhere.
- Stations and `RaidDirector` are **runtime-added in `Awake`**, so their `public` fields are the LIVE values — never add them to a scene or prefab by hand (the inspector copy would silently win).
- **Every gate is one `Unlocks.Has(...)` at the site that already decides the action** (`AssignWorker`, `CanRecruitWarrior`, `StartPlacement`, `SelectBuilding` for the Workshop, first line of `ConstructionAvailable` / `RepairAvailable`). Locked UI names its research via `Unlocks.ResearchTitleFor` → `ResearchCatalog.TitleGranting`. `Unlocks.Has` is NOT true under the sim — policies research like a player.
- **A research hands over its tool** (`ResearchDef.tool`, delivered by `CraftStation.TryComplete` to whoever stood at the bench); there are no tool recipes. A new tool = a `tool =` line on a research entry.
- **A warrior costs a weapon + 15 food + an idle colonist.** `SpawnWarrior` takes `BaseBuilding.SelectedWeapon` (the Arm-with picker; defaults to the best in stock) and sets `Warrior.weapon` right after `Instantiate`, before `Start` copies its `EquipmentDef` into damage / range / cooldown (before `SimOverrides.Apply`). `ItemCatalog.WoodenSpear` = 25 / 2 / 1.2, the live prefab numbers — retune the spear, not the prefab.
- **`ItemCatalog.Weapons` is a RANKING, best first** — the picker's default, `FirstWeaponInStock`, the sim and `BetterWeaponInStock` (never crosses melee↔ranged) all read it; a new weapon goes in at its rank. **`Warrior.ApplyWeapon` is the only place weapon stats land** (Start and `BaseBuilding.RearmWarrior`); it refreshes the blackboard and stopping distance. Rearm is peacetime-only, priority 0.5, zero momentum.
- **An archer is a warrior whose weapon says `ranged` (2026-09-04)** — no subclass, no second action: `bb.isRanged` flips `EngageEnemyExecutor.AttemptAttack` to `CombatEffects.FireArrow` (pool of 32 `Projectile`s, one shared mesh/material, damage decided at loose time, flies headless, no LOS). The range hold IS the existing edge-distance range check at reach 9; no kiting. `Warrior.ShowBody` toggles the plumber's `Model` / `Model_Archer` children — a prefab without the alt body just keeps the spearman.
- `PlayerCharacter.ToolFor` gates hand-harvest: tree → Stone Axe, rock → Stone Pick, ore → Metal Pick, food free. A new node type needs a line or it harvests bare-handed. Reach is measured to the node CENTRE against `GatherRingRadius`. `GatherResources(..., shedByproducts)` sheds sticks/chunks for workers (every 4 units, `maxLooseByproducts` 2) and gives them in hand to the player; a full inventory calls `ResourceNode.ShedOneByproduct()` instead (the pooled resource never overflows to the ground — a stick is worth 3 wood).
- Deposit has two reaches: any ground click within `DepositClickRadius` 3.5 of the fire's collider EDGE deposits; arrival is `DepositEdgeDistance` 2.4. `PlayerCharacter.Stalled()` (0.5s of no path and no velocity) counts as "as close as the NavMesh allows": within 2.5u the interaction happens, beyond it the task drops.
- `HeldItem` is visual only; nothing reads the held tool. `PlayerProfile.Name` never returns empty; the popup is skipped under the sim and on Restart, and the sim never writes PlayerPrefs. `FloatingText.alwaysShow` bypasses the state-label setting for the name label. `CameraController.CenterOn` intersects at the target's own height.
- **A `public float` on a unit script is DEAD DATA — the prefab value wins;** values assigned in `Start()` make the prefab the dead data. Change both and keep the comment honest. `ConstructionSite.LaborFactor` is a const for this reason.
- Large pickups scale the ROOT (the click collider is added to the root in Awake). Counts and sizes are `PickupSpawner` scene values.

---

## Opening Sequence & Startup Contract

- `GameStartController` phase machine `Landing → PlacingCampfire → Settling → Colony`; `Phase` returns `Colony` when no controller exists. During the intro `BuildPlacement` is disabled, `DayNightCycle.clockPaused` holds the clock, and the campfire is absent. **Anything that assumed a campfire at frame 0 must subscribe to `GameStartController.OnColonyStarted` or poll `BaseBuilding.ActiveList`.** The campfire is deliberately not a `BuildingType`.
- `Campfire.prefab` is the single source of truth (the setup tool bakes old scene overrides in and deletes the scene instance).
- Startup order: `TerrainGrid` (-100) → `SimRunner` (-1000, sim only) → `PauseController` (-50) → everything else.
- Debug flow (F4): yield a frame after spawning the campfire AND after spawning huts before assigning workers (housing registers in Start). Skip-to-day increments `currentDay` only when crossing midnight. Runtime huts need `layer = "Buildings"`.
- `RaidDirector.RollForTonight` runs from `OnDayStart` plus once in `Start` for day 1. `GameManager.daysToSurvive` has no `FormerlySerializedAs` from `nightsToSurvive` on purpose. `GetDaysSurvived` = calendar day − 1 in both endings.

---

## Balance Simulation Harness (`docs/SIMULATION.md`)

- **NEVER speed a sim up with `Time.timeScale`** (frame-based AI budget and NavMesh throttles would report AI starvation as balance). `SimRunner` sets `Time.captureDeltaTime = 1/60`; measure with `Time.time`.
- Reset `Time.timeScale = 1f` at the start of every run (game over sets 0 and it survives scene loads); `maxWallSecondsPerRun` failsafe. `AddComponent<T>()` runs `Awake` immediately — create the GameObject inactive, populate, activate.
- Unit knobs: `SimOverrides.Apply(this)` at the top of each unit's `Start`. `terrainSeed` inside `TerrainGrid.Awake`. Other scene knobs from `sceneLoaded`, except run 0 (set in `Bootstrap`) and anything consumed in an Awake (`ResourceManager` — write the live pool too). `-1` is the don't-override sentinel.
- `SimBuilder` mirrors `GhostPlacer.ConfirmPlacement` / `WallLinePlacer.ConfirmWallLine` step for step — change both together. Wall rings must have gaps. `SimTools` passes `MainIsland` to `BuildPipeline` explicitly.
- `SimPlayerDriver.Tick` (polled before every policy tick) drives the player character as bench labor. Policies keep one idle colonist while any site exists.
- **Not run-to-run deterministic** (async NavMesh rebuilds, job-system order, `-Parallel` contention): a seed makes runs comparable, not reproducible. n=12 win-rate CI ≈ ±13pp; read `campfire_hp_min` in `days.csv` first. Never report a one-or-two-run win-rate difference as a finding.
- Sweeps are NOT comparable across: gatherable palms (09-02), salvage, colonist pool, player character (starts with 0 colonists), research/craft split (0 research, 0 spears), hauled materials, foraging (09-03), the Crafter job and food consumption (09-04). Re-run baselines after any of these.
- A script-only player build rewrites `<player>_Data/Managed/Assembly-CSharp.dll`, not the .exe timestamp. Never let two sim processes share one `outputDir` (`run-sim.ps1 -Parallel N` shards). ~23–25× realtime per process.
- Sim-guards to keep: `Difficulty.Active` → Normal; name popup skipped; `PropScatter` runs gatherable + salvage rules but not decor; `OcclusionFadeManager` skipped; `GameManager.ShowEndScreen` guarded; `daysToSurvive` guarded behind `!SimHooks.Simulating` in `GameManager.Start`.

---

## Engine / Tooling Gotchas

- **Compile-verify without opening Unity:** Roslyn `csc.dll` (`Editor/Data/DotNetSdk/sdk/*/Roslyn/bincore/`) against `Editor/Data/Managed/UnityEngine/*.dll` + package DLLs, in three configs (editor `UNITY_EDITOR`, `DEVELOPMENT_BUILD` player, release player). Quote `-r:` paths in the .rsp (the install path has a space). Do NOT reference `UnityEditor.dll` alongside `UnityEditor.*Module.dll` (spurious CS0433). Five `DayNightCycle` editor-GUI field warnings are pre-existing.
- Edit `manifest.json` BEFORE launching the editor. Batchmode cannot run while another editor has the project open (`Temp/UnityLockfile`). Read the project-relative `Logs/Editor.log`.
- The upgrade flips `VersionControlSettings.asset` to Unity Version Control — set it back to **Visible Meta Files** if it reappears in a diff.
- **`EditorBuildSettings.asset` still lists only `SampleScene`** — a build made now ships the empty scene. Fix via File > Build Profiles before building.
- `.gitignore` covers `*.sln` and `*.slnx`. Assets re-serialize lazily on save — expect scene/prefab diffs the first time each is edited after an upgrade. New C# files get `.meta` on first editor focus.

---

## Logging Conventions

Console kept quiet on purpose (212 → 65 calls). **Before adding any `Debug.Log`, check the keep-list; if it doesn't fit, don't add it.**

- **Keep:** all `Debug.LogError`; `LogWarning` for recoverable misconfiguration (missing prefab/ref, duplicate singleton, no UI assigned, spawn fallback); `Debug.Log` only for once-per-night / once-per-game lifecycle: day/night transitions, raid summary, GameManager init / progress / VICTORY / DEFEAT, CAMPFIRE DESTROYED, ResourceManager init, TerrainGrid load line.
- **Never:** per-unit spawn/death, per-damage/heal, per-resource tick, per-button click, audio chatter, per-placement, helper init banners, PopulationManager add/remove, agent-configured dumps.
- Rule of thumb: would it spam a normal 5-minute session? Use the F3 overlay for live state instead.

---

## How to Run

1. Open `islandrts/` in Unity Hub (6000.5.9f1). **Run `Tools > Island RTS > Setup Everything (In Order)`** after pulling anything that touched art, prefabs or scene wiring — idempotent, and its order is load-bearing (Generate All Assets → Plumb Everything → Setup Opening Scene → Build Scatter Settings → Setup Terrain Scene → Setup Pickups + Workshop → Remove Legacy Victory-Defeat Panels → Setup Main Menu Scene). It leaves `MainMenu` open; `Open Game Scene (MainIsland)` jumps to gameplay.
2. Press Play. Name your castaway, right-click to walk ashore, **B** to place the campfire (free, one-time).
3. Hand-collect sticks and stone chunks, deposit at the fire (right-click), research **Woodcutting** on the campfire panel's Research tab (left-click the fire; the character must stand at the bench). Research opens jobs, build mode and Spearcraft; craft Wooden Spears to arm warriors.
4. Colonists come ashore on their own as housing allows; assign jobs on the Colonists tab; press B to build.

## Controls

| Key | Action |
|-----|--------|
| WASD / Arrows · Q / E · Wheel · Middle-drag | Pan · rotate · zoom · tilt/rotate |
| B | Build mode (opening: place the campfire) |
| 1–6 | Hut · Wood Wall · Stone Wall · Watchtower · Workshop · Shipyard (beach only) |
| G / R / Shift | Wall → gate · L-path toggle or rotate · Bresenham staircase |
| Delete / X | Demolish (50% refund) |
| F2 / F3 / F4 / F6 | Grid overlay · AI overlay · Debug menu (cheats) · Perf logger |
| Left-click campfire / Workshop | Panel: Colonists (jobs, Specialists, warriors) · Stockpile · Craft · Research · Queue |
| Esc → INFORMATION | Field guide from `Resources/Information.txt` + live catalog tables (also on the main menu) |
| Right-click | Character smart command: fetch · hand-harvest · deposit + work the queue · work a bench · walk |
| Space | Centre camera on the character |

All rebindable in *Options → Controls*. Esc, mouse buttons and F3/F4/F6/F7 are reserved. **Full controls + playtest checklists: `docs/CONTROLS_AND_CHECKLIST.md`** — keep in sync when a binding changes.

---

## Current State (2026-09-07)

Branch `feature/balance-sim-and-menus`. Slices 3–6 of `RESEARCH_AND_DAYS_PLAN.md` landed one commit each (2026-09-04); the utility-colonist fold and the Information screen are uncommitted (2026-09-07).

**Shipped:** four-resource economy with a colonist pool (arrivals by housing, jobs from the idle pool, jobless colonists build/craft/repair/forage with Builder/Crafter/Repairer specialists); player character with hand-harvest and campfire deposit; research → craft split with stations (campfire + Workshop), spears as per-warrior equipment with a recruit picker and the Iron Spear; 30-day calendar with dawn-rolled, prosperity-scaled raids; walls/gates/towers/demolish; Utility AI for every unit; random islands (size/style/seed) with terraces, cliffs, ponds, stylized water, runtime scatter; tree occlusion fade; code-built menus, end screens, an in-game changelog and an Information field guide (story tab empty); F4 debug menu; headless balance sim.

**Pending playtest:** everything in `DevQuests.txt` (Slices 3–6 need Setup Everything first for the archer body and the Shipyard assets; utility colonists, priorities, Information and the dev quests themselves are 09-07). `DevQuests` is editor/dev-build only (`const Enabled`, `#pragma 0162`); quest ids are batch-slug + index, so append rather than insert. A raid tuning pass is pending.

**Next:** playtest the above; write the Story tab; a tutorial (agreed shape: a reactive step list over a normal run, built on the intro hint canvas, not started because the game is still moving); then `COLONY_EXPANSION_PLAN.md` (collector radius, settlement tiers, processing chains, families, farming). Phase 10 Stages 3–4 (water polish, lighting bake) remain open.

---

## Balancing Reference

| Unit | HP | Damage | Attack Speed | DPS |
|------|----|--------|-------------|-----|
| Warrior (Wooden Spear) | 75 | 25 | 1.2s | 20.8 |
| Warrior (Iron Spear) | 75 | 35 | 1.2s | 29.2 |
| Archer (Bow, range 9) | 75 | 12 | 1.0s | 12 |
| Enemy | 50 | 10 | 1.5s | 6.67 |

| Building | Cost | HP |
|----------|------|----|
| Hut | 20W 10F | 100 |
| Wooden Wall / Gate | 15W 5S | 150 / 75 |
| Stone Wall / Gate | 10W 20S | 300 / 150 |
| Watchtower | 25W 15S | 200 |
| Workshop | 30W 20S | 150 |
| Shipyard (beach only, 45s×2 build; Shipwright 40W 30S 10M) | 200W 120S 30M | 300 |
| Warrior | 1 Wooden Spear + 15F + an idle colonist | 75 |
| Wooden Spear (craft, 10s) | 3 stick 1 chunk 5W | — |
| Iron Spear (craft, 12s; Iron Work 20W 25S 10M) | 2 stick 5W 4M | — |
| Bow (craft, 12s; Bowyery 20W 5F) | 4 stick 5W | — |

- Starting resources 100W 50F 0S 0M (metal buys Iron Work and Iron Spears). Workshop makes tools and weapons at 2×. Worker carry 5, gather 1/sec; stick = 3 wood, chunk = 3 stone, crate = 6 food, barrel = 5 wood; large pickups ×3.
- Raid size `round(2 + 0.4 × day + 0.08 × prosperity)`: day-4 ≈ 5, day-20 ≈ 16, day-30 ≈ 22; roughly every 3 days, spawned 0.4s apart so a raid arrives as one body.
- Food: 1 per colonist per calendar day (× difficulty 0.5–1.5); Hungry after 0.25 day (labor ×0.6, no arrivals), Starving after 1 day (one colonist leaves per day). Warrior heal at campfire 1.5 HP/s (deliberately slow). One builder finishes a site in `buildTime × 2` (10s), up to 3 stack. Repair = 25% of build cost per full repair. Stockpile 60 (+40 Storage Pits, +80 Racks and Baskets).
