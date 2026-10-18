using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class PT_MouseLook : MonoBehaviour
{
    [Header("Sensitivity (tune independently)")]
    public float sensitivityX = 100f;
    public float sensitivityY = 100f;

    [Header("Smoothing (higher = smoother, more lag)")]
    [Range(0f, 20f)] public float smoothing = 8f;

    [Header("Initial camera pitch (degrees, negative = look up, positive = look down)")]
    [Tooltip("X rotation the camera starts at when the scene loads. " +
             "0 = straight ahead (most scenes), 40 = angled down (Back-Yard).")]
    [SerializeField] private float initialXRotation = 0f;

    public Transform playerBody;

    private float xRotation = 0f;
    private Vector2 currentMouseDelta;
    private Vector2 currentMouseDeltaVelocity;

    private int framesToIgnore = 2;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;

        // Apply the per-scene starting pitch so saves / scene transitions
        // never carry over a stale camera angle.
        xRotation = initialXRotation;
        transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
    }

    // A frame far bigger than the normal cap below means an actual hitch
    // just happened - a scene finishing loading, a stutter, a heavy frame
    // in a busier scene - rather than steady frame-to-frame variation.
    private const float HitchThreshold = 0.15f;

    void Update()
    {
        // Frozen during a life-lost/game-over cutscene (RestartSceneOnLifeLoss
        // and GameOverScreen both zero Time.timeScale while the message is on
        // screen). Everything else in the game already stops moving because it
        // scales its movement by Time.deltaTime, but the new Input System path
        // below reads the raw mouse delta and scales it by a fixed constant
        // instead - so without this check the camera kept freely spinning
        // while the rest of the world was frozen.
        if (Time.timeScale <= 0f) return;

        // Don't rotate the camera when the game window doesn't have focus
        // (player is tabbed out) or when the cursor is not locked to the window.
        if (!Application.isFocused) return;
        if (Cursor.lockState != CursorLockMode.Locked) return;

        // Cap the frame time used to build the input delta AND (below) to
        // advance the SmoothDamp itself - not Time.deltaTime globally - so a
        // single hitch/stutter can't turn into one big, visible camera snap.
        // Below this cap, behaviour is identical to before.
        float dt = Mathf.Min(Time.deltaTime, 0.05f);

        // A real hitch (level just loaded, a stutter, an expensive frame in a
        // busy scene) means Time.deltaTime itself spikes far past that cap.
        // Feeding a spike like that into SmoothDamp below doesn't just make
        // it lag one frame - SmoothDamp is only numerically stable while
        // deltaTime stays small relative to its smoothTime, and a big enough
        // spike makes it overshoot and oscillate for a few frames after,
        // which is exactly the "flicks/jumps/spins in circles for a bit,
        // then suddenly catches up" behaviour that was reported (worst in
        // the Front Yard, which is the laggiest scene, but also happening
        // right at the start of every level while it finishes loading). So
        // when a hitch is detected, just drop this frame's input and reset
        // the smoothing state, the same way the very first frames after
        // locking the cursor already do below, instead of letting the
        // smoothing try to "catch up" across the gap.
        if (Time.deltaTime > HitchThreshold)
        {
            currentMouseDelta = Vector2.zero;
            currentMouseDeltaVelocity = Vector2.zero;
            return;
        }

        Vector2 targetMouseDelta = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
        var mouse = Mouse.current;
        if (mouse != null)
        {
            Vector2 delta = mouse.delta.ReadValue();
            targetMouseDelta = new Vector2(
                delta.x * sensitivityX * 0.02f,
                delta.y * sensitivityY * 0.02f
            );
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        // This project uses the old Input Manager (activeInputHandler: 0 in
        // ProjectSettings), so this is the path that's actually compiled and
        // running.
        targetMouseDelta = new Vector2(
            Input.GetAxisRaw("Mouse X") * sensitivityX * dt,
            Input.GetAxisRaw("Mouse Y") * sensitivityY * dt
        );
#endif

        // Ignore the first couple of frames — avoids the "jump on start" caused
        // by the OS reporting a large delta the instant the cursor locks/recenters.
        if (framesToIgnore > 0)
        {
            framesToIgnore--;
            currentMouseDeltaVelocity = Vector2.zero;
            currentMouseDelta = Vector2.zero;
            return;
        }

        // Passing `dt` explicitly here (instead of leaving SmoothDamp's
        // deltaTime argument to default to Time.deltaTime) is the actual
        // fix: previously the cap above was only ever used to build
        // targetMouseDelta, and SmoothDamp itself still silently advanced by
        // the raw, uncapped Time.deltaTime every frame - so a hitch that
        // slipped in just under HitchThreshold could still destabilise it.
        currentMouseDelta = Vector2.SmoothDamp(
            currentMouseDelta, targetMouseDelta,
            ref currentMouseDeltaVelocity, 1f / Mathf.Max(smoothing, 0.0001f),
            Mathf.Infinity, dt
        );

        xRotation -= currentMouseDelta.y;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        playerBody.Rotate(Vector3.up * currentMouseDelta.x);
    }

    // Re-lock the cursor whenever the player tabs back in - but only if
    // the game is actually running right now. Clicking back into the game
    // window to press a button on the Pause menu (or a life-lost/game-over/
    // win screen) fires this exact same "focus regained" callback, and
    // those all need a free, clickable cursor; forcing it back to locked
    // here stole the very click the player was making, which is why menu
    // buttons could seem unclickable. All of those screens zero
    // Time.timeScale while they're up, so that's the same signal Update()
    // above already uses to know the game is paused - only recapture the
    // cursor once real gameplay is actually running again (i.e. after
    // Play/Resume has been clicked).
    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus && Time.timeScale > 0f)
            Cursor.lockState = CursorLockMode.Locked;
    }
}
