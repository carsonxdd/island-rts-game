# Research, Crafting Stations & the 30-Day Calendar — Design Plan

Drafted 2026-09-02. Status: **Slice 1 (calendar + raids) implemented the same evening and
playtested 2026-09-03 ("a great start", tweaks to follow); Slices 2–6 not started.** Every decision is locked (section 8); section 9
records how the open points were settled. Supersedes the "knowledge-only" unlock model of
`CRAFTING_AND_PLAYER_CHARACTER_PLAN.md` §4.8 (that plan's Slices A–C are shipped and stay
the foundation).

---

## 1. The ask

1. **Research, then craft.** Researching a bow teaches the colony the recipe; the bow is
   then *crafted* from a craft menu, as many times as wanted. The campfire can craft
   anything, slowly. Dedicated buildings craft faster and carry upgrades for the units they
   equip.
2. **Days, not waves.** The run is a calendar: survive to day 30. Most nights are quiet.
   When a raid does come it is bigger, and the player is warned.

## 2. The loop this creates

```
Land  →  hand-collect, place the fire  →  RESEARCH Woodcutting (sticks + stone, 6 s)
  →  Wood job opens  →  colonists arrive, eat, gather  →  research Spearcraft
  →  CRAFT Wooden Spear ×3 at the fire (slow)  →  arm 3 warriors (spear + food each)
  →  "Raiders sighted — they land tonight" (day 4)  →  hold  →  quiet nights, build
  →  Workshop: crafts 2× faster, researches Iron Spear (uses metal at last)
  →  a Crafter colonist keeps the queue moving while you play the map
  →  prosperity grows, raids grow  →  day 30 rescue, or launch your own ship earlier
```

Three currencies of attention: **research** (once, opens things), **items** (repeatable,
consumed by units), **resources** (feed both). Research is the tech tree; crafting is the
supply line; the calendar is the clock they race.

## 3. What exists that this builds on

| Existing piece | Becomes |
|---|---|
| `Unlocks` (six flags, granted by first craft) | The **research ledger**: flags granted by completing a research entry, not a craft |
| `CraftingCatalog.Recipe { crafted, unlocks }` | Recipes lose `crafted` and `unlocks`, gain `requires` (research id) and `category`; repeatable |
| `CraftedUpgrades` (three workshop globals) | Its three recipes become **research entries at the Workshop**; the multipliers stay as point-of-effect statics |
| `Workshop` + `CraftingUI` (own uGUI) | A **station** with a queue; the panel is rebuilt on `MenuBuilder` and serves every station |
| `PlayerCharacter` crafting task (stand at fire, charge on completion) | Same task, now against a station queue; the player is one *worker* on the queue |
| `BaseBuilding.Stockpile` (materials only) | Also holds **equipment**; recruitment draws from it |
| `BaseBuilding.SpawnWarrior` (10 W 15 F + idle colonist) | Idle colonist + **one spear from the stockpile** + 15 F |
| `Worker.SetJob(ResourceType)` | `Worker.Job` enum gains **Crafter**; `BuildExecutor` is the template for `CraftExecutor` |
| `EnemySpawner` (spawns every `OnNightStart`) | Keeps the spawning mechanics; a new **`RaidDirector`** decides *whether* and *how many* |
| `GameManager.nightsToSurvive` / `Difficulty.nightsToSurvive` | `daysToSurvive` (30) |
| `PopulationManager` arrivals | Also **food consumption** and the hungry/starving states |
| `SimMetrics.nights.csv` | `days.csv` with a `raid` column; `campfire_hp_min` only means something on raid rows |

## 4. Research and crafting

### 4.1 Research (`ResearchCatalog`, static like the item catalog)

```
ResearchDef { id, title, description, tier, station (Campfire | Workshop),
              itemCosts[], wood/food/stone/metal, seconds, prerequisites[],
              grants: Unlocks.Kind[], done }
```

- Research is **one-time, per run** (`done`), reset on play like every other static.
- It runs on a station queue exactly like a craft (section 4.3) so there is one progress
  path, one UI row type, one labor rule. The only difference is the output: flags instead
  of an item.
- `Unlocks.Kind` grows: `WoodJob FoodJob StoneJob MetalJob Construction Militia` +
  `Crafting Archery IronWork Shipwright` (the last two for later slices).

**Starting tree (Normal; tune in playtest):**

| Research | Where | Cost | s | Needs | Grants / opens |
|---|---|---|---|---|---|
| Woodcutting | Fire | 2 stick 1 chunk | 6 | – | Wood job; recipe *Stone Axe* (player tool) |
| Foraging | Fire | 2 stick 1 chunk | 6 | – | Food job; *Fishing Spear* |
| Quarrying | Fire | 2 stick 2 chunk | 8 | – | Stone job; *Stone Pick* |
| Construction | Fire | 3 stick 1 chunk | 8 | Woodcutting | Build mode, builders; *Mallet* |
| Spearcraft | Fire | 3 stick 1 chunk 5 W | 10 | Woodcutting | Militia; recipe *Wooden Spear* |
| Crafting | Fire | 10 W 5 S | 10 | Construction | **Crafter job**; Workshop building |
| Mining | Fire | 2 stick 2 chunk 15 S | 12 | Quarrying | Metal job; *Metal Pick* |
| Bowyery | Workshop | 20 W 5 F | 14 | Spearcraft | Archers; recipe *Bow* |
| Sharpened Tools | Workshop | 25 W 15 S | 10 | Crafting | +30 % gather (existing multiplier) |
| Sturdy Scaffolds | Workshop | 30 W 10 S | 10 | Construction | +50 % build speed (existing) |
| Iron Work | Workshop | 20 W 25 S 10 M | 14 | Mining | recipe *Iron Spear* (replaces the Forged Blades global) |

Player tools (axe, pick, spears, mallet) stay **player-only, cosmetic** items the player
may craft after the matching research; they are no longer what unlocks a job.

### 4.2 Crafting (`CraftingCatalog`)

```
Recipe { id, title, category (Tool | Weapon | Upgrade | Construction),
         itemCosts[], resource costs, seconds, output ItemDef, outputCount, requires }
```

- `requires` is a research id; a recipe is listed greyed with "Research X" until then.
- **Repeatable.** Queue *n* of a recipe; each completion charges its own costs (the
  charge-on-completion rule from the previous plan stands — walking away or running out
  costs nothing).
- **Output goes to the campfire stockpile**, whichever station made it (one colony store;
  the stockpile grows to 16 slots). The player crafting by hand still receives tools in
  hand as now.
- **Equipment** is a new `ItemKind.Equipment` with an `EquipmentDef` (damage, range,
  attackInterval, ranged) hung off the `ItemDef`. Wooden Spear 15 dmg / 1.2 s (today's
  warrior), Iron Spear 20 dmg, Bow 9 dmg / 1.0 s at range 9.

### 4.3 Stations

`CraftStation` component on the campfire (`BaseBuilding`) and the Workshop (and any later
building): a `speed[category]` table. Campfire: **1× for every category** ("anything, but
slowly"). Workshop: 2× Tool / Weapon, 1× Upgrade, and it is the only station listing
Workshop-tier research. A station whose speed for a category is 0 does not list it.

Each station owns a **queue** (`List<QueueEntry { recipe or research, remaining }>`),
worked front to back, one entry active at a time. Progress per second = `speed[category] ×
labor`, where labor is 1 while *someone* is at the bench (player or Crafter colonist; the
two do not stack in v1, so a station is never faster than its multiplier).

### 4.4 Who crafts (locked: both)

- **Player:** the existing `PlayerCharacter` craft task, retargeted at a station. Right-click
  a station → walk → the panel; pressing Craft/Research queues; the player stands there as
  labor while the queue runs and is released by any other command.
- **Crafter job:** `Worker.Job.Crafter`, assigned from the campfire panel like the gathering
  jobs (needs the *Crafting* research). New action **Craft** (basePriority 1.0):
  `IsCrafter` (zero-cost, first) × `StationWorkAvailable` (nearest station with a
  non-empty queue and no crafter on it; caches `bb.targetStation`) × `ThreatNearby`.
  `CraftExecutor` = `BuildExecutor` with a station instead of a site: walk to the
  carve-safe approach point, stand (stationary avoidance), call `station.AddLabor(dt)`,
  leave when the queue empties, `ForceReeval` on empty. No yShift anywhere so the action
  scores exactly 0 with nothing queued.
- One crafter per station; a second crafter idles (or walks to another station with work).

### 4.5 Equipment in play

- **Recruit:** `CanRecruitWarrior` = Militia research ∧ idle colonist ∧ stockpile has a
  weapon ∧ 15 F. `SpawnWarrior(weapon)` removes the item and stores the `EquipmentDef` on
  the warrior (`Warrior.weapon`; `bb.damage`, `bb.attackRange` and the attack interval are
  read from it at Start — the dead-data rule: the prefab's `damage` field becomes the
  *fallback*). The panel's warrior row shows "Arm with: Wooden Spear (3) · Iron Spear (0)".
- **Dismiss** (`RemoveWarrior`) returns the weapon to the stockpile. Death does not.
- **Upgrade in place:** a warrior holding a Wooden Spear walks to the fire and swaps for an
  Iron Spear when one is in stock (a low-priority *Rearm* action, only with no enemies
  present). Keeps early warriors from being obsolete.
- **Archers** (later slice): `Warrior` with `weapon.ranged` — attack from `attackRange`
  with a projectile arc; Engage's approach stops at range; the enemy priority table already
  prefers warriors as targets. Watchtowers could garrison one archer later.
- **Colonist tools are NOT consumed** (open point 9.1 — recommended no). Ten workers across
  four jobs each needing an axe is bookkeeping without a decision; weapons are where supply
  matters because they are lost in fights.

### 4.6 UI

- One **Station panel** on `MenuBuilder` (replaces `CraftingUI`): tabs **Craft · Research ·
  Queue**. Craft/Research rows: title, effect, cost coloured by affordability, a count
  stepper for recipes, greyed "Research X first" rows. Queue tab: entries with progress,
  remove buttons, "No crafter — assign one or stand here" when idle.
- The campfire panel keeps its tabs; its Craft tab becomes this panel's Craft tab. The
  Colonists tab gains a **Crafter** row; the warriors row gains the weapon picker.
- HUD: stockpile weapon count beside the warrior chip.

## 5. The calendar

### 5.1 Victory

- `daysToSurvive` (Normal **30**; Peaceful/Relaxed 20; Hard and Brutal also 30 — length is
  not a difficulty lever, they raid more often via `Difficulty.raidFrequency` 1.25 / 1.5
  instead). Victory fires at the dawn of day N+1 ("the rescue ship arrives"). Defeat stays:
  campfire destroyed.
- **Escape** (locked, later slice): research *Shipwright* → a *Shipyard* building placed
  on a beach cell (dry ∧ within 6 m of the waterline) → a large build (≈200 W 120 S 30 M +
  rope items) by builders → **Set sail** button → early victory with its own end-screen
  line. Reaching day N without it is still a win; the ship is the *better* win (fewer days
  on the stats screen, a different title).

### 5.2 Clock

Day 120 s / night 60 s made 30 days **90 minutes** of real time. Locked: day **100 s** /
night **50 s** → 75 min at 30 days. `Difficulty.nightLength` stays.

### 5.3 Raids (`RaidDirector`, new; locked: escalating chance + one-day warning)

- **Rolled at dawn** for the coming night, so the warning covers a whole day:
  `chance = clamp01(baseChance + perQuietDay × daysSinceRaid)`, with a **grace** of no raid
  before day 3 and a **cap** of never more than `maxQuietDays` (5) without one. Normal:
  base 0.15, per quiet day +0.2 → typical gap 2–4 days, ~8–10 raids in 30 days.
- **Warning:** HUD banner "Raiders sighted — they land tonight", a horn/drum stinger, the
  day chip turns red; `RaidDirector.RaidTonight` is public for the sim and the AI.
- **Size** (prosperity-scaled, locked; as shipped in Slice 1):
  `count = round((2 + 0.4 × day + 0.08 × prosperity) × Difficulty.EnemyCountMultiplier)`
  where `prosperity = colonists×2 + warriors×1 (on top) + huts×3 + towers×6 + workshops×4 +
  (walls+gates)×0.3 + (wood+food+stone)/60 + metal/10`, read at roll time, minimum 2.
  Day-4 first raid on a bare colony ≈ 5 (the old night 1); a day-20 colony of 12 colonists,
  6 warriors, 5 huts, a tower and 30 walls ≈ 16; day 30 ≈ 22.
- **Landing:** `EnemySpawner.SpawnRaid(count)` at `OnNightStart` when `RaidTonight`, the
  existing offshore ring and one-body clustering. Dawn despawn stays. Quiet nights: nothing
  spawns; the combat-music trigger moves with it.
- **Composition** (later): from prosperity 60 up, a share of the raid is a bigger enemy
  variant (more HP, slower). Not in the first slice.

### 5.4 Food consumption (locked)

- `foodPerColonistPerDay` **1.0** (Normal), for every roster member including warriors;
  the player does not eat. Charged continuously through a fractional accumulator that
  deducts whole units (the `RepairCosts` pattern), so the chip reads a steady drain.
- **Hungry** (food 0 for > 0.25 day): gather and build labor × 0.6, no arrivals.
  **Starving** (food 0 for > 1 day): one colonist per day *leaves* — walks to the cove and
  despawns, jobless first, warriors last (gentler than death and no corpse bookkeeping).
- HUD food chip shows `−N/day` and turns amber under one day of reserve, red at 0.
- `Difficulty.foodConsumption`: Peaceful 0.5, Relaxed 0.75, Normal 1, Hard 1.25, Brutal 1.5.
- **Balance intent:** a food worker gathers ~5 food per 15 s trip ≈ 20+/day, so **one food
  worker feeds ~15 colonists** — hunger is a *setup* pressure (forget food and you lose
  people), not a treadmill. Crates (6 food) matter on day 1–2.

### 5.5 Night without a raid

Nothing new: workers already keep gathering at night at a mild penalty (`TimeOfDay` 0.3–0.7),
warriors patrol and heal, arrivals wait for day. The `ReturnUrgency` night boost stays (it
just brings carry home at dusk). **Dusk on a raid day** is where the tension goes: workers
should garrison as the raid lands, which `ThreatNearby` already does once enemies are close.

## 6. Simulation

- `days.csv` replaces `nights.csv`: one row per day with `raid` (0/1) and `raid_size`;
  the dawn/dusk columns stay. `campfire_hp_min` is only informative where `raid = 1`.
- `SimConfig.nightsToSurvive` → `daysToSurvive`; new knobs `raidBaseChance`,
  `raidPerQuietDay`, `foodPerDay`, `prosperityWeight` (‑1 sentinel as always).
- Policies gain: research the four starters in order, keep **one food worker per 8
  colonists**, queue spears at the fire before the first raid, assign a Crafter once the
  Workshop stands, and read `RaidDirector.RaidTonight` to recruit the day before a raid
  (a human would). `Unlocks.Has` stops returning true under the sim — the sim must go
  through research like a player, or it is not measuring the pivot.
- **Sweeps before and after this plan are not comparable** (calendar, raids, food).
- A 30-day run at ~25× realtime is ~3 min per run; 12 seeds × 3 strategies ≈ 2 hours
  single-process. `-Parallel 4` brings it to ~30 min.

## 7. Delivery slices (each leaves the game playable)

1. **Calendar + raids — DONE 2026-09-02, pending playtest.** `daysToSurvive`,
   `RaidDirector`, HUD calendar chip + warning banner, prosperity size, difficulty knobs
   (`raidFrequency`, `daysToSurvive`), `days.csv` + raid knobs in the sim, DebugMenu (raid
   toggle, day stepper), docs. Nothing about crafting changed.
2. **Research/craft split.** `ResearchCatalog`, repeatable recipes, `ItemKind.Equipment`,
   `CraftStation` + queue on the campfire, spear consumed on recruit, station panel on
   `MenuBuilder`, `CraftedUpgrades` recipes moved to research, player as labor.
   F4 "research all / give 5 spears". Sim policies research + queue spears.
3. **Crafter job + Workshop as a station.** `Worker.Job.Crafter`, `CraftExecutor`,
   Workshop speed table, Iron Work research + Iron Spear (metal's first use), Rearm action.
4. **Food.** Consumption, hungry/starving, chip, difficulty knob, sim knob and policies.
5. **Archers.** Bowyery research, Bow, ranged `Warrior`, projectile, Engage range hold.
6. **Escape.** Shipwright research, Shipyard building + beach placement rule, Set sail.

## 8. Decisions (locked 2026-09-02)

1. **Crafted weapons are equipment, consumed per unit.** A warrior takes a spear from the
   stockpile; an archer takes a bow.
2. **Both the player and Crafter colonists work stations.**
3. **Pressure between raids: food consumption + prosperity-scaled raids + an escape goal.**
   (All three; the "nothing extra" option was also ticked and is treated as superseded.)
4. **Raids are rolled by escalating chance with a one-day warning.**

## 9. Open points — all settled 2026-09-02 (second round of questions)

1. **Colonist job tools are NOT consumed.** Research alone opens jobs; tools stay
   player-only cosmetics. Weapons are the only per-unit supply.
2. **A warrior still costs 15 food** on top of the spear (assumed; not asked — keeps food in
   the military equation).
3. **30 days at 100 s / 50 s** (≈ 75 real minutes on Normal).
4. **Starving colonists leave** — one per day walks to the cove and despawns, jobless first,
   warriors last.
5. **The ship is an early win with a better ending;** the day-30 rescue is still a victory.
