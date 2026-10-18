using System;
using UnityEngine;
using UnityEngine.Events;

// Crunchy chip bag: stepping on it wakes Whiskers and escalates Edgar's alert level.
public class ChipPacket : MonoBehaviour
{
    [Header("Audio")]
    [Tooltip("Drag the chip crunch audio clip here (stepping on chips sound.mp3).")]
    [SerializeField] private AudioClip chipSound;

    [Header("Step Cooldown")]
    [Tooltip("Seconds that must pass before the bag can trigger again. " +
             "Applies to both the sound and all NPC alerts.")]
    [SerializeField] private float stepCooldown = 1.5f;

    [Header("Events (optional extra hookups)")]
    public UnityEvent onNoiseMade;

    // Fired on Bandit's very first crunch -- Whiskers only.
    public static event Action OnFirstNoise;

    // Fired on the second crunch and every crunch after that --
    // Whiskers re-alerted, and Edgar is put on alert (2nd crunch) or sent
    // straight at Bandit (3rd crunch+).
    public static event Action OnSecondNoise;

    private bool playerInside = false;
    private float cooldownTimer = 0f;   // 0 = ready to fire; counts down after each trigger
    private int stepCount = 0;          // lifetime trigger count, never resets

    private void Awake()
    {
        // If the Inspector didn't wire the clip (e.g. fresh scene load),
        // fall back to loading from Assets/Resources/Audio/ChipCrunch.mp3.
        if (chipSound == null)
            chipSound = Resources.Load<AudioClip>("Audio/ChipCrunch");

        if (chipSound == null)
            Debug.LogWarning("[ChipPacket] No audio clip assigned and ChipCrunch.mp3 not found " +
                             "in Resources/Audio/ -- drag the clip into the Inspector.");
    }

    private void Update()
    {
        // Tick the cooldown.
        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime;

        // A crunch only happens when Bandit STEPS ONTO the bag (see
        // OnTriggerEnter) - standing on it does not keep crunching every
        // few seconds.
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        bool wasInside = playerInside;
        playerInside = true;
        // New step onto the bag = one crunch. The short cooldown stops a
        // wobble on the edge of the bag counting as several steps.
        if (!wasInside && cooldownTimer <= 0f)
            TriggerNoise();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        playerInside = false;
        // Bandit stepped off: the bag puffs back up, ready to be crushed again.
        if (crushed)
        {
            if (squashRoutine != null) StopCoroutine(squashRoutine);
            squashRoutine = StartCoroutine(Puff());
        }
    }

    private System.Collections.IEnumerator Puff()
    {
        CacheVisuals();
        if (visuals.Length == 0) { crushed = false; squashRoutine = null; yield break; }
        float t = 0f;
        const float dur = 0.45f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
            // overshoot slightly, then settle
            float over = 1f + 0.08f * Mathf.Sin(k * Mathf.PI);
            SetSquash(Mathf.Lerp(squashedHeight, 1f, k) * over, Mathf.Lerp(squashedSpread, 1f, k));
            yield return null;
        }
        SetSquash(1f, 1f);
        crushed = false;
        squashRoutine = null;
    }

    private void TriggerNoise()
    {
        // Arm the cooldown before anything else so a single frame
        // can never double-fire before the timer starts ticking.
        cooldownTimer = stepCooldown;
        stepCount++;

        // PlayClipAtPoint spawns a temporary AudioSource at this world position --
        // no AudioSource component needed on the chip bag object itself.
        if (chipSound != null)
            AudioSource.PlayClipAtPoint(chipSound, transform.position, SoundFX.SfxVolumeScale);
        else
            Debug.LogWarning("[ChipPacket] TriggerNoise: chipSound is null, no sound played.");

        onNoiseMade?.Invoke();

        // Visibly crush the bag under Bandit (and wiggle it again on later steps).
        if (squashRoutine != null) StopCoroutine(squashRoutine);
        squashRoutine = StartCoroutine(Squash(stepCount == 1));

        if (stepCount == 1)
        {
            // First crunch: Whiskers wakes up and chases.
            Debug.Log("Chip packet noise! Whiskers is alerted.");
            if (CatNPC.Instance != null)
                CatNPC.Instance.WakeUp();
            OnFirstNoise?.Invoke();
        }
        else if (stepCount == 2)
        {
            // Second crunch: Whiskers re-alerted; Edgar is put on alert but
            // doesn't move yet - he sets off on his sofa -> Whiskers ->
            // fridge -> backdoor route once the kitchen timer is also low
            // (see HumanNPC.ArmChipEscalationPatrol).
            Debug.Log("Chip packet noise #2 -- Whiskers re-alerted; Edgar is on alert.");
            if (CatNPC.Instance != null)
                CatNPC.Instance.WakeUp();
            if (HumanNPC.Instance != null)
                HumanNPC.Instance.ArmChipEscalationPatrol();
            // Edgar doesn't come yet - so don't say he's awake and chasing.
            KitchenTimer timer = FindFirstObjectByType<KitchenTimer>();
            bool clockLow = timer != null && timer.timeRemaining <= 10f;
            ScreenMessage.Show(clockLow
                ? "CRUNCH! Edgar heard that - he's getting up to check the house. Hide!"
                : "CRUNCH! Edgar heard that - he'll come looking when the timer runs low. One more crunch and he comes straight for you!",
                5f, new Color(1f, 0.55f, 0.25f));
        }
        else
        {
            // Every crunch after the second: Whiskers is re-alerted AND
            // Edgar gets off the sofa and comes straight for Bandit.
            Debug.Log($"Chip packet noise #{stepCount} -- Whiskers re-alerted; Edgar is up and chasing!");
            if (CatNPC.Instance != null)
                CatNPC.Instance.WakeUp();
            if (HumanNPC.Instance != null)
                HumanNPC.Instance.StartPatrolling();
            OnSecondNoise?.Invoke();
        }
    }

    // Squash visuals
    [Header("Squash when stepped on")]
    [Tooltip("How flat the bag ends up after Bandit steps on it (1 = not squashed).")]
    [SerializeField] private float squashedHeight = 0.35f;
    [Tooltip("How much it spreads out sideways when flattened.")]
    [SerializeField] private float squashedSpread = 1.2f;

    private Coroutine squashRoutine;
    private Transform[] visuals;
    private Vector3[] visualRestScale, visualRestPos;
    private float restBottomY;
    private bool crushed;

    private void CacheVisuals()
    {
        if (visuals != null) return;
        System.Collections.Generic.List<Transform> list = new System.Collections.Generic.List<Transform>();
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
            if (r.transform != transform && !list.Contains(r.transform)) list.Add(r.transform);
        visuals = list.ToArray();
        visualRestScale = new Vector3[visuals.Length];
        visualRestPos = new Vector3[visuals.Length];
        restBottomY = float.MaxValue;
        for (int i = 0; i < visuals.Length; i++)
        {
            visualRestScale[i] = visuals[i].localScale;
            visualRestPos[i] = visuals[i].localPosition;
            Renderer r = visuals[i].GetComponent<Renderer>();
            if (r != null) restBottomY = Mathf.Min(restBottomY, r.bounds.min.y);
        }
    }

    // Scales only the bag's mesh (never the trigger), keeping it sitting on the floor.
    private void SetSquash(float heightK, float spreadK)
    {
        for (int i = 0; i < visuals.Length; i++)
        {
            Vector3 rs = visualRestScale[i];
            visuals[i].localScale = new Vector3(rs.x * spreadK, rs.y * heightK, rs.z * spreadK);
            visuals[i].localPosition = visualRestPos[i];
        }
        // Drop it back down so it rests on the floor instead of hovering.
        float bottom = float.MaxValue;
        foreach (Transform v in visuals)
        {
            Renderer r = v.GetComponent<Renderer>();
            if (r != null) bottom = Mathf.Min(bottom, r.bounds.min.y);
        }
        if (bottom < float.MaxValue && restBottomY < float.MaxValue)
        {
            float dy = restBottomY - bottom;
            foreach (Transform v in visuals) v.position += Vector3.up * dy;
        }
    }

    private System.Collections.IEnumerator Squash(bool firstTime)
    {
        CacheVisuals();
        if (visuals.Length == 0) yield break;

        if (firstTime) CrumbBurst();

        float from = crushed ? squashedHeight : 1f;
        float t = 0f;
        const float dur = 0.18f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            // squish past flat, then settle (a little bounce)
            float h = Mathf.Lerp(from, squashedHeight * 0.8f, k);
            float spread = Mathf.Lerp(crushed ? squashedSpread : 1f, squashedSpread * 1.05f, k);
            SetSquash(h, spread);
            yield return null;
        }
        t = 0f;
        while (t < 0.12f)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / 0.12f);
            SetSquash(Mathf.Lerp(squashedHeight * 0.8f, squashedHeight, k), Mathf.Lerp(squashedSpread * 1.05f, squashedSpread, k));
            yield return null;
        }
        SetSquash(squashedHeight, squashedSpread);
        crushed = true;
        squashRoutine = null;
    }

    // A few chip crumbs flying out the first time the bag is crushed.
    private void CrumbBurst()
    {
        Bounds b = new Bounds(transform.position, Vector3.zero);
        bool any = false;
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
        float size = Mathf.Max(0.05f, b.size.magnitude * 0.06f);
        for (int i = 0; i < 8; i++)
        {
            GameObject c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(c.GetComponent<Collider>());
            c.transform.position = b.center;
            c.transform.localScale = new Vector3(size, size * 0.3f, size);
            c.transform.rotation = UnityEngine.Random.rotation;
            c.GetComponent<Renderer>().material.color = new Color(0.95f, 0.78f, 0.3f);
            StartCoroutine(FlyCrumb(c.transform, (UnityEngine.Random.insideUnitSphere + Vector3.up * 1.2f).normalized * b.size.magnitude * 1.5f, b.min.y));
        }
    }

    private System.Collections.IEnumerator FlyCrumb(Transform c, Vector3 vel, float floorY)
    {
        float t = 0f;
        while (t < 1.5f && c != null)
        {
            t += Time.deltaTime;
            vel += Physics.gravity * Time.deltaTime;
            Vector3 p = c.position + vel * Time.deltaTime;
            if (p.y < floorY) { p.y = floorY; vel = Vector3.zero; }
            c.position = p;
            yield return null;
        }
        if (c != null) Destroy(c.gameObject);
    }
}
