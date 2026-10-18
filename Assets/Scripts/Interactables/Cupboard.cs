using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// A kitchen cupboard Bandit can open to find food inside. Only a cupboard with foods listed in "Stored Foods" opens.
public class Cupboard : MonoBehaviour, IInteractable
{
    [Tooltip("Food names kept in this cupboard (must match each FoodItem's Food Name). Empty = a normal, closed cupboard.")]
    [SerializeField] private string[] storedFoods = new string[0];
    [Tooltip("The cupboard that gets food if none has been set up in the Inspector.")]
    [SerializeField] private string defaultFoodCupboardName = "kitchenCabinet";
    private static readonly string[] DefaultFoods = { "Cookie", "Toast" };

    [SerializeField] private float openAngle = 95f;   // lift-up door: ~flat above the opening
    [SerializeField] private float swingTime = 0.4f;
    [Tooltip("How far from the front of the cupboard Bandit can use it (added to half the door width).")]
    [SerializeField] private float extraUseDistance = 1.6f;

    private static readonly List<Cupboard> all = new List<Cupboard>();
    private static readonly HashSet<string> foodsInCupboards = new HashSet<string>();
    /// Used by the checklist to add "(in cupboard)".
    public static bool IsInACupboard(string foodName) => foodsInCupboards.Contains(foodName);

    private bool active;           // has food, so it works
    private bool isOpen;
    private int lastInteractFrame = -1;
    private Transform door;
    private Vector3 doorClosedPos;
    private Quaternion doorClosedRot;
    private Vector3 hingePoint;
    private float swingSign = 1f;
    private float currentAngle;
    private Coroutine swing;

    private Vector3 frontNormal;   // out of the cupboard, flat
    private Vector3 frontCentre;   // centre of the closed door
    private Vector3 widthAxis;     // along the door, flat
    private float doorWidth, doorHeight, doorBottomY;

    private GameObject shelf;
    private readonly List<FoodItem> contents = new List<FoodItem>();
    private TextMesh prompt;
    private Transform player;

    private void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    private void OnDisable() { all.Remove(this); }

    private void Awake()
    {
        if ((storedFoods == null || storedFoods.Length == 0) && gameObject.name == defaultFoodCupboardName)
            storedFoods = DefaultFoods;
        active = storedFoods != null && storedFoods.Length > 0 && FindDoor();
        if (active)
            foreach (string f in storedFoods) foodsInCupboards.Add(f);
    }

    private void OnDestroy()
    {
        if (storedFoods != null) foreach (string f in storedFoods) foodsInCupboards.Remove(f);
        if (prompt != null) Destroy(prompt.gameObject);
    }

    private void Start()
    {
        if (!active) return;
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) player = p.transform;
        MeasureDoor();
        MakeSolid();
        BuildShelf();
        PutFoodInside();
        ShowInside(false);
        Debug.Log("Cupboard '" + name + "' holds: " + string.Join(", ", storedFoods));
    }

    // Setup
    private bool FindDoor()
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            if (t != transform && t.name.ToLowerInvariant() == "door" && t.GetComponent<Renderer>() != null) { door = t; return true; }
        Debug.LogWarning("Cupboard '" + name + "' has food set but no 'door' part to open.");
        return false;
    }

    private void MeasureDoor()
    {
        doorClosedPos = door.position;
        doorClosedRot = door.rotation;

        // The door's three axes in the world, and its size along each.
        MeshFilter mf = door.GetComponent<MeshFilter>();
        Bounds lb = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one * 0.1f);
        Vector3[] axes = { door.right, door.up, door.forward };
        float[] sizes = {
            Mathf.Abs(lb.size.x * door.lossyScale.x),
            Mathf.Abs(lb.size.y * door.lossyScale.y),
            Mathf.Abs(lb.size.z * door.lossyScale.z) };
        int up = 0, thin = 0;
        for (int i = 0; i < 3; i++)
        {
            if (Mathf.Abs(Vector3.Dot(axes[i], Vector3.up)) > Mathf.Abs(Vector3.Dot(axes[up], Vector3.up))) up = i;
        }
        thin = up == 0 ? 1 : 0;
        for (int i = 0; i < 3; i++) if (i != up && sizes[i] < sizes[thin]) thin = i;
        int wide = 3 - up - thin;

        doorHeight = sizes[up];
        doorWidth = sizes[wide];
        Renderer dr = door.GetComponent<Renderer>();
        frontCentre = dr.bounds.center;
        doorBottomY = dr.bounds.min.y;

        // Which way is "out"? Away from the middle of the cupboard's body.
        Bounds body = BodyBounds();
        Vector3 n = axes[thin]; n.y = 0f; n.Normalize();
        Vector3 fromBody = frontCentre - body.center; fromBody.y = 0f;
        if (Vector3.Dot(n, fromBody) < 0f) n = -n;
        frontNormal = n;
        widthAxis = axes[wide]; widthAxis.y = 0f; widthAxis.Normalize();

        // Hinge on whichever side has room for the door to swing open.
        float best = float.MaxValue;
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 hinge = frontCentre + widthAxis * side * doorWidth * 0.5f + frontNormal * sizes[thin] * 0.5f;
            foreach (float sign in new[] { -1f, 1f })
            {
                // Where the middle of the door ends up when open this way.
                Vector3 mid = hinge + Quaternion.AngleAxis(openAngle * sign, Vector3.up) * (frontCentre - hinge);
                Vector3 moved = mid - frontCentre; moved.y = 0f;
                if (Vector3.Dot(moved, frontNormal) <= 0f) continue;   // would swing into the cupboard
                int blockers = 0;
                foreach (Collider c in Physics.OverlapBox(mid, new Vector3(doorWidth * 0.4f, doorHeight * 0.4f, doorWidth * 0.4f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                    if (!c.transform.IsChildOf(transform) && !c.CompareTag("Player")) blockers++;
                if (blockers < best) { best = blockers; hingePoint = hinge; swingSign = sign; }
            }
        }
    }

    private Bounds BodyBounds()
    {
        Bounds b = new Bounds(transform.position, Vector3.zero); bool any = false;
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r.transform == door || r.transform.IsChildOf(door)) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    // Solid body (so Bandit can't walk into it) and a solid door that moves.
    private void MakeSolid()
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r.transform == door) continue;
            bool has = false;
            foreach (Collider c in r.GetComponents<Collider>()) if (c.enabled && !c.isTrigger) has = true;
            if (has) continue;
            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            BoxCollider bc = r.gameObject.AddComponent<BoxCollider>();
            bc.center = mf.sharedMesh.bounds.center;
            bc.size = mf.sharedMesh.bounds.size;
        }
        bool doorHas = false;
        foreach (Collider c in door.GetComponents<Collider>()) if (c.enabled && !c.isTrigger) doorHas = true;
        if (!doorHas)
        {
            MeshFilter mf = door.GetComponent<MeshFilter>();
            BoxCollider bc = door.gameObject.AddComponent<BoxCollider>();
            if (mf != null && mf.sharedMesh != null) { bc.center = mf.sharedMesh.bounds.center; bc.size = mf.sharedMesh.bounds.size; }
        }
        // Tell Whiskers' and Edgar's walkable-area maps about the door, even
        // when it swings open, so they path round it instead of into it.
        UnityEngine.AI.NavMeshObstacle obs = door.gameObject.GetComponent<UnityEngine.AI.NavMeshObstacle>();
        if (obs == null)
        {
            BoxCollider box = door.gameObject.GetComponent<BoxCollider>();
            obs = door.gameObject.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            obs.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
            MeshFilter omf = door.gameObject.GetComponent<MeshFilter>();
            if (box != null) { obs.center = box.center; obs.size = box.size; }
            else if (omf != null && omf.sharedMesh != null) { obs.center = omf.sharedMesh.bounds.center; obs.size = omf.sharedMesh.bounds.size; }
            obs.carving = true;
            obs.carveOnlyStationary = false;
        }
        Rigidbody rb = door.GetComponent<Rigidbody>();
        if (rb == null) rb = door.gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    // A dark inside and a shelf that slides into view when the door opens.
    private void BuildShelf()
    {
        shelf = new GameObject("CupboardInside");
        shelf.transform.position = frontCentre;
        shelf.transform.rotation = Quaternion.LookRotation(frontNormal, Vector3.up);

        GameObject back = GameObject.CreatePrimitive(PrimitiveType.Cube);
        back.name = "DarkInside";
        Destroy(back.GetComponent<Collider>());
        back.transform.SetParent(shelf.transform, false);
        back.transform.localPosition = Vector3.zero;
        back.transform.localScale = new Vector3(doorWidth * 0.94f, doorHeight * 0.94f, 0.02f);
        back.GetComponent<Renderer>().material.color = new Color(0.07f, 0.05f, 0.04f);

        float depth = Mathf.Clamp(doorHeight * 0.18f, 0.35f, 0.8f);
        GameObject board = GameObject.CreatePrimitive(PrimitiveType.Cube);
        board.name = "Shelf";
        board.transform.SetParent(shelf.transform, false);
        float boardTop = doorBottomY - frontCentre.y;
        board.transform.localPosition = new Vector3(0f, boardTop - 0.03f, depth * 0.5f);
        board.transform.localScale = new Vector3(doorWidth * 0.94f, 0.06f, depth);
        board.GetComponent<Renderer>().material.color = new Color(0.72f, 0.55f, 0.4f);
    }

    // Moves this cupboard's foods onto the shelf, side by side.
    private void PutFoodInside()
    {
        FoodItem[] foods = FindObjectsByType<FoodItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        List<string> wanted = new List<string>(storedFoods);
        foreach (FoodItem f in foods)
        {
            if (!wanted.Contains(f.FoodName)) continue;
            wanted.Remove(f.FoodName);
            contents.Add(f);
        }
        float depth = Mathf.Clamp(doorHeight * 0.18f, 0.35f, 0.8f);
        for (int i = 0; i < contents.Count; i++)
        {
            FoodItem f = contents[i];
            Transform t = f.transform;
            if (t.parent != null && t.parent.GetComponentInParent<Fridge>() != null) t.SetParent(null, true);

            // Not too big for the shelf.
            Bounds b = FoodBounds(f);
            float maxSize = Mathf.Min(depth * 0.8f, doorWidth / (contents.Count + 1) * 0.8f, doorHeight * 0.5f);
            float biggest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (biggest > maxSize && biggest > 0.0001f) { t.localScale *= maxSize / biggest; b = FoodBounds(f); }

            float across = ((i + 1f) / (contents.Count + 1f) - 0.5f) * doorWidth * 0.9f;
            Vector3 spot = frontCentre + widthAxis * across + frontNormal * (depth * 0.5f);
            spot.y = doorBottomY;
            t.position += spot - new Vector3(b.center.x, b.min.y, b.center.z);

            Rigidbody rb = f.GetComponent<Rigidbody>();
            if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }
        }
    }

    private static Bounds FoodBounds(FoodItem f)
    {
        Bounds b = new Bounds(f.transform.position, Vector3.zero); bool any = false;
        foreach (Renderer r in f.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    private void ShowInside(bool show)
    {
        if (shelf != null) shelf.SetActive(show);
        foreach (FoodItem f in contents)
        {
            if (f == null || f.IsBeingCollected) continue;
            bool eaten = GameManager.Instance != null && GameManager.Instance.collectedFood.Contains(f.FoodName);
            f.gameObject.SetActive(show && !eaten && !f.IsKnockedOver);
        }
    }

    // Using it
    public static bool TryHandleNearby(Vector3 playerPos)
    {
        Cupboard best = null; float bestD = float.MaxValue;
        foreach (Cupboard c in all)
        {
            float d;
            if (c != null && c.active && c.PlayerInReach(playerPos, out d) && d < bestD) { best = c; bestD = d; }
        }
        if (best == null) return false;
        best.Use();
        return true;
    }

    public void Interact()
    {
        if (!active) { Debug.Log("Bandit checked the cupboard - nothing in there."); return; }
        Use();
    }

    private void Use()
    {
        if (lastInteractFrame == Time.frameCount) return;
        lastInteractFrame = Time.frameCount;

        if (isOpen && PlayerLookingIn())
        {
            FoodItem next = NextFoodOnList();
            if (next != null) { next.Interact(); return; }
        }
        SetOpen(!isOpen);
    }

    private FoodItem NextFoodOnList()
    {
        foreach (FoodItem f in contents)
        {
            if (f == null || !f.gameObject.activeInHierarchy || f.IsBeingCollected || f.IsKnockedOver) continue;
            if (!f.IsOnShoppingList) continue;
            return f;
        }
        return null;
    }

    private void SetOpen(bool open)
    {
        isOpen = open;
        Debug.Log(open ? "Bandit opened the cupboard." : "Bandit closed the cupboard.");
        SoundFX.Play(open ? "cupboard_open" : "cupboard_close", 0.8f);
        if (!open) ShowInside(false);
        if (swing != null) StopCoroutine(swing);
        swing = StartCoroutine(Swing(open ? openAngle : 0f));
    }

    private IEnumerator Swing(float target)
    {
        float start = currentAngle;
        float t = 0f;
        while (t < swingTime)
        {
            t += Time.deltaTime;
            currentAngle = Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / swingTime)));
            PoseDoor();
            yield return null;
        }
        currentAngle = target;
        PoseDoor();
        ShowInside(target > 0f);   // food/shelf appear once the door is up (it sweeps through there)
        swing = null;
    }

    private void SetDoorSolid(bool solid)
    {
        foreach (Collider c in door.GetComponentsInChildren<Collider>())
            if (!c.isTrigger) c.enabled = solid;
    }

    // The door is hinged along its top edge (like a lift-up kitchen cabinet):
    // it swings up and out, ending up flat ABOVE the opening, so it's still
    // solid but never in Bandit's way - he walks straight up to the shelf.
    private void SetUpLiftHinge()
    {
        Vector3 top = frontCentre + Vector3.up * doorHeight * 0.5f + frontNormal * 0.02f;
        liftHinge = top;
        // Which way round the width axis lifts the door OUT (not into the cupboard)?
        Quaternion test = Quaternion.AngleAxis(60f, widthAxis);
        Vector3 moved = test * (frontCentre - top) + top - frontCentre;
        liftSign = Vector3.Dot(moved, frontNormal) > 0f ? 1f : -1f;
    }
    private Vector3 liftHinge;
    private float liftSign = 1f;

    private void PoseDoor()
    {
        if (liftHinge == Vector3.zero) SetUpLiftHinge();
        Quaternion lift = Quaternion.AngleAxis(currentAngle * liftSign, widthAxis);
        door.SetPositionAndRotation(liftHinge + lift * (doorClosedPos - liftHinge), lift * doorClosedRot);
    }

    private bool PlayerInReach(Vector3 playerPos, out float distance)
    {
        Vector3 to = playerPos - frontCentre; to.y = 0f;
        distance = to.magnitude;
        if (distance > doorWidth * 0.5f + extraUseDistance) return false;
        return Vector3.Dot(to, frontNormal) > 0f;   // in front, not behind/beside
    }

    private bool PlayerLookingIn()
    {
        if (player == null) return true;
        Vector3 f = player.forward; f.y = 0f;
        if (f.sqrMagnitude < 0.0001f) return true;
        return Vector3.Dot(f.normalized, -frontNormal) > 0.3f;
    }

    // Floating "Cupboard" sign nearby, and what E will do when in reach.
    private void Update()
    {
        if (!active || player == null) return;
        if (prompt == null)
        {
            GameObject g = new GameObject("CupboardPrompt");
            prompt = g.AddComponent<TextMesh>();
            prompt.fontSize = 60;
            prompt.characterSize = 0.05f;
            prompt.anchor = TextAnchor.LowerCenter;
            prompt.alignment = TextAlignment.Center;
            prompt.fontStyle = FontStyle.Bold;
            prompt.color = new Color(1f, 0.85f, 0.4f);
        }
        Vector3 to = player.position - frontCentre; to.y = 0f;
        float d;
        bool inReach = PlayerInReach(player.position, out d);
        // Only shown when Bandit is right at the cupboard, not through walls/furniture.
        prompt.gameObject.SetActive(inReach);
        if (!inReach) return;

        string text;
        if (!isOpen) text = "Press E to open cupboard";
        else if (NextFoodOnList() != null && PlayerLookingIn()) text = "Press E to grab food\n(turn away + E to close)";
        else text = "Press E to close cupboard";
        prompt.text = text;

        Vector3 pos = frontCentre + frontNormal * 0.4f;
        pos.y = Mathf.Max(doorBottomY + doorHeight * 0.5f, player.position.y + 2.2f);
        prompt.transform.position = pos;
        if (Camera.main != null) prompt.transform.forward = Camera.main.transform.forward;
    }
}
