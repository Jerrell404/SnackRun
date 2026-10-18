using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

// A short golden "power-up" glow over Bandit, played right after he eats Hammy's golden carrot: a gold outline + rim sheen that pours down over him.
public class GoldenGlowOverlay : MonoBehaviour
{
    [Tooltip("Outline thickness as a fraction of Bandit's own rendered height, so it looks the same regardless of the rig's scale.")]
    [SerializeField] private float outlineWidthFraction = 0.025f;
    [Tooltip("Peak brightness of the glow.")]
    [SerializeField] private float peakIntensity = 1.4f;
    [Tooltip("Fraction of the total duration spent sweeping from head to paws (the rest is the glow holding, then fading out).")]
    [Range(0.1f, 1f)]
    [SerializeField] private float sweepPortion = 0.6f;
    [Tooltip("Fraction of the total duration at which the fade-out begins.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float fadeStart = 0.65f;

    private const CameraEvent DrawEvent = CameraEvent.AfterForwardAlpha;

    private Material mat;
    private CommandBuffer cmd;
    private Camera attachedCam;
    private Renderer[] renderers = new Renderer[0];
    private Coroutine running;
    private bool active;

    public static void Play(GameObject target, float duration)
    {
        if (target == null || duration <= 0f) return;

        GoldenGlowOverlay overlay = target.GetComponent<GoldenGlowOverlay>();
        if (overlay == null) overlay = target.AddComponent<GoldenGlowOverlay>();
        overlay.StartGlow(duration);
    }

    public void StartGlow(float duration)
    {
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(RunGlow(duration));
    }

    private IEnumerator RunGlow(float duration)
    {
        if (!EnsureMaterial()) yield break;
        CollectRenderers();
        if (renderers.Length == 0) yield break;

        active = true;
        AttachToCamera();

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float f = Mathf.Clamp01(t / duration);

            Bounds b = CombinedBounds();
            float height = Mathf.Max(b.size.y, 0.01f);
            float softness = height * 0.06f;

            // Head -> paws, easing out so it starts quick and settles at the feet.
            // Starts just above his head and finishes a little below his paws
            // so the bright band fully clears his body by the end.
            float sweepFrac = Mathf.Clamp01(f / sweepPortion);
            float eased = 1f - (1f - sweepFrac) * (1f - sweepFrac);
            float top = b.max.y + softness * 2f;
            float bottom = b.min.y - softness * 4f;
            float sweepY = Mathf.Lerp(top, bottom, eased);

            float fadeIn = Mathf.Clamp01(f / 0.08f);
            float fadeOut = f < fadeStart ? 1f : 1f - Mathf.Clamp01((f - fadeStart) / Mathf.Max(1f - fadeStart, 0.0001f));

            mat.SetFloat("_SweepY", sweepY);
            mat.SetFloat("_SweepSoftness", softness);
            mat.SetFloat("_OutlineWidth", height * outlineWidthFraction);
            mat.SetFloat("_Intensity", peakIntensity * fadeIn * fadeOut);

            yield return null;
        }

        StopGlow();
        running = null;
    }

    private void LateUpdate()
    {
        if (!active) return;

        // Follow whichever camera is currently rendering the game.
        if (attachedCam != Camera.main) AttachToCamera();
        RebuildBuffer();
    }

    private bool EnsureMaterial()
    {
        if (mat != null) return true;

        Material source = Resources.Load<Material>("GoldenOutlineSweep");
        if (source != null)
        {
            mat = new Material(source);
        }
        else
        {
            Shader shader = Shader.Find("SnackRun/GoldenOutlineSweep");
            if (shader == null)
            {
                Debug.LogWarning("[GoldenGlowOverlay] Couldn't find the GoldenOutlineSweep material/shader - skipping the golden glow.");
                return false;
            }
            mat = new Material(shader);
        }
        return true;
    }

    // Only Bandit's actual body meshes - never text labels or particles
    // that happen to be parented under him.
    private void CollectRenderers()
    {
        var list = new List<Renderer>();
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue;
            if (r.GetComponent<TMP_Text>() != null || r.GetComponent<TextMesh>() != null) continue;
            list.Add(r);
        }
        renderers = list.ToArray();
    }

    private Bounds CombinedBounds()
    {
        Bounds b = new Bounds(transform.position, Vector3.zero);
        bool first = true;
        foreach (Renderer r in renderers)
        {
            if (r == null || !r.enabled) continue;
            if (first) { b = r.bounds; first = false; }
            else b.Encapsulate(r.bounds);
        }
        return b;
    }

    private void AttachToCamera()
    {
        DetachFromCamera();

        attachedCam = Camera.main;
        if (attachedCam == null) return;

        if (cmd == null) cmd = new CommandBuffer { name = "Golden Glow Overlay" };
        RebuildBuffer();
        attachedCam.AddCommandBuffer(DrawEvent, cmd);
    }

    private void DetachFromCamera()
    {
        if (attachedCam != null && cmd != null) attachedCam.RemoveCommandBuffer(DrawEvent, cmd);
        attachedCam = null;
    }

    private void RebuildBuffer()
    {
        if (cmd == null || mat == null) return;
        cmd.Clear();

        foreach (Renderer r in renderers)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;

            int subMeshes = 1;
            if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                subMeshes = smr.sharedMesh.subMeshCount;
            else
            {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) subMeshes = mf.sharedMesh.subMeshCount;
            }

            for (int s = 0; s < subMeshes; s++)
                cmd.DrawRenderer(r, mat, s, -1); // -1 = both passes (outline + rim)
        }
    }

    private void StopGlow()
    {
        active = false;
        DetachFromCamera();
        if (cmd != null) cmd.Clear();
    }

    private void OnDisable()
    {
        if (running != null) { StopCoroutine(running); running = null; }
        StopGlow();
    }

    private void OnDestroy()
    {
        StopGlow();
        if (cmd != null) { cmd.Release(); cmd = null; }
        if (mat != null) Destroy(mat);
    }
}
