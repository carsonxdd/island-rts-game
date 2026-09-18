# Island RTS Game

A Unity real-time-strategy survival game. You are one named character on a procedurally generated island. Survivors come ashore, you research the colony's first skills, hand out jobs, build defences, and hold the fire through raids that are announced at dawn and grow with your colony.

**Genre:** Top-down RTS + survival
**Setting:** A shipwreck on an uncharted island. Deliberately light on fiction for now — the backstory is unwritten and the long-term direction (a single castaway story, or pickable civilizations) is still open.
**Status:** Pre-alpha, mid-way through the architecture lap ([`docs/ARCHITECTURE_LAP_PLAN.md`](docs/ARCHITECTURE_LAP_PLAN.md)). Everything listed under **Game Systems** below is built. The work between the lap and a build handed to testers is in [`docs/ALPHA_PLAN.md`](docs/ALPHA_PLAN.md).

---

## Quick Start

1. Clone the repo
2. Open `islandrts/` in Unity Hub (requires **Unity 6000.5.9f1**)
3. **Run `Tools > Island RTS > Setup Everything (In Order)` once.** The art library, opening sequence, scatter settings, terrain, pickups/Workshop and the menu scene are all applied by editor tools, and their order is load-bearing. It is idempotent — re-run it after pulling anything that touched art, prefabs or scene wiring.
4. It leaves `MainMenu` open, which is what a build starts on. Press Play. `Tools > Island RTS > Open Game Scene (MainIsland)` skips the title screen.

> `Assets/Scenes/SampleScene.unity` is the leftover stock Unity scene and is *not* the game. It is not in the build: `Setup Everything` writes the scene list as `MainMenu` then `MainIsland`. The version a build reports is `ProjectSettings > Player > Version` (`0.2.0-alpha.1`); bump it before each build handed out.

### First game

1. **NEW GAME** → difficulty (Normal is the intended balance) → **BEGIN**, then name your castaway.
2. Right-click sticks and stones on the beach to gather them, right-click to walk ashore, then **B** and click to place the campfire. It is free and one-time.
3. Right-click the fire to deposit, then use the panel's **Research** tab. Woodcutting teaches the colony to cut wood *and* hands you the Stone Axe; Quarrying does the same with the Stone Pick; Construction opens build mode; Spearcraft opens spears. Your character has to stand at the bench while the queue runs.
4. Survivors land while there is free housing (the campfire sleeps 3, a hut 2). The **Colonists** tab hands them the jobs you have researched. Press **B** to build huts — a site only rises while someone works it: an idle colonist, or your castaway when you right-click the site. Keep one or two colonists unassigned anyway; a colony where everyone gathers has nobody to build.
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
| **F10** | Ring the bell: one colonist per spare weapon in stock arms; press again to stand them down |
| **Right-click** | Command your character: fetch, hand-harvest, deposit and work the queue, work a bench, build a site, or walk. A green ring marks the click and a trail shows the path |
| **Shift + Right-click** | Queue the order behind the current one; a plain right-click clears the queue. In build mode, **Shift + Left-click** places a building and keeps the ghost for the next |
| **Left-click** | Open a building's panel. The only gesture that opens UI |
| **Space** | Centre the camera on your character |
| **Left-click / drag the minimap** | Centre the camera there (the north-up map in the top-right corner) |
| **Esc** | Cancel the active mode, or open the pause menu when nothing is active |
| **F2 / F3 / F4 / F6-F7** | Grid overlay · AI overlay · debug cheats · perf recorder (the last three are editor and dev builds only) |

Every gameplay key is a default, not a fixed binding — *Options → Controls* rebinds all of them with a main and an alternate slot each. Esc, the mouse buttons and the debug keys are reserved.

Full reference: [`docs/CONTROLS.md`](docs/CONTROLS.md). In game, **Esc → Information** is a field guide built from the game's own catalogs, and in editor and dev builds its **DEV** tab holds the playtest quests — a tracker lists the next ones, most tick and pass themselves the moment the thing happens (only looks and "nothing went wrong" checks are ticked by hand), and SUBMIT REPORT writes a markdown report to `Playtests/`.

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
│   ├── Factions/                # Faction, Factions registry, Relations, ResourcePool, Population, Knowledge, Spawn
│   ├── UI/                      # Runtime uGUI menus, HUD, settings, keybindings, difficulty, dev quests
│   ├── Items/                   # ItemCatalog, Inventory, ResearchCatalog, CraftingCatalog, Unlocks
│   ├── Terrain/                 # TerrainGrid, IslandGenerator, IslandSettings, PropScatter
│   ├── Sim/                     # Balance-simulation harness: headless sweeps, visual runs, the lab
│   └── *.cs                     # Units, buildings, economy, day/night, combat, camera, clouds
├── Editor/
│   ├── LowPoly/                 # Procedural low-poly art generator, plumber, scatter table
│   ├── Sim/                     # Sweep runner + the silent sim player build
│   └── FullSetup.cs             # Runs all eight setup steps in dependency order
├── Art/  Prefabs/  Materials/  Audio/  Settings/  Shaders/
├── Resources/                   # Changelog, Information, DevQuests text + cloud materials
├── MainMenu.unity               # Entry point
├── MainIsland.unity             # The game scene
└── Scenes/SampleScene.unity     # Stock Unity scene, unused
SimSweeps/                       # Sweep definitions (JSON) and kept baselines
tools/
├── run-sim.ps1                  # Runs a balance sweep; -Visual watches it, -Lab tiles nine windows
├── run-overnight.ps1            # Unattended batch: rebuild, ten sweeps, REPORT.md
├── summarize-sim.ps1            # REPORT.md for any folder of sweep results
└── verify-scripts.py            # Roslyn compile check of every script in four configs, no Unity launch
docs/
├── ALPHA_PLAN.md                # The road to a build in a tester's hands (live)
├── ARCHITECTURE_LAP_PLAN.md     # Factions, spatial hash, governor, save/load, islands (live)
├── CONTROLS.md                  # Full control reference
├── MENU_WIREFRAMES.md           # Every menu screen as a text wireframe, for an artist
├── PHASE_HISTORY.md             # Session-by-session developer log
├── SCALING_NOTES.md             # Why the architecture lap is ordered the way it is
├── SIMULATION.md                # Balance-sim harness guide
└── plans/                       # Built, superseded and parked design docs, kept for their
                                #   locked decisions. Each opens with a status banner
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
- **Ownership is data** — every unit and building has a `Faction` (resources, population, knowledge, priorities, stance), and every scan filters by relation rather than keeping a second list.
- **Singletons** — AudioManager, WallGrid, AIWorldState, GameManager, BuildingDatabase. No `DontDestroyOnLoad`, so nothing goes stale across a restart.
- **Point-of-effect reads** — difficulty, settings and unlocks are read where they take effect, never pushed.
- **Zero GC in hot paths**, throttled NavMesh calls, dirty-checked UI text.
- **Buildings are data** — `BuildingData` ScriptableObjects define costs, prefabs and placement rules. Walls draw as lines and auto-connect with procedural meshes.

Deeper technical notes, the gotcha list and the session log: [`.claude/CLAUDE.md`](.claude/CLAUDE.md) and [`docs/PHASE_HISTORY.md`](docs/PHASE_HISTORY.md).

---

## Game Systems

| System | Description |
|--------|-------------|
| **Opening** | Name your castaway, land at the wreck, gather beach materials, walk ashore and place the campfire. The clock is held until the fire is lit. |
| **Your character** | Yours for the whole run. Never a colonist, takes no housing, eats nothing, ignored by enemies. Right-click fetches pickups into a six-slot inventory, picks a bush bare-handed, works a tree or rock once research hands over the matching tool, deposits at the fire, and builds a construction site by standing at it. Knocked out rather than killed. Losing the campfire is the only defeat. |
| **Economy** | Wood, food, stone, metal. Workers gather autonomously, spread themselves over nearby nodes rather than piling onto one, and fan out around the campfire to hand in. Every tree on the island is choppable and the big boulders are quarryable. Metal is deliberately scarce. |
| **World** | A new island every game: size, terrain style and an optional seed are picked on New Game and locked for the run. Plateaus, cliffs, ramps, ponds; every plateau is reachable. |
| **Pickups** | Sticks and small piles of stone that trickle-respawn, plus finite salvage along the shore. Single small rocks are scenery. Job workers detour for nearby ones; idle colonists haul anything within 70 m of the fire by day, 30 m after dusk. |
| **Colonists** | People are a pool, not a purchase. Survivors land while housing has room. Idle colonists are the colony's utility labour — build, then craft, then repair, then tidy — weighted by four priority sliders, with Builder / Crafter / Repairer specialists to pin one. With nothing to do they stroll the village by day, and neither they nor patrolling warriors ever stop in a gateway or on the wall line. Everyone has a name and a trait (Steady, Night Owl, Early Riser, Hardy, Lazy) that shifts the hours they keep; they work into the evening, deliver what they carry and sleep from midnight to dawn in their hut or beside the fire. Warriors are idle colonists taking up a spear and keep their name. |
| **Building** | Hut, Wooden and Stone Wall, Gate, Watchtower, Workshop, Shipyard. Placement flattens a pad. A site only rises while a colonist or your castaway works it. Repair costs a quarter of the build price. |
| **Research and crafting** | Research is one-time and opens jobs, build mode, weapons and the Workshop, and hands your character the matching tool. Recipes are repeatable and gated behind research. Both live on stations with a queue that only moves while someone stands at the bench. Costs are paid on completion; a short entry waits rather than failing. |
| **Storage** | Materials, spears and tools live in the campfire stockpile, 60 items to start, raised by research. The four pooled resources are uncapped. |
| **Combat** | The militia takes one colony-wide stance — Defensive, Offensive or Follow — and stands in a Line, Wedge or Ring. Warriors converge on a raider from different sides; archers keep their distance. Watchtowers buff nearby damage. Housing is the only cap on army size. The levy: every spare weapon in the stockpile arms a colonist when raiders reach the fire or the bell rings (F10); they walk to the fire for it, fight, and half a minute after the last threat put it back and return to their job. |
| **Calendar and raids** | 150 s day, 75 s night, 30 days to rescue, about two hours. Colonists sleep from midnight to dawn. A dawn roll decides whether raiders land that night, never before day 3 and forced after five quiet ones, and the size is fixed at the roll from the day number and the colony's prosperity. A raid night lasts until the last raider is dead. |
| **Difficulty** | Six presets plus Custom, chosen on New Game and locked for the run. Scales raid size and frequency, enemy stats, night length, starting resources and run length. |
| **Menus** | Main menu, New Game, pause, options across four tabs, rebindable controls, changelog, field guide, end screens. All built at runtime in code — see [`docs/MENU_WIREFRAMES.md`](docs/MENU_WIREFRAMES.md). |
| **Weather and light** | A sky condition rolled each dawn: drifting cloud puffs whose shade slides across the island as the sun's light cookie. Graphics presets in Options. |
| **Rival colonies** | Optional, off by default (the Rivals setting on New Game). About a third of the way through the run another ship breaks up and its survivors found a colony on a far beach of their own. A banner says it happened, not where; their camp stays under your fog until your people find it. Every colony, yours included, prefers to gather around its own fire. A rival runs itself with one of the sim's three temperaments: it researches, builds huts and a wall, arms warriors, and faces the same raids you do — the dawn warning names whose shore the raiders make for, and the richer camp draws them more often. |
| **Diplomacy** | What a colony thinks of you is a word on the DIPLOMACY screen (Esc, or the Neighbours entry on the bar once you have met them). Warriors on their side of the shared ground (nearer their fire than yours) and blows landed cool it; quiet days and peace warm it; two cool days make an enemy, two warm days an ally. Propose peace with a gift once a day, or declare war. A hostile camp with warriors to spare lands a party on your shore by day and sails home at dawn; an ally sends half its warriors when raiders are at your fire, and its camp shows on your minimap. A landing party besieges the camp it lands on — huts and works, then the wall, then the fire — and a campfire your warriors put out spills its colony's whole hoard onto the ground as pickups for any hauler (raiders burning a camp leave nothing). |
| **Fog of war** | The island starts under one solid dark colour, the same by day and night, and clears for good as your people and buildings see it; ground nobody is watching sits in a grey shroud. Raiders show only while something of yours can see them, so a raid can be an ambush and the Watchtower's long sight is its second job. Colonists only gather and fetch on explored ground, warriors only fight raiders something of yours can see, and nothing can be placed in the dark. A north-up minimap in the top-right corner draws the explored island, your buildings, walls and people, raiders on watched ground, the camera's footprint, and a red pulse where the last raid came ashore; click or drag it to move the camera. |
| **Readability** | Anything standing between the camera and one of your people (a tree, hut, tower, workshop, shipyard or wall) stays solid but opens a soft see-through window right where they are, so nobody is ever lost behind a canopy. Hover glow is emissive so it works through it. |
| **Balance sim** | Scripted strategies play full games and write CSVs, so balance is measured rather than guessed. Headless for sweeps, with a live dashboard in the terminal; `-Visual` renders the same run with a spectator camera, `-Lab` tiles nine windows — three islands, each played three ways side by side — and `run-overnight.ps1` plays ten sweeps unattended and writes a report. With rivals on, the CSVs, the dashboard, the camera and the report follow the neighbour too. See the **Simulation** section below and [`docs/SIMULATION.md`](docs/SIMULATION.md). |

---

## Simulation

Scripted strategies (Turtle / Rush / Eco, plus Conqueror, a player-only war test that declares war on the first rival ashore and sails against it every quiet day) play whole games without a human and write CSVs. Full guide: [`docs/SIMULATION.md`](docs/SIMULATION.md). Everything below runs from the repo root in PowerShell and needs the sim player built once (`Tools > Island RTS > Simulation > Build Headless Sim Player`, or let the overnight script do it). **Rebuild the sim player after any code change** — a script-only build rewrites `Build/SimPlayer/islandrts-sim_Data/Managed/Assembly-CSharp.dll`, so check that file's date, not the exe's.

### The four ways to run it

| Command | What you get |
|---|---|
| `.\tools\run-sim.ps1 -Sweep SimSweeps\baseline.json -Parallel 4` | **Headless sweep.** No windows, 15–30× realtime per process; the terminal shows a live dashboard (one row per process: day, colonists, food, warriors as `5+3` while the levy is mustered, the spare-weapon rack, campfire, the rival once one lands, state; a finished-runs counter; the survive tally). `runs.csv` + `days.csv` in `SimLogs/`. |
| `.\tools\run-sim.ps1 -Sweep SimSweeps\watch.json -Visual` | **Watch one.** Same decisions as headless, drawn in a window with a spectator camera and a metrics caption. ~4× by day, ~2× during a raid. |
| `.\tools\run-sim.ps1 -Lab` | **The lab.** Nine tiled windows: three islands (rows) each played three ways (columns), a live dashboard in the terminal, lost cells respawn for the first `-RespawnMinutes` 10. `-Seeds 7,8,9`, `-Strategies Eco`, `-WindowSize 480x270`, `-Rivals 1 -RivalStrategy Turtle` to seat a governed rival in every cell. |
| `.\tools\run-overnight.ps1` | **Overnight batch.** Close the editor first. Keeps the PC awake, rebuilds the sim player, plays `baseline` / `raids` / `difficulty` / `islands` / `rivals` / `levy` / `conquest` / `long` / `clock` / `economy` headless at `-Parallel 8` (~910 runs, ~10 h), writes `SimLogs/overnight-<date>/REPORT.md` (a second batch the same day gets a `-HHmm` suffix), then sleeps the PC after a 60 s countdown. `-DryRun` to preview, `-SkipBuild`, `-Sweeps baseline,raids`, `-NoSleep`. |

Afterwards: `.\tools\summarize-sim.ps1 -Dir SimLogs\overnight-<date>` regenerates the report for any folder of sweep results. `SimSweeps/smoke.json` is a two-minute sanity run, `SimSweeps/rivals.json` seats zero, one or two rival colonies on the baseline island (the report then adds a Neighbours table: arrival, contact, opinion, landings, relief, the rival's fate); `Tools > Island RTS > Simulation > Run Sweep In Editor…` plays a sweep inside the editor.

### Writing a sweep

A sweep is a JSON file: process-wide options plus a list of runs. Every knob defaults to `-1` = "don't override", so a run only names what it varies.

```json
{
  "outputDir": "SimLogs", "captureDeltaTime": 0.016666668,
  "repeats": 3, "maxWallSecondsPerRun": 2400, "respawnWallMinutes": 0,
  "runs": [
    { "id": "hard_eco", "strategy": "Eco", "seed": 1042, "terrainSeed": 1042,
      "difficulty": "Hard", "islandSize": "Large", "islandStyle": "Rugged",
      "raidSizePerDay": 0.55 }
  ]
}
```

Sweep-level: `repeats` (the whole list again with `seed + 1`, ids suffixed `_s<seed>`; `terrainSeed` is kept, so a repeat is the same island with different dice), `maxWallSecondsPerRun` (real-seconds ceiling; a frozen clock is caught separately), `respawnWallMinutes` (replay a lost run while the sweep is younger than this), `renderFrameInterval` / `renderFrameIntervalRaid` (visual draw rate only, never a decision).

Per run:

| Group | Fields |
|---|---|
| Identity | `id`, `strategy` (Turtle / Rush / Eco / Conqueror), `seed` (all RNG), `terrainSeed` (the island; `-1` = the scene's), `daysToSurvive`, `maxGameSeconds` |
| Rule set | `difficulty` (Peaceful / Relaxed / Normal / Hard / Brutal — every preset multiplier, but NOT the calendar: write `daysToSurvive` 20 for the gentle two yourself), `islandSize` (Small / Medium / Large), `islandStyle` (Rolling / Terraced / Rugged) |
| Economy | `startingWood`, `startingFood`, `startingStone`, `workerGatherRate`, `workerCarryCapacity`, `foodPerDay` (0 = nobody eats) |
| Raids, when | `raidFirstDay`, `raidBaseChance`, `raidChancePerQuietDay`, `raidMaxQuietDays`, `raidMinQuietNights` |
| Raids, how big | `raidBaseSize`, `raidSizePerDay`, `raidSizePerProsperity` — size = `base + perDay × day + perProsperity × prosperity` |
| Raiders | `enemyHealth`, `enemyDamage`, `enemyMoveSpeed`, `enemyAttackCooldown`, `enemyWarriorDetectionRange`, `spawnInterval`, `spawnDelay`, `spawnDistance` |
| Militia | `warriorHealth`, `warriorDamage`, `warriorMoveSpeed`, `warriorAttackCooldown`, `warriorCostFood`, `maxWarriors` (0 = no cap), `warriorSearchRadius`, `warriorPatrolRadius`, `warriorHealRate` |
| Buildings | `hutHealth`, `campfireHealth`, `watchtowerHealth`, `watchtowerDamageMultiplier`, `watchtowerBuffRadius` |
| Clock | `dayLengthSeconds`, `nightLengthSeconds` |
| Levy | `levyShare` (share of the wanted strength every governor leaves to spare weapons on the rack; `-1` = the policy's own: Turtle / Eco 0.5, Rush ⅓, Conqueror 0), `levyRackExtra` (weapons kept past the army's gap, `-1` = 0) |

The schema is `Assets/Scripts/Sim/SimConfig.cs`; if a knob is missing here, that file is the truth. Ready-made sweeps live in `SimSweeps/` and every overnight sweep is saved next to its results, so any of them replays alone with `run-sim.ps1 -Sweep <file>`.

### Reading the results

`runs.csv` is one row per game (outcome, day reached, raids, kills, peaks, finals, wall time, then the rival's strategy and fate — `alive` / `fell` / `deserted` / `conquered` — the peak opinion either way, the player's landings and what a conquered fire dropped). `days.csv` is one row per calendar day: resources and population at dusk and dawn, raid size, `campfire_hp_min` (200 or 5 — the fire dies in one night or not at all), `idle_dawn`, `weapons_dawn`, `sticks_dawn`, `chunks_dawn`, `queue_dawn`, `warriors_lost` (the player's full-time warriors), `ring_holes`, `chunks_loose`, the rival's dawn, and the levy's night — `spare_dusk`, `mustered_peak`, `levy_lost`, `asleep_landing`. The governors count the rack as soldiers: each keeps spare weapons for the levy and recruits full-timers only to a share (half; Rush two thirds; Conqueror all). Read error/timeout rows first — those are harness problems, not balance — and never call a one-or-two-run difference a finding: runs are comparable, not reproducible (async NavMesh, job order), and n = 12 is about ±13 pp on a win rate.

---

## Logging

The console is intentionally quiet — about 65 calls in the whole project. Only errors, recoverable-misconfiguration warnings, and once-per-night or once-per-game lifecycle lines. Everything per-unit, per-damage, per-tick and per-click has been removed. The keep-list and the rule for adding a new log live in [`.claude/CLAUDE.md`](.claude/CLAUDE.md) under **Logging Conventions**.

---

## Where the project is going

**Right now: the architecture lap** — factions, then a spatial hash and AI level of detail, then a colony governor with diplomacy and trade, then save/load (dawn checkpoints), then an archipelago of persistent islands. Step by step, with decisions and definitions of done: **[`docs/ARCHITECTURE_LAP_PLAN.md`](docs/ARCHITECTURE_LAP_PLAN.md)** (the reasoning behind the order is in [`docs/SCALING_NOTES.md`](docs/SCALING_NOTES.md)). Months of work; the first player-visible piece is the rival colony in step 3. **Step 1 (factions) landed on 2026-09-09:** ownership is data on every unit and building, every scan filters by relation, and the F4 debug menu can spawn a stub rival camp with a Hostile / Neutral / Allied toggle. **Step 2 is skipped for now; step 3 (rival colonies) is most of the way in** — Slice A (a rival lands on its own shore, territory) and the heart of Slice B (2026-09-16: the sim's policies became the colony governor and run every rival in real play; diplomacy with opinion, gifts and war; raids that pick a shore; landings and relief by sea) are built and awaiting their playtest and sim check. Still open: placement through the shared builder, a scored build order, and dock trade. The live plan is [`docs/RIVAL_COLONIES_PLAN.md`](docs/RIVAL_COLONIES_PLAN.md).

**After it: the alpha.** Playtesting the pile of built-but-unplayed work, tuning, a tutorial, a feedback path, and a build handed to testers — **[`docs/ALPHA_PLAN.md`](docs/ALPHA_PLAN.md)**, sections B → G, on the refactored foundation.

Parked with no committed order:

- [`docs/plans/COLONY_EXPANSION_PLAN.md`](docs/plans/COLONY_EXPANSION_PLAN.md) — collector radius and settlement tiers, processing chains, families, livestock and farming
- Building upgrades (hut to house, campfire to fortress) and a placeable Warehouse
- Enemies wading ashore from the shallows, the last unbuilt piece of [`docs/plans/TERRAIN_SYSTEM_PLAN.md`](docs/plans/TERRAIN_SYSTEM_PLAN.md)
- Phase 10 Stages 3-4: water polish and a lighting bake, [`docs/plans/PHASE_10_VISUAL_OVERHAUL.md`](docs/plans/PHASE_10_VISUAL_OVERHAUL.md)
- Setting and fiction: whether this stays one castaway's story or becomes pickable civilizations is an open question, not a plan

### History

The player-facing history is `islandrts/Assets/Resources/Changelog.txt`, which is also the in-game CHANGELOG screen: a short themed history where each entry folds to a one-line summary, with every entry before 2026-09-17 kept verbatim in [`docs/CHANGELOG_ARCHIVE.md`](docs/CHANGELOG_ARCHIVE.md). The developer history — every session, what broke and what it taught — is [`docs/PHASE_HISTORY.md`](docs/PHASE_HISTORY.md). Design plans that are already built are kept for their locked decisions: [`docs/plans/RESEARCH_AND_DAYS_PLAN.md`](docs/plans/RESEARCH_AND_DAYS_PLAN.md), [`docs/plans/CRAFTING_AND_PLAYER_CHARACTER_PLAN.md`](docs/plans/CRAFTING_AND_PLAYER_CHARACTER_PLAN.md), [`docs/plans/TERRAIN_SYSTEM_PLAN.md`](docs/plans/TERRAIN_SYSTEM_PLAN.md).

---

*A shipwreck survival RTS built in Unity*
