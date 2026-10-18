using System.Collections;
using UnityEngine;

public class FoodItem : MonoBehaviour, IInteractable
{
    [SerializeField] private string foodName;
    public string FoodName => foodName;

    [Header("Interact Label")]
    [Tooltip("How far above the object centre (in world units) the label floats.")]
    [SerializeField] private float labelHeight = 0.55f;
    [Tooltip("Horizontal (X/Z) distance at which the label appears.")]
    [SerializeField] private float showDistance = 3f;
    [Tooltip("Font size for the label texture - higher = crisper but uses more memory.")]
    [SerializeField] private int labelFontSize = 60;
    [Tooltip("World-space size of each character in metres.")]
    [SerializeField] private float labelCharacterSize = 0.038f;

    [Header("Collect Animation")]
    [Tooltip("When collected, the food is visibly pulled out (e.g. out of the fridge) and flies into Bandit instead of just vanishing.")]
    [SerializeField] private bool animateCollect = true;
    [SerializeField] private float pullOutTime = 0.3f;
    [SerializeField] private float flyTime = 0.35f;

    [Header("Placement helpers (for food added from raw models)")]
    [Tooltip("If above 0, the model is uniformly scaled on Play so its biggest side is this many world units.")]
    [SerializeField] private float fitToSize = 0f;
    [Tooltip("If ticked, the item's position is treated as the spot on the counter: on Play its BOTTOM is set down there (and snapped onto any surface collider just below).")]
    [SerializeField] private bool restOnPosition = false;

    [Header("Wrong item (not on this game's shopping list)")]
    [Tooltip("Impulse used when Bandit knocks a wrong item over.")]
    [SerializeField] private float knockForce = 1.5f;

    // True while the pull-out / fly-to-Bandit animation is playing.
    public bool IsBeingCollected { get; private set; }

    // True once Bandit has grabbed this as a WRONG item and knocked it over.
    public bool IsKnockedOver { get; private set; }

    // Every food in the scene, so E can pick the one Bandit is standing at
    // without having to aim the camera exactly at it.
    private static readonly System.Collections.Generic.List<FoodItem> all = new System.Collections.Generic.List<FoodItem>();
    private void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    private void OnDisable() { all.Remove(this); }

    // The ONE food E will take right now (only its label is shown), so the
    // player always knows exactly what they're about to grab.
    public static FoodItem CurrentTarget { get; private set; }
    private const float ReachDistance = 3f;     // how far (flat) Bandit can reach - same as the label distance

    /// Called every frame by PlayerInteract: picks the food in reach that's
    /// most in front of Bandit, then the closest.
    public static void UpdateTarget(Transform playerT)
    {
        FoodItem best = null;
        float bestScore = float.MaxValue;
        foreach (FoodItem f in all)
        {
            if (f == null || !f.isActiveAndEnabled || f.IsBeingCollected || f.IsKnockedOver) continue;
            Vector3 d = f.transform.position - playerT.position;
            // Only food Bandit can actually reach from where he's standing -
            // not food up on a counter or shelf while he's on the floor.
            if (d.y > 1.6f || d.y < -1.0f) continue;
            Vector3 flat = new Vector3(d.x, 0f, d.z);
            float dist = flat.magnitude;
            if (dist > ReachDistance) continue;
            float facing = dist > 0.01f ? Vector3.Dot(playerT.forward, flat / dist) : 1f;
            // Facing only breaks ties between several foods - it never hides
            // the label, so the camera angle doesn't matter.
            float score = dist - facing * 0.5f;
            if (score < bestScore) { bestScore = score; best = f; }
        }
        CurrentTarget = best;
    }

    /// Called by PlayerInteract on E: takes the current target, if any.
    public static bool TryCollectNearby(Transform playerT)
    {
        UpdateTarget(playerT);
        FoodItem best = CurrentTarget;
        if (best == null) return false;
        Debug.Log("Hit: " + best.name + " (nearby food)");
        best.Interact();
        return true;
    }

    // Shopping list lookup: is this one of this game's required foods?
    public bool IsOnShoppingList =>
        GameManager.Instance == null || !GameManager.Instance.shoppingListRolled ||
        GameManager.Instance.requiredFood.Contains(foodName);

    // Private state
    private TextMesh label;
    private Transform playerTransform;

    // Setup
    private void Awake()
    {
        FitAndPlace();
        FixColliders();

        // The label lives at SCENE ROOT - no SetParent - so it never
        // inherits the food item's scale. Works correctly whether the
        // item is scale 50 (Donut), scale 0.1 (Cookie) or anything else.
        // We reposition it manually every LateUpdate instead.
        GameObject labelObj = new GameObject("InteractLabel_" + gameObject.name);
        // DO NOT call labelObj.transform.SetParent(transform)

        label = labelObj.AddComponent<TextMesh>();
        label.text          = "Press E to collect";
        label.fontSize      = labelFontSize;
        label.characterSize = labelCharacterSize;   // raw world-space - no division needed
        label.anchor        = TextAnchor.LowerCenter;
        label.alignment     = TextAlignment.Center;
        label.color         = new Color(0.3f, 0.65f, 0.35f);   // light vibrant green
        label.fontStyle     = FontStyle.Bold;

        // Start hidden - shown only when the player is close.
        label.gameObject.SetActive(false);
    }

    private void Start()
    {
        // Cache the player transform once; re-find it if somehow null.
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
            playerTransform = playerObj.transform;
    }

    // Per-frame
    private void LateUpdate()
    {
        if (label == null) return;

        if (IsBeingCollected || IsKnockedOver)
        {
            label.gameObject.SetActive(false);
            return;
        }

        // Lazy-init if the player wasn't ready in Start().
        if (playerTransform == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null) playerTransform = playerObj.transform;
        }

        // Use horizontal (X/Z) distance only - ignores height differences
        // so a food item on a counter still shows its label correctly.
        bool nearPlayer = false;
        if (playerTransform != null)
        {
            Vector3 myPos     = transform.position;
            Vector3 playerPos = playerTransform.position;
            float dx = Mathf.Abs(myPos.x - playerPos.x);
            float dz = Mathf.Abs(myPos.z - playerPos.z);
            nearPlayer = dx <= showDistance && dz <= showDistance;
        }

        // Only the food E would actually take shows its label.
        nearPlayer = nearPlayer && CurrentTarget == this;
        if (nearPlayer) label.text = "Press E to take " + foodName;
        label.gameObject.SetActive(nearPlayer);

        if (nearPlayer)
        {
            // Position the label in world space, directly above this food item.
            // labelHeight is in world units - correct regardless of our own scale.
            label.transform.position = transform.position + Vector3.up * labelHeight;

            // Billboard: keep the text facing the camera every frame.
            if (Camera.main != null)
                label.transform.forward = Camera.main.transform.forward;
        }
    }

    // Cleanup
    private void OnDestroy()
    {
        // The label is not a child, so Unity won't auto-destroy it.
        // Clean it up ourselves to avoid stray objects in the scene.
        if (label != null)
            Destroy(label.gameObject);
    }

    // Collection
    public void Interact()
    {
        if (IsBeingCollected || IsKnockedOver) return;

        // Not on this game's shopping list - Bandit fumbles it and makes a racket.
        if (!IsOnShoppingList)
        {
            KnockOver();
            return;
        }

        // Hide the label explicitly (it's no longer a child).
        if (label != null)
            label.gameObject.SetActive(false);

        GameManager.Instance.CollectFood(foodName);
        SoundFX.Play("collect", 0.7f, 0f);

        if (animateCollect && isActiveAndEnabled)
            StartCoroutine(CollectRoutine());
        else
            gameObject.SetActive(false);
    }

    // Pulls the food out towards Bandit (e.g. the milk sliding out of the
    // fridge), then it flies into him while shrinking, and only then hides.
    private IEnumerator CollectRoutine()
    {
        IsBeingCollected = true;

        foreach (Collider c in GetComponentsInChildren<Collider>())
            c.enabled = false;

        Vector3 startPos = transform.position;
        Vector3 startScale = transform.localScale;
        Quaternion startRot = transform.rotation;

        // Size of the item in world units, so the motion suits big and small food.
        float itemSize = 0.5f;
        Renderer[] rends = GetComponentsInChildren<Renderer>();
        if (rends.Length > 0)
        {
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            itemSize = Mathf.Max(0.1f, b.size.magnitude);
        }

        if (playerTransform == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null) playerTransform = playerObj.transform;
        }

        Vector3 towardPlayer = playerTransform != null
            ? playerTransform.position - startPos
            : (Camera.main != null ? -Camera.main.transform.forward : Vector3.back);
        towardPlayer.y = 0f;
        if (towardPlayer.sqrMagnitude < 0.0001f) towardPlayer = Vector3.back;
        towardPlayer.Normalize();

        // Phase 1: slide out towards Bandit and lift a little.
        Vector3 pulledPos = startPos + towardPlayer * itemSize * 0.9f + Vector3.up * itemSize * 0.25f;
        Quaternion tiltRot = Quaternion.AngleAxis(-18f, Vector3.Cross(Vector3.up, towardPlayer)) * startRot;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, pullOutTime);
            float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f); // ease-out
            transform.position = Vector3.LerpUnclamped(startPos, pulledPos, e);
            transform.rotation = Quaternion.Slerp(startRot, tiltRot, e);
            yield return null;
        }

        // Phase 2: fly into Bandit, shrinking as he "grabs" it.
        t = 0f;
        Vector3 from = transform.position;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, flyTime);
            float e = Mathf.Clamp01(t) * Mathf.Clamp01(t); // ease-in

            Vector3 target = GetGrabPoint();
            Vector3 arc = Vector3.up * Mathf.Sin(e * Mathf.PI) * itemSize * 0.3f;
            transform.position = Vector3.Lerp(from, target, e) + arc;
            transform.localScale = Vector3.Lerp(startScale, startScale * 0.15f, e);
            yield return null;
        }

        IsBeingCollected = false;
        transform.localScale = startScale;
        gameObject.SetActive(false);
    }

    private Vector3 GetGrabPoint()
    {
        // Food goes straight into Bandit's bag if he's wearing it.
        if (BanditBackpack.Current != null)
            return BanditBackpack.Current.MouthPosition;

        if (playerTransform == null)
            return transform.position;

        CharacterController cc = playerTransform.GetComponent<CharacterController>();
        if (cc != null) return cc.bounds.center;

        Collider col = playerTransform.GetComponent<Collider>();
        if (col != null) return col.bounds.center;

        return playerTransform.position + Vector3.up * 0.3f;
    }

    // Wrong item: knocked over, makes noise
    private void KnockOver()
    {
        IsKnockedOver = true;
        SoundFX.Play("wrong_item", 0.9f);
        if (label != null) label.gameObject.SetActive(false);

        // No longer interactable.
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = 0;

        foreach (MeshCollider mc in GetComponentsInChildren<MeshCollider>())
            mc.convex = true;
        if (GetComponentInChildren<Collider>() == null)
            AddFittedCollider();

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.mass = 0.3f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        Vector3 dir = Vector3.zero;
        if (playerTransform != null) dir = playerTransform.position - transform.position;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Random.insideUnitSphere;
        dir.y = 0f;
        rb.AddForce((dir * knockForce + Vector3.up * knockForce * 0.8f) * rb.mass, ForceMode.Impulse);
        rb.AddTorque(Random.onUnitSphere * knockForce * 0.3f * rb.mass, ForceMode.Impulse);

        FoodShoppingList.ReportWrongItem(this);
        StartCoroutine(SettleAfter(4f));
    }

    private IEnumerator SettleAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;
    }

    // Placement helpers
    private void FitAndPlace()
    {
        // Every food needs a collider for the E-key raycast. If none was
        // added in the Editor (e.g. the Pasta inside the fridge, which is
        // hard to reach), one is fitted around the model automatically.
        // Colliders you add yourself are always kept as they are.
        if (fitToSize <= 0f && !restOnPosition)
        {
            if (GetComponentInChildren<Collider>(true) == null)
                AddFittedCollider();
            return;
        }

        Bounds b;
        if (!TryGetBounds(out b)) return;

        if (fitToSize > 0f)
        {
            float biggest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (biggest > 0.0001f)
            {
                transform.localScale *= fitToSize / biggest;
                TryGetBounds(out b);
            }
        }

        if (restOnPosition)
        {
            Vector3 spot = transform.position;

            // Snap onto a counter/table collider just below the spot, if there is one.
            RaycastHit[] hits = Physics.RaycastAll(spot + Vector3.up * 1f, Vector3.down, 2.5f, ~0, QueryTriggerInteraction.Ignore);
            float bestY = float.NegativeInfinity;
            foreach (RaycastHit h in hits)
            {
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.collider.CompareTag("Player")) continue;
                if (h.point.y > bestY) bestY = h.point.y;
            }
            float surfaceY = float.IsNegativeInfinity(bestY) ? spot.y : bestY;

            Vector3 bottomCentre = new Vector3(b.center.x, b.min.y, b.center.z);
            transform.position += new Vector3(spot.x, surfaceY + 0.005f, spot.z) - bottomCentre;
        }

        if (GetComponentInChildren<Collider>() == null)
            AddFittedCollider();
    }

    private void AddFittedCollider()
    {
        Bounds wb;
        if (!TryGetBounds(out wb)) return;
        BoxCollider bc = gameObject.AddComponent<BoxCollider>();
        Vector3 lossy = transform.lossyScale;
        bc.center = transform.InverseTransformPoint(wb.center);
        bc.size = new Vector3(
            wb.size.x / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
            wb.size.y / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
            wb.size.z / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));
    }

    private bool TryGetBounds(out Bounds b)
    {
        b = new Bounds();
        bool any = false;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        return any;
    }

    // Makes sure the food is solid (Bandit bumps into it). Mesh colliders
    // with no mesh, or with another object's mesh (the Muffin had the fish
    // tank's), are swapped for a box that fits the food.
    private void FixColliders()
    {
        bool good = false;
        foreach (Collider c in GetComponentsInChildren<Collider>(true))
        {
            MeshCollider mc = c as MeshCollider;
            if (mc != null)
            {
                MeshFilter mf = mc.GetComponent<MeshFilter>();
                bool ownMesh = mc.sharedMesh != null && mf != null && mf.sharedMesh == mc.sharedMesh;
                if (!ownMesh) { Destroy(mc); continue; }
            }
            if (c.enabled && !c.isTrigger) good = true;
        }
        if (!good) AddFittedCollider();
    }
}
