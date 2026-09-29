# Kin and Voyages — the last features before the alpha

**SCOPE, decided 2026-09-27 (final):** the **alpha is Part 1 (kin) + Part 8 (poles, fishing, boar) + an Endless goal + save/load.** Parts 2–7 (the world, boats, sea mode, landing, Outposts, the Far Shore, Settlers / Clans / Drifters) are the **first major update after the alpha**, kept here as designed. The Far Shore is no longer the alpha's victory: the alpha keeps **Survive 30 days** as the default goal, with **Endless** beside it, and the Shipyard's Set Sail escape stays as it is until the voyages update. The reasoning is in `ALPHA_PLAN.md`'s status banner.

**Written 2026-09-27. Status: PLAN, nothing built.** Parts 1–6 are the first round (kin, the world, boats). Parts 7–8 were added the same day: Settlers / Clans / Drifters, packing up, work poles, fishing and boar. This is the live plan for the two features the user named as the alpha's finish line. Once they are built and polished, the alpha is called and uploaded (`ALPHA_PLAN.md` section K, then B → G). Decisions below were taken with the user on 2026-09-27; the ones still open are listed at the end, each tied to the slice that has to answer it.

**The user's words:** start with 3–4 survivors plus yourself and grow by *love*, not by castaways washing ashore. Build boats — a basic 5–10 person boat and a 15–20 person ship — and sail to other islands, steering the boat yourself in a zoomed-out view of the world, with a proper landing moment.

---

## Decisions taken 2026-09-27

1. **Travel scope: one live world scene.** Every island exists at once, in the scene, ticking. Zoom out far enough and you are looking at the real world, not a chart. There is no scene reload and no save/load dependency. *This replaces the lap plan's step 5 shape* ("reload per island, abstract ledgers for the rest"); see "What this changes in the lap plan" below.
2. **Growth: couples → child → adult.** Two adults sharing a Hut can become a couple. A child is born after a few days of plenty, stays a child for about 5 days (eats, does not work, hides on raids), then becomes an ordinary jobless adult. The housing-timer arrivals are removed.
3. **Victory: reach the Far Shore.** A marked island across open sea. Landing there with the castaway alive wins; the days it took is the score. The 30-day calendar stays as the raid clock. The Shipyard's current Set Sail escape is replaced.
4. **At sea: steer, quiet sea.** A zoomed-out sea view where you steer the boat in real time. You see island silhouettes, the chart's fog lifts as you sail, and you can scoop up flotsam. A crossing takes roughly half a day. There is no combat at sea. It ends in a landing cinematic, and the crew walks ashore.

---

## Part 1 — Kin (survivor scarcity and births)

### The shape

- **You land with 3 survivors** (4 on Peaceful and Relaxed, `Difficulty.StartingSurvivors`). They wade ashore with the castaway in the `Landing` phase, so they exist before the campfire. **Two of them are already a couple**, which gives the first birth a reason to happen early and teaches the rule without text.
- **Nobody else washes ashore.** `Population`'s arrival timer is removed. `SpawnArrival` survives as the "walk up the beach" helper for the landing crews in Part 2. The Information screen line "A survivor lands every 20 seconds…" goes with it.
- **The other way to grow is to find people.** *(Proposed, open question 1.)* Each empty island in the world holds 1–3 **stranded survivors** at a wreck. Landing there and walking the castaway up to them adds them to the boat's manifest or to the colony. Births alone grow a colony very slowly (numbers below), so this is what makes voyages worth taking before the Far Shore.

### Bonds

- **Who can bond:** any two adults, with no sexes modelled. They cannot be parent and child or siblings (`Colonist.parents` is kept for this), and neither can already have a partner.
- **Where:** a **Hut** (2 beds). Two adults whose `home` is the same Hut and who sleep there for `BondNights` 2 nights become a couple. A Tent holds one bed, so a colony that never upgrades a Tent never has children. That gives the Hut upgrade a job beyond hit points.
- **What the player sees:** a heart floater once, a toast ("Mara and Tove are together"), both names paired in the People list, and a tooltip that says what a couple needs.
- **Loss:** a partner's death or departure leaves the other widowed, free to bond again after `MourningDays` 3. Couples separated by a voyage stay a couple. The boarding panel warns when you split one.

### Births

- **A couple conceives at dawn** when all of these hold: the colony is not Hungry; food stock is at least `FoodPerHeadForBirth` 5 × roster; the colony has **one free bed somewhere** (the child's future bed, so housing stays the growth lever it is today); the couple has no child on the way; and `BirthCooldownDays` 4 have passed since their last child.
- **The child arrives** `GestationDays` 3 later, at the parents' Hut. A toast names the child and both parents. A child has a `Persona` rolled like anyone else's, off `UnityEngine.Random`, because it decides behaviour, so headless and visual runs must agree.
- **Children do not take a bed.** A Hut holds its couple plus up to 2 children. The bed the conception reserved is needed at adulthood.

### Children

- **A child is a `Worker` with `Colonist.stage = Child`**, not a new unit type. That keeps it in the roster, in housing, under Flee and Sleep, and inside every existing registry.
- **Brain:** Sleep, Flee (garrison), Leave and a **Tag-along idle** (stroll near a parent by day, stay at home at night). Every work action gets a zero-cost `IsAdult` gate first in line, next to `IsJobless`, so the whole jobless ladder early-outs for children.
- **Never:** assigned a job, levied, recruited, a specialist, a builder, counted in `Worker.IsIdle`, or chosen to leave while an adult could go.
- **Body:** the worker art at `Model` scale 0.6 (child scale on the Model child, never the root), agent speed ×0.8, and **half** a colonist's food.
- **Growing up** happens at the dawn `ChildhoodDays` 5 after birth. The child becomes a jobless adult: the Model grows back to scale 1 and a toast plays ("Pip has grown up"). No body swap is needed, because it is the same `Worker`.

### What it does to the numbers (read this before tuning)

- A good start (build a Tent, upgrade to a Hut by day 3, couple bonded, first conception day 3) gives a first child on day 6 and a first new adult on day 11. With one couple and no stranded survivors, the colony is about **5 adults and 2 children on day 20**. Today's raid curve sends ~16 raiders on day 20. **The raid curve must be rebuilt around a small colony** (section C of the alpha plan stops being optional). The proposal: raid size keys off the colony's adult count and prosperity, with the day as a gentle ramp, not the base.
- **Warriors now cost scarce people.** Every full-time warrior is a worker lost for good. The levy (spare weapons arm colonists on alarm, then stand down) becomes the main defence, which is what it was built for. Expect governors' `LevyShare` to rise.
- **Governors learn kin (slice K3):** `KeepHousing` wants Huts for couples and a free bed for conception, `KeepUpgrades` prioritises Tent → Hut, and `WantedColonists` stops assuming arrivals. Rivals follow the same rules as the player, and a rival founded with 4 survivors gets a couple too.
- **Sim:** `days.csv` appends `couples, children, births, grown_up`, and `runs.csv` appends `peak_roster, births_total`. **Every baseline is invalidated again.**

---

## Part 2 — The world (one live scene, many islands)

### The key idea: one heightfield, many islands

`TerrainGrid` stays **one** singleton over **one** heightfield. The world is a larger map with several islands stamped into it. `SampleHeight`, `IsBuildable`, `IsReachable`, `FlattenArea`, the water depth map, the fog grid and the NavMesh bake then keep working with no API change. Islands are simply land separated by deep water, which the bake already marks `NotWalkable`, so each island's NavMesh is naturally its own. `TerrainGrid.Instance` has 83 call sites, and none of them change.

- **`WorldGenerator`** (pure and seeded like `IslandGenerator`) places `N` islands on a world map and runs the existing `IslandGenerator` pipeline once per island at that island's size and style. Each result is stamped into the world heightfield at an offset. Each island keeps its own `IslandField` anchors: cove, campfire site, ramp, and validation flood fill.
- **Default world (Medium):** the start island (Medium today, ~150 m), 3 neighbours (Small, 110 m) spaced so that every gap is **at least 120 m of sea**, and the **Far Shore**, placed beyond the deep-sea band that only the big ship can cross. World sizes Small / Medium / Large = 2 / 3 / 5 neighbours. That is an `IslandOptions` row, and the New Game screen's size option becomes the world size.
- **`World.IslandAt(pos)`** answers from an island-id grid at the heightfield's resolution (a `byte` per cell). This is the one new primitive. `World.Islands` lists `{id, name, bounds, cove, campfireSite, isFarShore, strandedCount}`.
- **A world of one is today's game.** The sim's default and the `-Lab` harness run with `worldIslands: 1` until slice W2 is swept, so nothing regresses while the world is built.

### What has to become island-aware

The 120 m sea gap is chosen so that every existing radius stays inside its own island: `HomeRadius` 70, `NightRadius` 30, `AlarmRadius` 70, the 22 m pickup attract and `ShoreRadius` 6. That leaves the **unbounded** scans, the ones that pick "nearest anywhere":

| Scan | Change |
|---|---|
| `Enemy.WatchProgress` / `EnemyAttackExecutor.PickTarget`'s last tier ("nearest hostile campfire") | nearest hostile fire **on my island** |
| `EnemySpawner.SpawnRaid` ring | ring around the target colony's island, not the map centre |
| `Dropoff.Nearest`, `FindHomeWithRoom`, `TargetingUtil.FindNearestHostile*` | filter `World.IslandAt(x) == World.IslandAt(me)` (a byte compare, done after the distance cull) |
| `RivalLandingDirector` / `FindShoreSite` | a rival is founded on its **own island** at world generation or by shipwreck, never on the player's |
| `Expedition.Send` | warps across water today. In the alpha, a party only sails to an island its governor can see. The visible rival boat is a stretch goal (open question 5) |
| `Minimap` | shows the island the camera is over, and the whole world in sea view |

Slice W2 includes an audit pass for the rest. Grep every `FindNearest*` and every `ActiveList` loop that has no radius.

### Cost and performance

- **Heightfield:** a Medium world is ~700 × 700 m at 1 m = ~490k vertices in ~1900 chunks. That is fine for memory. Chunk meshes are low-poly and frustum-culled.
- **NavMesh:** one `NavMeshSurface` per island with a bounded volume (`CollectObjects.Volume`), so a `FlattenArea` re-bakes only its own island. Five full-world bakes at startup would be the load-time problem.
- **Depth map and fog mask** grow to world size (700² RHalf, 350² fog cells). Both are already map-sized textures with a fixed value past the edge.
- **Scatter decor** is the render risk in sea view: every island's static-batched decor on screen at once. Cull decor and node art by layer cull distance at sea zoom (the islands read as silhouettes from there anyway).
- **AI:** islands with nobody on them hold only nodes and pickups, and those are only scanned by distance-pruned considerations. Two or three live colonies of ~20 are within today's budget. If the F6 log disagrees, the lap's step 2 `SpatialGrid<T>` for nodes and pickups is the fix. It was already designed as "configure from the map extent", which works unchanged for a world.

---

## Part 3 — Boats

### Two hulls

| Hull | Crew | Hold | Speed | Reach | Unlock | Cost | Build |
|---|---|---|---|---|---|---|---|
| **Longboat** | up to 10 | 60 units | fast | coastal: any island short of the deep-sea band | Shipwright (exists) | 80W 20S | 30 s × labor |
| **Carrack** | up to 20 | 200 units | slower | anywhere, **including the Far Shore** | new research **Deep Hulls** (60W 40S 20M) | 200W 120S 30M (today's escape ship price) | 60 s × labor |

- Built **at the Shipyard** as a project on its slipway. It takes labour exactly like a `ConstructionSite` (jobless colonists and the castaway through `AddLabor`), and the cost is paid when it starts, like a building. One hull on the slip at a time. The Shipyard's own price drops (it no longer *is* the escape) — to be tuned.
- A finished hull becomes a **`Vessel`**: an owned, registered entity (`ActiveRegistry<Vessel>`, `IOwned`) moored off the slipway along `Shipyard.LaunchDirection`. Raiders cannot reach water, so a vessel is not `ITargetable` in the alpha.
- **More tiers later** go in as data (hull capacity, hold, speed, reach, cost), the way building tiers did on 2026-09-18.

### Boarding (the Voyage panel)

- Left-click a moored vessel to open the **Voyage panel**: the crew list with a capacity counter ("7 / 10"), the hold (sliders drawing wood / food / stone / metal and stockpile materials and weapons from the pool, up to the hold size), and **Embark**.
- **The castaway always sails.** The player steers the boat they are on, and there are no AI voyages for the player in the alpha. The panel warns when a couple is split, or when leaving would take the last adult off an island that has a fire.
- **Embark** sends the chosen people to the slipway on foot, reusing the gear-up walk. Each one who arrives **boards**: the body is despawned into a **manifest record** `{name, Persona, stage, age, partner, parents, weapon, carry}` and taken off its colony's roster through the normal removal path. When all have boarded, or after 30 s, sea mode begins with whoever made it.
- **The manifest record is the same shape the save file will need later** (`ColonyLedger` in the lap plan), so it is written once, here.

---

## Part 4 — Sea mode

- **The camera** eases out from the RTS zoom to the **sea view**, a high ortho zoom well past today's `maxOrthoSize`, allowed only in sea mode. It follows the vessel. The clip-plane fit and shadow quantisation already scale with zoom. Shadows are probably OFF beyond a zoom threshold at sea (a `GraphicsQuality` rule, since it is the only writer).
- **Steering:** `W`/`S` throttle and `A`/`D` rudder, **or** right-click a point on the water to set a heading, matching how the character is commanded. It is kinematic movement on the water plane, with no NavMesh. The hull stops at `SampleHeight > DeepWaterY + draft` (it cannot sail through land). New keys go in `KeyBindings` (`BoatThrottle`, `BoatRudder`), and `Space` centres on the boat.
- **The Longboat's reach** is a soft wall: past the deep-sea band a banner reads "The open sea is too rough for a longboat" and the rudder bends it back. Only the Carrack crosses.
- **Fog:** the vessel is a `VisionSource` with a wide radius, so the world's fog lifts along your wake. **Unexplored land draws as a flat, dark silhouette at sea zoom, never invisible**, so there is always somewhere to aim. This is a small change in `FogOfWar.hlsl` gated on zoom.
- **Flotsam:** a few `GroundPickup`s floating on open water (barrels and crates, bobbing, using the existing art). Sailing within 4 m scoops them into the hold. The hold's space is the limit, and a full hold leaves them floating.
- **Time does not stop.** A crossing to a neighbour takes ~60–90 s real time (about half a day). Colonies keep working, raids keep rolling, and the HUD shows a toast when an island you own is raided while you sail. The day/night cycle plays over the sea.
- **Nothing attacks you at sea** (decision 4). Storms and rival boats are post-alpha.

---

## Part 5 — Landing

- **Approach:** sail into shallow water off a beach, and a **"Land here"** prompt appears (or right-click the beach). A beach anywhere works, not just the cove, and the nearest valid beach point uses the same shore test as `FindShoreSite`.
- **The cinematic**, 4–6 s, skippable with Esc or a click: letterbox bars slide in, the camera eases from sea zoom down to RTS zoom on the landing point, the hull slides up the sand and tilts, and a title card shows the island's name and the day ("ASHFALL · Day 12"), in the same style as the opening. The crew then steps off one at a time, and each is **respawned from its manifest record** at the waterline and walks ashore (the `SpawnArrival` walk). The hold's cargo lands as a crate pile at the waterline: pickups the crew carries up, reusing `PickupSpawner.DropLoot`.
- **After landing** there are three cases:
  - **An empty island:** you are at the `PlacingCampfire` beat again. **B** places a campfire, free, as at the opening. The new colony is yours. The colony you left becomes an **Outpost** (below).
  - **An island with your Outpost on it:** a **Take command** button on the Outpost's campfire swaps the roles. That island becomes your colony again, and the one you sailed from becomes the Outpost.
  - **The Far Shore:** the castaway steps ashore and the run is won (Part 6).
- **Stranded survivors** *(proposed, open question 1)*: a wreck on an empty island has 1–3 survivors idle beside it. Walking the castaway within 5 m of them adds them to your colony there, or to the boat if you leave before founding.

### Outposts: the colonies you are not leading

The player's faction owns **one campfire at a time** (`Faction.Campfire` is singular in 114 places, and splitting Faction into Colony would be another lap-sized refactor). So the colony you leave behind **becomes its own faction**:

- **`Faction.Kind.Outpost`**: the player's colour with a lighter shade, **Allied with the player permanently** (immutable, like self), and **Hostile to Raiders**. It is run by a **Steward governor**: a `GovernorPolicy` that keeps the colony fed, housed, armed and repaired. A Steward never declares war, never sends expeditions, and never spends metal on research. This reuses the whole rival stack (`GovernorRunner` adopts it, `FactionBuilder` places for it), which is why this costs a slice and not a lap.
- **The transfer** is `Factions.Transfer(islandId, from, to)`: every owned unit, building, site, station and vessel on that island gets the new owner. Each unit's blackboard `bb.faction` is refreshed. Formation slots, militia claims, bench claims and drop-off slots are released. The pool splits (whatever was loaded into the hold travels, and the rest stays with the island). Knowledge is **copied** (research is faction-wide in spirit: an Outpost knows what you knew when you left).
- **It is live**: an Outpost is a real colony in the scene. It works, gets raided and defends itself while you are away, and you can watch it from the sea.
- **The Diplomacy screen** lists Outposts as "Your settlements", with no opinion and no word. You cannot command an Outpost directly. Take command is the only way.

---

## Part 6 — The Far Shore (victory)

- **Placed by the world generator** beyond the deep-sea band, at the far side of the world from the start island. It is visible as a silhouette on the horizon at sea zoom from day 1, so the goal is always on screen.
- **Win:** the castaway lands there with a Carrack. The end screen reads **ARRIVED**, with the day, the number of people aboard, and the colonies you left behind. **Score** = days taken (fewer is better) plus people brought.
- **Defeat** is unchanged in spirit: the castaway is knocked out with no campfire of yours anywhere, or your colony falls while you are ashore. An Outpost falling is a toast, not a defeat.
- **The 30-day calendar** stays as the raid clock with **no deadline** *(open question 3)*. The raids keep growing, which is the pressure.
- **Removed:** the Shipyard's Set Sail escape (`GameManager.isEscape`, `EscapeInProgress`, `AskSetSail`) and Eco's escape plan. The sim's Eco policy learns "reach the Far Shore" in slice V5. Until then a sim world is a world of one, and the sim's victory stays survival to `daysToSurvive`.

---

## Part 7 — Other peoples: Settlers, Clans, Drifters (added 2026-09-27, second round)

Decided with the user the same day. **"Rival" stops being a word the player sees.** Other colonies are **Settlers** (small parties) or a **Clan** (a big, late-game people), and the raiders are **Drifters**. In code, `Faction.Kind.Rival` and `Kind.Raiders` keep their names: this is a display rename (`Faction.Name`, the Diplomacy screen, banners, Information, the Changelog), not a type rename. Renaming the code is ~hundreds of sites of churn for no behaviour.

### Settlers and Clans arrive the way you do

- **By boat, from somewhere else.** A party's hull sails in, AI-piloted along the same kinematic movement as V2, from **off the world's edge**, or from **another island in the world** when a group is relocating (below). It beaches with the V3 landing sequence minus the cinematic: the crew wades ashore, and **the party carries its tents**. This replaces `RivalLandingDirector`'s shipwreck and `RivalFounder`'s instant fire + hut.
- **Founding is a sequence the player can watch:** the fire goes down at the landing site, then **the party's tents are placed one by one** by its own people (real `ConstructionSite`s built by labour, through `FactionBuilder`, which is lap slice B2 landing here), then jobs. A settler party brings **4 tents**, and a Clan brings 8.
- **Party size follows the island they land on:** Small 5–6, Medium 7–8, Large 9–10 (`SettlerParty.SizeFor(island)`). A **Clan** lands late (from day 15 on a 30-day clock) with **15–25** people, only on a Medium or Large island.
- **A settler party that grows past 15 becomes a Clan.** It gets a banner ("The Reed settlers are now the Reed Clan"), its name changes, and it is a stronger governor (bigger army share, landings allowed). Size is what the name means, so it is read from the roster and never stored.
- **Names:** each group draws a name from a list (Reed, Ashfall, Gull, Saltmarsh...), off the seeded stream like personalities. Settlers are "the Reed settlers", and a Clan is "the Reed Clan".
- **Births apply to them too** (Part 1). Clans get their size from arrival and growth, not from a timer.

### They pack up and leave

A colony can decide to leave. `GovernorPolicy.ConsiderLeaving`, checked at dawn, adds up pressures and leaves when the total crosses a threshold, with `DwellDays` 2 of hysteresis, the same way attitude flips work:

| Pressure | Read from |
|---|---|
| **Conquered**: they burned a hostile colony and hold its island (Clans, Conqueror-minded) | `Siege` / loot events |
| **Nothing left**: few nodes left inside their home radius, or Starving | node count in `HomeRadius`, `Population.Hunger` |
| **Pressured by you**: opinion at Hostile, your army stronger than theirs nearby, your landings against them, your territory overlapping theirs | `Diplomacy`, `ColonyState.Strength`, `Trespassing` |
| **Beaten**: lost half their people, or their fire below 30% | roster, campfire HP |

- **Leaving is visible:** the colony stops working, walks to its shore, packs its **tents** (they vanish into the boat's hold), and sails. Huts, walls and other built things are **left standing as ruins**: `Kind` none, unowned, which anyone can demolish for the refund. They start crumbling about 10 days after the colony leaves (decided 2026-09-27). A player can salvage an abandoned camp.
- **Where they go** (decision: *both*): to an **empty island in the world** if one is free, where they land and found again. Otherwise they sail **off the edge**. `rival_fate` gains `relocated` beside `deserted`.
- A banner tells the player ("The Gull settlers are packing up") only once contact has been made. Otherwise they are just gone.

### Drifters are the raiders

- **Night raids come by ship.** At dusk on a raid night, a **Drifter ship** sails in from the world's edge toward the target's shore (`RaidDirector.Target` already picks the shore). It beaches, and the raiders wade off at the waterline instead of appearing on today's spawn ring. `EnemySpawner` keeps the reachability rules: the landing point must have a NavMesh path to the target's fire (`CanReachFire` / `FindReachableToward`, starting from the beach).
- **At dawn,** or when the raid is repelled, survivors walk back and the ship sails off. **A beached Drifter ship is the raid's tell:** seen in the fog, it is the "raid landed" pulse made real.
- The raid clock, the size formula (rebuilt in K3) and "a night lasts until the last raider dies" are unchanged. **Loot (decided 2026-09-27):** raiders that sense they are losing grab a load from a stockpile in reach and run for the ship. Killing a carrier drops it.
- Code keeps `Enemy`, `EnemySpawner` and `Kind.Raiders`. Only the display name becomes Drifters.

---

## Part 8 — Work poles, fishing and boar (added 2026-09-27, second round)

### Work poles: telling a job where to work

- **A Marker Pole** is a crafted item (bench recipe: 2 stick, no research gate beyond Woodcutting). It is **planted from the build bar** (a new `Markers` category), which uses up one pole from the stockpile. Demolishing it returns the pole. A pole is a tiny owned building with a coloured flag, a radius ring shown while it is selected or in build mode, and a **job** picked on its card (Wood / Stone / Metal / Food).
- **What it does:** workers of that job gather **only inside that job's poles** (`WorkPoles.Allows(job, pos, faction)` — the one filter, applied in `ResourceAvailability` after the distance prune, like the fog gate). **With no pole for a job, it works as it does today** (nearest to home, territory preference). If every pole of a job has run dry, those workers fall back to today's behaviour, and a toast says "No wood left at your Wood poles".
- **Food is where the pole's ground decides the work:** a Food pole whose radius touches water (`TerrainGrid.IsNearWater`) is a **fishing** pole, and one inland is a **gather and hunt** pole (food nodes, food pickups, boar). The job stays Food: there is no Fisher job and no Hunter job.
- **Governors plant poles too** (a rival's Food pole at its beach, a Wood pole at its busiest grove). `FactionBuilder` places them the same way.
- Radius: `WorkPole.Radius` 15 m. It never reaches outside the island it stands on.

### Fishing

- **Fish shoals** are a new `ResourceNode` type in the shallows: a ripple patch that yields Food, depletes, and **reappears elsewhere** along the same coast after a while, so poles get moved. The worker stands at the waterline, at the approach point from `GetApproachPoint`, which already snaps to the NavMesh edge.
- **A Food worker fishes only while carrying a Fishing Rod.** The rod is **worker equipment**, new but the same pattern as a warrior's weapon: taken from the stockpile on the job's gear-up trip and returned when the job changes. A worker without one on a fishing pole forages instead.
- **Fishing research** (cheap, early; wood + food) unlocks the rod recipe (2 stick, 1 wood) and **hands the castaway a rod** (the research-hands-over-its-tool pattern). The castaway can fish a shoal by hand with it (`PlayerCharacter.ToolFor` gains the Fish line).
- Fish is **Food** in the pool: no new resource, and `ResourceType` is never reordered. In hand it shows as "Fish" through `ItemCatalog`.

### Wild boar

- **One animal.** Herds of 2–4 roam inland on every island, including empty ones (a reason to land). A new unit category: `Boar` with the enum value, the `Add…Impl` art partial, a menu item and a showcase row, **and a line in the plumber's table** (or it is invisible). It has its own small brain: Wander (grazing), Flee (a person within 8 m, running away from them), and **Gore** (only when hit and the attacker is within 2 m: 6 damage per 1.5 s, which hurts a lone hunter without being a base threat). HP 40. It never targets buildings and never enters a gate.
- **Hunting:** a Food worker **with a spear** (taken from the rack at gear-up, like the rod) on an inland Food pole hunts boar inside the pole's radius. It chases, strikes and kills, and the **carcass drops a Meat pickup** (8 Food, `allowOverfill`), carried home like any load. A hunter's spear is a weapon out of the rack, so **a raid's levy counts it**: an armed hunter is the first to muster. No extra rule is needed.
- **Hunting research** unlocks spear-hunting for the Food job. The castaway can kill a boar by hand with the spear from Spearcraft.
- Herds **respawn slowly** per island (`BoarSpawner`, `PickupSpawner`-style budget), so an island can be hunted out for a while.
- Raiders and warriors ignore boar. Boar are not `ITargetable` for combat scans: they are a resource that runs.

---

## Before K1: what the user plays first (2026-09-27)

K1 and K2 rewrite how the colony grows, and that changes the levy, housing and the raid numbers underneath everything built since 2026-09-13. Bugs found after K lands can't be told apart from K's own. So these get played first, as three normal runs, each ending in SUBMIT REPORT (Esc → Information → DEV). Press **RESET** on the DEV tab once before the first (owed since the 2026-09-13 prune).

1. **Run 1, the build loop (to about day 6):** the build palette, Tents and upgrading to a Hut, the building card, drawn walls (drag, click points, slants, G on a slant), the fogged build grid, and both halves of the campfire panel with tooltips. Poles (F1) will live in this exact UI.
2. **Run 2, raid nights (to about day 12, Normal):** the levy (alarm and the F10 bell), Defensive holding the gate line, "Raid repelled", Storehouse drop-offs, colonists sleeping in huts, not blocking gateways. Births will make the levy the main defence.
3. **Run 3, a neighbour (F4 → Rival camp, or New Game with a rival):** the camp grows, builds tents, the diplomacy word changes, a landing party, burning their camp for loot.

Before run 1: run `Setup Everything` once (the Tent's data exists on disk, but prefab and art changes since have not been re-set up), and make one release build (A4).

---

## Build order (slices)

Each slice ends green in all four Roslyn configs (`py tools/verify-scripts.py -w`), with Changelog and DevQuests batches written, and with a commit. The sim runs a world of one until W2, so each kin slice is swept on its own.

| # | Slice | Size | Depends on |
|---|---|---|---|
| **K1** | Scarcity: 3/4 starting survivors wade ashore with you, a starting couple, arrival timer removed, Information screen updated | S | — |
| **K2** | Bonds, births, children (stage, brain gates, child body, food, toasts, People list) | M | K1 |
| **K3** | Governors and sim learn kin; **raid curve rebuilt around colony size**; baseline sweep | M | K2 |
| **W1** | `WorldGenerator`: N islands stamped into one heightfield, per-island anchors, `World.IslandAt`, per-island NavMesh surfaces, the Far Shore placed; a world of one is byte-identical to today | L | — |
| **W2** | Island-aware scans (the table above), raid ring per island, rivals founded on their own islands, world-size fog, depth map and minimap, decor culling at zoom; F6 perf pass | L | W1 |
| **V1** | Hulls at the Shipyard, `Vessel`, the Voyage panel, boarding into manifest records | M | W1 |
| **V2** | Sea mode: camera, steering, coastal collision, reach wall, fog wake, silhouettes, flotsam | L | V1, W2 |
| **V3** | Landing: prompt, cinematic, title card, crew respawn, cargo pile, found a fire | M | V2 |
| **V4** | Outposts: `Kind.Outpost`, `Factions.Transfer`, Steward governor, Take command, raid toasts while away; stranded survivors if approved | L | V3 |
| **V5** | Carrack, Deep Hulls research, the Far Shore victory, end screen and score; escape removed; sim world mode (Eco sails) | M | V4 |
| **F1** | Work poles: Marker Pole item, the `Markers` build category, pole card with its job, `WorkPoles.Allows` in the scans, fallback + toast, governors plant them | M | — |
| **F2** | Fishing: shoal nodes that move, worker equipment (rod) on gear-up, Fishing research, the castaway fishes | M | F1 |
| **F3** | Wild boar: unit + art + plumber line, Wander/Flee/Gore brain, hunting with a spear, Meat pickup, herd respawn, Hunting research | M–L | F1 |
| **S1** | Settlers and Clans: display rename, party names, arrival by boat (AI-piloted hull), tents placed by their own labour (lap B2), party size by island, Clan promotion at 15 | L | V3, K2 |
| **S2** | Packing up: `ConsiderLeaving`, ruins, relocate to an empty island or sail off the edge, `relocated` fate | M | S1 |
| **S3** | Drifters: raids arrive by ship, land at the beach, sail off at dawn; display rename | M | V2 |
| **P** | Polish pass over all of it: timings, toasts, sounds, the landing's feel, tooltips, Information entries; one full playtest per batch | M | all |

**K, W and F can all run in parallel** (they touch different code), and K and F give the next playtests something to test while W is under way. S waits for the boats. After P, go back to `ALPHA_PLAN.md` B → G. **This is the platform the game builds on after the alpha** (the user's words, 2026-09-27): once P is done, the alpha is "somewhat finished" and gets uploaded.

---

## What this changes in the lap plan

- **Step 5 is replaced** by Parts 2–6 above: one live scene instead of reload per island, Outposts on the rival stack instead of abstract ledgers. The reason: the user wants to *see* the world at a bigger zoom and watch it live, and the single-heightfield trick makes the world cheap for the terrain, fog and NavMesh code, which were the expensive part of a multi-island scene.
- **Step 4 (save/load) moves after the alpha.** An alpha run is one sitting. The manifest record from V1 is the first piece of its data model.
- **Step 2 (spatial hash + LOD)** stays skipped unless W2's perf pass says otherwise.

---

## Open questions (each blocks only its slice)

1. **Stranded survivors on empty islands** (V3/V4): proposed yes, 1–3 per empty island, 0 on the Far Shore. Without them, births are the only growth, and a neighbour island offers only resources.
2. **Starting survivors per difficulty** (K1): proposed 4 on Peaceful/Relaxed, 3 on Normal/Hard/Brutal.
3. **A Far Shore deadline** (V5): proposed none. The growing raids are the clock. Alternatively, Normal could fail at day 30 ("the colony was never rescued").
4. **Can an Outpost be ordered at all while you are away** (V4)? Proposed: no, except its campfire panel's priorities and stance when you stand on its island.
5. **Rival boats** (post-alpha unless cheap in V4): today an expedition warps. A visible AI Longboat that sails the same straight line as the player's would sell the world.
6. **Hull numbers** (V1): Longboat 10 / Carrack 20, as the user sized them. Should a third, smaller hull (a raft for the castaway + 2) be the first boat? Proposed no, the Longboat is "the basic one".
7. ~~Drifter loot~~ **Decided 2026-09-27: yes.** Drifters grab loot, and when they sense they are losing (raid below half strength, or a stockpile in reach with defenders closing), carriers grab a load and run for the ship. Killing a carrier drops the load. Folded into S3.
8. ~~Ruins~~ **Decided 2026-09-27: they crumble, slowly.** A departed colony's buildings stay as unowned ruins that anyone can demolish for the refund. They lose HP from about 10 days after the colony leaves and are gone a few days later. Folded into S2.
9. **Pole limits** (F1): proposed no cap on poles, since each costs 2 sticks. Should a pole also be able to mark "never gather here" (protect a grove)? Proposed post-alpha.
