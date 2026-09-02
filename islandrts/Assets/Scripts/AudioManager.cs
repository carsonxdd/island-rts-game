using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// All game audio in one place: music, ambience, UI and combat one-shots. Four dedicated
/// AudioSources (music, ambient, SFX, looping gather) so each layer can be faded and mixed
/// independently.
/// </summary>
/// <remarks>
/// Two things here exist to stop combat from wrecking the mix. Repeatable sounds go
/// through a short per-sound-id cooldown, so a dozen units swinging on the same frame
/// plays one hit rather than a wall of them. And music never cuts: day, night and combat
/// tracks crossfade through one shared routine, with a guard that ignores a second
/// crossfade request while one is already running.
///
/// Every clip is loaded once at startup - reading them on demand caused visible hitches
/// the first time each sound played.
///
/// Positional sounds are NOT played here: units own their own spatial AudioSource (see
/// AudioHelper). This singleton is for sounds that come from the game rather than a place.
/// </remarks>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Audio Sources")]
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private AudioSource ambientSource;
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioSource resourceSoundSource;  // For looping resource gathering sounds

    [Header("Combat Sounds")]
    public AudioClip warriorAttackSound;
    public AudioClip enemyAttackSound;
    public AudioClip hitSound;
    public AudioClip warriorDeathSound;
    public AudioClip enemyDeathSound;

    [Header("UI Sounds")]
    public AudioClip buttonClickSound;
    public AudioClip buildingPlacedSound;
    public AudioClip workerAssignedSound;
    public AudioClip victorySound;
    public AudioClip defeatSound;

    [Header("Music (Background Tracks)")]
    public AudioClip dayMusic;
    public AudioClip nightMusic;
    public AudioClip combatMusic;

    [Header("Ambient Nature Sounds (Birds, Wind, etc.)")]
    public AudioClip dayAmbientSounds;   // Birds chirping, gentle breeze
    public AudioClip nightAmbientSounds; // Crickets, owls, night sounds

    [Header("Resource Sounds")]
    public AudioClip gatherWoodSound;
    public AudioClip gatherFoodSound;
    public AudioClip gatherStoneSound;

    [Header("Settings")]
    [Range(0f, 1f)] public float masterVolume = 1f;
    [Range(0f, 1f)] public float musicVolume = 0.7f;
    [Range(0f, 1f)] public float sfxVolume = 1f;
    [Range(0f, 1f)] public float ambientVolume = 0.5f;

    [Header("Combat Audio")]
    public bool enableCombatSounds = true;
    public float combatSoundCooldown = 0.1f; // Prevent sound spam

    [Header("Music Transitions")]
    public float musicFadeTime = 2f;

    // Per-sound-id cooldowns, so one loud moment does not play the same clip 20 times.
    // The buffer is reused when ticking them down - a dictionary cannot be edited while
    // being iterated, and allocating a key list every frame would be pure garbage.
    private Dictionary<string, float> soundCooldowns = new Dictionary<string, float>();
    private readonly List<string> cooldownKeysBuffer = new List<string>();
    private AudioClip currentMusic;
    private bool isFadingMusic = false;

    void Awake()
    {
        // Singleton pattern
        if (Instance == null)
        {
            Instance = this;
            InitializeAudioSources();
            PreloadAllAudioClips();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Force all audio clips into memory at startup so they don't cause
    /// synchronous disk loads (184ms+ freezes) when played mid-gameplay.
    /// </summary>
    /// <summary>
    /// Forces every clip into memory at startup. Without this, the first play of each sound
    /// blocks on a synchronous disk read - a very visible hitch the first time combat starts.
    /// </summary>
    void PreloadAllAudioClips()
    {
        AudioClip[] allClips = new AudioClip[]
        {
            // Music
            dayMusic, nightMusic, combatMusic,
            // Ambient
            dayAmbientSounds, nightAmbientSounds,
            // Combat
            warriorAttackSound, enemyAttackSound, hitSound,
            warriorDeathSound, enemyDeathSound,
            // UI
            buttonClickSound, buildingPlacedSound, workerAssignedSound,
            victorySound, defeatSound,
            // Resources
            gatherWoodSound, gatherFoodSound, gatherStoneSound
        };

        for (int i = 0; i < allClips.Length; i++)
        {
            if (allClips[i] != null && allClips[i].loadState != AudioDataLoadState.Loaded)
            {
                allClips[i].LoadAudioData();
            }
        }
    }

    void Start()
    {
        // Start with day music and ambient sounds
        // Wait a frame to ensure DayNightCycle is initialized
        Invoke(nameof(StartInitialAudio), 0.1f);
    }

    void StartInitialAudio()
    {
        // Check if it's day or night from DayNightCycle
        DayNightCycle dayNight = FindAnyObjectByType<DayNightCycle>();

        if (dayNight != null && dayNight.IsNightTime())
        {
            // Start with night audio
            PlayNightMusic();
        }
        else
        {
            // Start with day audio (default)
            PlayDayMusic();
        }
    }

    void InitializeAudioSources()
    {
        // Create audio sources if they don't exist
        if (musicSource == null)
        {
            GameObject musicObj = new GameObject("MusicSource");
            musicObj.transform.SetParent(transform);
            musicSource = musicObj.AddComponent<AudioSource>();
            musicSource.loop = true;
            musicSource.playOnAwake = false;
            musicSource.volume = musicVolume * masterVolume;
        }

        if (ambientSource == null)
        {
            GameObject ambientObj = new GameObject("AmbientSource");
            ambientObj.transform.SetParent(transform);
            ambientSource = ambientObj.AddComponent<AudioSource>();
            ambientSource.loop = true;
            ambientSource.playOnAwake = false;
            ambientSource.volume = ambientVolume * masterVolume;
        }

        if (sfxSource == null)
        {
            GameObject sfxObj = new GameObject("SFXSource");
            sfxObj.transform.SetParent(transform);
            sfxSource = sfxObj.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.volume = sfxVolume * masterVolume;
        }

        if (resourceSoundSource == null)
        {
            GameObject resourceObj = new GameObject("ResourceSoundSource");
            resourceObj.transform.SetParent(transform);
            resourceSoundSource = resourceObj.AddComponent<AudioSource>();
            resourceSoundSource.loop = true;  // Loops while gathering
            resourceSoundSource.playOnAwake = false;
            resourceSoundSource.volume = sfxVolume * masterVolume * 0.6f;  // Slightly quieter
        }
    }

    void Update()
    {
        // Update volume settings
        if (musicSource != null) musicSource.volume = musicVolume * masterVolume;
        if (ambientSource != null) ambientSource.volume = ambientVolume * masterVolume;
        if (sfxSource != null) sfxSource.volume = sfxVolume * masterVolume;
        if (resourceSoundSource != null) resourceSoundSource.volume = sfxVolume * masterVolume * 0.6f;

        // Update cooldown timers (zero allocations — uses pooled key buffer)
        if (soundCooldowns.Count > 0)
        {
            // Collect keys into pooled buffer first to avoid modifying dictionary during iteration
            cooldownKeysBuffer.Clear();
            foreach (var kvp in soundCooldowns)
                cooldownKeysBuffer.Add(kvp.Key);

            // Update cooldowns and remove expired entries
            for (int i = cooldownKeysBuffer.Count - 1; i >= 0; i--)
            {
                string key = cooldownKeysBuffer[i];
                float newValue = soundCooldowns[key] - Time.deltaTime;
                if (newValue <= 0f)
                    soundCooldowns.Remove(key);
                else
                    soundCooldowns[key] = newValue;
            }
        }
    }

    #region Combat Sounds

    public void PlayHitSound()
    {
        if (!enableCombatSounds) return;
        if (CanPlaySound("hit"))
        {
            PlaySFX(hitSound, 0.8f);
            SetSoundCooldown("hit");
        }
    }

    #endregion

    #region UI Sounds

    public void PlayButtonClick()
    {
        PlaySFX(buttonClickSound, 0.5f);
    }

    public void PlayBuildingPlaced()
    {
        PlaySFX(buildingPlacedSound, 0.8f);
    }

    public void PlayWorkerAssigned()
    {
        PlaySFX(workerAssignedSound, 0.5f);
    }

    public void PlayVictory()
    {
        PlaySFX(victorySound, 1f);
    }

    public void PlayDefeat()
    {
        PlaySFX(defeatSound, 1f);
    }

    #endregion

    #region Resource Sounds

    // Start looping resource gathering sound
    public void StartGatheringSound(ResourceNode.ResourceType resourceType)
    {
        if (resourceSoundSource == null) return;

        AudioClip clipToPlay = null;

        switch (resourceType)
        {
            case ResourceNode.ResourceType.Wood:
                clipToPlay = gatherWoodSound;
                break;
            case ResourceNode.ResourceType.Food:
                clipToPlay = gatherFoodSound;
                break;
            case ResourceNode.ResourceType.Stone:
            case ResourceNode.ResourceType.Metal:
                clipToPlay = gatherStoneSound;
                break;
        }

        // Only start if we have a clip and it's not already playing this clip
        if (clipToPlay != null && resourceSoundSource.clip != clipToPlay)
        {
            resourceSoundSource.clip = clipToPlay;
            resourceSoundSource.Play();
        }
        else if (clipToPlay != null && !resourceSoundSource.isPlaying)
        {
            // Same clip but stopped - restart it
            resourceSoundSource.Play();
        }
    }

    // Stop looping resource gathering sound
    public void StopGatheringSound()
    {
        if (resourceSoundSource != null && resourceSoundSource.isPlaying)
        {
            resourceSoundSource.Stop();
            resourceSoundSource.clip = null;
        }
    }

    #endregion

    #region Music & Ambient

    public void PlayDayMusic()
    {
        if (currentMusic != dayMusic)
        {
            FadeToMusic(dayMusic);
        }

        // Also play day ambient sounds
        PlayAmbientSounds(dayAmbientSounds);
    }

    public void PlayNightMusic()
    {
        if (currentMusic != nightMusic)
        {
            FadeToMusic(nightMusic);
        }

        // Also play night ambient sounds
        PlayAmbientSounds(nightAmbientSounds);
    }

    // New method: Play ONLY night ambience, no music (for nighttime without combat)
    public void PlayNightAmbience()
    {
        // Fade out music, keep only ambient sounds
        StopMusic();

        // Play night ambient sounds
        PlayAmbientSounds(nightAmbientSounds);
    }

    public void PlayCombatMusic()
    {
        if (combatMusic == null)
        {
            Debug.LogWarning("AudioManager: Combat music clip not assigned! Please assign in Inspector.");
            return;
        }

        if (currentMusic != combatMusic)
        {
            FadeToMusic(combatMusic);
        }
    }

    public void StopMusic()
    {
        if (musicSource != null && musicSource.isPlaying)
        {
            StartCoroutine(FadeOutMusic());
        }
    }

    void PlayAmbientSounds(AudioClip ambientClip)
    {
        if (ambientClip == null || ambientSource == null) return;

        // If already playing this clip, don't restart
        if (ambientSource.clip == ambientClip && ambientSource.isPlaying)
        {
            return;
        }

        // Fade to new ambient sounds
        StartCoroutine(CrossfadeAmbient(ambientClip));
    }

    /// <summary>
    /// Switches the music track with a crossfade. A request that arrives while a fade is
    /// already running is dropped rather than queued, so rapid day/night/combat changes
    /// cannot stack fades on top of each other.
    /// </summary>
    void FadeToMusic(AudioClip newClip)
    {
        if (newClip == null)
        {
            Debug.LogWarning("AudioManager: Attempted to play null music clip!");
            return;
        }

        currentMusic = newClip;

        if (!isFadingMusic)
        {
            StartCoroutine(CrossfadeMusic(newClip));
        }
    }

    System.Collections.IEnumerator CrossfadeMusic(AudioClip newClip)
    {
        isFadingMusic = true;
        yield return StartCoroutine(CrossfadeSource(musicSource, newClip, musicVolume * masterVolume));
        isFadingMusic = false;
    }

    System.Collections.IEnumerator FadeOutMusic()
    {
        isFadingMusic = true;
        yield return StartCoroutine(FadeVolume(musicSource, 0f, musicFadeTime));
        musicSource.Stop();
        currentMusic = null;
        isFadingMusic = false;
    }

    System.Collections.IEnumerator CrossfadeAmbient(AudioClip newClip)
    {
        yield return StartCoroutine(CrossfadeSource(ambientSource, newClip, ambientVolume * masterVolume));
    }

    // Shared crossfade skeleton: fade out (if playing), swap clip, fade in
    System.Collections.IEnumerator CrossfadeSource(AudioSource source, AudioClip newClip, float targetVolume)
    {
        if (source.isPlaying)
        {
            yield return StartCoroutine(FadeVolume(source, 0f, musicFadeTime / 2f));
        }

        source.clip = newClip;
        source.Play();

        yield return StartCoroutine(FadeVolume(source, targetVolume, musicFadeTime / 2f));
    }

    System.Collections.IEnumerator FadeVolume(AudioSource source, float targetVolume, float duration)
    {
        float startVolume = source.volume;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            source.volume = Mathf.Lerp(startVolume, targetVolume, elapsed / duration);
            yield return null;
        }

        source.volume = targetVolume;
    }

    #endregion

    #region Helper Methods

    void PlaySFX(AudioClip clip, float volumeMultiplier = 1f)
    {
        if (clip != null && sfxSource != null)
        {
            sfxSource.PlayOneShot(clip, volumeMultiplier * sfxVolume * masterVolume);
        }
    }

    /// <summary>False while this sound id is still on cooldown. Repeatable combat sounds
    /// check this so a crowd of attackers does not play the same clip dozens of times.</summary>
    bool CanPlaySound(string soundId)
    {
        return !soundCooldowns.ContainsKey(soundId);
    }

    void SetSoundCooldown(string soundId, float cooldown = -1f)
    {
        if (cooldown < 0) cooldown = combatSoundCooldown;
        soundCooldowns[soundId] = cooldown;
    }

    #endregion

}
