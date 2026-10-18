using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LevelExit : MonoBehaviour
{
    [SerializeField] private string nextSceneName;
    [SerializeField] private bool requiresAllFood = false; // true only for the backyard exit

    // Raised when the player reaches a food-gated exit without all the
    // required food yet. UIManager listens for this to show a warning with
    // how many items are still left to collect.
    public static event Action<int> OnInsufficientFood;

    [Header("Hop through the door flap")]
    [Tooltip("How far Bandit hops forward through the flap while the screen fades.")]
    [SerializeField] private float hopDistance = 1.6f;
    [SerializeField] private float hopHeight = 0.35f;
    [SerializeField] private float hopTime = 0.35f;
    private bool triggered;

    // Stretch the door trigger down to floor level so walking into the doggy door works.
    private void Awake()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null || !box.isTrigger) return;
        Bounds b = box.bounds;
        float groundY = b.min.y;
        foreach (RaycastHit h in Physics.RaycastAll(b.center, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.transform.IsChildOf(transform)) continue;
            groundY = h.point.y;
            break;
        }
        float missing = b.min.y - (groundY - 0.2f);
        Vector3 s = transform.lossyScale;
        if (missing > 0f && Mathf.Abs(s.y) > 0.0001f)
        {
            Vector3 size = box.size, centre = box.center;
            size.y += missing / Mathf.Abs(s.y);
            centre.y -= missing * 0.5f / s.y;
            box.size = size;
            box.center = centre;
        }

        // Make the trigger reach out in front of (and behind) the door panel,
        // so it fires as Bandit walks up to the flap - before he bumps into
        // the solid door and ends up running on the spot against it.
        const float reachOut = 1.0f;   // world units added on each side
        Vector3 sz = box.size;
        if (Mathf.Abs(s.x) > 0.0001f) sz.x += 2f * reachOut / Mathf.Abs(s.x);
        if (Mathf.Abs(s.z) > 0.0001f) sz.z += 2f * reachOut / Mathf.Abs(s.z);
        box.size = sz;
    }

    // Also catch the case where Bandit is already standing inside the trigger.
    void OnTriggerStay(Collider other)
    {
        // (No repeated "food left" warnings while standing there.)
        if (requiresAllFood && GameManager.Instance != null && !GameManager.Instance.HasAllFood()) return;
        OnTriggerEnter(other);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (SceneFader.IsBusy || triggered) return;
        if (requiresAllFood && !GameManager.Instance.HasAllFood())
        {
            int remaining = GameManager.Instance.RemainingFoodCount();
            Debug.Log("You need all the food before leaving. Remaining: " + remaining);
            OnInsufficientFood?.Invoke(remaining);
            return;
        }
        if (requiresAllFood && GameManager.Instance != null)
        {
            // Bandit is actually walking out of the House with everything
            // required - lock this in permanently so a death anywhere later
            // in the Backyard (Rex, the lasers, ...) can never retroactively
            // undo it and block WinExit. See GameManager.collectedAllFoodFromHouse.
            GameManager.Instance.LockInAllFoodCollected();
        }
        triggered = true;
        SoundFX.Play("door_flap", 0.9f);
        StartCoroutine(HopThrough(other.transform.root));
        SceneFader.LoadScene(nextSceneName);
    }

    // Bandit dives through the flap (controls off) while the screen fades out.
    private IEnumerator HopThrough(Transform player)
    {
        PlayerController pc = player.GetComponentInChildren<PlayerController>();
        if (pc != null) pc.enabled = false;
        CharacterController cc = player.GetComponentInChildren<CharacterController>();
        if (cc != null) cc.enabled = false;   // hop straight through the flap - no bumping into the door
        Vector3 dir = player.forward; dir.y = 0f;
        dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward;

        float t = 0f, lastY = 0f;
        while (t < hopTime && player != null)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / hopTime);
            float y = Mathf.Sin(k * Mathf.PI) * hopHeight;
            Vector3 step = dir * (hopDistance / hopTime) * Time.deltaTime + Vector3.up * (y - lastY);
            lastY = y;
            if (cc != null && cc.enabled) cc.Move(step); else player.position += step;
            yield return null;
        }
    }
}
