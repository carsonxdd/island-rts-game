# Building UI + Upgrades Plan

**Status (2026-09-18): ALL FIVE SLICES BUILT.** Green in all four Roslyn configs, uncommitted,
UNPLAYTESTED and UNSWEPT. **`Setup Everything` has not been run yet** — until it does there is no
Tent in the database and the palette is empty, because the Tent's art, prefabs, `TentData.asset`
and the tier stamp on every other `BuildingData` are all produced by it.

Two things landed differently from the plan below, both for the better, and both now the rule:

- **`PointerBlock.OverHud`** replaced reading `Minimap.PointerOver` by name at eleven gameplay
  click sites. Three surfaces would otherwise have meant a second condition at every one of them,
  and one miss is a building placed under the bar you just clicked.
- **`IBuildingIdentity` moved into slice 1**, not slice 2. It had to: `DemolishTool`,
  `RepairAvailable` and `NoBuildZoneRenderer` all hardcoded `BuildingType.Hut` for anything
  carrying the `Hut` component, so without it a Tent refunded and repaired at Hut prices the
  moment it existed.

The body below is the plan as written before the work; read the status banner, not it, for what
is true now.

Scope: replace the (nonexistent) build UI with a real palette, add a selected-building card,
introduce the Tent as tier-1 housing with the Hut as its tier-2 upgrade, and put a generic
building-upgrade system underneath both so later tiers are data, not code.

---

## 0. What exists today

- `BuildingSelectionUI.cs` — a name + cost + hint text panel. **It is not in `MainIsland.unity`**
  (`grep -c BuildingSelectionUI Assets/MainIsland.unity` → 0), so `BuildPlacement.selectionUI` is
  always null and all 8 call sites in `BuildPlacement` / `GhostPlacer` / `WallLinePlacer` are dead.
  The shipping build has **no build UI** — number keys 1-7 and a `PlayerCharacter.SetActivity`
  toast when a type is research-locked.
- No finished building except the campfire has any click UI. `Hut`, `Workshop`, `Watchtower`,
  `Storehouse`, `Shipyard` have no `OnMouseDown`.
- No building knows its own `BuildingType` at runtime. `ConstructionSite` does; finished buildings
  do not. This is the one blocker for a generic upgrade.
- `BuildingType` is a flat enum with no tiers, categories, descriptions or unlock data. The three
  research gates (Workshop / Shipyard / Storehouse) are hardcoded `if`s in `BuildPlacement.SelectBuilding`.

## 1. Locked decisions (2026-09-18)

1. **Tent = tier 1, Hut = its tier-2 upgrade.** The Hut is never placed directly.
2. **Upgrades happen in place as a construction site** worked by colonists / the castaway through
   `AddLabor`, like any other build. No instant upgrades, no demolish-and-rebuild.
3. **Build palette = a grouped bottom icon bar**, above the `PlayerHUD` strip, visible only in
   build mode. Number keys keep working.
4. **Left-click a finished non-campfire building = a small selected-building card** (name, level,
   health, stats, Upgrade, Demolish). This card is where the upgrade system lives.

## 2. Data model — tiers become BuildingData fields

`BuildingData` gains, and these carry every rule the new UI needs:

| Field | Purpose |
|---|---|
| `int tier = 1` | Shown on the card as "Level N". |
| `BuildingData upgradesTo` | Direct SO reference; null = fully upgraded. Avoids a sentinel enum value. |
| `bool placeable = true` | **false on the Hut** — it exists only as a Tent's upgrade. The palette lists placeable types only. |
| `BuildCategory category` | `Housing / Production / Defence / Special`. Groups the palette bar. |
| `string description` | One line for the palette tooltip and the card. |
| `bool requiresUnlock` + `Unlocks.Kind requiredUnlock` | Replaces the three hardcoded `if`s. Still exactly one `Knowledge.Has` read at the site that decides (`SelectBuilding`), just parameterised — and the palette reads the same field to grey a tile and name the research via `Unlocks.ResearchTitleFor`. |

**Upgrade price = the target tier's own cost**, paid on confirm. One number to read and to tune;
no second cost table. Build time = the target's `buildTimeOverride` / site `buildTime` as usual.

## 3. The Tent

| | Tent (new, tier 1) | Hut (tier 2, no longer placeable) |
|---|---|---|
| Cost | 10W | 20W 10F (as the upgrade price) |
| Beds | 1 | 2 |
| HP | 50 | 100 |
| Build | ~4s | as today |
| Gate | none, from day one | none — any Tent may upgrade |
| Key | **1** | — |

Per bed the Tent is *cheaper* (10W/bed vs 15W/bed) but costs footprint, HP and a no-build radius,
so upgrading is the land- and defence-efficient move rather than a pure saving. That is the
intended tension and the first thing to tune after a sweep.

**Art:** a new `Tent` shape in `Shapes_Buildings.cs` (A-frame canvas over two poles, 2x2 footprint
to match the Hut so colliders and carve volumes are interchangeable), a plumber table entry, then
`Tent.prefab` + `TentGhost.prefab` + `TentData.asset` built by a new `BuildTent*` trio in
`NewContentSetup.cs` — the exact three-step recipe the Storehouse used on 2026-09-16.

**Component:** `Tent.prefab` carries the existing **`Hut`** component (workerCapacity 1, maxHealth 50).
Every consumer — `Population` housing, raider target lists, `Prosperity`, the minimap, `Siege` —
wants "any housing", which is what `Hut.ActiveList` already means. Renaming `Hut` → `Housing` is a
~30-site mechanical change plus a case-sensitive file rename (the 2026-09-10 `WatchTower.cs` trap),
so it is **deliberately deferred**; `Hut.cs`'s summary gets a line saying it is the housing
component for every tier. **Flagged as the one knowingly-lying name in this plan** — say the word
and it becomes a slice 0 rename instead.

## 4. The upgrade mechanism

**`IBuildingIdentity`** — `BuildingType BuildingType { get; }`, implemented by `Hut`, `Workshop`,
`Watchtower`, `Storehouse`, `Shipyard`, `Wall`. This is the missing link; nothing else can be
generic without it.

**`BuildingUpgrade`** (static, `Scripts/BuildingUpgrade.cs`):

- `bool CanUpgrade(GameObject building, out BuildingData next, out string reason)` — the single
  owner of the rule set: identity resolves → `upgradesTo != null` → unlock held → affordable.
  `reason` is the player-facing string the card greys out with ("Fully upgraded", "Needs Carpentry",
  "Not enough wood").
- `bool TryUpgrade(GameObject building)` — spends, records transform + faction, destroys the
  building (its own `OnDestroy` releases housing and unregisters exactly once, as today), and spawns
  a `ConstructionSite` at the same pose with `SetBuildingType(next)` and a new `isUpgrade` flag.

`ConstructionSite.isUpgrade` does two things: **skips `FlattenArea`** (the pad already exists — a
second flatten re-bakes the NavMesh for nothing) and lets the progress sign read "Upgrading to Hut".

**During the upgrade the beds are gone.** The Tent's housing is released on destroy and the Hut's
registers on completion, so its occupants are homeless for the build. `Population.RegisterHousing`
already moves homeless colonists in, and since 2026-09-17 a fire-side sleeper polls `HomeOf` and
walks in, so this self-heals — they sleep by the fire for ~10s. Upgrading mid-raid is therefore a
real mistake the player can make; the card warns rather than forbids ("raiders ashore").

## 5. `BuildPaletteHUD` — the palette

New `UI/BuildPaletteHUD.cs`, code-built uGUI in the `MenuBuilder` / `CombatHUD` idiom. Runtime-added,
never in a scene. Shown only while `BuildPlacement.isPlacing`.

- Bottom-centre, **above the `PlayerHUD` strip** (strip is sort 45; the bar sits at sort 50, y =
  strip height + margin, mirroring the `IntroHintCanvas` rule).
- Four groups with captions: Housing (Tent) · Production (Workshop, Storehouse) · Defence (Wood Wall,
  Stone Wall, Watchtower) · Special (Shipyard). Groups come from `BuildingData.category`, contents
  from `placeable`, so a new building appears with no UI change.
- Tile = hotkey badge + name + cost chips (W/F/S/M, red when unaffordable) + `Tooltip.Attach`
  description. Locked tiles are greyed and name their research. Selected tile lit like `TintTabs`.
- Clicking a tile calls `BuildPlacement.SelectBuilding` (made public).
- Wall-line mode swaps the row for the line readout ("Wood Wall x12 — 180W 60S"), replacing
  `UpdateWallLineDisplay`.
- Bottom hint line, context-sensitive, built from live `KeyBindings` names (rotate / Bresenham /
  gate / demolish / cancel) rather than the old hardcoded "1-4: Select".
- **`BuildPaletteHUD.PointerOver`** — a static rect test against `Input.mousePosition`, in the
  `Minimap.PointerOver` pattern, checked by `GhostPlacer` and `WallLinePlacer` before confirming and
  by `CameraController`'s edge pan. uGUI stops nothing; without this, clicking a tile also places a
  building under it.

## 6. `SelectedBuildingHUD` — the card

New `UI/SelectedBuildingHUD.cs`. Bottom-**left** (the palette owns centre, so the two never collide).

- Opened by a left-button-only `OnMouseDown` added to `Hut`, `Workshop`, `Watchtower`, `Storehouse`,
  `Shipyard`, `Wall`, each guarded by `PauseController.BlockGameplayInput` and `Minimap.PointerOver`
  like the four existing `OnMouseDown`s. Player-owned buildings only — a rival's opens nothing,
  matching the "rivals stay dark" rule.
- Rows: title + "Level N" · health bar and numbers · one stat line per type (beds / stockpile room /
  vision radius / bench) · **Upgrade** button showing the target and its cost, greyed with `reason` ·
  **Demolish** button (50% refund, campfire protected).
- Closed by Esc, by clicking elsewhere, and when the building dies. Registered with
  `PauseController.ModeActive` like `WorkerAssignmentUI` so Esc closes it instead of pausing.
- The campfire is untouched — it keeps opening `ColonyPanel`.

## 7. Deletions

- `BuildingSelectionUI.cs` + `.meta`.
- `BuildPlacement.selectionUI` and all 8 call sites in `BuildPlacement` / `GhostPlacer` /
  `WallLinePlacer`, each replaced by the `BuildPaletteHUD` equivalent.

## 8. Everything downstream that places or counts a Hut

Each of these places `BuildingType.Hut` today and must place `Tent`:

- `FactionBuilder.KeepHousing` (and its bed arithmetic — a Tent is 1 bed, not 2)
- `RivalFounder.Found` (the founding hut, plus its `HutPlaceTries` retry loop)
- `DebugMenu` F4 (player camp and rival camp)
- `GovernorPolicy.WantedColonists` / `KeepHousing` bed maths

Counting sites that read `Hut.ActiveList` keep working unchanged because both tiers carry the `Hut`
component: raider target order, `Siege.FindNearestBuilding`, `Prosperity`, Repair, Demolish refund,
Idle strolls, minimap, ghost clearance, `RivalFounder.IsClearForBuilding`.

**Governor upgrades are slice 5, not slice 1.** In slices 1-4 the AI colonies place Tents and never
upgrade; only the player upgrades. Slice 5 adds `GovernorPolicy.ConsiderUpgrades` (upgrade the oldest
Tent when beds are not short and wood is spare) with its own sweep.

## 9. Key bindings

Only key 1 changes meaning: `Action.SelectHut` → `Action.SelectTent`. 2-7 are untouched. The Hut
leaves the hotkey set entirely, since it is never placed.

## 10. Slices

| # | Content | Green + playable at the end? |
|---|---|---|
| 1 | `BuildingData` tier fields, `BuildCategory`, `IBuildingIdentity`, Tent art + prefabs + data, Hut → tier 2 `placeable=false`, key 1 → Tent, every placer switched to Tent | Yes — Tents build, Huts unreachable |
| 2 | `BuildingUpgrade`, `ConstructionSite.isUpgrade`, research gates moved into data | Yes — upgrade callable, no UI yet |
| 3 | `BuildPaletteHUD` + `PointerOver` guard; delete `BuildingSelectionUI` and its 8 call sites | Yes |
| 4 | `SelectedBuildingHUD` + the six `OnMouseDown`s + the Upgrade button | Yes — the feature is complete for the player |
| 5 | Governors place and upgrade Tents; sweep; docs, `Changelog.txt`, `DevQuests.txt` batch, `CONTROLS.md`, `Information.txt` table, CLAUDE.md gotchas | Yes |

Each slice: green in all four Roslyn configs (`py tools/verify-scripts.py -w`) before moving on.
Slices 1 and 5 need `Setup Everything` re-run (new art, new prefabs, new `BuildingData` asset).

## 11. Known costs and risks

- **Sim baselines are invalidated by slice 1.** Housing changes from 2 beds per 20W+10F to 1 bed per
  10W, and every governor's housing curve moves with it. Re-baseline after slice 5; do not compare
  against anything from 2026-09-17 or earlier.
- **`Hut` is the housing component for the Tent too** — see §3. Deliberate, reversible, flagged.
- **Upgrading destroys housing for the build duration.** Self-healing, but exploitable as a mistake
  during a raid. Warned, not forbidden.
- The palette and the card are two new always-listening click surfaces; both need their
  `PointerOver` guard or they will place buildings and pan the camera through themselves.
