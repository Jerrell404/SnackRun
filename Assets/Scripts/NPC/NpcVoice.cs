using System.Collections.Generic;
using UnityEngine;

// Gives an NPC a voice and speech bubbles: plays a voice line.
public class NpcVoice : MonoBehaviour
{
    public Color bubbleColour = Color.white;
    public float bubbleSize = 0.035f;
    public float markSize = 0.07f;

    private AudioSource source;
    private TextMesh bubble, mark;
    private float bubbleUntil, markUntil, quietUntil;
    private readonly Dictionary<string, AudioClip[]> clips = new Dictionary<string, AudioClip[]>();
    private Renderer[] renderers;

    public static NpcVoice For(Component npc)
    {
        NpcVoice v = npc.GetComponent<NpcVoice>();
        if (v == null) v = npc.gameObject.AddComponent<NpcVoice>();
        return v;
    }

    private void Awake()
    {
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0.5f;     // a bit directional, still easy to hear
        source.minDistance = 4f;
        source.maxDistance = 40f;
        renderers = GetComponentsInChildren<Renderer>();
    }

    /// Says a line. Won't talk over itself: after speaking it stays quiet for
    /// "quietFor" seconds unless force is true.
    public void Say(string clipName, string subtitle, float quietFor = 2.5f, bool force = false, float volume = 1f)
    {
        if (!force && Time.time < quietUntil) return;
        AudioClip c = Pick(clipName);
        if (c != null) { source.Stop(); source.PlayOneShot(c, volume * SoundFX.SfxVolumeScale); }
        if (!string.IsNullOrEmpty(subtitle))
        {
            EnsureTexts();
            bubble.text = subtitle;
            bubbleUntil = Time.time + Mathf.Max(1.8f, c != null ? c.length + 0.8f : 1.8f);
        }
        quietUntil = Time.time + quietFor;
    }

    /// Shows a mark above the head for a while ("!" spotted, "?" searching, "z z" asleep).
    public void ShowMark(string text, Color colour, float seconds)
    {
        EnsureTexts();
        mark.text = text;
        mark.color = colour;
        markUntil = Mathf.Max(markUntil, Time.time + seconds);
        if (mark.text != text) markUntil = Time.time + seconds;
    }

    public void HideMark() { markUntil = 0f; }

    private AudioClip Pick(string name)
    {
        AudioClip[] list;
        if (!clips.TryGetValue(name, out list))
        {
            var found = new List<AudioClip>();
            AudioClip one = Resources.Load<AudioClip>("Audio/Voices/" + name);
            if (one != null) found.Add(one);
            for (int i = 1; i <= 9; i++)
            {
                AudioClip c = Resources.Load<AudioClip>("Audio/Voices/" + name + "_" + i);
                if (c == null) break;
                found.Add(c);
            }
            list = found.ToArray();
            clips[name] = list;
        }
        return list.Length == 0 ? null : list[Random.Range(0, list.Length)];
    }

    private void EnsureTexts()
    {
        if (bubble != null) return;
        bubble = MakeText("SpeechBubble", bubbleSize, 60);
        bubble.color = bubbleColour;
        mark = MakeText("Mark", markSize, 90);
    }

    // Explicitly depth-tested (see WorldTextOccluded.shader) so this text is
    // properly hidden behind walls/furniture the same as any other object in
    // the scene, rather than clipping straight through them - resolved lazily
    // once and shared by every NpcVoice instance.
    private static Shader worldTextShader;

    private TextMesh MakeText(string name, float size, int font)
    {
        GameObject g = new GameObject(name);
        TextMesh t = g.AddComponent<TextMesh>();
        t.fontSize = font;
        t.characterSize = size;
        t.anchor = TextAnchor.LowerCenter;
        t.alignment = TextAlignment.Center;
        t.fontStyle = FontStyle.Bold;

        Renderer r = g.GetComponent<Renderer>();
        if (r != null)
        {
            if (worldTextShader == null) worldTextShader = Shader.Find("Custom/World Text (Depth Tested)");
            if (worldTextShader != null)
            {
                Texture fontTex = t.font != null ? t.font.material.mainTexture
                    : (r.sharedMaterial != null ? r.sharedMaterial.mainTexture : null);
                Material mat = new Material(worldTextShader);
                if (fontTex != null) mat.mainTexture = fontTex;
                r.material = mat; // instanced - safe per TextMesh, doesn't share back
            }
        }

        g.SetActive(false);
        return t;
    }

    private void LateUpdate()
    {
        if (bubble == null) return;
        bool showBubble = Time.time < bubbleUntil;
        bool showMark = Time.time < markUntil;
        bubble.gameObject.SetActive(showBubble);
        mark.gameObject.SetActive(showMark);
        if (!showBubble && !showMark) return;

        float top = transform.position.y + 0.5f;
        Vector3 centre = transform.position;
        foreach (Renderer r in renderers)
        {
            if (r == null || !r.enabled || r is ParticleSystemRenderer) continue;
            if (r.bounds.max.y > top) { top = r.bounds.max.y; }
        }
        Bounds b = new Bounds(transform.position, Vector3.zero);
        foreach (Renderer r in renderers) if (r != null && r.enabled && !(r is ParticleSystemRenderer)) b.Encapsulate(r.bounds);
        centre = new Vector3(b.center.x, 0f, b.center.z);

        Quaternion face = Camera.main != null ? Quaternion.LookRotation(Camera.main.transform.forward) : Quaternion.identity;
        mark.transform.SetPositionAndRotation(new Vector3(centre.x, top + 0.2f, centre.z), face);
        float markHeight = showMark ? markSize * 12f : 0f;
        bubble.transform.SetPositionAndRotation(new Vector3(centre.x, top + 0.35f + markHeight + (gameObject.name.Contains("Edgar") ? 0.45f : 0f), centre.z), face);
    }

    private void OnDestroy()
    {
        if (bubble != null) Destroy(bubble.gameObject);
        if (mark != null) Destroy(mark.gameObject);
    }
}
