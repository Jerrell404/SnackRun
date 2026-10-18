using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Smooth scene changes: the screen fades to black, the next scene loads in the background while it's black (so the loading hitch is never seen), then it fades back in.
public class SceneFader : MonoBehaviour
{
    public static bool IsBusy { get; private set; }

    [SerializeField] private float fadeOutTime = 0.25f;   // quick, so it's obvious straight away that the door worked
    [SerializeField] private float fadeInTime = 0.7f;

    private const int LoadBarSegments = 10;

    // The bar always counts 0% -> 100%, one whole percent at a time, over exactly this many seconds.
    private const float DefaultLoadCountSeconds = 5f;

    private static SceneFader instance;
    private CanvasGroup group;
    private TextMeshProUGUI caption;
    private TextMeshProUGUI loading;

    // Progress bar - a row of individually-lit blocks inside a bordered
    // frame, the way an old-school "loading" bar looks, rather than one
    // smooth continuous fill.
    private RectTransform barFrameRect;
    private Image barFrameImg;
    private Image[] barSegments;
    private Color litSegmentColor;
    private Color unlitSegmentColor;
    private RectTransform barPercentRect;
    private TextMeshProUGUI barPercent;
    private float barFraction;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        // The very first scene of a play session also fades in from black.
        Ensure();
        instance.group.alpha = 1f;
        instance.StartCoroutine(instance.FadeIn());
    }

    public static void LoadScene(string sceneName, float loadCountSeconds = DefaultLoadCountSeconds)
    {
        Ensure();
        if (IsBusy) return;
        instance.StartCoroutine(instance.Run(() => SceneManager.LoadSceneAsync(sceneName), CaptionFor(sceneName), loadCountSeconds));
    }

    public static void ReloadCurrentScene(float loadCountSeconds = DefaultLoadCountSeconds)
    {
        Ensure();
        if (IsBusy) return;
        int index = SceneManager.GetActiveScene().buildIndex;
        instance.StartCoroutine(instance.Run(() => SceneManager.LoadSceneAsync(index), null, loadCountSeconds));
    }

    private static string CaptionFor(string sceneName)
    {
        switch (sceneName)
        {
            case "FrontYard": return "Level 1: The Front Yard";
            case "House": return "Level 2: The House";
            case "Back-Yard": return "Level 3: The Backyard";
            default: return null;
        }
    }

    private static void Ensure()
    {
        if (instance != null) return;
        GameObject go = new GameObject("SceneFader");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SceneFader>();
        instance.Build();
    }

    private void Build()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000; // above every other UI
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        GameObject bg = new GameObject("Black", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(transform, false);
        RectTransform r = (RectTransform)bg.transform;
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
        bg.GetComponent<Image>().color = Color.black;

        GameObject txt = new GameObject("Caption", typeof(RectTransform), typeof(TextMeshProUGUI));
        txt.transform.SetParent(transform, false);
        RectTransform t = (RectTransform)txt.transform;
        t.anchorMin = t.anchorMax = new Vector2(0.5f, 0.5f);
        t.sizeDelta = new Vector2(1400f, 200f);
        caption = txt.GetComponent<TextMeshProUGUI>();
        caption.alignment = TextAlignmentOptions.Center;
        caption.fontSize = 72;
        caption.fontStyle = FontStyles.Bold;
        caption.color = new Color(1f, 0.85f, 0.3f);
        caption.raycastTarget = false;
        caption.text = "";

        GameObject ltxt = new GameObject("Loading", typeof(RectTransform), typeof(TextMeshProUGUI));
        ltxt.transform.SetParent(transform, false);
        RectTransform l = (RectTransform)ltxt.transform;
        l.anchorMin = l.anchorMax = new Vector2(0.5f, 0.5f);
        l.anchoredPosition = new Vector2(0f, -110f);
        l.sizeDelta = new Vector2(800f, 80f);
        loading = ltxt.GetComponent<TextMeshProUGUI>();
        loading.alignment = TextAlignmentOptions.Center;
        loading.fontSize = 40;
        loading.color = new Color(1f, 1f, 1f, 0.85f);
        loading.raycastTarget = false;
        loading.text = "";

        BuildProgressBar();
    }

    // A gold-bordered frame around a near-black track, with a row of
    // evenly-spaced blocks that light up vibrant orange left-to-right as
    // real load progress comes in, plus a live "NN%" readout underneath -
    // an old-school segmented loading bar rather than a smooth fill.
    private void BuildProgressBar()
    {
        const float barWidth = 560f;
        const float barHeight = 46f;

        GameObject barFrame = new GameObject("LoadBarFrame", typeof(RectTransform), typeof(Image));
        barFrame.transform.SetParent(transform, false);
        barFrameRect = (RectTransform)barFrame.transform;
        barFrameRect.anchorMin = barFrameRect.anchorMax = new Vector2(0.5f, 0.5f);
        barFrameRect.anchoredPosition = new Vector2(0f, -180f);
        barFrameRect.sizeDelta = new Vector2(barWidth, barHeight);
        barFrameImg = barFrame.GetComponent<Image>();
        barFrameImg.color = new Color(1f, 0.68f, 0.30f, 1f); // gold-orange border
        barFrameImg.raycastTarget = false;

        // Dark inset so the frame colour reads as a border/outline, not a
        // solid block - the segments sit on top of this.
        GameObject barInset = new GameObject("LoadBarInset", typeof(RectTransform), typeof(Image));
        barInset.transform.SetParent(barFrame.transform, false);
        RectTransform barInsetRect = (RectTransform)barInset.transform;
        barInsetRect.anchorMin = Vector2.zero;
        barInsetRect.anchorMax = Vector2.one;
        barInsetRect.offsetMin = new Vector2(3f, 3f);
        barInsetRect.offsetMax = new Vector2(-3f, -3f);
        Image barInsetImg = barInset.GetComponent<Image>();
        barInsetImg.color = new Color(0.08f, 0.08f, 0.09f, 1f);
        barInsetImg.raycastTarget = false;

        litSegmentColor = new Color(1f, 0.52f, 0.09f);          // vibrant orange
        unlitSegmentColor = new Color(0.22f, 0.20f, 0.22f, 1f);  // dark/unlit

        const float segPadding = 6f;
        const float segGap = 4f;
        float segAreaWidth = barWidth - segPadding * 2f;
        float segWidth = (segAreaWidth - segGap * (LoadBarSegments - 1)) / LoadBarSegments;
        float segHeight = barHeight - 12f;

        barSegments = new Image[LoadBarSegments];
        for (int i = 0; i < LoadBarSegments; i++)
        {
            GameObject segGO = new GameObject("Segment" + i, typeof(RectTransform), typeof(Image));
            segGO.transform.SetParent(barFrame.transform, false);
            RectTransform segRect = (RectTransform)segGO.transform;
            // Anchored/pivoted at the frame's left-centre, so anchoredPosition.x
            // is simply the offset from the frame's left edge.
            segRect.anchorMin = segRect.anchorMax = new Vector2(0f, 0.5f);
            segRect.pivot = new Vector2(0f, 0.5f);
            segRect.anchoredPosition = new Vector2(segPadding + i * (segWidth + segGap), 0f);
            segRect.sizeDelta = new Vector2(segWidth, segHeight);

            Image segImg = segGO.GetComponent<Image>();
            segImg.color = unlitSegmentColor;
            segImg.raycastTarget = false;
            barSegments[i] = segImg;
        }

        GameObject pctGO = new GameObject("LoadPercent", typeof(RectTransform), typeof(TextMeshProUGUI));
        pctGO.transform.SetParent(transform, false);
        barPercentRect = (RectTransform)pctGO.transform;
        barPercentRect.anchorMin = barPercentRect.anchorMax = new Vector2(0.5f, 0.5f);
        barPercentRect.anchoredPosition = new Vector2(0f, -222f);
        barPercentRect.sizeDelta = new Vector2(200f, 40f);
        barPercent = pctGO.GetComponent<TextMeshProUGUI>();
        barPercent.alignment = TextAlignmentOptions.Center;
        barPercent.fontSize = 26;
        barPercent.fontStyle = FontStyles.Bold;
        barPercent.color = new Color(1f, 0.72f, 0.35f, 0.9f);
        barPercent.raycastTarget = false;
        barPercent.text = "";
    }

    private void SetBarFraction(float fraction)
    {
        barFraction = Mathf.Clamp01(fraction);
    }

    // "Loading", "Loading.", "Loading..", "Loading..." on a loop, and
    // redraws the segment bar/percentage for the current barFraction.
    private void AnimateLoading(float alpha)
    {
        int dots = Mathf.FloorToInt(Time.unscaledTime * 3f) % 4;
        loading.text = "Loading" + new string('.', dots);
        loading.alpha = alpha;

        SetAlpha(barFrameImg, alpha);

        int litCount = Mathf.RoundToInt(barFraction * barSegments.Length);
        for (int i = 0; i < barSegments.Length; i++)
        {
            Color baseColor = i < litCount ? litSegmentColor : unlitSegmentColor;
            barSegments[i].color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
        }

        if (barPercent != null)
        {
            barPercent.alpha = alpha;
            barPercent.text = Mathf.RoundToInt(barFraction * 100f) + "%";
        }
    }

    private static void SetAlpha(Image img, float alpha)
    {
        if (img == null) return;
        Color c = img.color;
        img.color = new Color(c.r, c.g, c.b, alpha);
    }

    [Tooltip("Longest the loading screen is ever held (seconds). Keeps door transitions and restarts quick.")]
    public static float MaxLoadingScreenSeconds = 1.2f;

    private IEnumerator Run(Func<AsyncOperation> startLoad, string captionText, float loadCountSeconds)
    {
        loadCountSeconds = Mathf.Min(loadCountSeconds, MaxLoadingScreenSeconds);   // was 3-5s: felt like forever
        IsBusy = true;
        group.blocksRaycasts = true;
        caption.text = captionText ?? "";
        caption.alpha = 0f;
        // No area name (restarting after a lost life): pull everything up
        // and centre it, same as before, now including the bar + percent.
        bool hasCaption = !string.IsNullOrEmpty(captionText);
        loading.rectTransform.anchoredPosition = new Vector2(0f, hasCaption ? -110f : 0f);
        loading.fontSize = hasCaption ? 40 : 60;
        barFrameRect.anchoredPosition = new Vector2(0f, hasCaption ? -180f : -70f);
        barPercentRect.anchoredPosition = new Vector2(0f, hasCaption ? -222f : -112f);
        SetBarFraction(0f);
        AnimateLoading(0f);

        // 1. Fade to black (real time - works even while the game is frozen).
        yield return Fade(group.alpha, 1f, fadeOutTime);

        // 2. Kick off the scene load in the background without activating it yet.
        AsyncOperation op = startLoad();
        if (op == null) { IsBusy = false; yield break; }
        op.allowSceneActivation = false;

        // 3. Count the bar from 0% to 100% on a fixed clock, so it always
        // ticks up one percent at a time (0, 1, 2, 3 ... 100) rather than
        // depending on how fast the actual scene happens to load.
        float countStart = Time.unscaledTime;
        float c = 0f;
        float elapsed = 0f;
        while (elapsed < loadCountSeconds)
        {
            elapsed = Time.unscaledTime - countStart;
            c = Mathf.MoveTowards(c, 1f, Time.unscaledDeltaTime / 0.3f);
            caption.alpha = c;
            SetBarFraction(elapsed / loadCountSeconds);
            AnimateLoading(1f);
            yield return null;
        }
        SetBarFraction(1f);
        AnimateLoading(1f);

        // 4. Count's done - make sure the load is ready before switching scenes.
        while (op.progress < 0.9f) { yield return null; }
        op.allowSceneActivation = true;
        while (!op.isDone) { yield return null; }

        // The new scene always starts unfrozen.
        Time.timeScale = 1f;

        // Let the new scene finish its first frames (NPC set-up etc.)
        // behind the black screen, so there's no visible hitch.
        for (int i = 0; i < 3; i++) { yield return null; }

        yield return FadeIn();
    }

    private IEnumerator FadeIn()
    {
        IsBusy = true;
        float start = caption.alpha;
        float t = 0f;
        while (t < fadeInTime)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / fadeInTime);
            group.alpha = 1f - Mathf.SmoothStep(0f, 1f, k);
            yield return null;
        }
        group.alpha = 0f;
        caption.alpha = 0f;
        loading.text = "";
        SetBarFraction(0f);
        group.blocksRaycasts = false;
        IsBusy = false;
    }

    private IEnumerator Fade(float from, float to, float time)
    {
        float t = 0f;
        while (t < time)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / time)));
            group.alpha = a;
            AnimateLoading(a);
            yield return null;
        }
        group.alpha = to;
    }
}
