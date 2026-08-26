# Terrain System Plan — Dynamic Island Terrain

**Status: T1 IMPLEMENTED (2026-08-25) — pending editor setup run + playtest; T2–T4 not started.**
T1 code: `Assets/Scripts/Terrain/` (`TerrainGrid`, `IslandGenerator`) + `Assets/Editor/TerrainSetup.cs`.
Apply with `Tools > Island RTS > Terrain > Setup Terrain Scene (T1)` — run AFTER the Opening Sequence setup.
One deviation from the staging table: the deep-water NotWalkable volume (a T3 item) ships in T1, because
without it the runtime NavMesh would cover the seabed and units could walk across the ocean.
Companion to `PHASE_10_VISUAL_OVERHAUL.md`. This doc is the source of truth for the terrain
system; the CLAUDE.md Phase History entry is a summary.

## Locked Decisions (user, 2026-08-25)

1. **Random island per run.** Seeded procedural generation at game start; every playthrough
   gets a new island. Campfire and resource spawns become terrain-aware.
2. **Gentle tactical.** Rolling hills are all walkable; water is impassable; occasional steep
   slopes block pathing and create natural chokepoints. Generation must guarantee the island
   stays fully connected.
3. **Enemies emerge from the water.** Raiders spawn just offshore in a shallow wading band and
   walk up the beach.
4. **Smoothing fixes almost everything.** Any land above water level is placeable; the
   flatten/smooth op makes it work. Reject only footprints overlapping water or spanning a
   cliff. Building placement stays as fluid as today.

## Current State (what this replaces)

- Ground is one built-in Unity Plane scaled 10× → 100×100 m flat at y=0, MeshCollider, static.
- **No water exists at all.**
- NavMesh is an **editor bake embedded in the scene** (`NavMeshSettings`, agentRadius 0.5).
  Dynamic terrain forces the move to a runtime `NavMeshSurface` (confirmed present in
  com.unity.ai.navigation 2.0.14).
- Flat-y=0 assumptions live in: `BuildPlacement` (math-plane raycast fallback + fixed
  `placementHeight`), `WallGrid.GridToWorld(y=0)`, `WallConnector` (y=0.02), `EnemySpawner`
  (`spawnHeight=1`, ring at radius 30), `ResourceSpawner` (`spawnHeight=0`), grid overlay.
- The AI layer is already height-safe: every destination goes through
  `NavMesh.SamplePosition` / `TargetingUtil.GetApproachPoint`, which return points ON the
  navmesh at whatever height it is.

## Architecture

### Not Unity Terrain

Unity Terrain is smooth-shaded (fights the Phase 10 flat-shaded look), heavy to deform at
runtime, and awkward with runtime NavMesh. Instead: a custom chunked heightmap mesh — the
same procedural-mesh DNA as `WallConnector` and `LowPolyAssetGenerator`.

### 1. `TerrainGrid` (runtime singleton, sibling of WallGrid)

- **Height field:** `float[V,V]` at 1 m vertex spacing over the island area (start with the
  current 100×100 world, i.e. 101×101 verts). Sea level is **y = 0** by convention; land rises
  above it, seabed dips below.
- **Chunks:** 16×16-quad chunks, each = one flat-shaded mesh (duplicated verts per triangle
  for hard normals) + one `MeshCollider`. ~40k tris island-wide — trivial. Chunks live on the
  layer `BuildPlacement.groundLayer` expects so its raycast keeps working unmodified.
- **Coloring:** vertex colors by height band + slope — seabed → sand (0..~0.5) → grass →
  rock on steep slopes. One shared vertex-color LP material, zero textures. Beaches are free.
- **API:**
  - `SampleHeight(worldPos)` — bilinear; the one way anything asks "how high is the ground".
  - `SlopeAt(worldPos)`, `IsLand(worldPos)` (height > 0), `IsShallow(worldPos)`.
  - `FlattenArea(center, halfExtents, blendRadius)` — the smoothing op (below).
  - `RebuildDirtyChunks()` + `OnTerrainModified` event.
- Dirty-chunk rebuild only; mesh arrays reused. MeshCollider cooking is the expensive part —
  it happens only on placement (rare event), never per frame.

### 2. `IslandGenerator`

- Seed → **radial falloff × domain-warped coastline noise** (so the island isn't a circle)
  × 2–3 octaves of Perlin for hills and valleys. Amplitude budget: **max ~4 m above sea level**
  (see Camera Readability below). Shoreline gradient clamped gentle so the beach ring and
  wading band are walkable everywhere.
- **Campfire site:** pick the flattest area near the island centroid, flatten it during
  generation (same `FlattenArea` op), move the scene campfire there.
- **Validation loop:** generate → build chunks → build NavMesh → verify with *actual*
  `NavMesh.CalculatePath` tests (campfire → N sampled shoreline points). Fail → reroll seed
  (bounded attempts, then flatten the offending region as a fallback). Path-testing against
  the real NavMesh beats re-implementing Unity's walkability rules in the flood fill.

### 3. Runtime NavMesh

- One `NavMeshSurface` on the terrain root, collecting chunk colliders; agent settings match
  the old bake (radius 0.5). Built in `Start` **before** campfire/spawners run — script
  execution order: TerrainGrid → NavMesh build → everything else.
- **Delete the scene's baked NavMesh** — otherwise it unions with the runtime surface and
  units walk on the ghost of the old flat world.
- **Deformation updates:** `surface.UpdateNavMesh(surface.navMeshData)` — async, old mesh
  stays valid during rebuild. Full-surface updates are fine at this world size.
  **Coalesce:** a wall line places N walls in one click → one batched flatten + one chunk
  rebuild + one NavMesh update, not N.
- Building carve obstacles (`NavMeshObstacle.carving`) keep working unchanged on the surface.
- **Deep water is NotWalkable** via a `NavMeshModifierVolume` covering everything below
  **y = −0.4**. The −0.4..0 band is the *wading band* — walkable shallow water enemies spawn
  in and emerge from. Beach connectivity = wading band + sand ring.

### 4. Water

- Flat plane at y=0, larger than the island (~300×300, to the camera horizon), translucent LP
  material for now. This is exactly where the Phase 10 Stage 3 stylized water shader mounts
  later (depth-blend + foam will love a real seabed underneath). **Never static, never baked.**

### 5. Building Placement Smoothing (the headline feature)

`TerrainGrid.FlattenArea(center, halfExtents, blendRadius)`:
1. Target height = average of footprint samples, clamped ≥ ~0.5 (buildings never sink to sea).
2. Footprint set to target; a 2–3 m ring blends out with smoothstep by distance.
3. Dirty chunks rebuild; one async NavMesh update kicks off.

Called from `BuildPlacement` on confirm for huts/towers/walls alike. Validity: reject only if
any footprint sample is underwater or the footprint height range exceeds a cliff threshold
(~1.5 m) — ghost tints red via the existing `RendererTint` path. `placementHeight` semantics
become "offset above terrain" (already 0 for base-pivot Stage 2b buildings — good timing).

## System Impact Table

| System | Change |
|---|---|
| `BuildPlacement.GetSnappedMousePosition` | Raycast already hits chunk colliders; delete the math-plane fallback; y = `SampleHeight` + placementHeight |
| `WallGrid.GridToWorld` | y param → terrain sample |
| `WallConnector` | Walls sit at cell terrain height. Fine in practice: placement flattens the strip first, so per-cell steps are small |
| `WallLinePlacer` | Batch flatten for the whole line; ghost cursor y from terrain |
| `EnemySpawner` | Replace fixed ring: sample island perimeter for wading-band points (−0.4 < h < 0) with `NavMesh.SamplePosition`; keep the wave-direction logic |
| `ResourceSpawner` | Candidate filter += on land, slope < threshold, `SamplePosition` succeeds; trees keep off the sand band; y from terrain. Existing rejection-sampling structure extends cleanly |
| `LowPolyScatter` | Already raycasts — works as-is; later polish: flotsam allowed in shallows, radial bands re-tuned to actual coastline |
| Grid overlay | Cosmetic, off by default — conform later or leave |
| `AIWorldState` | No change (grid already 300×300, clamped) |
| Camera / AI executors / StuckResolver | No change (`SamplePosition`-based throughout) |

## Camera Readability Constraint

Orthographic RTS camera + hills = occlusion: terrain hides what's behind it. This is why the
amplitude budget is **~4 m max** and slopes are mostly gentle. Validate at the real gameplay
camera angle (Phase 10 gotcha applies: never judge terrain in scene-view fly-through). If
tall features ever land, they need the classic RTS answer (see-through/dither) — out of scope.

## Staging (each stage playable)

- **T1 — Static-shaped world:** TerrainGrid + chunk meshing + generator at a fixed seed +
  water plane + runtime NavMeshSurface (scene bake deleted). Spawners/placement get height
  sampling only (no smoothing yet); seed tuned gentle so everything works. Full game loop
  playable on a shaped island.
- **T2 — Deformation:** FlattenArea + placement integration + validity rules + async NavMesh
  updates + wall-line batching.
- **T3 — Waterline gameplay:** deep-water NotWalkable volume, wading band, shoreline enemy
  spawns, connectivity validation + seed reroll.
- **T4 — Random per run:** random seed, terrain-aware campfire + resource placement,
  vertex-color/beach polish, scatter integration.

## Risks / Gotchas (check during implementation)

- **Startup ordering** is the #1 break risk: today every system assumes a NavMesh exists at
  frame 0. Terrain gen + NavMesh build must complete before campfire spawn positions,
  ResourceSpawner, and unit spawning run.
- Old baked NavMesh left in the scene = double navmesh.
- `Gate` triggers, `ResourceNode.SetupNavMeshObstacle`, watchtower ranges: all XZ/edge-based —
  expected fine, but on the playtest checklist.
- MeshCollider cooking on flatten: profile; if a wall line stutters, spread cooks over frames.
- `FlattenArea` under an *existing* building's neighbor: smoothing ring may slightly shift
  ground under an adjacent building — clamp the ring so it never re-lowers terrain under a
  placed footprint (track flattened footprints in TerrainGrid).
- Respawning resources (`ResourceSpawner.enableRespawning`) must respawn at terrain height too.

## Open Questions (settle at implementation)

- Island footprint: keep 100×100 or grow (~140×140) so the coastline breathes? (Cost is tiny.)
- Flatten animation: snap on placement first; animated "ground settles" is later polish.
- Does the seabed render everywhere or only near shore (deep water fades to flat color)?
- Hill/valley influence on future phases: watchtower high-ground bonus (Phase 9+?), builder
  pathing (Phase 7 unaffected — same NavMesh rules as workers).
