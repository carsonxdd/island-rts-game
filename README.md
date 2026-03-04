# Island RTS Game - Complete Development Guide

A Unity-based real-time strategy survival game featuring autonomous worker AI, resource gathering, base management, and tactical combat.

---

## 🏝️ Game Vision

**Working Title:** *Island RTS Prototype*  
**Genre:** Top-down RTS + Survival → Action-RTS Hybrid  
**Setting:** Age of Sail era shipwreck survival

### The Concept
A naval vessel crashes into an uncharted tropical island during an Age of Sail voyage. As the Admiral, you and your crew of sailors must survive using only what washes ashore. Start with nothing—no tools, no weapons, just driftwood and stones on the beach. Progress from primitive stone tools to an industrialized colony while defending against nightly raids.

Think *Age of Empires* meets *Don't Starve* with a shipwreck survival twist, evolving into a *Warcraft 3* hero + RTS hybrid.

### Core Gameplay Loop (Current Alpha)
1. **Gather Resources** - Assign workers to collect wood, food, and stone
2. **Build Structures** - Construct buildings to expand your settlement
3. **Manage Economy** - Balance resource gathering and unit production
4. **Recruit Warriors** - Spend resources to build a defensive force
5. **Survive Raids** - Defend against escalating nightly enemy attacks
6. **Strategic Positioning** - Place warriors to intercept enemies before they reach buildings
7. **Win or Lose** - Survive 5 nights for victory, or lose when your campfire is destroyed

---

## 📋 Table of Contents

### Quick Reference
- [🎮 Current Status](#-current-status)
- [🎯 How to Play](#-how-to-play)
- [✅ Complete Feature List](#-complete-feature-list)
- [🧪 Testing Checklist](#-testing-checklist)

### Implementation Details
- [Phase 5.3 - Warriors & Victory/Defeat](#-phase-53-implementation-summary)
- [Phase 5.4 - Combat Visual Effects](#-phase-54-implementation-summary)
- [Phase 5.5 - Audio & 3D Spatial Audio](#-phase-55-implementation-summary--complete)
- [Phase 6.2 - Wall System Refactor](#phase-62-complete-)
- [Phase 6.3 - AI Bug Fixes](#phase-63-complete-)
- [Phase 6.4 - Pathfinding Improvements](#phase-64-complete----pathfinding-improvements)
- [Phase 6.5 - GC Allocation Elimination](#phase-65-complete----gc-allocation-elimination)
- [Phase 6.6 - Deep GC & Per-Frame Overhead Elimination](#phase-66-complete----deep-gc--per-frame-overhead-elimination)
- [Phase 6.7 - Simplified Gate System](#phase-67-complete----simplified-gate-system)
- [Phase 6.8 - Worker Refactor & Movement Smoothing](#phase-68-complete----worker-refactor--movement-smoothing)
- [Phase 6.9 - Staggered Updates & Final Stutter Fix](#phase-69-complete----staggered-updates-audio-preload--navmesh-stutter-fix)
- [Phase 6.10 - Simplified Worker Stuck Detection](#phase-610-complete----simplified-worker-stuck-detection)
- [Phase 6.11 - Utility AI System](#phase-611-complete----utility-ai-system)
- [Phase 6.12 - Utility AI Tuning & Debug](#phase-612-complete----utility-ai-tuning--debug)
- [Phase 6.13 - Pathfinding Audit & Worker Freeze Fixes](#phase-613-complete----pathfinding-audit--worker-freeze-fixes)
- [Phase 6.14 - Worker Return Logic & Delivery Fixes](#phase-614-complete----worker-return-logic--delivery-fixes)
- [Phase 6.15 - AI Momentum & Stuck State Fixes](#phase-615-complete----ai-momentum--stuck-state-fixes)

### Technical Documentation
- [🔧 Technical Architecture](#-technical-architecture)
- [📚 Project Structure](#-project-structure)
- [🎯 Development Roadmap](#-development-roadmap)
- [⚙️ Balancing Guide](#️-balancing-guide)
- [🐛 Known Issues & Fixes](#-known-issues--fixes)

### Gameplay & Strategy
- [🎮 Expected Gameplay Flow](#-expected-gameplay-flow)
- [📊 Statistics & Balancing](#-statistics--balancing)

### Future Development
- [🔮 Phase 6 - Economy Expansion](#-phase-6-economy-expansion)
- [🔮 Phase 7 - Building Progression](#-phase-7-building-progression)
- [🔮 Phase 8 - Worker Night Behavior](#-phase-8-worker-night-behavior-system)
- [🔮 Phase 9 - Player Character (Naval Admiral)](#-phase-9-player-character-naval-admiral-system)
- [🔮 Phase 10 - Art & Visual Upgrade](#-phase-10-art--visual-upgrade)
- [🎮 Future Game Vision Summary](#-future-game-vision-summary)

### Getting Started
- [🛠️ Tools & Dependencies](#️-tools--dependencies)
- [🎓 Unity Beginner Guide](#-unity-beginner-guide)
- [🐛 Debugging Tips](#-debugging-tips)
- [🎮 Quick Start for New Sessions](#-quick-start-for-new-sessions)

---

## 🎮 Current Status

**Development Phase:** Phase 6.15 - AI Momentum & Stuck State Fixes
**Last Updated:** March 2026
**Build Status:** Fully playable with Utility AI decision-making (Workers, Warriors, Enemies), ReturnUrgency compound scoring (carry fullness + enemy/night pressure + drop-off efficiency), delivery race condition fix with timer-based fallback, 20% commitment threshold to prevent action flip-flopping, ForceReeval triggers on damage/ally-death/wall-break, enemy stuck recovery, CrowdPenalty for worker resource spreading, F3 debug overlay for AI score visualization, pathfinding audit with 9 bug fixes (timer accuracy, off-mesh recovery, phase-through max duration, false-positive guards, throttled enemy SetDestination), worker freeze fixes (seamless node transitions, immediate return-to-base on full inventory), simplified worker stuck detection, commit-based worker resource selection, movement smoothing for all units, staggered per-frame checks, preloaded audio (no mid-game disk loads), non-carving resource nodes, smart wall connections, simplified trigger-based gate system, building selection menu, defensive structures, visual effects, 3D spatial audio, housing management, optimized pathfinding, and deeply GC-optimized hot paths (zero per-frame allocations across all systems)

### What's Working Now:
- ✅ Complete resource gathering system with autonomous workers (housing-based limits)
- ✅ Building placement with construction system
- ✅ Day/night cycle with visual lighting
- ✅ Enemy spawning and AI (scales with difficulty)
- ✅ Warrior recruitment and combat AI
- ✅ Victory/defeat conditions with full statistics screens
- ✅ Health systems for all units and buildings
- ✅ Smart enemy targeting (prioritizes warriors over buildings)
- ✅ Warrior patrol behavior when idle
- ✅ **Combat visual effects** (attack particles, hit effects, damage numbers)
- ✅ **Visual health bars** above all units with color coding
- ✅ **Death effects** with particle bursts and fade-out animations
- ✅ **Audio system** (AudioManager with music + ambient sounds working)
- ✅ **Day/night music transitions** (smooth crossfades)
- ✅ **Ambient nature sounds** (birds, crickets - separate from music)
- ✅ **Camera shake effects** (hits and deaths)
- ✅ **3D Spatial Audio** - Directional sound that fades with distance
- ✅ **Victory/Defeat sounds** - Triggered on game end
- ✅ **Combat music** - Plays when enemies spawn
- 🔄 **Combat/UI sound effects** (system ready, audio clips needed)
- ✅ **Population & Housing System** - Workers require housing to recruit
- ✅ **Dynamic worker capacity** - Scales with buildings (Campfire: 3 slots, Huts: 2 slots each)
- ✅ **Population UI display** - Shows "Workers: X/Y" with color coding
- ✅ **Building Selection System** - Press 1-4 to switch between building types
- ✅ **BuildingDatabase** - ScriptableObject-driven building data system
- ✅ **Defensive Structures** - Wooden Wall, Stone Wall, Watchtower placeable
- ✅ **Building Selection UI** - Shows building name, cost, and hotkey hints
- ✅ **NavMesh carving** - Buildings and construction sites properly block pathfinding
- ✅ **Smart Wall Connections** - Walls auto-connect with neighbor-driven shapes (isolated/endcap/straight/corner/T-junction/cross)
- ✅ **WallGrid Singleton** - Central O(1) grid dictionary replaces FindObjectsByType scanning
- ✅ **Procedural Wall Meshes** - Shape-driven geometry generated at runtime (pillar + arms, no overlapping faces)
- ✅ **Ghost Preview Connections** - Wall line preview shows connected shapes before placement
- ✅ **L-Shaped Wall Paths** - Default wall drawing uses L-shaped paths; R toggles X-first vs Z-first
- ✅ **Bresenham Staircase Mode** - Hold Shift during wall line drawing for diagonal staircase paths
- ✅ **Construction Site Grid Registration** - Wall construction sites connect to neighboring walls during build phase
- ✅ **Gate Traversal** - Workers/warriors walk through gates freely; enemies caught by trigger and forced to attack
- ✅ **Nearest-Side Gather Points** - Workers approach stone/resource nodes from the closest side
- ✅ **NavMesh-Validated Spawning** - Units spawn at validated NavMesh positions, not inside obstacles
- ✅ **Warrior Target Hysteresis** - Prevents rapid target switching (1s lock, 30% closer threshold)
- ✅ **Simplified Stuck Detection** - Position-based check every 3s, two consecutive = idle reset (replaced 25-field/6-method system)
- ✅ **Static Entity Registries** - O(1) entity lookups replace per-frame FindObjectsByType scanning
- ✅ **Simplified Gate System** - Walls carve 0.9x0.9 leaving walkable gaps; gates use triggers to catch enemies
- ✅ **Commit-Based Resource Selection** - Workers commit to target until arrival or invalidation, no mid-path switching
- ✅ **Claim System** - ResourceNodes track workers en route for load balancing across nodes
- ✅ **Cached Gather Points** - Gather point calculated once on selection, not every frame
- ✅ **NavMesh.CalculatePath Throttle** - Max 2 path calculations per frame across all enemies
- ✅ **Warrior SetDestination Throttle** - Max 3 path requests per frame across all warriors
- ✅ **Staggered AI Timers** - Enemy/warrior retarget timers randomized to prevent same-frame spikes
- ✅ **GC Allocation Elimination** - Zero per-frame heap allocations in hot paths (health text, state text, GetComponent, NavMeshPath, LINQ, materials)
- ✅ **Deep GC Optimization** - AudioManager pooled cooldowns, NavMeshPath.GetCornersNonAlloc, Camera.main caching, CachedHealth properties, ResourceUI dirty checking
- ✅ **Warrior Null Reference Fix** - Destroyed enemy transform no longer crashes Warrior.Update()
- ✅ **Movement Smoothing** - Reduced acceleration/angular speed for workers and warriors (less jitter)
- ✅ **Path Invalidation Grace Period** - Workers wait 0.5s before resetting on invalid path (NavMesh carving stabilization)
- ✅ **Phase-Through Min Duration** - Workers and warriors stay phased for 3s minimum (prevents oscillation)
- ✅ **Staggered Per-Frame Checks** - Worker stuck/phase-through checks spread across 5 frames (30 workers → ~6 per frame)
- ✅ **Randomized Stuck Timers** - Worker stuck check intervals start at random offsets to prevent synchronized spikes
- ✅ **Lambda Allocation Fix** - ResourceNode.GetClaimCount() uses manual loop instead of delegate-allocating RemoveAll
- ✅ **Cached EnemySpawner** - Enemy.Die() uses static cached reference instead of FindFirstObjectByType scene scan
- ✅ **Cached NavMeshObstacle** - ResourceNode.GetGatherPoint() uses cached component instead of per-call GetComponent
- ✅ **Enemy Agent Cache Cleanup** - Warrior's enemy NavMeshAgent cache clears periodically to prevent memory leak
- ✅ **Audio Clip Preloading** - All 18 audio clips force-loaded into memory at startup (eliminates 184ms synchronous disk loads during music crossfades)
- ✅ **Resource Node Carving Disabled** - Resources use local avoidance instead of NavMesh carving (eliminates constant NavMesh rebuilds from resource depletion/respawn)
- ✅ **Utility AI System** - Scoring-based decision-making replaces hardcoded state machines for Workers, Warriors, and Enemies
- ✅ **AIBrain Evaluation Loop** - Staggered 0.3s evaluation with max 5 evals/frame throttle, zero GC
- ✅ **Multiplicative Consideration Scoring** - Actions scored by multiplying response-curved inputs (health, distance, threat, resources, time-of-day)
- ✅ **Warrior Intercept Formation** - Warriors rally at colony perimeter facing enemy approach direction with lateral spread
- ✅ **Warrior Retreat Behavior** - Warriors fall back to campfire when outnumbered or low health (NEW emergent behavior)
- ✅ **A/B Testing Toggle** - `useUtilityAI` bool on each unit type; old state machines fully preserved behind toggle
- ✅ **Momentum/Hysteresis** - Small bonus to current action prevents rapid flip-flopping between decisions
- ✅ **Commitment Threshold** - Actions must beat current by 20%+ to trigger a switch (the most important tuning knob)
- ✅ **ForceReeval Bypass** - ForceReeval() skips both per-unit timer and per-frame throttle for instant response
- ✅ **Damage ForceReeval** - First hit on any unit triggers immediate AI re-evaluation (workers flee, warriors re-engage)
- ✅ **Ally Death ForceReeval** - Nearby warrior death triggers re-evaluation for workers (20m) and warriors (25m)
- ✅ **Wall Break ForceReeval** - Warriors immediately re-evaluate when walls/gates are destroyed
- ✅ **Enemy StuckResolver** - Enemies in utility AI mode now have phase-through stuck recovery (was missing)
- ✅ **CrowdPenalty Wiring** - Workers spread across resource nodes (4+ workers on one node = max penalty)
- ✅ **AI Debug Overlay** - F3 toggles IMGUI panel: click unit to see action scores, consideration breakdowns, action history (editor only)
- ✅ **StuckResolver Timer Fix** - Staggered checks now use elapsed-time tracking instead of Time.deltaTime (timers were 5x too slow)
- ✅ **Off-Mesh Recovery** - Units that fall off NavMesh auto-warp back via SamplePosition (campfire fallback)
- ✅ **Phase-Through Max Duration** - 10s hard cap prevents permanent phase-through when path is lost mid-phase
- ✅ **Stationary Phase-Through Guard** - Warriors at defend/retreat/intercept positions no longer false-trigger phase-through
- ✅ **Enemy SetDestination Throttle** - All enemy executors routed through AINavHelper (8/frame cap, was unthrottled)
- ✅ **BreachWall IsTargetAlive Fix** - Re-fetches Health component on null instead of returning true (prevents permanent lock)
- ✅ **Worker Node Transition** - GatherExecutor seamlessly picks up next best resource when current node depletes mid-gather
- ✅ **Worker Return-to-Base Fix** - Full workers immediately start walking home instead of freezing until brain re-evaluates
- ✅ **NavMesh-Validated Fallback Spawn** - BaseBuilding fallback spawn position validated with SamplePosition
- ✅ **ReturnUrgency Compound Scoring** - Workers gather full 5/5 before returning (carry^15 curve), return early only under enemy/night pressure
- ✅ **Delivery Race Condition Fix** - Delivery check runs before path-retry, stoppingDistance reduced during return, timer-based fallback prevents stuck workers
- ✅ **Drop-Off Efficiency** - Workers with 50%+ carry drop off at nearby base before running to far resource nodes
- ✅ **ReturnUrgency No-Resource Fallback** - Workers with partial carry return immediately when all resource nodes depleted (was stuck due to carry^15 ≈ 0)
- ✅ **Executor ForceReeval** - Brain reference on blackboard lets executors trigger instant re-evaluation (delivery complete, heading to base)
- ✅ **Flee Stuck Fix** - ThreatNearby yShift changed to 0 so Flee properly zeroes out when no enemies present (momentum can't keep dead action alive)
- ✅ **Enemy Death ForceReeval** - Workers within 30m of a dying enemy instantly re-evaluate (stop fleeing when threats cleared)
- ✅ **Delivery Distance Tightening** - Fallback delivery checks use additive offsets instead of multipliers (stays tight at any deliveryDistance setting)

### Known Issues & Bugs:
1. **Warrior Stuttering** ✅ FIXED (Phase 5.3 + Phase 6.3)
   - Warriors were jittering when chasing moving enemies
   - **Fix Applied:** Destination update threshold increased to 3.0m
   - Target-switching hysteresis: won't switch if target held < 1s, new target must be 30%+ closer
   - Smooth movement when tracking targets

2. **Warriors Standing Idle** ✅ FIXED
   - Warriors previously stood still when no enemies present
   - **Fix Applied:** Implemented patrol behavior
   - Warriors now patrol 8m radius around campfire (or near walls when walls exist)
   - Wait 3 seconds at each random patrol point

3. **Warrior Attack Range Too Short** ✅ FIXED
   - Warriors had to be dangerously close (3.0m) to attack
   - **Fix Applied:** Increased to 4.5m attack range
   - Better stopping distance (3.5m) for smoother engagement

4. **Enemies Ignoring Warriors** ✅ FIXED
   - Enemies previously went straight for buildings
   - **Fix Applied:** Complete priority system rewrite
   - Priority: Warriors > Buildings > Campfire
   - Enemies now engage warriors in combat first

5. **Gate Pathfinding Issues** ✅ FIXED (Phase 6.7)
   - Workers/warriors couldn't path through gates (ignored them completely)
   - **Fix Applied:** Walls now carve 0.9x0.9 instead of 1x1, leaving walkable gaps at edges; gates have no NavMeshObstacle; enemies caught by trigger colliders

6. **Worker Stuck Detection False Positives** ✅ FIXED (Phase 6.10)
   - Original system had 4 stuck signals, 3-attempt escalation with waypoint recovery, 25 fields — too complex for the problem
   - **Fix Applied:** Simplified to single position check (moved < 0.5m in 3s?), one bool flag, two consecutive = reset to idle. Phase-through handles soft stucks.

7. **Stone Node Wrong-Side Pathing** ✅ FIXED (Phase 6.3)
   - Workers would path to the far side of stone nodes instead of the nearest approach
   - **Fix Applied:** `GetGatherPoint()` on ResourceNode calculates nearest-side approach; stone obstacle radius reduced from 0.8 to 0.5

8. **Units Stuck Behind Campfire on Spawn** ✅ FIXED (Phase 6.3)
   - Rapidly spawned units could end up inside the campfire's NavMeshObstacle
   - **Fix Applied:** `GetValidSpawnPosition()` validates positions on NavMesh with fallback directions

9. **Worker "Deciding..." Duration Too Long** ✅ FIXED (Phase 6.3)
   - Workers paused 0.8-1.8s between tasks, felt sluggish
   - **Fix Applied:** Reduced to 0.2-0.6s

10. **Visual Border Too Large** ✅ FIXED
    - No-build zone visual overlay was inconsistent across building types
    - **Root Cause:** `Mathf.Round` uses banker's rounding (round to nearest even), causing `Round(2.5)=2` but `Round(3.5)=4`. This made grid snapping at no-build borders inconsistent — campfire borders (at 2.5) blocked placement but hut borders (at 3.5) allowed it
    - **Fix Applied:** Changed `GridSnap.SnapXZ` to use `Floor(x + 0.5)` for consistent rounding behavior
    - **Additional:** Added `visualNoBuildRadius` field to `BuildingData` (tunable per building type in Inspector) and `BaseBuilding` (campfire only). Visual red border is now decoupled from actual no-build validation radius, allowing independent tuning of the visual overlay size per building type

### Phase 5.4 Complete! ✨
**Combat Visual Polish - Finished**
- ✅ Attack particle effects (blue for warriors, red for enemies)
- ✅ Hit feedback with damage numbers and visual flashes
- ✅ Death effects with particle bursts and fade-out
- ✅ Visual health bars with dynamic color coding
- ✅ Performance-optimized particle system (max 10/frame)

### Phase 5.5 Complete! 🎵✨
**Audio System & 3D Spatial Audio - FINISHED**
- ✅ AudioManager system (singleton with volume controls)
- ✅ Music system with smooth crossfades
- ✅ Ambient nature sounds (separate from music)
- ✅ Day/night audio transitions
- ✅ Camera shake effects on combat
- ✅ **3D Spatial Audio System** - Each unit has individual AudioSource
- ✅ **Directional Audio** - Sounds fade based on camera distance
- ✅ **Worker gathering sounds** - Delayed looping (1-2s gaps)
- ✅ **Combat sounds** - Warriors and enemies have spatial audio
- ✅ **Victory/Defeat sounds** - Triggered on game end
- ✅ **Combat music** - Plays when enemies spawn at night
- 🔄 Audio clip assignments - System ready, just needs clip files added

### Phase 6.1 Complete! 🏗️
**Defensive Structures & Building Selection - FINISHED**
- ✅ Building selection system (Press 1-4 to switch: Hut, Wooden Wall, Stone Wall, Watchtower)
- ✅ BuildingDatabase with ScriptableObject-driven data (HutData, WoodenWallData, StoneWallData, WatchTowerData)
- ✅ Building Selection UI panel (shows name, cost, hotkey hints)
- ✅ Wooden Wall (15W + 5S, 150 HP)
- ✅ Stone Wall (10W + 20S, 300 HP)
- ✅ Watchtower (25W + 15S, 200 HP, 25% damage buff aura in 8-unit radius)
- ✅ Gates (Wooden 150 HP, Stone 300 HP) - workers/warriors traverse, enemies must break through
- ✅ NavMesh carving on huts and construction sites (workers path around properly)
- ✅ Stone now has gameplay purpose (defensive structures)

### Phase 6.2 Complete! 🧱
**Wall System Refactor & Smart Connections - FINISHED**
- ✅ WallGrid singleton — central `Dictionary<Vector2Int, MonoBehaviour>` for O(1) wall occupancy lookups
- ✅ Neighbor-driven wall shapes — walls auto-select isolated/endcap/straight/corner/T-junction/cross based on adjacent walls
- ✅ Procedural mesh generation — each shape built from a central pillar + non-overlapping directional arms (no z-fighting)
- ✅ Correct triangle winding — clockwise for Unity front faces, bottom face omitted (no ground z-fighting)
- ✅ Ghost preview connections — wall line preview shows the correct connected shapes in semi-transparent blue
- ✅ L-shaped wall paths — default path mode; press R to toggle X-first vs Z-first direction
- ✅ Bresenham staircase mode — hold Shift while drawing a wall line for diagonal staircase paths
- ✅ Construction sites register with WallGrid — walls connect to sites during construction phase
- ✅ Auto-refresh on place/destroy — placing or destroying a wall updates its neighbors automatically
- ✅ Eliminated all `FindObjectsByType` scanning in wall code — replaced with grid dictionary lookups

### Phase 6.3 Complete! 🐛
**Movement, Targeting, Resource Interaction Fixes - FINISHED**
- ✅ Gate rubber-banding loop fixed (later replaced by trigger-based system in Phase 6.7)
- ✅ Stuck detection reliability improved (gate traversal priority, baseline establishment, gradual counter decay)
- ✅ Stone node hitbox reduced and nearest-side gather point system added
- ✅ Spawn position validation with NavMesh sampling and fallbacks
- ✅ Warrior target-switching hysteresis (1s lock, 30% closer threshold)
- ✅ Warrior destination update threshold increased to 3.0m
- ✅ Worker "Deciding..." duration shortened (0.2-0.6s)

### Phase 6.4 Complete! -- Pathfinding Improvements
**Stuttering Fix, Static Registries & Path-Aware Resources - FINISHED**

**Problem 1: Pathfinding Stuttering**
All entities were calling `FindObjectsByType<T>()` every fraction of a second to find targets. Each call scans the entire scene and allocates a new array. With 10+ enemies, 5+ warriors, and 5+ workers all doing this independently, the cumulative cost caused periodic frame hitches.

**Solution: Static Entity Registries**
- Added `ActiveList` static registry to 9 entity types: Enemy, Warrior, Worker, Wall, Gate, ResourceNode, Watchtower, BaseBuilding (campfire), Hut
- Each entity registers in `Awake()` and unregisters in `OnDestroy()` -- zero per-frame overhead
- All `FindObjectsByType` calls in per-frame code replaced with direct list reads
- `NavMesh.CalculatePath` throttled to max 2 calls per frame across all enemies
- AI retarget/search timers staggered with random initial offsets so entities don't all recalculate on the same frame

**Problem 2: Gate Traversal Issues**
Workers couldn't path through gates at all - they would walk around the entire wall perimeter.

**Solution: Smaller Wall Carving (Phase 6.7)**
- Walls now carve 0.9x0.9 instead of 1x1 - leaves small walkable gaps at grid edges
- Gates have no NavMeshObstacle - fully walkable NavMesh at gate position
- No OffMeshLinks needed - standard NavMesh pathfinding works
- Enemies caught by trigger collider and forced to attack
- Much simpler architecture with normal pathfinding behavior

**Problem 3: Workers Ignore Walls in Distance Calculation**
Workers picked the Euclidean-nearest resource, ignoring walls. A resource on the other side of a wall would be selected even though the walking path was much longer.

**Solution: Path-Aware Resource Selection**
- `FindNearestResource()` now finds top 3 closest resources by Euclidean distance (cheap filter)
- For each candidate, calculates actual `NavMesh.CalculatePath` walking distance
- Picks the resource with the shortest path distance
- Falls back to Euclidean-closest if no complete paths exist
- New `CheckResourceRecheck()` runs every 4 seconds during movement: if a closer resource appears (e.g. placed nearby), the worker switches -- but only if the new path is 20%+ shorter to prevent oscillation

**Files Changed:**
- `Enemy.cs` - Static registry, rewritten FindTarget using typed lists, TryCalculatePath throttle, staggered timers
- `Warrior.cs` - Static registry, all FindObjectsByType replaced, staggered timers
- `Worker.cs` - Static registry, path-distance resource selection, resource re-evaluation timer
- `Wall.cs` - Static registry
- `Gate.cs` - Static registry
- `ResourceNode.cs` - Static registry
- `Watchtower.cs` - Static registry, GetDamageMultiplier uses ActiveList
- `BaseBuilding.cs` - Static registry
- `Hut.cs` - Static registry

### Phase 6.5 Complete! -- GC Allocation Elimination
**Periodic Stuttering Fix - Zero Per-Frame Heap Allocations in Hot Paths - FINISHED**

**Problem: Periodic Stuttering (30s smooth / 7s stutter)**
With ~40 entities, the game was creating ~75+ temporary heap objects per frame from string interpolations, GetComponent lookups, NavMeshPath instantiations, LINQ operations, and Material creations. When the garbage collector ran to reclaim them, it froze the main thread for multiple frames.

**Solution: Systematic GC Pressure Elimination (9 Phases)**

**Phase 1 - Health Text (Highest Impact):**
- Health text `UpdateHealthText()` now only rebuilds when `currentHealth` changes (was every frame for ~40 entities)
- Billboard rotation separated into zero-allocation `BillboardHealthText()` that still runs every frame
- ~40 string allocations/frame eliminated

**Phase 2 - State Text:**
- Enemy/Warrior/Worker state strings only set on state transitions (not every frame)
- `UpdateStateText()` early-outs when `currentState == lastRenderedState`
- Billboard rotation separated from text content updates
- Worker carry amount uses 0.5 threshold to avoid sub-visible rebuilds
- ~20 string allocations/frame eliminated

**Phase 3 - Cache GetComponent<Health>():**
- Enemy and Warrior cache target Health component reference
- Only re-fetched when target actually changes
- ~15 GetComponent calls/frame eliminated

**Phase 4 - Pool NavMeshPath Instances:**
- Enemy and Worker reuse a single `reusablePath` field instead of `new NavMeshPath()` each check
- Enemy: breach check (0.5s) + blocked-path check (2s)
- Worker: `FindNearestResource()` + `CheckResourceRecheck()`

**Phase 5 - Remove LINQ & Pool Lists:**
- Enemy `CleanupStaleEntries()`: LINQ `.Where().ToList()` replaced with manual loop + static buffer
- Enemy `HandleBlockedPath()`: pooled candidate list with `.Clear()` instead of `new List`
- Warrior `GetBuildingPerimeterPatrolPoint()`: pooled building buffer with value tuples
- EnemySpawner: 3 `RemoveAll(lambda)` calls replaced with backward for-loops

**Phase 6 - Cache CombatEffects Material:**
- Single `cachedParticleMaterial` created once in `Awake()`
- Shared across all attack/hit/death particle effects (was creating new Material per effect)

**Phase 8 - Stagger Enemy Despawn at Dawn:**
- Enemies destroyed one per 0.15s instead of all at once
- Prevents NavMesh carving spike + GC spike at dawn transition

**Phase 9 - Guard Debug.Log in Hot Paths:**
- 10+ hot-path Debug.Log calls wrapped in `#if UNITY_EDITOR`
- Eliminates string interpolation allocations in release builds

**Phase 10 - Billboard Rotation Optimization:**
- All billboard rotations (health text, state text) separated from text updates
- Run every frame with zero allocations (pure transform operations)

**Files Changed:**
- `Health.cs` - Only update text on health change; separate billboard from text update; guard Debug.Log
- `Enemy.cs` - Cache target Health; pool NavMeshPath; pool candidate list; remove LINQ; state-only transitions; guard Debug.Log
- `Warrior.cs` - Cache target Health; pool building list; state-only transitions; guard Debug.Log
- `Worker.cs` - Pool NavMeshPath; state text on change only; carry amount threshold
- `CombatEffects.cs` - Cache particle material (shared instance)
- `EnemySpawner.cs` - Stagger enemy despawn; replace lambda RemoveAll with for-loops

### Phase 6.6 Complete! -- Deep GC & Per-Frame Overhead Elimination
**Remaining Simultaneous Stutter Fix - Eliminated Hidden Allocation Sources & Per-Frame Overhead - FINISHED**

**Problem: All Units Freezing Simultaneously for ~0.5 Seconds**
After Phase 6.5 eliminated the most obvious GC sources (health text, state text, NavMeshPath `new`, LINQ, materials), units still stuttered periodically. The key diagnostic clue: ALL units froze at the SAME TIME, which is the signature of a GC collection pause. This meant there were still hidden per-frame heap allocations triggering garbage collection.

**Root Cause Analysis:**
Using the "all freeze simultaneously" clue to identify GC pauses, we found several remaining allocation sources that were invisible to casual inspection:

**Fix 1 - AudioManager Dictionary Clone (HIGHEST IMPACT):**
- `AudioManager.Update()` was creating `new Dictionary<string, float>(soundCooldowns)` EVERY FRAME to iterate cooldowns safely
- Also created `new List<string>()` every frame to collect keys
- This cloned the entire dictionary 60x/sec — the single biggest remaining GC source
- **Fix:** Added pooled `readonly List<string> cooldownKeysBuffer` field, iterate-and-update in-place with no allocations

**Fix 2 - NavMeshPath.corners Property (HIGH IMPACT):**
- `Worker.GetPathDistance()` accessed `path.corners` which allocates a NEW `Vector3[]` on every access (Unity GC trap)
- Called during `FindNearestResource()` (3 path calculations) and `CheckResourceRecheck()` — runs frequently for every worker
- **Fix:** Pre-allocated `Vector3[] cornersBuffer = new Vector3[64]` and switched to `path.GetCornersNonAlloc(cornersBuffer)`

**Fix 3 - Array Initializer Allocation:**
- `ResourceNode[] candidates = { best1, best2, best3 }` in worker resource selection created a new heap array every call
- **Fix:** Pre-allocated `readonly ResourceNode[] candidatesBuffer = new ResourceNode[3]` and assign elements individually

**Fix 4 - Camera.main Caching (5 Scripts):**
- `Camera.main` internally calls `FindObjectWithTag("MainCamera")` — slower than a cached field reference
- Called every frame in billboard rotation across Health.cs, HealthBar.cs, Enemy.cs, Warrior.cs, ConstructionSite.cs
- **Fix:** Added `private Camera cachedCamera` field initialized in `Start()`, used in all billboard methods

**Fix 5 - HealthBar Dirty-Check Optimization:**
- `HealthBar.UpdateHealthBar()` ran unconditionally every frame (updating fill, color, active state)
- **Fix:** Separated billboard rotation (every frame, zero-alloc) from content update (only when health percentage changes). `SetActive()` only called when visibility state actually changes.

**Fix 6 - DayNightCycle OnGUI Caching:**
- `OnGUI()` created `new GUIStyle()` every call (OnGUI runs 2+ times per frame: layout + repaint passes)
- String interpolation rebuilt debug text every call even when values unchanged
- **Fix:** Cached `GUIStyle` (created once on null check), cached debug string (only rebuilt when day/night/time values change)

**Fix 7 - ConstructionSite Text Optimization:**
- Construction progress text rebuilt every frame during building
- **Fix:** Separated billboard from text content. Text only rebuilds when displayed HP integer changes (`lastDisplayedProgressHP`)

**Fix 8 - CachedHealth Properties (7 Entity Types):**
- `GetComponent<Health>()` called during target scanning in Enemy.FindTarget(), Warrior.FindNearestEnemy(), etc.
- **Fix:** Added `public Health CachedHealth => healthComponent;` to Warrior, Enemy, Hut, Watchtower, BaseBuilding, Wall, Gate
- All target-scanning code now uses `entity.CachedHealth` instead of `entity.GetComponent<Health>()`

**Fix 9 - ResourceUI Dirty Checking:**
- `ResourceUI.UpdateUI()` rebuilt resource text strings 10x/sec even when values unchanged
- **Fix:** Added dirty-check fields (`lastWood`, `lastFood`, `lastStone`, `lastPopWorkers`, `lastPopCapacity`). Text only rebuilt when values actually change.

**Fix 10 - Debug.LogWarning Guards:**
- Worker stuck detection had unguarded `Debug.LogWarning()` with string interpolation in hot paths
- **Fix:** Wrapped in `#if UNITY_EDITOR` to eliminate string allocations in builds

**Fix 11 - Warrior Null Reference Fix:**
- `MissingReferenceException` at Warrior.cs line 215: `target.position` accessed on destroyed Transform
- Enemy could be destroyed between `FindNearestEnemy()` setting target and the next line accessing `target.position`
- **Fix:** Added `if (target == null) { hasTarget = false; return; }` null check before accessing target.position

**Files Changed (14 total):**
- `AudioManager.cs` - Pooled cooldown key buffer, eliminated per-frame dictionary clone and list allocation
- `Worker.cs` - GetCornersNonAlloc for path distance, pre-allocated candidates buffer, Debug.LogWarning guards
- `DayNightCycle.cs` - Cached GUIStyle and debug string in OnGUI
- `Health.cs` - Cached Camera.main reference
- `HealthBar.cs` - Cached Camera.main, dirty-check health bar updates, separated billboard from content
- `Enemy.cs` - Cached Camera.main, CachedHealth property, use CachedHealth in FindTarget/HandleBlockedPath
- `Warrior.cs` - Cached Camera.main, CachedHealth property, null reference fix for destroyed target
- `ConstructionSite.cs` - Cached Camera.main, dirty-check progress text
- `Hut.cs` - CachedHealth property
- `Watchtower.cs` - CachedHealth property, use cached health in GetDamageMultiplier
- `BaseBuilding.cs` - CachedHealth property
- `Wall.cs` - CachedHealth property
- `Gate.cs` - CachedHealth property
- `ResourceUI.cs` - Dirty checking for all resource and population text

**Prevention Guidelines (How to Avoid Future GC Stutters):**
1. **Never allocate in Update/FixedUpdate** — no `new`, no LINQ, no string interpolation, no `GetComponent`
2. **Watch for Unity property traps** — `NavMeshPath.corners`, `Renderer.material`, `Camera.main` all allocate internally
3. **Use dirty-flag pattern for UI** — only rebuild text/visuals when underlying values actually change
4. **Cache Camera.main** — store in a field during `Start()`, use the cached reference everywhere
5. **Expose CachedHealth** — add `public Health CachedHealth => healthComponent;` to any entity that might be scanned by targeting code
6. **Guard Debug.Log** — wrap hot-path debug calls in `#if UNITY_EDITOR` to avoid string allocations in builds
7. **Pre-allocate buffers** — use `readonly` array/list fields for temporary collections instead of creating new ones
8. **Use Unity Profiler** — Window > Analysis > Profiler, check GC.Alloc column to find hidden allocations

### Phase 6.7 Complete! -- Simplified Gate System
**Gate Pathfinding Fix - Smaller Wall Carving + Trigger-Based Enemy Detection - FINISHED**

**Problem: Workers Couldn't Path Through Gates**
Workers and warriors would completely ignore gates when pathfinding. They would walk around the entire wall perimeter instead of going through the gate. Previous attempts using OffMeshLinks caused oscillation issues.

**Root Cause:**
Walls used NavMeshObstacle with 1x1 carving. Even though gates had no NavMeshObstacle, the adjacent walls carved right up to the gate's grid cell edges, leaving no walkable NavMesh path through the gate area.

**Solution: Smaller Wall Carving + Simple Gate Triggers**

The fix was elegantly simple: make walls carve slightly smaller (0.9 x 0.9) instead of the full 1x1 grid cell. This leaves small walkable gaps at grid cell edges, creating a continuous NavMesh path through gates.

**Wall.cs Changes:**
- Set NavMeshObstacle size to `0.9 x 2 x 0.9` (was 1 x 2 x 1)
- Walls now carve 0.9 units, leaving 0.1 unit walkable gaps at edges
- Adjacent walls don't overlap into gate's grid cell

**ConstructionSite.cs Changes:**
- Same 0.9 x 0.9 carving for wall construction sites
- Only applies to wall-type buildings (checked via `BuildingData.isWall`)

**Gate.cs Changes:**
- No NavMeshObstacle at all - gates don't carve NavMesh
- No OffMeshLinks needed - normal pathfinding works through the gaps
- Simple trigger collider (1.5m x 2m x 1.5m) to detect enemies
- `OnTriggerEnter()` calls `enemy.ForceAttackGate(this)` when enemy enters

**Worker.cs & Warrior.cs Changes:**
- Removed all OffMeshLink traversal code (no longer needed)
- Units just walk through gates normally using standard NavMesh pathfinding

**Enemy.cs Changes:**
- Added `ForceAttackGate(Gate gate)` method
- When enemy physically enters gate trigger, they stop and attack the gate
- Registers with wall target tracking for crowd distribution

**How It Works Now:**
1. **Walls:** Carve 0.9x0.9 NavMesh holes, leaving small walkable gaps at edges
2. **Gates:** No carving at all - the 1x1 gate area is fully walkable NavMesh
3. **Workers/Warriors:** Path through gates using normal NavMesh (no special code)
4. **Enemies:** Path toward campfire, physically enter gate trigger, forced to attack
5. **After gate destroyed:** Enemies continue through normally

**Why This Is Better:**
- No OffMeshLinks = no oscillation or warping issues
- No special traversal code = simpler and more reliable
- Workers walk through gates at normal speed, naturally
- Clean separation: NavMesh handles friendlies, trigger handles enemies
- Minimal code changes with maximum effect

**Files Changed:**
- `Wall.cs` - Set NavMeshObstacle size to 0.9 x 2 x 0.9
- `ConstructionSite.cs` - Same smaller carving for wall construction sites
- `Gate.cs` - Removed NavMeshObstacle and OffMeshLinks, kept trigger for enemies
- `Worker.cs` - Removed OffMeshLink traversal code
- `Warrior.cs` - Removed OffMeshLink traversal code
- `Enemy.cs` - Added ForceAttackGate() method

### Phase 6.8 Complete! -- Worker Refactor & Movement Smoothing
**Commit-Based Resource Selection & Anti-Stuttering Fixes - FINISHED**

**Problem 1: Workers Stuttering and Changing Direction Mid-Path**
Workers were stuttering due to three interacting systems:
- `CheckResourceRecheck()` ran every 4s, iterated ALL nodes, calculated NavMesh paths, and switched targets mid-movement
- `FindNearestResource()` used expensive `NavMesh.CalculatePath()` on 3 candidates per search
- `CheckIfReachedBase()` had 3 different distance checks causing oscillation when returning home
- `GetGatherPoint()` was called every frame in `CheckIfReachedResource()`, recalculating NavMesh queries and returning slightly different positions as the worker moved

**Solution: Commit-Based Selection System**
Workers now commit to their target until arrival or invalidation. No mid-path re-evaluation.

**Worker.cs Changes:**
- Removed `resourceRecheckTimer`, `resourceRecheckInterval`, and `CheckResourceRecheck()` method entirely
- Removed `reusablePath`, `cornersBuffer`, `candidatesBuffer` fields and `GetPathDistance()` method
- Simplified `FindNearestResource()` to use Euclidean distance + claim penalty scoring (no NavMesh path calculations)
- Simplified `CheckIfReachedBase()` to single threshold with path completion fallback (was 3 separate distance checks)
- Added `ResetStuckDetection()` helper to consolidate repeated stuck reset logic
- All exit points (`GoIdleAndThink`, `HandleFullReset`, `CheckIfReachedResource`, `OnDestroy`) properly unclaim resources
- Cached gather point on target selection — `GetGatherPoint()` called once, not every frame
- Added path invalidation grace period (0.5s) — NavMesh carving changes no longer trigger immediate reset
- Reduced `acceleration` from 8 to 5 and `angularSpeed` from 180 to 120 for smoother movement
- Increased `stuckCheckInterval` from 2s to 3s to reduce false positives
- Lowered velocity stuck threshold from 0.3 to 0.1 m/s to avoid false triggers in congestion

**ResourceNode.cs Changes:**
- Added `claimedWorkers` list to track workers heading TO a node (not just gathering)
- Added `ClaimNode(Worker)`, `UnclaimNode(Worker)`, `GetClaimCount()` methods
- Claim count used as scoring penalty in `FindNearestResource()` for load balancing (each claimant adds 5 units equivalent distance)

**Problem 2: Warriors Stuttering When Enemies Spawn**
Warriors stuttered badly when enemies appeared because:
- High acceleration (8) and angular speed (180) caused jerky movement
- All warriors searched every 0.3s, causing synchronized SetDestination bursts
- Phase-through system had no minimum duration, causing rapid oscillation between phased/unphased states
- Immediate search after kill (`enemySearchTimer = 0`) caused all warriors to path simultaneously

**Warrior.cs Changes:**
- Reduced `acceleration` from 8 to 5 and `angularSpeed` from 180 to 120
- Increased `enemySearchInterval` from 0.3s to 0.5s to reduce search frequency
- Added phase-through minimum duration (3s) matching Worker — prevents phased/unphased oscillation
- Staggered search after kill: `enemySearchTimer = Random.Range(0, interval * 0.5f)` instead of `= 0`
- Added `TrySetDestination()` throttle: max 3 SetDestination calls per frame across ALL warriors
- All `agent.SetDestination()` calls replaced with throttled `TrySetDestination()` (target acquisition, MoveTowardTarget, PatrolBehavior)

**Files Changed:**
- `ResourceNode.cs` - Added claim system (claimedWorkers list, ClaimNode/UnclaimNode/GetClaimCount)
- `Worker.cs` - Commit-based selection, cached gather points, path grace period, smoother agent config
- `Warrior.cs` - Smoother agent config, phase-through min duration, SetDestination throttle, staggered search, reduced search frequency

### Phase 6.9 Complete! -- Staggered Updates, Audio Preload & NavMesh Stutter Fix
**Profiler-Driven Investigation: Synchronous Audio Loading + NavMesh Recarving Eliminated - FINISHED**

**Problem: All Units Freeze Simultaneously for ~0.5 Seconds**
Despite previous GC elimination passes, workers/warriors/enemies would all stop walking at the same time for about half a second. This happened periodically during normal gameplay, especially with ~18 workers. The stutter occurred with or without walls — just huts and lots of workers was enough to trigger it.

**Investigation Approach: Unity Profiler (Window > Analysis > Profiler)**
Instead of guessing, we used the Unity Profiler to capture the actual stutter frames. This revealed two distinct root causes that previous optimization passes had completely missed:

**ROOT CAUSE 1: Synchronous Audio Loading (184ms freeze — CONFIRMED BY PROFILER)**
The Profiler timeline showed the exact call chain causing the biggest freeze:
```
PlayerLoop (187.58ms)
└─ CoroutinesDelayedCalls (185.07ms)
   └─ EnemySpawner.StartSpawning() [Invoke] (185.00ms)
      └─ AudioManager.CrossfadeMusic() [Coroutine] (184.28ms)
         └─ SoundManager.LoadFMODSound (183.97ms)  ← 184ms disk load!
```
Every time music changed (day→night, night→combat, combat→day), Unity/FMOD loaded the audio clip from disk **synchronously on the main thread**. This froze the entire game for 184ms while the file loaded into memory.

**Fix 8 - Audio Clip Preloading (AudioManager.cs):**
- Added `PreloadAllAudioClips()` method called in `Awake()`
- Forces `AudioClip.LoadAudioData()` on all 18 audio clips (music, ambient, combat, UI, resources) at startup
- All clips loaded into memory before gameplay begins — crossfades are now instant
- Eliminated the 184ms `SoundManager.LoadFMODSound` freeze entirely

**ROOT CAUSE 2: NavMesh Recarving from Resource Nodes (stutter without CPU spike)**
After fixing audio loading, a smaller stutter remained — visible in gameplay but with no big CPU spike in the Profiler. This was caused by **NavMeshObstacle carving on resource nodes**:
- Every resource node had `carving = true` on its NavMeshObstacle
- With 18 workers gathering, resources depleted frequently
- Each depletion: `Destroy(gameObject)` removes carving obstacle → NavMesh recarves
- Each respawn: new resource with `carving = true` → NavMesh recarves again
- Each recarve **invalidated paths for ALL nearby NavMeshAgents**
- Agents briefly stopped walking while recalculating — visible as synchronized stutter
- No single-frame CPU spike because the recarve spread across 2-3 frames

**Why this explained every symptom:**
- Didn't happen early game: 3 workers = slow resource depletion = rare recarves
- Got worse with 18 workers: resources depleting constantly = frequent recarves
- ALL units froze together: NavMesh recarve affects every agent in the area
- Happened without walls: resource nodes were the carving churn source, not walls

**Fix 9 - Disabled Resource Node Carving (ResourceNode.cs):**
- Set `obstacle.carving = false` on all resource nodes
- NavMeshObstacle still exists for **local avoidance** (agents steer around resources at close range)
- Workers already path to offset gather points, so carving was unnecessary
- Eliminated constant NavMesh rebuilds from resource depletion/respawn cycle
- **This was the fix that eliminated the remaining stutter**

**Tradeoff:** Without carving, agents may occasionally plot paths through resource positions and steer around at the last second via local avoidance. In practice this is rarely noticeable because workers use offset gather points and other units don't interact with resources.

**Additional Fixes (from PLAN.md — now fully implemented, PLAN.md removed):**
A detailed investigation was documented in `PLAN.md` identifying optimization phases. All fixes have been implemented and documented here. The standalone PLAN.md file has been removed.

**Fix 1 - GetClaimCount() Lambda Elimination (ResourceNode.cs):**
- Replaced `claimedWorkers.RemoveAll(w => w == null)` with manual backward for-loop
- Zero delegate allocation per call

**Fix 2 - Frame-Staggered Per-Frame Checks (Worker.cs):**
- Added `frameOffset` field assigned from `activeList.IndexOf(this) % 5` in `Initialize()`
- `CheckFaceToFaceStuck()` and `CheckIfStuck()` only run when `(Time.frameCount + frameOffset) % 5 == 0`
- 30 workers spread across 5 frames = ~6 workers checking per frame instead of 30
- Phase-through timer still tracked on off-frames via `TrackPhaseTimer()` to ensure timely phase exit

**Fix 3 - Randomized Stuck Timer Start (Worker.cs):**
- `Initialize()` now sets `stuckTimer = Random.Range(0f, stuckCheckInterval)`
- Workers no longer all hit their 3-second stuck check on the same frame

**Fix 4 - Cached EnemySpawner (Enemy.cs):**
- Added static `cachedSpawner` + `spawnerCached` fields
- `Die()` looks up EnemySpawner once (first death), reuses for all subsequent enemy deaths
- Eliminates `FindFirstObjectByType` scene scan during combat

**Fix 5 - Cached NavMeshObstacle (ResourceNode.cs):**
- Added `cachedObstacle` field initialized in `Start()`
- `GetGatherPoint()` uses `cachedObstacle.radius` instead of `GetComponent<NavMeshObstacle>()`

**Fix 6 - IsTargetAlive() Cache Hit (Enemy.cs):**
- Now checks `cachedTargetHealth` first when the transform matches current target
- Only falls back to `GetComponent<Health>()` for non-current transforms (rare path)

**Fix 7 - Enemy Agent Cache Cleanup (Warrior.cs):**
- `enemyAgentCache` dictionary now clears every 10 seconds
- Prevents unbounded growth from accumulated dead enemy entries

**Key Lesson: Use the Profiler, Don't Guess**
Phases 6.5 through 6.8 spent significant effort optimizing GC allocations, caching GetComponent calls, and staggering timers. While these were valid improvements, the actual stutter was caused by synchronous audio loading (184ms) and NavMesh recarving — neither of which would have been found without the Profiler. **Always profile before optimizing.**

**Files Changed (5 total):**
- `AudioManager.cs` - PreloadAllAudioClips() forces all 18 clips into memory at startup
- `ResourceNode.cs` - Disabled NavMesh carving (local avoidance only), manual loop in GetClaimCount(), cached NavMeshObstacle
- `Worker.cs` - Frame-staggered checks (frameOffset + modulo guard), randomized stuck timer, TrackPhaseTimer()
- `Enemy.cs` - Cached static EnemySpawner reference in Die(), cache-first IsTargetAlive()
- `Warrior.cs` - Periodic enemyAgentCache cleanup (clear every 10s)

### Phase 6.10 Complete! -- Simplified Worker Stuck Detection
**Complexity Reduction: 25 Fields → 14, 6 Methods → 3, -185 Lines - FINISHED**

**Problem: Stuck Detection Grew Too Complex Across 7 Optimization Phases**
The worker stuck detection system accumulated complexity across phases 6.3 through 6.9:
- 4 stuck signals (position, progress/remainingDistance, velocity, path-invalid)
- 3-attempt escalation with random waypoint recovery (save state → move to random position → wait → resume)
- Baseline establishment, gradual decay, unstuck timeouts
- 25 fields and 6 methods dedicated to stuck detection/recovery
- Dead code: `faceToFaceCheckTimer` and `faceToFaceCheckInterval` declared but never read

The core problem is simple — sometimes workers get stuck and need recovery — but the solution didn't match.

**Solution: One Check, One Flag, One Reset**

Replaced the entire multi-signal escalation system with:
1. Every 3 seconds, check if worker moved at least 0.5m from last position
2. If stuck on two consecutive checks (6s total), reset to idle and find a new resource
3. Phase-through handles the soft case (face-to-face stucks resolved without interrupting the task)

**Fields Removed (12):**
`lastRemainingDistance`, `minMoveDistance`, `consecutiveStuckCount`, `maxStuckAttempts`, `stuckCheckReady`, `isUnsticking`, `unstickTimer`, `maxUnstickTime`, `stateBeforeUnstuck`, `targetBeforeUnstuck`, `faceToFaceCheckTimer`, `faceToFaceCheckInterval`

**Field Added (1):** `wasStuckLastCheck` — simple bool, two consecutive = reset

**Methods Removed (3, ~107 lines):**
- `AttemptUnstuck()` — no more waypoint recovery
- `CheckIfReachedUnstuckPosition()` — no more waypoint tracking
- `GetRandomNearbyPosition()` — no more random position sampling

**Methods Simplified:**
- `CheckIfStuck()` — ~85 lines → ~50 lines (one position check, one bool flag)
- `HandleFullReset()` — calls `ResetStuckDetection()` instead of inline field resets, removed unstuck cleanup
- `ResetStuckDetection()` — 4 fields instead of 6
- `ReturnToBase()` — replaced 6-line inline reset with `ResetStuckDetection()` call
- `Update()` — removed `isUnsticking` early-return block
- `CheckFaceToFaceStuck()` — removed `isUnsticking` guard

**What Was Kept Unchanged:**
- Phase-through system (CheckFaceToFaceStuck, TrackPhaseTimer) — already clean, resolves most soft stucks
- Path invalid grace period (0.5s) — prevents false resets during NavMesh recarving
- Frame staggering — 1 int field + modulo check, real benefit for many workers

**Files Changed (1):**
- `Worker.cs` — 1132 → 947 lines (-185 lines), 25 → 14 stuck detection fields, 6 → 3 stuck detection methods

### Phase 6.11 Complete! -- Utility AI System
**Scoring-Based Decision-Making for Workers, Warriors & Enemies - FINISHED**

**Problem: Rigid State Machines with Hardcoded Transitions**
Workers mindlessly gathered the nearest resource regardless of context. Warriors attacked the nearest enemy or patrolled randomly. Enemies followed a fixed Warriors > Buildings > Campfire priority. No unit reacted to context — a worker didn't care about approaching enemies, a warrior didn't retreat when outnumbered, and enemies didn't adapt tactics.

**Solution: Utility AI with Multiplicative Consideration Scoring**
Replaced all three unit AIs with a scoring-based system where units evaluate competing motivations and pick the best action. Each action has multiple Considerations whose scores are multiplied together. The highest-scoring action wins. This produces emergent "lifelike" behavior — no explicit `if (isNight && enemyNearby)` scripting needed.

**Architecture (`Assets/Scripts/AI/`):**
```
AI/
  Core/
    ResponseCurve.cs        — Zero-GC struct: Linear, InverseLinear, Exponential, Logistic, Constant
    Consideration.cs        — Abstract base: one scoring input + response curve → 0-1 output
    ActionOption.cs         — Bundles Consideration[] + ActionExecutor + basePriority + momentumBonus
    ActionExecutor.cs       — Abstract base: OnEnter/OnUpdate/OnExit state machine per action
    AIBlackboard.cs         — Per-unit data cache (references, world state, scratch data)
    AIBrain.cs              — MonoBehaviour: staggered eval loop, picks highest-scoring action

  WorldState/
    AIWorldState.cs         — Singleton: time-of-day cache, 30x30 enemy density grid (10m cells)

  Shared/
    StuckResolver.cs        — Extracted phase-through logic (shared by Worker/Warrior)
    AINavHelper.cs          — Static throttled SetDestination (3/frame) and CalculatePath (2/frame)

  Considerations/           — 11 reusable scoring inputs
    DistanceTo.cs, HealthPercent.cs, TimeOfDay.cs, ResourceCarry.cs,
    ThreatNearby.cs, ResourceAvailability.cs, CrowdPenalty.cs, WallIntegrity.cs,
    EnemyPresence.cs, EnemyProximity.cs, EnemyHasTarget.cs, PathBlocked.cs

  Executors/
    Worker/   — GatherExecutor, ReturnToBaseExecutor, IdleExecutor, FleeToHutExecutor
    Warrior/  — EngageEnemyExecutor, InterceptExecutor, PatrolExecutor, DefendWallExecutor, RetreatExecutor
    Enemy/    — AttackTargetExecutor, BreachWallExecutor

  Debug/
    AIDebugOverlay.cs   — F3 IMGUI panel: action scores, consideration breakdown, action history (#if UNITY_EDITOR)
```

**How the Evaluation Loop Works (AIBrain):**
Every ~0.3s (staggered per unit, max 5 evals/frame):
1. For each ActionOption, multiply all its Consideration scores together
2. Apply momentum bonus if this action is already running (hysteresis)
3. Pick the highest-scoring action
4. If different from current → call `OnExit()` then `OnEnter()`
5. Every frame: call `currentAction.OnUpdate()` for movement/combat/gathering

**Worker Decision-Making:**
| Action | Key Considerations | When It Wins |
|--------|-------------------|--------------|
| **Gather** | Resource quality, inventory space, daytime, low threat | Daytime, empty inventory, resources nearby |
| **Return to Base** | ReturnUrgency (carry^15 + enemy/night/efficiency) | Full inventory, enemy pressure, nightfall, or base closer than next node |
| **Idle** | Constant low score (0.1) | No resources available, nothing else to do |
| **Flee to Hut** | Night approaching, threat level, low health | Dusk/night, enemies spotted (Phase 8 prep) |

**Warrior Decision-Making:**
| Action | Key Considerations | When It Wins |
|--------|-------------------|--------------|
| **Engage Enemy** | EnemyPresence, EnemyProximity (Logistic), health | Enemies close, warrior healthy |
| **Intercept** | EnemyPresence, EnemyProximity (InverseLinear) | Enemies detected but far — rally at perimeter |
| **Defend Wall** | Wall under attack, distance to wall | Enemies hitting walls nearby |
| **Patrol** | No enemies present (InverseLinear on EnemyPresence) | Peacetime — existing patrol behavior |
| **Retreat** | Low health (InverseLinear), high threat | Warrior at 15% HP fighting 3 enemies |

**Enemy Decision-Making:**
| Action | Key Considerations | When It Wins |
|--------|-------------------|--------------|
| **Attack Warrior** | Warrior in detection range | Warriors nearby (preserves existing priority) |
| **Attack Building** | Building proximity, path clear | No warriors, buildings reachable |
| **Breach Wall** | Path blocked, wall score with crowd penalty | Walls blocking path to campfire |
| **Attack Campfire** | Always low baseline (0.3) | Ultimate fallback objective |

**New Emergent Behaviors:**
- **Warrior Intercept Formation:** Warriors rally at the colony edge between base and enemy cluster, forming a defensive line with lateral spread. As enemies close in, the brain naturally switches from Intercept to Engage.
- **Warrior Retreat:** Warriors at low health facing multiple enemies fall back to campfire. This behavior was never explicitly coded in the old system.
- **Context-Sensitive Workers:** A worker with 4/5 wood at dusk near enemies naturally prioritizes returning home because Gather scores drop (inventory penalty + dusk + threat) while Return scores high (inventory fullness).

**Performance Budget:**
- Zero GC: ResponseCurve is a struct, arrays pre-allocated, AIBlackboard created once, no LINQ/delegates
- ~55 units × 5 actions × 4 considerations = 1100 float multiplications per eval cycle ≈ 0.03ms/frame
- Existing throttles preserved in shared AINavHelper
- Enemy density grid rebuilt every 10 frames; O(9) cell lookups replace O(n) distance scans

**Key Bugs Fixed During Implementation:**
1. **Workers not returning to base** — ThreatNearby consideration on Return action returned 0.3 with no enemies, killing the score below Gather's momentum. Fix: removed ThreatNearby from Return, used ResourceCarry(Exponential) alone.
2. **Warrior stuck attacking dead enemy** — IsTargetAlive returned true when Health component was destroyed with GameObject. Fix: re-fetch Health via GetComponent on null, return false if still null.
3. **Choppy warrior targeting** — Brain's 0.3s re-evaluation thrashed OnExit/OnEnter, resetting target each cycle. Fix: internal retarget timer, preserved target across executor re-entry.
4. **bb.nearestEnemy never populated** — No consideration was writing to this field for warriors. Fix: created EnemyPresence consideration that scans and caches nearest enemy.

**Files Created (31 new files in `Assets/Scripts/AI/`):**
- 6 Core files (ResponseCurve, Consideration, ActionOption, ActionExecutor, AIBlackboard, AIBrain)
- 2 WorldState files (AIWorldState)
- 2 Shared files (StuckResolver, AINavHelper)
- 11 Consideration files
- 10 Executor files (4 Worker, 5 Warrior, 2 Enemy — note: 1 consideration PathBlocked also created)

**Files Modified (3):**
- `Worker.cs` — Added `useUtilityAI` toggle, `InitializeUtilityAI()` with action/consideration setup, gated Update() for old state machine, added public sound method wrappers
- `Warrior.cs` — Added `useUtilityAI` toggle, `InitializeUtilityAI()` with redesigned action scoring (Engage/Intercept/Patrol/DefendWall/Retreat), added public sound wrappers
- `Enemy.cs` — Added `useUtilityAI` toggle, `InitializeUtilityAI()`, updated ForceAttackGate() and OnWallOrGateDestroyed() for utility AI path, added public sound wrappers

### Phase 6.12 Complete! -- Utility AI Tuning & Debug
**Commitment Threshold, ForceReeval Triggers, Enemy StuckResolver, CrowdPenalty, Debug Overlay - FINISHED**

**Problem: Missing Critical Tuning & Reactivity Features**
The Utility AI core was working but lacked several features identified as critical in the design plan: any action scoring 0.001 higher would trigger a switch (causing flip-flopping), units didn't react immediately to damage or ally deaths, enemies in utility AI mode had zero stuck recovery, CrowdPenalty existed but wasn't wired to any action, and there was no way to inspect AI scores at runtime.

**Feature 1: Commitment Threshold (20% Override)**
Added `commitmentThreshold = 0.2f` to AIBrain — a new action must score 20%+ higher than the current action to trigger a switch. This is the single most important tuning knob for preventing flip-flopping. The momentum bonus (existing) handles minor score oscillations; the commitment threshold prevents switches where two actions are genuinely close in score.

**Feature 2: ForceReeval Bypass**
`ForceReeval()` now sets a `forceNextEval` flag that bypasses the 5-evals-per-frame throttle. External events (damage, ally death, wall break) get guaranteed immediate re-evaluation regardless of how many other brains evaluated that frame.

**Feature 3: ForceReeval Triggers**
- **First-damage:** All units (Worker, Warrior, Enemy) subscribe to `Health.onDamaged`. A captured `bool hasBeenDamaged` ensures only the first hit triggers re-evaluation (not every hit during combat). Zero per-frame cost.
- **Ally death:** New `Warrior.OnAnyWarriorDied` static event (with `[RuntimeInitializeOnLoadMethod]` cleanup). Workers within 20m and Warriors within 25m re-evaluate — workers may flee, warriors may switch targets.
- **Wall/gate break (Warriors):** Warriors subscribe to `Wall.OnAnyWallDestroyed` and `Gate.OnAnyGateDestroyed` for immediate re-evaluation when defenses fall.

**Feature 4: Enemy StuckResolver**
Bug fix: enemies in utility AI mode had no stuck recovery. Added `StuckResolver` component creation in `Enemy.InitializeUtilityAI()` (same pattern as Worker/Warrior). Added `UpdateMoving()` and `ResetStuckDetection()` calls to both `AttackTargetExecutor` and `BreachWallExecutor`.

**Feature 5: CrowdPenalty Wiring**
The `CrowdPenalty` consideration existed but wasn't in any action's consideration list. Wired it into the Worker's Gather action as the 2nd consideration (after `ResourceAvailability` which populates `bb.bestResource`). Parameters: `maxCrowd = 4f` (4+ workers on same node = max penalty), curve `Linear(0.8f, 0.2f)` (score drops from 1.0 to 0.2).

**Feature 6: AI Debug Overlay (`AI/Debug/AIDebugOverlay.cs`)**
New `#if UNITY_EDITOR` wrapped IMGUI panel for real-time AI score visualization:
- **F3** toggles visibility
- **Left-click** a unit to select it (Raycast → `GetComponentInParent<AIBrain>()`)
- Right-side panel shows: unit name, current action (green), all action score bars, consideration breakdown for active action, action switch history (last 30 seconds)
- Score data stored in AIBrain via `#if UNITY_EDITOR` guarded arrays (pre-allocated, zero GC)
- `Consideration.lastScore` cached after each `Score()` call for debug readback
- Cached GUIStyle and Texture2D objects (created once on null check)
- Singleton auto-created by `AIBrain.Initialize()` via `AIDebugOverlay.EnsureExists()`

**Files Modified (7):**
| File | Changes |
|------|---------|
| `AI/Core/AIBrain.cs` | Commitment threshold, ForceReeval bypass, `#if UNITY_EDITOR` debug score storage + action history |
| `AI/Core/Consideration.cs` | `lastScore` field (`#if UNITY_EDITOR`) set in `Score()` |
| `Enemy.cs` | StuckResolver setup, first-damage ForceReeval in `InitializeUtilityAI()` |
| `Worker.cs` | CrowdPenalty wiring in Gather action, first-damage ForceReeval, ally-death subscription + unsubscribe |
| `Warrior.cs` | `OnAnyWarriorDied` static event + `[RuntimeInitializeOnLoadMethod]` cleanup, first-damage/ally-death/wall-break triggers + unsubscribe |
| `AI/Executors/Enemy/AttackTargetExecutor.cs` | StuckResolver `ResetStuckDetection()` in OnEnter, `UpdateMoving()` in OnUpdate |
| `AI/Executors/Enemy/BreachWallExecutor.cs` | StuckResolver `ResetStuckDetection()` in OnEnter, `UpdateMoving()` in OnUpdate |

**Files Created (1):**
| File | Purpose |
|------|---------|
| `AI/Debug/AIDebugOverlay.cs` | IMGUI debug panel — action scores, consideration breakdowns, action history (editor only) |

### Phase 6.13 Complete! -- Pathfinding Audit & Worker Freeze Fixes
**StuckResolver Timer Accuracy, Off-Mesh Recovery, Phase-Through Guards, Enemy Throttling, Worker Node Transitions - FINISHED**

**Problem: Units Getting Stuck, Freezing, or Phase-Through Misfiring**
A thorough audit of all pathfinding and stuck-resolution code revealed 7 bugs causing units to freeze, trigger phase-through incorrectly, or remain permanently stuck. Two additional worker freeze bugs were discovered during playtesting after the initial fixes.

**Bug 1 (CRITICAL): StuckResolver Timers 5x Too Slow**
`UpdateMoving()` only runs every 5th frame (frame stagger), but timers accumulated `Time.deltaTime` as if running every frame. `faceToFaceTimer`, `stuckTimer`, `pathInvalidTimer` all grew at 1/5 real rate. Phase-through triggered at ~10s instead of 2s, stuck reset at ~30s instead of 6s. **Fix:** Added `lastStaggeredCheckTime` field — on each staggered check, compute `elapsed = Time.time - lastStaggeredCheckTime` and use that instead of `Time.deltaTime`.

**Bug 2 (CRITICAL): False-Positive Phase-Through on Stationary Warriors**
DefendWallExecutor, RetreatExecutor, and InterceptExecutor all called `bb.stuckResolver.UpdateMoving()` unconditionally, even after the unit arrived and stopped. StuckResolver saw zero velocity + nonzero remainingDistance → triggered phase-through on standing warriors. **Fix:** Guard `UpdateMoving()` calls with the arrival condition — only call while still moving toward target position.

**Bug 3 (CRITICAL): Off-Mesh Units Permanently Frozen**
`StuckResolver.UpdateMoving()` returned early with `!agent.isOnNavMesh` — units that fell off NavMesh got zero recovery. Additionally, BaseBuilding's fallback spawn position was NOT NavMesh-validated. **Fix:** Before the early return, attempt `NavMesh.SamplePosition` with 5f radius + `agent.Warp()`. If no valid position, try campfire as last resort. Validated fallback spawn in BaseBuilding.

**Bug 4 (HIGH): BreachWallExecutor IsTargetAlive Returns True on Null Health**
`IsTargetAlive()` returned `true` when `bb.currentTargetHealth == null` but `bb.currentTarget != null`. Enemy stayed locked in BreachWall forever attacking destroyed walls. **Fix:** Re-fetch Health from `bb.currentTarget` via `GetComponent<Health>()` when null. If still null, return false.

**Bug 5 (HIGH): Enemy Executors Bypass SetDestination Throttle**
Both AttackTargetExecutor and BreachWallExecutor called `bb.agent.SetDestination()` directly instead of `AINavHelper.TrySetDestination()`. With 20+ enemies, this caused NavMesh frame spikes. **Fix:** Routed all enemy SetDestination calls through AINavHelper. Increased global limit from 3 to 8 per frame for combat scenarios.

**Bug 6 (HIGH): Phase-Through Has No Max Duration**
`TrackPhaseTimer()` required `velocity > 0.5f` to exit phase-through. If path was lost mid-phase, velocity stayed 0, unit remained phased (tiny radius, no avoidance) permanently. **Fix:** Added `phaseMaxDuration = 10f`. Force restore original radius + priority when timer exceeds max.

**Bug 7 (MEDIUM): Worker Old State Machine Has Same Timer Bug**
Legacy state machine in Worker.cs used the same frame-stagger pattern with `Time.deltaTime` accumulation. Timers grew at 1/5 rate (same root cause as Bug 1). **Fix:** Same elapsed-time approach — track last check time, compute real elapsed on each staggered frame.

**Post-Audit Fix 1: Worker Freeze on Node Depletion**
When a resource node depleted, GatherExecutor set `bb.targetResource = null` and returned silently. If the brain re-selected Gather (another node existed), no OnExit/OnEnter cycle fired — the executor never picked up `bb.bestResource`. Worker froze permanently. **Fix:** Added `TryPickupNewResource()` helper called at all failure points — seamlessly transitions to next best resource when current node depletes.

**Post-Audit Fix 2: Worker Freeze at Full Inventory (5/5)**
Two sub-bugs: (1) `TryPickupNewResource()` didn't check if inventory was full, sending full workers to new nodes instead of home. (2) When full, executor just `return`ed with no path set — worker stood idle for up to 0.3s+ waiting for brain eval. Additionally, ReturnToBaseExecutor didn't retry if OnEnter's SetDestination failed. **Fix:** Added full-inventory guard to `TryPickupNewResource()`. Added `StartHeadingToBase()` helper that immediately sets destination toward base. Added path retry in ReturnToBaseExecutor.OnUpdate.

**Files Modified (10):**
| File | Bugs Fixed |
|------|-----------|
| `AI/Shared/StuckResolver.cs` | #1 (timer accuracy), #3 (off-mesh recovery), #6 (phase-through max duration) |
| `AI/Executors/Warrior/DefendWallExecutor.cs` | #2 (stationary phase-through guard) |
| `AI/Executors/Warrior/RetreatExecutor.cs` | #2 (stationary phase-through guard) |
| `AI/Executors/Warrior/InterceptExecutor.cs` | #2 (stationary phase-through guard) |
| `AI/Executors/Enemy/BreachWallExecutor.cs` | #4 (IsTargetAlive fix), #5 (SetDestination throttle) |
| `AI/Executors/Enemy/AttackTargetExecutor.cs` | #5 (SetDestination throttle) |
| `AI/Shared/AINavHelper.cs` | #5 (increased limit from 3 to 8/frame) |
| `BaseBuilding.cs` | #3 (NavMesh-validated fallback spawn) |
| `Worker.cs` | #7 (legacy state machine timer fix) |
| `AI/Executors/Worker/GatherExecutor.cs` | Post-audit fix 1 & 2 (node transitions, full-inventory return) |

**Also Modified (1):**
| File | Changes |
|------|---------|
| `AI/Executors/Worker/ReturnToBaseExecutor.cs` | Path retry when agent has no path in OnUpdate |

### Phase 6.14 Complete! -- Worker Return Logic & Delivery Fixes
**ReturnUrgency Compound Scoring, Delivery Race Condition Fix, Drop-Off Efficiency - FINISHED**

**Problem: Workers Returning Too Early & Getting Stuck at Campfire**
Workers were returning to base with only 4/5 resources instead of gathering a full 5. The Return action used a simple `ResourceCarry(Exponential(2f))` curve which produced scores high enough to beat Gather at ~70% carry (3.5/5). Additionally, workers would get stuck at the campfire showing "Returning to base" with full inventory — a race condition between the delivery distance check and the path-retry code prevented resources from ever being delivered.

**Fix 1: ReturnUrgency Compound Consideration**
Replaced the single `ResourceCarry(Exponential(2f))` with a new `ReturnUrgency` consideration that combines four signals using `max()`:
- **Base score** (`carryRatio^15`): Very steep curve — only triggers near-full inventory (0.9→0.21, 0.95→0.46, 1.0→1.0). Workers now gather to 5/5 before returning.
- **Enemy pressure** (`carryRatio × threatLevel`): Workers bring home partial resources when enemies are nearby (2+ enemies at half carry triggers return).
- **Night pressure** (`carryRatio × (1-dayProgress)`): Workers bring home partial resources as dusk approaches.
- **Drop-off efficiency** (`carryRatio × (1 - distBase/distResource)`): When carry > 50% and base is closer than the next resource node, workers drop off first instead of running to a far node, gathering 1, and running all the way back.

**Fix 2: Delivery Race Condition**
The `ReturnToBaseExecutor` had a bug where the path-retry code ran before the delivery check, setting `pathPending=true` on the same frame and causing the delivery condition to never trigger. Fixed by:
- Moving delivery checks before retry code
- Reducing agent `stoppingDistance` from 2.0f to 0.5f during return (was tuned for gathering, too far for delivery)
- Restoring `stoppingDistance` on exit so gathering isn't affected
- Adding timer-based fallback (3s generous range, 8s nuclear deliver-from-anywhere)
- Adding `stoppedNearBase` check for agents that stop due to NavMesh carving edges

**New Files (1):**
| File | Purpose |
|------|---------|
| `AI/Considerations/ReturnUrgency.cs` | Compound consideration combining carry fullness, enemy/night pressure, and drop-off efficiency |

**Modified Files (2):**
| File | Changes |
|------|---------|
| `AI/Executors/Worker/ReturnToBaseExecutor.cs` | Delivery race condition fix, stoppingDistance management, timer-based fallback |
| `Worker.cs` | Replaced `ResourceCarry(Exponential(2f))` with `ReturnUrgency(15f, 3f, Linear(1f, 0f))` for Return action |

---

### Phase 6.15 Complete! -- AI Momentum & Stuck State Fixes
**Worker Stuck-at-Base Fix, Flee Stuck Fix, Delivery Distance Tightening - FINISHED**

**Problem 1: Workers Stuck at Base Showing "Returning to base" (F3 Says "Gather")**
When resource nodes depleted while workers had partial carry (e.g. 60%), `GatherExecutor` called `StartHeadingToBase()` and the worker walked to base. But the brain couldn't switch to `ReturnToBase` because:
- `ReturnUrgency` uses `carry^15` — at 60% carry that's 0.00047 (essentially zero)
- The efficiency boost required `bb.bestResource != null`, which was null when all nodes depleted
- Gather's additive momentum bonus (0.15) created an unbeatable score floor
- Worker permanently stuck at base in Gather action, displaying "Returning to base" but never delivering

**Fix:** Three-part solution:
1. **ReturnUrgency no-resource fallback**: When `bb.bestResource == null` and carry > 10%, returns `max(carryRatio, 0.5)` instead of `carry^15`. Ensures Return reliably beats Gather's momentum + commitment threshold.
2. **AIBlackboard brain reference**: Added `brain` field so executors can call `ForceReeval()`. Set during initialization in Worker, Warrior, and Enemy.
3. **ForceReeval on state transitions**: `GatherExecutor.StartHeadingToBase()` and `ReturnToBaseExecutor.DeliverResources()` now trigger immediate re-evaluation instead of waiting up to 0.3s.
4. **Fixed hasPath guard**: Replaced `bb.agent.hasPath` check in `StartHeadingToBase` with a `headingToBase` flag (reset on enter/exit) to prevent stale NavMesh paths from blocking the destination set.

**Problem 2: Workers Stuck Fleeing After Enemies Defeated**
Workers entered Flee action when enemies appeared, but stayed stuck in Flee even after all enemies were killed. Root cause: Flee's `ThreatNearby` consideration used `Linear(0.8, 0.1)` — the `yShift=0.1` meant it returned 0.1 even with zero enemies. This prevented the multiplicative early-out, allowing Flee's 0.2 momentum bonus to create an unbeatable floor score. At night, Gather's low TimeOfDay multiplier couldn't overcome it.

**Fix:**
1. Changed Flee's `ThreatNearby` curve from `Linear(0.8, 0.1)` to `Linear(0.9, 0)` — zeroes out with no enemies, triggering early-out that prevents momentum from keeping Flee alive.
2. Added `Enemy.OnAnyEnemyDied` static event (mirrors `Warrior.OnAnyWarriorDied`), fired in `Die()`.
3. Workers subscribe to `OnAnyEnemyDied` for `ForceReeval` within 30m — instantly re-evaluate when nearby threats are cleared.

**Problem 3: Workers Delivering Resources Too Far from Base**
Delivery fallback checks used multipliers (`deliveryDistance * 3`, `* 4`) which were tuned for the original 3.5 distance but became too generous at tighter values (e.g. 1.5).

**Fix:** Changed from multiplicative to additive offsets (`deliveryDistance + 1.5`, `+ 3`) so fallbacks stay reasonable regardless of delivery distance setting.

**Key Lesson: Additive Momentum + Non-Zero yShift = Stuck Actions**
The Utility AI's additive momentum bonus creates a score floor on the current action. If considerations have `yShift > 0` in their response curves, they never return exactly 0, preventing the early-out optimization. The momentum then makes the action unbeatable even when its considerations have near-zero support. Fix: ensure considerations that should disable an action use `yShift = 0` so they can trigger the `score <= 0.001` early-out.

**Modified Files (7):**
| File | Changes |
|------|---------|
| `AI/Core/AIBlackboard.cs` | Added `brain` reference for executor ForceReeval access |
| `AI/Considerations/ReturnUrgency.cs` | No-resource fallback: returns `max(carryRatio, 0.5)` when `bestResource == null` |
| `AI/Executors/Worker/GatherExecutor.cs` | `headingToBase` flag replaces `hasPath` guard; ForceReeval on heading to base |
| `AI/Executors/Worker/ReturnToBaseExecutor.cs` | ForceReeval after delivery; additive delivery distance offsets |
| `Worker.cs` | Flee ThreatNearby yShift 0.1→0; subscribe to `OnAnyEnemyDied`; set `bb.brain` |
| `Enemy.cs` | Added `OnAnyEnemyDied` static event, fired in `Die()` |
| `Warrior.cs` | Set `bb.brain` reference |

### Next Up:
**Phase 7 - Building Progression** (Future)
- Building upgrades (campfire → fortress)
- Advanced buildings (storage, workshop)
- Tool upgrade system
- Wall repair system (workers repair damaged walls during daytime)
- Repair costs a fraction of original wall resources

---

## 🎯 How to Play

### Starting the Game:
1. **Press Play** in Unity Editor
2. You start with a campfire (your base) and starting resources

### Camera Controls:
- **WASD** - Move camera
- **Q/E** - Rotate camera
- **Mouse Wheel** - Zoom in/out
- **G** - Toggle grid overlay

### Resource Management:
1. Click the **Campfire** to open Worker Assignment UI
2. Click **+** buttons to assign workers:
   - Wood Workers (gather from trees)
   - Food Workers (gather from bushes)
   - Stone Workers (gather from rocks)
3. Click **-** buttons to remove workers
4. Workers require housing (Campfire: 3 slots, Huts: +2 each)
5. Watch resources in top UI bar

### Building:
1. Press **B** to enter build mode
2. Press **1-4** to select building type:
   - **1** = Hut (20W) - housing for 2 workers
   - **2** = Wooden Wall (15W + 5S) - basic defense, 150 HP
   - **3** = Stone Wall (10W + 20S) - strong defense, 300 HP
   - **4** = Watchtower (25W + 15S) - 200 HP, 25% damage buff to nearby warriors
3. Move mouse to position ghost building (green = valid, red = invalid)
4. **Left-click** to confirm placement
5. **ESC** or **Right-click** to cancel
6. Construction takes 5 seconds (auto-completes)

### Combat & Defense:
1. Click **Campfire** to open assignment panel
2. Click **+** in Warriors section to recruit warrior
   - Cost: 10 wood + 15 food each
   - Maximum: 5 warriors
3. Warriors automatically:
   - Patrol 8m around campfire when idle
   - Engage enemies that spawn at night
   - Prioritize nearest threats
   - Attack from 4.5m range

### Day/Night Cycle:
- **Day:** 2 minutes (120 seconds) - safe time to gather and build
- **Night:** 1 minute (60 seconds) - enemies spawn and attack
- Enemies spawn: 3 base + 1 per night (scales with difficulty)
- Enemies despawn automatically at dawn

### Victory & Defeat:
- **Victory:** Survive 5 complete nights
  - Shows comprehensive statistics
  - Options: Continue (sandbox mode) or Quit
- **Defeat:** Campfire destroyed (health reaches 0)
  - Shows performance statistics
  - Options: Restart or Quit

### Strategic Tips:
1. **Early Game:** Focus on wood and food gathering first
2. **Mid Game:** Build 2-3 huts, recruit 2-3 warriors
3. **Defense:** Warriors patrol automatically - they intercept enemies
4. **Positioning:** Build huts within warrior patrol range (8m from campfire)
5. **Economy:** Each warrior costs 10 wood + 15 food - balance carefully
6. **Combat Math:** 1 warrior (75 HP) can defeat ~2 enemies (50 HP each)

---

## ✅ Complete Feature List

### Phase 1: Core Systems ✅
- Camera movement, rotation, zoom
- Grid overlay with toggle
- Grid snapping for buildings
- NavMesh pathfinding (50×50 baked plane)

### Phase 2: Building System ✅
- Building placement mode (B key)
- Ghost preview (cyan valid, red invalid)
- Collision detection
- Construction sites with 5-second timer
- Resource costs (20 wood + 10 food)
- No-build zones (5×5 square around buildings)
- Visual no-build zone overlay
- Perimeter merging for overlapping zones

### Phase 3: Resource System ✅
- Centralized ResourceManager singleton
- Real-time UI display (top bar)
- Resource nodes: 15 trees, 10 bushes, 8 rocks
- Minimum spacing (2m between resources, 5m from campfire)
- Gradual depletion (10 resources per node)
- Visual depletion feedback (nodes shrink)
- Multi-worker gathering support
- Automatic respawning
- Thread-safe resource management

### Phase 4: Worker & AI System ✅
- Worker spawning (NavMesh-validated positions around campfire)
- Worker assignment UI (click campfire)
- +/- buttons for each resource type
- Housing-based worker limits (Campfire: 3, Huts: +2 each)
- NavMesh pathfinding with obstacle avoidance
- Worker state machine (Idle → Search → Move → Gather → Return)
- Carry capacity: 5 resources per trip
- Incremental gathering: 1 resource/second
- Smart resource search (nearest-side gather points)
- 360° campfire access for delivery
- Stuck detection with baseline establishment and gradual decay
- Gates use trigger colliders for enemies (workers/warriors use normal NavMesh pathfinding)
- Phase-through system for face-to-face stuck resolution
- Visual state display with color coding:
  - Gray: "Searching..."
  - Yellow: "Moving to Wood"
  - Green: "Collecting Wood (3.2/5)"
  - Cyan: "Returning to base (5.0 Wood)"

### Phase 5.1: Day/Night & Enemies ✅
- Time system (2 min day, 1 min night, configurable)
- Visual lighting changes (warm day → cool night)
- Sun rotation animation
- Smooth light transitions
- Day/night UI indicator (top center)
- Event system (OnNightStart, OnDayStart)
- Enemy spawning at night (3 base + 1 per night)
- Enemy despawning at dawn
- Enemy prefab (red capsule with NavMeshAgent)
- Enemy health: 50 HP
- Enemy AI pathfinding with obstacle avoidance
- Enemy state display (floating text)
- Enemy movement: 2.0 m/s shambling speed
- Spawn distribution: 30m ring around center
- Spawn timing: 2s delay, 1s intervals

### Phase 5.2: Health & Combat ✅
- Universal Health component
- Configurable max health per object
- TakeDamage() and Heal() methods
- IsAlive property
- Death event system (UnityEvent)
- Floating health text with color coding:
  - Green: >60% health
  - Yellow: 30-60% health  
  - Red: <30% health
- "DESTROYED!" message on death
- Billboard text (always faces camera)
- Enemy attack behavior (10 damage, 1.5s cooldown, 3.5m range)
- Smart enemy targeting:
  - Prioritizes warriors over buildings
  - Attacks nearest building if no warriors
  - Only attacks campfire if very close (<15m) or last target
- Building health:
  - Campfire: 200 HP
  - Huts: 100 HP
- Building destruction mechanics
- NavMeshObstacle carving disabled for combat access

### Phase 5.3: Warriors & Victory/Defeat ✅
- Warrior prefab (blue capsule)
- Warrior recruitment via campfire UI
- Warrior cost: 10 wood + 15 food
- Maximum 5 warriors
- Warrior stats:
  - Health: 75 HP
  - Damage: 15 per attack
  - Attack Range: 4.5m
  - Attack Cooldown: 1.2s
  - Move Speed: 3.5 m/s
- Warrior AI behaviors:
  - **Patrol Mode:** Random patrol points in 8m radius around campfire
  - **Engage Mode:** Smooth pursuit of enemies (updates only when target moves >3.0m)
  - **Attack Mode:** Stops, faces enemy, attacks every 1.2s
- Warrior state display:
  - "Patrolling" (gray)
  - "Engaging Enemy_X" (yellow)
  - "Attacking Enemy_X!" (red)
- Victory screen:
  - Triggers after surviving 5 nights
  - Shows comprehensive statistics:
    - Total nights survived
    - Total enemies killed
    - Settlement size (buildings built)
    - Resources collected (wood, food, stone)
    - Max workers/warriors achieved
  - Continue button (sandbox mode)
  - Quit button
- Defeat screen:
  - Triggers when campfire health reaches 0
  - Shows performance statistics
  - Restart button (reload scene)
  - Quit button
- Game state management (playing, victory, defeat)
- Time pause on victory/defeat

### Combat AI Improvements (Phase 5.3 + 6.3 Polish) ✅
- **Warrior Pathfinding Optimization:**
  - Destination update threshold (3.0m) - reduces path recalculation
  - Target-switching hysteresis (1s lock, 30% closer threshold)
  - Smooth rotation when attacking
  - No more stuttering or jittering
- **Patrol Behavior:**
  - 8m patrol radius around spawn point (or near walls when walls exist)
  - 3-second wait at each patrol point
  - Random patrol point generation
  - Visual feedback: "Patrolling" or "Guarding Walls"
- **Enhanced Attack Range:**
  - Increased from 3.0m to 4.5m
  - Better stopping distance (3.5m)
  - Warriors engage from safer distance
- **Enemy Priority System (Complete Rewrite):**
  - **Priority 1:** Warriors (always attack first if present)
  - **Priority 2:** Buildings/Huts (if closer than campfire)
  - **Priority 3:** Campfire (only if very close or last target)
  - Creates actual unit-vs-unit combat
  - Buildings protected when warriors are present
  - Strategic placement now matters

### Phase 5.4: Combat Visual Effects ✅
- **Attack Effects:**
  - Particle cone bursts when attacking (blue for warriors, red for enemies)
  - 15 particles per attack, 0.5s lifetime
  - Directional cone pointing toward target
  - Color-coded by unit type
- **Hit Effects:**
  - Yellow/orange particle flash on impact
  - Floating damage numbers (-15, -10, etc.)
  - Damage numbers rise and fade over 1 second
  - 10 particles per hit, sphere burst pattern
- **Death Effects:**
  - Large particle explosion on death (25 particles)
  - Color matches unit type (blue warriors, red enemies)
  - Fade-out effect over 0.5 seconds
  - Smooth material transparency transition
- **Health Bars:**
  - Visual quad-based health bars above all units
  - Color-coded: Green (>60%) → Yellow (30-60%) → Red (<30%)
  - Billboard effect (always faces camera)
  - Option to hide when full or dead
  - Left-aligned fill (drains right to left)
- **CombatEffects Manager:**
  - Singleton pattern for global access
  - Performance limits (max 10 particles/frame)
  - Auto-cleanup of particle GameObjects
  - Configurable colors and settings
- **Scripts Added:**
  - CombatEffects.cs (effects manager)
  - HealthBar.cs (visual health display)
  - DamageNumberAnimator.cs (floating text animation)
  - FadeOutEffect.cs (death fade shader)

---

## 🧪 Testing Checklist

### Basic Functionality:
- [ ] Workers spawn and gather resources
- [ ] Building placement works (ghost preview, collision)
- [ ] Resources update in UI in real-time
- [ ] Day/night cycle transitions smoothly
- [ ] Enemies spawn at night and despawn at dawn

### Combat System:
- [ ] Warriors spawn when recruited (blue capsule)
- [ ] Warriors patrol when idle (8m radius)
- [ ] Warriors engage enemies smoothly (no stuttering)
- [ ] Warriors attack from 4.5m range
- [ ] Warriors show correct state text

### Enemy Behavior:
- [ ] Enemies prioritize warriors over buildings
- [ ] Enemies attack nearest building if no warriors
- [ ] Enemies only attack campfire when close or last target
- [ ] Enemy state text shows correctly
- [ ] Enemies deal damage to targets

### Victory/Defeat:
- [ ] Victory screen appears after surviving 5 nights
- [ ] Victory shows correct statistics
- [ ] Continue button works (sandbox mode)
- [ ] Defeat screen appears when campfire destroyed
- [ ] Defeat shows correct statistics
- [ ] Restart button reloads scene
- [ ] Quit buttons work

### Balance Testing:
- [ ] 3-5 warriors can defend against night waves
- [ ] Resource costs feel balanced
- [ ] Combat feels fair (not too easy/hard)
- [ ] Game is winnable with good strategy

### Visual Effects (Phase 5.4):
- [ ] Blue particle cones appear when warriors attack
- [ ] Red particle cones appear when enemies attack
- [ ] Yellow flash effects appear on successful hits
- [ ] Damage numbers float up and fade (-15, -10, etc.)
- [ ] Health bars appear above all units
- [ ] Health bars change color (green → yellow → red)
- [ ] Health bars hide when units are at full health (if enabled)
- [ ] Particle burst appears when units die
- [ ] Units fade out smoothly on death
- [ ] CombatEffectsManager exists in scene hierarchy
- [ ] No excessive particles causing lag (performance check)

### Population System (Phase 6.1):
- [ ] PopulationManager GameObject exists in scene
- [ ] Population text displays "Workers: X/Y" in top UI bar
- [ ] Starting capacity is 3 (from Campfire only)
- [ ] Cannot recruit 4th worker without building a hut
- [ ] Building 1 hut increases capacity to 5 (3 + 2)
- [ ] Population display turns YELLOW when at capacity
- [ ] Worker assignment panel shows correct capacity (not hardcoded 10)
- [ ] Building multiple huts scales capacity correctly (+2 per hut)
- [ ] No worker limit beyond housing capacity
- [ ] Destroying a hut reduces capacity by 2
- [ ] Population display turns RED if workers become homeless
- [ ] Console warns about homeless workers when building destroyed
- [ ] Can recruit up to housing capacity (tested with 10+ workers)

### Walls, Gates & AI Fixes (Phase 6.3 / 6.7):
- [ ] Workers assigned to stone approach from nearest side (not far side)
- [ ] Workers path through gates normally (using standard NavMesh pathfinding)
- [ ] Warriors path through gates normally (no special traversal code)
- [ ] Enemies walking toward campfire get caught by gate trigger and attack gate
- [ ] After gate destroyed, enemies continue through to campfire
- [ ] Walls carve 0.9x0.9 - visible walkable gap at gate edges in NavMesh debug view
- [ ] Spawning 5+ workers rapidly - none stuck behind campfire
- [ ] Spawning 5+ warriors rapidly - none stuck behind campfire
- [ ] Warriors don't rapidly switch targets when multiple enemies present
- [ ] Warriors move smoothly toward enemies (no stuttering/jittering)
- [ ] Workers transition from "Deciding..." quickly (0.2-0.6s, not 1-2s)
- [ ] Watchtower provides 25% damage buff to warriors within 8 units

### GC Allocation Elimination (Phase 6.5):
- [ ] Stuttering: Spawn 5+ workers, wait for night with 5+ enemies -- movement smooth throughout, no periodic freezes
- [ ] Health text: Damage an entity -- health text updates immediately; when not taking damage, text still billboards but no GC allocations
- [ ] State text: Watch enemy/warrior state text -- updates on state transitions, not flickering/rebuilding every frame
- [ ] Dawn transition: When day breaks and enemies despawn -- no freeze, enemies disappear one by one over ~1 second
- [ ] Combat effects: During combat -- particles still appear correctly (attack cones, hit flashes, death bursts)
- [ ] Worker state: Worker carry amount display only updates when carry changes by >= 0.5

### Deep GC & Per-Frame Overhead (Phase 6.6):
- [ ] Simultaneous stutter: Spawn 5+ workers + 5+ enemies -- ALL units move smoothly, no simultaneous half-second freezes
- [ ] AudioManager: Trigger several combat sounds rapidly -- no stutter from cooldown dictionary management
- [ ] Camera billboard: Health bars and state text still face camera correctly (using cached Camera.main)
- [ ] HealthBar update: Damage a unit -- health bar updates immediately; when not taking damage, no unnecessary updates
- [ ] Construction text: Build a structure -- progress text updates as HP increases, no per-frame rebuilds
- [ ] Resource UI: Gather resources -- text updates only when resource count changes, not every 0.1s
- [ ] Warrior target death: Kill an enemy a warrior is targeting -- no MissingReferenceException in console
- [ ] Day/night debug: OnGUI debug text still displays correctly, no visual glitches from caching
- [ ] Worker pathfinding: Workers still pick nearest resource by walking distance (GetCornersNonAlloc working)
- [ ] CachedHealth: Enemy target scanning still works correctly (enemies attack warriors, then buildings)

### Worker Refactor & Movement Smoothing (Phase 6.8):
- [ ] Single worker: Walks directly to nearest resource without changing direction mid-path
- [ ] Multiple workers: Spread across different resource nodes (claim system load balancing)
- [ ] Returning home: Workers deliver smoothly at campfire without oscillation
- [ ] Resource depletion: Worker finds new resource after current one empties
- [ ] Worker destruction: No errors, claim properly released
- [ ] Cached gather point: Worker doesn't recalculate gather point every frame during movement
- [ ] Path invalidation: Worker waits 0.5s before resetting on invalid path (not immediate)
- [ ] Warrior combat: Warriors engage enemies smoothly with no stuttering on spawn
- [ ] Warrior throttle: With 5+ warriors, no frame spike when enemies spawn (max 3 SetDestination/frame)
- [ ] Phase-through: Warriors/workers stay phased for minimum 3s (no rapid oscillation)

### Pathfinding Improvements (Phase 6.4 / 6.7):
- [ ] Gate traversal: Workers/warriors path through gates using normal NavMesh (no warping, no special code)
- [ ] Gate enemy blocking: Enemy approaching gate gets caught by trigger and attacks gate
- [ ] Stuttering: With 10+ enemies and 5+ workers, movement is smooth with no periodic hitches
- [ ] Resource pathing: Place resources on both sides of a wall -- workers on inside pick inside resource
- [ ] Commit-based selection: Worker commits to target, doesn't switch mid-path unless target destroyed
- [ ] Enemy breach detection still works (enemies funnel through destroyed wall sections)
- [ ] Warrior wall patrol still works (warriors guard near walls when walls exist)
- [ ] Watchtower damage buff still works after static list refactor

### Staggered Updates, Audio Preload & NavMesh Stutter Fix (Phase 6.9):
- [ ] Music transitions: Day→night and night→day music crossfades with NO freeze (audio preloaded)
- [ ] Combat music: Enemy spawn triggers combat music with no stutter (was 184ms freeze before)
- [ ] Resource depletion: 18+ workers depleting resources constantly -- no synchronized unit freezing
- [ ] Resource respawn: New resources appearing -- no path invalidation stutter for nearby agents
- [ ] Worker pathing: Workers still path to resources correctly (local avoidance steers around nodes)
- [ ] Synchronized stutter: Spawn 18+ workers -- no simultaneous half-second freezes during gameplay
- [ ] Frame staggering: Workers check stuck/phase-through on different frames (verify with Profiler -- no 30-worker spike)
- [ ] Stuck detection: Workers still detect and recover from stuck positions (may take 1-2 extra frames vs before)
- [ ] Phase-through: Workers still phase through each other after 2s stuck (TrackPhaseTimer keeps phase exit timely)
- [ ] Resource search: Workers still find and commit to nearest resource with claim-based load balancing
- [ ] Enemy death: Multiple enemies dying in combat -- no stutter from EnemySpawner lookup (cached reference)
- [ ] Gather points: Workers still approach resource nodes from nearest side (cached NavMeshObstacle)
- [ ] Wall attack: Enemies still correctly identify and attack walls when path is blocked
- [ ] Enemy agent cache: After multiple nights, no memory growth from dead enemy entries in warrior cache
- [ ] AI behavior: All unit behaviors unchanged -- workers gather, warriors patrol/fight, enemies target correctly

---

## 🎮 Expected Gameplay Flow

### Early Game (Nights 1-2):
1. Assign 3-4 workers to wood
2. Assign 2-3 workers to food
3. Build 1-2 huts
4. Recruit 1-2 warriors before night 1
5. Survive first night with minimal defenses

### Mid Game (Nights 3-4):
1. Expand to 8-10 workers
2. Build 3-4 huts total
3. Recruit 3-4 warriors
4. Warriors defend perimeter
5. Enemies scale up but warriors handle them

### Late Game (Night 5):
1. Strong economy (10+ workers, 5 warriors)
2. 4-5 huts built
3. Strong defensive position
4. Survive final night wave
5. **VICTORY!**

### Defeat Scenario:
1. Not enough warriors recruited
2. Warriors defeated by overwhelming enemies
3. Enemies reach and destroy campfire
4. **DEFEAT SCREEN**

---

## ⚙️ Balancing Guide

### Make Game Easier:
- Increase warrior damage (15 → 20)
- Decrease warrior cost (10 wood → 5 wood)
- Decrease enemy damage (10 → 7)
- Increase campfire health (200 → 300)
- Increase warrior max health (75 → 100)

### Make Game Harder:
- Increase enemy health (50 → 75)
- Increase enemy spawn rate (3 base → 4 base)
- Decrease warrior attack range (4.5 → 3.5)
- Increase nights to survive (5 → 7)
- Increase enemy damage (10 → 15)

### Fine-Tuning:
- **Too many warriors needed:** Decrease warrior cost
- **Warriors die too fast:** Increase warrior HP or reduce enemy damage
- **Enemies too weak:** Increase enemy HP or spawn count
- **Not enough resources:** Increase worker carry capacity or gather rate
- **Building too expensive:** Reduce building costs

---

## 🔧 Technical Architecture

### Core Systems:

**ResourceManager (Singleton):**
- Centralized resource tracking
- Thread-safe operations
- Event-driven UI updates
- Global access via ResourceManager.Instance

**PopulationManager (Singleton):**
- Worker housing capacity tracking
- Dynamic scaling based on buildings
- Housing limit enforcement
- Homeless worker detection
- Global access via PopulationManager.Instance

**Worker AI State Machine:**
```
Idle → SearchForResource → MovingToResource → Gathering → ReturningToBase → Idle
```
- State-driven behavior
- NavMesh pathfinding
- Stuck detection and recovery
- Visual state display

**Warrior AI State Machine:**
```
Patrolling → Engaging → Attacking → Patrolling
```
- Patrol when idle (8m radius)
- Engage when enemy detected (50m range)
- Attack when in range (4.5m)
- Smooth path updates (3.0m threshold)

**Enemy AI:**
```
Searching → MovingToTarget → Attacking → Retargeting
```
- Priority targeting system
- NavMesh pathfinding
- Attack behavior with cooldown
- Retarget when current target destroyed

**GameManager:**
- Night counter and win condition
- Victory/defeat state management
- Statistics tracking
- Scene management

### Pathfinding System:
- Unity NavMesh (50×50 plane)
- NavMeshAgent for all units
- NavMeshObstacle for static objects
- Dynamic obstacle avoidance
- Stuck detection with 8-angle fallback
- Emergency delivery system

### Health System:
- Universal Health component
- Event-driven death system
- Visual feedback (floating text)
- Color-coded health display
- Billboard text effect

---

## 📚 Project Structure

```
Assets/
├── Scripts/
│   ├── Core/
│   │   ├── ResourceManager.cs (singleton)
│   │   ├── PopulationManager.cs (singleton - housing tracker)
│   │   ├── GameManager.cs (game state)
│   │   └── Health.cs (universal health)
│   ├── Building/
│   │   ├── BuildPlacement.cs (ghost preview)
│   │   ├── BuildingPlacement.cs (placement logic)
│   │   ├── ConstructionSite.cs (construction timer)
│   │   └── BaseBuilding.cs (campfire/warrior spawning)
│   ├── Workers/
│   │   ├── Worker.cs (state machine with gate traversal)
│   │   └── WorkerAssignmentUI.cs (UI management)
│   ├── Combat/
│   │   ├── Warrior.cs (warrior AI with patrol + target hysteresis)
│   │   ├── Enemy.cs (enemy AI with priority targeting + breach detection)
│   │   ├── EnemySpawner.cs (night spawning)
│   │   └── DayNightCycle.cs (time/lighting)
│   ├── Defense/
│   │   ├── Wall.cs (wall structures with smart connections)
│   │   ├── Gate.cs (trigger-based gate system - friendlies pass, enemies caught)
│   │   ├── WallGrid.cs (O(1) grid dictionary for wall lookups)
│   │   ├── WallConnector.cs (procedural mesh generation)
│   │   └── Watchtower.cs (damage buff aura)
│   ├── Resources/
│   │   ├── ResourceNode.cs (depletion/respawn + gather points)
│   │   └── ResourceSpawner.cs (auto-respawn system)
│   └── UI/
│       ├── ResourceUI.cs (top bar display)
│       └── VictoryDefeatUI.cs (end screens)
├── Prefabs/
│   ├── Buildings/
│   │   ├── Campfire.prefab
│   │   └── Hut.prefab
│   ├── Units/
│   │   ├── Worker.prefab
│   │   ├── Warrior.prefab (blue capsule)
│   │   └── Enemy.prefab (red capsule)
│   └── Resources/
│       ├── Tree.prefab
│       ├── Bush.prefab
│       └── Rock.prefab
├── Materials/
│   ├── Mat_Worker.mat (green)
│   ├── Mat_Warrior.mat (blue)
│   ├── Mat_Enemy.mat (red)
│   ├── Mat_Campfire.mat (orange)
│   ├── Mat_Hut.mat (brown)
│   ├── Mat_Tree.mat (dark green)
│   ├── Mat_Bush.mat (lime green)
│   └── Mat_Rock.mat (gray)
└── Scenes/
    └── MainScene.unity
```

---

## 🎯 Development Roadmap

### ✅ Phase 1-5.4: COMPLETE
- Core systems (camera, grid, pathfinding)
- Building placement system
- Resource gathering economy
- Worker AI and automation
- Day/night cycle
- Enemy spawning and combat
- Health system
- Warrior recruitment and combat AI
- Victory/defeat conditions
- Full statistics tracking
- Combat AI improvements (no stuttering, patrol, priority targeting)
- **Combat visual effects (attack particles, hit effects, damage numbers)**
- **Visual health bars with color coding**
- **Death effects with particle bursts and fade-out**
- **Performance-optimized particle system**

### ✅ Phase 5.5: Audio & 3D Spatial Audio - COMPLETE!
- ✅ AudioManager system with volume controls
- ✅ Music system with smooth crossfades
- ✅ Ambient nature sounds (separate layer from music)
- ✅ Day/night audio transitions
- ✅ Camera shake effects on combat hits
- ✅ **3D Spatial Audio System** - Individual AudioSource per unit
- ✅ **Directional Audio** - Sounds fade based on camera distance
- ✅ **Worker gathering sounds** - Delayed looping system (1-2s gaps)
- ✅ **Combat sounds** - Warriors and enemies with spatial audio
- ✅ **Victory/Defeat sounds** - Triggered on game end
- ✅ **Combat music** - Plays when enemies spawn at night
- 🔄 Audio clip assignments (system ready, clips needed for full implementation)

### 🔮 Phase 6: Economy Expansion

**Implementation Strategy:** Three major milestones (6.1 → 6.2 → Phase 7)

---

#### **Phase 6.1: Defensive Expansion** ✅ COMPLETE
**Goal:** Make stone useful + add population management

**New Defensive Structures:**
- [x] **Wooden Wall** - Basic perimeter defense (15W + 5S, 150 HP)
- [x] **Stone Wall** - Upgraded defense (10W + 20S, 300 HP)
- [x] **Watchtower** - Defensive tower with 25% damage buff aura (25W + 15S, 200 HP, 8-unit buff radius)
- [x] **Gates** - Wooden (150 HP) and Stone (300 HP) - units traverse, enemies must destroy

**Housing & Population System:**
- [x] **Worker Capacity System** - Campfire: 3 slots, Huts: +2 each, scales infinitely
- [x] **Population Management** - Housing enforcement, homeless detection, UI display

#### **Phase 6.2: Wall System Refactor & Smart Connections** ✅ COMPLETE
**Goal:** Intelligent wall connection system with grid-based management

- [x] WallGrid singleton for O(1) wall lookups
- [x] Neighbor-driven wall shapes (isolated/endcap/straight/corner/T-junction/cross)
- [x] Procedural mesh generation
- [x] Ghost preview connections
- [x] L-shaped and Bresenham staircase wall paths
- [x] Trigger-based gate system for workers/warriors (simplified in Phase 6.7)

#### **Phase 6.3: AI Bug Fixes** ✅ COMPLETE
- [x] Gate rubber-banding loop fix
- [x] Stuck detection reliability improvements
- [x] Stone node nearest-side gather points
- [x] NavMesh-validated spawn positions
- [x] Warrior target-switching hysteresis
- [x] Worker think duration reduction

#### **Phase 6.4: Pathfinding Improvements** ✅ COMPLETE
- [x] Static entity registries (9 types) replacing FindObjectsByType scanning
- [x] Gate traversal improvements (later replaced by trigger-based system in Phase 6.7)
- [x] Path-aware resource selection (NavMesh distance, not Euclidean)
- [x] Resource re-evaluation during movement (every 4s, 20% threshold)
- [x] NavMesh.CalculatePath throttle (max 2/frame)
- [x] AI timer staggering (random initial offsets)

---

#### **Phase 7: Building Progression** (Future)
**Goal:** Add building tiers and meaningful upgrades

**Building Upgrade System:**
- [ ] **Campfire Upgrade Path:**
  - **Campfire → Fire Pit** (50 Wood + 30 Stone)
    - Health: 300 HP (up from 200)
    - Unlocks: +2 max warriors (7 total instead of 5)
    - Visual: Larger fire with stone ring
  - **Fire Pit → Fortress** (100 Wood + 80 Stone)
    - Health: 500 HP
    - Unlocks: +3 max warriors (10 total)
    - Feature: Auto-repairs 5 HP per minute
    - Visual: Stone structure with battlements

- [ ] **Hut Upgrade Path:**
  - **Hut → Longhouse** (40 Wood + 20 Stone)
    - Health: 150 HP (up from 100)
    - Worker capacity: 4 (up from 2)
    - Visual: Longer building footprint
  - **Longhouse → Manor** (80 Wood + 50 Stone)
    - Health: 250 HP
    - Worker capacity: 6
    - Bonus: Workers housed here gather 10% faster
    - Visual: Two-story structure

**Upgrade UI:**
- [ ] Click building → "Upgrade Available" button appears
- [ ] Shows upgrade cost and benefits clearly
- [ ] Upgrade construction takes 10 seconds
- [ ] Building transforms (no demolish needed)

**New Specialized Buildings:**
- [ ] **Storage Shed** - Resource stockpiling
  - Cost: 30 Wood + 10 Stone
  - Health: 100 HP
  - Feature: Increases resource cap from 999 to 1500 per resource
  - Allows stockpiling for big upgrades

- [ ] **Workshop** - Tool upgrades
  - Cost: 50 Wood + 25 Stone
  - Health: 150 HP
  - Unlocks worker tool upgrades:
    - **Stone Tools** (default): 1 resource/second
    - **Iron Tools** (upgrade): 1.5 resources/second (costs 50 stone to research)
    - **Steel Tools** (upgrade): 2 resources/second (costs 100 stone to research)
  - Simplified version of Phase 9's full tech tree

**Strategic Impact:**
- Players choose: upgrade vs expand
- Resource sink for mid-late game
- Stronger buildings = better night survival
- Tool upgrades = economic efficiency boost

---

#### **Phase 7.2: Economic Depth** (Optional Polish)
**Goal:** Add military upgrades and research system
**Estimated Time:** 6-8 hours

**Advanced Buildings:**
- [ ] **Barracks** - Military training facility
  - Cost: 60 Wood + 40 Stone
  - Health: 250 HP
  - Unlocks warrior upgrades (one-time research):
    - **Veteran Training:** +15 HP to all warriors (40 wood + 60 food)
    - **Combat Drills:** +2 damage to all warriors (50 wood + 50 food)
    - **Formation Training:** Warriors patrol in pairs (30 wood + 30 food)
  - Creates choice: stronger warriors vs more warriors

- [ ] **Quarry** - Automated stone production
  - Cost: 80 Wood + 40 Stone
  - Health: 150 HP
  - Feature: Passively generates 1 stone every 5 seconds
  - Requires: 1 worker assigned (stays at quarry)
  - Frees workers for other tasks

**Simple Research System:**
- [ ] **Research UI Panel** - Button on campfire: "Research"
  - Shows available technologies
  - One research at a time (30 seconds to complete)
  - Each tech has cost, description, benefit

**Available Technologies:**
- [ ] **Economy Tier:**
  - Efficient Gathering: Workers gather 20% faster (50W + 50F + 30S)
  - Increased Capacity: Workers carry 7 resources (40W + 40F + 20S)

- [ ] **Military Tier:**
  - Armor Training: Warriors +20 HP (60W + 80F + 40S)
  - Weapon Sharpening: Warriors +3 damage (50W + 70F + 30S)

- [ ] **Building Tier:**
  - Reinforced Construction: All buildings +50 HP (80W + 100S)
  - Quick Construction: Buildings complete in 3s (60W + 40S)

**Strategic Impact:**
- Long-term goals and progression
- Meaningful resource sinks for stockpiled resources
- Replay value (different tech paths)
- Prepares for Phase 9's full tech tree

---

**Phase 6 Summary:**
- **6.1 (Complete):** Walls + Housing system → Stone becomes useful + population management
- **6.2 (Complete):** Wall system refactor + Smart connections + Gate traversal
- **6.3 (Complete):** AI bug fixes (gate rubber-banding, stuck detection, spawn positions, targeting)
- **6.4 (Complete):** Pathfinding improvements (static registries, smooth gates, path-aware resources)
- **6.5 (Complete):** GC allocation elimination → Zero per-frame heap allocations, no more periodic stuttering
- **6.6 (Complete):** Deep GC & overhead elimination → AudioManager dict clone, path.corners, Camera.main, CachedHealth, ResourceUI dirty checks, warrior null ref fix
- **6.7 (Complete):** Simplified gate system → Walls carve 0.9x0.9 leaving walkable gaps, gates use triggers for enemies only
- **6.8 (Complete):** Worker refactor & movement smoothing → Commit-based resource selection, claim system, cached gather points, SetDestination throttle, smoother agent config
- **6.9 (Complete):** Staggered updates, audio preload & NavMesh stutter fix → Profiler-driven: eliminated 184ms synchronous audio loading via preload, disabled resource node NavMesh carving (local avoidance only), frame-staggered worker checks, randomized stuck timers, cached references
- **6.10 (Complete):** Simplified worker stuck detection → Replaced 25-field/6-method escalation system with 1 position check + 1 bool flag; -185 lines
- **6.11 (Complete):** Utility AI system → Scoring-based decision-making for Workers, Warriors, Enemies
- **6.12 (Complete):** Utility AI tuning & debug → Commitment threshold, ForceReeval, CrowdPenalty, debug overlay
- **6.13 (Complete):** Pathfinding audit & worker freeze fixes → 9 bug fixes, node transitions, return-to-base
- **6.14 (Complete):** Worker return logic & delivery fixes → ReturnUrgency compound scoring, delivery race condition fix, drop-off efficiency

**Leads into Phase 7:** Building progression, then Phase 8 worker night behavior

### 🔮 Phase 8: Worker Night Behavior System
- [ ] **Worker Hide Mechanic:**
  - Workers automatically hide in huts at night (default ON)
  - UI toggle button to enable "Risk Mode" (workers continue working at night)
  - Workers without housing die if caught outside at night
  - Housing capacity system (1 hut = 2-4 worker slots)
- [ ] **Enemy Targeting Priority:**
  - Priority 1: Warriors (if in range)
  - Priority 2: Workers (if visible and close)
  - Priority 3: Buildings (huts and structures)
  - Priority 4: Campfire (only if very close or last target)
- [ ] **Archer Units:**
  - Ranged combat AI (attack from distance)
  - Spawned from watch towers
  - Different behavior than melee warriors

### 🔮 Phase 9: Player Character (Naval Admiral) System
**Major Paradigm Shift - From RTS to Action-RTS Hybrid**

**Story Setup:** A naval ship crashes on an uncharted island during an Age of Sail voyage, leaving the Admiral and a handful of sailors stranded on the beach with nothing but what washes ashore from the wreck.

- [ ] **Player Character - Naval Admiral:**
  - Point-and-click movement (like Diablo/RTS heroes)
  - Naval officer character model (Admiral uniform, tricorn hat, naval saber)
  - Basic stats (health, inventory capacity, movement speed)
  - Third-person or isometric camera follow
  - Only character who can learn and teach technology to crew
  
- [ ] **Dual Camera System:**
  - **RTS Mode (Default):** Free WASD camera movement, Q/E rotation, mouse wheel zoom
  - **Follow Mode (Toggle):** Camera locks to and follows player character
  - **Toggle Key:** F key or button to switch between modes
  - Smooth camera transitions between modes
  - Both modes support point-and-click for player movement
  - Follow mode allows WASD for direct character movement (optional)
  - Best of both worlds: manage base (RTS) + control hero (action)
  
- [ ] **Starting Beach Zone:**
  - Large beach area as starting zone (safe spawn point for shipwreck survivors)
  - Shipwreck debris scattered across beach (broken masts, barrels, torn sails)
  - Beach resources spawn naturally:
    - **Driftwood:** Spawns near waterline and tree edges (primary early material, weathered wood from sea)
    - **Small Stones:** Scattered across beach sand (smooth river stones for tool heads)
    - **Berries/Seaweed:** Only harvestable food at start (no tools needed, hand-gathering)
  - **No access to main resources yet:**
    - Trees visible but cannot chop (need axes first)
    - Stone nodes visible but cannot mine (need pickaxes first)
  - Forces initial exploration and scavenging phase
  
- [ ] **Foraging & Interaction (Beach Phase):**
  - Click to move to collectibles on beach
  - **Hand Gathering (No Tools Required):**
    - Pick up driftwood (lying on beach)
    - Pick up small stones (scattered on sand)
    - Gather berries/seaweed by hand (food source)
  - Interaction prompts (press E to gather, craft, etc.)
  - Different from worker AI (player is hands-on explorer)
  - Workers spawn as survivors but cannot work until taught
  
- [ ] **Inventory System:**
  - Grid-based or slot-based inventory UI
  - Resource stacks (driftwood x20, small stones x15, berries x10)
  - Weight/capacity limits (encourages trips back to camp)
  - Transfer to/from base storage (campfire acts as central storage)
  - Separate equipment slots for tools (one tool equipped at a time)
  - Visual indicator of inventory weight/capacity
  
- [ ] **Tool Crafting System (Bootstrap Phase):**
  - **Starting State:** No tools exist, only bare hands
  - **FIRST CRAFT - Campfire (Critical!):**
    - Recipe: 5 Driftwood + 8 Small Stones → Campfire
    - **Must be crafted before anything else can happen**
    - Workers are confused/idle until campfire exists
    - Campfire becomes rally point and command center
    - Unlocks worker assignment UI
    - Acts as base storage and crafting station
    - Survivors automatically gather around once placed
  - **Second Craft - Stone Axe:**
    - Recipe: 2 Driftwood + 3 Small Stones → Stone Axe
    - Allows Admiral to chop standing trees for proper wood logs
    - Slow harvesting speed (1 wood/2 seconds - baseline speed)
    - Unlocks wood economy
  - **Third Craft - Stone Pickaxe:**
    - Recipe: 2 Driftwood + 4 Small Stones → Stone Pickaxe
    - Allows mining stone nodes for building materials
    - Slow harvesting speed (1 stone/3 seconds - baseline)
    - Unlocks stone economy
  - **Resource Conversion:**
    - Driftwood + Stone Axe → Wooden Planks (manual crafting at campfire)
    - Only Admiral can craft initially (workers must be taught)
  
- [ ] **Technology Learning System (The Admiral as Teacher):**
  - **Admiral is the sole source of knowledge:**
    - Only Admiral can discover new technologies
    - Workers are unskilled sailors until taught
  - **Learning Through Crafting:**
    - First time Admiral crafts item = technology discovered
    - Technology unlocked permanently in tech tree
  - **Teaching Workers:**
    - Once Admiral learns recipe, workers can learn it
    - Admiral "teaches" by crafting item once
    - Workers can then craft that item themselves
  - **Technology Tree UI:**
    - Shows locked (grey/unknown) vs unlocked (colored/learned) recipes
    - Clear visual progression path
  - **Examples:**
    - Admiral crafts Stone Axe → All workers can now craft Stone Axes
    - Admiral crafts Stone Pickaxe → All workers can now craft Stone Pickaxes  
    - Admiral crafts Wooden Planks → Sawmill building becomes available
    - Admiral builds Sawmill → Automated plank production unlocked
    - Admiral smelts Iron Ore → Iron tools become craftable
  
- [ ] **Tool Progression (No Durability - Speed Tiers):**
  - **Stone Tools (Tier 1 - Beach Survival):**
    - Stone Axe: Slow tree harvesting (1 wood/2 seconds)
    - Stone Pickaxe: Slow stone harvesting (1 stone/3 seconds)
    - Cost: Driftwood + Small Stones (beach materials)
    - Unlocks: Basic resource gathering
  - **Iron Tools (Tier 2 - Industrial Age):**
    - Iron Axe: Medium harvesting speed (1 wood/1 second) - 2x faster
    - Iron Pickaxe: Medium stone harvesting (1 stone/1.5 seconds) - 2x faster
    - Cost: Iron Ingots + Wood Planks (requires forge)
    - Unlocks: Efficient resource gathering
  - **Steel Tools (Tier 3 - Advanced Metallurgy):**
    - Steel Axe: Fast harvesting (1 wood/0.5 seconds) - 4x faster than stone
    - Steel Pickaxe: Fast stone harvesting (1 stone/0.75 seconds) - 4x faster
    - Cost: Steel Ingots + Hardwood (requires advanced forge)
    - Unlocks: Maximum efficiency
  - **Design Philosophy:**
    - Tools don't break (no durability system) - focus on progression not busywork
    - Workers automatically equip best available tool from inventory
    - Tool upgrades = permanent power increase to colony efficiency
  
- [ ] **Resource Progression Chain (Survival to Civilization):**
  - **Phase 1 (Stranded - Day 1 - Tutorial Phase):**
    - **Shipwreck Event:** Admiral and sailors wash up on beach
    - **Workers Confused:** 5-10 sailors spawn but are idle/confused
      - Walk in circles on beach (random wander behavior)
      - Cannot be commanded (no rally point exists)
      - Text above: "Confused..." or "Lost..."
    - **Admiral's First Task:** Gather materials for campfire
      - Collect 5 Driftwood + 8 Small Stones from beach
      - Craft Campfire (first crafting recipe ever)
    - **Campfire = Command Center:**
      - Once campfire placed, workers automatically rally to it
      - "Found camp!" text appears above workers
      - Workers now wait at campfire for assignments
      - Unlocks worker assignment UI
    - Hand-gather berries/seaweed for initial food
    - No other activities possible until campfire exists
  - **Phase 2 (Stone Age - Days 2-3):**
    - Admiral crafts first Stone Axe
    - Chop standing trees for proper wood logs
    - Convert logs to planks manually
    - Teach workers to craft Stone Axes
    - Basic wooden structures now possible
  - **Phase 3 (Stone Tools - Days 4-5):**
    - Admiral crafts Stone Pickaxe
    - Mine stone nodes for building materials
    - Teach workers to mine stone
    - Build stone structures (walls, foundations)
    - Stone + Wood = Advanced buildings
  - **Phase 4 (Automation - Weeks 2-3):**
    - Build sawmill → Automated plank production
    - Build quarry → Automated stone cutting
    - Workers can focus on other tasks
    - Economy becomes self-sustaining
  - **Phase 5 (Iron Age - Weeks 3-4):**
    - Discover iron ore nodes (deeper inland)
    - Build forge and smelt iron
    - Craft iron tools (2x speed boost)
    - Unlock advanced buildings (barracks, armory)
  - **Phase 6 (Steel Age - Months 2-3):**
    - Build advanced forge with coal
    - Smelt steel from iron
    - Craft steel tools (4x speed - endgame efficiency)
    - Unlock military buildings and units
  
- [ ] **Crafting Menu/UI:**
  - Similar to Minecraft/Valheim/survival games
  - Recipe discovery through Admiral's exploration and experimentation
  - Manual crafting at campfire or specialized workbenches
  - Shows required materials and current inventory
  - Visual feedback when new recipe is learned (popup notification)
  - Filter by category: Tools, Weapons, Buildings, Food, etc.
  - Locked recipes show silhouette with "???" until discovered
  
- [ ] **Dual Gameplay Loop (Admiral + Colony):**
  - **Admiral (Player - Action Role):** 
    - Explore island and discover new areas/resources
    - Forage and gather materials manually
    - Craft tools and items (first to learn recipes)
    - Learn technology through crafting
    - Teach workers new skills/recipes
    - Fight enemies directly (optional combat role)
    - Quest: Discover all technology to fully establish colony
  - **AI Workers (Colony - RTS Role):** 
    - Spawn as shipwreck survivors (5-10 initial sailors)
    - Cannot work until Admiral teaches them skills
    - Automatically gather resources once they have tools
    - Must craft their own tools (Admiral provides blueprints)
    - Need appropriate tools to harvest specific resources
    - Hide in huts at night (safety protocol)
    - Can be assigned to tasks via RTS interface
  - **AI Warriors/Archers (Defense - Auto Role):**
    - Recruit once military tech is learned
    - Defend mode only (no manual control)
    - Auto-patrol perimeter and engage enemies
    - Different unit types from different buildings (barracks, archery range)
    - Protect both workers and Admiral

### 🔮 Phase 10: Art & Visual Upgrade
**When to Replace Primitives with Low Poly Models:**

**Recommended Timing:** After Phase 8-9 (worker night behavior + player character)

**Why Wait:**
- Gameplay systems are more important than visuals early on
- Easier to prototype with primitives (fast iteration)
- Don't waste time on art for systems that might change
- Low poly models require more setup (animations, LODs, materials)

**What to Replace First (Priority Order):**
1. **Player Character** - Most visible, most important (when you add Phase 9)
2. **Workers & Warriors** - Seen constantly, high impact
3. **Buildings** - Campfire, huts, walls (visual identity)
4. **Enemies** - Different types need different models
5. **Resources** - Trees, bushes, rocks (last priority)
6. **Terrain** - Low poly terrain system (ground, water, cliffs)

**Low Poly Asset Recommendations:**
- **POLYGON Starter Pack** (Synty Studios) - Great for prototyping
- **POLYGON Pirates Pack** - Perfect for Age of Sail theme
- **POLYGON Fantasy Kingdom** - Buildings and characters
- **Stylized Nature Pack** - Trees, rocks, plants
- Or make your own in Blender (low poly is beginner-friendly)

**Terrain System Transition:**
- Current: 50×50 flat plane
- Future: Unity Terrain with height painting
- Add: Cliffs, beaches, water, hills for strategic gameplay
- Timing: Phase 10 or later (after core mechanics done)

### 🔮 Phase 11: Content & Polish
- [ ] Replace all primitives with low poly models
- [ ] Particle effects (smoke, fire, dust, magic)
- [ ] Complete sound effects and music
- [ ] Multiple enemy types with unique behaviors
- [ ] Boss waves every 5-10 nights
- [ ] Save/load system
- [ ] Main menu and settings
- [ ] Tutorial system
- [ ] Advanced terrain (hills, water, cliffs)

---

## 💡 Design Philosophy

**Player Experience Goals:**
- Satisfying automation (workers do the work)
- Strategic decision-making (resource allocation)
- Clear visual feedback (always know what's happening)
- Balanced challenge (winnable but requires strategy)
- Tight economy (every resource matters)

**Technical Goals:**
- Clean, modular code
- Performance-conscious (60 FPS target)
- Easy to expand and modify
- Well-documented
- Minimal dependencies

**Development Approach:**
- Functionality first, polish later
- Test frequently (after every change)
- One system at a time
- Keep it simple (KISS principle)
- Iterate based on playtesting

---

## 🛠️ Tools & Dependencies

### Required Unity Packages:
- **Unity 2022.3 LTS** (or newer)
- **AI Navigation** - NavMesh system
- **TextMeshPro** - UI and floating text
- **Universal Render Pipeline (URP)** - Graphics
- **Input System** - Player controls

### Recommended Settings:
- **Script Execution Order:** ResourceManager at -100
- **NavMesh Agent Settings:**
  - Speed: 3.5
  - Angular Speed: 120 (reduced from 180 for smoother movement)
  - Acceleration: 5 (reduced from 8 to prevent jitter)
  - Stopping Distance: 2.5-3.5
- **NavMesh Obstacle Settings:**
  - Carve: Enabled
  - Carve Radius: 1.0-1.5
- **Quality Settings:**
  - Anti-Aliasing: 4x
  - VSync: Enabled
  - Shadow Quality: Medium

### Future Asset Packs:
- POLYGON Pirates Pack (Synty Studios)
- POLYGON Adventure Pack (environments)
- Audio packs for combat and ambience

---

## 📊 Statistics & Balancing

### Current Combat Balance:

**Warriors:**
- Health: 75 HP
- Damage: 15 per hit
- Attack Speed: 1.2s cooldown
- DPS: 12.5
- Time to kill enemy: ~4 seconds (4 hits)

**Enemies:**
- Health: 50 HP
- Damage: 10 per hit
- Attack Speed: 1.5s cooldown
- DPS: 6.67
- Time to kill warrior: ~11 seconds (8 hits)

**Math:**
- 1 warrior can defeat 2 enemies before dying
- Recommend 3-5 warriors for 5-night survival
- Each night spawns: 3 base + (night number) enemies
- Night 5 = 8 enemies total

**Economy:**
- Worker costs: Free (limited by housing capacity)
- Warrior costs: 10 wood + 15 food
- Hut cost: 20 wood + 10 food
- Starting resources: 100 wood, 50 food, 0 stone
- 5 warriors = 50 wood + 75 food
- Leaves resources for 2-3 buildings

**Resource Rates:**
- Gathering: 1 resource/second
- Carry capacity: 5 per trip
- Worker efficiency: ~15-20 resources/minute
- 3 wood workers = 45-60 wood/minute
- Enough for 2 warriors/minute

---

## 📝 Credits

**Built With:**
- Unity Game Engine 2022.3 LTS
- Visual Studio Code
- GitHub for version control

**Current Assets:**
- Unity primitives (cubes, spheres, cylinders, capsules)
- Unity Standard Assets
- TextMeshPro (Unity built-in)

**Developer:**
- Solo project
- Learning Unity development
- Building from scratch

---

**Last Updated:** February 2026
**Current Build:** Phase 6.15 Complete - AI Momentum & Stuck State Fixes
**Status:** Fully Playable with Utility AI (ReturnUrgency compound scoring, drop-off efficiency), Commit-Based Worker AI, Movement Smoothing, Frame-Staggered Checks, Preloaded Audio, Non-Carving Resources, Building Menu, Defensive Structures, Trigger-Based Gate System, Visual Effects, 3D Spatial Audio, Housing Management, Optimized Pathfinding, and Zero Per-Frame GC Allocations
**Known Issues:** None game-breaking - all critical bugs fixed. Visual no-build border size is tunable per building type via `visualNoBuildRadius` in BuildingData. Resource nodes use local avoidance instead of NavMesh carving (agents may occasionally steer around resources at close range rather than pre-planning around them).
**Next Milestone:** Phase 7 - Building Progression (Upgrades)

*A shipwreck survival RTS game built in Unity*

---

## 🎮 Quick Start for New Sessions

1. Open Unity project
2. Open MainScene
3. Press Play
4. Click Campfire → Assign 5-6 workers
5. Build 1-2 huts
6. Recruit 2-3 warriors before night
7. Watch the combat unfold with visual effects!
   - ✨ Blue attack particles from warriors
   - ✨ Red attack particles from enemies
   - 💥 Hit effects with floating damage numbers
   - 💚 Health bars above all units
   - 💀 Death particle bursts
8. Try to survive 5 nights!

**Have fun building your island settlement!** 🏝️⚔️✨

