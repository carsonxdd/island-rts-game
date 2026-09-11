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
| **Turtle** | 4 workers → wooden wall ring (r9, one gap per side) → 2 gates → tower | Is fortification a viable substitute for an army? Is wall HP vs enemy DPS sane? |
| **Rush** | 3 workers, 2 huts, everything else into warriors | Does warrior cost/DPS keep pace with raids that grow with the day and the colony's prosperity? |
| **Eco** | Huts to 6, workers to 10 (3:2:1 wood:food:stone), ~1 warrior per day (spends the reserve when a raid is announced), late tower + partial wall | The baseline the other two are read against |

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
takes they say nothing at all. Each visual process instead overwrites a one-line
`status.csv` in its own shard directory once a second (`SimStatus`), which is what
the rows read; the `survived` tally comes from the shards' `runs.csv`, the real
record. Headless sweeps write no heartbeat: nobody is watching, and six processes
touching a file every second is cost for nothing.

**Draw rate is a flat 8, about 3x realtime** (2026-09-10). It used to scale with
the window count, up to one frame in 24, which ran a lab at roughly 7x realtime —
too fast to read what a colony was doing, which is the only reason to watch one.
Override it with `-RenderInterval`: higher skims, lower studies a fight. Watch for
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
`renderFrameInterval`, default 8 from `run-sim.ps1`) renders one frame in every N. Never reach for
`Time.timeScale` to speed a visual run up - that is the exact mistake the
headless harness exists to avoid.

Expect visual runs to be several times slower than headless. Use them to
understand a result, not to gather one.

### The spectator camera

`SimSpectatorCamera` re-scores five shots twice a second and holds the winner for
at least four seconds, most urgent first:

| Shot | Trigger |
|---|---|
| Campfire | the fire lost HP in the last 5 s |
| Landing | raiders just came ashore (`EnemySpawner.OnRaidLanded`) |
| Battle | the densest cluster of raiders and warriors within 18 m |
| Raiders | enemies alive but not yet in contact |
| Colony | default: the campfire, leaned toward where the colonists are |

It does not take the camera over wholesale. `CameraController` keeps running its
zoom smoothing and its per-frame clip-plane fit (a fixed near clip starves the
ground of shadow texels); only the input half is suppressed, via
`CameraController.SuppressInput`. The caption under the window is
`SimVisualOverlay`, IMGUI on purpose so a dev readout never touches the game's
own uGUI.

---

## Sweep files

A sweep is a JSON list of runs. `SimSweeps/example.json` is a working starting
point; `Tools > … > Write Example Sweep` regenerates it.

```jsonc
{
  "outputDir": "SimLogs",
  "captureDeltaTime": 0.0166667,
  "renderFrameInterval": 6,  // visual mode only: draw 1 frame in 6
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

Unit knobs can't be applied by patching the prefab (a `public float` on a unit
script is dead data — the prefab wins — and unit `Start`s copy the value into the
AI blackboard immediately). So each unit calls `SimOverrides.Apply(this)` at the
top of its `Start`, guarded by `UNITY_EDITOR || DEVELOPMENT_BUILD`.

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
hunger_dawn, left_total, archers_dawn
```

`hunger_dawn` is 0 fed / 1 hungry / 2 starving at that dawn; `left_total` is
cumulative; `archers_dawn` is how many of `warriors_dawn` carry a bow. A run whose `hunger_dawn` is 2 for several days in a row is losing
to its own kitchen, not to the raiders. Sweeps from before food consumption
(2026-09-04) are not comparable: every colonist now eats one food a day.

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

Hooks added to existing scripts (all guarded, all one-liners): `Worker.Start`,
`Warrior.Start`, `Enemy.Start`, `TerrainGrid.Awake` (overrides);
`CombatEffects.Awake`, `Health.Start`, `HealthBar.Start`, `UnitBase.CreateStateText`
(cosmetic suppression).

`SimRunner` is the **second** deliberate exception to the project's
no-`DontDestroyOnLoad` rule (`DebugMenu` is the first): it has to outlive the
scene reload between runs. It holds only its own sweep bookkeeping.
