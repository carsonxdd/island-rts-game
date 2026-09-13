# Balance Simulation Harness

Runs the real game, headless, with a scripted player — many times, with varied
numbers — and writes CSVs you can sort and plot. It exists to answer balance
questions ("is night 5 survivable as Rush?", "where does the economy curve cross
the enemy curve?") without playing a hundred 15-minute games by hand.

It does **not** replace playtesting. See [What it can't test](#what-it-cant-test).

---

## Quick start

1. **Unity → `Tools > Island RTS > Simulation > Build Headless Sim Player`**
   (first build is slow, later ones are incremental). Produces
   `islandrts/Build/SimPlayer/islandrts-sim.exe`.
2. **`.\tools\run-sim.ps1`** — runs `SimSweeps/example.json` and prints a
   win/loss table per strategy.
3. Read `islandrts/Build/SimPlayer/SimLogs/runs.csv` and `days.csv`.

To iterate on the harness itself without rebuilding, use
**`Tools > Island RTS > Simulation > Run Sweep In Editor…`** — it queues a sweep,
enters Play mode, and exits Play mode when the sweep finishes. Slower per run
(the editor is in the loop) but you get the console and the Inspector.

> The sim runs on `Assets/MainIsland.unity` and requires the scene's editor setup
> to have been run (terrain, opening sequence, pickups/workshop). A scene without
> a `TerrainGrid` still works — the harness falls back the same way the game does.

---

## How a run works

```
force past the opening  ->  poll the policy 1x/game-second  ->  watch for
(DebugForceColonyStart)     (assign workers, build, recruit)     victory/defeat/timeout
```

Everything else — pathing, gathering, combat, fleeing, enemy targeting — is the
game's own Utility AI, untouched. That is what makes the output worth reading.

### The three strategies

| Strategy | Shape | The question it asks |
|---|---|---|
| **Turtle** | 4→8 workers, wooden wall ring (r9, a two-cell opening per side → two gates side by side in each), army at 0.6× the next raid, Bowyery for the wall | Does fortification let a SMALLER army hold? Is wall HP vs enemy DPS sane? |
| **Rush** | 3→5 workers, huts as the army needs beds (to 8), army at 1.0× the next raid, Workshop tier for Iron Spears and Bows | Does warrior cost/DPS keep pace with raids that grow with the day and the colony's prosperity? |
| **Eco** | Huts as needed (to 8), workers to 10 (3:2:1 wood:food:stone), army at 0.7× the next raid (spends the reserve when a raid is announced), gated wall ring (r8), escape from day 12 | The baseline the other two are read against |

Those worker counts are floors (2026-09-10): every policy wants `max(floor, warriors / 2)` workers, capped at the campfire's job cap of 10, because the second lab lost Rush to food — five workers feeding 25 warriors. The ring's openings are the only place the sim makes gates: `SimBuilder.GateOpenings` walls each opening cell and converts it to a gate the tick it finishes (a gate is only ever converted from a finished wall, like the player's G). The first two labs left the openings as bare holes, and no wall took a hit in twelve runs.

Every strategy sizes its militia against `SimState.NextRaidSize` (2026-09-10): tonight's committed size when the dawn roll said raiders land, else `RaidDirector.EstimateRaidSize(day + 1)`, what a roll tomorrow would land against the colony as it stands. Spearcraft is third on every research list, beds go up ahead of the arrivals the army needs (`KeepHousing`), and nobody builds a Watchtower — it is a vision building until the archer-tower path exists. The 2026-09-10 lab (`SimLogs/lab-2026-09-10/`) lost all nine runs before this: Rush pinned at 8 warriors by two huts' beds with 2000 wood hoarded, Eco met an 8-raider first raid with two spears, Turtle's ring only delayed.

Since 2026-09-02 the run is a **30-day calendar** with raids rolled at dawn
(`RaidDirector`), not a wave every night. Policies read `SimState.RaidTonight`,
the same verdict the player's HUD shows.

Policies live in `SimPolicy.cs` and take at most one action per tick, so the
resource curve stays legible instead of the whole bank emptying in one frame.

Since 2026-09-03 nothing is unlocked for free under the sim: `Unlocks.Has` is
the real ledger, so a policy has to **research** like a player (`Research(s,
ids…)` queues the next entry of its list at the campfire bench, one at a time)
and **craft spears** before it can recruit (`KeepSpears(s, n)`; a warrior costs
a Wooden Spear + 15 food, no wood). A bench only moves while someone stands at
it, and the sticks and stone chunks research costs are hand-collected by the
player's character, so `SimPlayerDriver.Tick` — run before every policy tick —
fetches whatever the front entry is short of, deposits it, and parks the
character at the bench until the queue runs dry. **Sweeps from before this
change are not comparable**: they started with every job open and armed
warriors for wood.

### The speed trick (important)

The harness sets **`Time.captureDeltaTime = 1/60`**, not `Time.timeScale`.

This codebase's AI evaluation budget (`AIBrain`) and NavMesh command throttles
(`AINavHelper`) are **frame**-based. Speeding a run up with `timeScale` would
give every brain proportionally fewer decisions per game-second and every agent
fewer path requests — the AI would degrade, units would lose fights they'd
normally win, and the harness would report that as *balance*. `captureDeltaTime`
instead pins game time to a fixed step per frame, so the run still gets exactly
60 frames per game-second while the loop runs as fast as the CPU allows.

Expect roughly **3–10× realtime** per process; `runs.csv` records the actual
ratio per run in `game_seconds` / `wall_seconds`. Run several processes in
parallel (`-Parallel 4`) for more throughput.

---

## Watching a run (visual mode)

A sweep tells you *that* Turtle died on day 22. Watching tells you *why*. Visual
mode plays a run in a window instead of headless, with a spectator camera that
frames whatever currently matters.

```powershell
.\tools\run-sim.ps1 -Sweep SimSweeps\watch.json -Visual
.\tools\run-sim.ps1 -Sweep SimSweeps\watch.json -Visual -WindowSize 800x450
```

Under the hood it drops `-batchmode -nographics` and adds `-simvisual`, plus
`-screen-width` / `-screen-height` / `-popupwindow`. Unity can set a window's
size from the command line but **not its position**, so `run-sim.ps1` tiles the
windows afterwards through `user32!MoveWindow`. That part is cosmetic - if the
handle never appears the runs still play, just stacked.

Pressing Play in the editor with a queued sweep is visual too: `-simvisual` is
implied whenever the process was not launched with `-batchmode`.

### The lab: nine windows at once

```powershell
.\tools\run-sim.ps1 -Lab
.\tools\run-sim.ps1 -Lab -Seeds 7,8,9 -WindowSize 480x270
```

`-Lab` builds the sweep itself rather than reading one. Every seed in `-Seeds` is
a **row** and every strategy in `-Strategies` a **column**, emitted row-major so
the tiler puts one island's three players side by side:

```
TURTLE seed 1042 | RUSH seed 1042 | ECO seed 1042
TURTLE seed 8851 | RUSH seed 8851 | ECO seed 8851
TURTLE seed 4711 | RUSH seed 4711 | ECO seed 4711
```

`terrainSeed` tracks the seed, so a row really is one island and one set of raid
rolls played three ways. That is a far cleaner question than nine unrelated games:
*given exactly this map and exactly these raids, what does the strategy change?*

**The cell size is computed from the grid, not fixed** (2026-09-10): with no
`-WindowSize`, the desktop's working area is divided by the grid's columns and
rows, so nine cells cover a 1920x1080 screen edge to edge. Pass `-WindowSize` to
go back to fixed cells and leave room for the terminal, which is where the
dashboard draws:

```
SIMULATION LAB      elapsed 04:12      5 of 6 windows running

window              day   pop  food   war  fire  state
TURTLE/1042        8/30    10    63     0  100%
RUSH/1042          9/30    11    56     1   91%  raid tonight
ECO/1042          10/30    12    49     2   82%
TURTLE/8851       11/30    13    42     3   73%  raid tonight
RUSH/8851         12/30    14    35     4   64%  under attack (6)
ECO/8851          13/30    15    28     5   55%  raid tonight

survived: Eco 1/1
```

It redraws once a second in place. Neither CSV can feed it — `runs.csv` and
`days.csv` are both appended when a run *ends*, so during the twenty minutes a run
takes they say nothing at all. Each process instead overwrites a one-line
`status.csv` in its own shard directory once a REAL second (`SimStatus`, throttled
in `SimRunner.PushOverlay` because a headless process plays 15–30 game seconds a
second), which is what the rows read; the `survived` tally and the finished-runs
counter in the title line come from the shards' `runs.csv`, the real record.
Since 2026-09-11 the dashboard runs in every mode — a headless sweep (and each
sweep of the overnight batch) shows its eight processes the same way the lab
shows nine windows, titled `SIMULATION HEADLESS`.

**Draw rate follows the raid** (2026-09-10): one frame in 11 while the island is
quiet (about 4x realtime) and one in 5 while raiders are on it (about 2x), switched
by the player process once a second on `Enemy.ActiveList`. Before that it was a
flat 8 (~3x), and before that it scaled with the window count up to one in 24
(~7x) — too fast to read what a colony was doing, which is the only reason to watch
one. Override with `-RenderInterval` (day) and `-RaidRenderInterval` (raid):
higher skims, lower studies a fight. Watch for
drift if you take it low with nine windows on one GPU — they share it, and a
window that cannot keep up falls behind the others in *game* time, which ruins the
comparison the lab exists to make. It never touches the simulation step.

---

### It is the same run

This is what makes visual mode worth having rather than a toy. A rendered run
takes the **same decisions** as the headless sweep it is explaining, because
rendering is gated on a different flag from policy:

| Flag | Means | Guards |
|---|---|---|
| `SimHooks.Simulating` | the harness is driving | difficulty snapshot, name popup, end screen, dev quests, PlayerPrefs, mouse-driven UI, terrain seed |
| `SimHooks.Headless` | nothing is being drawn | VFX, health bars, state text, fog mask upload, occluder cutout, unit hole mask, decor scatter, HUD, minimap, clouds |

The rule: **if a guard changes what the game DOES it belongs on `Simulating`; if
it only changes what the game LOOKS LIKE it belongs on `Headless`. Nothing may
read `Headless` to decide anything.** `SimHooks.Visual` is the pair of them.

Two divergences had to be closed to make that true:

- **`PropScatter` skips decor at the `Instantiate`, never at the rule.** All the
  scatter rules share one `System.Random` and one spacing hash, so dropping the
  decor rules shortened the stream and freed ground - which moved every
  gatherable node placed after them. A headless island was not the island the
  same seed produced with a camera attached.
- **Cosmetics draw from `CosmeticRng`, not `UnityEngine.Random`.** Clouds, hover
  shimmer and the fog-visibility timer used the global stream that the harness
  seeds and that AI stagger and spawn jitter draw from. Systems that only exist
  when something is rendered must not move gameplay's numbers.

> Both fixes change headless node layouts. **Baselines taken before 2026-09-10
> are not comparable** to runs after it - re-run them.

### Speed

Visual mode keeps `captureDeltaTime`, so the simulation still steps 60 frames per
game-second and the frame-based AI budget is untouched. What it skips is the
*draw*: `OnDemandRendering.renderFrameInterval` (the sweep's
`renderFrameInterval` by day and `renderFrameIntervalRaid` while raiders are on
the island; `run-sim.ps1` sends 11 and 5) renders one frame in every N. Never reach for
`Time.timeScale` to speed a visual run up - that is the exact mistake the
headless harness exists to avoid.

Expect visual runs to be several times slower than headless. Use them to
understand a result, not to gather one.

### The spectator camera

`SimSpectatorCamera` re-scores six shots twice a second and holds the winner for
at least four seconds, most urgent first:

| Shot | Trigger | Zoom |
|---|---|---|
| Campfire | the fire lost HP in the last 5 s | 8 |
| Landing | raiders just came ashore (`EnemySpawner.OnRaidLanded`) | 12 |
| Battle | the densest cluster within 18 m that holds raiders AND the player's warriors | 9 |
| Raiders | the raider nearest the fire, while it is still moving | 12 |
| Castaway | by day (2026-09-10): the character on an errand, close in | 8 |
| Colony | default: the campfire, leaned toward where the colonists are | 13 |

**The camera used to strand itself until dawn** (2026-09-10): Raiders framed the
centroid of every living raider, and a night lasts until the last raider dies. One
raider wedged on a rock and one at the wall put the centroid on empty ground
between them until the dawn-hold cap despawned the straggler. Raiders now follows
the raider nearest the fire and drops out once that raider has not moved a metre
in 6 s, and Battle needs both sides in the cluster.

It does not take the camera over wholesale. `CameraController` keeps running its
zoom smoothing and its per-frame clip-plane fit (a fixed near clip starves the
ground of shadow texels); only the input half is suppressed, via
`CameraController.SuppressInput`. The caption under the window is
`SimVisualOverlay`, IMGUI on purpose so a dev readout never touches the game's
own uGUI. Since 2026-09-10 it also says what the run is working on: the policy's
**goal** this second (`army 4/6 · workers 5/8 · beds 1 free`), the **last** move it
made (`recruit warrior`, `place hut`, `research Mining`; bracketed once it is 30 s
old) and what the **castaway** is doing (`working: Spearcraft 40%`, `fetching
stick`, `building Hut`). `SimPolicy.Goal` / `Intent` are written by the policies'
shared moves and read by nothing that decides.

### Respawning a lost cell

`respawnWallMinutes` on a sweep (the lab sends `-RespawnMinutes`, default 10):
while the process has been running for fewer real minutes than that, a DEFEAT
queues the same config again straight behind itself — same strategy, seed and
island, id suffixed `_try2`, `_try3`… — so nine windows keep teaching instead of
going dark one at a time. Past the budget a defeat is final; a victory, escape,
timeout or error never respawns. Every try is its own `runs.csv` row, so a cell's
tries are read side by side. The lab also sends `maxWallSecondsPerRun` 3600.

**The frozen-clock guard is a real one now** (2026-09-10): a run whose GAME time
has not advanced for 60 real seconds is cut as `timeout` / "game clock frozen";
`maxWallSecondsPerRun` (default 3600) is only a last-ditch ceiling. The old flat
900 s cap could not tell frozen from slow and cut five winning day-25 colonies
out of the first 4x lab.

**Lurking raids** (2026-09-10): `RaidDirector.RaidLurking` goes true when raiders
are alive but for 30 s nothing has died, the fire has not been hit, nobody is in
attack range and the count has not changed. Every policy's `ManageStance` then
goes Offensive and stands down at dawn — the same hint the HUD gives a player.
Raiders also spawn only where a NavMesh path to the fire exists, and one that
makes no progress for 20 s is warped toward the fire (`Enemy.WatchProgress`), so
the "one straggler holds dawn for three minutes" nights should be gone.

---

## Sweep files

A sweep is a JSON list of runs. `SimSweeps/example.json` is a working starting
point; `Tools > … > Write Example Sweep` regenerates it.

```jsonc
{
  "outputDir": "SimLogs",
  "captureDeltaTime": 0.0166667,
  "renderFrameInterval": 6,  // visual mode only: draw 1 frame in 6 while quiet
  "renderFrameIntervalRaid": 3,  // ...and 1 in 3 while raiders are on the island (-1 = same as above)
  "repeats": 3,              // repeat the whole list, seed += 1 each time
  "runs": [
    { "id": "eco_raids_big", "strategy": "Eco", "seed": 1,
      "raidSizePerDay": 0.6,             // the knob under test
      "raidBaseChance": -1 }             // -1 = leave the code/scene value alone
  ]
}
```

**`-1` means "don't override"**, not zero — zero is a legal value for most of
these. Every field defaults to `-1`, so a run only has to name what it varies.

### Knobs

| Field | Applied to |
|---|---|
| `terrainSeed` | `TerrainGrid` (different island per run; `-1` keeps the inspector seed — a sweep never gets the random per-run island the menu's NEW GAME does) |
| `startingWood/Food/Stone` | `ResourceManager` |
| `workerGatherRate`, `workerCarryCapacity` | each `Worker` at spawn |
| `raidFirstDay`, `raidBaseChance`, `raidChancePerQuietDay`, `raidMaxQuietDays` | `RaidDirector` — when raids come (rolled at dawn) |
| `raidBaseSize`, `raidSizePerDay`, `raidSizePerProsperity` | `RaidDirector` — how big: `base + perDay × day + perProsperity × prosperity` |
| `enemyHealth/Damage/MoveSpeed` | each `Enemy` at spawn |
| `warriorHealth/Damage/MoveSpeed` | each `Warrior` at spawn |
| `warriorCostFood`, `maxWarriors` | the campfire (a warrior also costs a Wooden Spear from the stockpile since 2026-09-03; the old `warriorCostWood` key is ignored; `maxWarriors` 0 = no cap, the shipping value since 2026-09-07 — housing is the limit) |
| `dayLengthSeconds`, `nightLengthSeconds` | `DayNightCycle` |
| `foodPerDay` | `PopulationManager` — food each colonist eats per calendar day (2026-09-04); `0` switches eating off, `-1` keeps the shipping 1 |
| `daysToSurvive`, `maxGameSeconds` | `GameManager` / the run's hard stop (a 30-day run is 4500 s of game time at the shipping clock) |
| `difficulty` | the preset by name (`Peaceful` / `Relaxed` / `Normal` / `Hard` / `Brutal`, 2026-09-11) — `Difficulty.Active` reads it under the sim through `SimHooks.Difficulty`, so every multiplier the menu's preset carries applies (raid size and frequency, enemy stats, night length, starting resources, food) EXCEPT the calendar: `daysToSurvive` stays the run's own, so a Peaceful row wants `20` written into it. Empty = Normal |
| `rivalCount` | rival colonies scheduled to land, 0—2 (2026-09-11, lap step 3). **NOT a `-1` sentinel** — 0 is the meaningful default, and a sweep that leaves it there plays the game every baseline before rivals existed played. `run-sim.ps1 -Lab -Rivals 1` sets it for a whole lab. `rivalStrategy` is reserved for slice B's governor and is read by nothing yet |
| `islandSize`, `islandStyle` | the island by name (`Small` / `Medium` / `Large`, `Rolling` / `Terraced` / `Rugged`, 2026-09-11) — `IslandOptions.Active` reads them under the sim through `SimHooks.IslandSize` / `IslandStyle`. Empty = Medium · Terraced |

The three names are published by `SimRunner.Activate` next to `SimOverrides.Active`,
BEFORE the run's scene loads: `ResourceManager.Awake` reads the difficulty and
`TerrainGrid.Awake` the island, and run 0 gets them from `Bootstrap`.

Unit knobs can't be applied by patching the prefab (a `public float` on a unit
script is dead data — the prefab wins — and unit `Start`s copy the value into the
AI blackboard immediately). So each unit calls `SimOverrides.Apply(this)` at the
top of its `Start`, guarded by `UNITY_EDITOR || DEVELOPMENT_BUILD`.

---

## The overnight batch

```powershell
.\tools\run-overnight.ps1 -DryRun     # writes the sweep files, checks the build, prints the run count
.\tools\run-overnight.ps1             # close the editor first
```

One command before bed (2026-09-11). `run-overnight.ps1` keeps the machine
awake (`SetThreadExecutionState`; the dev box sleeps after an hour otherwise),
rebuilds the sim player in batchmode (`SimTools.BuildSimPlayerBatch`, so **the
editor must be closed** — it tests whether `Temp/UnityLockfile` is held, not
whether it exists), refuses to continue unless the build log says `Build
Finished, Result: Success` and `Assembly-CSharp.dll` is at least as new as every
`.cs` under `Assets` (Bee copies the DLL with its compile time, so "newer than
the build started" failed a good build), then plays four headless sweeps through `run-sim.ps1
-Parallel 8` and files each under `SimLogs/overnight-<date>/<sweep>/` (its
`runs.csv`, `days.csv`, `player-N.log`) next to the `<sweep>.sweep.json` it
played and a `<sweep>.manifest.csv` mapping every config id to its cell
(variant, strategy, island). When the last sweep ends, `summarize-sim.ps1`
writes **`REPORT.md`** beside them — that is the morning read. A failed build
stops everything; a failed sweep is logged and the next one runs. After the
report the script counts down 60 s and puts the machine to sleep (Ctrl+C or
`-NoSleep` keeps it on; a build failure never sleeps it).

| Sweep | Cells | Runs | Question |
|---|---|---|---|
| `baseline` | Turtle / Rush / Eco × 12 islands × 3 repeats | 108 | the fresh win-rate baseline (every pre-09-10 one is invalid) |
| `raids` | `raidSizePerDay` 0.25 / 0.55 / 0.70, `raidMinQuietNights` 1 / 3, `raidSizePerProsperity` 0.04 / 0.12, `raidBaseSize` 3 — one knob at a time × 3 strategies × 6 islands | 144 | where each strategy's win rate crosses 50%; the shipped cell is the baseline |
| `difficulty` | Peaceful (20 days) / Relaxed (20) / Normal / Hard / Brutal × 3 × 6 islands | 90 | ladder spacing |
| `islands` | 3 sizes × 3 styles × 3 × 4 islands | 108 | a `SizeScale`- or style-dependent stall |

Ids are `<variant>__<strategy>__<island>` (repeats append `_s<seed>`, which
`Cell-Of` strips), and `terrainSeed` = `seed` throughout, so an island number
is the same island in every sweep. Headless runs at 15–30× realtime here, so
the 450 runs are roughly five hours at eight processes. Any one sweep replays
alone with `run-sim.ps1 -Sweep SimLogs\overnight-<date>\raids.sweep.json`, and
the report can be regenerated any time with `summarize-sim.ps1 -Dir <folder>`.

**Reading `REPORT.md`:** the *At a glance* table first, then each sweep's
variant × strategy table — `n`, wins, win rate with a Wilson 95% interval
(n = 6 is ±35 pp, n = 36 is ±15 pp; a one-cell difference inside the interval
is not a finding), mean loss day, raid nights, mean `campfire_hp_min` on raid
nights, warriors lost, mean `weapons_dawn`, `ring_holes`, hungry dawns, and
error/timeout rows. **Read err/timeout first** — those are harness problems,
and a sweep short of its expected count means a process died (see
`overnight.log`). The baseline also gets an island × strategy grid with the
loss day in each cell: an island every strategy loses on the same early day is
a map problem, not a policy one.

---

## Output

**`runs.csv`** — one row per game. Sort and filter this.

```
config_id, strategy, seed, outcome, day_reached, days_to_survive, raids,
enemies_killed, peak_workers, peak_warriors, final_wood/food/stone,
colonists_left, game_seconds, wall_seconds, frames, note
```

`outcome` is `victory` | `escape` | `defeat` | `timeout` | `error` — `escape` is the
Shipyard ending (2026-09-04), a win before the rescue dawn. `colonists_left` counts
the people who walked out because the colony starved them (2026-09-04).

**`days.csv`** — one row per calendar day per game (dusk to dawn). Plot this.

```
… day, raid, raid_size, survived, wood/food/stone at dusk AND dawn,
workers/warriors/huts/walls/towers at dusk AND dawn,
enemies_spawned, enemies_killed_total,
campfire_hp_dusk, campfire_hp_min, campfire_hp_dawn,
hunger_dawn, left_total, archers_dawn,
idle_dawn, weapons_dawn, sticks_dawn, chunks_dawn, queue_dawn, warriors_lost, ring_holes
```

`hunger_dawn` is 0 fed / 1 hungry / 2 starving at that dawn; `left_total` is
cumulative; `archers_dawn` is how many of `warriors_dawn` carry a bow. A run whose `hunger_dawn` is 2 for several days in a row is losing
to its own kitchen, not to the raiders. Sweeps from before food consumption
(2026-09-04) are not comparable: every colonist now eats one food a day.

The last seven (2026-09-10) are what a wiped colony rebuilds with: `idle_dawn`
jobless colonists at the fire, `weapons_dawn` weapons in the stockpile,
`sticks_dawn` / `chunks_dawn` the spear kit, `queue_dawn` the campfire bench's
"Waiting for 2 Stick" (empty when nothing is held), `warriors_lost` warriors
that died between that dusk and dawn, and `ring_holes` ring cells the sim
builder could neither wall nor notch around (a hole is a gap raiders walk
through). A defeat row with idle colonists, wood and no weapons is a spear-kit
famine, not a raid too big.

`campfire_hp_min` is the single most useful column — **on rows where `raid` is
1**. Quiet nights are still written (that is the economy curve), but their
campfire HP says nothing. A raid survived at 100% is a raid that never
happened, and one survived at 8% is the knife-edge you're tuning toward.

Rows are flushed after every run, so a sweep that dies on run 80 of 100 still
leaves 79 usable rows.

---

## What it can't test

Roughly 40% of the playtest checklists in `.claude/CLAUDE.md` stay manual:

- **Anything visual.** Ghost tint red/green, bloom on the campfire, health bar
  heights, no-build lines draping the hills, art silhouettes, death fades.
- **Anything input-driven.** Wall-line dragging, `R` rotate, `G` gate conversion,
  demolish mode, camera feel, hover highlights, the crafting panel.
- **Feel.** Whether motion reads as snappy or stuttery. Use `PerfLogger` (F6) for
  the measurable half of that.
- **The opening sequence.** The harness force-skips it (`DebugForceColonyStart`),
  so the survivor landing and campfire placement are never exercised.

Two fidelity caveats on what it *does* test:

- `SimBuilder` reproduces the confirm paths of `GhostPlacer` / `WallLinePlacer`
  step for step (afford → spend → T2 flatten → construction site → Buildings
  layer), but not the full no-build-zone overlap rules, so it can occasionally
  place slightly closer to a neighbour than a player could.
- Cosmetic systems are switched off during a run via `SimHooks.Simulating`
  (VFX, damage numbers, floating state text, health bars, audio). These are
  single early-returns and touch no gameplay decision — but they do mean a sim
  run is not a perf measurement of a real one.

**The sim player has no audio at all** (2026-09-10). `SimTools` builds it with the
project's "Disable Unity Audio" flipped on and restores the setting afterwards, so
no process ever opens an output device. Silencing the `AudioListener` at runtime is
too late for that: nine lab windows each opening a device was enough to take a
machine's audio driver down.

---

## Files

| File | Role |
|---|---|
| `Assets/Scripts/Sim/SimRunner.cs` | Driver: bootstrap, run loop, scene reload between runs, quit |
| `Assets/Scripts/Sim/SimPolicy.cs` | Turtle / Rush / Eco — the simulated player's decisions |
| `Assets/Scripts/Sim/SimPlayerDriver.cs` | The simulated player's character: fetches research materials, stands at the bench |
| `Assets/Scripts/Sim/SimBuilder.cs` | Programmatic placement mirroring the real confirm paths |
| `Assets/Scripts/Sim/SimConfig.cs` | Sweep + run JSON schema |
| `Assets/Scripts/Sim/SimOverrides.cs` | Per-unit knobs, applied from unit `Start` |
| `Assets/Scripts/Sim/SimMetrics.cs` | The two CSVs |
| `Assets/Scripts/Sim/SimHooks.cs` | `Simulating` (policy) and `Headless` (capability) - see "It is the same run" |
| `Assets/Scripts/Sim/SimSpectatorCamera.cs` | Visual mode's camera director |
| `Assets/Scripts/Sim/SimVisualOverlay.cs` | Visual mode's IMGUI metrics caption |
| `Assets/Scripts/Sim/SimStatus.cs` | The once-a-second `status.csv` heartbeat the lab dashboard reads |
| `Assets/Scripts/CosmeticRng.cs` | The random stream cosmetics draw from instead of the global one |
| `Assets/Editor/Sim/SimTools.cs` | Menu items + the headless player build |
| `tools/run-sim.ps1` | Launch the player (`-Visual` to watch), wait, summarise |
| `tools/run-overnight.ps1` | The unattended batch: keep awake, rebuild, four sweeps, report (2026-09-11) |
| `tools/summarize-sim.ps1` | `REPORT.md` for a folder of sweep results — rerunnable on its own |

Hooks added to existing scripts (all guarded, all one-liners): `Worker.Start`,
`Warrior.Start`, `Enemy.Start`, `TerrainGrid.Awake` (overrides);
`CombatEffects.Awake`, `Health.Start`, `HealthBar.Start`, `UnitBase.CreateStateText`
(cosmetic suppression).

`SimRunner` is the **second** deliberate exception to the project's
no-`DontDestroyOnLoad` rule (`DebugMenu` is the first): it has to outlive the
scene reload between runs. It holds only its own sweep bookkeeping.
