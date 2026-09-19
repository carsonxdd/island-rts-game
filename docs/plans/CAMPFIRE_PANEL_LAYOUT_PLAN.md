# Campfire Panel Layout Plan

Status (2026-09-17, late): BUILT as recommended — the density toolkit (T1–T7)
plus Shape C, both steps in one pass, with stance / formation / bell left to the
combat box, tooltips for help, and one panel open at a time in one corner
(title-bar BENCH / COLONY buttons swap). The Stock tab became one line on COLONY.
Unplaytested; the "Campfire panel in two halves" quest batch is the checklist.
The body below is the plan as written before building.

The content of the panel is right; its shape is wrong. This document lays out where the height comes from,
a density toolkit that applies to every option, five layouts for the same
content, and a recommendation. Decisions to make are at the end.

The panel is `WorkerAssignmentUI.cs` (1339 lines), built on `MenuBuilder`
widgets whose rhythm was designed for the Options screen: 48 px rows, 52 px
tabs, 20 px description under every control, 28 px panel padding. That rhythm
is right for a settings screen you visit once and wrong for a panel you open
forty times a run.

---

## 1. Where the height goes

Everything below is in reference pixels (1920 × 1080, `MenuScaler.BaseReference`).
The panel is 640 wide, bottom-left, draggable, height computed by
`FitPanelHeight`.

| Piece | Rows | Height |
|---|---|---|
| Padding + title row + divider + tab row | | ~150 |
| Colonists header + two count lines | | 62 |
| **Jobs** fold: 4 counters + help line | 4 × 48 + 20 | 243 |
| **Specialists** fold: 3 counters + help line | 3 × 48 + 20 | 195 |
| **People** fold: up to 12 lines + help line | 12 × 20 + 20 | 291 |
| **Priorities** fold: 4 sliders + help line | 4 × 48 + 20 | 243 |
| **Defence** fold: Warriors, Arm with, Levy, Bell, Stance, Formation + 5 help lines | 6 × 48 + 5 × 20 | 419 |
| Column spacing (4 px × ~40 elements) | | ~160 |
| **Colonists tab, everything open** | | **~1760** |
| Craft or Research tab | 470 scroll region + chrome | ~700 |
| Stockpile tab | Room + one 48 px row per material | ~450 |
| Queue tab | 8 × 48 + status + Send button | ~520 |

The screen is 1080. Every fold on the Colonists tab is there because the tab
cannot exist without them, and even then two folds open is already a screen.
Three things drive that number:

1. **The 48 px row.** A counter (`Wood cutters  − 3 +`) needs 30 px of glyph.
   Eighteen counter, stepper and slider rows on one tab at 48 px is 864 px on
   their own.
2. **The help line under everything.** Fourteen `RowDescription`s on the
   Colonists tab alone, 280 px of muted text the player reads once.
3. **Content that already lives elsewhere.** Stance, Formation and Bell are the
   combat box in the bottom-right (`CombatHUD`), which the panel mirrors
   stepper for stepper. Every stockpile line is a HUD chip breakdown
   (`ResourceUI`, `hudListed` items). Both duplicates are full 48 px rows.

The Craft, Research and Queue tabs are one workflow (pick → wait → done) cut
into three tabs, so the player tabs back and forth to see whether the thing
they queued is moving.

---

## 2. Density toolkit (applies to every option)

These are the small things. They are independent of the shape chosen in
section 3 and most of the height win comes from here.

**T1. A compact row height.** `MenuStyle.CompactRowHeight` 32 for data rows,
used by the panel's counters, steppers and value rows through a `height`
argument that `SettingRow` already takes. The Options screen keeps 48.
Colonists tab: 18 rows × 16 px = 288 px saved.

**T2. Help moves out of the flow.** Three choices, cheapest first:

- *A "?" toggle in the title bar* shows and hides every `RowDescription` on
  the panel at once (`SetActive` on a list the builders fill; prefs key
  `ui.campfire.help`, default on for the first three opens, then off). Zero
  new widgets. 14 lines × 20 px = 280 px saved on the Colonists tab.
- *Tooltips.* A single shared `MenuBuilder.Tooltip` label on its own canvas
  (sort 90), shown on pointer-enter over a row's caption (an `EventTrigger` on
  the row image, which already exists and is raycastable), hidden on exit,
  never over a control. The description text moves from the flow to the row.
  One new widget, ~80 lines, and every menu screen can use it later.
- *A one-line footer.* The panel's last line shows the help of whichever row
  the mouse is over. Same wiring as tooltips, no floating element.

Tooltips are the one to build; the "?" toggle is the fallback if the tooltip
turns out to fight the camera's edge pan or the minimap's pointer test.

**T3. Steppers become segmented rows.** `‹ Defensive ›` is a 48 px row that
hides the two other choices. `[Defensive][Offensive][Follow]` on one 32 px row
is what the combat box already draws (`CombatHUD` stance buttons, 36 × 104).
A `MenuBuilder.SegmentRow(parent, label, names, index, onPick)` built from the
combat box's row code serves Stance, Formation, Bell and the Arm-with picker.

**T4. Sliders become four mini sliders on one row.** The four priority sliders
are one 32 px row: `Build ▭▭▭  Craft ▭▭▭  Repair ▭▭▭  Tidy ▭▭▭`, each slider
~110 px wide. Priorities are set once a run; they do not need 192 px.

**T5. Fixed-height body, scrolling.** Whatever the shape, the body is a
`ScrollColumn` of fixed height (the Options screen does this at 380, the
station tabs at 470). The panel stops growing with the roster, `FitPanelHeight`
runs once at build, and the folds stop being load-bearing.

**T6. The People list is a fixed 6-line window that scrolls**, not 12 lines
that grow. Name · trait · role · doing, one 20 px line each, sorted jobless
first. (`PeopleRowsShown` 12 becomes the pool; the viewport shows 6.)

**T7. Craft and Research rows lose the description line** (it goes to the
tooltip) and put the cost on the same line as the title, coloured by
affordability: `Wooden Spear      3 stick 1 chunk 5W   [Craft][×5]`. Three
lines become one 32 px row. The 470 px scroll region then shows fourteen
recipes instead of five.

With T1–T7 and no shape change at all, the Colonists tab, everything shown,
is roughly:

| Piece | Height |
|---|---|
| Chrome | 150 |
| Count lines | 40 |
| Jobs 4 × 32 | 128 |
| Specialists 3 × 32 | 96 |
| People window | 120 |
| Priorities one row | 32 |
| Defence 4 rows (Warriors, Arm with, Levy, Stance / Formation / Bell) | 128 |
| Spacing and section headers | 120 |
| **Total** | **~810** |

Fits on one screen with no folds. That alone answers "too big"; the shape
options below answer "too much".

---

## 3. Five shapes for the same content

Each is drawn at reference pixels. `▸` marks a scroll region.

### Shape A — Ledger: side tabs, two columns, 960 wide

The panel turns sideways. Tabs are a vertical rail on the left (icon + word,
five entries, the Workshop shows three). The body is two columns of 440. Wide
and short instead of narrow and tall, so it sits over the beach in the
bottom-left and leaves the village visible.

```
╔══════════════════════════════════════════════════════════════════════════╗
║ CAMPFIRE                                                              [X]║
╟──────────┬───────────────────────────────┬───────────────────────────────╢
║ ● Colony │ 7 colonists · 2 idle · beds 8 │ PEOPLE                        ║
║   Stock  │ JOBS                          │ Ada · Hardy · Wood cutter     ║
║   Bench  │ ■ Wood cutters        − 3 +   │ Bo · Night owl · Forager      ║
║   Queue  │ ■ Foragers            − 2 +   │ Cy · Lazy · idle              ║
║          │ ■ Quarriers           − 0 +   │ Di · Early riser · Warrior    ║
║          │ ■ Miners              − 0 +   │ … ▸                           ║
║          │ SPECIALISTS                   │                               ║
║          │ Builders              − 1 +   │ PRIORITIES                    ║
║          │ Crafters              − 0 +   │ Build ▭▭ Craft ▭▭ Rep ▭▭ Tidy ▭▭ ║
║          │ Repairers             − 0 +   │                               ║
║          │ DEFENCE                       │                               ║
║          │ Warriors              − 2 +   │                               ║
║          │ Arm with  [Spear 3][Bow 1]    │                               ║
║          │ Levy  3 spare · bell [Quiet]  │                               ║
╚══════════╧═══════════════════════════════╧═══════════════════════════════╝
```

- Height fixed ~460. Colony tab needs no scroll at all with T1–T4.
- Stock: two columns of value rows, materials left, equipment right.
- Bench: recipes left (▸), research right (▸), the queue is a strip along the
  bottom of both (`3 · Wooden Spear 40% · Bow · Woodcutting`). The Queue tab
  survives as the place to remove entries and read who is at the bench.
- Stance / Formation are NOT on this panel; the combat box owns them (see
  the Defence question at the end).
- Cost: medium. New `MenuBuilder.Rail` (vertical tab column), a two-column
  body (`HorizontalLayoutGroup` of two `Column`s), every builder re-parented.
  The dirty-checked update code does not change.

### Shape B — Same panel, fixed height, no folds (the cheap one)

Keep the tab row and the 640 width. Apply T1–T7. The body is one 560 px
`ScrollColumn`; the folds are removed and the section headers stay as plain
headers. Nothing moves anywhere else.

```
╔════════════════════════════════════════════════╗
║ CAMPFIRE                                    [X]║
║ [Colonists][Stockpile][Craft][Research][Queue] ║
║ 7 colonists · 2 idle · Housing 7 / 8           ║
║ JOBS                                           ║
║ ■ Wood cutters                        − 3 +    ║
║ ■ Foragers                            − 2 +    ║
║ ■ Quarriers                           − 0 +    ║
║ ■ Miners                              − 0 +    ║
║ SPECIALISTS                                    ║
║ Builders                              − 1 +    ║
║ Crafters                              − 0 +    ║
║ Repairers                             − 0 +    ║
║ PEOPLE                                       ▸ ║
║ Ada · Hardy · Wood cutter · Chopping           ║
║ Bo · Night owl · Forager · Returning           ║
║ (4 more lines)                                 ║
║ PRIORITIES  Build ▭▭ Craft ▭▭ Repair ▭▭ Tidy ▭▭║
║ DEFENCE                                        ║
║ Warriors                              − 2 +    ║
║ Arm with          [Wooden Spear 3][Bow 1]      ║
║ Levy 3 spare · 0 mustered   Bell [Quiet][Ring] ║
║ Stance   [Defensive][Offensive][Follow]        ║
║ Formation [Auto][Line][Wedge][Ring][Loose]     ║
╚════════════════════════════════════════════════╝
```

- Height ~810, fixed. Fits at 1080 with the bottom strip beside it.
- Cost: low. Two days of widget work (T1–T7), delete the folds and their
  prefs keys, one `ScrollColumn` around the body.
- Weakness: still five tabs, still a tall column over a third of the screen,
  still Craft/Research/Queue as three trips.

### Shape C — Two panels by purpose: COLONY and BENCH

The five tabs are really two things: the roster and the workshop. Cut the
panel in two and let the duplicates go home.

- **COLONY** (left-click the fire or a colonist): Jobs, Specialists, People,
  Priorities, Warriors + Arm with + Levy. Two tabs at most: *People* and
  *Stock* (Stock keeps the Room line, which the HUD chips do not show).
- **BENCH** (left-click the fire's bench area, the Workshop, or a HUD "bench"
  button): one list with a segmented filter `[Make][Learn]` and the queue as
  a docked strip at the bottom with Remove on hover and the Send button.
  Craft, Research and Queue become one tab.
- **Stance / Formation / Bell** leave the panel. The combat box already has
  them; it gains a one-line `Levy 3 spare` readout so the levy is where the
  militia is.
- **Stockpile** lines leave the panel. The HUD chips show them on hover
  today; the Stock tab keeps only Room and the equipment rack (spears, bows,
  which no chip lists).

```
COLONY (560 wide, ~520 tall)              BENCH (560 wide, ~560 tall)
╔══════════════════════════════════╗      ╔══════════════════════════════════╗
║ CAMPFIRE · COLONY            [X] ║      ║ CAMPFIRE BENCH               [X] ║
║ [People][Stock]                  ║      ║ [Make][Learn]                    ║
║ 7 colonists · 2 idle · beds 7/8  ║      ║ Wooden Spear  3st 1ch 5W [1][5]▸ ║
║ JOBS                             ║      ║ Bow           4st 5W     [1][5]  ║
║ ■ Wood cutters          − 3 +    ║      ║ Iron Spear    2st 5W 4M  [1][5]  ║
║ ■ Foragers              − 2 +    ║      ║ Stone Axe     … (done)           ║
║ ■ Quarriers             − 0 +    ║      ║                                  ║
║ ■ Miners                − 0 +    ║      ║                                  ║
║ Builders − 1 +  Crafters − 0 +   ║      ║                                  ║
║ Repairers − 0 +                  ║      ║                                  ║
║ PEOPLE                         ▸ ║      ╟──────────────────────────────────╢
║ Ada · Hardy · Wood cutter        ║      ║ QUEUE  Ada at the bench          ║
║ Bo · Night owl · Forager         ║      ║ ▮ Wooden Spear ×3  40%       [x] ║
║ …                                ║      ║ ▮ Bow                        [x] ║
║ Build ▭▭ Craft ▭▭ Rep ▭▭ Tidy ▭▭ ║      ║ [ Send your character to work ]  ║
║ Warriors − 2 +  Arm [Spear][Bow] ║      ╚══════════════════════════════════╝
║ Levy 3 spare · 0 mustered        ║
╚══════════════════════════════════╝
```

- The Workshop opens BENCH only, exactly as today's `stationOnly`.
- Both panels can be open at once (COLONY bottom-left, BENCH beside it) or
  BENCH replaces COLONY in the same slot; decide in the questions.
- Cost: medium. One class becomes two (`ColonyPanel`, `BenchPanel`, sharing
  the row builders through a small `PanelRows` static), the combat box gains
  a line, `OpenPanel` / `OpenStation` / `OpenColonists` keep their names.

### Shape D — Command dock: a wide bar over the bottom strip

The RTS answer. No floating panel; a 1400 × 220 dock above `PlayerHUD`,
between the character strip and the combat box, with an icon column on its
left for the pages. Content runs sideways in tiles, so the world stays
visible above it.

```
╔══╤══════════════════════════════════════════════════════════════════════════╗
║👥│ JOBS  ■Wood −3+  ■Food −2+  ■Stone −0+  ■Metal −0+ │ SPEC Build −1+ Craft −0+ Repair −0+ ║
║📦│ PEOPLE  [Ada·chopping][Bo·returning][Cy·idle][Di·guard][Ed·asleep] … ▸       ║
║🔨│ PRIORITIES Build ▭▭ Craft ▭▭ Repair ▭▭ Tidy ▭▭ │ WARRIORS −2+ Arm [Spear][Bow] Levy 3 ║
║📜│                                                                          ║
╚══╧══════════════════════════════════════════════════════════════════════════╝
   (character strip below, combat box to the right, unchanged)
```

- Bench page: a grid of recipe tiles (name, cost, queue count, click = one,
  Shift-click = five, hover = tooltip), research tiles beside them, the queue
  as chips along the bottom edge with a progress bar each.
- Stock page: one chip per material, like the HUD's, plus Room.
- Cost: high. Every row widget in the panel is vertical; the dock needs
  horizontal tiles (`MenuBuilder.Tile`, `TileRow`), a page rail, and the
  PlayerHUD / CombatHUD anchors move up. The dirty-check code survives but the
  builders are rewritten.
- Risk: a 1400-wide bar at the reference size is 73% of the screen width and
  the minimap and combat box already own the right side; at 1280 × 720 it is
  the whole bottom.

### Shape E — One section at a time (exclusive accordion)

Smallest change. Keep everything; make the folds exclusive, so opening one
closes the others. The panel height is then bounded at
`chrome + 5 headers + the tallest section` ≈ 150 + 5 × 31 + 419 ≈ 720.

- Cost: a day. `CollapsibleSection` takes a group id; opening one closes
  the rest of its group; prefs remembers which one.
- Weakness: it is the current panel with more clicking. It answers "too big"
  and not "too much".

---

## 4. Comparison

| | A Ledger | B Fixed | C Two panels | D Dock | E Accordion |
|---|---|---|---|---|---|
| Colonists content on one screen, no folds | yes | yes | yes | yes | no |
| Tabs the player sees | 4 (rail) | 5 | 2 + 2 | 4 (rail) | 5 + folds |
| Craft/Research/Queue in one place | yes | no | yes | yes | no |
| Duplicates removed (stance, stock lines) | yes | no | yes | yes | no |
| Screen covered | wide-low | tall-narrow | two mid | wide-low | tall-narrow |
| New MenuBuilder widgets | Rail, 2-col, Tooltip, Segment | Tooltip, Segment | Tooltip, Segment, queue strip | Tile, TileRow, Rail, Tooltip | none |
| Rewrite of `WorkerAssignmentUI` | reparent | trim | split in two | rewrite builders | none |
| Effort | 3–4 days | 2 days | 3–4 days | 6–8 days | 1 day |
| Workshop panel | rail with 3 | 3 tabs | BENCH only | 1 page | as today |

---

## 5. Recommendation

Two steps, the first one cheap and shippable on its own.

**Step 1: the density toolkit on the current shape (Shape B).** T1–T7 in
this order: compact rows, segmented rows, tooltips, priorities on one row,
craft/research one-line rows, People window, then the fixed-height body and
the folds removed. Every step is visible on its own and none changes what a
button does. Playtest here: the panel now fits, and the question "is it still
too much" gets answered by playing rather than guessing.

**Step 2: Shape C, the two panels.** The five-tab campfire panel is two
things wearing one frame. COLONY is the roster and the fire's counters;
BENCH is the workshop with its queue docked under the list, and the Workshop
already opens exactly that. Stance / Formation / Bell go to the combat box
that already draws them; Levy gets a line there. The Stock tab shrinks to
Room and the rack. The result is two panels of ~520 px that each fit under
the resource bar with the bottom strip beside them, four tabs in total, and
no fold anywhere.

Shape A is the runner-up if a single frame matters more than a small one; it
takes the same toolkit and the same Bench merge and differs only in the rail
and two columns. Shape D is the best-looking answer and the wrong time for it:
the HUD's right side is already spoken for, and the panel's row widgets are
what would need rewriting. Shape E is a stopgap, not a layout.

---

## 6. Rules that carry over (from CLAUDE.md, so nothing regresses)

- Panels are computed height through `FitPanelHeight`, forced full rebuild;
  a fixed scroll region inside keeps it deterministic.
- Every click surface is a raycastable Image; scroll viewports clip with
  `RectMask2D` and carry a permanent opaque-white scrollbar handle.
- Wheel over a list belongs to the list (`CameraController.PointerOverScrollView`).
- A tooltip canvas must not swallow gameplay clicks: its Image is
  `raycastTarget = false`, always.
- `PauseController.ModeActive` consults `WorkerAssignmentUI.IsOpen`; two
  panels means two `IsOpen`s or one static that ORs them. Esc closes the
  newest.
- Draggable panels re-clamp on open (`DraggablePanel.Clamp`); a second panel
  gets its own prefs key and default corner.
- The People text is rebuilt at 2 Hz on a `StringBuilder`, never per frame;
  a scrolling window changes nothing about that.
- Segment rows read the faction's live state every frame and dirty-check, as
  the steppers do, so the combat box and the panel never disagree.
- Every visible change gets a `Changelog.txt` entry and a `DevQuests.txt`
  batch (`collapse` signals go away with the folds; new ones: `tooltip`,
  `segment`, `bench:filter`, `queue:strip`).

---

## 7. Decisions to make before code

1. **Shape.** Step 1 then Shape C is the recommendation. A (ledger) if one
   frame is preferred; D (dock) only if the HUD's right side is allowed to
   move.
2. **Where Stance / Formation / Bell live.** In the combat box only
   (recommended: it is already there and always on screen once a warrior
   exists), or mirrored in the panel too as a one-row segment.
3. **Help text.** Tooltips (recommended), a "?" toggle, or a footer line.
4. **Two panels open at once,** or BENCH replaces COLONY in one slot.
5. **Width.** Keep 640 for a single panel; 560 each for two; 960 for the
   ledger.
