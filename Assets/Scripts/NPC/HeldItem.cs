using System.Collections;
using UnityEngine;

// A small, self-contained visual prop for an item that travels from one character's hand to another's, gets raised up and "eaten", and vanishes.
public class HeldItem : MonoBehaviour
{
    // Shared across every HeldItem instance - built once, reused, same
    // approach TobyNPC/HammyNPC already use for their own runtime materials.
    private static Material normalMaterialCache;
    private static Material goldenMaterialCache;
    private static Material spriteMaterialCache;

    private Renderer[] visualRenderers;

    // A soft golden point light that rides along with the golden item and
    // gently pulses, so it actually lights up Bandit's paws/face at night
    // instead of only glowing on its own surface. Only created for the 3D
    // mesh path when the item turns golden.
    private Light glowLight;
    private float glowLightBaseIntensity;
    private const float GlowLightIntensity = 1.0f;
    private static readonly Color GlowLightColor = new Color(1f, 0.72f, 0.4f); // warm gold with a faint violet lean

    private AudioSource audioSource;

    private Transform followTarget;
    private Vector3 followLocalOffset;
    private Quaternion followRotationOffset = Quaternion.identity;
    private bool following;

    // True once BuildSpriteVisual has been used instead of BuildVisual. The sprite is a flat pixel-art icon with its own baked-in colour/glow.
    private bool isSpriteVisual;
    private Camera billboardCam;

    // Builds the item's visual from a mesh prefab, or a placeholder sphere if none is given.
    public void BuildVisual(GameObject modelPrefab, float modelScale = 1f, Vector3 modelRotationEuler = default)
    {
        GameObject visual;
        if (modelPrefab != null)
        {
            visual = Instantiate(modelPrefab, transform);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.Euler(modelRotationEuler);
            visual.transform.localScale = Vector3.one * modelScale;
        }
        else
        {
            visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.transform.SetParent(transform, false);
            visual.transform.localScale = Vector3.one * 0.16f;
            Collider col = visual.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        // GetComponentsInChildren (plural)
        visualRenderers = visual.GetComponentsInChildren<Renderer>();

        SetupAudio();
        ApplyMaterial(golden: false);
    }

    // Builds the item's visual from a flat pixel-art sprite instead of a 3D mesh.
    public void BuildSpriteVisual(Sprite sprite, float worldHeight)
    {
        isSpriteVisual = true;

        GameObject visual = new GameObject("SpriteVisual");
        visual.transform.SetParent(transform, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;

        SpriteRenderer sr = visual.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;

        if (spriteMaterialCache == null)
        {
            Shader spriteShader = Shader.Find("Sprites/Default");
            spriteMaterialCache = new Material(spriteShader != null ? spriteShader : Shader.Find("Standard"));
        }
        sr.material = spriteMaterialCache;

        if (sprite != null && sprite.bounds.size.y > 0.0001f && worldHeight > 0f)
        {
            float scale = worldHeight / sprite.bounds.size.y;
            visual.transform.localScale = Vector3.one * scale;
        }

        visualRenderers = new Renderer[] { sr };

        SetupAudio();
        FaceCamera(); // start out already facing the camera, not whatever rotation the caller left this object at
    }

    private void SetupAudio()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f; // it's a physical prop in the world, not UI
    }

    public void SetGolden(bool golden)
    {
        // The pixel-art sprite already has its gold/glow baked straight into
        // the art - swapping in the mesh path's glow material here would
        // just paint over the icon with a solid colour and erase it.
        if (isSpriteVisual) return;
        ApplyMaterial(golden);
    }

    private void ApplyMaterial(bool golden)
    {
        if (visualRenderers == null || visualRenderers.Length == 0) return;

        Material mat;
        if (golden)
        {
            if (goldenMaterialCache == null)
                goldenMaterialCache = LoadGoldenMaterial();
            mat = goldenMaterialCache;
        }
        else
        {
            if (normalMaterialCache == null)
            {
                normalMaterialCache = new Material(Shader.Find("Standard"));
                normalMaterialCache.color = new Color(0.95f, 0.55f, 0.15f); // neutral placeholder colour
            }
            mat = normalMaterialCache;
        }

        // The WHOLE item goes gold.
        foreach (Renderer r in visualRenderers)
        {
            if (r == null) continue;
            int slots = Mathf.Max(1, r.sharedMaterials.Length);
            Material[] mats = new Material[slots];
            for (int s = 0; s < slots; s++) mats[s] = mat;
            r.sharedMaterials = mats;
        }

        SetGlowLight(golden);
    }

    // Loads the golden "enchanted" material.
    private static Material LoadGoldenMaterial()
    {
        Material fromResources = Resources.Load<Material>("GoldenCarrotGlow");
        if (fromResources != null) return fromResources;

        Shader shader = Shader.Find("SnackRun/GoldenAppleGlow");
        if (shader != null) return new Material(shader);

        Material fallback = new Material(Shader.Find("Standard"));
        fallback.color = new Color(1f, 0.75f, 0.2f);
        fallback.SetFloat("_Metallic", 1f);
        fallback.SetFloat("_Glossiness", 0.8f);
        fallback.EnableKeyword("_EMISSION");
        fallback.SetColor("_EmissionColor", new Color(1f, 0.7f, 0.15f) * 1.5f);
        return fallback;
    }

    private void SetGlowLight(bool on)
    {
        if (!on)
        {
            if (glowLight != null) glowLight.enabled = false;
            return;
        }

        if (glowLight == null)
        {
            GameObject lightGO = new GameObject("GoldenGlowLight");
            lightGO.transform.SetParent(transform, false);
            glowLight = lightGO.AddComponent<Light>();
            glowLight.type = LightType.Point;
            glowLight.color = GlowLightColor;
            glowLight.shadows = LightShadows.None;

            // Range scales with the item's own rendered size so it's a
            // proportionate halo whatever scale the carrot ends up at.
            float size = 0.3f;
            if (visualRenderers != null && visualRenderers.Length > 0 && visualRenderers[0] != null)
            {
                Bounds b = visualRenderers[0].bounds;
                foreach (Renderer r in visualRenderers)
                    if (r != null) b.Encapsulate(r.bounds);
                size = Mathf.Max(b.size.magnitude, 0.05f);
            }
            glowLight.range = size * 2.2f;
            glowLightBaseIntensity = GlowLightIntensity;
        }

        glowLight.enabled = true;
    }

    public void Show(bool visible)
    {
        if (visualRenderers == null) return;
        foreach (Renderer r in visualRenderers)
        {
            if (r != null) r.enabled = visible;
        }
        if (glowLight != null) glowLight.enabled = visible && !isSpriteVisual;
    }

    // Turns this item to face the active camera, like a classic billboarded sprite.
    private void FaceCamera(float rollDegrees = 0f)
    {
        if (!isSpriteVisual) return;
        if (billboardCam == null) billboardCam = Camera.main;
        if (billboardCam == null) return;

        Vector3 toCam = transform.position - billboardCam.transform.position;
        if (toCam.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.LookRotation(toCam) * Quaternion.Euler(0f, 0f, rollDegrees);
    }

    // Snaps straight to a hand-offset position relative to `target` and keeps tracking it every frame after.
    public void SnapToHand(Transform target, Vector3 localOffset)
    {
        followTarget = target;
        followLocalOffset = localOffset;
        followRotationOffset = Quaternion.identity;
        following = true;
        UpdateFollowPosition();
    }

    public void StopFollowing() => following = false;

    private void LateUpdate()
    {
        if (following) UpdateFollowPosition();

        if (glowLight != null && glowLight.enabled)
        {
            // Same two-sine "breathing" as the shader, so the light and the
            // gold surface pulse together. Also scales with the item's own
            // size so the light grows in with PlayAppearEffect.
            float t = Time.time;
            float pulse = 1f + (Mathf.Sin(t * 2.2f) * 0.6f + Mathf.Sin(t * 2.2f * 1.9f + 2.1f) * 0.4f) * 0.15f;
            glowLight.intensity = glowLightBaseIntensity * pulse * Mathf.Clamp01(transform.localScale.x);
        }
    }

    private void UpdateFollowPosition()
    {
        if (followTarget == null) return;
        transform.position = followTarget.TransformPoint(followLocalOffset);

        // The sprite always turns to face the camera instead of copying the
        // hand/head bone's own rotation - a flat icon rotated to match a
        // bone's facing would go edge-on and vanish half the time.
        if (isSpriteVisual) FaceCamera();
        else transform.rotation = followTarget.rotation * followRotationOffset;
    }

    // A quick grow-in from nothing to full size, so the item reads as
    // "materialising" rather than just popping into being.
    public IEnumerator PlayAppearEffect(float duration)
    {
        Vector3 target = transform.localScale;
        transform.localScale = Vector3.zero;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            transform.localScale = Vector3.Lerp(Vector3.zero, target, Mathf.Clamp01(t / duration));
            yield return null;
        }
        transform.localScale = target;
    }

    // Flies from wherever this item currently is to `target`'s hand-offset position, with a gentle arc.
    public IEnumerator FlyTo(Transform target, Vector3 localOffset, float duration)
    {
        following = false;
        Vector3 start = transform.position;
        Vector3 roughTarget = target != null ? target.TransformPoint(localOffset) : start;
        float arcHeight = Vector3.Distance(start, roughTarget) * 0.15f;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float frac = Mathf.Clamp01(t / duration);
            float eased = 1f - (1f - frac) * (1f - frac); // ease-out
            Vector3 currentTarget = target != null ? target.TransformPoint(localOffset) : start;
            Vector3 pos = Vector3.Lerp(start, currentTarget, eased);
            pos += Vector3.up * Mathf.Sin(frac * Mathf.PI) * arcHeight;
            transform.position = pos;
            if (isSpriteVisual) FaceCamera();
            yield return null;
        }
        if (target != null) transform.position = target.TransformPoint(localOffset);
        if (isSpriteVisual) FaceCamera();
    }

    // The "bring it up to eat" motion: the item eases from `hand` toward `mouth` (raiseAmount: 0 = stays at the hand, 1 = all the way to the mouth anchor)
    public IEnumerator RaiseAndTurn(Transform hand, Vector3 handOffset, Transform mouth, Vector3 mouthOffset,
                                    float duration, float raiseAmount, float tiltDegrees)
    {
        following = false;
        Vector3 start = transform.position;
        Quaternion startRot = transform.rotation;
        raiseAmount = Mathf.Clamp01(raiseAmount);

        Vector3 dest = start;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float frac = Mathf.Clamp01(t / duration);
            float eased = frac * frac * (3f - 2f * frac); // smoothstep

            dest = RaisedPoint(hand, handOffset, mouth, mouthOffset, raiseAmount, start);
            transform.position = Vector3.Lerp(start, dest, eased);

            if (isSpriteVisual)
            {
                FaceCamera(tiltDegrees * eased);
            }
            else
            {
                Quaternion baseRot = hand != null ? hand.rotation : startRot;
                transform.rotation = baseRot * Quaternion.Euler(0f, 0f, tiltDegrees * eased);
            }
            yield return null;
        }

        dest = RaisedPoint(hand, handOffset, mouth, mouthOffset, raiseAmount, start);
        transform.position = dest;

        // Keep riding along with the hand at the raised spot for the eat beat.
        if (hand != null)
        {
            followTarget = hand;
            followLocalOffset = hand.InverseTransformPoint(dest);
            followRotationOffset = Quaternion.Euler(0f, 0f, tiltDegrees);
            following = true;
        }
    }

    private static Vector3 RaisedPoint(Transform hand, Vector3 handOffset, Transform mouth, Vector3 mouthOffset,
                                       float raiseAmount, Vector3 fallback)
    {
        Vector3 from = hand != null ? hand.TransformPoint(handOffset) : fallback;
        Vector3 to = mouth != null ? mouth.TransformPoint(mouthOffset) : from;
        return Vector3.Lerp(from, to, raiseAmount);
    }

    // Plays the eating sound, then a short beat later makes the item vanish (eaten in one go, no bite-by-bite shrinking)
    public IEnumerator PlayEatSequence(AudioClip eatingClip, float totalDuration, System.Action onEaten = null)
    {
        // Heard clearly wherever the camera is (a 2D sound, not a quiet 3D
        // one positioned on the carrot itself), plus a crunchy chomp-chomp-
        // chomp on top.
        if (audioSource != null && eatingClip != null)
        {
            audioSource.spatialBlend = 0f;
            audioSource.PlayOneShot(eatingClip, 1.6f * SoundFX.SfxVolumeScale);
        }
        SoundFX.Play("carrot_crunch", 0.9f, 0.03f);

        float vanishAt = Mathf.Min(totalDuration * 0.35f, 0.6f);
        if (vanishAt > 0f) yield return new WaitForSeconds(vanishAt);

        Show(false);
        onEaten?.Invoke();

        float remaining = totalDuration - vanishAt;
        if (remaining > 0f) yield return new WaitForSeconds(remaining);
    }
}
