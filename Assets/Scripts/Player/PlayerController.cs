using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] private float walkSpeed = 3f;
    [SerializeField] private float runSpeed = 6f;
    [SerializeField] private float crouchSpeed = 1.5f;
    [SerializeField] private float jumpHeight = 1.2f;
    [SerializeField] private float gravity = -9.81f;

    [Header("Knockback (Rex/Whiskers attacks only)")]
    [Tooltip("How quickly an active knockback push fades back to zero (higher = shorter, snappier push).")]
    [SerializeField] private float knockbackDrag = 8f;
    private Vector3 knockbackVelocity = Vector3.zero;

    [Header("Laser slowness")]
    [Tooltip("Movement speed multiplier while slowed by a laser (0.5 = half speed). Applies to walk, run and crouch alike.")]
    [Range(0.05f, 1f)]
    [SerializeField] private float laserSlowSpeedMultiplier = 0.5f;
    [Tooltip("Jump height multiplier while slowed by a laser (0.7 = jumps reach 70% of normal height).")]
    [Range(0.05f, 1f)]
    [SerializeField] private float laserSlowJumpMultiplier = 0.7f;
    [Tooltip("Animation playback speed while slowed, so Bandit visibly moves sluggishly (1 = normal).")]
    [Range(0.1f, 1f)]
    [SerializeField] private float laserSlowAnimSpeed = 0.75f;
    private float slowTimer = 0f;
    private bool animSlowed = false;

    [Header("Fall-through safety net")]
    [Tooltip("If Bandit ends up airborne and drops this far below the lowest solid ground he's ever stood on, snap him back instead of letting him keep falling through the level.")]
    [SerializeField] private float fallResetDistance = 3f;
    [Tooltip("How long he has to be continuously airborne and sinking before the reset kicks in - kept longer than a normal jump/fall so it never interrupts real gameplay.")]
    [SerializeField] private float fallResetDelay = 0.6f;

    private CharacterController controller;
    private Vector3 velocity;
    private bool isGrounded;
    private bool isCrouching;
    private Animator animator;
    private Vector3 lastGroundedPosition;
    private float airborneTimer = 0f;

    // Tracks the lowest Y Bandit has ever been grounded on, rather than just wherever he last stood.
    private float lowestGroundedY;
    private bool hasGroundedOnce = false;

    // Set by HidingSpot while Bandit is tucked away hiding: forces the same
    // crouched/sneaking movement as holding Shift, regardless of input.
    private bool isForcedHiding = false;
    public void SetHiding(bool hiding) => isForcedHiding = hiding;

    [Header("Body collider")]
    [Tooltip("Resize the CharacterController on Play to fit Bandit's actual body. The one set in the scene was tiny (about 0.2 wide, mostly under the floor)")]
    [SerializeField] private bool fitColliderToBody = true;
    [Tooltip("How much of Bandit's body width the collider covers (1 = full width).")]
    [SerializeField] private float bodyFit = 0.9f;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        if (fitColliderToBody) FitColliderToBody();
    }

    // Sizes the capsule around the raccoon model (Raccoon_Root), in this
    // object's local space, so the collider is where his body actually is.
    private void FitColliderToBody()
    {
        Transform body = null;
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            if (t.name == "Raccoon_Root") { body = t; break; }
        if (body == null) return;

        Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
        bool any = false;
        foreach (Renderer r in body.GetComponentsInChildren<Renderer>())
        {
            Bounds b = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                                        (i & 2) == 0 ? b.min.y : b.max.y,
                                        (i & 4) == 0 ? b.min.z : b.max.z);
                Vector3 l = transform.InverseTransformPoint(c);
                min = Vector3.Min(min, l);
                max = Vector3.Max(max, l);
                any = true;
            }
        }
        if (!any) return;

        Vector3 size = max - min;
        Vector3 centre = (min + max) * 0.5f;

        // Capsule radius: between half his width and half his length, so his
        // nose/tail don't poke far into things but he still fits through gaps.
        float radius = Mathf.Lerp(size.x, size.z, 0.35f) * 0.5f * bodyFit;
        float height = Mathf.Max(size.y, radius * 2f + 0.01f);

        controller.enabled = false;
        controller.radius = radius;
        controller.height = height;
        controller.center = new Vector3(centre.x, min.y + height * 0.5f, centre.z);
        controller.stepOffset = Mathf.Min(controller.stepOffset, height * 0.4f);
        controller.enabled = true;

        Debug.Log("Bandit's collider fitted to his body: radius " + (radius * transform.lossyScale.x).ToString("0.00") +
                  ", height " + (height * transform.lossyScale.y).ToString("0.00") + " (world units)");
    }

    void Update()
    {
        isGrounded = controller.isGrounded;
        if (isGrounded && velocity.y < 0) velocity.y = -2f;

        CheckFallThroughSafetyNet();

        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        Vector3 move = transform.right * h + transform.forward * v;

        float currentSpeed = walkSpeed;
        bool isSprinting = false;

        UpdateSlow();

        if (Input.GetKey(KeyCode.R))
        {
            if (isForcedHiding && HidingSpot.ActiveHidingSpot != null)
            {
                // Sprinting while tucked into a hiding spot breaks cover -
                // it pops Bandit back out of hiding (visible again) instead
                // of just being silently blocked.
                HidingSpot.ActiveHidingSpot.ForceExitFromSprint();
            }

            if (!isForcedHiding)
            {
                // Sprinting backwards isn't allowed - holding S+R just walks
                // backward at normal speed instead of dashing backward.
                if (v < 0f)
                {
                    currentSpeed = walkSpeed;
                }
                else
                {
                    currentSpeed = runSpeed;
                    isSprinting = true;
                }
            }
        }

        isCrouching = isForcedHiding || Input.GetKey(KeyCode.LeftShift);
        if (isCrouching)
        {
            // Shift always wins over R - can't sprint while crouched/hiding.
            currentSpeed = crouchSpeed;
            isSprinting = false;
        }

        if (IsSlowed) currentSpeed *= laserSlowSpeedMultiplier;

        controller.Move((move * currentSpeed + knockbackVelocity) * Time.deltaTime);

        // Fades the push back out on its own - callers only ever add to it,
        // never manage its lifetime themselves.
        knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero, knockbackDrag * Time.deltaTime);

        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (isForcedHiding && HidingSpot.ActiveHidingSpot != null)
            {
                // Jumping while tucked into a hiding spot pops Bandit back
                // out of hiding AND performs the actual jump below - it's
                // not an either/or, jumping should always be visible.
                HidingSpot.ActiveHidingSpot.ForceExitFromJump();
            }

            if (isGrounded)
            {
                float effectiveJumpHeight = IsSlowed ? jumpHeight * laserSlowJumpMultiplier : jumpHeight;
                velocity.y = Mathf.Sqrt(effectiveJumpHeight * -2f * gravity);

                // Plays the jump pose once, from whatever Idle/Walk/Crouch
                // state Bandit is currently in - the Animator Controller's
                // own transitions handle landing back in the right state
                // (based on Speed/IsCrouching at the moment the clip ends).
                if (animator != null) animator.SetTrigger("Jump");
            }
        }

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);

        float moveInput = Mathf.Abs(Input.GetAxisRaw("Horizontal")) + Mathf.Abs(Input.GetAxisRaw("Vertical"));
        bool isMoving = moveInput > 0.1f;

        // Drives the Animator Controller's Idle/Walk/Run/CrouchIdle/CrouchWalk blend.
        if (animator != null)
        {
            animator.SetFloat("Speed", isMoving ? 1f : 0f);
            animator.SetBool("IsCrouching", isCrouching);
            animator.SetBool("IsRunning", isSprinting && isMoving);
        }

    }

    void Awake()
    {
        animator = GetComponent<Animator>();
        controller = GetComponent<CharacterController>();
    }

    public bool IsCrouching() => isCrouching;

    public bool IsSlowed => slowTimer > 0f;

    // Called by LaserBeam every time Bandit touches a beam: for `duration` seconds he moves slower and can't jump as high.
    public void ApplySlow(float duration)
    {
        slowTimer = Mathf.Max(slowTimer, duration);
    }

    private void UpdateSlow()
    {
        if (slowTimer > 0f) slowTimer -= Time.deltaTime;

        bool shouldSlowAnim = IsSlowed;
        if (animator != null && shouldSlowAnim != animSlowed)
        {
            animator.speed = shouldSlowAnim ? laserSlowAnimSpeed : 1f;
        }
        animSlowed = shouldSlowAnim;
    }

    // Called by an attacker (Rex/Whiskers) the instant it lands a hit.
    public void ApplyKnockback(Vector3 fromPosition, float force)
    {
        Vector3 dir = transform.position - fromPosition;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = -transform.forward; // fallback if standing right on top of each other
        dir.Normalize();
        knockbackVelocity += dir * force;
    }

    // Catches any cause of falling through the floor/level (a physics push from an NPC's collider, a bad teleport, clipping through thin geometry, etc.)
    private void CheckFallThroughSafetyNet()
    {
        if (isGrounded)
        {
            lastGroundedPosition = transform.position;
            airborneTimer = 0f;

            if (!hasGroundedOnce || transform.position.y < lowestGroundedY)
            {
                lowestGroundedY = transform.position.y;
                hasGroundedOnce = true;
            }
            return;
        }

        airborneTimer += Time.deltaTime;

        float floorReference = hasGroundedOnce ? lowestGroundedY : lastGroundedPosition.y;

        if (airborneTimer > fallResetDelay && transform.position.y < floorReference - fallResetDistance)
        {
            controller.enabled = false;
            transform.position = lastGroundedPosition;
            controller.enabled = true;
            velocity = Vector3.zero;
            airborneTimer = 0f;
            Debug.Log("Bandit fell through the level - reset to last safe ground position.");
        }
    }
}
