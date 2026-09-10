# Pre-lap baseline (2026-09-09)

Taken after lap step 1 commit 1 (`d270593`, faction types only, no behaviour change) and before commit 2, from a script-only rebuild of the headless sim player. `sweep.json` is the exact sweep: Turtle / Rush / Eco, seed 1 × 6 repeats, `terrainSeed` 20260909, 3 processes.

**This is a regression reference for AI health, not a balance reference.** Read `runs.csv` and `days.csv` for shape, and compare a post-step sweep for *the same shape*:

| Strategy | Outcomes | Typical run |
|---|---|---|
| Turtle | 6 defeat, day 3–5 | 1–8 workers, 2–4 warriors, 0 huts, 0 walls; campfire dies to the first raid (`campfire_hp_min` 5) |
| Rush | 6 defeat, day 3–6 | 1 worker / 2 warriors (seed 1: 3 workers / 0), nothing built |
| Eco | 5 defeat day 4–6, **1 timeout at day 29** (seed 6: 10 workers, 17 warriors, 8 raids, 146 kills) |

Zero exceptions in the three player logs. `hunger_dawn` 0 throughout.

**Known harness problem, found here:** seventeen runs never grow past the starting crew (no huts, no research beyond what the player driver does, food climbing with one food worker), while one run plays the whole game. The policies have not been re-fitted since the research/craft split, the player character start and the fog gates (`docs/SIMULATION.md`, "not comparable across" list). Until that is fixed the regression check after each lap step is: same outcome shape, same `campfire_hp_min` pattern, zero exceptions, no new `timeout`/`error` rows — and the step-3 governor rewrite is where the policies get repaired.
