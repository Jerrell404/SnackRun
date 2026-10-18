using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Screen vignette and heartbeat that intensify with nearby danger; flashes red on a hit.
public class DangerFeedback : MonoBehaviour
{
    [Tooltip("How dark the edges are when nothing is happening (0-1).")]
    public float baseDarkness = 0.38f;
    public float minHeartRate = 65f;
    public float maxHeartRate = 150f;

    private static DangerFeedback instance;
    private Image vignette;
    private AudioSource heartSource;
    private AudioClip heartClip;

    private HumanNPC edgar;
    private CatNPC whiskers;
    private DogNPC rex;
    private Transform player;

    private float danger;      // smoothed 0..1
    /// How much danger Bandit is in right now (0 safe .. 1 chased up close).
    public static float Level => instance != null ? instance.danger : 0f;
    private float beatTimer;
    private float pulse;       // flash on each heartbeat
    private float hitFlash;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        GameObject go = new GameObject("DangerFeedback");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<DangerFeedback>();
        instance.Build();
        SceneManager.sceneLoaded += (s, m) => instance.FindThings();
        instance.FindThings();
    }

    /// Red flash around the screen (called when Bandit takes a hit).
    public static void Hit()
    {
        if (instance != null) instance.hitFlash = 0.55f;
    }

    private void FindThings()
    {
        edgar = FindFirstObjectByType<HumanNPC>();
        whiskers = FindFirstObjectByType<CatNPC>();
        rex = FindFirstObjectByType<DogNPC>();
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        player = p != null ? p.transform : null;
        danger = 0f; pulse = 0f; hitFlash = 0f;
    }

    private void Build()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;   // under the messages (400) and the loading screen (1000)
        gameObject.AddComponent<CanvasScaler>();

        GameObject img = new GameObject("Vignette", typeof(RectTransform), typeof(Image));
        img.transform.SetParent(transform, false);
        RectTransform r = (RectTransform)img.transform;
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
        vignette = img.GetComponent<Image>();
        vignette.raycastTarget = false;
        vignette.sprite = MakeVignetteSprite();
        vignette.color = new Color(0f, 0f, 0f, baseDarkness);

        heartSource = gameObject.AddComponent<AudioSource>();
        heartSource.playOnAwake = false;
        heartSource.spatialBlend = 0f;
        heartClip = Resources.Load<AudioClip>("Audio/SFX/heartbeat");
    }

    // Clear in the middle, fading to solid at the edges.
    private static Sprite MakeVignetteSprite()
    {
        const int size = 256;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        Color[] px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v) / 1.414f;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, d));
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    // 0 = safe, 1 = being hunted right up close.
    private float MeasureDanger()
    {
        if (player == null) return 0f;
        bool hunted = false;
        float nearest = float.MaxValue;
        float suspicion = 0f;

        if (edgar != null && edgar.isActiveAndEnabled)
        {
            if (edgar.IsHunting) hunted = true;
            if (edgar.IsUpAndAbout) nearest = Mathf.Min(nearest, Flat(edgar.transform.position));
            suspicion = edgar.Suspicion;
        }
        if (whiskers != null && whiskers.isActiveAndEnabled)
        {
            if (whiskers.IsHunting) hunted = true;
            if (!whiskers.IsAsleep) nearest = Mathf.Min(nearest, Flat(whiskers.transform.position));
        }
        if (rex != null && rex.isActiveAndEnabled && rex.IsChasing)
        {
            hunted = true;
            nearest = Mathf.Min(nearest, Flat(rex.transform.position));
        }

        float close = nearest < float.MaxValue ? Mathf.Clamp01(1f - (nearest - 2f) / 8f) : 0f;  // 2m = 1, 10m = 0
        float d = Mathf.Max(close * 0.45f, suspicion * 0.7f);
        if (hunted) d = Mathf.Max(d, 0.5f + 0.5f * close);
        return d;
    }

    private float Flat(Vector3 p)
    {
        Vector3 d = p - player.position; d.y = 0f;
        return d.magnitude;
    }

    private void Update()
    {
        float dt = Time.deltaTime;   // pauses while the game is frozen
        float target = MeasureDanger();
        danger = Mathf.MoveTowards(danger, target, dt * (target > danger ? 2f : 0.4f));

        // Heartbeat.
        if (danger > 0.12f && heartClip != null)
        {
            beatTimer -= dt;
            if (beatTimer <= 0f)
            {
                float bpm = Mathf.Lerp(minHeartRate, maxHeartRate, danger);
                beatTimer = 60f / bpm;
                heartSource.PlayOneShot(heartClip, Mathf.Lerp(0.2f, 1f, danger) * SoundFX.SfxVolumeScale);
                pulse = 1f;
            }
        }
        else beatTimer = 0f;

        pulse = Mathf.MoveTowards(pulse, 0f, dt * 3f);
        hitFlash = Mathf.MoveTowards(hitFlash, 0f, Time.unscaledDeltaTime * 0.9f);

        // Vignette: black at night, turning red with danger, throbbing with the heart.
        float redness = Mathf.Clamp01(danger * 1.2f + hitFlash * 2f);
        Color c = Color.Lerp(Color.black, new Color(0.6f, 0f, 0f), redness);
        c.a = Mathf.Clamp01(baseDarkness + danger * 0.3f + pulse * 0.12f * danger + hitFlash);
        vignette.color = c;
    }
}
