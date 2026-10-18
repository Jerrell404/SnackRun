using System.Collections;
using UnityEngine;

// A climbable rope/ladder.
[RequireComponent(typeof(BoxCollider))]
public class RopeClimb : MonoBehaviour, IInteractable
{
    [Header("Anchors")]
    [Tooltip("Where Bandit stands at the bottom of the rope (also where he ends up after sliding down).")]
    [SerializeField] private Transform bottomAnchor;
    [Tooltip("Where Bandit stands at the top of the rope (also where he ends up after climbing up).")]
    [SerializeField] private Transform topAnchor;
    [Tooltip("Which way Bandit faces while climbing this rope/ladder. Defaults to this object's own forward if left empty.")]
    [SerializeField] private Transform climbFacing;
    [Tooltip("Extra yaw (degrees, around world Y) added on top of climbFacing's forward direction - use this if Bandit ends up facing backwards (away from the rope/ladder) while climbing; 180 flips him around.")]
    [SerializeField] private float climbFacingOffsetY = 0f;
    [Tooltip("Optional - if set, this completely overrides climbFacing/climbFacingOffsetY/climbYawOffset for the ENTIRE climb, both directions: Bandit's rotation.")]
    [SerializeField] private Transform fixedClimbFacing;
    [Tooltip("How close Bandit needs to be to the bottom/top anchor for E to start that action.")]
    [SerializeField] private float useDistance = 1.75f;
    [Tooltip("Turns on climbing UP (E near the bottom anchor) for THIS rope/ladder specifically. Off by default project-wide (see the class comment above)")]
    [SerializeField] private bool allowClimbUp = false;
    [Tooltip("Multiplies useDistance for the floating 'Press E to climb up/down' PROMPT only.")]
    [SerializeField] private float promptDistanceMultiplier = 1f;

    [Header("Easy use")]
    [Tooltip("Shows a floating 'Press E to climb up/down' prompt when Bandit is close enough.")]
    [SerializeField] private bool showPrompt = true;

    [Header("Timing")]
    [SerializeField] private float climbUpDuration = 5f;
    [SerializeField] private float slideDownDuration = 2f;

    [Header("After the climb")]
    [Tooltip("After climbing UP, Bandit automatically walks forward (in whatever direction he ended up facing) for this many seconds before control is handed back.")]
    [SerializeField] private float postClimbUpWalkDuration = 0.5f;
    [Tooltip("Same idea, but after climbing/sliding DOWN.")]
    [SerializeField] private float postClimbDownWalkDuration = 0.1f;
    [Tooltip("How fast Bandit walks during the above (forward after climbing up, backward after climbing down) - matches PlayerController's own default walk speed so it doesn't read as a different gait.")]
    [SerializeField] private float postClimbWalkSpeed = 3f;
    [Tooltip("Normally, after climbing UP, Bandit turns 180 degrees to face away from the ladder before stepping onto the landing.")]
    [SerializeField] private bool keepFacingAfterClimbUp = false;

    [Header("Climbing yaw (optional visual flourish)")]
    [Tooltip("Both Bandit and the camera yaw (Y-axis turn) by this many degrees while actively climbing the vertical stretch.")]
    [SerializeField] private float climbYawOffset = 45f;

    [Header("Audio")]
    [SerializeField] private AudioClip climbUpClip;
    [SerializeField] private AudioClip slideDownClip;
    [SerializeField] private AudioSource audioSource;
    private float baseAudioVolume = 1f;

    [Header("Climbing pose (tweak here if it doesn't quite line up with the rig)")]
    [Tooltip("Local rotation (degrees) applied to each arm bone so it grips in close to the rope.")]
    [SerializeField] private Vector3 armGripEuler = new Vector3(-70f, 0f, 10f);
    [Tooltip("Local rotation (degrees) applied to each leg bone so it pulls up and wraps in close to the rope.")]
    [SerializeField] private Vector3 legGripEuler = new Vector3(-50f, 0f, 6f);
    [Tooltip("How far the torso leans back away from the rope (degrees) - a real climber's body hangs slightly away from the rope, not flush against it.")]
    [SerializeField] private float torsoLeanBackDegrees = 15f;
    [Tooltip("How far arms/legs swing away from the base grip pose each hand-over-hand cycle (degrees).")]
    [SerializeField] private float limbSwingDegrees = 16f;
    [Tooltip("Hand-over-hand cycles per second while climbing up.")]
    [SerializeField] private float climbCyclesPerSecond = 1.4f;
    [Tooltip("Hand-over-hand cycles per second while sliding down (faster - it's a fast slide, not a controlled climb).")]
    [SerializeField] private float slideCyclesPerSecond = 2.5f;

    private enum State { Idle, ClimbingUp, SlidingDown }
    private State state = State.Idle;

    private GameObject player;
    private CharacterController cc;
    private PlayerController playerController;
    private Animator animator;
    private float activeYawOffset = 0f; // 0 except during the vertical climb loop itself - see climbYawOffset above

    // Every rope in the scene, so PlayerInteract can ask "is Bandit standing at a rope?" the same way it already does for the fridge/cupboard/Toby.
    private static readonly System.Collections.Generic.List<RopeClimb> all = new System.Collections.Generic.List<RopeClimb>();
    private void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    private void OnDisable() { all.Remove(this); }

    // / True while Bandit is actively climbing/sliding on ANY rope or ladder / in the scene.
    public static bool AnyClimbing
    {
        get
        {
            foreach (RopeClimb r in all)
                if (r != null && r.state != State.Idle) return true;
            return false;
        }
    }

    private TextMesh prompt;
    private Transform promptPlayer;

    // Bones under Raccoon_Root, and the pose they were in before the climb
    // started, so it can be put back exactly afterwards.
    private Transform boneLeftArm, boneRightArm, boneLeftLeg, boneRightLeg, boneTorso;
    private Quaternion origLeftArm, origRightArm, origLeftLeg, origRightLeg, origTorso;
    private bool bonesFound;

    private void Awake()
    {
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        baseAudioVolume = audioSource.volume;
    }

    private void Reset()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box != null) box.isTrigger = true;
    }

    /// Editor-time/tooling helper (see TreehouseLadderSetup.cs) so a climb
    /// point can be fully wired up from code without needing SerializedObject
    /// or reflection to reach these private [SerializeField] fields.
    public void Configure(Transform bottom, Transform top, float distance, float upDuration, float downDuration, AudioClip upClip, AudioClip downClip, bool allowClimbUp = false, Transform facing = null)
    {
        bottomAnchor = bottom;
        topAnchor = top;
        useDistance = distance;
        climbUpDuration = upDuration;
        slideDownDuration = downDuration;
        climbUpClip = upClip;
        slideDownClip = downClip;
        this.allowClimbUp = allowClimbUp;
        if (facing != null) climbFacing = facing;
    }

    private void Update()
    {
        UpdatePrompt();
        if (audioSource != null) audioSource.volume = baseAudioVolume * SoundFX.SfxVolumeScale;
    }

    private void OnDestroy()
    {
        // The prompt lives at scene root (see UpdatePrompt), so it isn't
        // auto-destroyed with this object - clean it up ourselves.
        if (prompt != null) Destroy(prompt.gameObject);
    }

    /// Called by PlayerInteract when E is pressed. If Bandit is standing near
    /// a rope's bottom or top anchor, that rope handles the press - same
    /// "no aiming required" treatment as the fridge/cupboard/food.
    public static bool TryHandleNearby(Vector3 playerPos)
    {
        // If Bandit is already mid-climb on ANY rope/ladder, don't let a press of E start a second, different one.
        foreach (RopeClimb busy in all)
        {
            if (busy != null && busy.state != State.Idle) return false;
        }

        RopeClimb best = null;
        float bestDist = float.MaxValue;
        foreach (RopeClimb r in all)
        {
            if (r == null || r.state != State.Idle) continue;
            float d = r.NearestAnchorDistance(playerPos);
            if (d <= r.useDistance && d < bestDist) { best = r; bestDist = d; }
        }
        if (best == null) return false;
        best.Interact();
        return true;
    }

    // Whichever of the top (climb down) / bottom (climb up, only if allowClimbUp) anchors Bandit is closer to.
    private float NearestAnchorDistance(Vector3 playerPos)
    {
        float top = topAnchor != null ? Vector3.Distance(playerPos, topAnchor.position) : float.MaxValue;
        if (!allowClimbUp || bottomAnchor == null) return top;
        float bottom = Vector3.Distance(playerPos, bottomAnchor.position);
        return Mathf.Min(top, bottom);
    }

    // Widened useDistance for the floating prompt only; the E-press itself still uses useDistance.
    private float PromptDistance => useDistance * promptDistanceMultiplier;

    // True if no OTHER idle rope/ladder in the scene has an eligible anchor (within ITS OWN prompt distance) strictly closer than myDist. Prompt-only.
    private static bool IsGloballyNearest(Vector3 playerPos, float myDist)
    {
        foreach (RopeClimb r in all)
        {
            if (r == null || r.state != State.Idle) continue;
            float d = r.NearestAnchorDistance(playerPos);
            if (d <= r.PromptDistance && d < myDist - 0.001f) return false;
        }
        return true;
    }

    public void Interact()
    {
        if (state != State.Idle) return; // already climbing/sliding - ignore extra presses
        if (bottomAnchor == null || topAnchor == null) return;

        if (player == null) player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) return;

        float distTop = Vector3.Distance(player.transform.position, topAnchor.position);
        if (distTop <= useDistance)
        {
            StartCoroutine(DoClimb(topAnchor, bottomAnchor, slideDownDuration, slideDownClip, false));
            return;
        }

        // Climbing UP only counts on ropes/ladders that have it turned on
        // (see the class comment) - everywhere else this is a no-op, same
        // as before.
        if (allowClimbUp)
        {
            float distBottom = Vector3.Distance(player.transform.position, bottomAnchor.position);
            if (distBottom <= useDistance)
            {
                StartCoroutine(DoClimb(bottomAnchor, topAnchor, climbUpDuration, climbUpClip, true));
            }
        }
    }

    private IEnumerator DoClimb(Transform from, Transform to, float duration, AudioClip clip, bool goingUp)
    {
        state = goingUp ? State.ClimbingUp : State.SlidingDown;

        cc = player.GetComponent<CharacterController>();
        playerController = player.GetComponent<PlayerController>();
        animator = player.GetComponent<Animator>();

        FindBones();

        // Same freeze pattern as HidingSpot's bag teleport: disable the
        // CharacterController before moving the transform directly, and
        // disable PlayerController/Animator so nothing else drives movement
        // or the walk/idle animation while the climb pose is in control.
        if (cc != null) cc.enabled = false;
        if (playerController != null) playerController.enabled = false;
        if (animator != null) animator.enabled = false;

        activeYawOffset = climbYawOffset;

        player.transform.position = from.position;
        FacePlayerAlongRope();

        PlayAudio(clip, goingUp); // loop while climbing up; play once while sliding down

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float progress = Mathf.Clamp01(t / duration);
            // from/to always share X and Z (see TreehouseLadderSetup's anchor
            // placement) - so this Lerp only ever moves Bandit in Y, exactly
            // straight up/down the ladder's own vertical line, never
            // side-to-side.
            player.transform.position = Vector3.Lerp(from.position, to.position, progress);
            ApplyClimbPose(goingUp, t);
            FacePlayerAlongRope(); // held constantly, not just applied once at the start
            yield return null;
        }

        player.transform.position = to.position;
        RestorePose();
        StopAudio();

        // Back to no yaw offset for the turn/walk-off below and for
        // handing control back - the offset above is only for the vertical
        // climb itself.
        activeYawOffset = 0f;

        // Bandit climbed the whole way facing INTO the ladder.
        if (goingUp && !keepFacingAfterClimbUp) player.transform.rotation = Quaternion.LookRotation(-player.transform.forward, Vector3.up);

        float walkDuration = goingUp ? postClimbUpWalkDuration : postClimbDownWalkDuration;
        if (walkDuration > 0f)
        {
            Vector3 moveDir = goingUp ? player.transform.forward : -player.transform.forward;
            float wt = 0f;
            while (wt < walkDuration)
            {
                wt += Time.deltaTime;
                player.transform.position += moveDir * postClimbWalkSpeed * Time.deltaTime;
                yield return null;
            }
        }

        if (animator != null) animator.enabled = true;
        if (playerController != null) playerController.enabled = true;
        if (cc != null) cc.enabled = true;

        state = State.Idle;
    }

    private void FacePlayerAlongRope()
    {
        if (fixedClimbFacing != null)
        {
            // One fixed facing for the whole climb, both directions - see
            // fixedClimbFacing's tooltip. Deliberately bypasses
            // climbFacing/climbFacingOffsetY/climbYawOffset entirely so the
            // configured rotation is the whole story, nothing stacked on
            // top of it.
            player.transform.rotation = fixedClimbFacing.rotation;
            return;
        }

        Transform facing = climbFacing != null ? climbFacing : transform;
        Vector3 flatForward = new Vector3(facing.forward.x, 0f, facing.forward.z);
        if (flatForward.sqrMagnitude < 0.0001f) return;
        Quaternion rot = Quaternion.LookRotation(flatForward.normalized, Vector3.up);
        if (climbFacingOffsetY != 0f) rot *= Quaternion.Euler(0f, climbFacingOffsetY, 0f);
        if (activeYawOffset != 0f) rot *= Quaternion.Euler(0f, activeYawOffset, 0f);
        player.transform.rotation = rot;
    }

    private void FindBones()
    {
        if (bonesFound) return;

        Transform root = null;
        foreach (Transform tr in player.GetComponentsInChildren<Transform>(true))
        {
            if (tr.name == "Raccoon_Root") { root = tr; break; }
        }
        if (root == null) return;

        boneLeftArm = FindChild(root, "Left_Arm");
        boneRightArm = FindChild(root, "Right_Arm");
        boneLeftLeg = FindChild(root, "Left_Leg");
        boneRightLeg = FindChild(root, "Right_Leg");
        boneTorso = FindChild(root, "Torso");

        if (boneLeftArm != null) origLeftArm = boneLeftArm.localRotation;
        if (boneRightArm != null) origRightArm = boneRightArm.localRotation;
        if (boneLeftLeg != null) origLeftLeg = boneLeftLeg.localRotation;
        if (boneRightLeg != null) origRightLeg = boneRightLeg.localRotation;
        if (boneTorso != null) origTorso = boneTorso.localRotation;

        bonesFound = true;
    }

    private static Transform FindChild(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name) return t;
        }
        return null;
    }

    // Pulls arms and legs in tight to the rope with a slight hand-over-hand
    // swing, and leans the torso back a little - "a person in real life
    // climbing a rope", not a T-pose glued to it.
    private void ApplyClimbPose(bool goingUp, float elapsed)
    {
        if (!bonesFound) return;

        float cyclesPerSecond = goingUp ? climbCyclesPerSecond : slideCyclesPerSecond;
        float phase = elapsed * cyclesPerSecond * Mathf.PI * 2f;
        float swing = Mathf.Sin(phase) * limbSwingDegrees;

        if (boneLeftArm != null)
            boneLeftArm.localRotation = origLeftArm * Quaternion.Euler(armGripEuler + new Vector3(swing, 0f, 0f));
        if (boneRightArm != null)
            boneRightArm.localRotation = origRightArm * Quaternion.Euler(armGripEuler + new Vector3(-swing, 0f, 0f));

        // Legs swing opposite the arms, like real hand-over-hand/foot-over-foot climbing.
        if (boneLeftLeg != null)
            boneLeftLeg.localRotation = origLeftLeg * Quaternion.Euler(legGripEuler + new Vector3(-swing, 0f, 0f));
        if (boneRightLeg != null)
            boneRightLeg.localRotation = origRightLeg * Quaternion.Euler(legGripEuler + new Vector3(swing, 0f, 0f));

        if (boneTorso != null)
            boneTorso.localRotation = origTorso * Quaternion.Euler(-torsoLeanBackDegrees, 0f, 0f);
    }

    private void RestorePose()
    {
        if (!bonesFound) return;
        if (boneLeftArm != null) boneLeftArm.localRotation = origLeftArm;
        if (boneRightArm != null) boneRightArm.localRotation = origRightArm;
        if (boneLeftLeg != null) boneLeftLeg.localRotation = origLeftLeg;
        if (boneRightLeg != null) boneRightLeg.localRotation = origRightLeg;
        if (boneTorso != null) boneTorso.localRotation = origTorso;
    }

    private void PlayAudio(AudioClip clip, bool loop)
    {
        if (audioSource == null || clip == null) return;
        audioSource.clip = clip;
        audioSource.loop = loop;
        audioSource.Play();
    }

    private void StopAudio()
    {
        if (audioSource != null) audioSource.Stop();
    }

    // "Press E to climb up/down" prompt
    private void UpdatePrompt()
    {
        if (!showPrompt)
        {
            if (prompt != null) prompt.gameObject.SetActive(false);
            return;
        }
        if (promptPlayer == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p == null) return;
            promptPlayer = p.transform;
        }
        if (prompt == null)
        {
            // Lives at SCENE ROOT (no SetParent) - same reasoning as
            // FoodItem's label: it must never inherit this object's scale,
            // and it's repositioned manually every frame instead.
            GameObject g = new GameObject("RopeClimbPrompt_" + gameObject.name);
            prompt = g.AddComponent<TextMesh>();
            prompt.fontSize = 60;
            prompt.characterSize = 0.05f;
            prompt.anchor = TextAnchor.LowerCenter;
            prompt.alignment = TextAlignment.Center;
            prompt.fontStyle = FontStyle.Bold;
            prompt.color = new Color(0.55f, 0.85f, 1f);
            prompt.gameObject.SetActive(false);
        }

        // Hidden entirely while actually climbing/sliding, or if not fully wired.
        if (state != State.Idle || bottomAnchor == null || topAnchor == null)
        {
            prompt.gameObject.SetActive(false);
            return;
        }

        // Top anchor (climb down) always counts; bottom anchor (climb up)
        // only counts on ropes/ladders with allowClimbUp on (see the class
        // comment) - everywhere else this matches the old top-only prompt.
        float distTop = Vector3.Distance(promptPlayer.position, topAnchor.position);
        float distBottom = allowClimbUp ? Vector3.Distance(promptPlayer.position, bottomAnchor.position) : float.MaxValue;

        bool nearTop = distTop <= PromptDistance;
        bool nearBottom = distBottom <= PromptDistance;
        if (!nearTop && !nearBottom)
        {
            prompt.gameObject.SetActive(false);
            return;
        }

        // Whichever end Bandit is actually near - on a very short ladder
        // where both ends could be in range at once, the closer one wins.
        bool useBottom = nearBottom && distBottom < distTop;
        float myDist = useBottom ? distBottom : distTop;
        Transform myAnchor = useBottom ? bottomAnchor : topAnchor;

        // Only the single globally-nearest eligible rope/ladder ever shows a prompt.
        if (!IsGloballyNearest(promptPlayer.position, myDist))
        {
            prompt.gameObject.SetActive(false);
            return;
        }

        prompt.text = useBottom ? "Press E to climb up" : "Press E to climb down";

        prompt.transform.position = myAnchor.position + Vector3.up * 1.4f;
        if (Camera.main != null) prompt.transform.forward = Camera.main.transform.forward;
        prompt.gameObject.SetActive(true);
    }
}
