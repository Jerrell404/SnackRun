using UnityEngine;

// Master / Music / SFX volume.
public static class GameAudioSettings
{
    private const string MasterKey = "SnackRun_MasterVolume";
    private const string MusicKey = "SnackRun_MusicVolume";
    private const string SfxKey = "SnackRun_SfxVolume";

    public static float Master { get; private set; } = 1f;
    public static float Music { get; private set; } = 1f;
    public static float Sfx { get; private set; } = 1f;

    private static bool loaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        if (loaded) return;
        loaded = true;

        Master = PlayerPrefs.GetFloat(MasterKey, 1f);
        Music = PlayerPrefs.GetFloat(MusicKey, 1f);
        Sfx = PlayerPrefs.GetFloat(SfxKey, 1f);

        Apply();
    }

    public static void SetMaster(float value)
    {
        Master = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(MasterKey, Master);
        AudioListener.volume = Master;
    }

    public static void SetMusic(float value)
    {
        Music = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(MusicKey, Music);
        BackgroundAudio.MusicVolumeScale = Music;
    }

    public static void SetSfx(float value)
    {
        Sfx = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(SfxKey, Sfx);
        SoundFX.SfxVolumeScale = Sfx;
        BackgroundAudio.AmbienceVolumeScale = Sfx;
    }

    // Pushes the loaded values onto the actual audio systems. Called once
    // at boot; safe to call again any time.
    private static void Apply()
    {
        AudioListener.volume = Master;
        BackgroundAudio.MusicVolumeScale = Music;
        SoundFX.SfxVolumeScale = Sfx;
        BackgroundAudio.AmbienceVolumeScale = Sfx;
    }
}
