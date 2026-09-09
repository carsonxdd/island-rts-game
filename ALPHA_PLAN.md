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

**Also in, after the alpha ships:** the architecture lap in section H — factions, spatial hash and AI level of detail, a colony governor, save/load, multiple islands. Added deliberately, months of work, and sequenced behind the release rather than in front of it.

**Out, explicitly:** new buildings, new units, new resources, the story, processing chains, families, farming, the Warehouse, building upgrades, enemies wading ashore, the lighting bake. All of it is real and none of it is alpha.

**The one exception rule:** a bug found in playtest gets fixed even if the fix is a small feature. A wish found in playtest gets written down, not built.

---

## A. Ship blockers

These make a build wrong or unusable. Nothing else matters until they are closed.

| # | Item | Notes |
|---|------|-------|
| A1 | **`EditorBuildSettings` lists only `SampleScene`** | A build made today ships the empty stock scene. Needs `MainMenu` first, then `MainIsland`. Fix in File > Build Profiles, verify by building and running once. |
| A2 | **The cloud shaders have never been compiled by Unity** | Written outside the editor and never opened in it. A typo renders magenta. First editor launch, look at the sky. |
| A3 | **Run `Setup Everything (In Order)` and confirm it is clean** | Several sessions of art, prefab and scene work have landed since it last ran. |
| A4 | **A release (non-development) build has to be sanity-checked** | The dev-quest tracker, the F3/F4/F6 tools and the sim are all compiled out of a release build. Confirm nothing the player needs was behind one of those flags. |
| A5 | **Version string** | The game has no version anywhere. Needs one visible on the main menu and included in every feedback report, or you will not know which build a report is about. |

## B. Clear the playtest debt

The largest item and the least glamorous. `Assets/Resources/DevQuests.txt` is the checklist; each batch is one feature that has never been played.

Outstanding batches as of today: the research-and-days slices 3-6 (Iron Spear, food and hunger, archers, the escape ship), utility colonists and priorities, the Information screen, the dev-quest loop itself, warrior stances and formations, the gear-up trip, clouds and graphics presets, and reachable rocks.

**How to work it:** one batch per sitting, in the order the file lists them, newest last. Play a normal run rather than jumping to the feature with cheats where you can — half of what these will find is interaction between features, not the feature itself. Submit the report, fix what it turns up, delete the batch.

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

## H. The architecture lap

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

**A → B → C → D and E together → F → G → H.**

A is small and blocks everything. B is the long one and the one most likely to change the plan. C wants B's fixes first. D and E are the two things that make a stranger's run useful and can be built side by side. F and G are the release lap. H is the long architecture lap that runs while the alpha is in testers' hands.

Do not start D before B is finished. Building a tutorial for a build that is about to change is how the tutorial ends up wrong.

A through G is the alpha and should be measured in sessions. H is measured in months and has no shipping date attached to it.

---

## What this plan is not

It is not a design document. It settles no argument about what the game is, and it deliberately says nothing about the setting: whether this stays one castaway's story or becomes pickable civilizations is a question the alpha is meant to help answer, not one to answer before it. Ask the testers what they think they are playing.
