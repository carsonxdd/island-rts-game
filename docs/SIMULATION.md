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

## Sweep files

A sweep is a JSON list of runs. `SimSweeps/example.json` is a working starting
point; `Tools > … > Write Example Sweep` regenerates it.

```jsonc
{
  "outputDir": "SimLogs",
  "captureDeltaTime": 0.0166667,
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
| `warriorCostFood`, `maxWarriors` | the campfire (a warrior also costs a Wooden Spear from the stockpile since 2026-09-03; the old `warriorCostWood` key is ignored) |
| `dayLengthSeconds`, `nightLengthSeconds` | `DayNightCycle` |
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
game_seconds, wall_seconds, frames, note
```

`outcome` is `victory` | `defeat` | `timeout` | `error`.

**`days.csv`** — one row per calendar day per game (dusk to dawn). Plot this.

```
… day, raid, raid_size, survived, wood/food/stone at dusk AND dawn,
workers/warriors/huts/walls/towers at dusk AND dawn,
enemies_spawned, enemies_killed_total,
campfire_hp_dusk, campfire_hp_min, campfire_hp_dawn
```

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
| `Assets/Scripts/Sim/SimHooks.cs` | `Simulating` flag the cosmetic systems check |
| `Assets/Editor/Sim/SimTools.cs` | Menu items + the headless player build |
| `tools/run-sim.ps1` | Launch the player, wait, summarise |

Hooks added to existing scripts (all guarded, all one-liners): `Worker.Start`,
`Warrior.Start`, `Enemy.Start`, `TerrainGrid.Awake` (overrides);
`CombatEffects.Awake`, `Health.Start`, `HealthBar.Start`, `UnitBase.CreateStateText`
(cosmetic suppression).

`SimRunner` is the **second** deliberate exception to the project's
no-`DontDestroyOnLoad` rule (`DebugMenu` is the first): it has to outlive the
scene reload between runs. It holds only its own sweep bookkeeping.
