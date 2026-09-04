# Island RTS Game

A Unity-based real-time strategy survival game. Manage autonomous workers, gather resources, build defenses, recruit warriors, and hold out for thirty days against raids that are announced at dawn and grow with your colony.

**Genre:** Top-down RTS + Survival  
**Setting:** Age of Sail shipwreck on an uncharted island  
**Status:** Playable alpha (Phase 6.26 + Opening Sequence Stage 1 + Terrain T1–T4) — the game opens with the story beat (you name your castaway, wade ashore from the shipwreck, gather beach materials and place the campfire; your character then stays yours, researches the colony's first skills at the fire and crafts the spears that arm it — applied in-scene via the Opening Sequence setup tool) and the world is a procedurally generated island that is different every game (size, terrain style and seed picked on the New Game screen). All of that is applied by editor tools that must run in a specific order — **`Tools > Island RTS > Setup Everything (In Order)` does all eight in one go.** An F4 debug menu (editor/dev builds) covers resource grants, a quick-start colony, time controls, and combat cheats for playtesting. Phase 10 Stage 2 (low-poly art) is applied in-editor. Now on Unity 6000.5.9f1; Phases 6.24–6.26, the opening sequence, the terrain, the art plumbing, the menus, the settings/keybinding/difficulty pass and the rebuilt end screens all still await a proper playtest — the accumulated checklist, and the editor setup steps that must run first, are in [`docs/CONTROLS_AND_CHECKLIST.md`](docs/CONTROLS_AND_CHECKLIST.md).

---

## Quick Start

1. Clone the repo
2. Open `islandrts/` in Unity Hub (requires **Unity 6000.5.9f1**)
3. Open scene: `Assets/MainIsland.unity`
4. Press Play

> **Note:** `Assets/Scenes/SampleScene.unity` is the leftover stock Unity scene (3 objects) and is *not* the game. Build Settings still points at it — fix that before making a build.

### First Game

1. From the title screen, **NEW GAME** → pick a difficulty (Normal is the intended balance) → **BEGIN**
2. Name your castaway in the popup. Opening: **right-click** sticks and stones on the beach to gather them, right-click to walk ashore, then press **B** and click to place the campfire (free, must be near you). Your character stays yours for the whole run
3. Right-click the fire to deposit what you carry, then use the panel's **Research** tab: Woodcutting (2 sticks, 1 stone) teaches the colony to cut wood, Foraging to forage, Construction to build, Spearcraft to make spears. Your character stands at the fire while the queue runs — walk away and it waits. Survivors come ashore by day while there is free housing — the campfire houses 3 — and the Colonists tab hands them the jobs you have researched
4. Press **B** to build — place 1-2 Huts for housing. Buildings only go up while an idle colonist is working the site, so keep one or two unassigned
5. Watch the calendar chip on the HUD. The first two nights are always quiet; from day 3 a dawn may turn it red — "Raid tonight · N raiders" — and that is your day to arm 2-3 warriors (each one is an idle colonist taking a Wooden Spear from the stockpile — queue spears on the **Craft** tab ahead of time, ×5 at a go)
6. Reach the dawn after day 30 and the rescue ship arrives

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
| **Esc** | Cancel the current action (build ghost, wall line, demolish, campfire / workshop panel) — opens the pause menu when nothing is active |
| **F2** | Build grid overlay (also auto-shows during build mode) |
| **F3** | AI debug overlay (editor only) |
| **F4** | Debug menu — cheats for testing (editor + dev builds only) |
| **F6 / F7** | Perf recorder — F6 marks the frame where you saw a stutter, F7 stops/resumes (editor + dev builds only) |
| **Right-click** | Command your character: fetch a stick / stone / crate, deposit at the campfire (and open its panel), or walk there |
| **Space** | Centre the camera on your character |
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
│   │   │   ├── Worker/          # Gather, Return, Pickup, Build, Repair, Flee, Idle
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
│   ├── GameStartController.cs   # Opening sequence (name popup → landing → campfire → colony)
│   ├── PlayerCharacter.cs       # Your named character: right-click commands, inventory, crafting, knock-out
│   ├── CraftStation.cs          # A bench with a queue (campfire + workshop); moves only while someone stands at it
│   ├── Items/                   # ItemCatalog, Inventory, WorkDef, ResearchCatalog, CraftingCatalog, Unlocks, HeldItem
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
| **Opening** | A name popup, then your castaway lands at the wreck, gathers sticks and stones on the beach, walks ashore and places the campfire (day/night clock held until it's lit). |
| **Your character** | Stays under your control all run — never a colonist, takes no housing, ignored by enemies. Right-click fetches pickups into a six-slot inventory (HUD strip at the bottom) and deposits at the fire: resources into the pool, sticks and stones into the campfire stockpile. Has HP that regenerates by the fire; at zero it is knocked out and stands back up ten seconds later. Losing the campfire is still the only defeat. |
| **Economy** | Wood, food, stone, metal. Workers gather autonomously (cutters, foragers, quarriers, miners). Carry capacity 5, 1/sec rate. Every tree on the island is choppable, shoreline palms included. Metal has no cost sink yet. |
| **World** | A new island every game: size (110 / 150 / 190 m), terrain style (Rolling / Terraced / Rugged) and an optional seed are picked on the New Game screen and locked for the run. Plateaus, cliffs, ramps, ponds, rocky and sandy shores; every plateau is guaranteed reachable. |
| **Pickups** | Sticks and stones scattered on the island (+3 wood / +3 stone), which trickle-respawn, plus one-time salvage: the wreck's cargo and the crates and barrels along the shore (+6 food / +5 wood). Workers of the matching job detour to collect nearby ones. |
| **Colonists** | People are a pool, not a purchase. While there is free housing (campfire 3, hut 2), a survivor lands at the cove every ~20 s by day and walks to the home with room. The campfire panel hands idle colonists jobs and takes them back; it never spawns anyone. Idle colonists are the builders and repairers. Warriors are idle colonists taking up arms (a Wooden Spear from the stockpile + 15F) and occupy housing; dismissing one returns the spear, losing one loses it. |
| **Building** | Hut (housing), Wooden/Stone Wall, Gate, Watchtower, Workshop. Placement flattens a pad in the terrain. A site only goes up while an idle colonist is working it (one builder ≈ 10 s, up to three stack); with nobody idle it reads "Awaiting builder". Repair costs a quarter of the build price, paid as HP comes back. |
| **Research** | The tech tree, on the campfire panel's Research tab: Woodcutting / Foraging / Quarrying / Mining open the four jobs, Construction opens build mode and builders, Spearcraft opens spears and warriors, Crafting opens the Workshop. Each is researched once from sticks, stones and a little wood or stone; every locked control names the research that opens it. The Workshop lists its own tier: Sharpened Tools (+30% gather) and Sturdy Scaffolds (+50% build speed). |
| **Crafting** | Repeatable recipes at any bench: Wooden Spears (queue ×5) go to the campfire stockpile and arm warriors; the five player tools are cosmetic, one per run. The campfire and the Workshop are *stations* with a queue that only moves while someone stands at the bench — your character today, a Crafter colonist next. Costs are paid when an item finishes; a short entry waits for what is missing instead of failing. |
| **Combat** | Warriors auto-engage enemies. Watchtowers buff nearby warrior damage 1.25x. |
| **Calendar** | 100s day / 50s night, 30 days to rescue (~75 minutes). Raids are not nightly: at each dawn a roll decides whether raiders land tonight — never before day 3, the chance climbing with every quiet night and guaranteed after five — and the HUD announces it for the whole day. |
| **Raids** | Size is fixed at the dawn roll: `2 + 0.4 x day + 0.08 x prosperity`, where prosperity counts colonists, warriors, buildings and stock on hand. A first raid on a bare colony is ~5 raiders; a day-20 colony with a garrison and walls sees ~16. Raiders land 0.4s apart from one direction so a raid arrives as one body, and anything left at dawn withdraws. |
| **Healing** | Warriors heal 1.5 HP/sec at campfire between raids — slow on purpose, so damage carries from one raid to the next. |
| **Flee** | Workers garrison inside the nearest hut when enemies threaten, and pop back out when it's clear (or if the hut falls). |
| **Demolish** | Delete/X key. 50% resource refund. Campfire protected. |
| **Victory** | Reach the dawn after day 30 (20 on Peaceful and Relaxed) — the rescue ship arrives. Defeat if the campfire is destroyed. Both end on a screen showing days survived and raids weathered, offering Restart / Main Menu / Quit, plus Keep Playing after a win. |
| **Difficulty** | Peaceful / Relaxed / Normal / Hard / Brutal / Custom, chosen on New Game and **locked for the run**. Scales raid size, raid frequency, enemy health and damage, night length, starting resources, and days to rescue. Hard and Brutal keep the 30-day calendar and raid more often instead of running longer. |
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

Latest: **Research teaches, crafting supplies.** The six "craft a tool once and the colony learns a job" recipes became a tech tree. Research is one-time and lives on the campfire panel's new Research tab — Woodcutting, Foraging, Quarrying, Construction, Spearcraft, Crafting, Mining at the fire, and the Workshop's two upgrades as Workshop-tier entries — and every job row, the warriors row and the B key now name the research they wait on. Recipes are repeatable and gated by research: the Wooden Spear is equipment, crafted ×5 at a go into the campfire stockpile, and a warrior is an idle colonist taking one plus 15 food — the old 10 wood is gone, and dismissing a warrior hands the spear back. Both the campfire and the Workshop are crafting *stations* with a queue that only moves while someone stands at the bench: pressing Craft or Research sends your character over, right-clicking the fire deposits and then works whatever is queued, and walking away simply pauses it. Costs are paid when an entry finishes, and an entry that comes up short waits at 100% with "Waiting for 2 Stick" rather than failing, so a queue of five spears cannot lose four because a colonist spent the wood. The old Workshop panel is gone; clicking a Workshop opens the same panel with its Craft · Research · Queue tabs. Two things surfaced on the way: the Warrior prefab has carried 25 damage and range 2 all along while the docs said 15 (the spear encodes the live numbers, so nothing changed in combat), and the balance sim can no longer be handed its unlocks for free — its policies research and queue spears like a player, and a new driver walks the character out to fetch sticks and stand at the bench. Slice 2 of [`RESEARCH_AND_DAYS_PLAN.md`](RESEARCH_AND_DAYS_PLAN.md); Slice 3 adds the Crafter job, the Workshop's 2× speed and the Iron Spear. Sweeps from before this change are not comparable.

Before that: **A calendar instead of a countdown of waves.** Every night used to bring a wave, and the run ended after five of them — which left no room for the crafting and research layer to breathe. The run is now a thirty-day calendar: most nights are quiet, and when a raid does come it is announced. At each dawn a raid director rolls whether raiders land tonight — never before day 3, the odds climbing with every quiet night and forced after five — and the size is fixed at that roll from the day number plus the colony's *prosperity*: people, warriors, buildings and stock on hand. Building up during the warning day never enlarges the raid it was built against. The HUD grew a calendar chip that reads "Day 4 of 30" and turns red with "Raid tonight · 7 raiders" under a bold banner; victory is the rescue ship at the dawn after the last day, and the end screen counts days survived and raids weathered. Difficulty gained a raid-frequency knob — Hard and Brutal keep thirty days and raid more often rather than running longer, the gentle presets are twenty days. Days are 100 s and nights 50 s, about 75 minutes for a Normal run. The balance harness writes `days.csv` with a raid flag per row and its policies spend the reserve on warriors when a raid is announced. Three playtest fixes rode along: palm hitboxes were canopy-sized rectangles and, like every tree, sat on the layer the placement ray uses, so build ghosts snapped to tree bases — nodes now have trunk-sized boxes on their own layer; and the campfire panel opened squashed because its tab bodies were measured before their first layout pass. This is Slice 1 of [`RESEARCH_AND_DAYS_PLAN.md`](RESEARCH_AND_DAYS_PLAN.md), which also locks the next steps: research that opens recipes, crafted weapons consumed per warrior, crafting stations worked by the player or a Crafter colonist, food consumption, archers and an escape ship. Balance sweeps from before this change are not comparable.

Before that: **You are the castaway, and the colony learns from what you make.** The opening used to end with the survivor dissolving into the first colonist. Now a popup asks their name, and that character stays yours for the whole run — right-click walks them, fetches sticks and stones and washed-up crates into a six-slot inventory, and deposits it all at the fire, resources into the pool and materials into a new campfire stockpile. The campfire panel grew Stockpile and Craft tabs: standing at the fire, your character crafts six tools from beach materials, and each first craft is knowledge the colony keeps — the Stone Axe lets colonists cut wood, the Fishing Spear forage, the Stone Pick quarry, the Metal Pick mine, the Mallet opens build mode and lets idle colonists build and repair, the Wooden Spear arms warriors. Until then every job row, the warriors row and the B key say exactly which tool they are waiting on. Costs are taken when a craft finishes, so walking away loses nothing; the crafted tool appears in the character's hand. The character has HP and is knocked out rather than killed; enemies ignore it and the campfire remains the only thing you can lose. Pickups carry a click collider on a dedicated layer so placement raycasts never see them, and a cluster of sticks and stones now sits on the landing beach. Re-run `Setup Everything (In Order)` to apply. Balance sweeps from before this change are not comparable: a run now starts with zero colonists and every unlock is granted under the sim. Plan and locked decisions: [`CRAFTING_AND_PLAYER_CHARACTER_PLAN.md`](CRAFTING_AND_PLAYER_CHARACTER_PLAN.md).

Before that: **People are a pool, and buildings need hands.** Workers used to be conjured by the campfire panel — click plus and a fully-formed wood cutter appeared, capped only by housing. Now housing brings *people*: while the colony has a free slot, a survivor comes ashore at the cove every twenty seconds or so by day and walks to the hut that has room, and the panel simply hands idle colonists jobs and takes them back. Idle colonists are not idle for long — they are the builders. Construction no longer finishes on a timer; a site waits, reading "Awaiting builder", until a colonist reaches it, and three on one site work three times as fast. They repair too, paying a quarter of the build price as the HP comes back and downing tools when the pool runs dry. Warriors come out of the same pool: recruiting arms an idle colonist where they stand, so soldiers take up housing and a fallen one frees a slot for the next survivor. The opening's castaway is now the first colonist rather than the first wood cutter. No editor setup step needed. Balance sweeps from before this change are not comparable to sweeps after it — construction used to be free.

Before that: **The wreck's cargo is worth something.** The crates and barrels at the landing site, and the ones washed up along the shore, were scenery. They are supplies now: a worker of the matching job walks over, hauls one back to the fire, and it lands in the pool like any other delivery. Contents follow the silhouette — a crate holds food, which is what the opening is thinnest on, and a barrel breaks down into wood, so beachcombing is worth a wood worker's time too. Salvage is finite and deliberately different from the sticks and stones: nothing respawns it, and a crate grants its full contents even though that is more than a worker can normally carry, because a crate that quietly evaporated most of itself would read as a bug. Driftwood stays scenery. Re-run `Setup Everything (In Order)` to apply.

Before that: **Trees you can see through, and trees you can all chop.** Workers disappeared behind canopies, so a tree now fades out while anything is standing behind it and fades back the moment it clears — decided in screen space rather than with raycasts, so one pass over the tree list answers it for every unit at once and the cost does not grow with the crowd. Every tree on the island is harvestable too: the palms along the shore used to be scenery, and are now real resource nodes that shrink as they deplete, carry a little less wood than the inland trees, and are kept off ground no worker could reach. The metal node lost its bright veins and is a plain boulder again — the stone node keeps its crystals, which is what tells the two apart at a glance. Re-run `Setup Everything (In Order)` to apply.

Before that: **First playtest of the new world — water and HUD fixes.** The sea flipped light across the whole screen at some camera angles, with scattered dark tiles and, zoomed out, big diagonal stripes. Two causes: the camera is orthographic, so a hard-stepped specular has no highlight *spot* and simply switches the entire flat plane on or off with tilt and heading; and the 3 m water grid was sampling a 4 m wave, which aliased into a slow beat pattern. The shader now uses a soft sheen that cannot flip, gets its sparkle from per-triangle tone drift and glints that don't depend on the view, and runs on a 1.5 m grid with waves 11–20 m long. The resource bar is one continuous strip of five equal entries with thin dividers — big amount on top, `Wood · N workers` on one unbroken line beneath, worker count dimmer than the number — and the campfire panel opens bottom-left and can be dragged by its title bar (clamped to the screen, position remembered).

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
- **Phase 7 (revised, colonist pool shipped 2026-09-02):** jobless colonists, labour-gated construction and paid repair are in. Remaining: building upgrades (hut → house, campfire → fortress), storage
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
