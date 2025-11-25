# Visual Effects Setup Guide

## Phase 5.4 - Combat Visual Polish Complete!

I've created a complete visual effects system for combat. Here's what's been added and how to set it up in Unity.

---

## New Scripts Created

1. **CombatEffects.cs** - Main effects manager (singleton)
2. **HealthBar.cs** - Visual health bars for all units
3. **Updated Scripts:**
   - Warrior.cs (attack effects)
   - Enemy.cs (attack effects)
   - Health.cs (hit & death effects)

---

## Setup Instructions

### Step 1: Create CombatEffects Manager

1. **Create empty GameObject** in your scene
   - Right-click in Hierarchy → Create Empty
   - Name it: `CombatEffectsManager`

2. **Add CombatEffects script**
   - Select CombatEffectsManager
   - In Inspector, click "Add Component"
   - Search for "Combat Effects"
   - Add the script

3. **Configure settings** (optional, defaults are good):
   - Enable Attack Effects: ✅ (checked)
   - Enable Hit Effects: ✅ (checked)
   - Show Damage Numbers: ✅ (checked)
   - Enable Death Effects: ✅ (checked)
   - Max Particles Per Frame: 10

### Step 2: Add Health Bars to Units

You need to add the HealthBar component to **Warriors**, **Enemies**, and optionally **Buildings**.

#### For Warriors:
1. Open `Assets/Prefabs/Warrior.prefab`
2. Select the Warrior prefab
3. Click "Add Component" → Search "Health Bar"
4. Add HealthBar script
5. Configure:
   - Show Health Bar: ✅ (checked)
   - Height Offset: 2.5
   - Bar Width: 1.0
   - Bar Height: 0.15
   - Hide When Full: ✅ (checked) - optional
   - Hide When Dead: ✅ (checked)

#### For Enemies:
1. Open `Assets/Prefabs/Enemy.prefab`
2. Follow same steps as Warriors
3. Same settings work well

#### For Buildings (Optional):
1. Open Campfire and Hut prefabs
2. Add HealthBar component
3. Configure:
   - Height Offset: 3.0 (buildings are taller)
   - Bar Width: 2.0 (buildings are wider)
   - Hide When Full: ✅ (recommended for clean look)

### Step 3: Test the Effects

1. **Press Play** in Unity
2. **Start a game** and recruit some warriors
3. **Wait for night** - enemies will spawn
4. **Watch the combat!** You should see:
   - ✨ **Blue particle bursts** when warriors attack
   - ✨ **Red particle bursts** when enemies attack
   - 💥 **Yellow/orange flash** when units take damage
   - 🔢 **Floating red damage numbers** (-15, -10, etc.)
   - 💚 **Health bars** above all units (green → yellow → red)
   - 💀 **Death particle burst** when units die
   - 👻 **Fade out effect** as units disappear

---

## What Each Effect Does

### Attack Effects
- **Blue cone particles** shoot from Warriors toward enemies
- **Red cone particles** shoot from Enemies toward targets
- Helps visualize who's attacking who
- 15 particles per burst, lasts 0.5 seconds

### Hit Effects
- **Yellow/orange sphere burst** at impact point
- **Floating damage number** rises up and fades out
- Shows exactly how much damage was dealt
- 10 particles per hit, lasts 0.3 seconds

### Death Effects
- **Colored particle explosion** (blue for warriors, red for enemies)
- **Fade out** of the unit's body over 0.5 seconds
- 25 particles burst outward with slight gravity
- Makes deaths more impactful

### Health Bars
- **Visual bar** showing health percentage
- **Color changes:** Green (>60%) → Yellow (30-60%) → Red (<30%)
- **Hides when full** (optional) for clean look
- **Billboard effect** - always faces camera
- **Left-aligned** fill (drains from right to left)

---

## Performance Settings

The CombatEffects manager includes performance safeguards:
- **Max 10 particles per frame** - prevents lag with many units
- **Auto-cleanup** - particles self-destruct after playing
- **Efficient spawning** - reuses materials and simple geometry

If you experience FPS drops with many units (20+):
1. Reduce "Max Particles Per Frame" to 5
2. Disable "Show Damage Numbers"
3. Increase particle lifetimes to reduce spawning frequency

---

## Customization Options

### Change Effect Colors

In **CombatEffects** component:
- `Warrior Attack Color` - Default: Light Blue (0.3, 0.5, 1.0)
- `Enemy Attack Color` - Default: Red (1.0, 0.3, 0.2)

### Adjust Damage Numbers

- `Damage Number Duration` - How long numbers float (default: 1s)
- `Damage Number Rise Speed` - How fast they rise (default: 2 m/s)
- `Show Damage Numbers` - Toggle on/off

### Tune Health Bars

Per unit in HealthBar component:
- `Height Offset` - How high above unit (2.5 for units, 3.0 for buildings)
- `Bar Width` - Width in world units (1.0 for units, 2.0 for buildings)
- `Bar Height` - Thickness of bar (0.15 default)
- `Hide When Full` - Clean look vs always visible
- `High/Medium/Low Health Color` - Customize gradients

### Death Effect Intensity

In CombatEffects script (line 194-218):
- `main.startLifetime` - How long particles live
- `main.startSpeed` - How fast particles shoot out
- `Burst count` - How many particles spawn (default: 25)
- `gravityModifier` - How much gravity affects particles

---

## Troubleshooting

### No effects appearing?
1. Make sure CombatEffectsManager GameObject exists in scene
2. Check that CombatEffects script is attached
3. Verify "Enable X Effects" checkboxes are ticked
4. Look for errors in Console

### Health bars not showing?
1. Verify HealthBar component is on prefabs (not scene instances)
2. Check "Show Health Bar" is enabled
3. If "Hide When Full" is on, damage the unit first
4. Check height offset isn't too high/low

### Damage numbers not visible?
1. Enable "Show Damage Numbers" in CombatEffects
2. Check camera can see the units
3. Numbers only show for 1 second by default

### Effects look wrong?
1. Try different colors in CombatEffects settings
2. Adjust particle counts (lines in CombatEffects.cs)
3. Tweak health bar sizes and offsets

### Performance issues?
1. Reduce "Max Particles Per Frame" to 5
2. Disable damage numbers
3. Hide health bars when full
4. Check for other performance issues (too many workers, etc.)

---

## Next Steps

After setting this up and testing, you can:
1. ✅ **Balance visual intensity** - adjust colors, sizes, durations
2. 🎵 **Add audio** - attack sounds, hit sounds, death sounds
3. 📊 **Performance profiling** - test with 20+ units fighting
4. 🎨 **Further polish** - screen shake on hits, flash effects, trails

---

## Summary

You've added:
- ✨ Attack particle effects
- 💥 Hit feedback with damage numbers
- 💀 Death effects with fade out
- 💚 Health bars for all units

Total time: ~30-45 minutes to implement all visual effects!

This makes combat feel **100x better** - you can now clearly see:
- Who is attacking
- How much damage is being dealt
- Which units are low on health
- When units die dramatically

Enjoy the enhanced visual feedback! 🎉
