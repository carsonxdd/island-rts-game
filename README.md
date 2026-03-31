# Island RTS Game

A Unity-based real-time strategy survival game. Manage autonomous workers, gather resources, build defenses, recruit warriors, and survive escalating nightly enemy raids.

**Genre:** Top-down RTS + Survival  
**Setting:** Age of Sail shipwreck on an uncharted island  
**Status:** Playable alpha (Phase 6.20)

---

## Quick Start

1. Clone the repo
2. Open `islandrts/` in Unity Hub (requires **Unity 6000.0.25f1**)
3. Open scene: `Assets/Scenes/SampleScene.unity`
4. Press Play

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
| **G** | Convert wall to gate (hover over wall in build mode) |
| **R** | Toggle wall path direction / Rotate building |
| **Shift** (hold) | Diagonal wall path |
| **Delete / X** | Demolish building (50% resource refund) |
| **F3** | AI debug overlay (editor only) |

---

## Tech Stack

| Component | Version |
|-----------|---------|
| Unity | 6000.0.25f1 |
| Render Pipeline | URP 17.0.3 |
| Pathfinding | AI Navigation 2.0.9 |
| UI | TextMeshPro + uGUI |
| Input | Input System 1.11.2 |

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
│   │   │   └── Enemy/           # BreachWall, AttackTarget
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
│   └── ...                      # 40 root-level scripts total
├── Prefabs/                     # Units, buildings, resources
├── Materials/
├── Audio/
├── Scenes/
│   └── SampleScene.unity        # Main game scene
└── Settings/                    # URP pipeline configs
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

- **ActiveRegistry\<T\>** — Generic static registry for O(1) entity lookups (Workers, Enemies, Walls, etc.)
- **Singletons** — ResourceManager, AudioManager, WallGrid, AIWorldState, GameManager
- **NavMesh throttling** — Max 12 SetDestination/frame, 2 CalculatePath/frame
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

## Development

For detailed technical documentation, AI system internals, balancing data, phase history, and gotchas, see [`.claude/CLAUDE.md`](.claude/CLAUDE.md).

Future roadmap:
- **Phase 7:** Building upgrades, workshop, storage
- **Phase 8:** Worker night hide behavior, archer units
- **Phase 9:** Player character (Admiral), crafting, tech tree
- **Phase 10:** Art upgrade (low-poly models)

---

*A shipwreck survival RTS built in Unity*
