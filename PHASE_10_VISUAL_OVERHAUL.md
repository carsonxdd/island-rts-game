# Phase 10 — Visual Overhaul

**Status:** Planned
**Owner:** Carson
**Last updated:** 2026-04-30

This document is the source of truth for Phase 10. The summary in `.claude/CLAUDE.md` and the line in `README.md` point here. When the plan changes, update this file first.

---

## Goal

Replace primitive geometry and default lighting with a cohesive stylized low-poly aesthetic in the family of:

- Synty POLYGON Pirates / POLYGON Tropical
- Bad North
- Townscaper
- Islanders

Visual reference is the **Castaway Colony main menu mockup** — sunset palette, low-poly islands, stylized water, soft DOF on background.

For gameplay we want the same vibe but optimized for top-down RTS framing: **no DOF, no macro lensing, readable silhouettes from the play camera height.** Match the mockup's palette, water quality, and silhouette clarity — not its composition.

---

## Tech Foundation (already in place)

- Unity **6000.0.25f1**
- **URP 17.0.3** — Volume system available for post-processing
- **Shader Graph** — available for the stylized water shader
- `DayNightCycle.cs` — will drive sun rotation, sun color, ambient gradient, fog color, and water shader properties between day and night presets stored as `LightingPreset` ScriptableObjects (one for day, one for night)

No new packages or pipeline migrations are required for any stage of Phase 10.

---

## Stage 1 — Post-Processing Pass

**Cost:** ~1 evening. Can be done anytime — does not block any other phase.

### Global Volume on SampleScene

Add a Global Volume to `SampleScene` with these overrides:

| Override | Setting |
|---|---|
| **Bloom** | intensity ~0.5, threshold ~1.0 |
| **Color Adjustments** | post-exposure +0.2, contrast +10, slight saturation bump |
| **White Balance** | temperature +15 (warm) |
| **Tonemapping** | ACES (filmic highlight rolloff) |
| **Vignette** | intensity 0.25, smoothness 0.4 |
| **Depth of Field** | **skip** — works against RTS gameplay clarity |

### Directional Light Tuning

- Lower sun angle: **~15–25° elevation** for daytime — dramatic golden-hour feel
- Warm sun color (soft gold rather than pure white)
- Ambient lighting: **Gradient mode** — warm sky tint, neutral equator, cool ground. Creates the warm-light / cool-shadow split that defines the aesthetic.

### DayNightCycle Hook

`DayNightCycle.cs` should lerp the following between day/night `LightingPreset` ScriptableObjects:

- Sun rotation
- Sun color
- Ambient gradient
- Fog color
- Water shader properties (depth-blend tint, foam color, specular color)

Create the `LightingPreset` SO type with day and night entries. Don't bypass it with one-off `Lerp` calls in other scripts — extend the preset SO instead.

---

## Stage 2 — Asset Replacement (Hybrid Strategy)

### Bought Pack — filler / environment

Use a low-poly tropical pack for high-volume, non-identity assets:

- **Primary candidate:** Synty POLYGON Pirates or POLYGON Tropical Pack ($30–60)
- **Free alternatives:** Quaternius CC0 packs (itch.io), Kay Lousberg's KayKit (itch.io)

Use the bought pack for: palm trees, rocks, bushes, grass, environment props (barrels, crates, driftwood), terrain decoration, generic foliage.

### Custom Modeled — hero / identity assets

Things the player looks at constantly or that define the game's identity get bespoke models in Blender:

- **Campfire** — always on screen, sets the visual tone
- **Worker, Warrior, Enemy** — chunky readable silhouettes from the RTS camera angle
- **Buildings** — Hut, Wooden Wall, Stone Wall, Gate, Watchtower
- **Shipwreck / starting boat** — story element, anchors the setting

Reference: Imphenzia's low-poly modeling YouTube series.

### Replacement Workflow

1. Create the directory tree:

   ```
   Assets/Art/
     Models/
     Materials/
     Prefabs/Art/
   ```

2. Replace **one prefab category at a time** — e.g., all trees first, then rocks, then buildings, then units.
3. **Keep gameplay prefabs intact** — swap meshes and materials only. Do not touch `Health`, `AIBrain`, `NavMeshAgent`, `ActiveRegistry<T>` registrations, or any event subscriptions. The root GameObject and its component stack must remain stable.
4. **Validate NavMesh after each category swap** — mesh bounds may change, which moves carving regions and can break pathing until the bake is refreshed.
5. **Verify `ActiveRegistry` registrations still fire** (Awake / OnDestroy) on the swapped prefabs.

---

## Stage 3 — Stylized Water Shader (Shader Graph)

**Build as a side project during Phase 7–8 downtime, before Phase 10 starts.** This de-risks the hardest visual piece. The asset swap in Stage 2 is mostly mechanical; the water shader is where the look stands or falls.

### Components

- **Vertex displacement** — Gerstner waves or stacked sine, amplitude **0.05–0.15 units** (calm tropical, not open ocean)
- **Depth-based color blend** — sample the camera depth texture, lerp shallow turquoise → deep blue based on water depth at each pixel
- **Shoreline foam** — thin band where the depth difference between water surface and seabed is small; modulate with a scrolling noise texture and threshold for the stylized look
- **Sun specular** — Blinn-Phong style highlight, **quantized with smoothstep** for stylized hard-edge appearance (not a smooth blob)
- **Flat-shading (optional but recommended)** — per-triangle normal recalculation in the fragment shader, or a low-density mesh with hard edges. Without this you get smooth-shaded water that visually breaks the low-poly aesthetic.

### Resources

- Daniel Ilett — URP stylized water tutorial
- NedMakesGames — Shader Graph series
- Stylized Water 2 (Asset Store) — reference even if not used directly

### Time Budget — Hard Cap

- 1 weekend rough
- 1 weekend polish

**Do not exceed two weekends.** Pattern recognition: tendency to over-engineer foundational systems. If the shader isn't working after two weekends, ship what you have or buy Stylized Water 2 and move on.

---

## Stage 4 — Lighting Bake and Final Polish

- Mark static geometry — terrain, rocks, trees, buildings — as **Static**
- **Mixed lighting mode**, bake lightmaps for soft indirect bounce
- Dynamic units and the wave-displaced water stay **real-time** — never include water in the bake
- Add **URP exponential distance fog** for atmospheric depth and foreground/background separation
- Tune **shadow distance and cascade settings** for the RTS camera range — default cascades are tuned for FPS distances and waste resolution at our zoom levels
- Consider subtle **URP SSAO** — test whether it helps stylized reads or muddies them; keep only if it helps

---

## Stage 5 — Recommended Sequencing

| When | What |
|---|---|
| **Now / anytime** | Stage 1 post-processing pass — free morale win, evening of work |
| **During Phase 7–8 downtime** | Stage 3 water shader prototype as side project |
| **Pre–Phase 10** | Buy Synty pack, build a single test scene to validate the look before committing to a full swap |
| **Phase 10 proper** | Stage 2 asset replacement (categorical), Stage 4 lighting bake, final polish |

---

## Anti-Patterns / Gotchas

- **Don't chase the menu mockup exactly.** That image has macro DOF and a low/cinematic camera; the gameplay camera will never frame the world that way. Match palette, water quality, and silhouettes — not composition or DOF.
- **Don't bake the water.** It's dynamic geometry; it must stay real-time. Don't mark it Static, don't include it in the lightmap bake.
- **NavMesh re-bake after mesh swaps if collider bounds change.** Mesh swaps move carving regions; pathing breaks until the bake is refreshed.
- **Keep gameplay prefab GameObject hierarchy stable during swaps.** AI registries, `Health` components, and event subscriptions all reference these objects. Swap meshes and materials only — don't reparent, rename, or replace the root GameObject.
- **Per-triangle normals are the "low-poly water" look.** Without them you get smooth-shaded water that breaks the aesthetic. Either recalc normals in the fragment shader or use a low-density mesh with hard edges.
- **Test stylized lighting at the actual RTS camera height, not in a beauty-shot view.** What looks good at a 30° camera tilt or a scene-view fly-through can look flat or muddy from the gameplay camera. Validate in Play mode at the real camera transform.

---

## Success Criteria

- Game looks visually cohesive at the gameplay camera angle (top-down RTS framing)
- Water has depth-blended color, shoreline foam, and quantized sun specular at minimum
- Day/night cycle drives a smooth full-scene lighting transition: sun, sky, fog, and water all lerp together
- Frame rate **≥60 fps** on target hardware with bake + post-processing active
- Hero assets (units, buildings, campfire) are bespoke; environment is bought-pack
- A new player would describe the aesthetic in the same family as **Bad North**, **Townscaper**, or **Islanders**
