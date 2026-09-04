# Colony Expansion Plan

Design doc for the systems asked for on 2026-09-03 that are bigger than a polish pass:
gathering territory, settlement tiers, processing chains, families and breeding, livestock
and farming. Nothing here is implemented. The polish items from the same session (hover
glow, node hand-harvest, byproducts, resource breakdowns, sky dial, tighter tree fade) are
already in and are described in `CLAUDE.md`.

This file is the source of truth for these decisions. When a slice ships, summarise it in
`CLAUDE.md`'s Phase History and add its playtest steps to `docs/CONTROLS_AND_CHECKLIST.md`.

---

## Locked decisions

1. **Gather range is a campfire radius first, flags later.** Colonists may only collect
   within a circle around the campfire; the circle grows with the settlement tier. Flags
   come afterwards and simply add more circles. Picked over flags-first because the radius
   is testable on its own and the flag system is then additive rather than a rewrite.
2. **Collecting is a job, not a side effect.** "Collector" joins Wood / Food / Stone /
   Metal on the campfire panel. It is the job that picks up loose things — sticks, chunks,
   crates, and whatever future processing chains leave lying about — rather than working a
   node.
3. **Item categories drive the HUD, not the other way round.** Every new item (plank, fish,
   berry, wheat, bread, brick) is one `ItemCatalog` entry with `hudListed` and a
   `hudCategory`. It then appears in the right chip's breakdown with no UI work at all.

---

## Slice 1 — Collector job and the gather radius

**Radius.** A single float on `BaseBuilding`, in metres, scaled by `TerrainGrid.SizeScale`
like every other authored distance (a literal is wrong on two of the three island sizes).
Start at 45 m on the 150 m map, which is roughly the near third of the island.

**Where it is enforced.** One predicate, consulted at the point of effect, never pushed:

- `ResourceAvailability` and `PickupAvailability` reject anything outside it, so a worker
  never sets out for something it is not allowed to take.
- `ConstructionAvailable` / `RepairAvailable` are deliberately NOT gated: the player placed
  that building, and a site nobody will walk to reads as a bug.
- The player's own character ignores the radius entirely. Walking out past the frontier to
  fetch something is the character's job, and later the way flags get planted.

**Collector job.** A fifth `Worker` job whose Gather action scores zero (there is no node
type for it) and whose Pickup action is unrestricted by carry type. `bb.carryType` is
already set from whatever was collected, and `ReturnToBaseExecutor` already delivers by
carried type, so a collector hauling a food crate and then a stick needs no new code — only
`PickupAvailability` losing its "matches my job" clause for this job.

**Showing the frontier.** Reuse `NoBuildZoneRenderer`'s draped-line technique: subdivide the
circle and drape each point at `GroundYAt + 0.08`. Draw it only while the campfire panel or
build mode is open, or it becomes furniture the player stops seeing.

**Risks.** A radius that excludes every node of one type strands that job — surface it on
the campfire panel ("no stone in range") rather than letting workers idle silently.

---

## Slice 2 — Settlement tiers

Campfire → Hearth → Town Hall → Keep. Each tier is an upgrade in place (same GameObject,
same `IHousing` registration, same registry entry — do NOT destroy and respawn it, the
campfire is referenced by `GameManager`, `AIWorldState`, every enemy target list and the
player's deposit task).

Each tier raises: gather radius, housing capacity, stockpile slots, and the research tier
available at its `CraftStation`. Costs come out of the processing chains below, which is
what stops tiering being a pure wait.

Defeat still means losing this building at any tier.

---

## Slice 3 — Processing chains

Raw material in, refined material out, at a station with a queue — the `CraftStation`
machinery already does all of this, so a processing building is mostly a catalog entry.

| Building | Takes | Gives |
|---|---|---|
| Sawmill | Wood | Plank |
| Masonry | Stone chunk | Block |
| Smelter | Metal ore | Ingot |
| Kitchen | Berries, fish, wheat | Bread, meals |

Planks and blocks become the cost of tiers 2+ and of the better buildings, so the colony's
mid-game is about keeping two conversion queues fed rather than about raising four numbers.

Small and large ground pickups already exist (`PickupSpawner.largeChance`), so "small stones
and big stones" is done; a masonry that turns a large chunk into two blocks is the payoff.

---

## Slice 4 — Families and breeding

`PopulationManager` already homes each colonist to one `IHousing` provider and can list them
(`HousingProviders`, `OccupantsOf`), and the HUD's Housing breakdown already shows occupancy
per building. Families are the next layer on that record, not a new system:

- A colonist gains a name, an age band, and an optional partner reference.
- Two adult colonists sharing a home for long enough pair up; a paired home with a free slot
  produces a child on a long timer.
- A child occupies a housing slot, does no work, and becomes a colonist at adulthood.

The housing breakdown becomes the family view: each home lists its residents by name with
the children indented. That is the "cool UI effect" — it costs one row type, because the
panel that shows it is already built.

**Constraint that must hold:** the roster stays the single owner of who exists. Births go
through `AddColonist`, deaths through the existing single removal path. No second list.

---

## Slice 5 — Livestock and farming

Farms and pastures are buildings that own a plot and produce on a timer while a colonist
works them, which makes them a resource node with a job rather than a new AI shape:

- **Farm plot** — a placed building; a farmer works it; yields wheat on a season timer.
- **Pasture** — holds animals; animals are simple wandering objects inside the plot, not
  units with brains; yields food and hides periodically.

Both should reuse `ConstructionSite`-style labor accumulation rather than inventing a second
progress model, and both must sit inside the gather radius from Slice 1.

Livestock deliberately has no combat, no pathfinding beyond a wander inside its own plot, and
no hunger model. The moment an animal needs a `NavMeshAgent` and an `AIBrain` the frame cost
of a herd stops being free.

---

## Slice 6 — Flags

Only after Slices 1 and 2 are playing well.

The player's character carries flags, plants one anywhere reachable, and each planted flag
adds its own gather circle. Flags cost materials, can be demolished, and are destructible by
raiders — a raid that burns your far quarry flag is a raid that costs you the quarry.

The radius predicate from Slice 1 becomes "inside the campfire circle OR inside any live
flag's circle", which is the entire mechanical change; everything else is placement, art and
a count on the HUD.
