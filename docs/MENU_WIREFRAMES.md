# Menu Wireframes

Every menu screen in the game as a text wireframe. These aren't mockups waiting
to be built — they're what the game renders today, in placeholder form.

All nine screens are constructed at runtime by `MenuScreens.cs` from the
vocabulary in `MenuBuilder.cs`. Every colour, size and spacing value lives in
**`MenuStyle.cs`** — one file restyles all nine. Keep this document in sync
with `MenuScreens.cs`; it's what an artist works from.

> A formatted version of this document, with the asset checklist and palette
> swatches, is published as an artifact for sharing with an artist.

---

## Navigation

```
    ┌─────────────┐      ┌─────────────┐        ┌─────────────┐
    │  MAIN MENU  │ ───▶ │  NEW GAME   │ ─────▶ │  GAMEPLAY   │
    └──────┬──────┘      │ (difficulty)│        └──────┬──────┘
           │             └─────────────┘               │  Esc
           │                                           ▼
           │                                    ┌─────────────┐
           │                                    │    PAUSE    │ ◀── game frozen
           │                                    └──────┬──────┘
           │                                           │
           ├──────────────┬────────────────────────────┤
           ▼              ▼                            ▼
    ┌─────────────┐ ┌─────────────┐             ┌─────────────┐
    │   OPTIONS   │ │   CREDITS   │             │  CONTROLS   │
    │ ┌─────────┐ │ └─────────────┘             │ (rebinding) │
    │ │  AUDIO  │ │                             └─────────────┘
    │ │  VIDEO  │ │                                    ▲
    │ │ CAMERA  │ │  Destructive actions route through:│
    │ │INTERFACE│ │  ┌─────────────┐                   │
    │ └─────────┘ │  │   CONFIRM   │  Restart ·        │
    │  CONTROLS ──┼─▶└─────────────┘  Main Menu ·      │
    └─────────────┘                   Quit · Resets ───┘
```

**Difficulty is asked once, on its own screen, before the run starts** — it is
locked for the duration of that run and does not appear in Options. The pause
menu shows it as a read-only line so a player doesn't go hunting for it.

**Victory and defeat replace gameplay, not pause.** They come up on the same
canvas as everything above, have no Back, and lead to Restart / Main Menu /
Quit (plus Keep Playing on victory). See the Victory / Defeat section below.

**Options and Credits are shared** — reachable from the main menu and the pause
menu, and Back returns to wherever you came from. They must read correctly both
on a title backdrop and over a frozen game.

**Esc is contextual.** In game it first cancels whatever is active — a building
ghost, a wall line, demolish mode, the crafting panel, campfire placement during
the opening. Only when nothing is active does it pause. That check lives in
`PauseController.ModeActive()`; anything new that binds Esc must be added there.

**Panels are only as wide as stated — their height is computed.** Each screen is
sized to its own content by `MenuBuilder.FitPanelHeight` at the end of a rebuild,
so adding a row never pushes content out through the bottom edge. The height
argument passed to `MenuBuilder.Panel` is just a starting value. Design to the
widths below and to the row rhythm; don't design to a fixed panel height.

---

## Main Menu — 460 wide, own scene

```
╔══════════════════════════════════════════════╗
║                                              ║
║               C A S T A W A Y                ║
║                C O L O N Y                   ║
║                                              ║
║             survive five nights              ║
║                                              ║
║   ┌──────────────────────────────────────┐   ║
║   │              NEW GAME                │   ║
║   └──────────────────────────────────────┘   ║
║   ┌──────────────────────────────────────┐   ║
║   │              CONTINUE                │   ║  ← disabled
║   └──────────────────────────────────────┘   ║    (no saves yet)
║   ┌──────────────────────────────────────┐   ║
║   │              OPTIONS                 │   ║
║   └──────────────────────────────────────┘   ║
║   ┌──────────────────────────────────────┐   ║
║   │              CREDITS                 │   ║
║   └──────────────────────────────────────┘   ║
║   ┌──────────────────────────────────────┐   ║
║   │                QUIT                  │   ║  ← danger colour
║   └──────────────────────────────────────┘   ║
║                                              ║
║                v0.1 · pre-alpha              ║
╚══════════════════════════════════════════════╝
```

- The stacked wordmark is a placeholder, not a decision — a logo lockup replaces
  the whole block.
- NEW GAME opens the difficulty screen below rather than starting immediately.
- Continue ships disabled until there's a save system; it needs a clearly
  "unavailable, not broken" treatment.
- Behind the panel is a flat `#0D1119` fill. Island silhouette, wreck, dusk sky —
  this is the one screen with real room to set the game's tone.

---

## Pause — 460 × 520, over the frozen game

```
╔══════════════════════════════════════════════╗
║                                              ║
║                   PAUSED                     ║
║                                              ║
║      Night 3  ·  8 workers  ·  4 warriors    ║  ← live game state
║   ────────────────────────────────────────   ║
║                                              ║
║   ┌──────────────────────────────────────┐   ║
║   │               RESUME                 │   ║
║   └──────────────────────────────────────┘   ║
║   ┌──────────────────────────────────────┐   ║
║   │               OPTIONS                │   ║
║   └──────────────────────────────────────┘   ║
║   ┌──────────────────────────────────────┐   ║
║   │              CONTROLS                │   ║
║   └──────────────────────────────────────┘   ║
║   ┌──────────────────────────────────────┐   ║
║   │               RESTART                │   ║
║   └──────────────────────────────────────┘   ║
║   ┌──────────────────────────────────────┐   ║
║   │              MAIN MENU               │   ║
║   └──────────────────────────────────────┘   ║
║   ┌──────────────────────────────────────┐   ║
║   │                QUIT                  │   ║
║   └──────────────────────────────────────┘   ║
╚══════════════════════════════════════════════╝
```

- The status line is real data (day/night, workers, warriors), so a player
  returning after a break knows where they left off. Keep it quiet.
- Backdrop is 72% opaque so the frozen world reads through — worth preserving;
  it keeps pause feeling like a layer rather than a scene change.
- Restart, Main Menu and Quit all route through the confirm dialog.

---

## New Game — 720 wide, own screen

```
╔══════════════════════════════════════════════════════════════════════╗
║                            NEW GAME                                  ║
║  ──────────────────────────────────────────────────────────────────  ║
║   Difficulty                    ‹        Hard        ›               ║
║   Bigger waves, tighter resources, and two extra nights to hold.     ║
║                                                                      ║
║   RULES ─────────────────────────────────────────────────────────    ║
║   Raid size                     1.3x                                 ║
║   Enemy health                  1.15x                                ║
║   Enemy damage                  1.2x                                 ║
║   Night length                  1.1x                                 ║
║   Starting resources            0.8x                                 ║
║   Nights to survive             7                                    ║
║                                                                      ║
║   Difficulty is locked once the run begins.                          ║
║   ┌──────────────────────────────────────────────────────────────┐   ║
║   │                            BEGIN                             │   ║
║   └──────────────────────────────────────────────────────────────┘   ║
║   ┌──────────────────────────────────────────────────────────────┐   ║
║   │                            BACK                              │   ║
║   └──────────────────────────────────────────────────────────────┘   ║
╚══════════════════════════════════════════════════════════════════════╝
```

- Six presets: Peaceful, Relaxed, **Normal** (default, all 1.0x), Hard, Brutal,
  Custom. Picking **Custom** turns the read-only rules block into six sliders.
- Showing the actual multipliers is the point — a difficulty name alone doesn't
  let a player make an informed choice. The numbers want a treatment that reads
  as data, not as settings the player is about to change.
- Values live in `Difficulty.cs`; the picked level persists as the default for
  the next run.

---

## Options — 720 wide, four tabs, shared

```
╔══════════════════════════════════════════════════════════════════════╗
║                             OPTIONS                                  ║
║  ┌───────────┐┌───────────┐┌───────────┐┌───────────┐                ║
║  │   AUDIO   ││   VIDEO   ││  CAMERA   ││ INTERFACE │                ║
║  └───────────┘└───────────┘└───────────┘└───────────┘                ║
║  ──────────────────────────────────────────────────────────────────  ║
║  ┌────────────────────────────────────────────────────────────────┐  ║
║  │ Master volume         ▐████████████████▌─────────      80%     │  ║ ← scrolls
║  │ Scales everything below it.                                    │  ║
║  │ Music                 ▐███████████▌──────────────      55%     │  ║
║  │ Sound effects         ▐████████████████████▌─────     100%     │  ║
║  │ Combat, building, gathering.                                   │  ║
║  │ Ambience              ▐██████████▌───────────────      50%     │  ║
║  │ Waves, wind, birds, and the campfire.                          │  ║
║  │ Mute when unfocused        [x]                                 │  ║
║  │ Silence the game while another window has focus.               │  ║
║  └────────────────────────────────────────────────────────────────┘  ║
║   ┌──────────────────────────────────────────────────────────────┐   ║
║   │                   CONTROLS & KEYBINDINGS                     │   ║
║   └──────────────────────────────────────────────────────────────┘   ║
║   ┌──────────────────────────────────────────────────────────────┐   ║
║   │                     RESET TO DEFAULTS                        │   ║  ← danger
║   └──────────────────────────────────────────────────────────────┘   ║
║   ┌──────────────────────────────────────────────────────────────┐   ║
║   │                            BACK                              │   ║
║   └──────────────────────────────────────────────────────────────┘   ║
╚══════════════════════════════════════════════════════════════════════╝

   VIDEO tab                         CAMERA tab              INTERFACE tab
   ──────────────────────────        ────────────────────    ─────────────────────
   Display mode ‹Borderless›         Pan speed    ▐███▌──    UI scale     ▐███▌──
   Resolution  ‹1920 x 1080›         Zoom speed   ▐███▌──    Health bars ‹Damaged›
   Quality        ‹   High   ›       Rotation     ▐███▌──    Damage numbers   [x]
   V-Sync              [x]           Edge pan          [ ]   Unit state labels[ ]
   Frame rate cap ‹Unlimited›        Invert tilt       [ ]   Build grid       [ ]
                                     Screen shake ▐███▌──    Pause unfocused  [ ]
```

- **Five widget types total:** slider with a formatted readout, checkbox,
  left/right stepper, button, and the muted description line under a row. Skin
  those and every settings row is done.
- **Every row can carry a one-line description.** It is a separate element in
  the column, not text inside the row, so it never squeezes its control. Keep to
  one line — the height is fixed (TMP can't report a wrapped height before the
  layout pass that sizes the panel).
- **Tab bodies scroll.** The region is a fixed 380px; the panel no longer grows
  with the longest tab, which is what kept the buttons on screen at UI Scale
  1.6x. Scroll position survives a rebuild.
- The active tab is currently just a darker fill — the weakest part of the
  current wireframe, and the piece most in need of a real treatment.
- Settings apply **immediately** on change, so there is no Apply/Cancel pair.
  Leaving the screen is the save point.
- Values persist to PlayerPrefs via `GameSettings.cs`.

---

## Controls — 720 wide, rebindable

```
╔══════════════════════════════════════════════════════════════════════╗
║                             CONTROLS                                 ║
║   Click a key to change it. A key already in use is taken from its   ║
║   old action.                                                        ║
║  ──────────────────────────────────────────────────────────────────  ║
║  ┌────────────────────────────────────────────────────────────────┐  ║
║  │ CAMERA ──────────────────────────────────────────────────────  │  ║ ← scrolls
║  │ Pan up                     ┌─────────┐  ┌─────────┐            │  ║
║  │                            │    W    │  │   Up    │            │  ║
║  │                            └─────────┘  └─────────┘            │  ║
║  │ Pan down                   ┌─────────┐  ┌─────────┐            │  ║
║  │                            │    S    │  │  Down   │            │  ║
║  │                            └─────────┘  └─────────┘            │  ║
║  │ Rotate left *              ┌─────────┐  ┌─────────┐            │  ║
║  │                            │    Z    │  │    —    │            │  ║
║  │                            └─────────┘  └─────────┘            │  ║
║  │ BUILDING ────────────────────────────────────────────────────  │  ║
║  │ Build mode                 ┌─────────┐  ┌─────────┐            │  ║
║  │                            │    B    │  │    —    │            │  ║
║  │ …                                                              │  ║
║  │ FIXED ───────────────────────────────────────────────────────  │  ║
║  │ Cancel / pause menu        Esc                                 │  ║
║  │ Select / place             Left mouse                          │  ║
║  │ Tilt / orbit camera        Middle mouse drag                   │  ║
║  └────────────────────────────────────────────────────────────────┘  ║
║   * marks a binding you have changed.                                ║
║   ┌──────────────────────────────────────────────────────────────┐   ║
║   │                          RESET KEYS                          │   ║  ← disabled
║   └──────────────────────────────────────────────────────────────┘   ║    if unchanged
║   ┌──────────────────────────────────────────────────────────────┐   ║
║   │                            BACK                              │   ║
║   └──────────────────────────────────────────────────────────────┘   ║
╚══════════════════════════════════════════════════════════════════════╝
```

- **Two slots per action** — a main key and an alternate. That's how WASD *and*
  the arrow keys, or Delete *and* X, both work without a special case.
- **Three states per slot:** normal, armed (waiting for a key — accent text on
  the pressed fill), and empty (`—`). Design the key names as **chips**; the
  armed state is the one that needs to read at a glance.
- Clicking a slot arms it, and the next key pressed takes it. **Esc cancels**
  rather than binding — Esc is a back-out gesture five systems consume, so it
  can never be owned by an action. Same for the debug keys (F3/F4/F6/F7) and the
  mouse buttons; those are listed under FIXED as reference, without slots.
- Binding a key already in use **takes it** from the other action, whose slot
  goes blank. That's deliberate: a modal "already taken" error mid-remap is
  worse than a row that visibly empties.
- Bindings live in `KeyBindings.cs`, which is the single source of truth — no
  script holds a `KeyCode` of its own any more.

---

## Victory / Defeat — 600 wide, one screen, two dressings

```
╔══════════════════════════════════════════════════════════╗
║                                                          ║
║                       VICTORY                            ║  ← accent gold
║             You survived the pirate raids.               ║    (DEFEAT is danger red)
║  ──────────────────────────────────────────────────────  ║
║   Nights survived                    5                   ║
║   Enemies defeated                  48                   ║
║   Colony at its peak      6 workers · 4 warriors         ║
║   Resources on hand       210W · 95F · 140S              ║
║   Difficulty                       Hard                  ║
║  ──────────────────────────────────────────────────────  ║
║   ┌──────────────────────────────────────────────────┐   ║
║   │                  KEEP PLAYING                    │   ║  ← victory only
║   └──────────────────────────────────────────────────┘   ║
║   ┌──────────────────────────────────────────────────┐   ║
║   │                    RESTART                       │   ║
║   └──────────────────────────────────────────────────┘   ║
║   ┌──────────────────────────────────────────────────┐   ║
║   │                   MAIN MENU                      │   ║
║   └──────────────────────────────────────────────────┘   ║
║   ┌──────────────────────────────────────────────────┐   ║
║   │                QUIT TO DESKTOP                   │   ║  ← danger colour
║   └──────────────────────────────────────────────────┘   ║
╚══════════════════════════════════════════════════════════╝
```

- **One screen, two dressings.** Title, subtitle and accent colour change;
  the stats block, the layout and three of the four buttons are identical.
  Defeat drops KEEP PLAYING — there is nothing to keep playing, the campfire
  being gone *is* the lose condition.
- **No confirm dialogs.** The run is already over, so none of these three can
  lose the player anything they still have.
- **This screen has no Back.** Esc does nothing on it. Dismissing it would leave
  the player looking at a frozen world with no UI — the game sits at
  `timeScale 0` and `PauseController` refuses to unpause while `isGameOver`.
- Stats use the same label/value row as the difficulty summary, so a skin for
  one covers both.
- The victory/defeat screens used to be **hand-built uGUI panels in the scene**
  (`VictoryDefeatUI`), which is why the earlier version of this document listed
  them as not matching the menu system. They now use these widgets, and
  `Tools > Island RTS > Menus > Remove Legacy Victory-Defeat Panels` deletes the
  old scene objects.

---

## Confirm (580 × 260) and Credits (580 × 520)

```
╔══════════════════════════════════════════════╗   ╔══════════════════════════════╗
║                                              ║   ║           CREDITS            ║
║      Restart? Current progress is lost.      ║   ║  ──────────────────────────  ║
║                                              ║   ║                              ║
║   ┌──────────────────────────────────────┐   ║   ║      Design & Code           ║
║   │              CONFIRM                 │   ║   ║           —                  ║
║   └──────────────────────────────────────┘   ║   ║                              ║
║   ┌──────────────────────────────────────┐   ║   ║           Art                ║
║   │               CANCEL                 │   ║   ║           —                  ║
║   └──────────────────────────────────────┘   ║   ║                              ║
╚══════════════════════════════════════════════╝   ║          Audio               ║
                                                   ║           —                  ║
  Used by: Restart · Main Menu · Quit ·            ║                              ║
           Reset settings                          ║     Built with Unity         ║
                                                   ╚══════════════════════════════╝
```

Confirm is the only screen with a destructive action, and the only place the
danger colour appears besides Quit. Cancel should be the visually safer of the
two without being hard to find.

---

## Asset checklist

| Asset | Format | Notes |
|---|---|---|
| Panel frame | 9-slice PNG | One sprite serves all seven screens, 460×260 up to 720×640 |
| Button | 9-slice PNG | Needs normal / hover / pressed / disabled |
| Checkbox | 2 sprites | Empty and checked, 28×28 at reference res |
| Slider | 2–3 sprites | Track, fill, optional handle (there's no handle today) |
| Stepper arrows | 2 sprites | Left and right; currently the glyphs `<` and `>` |
| Keycap chip | 9-slice PNG | Controls screen key slots — needs normal / hover / armed / empty |
| Scrollbar | optional | Scroll regions currently have no visible track |
| Wordmark | PNG / SVG | "Castaway Colony" — replaces the stacked-text placeholder |
| Title backdrop | Full-bleed art | 16:9, safe area for a 460px panel centred |
| Display typeface | Font file | Titles and buttons; mostly uppercase |
| Body typeface | Font file | Settings rows; legible at 15–19px |

## Placeholder palette (`MenuStyle.cs`)

| Token | Value | Used for |
|---|---|---|
| Backdrop | `#0A0D14` @ 72% | Full-screen dim behind any menu |
| Panel fill | `#1A1C24` @ 96% | Panel body |
| Panel border | `#D9B873` | 2px frame — warm gold, ties menus to the campfire |
| Text primary | `#F2EEE0` | Button labels, setting names |
| Text muted | `#9E9992` | Status line, disabled labels, footnotes |
| Text accent | `#F2CC73` | Headings, slider fill, key names |
| Danger | `#EB7361` | Quit, Confirm |

## Not built yet

- **Save/load slots** — enables Continue, and adds a screen.
- **Loading screen** — scene swaps are currently fast enough not to need one.
- **Gamepad support** — `KeyBindings` skips the joystick KeyCodes deliberately;
  a controller needs axes and a glyph set, not more keyboard slots.
- **A visible scrollbar** for the scroll regions — they work by wheel and drag
  today, with no affordance showing there is more below.
- **The in-game HUD** — resources, build bar, worker panel, crafting. A much
  bigger surface than these menus and a separate conversation.
