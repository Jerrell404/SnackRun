using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Edgar: starts seated on the sofa, doing nothing.
public class HumanNPC : NPCPatrol
{
    public static HumanNPC Instance;

    // Kitchen-timer wake + chase
    [Header("Wake-up (TV glitch / timer threshold)")]
    [Tooltip("Seconds left when Edgar wakes from the sofa and plays his stand-up animation.")]
    [SerializeField] private float wakeThreshold = 20f;
    [Tooltip("Auto-found if left empty.")]
    [SerializeField] private TVFlickerController tvFlicker;

    [Header("Chase (starts 10 s before time runs out)")]
    [Tooltip("The House scene's kitchen timer - auto-found if left empty.")]
    [SerializeField] private KitchenTimer kitchenTimer;
    [Tooltip("Seconds left on the clock when Edgar starts his timed beeline for Bandit. " +
             "Not used once the chip-bag escalation below has been armed - see scriptedPatrolTimerThreshold instead.")]
    [SerializeField] private float chaseThreshold = 10f;
    [Tooltip("Edgar can only physically grab Bandit when the clock is at or below this many seconds.")]
    [SerializeField] private float catchThreshold = 1f;
    [Tooltip("Maximum chase speed (m/s). Edgar's actual speed is distance/timeRemaining " +
             "so he arrives at exactly t=0, but never exceeds this cap.")]
    [SerializeField] private float maxChaseSpeed = 8f;
    [Tooltip("Fallback speed used once the timer is effectively zero or Edgar is within catchRange.")]
    [SerializeField] private float chaseSpeed = 3.5f;
    [Tooltip("How close Edgar needs to be to grab Bandit.")]
    [SerializeField] private float catchRange = 1.3f;

    // Chip-bag escalation: scripted sofa -> Whiskers -> fridge -> backdoor patrol
    [Header("Chip-bag escalation patrol (2nd crunch, then last 10s)")]
    [Tooltip("In order: Whiskers, the fridge, the backdoor. Edgar starts from wherever he spawned " +
             "(the sofa) and automatically heads back there on his own after the last waypoint - " +
             "no sofa waypoint needed here.")]
    [SerializeField] private Transform[] chipEscalationWaypoints;
    [Tooltip("Movement speed while walking this scripted route.")]
    [SerializeField] private float scriptedPatrolSpeed = 2.5f;
    [Tooltip("Kitchen timer must be at or below this many seconds remaining - on top of the chip bag " +
             "having been stepped on a second time - before Edgar actually sets off on the route.")]
    [SerializeField] private float scriptedPatrolTimerThreshold = 10f;
    [Tooltip("Message shown in the caught cutscene if Edgar spots Bandit during this scripted route.")]
    [SerializeField] private string scriptedCatchMessage =
        "You've been caught by Edgar!\nYou lost a life. Try to stay out of his sight next time.";
    [Tooltip("Extra seconds added on top of the scene's own base freeze (currently 1s in " +
             "RestartSceneOnLifeLoss) so the \"caught\" message stays on screen for about 2 seconds total.")]
    [SerializeField] private float scriptedCatchFreezeBonus = 1f;

    // Patrol vision
    [Header("Patrol vision (active once Edgar is patrolling)")]
    [Tooltip("Max metres Edgar can see Bandit from while walking his route.")]
    [SerializeField] private float patrolVisionRange = 8f;
    [Tooltip("Half-angle of Edgar's vision cone in degrees. " +
             "55 = wide but not omniscient.")]
    [SerializeField] private float patrolVisionAngle = 55f;
    [Tooltip("Seconds Edgar must keep his eyes on Bandit before he breaks " +
             "into a chase. A small delay stops a single-frame glance " +
             "from immediately triggering a chase.")]
    [SerializeField] private float sightConfirmTime = 0.8f;

    // Losing sight
    [Header("Losing sight (e.g. Bandit hides)")]
    [Tooltip("Seconds Edgar waits at Bandit's last-known position before " +
             "giving up and returning to normal.")]
    [SerializeField] private float loseInterestDelay = 5f;

    // Game over on catch
    [Header("Game over on catch")]
    [Tooltip("Auto-found if left empty.")]
    [SerializeField] private TimeUpScreen timeUpScreen;

    // Animation
    [Header("Animation")]
    [Tooltip("Auto-found on this object if left empty.")]
    [SerializeField] private Animator animator;

    // Private state
    private Transform player;
    private Vector3 homePosition;
    private Quaternion homeRotation;

    // Core flags
    private bool patrolEnabled = false;
    private bool timerChaseActive = false;   // kitchen-timer chase
    private bool forceTimerChase = false;    // chip-bag 3rd+ crunch: skip chaseThreshold gate
    private bool returning = false;
    private bool caught = false;
    private bool warned = false;             // kept for legacy warn/catch callers

    // Wake-up state
    private bool wakeAnimTriggered = false;  // stand-up animation played once

    // Patrol-sight chase
    private bool patrolSightChaseActive = false;
    private float sightTimer = 0f;           // running total while Edgar can see Bandit
    private float patrolLostSightTimer = 0f; // counts up while Edgar can't see him mid-patrol-chase

    // Chip-bag escalation (2nd crunch scripted route)
    private bool chipEscalationArmed = false;    // set once by the 2nd crunch
    private bool scriptedPatrolConsumed = false; // true once the route has run once (caught, or made it home)
    private bool scriptedPatrolActive = false;   // true while walking the route or mid-catch-chase
    private bool scriptedPatrolChasing = false;  // true once Bandit's been spotted during the route
    private int scriptedWaypointIndex = 0;
    private float scriptedSightTimer = 0f;
    private float chipEscalationStatusLogTimer = 0f; // throttles the "waiting for the clock" log below

    // Shared last-known position (reused by both chase modes)
    private Vector3 lastKnownPlayerPos;
    private bool hasLastKnownPos = false;
    private float timerChaseLostSightTimer = 0f; // separate counter for the timer-chase lost-sight


    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) player = playerObj.transform;

        homePosition = transform.position;
        homeRotation = transform.rotation;
        if (!string.IsNullOrEmpty(scriptedCatchMessage))
            scriptedCatchMessage = scriptedCatchMessage.Replace("by the human", "by Edgar").Replace("the human", "Edgar");

        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.applyRootMotion = false; // the script moves Edgar
            foreach (AnimatorControllerParameter p in animator.parameters)
                if (p.name == "Speed") hasSpeedParam = true;
        }
        lastAnimPos = transform.position;
        SetUpBodyParts();
        BuildNavigation();
        if (kitchenTimer == null) kitchenTimer = FindObjectOfType<KitchenTimer>();
        if (timeUpScreen == null) timeUpScreen = FindObjectOfType<TimeUpScreen>();
        if (tvFlicker == null) tvFlicker = FindObjectOfType<TVFlickerController>();
    }

    // Called by ChipPacket on the 2nd crunch only. Doesn't move Edgar right away.
    public void ArmChipEscalationPatrol()
    {
        if (chipEscalationArmed) return; // already armed - a repeat call is a no-op
        chipEscalationArmed = true;
        Debug.Log("Edgar is on alert after that second crunch - he'll do his rounds once the clock gets low.");
    }

    // Called by ChipPacket on the 3rd crunch and every crunch after that.
    public void StartPatrolling()
    {
        TriggerWakeUp();
        patrolEnabled = true;
        SetPatrollingAnim(true);

        // Skip the chaseThreshold gate entirely - go straight into the
        // kitchen-timer chase branch of Update(), and allow an immediate
        // catch instead of waiting for the timer to reach catchThreshold.
        forceTimerChase = true;

        // A forced chase always takes priority over the scripted route -
        // abandon it in place if it was still in progress.
        scriptedPatrolActive = false;
        scriptedPatrolChasing = false;

        // Collapse the timer to (roughly) how long it'll actually take Edgar
        // to reach Bandit, so the distance/time speed math in
        // UpdateTimerChase() has him arrive right away instead of ambling in
        // at maxChaseSpeed over however much time happened to be left.
        if (kitchenTimer != null && player != null)
        {
            float dist = Vector3.Distance(transform.position, player.position);
            float travelTime = maxChaseSpeed > 0f ? dist / maxChaseSpeed : catchThreshold;
            kitchenTimer.timeRemaining = Mathf.Min(kitchenTimer.timeRemaining,
                                                    Mathf.Max(travelTime, catchThreshold));
        }

        Debug.Log("Edgar got off the sofa and is coming straight for Bandit!");
    }

    protected override void Update()
    {
        if (caught) return;

        // Returning to home position - this drains before anything else.
        if (returning)
        {
            UpdateReturning();
            return;
        }

        // Wake-up at wakeThreshold (but not yet chasing)
        if (!wakeAnimTriggered && kitchenTimer != null
            && kitchenTimer.timerRunning
            && kitchenTimer.timeRemaining <= wakeThreshold)
        {
            TriggerWakeUp();
            timerWoke = true;
        }

        //  Chip-bag escalation: set off on the scripted route once armed
        //    and the clock is low enough 
        if (chipEscalationArmed && !scriptedPatrolConsumed && !scriptedPatrolActive
            && !forceTimerChase && !timerChaseActive)
        {
            bool clockLowEnough = kitchenTimer != null && kitchenTimer.timerRunning
                                  && kitchenTimer.timeRemaining <= scriptedPatrolTimerThreshold;
            if (clockLowEnough)
            {
                StartScriptedPatrol();
            }
            else
            {
                // Armed but still waiting on the clock - log every few
                // seconds so it's obvious in the Console that this is just
                // waiting, not stuck. (~110s from a 120s timer start before
                // this actually fires, at the default 10s threshold.)
                chipEscalationStatusLogTimer += Time.deltaTime;
                if (chipEscalationStatusLogTimer >= 3f)
                {
                    chipEscalationStatusLogTimer = 0f;
                    float remaining = kitchenTimer != null ? kitchenTimer.timeRemaining : -1f;
                    Debug.Log($"Edgar is armed and waiting - kitchen timer at {remaining:0.0}s, " +
                              $"needs to reach {scriptedPatrolTimerThreshold:0.0}s before he sets off.");
                }
            }
        }

        // Kitchen-timer chase (highest priority): overridden once chip-bag escalation is armed.
        bool shouldTimerChase = forceTimerChase
                                || (!chipEscalationArmed
                                    && kitchenTimer != null
                                    && kitchenTimer.timerRunning
                                    && kitchenTimer.timeRemaining <= chaseThreshold);

        if (shouldTimerChase)
        {
            // A 3rd+ crunch's forced chase always wins over the scripted route.
            scriptedPatrolActive = false;
            scriptedPatrolChasing = false;

            if (!timerChaseActive)
            {
                timerChaseActive = true;
                patrolSightChaseActive = false;   // timer-chase overrides
                hasLastKnownPos = false;
                timerChaseLostSightTimer = 0f;
                Debug.Log("Edgar heard the TV glitch and is coming for Bandit!");
            }
            UpdateTimerChase();
            return;
        }

        // Timer-chase is off - clear the flag.
        timerChaseActive = false;
        SetChasingAnim(false);

        // Chip-bag scripted route in progress
        if (scriptedPatrolActive)
        {
            UpdateScriptedPatrol();
            return;
        }

        // Investigating a noise / Bandit's last known position
        if (investigating)
        {
            UpdateInvestigating();
            return;
        }

        // Still sitting on the sofa - watching for Bandit
        if (!patrolEnabled)
        {
            UpdateSofaWatch();
            return;
        }

        // Patrol or patrol-sight chase
        if (patrolSightChaseActive)
        {
            UpdatePatrolSightChase();
        }
        else
        {
            // Accumulate sight time while walking; switch to chase once confirmed.
            CheckPatrolVision();
            base.Update(); // walk the waypoints normally
        }
    }

    // Wake-up helper
    // Plays the stand-up animation and starts the TV flicker effect.
    // Safe to call multiple times - only fires once.
    private void TriggerWakeUp()
    {
        if (wakeAnimTriggered) return;
        wakeAnimTriggered = true;

        // Stand-up animation: the Animator Controller needs a "StandUp" trigger
        // wired to transition from the Sitting state.
        if (animator != null)
            animator.SetTrigger("StandUp");

        Debug.Log("Edgar woke up! (stand-up animation triggered)");
    }

    // Kitchen-timer chase
    private void UpdateTimerChase()
    {
        bool canSee = player != null && !HidingSpot.IsHiddenFrom(transform.position, "Edgar");

        if (canSee)
        {
            lastKnownPlayerPos = new Vector3(player.position.x,
                                             transform.position.y,
                                             player.position.z);
            hasLastKnownPos = true;
            timerChaseLostSightTimer = 0f;
        }
        else
        {
            // Lost sight - head to last known position and wait.
            SearchAndWait();
            return;
        }

        Vector3 targetPos = lastKnownPlayerPos;
        float dist = Vector3.Distance(transform.position, targetPos);
        bool canCatch = (kitchenTimer == null || kitchenTimer.timeRemaining <= catchThreshold)
                        || forceTimerChase; // chip-bag wake: can catch right away

        if (canCatch && dist <= CatchReach)
        {
            CatchPlayer();
            return;
        }

        SetChasingAnim(true);

        // Distance-based speed: speed = dist / timeRemaining guarantees Edgar arrives at exactly the moment the timer hits zero.
        float timeLeft = kitchenTimer != null ? kitchenTimer.timeRemaining : 0f;
        float speed;
        if (timeLeft > 0.1f && dist > 0.01f)
            speed = Mathf.Min(dist / timeLeft, maxChaseSpeed);
        else
            speed = chaseSpeed;

        StepTowards(targetPos, speed);
    }

    // Walks Edgar to the last-known position; starts returning if the
    // interest timer runs out.  Used only for the kitchen-timer chase.
    private void SearchAndWait()
    {
        if (!hasLastKnownPos)
        {
            SetChasingAnim(false);
            return;
        }

        float dist = Vector3.Distance(transform.position, lastKnownPlayerPos);
        if (dist > 0.2f)
        {
            SetChasingAnim(true);
            StepTowards(lastKnownPlayerPos, chaseSpeed);
        }
        else
        {
            SetChasingAnim(false);
            timerChaseLostSightTimer += Time.deltaTime;
            if (timerChaseLostSightTimer >= loseInterestDelay)
            {
                BeginReturning();
            }
        }
    }

    // Patrol vision
    // Called every frame while Edgar is patrolling (not yet in sight-chase).
    private void CheckPatrolVision()
    {
        if (!CanSeePlayerOnPatrol())
        {
            sightTimer = 0f; // look away -> reset the confirm window
            return;
        }

        sightTimer += Time.deltaTime;
        if (sightTimer >= sightConfirmTime)
        {
            // Confirmed - switch to sight-based chase.
            patrolSightChaseActive = true;
            hasLastKnownPos = false;
            patrolLostSightTimer = 0f;
            sightTimer = 0f;
            Debug.Log("Edgar spotted Bandit while patrolling - chasing!");
            RaiseAlertLevel();
        }
    }

    // Patrol-sight chase
    private void UpdatePatrolSightChase()
    {
        bool canSee = CanSeePlayerOnPatrol();

        if (canSee)
        {
            lastKnownPlayerPos = new Vector3(player.position.x,
                                             transform.position.y,
                                             player.position.z);
            hasLastKnownPos = true;
            patrolLostSightTimer = 0f;
        }
        else
        {
            patrolLostSightTimer += Time.deltaTime;

            // Lost sight of him (for more than a blink) - go and search
            // where he was last seen, then look around (Investigating state).
            if (patrolLostSightTimer >= lostSightGrace && hasLastKnownPos)
            {
                bool backToSofa = spottedFromSofa;
                spottedFromSofa = false;
                patrolSightChaseActive = false;
                sightTimer = 0f;
                patrolLostSightTimer = 0f;
                SetChasingAnim(false);
                if (backToSofa) patrolEnabled = false;
                StartInvestigation(lastKnownPlayerPos, backToSofa, null);
                hasLastKnownPos = false;
                return;
            }

            if (patrolLostSightTimer >= loseInterestDelay)
            {
                // Lost him for too long - give up and go back to patrol.
                patrolSightChaseActive = false;
                sightTimer = 0f;
                patrolLostSightTimer = 0f;
                hasLastKnownPos = false;
                SetChasingAnim(false);
                if (spottedFromSofa)
                {
                    // He only got up because he noticed Bandit - back to the sofa.
                    spottedFromSofa = false;
                    patrolEnabled = false;
                    BeginReturning();
                    return;
                }
                Debug.Log("Edgar lost sight of Bandit and is returning to patrol.");
                return;
            }
        }

        // No known position yet (shouldn't happen, but guard anyway).
        if (!hasLastKnownPos)
        {
            SetChasingAnim(false);
            return;
        }

        Vector3 targetPos = lastKnownPlayerPos;
        float dist = Vector3.Distance(transform.position, targetPos);

        // During patrol-sight chase Edgar can always catch (no catchThreshold
        // time guard - he's not going to wave hello and walk away).
        if (dist <= CatchReach)
        {
            CatchPlayer();
            return;
        }

        SetChasingAnim(true);
        StepTowards(targetPos, chaseSpeed);
    }

    // Chip-bag scripted route (sofa -> Whiskers -> fridge -> backdoor -> sofa)

    private void StartScriptedPatrol()
    {
        scriptedPatrolActive = true;
        scriptedPatrolConsumed = true; // this route only ever runs once per attempt
        scriptedPatrolChasing = false;
        scriptedWaypointIndex = 0;
        scriptedSightTimer = 0f;
        Debug.Log("Edgar is up and doing his rounds: sofa -> Whiskers -> fridge -> backdoor.");
    }

    // Walks the route.
    private void UpdateScriptedPatrol()
    {
        if (scriptedPatrolChasing)
        {
            UpdateScriptedChase();
            return;
        }

        if (CanSeePlayerOnPatrol())
        {
            scriptedSightTimer += Time.deltaTime;
            if (scriptedSightTimer >= sightConfirmTime)
            {
                scriptedPatrolChasing = true;
                lastKnownPlayerPos = new Vector3(player.position.x, transform.position.y, player.position.z);
                hasLastKnownPos = true;
                Debug.Log("Edgar spotted Bandit during his rounds - going after him!");
            RaiseAlertLevel();
                UpdateScriptedChase();
                return;
            }
        }
        else
        {
            scriptedSightTimer = 0f;
        }

        if (chipEscalationWaypoints == null || chipEscalationWaypoints.Length == 0)
        {
            Debug.LogWarning("HumanNPC: chipEscalationWaypoints isn't set up - Edgar has nowhere to " +
                              "patrol to. Assign the Whiskers/fridge/backdoor waypoints in the Inspector.", this);
            scriptedPatrolActive = false;
            BeginReturning();
            return;
        }

        if (scriptedWaypointIndex >= chipEscalationWaypoints.Length)
        {
            // Made it all the way round without spotting Bandit - go home.
            scriptedPatrolActive = false;
            Debug.Log("Edgar didn't see Bandit on his rounds - heading back to the sofa.");
            BeginReturning();
            return;
        }

        Transform target = chipEscalationWaypoints[scriptedWaypointIndex];
        if (target == null)
        {
            Debug.LogWarning($"HumanNPC: chipEscalationWaypoints[{scriptedWaypointIndex}] isn't " +
                              "assigned in the Inspector - skipping it.", this);
            scriptedWaypointIndex++;
            return;
        }

        StepTowards(target.position, scriptedPatrolSpeed);

        if (Vector3.Distance(transform.position, target.position) < 0.15f)
        {
            scriptedWaypointIndex++;
        }
    }

    // Edgar breaks off his route and goes straight for Bandit's last-seen
    // position. Catching him here is an instant, unconditional life loss -
    // he saw him, that's it, no catchThreshold time gate like the kitchen-
    // timer chase has.
    private void UpdateScriptedChase()
    {
        bool canSee = CanSeePlayerOnPatrol();

        if (canSee)
        {
            lastKnownPlayerPos = new Vector3(player.position.x, transform.position.y, player.position.z);
            hasLastKnownPos = true;
        }

        if (!hasLastKnownPos)
        {
            scriptedPatrolChasing = false;
            return;
        }

        Vector3 targetPos = lastKnownPlayerPos;
        float dist = Vector3.Distance(transform.position, targetPos);

        if (dist <= CatchReach)
        {
            CatchPlayerScripted();
            return;
        }

        StepTowards(targetPos, scriptedPatrolSpeed);
    }

    // Dedicated catch for the chip-bag scripted route.
    private void CatchPlayerScripted()
    {
        caught = true;
        // Stop the kitchen clock the instant Edgar actually catches
        // Bandit - otherwise it can keep ticking down through the grab/
        // freeze sequence below and independently hit zero, costing a
        // SECOND life on top of this catch for the same encounter.
        if (kitchenTimer != null) kitchenTimer.StopTimer();
        scriptedPatrolActive = false;
        scriptedPatrolChasing = false;

        Debug.Log("Edgar caught Bandit during his chip-bag rounds!");
        Voice.Say("edgar_gotcha", "GOTCHA!", 3f, true);

        bool wasLastLife = GameManager.Instance == null || GameManager.Instance.currentLives <= 1;

        ShowCaughtByEdgarBanner();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.pendingLifeLostMessage = scriptedCatchMessage;
            // RestartSceneOnLifeLoss already freezes for its own base
            // freezeDuration (1s in this scene) before reloading - top it up
            // so the message stays on screen for about 2 seconds total.
            GameManager.Instance.pendingFreezeDurationBonus = scriptedCatchFreezeBonus;
        }

        StartGrab(() =>
        {
            if (GameManager.Instance != null) GameManager.Instance.LoseLife();
            if (wasLastLife && timeUpScreen != null) timeUpScreen.OnTimeUp();
        });
    }

    // Shared helpers

    // Line-of-sight + FOV check for patrol vision.
    private bool CanSeePlayerOnPatrol()
    {
        if (player == null || HidingSpot.IsHiddenFrom(transform.position, "Edgar")) return false;

        float dist = Vector3.Distance(transform.position, player.position);
        // Right next to him Edgar notices Bandit even without looking (hears/feels him).
        float sense = senseRadius;
        if (investigating || onHighAlert) sense *= 1.75f;   // he's on edge: hears Bandit from further away, even behind him
        if (playerController == null) playerController = player.GetComponent<PlayerController>();
        if (playerController != null && playerController.IsCrouching()) sense *= 0.6f;
        if (dist <= sense) return true;
        if (dist > patrolVisionRange) return false;

        Vector3 dirToPlayer = (player.position - transform.position).normalized;
        float angle = Vector3.Angle(transform.forward, dirToPlayer);
        if (angle > patrolVisionAngle) return false;

        // Linecast from roughly eye-height (1.5 m) down to Bandit's centre (0.5 m up).
        Vector3 eyePos = transform.position + Vector3.up * 1.5f;
        Vector3 targetPos = player.position + Vector3.up * 0.5f;
        if (Physics.Linecast(eyePos, targetPos, out RaycastHit hit))
        {
            if (hit.transform != player && !hit.transform.IsChildOf(player))
                return false; // something solid is blocking the view
        }

        return true;
    }

    private void BeginReturning()
    {
        timerChaseActive = false;
        hasLastKnownPos = false;
        returning = true;
        SetChasingAnim(false);
        Debug.Log("Edgar lost track of Bandit and is heading back to where he started.");
    }

    private void UpdateReturning()
    {
        goingHome = true;          // only here may he step off the walkable area (onto his sofa seat)
        StepTowards(homePosition, chaseSpeed);
        goingHome = false;

        if (Vector3.Distance(transform.position, homePosition) < 0.15f)
        {
            transform.position = homePosition;
            transform.rotation = homeRotation;
            returning = false;
        }
    }

    private void CatchPlayer()
    {
        caught = true;
        // Stop the kitchen clock the instant Edgar actually catches
        // Bandit - otherwise it can keep ticking down through the grab/
        // freeze sequence below and independently hit zero, costing a
        // SECOND life on top of this catch for the same encounter.
        if (kitchenTimer != null) kitchenTimer.StopTimer();
        SetChasingAnim(false);

        // Play the catch animation - requires a "Catch" trigger in Edgar's
        // Animator Controller.
        if (animator != null)
            animator.SetTrigger("Catch");

        Debug.Log("Edgar caught Bandit!");
        Voice.Say("edgar_gotcha", "GOTCHA!", 3f, true);

        bool wasLastLife = GameManager.Instance == null || GameManager.Instance.currentLives <= 1;

        ShowCaughtByEdgarBanner();

        if (!wasLastLife && GameManager.Instance != null)
        {
            GameManager.Instance.pendingLifeLostMessage =
                "Edgar spotted you and nearly grabbed you! You got away this time...\n\n" +
                "Grab whatever food you still need and get out of here - hurry!";
            GameManager.Instance.pendingFreezeDurationBonus = 3f;
        }

        StartGrab(() =>
        {
            if (GameManager.Instance != null) GameManager.Instance.LoseLife();
            if (wasLastLife && timeUpScreen != null) timeUpScreen.OnTimeUp();
        });
    }

    // Grabbing Bandit
    // Edgar leans over, reaches out with both arms, scoops Bandit up and
    // holds him up in front of him (Bandit wriggling) - then the life is lost.
    [Header("Grab animation")]
    [SerializeField] private float grabHoldTime = 1.4f;
    private bool grabbing;
    private float grabT;
    private Vector3 grabStart;
    private Transform grabLLow, grabRLow;
    private Quaternion grabFacing;
    private Transform grabSpine, grabChest, grabLArm, grabRArm, grabLHand, grabRHand, banditBody;
    private Quaternion banditBodyRest;

    private void StartGrab(System.Action whenDone)
    {
        if (player == null) { whenDone(); return; }
        foreach (Transform t in GetComponentsInChildren<Transform>())
        {
            switch (t.name)
            {
                case "Spine": grabSpine = t; break;
                case "Chest": grabChest = t; break;
                case "LeftUpperArm": grabLArm = t; break;
                case "RightUpperArm": grabRArm = t; break;
                case "LeftLowerArm": grabLLow = t; break;
                case "RightLowerArm": grabRLow = t; break;
                case "LeftHand": grabLHand = t; break;
                case "RightHand": grabRHand = t; break;
            }
        }
        foreach (Transform t in player.GetComponentsInChildren<Transform>())
            if (t.name == "Raccoon_Root") { banditBody = t; banditBodyRest = t.localRotation; break; }

        PlayerController pc = player.GetComponent<PlayerController>();
        if (pc != null) pc.enabled = false;
        CharacterController cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        Vector3 look = player.position - transform.position; look.y = 0f;
        if (look.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(look);
        grabFacing = transform.rotation;
        if (modelRoot != null) modelRoot.localRotation = Quaternion.identity;
        SoundFX.Play("grab", 0.9f);
        DangerFeedback.Hit();

        grabStart = player.position;
        grabLast = player.position;
        grabT = 0f;
        grabbing = true;
        BeginCatchCamera();
        StartCoroutine(GrabThen(whenDone));
    }

    private IEnumerator GrabThen(System.Action whenDone)
    {
        yield return new WaitForSeconds(0.8f + grabHoldTime);
        whenDone();
    }

    // Runs after the animation each frame to bend the grab-pose bones.
    private void ApplyGrabPose()
    {
        if (!grabbing || player == null) return;
        if (Time.deltaTime <= 0f) return;   // game frozen: the pose stays as it is (don't bend the bones again)
        grabT += Time.deltaTime;
        transform.rotation = grabFacing;   // keep facing Bandit for the whole pickup
        float reach = Mathf.SmoothStep(0f, 1f, grabT / 0.35f);
        Vector3 right = transform.right;
        if (grabSpine != null) grabSpine.rotation = Quaternion.AngleAxis(18f * reach, right) * grabSpine.rotation;
        if (grabChest != null) grabChest.rotation = Quaternion.AngleAxis(12f * reach, right) * grabChest.rotation;
        if (grabLArm != null) grabLArm.rotation = Quaternion.AngleAxis(-75f * reach, right) * grabLArm.rotation;
        if (grabRArm != null) grabRArm.rotation = Quaternion.AngleAxis(-75f * reach, right) * grabRArm.rotation;

        // Bandit is scooped up into Edgar's hands and wriggles.
        float lift = Mathf.SmoothStep(0f, 1f, (grabT - 0.3f) / 0.5f);
        Vector3 hands = transform.position + transform.forward * 0.6f + Vector3.up * 1.2f;
        if (grabLHand != null && grabRHand != null) hands = (grabLHand.position + grabRHand.position) * 0.5f;
        Vector3 target = hands + Vector3.down * 0.35f + transform.forward * 0.1f;
        Vector3 want = Vector3.Lerp(grabStart, target, lift);
        player.position = KeepBanditClear(transform.position + Vector3.up * 1.0f, want);
        grabLast = player.position;
        if (banditBody != null)
            banditBody.localRotation = banditBodyRest * Quaternion.Euler(Mathf.Sin(grabT * 22f) * 12f * lift, 0f, Mathf.Sin(grabT * 17f) * 15f * lift);
    }

    // Catch camera: frozen on Edgar's face
    private Vector3 grabLast;
    private Transform catchCam, edgarHead;
    private PT_MouseLook catchMouseLook;
    private CameraCollision catchCamCollision;

    private void BeginCatchCamera()
    {
        Camera cam = player.GetComponentInChildren<Camera>();
        if (cam == null) cam = Camera.main;
        if (cam == null) return;
        catchCam = cam.transform;
        catchMouseLook = cam.GetComponent<PT_MouseLook>();
        catchCamCollision = cam.GetComponent<CameraCollision>();
        if (catchMouseLook != null) catchMouseLook.enabled = false;
        if (catchCamCollision != null) catchCamCollision.enabled = false;
        foreach (Transform t in GetComponentsInChildren<Transform>())
            if (t.name == "Head") { edgarHead = t; break; }
        ApplyCatchCamera();
    }

    [Tooltip("Catch camera distance from Edgar's face (1 = default, higher = further back).")]
    [SerializeField] private float catchCamZoom = 1f;

    private void ApplyCatchCamera()
    {
        if (!grabbing || catchCam == null) return;

        // Bandit faces Edgar while he's held.
        Vector3 toEdgar = transform.position - player.position; toEdgar.y = 0f;
        if (toEdgar.sqrMagnitude < 0.001f) toEdgar = -transform.forward;
        toEdgar.Normalize();
        player.rotation = Quaternion.LookRotation(toEdgar, Vector3.up);

        // The camera stands in front of Edgar at his face height, looking into his face and upper body,
        // with Bandit held up in his hands in the lower part of the shot.
        Vector3 face = edgarHead != null ? edgarHead.position : transform.position + Vector3.up * 1.6f;
        float h = Mathf.Max(0.5f, face.y - transform.position.y);
        Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 side = transform.right; side.y = 0f; side.Normalize();
        Vector3 hands = transform.position + fwd * 0.6f + Vector3.up * (h * 0.75f);
        if (grabLHand != null && grabRHand != null) hands = (grabLHand.position + grabRHand.position) * 0.5f;
        Vector3 lookAt = Vector3.Lerp(hands, face, 0.6f);

        // Slightly off to one side so Bandit doesn't cover his face; use whichever side has more room.
        Vector3 bestCam = face + fwd * 0.6f; float bestDist = -1f;
        for (int k = 0; k < 2; k++)
        {
            Vector3 cand = face + fwd * (h * 0.95f * catchCamZoom) + side * (k == 0 ? 0.2f : -0.2f) * h + Vector3.up * (0.05f * h);
            Vector3 dir = cand - face;
            float full = dir.magnitude, dist = full;
            foreach (RaycastHit hit in Physics.RaycastAll(face, dir / full, full, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(transform) || hit.transform.IsChildOf(player)) continue;
                dist = Mathf.Min(dist, Mathf.Max(0.6f, hit.distance - 0.15f));
            }
            if (dist > bestDist + 0.01f) { bestDist = dist; bestCam = face + dir / full * dist; }
        }
        catchCam.position = bestCam;
        catchCam.rotation = Quaternion.LookRotation(lookAt - bestCam, Vector3.up);
    }

    // Keeps Bandit from going through the floor, walls or furniture while Edgar holds him.
    private bool IsGrabObstacle(Collider c)
    {
        if (c == null || c.isTrigger) return false;
        if (c.transform.IsChildOf(transform) || c.transform.IsChildOf(player)) return false;
        return true;
    }

    private Vector3 KeepBanditClear(Vector3 from, Vector3 want)
    {
        Vector3 lift = Vector3.up * 0.3f;
        Vector3 d = want - from;
        float len = d.magnitude;
        if (len > 0.001f)
        {
            Vector3 dn = d / len;
            float best = len;
            foreach (RaycastHit h in Physics.SphereCastAll(from + lift, 0.2f, dn, len, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!IsGrabObstacle(h.collider) || h.distance <= 0.0001f) continue;
                best = Mathf.Min(best, Mathf.Max(0f, h.distance - 0.02f));
            }
            want = from + dn * best;
        }

        // Floor: keep the lowest point of his body above the ground under him.
        float footOffset = 0f;
        Renderer[] rs = player.GetComponentsInChildren<Renderer>();
        if (rs.Length > 0)
        {
            float low = float.MaxValue;
            foreach (Renderer r in rs) if (r.enabled && !(r is ParticleSystemRenderer)) low = Mathf.Min(low, r.bounds.min.y);
            if (low < float.MaxValue) footOffset = player.position.y - low;
        }
        float groundY = float.NegativeInfinity;
        foreach (RaycastHit h in Physics.RaycastAll(want + Vector3.up * 1.5f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore))
            if (IsGrabObstacle(h.collider) && h.point.y > groundY && h.point.y < want.y + 1.5f) groundY = h.point.y;
        if (groundY > float.NegativeInfinity)
            want.y = Mathf.Max(want.y, groundY + footOffset + 0.03f);
        return want;
    }


    // The shoes were skinned loosely, so they slid along the ground while his feet moved. Tie every shoe/sole vertex
    // to the nearest foot bone so each shoe lifts, swings and lands together with its foot.
    private void FixShoes()
    {
        SkinnedMeshRenderer smr = null;
        foreach (SkinnedMeshRenderer r in modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            foreach (Material m in r.sharedMaterials)
                if (m != null && (m.name.ToLower().Contains("shoe") || m.name.ToLower().Contains("sole"))) { smr = r; break; }
            if (smr != null) break;
        }
        if (smr == null || smr.sharedMesh == null) return;

        Mesh src = smr.sharedMesh;
        if (!src.isReadable) { Debug.LogWarning("Edgar: body mesh isn't readable, can't fix the shoes."); return; }

        Transform[] bones = smr.bones;
        int left = -1, right = -1;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null) continue;
            if (bones[i].name == "LeftFoot") left = i;
            else if (bones[i].name == "RightFoot") right = i;
        }
        if (left < 0 || right < 0) { Debug.LogWarning("Edgar: couldn't find his foot bones to fix the shoes."); return; }

        Mesh mesh = Instantiate(src);
        Matrix4x4[] bind = mesh.bindposes;
        Vector3 leftPos = bind[left].inverse.MultiplyPoint3x4(Vector3.zero);
        Vector3 rightPos = bind[right].inverse.MultiplyPoint3x4(Vector3.zero);
        Vector3[] verts = mesh.vertices;
        BoneWeight[] weights = mesh.boneWeights;

        Material[] mats = smr.sharedMaterials;
        for (int sub = 0; sub < mesh.subMeshCount && sub < mats.Length; sub++)
        {
            if (mats[sub] == null) continue;
            string n = mats[sub].name.ToLower();
            if (!n.Contains("shoe") && !n.Contains("sole")) continue;

            foreach (int v in mesh.GetTriangles(sub))
            {
                bool isLeft = (verts[v] - leftPos).sqrMagnitude <= (verts[v] - rightPos).sqrMagnitude;
                BoneWeight bw = new BoneWeight();
                bw.boneIndex0 = isLeft ? left : right;
                bw.weight0 = 1f;
                weights[v] = bw;
            }
        }

        mesh.boneWeights = weights;
        smr.sharedMesh = mesh;
    }

    // Seated arms
    [Header("Seated arms")]
    [Tooltip("How far the upper arms swing forward while he sits (degrees).")]
    [SerializeField] private float seatedUpperArmForward = 20f;
    [Tooltip("How far the forearms bend forward while he sits (degrees), so his hands rest toward his lap.")]
    [SerializeField] private float seatedForearmBend = 55f;

    private static readonly string[] ArmBoneNames =
        { "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand" };
    private Transform[] armBones;
    private Quaternion[] standingArm;

    // Reads how his arms hang when he is standing/walking (from his idle clip), so the sitting pose can reuse it.
    private void CaptureStandingArms()
    {
        if (modelRoot == null || animator == null || animator.runtimeAnimatorController == null) return;
        armBones = new Transform[ArmBoneNames.Length];
        foreach (Transform t in modelRoot.GetComponentsInChildren<Transform>(true))
            for (int i = 0; i < ArmBoneNames.Length; i++)
                if (t.name == ArmBoneNames[i]) armBones[i] = t;
        foreach (Transform b in armBones) if (b == null) { armBones = null; return; }

        AnimationClip pick = null;
        foreach (AnimationClip c in animator.runtimeAnimatorController.animationClips)
        {
            string n = c.name.ToLowerInvariant();
            if (n.Contains("idle") && !n.Contains("sit")) { pick = c; break; }
        }
        if (pick == null)
            foreach (AnimationClip c in animator.runtimeAnimatorController.animationClips)
                if (c.name.ToLowerInvariant().Contains("walk")) { pick = c; break; }
        if (pick == null) { armBones = null; return; }

        try { pick.SampleAnimation(animator.gameObject, 0f); }
        catch (System.Exception) { armBones = null; return; }
        standingArm = new Quaternion[armBones.Length];
        for (int i = 0; i < armBones.Length; i++) standingArm[i] = armBones[i].localRotation;
    }

    // While he sits, his arms take the same relaxed hang as when he stands, then swing forward so his hands rest in his lap.
    private void PoseSeatedArms()
    {
        if (armBones == null || standingArm == null || grabbing || caught) return;
        float w = seatBlend;
        if (w <= 0.01f) return;
        for (int i = 0; i < armBones.Length; i++)
            armBones[i].localRotation = Quaternion.Slerp(armBones[i].localRotation, standingArm[i], w);

        Vector3 right = transform.right;
        for (int side = 0; side < 2; side++)
        {
            Transform upper = armBones[side * 3], lower = armBones[side * 3 + 1];
            upper.rotation = Quaternion.AngleAxis(-seatedUpperArmForward * w, right) * upper.rotation;
            lower.rotation = Quaternion.AngleAxis(-seatedForearmBend * w, right) * lower.rotation;
        }
    }

    // Animation driver
    // Picks Edgar's animation from what he's actually doing: sitting on the
    // sofa until something gets him up, standing up / sitting down in
    // between, and idle/patrol/walk blended by his real movement speed.
    private bool hasSpeedParam;
    private Vector3 lastAnimPos;
    private float animSpeed;
    private bool animSeated = true;
    private Coroutine poseRoutine;

    // Voice
    private bool voiceWasChasing, sofaHuhSaid;
    private NpcVoice voice;
    private NpcVoice Voice => voice != null ? voice : (voice = NpcVoice.For(this));

    private void UpdateVoice()
    {
        bool chasing = patrolSightChaseActive || scriptedPatrolChasing || forceTimerChase || timerChaseActive;
        if (chasing && !voiceWasChasing && !caught) Voice.Say("edgar_hey", "HEY!", 2.5f, true);
        voiceWasChasing = chasing;

        // "?" starting to fill on the sofa.
        if (!chasing && !investigating && suspicion > 0.3f && !sofaHuhSaid)
        {
            sofaHuhSaid = true;
            Voice.Say("edgar_huh", "Hm? What's that?");
        }
        if (suspicion <= 0.01f && !investigating) sofaHuhSaid = false;
    }

    private void LateUpdate()
    {
        UpdateVoice();
        ApplyGrabPose();
        ApplyCatchCamera();
        if (animator == null || !hasSpeedParam) return;
        FitToSeat();
        PoseSeatedArms();

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 delta = transform.position - lastAnimPos;
        delta.y = 0f;
        lastAnimPos = transform.position;
        animSpeed = Mathf.Lerp(animSpeed, delta.magnitude / dt, dt * 8f);
        animator.SetFloat("Speed", animSpeed);

        bool atHome = Vector3.Distance(transform.position, homePosition) < 0.3f;
        bool wantSeated = !timerWoke && !caught && !investigating && atHome && !returning
                          && !patrolEnabled && !timerChaseActive && !scriptedPatrolActive
                          && !patrolSightChaseActive;

        if (wantSeated != animSeated)
        {
            animSeated = wantSeated;
            if (poseRoutine != null) StopCoroutine(poseRoutine);
            poseRoutine = StartCoroutine(wantSeated ? SitDownRoutine() : StandUpRoutine());
        }
    }

    // Path-finding
    // On Start a navigation mesh is built from the House's colliders (floor,
    // walls, furniture), and every movement step follows a proper path
    // around obstacles and through doorways, rather than a straight line
    // through walls and furniture.
    [Header("Path-finding")]
    [Tooltip("How close Edgar can get to walls/furniture on his paths. Keep small enough that he fits through doorways.")]
    [SerializeField] private float navAgentRadius = 0.4f;
    [Tooltip("Right next to Bandit (this close) Edgar notices him even without looking.")]
    [SerializeField] private float senseRadius = 2f;

    private bool navReady;
    private UnityEngine.AI.NavMeshDataInstance navInstance;
    private UnityEngine.AI.NavMeshPath navPath;
    private Vector3[] navCorners = new Vector3[0];
    private bool goingHome;     // returning to the sofa: may step off the walkable area onto the seat
    private bool atPathEnd;     // reached the last walkable point of his current path
    private int navCornerIndex;
    private Vector3 navTarget = new Vector3(float.NaN, 0f, 0f);
    private float navRepathTime;

    private void BuildNavigation()
    {
        var settings = UnityEngine.AI.NavMesh.GetSettingsByID(0);
        // As wide as his actual body, so the path keeps ALL of him clear of
        // counters and cupboards (a thinner path let his shoulders clip in).
        settings.agentRadius = Mathf.Clamp(Mathf.Max(navAgentRadius, EdgarBodyRadius * 0.9f) + 0.1f, 0.3f, 0.8f);
        settings.agentHeight = 2f;
        settings.agentClimb = 0.4f;
        settings.agentSlope = 30f;

        Vector3 centre = transform.position;
        Bounds area = new Bounds(centre, new Vector3(160f, 30f, 160f));

        var sources = new System.Collections.Generic.List<UnityEngine.AI.NavMeshBuildSource>();
        UnityEngine.AI.NavMeshBuilder.CollectSources(area, ~0, UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders, 0,
            new System.Collections.Generic.List<UnityEngine.AI.NavMeshBuildMarkup>(), sources);

        // Leave out anything that moves or isn't solid: characters, food, triggers.
        CatNPC cat = CatNPC.Instance != null ? CatNPC.Instance : FindFirstObjectByType<CatNPC>();
        sources.RemoveAll(src =>
        {
            Component c = src.component;
            if (c == null) return false;
            Collider col = c as Collider;
            if (col != null && (col.isTrigger || !col.enabled)) return true;
            Transform t = c.transform;
            if (t.IsChildOf(transform)) return true;
            if (player != null && t.IsChildOf(player)) return true;
            if (cat != null && t.IsChildOf(cat.transform)) return true;
            if (t.GetComponentInParent<FoodItem>() != null) return true;
            Rigidbody rb = t.GetComponentInParent<Rigidbody>();
            if (rb != null && !rb.isKinematic) return true;
            return false;
        });

        // Tables, chairs, boxes, the chip packet... some only have a model and no collider.
        int added = 0;
        foreach (MeshRenderer mr in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (!mr.enabled || !mr.gameObject.activeInHierarchy) continue;
            Transform t = mr.transform;
            if (t.IsChildOf(transform)) continue;
            if (player != null && t.IsChildOf(player)) continue;
            if (cat != null && t.IsChildOf(cat.transform)) continue;
            if (t.GetComponentInParent<FoodItem>() != null) continue;
            if (t.GetComponentInParent<Canvas>() != null) continue;
            Rigidbody rb = t.GetComponentInParent<Rigidbody>();
            if (rb != null && !rb.isKinematic) continue;
            MeshFilter mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            if (!area.Intersects(mr.bounds)) continue;
            if (HasSolidCollider(t, mr.bounds)) continue;   // already covered exactly
            if (IsOpeningOrSurface(t)) continue;           // doorways, walls, floors, rugs

            Bounds lb = mf.sharedMesh.bounds;
            Vector3 world = Vector3.Scale(lb.size, t.lossyScale);
            world = new Vector3(Mathf.Abs(world.x), Mathf.Abs(world.y), Mathf.Abs(world.z));
            Bounds wb = mr.bounds;
            // Skip whole rooms/walls/floors (big), flat things like rugs, and
            // things hanging above head height.
            if (wb.size.x > 5f || wb.size.z > 5f || wb.size.y > 4f) continue;
            if (wb.size.y < 0.08f) continue;
            if (wb.min.y > transform.position.y + 1.9f) continue;

            var box = new UnityEngine.AI.NavMeshBuildSource
            {
                shape = UnityEngine.AI.NavMeshBuildSourceShape.Box,
                size = world,
                transform = Matrix4x4.TRS(t.TransformPoint(lb.center), t.rotation, Vector3.one),
                area = 0
            };
            sources.Add(box);
            added++;
        }

        var data = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(settings, sources, area, Vector3.zero, Quaternion.identity);
        if (data == null) { Debug.LogWarning("Edgar: couldn't build a navigation mesh - he'll walk in straight lines."); return; }
        navInstance = UnityEngine.AI.NavMesh.AddNavMeshData(data);
        navPath = new UnityEngine.AI.NavMeshPath();
        navReady = true;
        Debug.Log("Edgar: navigation mesh built from " + (sources.Count - added) + " colliders + " + added + " furniture/prop shapes - he now walks around walls and furniture.");
    }

    // Already has a real collider (on itself, or a parent covering it)?
    private static bool HasSolidCollider(Transform t, Bounds rb)
    {
        foreach (Collider c in t.GetComponents<Collider>())
            if (c.enabled && !c.isTrigger) return true;
        for (Transform p = t.parent; p != null; p = p.parent)
            foreach (Collider c in p.GetComponents<Collider>())
                if (c.enabled && !c.isTrigger && c.bounds.Intersects(rb)) return true;
        return false;
    }

    // A box around these would block the gap they frame, or they're
    // something you walk on.
    private static readonly string[] OpenNames = { "door", "frame", "arch", "window", "wall", "floor", "rug", "carpet", "curtain", "ceiling", "stair" };
    private static bool IsOpeningOrSurface(Transform t)
    {
        for (Transform p = t; p != null; p = p.parent)
        {
            string n = p.name.ToLowerInvariant();
            if (n.Contains("fridge")) return false;
            foreach (string o in OpenNames) if (n.Contains(o)) return true;
        }
        return false;
    }

    private void OnDestroy()
    {
        if (navReady) UnityEngine.AI.NavMesh.RemoveNavMeshData(navInstance);
    }

    // Edgar's body is big - he catches Bandit from arm's length instead of
    // walking into him, and never overlaps him.
    private float edgarBodyRadius = -1f;
    private float EdgarBodyRadius
    {
        get
        {
            if (edgarBodyRadius < 0f)
            {
                CapsuleCollider cap = GetComponent<CapsuleCollider>();
                edgarBodyRadius = cap != null ? cap.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z) : 0.6f;
            }
            return edgarBodyRadius;
        }
    }
    private const float BanditRadius = 0.3f;
    private float CatchReach => Mathf.Max(catchRange, EdgarBodyRadius + BanditRadius + 0.6f);

    private void KeepOutOfBandit()
    {
        if (player == null) return;
        Vector3 d = transform.position - player.position; d.y = 0f;
        float min = EdgarBodyRadius + BanditRadius + 0.1f;
        if (d.magnitude < min)
        {
            if (d.sqrMagnitude < 0.0001f) d = -transform.forward;
            Vector3 p2 = player.position + d.normalized * min;
            transform.position = new Vector3(p2.x, transform.position.y, p2.z);
        }
    }

    // Last line of defence: if his body ends up overlapping a wall, table or chair, shove him back out.
    [SerializeField] private bool pushOutOfFurniture = false;
    private CapsuleCollider ownCapsule;
    private void PushOutOfFurniture()
    {
        if (goingHome || animSeated || seatBlend > 0.01f) return;
        if (Vector3.Distance(transform.position, homePosition) < 1.6f) return;   // getting up from / sitting down on the sofa
        if (ownCapsule == null) ownCapsule = GetComponent<CapsuleCollider>();
        if (ownCapsule == null || !ownCapsule.enabled) return;

        Bounds b = ownCapsule.bounds;
        foreach (Collider c in Physics.OverlapBox(b.center, b.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c == ownCapsule || c.isTrigger || c.transform.IsChildOf(transform)) continue;
            if (player != null && c.transform.IsChildOf(player)) continue;
            if (c.bounds.max.y <= transform.position.y + 0.25f) continue;       // floor / rugs
            if (c is MeshCollider mc && !mc.convex) continue;                    // can't be measured; the nav mesh covers these
            Vector3 dir; float dist;
            if (Physics.ComputePenetration(ownCapsule, ownCapsule.transform.position, ownCapsule.transform.rotation,
                                           c, c.transform.position, c.transform.rotation, out dir, out dist))
            {
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f)
                    transform.position += dir.normalized * Mathf.Min(dist, 0.3f);
            }
        }
    }

    // Follows a path round obstacles instead of a straight line.
    protected override void StepTowards(Vector3 target, float speed)
    {
        StepTowardsInner(target, speed);
        KeepOutOfBandit();
        if (pushOutOfFurniture) PushOutOfFurniture();
    }

    private void StepTowardsInner(Vector3 target, float speed)
    {
        atPathEnd = false;
        if (!navReady)
        {
            base.StepTowards(target, speed);
            return;
        }

        bool targetMoved = float.IsNaN(navTarget.x) || (new Vector3(target.x - navTarget.x, 0f, target.z - navTarget.z)).sqrMagnitude > 0.25f;
        if (targetMoved || Time.time >= navRepathTime)
        {
            navTarget = target;
            navRepathTime = Time.time + 0.4f;
            UnityEngine.AI.NavMeshHit from, to;
            bool okFrom = UnityEngine.AI.NavMesh.SamplePosition(transform.position, out from, 3f, UnityEngine.AI.NavMesh.AllAreas);
            bool okTo = UnityEngine.AI.NavMesh.SamplePosition(target, out to, 4f, UnityEngine.AI.NavMesh.AllAreas);
            if (okFrom && okTo && UnityEngine.AI.NavMesh.CalculatePath(from.position, to.position, UnityEngine.AI.NavMesh.AllAreas, navPath)
                && navPath.corners.Length > 0)
            {
                navCorners = navPath.corners;
                navCornerIndex = navCorners.Length > 1 ? 1 : 0;
            }
            else
            {
                navCorners = new Vector3[0];
            }
        }

        Vector3 next = target;
        if (navCorners.Length > 0)
        {
            while (navCornerIndex < navCorners.Length - 1 &&
                   new Vector2(navCorners[navCornerIndex].x - transform.position.x, navCorners[navCornerIndex].z - transform.position.z).magnitude < 0.3f)
                navCornerIndex++;
            next = navCorners[Mathf.Min(navCornerIndex, navCorners.Length - 1)];
            // End of the path = the nearest spot he can actually stand. He stops
            // there (never walks on into the counter/cupboard where Bandit was),
            // except when going back to sit on his sofa.
            Vector3 last = navCorners[navCorners.Length - 1];
            atPathEnd = navCornerIndex >= navCorners.Length - 1 &&
                new Vector2(last.x - transform.position.x, last.z - transform.position.z).magnitude < 0.35f;
            if (atPathEnd && goingHome) next = target;
        }
        next.y = transform.position.y;

        transform.position = Vector3.MoveTowards(transform.position, next, speed * Time.deltaTime);
        Vector3 look = next - transform.position; look.y = 0f;
        if (look.sqrMagnitude > 0.0004f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(look), 540f * Time.deltaTime);
    }

    // Investigating state One shared behaviour for everything that makes Edgar go and check a spot: losing sight of Bandit (his last seen position), the.
    [Header("Investigating")]
    [Tooltip("Walking speed while going to check something out.")]
    [SerializeField] private float investigateSpeed = 2.5f;
    [Tooltip("How close he gets to the spot before looking around.")]
    [SerializeField] private float investigateArriveDistance = 1.2f;
    [Tooltip("Seconds spent looking around once he gets there.")]
    [SerializeField] private float lookAroundTime = 4f;
    [Tooltip("How far he turns left/right while looking around (degrees).")]
    [SerializeField] private float lookAroundAngle = 70f;
    [Tooltip("Seconds of lost sight before a chase turns into a search of Bandit's last known position.")]
    [SerializeField] private float lostSightGrace = 0.75f;

    public bool IsInvestigating => investigating;
    /// Off the sofa and walking about (not sitting watching TV).
    public bool IsUpAndAbout => !animSeated || investigating || patrolEnabled || returning;
    /// Actively looking for / chasing Bandit right now.
    public bool IsHunting => investigating || patrolSightChaseActive || timerChaseActive || scriptedPatrolActive;

    private bool investigating;
    private Vector3 investigatePoint;
    private bool investigateReturnToSofa;
    private float investigateTimer;
    private int investigatePhase;          // 0 = getting up, 1 = walking, 2 = looking around
    private Quaternion lookAroundBase;

    private void OnEnable()
    {
        ChipPacket.OnFirstNoise += HandleChipFirstNoise;
        Fridge.OnLeftOpenTooLong += HandleFridgeLeftOpen;
    }

    private void OnDisable()
    {
        ChipPacket.OnFirstNoise -= HandleChipFirstNoise;
        Fridge.OnLeftOpenTooLong -= HandleFridgeLeftOpen;
    }

    private void HandleChipFirstNoise()
    {
        ChipPacket chips = FindFirstObjectByType<ChipPacket>();
        if (chips != null) Investigate(chips.transform.position, "Edgar heard a crunch in the kitchen and is coming to check...");
    }

    private void HandleFridgeLeftOpen(Vector3 spot)
    {
        Investigate(spot, "The fridge has been open too long - Edgar is coming to check the kitchen!");
    }

    /// Send Edgar to check out a spot (noise, open fridge...). Ignored while
    /// he's already chasing Bandit or doing his end-of-timer / chip-bag routes.
    public void Investigate(Vector3 point, string playerMessage = null)
    {
        if (caught || timerChaseActive || scriptedPatrolActive || patrolSightChaseActive) return;
        if (investigating)
        {
            // Already checking something - just redirect him to the new noise.
            investigatePoint = new Vector3(point.x, transform.position.y, point.z);
            if (investigatePhase >= 2) { investigatePhase = 1; investigateTimer = 0f; }
            return;
        }
        StartInvestigation(point, !patrolEnabled, playerMessage);
    }

    private void StartInvestigation(Vector3 point, bool returnToSofa, string playerMessage)
    {
        bool wasSeated = animSeated;
        investigating = true;
        investigateReturnToSofa = returnToSofa;
        investigatePoint = new Vector3(point.x, transform.position.y, point.z);
        investigatePhase = wasSeated ? 0 : 1;
        investigateTimer = 0f;
        sightTimer = 0f;
        suspicion = 0f;

        Debug.Log("Edgar is investigating " + investigatePoint);
        // Heard/saw something -> "What's that?"; lost Bandit -> "Where'd it go?"
        if (string.IsNullOrEmpty(playerMessage)) Voice.Say("edgar_search", "Where did it go?");
        else Voice.Say("edgar_huh", "Hm? What's that?");
        if (!string.IsNullOrEmpty(playerMessage))
        {
            UIManager ui = FindFirstObjectByType<UIManager>();
            if (ui != null) ui.ShowWarning(playerMessage);
        }
    }

    private void UpdateInvestigating()
    {
        investigateTimer += Time.deltaTime;

        // Spotting Bandit at any point turns the search into a chase.
        if (CanSeePlayerOnPatrol())
        {
            sightTimer += Time.deltaTime;
            if (sightTimer >= sightConfirmTime)
            {
                investigating = false;
                spottedFromSofa = investigateReturnToSofa;
                patrolEnabled = true;
                patrolSightChaseActive = true;
                lastKnownPlayerPos = new Vector3(player.position.x, transform.position.y, player.position.z);
                hasLastKnownPos = true;
                patrolLostSightTimer = 0f;
                sightTimer = 0f;
                ShowInvestigateMark(false);
                ShowSuspicion(true);
                Debug.Log("Edgar found Bandit while investigating - chasing!");
            RaiseAlertLevel();
                return;
            }
        }
        else
        {
            sightTimer = Mathf.Max(0f, sightTimer - Time.deltaTime);
        }

        ShowInvestigateMark(true);

        switch (investigatePhase)
        {
            case 0: // give the stand-up animation a moment
                if (investigateTimer >= 1.2f) { investigatePhase = 1; investigateTimer = 0f; }
                break;

            case 1: // walk to the spot
            {
                Vector3 to = investigatePoint - transform.position;
                to.y = 0f;
                // Arrived - or got as close as he can (the spot is inside furniture).
                if (to.magnitude <= investigateArriveDistance || (navReady && atPathEnd && investigateTimer > 0.5f))
                {
                    investigatePhase = 2;
                    investigateTimer = 0f;
                    lookAroundBase = transform.rotation;
                    Debug.Log("Edgar is looking around...");
                    break;
                }
                StepTowards(investigatePoint, investigateSpeed);
                break;
            }

            case 3: // walking to a hiding spot to check it
            case 4: // checking it
                if (UpdateHidingSpotSearch())
                {
                    investigating = false;
                    ShowInvestigateMark(false);
                    Voice.Say("edgar_giveup", "Must have been nothing...");
                    Debug.Log("Edgar checked the hiding spots, found nothing and is " + (investigateReturnToSofa ? "going back to the sofa." : "carrying on with his patrol."));
                    if (investigateReturnToSofa) BeginReturning();
                }
                break;

            case 2: // look around left and right
            {
                float yaw = Mathf.Sin(investigateTimer / lookAroundTime * Mathf.PI * 2f) * lookAroundAngle;
                transform.rotation = lookAroundBase * Quaternion.Euler(0f, yaw, 0f);
                if (investigateTimer >= lookAroundTime)
                {
                    // Nothing in the open - now check the hiding places nearby.
                    if (PlanHidingSpotSearch()) { investigatePhase = 3; investigateTimer = 0f; break; }
                    investigating = false;
                    ShowInvestigateMark(false);
                    Voice.Say("edgar_giveup", "Must have been nothing...");
                    Debug.Log("Edgar found nothing and is " + (investigateReturnToSofa ? "going back to the sofa." : "carrying on with his patrol."));
                    if (investigateReturnToSofa) BeginReturning();
                }
                break;
            }
        }
    }

    [Tooltip("After looking around, Edgar checks the hiding spots this close to where he's searching.")]
    [SerializeField] private float searchSpotsRadius = 6f;
    [Tooltip("How many nearby hiding spots he checks (nearest first).")]
    [SerializeField] private int searchSpotsCount = 2;
    [Tooltip("Seconds he spends checking each hiding spot.")]
    [SerializeField] private float checkSpotTime = 1.5f;
    private readonly List<Vector3> spotStandPoints = new List<Vector3>();
    private readonly List<Vector3> spotCentres = new List<Vector3>();
    private int spotIndex;

    private bool PlanHidingSpotSearch()
    {
        spotStandPoints.Clear(); spotCentres.Clear(); spotIndex = 0;
        foreach (HidingSpot h in HidingSpot.NearestTo(investigatePoint, searchSpotsRadius, searchSpotsCount))
        {
            Vector3 stand = h.SearchPointFrom(transform.position);
            UnityEngine.AI.NavMeshHit hit;
            if (navReady && UnityEngine.AI.NavMesh.SamplePosition(stand, out hit, 2.5f, UnityEngine.AI.NavMesh.AllAreas))
                stand = hit.position;
            stand.y = transform.position.y;
            spotStandPoints.Add(stand);
            spotCentres.Add(h.Centre);
            Debug.Log("Edgar will check the hiding spot '" + h.name + "'.");
        }
        return spotStandPoints.Count > 0;
    }

    // Phase 3/4 of investigating: walk up to each nearby hiding spot and
    // look at it. If Bandit is hiding there, getting this close finds him
    // (HidingSpot does that). Returns true when all spots are checked.
    private bool UpdateHidingSpotSearch()
    {
        if (spotIndex >= spotStandPoints.Count) return true;
        Vector3 stand = spotStandPoints[spotIndex];
        if (investigatePhase == 3)
        {
            Vector3 to = stand - transform.position; to.y = 0f;
            if (to.magnitude <= 0.6f || (investigateTimer > 6f)) { investigatePhase = 4; investigateTimer = 0f; }
            else StepTowards(stand, investigateSpeed);
        }
        else
        {
            Vector3 look = spotCentres[spotIndex] - transform.position; look.y = 0f;
            if (look.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(look), 360f * Time.deltaTime);
            if (investigateTimer >= checkSpotTime)
            {
                spotIndex++;
                investigatePhase = 3; investigateTimer = 0f;
            }
        }
        return spotIndex >= spotStandPoints.Count;
    }

    // Orange "?" over Edgar while he's searching.
    private void ShowInvestigateMark(bool on)
    {
        if (!on)
        {
            if (suspicionMark != null && suspicion <= 0.02f) suspicionMark.gameObject.SetActive(false);
            return;
        }
        suspicion = 0.6f;          // reuse the "?" display at a mid colour
        ShowSuspicion();
        suspicion = 0f;
    }

    // Vision tightens after the first detection Once Edgar has actually spotted Bandit, he's on edge for the rest of this attempt: he confirms a sighting.
    [Header("High alert (after first spotting Bandit)")]
    [Tooltip("Sight confirm time and sofa suspicion time are multiplied by this (0.5 = twice as quick).")]
    [SerializeField] private float alertConfirmMultiplier = 0.5f;
    [Tooltip("Vision ranges are multiplied by this (1.25 = sees 25% further).")]
    [SerializeField] private float alertRangeMultiplier = 1.25f;

    public bool IsOnHighAlert => onHighAlert;
    private bool onHighAlert;

    /// Test shortcut (F5): Edgar spots Bandit right now, wherever he is.
    public void TestSpotPlayer()
    {
        if (player == null || caught) return;
        investigating = false;
        spottedFromSofa = !patrolEnabled;
        patrolEnabled = true;
        patrolSightChaseActive = true;
        lastKnownPlayerPos = new Vector3(player.position.x, transform.position.y, player.position.z);
        hasLastKnownPos = true;
        patrolLostSightTimer = 0f;
        ShowSuspicion(true);
        Debug.Log("TEST: Edgar spotted Bandit.");
        RaiseAlertLevel();
    }

    private void RaiseAlertLevel()
    {
        if (onHighAlert) return;
        onHighAlert = true;

        float oldConfirm = sightConfirmTime, oldRange = patrolVisionRange;
        sightConfirmTime *= alertConfirmMultiplier;
        suspicionTime *= alertConfirmMultiplier;
        patrolVisionRange *= alertRangeMultiplier;
        sofaVisionRange *= alertRangeMultiplier;

        Debug.Log($"Edgar is on HIGH ALERT: sight confirm {oldConfirm:0.00}s -> {sightConfirmTime:0.00}s, " +
                  $"vision range {oldRange:0.0} -> {patrolVisionRange:0.0}.");
        // Edgar's shout wakes the cat too, if she's still asleep.
        if (CatNPC.Instance != null && CatNPC.Instance.IsAsleep)
        {
            CatNPC.Instance.WakeUp();
            Debug.Log("Edgar's alert woke Whiskers up!");
            ScreenMessage.Show("Edgar spotted you - RUN and hide! His shout woke Whiskers too!",
                               4.5f, new Color(1f, 0.5f, 0.3f));
        }
        else
        {
            ScreenMessage.Show("Edgar has seen you - RUN and hide! He's on high alert now and will spot you faster.",
                               4f, new Color(1f, 0.5f, 0.3f));
        }
    }

    [Header("Watching from the sofa")]
    [Tooltip("How far Edgar can see Bandit from the sofa.")]
    [SerializeField] private float sofaVisionRange = 9f;
    [Tooltip("Half-angle of Edgar's view from the sofa (degrees either side of where he faces).")]
    [SerializeField] private float sofaVisionAngle = 60f;
    [Tooltip("Seconds Bandit can stand in Edgar's view before Edgar gets up. Crouching makes it take much longer.")]
    [SerializeField] private float suspicionTime = 2f;
    [Tooltip("Crouching fills Edgar's suspicion this much slower (0.3 = about 3x slower).")]
    [SerializeField] private float crouchSuspicionMultiplier = 0.3f;
    [Tooltip("Entering this distance of Edgar's sofa shows the 'careful' warning.")]
    [SerializeField] private float loungeWarningRadius = 9f;
    [SerializeField] private string loungeWarningText = "Careful - Edgar is on the sofa. Stay low and out of his sight!";

    private bool timerWoke;          // woken for good by the kitchen timer
    private bool spottedFromSofa;    // got up because he noticed Bandit
    private float suspicion;         // 0..1
    /// How full Edgar's "?" is on the sofa (0..1).
    public float Suspicion => suspicion;
    private bool loungeWarned;
    private TextMesh suspicionMark;
    private PlayerController playerController;

    // Sitting on the sofa: warn Bandit when he comes into the lounge, and
    // let Edgar notice him if he hangs around in view. A "?" over Edgar's
    // head fills up (yellow -> red); when it's full, "!" - he gets up and
    // goes after Bandit.
    private Vector3 lastSeenFromSofa;
    private bool hadSightFromSofa;

    private void UpdateSofaWatch()
    {
        if (player == null) return;

        float dist = Vector3.Distance(homePosition, player.position);
        if (!loungeWarned && dist <= loungeWarningRadius)
        {
            loungeWarned = true;
            UIManager ui = FindFirstObjectByType<UIManager>();
            if (ui != null) ui.ShowWarning(loungeWarningText);
        }
        else if (loungeWarned && dist > loungeWarningRadius + 3f)
        {
            loungeWarned = false; // warn again next time he comes in
        }

        bool seen = CanSeePlayerFromSofa();
        if (seen)
        {
            lastSeenFromSofa = new Vector3(player.position.x, transform.position.y, player.position.z);
            hadSightFromSofa = true;
            if (playerController == null) playerController = player.GetComponent<PlayerController>();
            bool crouching = playerController != null && playerController.IsCrouching();
            suspicion += Time.deltaTime / Mathf.Max(0.1f, suspicionTime) * (crouching ? crouchSuspicionMultiplier : 1f);
        }
        else
        {
            suspicion -= Time.deltaTime * 0.35f;
            // Bandit slipped out of view after Edgar had started to notice him:
            // Edgar gets suspicious and goes to check where he was.
            if (hadSightFromSofa && suspicion > 0.1f)
            {
                hadSightFromSofa = false;
                suspicion = 0f;
                ScreenMessage.SetStatus(null, Color.white);
                StartInvestigation(lastSeenFromSofa, true, "Edgar saw something move and is coming to check...");
                return;
            }
        }
        if (!seen && suspicion <= 0.1f) hadSightFromSofa = false;
        suspicion = Mathf.Clamp01(suspicion);
        ShowSuspicion();

        // Always-visible lounge status while Bandit is in here (the hiding
        // spot shows its own status while he's hidden).
        if (HidingSpot.PlayerIsHidden) { }
        else if (dist <= loungeWarningRadius)
        {
            int pct = Mathf.RoundToInt(suspicion * 100f);
            string state = seen ? "EDGAR CAN SEE YOU - " + (pct > 0 ? "suspicion " + pct + "%" : "hide or crouch!")
                                : "You're in the lounge - Edgar is on the sofa. Stay out of his view.";
            ScreenMessage.SetStatus(state, seen ? new Color(1f, 0.35f, 0.25f) : new Color(1f, 0.85f, 0.3f));
        }
        else
        {
            ScreenMessage.SetStatus(null, Color.white);
        }

        if (suspicion >= 1f)
        {
            suspicion = 0f;
            ShowSuspicion(true);
            Debug.Log("Edgar noticed Bandit from the sofa and is getting up!");
            RaiseAlertLevel();
            spottedFromSofa = true;
            patrolEnabled = true;
            patrolSightChaseActive = true;
            lastKnownPlayerPos = new Vector3(player.position.x, transform.position.y, player.position.z);
            hasLastKnownPos = true;
            patrolLostSightTimer = 0f;
            ScreenMessage.SetStatus(null, Color.white);
        }
    }

    private bool CanSeePlayerFromSofa()
    {
        if (HidingSpot.IsHiddenFrom(transform.position, "Edgar")) return false;
        Vector3 eye = (hips != null ? hips.position : transform.position) + Vector3.up * 1.2f;
        Vector3 target = player.position + Vector3.up * 0.3f;
        Vector3 to = target - eye;
        if (to.magnitude > sofaVisionRange) return false;
        Vector3 flat = new Vector3(to.x, 0f, to.z);
        if (Vector3.Angle(transform.forward, flat) > sofaVisionAngle) return false;
        if (Physics.Linecast(eye, target, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!hit.transform.IsChildOf(player) && !hit.transform.IsChildOf(transform)) return false;
        }
        return true;
    }

    private void ShowSuspicion(bool alarmed = false)
    {
        if (suspicionMark == null)
        {
            GameObject g = new GameObject("EdgarSuspicion");
            suspicionMark = g.AddComponent<TextMesh>();
            suspicionMark.fontSize = 90;
            suspicionMark.characterSize = 0.06f;
            suspicionMark.anchor = TextAnchor.LowerCenter;
            suspicionMark.alignment = TextAlignment.Center;
            suspicionMark.fontStyle = FontStyle.Bold;
        }

        bool show = alarmed || suspicion > 0.02f;
        suspicionMark.gameObject.SetActive(show);
        if (!show) return;

        float headY = transform.position.y + 2f;
        Renderer[] rs = GetComponentsInChildren<Renderer>();
        foreach (Renderer r in rs) headY = Mathf.Max(headY, r.bounds.max.y);
        suspicionMark.transform.position = new Vector3(transform.position.x, headY + 0.3f, transform.position.z)
                                           + (hips != null ? new Vector3(hips.position.x - transform.position.x, 0f, hips.position.z - transform.position.z) : Vector3.zero);
        if (Camera.main != null) suspicionMark.transform.forward = Camera.main.transform.forward;

        suspicionMark.text = alarmed ? "!" : "?";
        suspicionMark.color = alarmed ? Color.red : Color.Lerp(Color.yellow, new Color(1f, 0.3f, 0f), suspicion);
        suspicionMark.characterSize = 0.04f + 0.04f * (alarmed ? 1f : suspicion);
        if (alarmed) StartCoroutine(HideMarkLater());
    }

    private System.Collections.IEnumerator HideMarkLater()
    {
        yield return new WaitForSeconds(1.5f);
        if (suspicionMark != null && suspicion <= 0.02f) suspicionMark.gameObject.SetActive(false);
    }

    [Header("Sofa")]
    [Tooltip("How far above the sofa cushion Edgar's hips rest while sitting (world units). Raise if he sinks in, lower if he hovers.")]
    [SerializeField] private float sitClearance = 0.35f;

    private Transform modelRoot;   // the animated model under Edgar
    private Transform hips;
    private float seatBlend = 1f;  // 1 = sitting fit applied, 0 = standing

    private void SetUpBodyParts()
    {
        if (animator == null || animator.transform == transform) return;
        modelRoot = animator.transform;
        foreach (Transform t in modelRoot.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Hips") hips = t;
        }
        FixWatch();
        CaptureStandingArms();
        FixShoes();
    }

    // While sitting, moves Edgar's model so his hips land ON the sofa cushion. The sitting clip pushes his hips back a long way.
    private bool seatSearched, hasSeat;
    private Vector3 seatPoint;
    private Vector3 seatOffsetWorld;

    private void FitToSeat()
    {
        if (modelRoot == null || hips == null) return;

        seatBlend = Mathf.MoveTowards(seatBlend, animSeated ? 1f : 0f, Time.deltaTime * 1.5f);

        Vector3 currentOffset = transform.TransformVector(modelRoot.localPosition);
        Vector3 rawHips = hips.position - currentOffset;

        bool steadySitting = animSeated && animator.GetCurrentAnimatorStateInfo(0).IsName("Sitting");
        if (steadySitting)
        {
            if (!seatSearched) { seatSearched = true; hasSeat = FindSeat(rawHips); }
            if (hasSeat)
                seatOffsetWorld = Vector3.ClampMagnitude(seatPoint + Vector3.up * sitClearance - rawHips, 4f);
        }

        modelRoot.localPosition = transform.InverseTransformVector(seatOffsetWorld * seatBlend);
    }

    // The lounge's corner sofa (loungeDesignSofaCorner). Its cushions, read
    // straight from the model file, in the sofa's own local space: an L of
    // two seat strips at height 2.3 (backrest is 4.0, arms 3.3).
    private static readonly Vector4[] CornerSofaSeatStrips =
    {
        new Vector4(1.6f, 12.21f, 9.4f, 12.21f),   // x from/to, z from/to
        new Vector4(9.4f, 12.21f, 1.6f, 12.21f),
    };
    private const float CornerSofaSeatHeight = 2.3f;

    private bool TryKnownSofaSeat(Vector3 rawHips)
    {
        Transform sofa = null;
        float best = 15f;
        foreach (HidingSpot h in FindObjectsByType<HidingSpot>(FindObjectsSortMode.None))
        {
            if (!h.name.Contains("loungeDesignSofaCorner")) continue;
            float d = Vector3.Distance(h.transform.position, transform.position);
            if (d < best) { best = d; sofa = h.transform; }
        }
        if (sofa == null)
        {
            foreach (Transform t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name == "loungeDesignSofaCorner") { sofa = t; break; }
        }
        if (sofa == null) return false;

        Vector3 local = sofa.InverseTransformPoint(rawHips);
        const float margin = 0.5f;
        Vector3 bestLocal = Vector3.zero;
        float bestDist = float.MaxValue;
        foreach (Vector4 st in CornerSofaSeatStrips)
        {
            Vector3 c = new Vector3(
                Mathf.Clamp(local.x, st.x + margin, st.y - margin),
                CornerSofaSeatHeight,
                Mathf.Clamp(local.z, st.z + margin, st.w - margin));
            float d = new Vector2(c.x - local.x, c.z - local.z).sqrMagnitude;
            if (d < bestDist) { bestDist = d; bestLocal = c; }
        }
        seatPoint = sofa.TransformPoint(bestLocal);
        Debug.Log("Edgar found his sofa seat at " + seatPoint + " (corner sofa cushion).");
        return true;
    }

    private bool FindSeat(Vector3 rawHips)
    {
        if (TryKnownSofaSeat(rawHips)) return true;

        Vector3 root = transform.position;
        Vector3 back = rawHips - root;
        back.y = 0f;
        if (back.sqrMagnitude < 0.01f) back = -transform.forward;
        back.Normalize();

        const float step = 0.1f;
        const int samples = 35;
        float floorY = root.y;
        float[] h = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            // From high above, note every upward-facing surface under this point; keep the LOWEST one that's clearly above the floor.
            Vector3 p = root + back * (i * step) + Vector3.up * 15f;
            h[i] = float.PositiveInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(p, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!(hit.collider is MeshCollider)) continue;
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (player != null && hit.collider.transform.IsChildOf(player)) continue;
                if (hit.normal.y < 0.7f) continue;                       // not a sit-on-able surface
                float y = hit.point.y;
                if (y < floorY + 0.3f || y > floorY + 6f) continue;     // floor / ceiling
                if (y < h[i]) h[i] = y;
            }
            if (float.IsPositiveInfinity(h[i])) h[i] = float.NegativeInfinity;
        }

        // Heights found, for troubleshooting in the Console.
        System.Text.StringBuilder sb = new System.Text.StringBuilder("Edgar seat scan (back " + back + "): ");
        for (int i = 0; i < samples; i += 5) sb.Append(float.IsNegativeInfinity(h[i]) ? "-" : h[i].ToString("0.00")).Append(' ');
        Debug.Log(sb.ToString());

        // Cushion = the lowest surface clearly above the floor.
        float minH = float.PositiveInfinity;
        int first = -1;
        for (int i = 0; i < samples; i++)
        {
            if (h[i] > root.y + 0.3f && h[i] < minH) minH = h[i];
        }
        if (float.IsPositiveInfinity(minH))
        {
            Debug.Log("Edgar: no sofa cushion found behind him, so he sits where the animation puts him.");
            return false;
        }
        for (int i = 0; i < samples; i++) if (Mathf.Abs(h[i] - minH) < 0.2f) { first = i; break; }
        int last = first;
        while (last + 1 < samples && Mathf.Abs(h[last + 1] - minH) < 0.2f) last++;

        seatPoint = root + back * ((first + last) * 0.5f * step);
        seatPoint.y = minH;
        Debug.Log("Edgar found his sofa seat at " + seatPoint);
        return true;
    }

    // The watch in Edgar's model was exported with a broken offset from his
    // arm, so it floats off on its own when he moves. Re-attach it around his
    // left wrist so it follows the arm properly.
    private void FixWatch()
    {
        Transform watch = null, lowerArm = null, hand = null;
        foreach (Transform t in modelRoot.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "Edgar_Watch") watch = t;
            else if (t.name == "LeftLowerArm") lowerArm = t;
            else if (t.name == "LeftHand") hand = t;
        }
        if (watch == null || lowerArm == null || hand == null) return;
        MeshFilter mf = watch.GetComponent<MeshFilter>();
        Renderer r = watch.GetComponent<Renderer>();
        if (mf == null || mf.sharedMesh == null || r == null) return;

        // The watch's thinnest axis = the direction the strap wraps round.
        Vector3 ext = mf.sharedMesh.bounds.extents;
        Vector3 thinLocal = ext.x <= ext.y && ext.x <= ext.z ? Vector3.right : (ext.y <= ext.z ? Vector3.up : Vector3.forward);
        Vector3 thinWorld = watch.TransformDirection(thinLocal).normalized;

        // Pivot at the watch's centre so it can be moved/turned cleanly.
        GameObject pivot = new GameObject("WatchPivot");
        pivot.transform.position = r.bounds.center;
        pivot.transform.rotation = Quaternion.identity;
        watch.SetParent(pivot.transform, true);

        Vector3 forearm = (hand.position - lowerArm.position).normalized;
        pivot.transform.rotation = Quaternion.FromToRotation(thinWorld, forearm);
        pivot.transform.position = Vector3.Lerp(lowerArm.position, hand.position, 0.85f);
        pivot.transform.SetParent(lowerArm, true);
    }

    private System.Collections.IEnumerator StandUpRoutine()
    {
        animator.Play("StandingUp", 0, 1f);     // played backwards from the end
        yield return null;
        float len = animator.GetCurrentAnimatorStateInfo(0).length;
        yield return new WaitForSeconds(Mathf.Clamp(len, 0.3f, 3f) * 0.95f);
        animator.CrossFade("Locomotion", 0.2f);
        poseRoutine = null;
    }

    private System.Collections.IEnumerator SitDownRoutine()
    {
        animator.CrossFade("SittingDown", 0.2f);
        yield return null;
        yield return null;
        float len = animator.GetNextAnimatorStateInfo(0).length;
        if (len <= 0f) len = animator.GetCurrentAnimatorStateInfo(0).length;
        yield return new WaitForSeconds(Mathf.Clamp(len, 0.3f, 3f) * 0.95f);
        animator.CrossFade("Sitting", 0.2f);
        poseRoutine = null;
    }

    private void FaceTowards(Vector3 targetPos)
    {
        Vector3 look = new Vector3(targetPos.x, transform.position.y, targetPos.z);
        if ((look - transform.position).sqrMagnitude > 0.0001f)
            transform.LookAt(look);
    }

    private void SetChasingAnim(bool isChasing)
    {
        if (animator != null)
            animator.SetBool("IsChasing", isChasing);
    }

    private void SetPatrollingAnim(bool isPatrolling)
    {
        if (animator != null)
            animator.SetBool("IsPatrolling", isPatrolling);
    }

    // Kept for any external trigger (garden light, etc.) that still uses the
    // classic two-strike warn/catch system independently of the chip-packet escalation.
    public void OnPlayerDetected()
    {
        if (HidingSpot.PlayerIsHidden) return;

        if (!warned)
        {
            warned = true;
            Debug.Log("Human: warning - you were seen once.");
        }
        else
        {
            Debug.Log("Human: caught again - lose a life.");
            // Stop the kitchen clock the instant Edgar actually catches
            // Bandit - otherwise it can keep ticking down through the grab/
            // freeze sequence below and independently hit zero, costing a
            // SECOND life on top of this catch for the same encounter.
            if (kitchenTimer != null) kitchenTimer.StopTimer();
            ShowCaughtByEdgarBanner();
            GameManager.Instance.LoseLife();
        }
    }

    // Edgar catching Bandit is announced ONLY by the black/red banner -
    // the white "You've been caught..." life-lost panel is switched off for
    // this catch (RestartSceneOnLifeLoss skips it when this flag is set).
    private void ShowCaughtByEdgarBanner()
    {
        ScreenMessage.Show("Bandit has been caught by Edgar!", 3f, new Color(1f, 0.3f, 0.25f));
        if (GameManager.Instance != null) GameManager.Instance.suppressLifeLostMessage = true;
    }
}
