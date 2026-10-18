using UnityEngine;

public class HidingSpot : MonoBehaviour, IInteractable
{
    [Header("Optional: snap Bandit fully out of sight (bag/box style). Leave empty for the crouch/see-through hiding style.")]
    public Transform hidePoint;

    [Header("Crouch-hiding settings (used when hidePoint is empty)")]
    [Tooltip("How see-through Bandit becomes while hiding - like an invisibility potion, not fully gone.")]
    [Range(0.05f, 0.9f)] public float hiddenAlpha = 0.25f;
    [Tooltip("If Bandit wanders this many units away on X, Y or Z from where he started hiding, he's automatically revealed.")]
    public float maxWanderDistance = 10f;

    // Shared across every hiding spot in the level - true the instant Bandit
    // tucks into ANY spot. Human and Cat detection/attack logic both check
    // this before they're allowed to see or catch the player.
    public static bool PlayerIsHidden { get; private set; } = false;

    // Whichever hiding spot Bandit is currently tucked into, if any - lets
    // PlayerController pop him back out (e.g. on jump) without needing to
    // search the scene for it.
    public static HidingSpot ActiveHidingSpot { get; private set; }

    [Header("Realistic hiding")]
    [Tooltip("Bandit has to stay tucked in at the spot - moving further than this (flat distance) from where he hid gives him away.")]
    public float stayCloseDistance = 1.5f;

    /// An NPC this close finds Bandit even if he's hiding (sniffed out /
    /// walked right up to him). Further away, hiding keeps him unseen.
    public static float DiscoverDistance = 2f;

    /// Use instead of PlayerIsHidden in NPC sight checks: true if Bandit is
    /// hidden from a viewer at this position. If the viewer is right on top
    /// of him, he's discovered instead (pops out of hiding) and this is false.
    public static bool IsHiddenFrom(Vector3 viewerPos, string viewerName)
    {
        if (!PlayerIsHidden || ActiveHidingSpot == null || ActiveHidingSpot.player == null) return false;
        Vector3 d = ActiveHidingSpot.player.transform.position - viewerPos;
        d.y = 0f;
        if (d.magnitude > DiscoverDistance) return true;

        ActiveHidingSpot.Discover(viewerName);
        return false;
    }

    // For NPCs searching hiding places
    private static readonly System.Collections.Generic.List<HidingSpot> allSpots = new System.Collections.Generic.List<HidingSpot>();
    private void OnEnable() { if (!allSpots.Contains(this)) allSpots.Add(this); }
    private void OnDisable() { allSpots.Remove(this); }

    /// The hiding spots within radius of a point, nearest first.
    public static System.Collections.Generic.List<HidingSpot> NearestTo(Vector3 point, float radius, int max)
    {
        var found = new System.Collections.Generic.List<HidingSpot>();
        foreach (HidingSpot h in allSpots)
        {
            if (h == null || !h.isActiveAndEnabled) continue;
            Vector3 d = h.ClosestPointTo(point) - point; d.y = 0f;
            if (d.magnitude <= radius) found.Add(h);
        }
        found.Sort((a, b) => FlatDist(a.ClosestPointTo(point), point).CompareTo(FlatDist(b.ClosestPointTo(point), point)));
        if (found.Count > max) found.RemoveRange(max, found.Count - max);
        return found;
    }

    private static float FlatDist(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

    /// Where a searcher coming from "from" should stand to check this spot:
    /// right beside the furniture, on the side they approach from.
    public Vector3 SearchPointFrom(Vector3 from)
    {
        Vector3 edge = ClosestPointTo(from);
        Vector3 away = from - edge; away.y = 0f;
        Vector3 p = edge + (away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.forward) * 0.6f;
        p.y = from.y;
        return p;
    }

    public Vector3 Centre => SpotBounds().center;

    private Vector3 ClosestPointTo(Vector3 p)
    {
        Bounds b = SpotBounds();
        Vector3 q = b.ClosestPoint(new Vector3(p.x, b.center.y, p.z));
        return q;
    }

    private Bounds SpotBounds()
    {
        Bounds b = new Bounds(transform.position, Vector3.zero); bool any = false;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    private void Discover(string byWhom)
    {
        if (!isHiding) return;
        isHiding = false;
        ExitHiding();
        Debug.Log(byWhom + " found Bandit's hiding spot!");
        ScreenMessage.Show(byWhom + " found your hiding spot - RUN!", 4f, new Color(1f, 0.35f, 0.25f));
    }

    private bool isHiding = false;
    private GameObject player;
    private PlayerController playerMovement;
    private Renderer[] playerRenderers;
    private Material[] playerMaterials;
    private Color[] originalColors;
    private Material[][] savedSharedMaterials; // exact materials before hiding, put back afterwards
    private Vector3 preHidePosition;
    private Vector3 hideStartPosition;

    // Some hiding spots (the corner sofa) had a big box collider covering their whole footprint.
    [Tooltip("Off (default): the spot's box colliders stay on, so Bandit can't walk through its body. On: they are switched off and only the exact-shape mesh collider is used.")]
    public bool useExactShapeInsteadOfBox = false;

    private void Awake()
    {
        if (!useExactShapeInsteadOfBox) return;
        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null || !box.enabled || box.isTrigger) return;
        foreach (MeshCollider mc in GetComponentsInChildren<MeshCollider>())
        {
            if (mc.gameObject != gameObject && mc.enabled)
            {
                box.enabled = false;
                Debug.Log(name + ": using its exact-shape collider for hiding instead of the big box.");
                return;
            }
        }
    }

    // While hiding: a status line saying whether anyone is hunting for
    // Bandit nearby, so the player knows when it's safe to come out.
    private void ShowSafetyStatus()
    {
        if (player == null) return;
        // Anyone AWAKE counts - not just someone actively chasing. Whiskers
        // wandering back to the fridge a metre away is not "safe".
        string who = null; float nearest = float.MaxValue; bool hunting = false;
        CheckDanger("Edgar", AwakeEdgar(), HumanNPC.Instance != null && HumanNPC.Instance.IsHunting, ref who, ref nearest, ref hunting);
        CheckDanger("Whiskers", AwakeWhiskers(), CatNPC.Instance != null && CatNPC.Instance.IsHunting, ref who, ref nearest, ref hunting);
        int m = Mathf.Max(1, Mathf.RoundToInt(nearest));

        if (who == null)
            ScreenMessage.SetStatus("Hiding - nobody is near you or looking for you. Safe to come out (press E).", new Color(0.5f, 1f, 0.5f));
        else if (nearest <= 4f)
            ScreenMessage.SetStatus("DON'T MOVE - " + who + " is right next to you (" + m + "m)! If they get any closer they'll find you.", new Color(1f, 0.3f, 0.25f));
        else if (hunting)
            ScreenMessage.SetStatus("Hiding - " + who + " is looking for you (" + m + "m away). Wait until they give up...", new Color(1f, 0.6f, 0.25f));
        else
            ScreenMessage.SetStatus("Hiding - " + who + " is awake nearby (" + m + "m away). Wait for them to move off...", new Color(1f, 0.85f, 0.3f));
    }

    private const float NearbyDistance = 8f;

    private void CheckDanger(string name, Transform npc, bool isHunting, ref string who, ref float nearest, ref bool hunting)
    {
        if (npc == null || player == null) return;
        Vector3 d = npc.position - player.transform.position; d.y = 0f;
        float dist = d.magnitude;
        if (!isHunting && dist > NearbyDistance) return; // awake but far away - fine
        if (dist < nearest) { nearest = dist; who = name; hunting = isHunting; }
    }

    private static Transform AwakeEdgar()
    {
        HumanNPC e = HumanNPC.Instance;
        return e != null && e.isActiveAndEnabled && e.IsUpAndAbout ? e.transform : null;
    }

    private static Transform AwakeWhiskers()
    {
        CatNPC c = CatNPC.Instance;
        return c != null && c.isActiveAndEnabled && !c.IsAsleep ? c.transform : null;
    }

    // Anyone awake who walks right up to the hiding spot finds Bandit -
    // whatever they were doing (chasing, searching, walking back to the
    // fridge or the sofa). Whiskers then goes after him again.
    private void CheckWalkedIntoHidingSpot()
    {
        Transform cat = AwakeWhiskers();
        if (cat != null && FlatDistance(cat.position) <= DiscoverDistance)
        {
            Discover("Whiskers");
            CatNPC.Instance.WakeUp();
            return;
        }
        Transform edgar = AwakeEdgar();
        if (edgar != null && FlatDistance(edgar.position) <= DiscoverDistance)
        {
            Discover("Edgar");
            NpcVoice.For(HumanNPC.Instance).Say("edgar_found", "Aha! There you are!", 3f, true);
            HumanNPC.Instance.Investigate(player.transform.position);
        }
    }

    private float FlatDistance(Vector3 p)
    {
        Vector3 d = p - player.transform.position; d.y = 0f;
        return d.magnitude;
    }

    private void Update()
    {
        if (isHiding && ActiveHidingSpot == this && player != null)
        {
            CheckWalkedIntoHidingSpot();
            if (isHiding) ShowSafetyStatus();
        }

        // Only the mobile crouch-hiding style needs the wander check - the
        // bag/box style freezes Bandit in place already.
        if (!isHiding || hidePoint != null || player == null) return;

        Vector3 delta = player.transform.position - hideStartPosition;
        delta.y = 0f;
        if (delta.magnitude >= Mathf.Min(stayCloseDistance, maxWanderDistance))
        {
            isHiding = false;
            ExitHiding();
            Debug.Log("Bandit moved out of his hiding spot - visible again.");
            ScreenMessage.Show("You moved out of your hiding spot - you're visible again!", 3f);
        }
    }

    public void Interact()
    {
        isHiding = !isHiding;

        if (isHiding)
        {
            EnterHiding();
        }
        else
        {
            ExitHiding();
        }
    }

    // Called by PlayerController when Bandit presses Jump while hiding -
    // jumping out of a hiding spot doesn't make sense, so treat it as an
    // instant "come out of hiding" instead of performing an actual jump.
    public void ForceExitFromJump()
    {
        if (!isHiding) return;
        isHiding = false;
        ExitHiding();
        Debug.Log("Bandit jumped - popped back out of hiding.");
    }

    // Called by PlayerController when Bandit presses Sprint while hiding -
    // breaking into a run gives him away, so sprinting forces him back out
    // of hiding (visible again) instead of just being blocked.
    public void ForceExitFromSprint()
    {
        if (!isHiding) return;
        isHiding = false;
        ExitHiding();
        Debug.Log("Bandit sprinted - popped back out of hiding.");
    }

    private void EnterHiding()
    {
        PlayerIsHidden = true;
        ActiveHidingSpot = this;

        if (player == null)
        {
            player = GameObject.FindGameObjectWithTag("Player");
        }

        if (player != null)
        {
            playerMovement = player.GetComponent<PlayerController>();
            playerRenderers = player.GetComponentsInChildren<Renderer>();

            if (hidePoint != null)
            {
                // Bag/box-style spot: tuck Bandit fully out of sight and freeze him there.
                CharacterController cc = player.GetComponent<CharacterController>();
                preHidePosition = player.transform.position;
                if (cc != null) cc.enabled = false;
                player.transform.position = hidePoint.position;

                foreach (Renderer r in playerRenderers)
                {
                    r.enabled = false;
                }
            }
            else
            {
                // Crouch/see-through style: Bandit stays mobile, just slower,
                // sneaking, and hard to spot until he wanders off or unhides.
                hideStartPosition = player.transform.position;
                SetTransparent(true);
                if (playerMovement != null) playerMovement.SetHiding(true);
            }
        }

        Debug.Log("Bandit is hiding.");
        ScreenMessage.Show("Hiding! Stay put - anyone who comes right up to you will find you. Press E to come out.", 4f, new Color(0.6f, 0.85f, 1f));
    }

    private void ExitHiding()
    {
        ScreenMessage.SetStatus(null, Color.white);
        PlayerIsHidden = false;
        if (ActiveHidingSpot == this) ActiveHidingSpot = null;

        if (player != null)
        {
            if (hidePoint != null)
            {
                CharacterController cc = player.GetComponent<CharacterController>();
                player.transform.position = preHidePosition;
                if (cc != null) cc.enabled = true;

                if (playerRenderers != null)
                {
                    foreach (Renderer r in playerRenderers)
                    {
                        if (r != null) r.enabled = true;
                    }
                }
            }
            else
            {
                SetTransparent(false);
                if (playerMovement != null) playerMovement.SetHiding(false);
            }
        }

        Debug.Log("Bandit stopped hiding - visible again, free to move and sneak normally.");
    }

    // Fades Bandit down to a translucent "invisibility potion" look instead of
    // just switching renderers off, so he's still faintly visible while hidden.
    private void SetTransparent(bool hidden)
    {
        if (playerRenderers == null) return;

        if (hidden)
        {
            if (savedSharedMaterials != null) return; // already faded
            playerMaterials = new Material[playerRenderers.Length];
            originalColors = new Color[playerRenderers.Length];
            savedSharedMaterials = new Material[playerRenderers.Length][];
            for (int i = 0; i < playerRenderers.Length; i++)
                if (playerRenderers[i] != null) savedSharedMaterials[i] = playerRenderers[i].sharedMaterials;

            for (int i = 0; i < playerRenderers.Length; i++)
            {
                Renderer r = playerRenderers[i];
                if (r == null) continue;

                // Brand-new copies for the see-through look. (r.material was
                // NOT a copy here: PlayerHealth had already made Bandit's
                // materials instances, so r.material handed back those same
                // ones and fading them changed the "originals" we put back.)
                Material[] orig = r.sharedMaterials;
                Material[] faded = new Material[orig.Length];
                for (int j = 0; j < orig.Length; j++)
                {
                    if (orig[j] == null) continue;
                    Material mat = new Material(orig[j]);
                    Color c = mat.HasProperty("_Color") ? mat.color : Color.white;
                    SetupTransparentRenderMode(mat);
                    c.a = hiddenAlpha;
                    if (mat.HasProperty("_Color")) mat.color = c;
                    faded[j] = mat;
                }
                r.sharedMaterials = faded;
                playerMaterials[i] = faded.Length > 0 ? faded[0] : null;
                originalColors[i] = Color.white;
            }
        }
        else
        {
            // Put the exact original materials back. Only resetting the alpha
            // left them in see-through (Fade) mode, so Bandit stayed
            // translucent/"ghostly" after coming out of hiding.
            if (savedSharedMaterials != null)
            {
                for (int i = 0; i < playerRenderers.Length && i < savedSharedMaterials.Length; i++)
                {
                    if (playerRenderers[i] == null || savedSharedMaterials[i] == null) continue;
                    Material[] faded = playerRenderers[i].sharedMaterials;
                    playerRenderers[i].sharedMaterials = savedSharedMaterials[i];
                    foreach (Material m in faded)
                        if (m != null && System.Array.IndexOf(savedSharedMaterials[i], m) < 0) Destroy(m);
                }
                playerMaterials = null;
                savedSharedMaterials = null;
                return;
            }

            if (playerMaterials == null) return;

            for (int i = 0; i < playerMaterials.Length; i++)
            {
                Material mat = playerMaterials[i];
                if (mat == null) continue;

                Color c = originalColors[i];
                c.a = 1f;
                mat.color = c;
            }
        }
    }

    // Standard shader defaults to Opaque, which ignores alpha entirely - switch
    // it into Transparent/Fade mode so the reduced alpha actually shows through.
    private void SetupTransparentRenderMode(Material mat)
    {
        if (!mat.HasProperty("_Mode")) return; // not the Standard shader - leave it alone

        mat.SetFloat("_Mode", 3); // Transparent
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
    }

    public bool IsHiding() => isHiding;
}
