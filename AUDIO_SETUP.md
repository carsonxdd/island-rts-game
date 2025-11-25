# Audio System Setup Guide - Phase 5.5

## Overview

Complete audio system with combat sounds, ambient music, UI sounds, and camera shake effects. All audio is managed through a centralized AudioManager singleton with volume controls and smart cooldowns to prevent sound spam.

---

## New Scripts Created

1. **AudioManager.cs** - Central audio management system
2. **CameraShake.cs** - Screen shake for combat impact
3. **Updated Scripts:**
   - Warrior.cs (attack & death sounds)
   - Enemy.cs (attack & death sounds)
   - Health.cs (hit sound + camera shake)
   - DayNightCycle.cs (day/night music transitions)

---

## Setup Instructions

### Step 1: Create AudioManager GameObject

1. **Create empty GameObject** in your scene
   - Right-click in Hierarchy → Create Empty
   - Name it: `AudioManager`

2. **Add AudioManager script**
   - Select AudioManager GameObject
   - In Inspector, click "Add Component"
   - Search for "Audio Manager"
   - Add the script

3. **AudioManager will auto-create 3 Audio Sources:**
   - Music Source (loops, fades between tracks)
   - Ambient Source (loops, background sounds)
   - SFX Source (one-shots, combat sounds)

### Step 2: Add CameraShake to Camera

1. **Select Main Camera** in hierarchy
2. **Add CameraShake script**
   - Click "Add Component"
   - Search for "Camera Shake"
   - Add the script

3. **Configure shake settings** (optional):
   - Enable Shake: ✅ (checked)
   - Shake Duration: 0.2
   - Light Shake Intensity: 0.05 (hits)
   - Medium Shake Intensity: 0.1 (deaths)
   - Heavy Shake Intensity: 0.25 (explosions)

### Step 3: Add Audio Clips

You need to add audio files to the AudioManager. The system is ready - it just needs the actual sound files.

#### Where to Get Free Audio:

**Recommended Free Sources:**
1. **Freesound.org** - Free sound effects (requires account)
2. **OpenGameArt.org** - Free game audio
3. **Incompetech.com** - Free royalty-free music
4. **ZapSplat.com** - Free sound effects
5. **Unity Asset Store** - Free audio packs

#### Audio Clips Needed:

**Combat Sounds** (short, punchy):
- Warrior Attack Sound - sword swing (0.2-0.5s)
- Enemy Attack Sound - growl/slash (0.2-0.5s)
- Hit Sound - impact/thud (0.1-0.3s)
- Warrior Death Sound - death cry (0.5-1s)
- Enemy Death Sound - monster death (0.5-1s)

**UI Sounds** (short, clean):
- Button Click Sound - click/beep (0.1s)
- Building Placed Sound - construction (0.3-0.5s)
- Worker Assigned Sound - positive chime (0.2s)
- Victory Sound - triumph fanfare (1-3s)
- Defeat Sound - sad horn (1-3s)

**Music** (loops, 1-3 minutes):
- Day Ambient Music - peaceful, calm (loop)
- Night Ambient Music - tense, ominous (loop)
- Combat Music - intense, fast-paced (loop)

**Resource Sounds** (short, thematic):
- Gather Wood Sound - chop/axe (0.3s)
- Gather Food Sound - rustle/pick (0.3s)
- Gather Stone Sound - mining/clink (0.3s)

#### How to Add Audio Clips:

1. **Import audio files** into Unity:
   - Drag audio files into `Assets/Audio/` folder (create if needed)
   - Unity supports: .wav, .mp3, .ogg, .aiff

2. **Assign clips to AudioManager**:
   - Select AudioManager GameObject
   - In Inspector, find the AudioManager component
   - Drag audio files from Project window into the slots:
     - Combat Sounds section
     - UI Sounds section
     - Ambient Sounds section
     - Resource Sounds section

3. **Configure audio import settings** (optional):
   - Select audio file in Project window
   - In Inspector:
     - Load Type: "Streaming" for music, "Decompress On Load" for short SFX
     - Compression Format: Vorbis for music, PCM for SFX
     - Quality: 70-100% for music, 100% for SFX

---

## Features

### Audio System Features

✅ **Combat Audio Integration:**
- Warriors play attack sound when striking
- Enemies play attack sound when striking
- Hit sound plays on all damage
- Death sounds for warriors and enemies
- Smart cooldowns prevent sound spam

✅ **Ambient Music System:**
- Day music plays during daytime
- Night music plays at night
- Smooth crossfade transitions (2 second fade)
- Music loops automatically

✅ **Volume Controls:**
- Master Volume (affects all audio)
- Music Volume (background music only)
- SFX Volume (combat and UI sounds)
- Ambient Volume (environment sounds)
- All adjustable 0-100%

✅ **Performance Optimized:**
- Sound cooldowns prevent spam
- Pooled audio sources (only 3 total)
- Smart fade system (no audio pop)

### Camera Shake Features

✅ **Impact Feedback:**
- Light shake on combat hits (0.05 intensity)
- Medium shake on unit deaths (0.1 intensity)
- Heavy shake option for explosions (0.25 intensity)
- Customizable per event type

✅ **Smooth Shake:**
- Natural shake decay
- Returns to original position
- No permanent camera offset
- Can be toggled on/off

---

## Testing

### Test Audio System:

1. **Press Play** in Unity
2. **Recruit warriors** and wait for night
3. **Listen for:**
   - ⚔️ Sword swing sounds when warriors attack
   - 👹 Growl sounds when enemies attack
   - 💥 Hit sounds on each impact
   - 💀 Death sounds when units die
   - 🎵 Music changes from day to night
   - 📷 Screen shakes on hits and deaths

### If No Sound:

1. **Check AudioManager exists** in scene
2. **Verify audio clips are assigned** in Inspector
3. **Check volume sliders** aren't at 0
4. **Check "Enable Combat Sounds"** is checked
5. **Check Unity's Audio Mixer** isn't muted
6. **Look for errors** in Console

---

## Customization

### Adjust Volume Balance:

In AudioManager Inspector:
- `Master Volume`: Overall game volume (0-1)
- `Music Volume`: Background music (0-1)
- `SFX Volume`: Combat/UI sounds (0-1)
- `Ambient Volume`: Environment sounds (0-1)

### Change Shake Intensity:

In CameraShake Inspector:
- `Light Shake`: Hits, minor events (default: 0.05)
- `Medium Shake`: Deaths, explosions (default: 0.1)
- `Heavy Shake`: Big impacts (default: 0.25)
- `Shake Duration`: How long shake lasts (default: 0.2s)

### Disable Features:

**Disable Combat Sounds:**
- AudioManager → Enable Combat Sounds: ❌ (unchecked)

**Disable Camera Shake:**
- CameraShake → Enable Shake: ❌ (unchecked)

**Disable Music:**
- Set Music Volume to 0 in AudioManager

---

## Audio Integration Points

### Combat Sounds:

**Warrior.cs (line 357-361):**
```csharp
// Play attack sound
if (AudioManager.Instance != null)
{
    AudioManager.Instance.PlayWarriorAttack();
}
```

**Enemy.cs (line 310-314):**
```csharp
// Play attack sound
if (AudioManager.Instance != null)
{
    AudioManager.Instance.PlayEnemyAttack();
}
```

**Health.cs (line 83-93):**
```csharp
// Play hit sound
if (AudioManager.Instance != null)
{
    AudioManager.Instance.PlayHitSound();
}

// Camera shake on hit
if (CameraShake.Instance != null)
{
    CameraShake.Instance.ShakeLight();
}
```

### Music Transitions:

**DayNightCycle.cs (line 139-156):**
```csharp
// Play night music
if (AudioManager.Instance != null)
{
    AudioManager.Instance.PlayNightMusic();
}

// Play day music
if (AudioManager.Instance != null)
{
    AudioManager.Instance.PlayDayMusic();
}
```

---

## Finding Free Audio Assets

### Sound Effects:

1. **Freesound.org**
   - Search: "sword swing", "monster growl", "hit impact"
   - Filter: CC0 (public domain) for no attribution needed
   - Download .wav or .ogg format

2. **ZapSplat.com**
   - Great for UI sounds
   - Search: "button click", "construction", "victory fanfare"
   - Free with account

3. **OpenGameArt.org**
   - Search: "combat sounds", "fantasy weapons"
   - All content is free for games

### Music:

1. **Incompetech.com (Kevin MacLeod)**
   - Huge selection of royalty-free music
   - Search: "medieval", "ambient", "tense"
   - Free with attribution in credits

2. **OpenGameArt.org**
   - Search: "ambient music", "battle music"
   - Many loopable tracks

3. **Unity Asset Store**
   - Search "free music pack"
   - Filter by "Free Assets"

### Quick Search Terms:

**Combat:**
- "medieval sword swing"
- "light sword slash"
- "metal impact"
- "hit punch"
- "monster death"

**Music:**
- "ambient medieval"
- "peaceful village"
- "tense atmosphere"
- "battle music loop"

**UI:**
- "game button click"
- "ui select"
- "victory fanfare"
- "defeat horn"

---

## Example Audio Setup (Quick Start)

If you want to test the system WITHOUT custom audio:

1. **Use Unity's built-in sounds temporarily**
2. **Or download a free pack:**
   - Unity Asset Store → Search "Free SFX Pack"
   - Import pack
   - Assign any sounds to test the system

3. **Recommended Free Packs:**
   - "Casual Game SFX" (free on Unity Asset Store)
   - "Interface SFX" (free on Unity Asset Store)
   - "Fantasy SFX" (free on Unity Asset Store)

---

## Advanced Features

### Custom Audio Calls:

You can call audio from any script:

```csharp
// Play a combat sound
AudioManager.Instance.PlayWarriorAttack();

// Play UI sound
AudioManager.Instance.PlayButtonClick();

// Change music
AudioManager.Instance.PlayCombatMusic();

// Trigger shake
CameraShake.Instance.ShakeMedium();
```

### Volume Control UI:

You can create slider UI to control volumes:

```csharp
// In your UI script:
public void OnMusicVolumeChanged(float value)
{
    AudioManager.Instance.SetMusicVolume(value);
}

public void OnSFXVolumeChanged(float value)
{
    AudioManager.Instance.SetSFXVolume(value);
}
```

---

## Summary

You've added:
- 🎵 Complete audio management system
- ⚔️ Combat sounds (attack, hit, death)
- 🎶 Day/night music transitions
- 🔊 Volume controls
- 📷 Camera shake effects
- 🎚️ Smart cooldowns (no sound spam)

**Total Setup Time:** 15-30 minutes (once you have audio files)

The system is **fully integrated** - it just needs audio clips added in the Unity Inspector!

---

## Next Steps

1. Find and download free audio files
2. Import them into Unity (`Assets/Audio/`)
3. Assign them to AudioManager in Inspector
4. Press Play and enjoy the audio!

**Recommended:** Start with just 5-10 sounds to test:
- 1 attack sound (any slash/swing)
- 1 hit sound (any impact)
- 1 death sound (any death cry)
- 1 day music (any calm loop)
- 1 night music (any tense loop)

You can always add more sounds later!

🎉 Enjoy your fully polished combat experience!
