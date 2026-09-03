# Player Character & Campfire Crafting — Design Plan

Drafted 2026-09-02. Status: **plan only, nothing implemented.** The design decisions are
locked (section 9); Slices A–C are the first batch, Slice D follows a playtest.

---

## 1. The ask

1. A name prompt at the start of a run (UI popup). The name floats above the character.
2. The castaway is **the player's character for the whole run.** Placing the campfire no
   longer converts them into a colonist; they stay under direct control.
3. The character hand-collects sticks and stones on the beach and **crafts at the campfire.**
   Crafting a tool **unlocks** the matching activity for the colonists.
4. The character can **hold things** (a visible tool / carried item).
5. The campfire has an **inventory** (a stockpile) separate from the four-resource pool.
6. The four main resources stay; on top of them come **items** (sticks, stones, tools, later
   more).

## 2. The loop this creates

```
Land at the cove  →  right-click sticks / stones on the beach  →  place the campfire
   →  craft a Stone Axe at the fire (2 sticks + 1 stone)  →  Wood job unlocked
   →  first survivor arrives (housing timer, unchanged)  →  assign Wood
   →  craft Pick / Spear / Mallet …  →  Stone, Food, Construction, Militia unlock
   →  build a Workshop  →  tier-2 upgrades (the existing three recipes)
```

The colony still runs itself. The player's character is the *bootstrap*: they gather with
their hands, craft the first tools, and spend the rest of the run as a free agent
(errands, extra hands at a node, carrying salvage) while the Utility AI does the work.

## 3. What exists that this builds on

| Existing piece | Reused how |
|---|---|
| `Survivor.cs` + `Survivor.prefab` (right-click move, `AINavHelper` retry, no Health/AI/registry) | Becomes `PlayerCharacter` — same locomotion, plus Health, inventory, commands |
| `GameStartController` phases `Landing → PlacingCampfire → Settling → Colony` | Kept; `StartColony` stops destroying the survivor |
| `GroundPickup` (stick +3 wood, stone +3 stone, crate 6 food, barrel 5 wood; no collider) | Same objects; gain a click collider on a new layer and an item mapping for the player |
| `PickupSpawner` (26 sticks / 16 stones, trickle respawn) | Gains a cove cluster so the beach has materials on landing |
| `CraftedUpgrades` static (multipliers read at point of effect) + `Workshop` + `CraftingUI` | Multipliers stay; recipes fold into one `CraftingCatalog` with a *station* field |
| `WorkerAssignmentUI` (code-built campfire panel on `MenuBuilder`, draggable) | Grows tabs: **Colonists · Stockpile · Craft** |
| `MenuScreens` / `MenuBuilder.InputRow` (seed field) | Name popup is a new `Screen.NameEntry` |
| `FloatingText` (billboard state text) | Name label above the character |
| `Difficulty` / `IslandOptions` snapshot pattern | `PlayerProfile` holds the name the same way |
| `KeyBindings` catalog | New action: centre camera on your character |
| `ResourceManager` (wood / food / stone / metal) | Untouched; items are a separate layer |

## 4. Design

### 4.1 Name entry

- **Where:** a modal popup the moment `MainIsland` loads, before the Landing hint —
  `MenuScreens.Screen.NameEntry`, one `InputRow` ("Your name"), one **Begin** button.
  Pre-filled with the last name used (PlayerPrefs `player.name`); empty confirms as
  "Castaway". Random-name button optional (small list of period-appropriate names).
- **No Back / no Esc dismiss** — same rule as `Screen.GameOver`: `MenuScreens.Back()`
  early-returns on it, and `PauseController.ModeActive` treats it as a mode.
- **State:** `PlayerProfile.Active.name` — a run snapshot taken when Begin is pressed.
  `MenuFlow.Restart` keeps it (same rule as difficulty and seed); `NewGame` clears it so the
  popup shows again (pre-filled).
- **Skipped** under `SimHooks.Simulating`, when `skipIntro` is ticked, and by the F4
  force-start (name = "Castaway" / last used).
- The world is already frozen during the intro (`clockPaused`), so the popup does not need
  `timeScale 0`; it just blocks input via `BlockGameplayInput`.

### 4.2 The player character (`PlayerCharacter.cs`, replaces `Survivor.cs`)

- **Persists for the whole run.** `GameStartController.StartColony` no longer calls
  `SpawnColonist` for the survivor and no longer destroys them. The colony's first colonist
  comes from the existing arrival timer (campfire housing 3, 20 s cadence) — nothing else
  about arrivals changes.
- **Not a colonist.** Never in the `PopulationManager` roster, occupies no housing, holds no
  job, has no `AIBrain`. Keeps its own `NavMeshAgent` with worker locomotion values.
- **Control:** right-click is a *smart command* (see 4.6). Only the character is ever
  directly controlled; the colony stays indirect, so there is no selection model to add.
- **Name label:** `FloatingText` with a new `alwaysShow` flag (the player's name must not
  disappear with the "Unit state labels" setting), gold, slightly larger than unit text. A
  second, smaller line shows the current activity ("Gathering", "Crafting Stone Axe 60%").
- **Health (recommended):** 75 HP, regenerates slowly at the campfire like warriors. **Not
  targeted by enemies in v1** — enemies never targeted workers either, and the priority
  list in `EnemyAttackExecutor.PickTarget` is untouched. At 0 HP the character is *knocked
  out*: renderers off, respawns at the campfire edge after 10 s, inventory kept. **No new
  defeat condition** — losing the campfire is still the only loss.
- **Camera:** no auto-follow (it is an RTS). New `KeyBindings.Action.CenterOnCharacter`
  (default **Space**, currently unbound) pans the view centre onto the character using the
  same XZ-delta trick `FrameCameraOnSurvivor` uses. Shows up on the Controls screen
  automatically via the catalog.
- **Art:** a `Castaway` variant of the Worker meeple in `Shapes_Units` (different hat /
  colour so the player is never mistaken for a colonist), plus a `HandSocket` empty on the
  Model child for held items.
- **Prefab:** `PlayerCharacter.prefab`, built by `OpeningSequenceSetup` from what
  `Survivor.prefab` is today (agent + script + nested art). `Survivor.prefab` is deleted by
  the tool.

### 4.3 Items and inventories

**Two layers, deliberately separate:**

| Layer | Owner | Holds | Spent by |
|---|---|---|---|
| Resources (existing) | `ResourceManager` | wood, food, stone, metal — a number each | buildings, warriors, workshop recipes, repairs |
| Items (new) | `Inventory` instances | stacks of `ItemDef` — sticks, stones, tools, and *resource items* in transit | campfire recipes |

- **`ItemCatalog`** (static array, the `CraftedUpgrades.Recipes` pattern — no editor assets
  to churn): `ItemDef { id, displayName, kind (Material / Tool / Resource), stackMax,
  resourceType? }`. `Resource` items are wood/food/stone/metal *in hand*: a crate gives
  Food ×6 as an item stack, and depositing at the fire converts it to `ResourceManager`
  food. That lets one inventory carry everything the character can pick up or gather.
- **`Inventory`** (plain class): fixed slot count, `Add / Remove / Count / CanFit`,
  `OnChanged` event. Zero allocation after construction.
- **Player inventory:** 6 slots, materials stack 10, resources stack 10, tools stack 1.
  Plus one **held** slot (4.5).
- **Campfire stockpile:** `BaseBuilding.stockpile`, 12 slots, stack 99. This *is* the
  campfire inventory the ask names. Shown on the panel's Stockpile tab.
- **Deposit:** right-click the campfire (or the **Deposit all** button on the panel while
  within 2 u of its edge). Resource items go to `ResourceManager`; materials and tools go
  to the stockpile.
- **Workers are unchanged.** Their pickup loop still converts a stick straight to wood carry
  (`GroundPickup.Collect(AIBlackboard)`). The player path is a new `CollectAsItem`
  on the same component. One object, two collectors.

### 4.4 Collecting by hand

- Pickups gain a small `SphereCollider` (r ≈ 0.5) on a **new layer `Pickups` (index 7)**,
  created by the setup tool via `TagManager`. Reason: every ground raycast in the game
  (`BuildPlacement.groundLayer`, `GameStartController.RaycastGround`) is mask `Default`, so
  a collider on Default would park ghosts on top of sticks — the exact bug the ghost-collider
  gotcha records. A separate layer keeps pickups invisible to all of that and to
  `Physics.CheckBox` placement validity.
- **Cove cluster:** `PickupSpawner` places `coveSticks` (8) + `coveStones` (5) within 12 u of
  `TerrainGrid.CoveCenter` on land + NavMesh, so the opening has materials in reach
  (the user's "on the beach"). These count as spawner-owned and trickle back like the rest.
- Collect distance 0.9 (same as `CollectPickupExecutor`), pickup claimed by the player while
  walking so a worker never races them for it (`GroundPickup.claimedBy` is typed `Worker`
  today → widen to `MonoBehaviour`).

### 4.5 Held items

- `HeldItem.cs` on the character: one `ItemDef` equipped, rendered by instantiating the
  item's art prefab under `HandSocket`. Art lookup is an `ItemArtTable` (id → prefab) wired
  by the setup tool onto the prefab — not `Resources.Load`.
- Auto-equip: crafting a tool equips it; commanding a node equips the tool that node needs.
- Carried materials are shown on the HUD strip, not in hand (v1). A bundle-on-back visual
  can come later.
- Art: new `Shapes_Tools.cs` category — StoneAxe, StonePick, FishingSpear, Mallet,
  WoodenSpear — tiny meshes on existing palette keys. Add the category to the plumber's
  table or it is invisible in-game (the "art ≠ plumbing" gotcha).

### 4.6 Player commands (right-click)

One raycast against `Default | Buildings | Pickups`, nearest hit wins, classified by
component on the hit collider:

| Hit | Command |
|---|---|
| `GroundPickup` | walk (claim) → collect into inventory |
| `ResourceNode` | *Slice D:* walk to `GetGatherPoint` → gather by hand if the matching tool is unlocked; otherwise a hint ("Craft a Stone Axe first") |
| `BaseBuilding` (campfire) | walk to `GetApproachPoint` → deposit all → open the campfire panel |
| `Workshop` | walk → open its panel |
| terrain / anything else | move there (existing `MoveTo`, 4 u NavMesh snap) |

Left-click keeps its current meaning (buildings open their panels via `OnMouseDown`).
Commands are queued as a tiny `PlayerTask` (kind + target + approach point) so the
"arrive → act" step survives NavMesh rejections and stuck resets the same way executors do.
All building interactions use edge distance (`TargetingUtil.EdgeDistance`), never centre.

### 4.7 Crafting at the campfire

- **`CraftingCatalog`** (static): `Recipe { id, title, description, station (Campfire |
  Workshop), itemCosts[] (ItemDef, count), wood/food/stone/metal costs, seconds, output
  ItemDef?, unlocks Unlock[] }`. The three current workshop recipes move in with
  `station = Workshop`; `CraftedUpgrades` keeps only the multipliers.
- **Where materials come from (recommended):** the *combined pool at the fire* — player
  inventory + campfire stockpile + `ResourceManager` — so the player does not have to
  deposit before every craft. Player inventory is drained first.
- **How it runs:** the player must be within 2 u of the campfire edge; pressing Craft starts
  a timed player state (`Crafting`, agent stopped, label shows progress). **Costs are
  charged at completion** (re-checked then), so cancelling by moving away refunds nothing
  because nothing was taken. One craft at a time; the workshop keeps its own queue.
- **Output:** a tool goes to the player's inventory and is auto-equipped; overflow goes to
  the stockpile. The unlock fires on the first completion, permanently.
- **UI:** the campfire panel's **Craft** tab — one row per campfire recipe: title, effect,
  cost line coloured by affordability, Craft button, lock icon once done. Recipes the player
  cannot reach yet (needs Workshop) are listed greyed with the reason.

### 4.8 Unlocks — what a craft opens up

`Unlocks` static (flags + `Has(Unlock)`, reset on play like `CraftedUpgrades`,
`GrantAll()` for the sim and F4), **read at the point of effect, never pushed:**

| Recipe (campfire) | Cost (starting point) | Time | Unlocks | Point of effect |
|---|---|---|---|---|
| Stone Axe | 2 sticks · 1 stone | 6 s | **Wood** job | `BaseBuilding.AssignWorker` refuses; panel row shows lock + "Craft a Stone Axe" |
| Fishing Spear | 2 sticks · 1 stone | 6 s | **Food** job | same |
| Stone Pick | 2 sticks · 2 stones | 8 s | **Stone** job | same |
| Mallet | 3 sticks · 1 stone | 8 s | **Construction** | `BuildPlacement.StartPlacement` refuses with a hint; `ConstructionAvailable` / `RepairAvailable` score 0 |
| Wooden Spear | 3 sticks · 1 stone · 5 wood | 10 s | **Militia** (warriors) | `BaseBuilding.CanRecruitWarrior`; panel row lock |
| Metal Pick | 2 sticks · 2 stones · 15 stone | 12 s | **Metal** job | `AssignWorker` |

Gate design rules: a locked action must be *visibly* locked with the recipe named (no
silent no-ops), and every gate is a single `Unlocks.Has(...)` check at the site that already
decides the action, so nothing is duplicated in UI and logic.

The Workshop building itself needs only Construction. Its three recipes are unchanged in
effect and cost (wood/stone) and now sit in the same catalog.

### 4.9 HUD

- `PlayerHUD.cs` (code-built on `MenuBuilder`, sort order 45, registered with `MenuScaler`):
  a bottom-centre strip — name, held item, 6 inventory slots (glyph + count), and the
  current activity/progress. Dirty-checked like `ResourceUI`. Hidden under the sim.
- The intro hint text gains the collect and craft steps.

### 4.10 Balance intent (tune in playtest)

- The Stone Axe should be craftable within ~90 s of landing from beach materials alone.
- A full first-day bootstrap (axe + spear + pick + mallet) ≈ 10 sticks, 5 stones, all on the
  landing beach plus the first ring inland — the cove cluster plus the existing band.
- Colonist arrival cadence is unchanged, so unlocking Wood before the first survivor lands
  (~20 s after the fire) is *possible but tight*; a player who dawdles just has an idle
  colonist who builds nothing (no Mallet) and stands at the fire. Acceptable early friction.
- Warriors before night 1 require the Wooden Spear; night 1 (5 enemies) is survivable
  behind the campfire's 200 HP only if the player crafts it by then. This makes the spear
  the first real decision — keep its cost cheap.

## 5. Delivery slices (each leaves the game playable)

### Slice A — Identity (name popup + persistent character) — IMPLEMENTED 2026-09-02, pending setup re-run + playtest
`PlayerProfile.cs`, `PlayerCharacter.cs` (from `Survivor.cs`), `MenuScreens.Screen.NameEntry`,
`PauseController` mode check, `FloatingText.alwaysShow`, `KeyBindings.CenterOnCharacter`,
`GameStartController` (popup, no destroy, hints, `DebugForceColonyStart`), Castaway art +
`OpeningSequenceSetup` prefab build. Everything still unlocked, so play is unchanged apart
from the character walking around.

### Slice B — Hands and stockpile — IMPLEMENTED 2026-09-02, pending setup re-run + playtest
`Items/ItemCatalog.cs`, `Items/Inventory.cs`, `GroundPickup.CollectAsItem` + click collider +
`Pickups` layer (setup tool), `PickupSpawner` cove cluster, `PlayerCharacter` command
raycast + `PlayerTask` (collect, deposit, move), `BaseBuilding.stockpile` + deposit,
campfire panel tabs (Colonists · Stockpile), `PlayerHUD`.

### Slice C — Crafting and unlocks — IMPLEMENTED 2026-09-02, pending setup re-run + playtest
(Deviation from 4.3/4.7: crafted tools stay in the character's hands and are never deposited;
the stockpile holds materials only. The workshop recipes were NOT folded in — still Slice D.)
`Items/CraftingCatalog.cs`, `Items/Unlocks.cs`, Craft tab, timed crafting state, the six
gates from 4.8 with their UI locks, `HeldItem` + `Shapes_Tools` + `ItemArtTable` + plumber
row, `Unlocks.GrantAll()` under `SimHooks.Simulating` and in F4, F4 "give 10 sticks /
stones" and "unlock all" buttons.

### Slice D — Depth
Player hand-gathering at nodes with the equipped tool (1.5× worker rate, respects
`HasWorkerRoom`, uses `GatherRingRadius`); the workshop recipes folded into the catalog
and `CraftingUI` rebuilt on `MenuBuilder`; knocked-out / respawn; `docs/CONTROLS_AND_CHECKLIST.md`
+ `CLAUDE.md` gotchas; sim policies that craft the axe/spear before hiring (needed once
gates are on, or every sweep stalls at zero workers).

## 6. Gotchas to honour (from CLAUDE.md, applied here)

- **Esc order.** The name popup and the panel tabs both sit under `PauseController.ModeActive`;
  a new modal must be listed there or Esc pauses instead of closing it.
- **`MenuBuilder.Label` wraps by default** — HUD slot captions need `NoWrap`.
- **Raycastable surface** on anything clickable that is built in code (HUD slots, tabs).
- **Ghosts must never see a click collider** — hence the `Pickups` layer, never Default.
- **Edge distance for every building interaction** (`TargetingUtil.EdgeDistance` /
  `GetApproachPoint`); the campfire carves.
- **`TrySetDestination` returns Unity's real result** — `PlayerTask` retries next frame.
- **Point-of-effect reads, never pushes** for `Unlocks` (same as `CraftedUpgrades`, `Difficulty`).
- **Statics reset on play** via `[RuntimeInitializeOnLoadMethod]` for `Unlocks`,
  `CraftingCatalog.crafted`, `PlayerProfile`.
- **Anything that builds UI must guard `SimHooks.Simulating`** (popup, HUD, tabs).
- **Sim comparability:** gates change the economy; sweeps before and after Slice C are not
  comparable. Record it in `docs/SIMULATION.md`.
- **No new `Debug.Log`** for collects, crafts or deposits — HUD and labels show them.
- **Logging keep-list:** one line "Crafted <tool> — <unlock> unlocked" is a lifecycle event
  (once per recipe per run) and is acceptable.

## 7. Files

**New:** `Scripts/PlayerProfile.cs`, `Scripts/PlayerCharacter.cs`, `Scripts/Items/{ItemCatalog,
Inventory, CraftingCatalog, Unlocks, HeldItem}.cs`, `Scripts/UI/PlayerHUD.cs`,
`Editor/LowPoly/Shapes_Tools.cs`.

**Edited:** `GameStartController`, `MenuScreens`, `PauseController`, `FloatingText`,
`KeyBindings`, `GroundPickup`, `PickupSpawner`, `BaseBuilding`, `WorkerAssignmentUI`,
`BuildPlacement`, `ConstructionAvailable`, `RepairAvailable`, `Workshop`, `CraftingUI`,
`CraftedUpgrades`, `DebugMenu`, `SimRunner` / `SimPolicy`, `OpeningSequenceSetup`,
`NewContentSetup`, `LowPolyPlumber`, `Shapes_Units`, docs.

**Deleted:** `Survivor.cs`, `Survivor.prefab` (by the setup tool).

## 8. Playtest checklist (to be merged into `docs/CONTROLS_AND_CHECKLIST.md`)

- Name popup appears once on a new game, pre-filled next time, never on Restart; Esc does
  nothing on it; the name floats above the character in gold regardless of the state-label
  setting; Space centres the camera on them.
- The character survives campfire placement and can still be commanded; the first colonist
  lands ~20 s later from the cove; population reads 1/3 (the player is not counted).
- Sticks and stones are visible near the wreck; right-click walks to and collects them; the
  HUD strip fills; a worker never steals a pickup the player is walking to; placement ghosts
  never sit on top of a stick.
- Right-click on the campfire deposits (resource items hit the pool, sticks/stones hit the
  Stockpile tab) and opens the panel.
- Craft tab: costs colour by affordability across inventory + stockpile + pool; crafting
  takes the listed seconds, walking away cancels with nothing lost; the tool appears in the
  character's hand; the job row unlocks with no restart.
- Every locked action says which recipe unlocks it: job +, build mode B, warrior +.
- F4 "Unlock all" makes a run behave exactly like today's build; a sweep still completes.
- Slice D: with the axe equipped, right-click a tree gathers into the inventory at ~1.5×
  worker rate and respects node capacity.

## 9. Decisions (locked 2026-09-02)

1. **Unlock model: knowledge.** Craft a tool once and every colonist may do the job. No
   per-colonist tool supply.
2. **Character HP: yes, enemies ignore them, knocked out at 0 HP** → respawn at the campfire
   edge after 10 s with inventory kept. Campfire loss stays the only defeat.
3. **Crafting draws from the combined pool at the fire** (player inventory + stockpile +
   resource pool), player inventory drained first. Taken as the recommended default.
4. **Gated: the four jobs, construction, and warriors.** Every lock names its recipe.
5. **Slice D is a follow-up.** This batch is Slices A–C: name popup, persistent character,
   pickups + stockpile, campfire crafting + unlocks. Hand-gathering at nodes and the
   workshop recipe merge come after a playtest of A–C.
