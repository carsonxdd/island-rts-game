# Alpha Plan — from here to a build in a tester's hands

**Written 2026-09-08. This is the only forward-looking plan that is in scope.** Everything else at the repo root is either already built or parked (see the README's "Where the project is going").

**The decision behind it:** the game is feature-complete enough to learn from real players, and it has accumulated a large pile of built-but-never-played work. More features would grow that pile. So: **feature freeze.** Nothing new goes in unless it is on this page.

---

## Definition of done

An alpha is done when a person who has never seen the game can:

1. Be handed a zip, unzip it, and run the game with no instructions from you.
2. Understand what to do in the first five minutes without being told.
3. Reach an ending — rescue or defeat — inside one sitting.
4. Send feedback without leaving the game.

And when, for you:

5. Nothing in `DevQuests.txt` is untested.
6. A release build actually contains the game.

That is the bar. Not "polished", not "balanced", not "content-complete".

---

## Scope

**In:** the six items below, in order.

**Two accepted exceptions, both requested on 2026-09-08 and both before the alpha:** the occluder polish in section H, and fog of war with a minimap in section I. Fog is the larger of the two by a wide margin and the freeze should hold against everything else.

**Also in, after the alpha ships:** the architecture lap in section J — factions, spatial hash and AI level of detail, a colony governor, save/load, multiple islands. Added deliberately, months of work, and sequenced behind the release rather than in front of it.

**Out, explicitly:** new buildings, new units, new resources, the story, processing chains, families, farming, the Warehouse, building upgrades, enemies wading ashore, the lighting bake. All of it is real and none of it is alpha.

**The one exception rule:** a bug found in playtest gets fixed even if the fix is a small feature. A wish found in playtest gets written down, not built.

---

## A. Ship blockers

These make a build wrong or unusable. Nothing else matters until they are closed.

| # | Item | Notes |
|---|------|-------|
| A1 | ~~**`EditorBuildSettings` lists only `SampleScene`**~~ **Closed 2026-09-08.** | `Setup Everything`'s menu step writes the scene list (`MainMenu` first, then `MainIsland`) on every run, so this cannot regress unless the setup is skipped. Verified in the asset the same day. |
| A2 | ~~**The cloud shaders have never been compiled by Unity**~~ **Closed 2026-09-08**, one loose end. | Both shaders imported with no shader errors and the game has run in the editor since. The cookie render texture asked for R8 sRGB, which the platform refuses and silently widens to RGBA — now created linear. Whether the sky *looks* right is the "Clouds and graphics" playtest batch, not a blocker. |
| A3 | ~~**Run `Setup Everything (In Order)` and confirm it is clean**~~ **Closed 2026-09-08.** | Ran clean ("Full setup complete", all eight steps). The scene and prefab re-serialization it produced is committed. |
| A4 | **A release (non-development) build has to be sanity-checked** | The dev-quest tracker, the F3/F4/F6 tools and the sim are all compiled out of a release build. Every `UNITY_EDITOR || DEVELOPMENT_BUILD` guard in gameplay code was read on 2026-09-08 and hides only `SimOverrides` and F4 hooks — so this is now "make one release build and play the opening", nothing to code. |
| A5 | ~~**Version string**~~ **Closed 2026-09-08.** | `ProjectSettings.bundleVersion` is the one source (`0.2.0-alpha.1`; bump it per build handed out — alpha.2, alpha.3). The main menu shows `v<version> · updated <changelog date>` and every playtest report opens with it, via `Application.version`. The feedback form (F) reads the same field. |

**What is left of A:** one release build, played through the opening. That needs the editor, so it happens at the start of the first B sitting.

## B. Clear the playtest debt

The largest item and the least glamorous. `Assets/Resources/DevQuests.txt` is the checklist; each batch is one feature that has never been played.

Outstanding batches as of 2026-09-08, twenty of them: the research-and-days slices 3-6 (Iron Spear, food and hunger, archers, the escape ship), the Information screen, utility colonists and priorities, the dev-quest loop itself, the gear-up trip, formations and archers, the water stripe, the warrior cap, the combat box and folding sections, the patrol freeze, night lasts the raid, warrior stances, clouds and graphics presets, reachable rocks, the occluder fade, and the version string.

**The loop has never closed.** `Playtests/` is empty: no report has ever been submitted. So the first sitting is the **"Dev quests" batch itself** — tracker, DEV tab, SUBMIT REPORT, a file in `Playtests/` — before any feature batch, or every later sitting is testing the feature and the tool at once and cannot tell which one failed.

**How to work it:** one batch per sitting, in the order the file lists them, newest last. Play a normal run rather than jumping to the feature with cheats where you can — half of what these will find is interaction between features, not the feature itself. Submit the report, fix what it turns up, delete the batch.

**Fog will reopen some of these.** Section I changes gathering, the raid warning and what warriors can see, so the batches that test those — stances, formations and archers, night lasts the raid, slices 3 and 5 — get a second, shorter pass after I lands. B is "done" for the first time when the file is empty; those five come back as one "After fog" batch.

**Done when:** `DevQuests.txt` has no batches left and the reports are in `Playtests/`.

## C. A raid and economy tuning pass

Deferred for a while and now overdue, because the alpha's whole purpose is asking people whether the game is any good.

- Re-run the balance sweeps. **Every sweep baseline is stale** — food, the player character, the research and craft split, foraging, and utility colonists all changed since the last comparable run. Start from a fresh baseline, not the old numbers.
- Read `campfire_hp_min` in `days.csv` before win rate. The known failure mode is that a night is a shutout or a collapse with nothing in between.
- The open design question the sweeps could not answer with a number: what reaches the base past the warrior line. Do not solve it now. Write down what the sweeps say and let testers tell you whether it matters.

## D. The tutorial

A reactive step list over a normal run, drawn on the intro hint canvas. Not a scripted mode, not a separate scene: the steps watch a real run and tick themselves.

Shape:

- One step visible at a time, the next appearing when the current one completes.
- Steps follow the real opening: gather a stick, walk ashore, place the fire, deposit, research Woodcutting, assign a colonist, build a hut, craft a spear, arm a warrior. Then it goes quiet.
- It reuses the dev-quest signal idea (`DevQuests.Signal`) — the game already raises a signal at most of these points — but it is a separate, player-facing, always-on system, not the DEV tab.
- Skippable from Options, and off by default on a second run.

**Done when:** someone who has never played reaches their first armed warrior without asking you a question.

## E. A run a tester can finish

A full run is 30 days, about 75 minutes. Most testers will stop partway and you will learn nothing about the ending.

- Add a **short run length** to the New Game screen: 10 to 15 days, roughly 25 to 40 minutes.
- It has to be a real option, not a difficulty preset, because difficulty already means something else and is locked for the run.
- Raid pacing is tied to day number, so a compressed calendar needs its raid curve checked, not just its length changed. This is where the tuning in C earns its keep.
- The 30-day run stays as the default for you and as an option for testers who want it.

## F. The feedback path

Decided: **an in-game feedback button that posts to a Discord webhook, plus a Discord server for the conversation.** No custom site, no backend, no hosting bill.

Why this and not the alternatives: a Google Form gets you a spreadsheet but no community, platform comments lose the run context, and your own site is several sessions of work plus something to maintain forever. A webhook is one HTTP POST and costs nothing.

**What to build (next session, with its own design questions):**

- A **Feedback** entry on the pause menu and on the end screen. The end screen is the important one — that is when a tester has an opinion.
- A short form: a category, a free-text box, and a mood or rating. Keep it to one screen.
- Attach the run context automatically: version, difficulty, run length, island seed and size, day reached, outcome, colonist and warrior counts, and how long they played. A tester will not type any of that, and it is what makes a report actionable.
- Post as JSON to the webhook. Fail quietly and keep a local copy in `Feedback/` so nothing is lost when someone is offline.
- The `Playtests/` report writer already assembles a markdown document from a run. Reuse that assembly rather than writing a second one.

**The one caveat:** a webhook URL inside a distributed build can be extracted and spammed. For a private alpha with people you know, that is an acceptable risk — the mitigation is that a webhook is one click to delete and recreate. Keep the URL out of the repo (gitignored) so it is not in the git history when the repo ever opens up.

**Also set up, outside the code:**

- A Discord server. Three channels is enough to start: announcements, feedback, bugs.
- The invite link goes on the main menu, in the tester readme, and in the feedback form's confirmation.

## G. Package and hand it out

Decided: **direct file share** — a zip to people you know. No storefront, no page to maintain, nothing public.

- Zip the build with a short `PLAY ME.txt`: what the game is, how long a run takes, the controls that are not obvious (right-click is your character, B builds, left-click a building opens its panel), the Discord invite, and one line asking for the feedback button.
- Name the zip with the version and date so you can tell reports apart.
- Windows only for now. There is no reason to fight a Mac build for an alpha.
- Keep a list of who has which build.

**Worth knowing for later:** when the alpha outgrows a handful of people, itch.io is the next step and takes about five minutes — free, password-protectable, with a devlog and comments. Steam only becomes worth its 100 dollars and its review queue when there is a store page worth having.

## H. Occluder polish

The tree fade works but has three faults, all reported from play on 2026-09-08. **Partly built the same day; the rest is one session.**

- **Only trees faded.** Huts, the Watchtower, the Workshop and the Shipyard never did, so a worker behind a building was simply gone. *Done:* every resource node and those four buildings now carry the fade, and anything too short to hide a standing unit retires itself on first measure rather than being listed by type.
- **It missed cases and fired late.** A unit was tested as one point at chest height, so a head behind a canopy or a body under a roofline read as clear; the decision ran at 10 Hz and the fade took a quarter second on top of it. *Done:* a unit is tested as two points down its body, the tick is 20 Hz, fading out is more than twice as fast as fading back, one shared tightness constant became a per-object one because a canopy is mostly gaps and a hut is a solid box, and hysteresis stops an object chattering when a unit walks its edge.
- **The whole object ghosts out.** *Built 2026-09-08, unplaytested.* The occluder stays solid except for a soft round window where the unit is: `UnitHoleMask` blits one disc per unit (coverage + view depth) into a quarter-resolution screen texture, and `OccluderCutout.shader` — a copy of URP Lit whose forward fragment samples it and clips where the surface is nearer than the unit, with a dithered rim — replaces Lit on every occluder's material instances at runtime. Emission is untouched, so the hover glow still works. The old manager, its silhouette test and the opaque/transparent toggle are gone.

Both open questions closed by the same change: walls and gates now carry the cutout (a window in a wall line has none of the cost a line of ghosting cells had), and a window never shows in a shadow because the ShadowCaster pass is the stock one. Also found and fixed on the way: `TreeVariance` swapped a tree's materials in `Start`, after `ResourceNode.Start` had collected its instances, so on those trees the fade and the hover glow wrote to copies nothing drew — the "some trees never fade" report. It runs in `Awake` now.

**What is left of H:** the "Occluder cutout" batch in `DevQuests.txt`. The shaders have never been compiled by Unity; magenta trees on first Play means a compile error in `OccluderCutout.hlsl`, and windows opening below the units means `UnitHoleMask.flipY`.

## I. Fog of war and a minimap

Requested for the alpha on 2026-09-08 and accepted as a scope exception. **This is the biggest item on the page — several sessions, not one.**

**Decisions taken:**

- Explored memory fog: unseen island starts dark and clears permanently as your people move.
- It **gates colonists**: workers only gather nodes the colony has discovered, so exploring is a real act rather than a visual filter.
- It **hides raiders** until something of yours sees them. This is the expensive half. It turns a raid into an ambush and gives the Watchtower a second job.
- Vision comes from units and buildings, with the Watchtower seeing much further.
- Unexplored ground is **not placeable**, consistent with colonists refusing to gather what they have not found.
- A simple minimap ships with it. Without one, fog makes the map unreadable and reads as a bug rather than a feature.

**Why it is not the easy feature it looks like.** The rendering is the cheap part. The cost is that the AI reads global registries: workers scan every node on the island, enemies path to `BaseBuilding.ActiveList[0]`, and the raid warning tells you the size of tonight's raid at dawn. Making sight matter means a knowledge layer that sits between those scans and the world — which is a large piece of the faction work in section J, brought forward. Expect it to touch `ResourceAvailability`, the enemy target function, the warrior engage filter, the raid banner, `GhostPlacer` and the terrain material.

**Shape to build to:**

1. A visibility grid over the island, coarse (2 m cells is plenty), owned by one manager, with two bits per cell: ever-explored, and currently-visible. Update at a few hertz from a source list, not per frame per unit.
2. Vision sources register the way housing providers already do: a component with a radius, added by units and buildings, so nothing rescans the scene.
3. Terrain reads the grid as a texture. The precedent is the water depth map, which already feeds a heightfield texture to a material property block — the same pattern applies here.
4. Anything the player should not see checks the grid before it renders: enemies, undiscovered nodes, their health bars and state labels.
5. Gameplay gates last, in exactly three places to start: the resource scan, the enemy's visibility to warrior AI, and placement validity.
6. The minimap draws the explored mask plus friendly markers. It is also the answer to "where did that raid land".

**The one thing to resist:** making enemy AI fog-aware too. Raiders knowing where the campfire is remains fine, and a symmetric fog is a strategy game, not this one.

## J. The architecture lap

Added at the user's request on 2026-09-08, knowingly pushing the schedule out. This is the sequence from [`docs/SCALING_NOTES.md`](docs/SCALING_NOTES.md), promoted from "someday" into the plan:

**Factions → spatial hash + AI LOD → colony governor → save/load → islands.**

- **Factions.** "One colony" is baked into the type system rather than the data: `ResourceManager` and `PopulationManager` are singletons, `ActiveRegistry<T>` lists are global statics where "all workers" quietly means "my workers", and the enemy's campfire lookup is `BaseBuilding.ActiveList[0]`. Every targeting scan, economy call and population check assumes a single owner. The shape of the fix: registries become per-faction, `ResourceManager` becomes a component on a faction, `TargetingUtil.FindNearest` takes a faction filter. The second faction can stay a stub for a long time — the point is the refactor, not the opponent.
- **Spatial hash + AI LOD.** Considerations still walk whole registry lists. A uniform grid makes node, pickup and enemy queries cost the neighbourhood instead of the island. AI level of detail simulates distant colonies as abstract economies at 1 Hz and instantiates real agents only near the player. Four colonies at a hundred units each is right at Unity's ORCA ceiling, which is where both stop being optional.
- **Colony governor.** A second layer above the Utility AI, ticking at 1-2 Hz, deciding build orders and army composition and writing goals into a blackboard the unit considerations read. Additive, not a refactor — the unit AI needs almost nothing.
- **Save/load.** Serializable colony state. It is a prerequisite for islands, not a companion to them.
- **Islands.** Nearly free once save/load exists, because `IslandGenerator` is pure and seeded and an island is a seed plus parameters. Tear down and regenerate inside the single scene rather than loading a scene per island — scene loads resurrect exactly the stale-singleton problems that removing `DontDestroyOnLoad` was meant to fix.

**The honest cost.** This is months, not sessions, and none of it is visible to a player until the very end of it. Factions alone touches most of the combat, economy and population code, and the whole run of it lands with no new player-facing feature to show for it — which is the hardest kind of work to stay motivated through, and the reason to have testers already talking to you while it happens.

**Where it sits.** After G, not before. The argument for factions-first is that its cost grows with every feature added between now and then — but the alpha adds almost no features, so those weeks cost the refactor very little, while doing the refactor first costs the alpha its entire schedule and risks shipping to testers on freshly destabilised foundations. Ship the build, keep the testers, then start the lap. **If you want factions to jump ahead of the alpha instead, that is the one line to change here.**

---

## Order and rough shape

**A → B → H → I → C → D and E together → F → G → J.**

A is small and blocks everything. B is the long one and the one most likely to change the plan. H and I are the two accepted scope exceptions and go in before the tuning, because fog changes how a night plays and there is no point tuning raids twice. C wants B's fixes and I's changes behind it. D and E are what make a stranger's run useful and can be built side by side. F and G are the release lap. J runs for months afterwards while the alpha is in testers' hands.

Do not start D before B and I are finished. Building a tutorial for a build that is about to change is how the tutorial ends up wrong, and fog changes the first five minutes more than anything else on this page.

A through G is the alpha and should be measured in sessions, with I as the one item likely to take several. J is measured in months and has no shipping date attached to it.

---

## What this plan is not

It is not a design document. It settles no argument about what the game is, and it deliberately says nothing about the setting: whether this stays one castaway's story or becomes pickable civilizations is a question the alpha is meant to help answer, not one to answer before it. Ask the testers what they think they are playing.
