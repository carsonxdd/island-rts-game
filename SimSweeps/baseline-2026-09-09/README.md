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

## Read this first: the baseline is contaminated (found by the commit-5 regression sweep)

The commit-5 sweep (`SimSweeps/regress-2026-09-09-c5/`, same sweep file) came back as 18 defeats at **3 workers / 0 warriors, day 3–6, zero exceptions** — a different shape from the table above, and the difference is the baseline's bug, not the lap's. In this baseline only the FIRST run of each process (`*_s1`) has the 3w/0s shape; every later run (`s2`–`s6`) had Spearcraft (and whatever else the earlier runs researched) from second one, because `Unlocks.granted`, `ResearchDef.done` and `CraftedUpgrades` were statics reset by `[RuntimeInitializeOnLoadMethod]` — once per process, never per scene load — and the sim reloads the scene between runs. That is why `s2+` recruit two warriors without a research line in the log, and why Eco seed 6, the last run of its process, played a whole game. Lap step 1 commit 4 put that state on the faction, rebuilt per scene, so from commit 5 on every run starts clean.

**The regression reference is therefore the three `*_s1` rows plus the commit-5 sweep**, not the table above:

| Run | Outcome | Peak | Research lines |
|---|---|---|---|
| baseline `*_s1` (3 runs) | defeat day 4–6 | 3w / 0s | ~5 per run |
| commit 5, all 18 | defeat day 3–6 | 3w / 0s | 4–6 per run (Woodcutting, Foraging, Spearcraft, Construction; Quarrying/Crafting/Mining sometimes) |

The genuine harness stall stands and is now cleaner to read: research completes, all three colonists take jobs, wood climbs past 700, and no hut is ever placed and no spear ever made. Sticks are the suspect (research eats them faster than the shore respawns them, and a spear needs three), and `SimBuilder.PlaceBuilding` is the other (Construction is known and 20W is affordable every tick). Neither can be confirmed without a per-day stockpile / queue-status column in `days.csv` — the step-3 governor rewrite is where that gets built.
