using UnityEngine;
using System.Collections;
using System.Collections.Generic;

// Rex: the backyard dog.
[RequireComponent(typeof(CharacterController))]
public class DogNPC : MonoBehaviour
{
    private enum State { Sleeping, Chasing, Returning, Patrolling }

    // Collision
    [Header("Collision")]
    [Tooltip("Rex's CharacterController radius - keep larger than Bandit's so he reads as a bigger animal.")]
    [SerializeField] private float controllerRadius = 0.75f;
    [SerializeField] private float controllerHeight = 1.5f;
    [SerializeField] private Vector3 controllerCenter = new Vector3(0f, 0.75f, 0f);
    [Tooltip("Small constant downward push each frame so the CharacterController stays grounded.")]
    [SerializeField] private float groundedPushSpeed = 2f;

    private CharacterController controller;

    // Proximity wake
    [Header("Proximity Wake")]
    [Tooltip("While Rex is asleep, Bandit wandering within this radius of his dog bed wakes him - no toy needed.")]
    [SerializeField] private float wakeRadius = 3.5f;
    [Tooltip("Multiplier applied to wakeRadius while Bandit is crouching, so sneaking past is safer than walking/running past.")]
    [SerializeField, Range(0f, 1f)] private float crouchWakeRadiusMultiplier = 0.5f;

    // Chase
    [Header("Chase")]
    [Tooltip("How long Rex chases Bandit once woken before giving up and heading back to bed.")]
    [SerializeField] private float chaseDuration = 8f;
    [Tooltip("Rex's BASE chase speed, before any escalation from being fooled (see Escalating Chase Speed below) - never modified directly at runtime.")]
    [SerializeField] private float chaseSpeed = 2.6f;
    [Tooltip("How fast Rex walks home once he's given up the chase.")]
    [SerializeField] private float returnSpeed = 1.8f;

    // Bypass triggers
    [Header("Bypass Triggers")]
    [Tooltip("Extra seconds added on top of chaseDuration while Bandit is standing in one of Rex's bypass trigger zones (see RexBypassTrigger.cs)")]
    [SerializeField] private float bypassChaseBonus = 10f;
    [Tooltip("The FIRST time Bandit triggers one of the LEFT bypass zones, Rex's base chaseSpeed is permanently multiplied by this.")]
    [SerializeField] private float bypassSpeedBoostMultiplier = 1.5f;
    [Tooltip("Same idea as Bypass Speed Boost Multiplier above, but for the RIGHT bypass zones specifically.")]
    [SerializeField] private float bypassSpeedBoostMultiplierRight = 2.0f;
    // How many bypass triggers Bandit is currently standing in (a count, not
    // a bool, so overlapping left/right zones don't cancel each other out
    // when one is exited before the other).
    private int bypassTriggerCount = 0;
    // The highest one-time-permanent bypass multiplier applied so far this
    // playthrough - 1 means neither side has been triggered yet (no boost).
    // Synced with GameManager.rexBypassSpeedMultiplier the same way
    // failedChaseCount is synced with rexFailedChaseCount, below.
    private float bypassSpeedMultiplierApplied = 1f;

    /// Called by RexBypassTrigger when Bandit enters one of Rex's bypass
    /// zones - isRightSide says whether it was one of the RIGHT zones
    /// (bypassSpeedBoostMultiplierRight) or a LEFT one (bypassSpeedBoostMultiplier).
    public void EnterBypassZone(bool isRightSide)
    {
        bypassTriggerCount++;

        float sideMultiplier = isRightSide ? bypassSpeedBoostMultiplierRight : bypassSpeedBoostMultiplier;
        if (sideMultiplier > bypassSpeedMultiplierApplied)
        {
            bypassSpeedMultiplierApplied = sideMultiplier;
            if (GameManager.Instance != null) GameManager.Instance.rexBypassSpeedMultiplier = bypassSpeedMultiplierApplied;
            RecalculateChaseSpeed();
            Debug.Log("Rex was fooled by a " + (isRightSide ? "right" : "left") + " bypass zone - his baseline chase speed is now permanently x" + bypassSpeedMultiplierApplied.ToString("F1") + ".");
        }
    }

    /// Called by RexBypassTrigger when Bandit leaves one of Rex's bypass zones.
    public void ExitBypassZone() => bypassTriggerCount = Mathf.Max(0, bypassTriggerCount - 1);

    // chaseDuration, plus the bypass bonus while Bandit is trying to slip past.
    private float EffectiveChaseDuration => chaseDuration + (bypassTriggerCount > 0 ? bypassChaseBonus : 0f);

    // Escalating chase speed
    [Header("Escalating Chase Speed")]
    [Tooltip("chaseSpeed is multiplied by this each time Rex is fooled (fails to catch Bandit before chaseDuration runs out).")]
    [SerializeField] private float chaseSpeedEscalationFactor = 1.15f;
    [Tooltip("chaseSpeed can never escalate past this value.")]
    [SerializeField] private float maxChaseSpeed = 5.5f;
    // How many times Rex has failed to catch Bandit THIS PLAYTHROUGH.
    private int failedChaseCount = 0;
    private float currentChaseSpeed; // chaseSpeed with escalation applied - what actually moves him

    // Post-chase suspicion patrol
    [Header("Post-Chase Suspicion Patrol")]
    [Tooltip("How far from his dog bed Rex's suspicion-patrol waypoints are placed.")]
    [SerializeField] private float patrolRadius = 2.5f;
    [SerializeField] private float patrolSpeed = 1.8f;
    [Tooltip("How many waypoints make up the patrol loop (2-3 is plenty).")]
    [SerializeField, Range(2, 3)] private int patrolWaypointCount = 3;
    [Tooltip("How long Rex pauses at each waypoint to sniff around before moving to the next one.")]
    [SerializeField] private float patrolWaypointPause = 1.5f;
    [Tooltip("Safety cap on how long Rex will spend walking toward any single patrol waypoint, or toward home, before giving up on that leg and moving on to the next one.")]
    [SerializeField] private float maxLegDuration = 6f;
    private List<Vector3> patrolWaypoints;
    private int patrolIndex;
    private float patrolPauseTimer;
    private float legTimer; // how long he's been trying to reach his current patrol waypoint / home

    // Catch
    [Header("Catch")]
    [Tooltip("Distance at which Rex catches Bandit and triggers instant game over.")]
    [SerializeField] private float catchRange = 1f;
    [SerializeField] private float knockbackForce = 6f;
    [Tooltip("Rex gets a solid box collider around his body at Start so Bandit can't walk through him. This scales the box (1 = exactly the size of his model).")]
    [SerializeField] private float bodyColliderScale = 0.9f;
    [Tooltip("How close (metres) Bandit has to get to Rex's body, while Rex is awake, to count as caught.")]
    [SerializeField] private float bodyCatchReach = 0.25f;
    [Tooltip("Maximum vertical (Y) difference, in metres, between Rex and Bandit for a catch to actually count.")]
    [SerializeField] private float catchMaxHeightDifference = 2.5f;

    [Header("Catch - holding Bandit in Rex's mouth")]
    [Tooltip("Rex's mouth-area child transform Bandit is snapped to (and kept following) for the rest of the catch.")]
    [SerializeField] private Vector3 mouthLocalOffset = new Vector3(0f, -0.12f, 0.32f);
    [Tooltip("Bandit's rotation relative to the mouth transform while held.")]
    [SerializeField] private Vector3 mouthLocalEulerOffset = new Vector3(0f, 0f, 90f);
    private Transform mouthAnchor;

    [Header("Catch - camera framing the bite")]
    [Tooltip("How far in FRONT of Rex's mouth (along Rex's own forward direction) the camera sits during the catch hold.")]
    [SerializeField] private float catchCameraForwardOffset = 2.2f;
    [Tooltip("Sideways offset (perpendicular to the outward direction above) so the shot is a 3/4 front angle rather than dead-on face-first - reads better and avoids clipping into Rex's own head model.")]
    [SerializeField] private float catchCameraSideOffset = 1.3f;
    [Tooltip("Extra height above the mouth anchor the camera sits at.")]
    [SerializeField] private float catchCameraHeight = 0.9f;

    // Audio
    [Header("Audio - barking while chasing")]
    [SerializeField] private AudioClip barkClip;
    [SerializeField] private AudioSource audioSource;
    [Tooltip("Default bark volume (0-1) - a little softer than Unity's out-of-the-box AudioSource default of 1, which read as too loud when Rex caught Bandit.")]
    [SerializeField, Range(0f, 1f)] private float barkVolume = 0.55f;
    [Tooltip("Rex's bark is hard-cut after this many real-time seconds once he catches Bandit.")]
    [SerializeField] private float catchBarkMaxDuration = 2f;
    [Tooltip("How many REAL-TIME seconds Bandit stays frozen, caught in Rex's 'Catch' animation, before the death screen actually appears.")]
    [SerializeField] private float catchAnimationHoldDuration = 2.5f;
    private float baseBarkVolume = 1f;

    // Animation
    [Header("Animation (optional)")]
    [Tooltip("Animator receives bools 'IsChasing'/'IsSleeping', and triggers 'Wake'/'Catch'.")]
    [SerializeField] private Animator animator;

    // Private runtime state
    private State state = State.Sleeping;
    /// True while Rex is running after Bandit.
    public bool IsChasing => state == State.Chasing;
    private Transform player;
    private PlayerController playerController;
    private Vector3 homePosition;     // his dog bed - where he starts, sleeps, and returns to
    private Quaternion homeRotation;
    private float chaseTimer = 0f;
    private float fixedX, fixedZ;     // pitch/roll locked on Start to keep Rex upright
    private BoxCollider bodyCollider;
    private float playerRadius = 0.3f;
    private bool caught = false;

    // Unity lifecycle

    private void Start()
    {
        controller = GetComponent<CharacterController>();

        // Rex's GameObject carries a large compensating localScale.
        float scaleCompensation = transform.lossyScale.x > 0.0001f ? 1f / transform.lossyScale.x : 1f;
        controller.radius = controllerRadius * scaleCompensation;
        controller.height = controllerHeight * scaleCompensation;
        controller.center = controllerCenter * scaleCompensation;

        stepScale = scaleCompensation;
        controller.stepOffset = Mathf.Min(stepOverHeight * stepScale, controller.height * 0.5f);

        // His own dog bed has a solid MeshCollider so BANDIT can't just walk through it.
        GameObject bed = GameObject.Find("DogBed");
        if (bed != null) bedRoot = bed.transform;
        if (bed != null)
        {
            foreach (Collider bedCollider in bed.GetComponentsInChildren<Collider>(true))
            {
                if (bedCollider != null) Physics.IgnoreCollision(controller, bedCollider, true);
            }
        }

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
            playerController = playerObj.GetComponent<PlayerController>();
        }

        // Resolve Rex's mouth-area anchor ONCE here rather than every catch.
        mouthAnchor = FindChildRecursive(transform, "Muzzle")
                   ?? FindChildRecursive(transform, "Head")
                   ?? transform;

        BuildBodyCollider();
        CharacterController playerCC = player != null ? player.GetComponent<CharacterController>() : null;
        if (playerCC != null)
            playerRadius = playerCC.radius * Mathf.Max(player.lossyScale.x, player.lossyScale.z);

        // Wherever Rex is placed in the scene IS his dog bed - Sleeping,
        // Returning and his suspicion patrol all resolve back around this spot.
        homePosition = transform.position;
        homeRotation = transform.rotation;

        fixedX = transform.eulerAngles.x;
        fixedZ = transform.eulerAngles.z;

        GeneratePatrolWaypoints();

        // Pick up wherever this playthrough left off, rather than starting
        // back at 0 just because this scene instance of Rex is freshly loaded.
        failedChaseCount = GameManager.Instance != null ? GameManager.Instance.rexFailedChaseCount : 0;
        bypassSpeedMultiplierApplied = GameManager.Instance != null ? GameManager.Instance.rexBypassSpeedMultiplier : 1f;
        RecalculateChaseSpeed();

        if (animator    == null) animator    = GetComponent<Animator>();
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop        = true;
        audioSource.volume      = barkVolume; // softer default than a freshly-added AudioSource's out-of-the-box volume of 1
        if (barkClip != null)   audioSource.clip = barkClip;
        baseBarkVolume = audioSource.volume;

        EnterSleep();
    }

    private void Update()
    {
        // Debug shortcut - Y key triggers a wake-up call from a toy
        if (Input.GetKeyDown(KeyCode.Y)) WakeUp();

        if (audioSource != null) audioSource.volume = baseBarkVolume * SoundFX.SfxVolumeScale;

        // Don't let Rex's bark carry on underneath the loading screen while the level is restarting (e.g. after losing a life to the lasers mid-chase)
        if (SceneFader.IsBusy)
        {
            StopBarking();
        }

        switch (state)
        {
            case State.Sleeping:   CheckProximityWake(); break;
            case State.Chasing:    UpdateChasing();      break;
            case State.Returning:  UpdateReturning();    break;
            case State.Patrolling: UpdatePatrolling();   break;
        }

        // Awake Rex touching Bandit = caught, whatever he was doing.
        if (!caught && state != State.Sleeping && !HidingSpot.PlayerIsHidden && SameLevelAsPlayer() && BodyTouchingPlayer())
            CatchPlayer();
    }

    // Public API

    // Called by DogToy when Bandit disturbs it, or automatically by
    // CheckProximityWake() when Bandit gets too close to his dog bed.
    public void WakeUp()
    {
        if (state == State.Chasing)
        {
            // Already after him - a fresh disturbance just tops the
            // clock back up rather than layering a second chase on top.
            chaseTimer = 0f;
            return;
        }

        Debug.Log("Rex woke up! He's chasing Bandit!");
        EnterChase();
    }

    // State: Sleeping

    // While Rex is asleep, Bandit walking within range of his dog bed
    // wakes him on his own - no dog toy required. Crouching shrinks the
    // effective radius, so sneaking past sleeping Rex is safer than
    // walking or running past him.
    private void CheckProximityWake()
    {
        if (player == null) return;

        float effectiveRadius = wakeRadius;
        if (playerController != null && playerController.IsCrouching())
            effectiveRadius *= crouchWakeRadiusMultiplier;

        float flatDist = new Vector2(
            transform.position.x - player.position.x,
            transform.position.z - player.position.z).magnitude;

        if (flatDist <= effectiveRadius)
            WakeUp();
    }

    // State: Chasing

    private void UpdateChasing()
    {
        chaseTimer += Time.deltaTime;

        float effectiveChaseDuration = EffectiveChaseDuration;
        if (chaseTimer >= effectiveChaseDuration)
        {
            Debug.Log("Rex gave up the chase after " + effectiveChaseDuration + "s without catching Bandit.");
            OnChaseFailed();
            return;
        }

        if (player == null) return;

        // Face and move toward Bandit (ignore Y so Rex stays on the ground).
        Vector3 target = new Vector3(player.position.x, transform.position.y, player.position.z);
        Vector3 moveDir = target - transform.position;
        moveDir.y = 0f;

        if (moveDir.sqrMagnitude > 0.0001f)
        {
            float yaw = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
            transform.eulerAngles = new Vector3(fixedX, yaw, fixedZ);
        }

        // Stop advancing while Bandit is hidden, but keep the timer running
        // (Rex is still tired even if he loses the scent temporarily).
        if (!HidingSpot.PlayerIsHidden)
            MoveTowardsWithCollision(target, currentChaseSpeed);

        // Catch check.
        float flatDist = new Vector2(
            transform.position.x - player.position.x,
            transform.position.z - player.position.z).magnitude;

        if (!HidingSpot.PlayerIsHidden && SameLevelAsPlayer() && flatDist < catchRange)
            CatchPlayer();
    }

    // Called whenever a chase times out without a catch. Handles both the
    // escalating chase speed and kicking off the post-chase suspicion patrol.
    private void OnChaseFailed()
    {
        // Escalating chase speed.
        failedChaseCount++;
        if (GameManager.Instance != null) GameManager.Instance.rexFailedChaseCount = failedChaseCount;
        RecalculateChaseSpeed();
        Debug.Log("Rex has been fooled " + failedChaseCount + " time(s) - chase speed is now " + currentChaseSpeed.ToString("F2") + " m/s.");

        // Post-chase suspicion patrol - instead of heading straight back to
        // bed, he's now suspicious and checks the area around his dog bed
        // first. This replaces the old "straight to sleep" behaviour for
        // good, permanently, for the rest of the playthrough.
        BeginPatrol();
    }

    // Recomputes currentChaseSpeed from the base chaseSpeed.
    private void RecalculateChaseSpeed()
    {
        float baseSpeed = chaseSpeed * bypassSpeedMultiplierApplied;
        currentChaseSpeed = Mathf.Min(
            baseSpeed * Mathf.Pow(chaseSpeedEscalationFactor, failedChaseCount),
            maxChaseSpeed);
    }

    // State: Patrolling

    private void GeneratePatrolWaypoints()
    {
        patrolWaypoints = new List<Vector3>(patrolWaypointCount);
        for (int i = 0; i < patrolWaypointCount; i++)
        {
            float angle = (360f / patrolWaypointCount) * i * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * patrolRadius;
            patrolWaypoints.Add(homePosition + offset);
        }
    }

    private void BeginPatrol()
    {
        state = State.Patrolling;
        patrolIndex = 0;
        patrolPauseTimer = 0f;
        legTimer = 0f;
        SetAnimState(chasing: true, sleeping: false); // walking to the first waypoint
        StopBarking();
        Debug.Log("Rex is suspicious - patrolling around his dog bed before settling.");
    }

    private void UpdatePatrolling()
    {
        // Paused at a waypoint, sniffing around before moving to the next one.
        if (patrolPauseTimer > 0f)
        {
            patrolPauseTimer -= Time.deltaTime;
            if (patrolPauseTimer <= 0f)
            {
                patrolIndex++;
                if (patrolIndex >= patrolWaypoints.Count)
                {
                    // Patrol loop finished - only now does he actually head home.
                    BeginReturning();
                    return;
                }
                legTimer = 0f;
                SetAnimState(chasing: true, sleeping: false); // resume walking
            }
            return;
        }

        Vector3 target = patrolWaypoints[patrolIndex];
        Vector3 toTarget = target - transform.position;
        toTarget.y = 0f;

        if (toTarget.sqrMagnitude > 0.0001f)
        {
            float yaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
            transform.eulerAngles = new Vector3(fixedX, yaw, fixedZ);
        }

        MoveTowardsWithCollision(target, patrolSpeed);
        legTimer += Time.deltaTime;

        bool arrived = new Vector2(toTarget.x, toTarget.z).magnitude < 0.2f;
        bool gaveUpOnThisWaypoint = legTimer >= maxLegDuration;

        if (arrived || gaveUpOnThisWaypoint)
        {
            if (gaveUpOnThisWaypoint && !arrived)
                Debug.Log("Rex couldn't reach a patrol waypoint (likely blocked) - moving on so he doesn't get stuck.");

            // Arrived at (or gave up on) this waypoint - pause and sniff before moving on.
            SetAnimState(chasing: false, sleeping: false);
            patrolPauseTimer = patrolWaypointPause;
        }
    }

    // State: Returning

    private void BeginReturning()
    {
        state = State.Returning;
        legTimer = 0f;
        SetAnimState(chasing: false, sleeping: false);
        StopBarking();
    }

    private void UpdateReturning()
    {
        Vector3 toHome = homePosition - transform.position;
        toHome.y = 0f;

        if (toHome.sqrMagnitude > 0.0001f)
        {
            float yaw = Mathf.Atan2(toHome.x, toHome.z) * Mathf.Rad2Deg;
            transform.eulerAngles = new Vector3(fixedX, yaw, fixedZ);
        }

        MoveTowardsWithCollision(homePosition, returnSpeed);
        legTimer += Time.deltaTime;

        bool arrived = new Vector2(toHome.x, toHome.z).magnitude < 0.2f;
        bool gaveUpWalkingHome = legTimer >= maxLegDuration;

        if (arrived || gaveUpWalkingHome)
        {
            if (gaveUpWalkingHome && !arrived)
                Debug.Log("Rex couldn't walk all the way home (likely blocked) - snapping back to bed so he doesn't get stuck.");

            // Either he genuinely made it, or he's been trying for too long and
            // something's blocking the path - either way, put him to bed rather
            // than leaving him stuck mid-walk forever.
            transform.position = homePosition;
            // Force Z back to level on arrival.
            transform.eulerAngles = new Vector3(fixedX, homeRotation.eulerAngles.y, 0f);
            EnterSleep();
        }
    }

    // Transitions

    private void EnterSleep()
    {
        state = State.Sleeping;
        SetAnimState(chasing: false, sleeping: true);
        StopBarking();
        Debug.Log("Rex is asleep by his bed.");
    }

    private void EnterChase()
    {
        state       = State.Chasing;
        chaseTimer  = 0f;
        SetAnimState(chasing: true, sleeping: false);
        if (animator != null) animator.SetTrigger("Wake");
        StartBarking();
    }

    // Helpers

    // True only while Bandit is roughly on the same level as Rex (see catchMaxHeightDifference)
    private bool SameLevelAsPlayer()
    {
        if (player == null) return false;
        return Mathf.Abs(player.position.y - transform.position.y) <= catchMaxHeightDifference;
    }

    private void CatchPlayer()
    {
        if (caught) return;
        caught = true;
        Debug.Log("Rex caught Bandit!");
        state = State.Sleeping; // freeze Rex immediately; the scene is reloading anyway
        SetAnimState(chasing: false, sleeping: true);
        if (animator != null) animator.SetTrigger("Catch");
        SoundFX.Play("dog_bite"); // Rex's bite sound - Assets/Resources/Audio/SFX/dog_bite.mp3, timed with the Catch animation/mouth-hold starting above

        // Let the bark ring out a touch rather than cutting it dead silent,
        // but hard-capped so it can never linger on top of the death screen.
        StopBarkingAfterDelay(catchBarkMaxDuration);

        if (player != null)
        {
            PlayerController pc = player.GetComponent<PlayerController>();
            if (pc != null) pc.ApplyKnockback(transform.position, knockbackForce);

            // Freeze Bandit right where he's caught - no more moving, and no
            // more of his own idle/run animation playing underneath - so
            // Rex's "Catch" trigger above is actually what's seen happening,
            // instead of the death screen slamming down over it instantly.
            if (pc != null) pc.enabled = false;
            Animator playerAnim = player.GetComponentInChildren<Animator>();
            if (playerAnim != null) playerAnim.enabled = false;

            // Snap Bandit into Rex's mouth and keep him there for the rest
            // of the hold - this is what actually SHOWS he's been caught in
            // Rex's mouth, rather than just standing frozen next to him with
            // Rex's own Catch animation playing off to the side.
            StartCoroutine(HoldBanditInMouth(mouthAnchor, catchAnimationHoldDuration));

            // Take the camera over for the same hold, so it actually shows
            // off the bite instead of the normal over-the-shoulder framing
            // breaking (see CatchCamera below for exactly why that happens).
            StartCoroutine(CatchCamera(mouthAnchor, catchAnimationHoldDuration));
        }

        // Hold here in real time - long enough for Rex's "Catch" animation to
        // actually play out with Bandit frozen in it - before the death
        // screen appears. See CatchGameOverAfterDelay below.
        StartCoroutine(CatchGameOverAfterDelay(catchAnimationHoldDuration));

        // GameManager.LoseAllLivesInstantly() (called from the delayed coroutine above, via GameOver() -> the Game Over screen) sets Time.timeScale = 0 once IT.
        enabled = false;
    }

    // Waits `seconds` of REAL time.
    private IEnumerator CatchGameOverAfterDelay(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);

        if (GameManager.Instance != null)
        {
            GameManager.Instance.pendingGameOverMessage =
                "REX CAUGHT YOU.\n\nGAME OVER! YOU LOSE.\n\nWould you like to try again?";
            GameManager.Instance.LoseAllLivesInstantly();
        }
    }

    // Moves Rex through the CharacterController so he collides with walls and
    // furniture rather than phasing through them. He also checks his whole
    // body box a little way ahead and steers around anything solid, then
    // pushes himself back out of anything his body ends up overlapping.
    private float steerSign = 1f;

    [Header("Stepping / getting unstuck")]
    [Tooltip("Things lower than this (metres) Rex simply walks over, like Bandit does.")]
    [SerializeField] private float stepOverHeight = 0.5f;
    [Tooltip("When Rex is stuck he hops over / out of things up to this height (metres).")]
    [SerializeField] private float hopOverHeight = 1.2f;
    private float stepScale = 1f;
    private Transform bedRoot;
    private float escapeUntil;
    private float stuckTimer;
    private Vector3 commitDir;
    private float commitUntil, heading, lastSteerTime;
    private Vector3 stuckCheckPos;

    private bool Escaping => Time.time < escapeUntil;

    private void MoveTowardsWithCollision(Vector3 target, float speed)
    {
        Vector3 toTarget = target - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.0001f) return;

        // Stuck detection: trying to move but barely getting anywhere -> hop out.
        stuckTimer += Time.deltaTime;
        if (stuckTimer >= 0.6f)
        {
            Vector3 moved = transform.position - stuckCheckPos; moved.y = 0f;
            if (moved.magnitude < speed * 0.6f * 0.25f && toTarget.magnitude > 0.5f)
            {
                escapeUntil = Time.time + 0.9f;
                steerSign = -steerSign;
                commitUntil = 0f;
            }
            stuckCheckPos = transform.position;
            stuckTimer = 0f;
        }

        controller.stepOffset = Mathf.Min((Escaping ? hopOverHeight : stepOverHeight) * stepScale, controller.height * 0.5f);

        Vector3 dir = toTarget.normalized;
        float step = speed * Time.deltaTime;
        float look = Mathf.Max(step * 3f, 0.4f);

        // Stick with a detour once picked, so he doesn't dither between directions.
        Vector3 chosen = dir;
        bool found = false;
        if (Time.time < commitUntil && !BodyBlockedAt(transform.position + commitDir * look, commitDir))
        {
            chosen = commitDir;
            found = true;
        }
        else if (!BodyBlockedAt(transform.position + dir * look, dir))
        {
            chosen = dir;
            found = true;
            commitUntil = 0f;
        }
        else
        {
            float[] angles = { 30f, 60f, 90f, 120f, 150f };
            for (int i = 0; i < angles.Length && !found; i++)
            {
                // Always try the side he's already going round first.
                for (int k = 0; k < 2; k++)
                {
                    float sg = k == 0 ? steerSign : -steerSign;
                    Vector3 cand = Quaternion.Euler(0f, angles[i] * sg, 0f) * dir;
                    if (!BodyBlockedAt(transform.position + cand * look, cand))
                    {
                        chosen = cand;
                        found = true;
                        steerSign = sg;
                        commitDir = cand;
                        commitUntil = Time.time + 0.8f;
                        break;
                    }
                }
            }
        }
        if (!found)
        {
            // Boxed in on every side: hop out instead of freezing.
            escapeUntil = Time.time + 0.9f;
            controller.stepOffset = Mathf.Min(hopOverHeight * stepScale, controller.height * 0.5f);
            chosen = dir;
        }

        // Turn smoothly towards where he is actually heading.
        if (Time.time - lastSteerTime > 0.25f)
            heading = transform.eulerAngles.y;
        lastSteerTime = Time.time;
        float wantYaw = Mathf.Atan2(chosen.x, chosen.z) * Mathf.Rad2Deg;
        heading = Mathf.MoveTowardsAngle(heading, wantYaw, 300f * Time.deltaTime);
        transform.eulerAngles = new Vector3(fixedX, heading, fixedZ);

        Vector3 delta = Vector3.ClampMagnitude(chosen * step, step);
        delta.y = -groundedPushSpeed * Time.deltaTime; // keep him pressed to the ground
        controller.Move(delta);

        if (!Escaping) PushOutOfObstacles();
    }

    private bool IsSolidObstacle(Collider c)
    {
        if (c == null || c.isTrigger || c == bodyCollider) return false;
        if (c.transform.IsChildOf(transform)) return false;
        if (bedRoot != null && c.transform.IsChildOf(bedRoot)) return false;
        if (c.CompareTag("Player") || c.transform.root.CompareTag("Player")) return false;
        // Floors and anything low enough to walk (or hop) over
        float limit = Escaping ? hopOverHeight : stepOverHeight;
        if (c.bounds.max.y <= transform.position.y + limit) return false;
        return true;
    }

    private bool BodyBlockedAt(Vector3 pos, Vector3 facing)
    {
        if (bodyCollider == null) return false;
        float yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
        Quaternion rot = Quaternion.Euler(fixedX, yaw, fixedZ);
        Vector3 ls = transform.lossyScale;
        Vector3 center = pos + rot * Vector3.Scale(bodyCollider.center, ls);
        Vector3 half = Vector3.Scale(bodyCollider.size * 0.5f, new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z)));
        foreach (Collider c in Physics.OverlapBox(center, half, rot, ~0, QueryTriggerInteraction.Ignore))
            if (IsSolidObstacle(c)) return true;
        return false;
    }

    private void PushOutOfObstacles()
    {
        if (bodyCollider == null) return;
        Bounds b = bodyCollider.bounds;
        foreach (Collider c in Physics.OverlapBox(b.center, b.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!IsSolidObstacle(c)) continue;
            Vector3 pushDir; float dist;
            if (Physics.ComputePenetration(bodyCollider, bodyCollider.transform.position, bodyCollider.transform.rotation,
                                           c, c.transform.position, c.transform.rotation, out pushDir, out dist))
            {
                pushDir.y = 0f;
                if (pushDir.sqrMagnitude > 0.0001f)
                    controller.Move(pushDir.normalized * dist);
            }
        }
    }

    private void SetAnimState(bool chasing, bool sleeping)
    {
        if (animator == null) return;
        animator.SetBool("IsChasing",  chasing);
        animator.SetBool("IsSleeping", sleeping);
    }

    private void StartBarking()
    {
        if (audioSource == null || barkClip == null) return;
        if (!audioSource.isPlaying)
        {
            audioSource.clip = barkClip;
            audioSource.loop = true;
            audioSource.Play();
        }
    }

    private void StopBarking()
    {
        if (audioSource != null) audioSource.Stop();
    }

    // Hard-stops the bark after `seconds` of REAL time.
    private void StopBarkingAfterDelay(float seconds)
    {
        if (audioSource == null) return;
        StartCoroutine(StopBarkingAfterDelayRoutine(seconds));
    }

    private IEnumerator StopBarkingAfterDelayRoutine(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        StopBarking();
    }

    // Holds Bandit in Rex's jaws for the catch and shakes him like a toy.
    // Rex's root is rotated -90 on X (Blender import), so transform.forward
    // points up - the direction from his root to his Muzzle is used as
    // "forward" instead.
    private IEnumerator HoldBanditInMouth(Transform mouth, float duration)
    {
        if (player == null) yield break;

        CharacterController pcc = player.GetComponent<CharacterController>();
        if (pcc != null) pcc.enabled = false;

        Vector3 outward = mouth != null ? mouth.position - transform.position : Vector3.zero;
        outward.y = 0f;
        if (outward.sqrMagnitude < 0.0001f)
            outward = Quaternion.Euler(0f, transform.eulerAngles.y, 0f) * Vector3.forward;
        outward.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, outward).normalized;

        // Tip of Rex's snout and the ground under him, from his renderers.
        float front = 0.5f;
        float mouthY = mouth != null ? mouth.position.y : transform.position.y + 0.5f;
        float groundY = transform.position.y;
        bool anyR = false;
        Bounds rb = new Bounds(transform.position, Vector3.zero);
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || !r.enabled) continue;
            if (!anyR) { rb = r.bounds; anyR = true; } else rb.Encapsulate(r.bounds);
        }
        if (anyR)
        {
            Vector3 e = rb.extents;
            front = 0f;
            for (int i = 0; i < 4; i++)
            {
                Vector3 c = rb.center + new Vector3((i & 1) == 0 ? -e.x : e.x, 0f, (i & 2) == 0 ? -e.z : e.z);
                front = Mathf.Max(front, Vector3.Dot(c - transform.position, outward));
            }
            groundY = rb.min.y;
        }
        Vector3 bitePoint = transform.position + outward * front;
        bitePoint.y = mouthY;

        Renderer[] banditRenderers = player.GetComponentsInChildren<Renderer>();
        float baseYaw = transform.eulerAngles.y;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            // Hard shakes at first, settling down towards the end.
            float shake = Mathf.Sin(elapsed * 14f) * Mathf.Clamp01(1.6f - elapsed * 0.5f);

            // Rex swings his head side to side.
            transform.eulerAngles = new Vector3(fixedX, baseYaw + shake * 12f, fixedZ);

            // Bandit lies sideways across the jaws.
            player.rotation = Quaternion.AngleAxis(shake * 30f, Vector3.up)
                            * Quaternion.LookRotation(side, Vector3.up)
                            * Quaternion.Euler(0f, 0f, 75f);
            player.position = bitePoint + side * shake * 0.18f;

            // Line up the middle of Bandit's body with the bite point, so it
            // doesn't matter where his pivot is, and keep him above the ground.
            Bounds bb = GetBounds(banditRenderers, player.position);
            Vector3 fix = bitePoint + side * shake * 0.18f - bb.center;
            float lift = (groundY + 0.02f) - (bb.min.y + fix.y);
            if (lift > 0f) fix.y += lift;
            player.position += fix;

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private static Bounds GetBounds(Renderer[] renderers, Vector3 fallback)
    {
        Bounds b = new Bounds(fallback, Vector3.zero);
        bool any = false;
        foreach (Renderer r in renderers)
        {
            if (r == null || !r.enabled || r is ParticleSystemRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    // Frames the catch shot off Rex instead of the player camera, disabling
    // PT_MouseLook and CameraCollision for the hold so they don't inherit the tilt.
    private IEnumerator CatchCamera(Transform mouth, float duration)
    {
        if (player == null) yield break;
        Camera cam = player.GetComponentInChildren<Camera>();
        if (cam == null) yield break;

        Transform camTransform = cam.transform;
        PT_MouseLook mouseLook = cam.GetComponent<PT_MouseLook>();
        CameraCollision camCollision = cam.GetComponent<CameraCollision>();

        Vector3 originalLocalPos = camTransform.localPosition;
        Quaternion originalLocalRot = camTransform.localRotation;
        bool mouseLookWasEnabled = mouseLook != null && mouseLook.enabled;
        bool camCollisionWasEnabled = camCollision != null && camCollision.enabled;

        if (mouseLook != null) mouseLook.enabled = false;
        if (camCollision != null) camCollision.enabled = false;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (mouth != null)
            {
                // Deliberately NOT transform.forward/transform.right here.
                Vector3 outward = mouth.position - transform.position;
                outward.y = 0f;
                float outwardDist = outward.magnitude;
                outward = outwardDist > 0.0001f ? outward / outwardDist : transform.forward;
                Vector3 side = Vector3.Cross(Vector3.up, outward).normalized;

                Vector3 camPos = mouth.position
                    + outward * catchCameraForwardOffset
                    + side * catchCameraSideOffset
                    + Vector3.up * catchCameraHeight;
                camTransform.position = camPos;
                camTransform.rotation = Quaternion.LookRotation(mouth.position - camPos, Vector3.up);
            }
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // Restore exactly how the camera normally sits (its parent never
        // changed - only its local position/rotation and the two scripts
        // above were touched) so gameplay after a restart isn't left with a
        // stale framing.
        camTransform.localPosition = originalLocalPos;
        camTransform.localRotation = originalLocalRot;
        if (mouseLook != null) mouseLook.enabled = mouseLookWasEnabled;
        if (camCollision != null) camCollision.enabled = camCollisionWasEnabled;
    }

    // Recursive child search (unlike Transform.Find, which only checks immediate children)
    private void BuildBodyCollider()
    {
        Bounds local = new Bounds();
        bool any = false;

        foreach (MeshFilter mf in GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            AddCorners(mf.sharedMesh.bounds, mf.transform, ref local, ref any);
        }
        foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (smr.sharedMesh == null) continue;
            AddCorners(smr.sharedMesh.bounds, smr.transform, ref local, ref any);
        }
        if (!any) return;

        bodyCollider = gameObject.AddComponent<BoxCollider>();
        bodyCollider.center = local.center;
        bodyCollider.size = local.size * bodyColliderScale;

        // Rex moves with his own CharacterController - don't let it bump
        // into his own body box.
        if (controller != null) Physics.IgnoreCollision(controller, bodyCollider, true);
    }

    private void AddCorners(Bounds b, Transform owner, ref Bounds local, ref bool any)
    {
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3(
                (i & 1) == 0 ? b.min.x : b.max.x,
                (i & 2) == 0 ? b.min.y : b.max.y,
                (i & 4) == 0 ? b.min.z : b.max.z);
            Vector3 p = transform.InverseTransformPoint(owner.TransformPoint(corner));
            if (!any) { local = new Bounds(p, Vector3.zero); any = true; }
            else local.Encapsulate(p);
        }
    }

    private bool BodyTouchingPlayer()
    {
        if (bodyCollider == null || player == null) return false;
        Vector3 probe = player.position + Vector3.up * playerRadius;
        Vector3 closest = bodyCollider.ClosestPoint(probe);
        Vector2 gap = new Vector2(closest.x - probe.x, closest.z - probe.z);
        return gap.magnitude < playerRadius + bodyCatchReach;
    }

    private static Transform FindChildRecursive(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name) return t;
        }
        return null;
    }
}
