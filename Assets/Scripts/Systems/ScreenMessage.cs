using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A simple, always-visible message banner near the top-middle of the
// screen (its own overlay canvas, so it can never end up off-camera).
// ScreenMessage.Show("text", seconds, colour).
public class ScreenMessage : MonoBehaviour
{
    private static ScreenMessage instance;

    private TextMeshProUGUI label;
    private Image background;
    private TextMeshProUGUI statusLabel;
    private Image statusBackground;
    private TextMeshProUGUI timerLabel;
    private Image timerBackground;
    private Coroutine hideRoutine;

    public static void Show(string text, float seconds = 3f, Color? colour = null)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (instance == null)
        {
            GameObject go = new GameObject("ScreenMessage");
            instance = go.AddComponent<ScreenMessage>(); // lives with the scene
            instance.Build();
        }
        Color c = colour ?? new Color(1f, 0.85f, 0.3f);
        if (c.r > 0.9f && c.g < 0.6f) SoundFX.Play("warning", 0.45f, 0f);   // red = danger
        instance.Enqueue(text, Mathf.Max(seconds, 4f), c);
    }

    /// A second, persistent line under the banner (e.g. "You're in the
    /// lounge..."). Stays until changed; pass null to hide it.
    public static void SetStatus(string text, Color colour)
    {
        if (instance == null)
        {
            if (string.IsNullOrEmpty(text)) return;
            GameObject go = new GameObject("ScreenMessage");
            instance = go.AddComponent<ScreenMessage>();
            instance.Build();
        }
        bool show = !string.IsNullOrEmpty(text);
        instance.statusBackground.gameObject.SetActive(show);
        if (show)
        {
            instance.statusLabel.text = text;
            instance.statusLabel.color = colour;
        }
    }

    /// A countdown line under the status line (e.g. the open fridge).
    /// Pass null to hide it.
    public static void SetTimer(string text, Color colour)
    {
        if (instance == null)
        {
            if (string.IsNullOrEmpty(text)) return;
            GameObject go = new GameObject("ScreenMessage");
            instance = go.AddComponent<ScreenMessage>();
            instance.Build();
        }
        bool show = !string.IsNullOrEmpty(text);
        instance.timerBackground.gameObject.SetActive(show);
        if (show)
        {
            instance.timerLabel.text = text;
            instance.timerLabel.color = colour;
        }
    }

    private void Build()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400;
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(transform, false);
        RectTransform bgRect = (RectTransform)bg.transform;
        bgRect.anchorMin = bgRect.anchorMax = new Vector2(0.5f, 1f);
        bgRect.pivot = new Vector2(0.5f, 1f);
        bgRect.anchoredPosition = new Vector2(0f, -170f);
        bgRect.sizeDelta = new Vector2(1400f, 160f);
        background = bg.GetComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.8f);
        background.raycastTarget = false;

        GameObject txt = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        txt.transform.SetParent(bg.transform, false);
        RectTransform tRect = (RectTransform)txt.transform;
        tRect.anchorMin = Vector2.zero;
        tRect.anchorMax = Vector2.one;
        tRect.offsetMin = new Vector2(30f, 10f);
        tRect.offsetMax = new Vector2(-30f, -10f);
        label = txt.GetComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 52;
        label.enableAutoSizing = true;
        label.fontSizeMin = 22;
        label.fontSizeMax = 52;
        label.fontStyle = FontStyles.Bold;
        label.raycastTarget = false;

        bg.SetActive(false);

        // Persistent status line, just below the banner.
        GameObject sbg = new GameObject("StatusBackground", typeof(RectTransform), typeof(Image));
        sbg.transform.SetParent(transform, false);
        RectTransform sRect = (RectTransform)sbg.transform;
        sRect.anchorMin = sRect.anchorMax = new Vector2(0.5f, 1f);
        sRect.pivot = new Vector2(0.5f, 1f);
        sRect.anchoredPosition = new Vector2(0f, -345f);
        sRect.sizeDelta = new Vector2(1000f, 64f);
        statusBackground = sbg.GetComponent<Image>();
        statusBackground.color = new Color(0f, 0f, 0f, 0.55f);
        statusBackground.raycastTarget = false;

        GameObject stxt = new GameObject("StatusText", typeof(RectTransform), typeof(TextMeshProUGUI));
        stxt.transform.SetParent(sbg.transform, false);
        RectTransform stRect = (RectTransform)stxt.transform;
        stRect.anchorMin = Vector2.zero;
        stRect.anchorMax = Vector2.one;
        stRect.offsetMin = new Vector2(20f, 6f);
        stRect.offsetMax = new Vector2(-20f, -6f);
        statusLabel = stxt.GetComponent<TextMeshProUGUI>();
        statusLabel.alignment = TextAlignmentOptions.Center;
        statusLabel.enableAutoSizing = true;
        statusLabel.fontSizeMin = 18;
        statusLabel.fontSizeMax = 30;
        statusLabel.fontStyle = FontStyles.Bold;
        statusLabel.raycastTarget = false;
        sbg.SetActive(false);

        // Countdown line, under the status line.
        GameObject tbg = new GameObject("TimerBackground", typeof(RectTransform), typeof(Image));
        tbg.transform.SetParent(transform, false);
        RectTransform tbRect = (RectTransform)tbg.transform;
        tbRect.anchorMin = tbRect.anchorMax = new Vector2(0.5f, 1f);
        tbRect.pivot = new Vector2(0.5f, 1f);
        tbRect.anchoredPosition = new Vector2(0f, -419f);
        tbRect.sizeDelta = new Vector2(900f, 64f);
        timerBackground = tbg.GetComponent<Image>();
        timerBackground.color = new Color(0f, 0f, 0f, 0.55f);
        timerBackground.raycastTarget = false;

        GameObject ttxt = new GameObject("TimerText", typeof(RectTransform), typeof(TextMeshProUGUI));
        ttxt.transform.SetParent(tbg.transform, false);
        RectTransform ttRect = (RectTransform)ttxt.transform;
        ttRect.anchorMin = Vector2.zero;
        ttRect.anchorMax = Vector2.one;
        ttRect.offsetMin = new Vector2(20f, 6f);
        ttRect.offsetMax = new Vector2(-20f, -6f);
        timerLabel = ttxt.GetComponent<TextMeshProUGUI>();
        timerLabel.alignment = TextAlignmentOptions.Center;
        timerLabel.enableAutoSizing = true;
        timerLabel.fontSizeMin = 18;
        timerLabel.fontSizeMax = 32;
        timerLabel.fontStyle = FontStyles.Bold;
        timerLabel.raycastTarget = false;
        tbg.SetActive(false);
    }

    // Messages queue up instead of instantly replacing each other, so every
    // warning stays readable for a few seconds before the next one appears.
    private struct Msg { public string text; public float seconds; public Color colour; }
    private readonly System.Collections.Generic.Queue<Msg> queue = new System.Collections.Generic.Queue<Msg>();
    private const float MinShowTime = 2.5f;

    private void Enqueue(string text, float seconds, Color colour)
    {
        // Same text already showing/queued - don't repeat it.
        if (background.gameObject.activeSelf && label.text == text) return;
        foreach (Msg m in queue) if (m.text == text) return;
        queue.Enqueue(new Msg { text = text, seconds = seconds, colour = colour });
        if (hideRoutine == null) hideRoutine = StartCoroutine(RunQueue());
    }

    private IEnumerator RunQueue()
    {
        while (queue.Count > 0)
        {
            Msg m = queue.Dequeue();
            label.text = m.text;
            label.color = m.colour;
            background.gameObject.SetActive(true);

            // Pop in: quick grow + flash so it catches the eye.
            float t = 0f;
            RectTransform rt = (RectTransform)background.transform;
            while (t < 0.25f)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / 0.25f);
                rt.localScale = Vector3.one * Mathf.Lerp(1.25f, 1f, k);
                background.color = Color.Lerp(new Color(0.6f, 0.05f, 0.05f, 0.9f), new Color(0f, 0f, 0f, 0.8f), k);
                yield return null;
            }

            // Show for its full time, or MinShowTime if another message is waiting.
            float shown = 0.25f;
            while (shown < m.seconds && !(queue.Count > 0 && shown >= MinShowTime))
            {
                shown += Time.unscaledDeltaTime;
                yield return null;
            }
        }
        background.gameObject.SetActive(false);
        hideRoutine = null;
    }

    // (kept for older callers)
    private void Display(string text, float seconds, Color colour) { Enqueue(text, seconds, colour); }

    private IEnumerator HideAfter(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        background.gameObject.SetActive(false);
        hideRoutine = null;
    }
}
