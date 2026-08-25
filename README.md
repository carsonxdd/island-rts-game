# Island RTS Game

A Unity-based real-time strategy survival game. Manage autonomous workers, gather resources, build defenses, recruit warriors, and survive escalating nightly enemy raids.

**Genre:** Top-down RTS + Survival  
**Setting:** Age of Sail shipwreck on an uncharted island  
**Status:** Playable alpha (Phase 6.24) — Phase 10 Stage 2 (low-poly art) in progress: the procedural art library is plumbed onto the gameplay prefabs and simplified to a clean template style, pending an in-editor regenerate + playtest. Now on Unity 6000.5.9f1; the Phase 6.24 refactors are committed but still awaiting a playtest.

---

## Quick Start

1. Clone the repo
2. Open `islandrts/` in Unity Hub (requires **Unity 6000.5.9f1**)
3. Open scene: `Assets/MainIsland.unity`
4. Press Play

> **Note:** `Assets/Scenes/SampleScene.unity` is the leftover stock Unity scene (3 objects) and is *not* the game. Build Settings still points at it — fix that before making a build.

### First Game

1. Click the campfire to assign 5-6 workers (wood + food)
2. Press **B** to build — place 1-2 Huts for housing
3. Recruit 2-3 warriors before nightfall
4. Survive 5 nights to win

---

## Controls

| Key | Action |
|-----|--------|
| **WASD** / Arrows | Pan camera |
| **Q / E** | Rotate camera |
| **Mouse Wheel** | Zoom |
| **B** | Enter build mode |
| **1-4** | Select building (Hut, Wood Wall, Stone Wall, Watchtower) |
| **G** | Toggle grid overlay (off by default); convert wall to gate in build mode |
| **R** | Toggle wall path direction / Rotate building |
| **Shift** (hold) | Diagonal wall path |
| **Delete / X** | Demolish building (50% resource refund) |
| **F3** | AI debug overlay (editor only) |

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
│   ├── GameManager.cs           # Victory/defeat, statistics
│   ├── ResourceManager.cs       # Wood/food/stone economy
│   ├── Worker.cs                # Worker unit + AI setup
│   ├── Warrior.cs               # Warrior unit + AI setup
│   ├── Enemy.cs                 # Enemy unit + AI setup
│   ├── BuildPlacement.cs        # Building placement, wall drawing, demolish
│   ├── WallGrid.cs              # O(1) wall/gate grid registry
│   ├── WallConnector.cs         # Procedural wall mesh generation
│   ├── Health.cs                # Universal health component
│   ├── DayNightCycle.cs         # Day/night with lighting
│   └── ...                      # 38 root-level scripts total
├── Editor/
│   └── LowPoly/                 # Procedural low-poly asset generator (editor-only)
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
- Max 5 evaluations per frame globally
- 20% commitment threshold prevents action flip-flopping
- `ForceReeval()` bypasses throttles for instant response to events

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
| **Economy** | Wood, food, stone. Workers gather autonomously. Carry capacity 5, 1/sec rate. |
| **Building** | Hut (housing), Wooden/Stone Wall, Gate, Watchtower. Construction sites auto-complete. |
| **Combat** | Warriors auto-engage enemies. Watchtowers buff nearby warrior damage 1.25x. |
| **Day/Night** | 120s day / 60s night. Enemies spawn at night (3 + night number). |
| **Healing** | Warriors heal 5 HP/sec at campfire between waves. |
| **Flee** | Workers flee away from enemies (day or night). |
| **Demolish** | Delete/X key. 50% resource refund. Campfire protected. |
| **Victory** | Survive 5 nights. Defeat if campfire is destroyed. |

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

Latest: **Phase 6.26 (worker crowd interaction)** — workers now switch ORCA avoidance roles by state: a stationary worker (gathering, idle, sheltering) becomes max-importance so movers route around it like furniture instead of shoving it (a stander has no path and can't yield), and every moving errand re-rolls a random priority band so meeting workers never tie. Gatherers also get a rubber band — if the crowd nudges one off its spot it walks back and freezes again — and worker avoidance quality went Med → High to kill the head-on side-step dance. Fixes campfire delivery jams. Also planned: **Phase 7 gains Builders** — a dedicated unit that will do construction (replacing timed auto-build) and repair (costing a fraction of build cost); design sketch in [`.claude/CLAUDE.md`](.claude/CLAUDE.md).

Before that: **Phase 6.25 + night moonlight** — targeting logic unified onto shared code: `TargetingUtil` (`FindNearest` / `GetApproachPoint` / `EdgeDistance`), an `ITargetable` interface on every unit and building, and single-owner target bookkeeping on `AIBlackboard` (`SetTarget` / `ClearTarget` / `IsTargetAlive`). Worker spacing tightened (agent radius 0.3, derived gather-ring slots, edge-based campfire delivery). Night lighting reworked: the directional light now holds a fixed moon pose at night — the sun sweep otherwise points below the horizon, which is why night used to be pitch black — with a retuned cool-blue `NightPreset` and soft half-strength moon shadows. The build grid overlay now starts hidden (G toggles it). Pending playtest.

Earlier: **Phase 10 Stage 2 (low-poly art, in progress)** — a procedural editor-only generator (`Assets/Editor/LowPoly/`) builds the whole template art library (units, buildings, resource nodes, environment props) and `LowPolyPlumber` mounts it onto the gameplay prefabs without touching their components. The set was then simplified to a flat template style: meeple-style units, plain hut, single-tone roofs, solid one-piece resource nodes with embedded berries/ore crystals, and three tree variants picked per instance at runtime (`TreeVariance`). Worker behavior got a matching pass: faster turning (360°/s), workers stand right beside nodes, unreachable nodes are remembered and skipped for 15s, and each node has a room-based worker capacity so extra workers spill to the next node instead of dog-piling. To apply in the editor: `Tools > Island RTS > Low-Poly Templates > Generate All Assets`, then `Plumb Everything` (+ `Scatter Environment Props`), then re-bake the NavMesh.

Earlier still: **Unity 6000.5.9f1 upgrade** — the project moved up from 6000.0.25f1. All 76 runtime scripts and the 9 editor scripts compile with zero errors and zero warnings; the only source change needed was swapping 11 `FindFirstObjectByType<T>()` calls (obsolete as of 6000.5) for `FindAnyObjectByType<T>()`. Packages were pinned to the editor's defaults up front — URP 17.0.3 → 17.5.0, uGUI 2.0.0 → 2.5.0, Input System 1.11.2 → 1.20.0, AI Navigation 2.0.9 → 2.0.14. Not yet run in Play mode.

In progress: **Phase 6.24** (queued refactors — *committed, still pending playtest*) — four structural cleanups that preserve behavior: deduplicated `AudioManager`'s fade coroutines; extracted a `UnitBase<T>` base for Worker/Warrior/Enemy boilerplate (component type names unchanged, so prefabs are unaffected); split the 1729-line `BuildPlacement` into a thin coordinator plus four plain helper classes (`WallLinePlacer`, `GhostPlacer`, `DemolishTool`, `NoBuildZoneRenderer` — no scene edits needed); and routed the worker executors' movement through `AINavHelper` so a throttled/rejected `SetDestination` retries instead of faking success. Compiles with zero warnings; still needs a Unity playtest (worker movement, wall building, demolish, day/night audio), which now doubles as the engine-upgrade playtest.

Recent: **Phase 6.23** (code health pass) — fixed four population/housing bookkeeping bugs and a warrior-heal stuck state, eliminated all `FindObjectsByType` scene scans, added UI dirty-checking, deduplicated the warrior enemy scan (~4x fewer list scans per AI tick), and removed ~20 dead members plus two leftover test scripts. The project compiles with zero warnings.

Future roadmap:
- **Phase 7:** Builders (dedicated unit that constructs and repairs the fort — construction will require builder labor, repair costs a fraction of build cost), building upgrades, workshop, storage
- **Phase 8:** Worker night hide behavior, archer units
- **Phase 9:** Player character (Admiral), crafting, tech tree
- **Phase 10: Visual Overhaul** — stylized low-poly tropical aesthetic in the Bad North / Townscaper / Islanders family. Five stages:
  - **Stage 1 ✓ shipped** — URP Global Volume (Bloom, Color Adjustments, White Balance, ACES Tonemapping, Vignette) + warm directional light + ambient gradient. New `LightingPreset` ScriptableObject drives day/night via `DayNightCycle` (replaces old `dayColor`/`nightColor` inspector fields). Campfire has HDR emission so it blooms with threshold at spec value. Night is moonlit: the directional light holds a fixed moon pose during night with soft shadows (`shadowStrength` lerped via the presets), instead of sweeping below the horizon and leaving the scene ambient-only.
  - **Stage 2 (in progress)** — Asset replacement via the in-repo procedural generator (`Assets/Editor/LowPoly/`): template-simple units/buildings/resource nodes plumbed onto the gameplay prefabs, environment scatter, per-instance tree variants. Bought-pack / Blender assets remain an option for later hero polish.
  - **Stage 3** — Stylized URP water shader (Shader Graph): Gerstner displacement, depth-blended turquoise, shoreline foam, quantized sun specular, flat-shaded normals
  - **Stage 4** — Lighting bake (mixed mode), exponential fog, shadow cascade tuning, optional SSAO
  - **Stage 5** — Sequencing: post-processing now, water shader as Phase 7–8 side project, full asset swap during Phase 10 proper
  - Full spec: [`PHASE_10_VISUAL_OVERHAUL.md`](PHASE_10_VISUAL_OVERHAUL.md)

---

*A shipwreck survival RTS built in Unity*
