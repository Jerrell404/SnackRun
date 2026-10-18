using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Shows that Bandit got scratched by Whiskers: 1.
public class ScratchEffect : MonoBehaviour
{
    private static ScratchEffect instance;

    private Canvas canvas;
    private readonly List<GameObject> marks = new List<GameObject>();

    public static void Play(Transform player, Vector3 fromPosition)
    {
        if (instance == null)
        {
            GameObject go = new GameObject("ScratchEffect");
            instance = go.AddComponent<ScratchEffect>(); // lives with the scene, reset on reload
        }
        instance.StartCoroutine(instance.ScreenSlash());
        if (player != null) instance.AddMarks(player, fromPosition);
    }

    public static void ClearMarks()
    {
        if (instance == null) return;
        foreach (GameObject m in instance.marks) if (m != null) Destroy(m);
        instance.marks.Clear();
    }

    // ------------------------------------------------------------------
    //  1. On-screen claw swipe
    // ------------------------------------------------------------------
    private IEnumerator ScreenSlash()
    {
        if (canvas == null)
        {
            GameObject c = new GameObject("ScratchCanvas");
            c.transform.SetParent(transform, false);
            canvas = c.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            c.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        }

        // Three parallel diagonal slashes, slightly random each time.
        float angle = Random.Range(-40f, -25f) * (Random.value < 0.5f ? 1f : -1f);
        Vector2 centre = new Vector2(Random.Range(-120f, 120f), Random.Range(-60f, 60f));
        List<Image> slashes = new List<Image>();
        for (int i = 0; i < 3; i++)
        {
            GameObject s = new GameObject("Slash" + i, typeof(RectTransform), typeof(Image));
            s.transform.SetParent(canvas.transform, false);
            RectTransform rt = (RectTransform)s.transform;
            rt.sizeDelta = new Vector2(520f, 16f - i * 2f);
            rt.localRotation = Quaternion.Euler(0f, 0f, angle);
            Vector2 perp = Quaternion.Euler(0f, 0f, angle) * Vector2.up;
            rt.anchoredPosition = centre + perp * (i - 1) * 42f;
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition -= (Vector2)(Quaternion.Euler(0f, 0f, angle) * Vector2.right) * 260f;
            Image img = s.GetComponent<Image>();
            img.color = new Color(0.85f, 0.05f, 0.05f, 0.9f);
            img.raycastTarget = false;
            rt.localScale = new Vector3(0f, 1f, 1f);
            slashes.Add(img);
        }

        // Swipe in fast (staggered), then fade.
        float t = 0f;
        const float swipe = 0.12f, hold = 0.15f, fade = 0.45f;
        while (t < swipe + 0.08f + hold + fade)
        {
            t += Time.unscaledDeltaTime;
            for (int i = 0; i < slashes.Count; i++)
            {
                float k = Mathf.Clamp01((t - i * 0.04f) / swipe);
                slashes[i].rectTransform.localScale = new Vector3(1f - Mathf.Pow(1f - k, 3f), 1f, 1f);
                float a = t < swipe + 0.08f + hold ? 0.9f : Mathf.Lerp(0.9f, 0f, (t - swipe - 0.08f - hold) / fade);
                Color c = slashes[i].color; c.a = a; slashes[i].color = c;
            }
            yield return null;
        }
        foreach (Image img in slashes) Destroy(img.gameObject);
    }

    // ------------------------------------------------------------------
    //  2. Scratch marks on Bandit
    // ------------------------------------------------------------------
    private void AddMarks(Transform player, Vector3 fromPosition)
    {
        // On the back of Bandit's HEAD - the part the camera (behind him)
        // always sees, and not covered by the backpack.
        Transform head = FindPart(player, "Head");
        Renderer headR = head != null ? head.GetComponent<Renderer>() : null;
        if (headR == null) return;

        // Replace the previous set - one set of marks per hurt state.
        ClearMarks();

        // Head size/position in Bandit's own space (he faces +Z, camera is behind).
        MeshFilter headMesh = head.GetComponent<MeshFilter>();
        Bounds lb = headMesh != null && headMesh.sharedMesh != null
            ? MeshBoundsIn(player, head, headMesh.sharedMesh.bounds)   // tight, follows the head's own shape
            : LocalBounds(player, headR.bounds);
        Vector3 backOfHeadLocal = new Vector3(lb.center.x, lb.center.y + lb.size.y * 0.05f, lb.min.z);
        Vector3 surface = player.TransformPoint(backOfHeadLocal);
        float size = lb.size.y * player.lossyScale.y;   // head height in world units
        Vector3 outward = -player.forward;

        FurBurst(surface, outward, size * 1.5f);

        GameObject group = new GameObject("ScratchMarks");
        group.transform.position = surface + outward * size * 0.01f;
        group.transform.rotation = Quaternion.LookRotation(player.forward, Vector3.up); // flat against the head
        group.transform.SetParent(head, true); // follows Bandit's animations

        Color red = new Color(0.75f, 0.03f, 0.05f);
        for (int i = 0; i < 3; i++)
        {
            GameObject m = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(m.GetComponent<Collider>());
            m.name = "Scratch" + i;
            m.transform.SetParent(group.transform, false);
            m.transform.localRotation = Quaternion.Euler(0f, 0f, 30f);
            m.transform.position = group.transform.position + group.transform.right * (i - 1) * size * 0.16f;
            Vector3 lossy = group.transform.lossyScale;
            m.transform.localScale = new Vector3(
                size * 0.07f / Mathf.Max(0.0001f, lossy.x),
                size * 0.55f / Mathf.Max(0.0001f, lossy.y),
                size * 0.05f / Mathf.Max(0.0001f, lossy.z));
            Renderer r = m.GetComponent<Renderer>();
            r.material.color = red;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        marks.Add(group);
        StartCoroutine(ClearWhenHealed(player));
    }

    // A puff of grey/cream fur flying off where he was hit.
    private void FurBurst(Vector3 at, Vector3 outward, float size)
    {
        StartCoroutine(FurRoutine(at, outward, size));
    }

    private IEnumerator FurRoutine(Vector3 at, Vector3 outward, float size)
    {
        const int count = 10;
        const float life = 0.7f;
        GameObject[] bits = new GameObject[count];
        Vector3[] vel = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(g.GetComponent<Collider>());
            g.transform.position = at;
            g.transform.rotation = Random.rotation;
            g.transform.localScale = Vector3.one * size * Random.Range(0.04f, 0.08f);
            Renderer r = g.GetComponent<Renderer>();
            r.material.color = Random.value < 0.6f ? new Color(0.45f, 0.45f, 0.48f) : new Color(0.9f, 0.86f, 0.78f);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bits[i] = g;
            vel[i] = (outward + Random.insideUnitSphere * 0.8f + Vector3.up * 0.6f).normalized * size * Random.Range(1.5f, 3f);
        }
        float t = 0f;
        while (t < life)
        {
            float dt = Time.unscaledDeltaTime;
            t += dt;
            for (int i = 0; i < count; i++)
            {
                if (bits[i] == null) continue;
                vel[i] += Vector3.down * size * 6f * dt;
                bits[i].transform.position += vel[i] * dt;
                bits[i].transform.Rotate(300f * dt, 200f * dt, 0f);
                bits[i].transform.localScale *= 1f - dt * 1.2f;
            }
            yield return null;
        }
        foreach (GameObject g in bits) if (g != null) Destroy(g);
    }

    // Marks stay while Bandit is on half health; gone once he's healed.
    private IEnumerator ClearWhenHealed(Transform player)
    {
        PlayerHealth health = player.GetComponent<PlayerHealth>();
        if (health == null) yield break;
        yield return new WaitForSeconds(0.5f);
        while (health != null && health.IsAtHalfHealth()) yield return null;
        ClearMarks();
    }

    private static Transform FindPart(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    private static Bounds LocalBounds(Transform space, Bounds world)
    {
        Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            Vector3 c = new Vector3((i & 1) == 0 ? world.min.x : world.max.x,
                                    (i & 2) == 0 ? world.min.y : world.max.y,
                                    (i & 4) == 0 ? world.min.z : world.max.z);
            Vector3 l = space.InverseTransformPoint(c);
            min = Vector3.Min(min, l);
            max = Vector3.Max(max, l);
        }
        Bounds b = new Bounds();
        b.SetMinMax(min, max);
        return b;
    }

    // The mesh's own box (in the head's space) converted into Bandit's space -
    // tighter than the world-aligned renderer box, so the marks sit ON the head.
    private static Bounds MeshBoundsIn(Transform space, Transform part, Bounds meshBounds)
    {
        Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            Vector3 c = new Vector3((i & 1) == 0 ? meshBounds.min.x : meshBounds.max.x,
                                    (i & 2) == 0 ? meshBounds.min.y : meshBounds.max.y,
                                    (i & 4) == 0 ? meshBounds.min.z : meshBounds.max.z);
            Vector3 l = space.InverseTransformPoint(part.TransformPoint(c));
            min = Vector3.Min(min, l);
            max = Vector3.Max(max, l);
        }
        Bounds b = new Bounds();
        b.SetMinMax(min, max);
        return b;
    }
}
