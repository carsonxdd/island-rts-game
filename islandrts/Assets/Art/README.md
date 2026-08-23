# Low-Poly Template Art Set

Procedurally generated placeholder art for Phase 10 Stage 2. Everything in this folder
is produced by `Assets/Editor/LowPoly/` — **do not hand-edit the meshes**, they are
overwritten on every regenerate. Change the generator source instead.

## Menu

`Tools > Island RTS > Low-Poly Templates >`

| Item | What it does |
|---|---|
| Generate All Assets | Rebuilds all 26 meshes, 38 materials, 26 prefabs |
| Generate *Category* Only | Same, limited to one category |
| Regenerate Materials Only | Re-applies the palette without touching geometry |
| Build Showcase Scene | Writes `LowPolyShowcase.unity` — everything laid out, lit, framed at the gameplay camera angle (euler 45/45/0, orthographic) |
| Capture Contact Sheet | Renders `ContactSheet.png`, one cell per asset |

Regenerating is safe and idempotent: existing meshes and materials are updated in place
via `EditorUtility.CopySerialized`, so their GUIDs survive and anything you have already
dragged into a scene keeps working.

## Contents

| Category | Assets |
|---|---|
| Environment | Palm_Tall, Palm_Bent, Palm_Young, Rock_Small/Medium/Large, Bush_Round, Bush_Wide, GrassTuft, Fern, DriftwoodLog, Barrel, Crate |
| Buildings | Hut, WoodenWall_Segment, StoneWall_Segment, Gate_Wooden, Watchtower, Campfire |
| Units | Worker, Warrior, Enemy |
| Resources | Tree, Tree_Small, BerryBush, RockNode |

26 assets, ~5,600 triangles total.

## Authoring conventions

- **Real world units, pivot at the base (y = 0), facing +Z.** The prefabs sit at scale 1.
  This differs from the current gameplay prefabs, which are unit primitives squashed by
  a transform scale — see the swap notes below.
- **Flat shaded.** Every triangle carries its own three vertices and a face normal. That
  faceted read is the low-poly look, per the "per-triangle normals" note in CLAUDE.md.
- **Submesh per palette colour.** A palm is a trunk submesh plus frond submeshes, not
  several GameObjects. Material slot order matches `MeshBuilder.MaterialKeys`.
- **Deterministic.** All jitter runs through a seeded `System.Random`, so regenerating
  reproduces identical geometry. Change a seed to reroll a shape.
- **Vertex colours** are written alongside the materials, in case a single vertex-colour
  shader is preferred later over 38 material assets.

## Dimensions (matched to the existing gameplay prefabs)

| Asset | Footprint | Height | Matches |
|---|---|---|---|
| Worker | 0.40 | 1.2 | `Worker.prefab` scale |
| Warrior | 0.50 | 1.4 | `Warrior.prefab` scale |
| Enemy | 0.45 | 1.4 | `Enemy.prefab` scale |
| Hut | 2.0 x 2.0 | 1.5 body, 2.6 peak | `Hut.prefab` scale |
| WoodenWall_Segment | 1.0 x 0.3 | 1.2 | `WallConnector.WOODEN_HEIGHT` |
| StoneWall_Segment | 1.0 x 0.3 | 2.0 | `WallConnector.STONE_HEIGHT` |
| Watchtower | 2.0 x 2.0 | 4.0 | `WatchTower.prefab` scale |
| Campfire | 1.5 dia | ~1.0 | `Campfire.prefab` scale |

## Editing the look

- **Colours:** `Assets/Editor/LowPoly/LowPolyPalette.cs`, then *Regenerate Materials Only*.
- **Shapes:** `Shapes_Environment.cs` / `Shapes_Buildings.cs` / `Shapes_Units.cs` /
  `Shapes_Resources.cs`, then regenerate that category.
- **New shape primitives:** `MeshBuilder.cs`.

Winding convention: perimeter points run counter-clockwise seen from above, and Unity
treats clockwise-from-the-front as front-facing. So a top cap is the *reversed* fan and a
bottom cap is the forward one. Getting this backwards produces meshes that still show a
silhouette but are lit inside-out.

## Emissive / bloom

`LP_FireCore` and `LP_Ember` carry HDR emission at intensity 3 and 2. That clears the
Global Volume's Bloom Threshold of 1.0 without the threshold being lowered scene-wide,
which is the approach CLAUDE.md calls for — only true HDR-emissive hero assets should bloom.

## If you swap these into the gameplay prefabs

Nothing here is wired into gameplay yet; these are meshes, materials, and plain
MeshRenderer prefabs. Before swapping:

1. **Swap meshes and materials only.** Never reparent, rename, or replace the root
   GameObject — `Health`, `AIBrain`, `NavMeshAgent`, and `ActiveRegistry<T>` registrations
   all reference it.
2. **Reset the transform scale to 1** and remove the old squash scale, since these meshes
   are authored at true size with a base pivot. The old prefabs use unit primitives at
   scales like `(0.4, 0.6, 0.4)`.
3. **Re-check collider bounds, then re-bake the NavMesh.** If a footprint moves, carving
   regions move with it and pathing breaks until the bake is refreshed.
4. **Walls are special.** `WallConnector` generates wall and gate meshes procedurally from
   a 4-bit neighbour bitmask; `WoodenWall_Segment` / `StoneWall_Segment` / `Gate_Wooden`
   here are standalone shapes, not drop-in replacements for that system.
