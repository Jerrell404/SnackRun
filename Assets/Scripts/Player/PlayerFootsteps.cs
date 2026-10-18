using UnityEngine;
using UnityEngine.SceneManagement;

// Bandit's footsteps: quiet and slow when sneaking (crouched), normal when
// walking, fast and loud when running. Wood inside the House, grass outside.
// Added to the player automatically in every scene.
public class PlayerFootsteps : MonoBehaviour
{
    [SerializeField] private float walkInterval = 0.42f;
    [SerializeField] private float runInterval = 0.27f;
    [SerializeField] private float crouchInterval = 0.6f;
    [SerializeField] private float walkVolume = 0.35f;
    [SerializeField] private float runVolume = 0.6f;
    [SerializeField] private float crouchVolume = 0.1f;

    private CharacterController cc;
    private PlayerController pc;
    private float timer;
    private Vector3 lastPos;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        Attach();
    }
    private static void OnLoaded(Scene s, LoadSceneMode m) => Attach();
    private static void Attach()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null && p.GetComponent<PlayerFootsteps>() == null) p.AddComponent<PlayerFootsteps>();
    }

    private void Start()
    {
        cc = GetComponent<CharacterController>();
        pc = GetComponent<PlayerController>();
        lastPos = transform.position;
    }

    private void Update()
    {
        if (Time.deltaTime <= 0f) return;
        Vector3 moved = transform.position - lastPos; moved.y = 0f;
        lastPos = transform.position;
        float speed = moved.magnitude / Time.deltaTime;
        bool grounded = cc == null || !cc.enabled || cc.isGrounded;
        // Only when the player is actually pressing a movement key - turning
        // the camera (which turns Bandit on the spot) must never make steps.
        bool moveKey = Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f || Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.1f;
        if (!moveKey || speed < 0.4f || !grounded || (pc != null && !pc.enabled)) { timer = 0.15f; return; }

        bool crouch = pc != null && pc.IsCrouching();
        bool run = !crouch && Input.GetKey(KeyCode.R);   // running sound only while R is held
        float interval = crouch ? crouchInterval : run ? runInterval : walkInterval;
        timer -= Time.deltaTime;
        if (timer > 0f) return;
        timer = interval;

        string surface = SceneManager.GetActiveScene().name == "House" ? "footstep_wood" : "footstep_grass";
        SoundFX.Play(surface, crouch ? crouchVolume : run ? runVolume : walkVolume, 0.08f);
    }
}
