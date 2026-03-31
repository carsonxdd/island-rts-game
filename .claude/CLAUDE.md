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
    │   ├── Scripts/                 # All C# source (75 files)
    │   │   ├── AI/                  # Utility AI system (35 files)
    │   │   │   ├── Core/            # AIBrain, ActionOption, Consideration, ResponseCurve, AIBlackboard
    │   │   │   ├── WorldState/      # AIWorldState (enemy density grid, day progress)
    │   │   │   ├── Considerations/  # 13 scoring inputs (HealthPercent, ThreatNearby, etc.)
    │   │   │   ├── Executors/
    │   │   │   │   ├── Worker/      # Gather, Return, Flee, Idle
    │   │   │   │   ├── Warrior/     # Engage, Intercept, DefendWall, Patrol, Retreat, HealAtCampfire
    │   │   │   │   └── Enemy/       # BreachWall, AttackTarget
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
| `DayNightCycle.cs` | 120s day / 60s night, lighting, static events |
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
**Enemy actions:** AttackWarrior, AttackBuilding, BreachWall, AttackCampfire

### Singleton Pattern
Used by: ResourceManager, AudioManager, WallGrid, AIWorldState, PopulationManager, GameManager, CombatEffects, CameraShake, BuildingDatabase

### ActiveRegistry Pattern
`ActiveRegistry<T>` provides O(1) static lists for: Worker, Warrior, Enemy, BaseBuilding, Hut, Wall, Gate, Watchtower, ResourceNode. Units register in Awake, unregister in OnDestroy.

### Performance Conventions
- Zero GC allocations in hot paths (Update, AI evaluation)
- NavMesh throttling: max 12 SetDestination/frame, 2 CalculatePath/frame (AINavHelper)
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

## Key Conventions

- `CachedHealth` property on all units avoids per-frame GetComponent
- Wall/Gate events: `OnAnyWallDestroyed` / `OnAnyGateDestroyed` static events
- WallGrid: `WorldToGrid()`, `HasWallAt()`, `GetWallAt()` (not GetAt)
- ResourceNode scoring: `distance + (claimCount * 5f)`
- Wall scoring: `dist * (1 + attackers * 0.5f)`, gates at `0.3x` distance
- Public sound methods: `StartGatheringSoundPublic()`, `PlayAttackSoundPublic()`, etc.
- `StuckResolver` is a shared component (Worker, Warrior, Enemy all use it)

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

## Current State (Phase 6.20)

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
- Phase 10: Art upgrade (replace primitives with low-poly models)
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
