using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class Fridge : MonoBehaviour, IInteractable
{
    public enum Face { FrontZ, BackZ, RightX, LeftX }

    [Header("Light (already wired in the scene)")]
    public GameObject fridgeLight;

    [Header("Events (Person 3 wires Whiskers.WakeUp here)")]
    public UnityEvent onFridgeOpened;

    [Header("Door")]
    [Tooltip("Leave EMPTY to use the fridge model's own 'doorFridge' part (hinged on the side opposite its handle, like a real fridge). Assign a hinge/door object to use your own.")]
    public Transform door;
    [Tooltip("Name of the model's door child that gets swung open when 'door' is left empty. The freezer door on top stays shut.")]
    public string modelDoorName = "doorFridge";
    public float openAngle = 115f;
    public float openSpeed = 6f;

    [Header("Hinge side")]
    [Tooltip("Tick to hinge the door on the OTHER edge. By default the door hinges on the edge opposite the handle, like a normal fridge.")]
    public bool hingeOnOtherSide = false;

    [Header("Fallback auto-built door (only used if the model has no door part)")]
    public Face doorFace = Face.FrontZ;
    public float doorThickness = 0.09f;

    [Header("Items inside the fridge (e.g. Milk)")]
    [Tooltip("Collectable FoodItems kept in the fridge. Shown while the door is open, hidden while closed. Already-eaten items stay hidden.")]
    public GameObject[] doorContents;
    [Tooltip("Moves the items above onto the bottom shelf INSIDE the fridge on Play, so they sit behind the door instead of floating in front of it.")]
    public bool arrangeContentsInside = true;

    [Header("Fridge interior dressing (decoration only - not collectable)")]
    [Tooltip("Builds shelves and fills the fridge with food on Play.")]
    public bool stockInterior = true;
    [Tooltip("Optional food models (e.g. SaladBowl, PastaBowl, ChickenLeg) placed on the shelves. Empty slots get simple built-in food instead.")]
    public GameObject[] extraFoodModels;
    [Tooltip("Adds a soft light inside the fridge that switches on while the door is open.")]
    public bool addInteriorLight = true;
    public Color interiorLightColor = new Color(0.85f, 0.95f, 1f);
    public float interiorLightIntensity = 1.4f;

    [Header("Left open too long")]
    [Tooltip("If the door stays open this many seconds, Edgar comes to check the kitchen (once per opening).")]
    public float leftOpenAlertTime = 8f;

    /// Fired when the fridge has been left open too long; gives a spot in front of the fridge.
    public static event System.Action<Vector3> OnLeftOpenTooLong;
    private float openTimer;
    private bool leftOpenAlerted;

    [Header("Easy use")]
    [Tooltip("How close (world units) Bandit needs to be to the front of the fridge to open/close it or grab food with E - no aiming needed.")]
    public float useDistance = 3.5f;
    [Tooltip("Shows a floating 'E' prompt when Bandit is close enough.")]
    public bool showPrompt = true;

    // Every fridge in the scene, so PlayerInteract can ask "is Bandit standing at a fridge?"
    private static readonly List<Fridge> all = new List<Fridge>();
    private TextMesh prompt;
    private Transform promptPlayer;

    private bool isOpen = false;
    private Quaternion closedRotation;
    private Quaternion openRotation;

    // Interior space in this fridge's local coordinates (worked out from the door).
    private bool hasInterior = false;
    private Vector3 interiorMin;
    private Vector3 interiorMax;
    private Transform interiorRoot;
    private Light interiorLight;
    private readonly List<float> levelHeights = new List<float>(); // floor + shelf tops (local Y)

    private void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    private void OnDisable() { all.Remove(this); }

    /// Called by PlayerInteract when E is pressed. If Bandit is standing in
    /// front of a fridge, that fridge handles the press (open / grab the next
    /// food on the list / close) and true is returned - no aiming required.
    public static bool TryHandleNearby(Vector3 playerPos)
    {
        Fridge best = null;
        float bestDist = float.MaxValue;
        foreach (Fridge f in all)
        {
            float d;
            if (f != null && f.IsPlayerInFront(playerPos, out d) && d < bestDist) { best = f; bestDist = d; }
        }
        if (best == null) return false;
        best.Interact();
        return true;
    }

    // Front of the fridge = the side the door is on. Uses horizontal distance
    // to the door's face so height differences don't matter.
    private bool IsPlayerInFront(Vector3 playerPos, out float distance)
    {
        distance = float.MaxValue;
        Vector3 front = FrontCentreWorld();
        Vector3 outward = FrontNormalWorld();
        Vector3 toPlayer = playerPos - front;
        toPlayer.y = 0f;
        distance = toPlayer.magnitude;
        if (distance > useDistance) return false;
        // must be roughly in front, not behind/beside the fridge
        return Vector3.Dot(toPlayer, outward) > -0.3f;
    }

    /// A spot in front of the fridge, to one side (the handle side, away
    /// from where the door swings), and the direction facing out into the
    /// kitchen. Used by Whiskers to guard the fridge.
    public Vector3 GetGuardSpot(float outDistance, float sideDistance, out Vector3 facing)
    {
        Vector3 normal = FrontNormalWorld();
        Vector3 right = Vector3.Cross(Vector3.up, -normal).normalized; // viewer's right, facing the fridge
        if (hingeOnOtherSide) right = -right;
        facing = normal;
        return FrontCentreWorld() + normal * outDistance + right * sideDistance;
    }

    private Vector3 FrontCentreWorld()
    {
        if (hasInterior)
        {
            float z = frontAtMin ? interiorMin.z - 1f : interiorMax.z + 1f;
            Vector3 local = new Vector3((interiorMin.x + interiorMax.x) * 0.5f, interiorMin.y, z);
            return transform.TransformPoint(local);
        }
        Collider c = GetComponent<Collider>();
        return c != null ? c.bounds.center : transform.position;
    }

    private Vector3 FrontNormalWorld()
    {
        Vector3 n = hasInterior ? transform.TransformDirection(frontAtMin ? Vector3.back : Vector3.forward) : transform.forward;
        n.y = 0f;
        return n.sqrMagnitude > 0.0001f ? n.normalized : Vector3.forward;
    }

    private void UpdatePrompt()
    {
        if (!showPrompt) return;
        if (promptPlayer == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p == null) return;
            promptPlayer = p.transform;
        }
        if (prompt == null)
        {
            GameObject g = new GameObject("FridgePrompt");
            prompt = g.AddComponent<TextMesh>();
            prompt.fontSize = 60;
            prompt.characterSize = 0.05f;
            prompt.anchor = TextAnchor.LowerCenter;
            prompt.alignment = TextAlignment.Center;
            prompt.fontStyle = FontStyle.Bold;
            prompt.color = new Color(0.55f, 0.85f, 1f);
        }

        float d;
        bool near = IsPlayerInFront(promptPlayer.position, out d);
        prompt.gameObject.SetActive(near);
        if (!near) return;

        string text;
        if (!isOpen) text = "Press E to open fridge";
        else if (GetNextUncollectedFoodInside() != null && PlayerLookingIn()) text = "Press E to grab food\n(turn away + E to close)";
        else text = "Press E to close fridge";
        prompt.text = text;

        Vector3 pos = FrontCentreWorld() + FrontNormalWorld() * 0.3f;
        pos.y = promptPlayer.position.y + 2.2f;
        prompt.transform.position = pos;
        if (Camera.main != null) prompt.transform.forward = Camera.main.transform.forward;
    }

    // Is Bandit facing into the fridge? Then E grabs food; turned away, E
    // closes the door (so you can always shut it quickly).
    private bool PlayerLookingIn()
    {
        if (promptPlayer == null)
        {
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            if (p == null) return true;
            promptPlayer = p.transform;
        }
        Vector3 f = promptPlayer.forward; f.y = 0f;
        Vector3 into = -FrontNormalWorld(); into.y = 0f;
        if (f.sqrMagnitude < 0.0001f || into.sqrMagnitude < 0.0001f) return true;
        return Vector3.Dot(f.normalized, into.normalized) > 0.3f;
    }

    // Countdown on screen while the fridge is open (only matters if Edgar is here).
    private void UpdateOpenCountdown()
    {
        if (HumanNPC.Instance == null) { if (countdownShown) { ScreenMessage.SetTimer(null, Color.white); countdownShown = false; } return; }
        if (!isOpen)
        {
            if (countdownShown) { ScreenMessage.SetTimer(null, Color.white); countdownShown = false; }
            return;
        }
        countdownShown = true;
        if (!leftOpenAlerted)
        {
            int left = Mathf.CeilToInt(leftOpenAlertTime - openTimer);
            ScreenMessage.SetTimer("Fridge open - close it in " + left + "s or Edgar will notice!",
                left <= 3 ? new Color(1f, 0.35f, 0.25f) : new Color(1f, 0.85f, 0.3f));
        }
        else
        {
            ScreenMessage.SetTimer("Fridge left open - Edgar noticed! Close it and hide!", new Color(1f, 0.35f, 0.25f));
        }
    }
    private bool countdownShown;

    // The swinging door gets a solid collider so Bandit and Whiskers can't
    // walk through it (it has none on the model). Kinematic rigidbody = it
    // moves properly with the door.
    private void AddDoorCollider(Transform doorPart)
    {
        Renderer[] rs = doorPart.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return;
        Renderer r = rs[0];
        if (r.GetComponent<Collider>() == null) // may already have one from the collider tool
        {
            MeshFilter mf = r.GetComponent<MeshFilter>();
            BoxCollider bc = r.gameObject.AddComponent<BoxCollider>();
            if (mf != null && mf.sharedMesh != null)
            {
                bc.center = mf.sharedMesh.bounds.center;
                bc.size = mf.sharedMesh.bounds.size;
            }
        }
        r.gameObject.layer = 0; // solid, but not something E-aim should snag on

        // Tell Whiskers' and Edgar's walkable-area maps about the door, even
        // when it swings open, so they path round it instead of into it.
        UnityEngine.AI.NavMeshObstacle obs = r.gameObject.GetComponent<UnityEngine.AI.NavMeshObstacle>();
        if (obs == null)
        {
            BoxCollider box = r.gameObject.GetComponent<BoxCollider>();
            obs = r.gameObject.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            obs.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
            MeshFilter omf = r.gameObject.GetComponent<MeshFilter>();
            if (box != null) { obs.center = box.center; obs.size = box.size; }
            else if (omf != null && omf.sharedMesh != null) { obs.center = omf.sharedMesh.bounds.center; obs.size = omf.sharedMesh.bounds.size; }
            obs.carving = true;
            obs.carveOnlyStationary = false;
        }

        Rigidbody rb = doorPart.GetComponent<Rigidbody>();
        if (rb == null) rb = doorPart.gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    private void Start()
    {
        if (door == null)
        {
            door = FindModelDoor();
        }

        if (door != null)
        {
            ComputeInterior(door);
            AddDoorCollider(door);

            if (hingeOnOtherSide)
            {
                door = MoveHingeToOtherEdge(door);
            }

            closedRotation = door.localRotation;
            float sign = OutwardSwingSign(door);
            openRotation = closedRotation * Quaternion.Euler(0f, sign * openAngle, 0f);
        }
        else
        {
            // No door part on the model - fall back to the old auto-built panel.
            door = BuildDoor();
            if (door != null)
            {
                closedRotation = door.localRotation;
                openRotation = closedRotation * Quaternion.Euler(0f, hingeOnOtherSide ? -openAngle : openAngle, 0f);
            }
        }

        if (hasInterior && stockInterior)
        {
            BuildInterior();
        }

        if (doorContents != null)
        {
            int slot = 0;
            foreach (GameObject item in doorContents)
            {
                if (item == null) continue;

                if (hasInterior && arrangeContentsInside && item.activeInHierarchy)
                {
                    Vector3 spot = ContentSpots[Mathf.Min(slot, ContentSpots.Length - 1)];
                    PlaceOnLevel(item.transform, spot.x, spot.y, (int)spot.z);
                    slot++;
                }

                item.SetActive(false); // hidden while the fridge is closed
            }
        }

        if (fridgeLight != null)
        {
            fridgeLight.SetActive(false);
        }
    }

    private int lastInteractFrame = -1;

    public void Interact()
    {
        // One action per key press, even if something calls this twice in a frame.
        if (lastInteractFrame == Time.frameCount) return;
        lastInteractFrame = Time.frameCount;

        // While the fridge is open and something inside hasn't been grabbed yet, pressing E always goes for that food FIRST.
        if (isOpen && PlayerLookingIn())
        {
            FoodItem nextFood = GetNextUncollectedFoodInside();
            if (nextFood != null)
            {
                nextFood.Interact();
                UpdateDoorContentsVisibility();
                return;
            }
        }

        isOpen = !isOpen;

        if (fridgeLight != null)
        {
            fridgeLight.SetActive(isOpen);
        }

        if (interiorLight != null)
        {
            interiorLight.enabled = isOpen;
        }

        UpdateDoorContentsVisibility();

        if (isOpen)
        {
            Debug.Log("Bandit opened the fridge!");
            SoundFX.Play("fridge_open", 0.8f);
            onFridgeOpened.Invoke();
        }
        else
        {
            Debug.Log("Bandit closed the fridge.");
            SoundFX.Play("fridge_close", 0.8f);
        }
    }

    // Where collectable items go inside the fridge: (u across, v depth, shelf level).
    // 0 = bottom (left, near the front), 1 = middle shelf, 2 = top shelf.
    private static readonly Vector3[] ContentSpots =
    {
        new Vector3(0.14f, 0.22f, 0),
        new Vector3(0.40f, 0.30f, 1),
        new Vector3(0.68f, 0.30f, 2),
        new Vector3(0.31f, 0.25f, 0),
    };

    private int ContentCount()
    {
        int n = 0;
        if (doorContents != null)
            foreach (GameObject g in doorContents) if (g != null) n++;
        return n;
    }

    // Only ever auto-grabs food that's ON the shopping list. Wrong items
    // in the fridge have to be deliberately aimed at (and knocked over).
    private FoodItem GetNextUncollectedFoodInside()
    {
        if (doorContents == null) return null;

        foreach (GameObject item in doorContents)
        {
            if (item == null) continue;

            FoodItem food = item.GetComponent<FoodItem>();
            if (food == null || food.IsBeingCollected || food.IsKnockedOver || !food.IsOnShoppingList) continue;

            bool alreadyEaten = GameManager.Instance != null &&
                                 GameManager.Instance.collectedFood.Contains(food.FoodName);
            if (!alreadyEaten) return food;
        }

        return null;
    }

    private void UpdateDoorContentsVisibility()
    {
        if (doorContents == null) return;

        foreach (GameObject item in doorContents)
        {
            if (item == null) continue;

            FoodItem food = item.GetComponent<FoodItem>();

            // Leave food alone while its "taken out of the fridge" animation
            // is playing - it hides itself when the animation finishes.
            if (food != null && (food.IsBeingCollected || food.IsKnockedOver)) continue;

            bool alreadyEaten = food != null && GameManager.Instance != null &&
                                 GameManager.Instance.collectedFood.Contains(food.FoodName);

            item.SetActive(isOpen && !alreadyEaten);
        }
    }

    private void Update()
    {
        UpdatePrompt();

        if (isOpen)
        {
            openTimer += Time.deltaTime;
            if (!leftOpenAlerted && openTimer >= leftOpenAlertTime)
            {
                leftOpenAlerted = true;
                Debug.Log("The fridge has been open for " + leftOpenAlertTime + "s - Edgar might notice!");
                OnLeftOpenTooLong?.Invoke(FrontCentreWorld() + FrontNormalWorld() * 2f);
            }
        }
        else
        {
            openTimer = 0f;
            leftOpenAlerted = false;
        }
        UpdateOpenCountdown();

        if (door == null)
            return;

        door.localRotation = Quaternion.Slerp(
            door.localRotation,
            isOpen ? openRotation : closedRotation,
            openSpeed * Time.deltaTime
        );
    }

    // =====================================================================
    //  Door
    // =====================================================================

    private Transform FindModelDoor()
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t != transform && t.name == modelDoorName) return t;
        }
        return null;
    }

    // Picks +1 or -1 so the door swings OUT of the fridge (away from the
    // body) instead of into it - works whichever way the model was exported.
    private float OutwardSwingSign(Transform hinge)
    {
        Bounds doorWorld;
        if (!TryGetWorldBounds(door, out doorWorld)) return 1f;

        Vector3 bodyCentre = GetBodyWorldCentre();
        Vector3 rel = Quaternion.Inverse(hinge.rotation) * (doorWorld.center - hinge.position);
        Quaternion parentRot = hinge.parent != null ? hinge.parent.rotation : Quaternion.identity;

        float bestSign = 1f;
        float bestDist = float.MinValue;
        foreach (float s in new[] { 1f, -1f })
        {
            Quaternion r = parentRot * hinge.localRotation * Quaternion.Euler(0f, s * 90f, 0f);
            Vector3 swung = hinge.position + r * rel;
            float d = (swung - bodyCentre).sqrMagnitude;
            if (d > bestDist) { bestDist = d; bestSign = s; }
        }
        return bestSign;
    }

    private Vector3 GetBodyWorldCentre()
    {
        Collider c = GetComponent<Collider>();
        if (c != null) return c.bounds.center;

        Bounds b;
        return TryGetWorldBounds(transform, out b) ? b.center : transform.position;
    }

    // Wraps the door in a new pivot on its opposite vertical edge.
    private Transform MoveHingeToOtherEdge(Transform doorPart)
    {
        Bounds local = LocalBoundsOf(doorPart.GetComponentsInChildren<Renderer>());
        Vector3 pivotLocal = transform.InverseTransformPoint(doorPart.position);

        float otherX = Mathf.Abs(local.min.x - pivotLocal.x) > Mathf.Abs(local.max.x - pivotLocal.x)
            ? local.min.x : local.max.x;

        GameObject pivot = new GameObject("DoorHinge_OtherSide");
        pivot.transform.SetParent(doorPart.parent, false);
        pivot.transform.position = transform.TransformPoint(new Vector3(otherX, pivotLocal.y, pivotLocal.z));
        pivot.transform.rotation = doorPart.rotation;
        doorPart.SetParent(pivot.transform, true);
        return pivot.transform;
    }

    // =====================================================================
    //  Interior (shelves + food + light)
    // =====================================================================

    private void ComputeInterior(Transform doorPart)
    {
        Renderer[] doorRenderers = doorPart.GetComponentsInChildren<Renderer>();
        if (doorRenderers.Length == 0) return;

        Bounds doorLocal = LocalBoundsOf(doorRenderers);

        List<Renderer> body = new List<Renderer>();
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (!r.transform.IsChildOf(doorPart) && !r.name.ToLower().Contains("door"))
                body.Add(r);
        }
        if (body.Count == 0) return;
        Bounds bodyLocal = LocalBoundsOf(body.ToArray());

        // The door sits on the body's front face; the hollow is behind it.
        bool frontIsMinZ = Mathf.Abs(doorLocal.center.z - bodyLocal.min.z) < Mathf.Abs(doorLocal.center.z - bodyLocal.max.z);
        float inset = doorLocal.size.x * 0.03f;

        float zFront, zBack;
        if (frontIsMinZ)
        {
            zFront = doorLocal.max.z + 0.05f;
            zBack = bodyLocal.max.z - bodyLocal.size.z * 0.13f;
        }
        else
        {
            zFront = doorLocal.min.z - 0.05f;
            zBack = bodyLocal.min.z + bodyLocal.size.z * 0.13f;
        }

        interiorMin = new Vector3(doorLocal.min.x + inset, doorLocal.min.y + 0.01f, Mathf.Min(zFront, zBack));
        interiorMax = new Vector3(doorLocal.max.x - inset, doorLocal.max.y - doorLocal.size.y * 0.03f, Mathf.Max(zFront, zBack));
        frontAtMin = frontIsMinZ;
        hasInterior = interiorMax.x > interiorMin.x && interiorMax.z > interiorMin.z;
    }

    private bool frontAtMin = true;

    // u = 0..1 left->right across the fridge, v = 0..1 front->back.
    private Vector3 InteriorPoint(float u, float v, float y)
    {
        float x = Mathf.Lerp(interiorMin.x, interiorMax.x, u);
        float z = frontAtMin ? Mathf.Lerp(interiorMin.z, interiorMax.z, v)
                             : Mathf.Lerp(interiorMax.z, interiorMin.z, v);
        return new Vector3(x, y, z);
    }

    private void BuildInterior()
    {
        interiorRoot = new GameObject("FridgeInterior").transform;
        interiorRoot.SetParent(transform, false);

        float h = interiorMax.y - interiorMin.y;
        float shelfThickness = h * 0.01f;
        float[] shelfFractions = { 0.35f, 0.67f };

        levelHeights.Clear();
        levelHeights.Add(interiorMin.y);

        Color shelfColour = new Color(0.82f, 0.93f, 0.97f);
        foreach (float f in shelfFractions)
        {
            float y = interiorMin.y + h * f;
            Vector3 centre = InteriorPoint(0.5f, 0.5f, y);
            Vector3 size = new Vector3(interiorMax.x - interiorMin.x, shelfThickness, interiorMax.z - interiorMin.z);
            Part(interiorRoot, PrimitiveType.Cube, centre, size, shelfColour, "Shelf", 0.8f);
            levelHeights.Add(y + shelfThickness * 0.5f);
        }

        if (addInteriorLight)
        {
            GameObject lightObj = new GameObject("FridgeInteriorLight");
            lightObj.transform.SetParent(interiorRoot, false);
            lightObj.transform.localPosition = InteriorPoint(0.5f, 0.15f, interiorMin.y + h * 0.9f);
            interiorLight = lightObj.AddComponent<Light>();
            interiorLight.type = LightType.Point;
            interiorLight.color = interiorLightColor;
            interiorLight.intensity = interiorLightIntensity;
            interiorLight.range = h * 1.3f;
            interiorLight.shadows = LightShadows.None;
            interiorLight.enabled = false;
        }

        float W = interiorMax.x - interiorMin.x; // interior width, for sizing food

        // ---- Optional real food models (or a built-in stand-in for empty slots) ----
        // slot: level, u, v, size (fraction of interior width)
        float[,] modelSlots =
        {
            { 0, 0.80f, 0.45f, 0.30f },  // bottom: big salad bowl
            { 1, 0.40f, 0.45f, 0.24f },  // middle: leftovers bowl
            { 1, 0.64f, 0.40f, 0.20f },  // middle: drumstick
            { 2, 0.68f, 0.45f, 0.18f },  // top
        };

        int contents = ContentCount();
        for (int i = 0; i < modelSlots.GetLength(0); i++)
        {
            // Leave room for collectable items on the middle/top shelves.
            if (i == 1 && contents >= 2) continue;
            if (i == 3 && contents >= 3) continue;

            int level = (int)modelSlots[i, 0];
            float u = modelSlots[i, 1], v = modelSlots[i, 2], size = modelSlots[i, 3] * W;
            GameObject prefab = (extraFoodModels != null && i < extraFoodModels.Length) ? extraFoodModels[i] : null;

            if (prefab != null)
            {
                PlaceModel(prefab, level, u, v, size);
            }
            else
            {
                switch (i)
                {
                    case 0: MakeTomatoes(level, u, v, W); break;
                    case 1: MakeLeftoversBox(level, u, v, W); break;
                    case 2: MakeButter(level, u, v, W); break;
                    default: MakeGrapes(level, u, v, W); break;
                }
            }
        }

        // ---- Built-in food ----
        MakeEggCarton(0, 0.50f, 0.45f, W);
        MakeJuiceCarton(1, 0.13f, 0.45f, W);
        MakeCheese(1, 0.87f, 0.40f, W);
        MakeSodaCan(2, 0.09f, 0.35f, W, new Color(0.85f, 0.12f, 0.12f));
        MakeSodaCan(2, 0.19f, 0.45f, W, new Color(0.15f, 0.55f, 0.2f));
        MakeSodaCan(2, 0.29f, 0.35f, W, new Color(0.2f, 0.35f, 0.85f));
        MakeJamJar(2, 0.46f, 0.45f, W);
        MakeWaterBottle(2, 0.88f, 0.45f, W);
    }

    private float LevelY(int level)
    {
        level = Mathf.Clamp(level, 0, levelHeights.Count - 1);
        return levelHeights.Count > 0 ? levelHeights[level] : interiorMin.y;
    }

    // Moves an existing object so its bottom-centre rests on a shelf.
    private void PlaceOnLevel(Transform obj, float u, float v, int level)
    {
        if (levelHeights.Count == 0) levelHeights.Add(interiorMin.y);

        Bounds b;
        if (!TryGetWorldBounds(obj, out b)) return;

        Vector3 target = transform.TransformPoint(InteriorPoint(u, v, LevelY(level)));
        Vector3 bottomCentre = new Vector3(b.center.x, b.min.y, b.center.z);
        obj.position += target - bottomCentre + Vector3.up * 0.01f;
    }

    private void PlaceModel(GameObject prefab, int level, float u, float v, float maxSize)
    {
        GameObject g = Instantiate(prefab, interiorRoot);
        g.name = prefab.name + " (fridge)";
        foreach (Collider c in g.GetComponentsInChildren<Collider>()) Destroy(c);
        foreach (MonoBehaviour m in g.GetComponentsInChildren<MonoBehaviour>()) Destroy(m);
        foreach (Transform t in g.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;

        Bounds b;
        if (!TryGetWorldBounds(g.transform, out b)) return;
        float biggest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (biggest > 0.0001f)
        {
            float worldMax = maxSize * transform.lossyScale.x;
            g.transform.localScale *= worldMax / biggest;
        }
        PlaceOnLevel(g.transform, u, v, level);
    }

    // ---- simple low-poly food built from primitives -----------------------

    private Transform FoodRoot(string name, int level, float u, float v)
    {
        Transform root = new GameObject(name).transform;
        root.SetParent(interiorRoot, false);
        root.localPosition = InteriorPoint(u, v, LevelY(level));
        return root;
    }

    private void MakeEggCarton(int level, float u, float v, float W)
    {
        Transform r = FoodRoot("EggCarton", level, u, v);
        float cw = W * 0.24f, cd = W * 0.12f, ch = W * 0.035f;
        Part(r, PrimitiveType.Cube, new Vector3(0, ch * 0.5f, 0), new Vector3(cw, ch, cd), new Color(0.72f, 0.66f, 0.55f), "Carton");
        float e = W * 0.05f;
        for (int row = 0; row < 2; row++)
            for (int col = 0; col < 4; col++)
            {
                Vector3 p = new Vector3(-cw * 0.375f + col * cw * 0.25f, ch + e * 0.45f, -cd * 0.25f + row * cd * 0.5f);
                Part(r, PrimitiveType.Sphere, p, new Vector3(e * 0.85f, e * 1.1f, e * 0.85f), new Color(0.98f, 0.95f, 0.86f), "Egg");
            }
    }

    private void MakeJuiceCarton(int level, float u, float v, float W)
    {
        Transform r = FoodRoot("JuiceCarton", level, u, v);
        float w = W * 0.13f, h = W * 0.34f;
        Part(r, PrimitiveType.Cube, new Vector3(0, h * 0.5f, 0), new Vector3(w, h, w), new Color(1f, 0.6f, 0.1f), "Carton");
        Part(r, PrimitiveType.Cube, new Vector3(0, h * 0.55f, -w * 0.505f), new Vector3(w * 0.7f, h * 0.35f, w * 0.02f), Color.white, "Label");
        Part(r, PrimitiveType.Cube, new Vector3(0, h + w * 0.15f, 0), new Vector3(w, w * 0.3f, w * 0.15f), new Color(1f, 0.6f, 0.1f), "Top");
        Part(r, PrimitiveType.Cylinder, new Vector3(w * 0.25f, h + w * 0.05f, -w * 0.2f), new Vector3(w * 0.25f, w * 0.06f, w * 0.25f), new Color(0.2f, 0.6f, 0.2f), "Cap");
    }

    private void MakeCheese(int level, float u, float v, float W)
    {
        Transform r = FoodRoot("Cheese", level, u, v);
        float s = W * 0.14f;
        Color cheese = new Color(1f, 0.82f, 0.25f);
        Part(r, PrimitiveType.Cube, new Vector3(0, s * 0.25f, 0), new Vector3(s, s * 0.5f, s * 0.8f), cheese, "Block", 0.2f, new Vector3(0, 20f, 0));
        Part(r, PrimitiveType.Cube, new Vector3(-s * 0.1f, s * 0.53f, 0), new Vector3(s * 0.6f, s * 0.06f, s * 0.5f), new Color(1f, 0.9f, 0.5f), "Slice", 0.2f, new Vector3(0, -10f, 0));
    }

    private void MakeSodaCan(int level, float u, float v, float W, Color colour)
    {
        Transform r = FoodRoot("SodaCan", level, u, v);
        float d = W * 0.075f, h = W * 0.16f;
        Part(r, PrimitiveType.Cylinder, new Vector3(0, h * 0.5f, 0), new Vector3(d, h * 0.5f, d), colour, "Can", 0.7f);
        Part(r, PrimitiveType.Cylinder, new Vector3(0, h + d * 0.03f, 0), new Vector3(d * 0.92f, d * 0.03f, d * 0.92f), new Color(0.8f, 0.8f, 0.82f), "Lid", 0.8f);
    }

    private void MakeJamJar(int level, float u, float v, float W)
    {
        Transform r = FoodRoot("JamJar", level, u, v);
        float d = W * 0.11f, h = W * 0.12f;
        Part(r, PrimitiveType.Cylinder, new Vector3(0, h * 0.5f, 0), new Vector3(d, h * 0.5f, d), new Color(0.55f, 0.1f, 0.35f), "Jar", 0.8f);
        Part(r, PrimitiveType.Cylinder, new Vector3(0, h + d * 0.12f, 0), new Vector3(d * 1.05f, d * 0.12f, d * 1.05f), new Color(0.9f, 0.72f, 0.2f), "Lid", 0.7f);
    }

    private void MakeWaterBottle(int level, float u, float v, float W)
    {
        Transform r = FoodRoot("WaterBottle", level, u, v);
        float d = W * 0.09f, h = W * 0.3f;
        Part(r, PrimitiveType.Cylinder, new Vector3(0, h * 0.5f, 0), new Vector3(d, h * 0.5f, d), new Color(0.55f, 0.8f, 0.95f), "Bottle", 0.9f);
        Part(r, PrimitiveType.Cylinder, new Vector3(0, h * 0.62f, 0), new Vector3(d * 1.02f, h * 0.08f, d * 1.02f), new Color(0.1f, 0.4f, 0.8f), "Label");
        Part(r, PrimitiveType.Cylinder, new Vector3(0, h + d * 0.25f, 0), new Vector3(d * 0.45f, d * 0.25f, d * 0.45f), new Color(0.1f, 0.3f, 0.75f), "Cap");
    }

    private void MakeTomatoes(int level, float u, float v, float W)
    {
        Transform r = FoodRoot("Tomatoes", level, u, v);
        float s = W * 0.08f;
        Vector3[] spots = { new Vector3(-s, 0, 0), new Vector3(s * 0.2f, 0, s * 0.3f), new Vector3(s * 1.1f, 0, -s * 0.2f), new Vector3(0, s * 0.75f, 0) };
        foreach (Vector3 p in spots)
        {
            Part(r, PrimitiveType.Sphere, p + Vector3.up * s * 0.45f, new Vector3(s, s * 0.85f, s), new Color(0.9f, 0.15f, 0.1f), "Tomato", 0.6f);
            Part(r, PrimitiveType.Cube, p + Vector3.up * s * 0.9f, new Vector3(s * 0.12f, s * 0.12f, s * 0.12f), new Color(0.2f, 0.55f, 0.15f), "Stem");
        }
    }

    private void MakeLeftoversBox(int level, float u, float v, float W)
    {
        Transform r = FoodRoot("Leftovers", level, u, v);
        float w = W * 0.2f, d = W * 0.14f, h = W * 0.07f;
        Part(r, PrimitiveType.Cube, new Vector3(0, h * 0.5f, 0), new Vector3(w, h, d), new Color(0.95f, 0.95f, 0.95f), "Box", 0.5f);
        Part(r, PrimitiveType.Cube, new Vector3(0, h + h * 0.08f, 0), new Vector3(w * 1.04f, h * 0.16f, d * 1.04f), new Color(0.25f, 0.6f, 0.85f), "Lid", 0.5f);
    }

    private void MakeButter(int level, float u, float v, float W)
    {
        Transform r = FoodRoot("Butter", level, u, v);
        float w = W * 0.12f;
        Part(r, PrimitiveType.Cube, new Vector3(0, w * 0.2f, 0), new Vector3(w, w * 0.4f, w * 0.55f), new Color(1f, 0.93f, 0.55f), "Butter", 0.3f, new Vector3(0, -15f, 0));
    }

    private void MakeGrapes(int level, float u, float v, float W)
    {
        Transform r = FoodRoot("Grapes", level, u, v);
        float s = W * 0.035f;
        Color grape = new Color(0.45f, 0.15f, 0.55f);
        int n = 0;
        for (int layer = 0; layer < 3; layer++)
            for (int i = 0; i < 4 - layer; i++)
            {
                float a = (i + layer * 0.5f) * Mathf.PI * 2f / (4 - layer);
                Vector3 p = new Vector3(Mathf.Cos(a) * s * (1.2f - layer * 0.35f), s * 0.5f + layer * s * 0.8f, Mathf.Sin(a) * s * (1.2f - layer * 0.35f));
                Part(r, PrimitiveType.Sphere, p, Vector3.one * s, grape, "Grape" + (n++), 0.7f);
            }
    }

    private GameObject Part(Transform parent, PrimitiveType type, Vector3 localPos, Vector3 localScale, Color colour,
                            string name, float gloss = 0.3f, Vector3 euler = default)
    {
        GameObject g = GameObject.CreatePrimitive(type);
        g.name = name;
        g.layer = 0;
        Destroy(g.GetComponent<Collider>()); // decoration only - never blocks the E-key raycast
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localRotation = Quaternion.Euler(euler);
        g.transform.localScale = localScale;

        Renderer rend = g.GetComponent<Renderer>();
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Material m = rend.material;
        m.color = colour;
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", gloss);
        return g;
    }

    // ---- bounds helpers ---------------------------------------------------

    private bool TryGetWorldBounds(Transform root, out Bounds bounds)
    {
        bounds = new Bounds();
        bool any = false;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
        {
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return any;
    }

    // World renderer bounds converted into this fridge's local space.
    private Bounds LocalBoundsOf(Renderer[] renderers)
    {
        Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        foreach (Renderer r in renderers)
        {
            Bounds w = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? w.min.x : w.max.x,
                    (i & 2) == 0 ? w.min.y : w.max.y,
                    (i & 4) == 0 ? w.min.z : w.max.z);
                Vector3 local = transform.InverseTransformPoint(corner);
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }
        }
        Bounds b = new Bounds();
        b.SetMinMax(min, max);
        return b;
    }

    // =====================================================================
    //  Fallback: builds a hinge + door panel if the model has no door part
    // =====================================================================
    private Transform BuildDoor()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Debug.LogWarning("Fridge: no renderers found, cannot auto-build a door.");
            return null;
        }

        Bounds lb = LocalBoundsOf(renderers);
        Vector3 min = lb.min, max = lb.max, size = lb.size, centre = lb.center;

        float width, height = size.y * 0.99f;
        float doorCentreY = centre.y;
        Vector3 hingePos;
        Vector3 panelOffset;
        float sign = hingeOnOtherSide ? -1f : 1f;

        switch (doorFace)
        {
            case Face.BackZ:
                width = size.x * 0.96f;
                hingePos = new Vector3(centre.x - sign * width * 0.5f, doorCentreY, min.z);
                panelOffset = new Vector3(sign * width * 0.5f, 0f, -doorThickness * 0.5f);
                break;
            case Face.RightX:
                width = size.z * 0.96f;
                hingePos = new Vector3(max.x, doorCentreY, centre.z - sign * width * 0.5f);
                panelOffset = new Vector3(doorThickness * 0.5f, 0f, sign * width * 0.5f);
                break;
            case Face.LeftX:
                width = size.z * 0.96f;
                hingePos = new Vector3(min.x, doorCentreY, centre.z - sign * width * 0.5f);
                panelOffset = new Vector3(-doorThickness * 0.5f, 0f, sign * width * 0.5f);
                break;
            default: // FrontZ
                width = size.x * 0.96f;
                hingePos = new Vector3(centre.x - sign * width * 0.5f, doorCentreY, max.z);
                panelOffset = new Vector3(sign * width * 0.5f, 0f, doorThickness * 0.5f);
                break;
        }

        GameObject hinge = new GameObject("DoorHinge");
        hinge.transform.SetParent(transform, false);
        hinge.transform.localPosition = hingePos;
        hinge.transform.localRotation = Quaternion.identity;

        GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        panel.name = "FridgeDoor";
        panel.layer = 0;
        Destroy(panel.GetComponent<Collider>());
        panel.transform.SetParent(hinge.transform, false);
        panel.transform.localPosition = panelOffset;
        panel.transform.localRotation = Quaternion.identity;

        bool depthAxisIsX = (doorFace == Face.RightX || doorFace == Face.LeftX);
        panel.transform.localScale = depthAxisIsX
            ? new Vector3(doorThickness, height, width)
            : new Vector3(width, height, doorThickness);

        Renderer panelRenderer = panel.GetComponent<Renderer>();
        Renderer source = null;
        float best = -1f;
        foreach (Renderer r in renderers)
        {
            float v = r.bounds.size.x * r.bounds.size.y * r.bounds.size.z;
            if (v > best) { best = v; source = r; }
        }
        if (source != null && source.sharedMaterial != null)
        {
            panelRenderer.sharedMaterial = source.sharedMaterial;
        }

        Debug.Log("Fridge: auto-built door panel (" + doorFace + ").");
        return hinge.transform;
    }
}
