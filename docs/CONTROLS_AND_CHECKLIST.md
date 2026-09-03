# Island RTS — Controls & Playtest Checklist

Living reference for the current build. Last updated **2026-08-26**.

Everything below the Controls section is **unverified in Play mode** — see
[Status](#status) for why.

---

## Status

A long run of implemented-but-never-run work has stacked up. All of it is
compile-verified against Unity 6000.5.9f1 (0 errors), and none of it has been
exercised in Play mode:

| Work | State |
|---|---|
| Phase 6.24 (queued refactors) | Committed `ae8f632`, unplaytested |
| Unity 6000.0.25f1 → 6000.5.9f1 upgrade | Committed `3d23486`, never run in Play |
| Phase 6.25 (targeting unification + worker spacing) | Unplaytested |
| Phase 6.26 (worker crowd interaction) | Unplaytested |
| Phase 10 Stage 2a/2b/2c (low-poly art plumbing) | Needs editor menu runs |
| Opening Sequence Stage 1 (survivor landing) | Needs editor menu run |
| Terrain T1 (shaped island) + T2 flatten | Needs editor menu run |
| Session 2026-08-26 (150×150 island, node carving, garrison flee, pickups, workshop) | Committed `46e3e91`, needs editor runs |

⚠️ **`EditorBuildSettings.asset` still lists only `Assets/Scenes/SampleScene.unity`.**
A build made right now ships the empty stock scene. Fix in *File > Build Profiles*
(add `MainIsland`, remove `SampleScene`) before building anything.

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
| Right click | Smart command for your character, for the whole run: on a stick / stone / crate → fetch it; on the campfire → deposit everything and open the panel; anywhere else → walk there |
| Space | Centre the camera on your character |
| B | Show the campfire ghost (free, one-time; must be ≤6u from your character, on buildable ground) |
| Left click | Place the campfire |
| Esc / Right click | Cancel placement |

### UI & debug

| Input | Action |
|---|---|
| Click campfire | Campfire panel: Colonists · Stockpile · Craft tabs |
| Click workshop | Crafting panel (Esc closes) |
| Space | Centre the camera on your character (rebindable, "Character" group) |
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

- **Status** — day / time / phase / timescale, population vs housing, warrior and
  enemy counts
- **Resources** — +100 / +1000 per type, +1000 all, zero all
- **Quick-Start Colony** — steppers for huts / wood / food / stone workers /
  warriors (defaults 2 / 4 / 2 / 1 / 3). One button grants +1000 of each resource,
  force-finishes the intro if it's running, rings huts around the campfire, then
  assigns workers and recruits warriors. Fastest route into a working base for
  testing anything that isn't the opening sequence.
- **Time** — Skip to Night (`t=0.76`) / Skip to Day (`t=0.26`), clock-pause toggle,
  1× / 2× / 4× timescale (disabled on game over so it can't fight the pause)
- **Cheats** — Spawn Enemy Wave (disabled until a campfire exists), Kill All
  Enemies (runs the real death path so stats count), Heal Everything Friendly,
  Finish All Construction

Both overlays are wrapped in `#if UNITY_EDITOR || DEVELOPMENT_BUILD`; release
builds ship without them.

---

## Editor setup — run in this exact order

**Nothing from the recent sessions exists in the scene until these run**, and the
order matters: later tools consume earlier outputs.

1. `Tools > Island RTS > Low-Poly Templates > Generate All Assets`
2. `Tools > Island RTS > Low-Poly Templates > Plumb Everything`
3. `Tools > Island RTS > Opening Sequence > Setup Opening Scene`
4. `Tools > Island RTS > Low-Poly Templates > Build Scatter Settings`
5. `Tools > Island RTS > Terrain > Setup Terrain Scene`
6. `Tools > Island RTS > Session Content > Setup Pickups + Workshop`
7. Save the scene, then Play.

Scene: `Assets/MainIsland.unity` — **not** `Assets/Scenes/SampleScene.unity`, which
is the leftover stock Unity scene.

---

## Playtest checklist

Grouped by what breaks if it's wrong.

### Opening & terrain foundation

- [ ] Console shows one `TerrainGrid: island generated (seed …) + NavMesh built in … ms` line, and no errors
- [ ] World reads as a 150×150 island: beach ring, rolling green interior, rock facets on steep faces, ocean to the horizon
- [ ] Survivor starts wading in the west cove; right-click walks him up the beach ramp
- [ ] Day/night clock stays frozen at dawn until the campfire is placed
- [ ] Campfire ghost is red in water / on slopes / on a resource node, green on clear flats near the survivor
- [ ] Placing it: placement sound → survivor walks to the fire → despawns → a wood worker spawns (pop 1/3) → clock starts → hints fade
- [ ] `skipIntro` on `GameStart` still gives the classic instant-campfire start with no regressions

### Movement & AI — the riskiest bucket

Covers Phase 6.24's rerouting of worker executors through `AINavHelper` and the
2026-08-26 switch to carving resource nodes.

- [ ] Gather → return → deliver loop completes; carry count hits 0 *next to* the fire, not 2m away and not after a 3s fallback pause
- [ ] Workers path **around** trees and rocks at full speed — no rubbing slowdown
- [ ] Enemies chasing warriors never wedge behind a trunk
- [ ] ~6 workers pack around a tree without orbiting or shoving; a bumped gatherer springs back to its spot
- [ ] A worker walled off from a node re-targets a different one within ~1s
- [ ] Flee = garrison: workers sprint into a hut and vanish; they pop out when enemies die, and immediately if the hut is destroyed mid-hide
- [ ] Warriors engage and disengage cleanly at range; Retreat / Intercept / DefendWall all start moving immediately when chosen
- [ ] Warriors heal at the campfire between waves and stop cleanly at full HP

### Building

- [ ] Wall lines work: L-path, Shift staircase, R toggle
- [ ] G converts a hovered wall to a gate — **and no longer also toggles the grid**
- [ ] Demolish mode (Delete / X) refunds 50%; campfire is protected
- [ ] Buildings flatten a pad and sit flush on slopes; ghost height matches placed height
- [ ] Red no-build lines drape over the hills instead of cutting through them
- [ ] F2 grid: draws only on buildable land, drapes over the terrain, is not visible under the island or out over water
- [ ] Grid cells line up with where buildings actually snap
- [ ] Grid auto-appears on B and hides on cancel; F2 still forces it on/off outside build mode

### New content (2026-08-26)

- [ ] Wood and stone workers detour to nearby sticks/stones and deliver them
- [ ] Key 5 places the Workshop; clicking it opens the crafting panel
- [ ] Each recipe crafts once and its effect visibly applies (gather rate, build speed, warrior damage)
- [ ] Night wave spawns at the shore ring (distance 45) and will chew the Workshop like a hut
- [ ] Forests mix five tree silhouettes and shades, and read taller

### Visual & engine upgrade

- [ ] Post-processing reads correctly — no blown-out vignette or tonemap shift from URP 17.5
- [ ] Campfire shows exactly one flame and still blooms
- [ ] Day/night `LightingPreset` lerp drives sun + ambient; night reads as cool blue moonlight with soft shadows
- [ ] Hover highlight tints the *whole* building or node, not one panel
- [ ] Death fade covers the whole body
- [ ] TextMeshPro UI renders correctly (uGUI 2.0 → 2.5 was the largest package jump)
- [ ] ~60 fps held

### Debug tools

- [ ] F4 quick-start colony lands a full working base on the big island
- [ ] 4× timescale doesn't visibly starve AI evaluation (eval throttles are frame-based)
- [ ] Kill All Enemies clears the wave and the stats count the kills
- [ ] Restart (defeat → restart button) replays the intro cleanly and regenerates the identical island
- [ ] F4 status shows the run's difficulty, and it matches what was picked on New Game

### Island generator v2 — random islands, terraces, cliffs, ponds (2026-09-01)

Before playing: re-run `Setup Everything (In Order)` (or steps 1, 4 and 5 above — the
terrain step creates `Assets/Settings/IslandSettings.asset` + `ScatterSettings.asset`,
deletes the old `_LowPolyScatter` scene decor, and wires seven band materials).
`Tools > Island RTS > Terrain > Preview Island Seeds` renders twelve seeds to PNG without
Play mode — use it to tune the asset.

- [ ] Console: one `TerrainGrid: island seed N (attempt 1, 0 ramp(s) carved, …% of land reachable, … buildable cells) + NavMesh built in … ms` line, no errors, load under ~1 s
- [ ] NEW GAME twice from the menu gives two different islands; defeat → Restart gives the SAME island (F4 shows the seed)
- [ ] Island fills the map: coast between ~50 and ~65 m out, a bay on the west where the wreck sits, no land clipped at the map edge
- [ ] Plateaus read as stepped terraces with rock rims; cliff faces are dark rock, gentler slopes mid rock; beach line is ragged (not a contour ring), wet sand under the waterline, grass in three tones
- [ ] Units cannot walk up a cliff face but CAN reach every plateau via a ramp or slope somewhere along its edge; nothing paths into a pond
- [ ] Buildings cannot be placed on a cliff face, in a pond, or on an unreachable outcrop (F2 grid leaves those cells blank); plateau tops are the easy build sites
- [ ] Survivor wades ashore at the cove and the walk to the campfire site never meets a cliff (guaranteed corridor)
- [ ] Palms hug the beaches, flotsam sits in the wading band, rocks cluster at cliff feet, ferns prefer dark grass; nothing in the campfire clearing or on the wreck; props reappear on Restart
- [ ] Night wave spawns on reachable land (never in the sea on the island's short axis) and paths in
- [ ] Balance sim (`run-sim.ps1`) still uses the fixed inspector seed unless `terrainSeed` is set

### World options, metal, HUD, water (2026-09-01 evening)

Before playing: re-run `Setup Everything (In Order)`. Watch the console for shader errors
from `StylizedWater` (a magenta sea means the shader failed to import; the setup falls
back to a blue Lit material only if the shader is missing entirely).

**New Game screen**
- [ ] A "World" section shows Island size (Small / Medium / Large), Terrain (Rolling / Terraced / Rugged) and a Seed field; the blurb under each stepper changes with the choice
- [ ] Typing a seed (number or word) and pressing BEGIN twice gives the same island both times; clearing it gives a new island each time
- [ ] Small island: coast noticeably closer, survivor still starts at the cove beside the wreck, night wave still arrives from land; Large: the reverse, load still under ~1 s
- [ ] Rolling: few cliffs, almost everywhere buildable; Rugged: many levels, long cliff lines, rocky shores — but every plateau still has a way up
- [ ] Pause menu status line shows the difficulty, island size · style and the seed

**Terrain feel**
- [ ] Landforms are broad and gradual; plateaus read as a few large stepped levels with rock rims, not scattered bumps
- [ ] Bands flow: a hillside is one continuous band, no rock/grass checkerboard; valleys and low ground run dark green, plateau tops dry; the beach line is only slightly ragged
- [ ] Noticeably fewer props than before; palms still on beaches, rocks at cliff feet

**Nodes**
- [ ] Trees form a handful of large forests in the low, dark ground plus a few loners; berry bushes on the light meadows; rock nodes on high or broken ground; ore nodes (dark rock with bright veins) up on the plateaus and at cliff feet
- [ ] Nodes have room between them; nothing spawns in the campfire clearing; all four types present on a Rolling island too (the habitat relaxes)
- [ ] Depleted ore respawns as ore

**Metal**
- [ ] HUD is ONE continuous bar top-left with five equal-width entries (wood, food, stone, metal, housing) separated by thin vertical rules — no boxes around individual entries
- [ ] Every entry is exactly two lines: big bold amount on top, `Wood · N workers` on one unbroken line under it (check at 12 workers and at UI Scale max — it must never wrap to a third line); both lines start at the same left edge and the tops/bottoms line up across all five
- [ ] The worker count reads smaller and dimmer than the amount; hovering an entry lights it faintly; clicking any entry opens the campfire panel
- [ ] Campfire panel opens at the bottom-left; dragging its CAMPFIRE title bar moves it, it cannot be pushed off any screen edge, the X still closes it, and the position survives a restart and a scene reload
- [ ] Campfire panel: four job rows with −/+ and live counts, a warriors row with its cost, a housing line; + greys out when housing is full, warrior + greys out when unaffordable; Esc and X close it; Esc does NOT open the pause menu while it is open
- [ ] Assign a miner: they walk to an ore node, chip at it (stone sound), deliver, and the metal chip climbs; F4 shows a Metal row and +1000 all includes metal
- [ ] Unassigning a miner removes exactly one and the housing count drops by one
- [ ] Game-over stats line shows metal on hand

**Water**
- [ ] Sea is turquoise in the shallows fading to deep blue, with a foam line along the shore that drifts; gentle waves visible against the beach; night tints it dark blue
- [ ] Facets: the sea has a faint per-triangle tone jitter that drifts slowly, and single facets flash as small sun glints (a few percent of them at a time, none at night)
- [ ] Rotate (Q/E) and tilt (middle-drag) through every angle at several zooms: the sea never flips light all at once, never shows scattered dark tiles, and never shows big diagonal stripes (the 2026-09-01 ortho-specular / wave-aliasing bug)
- [ ] No banding or garbage along the bottom of the screen when zoomed out and tilted low (the ortho negative-near-clip case)
- [ ] Ponds show the same shallow colour and foam ring


### Trees, occlusion fade & the metal node (2026-09-02)

Run `Setup Everything (In Order)` first: the metal node mesh is regenerated in step 1, remounted in step 2, and step 4 rewrites the scatter table so palms come back gatherable.

**Seeing units behind trees**
- [ ] Walk a worker behind a tree: the tree fades to roughly 30% and comes back the moment the worker clears it
- [ ] The fade is smooth both ways, with no pop and no tree left stuck transparent after the unit dies or garrisons
- [ ] Several units behind one tree, and one unit behind several trees, both behave
- [ ] Units in FRONT of a tree do not fade it
- [ ] Hovering a faded tree still highlights the whole tree, and the highlight clears correctly afterwards
- [ ] Enemies at night fade trees too, so a wave is never invisible in a forest
- [ ] During the opening, the survivor fades palms on the beach
- [ ] Frame rate is unchanged in a dense forest at full zoom-out

**Gatherable palms**
- [ ] Every tree on the island can be chopped, palms included
- [ ] A palm shrinks as it depletes and disappears when emptied
- [ ] Workers path around palms rather than through them, and stand in a ring to chop as they do at broadleaf trees
- [ ] Palms carry less wood than broadleaf trees, and young palms least
- [ ] No palms inside the campfire clearing or on unreachable outcrops
- [ ] Clicking a palm highlights it; the shake pulse plays on the chop beat

**Metal node**
- [ ] Ore nodes are plain boulders with no crystals or bright veins
- [ ] Stone nodes still have their crystals, so the two are told apart at a glance
- [ ] Miners still gather metal from them and the HUD metal count rises
### Settings, keybinding & difficulty (2026-08-30)

**Options — all four tabs**

- [ ] Every row's description line sits under its control and doesn't clip or overlap
- [ ] Tab bodies scroll by wheel and drag; switching tabs and coming back keeps the scroll position
- [ ] Sliders read out in their own units — `80%` for volumes, `1.25x` for camera and shake, whole numbers for the frame cap
- [ ] Audio sliders change the mix live; Master scales the others
- [ ] Mute when unfocused: alt-tab away silences the game, coming back restores the exact volume (not full)
- [ ] Video: display mode and resolution apply in a **built** game (the editor ignores them by design and says so)
- [ ] V-Sync on greys the frame-cap description to "Ignored while V-Sync is on"; off, a cap actually limits FPS
- [ ] Quality stepper doesn't hitch the game per frame while stepping (change-guarded in `Apply`)
- [ ] Camera: pan / zoom / rotation sliders visibly change feel; Invert tilt flips middle-mouse drag
- [ ] Screen shake at `0x` is completely still; at `1.5x` noticeably heavier than default
- [ ] Interface: UI scale resizes menus as the slider drags, at both extremes buttons stay on screen
- [ ] Health bars — Always / When damaged / Never all take effect **immediately** on units already alive
- [ ] Unit state labels toggle on units already on the field, not just newly spawned ones
- [ ] Pause when unfocused opens the pause menu on alt-tab (and does nothing in the menu scene)
- [ ] RESET TO DEFAULTS restores settings *and* keybindings, and the game still plays
- [ ] Every setting survives quitting and relaunching

**Controls screen**

- [ ] Clicking a slot highlights it and the hint changes to "Press any key… (Esc cancels)"
- [ ] The next key pressed binds; Esc cancels without binding; F3/F4/F6/F7 and mouse buttons are refused
- [ ] Binding a key that's already used blanks it on the old action (and promotes its alternate into the empty main slot)
- [ ] Changed rows show `*`; RESET KEYS is disabled until something is changed
- [ ] Rebinding a key **actually works in game** — rebind Build Mode off B and confirm the new key opens build mode and B does not
- [ ] Pan rebinds work: WASD is no longer read through the fixed input axes
- [ ] Scroll position is preserved after each rebind (the clicked row doesn't jump off screen)
- [ ] Bindings persist across a relaunch

**Difficulty**

- [ ] NEW GAME opens the difficulty screen; Normal is all `1.0x` / 5 nights
- [ ] Custom turns the rules block into six working sliders
- [ ] Starting resources scale (Peaceful ≈ 150 wood, Brutal ≈ 60)
- [ ] Wave sizes visibly differ between Peaceful and Brutal on night 1
- [ ] Nights to survive matches the preset (Hard = 7, Brutal = 10) in the victory message
- [ ] Nights are longer on Hard/Brutal; **days are not**
- [ ] The pause menu shows the difficulty as locked, and Options has no way to change it mid-run
- [ ] RESTART from the pause menu keeps the same difficulty even after changing the menu selection

**Victory / defeat screens (2026-08-31)**

- [ ] Losing the campfire brings up DEFEAT in the menu style, not the old scene panel
- [ ] Surviving the required nights brings up VICTORY in accent gold
- [ ] Stats are right: nights survived (defeat shows one fewer — the night you lost doesn't count), enemies defeated, peak workers/warriors, resources on hand, difficulty
- [ ] **RESTART** replays the scene at the same difficulty
- [ ] **MAIN MENU** returns to the title screen with the game unfrozen (this never worked before)
- [ ] **QUIT TO DESKTOP** exits the build / stops Play in the editor
- [ ] **KEEP PLAYING** (victory only) closes the screen and the game actually resumes — not frozen
- [ ] Keep Playing then losing later still brings up DEFEAT correctly
- [ ] Esc does nothing on the end screen (it must not be dismissable)
- [ ] Defeat has no Keep Playing button
- [ ] After running `Tools > Island RTS > Menus > Remove Legacy Victory-Defeat Panels`, the scene Canvas has no VictoryScreen/DefeatScreen children and no missing-script warnings

**Balance harness (regression — difficulty must not leak into sweeps)**

- [ ] A sweep run reports the same numbers with the developer's difficulty set to Brutal as to Normal
- [ ] `nightsToSurvive` in `runs.csv` matches the sweep config, not the difficulty preset
- [ ] The `startingWood` / `startingFood` / `startingStone` knobs now actually take effect (they were silently ignored before — `ResourceManager` reads them in `Awake`)

**Salvage crates and barrels (2026-09-02)**

- [ ] The shipwreck at the landing site has two crates and a barrel, and the shore has more of both
- [ ] A food worker walks to a nearby crate and delivers 6 food to the campfire, even though a worker carries 5
- [ ] A wood worker does the same with a barrel (5 wood)
- [ ] Workers ignore salvage while enemies are near, the same way they stop gathering
- [ ] Two workers never chase the same crate
- [ ] A collected crate does not come back; sticks and stones still trickle in at the usual rate
- [ ] No crate or barrel is stranded in water or on ground no worker can reach
- [ ] Driftwood logs are still scenery

### Colonist pool, arrivals, builders (2026-09-02)

**Arrivals and housing**

- [ ] After placing the campfire your character stays under your control (they are **not** a colonist); the first colonist lands at the cove ~20s later
- [ ] The campfire panel opens with a "Colonists" line (`1 colonist · 1 idle · next survivor in Ns`) and a countdown that ticks down
- [ ] Every ~20s by day a survivor lands at the cove by the wreck and walks to the campfire, until the campfire's 3 slots are full
- [ ] Nobody lands at night; the countdown reads "survivors land by day" and resumes at dawn
- [ ] Finishing a hut restarts arrivals; the new arrivals walk to **that hut** and wait beside it
- [ ] Losing a hut makes its residents homeless (HUD housing chip goes red) and they move into the next free slot when one appears
- [ ] F4 "Land a Survivor" drops one at the cove immediately; it is greyed out when housing is full

**Jobs (assignment moves people, never creates them)**

- [ ] `+` on a job row turns the nearest idle colonist into that worker — nobody spawns, the idle count drops by one
- [ ] `+` is greyed out when nobody is idle; `−` sends a worker back to idle without destroying them
- [ ] Changing a worker's job while they carry something: they deliver what they hold first, then gather the new type (HUD credits the right resource)
- [ ] Warrior `+` converts an idle colonist where they stand (needs 10W 15F **and** an idle colonist); `−` dismisses one back to idle, no refund
- [ ] A killed warrior frees a housing slot and a new survivor lands to replace them
- [ ] Peak-colony stat and the HUD housing chip count everyone: workers, idle colonists and warriors

**Builders (construction needs labour now)**

- [ ] A placed site shows "Awaiting builder" until an idle colonist reaches it, then "Building…" and the HP climbs
- [ ] With zero idle colonists a site waits forever; unassigning a worker gets it built
- [ ] One builder finishes a hut in ~10s; three on one site is roughly 3x faster, a fourth goes to a different site
- [ ] Builders stand at the site edge as "furniture" and walk back if shoved off
- [ ] Enemies nearby make builders flee like workers; they return when the threat clears
- [ ] Sturdy Scaffolds still speeds construction
- [ ] F4 "Finish All Construction" still completes sites instantly
- [ ] Wall lines get built segment by segment as colonists reach them (walls carve — check no builder wedges inside a ring)

**Repair**

- [ ] A damaged wall/hut/tower/workshop/campfire is repaired by an idle colonist at ~5 HP/s once nothing is left to build
- [ ] Repair charges about a quarter of the build cost spread over the repair (a 15W wall repaired from near-zero costs ~4W); the HUD ticks down a unit at a time
- [ ] With no resources the colonist stands at the building reading "Repairing (no materials)" and resumes when resources arrive
- [ ] Repair stops when enemies are near and resumes after

**Quick-start and sim**

- [ ] F4 quick-start lands the colonists beside the fire, arms the warriors, then hands out jobs — no one walks in from the cove
- [ ] A sweep still runs: policies keep one idle colonist as a builder whenever sites exist (sweeps from before this change are not comparable)

### Player character & name popup (2026-09-02 — Slice A of the crafting plan)

**Name popup**

- [ ] A new game opens on a "WASHED ASHORE" popup over the frozen cove; the field is pre-filled with the last name used (first run: "Castaway")
- [ ] Enter and BEGIN both confirm; Esc does nothing on it; the clock stays frozen and right-click does not move the character while it is open
- [ ] Whitespace-only or empty confirms as "Castaway"; the name is capped at 16 characters
- [ ] Restart (pause menu / game over) does NOT ask again; NEW GAME from the main menu does, pre-filled with the last name
- [ ] The intro hint greets you by name, and so does the "fire is lit" hint

**Your character**

- [ ] The character has a red bandana and blue sash (not a straw hat) and a gold name label that stays visible with "Unit state labels" off
- [ ] Right-click moves them during the opening and for the whole run afterwards; clicks in the shallows land on the nearest walkable spot
- [ ] Space centres the camera on them, including on a hill (they land mid-screen, not downhill of it); the key shows on the Controls screen under "Character" and can be rebound
- [ ] Placing the campfire sends them to the fire; they stay yours — the campfire panel reads 0 colonists until the first survivor lands
- [ ] They are never given a job, never counted in housing, and enemies walk past them
- [ ] F4 "Kill all enemies" / "Spawn wave" behave as before; the character is not in any target list
- [ ] Health bar sits just above the head; standing within ~3u of the fire regenerates ~2 HP/s
- [ ] Knock-out (F4 or a stray hit): body vanishes, label reads "Knocked out", 10s later they stand beside the fire at full HP with the label back to the name; no defeat
- [ ] A tree between the camera and the character fades exactly as it does for a worker, and un-fades while they are knocked out

**Classic start, quick-start, sim**

- [ ] `skipIntro` ticked: no popup, campfire at the origin, the character standing beside it under the last-used name
- [ ] F4 quick-start from inside the popup dismisses it, uses the last name, and lands the colony as before
- [ ] A sweep completes with no popup and no PlayerPrefs write (last-used name unchanged afterwards); note it now starts with 0 colonists, not 1

### Hands, stockpile & campfire crafting (2026-09-02 — Slices B and C of the crafting plan)

**Collecting by hand**

- [ ] The landing beach around the wreck has ~8 sticks and ~5 stone chunks on top of the island-wide scatter
- [ ] Right-click a stick: the character walks over, it vanishes, the HUD strip at the bottom shows `St 1`; the label flashes "+1 Stick"
- [ ] Sticks stack to 10 per slot, stone chunks likewise; the six slots fill left to right
- [ ] Right-click a crate: `F 6` in hand (six food), a barrel gives `W 5`; right-click the fire → the HUD resource bar rises by exactly that
- [ ] With every slot full the label reads "Hands full — deposit at the fire" and the pickup stays on the ground
- [ ] A worker never steals a pickup the character is walking to, and the character never takes one a worker is heading for ("Someone is fetching that")
- [ ] Placement ghosts never sit on top of a stick (pickup colliders are on the Pickups layer); hovering a stick does not highlight anything
- [ ] Right-click a pickup on the ragged shore edge the NavMesh cannot quite reach: the character stops nearby and still takes it, or reads "Can't reach that" instead of standing forever

**Campfire stockpile**

- [ ] Right-click the fire with sticks and stones in hand: the label flashes "Deposited", the HUD slots empty, the panel opens on its current tab
- [ ] The Stockpile tab lists Stick and Stone chunk with the deposited counts; resources never appear there (they went to the top bar)
- [ ] Tools are never deposited — a crafted axe stays in the character's hands
- [ ] The tab row switches Colonists · Stockpile · Craft without the panel jumping off-screen (it refits its height)

**Crafting**

- [ ] Craft tab: six recipes with effect and cost lines; a cost line is red until it is affordable from hands + stockpile + the resource pool together
- [ ] Craft with the character far away: they walk to the fire ("Walking to the fire to craft…"), then the label counts up "Crafting Stone Axe 45%"
- [ ] Craft while standing at the fire: starts at once; nothing is charged until the bar completes; walking away mid-craft cancels and costs nothing
- [ ] On completion: "Crafted Stone Axe" flash, one console line, the axe appears in the character's right hand, the recipe reads Done
- [ ] Materials are drawn from the hands first, then the stockpile (check the Stockpile tab and the HUD slots after a craft)
- [ ] Spending the pool's wood elsewhere while the Wooden Spear is crafting makes it fail at the end with "Missing materials" and nothing lost

**Unlocks — every lock names its recipe**

- [ ] Fresh run: every job row on the Colonists tab reads "needs Stone Axe / Fishing Spear / Stone Pick / Metal Pick" with `+` greyed even when a colonist is idle
- [ ] Crafting the Stone Axe un-greys Wood at once, with no restart and no panel reopen
- [ ] The warriors row reads "Craft a Wooden Spear at the fire to arm warriors" until it is crafted, then shows the usual cost
- [ ] Pressing B before the Mallet: no ghost, the character's label flashes "Craft a Mallet at the fire to build"; after the Mallet, build mode works as before
- [ ] Idle colonists ignore a construction site and damaged buildings until the Mallet exists (place one with F4 "Unlock all" toggled off… i.e. craft the Mallet mid-site and watch them start)
- [ ] F4 "Unlock All Crafting" opens everything and marks every recipe Done; "+10 Sticks & Stones (stockpile)" fills the stockpile; "Knock Out Player" runs the knock-out
- [ ] A sweep still hires workers and recruits warriors (every unlock is granted under the sim)
