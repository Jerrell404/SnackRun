using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Bandit's loot bag.
public class BanditBackpack : MonoBehaviour
{
    public static BanditBackpack Current { get; private set; }

    [Header("Placement (tweak if the bag sits wrong on Bandit)")]
    [Tooltip("Name of the body part the bag is strapped to.")]
    public string attachToPart = "Torso";
    [Tooltip("Bag height as a fraction of Bandit's torso height.")]
    public float sizeVsTorso = 0.6f;
    [Tooltip("Extra nudge in Bandit's own space (x = right, y = up, z = forward), in torso heights.")]
    public Vector3 offset = new Vector3(0f, 0.05f, 0f);

    [Header("Look")]
    public Color bagColour = new Color(0.47f, 0.31f, 0.18f);
    public Color flapColour = new Color(0.36f, 0.23f, 0.13f);
    public Color strapColour = new Color(0.2f, 0.14f, 0.09f);
    public Color buckleColour = new Color(0.9f, 0.75f, 0.25f);

    [Header("Filling")]
    [Tooltip("Seconds after collecting before the food shows in the bag (matches the food flying into it).")]
    public float appearDelay = 0.6f;
    [Tooltip("How many of the most recently collected foods peek out of the top.")]
    public int visibleLoot = 2;

    private Transform bagRoot;      // scaled so 1 unit = bag height
    private Transform body;         // the part that bulges as it fills
    private Transform lootRoot;     // food sticking out the top
    private Transform mouth;        // where food flies into
    private readonly List<string> shownFood = new List<string>();
    private readonly List<Transform> lootItems = new List<Transform>(); // only the last few, oldest first
    private readonly List<KeyValuePair<string, float>> pending = new List<KeyValuePair<string, float>>();
    private float popTimer = 1f;
    private bool built;

    private static readonly Vector3 EmptyBody = new Vector3(0.72f, 0.78f, 0.30f);
    private static readonly Vector3 FullBody = new Vector3(0.86f, 0.92f, 0.55f);

    // ---------------------------------------------------------------------
    //  Auto-attach to the Player in every scene
    // ---------------------------------------------------------------------
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        AttachToPlayer();
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m) => AttachToPlayer();

    private static void AttachToPlayer()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null && player.GetComponent<BanditBackpack>() == null)
            player.AddComponent<BanditBackpack>();
    }

    // ---------------------------------------------------------------------

    private void OnEnable() { Current = this; }
    private void OnDisable() { if (Current == this) Current = null; }

    private void Start()
    {
        Build();

        // Whatever was already collected (e.g. arriving in the Backyard) shows straight away.
        if (GameManager.Instance != null)
        {
            foreach (string food in GameManager.Instance.collectedFood)
                AddLoot(food, false);
        }
        UpdateBulge(false);
    }

    private void Update()
    {
        if (!built) return;

        GameManager gm = GameManager.Instance;
        if (gm != null)
        {
            // Game restarted - empty the bag.
            if (gm.collectedFood.Count < shownFood.Count)
                ClearLoot();

            foreach (string food in gm.collectedFood)
            {
                if (shownFood.Contains(food) || pending.Exists(p => p.Key == food)) continue;
                pending.Add(new KeyValuePair<string, float>(food, Time.time + appearDelay));
            }
        }

        for (int i = pending.Count - 1; i >= 0; i--)
        {
            if (Time.time >= pending[i].Value)
            {
                AddLoot(pending[i].Key, true);
                pending.RemoveAt(i);
            }
        }

        // little "stuffed it in" bounce
        if (popTimer < 1f)
        {
            popTimer += Time.deltaTime / 0.35f;
            float k = 1f + Mathf.Sin(Mathf.Clamp01(popTimer) * Mathf.PI) * 0.12f;
            bagRoot.localScale = baseScale * k;
        }
    }

    /// World position of the bag's opening (FoodItem flies collected food here).
    public Vector3 MouthPosition => mouth != null ? mouth.position : transform.position;

    // ---------------------------------------------------------------------
    //  Building the bag
    // ---------------------------------------------------------------------
    private Vector3 baseScale;

    private void Build()
    {
        Transform torso = FindPart(transform, attachToPart);
        Renderer torsoRenderer = torso != null ? torso.GetComponent<Renderer>() : null;
        if (torso == null || torsoRenderer == null)
        {
            Debug.LogWarning("BanditBackpack: couldn't find '" + attachToPart + "' on the Player - bag not added.");
            enabled = false;
            return;
        }

        // Torso size/position in the Player's own space (Bandit faces +Z).
        Bounds tb = ToLocalBounds(transform, torsoRenderer.bounds);
        float h = tb.size.y;
        float bagH = h * sizeVsTorso;

        Vector3 localPos = new Vector3(
            tb.center.x + offset.x * h,
            tb.center.y + offset.y * h,
            tb.min.z + bagH * 0.05f + offset.z * h); // sits against his back; the bag grows outwards

        bagRoot = new GameObject("Backpack").transform;
        bagRoot.SetParent(transform, false);
        bagRoot.localPosition = localPos;
        bagRoot.localRotation = Quaternion.identity;
        bagRoot.localScale = Vector3.one * bagH; // 1 bag unit = bag height

        // Strap it to the torso so it follows crouch/lean animations.
        bagRoot.SetParent(torso, true);
        baseScale = bagRoot.localScale;

        // ---- parts (bag space: +z = towards Bandit's back, -z = away) ----
        body = Part(bagRoot, PrimitiveType.Cube, Vector3.zero, EmptyBody, bagColour, "Body").transform;

        // rounded top roll
        Part(body, PrimitiveType.Cylinder, new Vector3(0f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 1.05f), bagColour, "TopRoll", new Vector3(0f, 0f, 90f));

        // dark opening where the food goes in
        Part(body, PrimitiveType.Cylinder, new Vector3(0f, 0.76f, 0f), new Vector3(0.8f, 0.02f, 0.75f), new Color(0.12f, 0.08f, 0.05f), "Opening");
        mouth = new GameObject("Mouth").transform;
        mouth.SetParent(bagRoot, false);
        mouth.localPosition = new Vector3(0f, 0.7f, 0f);

        // outer pocket + flap + buckle (facing away from Bandit)
        Part(body, PrimitiveType.Cube, new Vector3(0f, -0.2f, -0.6f), new Vector3(0.7f, 0.42f, 0.35f), flapColour, "Pocket");
        Part(body, PrimitiveType.Cube, new Vector3(0f, 0.28f, -0.52f), new Vector3(0.9f, 0.4f, 0.1f), flapColour, "Flap", new Vector3(-8f, 0f, 0f));
        Part(body, PrimitiveType.Cube, new Vector3(0f, 0.1f, -0.6f), new Vector3(0.12f, 0.1f, 0.1f), buckleColour, "Buckle");

        // shoulder straps going over Bandit's shoulders
        foreach (float side in new[] { -1f, 1f })
        {
            Part(bagRoot, PrimitiveType.Cube, new Vector3(side * 0.22f, 0.38f, 0.3f), new Vector3(0.08f, 0.04f, 0.45f), strapColour, "StrapTop", new Vector3(-20f, 0f, 0f));
            Part(bagRoot, PrimitiveType.Cube, new Vector3(side * 0.22f, 0.02f, 0.2f), new Vector3(0.08f, 0.7f, 0.04f), strapColour, "StrapBack");
        }

        lootRoot = new GameObject("Loot").transform;
        lootRoot.SetParent(bagRoot, false);

        built = true;
    }

    private void UpdateBulge(bool animate)
    {
        GameManager gm = GameManager.Instance;
        int required = gm != null ? Mathf.Max(1, gm.requiredFood.Count) : 5;
        float t = Mathf.Clamp01(shownFood.Count / (float)required);
        Vector3 size = Vector3.Lerp(EmptyBody, FullBody, t);
        body.localScale = size;
        // keep the front of the bag against Bandit's back so it bulges outwards
        Vector3 back = new Vector3(0f, 0f, -size.z * 0.5f);
        body.localPosition = back;
        lootRoot.localPosition = back;
        mouth.localPosition = back + new Vector3(0f, size.y * 0.7f, 0f);
        PositionLoot();
        if (animate) popTimer = 0f;
    }

    private void ClearLoot()
    {
        shownFood.Clear();
        pending.Clear();
        foreach (Transform t in lootItems)
            if (t != null) Destroy(t.gameObject);
        lootItems.Clear();
        UpdateBulge(false);
    }

    // --------------------------------------------------------------------- Food sticking out of the bag.
    private static readonly Vector3[] PeekSpots =
    {
        new Vector3(-0.13f, 0f, 0.03f),
        new Vector3(0.11f, 0f, -0.03f),
        new Vector3(0f, 0f, 0f),
    };
    [Tooltip("How much of each food pokes out above the bag's rim (fraction of the bag's height). Small = just the tip peeking out.")]
    public float peekAmount = 0.06f;

    // Height of each food's top above its own centre, in bag units.
    private readonly Dictionary<Transform, float> itemTop = new Dictionary<Transform, float>();

    private void AddLoot(string food, bool animate)
    {
        if (shownFood.Contains(food)) return;
        shownFood.Add(food);

        Transform item = new GameObject("Loot_" + food).transform;
        item.SetParent(lootRoot, false);
        item.localRotation = Quaternion.Euler(0f, (shownFood.Count * 47f) % 50f - 25f, (shownFood.Count % 2 == 0 ? 10f : -10f));
        item.localScale = Vector3.one * 0.8f;
        BuildFood(item, food.ToLower());
        itemTop[item] = MeasureTop(item);
        lootItems.Add(item);

        // Only the last few collected stay visible - older ones are "deeper" in the bag.
        int keep = Mathf.Max(0, visibleLoot);
        while (lootItems.Count > keep)
        {
            if (lootItems[0] != null) Destroy(lootItems[0].gameObject);
            lootItems.RemoveAt(0);
        }

        UpdateBulge(animate); // also re-positions the visible items
    }

    private void PositionLoot()
    {
        if (body == null) return;
        // Top of the bag's rolled rim in bag space (rim reaches 0.75 of body height).
        float rimY = body.localScale.y * 0.75f;
        for (int i = 0; i < lootItems.Count; i++)
        {
            if (lootItems[i] == null) continue;
            int slot = Mathf.Min(i, PeekSpots.Length - 1);
            Vector3 p = PeekSpots[slot];
            float top;
            if (!itemTop.TryGetValue(lootItems[i], out top)) top = 0.1f;
            // Sink the food so only its top 'peekAmount' shows above the rim.
            p.y = rimY + peekAmount - top;
            lootItems[i].localPosition = p;
        }
    }

    // Highest point of a food item above its pivot, in bag (lootRoot) units.
    private float MeasureTop(Transform item)
    {
        float top = 0f;
        foreach (Transform part in item)
        {
            // primitives: cube/sphere are 1 unit tall, cylinders 2 units
            bool cylinder = part.name == "Dough" || part.name == "Icing" || part.name == "Hole" || part.name == "Cookie";
            float halfHeight = Mathf.Abs(part.localScale.y) * (cylinder ? 1f : 0.5f);
            // tilted flat discs stand up - use their width instead
            if (Mathf.Abs(part.localEulerAngles.x) > 45f && Mathf.Abs(part.localEulerAngles.x) < 135f)
                halfHeight = Mathf.Abs(part.localScale.x) * 0.5f;
            top = Mathf.Max(top, part.localPosition.y + halfHeight);
        }
        return top * item.localScale.y;
    }

    private void BuildFood(Transform r, string food)
    {
        if (food.Contains("donut") || food.Contains("doughnut"))
        {
            Part(r, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.26f, 0.035f, 0.26f), new Color(0.85f, 0.6f, 0.3f), "Dough", new Vector3(75f, 0f, 0f));
            Part(r, PrimitiveType.Cylinder, new Vector3(0f, 0f, -0.012f), new Vector3(0.24f, 0.03f, 0.24f), new Color(1f, 0.5f, 0.75f), "Icing", new Vector3(75f, 0f, 0f));
            Part(r, PrimitiveType.Cylinder, new Vector3(0f, 0f, -0.02f), new Vector3(0.08f, 0.04f, 0.08f), new Color(0.15f, 0.1f, 0.06f), "Hole", new Vector3(75f, 0f, 0f));
        }
        else if (food.Contains("cookie"))
        {
            Part(r, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.24f, 0.025f, 0.24f), new Color(0.82f, 0.6f, 0.32f), "Cookie", new Vector3(70f, 0f, 0f));
            for (int i = 0; i < 4; i++)
            {
                float a = i * 1.7f;
                Part(r, PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * 0.06f, Mathf.Sin(a) * 0.06f, -0.02f), Vector3.one * 0.035f, new Color(0.25f, 0.13f, 0.06f), "Chip");
            }
        }
        else if (food.Contains("toast") || food.Contains("bread"))
        {
            Part(r, PrimitiveType.Cube, Vector3.zero, new Vector3(0.26f, 0.28f, 0.045f), new Color(0.72f, 0.45f, 0.2f), "Crust");
            Part(r, PrimitiveType.Cube, new Vector3(0f, -0.01f, -0.005f), new Vector3(0.21f, 0.22f, 0.05f), new Color(0.95f, 0.8f, 0.5f), "Slice");
        }
        else if (food.Contains("apple"))
        {
            Part(r, PrimitiveType.Sphere, Vector3.zero, new Vector3(0.2f, 0.19f, 0.2f), new Color(0.85f, 0.12f, 0.1f), "Apple");
            Part(r, PrimitiveType.Cube, new Vector3(0f, 0.11f, 0f), new Vector3(0.02f, 0.06f, 0.02f), new Color(0.35f, 0.22f, 0.1f), "Stem");
            Part(r, PrimitiveType.Cube, new Vector3(0.035f, 0.12f, 0f), new Vector3(0.06f, 0.015f, 0.035f), new Color(0.25f, 0.65f, 0.2f), "Leaf", new Vector3(0f, 0f, 20f));
        }
        else if (food.Contains("milk"))
        {
            Part(r, PrimitiveType.Cube, new Vector3(0f, 0.05f, 0f), new Vector3(0.14f, 0.3f, 0.14f), new Color(0.97f, 0.97f, 0.97f), "Carton");
            Part(r, PrimitiveType.Cube, new Vector3(0f, 0.07f, -0.0005f), new Vector3(0.145f, 0.1f, 0.145f), new Color(0.2f, 0.45f, 0.9f), "Label");
            Part(r, PrimitiveType.Cube, new Vector3(0f, 0.23f, 0f), new Vector3(0.14f, 0.07f, 0.03f), new Color(0.97f, 0.97f, 0.97f), "Top");
        }
        else
        {
            int hash = Mathf.Abs(food.GetHashCode());
            Color c = Color.HSVToRGB((hash % 360) / 360f, 0.6f, 0.9f);
            Part(r, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.18f, c, "Food");
        }
    }

    // ---------------------------------------------------------------------
    //  Helpers
    // ---------------------------------------------------------------------
    private GameObject Part(Transform parent, PrimitiveType type, Vector3 localPos, Vector3 localScale, Color colour,
                            string name, Vector3 euler = default)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        g.name = name;
        Destroy(g.GetComponent<Collider>()); // never blocks movement or the E-key raycast
        g.layer = gameObject.layer;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localRotation = Quaternion.Euler(euler);
        g.transform.localScale = localScale;

        Renderer rend = g.GetComponent<Renderer>();
        Material m = rend.material;
        m.color = colour;
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.15f);
        return g;
    }

    private static Transform FindPart(Transform root, string partName)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == partName) return t;
        return null;
    }

    private static Bounds ToLocalBounds(Transform space, Bounds world)
    {
        Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            Vector3 c = new Vector3(
                (i & 1) == 0 ? world.min.x : world.max.x,
                (i & 2) == 0 ? world.min.y : world.max.y,
                (i & 4) == 0 ? world.min.z : world.max.z);
            Vector3 l = space.InverseTransformPoint(c);
            min = Vector3.Min(min, l);
            max = Vector3.Max(max, l);
        }
        Bounds b = new Bounds();
        b.SetMinMax(min, max);
        return b;
    }
}
