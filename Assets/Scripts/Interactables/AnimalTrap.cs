using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class AnimalTrap : MonoBehaviour
{
    [Header("Snap-shut animation (procedural - no Animator needed)")]
    [Tooltip("The part(s) of the trap that swing shut when triggered (e.g. the two jaw halves). Leave empty and the whole trap will just do a quick squash reaction instead.")]
    [SerializeField] private Transform[] jaws;
    [Tooltip("How far each jaw rotates (degrees) as it snaps shut, around its local X axis. Jaws alternate direction so opposing halves close towards each other.")]
    [SerializeField] private float jawCloseAngle = 45f;
    [SerializeField] private float closeDuration = 0.15f;
    [Tooltip("Fallback-only (no jaws assigned): how far the trap folds inward on X/Z, e.g. 0.5 = collapses to half width.")]
    [SerializeField] private float foldInwardAmount = 0.55f;
    [Tooltip("Fallback-only (no jaws assigned): how flat the trap squashes down on Y once triggered.")]
    [SerializeField] private float flattenAmount = 0.35f;

    // Lets UIManager (or anything else) react the instant a trap goes off,
    // without the trap needing to know anything about UI.
    public static event Action OnTrapTriggered;

    private bool triggered = false;

    // Only ONE trap can catch Bandit per life. Once any trap has gone off,
    // every other trap in the level stays harmless until the level reloads
    // for the next life (a fresh life = all traps armed again).
    private static bool trapSprungThisLife = false;
    public static bool TrapSprungThisLife => trapSprungThisLife;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        trapSprungThisLife = false;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single) trapSprungThisLife = false;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player") && !triggered && !trapSprungThisLife)
        {
            triggered = true; // one-time trap; remove this line if it should re-trigger
            trapSprungThisLife = true;

            // A trap is deadly: the jaws snap shut on Bandit, he collapses,
            // and he loses a whole life (the level restarts).
            StartCoroutine(SnapShut());
            StartCoroutine(SnapJaws());
            SoundFX.Play("trap_snap", 1f, 0f);
            DangerFeedback.Hit();
            ScreenMessage.Show("SNAP! Bandit is caught in a trap!", 3f, new Color(1f, 0.3f, 0.25f));
            StartCoroutine(CatchBandit(other.transform.root));
            Debug.Log("SNAP! Bandit stepped in a trap - a life is lost.");
        }
    }

    [Header("Catching Bandit")]
    [Tooltip("Seconds Bandit lies there, caught, ON TOP OF the ~2 second fall-and-hold above, before the life is lost and the level restarts. Left at 0.")]
    [SerializeField] private float caughtTime = 0f;

    // Bandit can't move, falls forward into the trap, and then loses a life.
    private IEnumerator CatchBandit(Transform player)
    {
        // From right now until LoseLife() actually fires below, Bandit is falling/pinned at normal Time.timeScale (not yet frozen)
        if (GameManager.Instance != null) GameManager.Instance.hazardsSuspended = true;

        PlayerController pc = player.GetComponentInChildren<PlayerController>();
        if (pc != null) pc.enabled = false;

        // Freeze the camera too: mouse look turns Bandit's whole body, so
        // leaving it on let the player spin him around while pinned in the
        // trap. Same approach Rex uses while holding Bandit. The level
        // reload for the next life brings a fresh, enabled camera back.
        foreach (PT_MouseLook look in FindObjectsByType<PT_MouseLook>(FindObjectsSortMode.None))
            look.enabled = false;

        // Disabling PlayerController only stops HIS Update (movement/input)
        Animator anim = player.GetComponentInChildren<Animator>();
        if (anim != null) anim.enabled = false;

        Transform body = null;
        foreach (Transform t in player.GetComponentsInChildren<Transform>())
            if (t.name == "Raccoon_Root") { body = t; break; }

        if (body != null)
        {
            Quaternion start = body.rotation;
            Vector3 startPos = body.position;
            Vector3 axis = player.right;
            float t = 0f;
            while (t < 2f)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / 0.35f);
                body.rotation = Quaternion.AngleAxis(80f * k, axis) * start;   // pitches forward, into the trap
                body.position = startPos + (Vector3.down * 0.15f + player.forward * 0.15f) * k;
                yield return null;
            }
        }

        yield return new WaitForSeconds(caughtTime);

        if (GameManager.Instance != null)
        {
            // The black/red "SNAP! Bandit is caught in a trap!" popup at the top of this script already told the player what happened.
            GameManager.Instance.suppressLifeLostMessage = true;

            // Cancel out RestartSceneOnLifeLoss's own freezeDuration for THIS life loss only.
            GameManager.Instance.pendingFreezeDurationBonus = -10f;
            GameManager.Instance.LoseLife();
            GameManager.Instance.hazardsSuspended = false;
        }

        // Keep Bandit pinned down on the ground for however long it takes the level to actually restart.
        if (body != null)
        {
            Quaternion fallenRotation = body.rotation;
            Vector3 fallenPosition = body.position;
            while (true)
            {
                body.rotation = fallenRotation;
                body.position = fallenPosition;
                yield return null;
            }
        }
    }

    // Two metal jaws with teeth spring up out of the trap and clamp shut.
    private IEnumerator SnapJaws()
    {
        Bounds b = new Bounds(transform.position, Vector3.zero); bool any = false;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        float radius = Mathf.Max(0.12f, Mathf.Min(b.extents.x, b.extents.z) * 0.75f);
        Vector3 centre = new Vector3(b.center.x, b.min.y + 0.02f, b.center.z);

        Material metal = new Material(Shader.Find("Standard"));
        metal.color = new Color(0.32f, 0.33f, 0.35f);
        metal.SetFloat("_Metallic", 0.8f);
        metal.SetFloat("_Glossiness", 0.55f);

        GameObject root = new GameObject("TrapJaws");
        root.transform.position = centre;
        root.transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        Transform jawA = MakeJaw(root.transform, radius, metal, 1f);
        Transform jawB = MakeJaw(root.transform, radius, metal, -1f);

        const float openAngle = 88f, closedAngle = 6f, snapTime = 0.09f;
        float t = 0f;
        while (t < snapTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / snapTime);
            float a = Mathf.Lerp(openAngle, closedAngle, k * k);
            jawA.localRotation = Quaternion.Euler(a, 0f, 0f);
            jawB.localRotation = Quaternion.Euler(-a, 0f, 0f);
            yield return null;
        }
        // Tiny bounce as the teeth bite.
        for (int i = 0; i < 2; i++)
        {
            jawA.localRotation = Quaternion.Euler(closedAngle + 4f, 0f, 0f);
            jawB.localRotation = Quaternion.Euler(-closedAngle - 4f, 0f, 0f);
            yield return new WaitForSeconds(0.04f);
            jawA.localRotation = Quaternion.Euler(closedAngle, 0f, 0f);
            jawB.localRotation = Quaternion.Euler(-closedAngle, 0f, 0f);
            yield return new WaitForSeconds(0.04f);
        }
    }

    // A half-ring of metal (with teeth) hinged along the trap's middle.
    private static Transform MakeJaw(Transform parent, float radius, Material mat, float side)
    {
        GameObject jaw = new GameObject(side > 0 ? "JawA" : "JawB");
        jaw.transform.SetParent(parent, false);
        jaw.transform.localRotation = Quaternion.Euler(88f * side, 0f, 0f);
        const int segments = 9;
        float thick = radius * 0.12f;
        for (int i = 0; i <= segments; i++)
        {
            float ang = Mathf.PI * i / segments;                       // 0..180 degrees
            Vector3 p = new Vector3(Mathf.Cos(ang) * radius, Mathf.Sin(ang) * radius, 0f);
            GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.Destroy(seg.GetComponent<Collider>());
            seg.transform.SetParent(jaw.transform, false);
            seg.transform.localPosition = p;
            seg.transform.localRotation = Quaternion.Euler(0f, 0f, ang * Mathf.Rad2Deg + 90f);
            seg.transform.localScale = new Vector3(radius * Mathf.PI / segments * 1.1f, thick, thick);
            seg.GetComponent<Renderer>().sharedMaterial = mat;
            if (i > 0 && i < segments)
            {
                // Tooth pointing inwards.
                GameObject tooth = GameObject.CreatePrimitive(PrimitiveType.Cube);
                UnityEngine.Object.Destroy(tooth.GetComponent<Collider>());
                tooth.transform.SetParent(jaw.transform, false);
                tooth.transform.localPosition = p * 0.86f;
                tooth.transform.localRotation = Quaternion.Euler(0f, 0f, ang * Mathf.Rad2Deg + 45f);
                tooth.transform.localScale = Vector3.one * thick * 0.9f;
                tooth.GetComponent<Renderer>().sharedMaterial = mat;
            }
        }
        return jaw.transform;
    }

    private IEnumerator SnapShut()
    {
        if (jaws != null && jaws.Length > 0)
        {
            Quaternion[] startRotations = new Quaternion[jaws.Length];
            for (int i = 0; i < jaws.Length; i++)
            {
                if (jaws[i] != null) startRotations[i] = jaws[i].localRotation;
            }

            float t = 0f;
            while (t < closeDuration)
            {
                t += Time.deltaTime;
                float frac = Mathf.Clamp01(t / closeDuration);
                for (int i = 0; i < jaws.Length; i++)
                {
                    if (jaws[i] == null) continue;
                    // Alternate the swing direction per jaw so a pair of
                    // opposing jaws close towards each other.
                    float sign = (i % 2 == 0) ? 1f : -1f;
                    jaws[i].localRotation = startRotations[i] * Quaternion.Euler(sign * jawCloseAngle * frac, 0, 0);
                }
                yield return null;
            }
        }
        else
        {
            // No jaw parts assigned.
            Vector3 startScale = transform.localScale;
            Vector3 folded = new Vector3(startScale.x * foldInwardAmount, startScale.y * flattenAmount, startScale.z * foldInwardAmount);

            float t = 0f;
            while (t < closeDuration)
            {
                t += Time.deltaTime;
                float frac = Mathf.Clamp01(t / closeDuration);
                // Ease-out so the fold snaps hard at the start and settles
                // gently into its collapsed shape, rather than a flat lerp.
                float eased = 1f - (1f - frac) * (1f - frac);
                transform.localScale = Vector3.Lerp(startScale, folded, eased);
                yield return null;
            }

            transform.localScale = folded;
        }
    }
}
