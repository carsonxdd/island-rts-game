# Architecture Lap Plan — factions, spatial hash + AI LOD, colony governor, save/load, islands

**Written 2026-09-09. Supersedes the freeze in `ALPHA_PLAN.md`: the lap starts now, the alpha ships on top of it.** The order and the reasoning come from [`SCALING_NOTES.md`](SCALING_NOTES.md) and section J of the alpha plan; this page is the build shape, step by step, with the decisions taken on 2026-09-09 and the ones still open.

**Decisions taken 2026-09-09 (with the user):**

1. **Timing:** build now. The freeze is lifted. The alpha's remaining items (playtest debt, tuning, tutorial, feedback form, zip) ship after the lap, on the refactored foundation.
2. **Factions in the end state:** rival AI colonies that play the game, with **diplomacy (hostile / neutral / allied) and trade at a dock**. Raiders stay a faction with no colony.
3. **Save/load:** **dawn checkpoint.** Autosave every dawn; manual save only while no raider is alive. AI state is never serialised; units re-decide within one eval tick of loading.
4. **Islands:** **archipelago world map, one persistent colony per island.** Rivals live on other islands and raid across water. Islands the player is not on run as abstract economies (AI LOD).

**The honest cost, restated:** months. Nothing player-visible lands until step 3 (a rival camp), and the alpha's testers will not see any of it before step 5. Every step ends green in all four Roslyn configs, with the sim harness running, and with a commit, so the project is shippable between steps.

---

## What the code says today (inventory, 2026-09-09)

The refactor is sized by these numbers, not by feel. All paths relative to `foundingtide/Assets/Scripts`.

| "One colony" assumption | Sites |
|---|---|
| `ResourceManager.Instance` | 64 uses in 21 files (SimBuilder 10, WallLinePlacer 7, DemolishTool 5, DebugMenu 5, BuildPlacement 5 …) |
| `PopulationManager.Instance` | 60 uses in 11 files (BaseBuilding 19, ResourceUI 12, Hut 7, DebugMenu 7 …) |
| `BaseBuilding.ActiveList[0]` as "the campfire" | 3 (`AIWorldState.UpdateCampfireState`, `EnemyAttackExecutor.FindLiveCampfire`, `SimBuilder.Campfire`) |
| `BaseBuilding.FindAlive()` as "my campfire" | 13 uses in 11 files |
| `X.ActiveList` scans (all owners mixed) | 217 uses; Enemy 25, ConstructionSite 21, Hut 20, Wall 19, Watchtower 17, Warrior 17, Shipyard 12, Gate 11, Workshop 10, Worker 8, ResourceNode 7, GroundPickup 5 |
| Faction-ambiguous statics | `Unlocks.granted[]`, `ResearchCatalog.All[i].done`, `CraftedUpgrades.*`, `LaborPriorities.*`, `GuardStance.Active`, `Formation.Active`, `ResourceManager.{wood,food,stone,metal}` |
| Enemy target function tiers reading registries with no owner filter | `Warrior`, `Hut`/`Watchtower`/`Workshop`/`Shipyard`, `Wall`/`Gate`, `BaseBuilding` |
| Considerations that walk a whole registry | `ResourceAvailability`, `PickupAvailability`, `ForageAvailability`, `EnemyPresence`, `StanceTargetAvailable`, `ConstructionAvailable`, `StationWorkAvailable`, `RepairAvailable` (six registries), `ConstantScore` |

Other facts that shape the plan:

- `TargetingUtil.FindNearest<T>(IReadOnlyList<T>, from, maxRange, out dist)` is the ONE scan primitive; every scan already goes through it. That is the seam for both the faction filter and the spatial hash.
- `AIWorldState`'s enemy density grid is hard-coded to ±150 m, cell 10, world-origin anchored; it reads `Enemy.ActiveList` only.
- `ActiveRegistryReset` forgets `Shipyard` and `CraftStation`. Fix in step 1.
- `TerrainGrid.FlattenArea` is unrecorded: pads live only in `heights[,]`. Save/load must replay them.
- `MenuFlow` does full scene reloads; statics are reset by `[RuntimeInitializeOnLoadMethod]` hooks that fire once per play session, not per load. Every static that holds game state must have a per-load reset path (many do through `OnDestroy`; the rest are listed in step 4).
- `SimPolicy` (Turtle / Rush / Eco) already has the action vocabulary of a colony governor: `Research`, `KeepSpears`, `HireWorker`, `Recruit`, `BuildHutIfCapped`, `RunWorkshop`, `PlaceWallRing`, `ConvertGates`, `PlaceShoreBuilding`. `SimBuilder` mirrors `GhostPlacer` / `WallLinePlacer` step for step and has to be kept in sync by hand. Step 3 makes them one thing.
- `PropScatter` has a private placement-only `SpatialHash`; not reusable for entity queries.
- `GameStartController` already has a "skip intro" classic start (campfire immediately, no popup). That is the entry for loading a save.

---

## Cross-cutting rules for the whole lap

- **Every step is a green build in all four Roslyn configs before commit**, and `Setup Everything` is re-run whenever a prefab or scene is touched.
- **The sim harness is the regression suite.** Before step 1 starts, run Turtle / Rush / Eco at n=6 on one seed and keep the CSVs as the "pre-lap" baseline (`SimSweeps/baseline-2026-09-09/`). After every step, run the same three. The check is not balance (which will drift) but AI health: `campfire_hp_min`, worker idle share, unreachable-target counts, and zero NREs in the player log. A step that changes those without a known reason is not done.
- **A compatibility shim lives for one step, then dies.** `ResourceManager.Instance` → `Faction.Player.Resources` etc. keeps the build green mid-migration; the step's definition of done includes deleting the shim and adding the old accessor to the CLAUDE.md banned list, the way `FindObjectsByType` was.
- **`DevQuests.txt` gets a batch per step**, with signals at the point of effect; steps 3–5 also get `Changelog.txt` entries because they are player-visible. Steps 1–2 are not visible and get no changelog line.
- **Sweeps are not comparable across steps.** Re-baseline after each.
- **Faction is read in `Start` or later, never in `Awake`.** `Instantiate` runs `Awake` before the caller can set the owner, so anything that needs its faction at construction (housing registration, campfire population lookup) moves to `Start` or first use. This rule goes into CLAUDE.md the day step 1 lands.
- **One data model for three jobs.** The `ColonySnapshot` / `ColonyLedger` records built in step 2 (abstract tier) are the same types that save/load writes (step 4) and that island travel materialises from (step 5). Do not build three serialisers.

---

## Step 1 — Factions

**Goal:** ownership becomes data. A second colony can exist on the island with its own resources, population, research, stance and campfire, and every scan, economy call and population check asks "whose?" The second faction stays a debug-spawned stub until step 3. The Raiders become a faction too, so "hostile" is a relation, not a type.

### Shape

- **`Faction`** — a plain C# class, not a MonoBehaviour (survives without a scene object, serialisable, testable). Owns:
  - `Id` (stable small int), `Name`, `Colour`, `Kind` (`Player` / `Rival` / `Raiders`).
  - `Resources` (the `ResourceManager` pool, moved off the singleton), `Population` (the `PopulationManager` roster, housing list, arrival timer, food debt, `starvedSeconds`), `Unlocks`, `Research` (done flags), `Upgrades` (`CraftedUpgrades` values), `LaborPriorities`, `Stance` (`GuardStance.Mode`), `Formation`, `Campfire` (a `BaseBuilding`, may be null), `Stockpile` (stays on the campfire; `Faction.Stockpile` forwards).
  - `Relations`: `Attitude Toward(Faction other)` — `Hostile` / `Neutral` / `Allied`, backed by a small symmetric matrix on a static `Factions` registry. Raiders are hostile to everyone and nobody can change it.
- **`Factions`** — the static registry: `Player`, `Raiders`, `All` (list), `ById`, `Register`, `ResetAll()` per scene load (subscribe `sceneLoaded`, the `PauseController` pattern), and the debug spawn.
- **`IOwned { Faction Faction { get; } }`** added to `ITargetable`. Every unit, building, construction site and craft station gets a `Faction` field set right after `Instantiate` by a `Spawn.Owned(prefab, pos, rot, faction)` helper (the only way to instantiate an owned thing). Resource nodes and pickups are unowned. `PlayerCharacter` is `Factions.Player`.
- **Registries stay global; scans get a filter.** `ActiveRegistry<T>` keeps its global list (minimap, fog, `UnitHoleMask`, perf, debug all want everything). `TargetingUtil` gains the two overloads that cover every case, both zero-GC (a `Faction` and a relation enum, no delegates):
  - `FindNearestOwned<T>(list, from, range, Faction owner, out dist)` — my huts, my sites, my stations.
  - `FindNearestHostile<T>(list, from, range, Faction me, out dist)` — anything whose faction is `Hostile` to me.
  - Per-faction counts (`faction.Count<Hut>()`) come from a `FactionCounts` table maintained in `Register`/`Unregister`, so prosperity, housing and UI never walk a list to count.
- **`AIBlackboard.faction`** set in every unit's `Start` from `Faction`; considerations and executors read `bb.faction`, never a static.
- **`AIWorldState` becomes per-faction where it was "mine":** `campfirePosition` / `campfireExists` / `campfireHealthPercent` move to `Faction.Campfire`; the density grid keeps one grid per faction id (4 × 900 ints) and `GetNearbyHostileCount(pos, me)` sums the hostile ones; `wallsUnderAttack` is per faction. Time of day stays global.
- **Enemy target function** tiers become "hostile warrior in range → hostile campfire commit → hostile reachable building → hostile wall/gate → hostile campfire". Same executor, same order, the filter added. A raider with two hostile colonies in reach attacks the nearer.
- **Warrior Engage / Intercept / DefendWall** scan hostile `Warrior`s AND `Enemy`s. `Enemy` stays its own unit type (the raider body, no economy) so nothing about the Raiders' landing flow changes in this step. `StanceTargetAvailable` and `EnemyPresence` read the merged hostile scan.
- **Worker Flee** reacts to hostile presence, not to `Enemy` specifically. Damage between factions checks `Relations` at the hit site, so an Allied warrior standing in a fight never takes a spear.
- **Fog stays the player's.** Only `Factions.Player` has a `FogOfWar`; rivals and raiders are omniscient by decision (an AI that respects fog is a different game, and a cheating rival is standard RTS practice). `VisionSource` registers only for player-owned things.
- **`DayNightCycle` and `RaidDirector` stay world-level.** The dawn roll targets `Factions.Player` explicitly (rival raids are step 3).

### Migration order (each bullet is a commit that builds)

1. Add `Faction`, `Factions`, `IOwned`, `Spawn.Owned`, `Relations`. Create `Player` and `Raiders` at scene load. Nothing reads them yet. Fix `ActiveRegistryReset`'s two missing registries.
2. Move the resource pool into `Faction.Resources` with `ResourceManager.Instance` as a forwarding shim to `Factions.Player.Resources`. Rewrite the 64 sites: UI, placement, demolish, repair and debug → `Factions.Player`; executors and considerations → `bb.faction`; sim → `Factions.Player`. Delete the shim.
3. Same for `PopulationManager` (60 sites; `EnsureExists` becomes `faction.Population`, lazily created). The `Worker.OnDestroy → BaseBuilding.NotifyWorkerRemoved → Population.RemoveColonist` chain keeps one owner.
4. Same for `Unlocks`, `ResearchCatalog` done flags, `CraftedUpgrades`, `LaborPriorities`, `GuardStance`, `Formation`. The Colonists tab and combat box write to `Factions.Player`.
5. `ITargetable.Faction`, the two `TargetingUtil` overloads, `bb.faction`, per-faction density grid. Rewrite the enemy tiers and the warrior scans. The 13 `FindAlive()` and 3 `ActiveList[0]` sites become `faction.Campfire` / `bb.faction.Campfire`.
6. `Spawn.Owned` everywhere something is instantiated with an owner (`BaseBuilding.SpawnColonist` / `SpawnWarrior`, `GhostPlacer`, `WallLinePlacer`, `ConstructionSite.Complete`, `EnemySpawner`, `GameStartController`, `SimBuilder`, `DebugMenu`).
7. F4: **Spawn rival camp** — a second campfire at a random buildable spot 40 u + from the player's, 3 workers assigned wood/food/stone from its own pool, 100W 50F, one warrior. Relation toggle Hostile / Neutral / Allied in the same menu.

### Definition of done

- The stub rival gathers into its own pool (visible on an F3 line per faction), its workers never deliver to the player's fire, its warrior fights raiders, and flips to fighting the player's warriors when the relation is set Hostile.
- Raiders attack whichever colony is nearer.
- `ResourceManager.Instance`, `PopulationManager.Instance`, `BaseBuilding.FindAlive`, `BaseBuilding.ActiveList[0]` no longer exist; CLAUDE.md bans them.
- Sim regression: three policies at n=6 match the baseline on AI health.
- The player's own experience is unchanged: same HUD, same panels, same raids.

**Size:** the largest step of the five. Roughly 200 call sites plus the scan rewrites. Budget it as several sessions and land it in the seven commits above, never as one.

### Landed 2026-09-09 (seven commits, one session)

Done as written, with these deviations: the registry is **scene-keyed** (`Factions.EnsureForScene` compares the active `SceneHandle` on every access) rather than `sceneLoaded`-keyed, because the first reader of `Factions.Player` is `ResourceManager.Awake`; **no shims were needed** — each rename was mechanical enough to delete the accessor in the same commit; `LaborPriorities` became an instance class and `GuardStance` / `Formation` keep their logic with the state on the faction; `CraftedUpgrades` is deleted (the three multipliers live on `Knowledge`); the F4 rival gets a hut so the camp sleeps four; rivals are omniscient AND dark (every non-player thing carries `FogVisibility(Visible)` and no `VisionSource`). The commit-5 regression sweep found that the pre-lap baseline was contaminated — unlock and research statics leaked across the sim's scene reloads, so only its first-run rows are a reference (`SimSweeps/baseline-2026-09-09/README.md`); commit 4 fixed the leak as a side effect. Left for step 3: `RaidDirector` prosperity counts every colony's buildings, rival arrivals land at the player's cove, and the harness stall itself.

---

## Step 2 — Spatial hash + AI level of detail

**Goal:** scans cost the neighbourhood, and a colony the player is not looking at costs almost nothing. This is where the ORCA ceiling and the "four colonies at a hundred units" future are bought.

### Spatial hash

- **`SpatialGrid<T>`** — static per type like `ActiveRegistry<T>`, cell 10 m, a flat bucket array sized from the map extent in `TerrainGrid.Awake` (`Configure(origin, sizeMeters)`), because the active island is always at the world origin (step 5 regenerates in place, it never offsets). Buckets are pooled `List<T>`s; no allocation after configure.
- **Membership** rides the existing `Register` / `Unregister`. Static things (buildings, nodes, sites) insert once. Units call `SpatialGrid<T>.Moved(this)` from their own `Update` when the cell index changes (an integer compare per frame, a list move only on a crossing).
- **Queries** are a ring walk outward from the origin cell, stopping when the ring's inner distance exceeds the best found: `FindNearest`, `FindNearestOwned`, `FindNearestHostile` keep their signatures and swap the `IReadOnlyList<T>` argument for the grid. `ForEachInRadius(pos, r, ref TVisitor)` with a struct visitor is the one general query, for crowd counts and the density replacement.
- **What gets rewritten:** the nine scanning considerations from the inventory, the Engage / Intercept / DefendWall / Patrol scans, the enemy target tiers, `RepairAvailable`'s six registries (one grid walk over an `IRepairable` view instead), `AIWorldState`'s density grids (become per-faction cell counts inside the grid itself, so the 10-frame rebuild goes away), `UnitHoleMask` and `FogOfWar` source gathering (a radius query around the camera / each source). Minimap and perf keep the global lists.
- **Range semantics stay edge-based.** The grid returns candidates; `EdgeDistance` and the carve-safe `GetApproachPoint` are unchanged.

### AI level of detail

Three tiers, decided per faction per island, never per unit:

| Tier | Where | What runs |
|---|---|---|
| **Full** | units within the camera's view plus a margin, and any unit in combat | today's everything |
| **Reduced** | the rest of the active island | `AIBrain` eval interval ×4 (1–1.4 s), NavMesh `obstacleAvoidanceType` Low, no VFX, no floating text, no health bar, `HoverGlow` / `OccluderCutout` skipped, animations off |
| **Abstract** | every other island | no GameObjects at all; the colony is a `ColonyLedger` ticked at 1 Hz by its governor |

- **Full ↔ Reduced** is a per-unit flag flipped by a 2 Hz pass over `SpatialGrid<Worker>` / `Warrior` / `Enemy` from the camera frustum, hysteresis 10 m. A unit in the Reduced tier still works, gathers and delivers; it just decides less often and draws less. Combat forces Full for both parties so fights never run at 1 Hz.
- **Abstract** is the `ColonyLedger`: per faction, per island — resources, job counts, building counts by type with HP, warrior count and weapon mix, research done, unlocks, colonist count, hunger, day the ledger was last materialised. `Tick(dt)` accrues resources at `jobCount × gatherRate × LaborMultiplier`, advances construction, pays food, and lets the governor (step 3) spend. **Calibrate the rates against the sim harness:** the sweeps' `days.csv` already gives per-day resource curves per policy, so the abstract model is fitted to the real one, not guessed.
- **`ColonyLedger` is the serialisable record.** `Dematerialise(faction)` builds it from live registries; `Materialise(ledger)` spawns the GameObjects. Step 4 writes it to disk; step 5 travels with it. Build the types now, materialise only as far as the F4 test needs.

### Definition of done

- F4 **Stress** spawns 300 workers across three factions on the active island; frame time at the RTS angle stays under the pre-lap 100-unit cost, and the F6 perf log shows scan cost flat with population.
- Toggling **Abstract** on the stub rival (F4) removes its objects, its ledger keeps accruing at the fitted rate, and re-materialising it brings back the same building set at the same HP with the ledger's resources.
- Sim regression holds.

---

## Step 3 — Colony governor, diplomacy and trade

**Goal:** a rival colony plays the game on its own, has an attitude toward the player, and can trade. This is the first player-visible step. It also collapses `SimPolicy` and `SimBuilder` into the governor, ending the "mirror `GhostPlacer` by hand" obligation.

### Castaway arrival, territory and measurement (decided 2026-09-11)

**The working detail for this step lives in [`docs/RIVAL_COLONIES_PLAN.md`](RIVAL_COLONIES_PLAN.md)**, which splits it
into Slice A (arrival + territory, on top of the existing debug rival) and Slice B (the governor refactor). Build A
first: it makes the landing and the territory watchable before the refactor churns the policies.

These four answers replace the step's original "debug-spawned rival beside the player" assumption. A rival is a castaway story, not a spawner.

- **Rivals wash ashore on a schedule, not at world start.** A `RivalLandingDirector` lands one rival colony on an announced day (banner + `DevQuests` signal), the way the player's own run opens. The player gets a head start, first contact is an event with a date, and the sim can put the arrival day in `days.csv`. The mid-run difficulty step this creates is the cost, and it is deliberate: the schedule is the tuning knob.
- **They settle where they land, and they land far away.** The landing cove is chosen at a minimum distance from the player's cove, scaled by `TerrainGrid.SizeScale`, so a Small island holds fewer rivals than a Large one. The colony is founded near its own beach, not near the player's - the existing `DebugMenu.SpawnRivalRoutine` places a rival 40-70 u from the PLAYER's fire and that is exactly what this replaces. Reuse `TerrainGrid`'s cove/campfire-site anchors so a rival's opening is as safe as the player's.
- **Territory is a preference, not a wall.** Each colony gathers inside its own home radius around its campfire and only reaches beyond it when its own nodes are exhausted. Crossing is therefore possible but uncommon, which is what makes it *mean* something when it happens: that is the trigger surface for the opinion drain below, and the reason a land war can start at all. A hard claim was rejected for removing the pressure entirely; a free-for-all was rejected as unbalanceable.
- **The harness measures rivals from the start.** `SimConfig` gains a rival count (0 / 1 / 2) and the rival personality; `days.csv` gains the arrival day, contact, opinion and any rival landing. The 2026-09-11 overnight batch is the argument: 450 runs turned "Turtle loses to walls" into four unrelated bugs, none of which was walls, and a feature this size should not be balanced by feel.

**Prosperity was fixed ahead of this step (2026-09-11).** `RaidDirector.Prosperity` read `Hut`/`Watchtower`/`Workshop`/`Shipyard`/`Wall` from the global registries while reading population and stockpile per faction, so every building a rival raised enlarged the PLAYER's nightly raid. It now counts through `TargetingUtil.CountOwned`. Any new prosperity term must be owner-filtered.

### Governor

- **`Governor`** — plain C# per AI faction, ticked at 1 Hz from a single `GovernorRunner` MonoBehaviour (one `Update`, staggered offsets). Reads faction stats (live counts from `FactionCounts` when materialised, the `ColonyLedger` when abstract) and writes **goals**, which are exactly the surfaces the unit AI already reads per faction: job assignments via `Campfire.AssignWorker`, `LaborPriorities`, `GuardStance`, `Formation`, the craft/research queue, and a build order.
- **Personalities are the sim policies.** `SimPolicy` becomes `GovernorPolicy` with the same subclasses (Turtle / Rush / Eco) and a fourth, **Trader**, that leans on the dock. The sim harness drives `Factions.Player` with a governor instead of a policy; the CSV columns and `run-sim.ps1` are untouched. Rivals pick a personality from the world seed.
- **`SimBuilder` becomes `FactionBuilder`**, and `GhostPlacer` / `WallLinePlacer` call it for the placement itself (validate → flatten → instantiate site → register grid). The human path keeps the ghost and the input; the rule set lives in one place. The CLAUDE.md "change both together" rule is deleted.
- **Build order** is a scored list, not a script: each candidate (hut when housing full, wall ring when raid pressure > threshold, tower at the most-attacked bearing, workshop when research queue non-empty, shipyard when Trader) scores from the ledger; the top affordable one is placed by `FactionBuilder` with the same validity rules as the player.
- **Army:** desired warrior count = `base + 0.4 × day`, weapon = best in stock, stance Defensive by default, Offensive when hostile to a neighbour and army ≥ 1.5 × their count.
- **Rival raids** are a governor decision, not a dawn roll: a hostile rival with a surplus army sends `k` warriors by sea. They arrive through `EnemySpawner`'s landing flow (renamed `Landing`), spawning that faction's `Warrior`s at a shore bearing, with the raid banner naming the faction. The Raiders faction keeps `RaidDirector`'s dawn roll unchanged.

### Diplomacy

- **Attitude** per pair: `Hostile` / `Neutral` / `Allied`, plus a hidden `opinion` scalar (−100..100) the governor moves: −20 when attacked, −5/day for a warrior inside its `HomeRadius`, +10 per completed trade, +2/day while Allied, drifting toward its personality's rest value. Thresholds flip the attitude with hysteresis (Hostile below −40, Allied above +40, both need 2 days of dwell) and every flip posts a banner and a `DevQuests.Signal`.
- **Player controls**: a **Diplomacy** screen (Esc → DIPLOMACY, also a HUD button once a second faction is known) listing each known faction, attitude, opinion as a word, and two actions: **Propose peace** (costs a gift from the stockpile, +opinion) and **Declare war** (instant Hostile, −opinion everywhere else). Known = ever seen in the player's fog.
- **Allied** means: no damage between them, warriors of both engage a common hostile, and the ally's colony is visible on the minimap.

### Trade

- Trade happens at the **Shipyard** (renamed in the field guide to also be the dock; no new building). A **Trade** tab on its panel lists standing offers from Neutral/Allied factions: give `a × X`, receive `b × Y`, fulfilled after a delay of `ceil(distance / shipSpeed)` days by a cargo arrival (a pickup crate at the shore with the goods, spawned by the existing salvage path). Offers refresh each dawn from the partner's surplus in its ledger; prices from a fixed table tilted by the partner's shortages. The player pays on accept, the partner ledger receives on the same tick.
- The Trader governor accepts offers the same way from its side, so two AI colonies trade with each other in the abstract tier at no cost.

### Definition of done

- On one island with **Spawn rival camp**, a governed rival grows from a campfire to huts, a wall ring and four warriors over ten days with no input; it repels a raid; it trades wood for food with the player through the dock and turns Hostile after being attacked, then sends a landing.
- The sim harness runs `strategy: "Turtle"` through the governor with results in the baseline's range.
- Changelog: "Rival colonies (F4 debug), diplomacy screen, dock trade".

---

## Step 4 — Save/load (dawn checkpoint)

**Goal:** a run persists across sessions. Autosave each dawn, manual save while no raider is alive, Continue and a slot screen on the main menu. Nothing in-flight is saved.

### What is saved (`SaveGame`, JSON via `JsonUtility`, versioned)

`JsonUtility` rules apply: public fields, arrays not lists of interfaces, no dictionaries, `-1` sentinels. Every record is a flat `[Serializable]` class.

- **Header** (also written to `slotN.head.json` so the slot screen never parses the whole file): version, timestamp, player name, island name, day, colonist count, `Application.version`.
- **World:** `Difficulty` snapshot, `IslandOptions` snapshot, world seed and the archipelago table (step 5; one entry until then), calendar (`currentDay`, `currentTimeOfDay` = the dawn value, `RaidDirector` state: `RaidsSoFar`, `LastRaidDay`, `RolledForDay`), cloud roll (optional, cosmetic; re-rolled if missing).
- **Terrain edits:** the ordered list of `FlattenArea(center, radius, blend)` calls, recorded by a new `TerrainGrid.PadLog`. On load they are applied to `heights` **before** `BuildAllChunks` in `Awake` (a `PendingPads` static consumed there), so the bake sees the pads. No heightfield dump.
- **Per faction:** the `ColonyLedger` from step 2 (resources, unlocks, research, upgrades, priorities, stance, formation, governor state, opinions/attitudes) plus the entity lists below when materialised.
- **Buildings:** type, faction, position, rotation, HP; construction sites with `progress`, `timeElapsed`, `buildingType`, `gridPos`; walls and gates by grid cell (the `WallGrid` is rebuilt from them); the campfire's stockpile slots; craft queues (`recipe` / `research` id, `remaining`, `progress`).
- **Units:** type, faction, position, HP; worker `hasJob` / `assignedResourceType` / `specialty` / `carryAmount` / `carryType` / `carryItem` / `isGarrisoned` (restored as "at home", never mid-flee) / `leaving`; warrior weapon id; player position, inventory slots, `knockedOut`, `reviveAt` as remaining seconds. Population: roster as unit indices + home building index, `arrivalTimer`, `foodDebt`, `starvedSeconds`, `nextDepartureAt`, `ColonistsLeft`.
- **Nodes:** the scatter and spawner are seeded, so nodes regenerate; the save stores `(index, currentAmount)` for every node that differs from full, and the depleted-and-replaced set (palm → broadleaf). `ResourceSpawner` and `PropScatter` number their nodes in creation order to make the index stable.
- **Pickups:** every `GroundPickup` in full (type, amount, item, position, `spawnerOwned`, `allowOverfill`), since the spawner's placement is time-random.
- **Fog:** the `explored[]` byte grid, bit-packed and base64. `seenStamp` is not saved (rebuilds within a second).
- **Never saved:** AI blackboards, targets, paths, cooldowns, attack and drop-off slots, node claims, projectiles, VFX, `DawnHeld` (a save is only legal with no raider alive), DevQuests (PlayerPrefs).

### Load pipeline

1. `MenuFlow.LoadGame(slot)`: read the file, set `Difficulty` / `IslandOptions` / `PlayerProfile` / `TerrainGrid.RunSeed` from it, park the `SaveGame` in a static `PendingLoad`, `LoadScene("MainIsland")`.
2. `TerrainGrid.Awake` consumes `PendingPads` between `Generate` and `BuildAllChunks`.
3. `SaveLoader` (runtime-added, subscribed to `sceneLoaded`, which runs after every `Awake` and before every `Start`): `GameStartController` takes the classic-start path with no popup and no campfire spawn; factions and ledgers are restored; buildings, then units, are instantiated through `Spawn.Owned`; nodes are patched; pickups spawned; fog grid uploaded; clock set.
4. One frame later (a coroutine; housing registers in `Hut.Start`): roster and homes are linked, queues restored, `PopulationManager` timers set, `GameManager` day counters set, `DayNightCycle.clockPaused` released. The intro hint canvas stays hidden.
5. Units start on Idle; the first eval tick reassigns work.

### Menu and triggers

- **Autosave** fires from `OnDayStart` after the despawn tick, into slot `auto`. **Save** (pause menu) is enabled only when `Enemy` and hostile `Warrior` landings are absent, not during the intro, not during the escape; disabled state reads "Raiders ashore" / "Not yet".
- **Continue** on the main menu loads the newest header. **Load game** is a slot screen (`MenuScreens.Screen.Saves`): 5 manual slots + auto, each a row from its header, with Load / Overwrite / Delete (delete confirms).
- Restart keeps its meaning (same seed, fresh run); a loaded game's Restart restarts the run, not the save.

### Verification

- **Editor menu `Tools > Founding Tide > Save round-trip`** and a sim flag `saveRoundTripDay`: at that dawn the harness saves, reloads the scene from the file, and asserts counts (units per faction, buildings per type, resources, research done, node totals, explored cell count) equal before and after, then continues the run to its normal end. The assert list is the definition of "everything is saved".
- DevQuests: "Continue appears after a saved run", "Autosave at dawn signals", "Loaded worker keeps its job", "Loaded wall pad is flat" (a state quest on the pad height), "Loaded fog matches".
- Changelog: "Save and load: autosave at dawn, manual save while no raider is ashore, Continue on the main menu".

---

## Step 5 — Islands (archipelago)

**Goal:** a world of seeded islands; one persistent colony per island; the player sails between them; rivals live on theirs and raid or trade across the water.

### World

- **`World`** — generated from the world seed at New Game: `N` islands (Small world 3, Medium 5, Large 8, an `IslandOptions` row), each `{ id, name, seed, size, style, chartPosition, ownerFactionId or none }`, plus sea distances (chart distance → days at sea, 1–3). The player's starting island is the one the intro lands on; each rival gets a home island; one or two are empty. `IslandGenerator` is pure, so a 24-vert thumbnail per island is generated for the chart at no cost.
- **`ColonyLedger` per (faction, island)**. The player's home colony keeps running while away under a **Steward** governor (Turtle personality, honours the player's `LaborPriorities` and stance; never declares war, never spends metal). A colony with no campfire is an empty island.

### Travel

- The Shipyard's **Set Sail** opens the **Chart** screen instead of ending the run: islands as silhouettes, the current one marked, days-at-sea on the routes, known factions' flags where the fog (of the chart, revealed by trade and by visiting) allows.
- **Manifest**: the player always sails; up to `shipCapacity` (6, `CraftedUpgrades` can raise it) colonists and warriors chosen on the Colonists tab, plus cargo drawn from the pool into crates (capacity in units). The chosen units leave the roster; the rest stay in the ledger.
- **Departure = dematerialise → autosave → reload the scene with the destination seed → materialise.** This is one path with step 4, deliberately. `SCALING_NOTES.md` recommended teardown-and-regenerate without a scene load; with ~30 `Instance` singletons and ~40 game-state statics in the inventory, a second teardown path that must cover every one of them is a worse bet than the reload that save/load already made reliable. The scaling note is amended with this reasoning. A **loading screen** (island name, day at sea, thumbnail) covers the reload.
- **Arrival**: the ship beaches at the destination's cove; the manifest units walk ashore the way the first colonist does today; the player places a campfire (B) if the island has none, or the existing one is already there if this is a return. Days at sea advance the calendar and every ledger ticks for those days (raids can happen to the home colony while away; the steward handles them in the abstract, and the banner on arrival reports "Your colony on Ashfall was raided on day 12: lost a hut").

### Rivals across water

- The abstract tick from step 2 runs every rival ledger every game second regardless of island. Governor raids against the player pick a landing on the player's **current** island when the target colony is materialised, otherwise resolve abstractly against the ledger (a small combat model: attacker strength vs. defender strength × wall factor, losses on both sides, buildings damaged, reported on the next visit or the next dawn banner).
- Trade delays use chart distance. An Allied rival's island shows its colony on the chart.

### Run structure

- **Open question, decide before building step 5:** the 30-day escape no longer fits a world. Proposed: the calendar stays, the ending becomes **reach the Far Shore** (a marked island `k` routes away; arriving there with the player alive is the victory) with the day count as a score; defeat stays "the player is knocked out with no campfire anywhere". `daysToSurvive` becomes the Peaceful/Relaxed "the sea calms" option instead.

### Definition of done

- New Game builds a world of five; the chart shows it; sailing to an empty island generates it and lets the player found a second colony; sailing back finds the first colony where it was, with the steward's progress; a rival's landing hits whichever island the player is on; a trade with a rival two routes away arrives two days later.
- Sim: a `world` config runs the harness across two islands with one crossing, asserting the round trip.
- Changelog: "The archipelago: sail between islands, keep every colony, rivals across the water".

---

## Order, sizes and what unblocks what

| Step | Depends on | Relative size | Player-visible |
|---|---|---|---|
| 1 Factions | — | XL (≈200 sites + scan rewrite) | No |
| 2 Spatial hash + LOD | 1 (per-faction scans and counts) | L | No |
| 3 Governor + diplomacy + trade | 1, 2 (ledger) | L | Yes: rival camp, Diplomacy screen, dock trade |
| 4 Save/load | 2 (ledger), 1 (Spawn.Owned) | M–L | Yes: Continue, slots |
| 5 Islands | 4 (the reload path), 3 (rivals, steward) | L | Yes: chart, sailing |

Factions first because every site added later is another site to migrate. The spatial hash immediately after because it rewrites the same scan functions factions just touched, and the ledger it introduces is what the governor, the save and the islands all consume. The governor before save/load because a save with no rival in it tests less. Islands last because they are the reload path plus the ledger plus the governor, all of which exist by then.

**After the lap:** back to `ALPHA_PLAN.md` B → G (playtest debt, tuning on fresh baselines, tutorial, feedback, zip), on the new foundation.

---

## Open decisions (not blocking step 1)

1. **Victory in a world** (step 5, above).
2. **Does the Raiders faction get a home island** (a pirate cove that could be attacked to end raids)? Parked; the dawn roll stays.
3. **Rival fog:** rivals cheat by decision. Revisit only if a tester notices rivals "knowing" things.
4. **Unify `Enemy` into `Warrior`?** Not in step 1. Once rivals send `Warrior`s (step 3), the raider body is the only `Enemy` left; folding it in is a cleanup for after step 5, not before.
5. **Reduced tier and ORCA quality:** whether Medium/Low avoidance on far units reintroduces the head-on dance the High setting fixed. Measure in step 2's stress test before deciding the default.
