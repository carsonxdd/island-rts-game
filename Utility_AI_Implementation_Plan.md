# Utility AI Implementation Plan

## What This System Does

Replace rigid if/then state machines in Worker, Warrior, and Enemy units with a scoring-based decision system. Every ~0.3 seconds, each unit scores all its possible actions and picks the highest one. Scores come from multiplying several input factors (health, distance, time of day, threat level, etc.) together. The right behavior emerges naturally from the math — no explicit scripting of complex conditions needed.

A worker with 4/5 wood at dusk with enemies nearby doesn't need `if (isNight && enemyNearby && inventoryAlmostFull)`. Instead, "gather" scores low (dusk penalty + threat + full inventory) and "return to base" scores high. The worker just goes home.

---

## Core Architecture (3 Things)

### 1. Scoring Function (ResponseCurve)
- Takes a float input (health %, distance, time of day), outputs a 0–1 score
- Curve types: Linear, Exponential, Logistic, Constant
- Implemented as a struct for zero garbage collection
- **Start everything as Linear. Only change to exponential/logistic when linear doesn't feel right during playtesting.**

### 2. Action (ActionOption)
- Bundles several scoring functions (Considerations) together with an Executor
- Final score = all consideration scores multiplied together
- Has a momentum/hysteresis bonus for the currently running action

### 3. Brain (AIBrain)
- MonoBehaviour attached to each unit
- Every ~0.3s (staggered per unit, max 5 evals per frame), loops through all actions, scores them, picks the highest
- If the winner changed: call old action's OnExit(), new action's OnEnter()
- Every frame: call current action's OnUpdate() for movement/combat/gathering

That's the entire engine. Everything else is content — specific considerations and executors built per unit type.

---

## Critical Implementation Rules

### Hysteresis / Commitment Threshold
**This prevents jittery, indecisive units.** Once an action wins, it stays active until another action scores at least 20% higher. Without this, units flip-flop between similarly scored actions and look broken. This is the single most important tuning knob.

Example: Warrior retreating scores 0.6. Engage ticks up to 0.61. WITHOUT commitment threshold, warrior turns around to fight, gets hit, retreat scores 0.62, turns around again — looks terrible. WITH 20% threshold, engage would need to score 0.72+ to override retreat.

### Forced Re-evaluation
The 0.3s eval interval is fine normally, but some moments need instant response. Build `AIBrain.ForceEvaluate()` and trigger it on:
- Taking damage for the first time
- A nearby allied unit dying
- A wall breaking
- Entering/exiting detection range of a threat

Without this, there's a visible delay where a worker stands gathering happily while an enemy charges at them from 5 meters away.

### Smooth Action Transitions
When a unit switches actions, pay attention to the physical movement. A sharp 180-degree NavMesh snap looks robotic. Let the NavMeshAgent's natural steering handle direction changes — don't teleport or instantly reorient the unit. If transitions look jarring, consider a brief deceleration before the new action takes over.

---

## Debug Overlay (BUILD THIS FIRST)

Before tuning anything, build a debug UI that shows for a selected unit:
- All action scores in real time (bar chart style)
- Which action is currently running (highlighted)
- A short history: what the unit was doing 2-3 seconds ago and what caused the switch
- The individual consideration scores feeding into each action

Example display:
```
[WORKER #3] Current: Return to Base
  Gather:    0.32  ███░░░░░░░  (resource: 0.8, space: 0.6, safety: 0.2, threat: 0.3)
  Return:    0.71  ███████░░░  (inventory: 0.9, threat: 0.8, scarcity: 0.1)
  Flee:      0.45  ████░░░░░░  (night: 0.6, threat: 0.8, health: 0.95)
  Idle:      0.10  █░░░░░░░░░  (constant: 0.1)
  
  History: Gather → Return (2.1s ago, inventory threshold crossed)
```

This is not optional polish. Without it, tuning is guesswork. With it, you look at the numbers and immediately see why a unit did something weird.

---

## Tuning Methodology

### Tune Crossover Points, Not Individual Curves
Don't adjust curves in isolation. Instead, decide the transition moment first, then shape curves to produce it.

Process:
1. Write down what you WANT to happen: "Warrior should retreat at ~25% HP when facing 2+ enemies"
2. Set up that exact scenario in a test scene
3. Watch the scores in the debug overlay
4. Adjust curves until the crossover happens where you want it
5. Verify it didn't break other scenarios

### Scenario Test Checklist
Build these specific situations and verify correct behavior:

**Workers:**
- Full inventory, no threats → returns home
- Half inventory, enemies 20m away → keeps gathering
- Half inventory, enemies 8m away → flees
- Dusk approaching, near base → squeezes in one last trip
- Dusk approaching, far from base → heads home early
- Last resource node on map, multiple workers → they spread out / some idle
- Resource node depletes mid-walk → gracefully re-targets or re-evaluates
- Warrior dies nearby, enemy now free → worker reacts within reasonable time

**Warriors:**
- 80% HP, 1 enemy → fights
- 15% HP, 3 enemies → retreats
- Retreating, passes wall under attack → stays committed to retreat (commitment threshold)
- Two equidistant enemies → picks one and sticks with it (target hysteresis)
- All enemies dead → transitions smoothly to patrol
- Wall under attack, warrior nearby → rushes to defend

**Enemies:**
- Warriors nearby → prioritizes warriors (existing behavior preserved)
- No warriors, buildings reachable → attacks buildings
- Wall blocking path → breaches wall (crowd penalty spreads attackers)
- Almost-broken wall section → enemies concentrate on it
- Wall breaks → smooth transition to next target, no visible hesitation
- Gate far away, wall right here → attacks wall (distance factor outweighs gate preference)

### When Two Actions Score Within 5% of Each Other
This is the #1 source of weird behavior. If the debug overlay shows two actions hovering near the same score, either:
- Increase the commitment threshold for that pair
- Adjust a curve to create more separation in that scenario
- Add a consideration that differentiates them better

---

## Implementation Phases

### Phase A: Core Framework + Debug Overlay
Build AI/Core/ files (ResponseCurve, Consideration, ActionOption, ActionExecutor, AIBlackboard, AIBrain) and AI/WorldState/AIWorldState. Build the debug overlay. Add AIBrain to a test object with dummy actions and verify:
- Evaluation loop works
- Staggered timing works
- Momentum/commitment threshold works
- Debug overlay displays correctly
- Zero GC in Unity profiler

**Do not proceed until the debug overlay is working and showing real-time scores.**

### Phase B: Worker Conversion
1. Implement worker Considerations: DistanceTo, ResourceCarry, ThreatNearby, TimeOfDay, ResourceAvailability, CrowdPenalty
2. Implement worker Executors: GatherExecutor, ReturnToBaseExecutor, IdleExecutor
3. Extract StuckResolver as shared component
4. Modify Worker.cs: add useUtilityAI toggle, AIBrain init, gate the old Update()
5. Start all curves as Linear
6. Run through the Worker scenario checklist using debug overlay
7. Tune crossover points until behavior feels natural
8. A/B compare with old system (toggle useUtilityAI on/off on prefab)

**Do not proceed to Warriors until Workers feel good.**

### Phase C: Warrior Conversion
1. Implement warrior Considerations: HealthPercent, EnemyHasTarget, WallIntegrity
2. Implement warrior Executors: EngageEnemyExecutor, PatrolExecutor, DefendWallExecutor, RetreatExecutor (NEW)
3. Port target hysteresis (1s lock, 30% closer threshold) into EngageEnemyExecutor
4. Port wall-attack bonus (distance *= 0.5) into engage consideration
5. Modify Warrior.cs with toggle
6. Run Warrior scenario checklist
7. Tune — pay special attention to the retreat commitment threshold

### Phase D: Enemy Conversion
1. Implement enemy Considerations: PathBlocked, EnemyHasTarget
2. Implement enemy Executors: AttackTargetExecutor, BreachWallExecutor
3. Preserve static wallTargetCounts coordination in BreachWallExecutor
4. Preserve gate trigger (ForceAttackGate) logic
5. Port wall scoring: dist * (1 + attackers * 0.5), gates at 0.3x
6. Modify Enemy.cs with toggle
7. Run Enemy scenario checklist

### Phase E: Cross-Unit Testing & Polish
1. Run combined scenarios — workers + warriors + enemies all using utility AI
2. Watch for interaction issues:
   - Worker reaction time when nearby warrior dies
   - Enemy target switching when chasing worker past a warrior  
   - Multiple unit types competing for the same evaluation frame budget
3. Verify ForceEvaluate triggers work for critical moments
4. Verify performance: AI eval should be < 0.1ms/frame with 50+ units
5. Verify zero GC in deep profiler
6. Regression test: gate triggers, wall breach events, stuck resolution, audio all still work

### Phase F: Night/Day Integration (Future — Phase 7)
1. Add FleeToHutExecutor to workers
2. TimeOfDay consideration already exists — connect it to flee scoring
3. Tune dusk transition: workers far from base should leave earlier than workers near base
4. Dawn transition: workers trickle out of huts (natural from staggered eval timing)
5. "Risk Mode" toggle: just set FleeToHut basePriority to 0

---

## Performance Budget

- 55 units × 5 actions × 4 considerations = 1,100 float multiplications per eval cycle ≈ 0.03ms/frame
- Staggered evaluation: 0.3s intervals with random offset + max 5 evals/frame
- SetDestination throttled to 3/frame (warriors), CalculatePath 2/frame (enemies) — via shared AINavHelper
- AIWorldState enemy density grid rebuilt every 10 frames; O(9) lookups replace O(n) distance scans
- Zero GC: ResponseCurve is a struct, arrays pre-allocated, no LINQ/delegates/closures
- StuckResolver: frame-staggered checks, extracted as shared component

---

## Existing Code Patterns to Preserve Exactly

These are proven and should be ported into executors unchanged:
- Resource scoring: `distance + (claimCount * 5f)` → GatherExecutor
- Wall scoring: `dist * (1 + attackers * 0.5f)`, gates at `0.3x` → BreachWallExecutor
- Target hysteresis: 1s lock, 30% closer threshold → EngageEnemyExecutor
- Wall-attack bonus: `distance *= 0.5f` → EngageEnemy consideration
- Static wallTargetCounts coordination → BreachWallExecutor static field
- Phase-through stuck detection: velocity check → shared StuckResolver component
- Frame-staggered checks → AIBrain throttle + StuckResolver frameOffset

---

## Key Reminders

1. **Debug overlay is not optional.** Build it in Phase A. Use it constantly.
2. **Start all curves as Linear.** Only change when something feels wrong.
3. **Tune one unit type at a time.** Don't try to balance everything simultaneously.
4. **Tune crossover points, not individual curves.** Decide what should happen first, then adjust to produce it.
5. **The commitment threshold prevents jitter.** If units look indecisive, increase it.
6. **ForceEvaluate for critical moments.** Don't let the 0.3s delay cause obviously wrong behavior.
7. **Keep the A/B toggle.** Always be able to compare old vs new behavior on any unit.
8. **Test specific scenarios repeatedly.** Use a controlled test scene, don't rely on full gameplay to surface edge cases.
