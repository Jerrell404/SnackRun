using UnityEngine;

// Whiskers the cat: sleeps in her bed until the fridge (or the chip packet) alerts her, then slowly chases Bandit for a while before getting tired and going back to bed.
public class CatNPC : NPCPatrol
{
    public static CatNPC Instance;

    private enum State { Asleep, Chasing, Returning, Guarding }

    [Header("Chase")]
    [Tooltip("How fast Whiskers moves while chasing - kept slow on purpose.")]
    [SerializeField] private float chaseSpeed = 2.2f;
    [Tooltip("Base seconds spent chasing before Whiskers gets tired (8-10s).")]
    [SerializeField] private float chaseDuration = 9f;
    [Tooltip("If Whiskers can still see Bandit when the base timer runs out, she gets this many extra seconds before giving up.")]
    [SerializeField] private float sightExtension = 5f;
    [SerializeField] private float returnSpeed = 2f;

    [Header("Losing sight (e.g. Bandit hides)")]
    [Tooltip("How long Whiskers waits at Bandit's last known position, still awake and alert, before giving up and heading back to bed.")]
    [SerializeField] private float loseInterestDelay = 5f;

    [Header("Attack")]
    [Tooltip("How close Whiskers needs to be to land a scratch - also doubles as her stopping distance, so she never walks all the way on top of Bandit.")]
    [SerializeField] private float scratchRange = 1.2f;
    [Tooltip("Seconds Whiskers must wait between hits on the player (3-5s).")]
    [SerializeField] private float attackCooldown = 4f;
    [Tooltip("Seconds Whiskers stands still (stops chasing) right after landing a hit.")]
    [SerializeField] private float postHitPause = 3f;
    [Tooltip("How hard Bandit gets shoved away from Whiskers the instant she lands a scratch.")]
    [SerializeField] private float knockbackForce = 5f;

    [Header("Proximity Wake")]
    [Tooltip("While Whiskers is asleep, Bandit getting within this distance of her wakes her up and she chases him - same as the fridge or chip packet.")]
    [SerializeField] private float wakeRadius = 1.6f;
    [Tooltip("Multiplier applied to wakeRadius while Bandit is crouching, so sneaking past is safer than walking/running past.")]
    [SerializeField, Range(0f, 1f)] private float crouchWakeRadiusMultiplier = 0.5f;

    private PlayerController playerController;

    [Header("Spotting Bandit while walking back to bed")]
    [Tooltip("While Whiskers is tired and heading back to bed, she'll spot Bandit and start chasing again if he's within this distance AND she can see him.")]
    [SerializeField] private float returnSightRange = 5f;
    [Tooltip("Multiplier applied to returnSightRange while Bandit is crouching.")]
    [SerializeField, Range(0f, 1f)] private float returnCrouchMultiplier = 0.5f;
    [Tooltip("Seconds after she gives up before she can spot Bandit again, so she doesn't instantly re-chase him in the same spot.")]
    [SerializeField] private float returnGraceTime = 2f;
    private float returnTimer;

    [Header("Line of sight")]
    [Tooltip("How high above the floor Bandit counts as 'out of reach' (on a stool/table) - Whiskers can't see or catch him up there.")]
    [SerializeField] private float elevationThreshold = 0.5f;
    [Tooltip("Height above the ground to cast the sight line from/to, roughly eye level.")]
    [SerializeField] private float eyeHeight = 0.3f;

    [Header("Smooth movement")]
    [Tooltip("How quickly Whiskers speeds up / slows down (units per second, per second).")]
    [SerializeField] private float acceleration = 7f;
    [Tooltip("How fast Whiskers can turn (degrees per second). She curves round instead of snapping to face Bandit.")]
    [SerializeField] private float turnSpeed = 300f;

    [Header("Obstacles")]
    [Tooltip("Whiskers steers round furniture (fridge door, counters...) instead of walking through it.")]
    [SerializeField] private bool avoidObstacles = true;
    [Tooltip("Roughly half Whiskers' body width, for bumping into things.")]
    [SerializeField] private float bodyRadius = 0.3f;
    [Tooltip("Height above her feet to check for obstacles (keeps the floor out of it).")]
    [SerializeField] private float obstacleCheckHeight = 0.35f;
    [Tooltip("How far ahead she looks for something in the way.")]
    [SerializeField] private float lookAhead = 1.2f;

    [Header("Intercept")]
    [Tooltip("Whiskers aims ahead of Bandit, at where he'll be, instead of at where he is now.")]
    [SerializeField] private bool interceptPlayer = true;
    [Tooltip("Longest she'll predict ahead (seconds). Short on purpose so she doesn't overshoot.")]
    [SerializeField] private float maxLeadTime = 0.6f;
    [Tooltip("Furthest ahead of Bandit she'll aim (world units).")]
    [SerializeField] private float maxLeadDistance = 2.5f;
    [Tooltip("Closer than this she just goes straight for him.")]
    [SerializeField] private float interceptMinDistance = 2.5f;
    [Tooltip("Draw her aim point in the Scene view (yellow line) while testing.")]
    [SerializeField] private bool showInterceptDebug = true;

    private Vector3 lastPlayerPos;
    private Vector3 playerVelocity;
    private bool hasPlayerSample;

    [Header("Animation")]
    [Tooltip("Left empty, this is found automatically on this object.")]
    [SerializeField] private Animator animator;
    [Tooltip("Assigned automatically if left empty and the Animator has no controller yet.")]
    [SerializeField] private RuntimeAnimatorController catController;

    private State state = State.Asleep;
    private float asleepTimer = 0f;
    private Transform player;
    private Vector3 homePosition;
    private Quaternion homeRotation;
    private float chaseTimer = 0f;
    private float currentChaseDuration;
    private bool sightExtensionUsed = false;
    private float attackCooldownTimer = 0f;
    private float postHitPauseTimer = 0f;
    private float currentSpeed = 0f;
    private bool hasSpeedParam, hasScratchParam, hasChasingParam;

    // Where Whiskers last actually saw Bandit during the current chase, and
    // how long it's been since she's seen him at all.
    private Vector3 lastKnownPlayerPos;
    private bool hasLastKnownPos = false;
    private float lostSightTimer = 0f;

    void Start()
    {
        Instance = this;

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) { player = playerObj.transform; playerController = playerObj.GetComponent<PlayerController>(); }

        // Whiskers only ever stands on the floor she started on - this is
        // also used as the "ground level" reference for the elevation check.
        homePosition = transform.position;
        homeRotation = transform.rotation;
        bedPosition = transform.position;
        FitBodyCollider();
        MeasureFeet();
        BuildCatNavigation();

        // The cat doesn't use waypoint patrolling like the human - she sleeps
        // in bed until something wakes her up.
        isPatrolling = false;

        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator != null && animator.runtimeAnimatorController == null && catController != null)
        {
            animator.runtimeAnimatorController = catController;
        }
        if (animator != null)
        {
            animator.applyRootMotion = false; // the script moves her, not the clips
            foreach (AnimatorControllerParameter p in animator.parameters)
            {
                if (p.name == "Speed") hasSpeedParam = true;
                if (p.name == "Scratch") hasScratchParam = true;
                if (p.name == "IsChasing") hasChasingParam = true;
            }
        }

        SetChasingAnim(false);
    }

    // Hooked up to Fridge.onFridgeOpened and to ChipPacket's first trigger.
    /// True while Whiskers is still asleep in her bed (never woken this attempt).
    public bool IsAsleep => state == State.Asleep;
    /// Chasing or searching for Bandit right now.
    public bool IsHunting => state == State.Chasing;

    // Her body half-length (from the model), so she scratches from her own
    // reach and never walks into Bandit.
    private float catHalfLength = -1f;
    private float CatHalfLength
    {
        get
        {
            if (catHalfLength < 0f)
            {
                catHalfLength = 0.4f;
                Bounds b; bool any = false;
                b = new Bounds(transform.position, Vector3.zero);
                foreach (Renderer r in GetComponentsInChildren<Renderer>())
                {
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                if (any) catHalfLength = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z), 0.2f, 2f);
            }
            return catHalfLength;
        }
    }
    private float ScratchReach => Mathf.Max(scratchRange, CatHalfLength + 0.3f + 0.35f);

    private void KeepOutOfBandit()
    {
        if (player == null) return;
        Vector3 d = transform.position - player.position; d.y = 0f;
        float min = CatHalfLength + 0.3f;
        if (d.magnitude < min)
        {
            if (d.sqrMagnitude < 0.0001f) d = -transform.forward;
            Vector3 p2 = player.position + d.normalized * min;
            transform.position = new Vector3(p2.x, transform.position.y, p2.z);
        }
    }

    // Makes her collider match her body, so Bandit can't run through her.
    private void FitBodyCollider()
    {
        CapsuleCollider cap = GetComponent<CapsuleCollider>();
        if (cap == null) return;
        Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
        bool any = false;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            Bounds w = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = new Vector3((i & 1) == 0 ? w.min.x : w.max.x, (i & 2) == 0 ? w.min.y : w.max.y, (i & 4) == 0 ? w.min.z : w.max.z);
                Vector3 l = transform.InverseTransformPoint(c);
                min = Vector3.Min(min, l); max = Vector3.Max(max, l); any = true;
            }
        }
        if (!any) return;
        Vector3 size = max - min;
        cap.direction = 2;                                   // along her body (front-to-back)
        cap.radius = Mathf.Min(size.x, size.y) * 0.45f;
        cap.height = Mathf.Max(size.z * 0.9f, cap.radius * 2f + 0.01f);
        cap.center = (min + max) * 0.5f;
        cap.isTrigger = false;
    }

    public void WakeUp()
    {
        if (state == State.Chasing) return; // already awake and on the hunt

        // Woken from sleep: a grumpy meow. Spotted Bandit while awake: a yowl.
        NpcVoice v = NpcVoice.For(this);
        v.bubbleSize = 0.025f; v.markSize = 0.05f;
        v.ShowMark("!", new Color(1f, 0.2f, 0.15f), 1.5f);
        if (state == State.Asleep) v.Say("whiskers_meow", "Mrrow?!", 1.5f, true);
        else v.Say("whiskers_yowl", "MRROWWW!", 1.5f, true);

        asleepTimer = 0f;
        if (animator != null) animator.speed = 1f;   // un-freeze
        state = State.Chasing;
        spotSearchState = 0;
        chaseTimer = 0f;
        currentChaseDuration = chaseDuration;
        sightExtensionUsed = false;
        hasLastKnownPos = false;
        lostSightTimer = 0f;
        SetChasingAnim(true);
        Debug.Log("Whiskers woke up and is chasing Bandit!");
    }

    protected override void Update()
    {
        if (attackCooldownTimer > 0f)
        {
            attackCooldownTimer -= Time.deltaTime;
        }

        if (postHitPauseTimer > 0f)
        {
            postHitPauseTimer -= Time.deltaTime;
        }

        switch (state)
        {
            case State.Asleep:
                currentSpeed = 0f;
                SetSpeedAnim(0f);
                // Fast asleep = completely still: after a moment of settling into her idle pose, freeze the
                // animation so her head and body don't move at all.
                asleepTimer += Time.deltaTime;
                if (animator != null && asleepTimer > 0.4f && animator.speed != 0f) animator.speed = 0f;
                NpcVoice.For(this).ShowMark("z z", new Color(0.75f, 0.85f, 1f), 0.25f);   // clearly asleep
                CheckProximityWake();
                return;
            case State.Chasing:
                UpdateChasing();
                break;
            case State.Returning:
                UpdateReturning();
                break;
            case State.Guarding:
                UpdateGuarding();
                break;
        }
    }

    // Asleep: Bandit walking right up to her wakes her, no fridge/chips needed.
    // Crouching shrinks the radius; being hidden or up on furniture doesn't count.
    private void CheckProximityWake()
    {
        if (player == null) return;
        if (IsPlayerElevated()) return;
        if (HidingSpot.IsHiddenFrom(transform.position, "Whiskers")) return;

        float effectiveRadius = wakeRadius;
        if (playerController != null && playerController.IsCrouching())
            effectiveRadius *= crouchWakeRadiusMultiplier;

        float flatDist = new Vector2(
            transform.position.x - player.position.x,
            transform.position.z - player.position.z).magnitude;

        if (flatDist <= effectiveRadius)
        {
            Debug.Log("Bandit got too close to sleeping Whiskers - she woke up!");
            WakeUp();
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, wakeRadius);
    }

    [Header("Waiting by the fridge (after she tires out)")]
    [Tooltip("How far out from the fridge door she sits.")]
    [SerializeField] private float guardDistanceOut = 2.2f;
    [Tooltip("How far to the side of the door she sits (so she's not in the door's way).")]
    [SerializeField] private float guardDistanceSide = 1.6f;
    [Tooltip("How far she can spot Bandit from her guard spot.")]
    [SerializeField] private float guardSightRange = 7f;
    [Tooltip("While waiting by the fridge she also senses Bandit with her whiskers: closer than this, she goes after him even without seeing him.")]
    [SerializeField] private float guardSenseRadius = 2.5f;
    [Tooltip("Multiplier applied to the sense radius while Bandit is crouching.")]
    [SerializeField, Range(0f, 1f)] private float guardSenseCrouchMultiplier = 0.5f;

    private Vector3 returnTarget;
    private Quaternion returnTargetRot;
    private bool returningToFridge;

    private bool TryFindFridgeSpot(out Vector3 spotOut, out Quaternion rotOut)
    {
        spotOut = homePosition;
        rotOut = homeRotation;
        Fridge fridge = FindFirstObjectByType<Fridge>();
        if (fridge == null) return false;

        // Try the handle side first, then the other side, then straight out -
        // whichever isn't inside furniture.
        float[] sides = { guardDistanceSide, -guardDistanceSide, 0f };
        foreach (float side in sides)
        {
            Vector3 facing;
            Vector3 spot = fridge.GetGuardSpot(guardDistanceOut, side, out facing);
            spot.y = GroundHeight(new Vector3(spot.x, transform.position.y, spot.z));
            Vector3 probe = spot + Vector3.up * (bodyRadius + 0.15f);
            bool blocked = false;
            foreach (Collider c in Physics.OverlapSphere(probe, bodyRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c.transform.IsChildOf(transform)) continue;
                if (player != null && c.transform.IsChildOf(player)) continue;
                blocked = true;
                break;
            }
            if (blocked) continue;

            spotOut = spot;
            rotOut = Quaternion.LookRotation(new Vector3(facing.x, 0f, facing.z));
            return true;
        }
        Debug.Log("Whiskers couldn't find a clear spot by the fridge - she'll go back to her bed instead.");
        return false;
    }

    // Awake and standing by the fridge: if Bandit comes into view, she's off again.
    [Tooltip("Seconds Whiskers stays awake by the fridge before she walks back to her bed to sleep.")]
    [SerializeField] private float dozeOffTime = 20f;
    private float guardTimer;

    private void UpdateGuarding()
    {
        currentSpeed = 0f;
        SetSpeedAnim(0f);
        if (player == null) return;
        guardTimer += Time.deltaTime;
        if (guardTimer >= returnGraceTime && !IsPlayerElevated() && !HidingSpot.IsHiddenFrom(transform.position, "Whiskers"))
        {
            float sense = guardSenseRadius;
            if (playerController != null && playerController.IsCrouching()) sense *= guardSenseCrouchMultiplier;
            Vector3 toBandit = player.position - transform.position; toBandit.y = 0f;
            if (toBandit.magnitude <= sense)
            {
                Debug.Log("Whiskers sensed Bandit with her whiskers by the fridge!");
                guardTimer = 0f;
                WakeUp();
                lastKnownPlayerPos = new Vector3(player.position.x, homePosition.y, player.position.z);
                hasLastKnownPos = true;
                return;
            }
        }
        if (guardTimer >= returnGraceTime && Vector3.Distance(transform.position, player.position) <= guardSightRange && CanSeePlayer())
        {
            Debug.Log("Whiskers spotted Bandit from her guard post by the fridge!");
            guardTimer = 0f;
            WakeUp();
            return;
        }

        // Waited long enough by the fridge: now she heads to her bed to sleep.
        if (guardTimer >= dozeOffTime)
        {
            guardTimer = 0f;
            state = State.Returning;
            returningToFridge = false;
            returnTarget = homePosition;
            returnTargetRot = homeRotation;
            returnTimer = returnGraceTime;
            Debug.Log("Whiskers is done waiting by the fridge and is heading to bed.");
        }
    }

    private void UpdateChasing()
    {
        // The timer runs no matter what - getting hit, hiding, or Bandit
        // climbing out of reach doesn't pause it or reset the clock.
        chaseTimer += Time.deltaTime;

        bool canSee = CanSeePlayer();

        if (canSee)
        {
            lastKnownPlayerPos = new Vector3(player.position.x, homePosition.y, player.position.z);
            hasLastKnownPos = true;
            lostSightTimer = 0f;
            spotSearchState = 0;   // saw him again - any hiding-spot check starts over
        }

        if (chaseTimer >= currentChaseDuration && spotSearchState != 1 && spotSearchState != 2)
        {
            if (!sightExtensionUsed && canSee)
            {
                // Still in view right as she was about to give up - one
                // last burst of energy before she actually tires out.
                currentChaseDuration += sightExtension;
                sightExtensionUsed = true;
            }
            else
            {
                BeginReturning();
                return;
            }
        }

        if (!canSee)
        {
            // Hidden, elevated, or blocked - head to wherever he was last
            // seen and wait there rather than instantly giving up or
            // freezing in place mid-stride.
            SearchLastKnownPosition();
            return;
        }

        Vector3 targetPos = lastKnownPlayerPos;
        float dist = Vector3.Distance(transform.position, targetPos);

        // Right after landing a hit, Whiskers just stands there for a beat
        // instead of immediately closing back in - frozen in place AND out
        // of her running animation, so she visibly stops rather than just
        // continuing to run in place.
        bool pausedFromHit = postHitPauseTimer > 0f;
        SetChasingAnim(!pausedFromHit);

        // Whiskers only ever walks the floor.
        Vector3 moveTarget = GetInterceptPoint(targetPos, dist);
        MoveSmoothly(moveTarget, pausedFromHit ? 0f : chaseSpeed,
                     moveTarget == targetPos ? ScratchReach * 0.9f : 0.2f);
        KeepOutOfBandit();

        if (!pausedFromHit && dist <= ScratchReach && attackCooldownTimer <= 0f)
        {
            PlayerHealth health = player.GetComponent<PlayerHealth>();
            if (health != null)
            {
                health.TakeHit();
            }

            PlayerController playerController = player.GetComponent<PlayerController>();
            if (playerController != null)
            {
                playerController.ApplyKnockback(transform.position, knockbackForce);
            }

            attackCooldownTimer = attackCooldown;
            postHitPauseTimer = postHitPause;
            SetChasingAnim(false); // freeze the running animation the instant she lands the hit

            // Swipe animation + visible scratch marks on Bandit.
            if (animator != null && hasScratchParam) animator.SetTrigger("Scratch");
            ScratchEffect.Play(player, transform.position);
            NpcVoice.For(this).Say("whiskers_hiss", "HSSS!", 0.5f, true);
            StartCoroutine(Lunge());
        }
    }

    // Can't see Bandit right now - walk to wherever he was last seen and
    // wait there a few seconds (still awake and alert) before giving up.
    private void SearchLastKnownPosition()
    {
        if (!hasLastKnownPos)
        {
            SetChasingAnim(false);
            return; // never saw him at all yet this chase - nothing to path to
        }

        // Walk to near where she last saw him (not onto the spot itself -
        // that's usually inside the furniture he's hiding behind), then wait.
        // Busy checking a hiding spot - finish that first.
        if (spotSearchState == 1 || spotSearchState == 2) { UpdateSpotCheck(); return; }

        float dist = Vector3.Distance(transform.position, lastKnownPlayerPos);
        const float searchStop = 1.3f;
        if (spotSearchState != 3 && dist > searchStop + 0.1f && !(navAtEnd && navEndOffMesh))
        {
            SetChasingAnim(true);
            MoveSmoothly(lastKnownPlayerPos, chaseSpeed, searchStop);
        }
        else if (spotSearchState == 0)
        {
            // Not in the open where he was - sniff out the nearest hiding place.
            spotSearchState = 3;
            var spots = HidingSpot.NearestTo(lastKnownPlayerPos, spotSearchRadius, 1);
            if (spots.Count > 0)
            {
                spotStand = spots[0].SearchPointFrom(transform.position);
                spotStand.y = transform.position.y;
                spotLook = spots[0].Centre;
                spotSearchState = 1;
                spotTimer = 0f;
                Debug.Log("Whiskers is sniffing around the hiding spot '" + spots[0].name + "'.");
            }
        }
        else if (spotSearchState == 1 || spotSearchState == 2)
        {
            UpdateSpotCheck();
        }
        else
        {
            SetChasingAnim(false);
            MoveSmoothly(lastKnownPlayerPos, 0f, searchStop); // eases to a stop instead of halting dead
            lostSightTimer += Time.deltaTime;
            if (lostSightTimer >= loseInterestDelay)
            {
                BeginReturning();
            }
        }
    }

    // Scratch lunge
    // She rears up and pounces forward at Bandit, swipes, and drops back.
    // Only her model moves (not her position), so it never pushes into walls.
    private Transform lungeModel;
    private Vector3 lungeRestPos;
    private Quaternion lungeRestRel;

    private System.Collections.IEnumerator Lunge()
    {
        if (lungeModel == null)
        {
            Transform m = animator != null && animator.transform != transform ? animator.transform
                        : (transform.childCount > 0 ? transform.GetChild(0) : null);
            if (m == null) yield break;
            lungeModel = m;
            lungeRestPos = m.localPosition;
            lungeRestRel = Quaternion.Inverse(transform.rotation) * m.rotation;
        }
        float t = 0f;
        const float up = 0.1f, back = 0.25f;
        while (t < up + back)
        {
            t += Time.deltaTime;
            float k = t < up ? Mathf.SmoothStep(0f, 1f, t / up) : 1f - Mathf.SmoothStep(0f, 1f, (t - up) / back);
            lungeModel.localPosition = lungeRestPos;
            lungeModel.position += transform.forward * 0.35f * k + Vector3.up * 0.06f * k;
            lungeModel.rotation = Quaternion.AngleAxis(-25f * k, transform.right) * transform.rotation * lungeRestRel;
            yield return null;
        }
        lungeModel.localPosition = lungeRestPos;
        lungeModel.rotation = transform.rotation * lungeRestRel;
    }

    // Checking the nearest hiding spot
    [Tooltip("When she loses Bandit, she sniffs out the nearest hiding spot within this distance of where she last saw him.")]
    [SerializeField] private float spotSearchRadius = 6f;
    private int spotSearchState;      // 0 not yet, 1 walking there, 2 sniffing, 3 done
    private Vector3 spotStand, spotLook;
    private float spotTimer;

    private void UpdateSpotCheck()
    {
        spotTimer += Time.deltaTime;
        NpcVoice.For(this).ShowMark("?", new Color(1f, 0.85f, 0.2f), 0.25f);   // sniffing around
        if (spotSearchState == 1)
        {
            Vector3 to = spotStand - transform.position; to.y = 0f;
            if (to.magnitude <= 0.7f || spotTimer > 5f || (navAtEnd && spotTimer > 0.3f)) { spotSearchState = 2; spotTimer = 0f; }
            else { SetChasingAnim(true); MoveSmoothly(spotStand, chaseSpeed, 0.5f); }
        }
        else
        {
            SetChasingAnim(false);
            SetSpeedAnim(0f);
            Vector3 look = spotLook - transform.position; look.y = 0f;
            if (look.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(look), turnSpeed * Time.deltaTime);
            if (spotTimer >= 1.2f) { spotSearchState = 3; lostSightTimer = 0f; }
        }
    }

    // Whiskers "sees" Bandit only if he's not hiding, not up on furniture out
    // of her reach, and nothing solid is blocking the line between them.
    private bool CanSeePlayer()
    {
        if (player == null) return false;
        if (HidingSpot.IsHiddenFrom(transform.position, "Whiskers")) return false;
        if (IsPlayerElevated()) return false;

        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        Vector3 target = player.position + Vector3.up * eyeHeight;

        if (Physics.Linecast(eye, target, out RaycastHit hit))
        {
            if (hit.transform != player && !hit.transform.IsChildOf(player))
            {
                return false; // something solid is in the way
            }
        }

        return true;
    }

    // Her own navigation mesh
    // Built when the scene starts from the room's colliders, sized for a cat
    // that doesn't fit under tables, so she paths round furniture instead of
    // wedging herself under it.
    private bool navReady;
    private UnityEngine.AI.NavMeshDataInstance navInstance;
    private UnityEngine.AI.NavMeshQueryFilter navFilter;
    private UnityEngine.AI.NavMeshPath navPath;
    private Vector3[] navCorners = new Vector3[0];
    private int navCornerIndex;
    private float navRepathTime;
    private Vector3 navTarget = new Vector3(float.NaN, 0f, 0f);
    private float blockedTimer;
    private float pathBlockedCooldown;   // >0: steer round an obstacle instead of following the path
    private bool navAtEnd;        // at the last walkable point of her path
    private bool navEndOffMesh;   // her target is inside furniture

    private void BuildCatNavigation()
    {
        var settings = UnityEngine.AI.NavMesh.CreateSettings();   // her own agent type (Edgar has another)
        settings.agentRadius = Mathf.Clamp(bodyRadius, 0.2f, 0.35f);
        settings.agentHeight = 0.9f;   // taller than the gap under a table
        settings.agentClimb = 0.45f;   // can hop into her bed
        settings.agentSlope = 40f;

        Bounds area = new Bounds(transform.position, new Vector3(160f, 30f, 160f));
        var sources = new System.Collections.Generic.List<UnityEngine.AI.NavMeshBuildSource>();
        UnityEngine.AI.NavMeshBuilder.CollectSources(area, ~0, UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders, 0,
            new System.Collections.Generic.List<UnityEngine.AI.NavMeshBuildMarkup>(), sources);
        HumanNPC edgar = HumanNPC.Instance != null ? HumanNPC.Instance : FindFirstObjectByType<HumanNPC>();
        sources.RemoveAll(src =>
        {
            Component c = src.component;
            if (c == null) return false;
            Collider col = c as Collider;
            if (col != null && (col.isTrigger || !col.enabled)) return true;
            Transform t = c.transform;
            if (t.IsChildOf(transform)) return true;
            if (player != null && t.IsChildOf(player)) return true;
            if (edgar != null && t.IsChildOf(edgar.transform)) return true;
            if (t.GetComponentInParent<FoodItem>() != null) return true;
            if (t.GetComponentInParent<Rigidbody>() != null) return true; // doors/props that move
            return false;
        });

        var data = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(settings, sources, area, Vector3.zero, Quaternion.identity);
        if (data == null) { Debug.LogWarning("Whiskers: couldn't build her navigation mesh - she'll steer by feel."); return; }
        navInstance = UnityEngine.AI.NavMesh.AddNavMeshData(data);
        navFilter = new UnityEngine.AI.NavMeshQueryFilter { agentTypeID = settings.agentTypeID, areaMask = UnityEngine.AI.NavMesh.AllAreas };
        navPath = new UnityEngine.AI.NavMeshPath();
        navReady = true;
        Debug.Log("Whiskers: navigation mesh built from " + sources.Count + " colliders - she now paths round furniture.");
    }

    // Next point to head for on the way to target (false = no path, steer normally).
    private bool NavNext(Vector3 target, out Vector3 next)
    {
        next = target;
        if (!navReady) return false;
        bool moved = float.IsNaN(navTarget.x) || new Vector2(target.x - navTarget.x, target.z - navTarget.z).sqrMagnitude > 0.16f;
        if (moved || Time.time >= navRepathTime)
        {
            navTarget = target;
            navRepathTime = Time.time + 0.3f;
            UnityEngine.AI.NavMeshHit a, b;
            if (UnityEngine.AI.NavMesh.SamplePosition(transform.position, out a, 1.5f, navFilter)
                && UnityEngine.AI.NavMesh.SamplePosition(target, out b, 2f, navFilter)
                && UnityEngine.AI.NavMesh.CalculatePath(a.position, b.position, navFilter, navPath)
                && navPath.status != UnityEngine.AI.NavMeshPathStatus.PathInvalid && navPath.corners.Length > 1)
            {
                navCorners = navPath.corners;
                navCornerIndex = 1;
                // Is the target somewhere she can stand, or inside furniture?
                navEndOffMesh = new Vector2(b.position.x - target.x, b.position.z - target.z).magnitude > 0.3f;
            }
            else navCorners = new Vector3[0];
        }
        navAtEnd = false;
        if (navCorners.Length == 0) return false;
        while (navCornerIndex < navCorners.Length - 1 &&
               new Vector2(navCorners[navCornerIndex].x - transform.position.x, navCorners[navCornerIndex].z - transform.position.z).magnitude < 0.25f)
            navCornerIndex++;
        next = navCorners[navCornerIndex];
        // Last corner reached: go the last bit if target is in the open, else stop here.
        navAtEnd = navCornerIndex == navCorners.Length - 1 &&
            new Vector2(next.x - transform.position.x, next.z - transform.position.z).magnitude < 0.35f;
        if (navAtEnd && !navEndOffMesh) next = target;
        return true;
    }

    private void OnDestroy()
    {
        if (navReady) UnityEngine.AI.NavMesh.RemoveNavMeshData(navInstance);
    }

    // Keeping her feet on the floor
    private Vector3 bedPosition;
    private float feetOffset;   // pivot height above the bottom of her feet

    private void MeasureFeet()
    {
        Bounds b = new Bounds(); bool any = false;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        feetOffset = any ? Mathf.Clamp(transform.position.y - b.min.y, -0.2f, 0.2f) : 0f;
    }

    // Height to put her pivot at for this spot: the top of whatever solid
    // thing is under her (floor, rug, her bed), never a table she walks under.
    private float GroundHeight(Vector3 at)
    {
        Vector3 flatToBed = at - bedPosition; flatToBed.y = 0f;
        float stepUp = flatToBed.magnitude < 1.5f ? 0.6f : 0.25f; // can climb into her bed
        float feetY = transform.position.y - feetOffset;
        Vector3 origin = new Vector3(at.x, feetY + stepUp, at.z);
        float best = float.NegativeInfinity;
        foreach (RaycastHit h in Physics.RaycastAll(origin, Vector3.down, stepUp + 3f, ~0, QueryTriggerInteraction.Ignore))
        {
            Transform t = h.collider.transform;
            if (t.IsChildOf(transform)) continue;
            if (player != null && t.IsChildOf(player)) continue;
            if (h.collider.attachedRigidbody != null && !h.collider.attachedRigidbody.isKinematic) continue;
            if (t.GetComponentInParent<FoodItem>() != null) continue;
            if (h.point.y > best) best = h.point.y;
        }
        return float.IsNegativeInfinity(best) ? transform.position.y : best + feetOffset;
    }

    // "Unreachable" as in standing on a stool/table - Whiskers stays on the floor.
    private bool IsPlayerElevated()
    {
        if (player == null) return false;
        return (player.position.y - homePosition.y) > elevationThreshold;
    }

    private void BeginReturning()
    {
        state = State.Returning;
        returnTimer = 0f;
        hasLastKnownPos = false;
        lostSightTimer = 0f;
        SetChasingAnim(false); // tired - walks the rest of the way, no more running
        returningToFridge = TryFindFridgeSpot(out returnTarget, out returnTargetRot);
        if (!returningToFridge) { returnTarget = homePosition; returnTargetRot = homeRotation; }
        Debug.Log(returningToFridge ? "Whiskers got tired and is heading to the fridge." : "Whiskers got tired and is heading back to bed.");
    }

    private void UpdateReturning()
    {
        // Tired and walking home, but not blind: if Bandit comes within her sight range and she can
        // actually see him (not hidden, not up on furniture, nothing in the way), she's after him again.
        returnTimer += Time.deltaTime;
        if (player != null && returnTimer >= returnGraceTime)
        {
            float range = returnSightRange;
            if (playerController != null && playerController.IsCrouching()) range *= returnCrouchMultiplier;
            Vector3 d = player.position - transform.position; d.y = 0f;
            if (d.magnitude <= range && CanSeePlayer())
            {
                Debug.Log("Whiskers spotted Bandit while heading back - chasing again!");
                WakeUp();
                return;
            }
        }

        Vector3 flatToHome = returnTarget - transform.position; flatToHome.y = 0f;
        float dist = flatToHome.magnitude;

        if (dist > 0.6f)
        {
            MoveSmoothly(returnTarget, returnSpeed, 0.05f);
            return;
        }

        // Last bit: walk straight onto the bed and turn into the sleeping pose in one smooth motion
        // (steering here made her wobble her head back and forth).
        if (dist > 0.05f)
        {
            Vector3 target = new Vector3(returnTarget.x, transform.position.y, returnTarget.z);
            Vector3 np = Vector3.MoveTowards(transform.position, target, returnSpeed * 0.6f * Time.deltaTime);
            np.y = Mathf.MoveTowards(transform.position.y, returnTarget.y, 2.5f * Time.deltaTime);
            transform.position = np;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, returnTargetRot, turnSpeed * 0.5f * Time.deltaTime);
            SetSpeedAnim(returnSpeed * 0.6f);
            return;
        }

        // Arrived: settle in and turn round into her final pose.
        currentSpeed = 0f;
        SetSpeedAnim(0f);
        transform.position = Vector3.MoveTowards(transform.position, returnTarget, returnSpeed * Time.deltaTime);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, returnTargetRot, turnSpeed * 0.5f * Time.deltaTime);

        if (Quaternion.Angle(transform.rotation, returnTargetRot) < 2f)
        {
            transform.position = returnTarget;
            transform.rotation = returnTargetRot;
            if (returningToFridge)
            {
                state = State.Guarding;
                guardTimer = 0f;
                Debug.Log("Whiskers is at the fridge, staying awake for a while.");
            }
            else
            {
                state = State.Asleep;
                Debug.Log("Whiskers is back asleep in bed.");
            }
        }
    }

    private void FaceTowards(Vector3 targetPos)
    {
        Vector3 look = new Vector3(targetPos.x, transform.position.y, targetPos.z);
        if ((look - transform.position).sqrMagnitude > 0.0001f)
        {
            transform.LookAt(look);
        }
    }

    private void SetChasingAnim(bool chasing)
    {
        if (animator != null && hasChasingParam)
        {
            animator.SetBool("IsChasing", chasing);
        }
    }

    // Predicted meeting point: Bandit's position plus his (smoothed) velocity
    // times how long it would roughly take Whiskers to get there - capped
    // so it's only a short look-ahead, and ignored up close.
    private Vector3 GetInterceptPoint(Vector3 playerPos, float dist)
    {
        if (!interceptPlayer || dist < interceptMinDistance || !hasPlayerSample) return playerPos;

        Vector3 v = playerVelocity;
        v.y = 0f;
        if (v.magnitude < 0.3f) return playerPos; // standing still - nothing to predict

        float lead = Mathf.Min(maxLeadTime, dist / Mathf.Max(0.1f, chaseSpeed));
        Vector3 offset = Vector3.ClampMagnitude(v * lead, maxLeadDistance);
        Vector3 aim = playerPos + offset;
        aim.y = homePosition.y;

        // Don't aim into a wall/furniture - fall back to Bandit himself.
        if (avoidObstacles && Physics.Linecast(playerPos + Vector3.up * obstacleCheckHeight, aim + Vector3.up * obstacleCheckHeight,
                                                out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)
            && !hit.transform.IsChildOf(transform) && !(player != null && hit.transform.IsChildOf(player)))
        {
            aim = new Vector3(hit.point.x, homePosition.y, hit.point.z) - offset.normalized * bodyRadius;
        }

        if (showInterceptDebug)
        {
            Debug.DrawLine(transform.position + Vector3.up * 0.3f, aim + Vector3.up * 0.3f, Color.yellow);
            Debug.DrawLine(playerPos + Vector3.up * 0.3f, aim + Vector3.up * 0.3f, new Color(1f, 0.5f, 0f));
        }
        return aim;
    }

    // Tracks Bandit's velocity every frame (from his position), smoothed.
    private void LateUpdate()
    {
        if (player == null) return;
        if (hasPlayerSample && Time.deltaTime > 0f)
        {
            Vector3 v = (player.position - lastPlayerPos) / Time.deltaTime;
            if (v.magnitude < 30f) playerVelocity = Vector3.Lerp(playerVelocity, v, Time.deltaTime * 6f);
        }
        lastPlayerPos = player.position;
        hasPlayerSample = true;
    }

    // Accelerates/decelerates instead of jumping to full speed, turns at a
    // limited rate and always moves the way she's facing - so she runs in
    // curves and her paws (Walk/Run animation) match how fast she's going,
    // instead of gliding sideways across the floor.
    private void MoveSmoothly(Vector3 target, float maxSpeed, float stopDistance)
    {
        Vector3 to = target - transform.position;
        to.y = 0f;
        float dist = to.magnitude;

        float desired = dist > stopDistance ? maxSpeed : 0f;
        const float brakeZone = 0.8f;
        if (dist < stopDistance + brakeZone)
            desired *= Mathf.Clamp01((dist - stopDistance) / brakeZone);

        currentSpeed = Mathf.MoveTowards(currentSpeed, desired, acceleration * Time.deltaTime);

        float facing = 1f;
        bool onPath = false;
        if (dist > 0.001f)
        {
            Vector3 wantDir = to / dist;
            // Follow her navigation path round tables/chairs if she has one.
            Vector3 next;
            if (pathBlockedCooldown > 0f) pathBlockedCooldown -= Time.deltaTime;
            if (pathBlockedCooldown <= 0f && dist > stopDistance + 0.6f && NavNext(target, out next))
            {
                Vector3 toNext = next - transform.position; toNext.y = 0f;
                if (toNext.sqrMagnitude > 0.0001f) { wantDir = toNext.normalized; onPath = true; }
            }
            // Near the end of the path (e.g. climbing into her bed, or right
            // next to Bandit) she goes straight in.
            if (!onPath && avoidObstacles && dist > stopDistance + 1f) wantDir = SteerAround(wantDir, dist);
            Quaternion want = Quaternion.LookRotation(wantDir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeed * Time.deltaTime);
            facing = Vector3.Dot(transform.forward, wantDir);
        }

        // Turn on the spot first if she's facing the wrong way - moving
        // forward while still turning is what made her run in circles.
        float speedNow = currentSpeed * Mathf.InverseLerp(0.5f, 0.95f, facing);
        float step = Mathf.Min(speedNow * Time.deltaTime, Mathf.Max(0f, dist - stopDistance));
        if (avoidObstacles && step > 0f && IsBlocked(transform.forward, step + bodyRadius * 0.3f, float.MaxValue))
        {
            // NEVER step into something solid - pushing on through a blocked
            // path after a short delay would let her run straight through
            // solid geometry like the fridge door.
            blockedTimer += Time.deltaTime;
            step = 0f;
            speedNow = 0f;
            if (blockedTimer > 0.3f)
            {
                // Something's in the way that her path didn't know about (the
                // open fridge door) - feel her way round it for a moment and
                // work out a fresh path.
                pathBlockedCooldown = 0.8f;
                navRepathTime = 0f;
                blockedTimer = 0f;
            }
        }
        else blockedTimer = 0f;
        Vector3 pos = transform.position + transform.forward * step;
        // Stand on whatever is really under her (her bed is raised, so using
        // the bed's height everywhere made her walk on air around the house).
        pos.y = Mathf.MoveTowards(transform.position.y, GroundHeight(pos), 2.5f * Time.deltaTime);
        transform.position = pos;

        SetSpeedAnim(speedNow);
    }

    // First direction (closest to where she wants to go) that isn't blocked.
    // Once she picks a side to go round something she sticks with it for a
    // moment instead of flip-flopping left/right every frame (which looked
    // like spinning on the spot next to the fridge door).
    private static readonly float[] SteerAngles = { 0f, 30f, 60f, 90f, 120f };
    private float steerSide = 1f;
    private float steerHoldTimer = 0f;
    private Vector3 lastSteerDir;

    private Vector3 SteerAround(Vector3 desired, float distToTarget)
    {
        if (steerHoldTimer > 0f)
        {
            steerHoldTimer -= Time.deltaTime;
            if (!IsBlocked(lastSteerDir, lookAhead, distToTarget)) return lastSteerDir;
        }

        if (!IsBlocked(desired, lookAhead, distToTarget)) return desired;

        for (int pass = 0; pass < 2; pass++)
        {
            float side = pass == 0 ? steerSide : -steerSide;
            for (int i = 1; i < SteerAngles.Length; i++)
            {
                Vector3 d = Quaternion.Euler(0f, SteerAngles[i] * side, 0f) * desired;
                if (!IsBlocked(d, lookAhead, distToTarget))
                {
                    steerSide = side;
                    steerHoldTimer = 0.4f;
                    lastSteerDir = d;
                    return d;
                }
            }
        }
        return desired;
    }

    // Anything solid between her and "ignoreBeyond" (usually Bandit's
    // distance) in that direction? Checks a tall capsule so low-hanging
    // things like the open fridge door count too.
    private bool IsBlocked(Vector3 dir, float distance, float ignoreBeyond)
    {
        Vector3 bottom = transform.position + Vector3.up * (0.1f + bodyRadius);
        Vector3 top = transform.position + Vector3.up * Mathf.Max(0.1f + bodyRadius, obstacleCheckHeight + 0.6f);
        RaycastHit[] hits = Physics.CapsuleCastAll(bottom, top, bodyRadius * 0.9f, dir, distance, ~0, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit h in hits)
        {
            if (h.distance <= 0f) continue;                       // already overlapping - let her move out
            if (h.distance > ignoreBeyond) continue;              // it's behind Bandit, doesn't matter
            if (h.collider.transform.IsChildOf(transform)) continue; // herself
            if (player != null && h.collider.transform.IsChildOf(player)) continue; // Bandit is the target, not an obstacle
            if (h.collider.attachedRigidbody != null && !h.collider.attachedRigidbody.isKinematic) continue; // loose props
            return true;
        }
        return false;
    }

    private void SetSpeedAnim(float speed)
    {
        if (animator != null && hasSpeedParam)
            animator.SetFloat("Speed", speed, 0.1f, Time.deltaTime);
    }
}
