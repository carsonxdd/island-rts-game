# Rival Colonies — arrival, territory, then the governor

**Status (2026-09-11): SLICE A IS BUILT, uncommitted, unplaytested and unswept. Slice B is untouched and is still the live plan below.**
Slice A landed A1—A7 as written, with three decisions taken at the start: the territory penalty of A4 applies to EVERY
colony including the player's (so post-2026-09-11 baselines are not comparable until a `rivalCount: 0` regression lab
says they are), the patch radius reuses `ForageAvailability.HomeRadius` 70, and `ForageAvailability` itself needed no
change because its own-campfire radius already was the rule. What is still owed on Slice A: rebuild the sim player,
the `rivalCount: 0` regression lab, the playtest batch in `DevQuests.txt`, then a `rivalCount: 1` lab to watch it.
Step 1 (factions) landed 2026-09-09. Step 2 (spatial hash + AI LOD) is NOT done and is not a prerequisite for Slice A.
`docs/ARCHITECTURE_LAP_PLAN.md` step 3 holds the same decisions in summary; this file is the working detail.
Read `.claude/CLAUDE.md` first — its Factions and Utility AI gotcha sections are load-bearing for everything below.

---

## What the player gets

A second castaway ship breaks up on a far shore partway through the run. Its survivors found their own colony by
their own beach, gather inside their own patch of the island, and grow without the player's help. They can be
talked to or fought. They are not a raiding party and they are not scenery: they play the same game.

---

## Decisions (locked 2026-09-11, do not re-litigate)

| Question | Decision |
|---|---|
| When do rivals arrive? | **A scheduled shipwreck**, not at world start. Announced. |
| Which day? | **A fraction of the calendar** — about one third of `GameManager.daysToSurvive`, so day 10 of 30 and day 7 of 20. Never a constant. |
| Where do they land? | **Their own cove**, at a minimum distance from the player's cove, scaled by `TerrainGrid.SizeScale`. |
| Where do they settle? | **By their own beach**, not near the player. This replaces `DebugMenu.SpawnRivalRoutine`'s "40–70 u from the PLAYER's fire". |
| Territory | **A gathering preference, not a claim.** A colony works nodes inside its own home radius and only reaches beyond when its own are exhausted. Crossing is possible but uncommon — that is what makes an incursion mean something and gives diplomacy something to be about. |
| Default | **Off.** A New Game world option beside island size and style picks the rival count. Existing balance and the overnight baselines stay comparable. |
| First contact | **Banner only.** "A ship broke up on the far shore." No minimap marker. Their colony stays under fog until the player's people see it. |
| Measurement | **The harness gets a rival knob from day one.** `SimConfig` count + personality; `days.csv` records arrival, contact and opinion. |

**Why measurement is not optional:** the 2026-09-11 overnight batch (450 runs) turned "Turtle loses because of walls"
into four unrelated bugs, none of which was walls. A feature this size must not be balanced by feel.

---

## Slice A — Arrival and territory

Builds on the **existing** debug rival. Does NOT touch `SimPolicy` / `SimBuilder`. The rival still does not think for
itself at the end of Slice A: it lands, founds, gathers in its own patch and defends. That is deliberate — it makes the
arrival and territory observable before the governor refactor churns everything.

### A1 — `RivalLandingDirector`

New `Scripts/Factions/RivalLandingDirector.cs`. Runtime-added in `Awake` by whatever already ensures world systems
(follow `RaidDirector`, which `EnemySpawner` adds to itself — **its public fields are then the LIVE values, so never
put it in a scene or on a prefab**).

- Subscribes to `DayNightCycle.OnDayStart`.
- `ArrivalDay` = `Mathf.Max(3, Mathf.RoundToInt(GameManager.daysToSurvive / 3f))`. Floor of 3 so a very short calendar
  still gives a head start.
- On that dawn, for each configured rival: pick a cove (A2), found the colony (A3), post the banner (A6).
- One-shot per faction; guard against re-entry the way `RaidDirector.RollForTonight` does.
- Nothing happens when the rival count is 0, which is the default.

### A2 — Choosing the landing cove

The island generator already anchors ONE cove (`TerrainGrid.CoveCenter`) with a fixed-height shelf and ramp so the
player's opening is safe. A rival needs the same guarantee without regenerating the island.

- Add `TerrainGrid.FindShoreSite(Vector3 awayFrom, float minDistance, out Vector3 cove, out Vector3 campfireSite)`.
- Walk the shoreline: sample bearings around the island at the water's edge using the existing `IsNearWater(pos, radius)`
  and `IsReachable(pos)`; require `distance(candidate, awayFrom) >= minDistance`.
- `minDistance` = `55f * TerrainGrid.SizeScale`. **Every 150 m-map distance must scale with `SizeScale`** — a literal is
  wrong on two of three island sizes. A Small island will therefore fail to seat a second rival, which is correct.
- The campfire site is the first buildable, reachable, standing-room spot inland of the cove. Reuse the validity rules,
  not new ones: `IsBuildable` (dry ∧ gentle ∧ reachable) plus `ResourceNode.HasStandingRoom`-style side checks.
- Failure returns false and the director logs ONE warning and gives up for the run. A missing rival is not a crash.

### A3 — Founding the colony

Move the body of `DebugMenu.SpawnRivalRoutine` into `Scripts/Factions/RivalFounder.cs` as
`public static IEnumerator Found(Faction rival, Vector3 campfireSite, Vector3 coveCenter)`. `DebugMenu` then calls it
with a site from A2 rather than its own `FindRivalSite`, so F4 and a real landing exercise the same path.

Keep every `yield return null` that is already there and the reason for each:

- After the campfire object: **`Start` registers housing and sets `Faction.Campfire`.**
- After the hut: `Hut.Start` registers its housing.
- After the colonists: their `Start` runs.

**Faction is read in `Start` or later, never `Awake`** — `Instantiate` runs `Awake` before `Spawn.Owned` can set the
owner. Every owned object goes through `Spawn.Owned(prefab, pos, rot, faction)`; a bare `Instantiate` is banned.

Landing flavour: walk the survivors in from the cove rather than materialising them at the fire. `Worker`'s Idle action
already walks a fresh arrival in from the cove ("far from home → the home provider's approach point"), so spawning them
at the cove and letting Idle carry them is enough. No new executor.

### A4 — Territory as a gathering preference

Three considerations decide where a colonist works. All three already take the faction from the blackboard.

| File | Change |
|---|---|
| `AI/Considerations/ResourceAvailability.cs` | After the squared-distance cull and before the expensive checks, add a home-radius preference (below). |
| `AI/Considerations/ForageAvailability.cs` | Already has `HomeRadius` 70 / `NightRadius` 30 around the campfire. Make the radius come from the faction's campfire, not a constant read of the player's. |
| `AI/Considerations/PickupAvailability.cs` | Same preference, same place. |

The rule, stated once so all three match:

> A node outside my colony's home radius scores as if it were much further away. It is not hidden — if nothing inside
> the radius is available, the distant node still wins and the colonist walks.

Implement as a **score penalty, not a reject**: `score += OutsideHomePenalty` (start at 60 m, tune in the lab) when
`distance(node, myCampfire) > HomeRadius`. This keeps exhaustion handling free — when the home patch empties, every
candidate carries the penalty and the nearest distant node wins on its own.

Gotchas that will bite here, all already recorded in CLAUDE.md:

- **Order considerations cheapest-first and prune against the running best.** The penalty must be applied where the
  existing `if (distance >= bestScore) continue;` prune can still see it, or the prune becomes wrong.
- **A scan's "nothing found" must reach the brain as a real 0.** Do not add a floor that survives an empty scan; that is
  the 2026-09-08 "specialists stand at the fire labelled Gathering" bug.
- **The fog gate is player-only** (`bb.faction.IsPlayer`). Rivals stay omniscient by decision; do not add a fog read for
  them here.

### A5 — The New Game world option

`IslandOptions` already carries size, style and seed as a persisted `Selected` set plus a `Snapshot` frozen by
`BeginRun`. Add `rivalCount` (0–2) to both, a row on the New Game screen beside the existing ones, and a blurb.

- **Run-scoped choices are snapshotted by `MenuFlow.NewGame` and deliberately kept by `Restart`.** Follow that exactly.
- `IslandOptions.Active` must resolve to the sim's value under `SimHooks.Simulating`, the way size and style now do via
  `SimHooks.IslandSize` / `IslandStyle`. Add `SimHooks.RivalCount` and publish it in `SimRunner.Activate` **before the
  scene loads** — the director reads it on day one.
- Menu rows are code-built: see the Runtime uGUI gotchas before touching `MenuScreens`. A six-caption `TabRow` across
  `OptionsWidth` wraps at `ButtonSize`.

### A6 — What the player sees

- **Banner** through `ResourceUI`'s existing calendar/banner area (the raid banner sits below the resource bar). Text:
  "A ship broke up on the far shore." No position, no marker.
- **Changelog** entry — this is player-visible, so it gets one: `## yyyy-mm-dd — Castaways on the far shore`.
- **DevQuests** batch with signals at the point of effect: `@rival:landed`, `@rival:met` (first time a rival unit or
  building enters the player's fog), `@rival:territory` (a rival colonist works a node outside its own home radius).
- **Information** screen: a line about neighbours under the existing prose. `Resources/Information.txt` holds prose;
  `@name` lines ask for a live table. **Never type a catalog number into it.**

### A7 — Harness

- `SimConfig`: `rivalCount` (default 0, `-1` is the don't-override sentinel used elsewhere but 0 is meaningful here, so
  use 0 as "none" and document it) and `rivalStrategy` (string, same vocabulary as `strategy`).
- `SimMetrics.DayRow`: `rivalArrivalDay`, `rivalContact` (0/1), `rivalOpinion`, `rivalWarriors`. Append to the END of
  the `days.csv` header and the writer — column order is load-bearing for every existing reader.
- `summarize-sim.ps1` gains a rival column only once there is something to say; do not pre-build the report.

### Definition of done for Slice A

1. A Normal 30-day run with `rivalCount: 1` lands a rival on day 10 on a shore at least `55 × SizeScale` from the
   player's cove, and the banner fires.
2. The rival's colonists gather within their own patch. A player standing at their own fire sees no rival worker on
   their own nodes while the rival's own patch has anything left.
3. The rival's colony is invisible until the player's fog reaches it.
4. F4 → Spawn rival camp still works and goes through the same `RivalFounder`.
5. All four Roslyn configs green: `py tools/verify-scripts.py -w`.
6. A regression lab: `.\tools\run-sim.ps1 -Lab` with `rivalCount: 0` sits inside the post-2026-09-11 range. **Rebuild
   the sim player first** — a script-only build rewrites `Build/SimPlayer/islandrts-sim_Data/Managed/Assembly-CSharp.dll`,
   so check THAT file's date, not the exe's.
7. Commit, then a lab with `rivalCount: 1` to see it play.

---

## Slice B — The governor refactor

Only after Slice A is committed and watched. This is the large one, and it is a REFACTOR: the goal is that a rival and
the sim's simulated player are driven by the same code, so the "mirror `GhostPlacer` by hand" obligation dies.

### B1 — `SimPolicy` becomes `GovernorPolicy`

Same subclasses (Turtle / Rush / Eco), plus **Trader** later for the dock. They already have the right vocabulary:
`Research`, `KeepSpears`, `HireWorker`, `Recruit`, `KeepHousing`, `RunWorkshop`, `PlaceWallRing`, `GateOpenings`,
`PlaceShoreBuilding`, `ManageStance`.

**What has to change first, and the 2026-09-11 batch is the evidence:**

- `Did()` / `Goal` / `Intent` are STATIC. With two governors ticking they would trample each other. Make them instance
  state on the governor and have the visual overlay read the player's.
- Every `Factions.Player` read inside a policy becomes `this.faction`. There are many; grep for it and expect the
  compiler to find the rest once the static shims go.
- `SimBuilder` is a static class with static run state (`holeCells`, `nextHoleSweep`, `RingHalf`). Those become per
  faction, or the second colony's ring bookkeeping silently overwrites the first's. **Any static that holds colony
  state is a leak** — that rule already cost this project a contaminated baseline on 2026-09-09.

### B2 — `SimBuilder` becomes `FactionBuilder`

Move it out of `Scripts/Sim/` (it is `#if UNITY_EDITOR || DEVELOPMENT_BUILD` today and must stop being). `GhostPlacer`
and `WallLinePlacer` call it for the placement itself: validate → flatten → instantiate the SITE (never the finished
building) → Buildings layer → `SetBuildingType`. The human path keeps the ghost and the input.

When this lands, delete the "change both together" rule from CLAUDE.md — that is part of the definition of done.

### B3 — `GovernorRunner`

One MonoBehaviour, one `Update`, ticking each AI faction's governor at 1 Hz on **staggered** offsets. **Stagger every
per-unit and per-colony timer** or a whole population re-paths in one frame.

### B4 — Build order as a score, not a script

Each candidate (hut when housing is full, wall ring when raid pressure crosses a threshold, tower at the most-attacked
bearing, workshop when the research queue is non-empty, shipyard for a Trader) scores from the colony's own state, and
the top affordable one is placed. This replaces the fixed ladders inside each policy's `Tick`.

Carry forward what the 2026-09-11 batch proved, or the rivals will inherit the same bugs:

- **Size the militia against the incoming raid at ~1.0 per raider.** Turtle's 0.6 lost all 36 baseline runs while
  holding the materials for fourteen more spears.
- **A research list must be prerequisite-complete.** Rush was missing `quarrying`, and `Research` skips an id whose
  prerequisite is unmet, so its last three entries were unreachable for the whole run and it never saw a stone chunk.
- **Hold a builder out of every job from day one.** A colony with no jobless colonist can never finish a hut, never gain
  housing, and so never get a jobless colonist back.
- **Keep one forager and one quarryman whatever the ratio says.** A spear is 3 sticks and a chunk, and a chunk only ever
  falls off a worked rock.

### B5 — Diplomacy and trade

As already written in `docs/ARCHITECTURE_LAP_PLAN.md` step 3: an `opinion` scalar per pair with hysteresis on the
attitude flip, a Diplomacy screen, and trade through the Shipyard as the dock. **The territory penalty from A4 is the
opinion hook** — a rival colonist working inside the player's home radius is the daily drain that eventually flips a
Neutral neighbour Hostile.

### Definition of done for Slice B

1. A governed rival grows from campfire to huts, a wall ring and four warriors over ten days with no input, and repels
   a raid.
2. The sim harness runs `strategy: "Turtle"` **through the governor** with results inside the post-2026-09-11 range.
3. `SimBuilder` no longer exists; `GhostPlacer` and `WallLinePlacer` call `FactionBuilder`; the CLAUDE.md "change both
   together" rule is deleted.
4. Two AI colonies on one island each run their own governor without touching each other's state.

---

## Read these before starting

| File | Why |
|---|---|
| `.claude/CLAUDE.md` | The Factions section lists what is BANNED (`ResourceManager.Instance`, `PopulationManager.Instance`, static `GuardStance.Active`, bare `Instantiate` of an owned prefab, …). The Utility AI gotchas cover the consideration ordering rules A4 depends on. |
| `Scripts/Factions/` | `Faction`, `Factions`, `Relations`, `Population`, `Knowledge`, `Spawn`. Small files, read them all. |
| `Scripts/DebugMenu.cs` `SpawnRivalRoutine` | The colony-founding sequence A3 lifts, including every `yield return null` and why. |
| `Scripts/Terrain/TerrainGrid.cs` | `CoveCenter`, `CampfireSite`, `SizeScale`, `IsBuildable`, `IsReachable`, `IsNearWater`, `FlattenArea`. Everything happens in `Awake`. |
| `Scripts/AI/Considerations/ResourceAvailability.cs` | The scan A4 modifies, and the model for cheapest-first ordering. |
| `docs/SIMULATION.md` | How to run the lab and the overnight batch, and the two sim flags. |
| `SimLogs/overnight-2026-09-11/REPORT.md` | The batch that produced the B4 rules above. |

## Standing rules while this runs

- Every step green in all four Roslyn configs before commit: `py tools/verify-scripts.py -w`.
- Re-run `Tools > Island RTS > Setup Everything (In Order)` after touching art, prefabs or scene wiring.
- Player-visible changes get a `Assets/Resources/Changelog.txt` entry; every feature gets a `DevQuests.txt` batch with
  a signal at the point of effect.
- **Never speed a sim up with `Time.timeScale`.** The AI budget and NavMesh throttles are frame-based.
- Before committing, check `PC_RPAsset.asset`, `QualitySettings.asset`, `AudioManager.asset` and
  `VersionControlSettings.asset` for editor and build leaks. They were all dirty on 2026-09-11 and none belonged in the
  commit. `VersionControlSettings` must read `Visible Meta Files`.
