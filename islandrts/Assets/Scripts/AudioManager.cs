using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Manages all audio in the game (combat, UI, ambient, music)
/// Singleton pattern for global access
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Audio Sources")]
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private AudioSource ambientSource;
    [SerializeField] private AudioSource sfxSource;

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

    // Private
    private Dictionary<string, float> soundCooldowns = new Dictionary<string, float>();
    private AudioClip currentMusic;
    private bool isFadingMusic = false;

    void Awake()
    {
        // Singleton pattern
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            InitializeAudioSources();
            Debug.Log("AudioManager: Initialized");
        }
        else
        {
            Destroy(gameObject);
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
        DayNightCycle dayNight = FindFirstObjectByType<DayNightCycle>();

        if (dayNight != null && dayNight.IsNightTime())
        {
            // Start with night audio
            PlayNightMusic();
            Debug.Log("AudioManager: Starting with night audio");
        }
        else
        {
            // Start with day audio (default)
            PlayDayMusic();
            Debug.Log("AudioManager: Starting with day audio");
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
    }

    void Update()
    {
        // Update volume settings
        if (musicSource != null) musicSource.volume = musicVolume * masterVolume;
        if (ambientSource != null) ambientSource.volume = ambientVolume * masterVolume;
        if (sfxSource != null) sfxSource.volume = sfxVolume * masterVolume;

        // Update cooldown timers
        List<string> keysToRemove = new List<string>();
        foreach (var key in soundCooldowns.Keys)
        {
            soundCooldowns[key] -= Time.deltaTime;
            if (soundCooldowns[key] <= 0)
            {
                keysToRemove.Add(key);
            }
        }
        foreach (var key in keysToRemove)
        {
            soundCooldowns.Remove(key);
        }
    }

    #region Combat Sounds

    public void PlayWarriorAttack()
    {
        if (!enableCombatSounds) return;
        if (CanPlaySound("warrior_attack"))
        {
            PlaySFX(warriorAttackSound, 0.6f);
            SetSoundCooldown("warrior_attack");
        }
    }

    public void PlayEnemyAttack()
    {
        if (!enableCombatSounds) return;
        if (CanPlaySound("enemy_attack"))
        {
            PlaySFX(enemyAttackSound, 0.6f);
            SetSoundCooldown("enemy_attack");
        }
    }

    public void PlayHitSound()
    {
        if (!enableCombatSounds) return;
        if (CanPlaySound("hit"))
        {
            PlaySFX(hitSound, 0.8f);
            SetSoundCooldown("hit");
        }
    }

    public void PlayWarriorDeath()
    {
        PlaySFX(warriorDeathSound, 1f);
    }

    public void PlayEnemyDeath()
    {
        PlaySFX(enemyDeathSound, 1f);
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

    public void PlayGatherWood()
    {
        if (CanPlaySound("gather_wood"))
        {
            PlaySFX(gatherWoodSound, 0.3f);
            SetSoundCooldown("gather_wood", 0.5f);
        }
    }

    public void PlayGatherFood()
    {
        if (CanPlaySound("gather_food"))
        {
            PlaySFX(gatherFoodSound, 0.3f);
            SetSoundCooldown("gather_food", 0.5f);
        }
    }

    public void PlayGatherStone()
    {
        if (CanPlaySound("gather_stone"))
        {
            PlaySFX(gatherStoneSound, 0.3f);
            SetSoundCooldown("gather_stone", 0.5f);
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

    public void PlayCombatMusic()
    {
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

    public void StopAmbientSounds()
    {
        if (ambientSource != null && ambientSource.isPlaying)
        {
            StartCoroutine(FadeOutAmbient());
        }
    }

    void FadeToMusic(AudioClip newClip)
    {
        if (newClip == null) return;

        currentMusic = newClip;

        if (!isFadingMusic)
        {
            StartCoroutine(CrossfadeMusic(newClip));
        }
    }

    System.Collections.IEnumerator CrossfadeMusic(AudioClip newClip)
    {
        isFadingMusic = true;
        float targetVolume = musicVolume * masterVolume;

        // Fade out current music
        if (musicSource.isPlaying)
        {
            float startVolume = musicSource.volume;
            float elapsed = 0f;

            while (elapsed < musicFadeTime / 2f)
            {
                elapsed += Time.deltaTime;
                musicSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / (musicFadeTime / 2f));
                yield return null;
            }
        }

        // Switch to new clip
        musicSource.clip = newClip;
        musicSource.Play();

        // Fade in new music
        float elapsedFadeIn = 0f;
        while (elapsedFadeIn < musicFadeTime / 2f)
        {
            elapsedFadeIn += Time.deltaTime;
            musicSource.volume = Mathf.Lerp(0f, targetVolume, elapsedFadeIn / (musicFadeTime / 2f));
            yield return null;
        }

        musicSource.volume = targetVolume;
        isFadingMusic = false;
    }

    System.Collections.IEnumerator FadeOutMusic()
    {
        isFadingMusic = true;
        float startVolume = musicSource.volume;
        float elapsed = 0f;

        while (elapsed < musicFadeTime)
        {
            elapsed += Time.deltaTime;
            musicSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / musicFadeTime);
            yield return null;
        }

        musicSource.Stop();
        currentMusic = null;
        isFadingMusic = false;
    }

    System.Collections.IEnumerator CrossfadeAmbient(AudioClip newClip)
    {
        float targetVolume = ambientVolume * masterVolume;

        // Fade out current ambient
        if (ambientSource.isPlaying)
        {
            float startVolume = ambientSource.volume;
            float elapsed = 0f;

            while (elapsed < musicFadeTime / 2f)
            {
                elapsed += Time.deltaTime;
                ambientSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / (musicFadeTime / 2f));
                yield return null;
            }
        }

        // Switch to new ambient clip
        ambientSource.clip = newClip;
        ambientSource.Play();

        // Fade in new ambient
        float elapsedFadeIn = 0f;
        while (elapsedFadeIn < musicFadeTime / 2f)
        {
            elapsedFadeIn += Time.deltaTime;
            ambientSource.volume = Mathf.Lerp(0f, targetVolume, elapsedFadeIn / (musicFadeTime / 2f));
            yield return null;
        }

        ambientSource.volume = targetVolume;
    }

    System.Collections.IEnumerator FadeOutAmbient()
    {
        float startVolume = ambientSource.volume;
        float elapsed = 0f;

        while (elapsed < musicFadeTime)
        {
            elapsed += Time.deltaTime;
            ambientSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / musicFadeTime);
            yield return null;
        }

        ambientSource.Stop();
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

    #region Public Utility

    public void SetMasterVolume(float volume)
    {
        masterVolume = Mathf.Clamp01(volume);
    }

    public void SetMusicVolume(float volume)
    {
        musicVolume = Mathf.Clamp01(volume);
    }

    public void SetSFXVolume(float volume)
    {
        sfxVolume = Mathf.Clamp01(volume);
    }

    #endregion
}
