using System.Collections.Generic;
using UnityEngine;

// Plays the game's sound effects. Sounds live in Assets/Resources/Audio/SFX and are played by name.
public class SoundFX : MonoBehaviour
{
    private static SoundFX instance;

    // Player-controlled multiplier (0..1) from the Main Menu's SFX slider.
    public static float SfxVolumeScale = 1f;
    private readonly Dictionary<string, AudioClip[]> cache = new Dictionary<string, AudioClip[]>();
    private AudioSource[] sources;
    private int next;

    public static void Play(string name, float volume = 1f, float pitchVariation = 0.05f)
    {
        Ensure();
        AudioClip clip = instance.Get(name);
        if (clip == null) return;
        // Let jingles be heard over the music.
        if (name == "victory" || name == "game_over" || name == "life_lost") BackgroundAudio.Duck(clip.length + 1f);
        AudioSource s = instance.sources[instance.next];
        instance.next = (instance.next + 1) % instance.sources.Length;
        s.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
        s.PlayOneShot(clip, volume * SfxVolumeScale);
    }

    private static void Ensure()
    {
        if (instance != null) return;
        GameObject go = new GameObject("SoundFX");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SoundFX>();
        instance.sources = new AudioSource[8];
        for (int i = 0; i < instance.sources.Length; i++)
        {
            AudioSource s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;           // plain 2D - always heard clearly
            s.ignoreListenerPause = true;
            instance.sources[i] = s;
        }
    }

    private AudioClip Get(string name)
    {
        AudioClip[] clips;
        if (!cache.TryGetValue(name, out clips))
        {
            List<AudioClip> found = new List<AudioClip>();
            AudioClip single = Resources.Load<AudioClip>("Audio/SFX/" + name);
            if (single != null) found.Add(single);
            for (int i = 1; i <= 12; i++)
            {
                AudioClip c = Resources.Load<AudioClip>("Audio/SFX/" + name + "_" + i);
                if (c == null) break;
                found.Add(c);
            }
            if (found.Count == 0) Debug.LogWarning("SoundFX: no sound called '" + name + "' in Resources/Audio/SFX.");
            clips = found.ToArray();
            cache[name] = clips;
        }
        return clips.Length == 0 ? null : clips[Random.Range(0, clips.Length)];
    }
}
