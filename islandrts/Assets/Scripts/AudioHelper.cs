using UnityEngine;

/// <summary>
/// Shared utility for creating pre-configured 3D spatial AudioSource components.
/// Used by Worker, Warrior, and Enemy to avoid duplicating audio setup code.
/// </summary>
public static class AudioHelper
{
    public static AudioSource CreateSpatialAudioSource(
        GameObject target, float volume, float minDist, float maxDist, float dopplerLevel = 0f)
    {
        var source = target.AddComponent<AudioSource>();
        source.spatialBlend = 1.0f;  // Full 3D
        source.playOnAwake = false;
        source.loop = false;
        source.minDistance = minDist;
        source.maxDistance = maxDist;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.volume = volume;
        source.dopplerLevel = dopplerLevel;
        return source;
    }
}
