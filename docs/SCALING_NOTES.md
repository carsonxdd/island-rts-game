# Scaling Notes — Toward Hundreds of Units, AI Colonies, and Multiple Islands

Design thinking captured while fixing the AI evaluation budget (August 2026), moved out of the README on 2026-09-08. **None of this is alpha scope** — see [`ALPHA_PLAN.md`](../ALPHA_PLAN.md) for what is. Not committed work — a map of what the current architecture supports, what it blocks, and the order the blockers are cheapest to clear.

### Where the entity ceiling actually is

The limit was never Unity — it was the AI layer's own throttles and a scan whose cost was `O(units × resource nodes)`. With the scaling pass above, the next wall is **Unity's NavMesh ORCA avoidance**, which is the real ceiling at somewhere in the low hundreds of agents (workers run `High` avoidance quality, the most expensive setting, deliberately — see Phase 6.26). Beyond that:

- **Spatial hash for registry lookups.** Considerations still walk whole `ActiveRegistry` lists. A uniform grid keyed by cell would make node/pickup/enemy queries `O(nearby)` instead of `O(all)`. This is the next optimization worth doing, and it gets more valuable with every entity added.
- **Avoidance quality by population.** Dropping workers to `Medium` above some unit count buys Unity-side headroom, at the cost of the head-on side-step dance that Phase 6.26 raised the quality to fix. A tuning trade, not a free win.
- **Decision richness is not the problem.** Five actions × three-to-five considerations, multiplied, is cheap. The cost lives in registry scans hidden inside considerations. Units do not need to get dumber to scale — the scans need to get narrower.

### AI colonies — decide faction ownership early

Rival colonies are the feature with a deadline attached, because "one colony" is currently baked into the type system rather than the data:

- `ResourceManager` and `PopulationManager` are singletons
- `ActiveRegistry<T>` lists are global statics — "all workers" implicitly means "my workers"
- `EnemyAttackExecutor.FindLiveCampfire()` returns `BaseBuilding.ActiveList[0]`

Every targeting scan, every economy call, and every population check assumes a single owner. Introducing a `Faction` concept means touching all of those sites, and **that set grows with every feature added between now and then** — mechanical if done early, a rewrite if done late. The shape: registries become per-faction, `ResourceManager` becomes a component on a faction rather than a singleton, and `TargetingUtil.FindNearest` takes a faction filter. Worth doing before the next large gameplay system even if the second faction stays a stub for a long time.

The unit AI itself needs almost nothing. Utility AI is the right layer for *"what does this worker do next."* Colony **strategy** wants a second layer above it — a governor ticking at 1–2 Hz that decides build orders and army composition and writes goals into a shared blackboard that unit considerations read. That is additive, not a refactor.

### Multiple islands — better positioned than expected

`IslandGenerator` is pure and seeded, so an island is just a seed plus a few parameters. Islands that "load differently" are nearly free, and no scene-per-island is required. `TerrainGrid` doing all of its work in `Awake` under `[DefaultExecutionOrder(-100)]` is the contract that makes this hold: every `Start()`-time system already finds a finished world and a live NavMesh.

**Prefer teardown-and-regenerate inside the single scene over scene loading per island.** Scene loads would resurrect exactly the stale-singleton problems that removing `DontDestroyOnLoad` was meant to fix (see the Phase 6.21 notes). The real prerequisite is *serializable colony state* — leaving an island and returning to it means that colony must persist as data — which is the Phase 11 save/load system. Islands therefore naturally follow save/load rather than preceding it.

### Where the two features collide

Four AI colonies at ~100 units each is ~400 agents, right at the NavMesh ceiling. That is the point where the spatial hash becomes mandatory and **AI level-of-detail** starts to matter: simulate distant or offscreen colonies as abstract economies ticking at 1 Hz, and instantiate real agents only near the player. Standard RTS practice — and substantially easier to retrofit if factions already exist.

### Suggested order

**Factions → spatial hash + AI LOD → colony governor → save/load → islands.**

Factions first because their cost grows over time; islands last because they depend on save/load.
