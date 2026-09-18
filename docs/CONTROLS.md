# Island RTS — Controls

The full control reference for the current build. Every key here is a **default**;
`KeyBindings.cs` is the single source of truth and *Options → Controls* rebinds all
of them.

**The playtest checklists that used to live in this file are gone (2026-09-10).**
They were replaced by the dev-quest loop: each feature adds a batch to
`islandrts/Assets/Resources/DevQuests.txt`, the game shows the open quests on a
PLAYTEST tracker (top-right, editor and development builds) and the full list under
**Esc → Information → DEV**, where each quest takes done / PASS / FAIL and a note.
Most quests tick and pass themselves from a signal raised at the point of effect;
only looks, feel and "nothing went wrong over time" are ticked by hand. SUBMIT REPORT
writes `Playtests/playtest_<date>.md` at the repo root. A batch is deleted once its
report is in. The old checklists are in this file's git history if you ever want them.

---

## Controls

Every key below is a **default**, not a fixed binding. `KeyBindings.cs` owns the
whole map and *Options → Controls & Keybindings* rebinds it, with two slots per
action (main and alternate). No script holds a `KeyCode` of its own any more, so
this table and the in-game Controls screen are generated from the same source.

**The exceptions are deliberately not rebindable:** Esc (a back-out gesture five
systems consume in order — see `PauseController.ModeActive`), the mouse buttons,
and the debug keys F3 / F4 / F6 / F7.

### Camera — `CameraController.cs`

| Input | Action |
|---|---|
| W A S D / Arrows | Pan (speed scales with zoom, smoothed) |
| Q / E | Rotate left / right |
| Mouse wheel | Zoom (orthographic, eased) |
| Middle mouse drag | Free-look — vertical tilts 30°–60°, horizontal rotates, orbiting the view-center ground point |

### Build mode — `BuildPlacement.cs` + helpers

| Input | Action |
|---|---|
| B | Enter build mode |
| 1 | Hut |
| 2 | Wooden Wall |
| 3 | Stone Wall |
| 4 | Watchtower |
| 5 | Workshop |
| 6 | Shipyard (beach only, after Shipwright) |
| 7 | Storehouse (after Storage Pits) |
| Left click | Place (walls: click-start → click-end line) |
| R | Rotate building / toggle L-path direction in wall mode |
| Shift (hold) | Bresenham staircase wall path instead of L-path |
| G | Convert the hovered wall into a gate (costs 5 wood) |
| Esc / Right click | Cancel |
| Delete or X | Demolish mode (50% refund; campfire protected) |

### Opening sequence — `GameStartController.cs`

| Input | Action |
|---|---|
| Name popup | Names your character (Enter or BEGIN confirms; no Esc — the run cannot start unnamed) |
| Right click | Smart command for your character, for the whole run: on a stick / stone / crate → fetch it; on a bush → pick it by hand; on a tree, rock or ore boulder → work it, once the matching tool is in hand; on the campfire → deposit everything and work its queue, without opening the panel; on the ground beside the fire → walk there, emptying your hands as you pass the fire (just a walk with nothing to drop); on a Workshop → work its queue; on a Storehouse → walk there and empty your hands into the colony store; on a construction site → walk over and build it, the same as a colonist; anywhere else → walk there. A green ring marks the click and a trail shows the path |
| Shift + Right click | Queue the order behind the current one (rebindable, "Character" group). Orders run one after another; the label under your name counts the rest; a plain right-click clears the queue |
| Shift + Left click (build mode) | Place the building and keep the ghost, so the next click places another |
| Space | Centre the camera on your character |
| Left-click / drag the minimap | Centre the camera there (the top-right map; right-click on it does nothing) |
| B | Show the campfire ghost (free, one-time; must be ≤6u from your character, on buildable ground) |
| Left click | Place the campfire |
| Esc / Right click | Cancel placement |

### UI & debug

| Input | Action |
|---|---|
| Left-click campfire | Campfire panel: Colonists · Stockpile · Craft · Research · Queue tabs. Left-click is the only gesture that opens it; a right-click deposit never does |
| Click workshop | Crafting panel (Esc closes) |
| Space | Centre the camera on your character (rebindable, "Character" group) |
| F5 / F8 / F9 | Militia stance: Defensive / Offensive / Follow (rebindable, "Militia" group). The same three buttons, plus the formation buttons, sit in the bottom-right box once you have a warrior |
| F10 | Ring the bell: one colonist per spare weapon in stock arms and stays armed; press again to stand them down (rebindable, "Militia" group). Also the Bell row on the Defence section and the bottom button of the militia box, which shows once you have a warrior or a spare weapon |
| Click a section header | Campfire panel, Colonists tab: Jobs / Specialists / Priorities / Defence fold and unfold (remembered) |
| Esc | Cancels the active mode; pauses when nothing is active (not rebindable) |
| F2 | Build grid overlay — also auto-shows while build mode is active |
| F3 | AI debug overlay (editor only) |
| F4 | Debug cheat menu (editor + development builds only) |

> **Historical note:** the grid used to be on **G**, which collided with build
> mode's wall→gate conversion — both handlers fired on the same frame. Keep the
> grid off G.

---

## Debug tools

### F3 — AI Debug Overlay (right side of screen)

Toggle on, then **click any unit**. Shows:

- the unit's current action
- every action's score, with `▶` marking the active one
- the individual **consideration** scores for the active action — this is how you
  find the one near-zero consideration that's killing an action, since all
  considerations are multiplicative
- recent action history — catches flip-flopping and momentum lock

### F4 — Debug Menu (left side, IMGUI)

- **Status** — day / time / phase / timescale, difficulty, the calendar line
  (day N of M, tonight's verdict and planned raid size, raids so far, current
  prosperity), population vs housing, warrior and enemy counts
- **Resources** — +100 / +1000 per type, +1000 all, zero all
- **Quick-Start Colony** — steppers for huts / wood / food / stone workers /
  warriors (defaults 2 / 4 / 2 / 1 / 3). One button grants +1000 of each resource,
  force-finishes the intro if it's running, rings huts around the campfire, then
  assigns workers and recruits warriors. Fastest route into a working base for
  testing anything that isn't the opening sequence.
- **Time** — Skip to Night (`t=0.76`) / Skip to Day (`t=0.26`), clock-pause toggle,
  day counter −1 / +1 / +5, **Raid tonight** toggle (forces or cancels the dawn
  roll's verdict; a forced raid is sized as a real one would be),
  1× / 2× / 4× timescale (disabled on game over so it can't fight the pause)
- **Cheats** — Spawn Raid Now (today's size; disabled until a campfire exists),
  Kill All Enemies (runs the real death path so stats count), Heal Everything
  Friendly, Research Everything (the whole tech tree at once), +10 Sticks &
  Stones and +5 Wooden Spears into the stockpile, Knock Out Player, Finish All
  Construction

Both overlays are wrapped in `#if UNITY_EDITOR || DEVELOPMENT_BUILD`; release
builds ship without them.

---

## Editor setup

One menu item does all of it, and its order is load-bearing:

**`Tools > Island RTS > Setup Everything (In Order)`**

It is idempotent. Re-run it after pulling anything that touched art, prefabs or scene
wiring. It leaves `MainMenu` open, which is what a build starts on;
`Tools > Island RTS > Open Game Scene (MainIsland)` jumps straight to gameplay.

The game scene is `Assets/MainIsland.unity`. `Assets/Scenes/SampleScene.unity` is the
leftover stock Unity scene and is not in the build.
