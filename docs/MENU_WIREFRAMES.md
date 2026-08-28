# Menu Wireframes

Every menu screen in the game as a text wireframe. These aren't mockups waiting
to be built — they're what the game renders today, in placeholder form.

All seven screens are constructed at runtime by `MenuScreens.cs` from the
vocabulary in `MenuBuilder.cs`. Every colour, size and spacing value lives in
**`MenuStyle.cs`** — one file restyles all seven. Keep this document in sync
with `MenuScreens.cs`; it's what an artist works from.

> A formatted version of this document, with the asset checklist and palette
> swatches, is published as an artifact for sharing with an artist.

---

## Navigation

```
    ┌─────────────┐                        ┌─────────────┐
    │  MAIN MENU  │  ── New Game ────────▶ │  GAMEPLAY   │
    └──────┬──────┘                        └──────┬──────┘
           │                                      │  Esc
           │                                      ▼
           │                               ┌─────────────┐
           │                               │    PAUSE    │ ◀── game frozen
           │                               └──────┬──────┘
           │                                      │
           ├──────────────┬───────────────────────┤
           ▼              ▼                       ▼
    ┌─────────────┐ ┌─────────────┐        ┌─────────────┐
    │   OPTIONS   │ │   CREDITS   │        │  CONTROLS   │
    │  ┌───────┐  │ └─────────────┘        └─────────────┘
    │  │ AUDIO │  │
    │  │ VIDEO │  │        Destructive actions route through:
    │  │ PLAY  │  │        ┌─────────────┐
    │  └───────┘  │        │   CONFIRM   │  Restart · Main Menu · Quit
    └─────────────┘        └─────────────┘
```

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

## Options — 720 wide, three tabs, shared

```
╔══════════════════════════════════════════════════════════════════════╗
║                             OPTIONS                                  ║
║  ┌────────────────┐┌────────────────┐┌────────────────┐              ║
║  │     AUDIO      ││     VIDEO      ││    GAMEPLAY    │              ║
║  └────────────────┘└────────────────┘└────────────────┘              ║
║  ──────────────────────────────────────────────────────────────────  ║
║                                                                      ║
║   Master volume        ▐████████████████▌──────────────       80%    ║
║   Music                ▐███████████▌───────────────────       55%    ║
║   Sound effects        ▐████████████████████▌──────────      100%    ║
║   Ambience             ▐██████████▌────────────────────       50%    ║
║                                                                      ║
║   ┌──────────────────────────────────────────────────────────────┐   ║
║   │                     RESET TO DEFAULTS                        │   ║
║   └──────────────────────────────────────────────────────────────┘   ║
║   ┌──────────────────────────────────────────────────────────────┐   ║
║   │                            BACK                              │   ║
║   └──────────────────────────────────────────────────────────────┘   ║
╚══════════════════════════════════════════════════════════════════════╝

   VIDEO tab                              GAMEPLAY tab
   ─────────────────────────────          ─────────────────────────────
   Fullscreen           [x]               Camera speed   ▐██████▌───
   V-Sync               [x]               Edge pan             [ ]
   Quality       ‹  High  ›               Screen shake         [x]
                                          Damage numbers       [x]
   Resolution: placeholder,               Show build grid      [ ]
   needs a real mode list.
```

- **Four widget types total:** slider with % readout, checkbox, left/right
  stepper, button. Skin those and every settings row is done.
- The active tab is currently just a darker fill — the weakest part of the
  current wireframe, and the piece most in need of a real treatment.
- Settings apply **immediately** on change, so there is no Apply/Cancel pair.
- Values persist to PlayerPrefs via `GameSettings.cs`.

---

## Controls — 720 wide, read-only for now

```
╔══════════════════════════════════════════════════════════════════════╗
║                             CONTROLS                                 ║
║  ──────────────────────────────────────────────────────────────────  ║
║                                                                      ║
║   Pan camera                    W A S D  /  Arrows                   ║
║   Rotate camera                 Q  /  E                              ║
║   Zoom                          Mouse wheel                          ║
║   Tilt / orbit                  Middle mouse drag                    ║
║   Build mode                    B                                    ║
║   Select building               1 - 5                                ║
║   Wall to gate                  G                                    ║
║   Rotate / path flip            R                                    ║
║   Staircase walls               Shift                                ║
║   Demolish                      Delete  /  X                         ║
║   Build grid                    F2                                   ║
║   Pause menu                    Esc                                  ║
║                                                                      ║
║   Rebinding is not implemented yet.                                  ║
║   ┌──────────────────────────────────────────────────────────────┐   ║
║   │                            BACK                              │   ║
║   └──────────────────────────────────────────────────────────────┘   ║
╚══════════════════════════════════════════════════════════════════════╝
```

Design the key names as **chips, not text**. This list will eventually gain
clickable rebinding fields, so a keycap treatment now makes the interactive
version a state change rather than a redesign.

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

- **Victory / defeat screens** exist but predate this system and don't match it;
  they should be rebuilt on the same widgets.
- **Save/load slots** — enables Continue, and adds a screen.
- **Key rebinding** — turns the Controls list interactive.
- **Loading screen** — scene swaps are currently fast enough not to need one.
- **The in-game HUD** — resources, build bar, worker panel, crafting. A much
  bigger surface than these menus and a separate conversation.
