# Island RTS Game

A Unity-based real-time strategy survival game. Manage autonomous workers, gather resources, build defenses, recruit warriors, and survive escalating nightly enemy raids.

**Genre:** Top-down RTS + Survival  
**Setting:** Age of Sail shipwreck on an uncharted island  
**Status:** Playable alpha (Phase 6.26 + Opening Sequence Stage 1 + Terrain T1–T4) — the game opens with the story beat (a lone survivor wades ashore from the shipwreck and places the campfire himself; applied in-scene via the Opening Sequence setup tool) and the world is a procedurally generated island that is different every game (size, terrain style and seed picked on the New Game screen). All of that is applied by editor tools that must run in a specific order — **`Tools > Island RTS > Setup Everything (In Order)` does all eight in one go.** An F4 debug menu (editor/dev builds) covers resource grants, a quick-start colony, time controls, and combat cheats for playtesting. Phase 10 Stage 2 (low-poly art) is applied in-editor. Now on Unity 6000.5.9f1; Phases 6.24–6.26, the opening sequence, the terrain, the art plumbing, the menus, the settings/keybinding/difficulty pass and the rebuilt end screens all still await a proper playtest — the accumulated checklist, and the editor setup steps that must run first, are in [`docs/CONTROLS_AND_CHECKLIST.md`](docs/CONTROLS_AND_CHECKLIST.md).

---

## Quick Start

1. Clone the repo
2. Open `islandrts/` in Unity Hub (requires **Unity 6000.5.9f1**)
3. Open scene: `Assets/MainIsland.unity`
4. Press Play

> **Note:** `Assets/Scenes/SampleScene.unity` is the leftover stock Unity scene (3 objects) and is *not* the game. Build Settings still points at it — fix that before making a build.

### First Game

1. From the title screen, **NEW GAME** → pick a difficulty (Normal is the intended balance) → **BEGIN**
2. Opening: **right-click** to walk your survivor ashore, then press **B** and click to place the campfire (free, must be near him — he becomes your first worker)
3. Click the campfire to assign 5-6 workers (wood + food)
4. Press **B** to build — place 1-2 Huts for housing
5. Recruit 2-3 warriors before nightfall
6. Survive 5 nights to win

> **First time in a fresh clone, run `Tools > Island RTS > Setup Everything (In Order)` once.** The art library, opening sequence, prop-scatter settings, island terrain, pickups/workshop and menu scene are all applied by editor tools, and the order they run in matters — the master item does all eight in dependency order and leaves `MainMenu` open, which is what a build starts on. `Tools > Island RTS > Open Game Scene (MainIsland)` skips the title screen. Every step is idempotent, so re-run it after pulling art or a generator defaults pass. A `skipIntro` toggle on the `GameStart` object restores the classic instant start.

---

## Controls

| Key | Action |
|-----|--------|
| **WASD** / Arrows | Pan camera |
| **Q / E** | Rotate camera |
| **Mouse Wheel** | Zoom |
| **Middle Mouse (drag)** | Tilt (vertical) / rotate (horizontal) camera |
| **B** | Enter build mode |
| **1-5** | Select building (Hut, Wood Wall, Stone Wall, Watchtower, Workshop) |
| **G** | Convert wall to gate (in build mode, hover over a wall) |
| **R** | Toggle wall path direction / Rotate building |
| **Shift** (hold) | Diagonal wall path |
| **Delete / X** | Demolish building (50% resource refund) |
| **Esc** | Cancel the current action (build ghost, wall line, demolish, crafting panel) — opens the pause menu when nothing is active |
| **F2** | Build grid overlay (also auto-shows during build mode) |
| **F3** | AI debug overlay (editor only) |
| **F4** | Debug menu — cheats for testing (editor + dev builds only) |
| **F6 / F7** | Perf recorder — F6 marks the frame where you saw a stutter, F7 stops/resumes (editor + dev builds only) |
| **Right-click** (opening) | Move the survivor |
| **B** (opening) | Place the campfire (Esc / right-click cancels) |

Every gameplay key above is a **default, not a fixed binding** — *Options → Controls & Keybindings* rebinds all of them, with a main and an alternate slot per action. Esc, the mouse buttons and the debug keys are deliberately reserved: Esc in particular is a back-out gesture five systems consume in order, not a binding.

Full reference — including how to drive the F3/F4 debug tools, the editor setup run order, and the current playtest checklist: [`docs/CONTROLS_AND_CHECKLIST.md`](docs/CONTROLS_AND_CHECKLIST.md).

---

## Tech Stack

| Component | Version |
|-----------|---------|
| Unity | 6000.5.9f1 |
| Render Pipeline | URP 17.5.0 |
| Pathfinding | AI Navigation 2.0.14 |
| UI | TextMeshPro + uGUI |
| Input | Input System 1.20.0 |

### Required Packages

All packages are included in the project manifest. No manual installation needed.

- AI Navigation (NavMesh)
- TextMeshPro
- Universal Render Pipeline (URP)
- Input System

---

## Project Structure

```
islandrts/Assets/
├── Scripts/
│   ├── AI/                      # Utility AI system (35 files)
│   │   ├── Core/                # AIBrain, ActionOption, Consideration, ResponseCurve
│   │   ├── WorldState/          # Enemy density grid, global state
│   │   ├── Considerations/      # 13 scoring inputs (health, threat, distance, etc.)
│   │   ├── Executors/
│   │   │   ├── Worker/          # Gather, Return, Flee, Idle
│   │   │   ├── Warrior/         # Engage, Intercept, Defend, Patrol, Retreat, Heal
│   │   │   └── Enemy/           # EnemyAttack (single action + priority-based targeting)
│   │   ├── Shared/              # NavMesh throttling, stuck detection
│   │   └── Debug/               # F3 debug overlay
│   ├── UI/                      # Menus (runtime uGUI) + GameSettings, KeyBindings, Difficulty
│   ├── Sim/                     # Headless balance-simulation harness (editor + dev builds only)
│   ├── GameManager.cs           # Victory/defeat, statistics
│   ├── ResourceManager.cs       # Wood/food/stone economy
│   ├── Worker.cs                # Worker unit + AI setup
│   ├── Warrior.cs               # Warrior unit + AI setup
│   ├── Enemy.cs                 # Enemy unit + AI setup
│   ├── BuildPlacement.cs        # Building placement, wall drawing, demolish
│   ├── Terrain/                 # TerrainGrid (chunked island mesh + runtime NavMesh), IslandGenerator pipeline, IslandSettings/IslandOptions, PropScatter
│   ├── GameStartController.cs   # Opening sequence (survivor landing → campfire → colony)
│   ├── DebugMenu.cs             # F4 cheat menu (editor + dev builds only)
│   ├── WallGrid.cs              # O(1) wall/gate grid registry
│   ├── WallConnector.cs         # Procedural wall mesh generation
│   ├── Health.cs                # Universal health component
│   ├── DayNightCycle.cs         # Day/night with lighting
│   └── ...                      # 38 root-level scripts total
├── Editor/
│   ├── LowPoly/                 # Procedural low-poly asset generator (editor-only)
│   ├── Sim/                     # Sweep runner menu items + headless sim player build
│   ├── FullSetup.cs             # Runs all eight setup steps in dependency order
│   └── MenuSceneSetup.cs        # Creates the MainMenu scene, fixes the build scene list
├── Art/                         # Phase 10 low-poly art library + showcase scene
├── Prefabs/                     # Units, buildings, resources
├── Materials/
├── Audio/
├── MainIsland.unity             # Main game scene
├── Scenes/
│   └── SampleScene.unity        # Stock Unity scene, unused
└── Settings/                    # URP pipeline configs + LightingPresets
```

---

## Architecture Overview

### Utility AI

All units (Workers, Warriors, Enemies) use a **scoring-based Utility AI** — no state machines. Each unit has an `AIBrain` that evaluates `ActionOptions` by multiplying `Consideration` scores (0-1) shaped by `ResponseCurves`. The highest-scoring action runs its `ActionExecutor`.

- Evaluations staggered at 0.25-0.35s per unit (randomized)
- Per-frame evaluation budget **scales with population** — `clamp(activeBrains × deltaTime / 0.25s, 5, 64)`, so each unit's think rate stays constant as the colony grows
- A brain that loses the budget race **defers** its evaluation to the next frame rather than dropping it
- 20% commitment threshold prevents action flip-flopping
- `ForceReeval()` jumps the queue for instant response to events, but is still budgeted (up to the hard ceiling) so a mass event can't spike a frame

### Key Patterns

- **ActiveRegistry\<T\>** — Generic static registry for O(1) entity lookups (Workers, Enemies, Walls, etc.). The codebase contains zero `FindObjectsByType` scene scans — every lookup goes through a registry.
- **Singletons** — ResourceManager, AudioManager, WallGrid, AIWorldState, GameManager
- **NavMesh throttling** — Max 20 SetDestination/frame, 2 CalculatePath/frame
- **Zero GC in hot paths** — No per-frame heap allocations in Update or AI evaluation
- **Event-driven** — Static events for day/night transitions, unit deaths, wall destruction

### Building System

- **ScriptableObject-driven** — `BuildingData` assets define costs, prefabs, placement rules
- **Wall line drawing** — Click start + click end, L-shaped or Bresenham staircase paths
- **Procedural meshes** — Walls auto-connect with 6 shapes based on neighbor bitmask
- **WallGrid** — O(1) dictionary tracks wall/gate occupancy for placement validation

---

## Game Systems

| System | Description |
|--------|-------------|
| **Opening** | Survivor lands at the wreck, walks ashore, places the campfire (day/night clock held until it's lit). |
| **Economy** | Wood, food, stone, metal. Workers gather autonomously (cutters, foragers, quarriers, miners). Carry capacity 5, 1/sec rate. Metal has no cost sink yet. |
| **World** | A new island every game: size (110 / 150 / 190 m), terrain style (Rolling / Terraced / Rugged) and an optional seed are picked on the New Game screen and locked for the run. Plateaus, cliffs, ramps, ponds, rocky and sandy shores; every plateau is guaranteed reachable. |
| **Pickups** | Sticks and stones scattered on the island (+3 wood / +3 stone). Wood and stone workers detour to collect nearby ones; they trickle-respawn. |
| **Building** | Hut (housing), Wooden/Stone Wall, Gate, Watchtower, Workshop. Placement flattens a pad in the terrain. Construction sites auto-complete. |
| **Crafting** | Click the Workshop for one-time upgrades: Sharpened Tools (+30% gather), Sturdy Scaffolds (+50% build speed), Forged Blades (+30% warrior damage). |
| **Combat** | Warriors auto-engage enemies. Watchtowers buff nearby warrior damage 1.25x. |
| **Day/Night** | 120s day / 60s night. Enemies spawn at night — `5 + (night-1) x 2.25`, rounded (5 / 7 / 10 / 12 / 14), 0.4s apart so a wave lands as one body. |
| **Healing** | Warriors heal 1.5 HP/sec at campfire between waves — slow on purpose, so damage carries across nights. |
| **Flee** | Workers garrison inside the nearest hut when enemies threaten, and pop back out when it's clear (or if the hut falls). |
| **Demolish** | Delete/X key. 50% resource refund. Campfire protected. |
| **Victory** | Survive 5 nights (7 on Hard, 10 on Brutal). Defeat if the campfire is destroyed. Both end on a screen offering Restart / Main Menu / Quit, plus Keep Playing after a win. |
| **Difficulty** | Peaceful / Relaxed / Normal / Hard / Brutal / Custom, chosen on New Game and **locked for the run**. Scales raid size, enemy health and damage, night length, starting resources, and nights to survive. |
| **Menus** | Main menu (own scene), New Game, Esc pause, options (audio / video / camera / interface, saved to PlayerPrefs), rebindable controls, credits, confirm dialogs, victory/defeat. Built at runtime — see [`docs/MENU_WIREFRAMES`](docs/MENU_WIREFRAMES.md). |
| **Balance sim** | Headless autoplay: scripted strategies play full games and write CSVs, so balance is measured rather than guessed. See [`docs/SIMULATION.md`](docs/SIMULATION.md). |

---

## Logging

The console is intentionally quiet. Only three categories of log fire at runtime:

1. **Errors** (`Debug.LogError`) — missing prefabs, null refs, misconfigured scene objects. Should never appear in a healthy build.
2. **Warnings** (`Debug.LogWarning`) — recoverable misconfigurations (missing audio clip, no renderer for hover, homeless workers after hut loss, resource spawner fallbacks).
3. **Key lifecycle events** (`Debug.Log`) — day/night transitions, wave spawn summary, night-survived progress, victory/defeat banners, campfire destroyed banner, resource-manager init line.

Everything else (per-unit spawn/death, per-damage, per-resource tick, per-button-click, audio fades, build-placement chatter) has been removed. See [`.claude/CLAUDE.md`](.claude/CLAUDE.md) **Logging Conventions** for the full keep-list and rules when adding new logs.

---

## Development

For detailed technical documentation, AI system internals, balancing data, phase history, and gotchas, see [`.claude/CLAUDE.md`](.claude/CLAUDE.md).

Latest: **First playtest of the new world — water and HUD fixes.** The sea flipped light across the whole screen at some camera angles, with scattered dark tiles and, zoomed out, big diagonal stripes. Two causes: the camera is orthographic, so a hard-stepped specular has no highlight *spot* and simply switches the entire flat plane on or off with tilt and heading; and the 3 m water grid was sampling a 4 m wave, which aliased into a slow beat pattern. The shader now uses a soft sheen that cannot flip, gets its sparkle from per-triangle tone drift and glints that don't depend on the view, and runs on a 1.5 m grid with waves 11–20 m long. The resource bar is one continuous strip of five equal entries with thin dividers — big amount on top, `Wood · N workers` on one unbroken line beneath, worker count dimmer than the number — and the campfire panel opens bottom-left and can be dragged by its title bar (clamped to the screen, position remembered).

Before that: **A random island every game, and a world worth picking.** The island generator became a settings-driven pipeline (`IslandSettings` asset: shape → relief → terraces → ponds → seabed → detail → anchors → validation). Every NEW GAME rolls a fresh island — restart replays the same one — and the New Game screen gained a **World** section: island size (110 / 150 / 190 m), terrain style (Rolling / Terraced / Rugged) and an optional seed. Landforms are broad and gradual with stepped plateaus whose edges are true cliffs in places and walkable ramps in others; a flood-fill validator from the campfire site carves ramps to any cut-off region and rerolls seeds that still fail, so every plateau is reachable and the survivor's walk from the cove never meets a cliff. Seven terrain material bands (wet sand, sand, three grass tones, rock, cliff) are classified from the smooth field so hillsides read as one flowing band, with valleys dark and plateau tops dry. Environment props are scattered at runtime from terrain rules instead of baked into the scene, and are deliberately sparse. Resource nodes are placed by habitat — forests in the low dark ground, bushes on meadows, stone on high or broken ground — with a new **metal** resource from ore nodes on the plateaus, gathered by a Miner job (nothing costs it yet). The resource bar and campfire panel were rebuilt in code on the menu widgets (four resources plus housing, every chip a shortcut to the panel), and the placeholder grey sea is now a hand-written stylized water shader: depth-based turquoise-to-blue, drifting shoreline foam, gentle low-poly waves and a stepped sun glint. `Tools > Island RTS > Terrain > Preview Island Seeds` renders twelve islands to PNG without Play mode for tuning. Re-run `Setup Everything (In Order)` to apply.

Before that: **Settings with real depth, and the end screens finally lead somewhere.**

The options screen went from 12 settings on three tabs to 22 on four, and grew the two things it had been stubbing out. **Key rebinding is real**: `KeyBindings.cs` is now the single source of truth for all 17 gameplay actions, with a main and an alternate slot each — which is how WASD *and* the arrow keys, or Delete *and* X, work without a special case. No script holds a `KeyCode` of its own any more, and camera panning stopped reading Unity's fixed `"Horizontal"`/`"Vertical"` axes, which had made pan the one action visible on the Controls screen that couldn't actually be changed. Binding a key already in use takes it from the old action rather than throwing a modal error mid-remap. **Resolution is real too** — a proper display-mode and resolution list, plus a frame cap. New alongside them: a Camera tab (zoom and rotation speed, invert tilt, screen shake as a 0–1.5× slider instead of a toggle), an Interface tab (UI scale, health-bar mode, unit state labels, pause-when-unfocused), and per-row description lines so a setting says what it does.

**Difficulty** arrived with them: six presets from Peaceful to Brutal plus an editable Custom, picked on a new NEW GAME screen and *locked for the run* — waves already spawned wouldn't retroactively change, and "survived five nights" stops meaning anything if night four can be softened from the pause menu. The presets show their actual multipliers rather than just a label. Every knob is read at the point of effect, and `Difficulty.Active` forces Normal during a balance sweep so a difficulty saved in a developer's PlayerPrefs can't silently skew simulated runs.

Wiring that up turned up a quiet harness bug worth flagging: `SimRunner` set `ResourceManager.startingWood/Food/Stone` from the `sceneLoaded` callback, but `ResourceManager` consumes them in `Awake` — which runs first. Those three sweep knobs had never done anything. Fixed; any past sweep that varied starting resources needs re-running.

Finally, **victory and defeat moved onto the menu system**. They were the last screens still hand-built in the scene, and the real problem wasn't that they looked different — it was that neither could return to the main menu at all. The old Quit button called `Application.Quit`, which in the editor just stopped Play, and it bypassed the menu flow entirely so settings were never saved. Both are now one screen with two dressings (accent gold or danger red), showing nights survived, enemies defeated, peak colony size, resources on hand and the run's difficulty, over **Restart · Main Menu · Quit to Desktop** — plus Keep Playing after a win. The screen deliberately has no Back: dismissing it would strand the player on a frozen world with no UI.

Before that: **Balance measured instead of guessed, plus menus** — two additions. First, a headless balance-simulation harness ([`docs/SIMULATION.md`](docs/SIMULATION.md)): the real game runs in a `-batchmode -nographics` player with a *scripted player* — worker assignment, build orders, recruitment, and nothing else — while pathing, gathering, combat and enemy targeting stay the game's own Utility AI. Three strategies (Turtle / Rush / Eco) play full 5-night games at ~25× realtime and write two CSVs: one row per game, one row per night. The load-bearing detail is that it speeds runs up with `Time.captureDeltaTime`, **not** `Time.timeScale` — this codebase's AI evaluation budget and NavMesh throttles are frame-based, so `timeScale` would starve every brain and report the resulting losses as "balance".

It immediately paid for itself. Across 376 simulated games the shipping numbers turned out to be a shutout: Rush and Eco won **15–0** with the campfire at full HP on all five nights in 14 of 15 runs, and enemy count had to roughly triple before anything threatened it. Two findings changed the plan. Raising `enemyIncreasePerNight` produces a far better curve than raising `baseEnemiesPerNight` — both reach 0% win rate, but the ramp version has you lose on night 4.5 and the base-count version on night 2.4, so the *ramp* is the difficulty lever and the base count should stay low. And `spawnInterval` turned out to be the strongest knob of all: at the shipping 1 s, a 15-enemy wave arrives as fifteen one-enemy fights that warriors win in detail; tightening it to 0.15 s takes the same wave from a 100% win rate to 13%.

**The retune is now shipped**, drawn from those sweeps: waves arrive together (`spawnInterval` 1.0 → 0.4), the ramp does the scaling (`enemyIncreasePerNight` 2 → 2.25), and campfire healing dropped 5 → 1.5 HP/sec so damage carries across nights instead of resetting every dawn. A verification sweep of the shipped numbers — 96 runs, no overrides — puts Eco around 79% (19 of 24 pooled runs), Rush at 50% and Turtle at 0%.

**Read those numbers as ranges, not values.** Running the same configuration twice with the same seeds produced 92% and then 67%, with 5 of 12 individual seeds flipping outcome. The seed fixes `UnityEngine.Random`, but it cannot fix async NavMesh rebuilds completing after a wall-clock-dependent number of frames, or the job system's agent update order — so a seed makes runs comparable in aggregate, not reproducible. Anything smaller than roughly ±13pp at n=12 is noise, which is why the campfire-damage columns are the ones worth reading.

The sweeps also surfaced something the win rate hides, and it is the more useful result: **night 5 is not a close fight, it is a shutout or a collapse.** In 9 of 12 Eco runs the campfire finishes the last night at a *full* 200/200 — not "survived at 180", untouched — and in the one it loses, it is destroyed outright. There is no middle outcome available, which is also why the warrior heal-rate lever tested as noise: attrition cannot matter when the base is never touched.

The obvious explanation was engagement geometry. `Warrior.searchRadius` is 50 and enemies spawn at 45, so every enemy is inside warrior range the instant it appears and all five warriors sprint out to fight at the map edge — win that one field battle and nothing reaches the base, lose it and nothing stands in the way. **A sweep of shorter radii (50 / 30 / 22 / 18 / 12) refuted it.** Pulling warriors in did not let enemies through; it made the shutout *more* complete — at radius 18 and 22 the campfire finished night 5 at a full 200/200 in **12 of 12** runs, against 7 of 12 at the shipping 50. So the lever is not engagement *distance*, it is warrior *concentration*: at radius 50 each warrior independently chases the nearest enemy and fights alone, and the wave defeats a scattered line in detail; at 18 they hold together and win as a block. The shipping 50 is the worst value tested on both win rate and damage taken.

Turtle barely moved either (0% → 8–17% across those radii), so its losses are not about walls sitting behind the front line — it fields 1–3 warriors where Eco fields 5, and it is losing on army size. Making night 5 a *graded* fight rather than a coin flip now looks like it needs a design change — something that reaches the base past the warrior line — rather than a number. The sweep files that produced all of this live in `SimSweeps/`.

Second, the **menu system**: main menu (its own scene), Esc pause, options persisted to PlayerPrefs, controls, credits, and confirm dialogs — all built at runtime in uGUI with zero scene wiring, and all styled from a single `MenuStyle.cs` so an artist re-skins every screen by editing one file. Esc is deliberately *contextual*: it was already bound by five systems, so it cancels the active mode first (building ghost, wall line, demolish, crafting panel) and only opens the pause menu when nothing is active. Wireframes and the asset checklist: [`docs/MENU_WIREFRAMES.md`](docs/MENU_WIREFRAMES.md). Setting up the menu scene also fixes the build scene list, which still pointed only at the stock `SampleScene` — a build made before this shipped an empty world.

The menus' first pass shipped with three faults, since fixed. Every screen laid out at the wrong heights because `MenuBuilder.Column` had `childControlHeight = false`, and a `VerticalLayoutGroup` that isn't controlling an axis ignores `LayoutElement.preferredHeight` entirely — so the 52px buttons and 44px rows the screens asked for were never applied, and content spilled out of its panel. Sliders and toggles were completely unclickable: both are built from a helper that defaults `raycastTarget` to false, and in each case that graphic was the control's `targetGraphic` *and* its only click surface, so the EventSystem never delivered them a pointer event. And `PauseController` bootstrapped from `[RuntimeInitializeOnLoadMethod]`, which fires once per launch rather than once per scene load — with no `DontDestroyOnLoad` (correct here), it died with the menu scene on NEW GAME and Esc did nothing for the rest of the session. Panel heights are now computed from content rather than hand-typed, and the four settings that were saved to PlayerPrefs but read by nothing (camera speed, edge pan, screen shake, grid default) are wired to the systems that own them.

Before that: **Movement snap pass + a perf recorder that measures the right thing** — unit locomotion was retuned for responsiveness without giving up weight. The key idea is that weight should come from a unit's *top speed and turn rate*, not from a long acceleration ramp: a slow ramp doesn't read as mass, it reads as input lag, because the delay sits between the decision and any visible response. So acceleration roughly tripled across the board (worker 5 → 18, warrior 5 → 16, enemy 4 → 9 — spin-up times of 0.19 s / 0.22 s / 0.29 s, down from ~0.7 s) while the *ordering* that makes the three unit types feel different was preserved and widened. Warrior turn rate went 120 → 400 °/s, which was the single largest source of sluggishness — a 180° pivot took a second and a half. Enemies stay deliberately the heaviest thing on the field: the longest ramp and half the warrior's turn rate, which is where the lumbering read lives.

Two prefab/script divergences surfaced during the pass and were corrected: `Warrior.moveSpeed` read `3.5f` in code (commented "faster than enemies") while the prefab serialized **2.5**, and `Enemy.moveSpeed` read `2f` while the prefab serialized **3** — so warriors were in fact the *slowest* unit in the game and could never close on anything they chased. Because a `public float` is deserialized from the prefab, the script value was dead data and the comment had been wrong for a long time. Enemy speed also had not kept up with the island growing to 150×150: at 2 m/s against a spawn ring of 45, a third of every 60-second night was spent walking.

Alongside it, `PerfLogger` (F6/F7, editor + dev builds) streams a per-frame CSV to `PerfLogs/`, because a framerate counter cannot see the kind of stutter that actually shows up here. A capture during play proved the point: at the frame the player marked as stuttering, the game was running at 3–4 ms with nothing blocked and nothing pending — but across the session **two thirds of all frames had at least one agent with a path and a real desired velocity moving at under 40 % of its speed**, and one frame in ten had an agent pinned at zero. Units grinding against ORCA avoidance and carve geometry read as stutter to the eye at any framerate. The instrumentation also cleared the AI budgeting of suspicion: zero throttled `SetDestination`, zero throttled `CalculatePath`, and 2 deferred evaluations out of 33,273. The acceleration change targets this directly — recovery from an avoidance nudge drops from ~0.28 s below the slow threshold to ~0.08 s — and the capture is kept as a baseline to measure the next run against.

Before that: **Placement ghosts stopped lying about where a building lands** — `HutGhost` and `WatchTowerGhost` shipped with a full-size `BoxCollider` on layer 0, which is exactly what `groundLayer` is set to, so the placement raycast hit the *ghost's own roof* instead of the terrain. On the tilted camera the ghost settled with its roof under the cursor, parking the building's base about one building-height down-screen from the mouse (hut ≈ 2.6 m, tower ≈ 4 m). `WorkshopGhost` and `CampfireGhost` carry no collider and so felt correct, which is how it was isolated. Ghost colliders serve nothing — validity uses `Physics.CheckBox` — so every collider on a spawned ghost is now disabled and moved to Ignore Raycast. Ghosts also stopped lerping: the ghost was *drawn* trailing the cursor at a framerate-dependent rate while validity was *evaluated* at the unlagged target, so the green/red tint could disagree with the silhouette on screen. Grid snap already quantizes motion to whole cells, so there was nothing to smooth.

Before that: **Build grid rebuilt for the island** — the grid overlay drew a flat 50×50 square of `LineRenderer`s at y=0, which an island rising to ~3.5 m buried completely; it was only ever visible out over the water. It now keeps the cells that pass `TerrainGrid.IsBuildable` and drapes each boundary on the surface, so the drawn area *is* the placeable area and doubles as a placement guide. The whole grid became one `MeshTopology.Lines` mesh — at island scale, one LineRenderer per line would be tens of thousands of GameObjects. Cell boundaries also moved half a cell: `GridSnap` puts a building's *center* on a whole coordinate, so the old lines ran through the centers, out of phase with where things actually land. The toggle moved off **G** (which `BuildPlacement` also uses for wall→gate conversion — both handlers fired on the same frame) to **F2**, and the grid now auto-shows while build mode is active.

Before that: **Bigger island + carving nodes + pickups + workshop** — the island grew to 150×150 with resource nodes spread across it, and resource nodes now carve the NavMesh instead of merely repelling agents: paths used to run straight through trees, so units rubbed and slowed on trunks and enemies chasing warriors could wedge behind one. Building placement flattens a pad under the footprint (terrain T2, pulled forward), which fixes buildings clipping into slopes and the ghost previewing at a different height than the placed result. Worker flee became *garrison* — they run into the nearest hut and shelter inside rather than fleeing into open ground. Two new content slices: ground pickups (sticks and stones that wood/stone workers detour to collect) and a Workshop building with three one-time crafted upgrades.

Before that: **AI scaling pass + enemy re-path stutter fix** — the Utility AI could not grow past roughly 90 units. `AIBrain` used a fixed budget of 5 evaluations per frame, which at 60 fps against a 0.25–0.35 s think interval is a hard ceiling; past it, brains silently starved and units got visibly sluggish. Worse, a brain that lost the race reset its timer *before* the budget check, so it dropped that evaluation and waited another full interval instead of retrying, and `ForceReeval()` bypassed the throttle entirely without consuming budget — meaning one enemy dying inside the base made every worker within 30 u evaluate on the same frame, a spike that grew linearly with population. The budget now scales with the live brain count, over-budget evaluations defer instead of dropping, and forced evaluations are budgeted up to a hard ceiling. Alongside it, `ResourceAvailability` ran its distance cull *last*, so the per-node capacity check (which compacts a claim list and can fire eight `NavMesh.SamplePosition` calls) executed for every same-type node on the island — ~440 of them, per worker, three times a second; it now culls by squared distance first and prunes exactly against the running best. Worker Idle's duplicate node scan, whose result a `Constant` response curve threw away, is replaced by a free `ConstantScore`. Separately, enemies stopped freezing as a group: their retarget timer was unstaggered (the warriors' already was) and every forced move called `ResetPath()`, which zeroes velocity a frame or more before the replacement path is ready — so any shared priority shift stalled the whole wave in lockstep.

Before that: **Terrain System T1 (shaped island)** — the flat square ground is replaced by a procedurally generated island: a chunked flat-shaded heightmap mesh (per-triangle sand/grass/rock banding with the LP materials), rolling hills under a ~3.5 m readability budget, a real surrounding ocean at sea level, deep water marked NotWalkable (the shallow band stays wadeable), and the NavMesh built at runtime from the terrain colliders before anything else starts. The generator is seeded and deterministic — T1 ships a fixed seed with a flattened campfire site at the island heart and a guaranteed shallow landing cove carved at the shipwreck so the opening sequence works unchanged. Apply with `Tools > Island RTS > Terrain > Setup Terrain Scene (T1)`. Next stages: T2 terrain flattening under placed buildings, T3 shoreline enemy spawns, T4 a new random island every run (see [`TERRAIN_SYSTEM_PLAN.md`](TERRAIN_SYSTEM_PLAN.md)).

Before that: **Opening Sequence Stage 1 (survivor landing)** — the game start is now the story beat: a lone castaway stands in the shallows beside a shipwreck set piece on the west shore, the player right-clicks him ashore and places the campfire near him (free, one-time, bespoke placer — the campfire is deliberately not in the build menu), and he settles in as the colony's first worker before normal gameplay begins. The day/night clock is frozen at dawn until the fire is lit. A one-time editor tool (`Tools > Island RTS > Opening Sequence > Setup Opening Scene`) applies it: converts the scene campfire into a runtime-spawned prefab, builds the Survivor and campfire-ghost prefabs, and dresses the scene with a shallow-water ocean frame and the wreck. `skipIntro` on the `GameStart` object restores the classic start. Also decided: **Phase 7's dedicated Builder unit is replaced by jobless generalist colonists** — colonists without a job will wander, pick up ground items (sticks/rocks, a coming slice), and build/repair when needed; assigning a gathering job specializes them.

Before that: **Phase 6.26 (worker crowd interaction)** — workers now switch ORCA avoidance roles by state: a stationary worker (gathering, idle, sheltering) becomes max-importance so movers route around it like furniture instead of shoving it (a stander has no path and can't yield), and every moving errand re-rolls a random priority band so meeting workers never tie. Gatherers also get a rubber band — if the crowd nudges one off its spot it walks back and freezes again — and worker avoidance quality went Med → High to kill the head-on side-step dance. Fixes campfire delivery jams.

Before that: **Phase 6.25 + night moonlight** — targeting logic unified onto shared code: `TargetingUtil` (`FindNearest` / `GetApproachPoint` / `EdgeDistance`), an `ITargetable` interface on every unit and building, and single-owner target bookkeeping on `AIBlackboard` (`SetTarget` / `ClearTarget` / `IsTargetAlive`). Worker spacing tightened (agent radius 0.3, derived gather-ring slots, edge-based campfire delivery). Night lighting reworked: the directional light now holds a fixed moon pose at night — the sun sweep otherwise points below the horizon, which is why night used to be pitch black — with a retuned cool-blue `NightPreset` and soft half-strength moon shadows. The build grid overlay also started hidden by default. Pending playtest.

Earlier: **Phase 10 Stage 2 (low-poly art, in progress)** — a procedural editor-only generator (`Assets/Editor/LowPoly/`) builds the whole template art library (units, buildings, resource nodes, environment props) and `LowPolyPlumber` mounts it onto the gameplay prefabs without touching their components. The set was then simplified to a flat template style: meeple-style units, plain hut, single-tone roofs, solid one-piece resource nodes with embedded berries/ore crystals, and three tree variants picked per instance at runtime (`TreeVariance`). Worker behavior got a matching pass: faster turning (360°/s), workers stand right beside nodes, unreachable nodes are remembered and skipped for 15s, and each node has a room-based worker capacity so extra workers spill to the next node instead of dog-piling. The regenerate + re-plumb (`Tools > Island RTS > Low-Poly Templates > Generate All Assets`, then `Plumb Everything`) has been run in the editor — remaining: `Scatter Environment Props` and a NavMesh re-bake. Resource nodes also gained a gather-shake: a short damped wobble pulse on the art model whenever a worker's chop lands (rotation-only, on the visual `Model` child — the root, its obstacle carve, and the gather ring never move).

Earlier still: **Unity 6000.5.9f1 upgrade** — the project moved up from 6000.0.25f1. All 76 runtime scripts and the 9 editor scripts compile with zero errors and zero warnings; the only source change needed was swapping 11 `FindFirstObjectByType<T>()` calls (obsolete as of 6000.5) for `FindAnyObjectByType<T>()`. Packages were pinned to the editor's defaults up front — URP 17.0.3 → 17.5.0, uGUI 2.0.0 → 2.5.0, Input System 1.11.2 → 1.20.0, AI Navigation 2.0.9 → 2.0.14. Not yet run in Play mode.

In progress: **Phase 6.24** (queued refactors — *committed, still pending playtest*) — four structural cleanups that preserve behavior: deduplicated `AudioManager`'s fade coroutines; extracted a `UnitBase<T>` base for Worker/Warrior/Enemy boilerplate (component type names unchanged, so prefabs are unaffected); split the 1729-line `BuildPlacement` into a thin coordinator plus four plain helper classes (`WallLinePlacer`, `GhostPlacer`, `DemolishTool`, `NoBuildZoneRenderer` — no scene edits needed); and routed the worker executors' movement through `AINavHelper` so a throttled/rejected `SetDestination` retries instead of faking success. Compiles with zero warnings; still needs a Unity playtest (worker movement, wall building, demolish, day/night audio), which now doubles as the engine-upgrade playtest.

Recent: **Phase 6.23** (code health pass) — fixed four population/housing bookkeeping bugs and a warrior-heal stuck state, eliminated all `FindObjectsByType` scene scans, added UI dirty-checking, deduplicated the warrior enemy scan (~4x fewer list scans per AI tick), and removed ~20 dead members plus two leftover test scripts. The project compiles with zero warnings.

Future roadmap:
- **Phase 7 (revised):** Jobless generalist colonists — unassigned colonists wander, collect ground pickups (sticks/rocks carried to the campfire), and construct/repair buildings (construction will require their labor, repair costs a fraction of build cost); assigning a gathering job specializes them. Plus building upgrades (hut → house, campfire → fortress), workshop, storage
- **Phase 8:** Worker night hide behavior, archer units
- **Phase 9:** Player character (Admiral), crafting, tech tree
- **Phase 10: Visual Overhaul** — stylized low-poly tropical aesthetic in the Bad North / Townscaper / Islanders family. Five stages:
  - **Stage 1 ✓ shipped** — URP Global Volume (Bloom, Color Adjustments, White Balance, ACES Tonemapping, Vignette) + warm directional light + ambient gradient. New `LightingPreset` ScriptableObject drives day/night via `DayNightCycle` (replaces old `dayColor`/`nightColor` inspector fields). Campfire has HDR emission so it blooms with threshold at spec value. Night is moonlit: the directional light holds a fixed moon pose during night with soft shadows (`shadowStrength` lerped via the presets), instead of sweeping below the horizon and leaving the scene ambient-only.
  - **Stage 2 (in progress)** — Asset replacement via the in-repo procedural generator (`Assets/Editor/LowPoly/`): template-simple units/buildings/resource nodes plumbed onto the gameplay prefabs, environment scatter, per-instance tree variants. Bought-pack / Blender assets remain an option for later hero polish.
  - **Stage 3 ✓ shipped (simple form)** — hand-written URP water shader (`Assets/Shaders/StylizedWater.shader`): sine displacement on a real 1.5 m grid mesh, depth-blended turquoise-to-blue, noise-broken shoreline foam, derivative-based flat normals, per-facet tone drift and sun glints, and a soft sheen (an orthographic camera cannot form a specular *spot*, so a stepped highlight flips the whole sea)
  - **Stage 4** — Lighting bake (mixed mode), exponential fog, shadow cascade tuning, optional SSAO
  - **Stage 5** — Sequencing: post-processing now, water shader as Phase 7–8 side project, full asset swap during Phase 10 proper
  - Full spec: [`PHASE_10_VISUAL_OVERHAUL.md`](PHASE_10_VISUAL_OVERHAUL.md)
- **Terrain System:** dynamic island terrain — chunked low-poly heightmap with hills, valleys, beaches, and real water. **T1–T4 implemented** (shaped island + runtime NavMesh, flatten-on-placement, connectivity validation with ramp repair, a random island per run with player-chosen size / style / seed); still ahead from T3: enemies wading ashore from the shallows. Full spec: [`TERRAIN_SYSTEM_PLAN.md`](TERRAIN_SYSTEM_PLAN.md)

---

## Scaling Notes — Toward Hundreds of Units, AI Colonies, and Multiple Islands

Design thinking captured while fixing the AI evaluation budget. Not committed work — a map of what the current architecture supports, what it blocks, and the order the blockers are cheapest to clear.

### Where the entity ceiling actually is

The limit was never Unity — it was the AI layer's own throttles and a scan whose cost was `O(units × resource nodes)`. With the scaling pass above, the next wall is **Unity's NavMesh ORCA avoidance**, which is the real ceiling at somewhere in the low hundreds of agents (workers run `High` avoidance quality, the most expensive setting, deliberately — see Phase 6.26). Beyond that:

- **Spatial hash for registry lookups.** Considerations still walk whole `ActiveRegistry` lists. A uniform grid keyed by cell would make node/pickup/enemy queries `O(nearby)` instead of `O(all)`. This is the next optimization worth doing, and it gets more valuable with every entity added.
- **Avoidance quality by population.** Dropping workers to `Medium` above some unit count buys Unity-side headroom, at the cost of the head-on side-step dance that Phase 6.26 raised the quality to fix. A tuning trade, not a free win.
- **Decision richness is not the problem.** Five actions × three-to-five considerations, multiplied, is cheap. The cost lives in registry scans hidden inside considerations. Units do not need to get dumber to scale — the scans need to get narrower.

### AI colonies — decide faction ownership early

Rival colonies are the feature with a deadline attached, because "one colony" is currently baked into the type system rather than the data:

- `ResourceManager` and `PopulationManager` are singletons
- `ActiveRegistry<T>` lists are global statics — "all workers" implicitly means "my workers"
- `EnemyAttackExecutor.FindLiveCampfire()` returns `BaseBuilding.ActiveList[0]`

Every targeting scan, every economy call, and every population check assumes a single owner. Introducing a `Faction` concept means touching all of those sites, and **that set grows with every feature added between now and then** — mechanical if done early, a rewrite if done late. The shape: registries become per-faction, `ResourceManager` becomes a component on a faction rather than a singleton, and `TargetingUtil.FindNearest` takes a faction filter. Worth doing before the next large gameplay system even if the second faction stays a stub for a long time.

The unit AI itself needs almost nothing. Utility AI is the right layer for *"what does this worker do next."* Colony **strategy** wants a second layer above it — a governor ticking at 1–2 Hz that decides build orders and army composition and writes goals into a shared blackboard that unit considerations read. That is additive, not a refactor.

### Multiple islands — better positioned than expected

`IslandGenerator` is pure and seeded, so an island is just a seed plus a few parameters. Islands that "load differently" are nearly free, and no scene-per-island is required. `TerrainGrid` doing all of its work in `Awake` under `[DefaultExecutionOrder(-100)]` is the contract that makes this hold: every `Start()`-time system already finds a finished world and a live NavMesh.

**Prefer teardown-and-regenerate inside the single scene over scene loading per island.** Scene loads would resurrect exactly the stale-singleton problems that removing `DontDestroyOnLoad` was meant to fix (see the Phase 6.21 notes). The real prerequisite is *serializable colony state* — leaving an island and returning to it means that colony must persist as data — which is the Phase 11 save/load system. Islands therefore naturally follow save/load rather than preceding it.

### Where the two features collide

Four AI colonies at ~100 units each is ~400 agents, right at the NavMesh ceiling. That is the point where the spatial hash becomes mandatory and **AI level-of-detail** starts to matter: simulate distant or offscreen colonies as abstract economies ticking at 1 Hz, and instantiate real agents only near the player. Standard RTS practice — and substantially easier to retrofit if factions already exist.

### Suggested order

**Factions → spatial hash + AI LOD → colony governor → save/load → islands.**

Factions first because their cost grows over time; islands last because they depend on save/load.

---

*A shipwreck survival RTS built in Unity*
