using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Handles the background music + ambience sounds for every scene
// crickets play in the front yard and backyard, house gets its own room sound
public class BackgroundAudio : MonoBehaviour
{
    public static float MusicVolume = 0.22f;
    public static float AmbienceVolume = 0.35f;

    // set by the music slider in the main menu (0 to 1)
    // 1 = normal volume, 0 = muted
    public static float MusicVolumeScale = 1f;

    // set by the SFX slider - we counted the crickets as sfx not music
    public static float AmbienceVolumeScale = 1f;

    private static BackgroundAudio instance;

    private AudioSource musicSrc;
    private AudioSource chaseSrc;   // chase music (fades in when something is chasing you)
    private bool isChasing;

    // two sources so we can fade between ambience tracks when the scene changes
    private AudioSource ambSrc1;
    private AudioSource ambSrc2;
    private bool using1 = true;

    private float duckTimer;

    // name of the ambience clip we want for this scene (null on main menu)
    private string currentAmb;

    // runs automatically when the game starts so we dont have to put it in every scene
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        if (instance != null)
            return;

        GameObject obj = new GameObject("BackgroundAudio");
        DontDestroyOnLoad(obj);
        instance = obj.AddComponent<BackgroundAudio>();
        instance.SetupSources();

        SceneManager.sceneLoaded += SceneLoaded;
        instance.ChangeScene(SceneManager.GetActiveScene());
    }

    private static void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        instance.ChangeScene(scene);
    }

    // makes the music quieter for a bit so sound effects can be heard
    public static void Duck(float seconds)
    {
        if (instance != null)
            instance.duckTimer = Time.unscaledTime + seconds;
    }

    private AudioSource CreateSource(AudioClip clip)
    {
        AudioSource src = gameObject.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = true;
        src.playOnAwake = false;
        src.spatialBlend = 0f; // 2D sound
        src.volume = 0f;
        src.ignoreListenerPause = true; // keep playing when paused
        return src;
    }

    private void SetupSources()
    {
        musicSrc = CreateSource(Resources.Load<AudioClip>("Audio/Loops/music_sneak"));
        ambSrc1 = CreateSource(null);
        ambSrc2 = CreateSource(null);
        chaseSrc = CreateSource(null); // ended up just using one track so this stays empty

        if (musicSrc.clip != null)
        {
            musicSrc.Play();
            // start at full volume straight away instead of fading in
            musicSrc.volume = MusicVolume * MusicVolumeScale;
        }
    }

    private void ChangeScene(Scene scene)
    {
        // pick which ambience to play
        if (scene.name == "MainMenu")
            currentAmb = null;
        else if (scene.name == "House")
            currentAmb = "ambience_house";
        else
            currentAmb = "ambience_crickets";

        // main menu has no ambience, Update() will fade out whatever was playing
        if (currentAmb == null)
            return;

        AudioSource current;
        AudioSource next;
        if (using1)
        {
            current = ambSrc1;
            next = ambSrc2;
        }
        else
        {
            current = ambSrc2;
            next = ambSrc1;
        }

        // already playing the right one
        if (current.clip != null && current.clip.name == currentAmb && current.isPlaying)
            return;

        next.clip = Resources.Load<AudioClip>("Audio/Loops/" + currentAmb);
        if (next.clip == null)
            return;

        next.volume = 0f;
        // start at a random point so it doesnt sound the same every time
        next.time = Random.Range(0f, next.clip.length * 0.9f);
        next.Play();
        using1 = !using1;
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        // mute everything while the scene is loading
        bool loading = SceneFader.IsBusy;

        // check if the player is being chased
        float danger = DangerFeedback.Level;
        if (!isChasing && danger > 0.5f && chaseSrc.clip != null && !loading)
        {
            isChasing = true;
            if (!chaseSrc.isPlaying)
            {
                chaseSrc.time = 0f;
                chaseSrc.Play();
            }
        }
        else if (isChasing && (danger < 0.25f || loading))
        {
            isChasing = false;
        }

        // work out how loud the normal music should be
        float normalVol;
        if (loading)
            normalVol = 0f;
        else if (Time.unscaledTime < duckTimer)
            normalVol = MusicVolume * 0.2f * MusicVolumeScale; // ducked
        else
            normalVol = MusicVolume * MusicVolumeScale;

        // fade between normal music and chase music
        float musicTarget = isChasing ? 0f : normalVol;
        float musicSpeed = isChasing ? 0.5f : 0.25f;
        musicSrc.volume = Mathf.MoveTowards(musicSrc.volume, musicTarget, dt * musicSpeed);

        float chaseTarget = isChasing ? normalVol * 1.1f : 0f;
        float chaseSpeed = isChasing ? 0.8f : 0.3f;
        chaseSrc.volume = Mathf.MoveTowards(chaseSrc.volume, chaseTarget, dt * chaseSpeed);

        if (!isChasing && chaseSrc.isPlaying && chaseSrc.volume <= 0f)
            chaseSrc.Stop();

        // ambience fading (crickets etc)
        AudioSource cur = using1 ? ambSrc1 : ambSrc2;
        AudioSource old = using1 ? ambSrc2 : ambSrc1;

        float ambTarget = 0f;
        if (!loading && currentAmb != null)
            ambTarget = AmbienceVolume * AmbienceVolumeScale;

        cur.volume = Mathf.MoveTowards(cur.volume, ambTarget, dt * 0.25f); // takes about 1.5 sec
        old.volume = Mathf.MoveTowards(old.volume, 0f, dt * 0.35f);

        if (old.isPlaying && old.volume <= 0f)
            old.Stop();

        if (!loading && currentAmb == null && cur.isPlaying && cur.volume <= 0f)
            cur.Stop();
    }
}
