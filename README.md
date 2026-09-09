# Island RTS Game

A Unity real-time-strategy survival game. You are one named character on a procedurally generated island. Survivors come ashore, you research the colony's first skills, hand out jobs, build defences, and hold the fire through raids that are announced at dawn and grow with your colony.

**Genre:** Top-down RTS + survival
**Setting:** A shipwreck on an uncharted island. Deliberately light on fiction for now — the backstory is unwritten and the long-term direction (a single castaway story, or pickable civilizations) is still open.
**Status:** Pre-alpha, in feature freeze. Everything listed under **Game Systems** below is built. The work between here and a build handed to testers is in [`ALPHA_PLAN.md`](ALPHA_PLAN.md).

---

## Quick Start

1. Clone the repo
2. Open `islandrts/` in Unity Hub (requires **Unity 6000.5.9f1**)
3. **Run `Tools > Island RTS > Setup Everything (In Order)` once.** The art library, opening sequence, scatter settings, terrain, pickups/Workshop and the menu scene are all applied by editor tools, and their order is load-bearing. It is idempotent — re-run it after pulling anything that touched art, prefabs or scene wiring.
4. It leaves `MainMenu` open, which is what a build starts on. Press Play. `Tools > Island RTS > Open Game Scene (MainIsland)` skips the title screen.

> `Assets/Scenes/SampleScene.unity` is the leftover stock Unity scene and is *not* the game. **Build Settings still points at it** — a build made today ships an empty world. Fixing that is the first item in the alpha plan.

### First game

1. **NEW GAME** → difficulty (Normal is the intended balance) → **BEGIN**, then name your castaway.
2. Right-click sticks and stones on the beach to gather them, right-click to walk ashore, then **B** and click to place the campfire. It is free and one-time.
3. Right-click the fire to deposit, then use the panel's **Research** tab. Woodcutting teaches the colony to cut wood *and* hands you the Stone Axe; Quarrying does the same with the Stone Pick; Construction opens build mode; Spearcraft opens spears. Your character has to stand at the bench while the queue runs.
4. Survivors land while there is free housing (the campfire sleeps 3, a hut 2). The **Colonists** tab hands them the jobs you have researched. Press **B** to build huts — a site only rises while an idle colonist works it, so keep one or two unassigned.
5. Watch the calendar chip. The first two nights are always quiet; from day 3 a dawn can turn it red with "Raid tonight · N raiders". That is your day to craft spears and arm warriors.
6. Reach the dawn after day 30 and the rescue ship arrives.

---

## Controls

| Key | Action |
|-----|--------|
| **WASD** / Arrows | Pan camera |
| **Q / E** · **Wheel** · **Middle-drag** | Rotate · zoom · tilt and rotate |
| **B** | Build mode (during the opening, place the campfire) |
| **1-6** | Hut, Wood Wall, Stone Wall, Watchtower, Workshop, Shipyard |
| **G** · **R** · **Shift** | Wall to gate · toggle wall path or rotate · diagonal wall path |
| **Delete / X** | Demolish, 50% refund |
| **F5 / F8 / F9** | Militia stance: Defensive / Offensive / Follow |
| **Right-click** | Command your character: fetch, hand-harvest, deposit and work the queue, work a bench, or walk |
| **Left-click** | Open a building's panel. The only gesture that opens UI |
| **Space** | Centre the camera on your character |
| **Esc** | Cancel the active mode, or open the pause menu when nothing is active |
| **F2 / F3 / F4 / F6-F7** | Grid overlay · AI overlay · debug cheats · perf recorder (the last three are editor and dev builds only) |

Every gameplay key is a default, not a fixed binding — *Options → Controls* rebinds all of them with a main and an alternate slot each. Esc, the mouse buttons and the debug keys are reserved.

Full reference: [`docs/CONTROLS_AND_CHECKLIST.md`](docs/CONTROLS_AND_CHECKLIST.md). In game, **Esc → Information** is a field guide built from the game's own catalogs, and in editor and dev builds its **DEV** tab holds the playtest quests — a tracker lists the next ones, some tick themselves, and SUBMIT REPORT writes a markdown report to `Playtests/`.

---

## Tech Stack

| Component | Version |
|-----------|---------|
| Unity | 6000.5.9f1 |
| Render pipeline | URP 17.5.0 |
| Pathfinding | AI Navigation 2.0.14 |
| UI | TextMeshPro + uGUI 2.5.0 |
| Input | Input System 1.20.0 |

All packages are in the project manifest. Nothing to install by hand.

---

## Project Structure

```
islandrts/Assets/
├── Scripts/
│   ├── AI/                      # Utility AI: Core, WorldState, Considerations, Executors, Shared, Debug
│   ├── UI/                      # Runtime uGUI menus, HUD, settings, keybindings, difficulty, dev quests
│   ├── Items/                   # ItemCatalog, Inventory, ResearchCatalog, CraftingCatalog, Unlocks
│   ├── Terrain/                 # TerrainGrid, IslandGenerator, IslandSettings, PropScatter
│   ├── Sim/                     # Headless balance-simulation harness
│   └── *.cs                     # Units, buildings, economy, day/night, combat, camera, clouds
├── Editor/
│   ├── LowPoly/                 # Procedural low-poly art generator, plumber, scatter table
│   ├── Sim/                     # Sweep runner + headless sim player build
│   └── FullSetup.cs             # Runs all eight setup steps in dependency order
├── Art/  Prefabs/  Materials/  Audio/  Settings/  Shaders/
├── Resources/                   # Changelog, Information, DevQuests text + cloud materials
├── MainMenu.unity               # Entry point
├── MainIsland.unity             # The game scene
└── Scenes/SampleScene.unity     # Stock Unity scene, unused
```

---

## Architecture

### Utility AI

Workers, warriors and enemies all run a scoring-based Utility AI — no state machines anywhere. Each unit's `AIBrain` scores its actions by multiplying `Consideration` values (0-1, each shaped by a `ResponseCurve`) and runs the winner's executor.

- Evaluations staggered 0.25-0.35 s per unit, randomized
- The per-frame budget scales with population, so think rate stays constant as the colony grows
- A brain that loses the budget race defers rather than dropping its evaluation
- A 20% commitment threshold stops flip-flopping; `ForceReeval()` jumps the queue but is still budgeted

### Key patterns

- **`ActiveRegistry<T>`** — static O(1) lists for every unit, building, node and pickup. The codebase contains zero `FindObjectsByType` scans.
- **Singletons** — ResourceManager, PopulationManager, AudioManager, WallGrid, AIWorldState, GameManager, BuildingDatabase. No `DontDestroyOnLoad`, so nothing goes stale across a restart.
- **Point-of-effect reads** — difficulty, settings and unlocks are read where they take effect, never pushed.
- **Zero GC in hot paths**, throttled NavMesh calls, dirty-checked UI text.
- **Buildings are data** — `BuildingData` ScriptableObjects define costs, prefabs and placement rules. Walls draw as lines and auto-connect with procedural meshes.

Deeper technical notes, the gotcha list and the session log: [`.claude/CLAUDE.md`](.claude/CLAUDE.md) and [`docs/PHASE_HISTORY.md`](docs/PHASE_HISTORY.md).

---

## Game Systems

| System | Description |
|--------|-------------|
| **Opening** | Name your castaway, land at the wreck, gather beach materials, walk ashore and place the campfire. The clock is held until the fire is lit. |
| **Your character** | Yours for the whole run. Never a colonist, takes no housing, eats nothing, ignored by enemies. Right-click fetches pickups into a six-slot inventory, picks a bush bare-handed, works a tree or rock once research hands over the matching tool, and deposits at the fire. Knocked out rather than killed. Losing the campfire is the only defeat. |
| **Economy** | Wood, food, stone, metal. Workers gather autonomously. Every tree on the island is choppable and the big boulders are quarryable. Metal is deliberately scarce. |
| **World** | A new island every game: size, terrain style and an optional seed are picked on New Game and locked for the run. Plateaus, cliffs, ramps, ponds; every plateau is reachable. |
| **Pickups** | Sticks and stones that trickle-respawn, plus finite salvage along the shore. Job workers detour for nearby ones; idle colonists haul anything within 70 m of the fire by day, 30 m after dusk. |
| **Colonists** | People are a pool, not a purchase. Survivors land while housing has room. Idle colonists are the colony's utility labour — build, then craft, then repair, then tidy — weighted by four priority sliders, with Builder / Crafter / Repairer specialists to pin one. Warriors are idle colonists taking up a spear. |
| **Building** | Hut, Wooden and Stone Wall, Gate, Watchtower, Workshop, Shipyard. Placement flattens a pad. A site only rises while a colonist works it. Repair costs a quarter of the build price. |
| **Research and crafting** | Research is one-time and opens jobs, build mode, weapons and the Workshop, and hands your character the matching tool. Recipes are repeatable and gated behind research. Both live on stations with a queue that only moves while someone stands at the bench. Costs are paid on completion; a short entry waits rather than failing. |
| **Storage** | Materials, spears and tools live in the campfire stockpile, 60 items to start, raised by research. The four pooled resources are uncapped. |
| **Combat** | The militia takes one colony-wide stance — Defensive, Offensive or Follow — and stands in a Line, Wedge or Ring. Warriors converge on a raider from different sides; archers keep their distance. Watchtowers buff nearby damage. Housing is the only cap on army size. |
| **Calendar and raids** | 100 s day, 50 s night, 30 days to rescue, about 75 minutes. A dawn roll decides whether raiders land that night, never before day 3 and forced after five quiet ones, and the size is fixed at the roll from the day number and the colony's prosperity. A raid night lasts until the last raider is dead. |
| **Difficulty** | Six presets plus Custom, chosen on New Game and locked for the run. Scales raid size and frequency, enemy stats, night length, starting resources and run length. |
| **Menus** | Main menu, New Game, pause, options across four tabs, rebindable controls, changelog, field guide, end screens. All built at runtime in code — see [`docs/MENU_WIREFRAMES.md`](docs/MENU_WIREFRAMES.md). |
| **Weather and light** | A sky condition rolled each dawn: drifting cloud puffs whose shade slides across the island as the sun's light cookie. Graphics presets in Options. |
| **Balance sim** | Headless autoplay: scripted strategies play full games and write CSVs, so balance is measured rather than guessed. See [`docs/SIMULATION.md`](docs/SIMULATION.md). |

---

## Logging

The console is intentionally quiet — about 65 calls in the whole project. Only errors, recoverable-misconfiguration warnings, and once-per-night or once-per-game lifecycle lines. Everything per-unit, per-damage, per-tick and per-click has been removed. The keep-list and the rule for adding a new log live in [`.claude/CLAUDE.md`](.claude/CLAUDE.md) under **Logging Conventions**.

---

## Where the project is going

**Right now: feature freeze, then an alpha.** The scope is closed. What remains is playtesting the pile of built-but-unplayed work, a tutorial, a shorter run length for testers, a feedback path, and a build that actually ships the game scene. The full list, in order, with a definition of done: **[`ALPHA_PLAN.md`](ALPHA_PLAN.md)**.

**After it ships: the architecture lap** — factions, then a spatial hash and AI level of detail, then a colony governor, then save/load, then multiple islands. Sequenced and argued in the alpha plan's section H, with the reasoning in [`docs/SCALING_NOTES.md`](docs/SCALING_NOTES.md). Months of work with nothing player-visible until the end of it, which is why testers come first.

Parked with no committed order:

- [`COLONY_EXPANSION_PLAN.md`](COLONY_EXPANSION_PLAN.md) — collector radius and settlement tiers, processing chains, families, livestock and farming
- Building upgrades (hut to house, campfire to fortress) and a placeable Warehouse
- Enemies wading ashore from the shallows, the last unbuilt piece of [`TERRAIN_SYSTEM_PLAN.md`](TERRAIN_SYSTEM_PLAN.md)
- Phase 10 Stages 3-4: water polish and a lighting bake, [`PHASE_10_VISUAL_OVERHAUL.md`](PHASE_10_VISUAL_OVERHAUL.md)
- Setting and fiction: whether this stays one castaway's story or becomes pickable civilizations is an open question, not a plan

### History

The player-facing history is `islandrts/Assets/Resources/Changelog.txt`, which is also the in-game CHANGELOG screen. The developer history — every session, what broke and what it taught — is [`docs/PHASE_HISTORY.md`](docs/PHASE_HISTORY.md). Design plans that are already built are kept for their locked decisions: [`RESEARCH_AND_DAYS_PLAN.md`](RESEARCH_AND_DAYS_PLAN.md), [`CRAFTING_AND_PLAYER_CHARACTER_PLAN.md`](CRAFTING_AND_PLAYER_CHARACTER_PLAN.md), [`TERRAIN_SYSTEM_PLAN.md`](TERRAIN_SYSTEM_PLAN.md).

---

*A shipwreck survival RTS built in Unity*
