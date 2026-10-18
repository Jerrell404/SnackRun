using UnityEngine;
using UnityEngine.SceneManagement;

// Stops the follow camera going inside furniture, walls and NPCs.
public class CameraCollision : MonoBehaviour
{
    [Tooltip("How thick the camera is treated as (bigger = stays further from surfaces).")]
    [SerializeField] private float cameraRadius = 0.25f;
    [Tooltip("Extra gap kept between the camera and whatever it hit.")]
    [SerializeField] private float margin = 0.1f;
    [Tooltip("Closest the camera may get to Bandit's head.")]
    [SerializeField] private float minDistance = 0.4f;
    [Tooltip("How fast it eases back out once the way is clear (metres per second).")]
    [SerializeField] private float returnSpeed = 4f;

    private Transform player;
    private CharacterController playerBody;
    private Vector3 restLocalPosition;
    private float currentDistance = -1f;

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
        if (p == null) return;
        foreach (Camera cam in p.GetComponentsInChildren<Camera>())
            if (cam.GetComponent<CameraCollision>() == null) cam.gameObject.AddComponent<CameraCollision>();
    }

    private void Start()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null)
        {
            player = p.transform;
            playerBody = p.GetComponent<CharacterController>();
        }
        restLocalPosition = transform.localPosition;
        // Close-up views would cut into Bandit with the default near plane.
        Camera cam = GetComponent<Camera>();
        if (cam != null && cam.nearClipPlane > 0.05f) cam.nearClipPlane = 0.05f;
    }

    private void LateUpdate()
    {
        if (player == null || transform.parent == null) return;

        Vector3 pivot = HeadPoint();
        Vector3 wanted = transform.parent.TransformPoint(restLocalPosition);
        Vector3 toCam = wanted - pivot;
        float fullDistance = toCam.magnitude;
        if (fullDistance < 0.001f) return;
        Vector3 dir = toCam / fullDistance;

        float allowed = fullDistance;
        // While Bandit is climbing a rope/ladder, keep the camera exactly where it normally rests.
        if (!RopeClimb.AnyClimbing)
        {
            foreach (RaycastHit h in Physics.SphereCastAll(pivot, cameraRadius, dir, fullDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(player)) continue;       // Bandit himself
                if (h.distance <= 0f) continue;                              // started inside - ignore
                allowed = Mathf.Min(allowed, Mathf.Max(minDistance, h.distance - margin));
            }
        }

        if (currentDistance < 0f) currentDistance = allowed;
        // Snap in straight away (never show the inside of a cupboard), ease back out.
        if (allowed < currentDistance) currentDistance = allowed;
        else currentDistance = Mathf.MoveTowards(currentDistance, allowed, returnSpeed * Time.unscaledDeltaTime);

        transform.position = pivot + dir * currentDistance;
    }

    // Roughly Bandit's head height, from his body collider.
    private Vector3 HeadPoint()
    {
        if (playerBody != null)
        {
            Bounds b = playerBody.bounds;
            return new Vector3(b.center.x, b.max.y - b.extents.y * 0.25f, b.center.z);
        }
        return player.position + Vector3.up * 0.8f;
    }
}
