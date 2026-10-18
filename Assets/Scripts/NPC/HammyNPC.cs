using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class HammyNPC : MonoBehaviour, IInteractable
{
    [Header("Visual")]
    [Tooltip("Optional. Instantiated as a child at Awake ONLY if no model is already sitting under this object. Leave empty in the Backyard scene - Hammy's model is already there.")]
    [SerializeField] private GameObject modelPrefab;
    [Tooltip("The actual model to turn and face Bandit with.")]
    [SerializeField] private GameObject visualModel;

    [Header("Proximity")]
    [SerializeField] private float promptRange = 3f;
    [Tooltip("Only used by the runtime fallback UI. Ignored when the UI references below are assigned.")]
    [SerializeField] private float uiHeight = 1.6f;
    [Tooltip("Hammy turns to face Bandit directly whenever Bandit is within promptRange - i.e. close enough to actually interact. Slow, calm turn (see turnSpeed) - not a snappy one.")]
    [SerializeField] private float turnSpeed = 60f;
    [Tooltip("Degrees (around Y) from the model's local +Z that its visual front actually faces.")]
    [SerializeField] private float modelForwardOffsetY = 180f;

    [Header("World-space UI (assign the existing HammyWorldUI children here)")]
    [Tooltip("The 'Press E to talk to Hammy' prompt GameObject (child of HammyWorldUI).")]
    [SerializeField] private GameObject promptObject;
    [Tooltip("The speech bubble background GameObject (child of HammyWorldUI).")]
    [SerializeField] private GameObject speechBubbleObject;
    [Tooltip("The TextMeshProUGUI component inside the speech bubble.")]
    [SerializeField] private TextMeshProUGUI speechText;

    [Header("Colours")]
    [SerializeField] private Color friendlyColor = new Color(0.85f, 0.55f, 0.1f);

    [Header("Voice")]
    [Tooltip("How fast Hammy's dialogue types out, in characters per second.")]
    [SerializeField] private float lettersPerSecond = 38f;
    [Tooltip("Plays a soft procedural 'hm' blip as each line types out - the same babble voice Toby uses.")]
    [SerializeField] private bool babbleVoice = true;
    [Tooltip("Volume of Hammy's babble voice.")]
    [SerializeField, Range(0f, 1f)] private float voiceVolume = 0.6f;

    [Header("Golden Carrot Heal - model & sizing")]
    [Tooltip("Preferred visual: a pixel-art golden-carrot icon. When assigned, this is used instead of carrotModelPrefab.")]
    [SerializeField] private Sprite carrotSprite;
    [Tooltip("How tall carrotSprite should appear in the world, used as a fallback ONLY if Bandit's renderers can't be measured at runtime.")]
    [SerializeField] private float carrotSpriteFallbackHeight = 0.3f;
    [Tooltip("The golden carrot model to show if carrotSprite isn't assigned. Assign Assets/Models/Carrot/Carrot.obj here. Leave both empty to fall back to a simple placeholder sphere.")]
    [SerializeField] private GameObject carrotModelPrefab;
    [Tooltip("How tall the carrot should appear, as a fraction of Bandit's own measured height (the combined bounds of every renderer under him).")]
    [SerializeField] private float carrotHeightFraction = 0.5f;
    [Tooltip("Fallback uniform scale for carrotModelPrefab, used ONLY if Bandit's renderers can't be measured at runtime. The raw converted mesh is ~4.3 Unity units tall, so 0.08 lands it around 35cm.")]
    [SerializeField] private float carrotModelScale = 0.08f;
    [Tooltip("Local rotation (Euler) applied to carrotModelPrefab, in case its authored orientation doesn't read as 'upright'.")]
    [SerializeField] private Vector3 carrotRotationOffset = Vector3.zero;

    [Header("Golden Carrot Heal - hand-off & eating")]
    [Tooltip("Name of Bandit's hand bone the carrot flies to and is held at, searched recursively under the player. His rig has a 'Right_Hand' bone (Raccoon_Root/Right_Arm/Right_Hand).")]
    [SerializeField] private string handBoneName = "Right_Hand";
    [Tooltip("Name of Bandit's head bone, used as the 'raise the carrot up to his face' destination, searched recursively under the player.")]
    [SerializeField] private string headBoneName = "Head";
    [Tooltip("Local offset from the hand bone (in ITS own local space, so it's correct regardless of the rig's scale). (0,0,0) = exactly at the hand bone.")]
    [SerializeField] private Vector3 carrotHandOffset = Vector3.zero;
    [Tooltip("Local offset from the head bone (in ITS own local space) for where the carrot ends up once raised to his face/mouth.")]
    [SerializeField] private Vector3 carrotMouthOffset = new Vector3(0f, -0.01f, 0.03f);
    [Tooltip("How long the carrot takes to fly from Hammy's paws to Bandit's hand.")]
    [SerializeField] private float carrotHandoffDuration = 0.6f;
    [Tooltip("How long Bandit holds the carrot in-hand before raising it.")]
    [SerializeField] private float carrotHoldDuration = 0.3f;
    [Tooltip("How long the 'raise the carrot up to his face, turning it over' motion takes.")]
    [SerializeField] private float carrotRaiseDuration = 1.3f;
    [Tooltip("How far the carrot is lifted from Bandit's hand toward his mouth before he eats it. 0 = stays at the hand, 1 = all the way up to the mouth anchor.")]
    [Range(0f, 1f)]
    [SerializeField] private float carrotLiftToMouth = 1f;
    [Tooltip("How many degrees the carrot spins over while being raised to his mouth.")]
    [SerializeField] private float carrotSpinDegrees = 270f;
    [Tooltip("Played once the carrot reaches Bandit's mouth.")]
    [SerializeField] private AudioClip carrotEatingClip;
    [Tooltip("Total time the eating 'beat' takes if carrotEatingClip's own length can't be read. The carrot vanishes partway through this beat, but the heal doesn't apply until the whole beat has played out.")]
    [SerializeField] private float carrotEatFallbackDuration = 2.5f;
    [Tooltip("How long the golden outline glow lasts on Bandit after he eats the carrot (sweeps head to paws, then fades).")]
    [SerializeField] private float goldenGlowDuration = 1.5f;

    private Transform player;
    private Animator playerAnimator;
    private Transform handAnchor; // Bandit's actual hand bone, if found
    private Transform headAnchor; // Bandit's actual head bone, if found
    private bool playerInRange = false;
    private bool modelBuilt = false;

    // Facing calibration, captured once in Awake() - see FacePlayer() below.
    private Quaternion modelRestRotation = Quaternion.identity;
    private Vector3 modelRestForwardFlat = Vector3.forward;

    private HeldItem heldCarrot;
    private bool exchangeInProgress = false; // true for the whole automatic hand-off/raise/eat/heal sequence

    private AudioSource voice;
    private AudioClip babbleClip;
    private Coroutine typing;
    private bool isTyping;

    // The converted carrot mesh (Assets/Models/Carrot/Carrot.obj) measures
    // ~4.335 Unity units tall in its raw, unscaled form (from the original
    // source model's bounds: y ranges roughly -2.136 to 2.199).
    private const float CarrotRawMeshHeight = 4.335f;

    private static readonly string[] AlreadyHelpedLines =
    {
        "Sorry, Bandit - that golden carrot back there was my only one tonight, and I already used it on you. You've got what it takes now, buddy - go finish this!",
        "Wish I had another for you, pal, but one golden carrot a night is all Hammy's got. Trust those quick paws, Bandit - you're almost there, I can feel it!",
        "Already did my bit for you tonight, remember? No more carrots in the stash, Bandit - just you now. Go show 'em what a raccoon can do!",
    };

    // Shown when Bandit hasn't taken any damage yet - full lives, full health -
    // and Hammy's one-time heal hasn't been used. He's still got it saved up
    // for later, so this does NOT set hammyHealUsed; come back hurt and the
    // heal will still be there waiting.
    private static readonly string[] NotHurtYetLines =
    {
        "Hey there, Bandit! Hammy's the name - and not a scratch on you, nice! I'll save this golden carrot for when you really need it. Past Rex, clear of his toys, and that fence is yours!",
        "Well would you look at you, Bandit - no scrapes, no scratches! Keep it up. Slip past Rex, mind his toys, and you'll be home free before you know it.",
        "Nothing for ol' Hammy to patch up here, buddy - you're doing great! Past Rex, clear of his toys, and victory's all yours tonight.",
    };

    // First line of the heal - Hammy offers up one of his own stashed golden
    // carrots. Nothing changes hands from the player's side; he just produces it.
    private static readonly string[] AskForCarrotLines =
    {
        "Whoa, Bandit - you're looking a little worse for wear! Good thing Hammy always keeps a golden carrot stashed for nights like this. Here, eat up - it'll set you right!",
        "Ooh, you've taken a beating out there, Bandit! Lucky for you, I squirrel away more than just nuts. Here's a golden carrot, fresh from my stash - one bite and you'll feel brand new.",
        "Yikes, rough night so far, huh, Bandit? Don't you worry, pal - I've got just the thing. Here's one of my golden carrots. Trust me, you'll be right as rain.",
    };

    // Shown once the golden carrot's been eaten and the heal actually applies.
    private const string LifeRestoredLine =
        "There you go, Bandit - one golden carrot, straight from Hammy's own stash! That's a life back in your pocket, buddy. Us backyard critters gotta look out for each other.\n\nNow get out there - past Rex, clear of his toys, and go win this thing!";
    private const string HealthPatchedLine =
        "There you go, Bandit - one golden carrot, straight from Hammy's own stash! Feel that? Good as new, buddy.\n\nNow get out there - past Rex, clear of his toys, and go win this thing!";

    private void Awake()
    {
        BuildModelIfNeeded();

        voice = GetComponent<AudioSource>();
        if (voice == null) voice = gameObject.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.spatialBlend = 0.6f;
        voice.minDistance = 2f;
        voice.maxDistance = 20f;

        // Only build a runtime UI if the designer hasn't assigned the scene-authored one.
        if (promptObject == null || speechBubbleObject == null || speechText == null)
        {
            BuildWorldSpaceUI();
        }

        // Facing calibration.
        if (visualModel != null)
        {
            modelRestRotation = visualModel.transform.rotation;

            Transform head = FindDeepChild(visualModel.transform, "Head");
            Vector3 localForward;
            if (head != null)
            {
                Vector3 headLocal = visualModel.transform.InverseTransformDirection(head.position - visualModel.transform.position);
                headLocal.y = 0f;
                localForward = headLocal.sqrMagnitude > 0.0001f
                    ? headLocal.normalized
                    : (Quaternion.Euler(0f, modelForwardOffsetY, 0f) * Vector3.forward);
            }
            else
            {
                localForward = Quaternion.Euler(0f, modelForwardOffsetY, 0f) * Vector3.forward;
            }

            Vector3 f = modelRestRotation * localForward;
            f.y = 0f;
            if (f.sqrMagnitude > 0.0001f) modelRestForwardFlat = f.normalized;

            Debug.Log($"[HammyNPC] Facing calibration - local forward: {localForward}, world rest forward: {modelRestForwardFlat}, Head child found: {head != null}.");
        }
    }

    private void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
            playerAnimator = playerObj.GetComponent<Animator>();

            // The visible rig is a separately scaled child of the root, so find the actual bones directly.
            handAnchor = FindDeepChild(player, handBoneName);
            headAnchor = FindDeepChild(player, headBoneName);

            if (handAnchor == null)
                Debug.LogWarning($"[HammyNPC] Couldn't find a '{handBoneName}' bone under the player - falling back to the player's root transform for the golden carrot.");
            if (headAnchor == null)
                Debug.LogWarning($"[HammyNPC] Couldn't find a '{headBoneName}' bone under the player - the carrot will raise to the hand position instead of his face.");
        }

        if (promptObject != null) promptObject.SetActive(false);
        if (speechBubbleObject != null) speechBubbleObject.SetActive(false);
    }

    private void Update()
    {
        if (player == null) return;

        float dist = Vector3.Distance(transform.position, player.position);
        FacePlayer(dist);

        bool inRange = dist <= promptRange;
        if (inRange == playerInRange) return;

        playerInRange = inRange;

        if (!playerInRange)
        {
            if (promptObject != null) promptObject.SetActive(false);
            if (speechBubbleObject != null) speechBubbleObject.SetActive(false);
        }
        else if (speechBubbleObject == null || !speechBubbleObject.activeSelf)
        {
            if (promptObject != null) promptObject.SetActive(true);
        }
    }

    // Turns Hammy's model to face Bandit directly whenever Bandit is close enough to actually interact.
    private void FacePlayer(float dist)
    {
        if (visualModel == null || player == null) return;

        Quaternion want;
        if (dist <= promptRange)
        {
            Vector3 to = player.position - visualModel.transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) return;
            to.Normalize();

            Quaternion delta = Quaternion.FromToRotation(modelRestForwardFlat, to);
            want = delta * modelRestRotation;
        }
        else
        {
            want = modelRestRotation;
        }

        visualModel.transform.rotation = Quaternion.RotateTowards(visualModel.transform.rotation, want, turnSpeed * Time.deltaTime);
    }

    public void Interact()
    {
        // The carrot sequence is already playing out - ignore extra presses
        // until it finishes on its own.
        if (exchangeInProgress) return;

        if (promptObject != null) promptObject.SetActive(false);

        if (GameManager.Instance == null)
        {
            ShowLine("Hmm, something's not right - come back in a moment!", LoadVoiceClip("hammy_error_1"));
            return;
        }

        // Once the one-time heal has actually been used, every later visit
        // gets the "already helped" brush-off, no matter what state Bandit's
        // health/lives are in now.
        if (GameManager.Instance.hammyHealUsed)
        {
            int i = Random.Range(0, AlreadyHelpedLines.Length);
            ShowLine(AlreadyHelpedLines[i], LoadVoiceClip("hammy_alreadyhelped_" + (i + 1)));
            return;
        }

        PlayerHealth health = player != null ? player.GetComponent<PlayerHealth>() : null;
        bool isDamaged = GameManager.Instance.currentLives < GameManager.Instance.startingLives
                          || (health != null && health.IsAtHalfHealth());

        // Not hurt yet - Hammy holds onto his one-time heal rather than
        // "wasting" it on a visit where Bandit didn't need it. Come back
        // later after taking a hit and the heal will still be available.
        if (!isDamaged)
        {
            int i = Random.Range(0, NotHurtYetLines.Length);
            ShowLine(NotHurtYetLines[i], LoadVoiceClip("hammy_nothurtyet_" + (i + 1)));
            return;
        }

        // Bandit's actually down in health/lives, and this is the first
        // qualifying visit since that happened - play the one-time golden
        // carrot heal out automatically from here.
        bool healedLife = GameManager.Instance.currentLives < GameManager.Instance.startingLives;
        StartCoroutine(RunCarrotHeal(healedLife));
    }

    // Hammy produces one of his own golden carrots, it flies over to Bandit's hand, he raises it up to his face and eats it.
    private IEnumerator RunCarrotHeal(bool healedLife)
    {
        exchangeInProgress = true;

        int askIndex = Random.Range(0, AskForCarrotLines.Length);
        ShowLine(AskForCarrotLines[askIndex], LoadVoiceClip("hammy_askcarrot_" + (askIndex + 1)));
        while (isTyping) yield return null; // let the offer line finish typing out
        yield return new WaitForSeconds(0.4f); // brief buffer after it lands before anything appears

        // 1) The carrot appears, already golden, near Hammy's own paws.
        SpawnCarrot();
        heldCarrot.transform.position = GetHammyPawWorldPosition();
        heldCarrot.transform.rotation = transform.rotation;
        yield return heldCarrot.PlayAppearEffect(0.25f);

        // 2) It flies from Hammy over to Bandit's actual hand.
        Transform hand = handAnchor != null ? handAnchor : player;
        if (hand != null)
            yield return heldCarrot.FlyTo(hand, carrotHandOffset, carrotHandoffDuration);

        // A short beat holding it in-hand before raising it to eat.
        if (hand != null)
        {
            heldCarrot.SnapToHand(hand, carrotHandOffset);
            yield return new WaitForSeconds(carrotHoldDuration);
        }

        // 3) Raise it up to his face, spinning it over as it goes.
        if (playerAnimator != null && HasParameter(playerAnimator, "Eat"))
            playerAnimator.SetTrigger("Eat");

        Transform mouth = headAnchor != null ? headAnchor : hand;
        Vector3 mouthOffset = headAnchor != null ? carrotMouthOffset : carrotHandOffset;
        if (mouth != null)
            yield return heldCarrot.RaiseAndTurn(hand, carrotHandOffset, mouth, mouthOffset,
                                                 carrotRaiseDuration, carrotLiftToMouth, carrotSpinDegrees);

        // 4) Eating: the sound plays, the carrot vanishes shortly after, and
        // this waits out the rest of the eating "beat" (matched to the clip's
        // own length when we have one) before the heal actually lands.
        float eatDuration = (carrotEatingClip != null) ? carrotEatingClip.length : carrotEatFallbackDuration;
        // The golden glow starts the instant the carrot vanishes (eaten),
        // not after the rest of the eating beat has played out.
        yield return heldCarrot.PlayEatSequence(carrotEatingClip, eatDuration, () =>
        {
            if (player != null) GoldenGlowOverlay.Play(player.gameObject, goldenGlowDuration);
        });

        Destroy(heldCarrot.gameObject);
        heldCarrot = null;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.hammyHealUsed = true;

            if (healedLife)
            {
                GameManager.Instance.currentLives++;
            }
            else
            {
                PlayerHealth health = player != null ? player.GetComponent<PlayerHealth>() : null;
                if (health != null && health.IsAtHalfHealth()) health.Heal();
            }
        }

        exchangeInProgress = false;
        ShowLine(healedLife ? LifeRestoredLine : HealthPatchedLine,
                 LoadVoiceClip(healedLife ? "hammy_liferestored_1" : "hammy_healthpatched_1"));
    }

    private void SpawnCarrot()
    {
        if (heldCarrot == null)
        {
            GameObject go = new GameObject("HeldCarrot (Hammy heal)");
            heldCarrot = go.AddComponent<HeldItem>();

            if (carrotSprite != null)
                heldCarrot.BuildSpriteVisual(carrotSprite, ComputeCarrotWorldHeight());
            else
                heldCarrot.BuildVisual(carrotModelPrefab, ComputeCarrotScale(), carrotRotationOffset);
        }

        heldCarrot.SetGolden(true); // Hammy's carrot is already golden - it has no "plain" state (a no-op for the sprite visual, which already has the gold baked into the art)
        heldCarrot.Show(true);
    }

    // Sizes the carrot as a fraction of Bandit's OWN rendered height, measured at runtime from the combined bounds of every renderer under him.
    private float ComputeCarrotScale()
    {
        float bandsHeight = player != null ? GetCombinedRenderedHeight(player) : 0f;
        if (bandsHeight > 0f) return (bandsHeight * carrotHeightFraction) / CarrotRawMeshHeight;
        return carrotModelScale;
    }

    // Same idea as ComputeCarrotScale, but returns a target WORLD height
    // directly rather than a mesh-relative scale factor - what the sprite
    // path needs, since a sprite's size is driven by its pixels-per-unit,
    // not a raw mesh height constant like CarrotRawMeshHeight.
    private float ComputeCarrotWorldHeight()
    {
        float bandsHeight = player != null ? GetCombinedRenderedHeight(player) : 0f;
        if (bandsHeight > 0f) return bandsHeight * carrotHeightFraction;
        return carrotSpriteFallbackHeight;
    }

    private static float GetCombinedRenderedHeight(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return 0f;
        Bounds combined = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) combined.Encapsulate(renderers[i].bounds);
        return combined.size.y;
    }

    // Hammy's model has no rigged hands to anchor to.
    private Vector3 GetHammyPawWorldPosition()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return transform.position;

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

        return b.center + Vector3.up * (b.extents.y * 0.15f) + transform.forward * (b.extents.z * 0.7f);
    }

    private static bool HasParameter(Animator animator, string paramName)
    {
        foreach (AnimatorControllerParameter p in animator.parameters)
        {
            if (p.name == paramName) return true;
        }
        return false;
    }

    // Recursively finds a descendant transform by name, to locate rig bones without hard-coding the path.
    private static Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            Transform found = FindDeepChild(child, name);
            if (found != null) return found;
        }
        return null;
    }

    // --------------------------------------------------------------------- Talking: typewriter + babble voice (same system as Toby's dialogue)
    private static AudioClip LoadVoiceClip(string name) => Resources.Load<AudioClip>("Audio/Voices/" + name);

    private void ShowLine(string text, AudioClip clip = null)
    {
        if (speechBubbleObject != null) speechBubbleObject.SetActive(true);
        if (speechText == null) return;

        speechText.text = text;
        speechText.color = friendlyColor;

        StopTyping();
        typing = StartCoroutine(TypeOut(text, clip));
    }

    private IEnumerator TypeOut(string text, AudioClip clip)
    {
        isTyping = true;
        speechText.maxVisibleCharacters = 0;
        speechText.ForceMeshUpdate();
        int total = speechText.textInfo.characterCount;

        if (clip != null)
        {
            voice.pitch = 1f;
            voice.PlayOneShot(clip, voiceVolume * SoundFX.SfxVolumeScale);
        }

        // With a recorded clip, spread the typing over the clip's length so the text finishes revealing roughly when Hammy stops talking.
        float pauseTime = 0f;
        foreach (char ch in text)
        {
            if (ch == ',' || ch == '-') pauseTime += 0.08f;
            else if (ch == '.' || ch == '!' || ch == '?') pauseTime += 0.18f;
        }
        float revealTime = clip != null ? clip.length - pauseTime : 0f;
        float cps = clip != null ? Mathf.Max(lettersPerSecond * 0.5f, total / Mathf.Max(0.5f, revealTime)) : lettersPerSecond;
        int lastPause = -1;

        float shown = 0f;
        int lastBlip = -10;
        while (shown < total)
        {
            shown += cps * Time.deltaTime;
            int visible = Mathf.Min(total, Mathf.FloorToInt(shown));
            speechText.maxVisibleCharacters = visible;

            // Babble: a blip every couple of letters (skipping spaces) -
            // only when there's no recorded clip actually playing.
            if (clip == null && babbleVoice && visible - lastBlip >= 2 && visible > 0)
            {
                char c = text[Mathf.Min(visible - 1, text.Length - 1)];
                if (char.IsLetterOrDigit(c))
                {
                    PlayBlip();
                    lastBlip = visible;
                }
            }

            yield return null;

            // A comma or full stop gives a tiny natural pause.
            if (visible > 0 && visible <= text.Length && visible != lastPause)
            {
                lastPause = visible;
                char c = text[visible - 1];
                if (c == ',' || c == '.' || c == '!' || c == '?' || c == '-')
                {
                    yield return new WaitForSeconds(c == ',' || c == '-' ? 0.08f : 0.18f);
                }
            }
        }

        FinishTyping();
    }

    private void FinishTyping()
    {
        if (typing != null) StopCoroutine(typing);
        typing = null;
        isTyping = false;
        if (speechText != null) speechText.maxVisibleCharacters = 99999;
    }

    private void StopTyping()
    {
        FinishTyping();
        if (voice != null) voice.Stop();
    }

    private void PlayBlip()
    {
        if (babbleClip == null) babbleClip = MakeBabbleClip();
        voice.pitch = Random.Range(0.85f, 1.2f);
        voice.PlayOneShot(babbleClip, voiceVolume * 0.7f * SoundFX.SfxVolumeScale);
    }

    // A short, soft, low "hm" - generated in code so nothing needs recording.
    // Same formula as TobyNPC's babble clip, just cached separately per instance.
    private static AudioClip MakeBabbleClip()
    {
        int rate = 44100;
        float dur = 0.07f;
        int n = Mathf.CeilToInt(rate * dur);
        float[] data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float env = Mathf.Sin(Mathf.PI * t / dur);                 // soft in and out
            float f = Mathf.Lerp(260f, 200f, t / dur);                 // slight downward "mm"
            float s = Mathf.Sin(2f * Mathf.PI * f * t) + 0.35f * Mathf.Sin(4f * Mathf.PI * f * t);
            data[i] = s * env * 0.35f;
        }
        AudioClip clip = AudioClip.Create("HammyBabble", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private void BuildModelIfNeeded()
    {
        if (modelBuilt) return;
        modelBuilt = true;

        // If there's already a child (Hammy's real model is sitting here), don't spawn anything else.
        if (transform.childCount > 0)
        {
            if (visualModel == null)
            {
                Transform fallback = null;
                foreach (Transform child in transform)
                {
                    if (child.GetComponent<Canvas>() != null) continue; // the world-space UI, not the model
                    if (fallback == null) fallback = child;
                    if (child.GetComponentInChildren<Renderer>() != null)
                    {
                        visualModel = child.gameObject;
                        break;
                    }
                }
                // Nothing had a Renderer (shouldn't normally happen) - fall
                // back to the old "first non-UI child" behaviour rather
                // than leaving visualModel null and Hammy never turning at all.
                if (visualModel == null && fallback != null) visualModel = fallback.gameObject;

                Debug.Log(visualModel != null
                    ? $"[HammyNPC] Auto-picked '{visualModel.name}' as the visual model to face Bandit with."
                    : "[HammyNPC] Couldn't find any child to use as the visual model - Hammy won't turn to face Bandit. Assign 'Visual Model' by hand in the Inspector.");
            }
            return;
        }
        if (modelPrefab == null) return;

        GameObject instance = Instantiate(modelPrefab, transform);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        if (visualModel == null) visualModel = instance;
    }

    // Fallback only - builds a minimal UI if you forgot to wire the fields.
    private void BuildWorldSpaceUI()
    {
        Debug.LogWarning("[HammyNPC] UI references not assigned. Building a runtime fallback. " +
                         "Assign the existing HammyWorldUI pieces in the Inspector to use the scene-authored UI.");

        GameObject canvasGO = new GameObject("HammyUI (auto)");
        canvasGO.transform.SetParent(transform, false);
        canvasGO.transform.localPosition = new Vector3(0f, uiHeight, 0f);

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform canvasRect = canvasGO.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(400f, 160f);
        canvasGO.transform.localScale = Vector3.one * 0.01f;
        canvasGO.AddComponent<Billboard>();

        promptObject = new GameObject("PressEPrompt");
        promptObject.transform.SetParent(canvasGO.transform, false);
        RectTransform promptRect = promptObject.AddComponent<RectTransform>();
        promptRect.anchorMin = new Vector2(0.5f, 0f);
        promptRect.anchorMax = new Vector2(0.5f, 0f);
        promptRect.anchoredPosition = new Vector2(0f, 0f);
        promptRect.sizeDelta = new Vector2(380f, 50f);
        TextMeshProUGUI promptText = promptObject.AddComponent<TextMeshProUGUI>();
        promptText.text = "Press E to talk to Hammy";
        promptText.fontSize = 28;
        promptText.alignment = TextAlignmentOptions.Center;
        promptText.color = Color.white;

        speechBubbleObject = new GameObject("SpeechBubble");
        speechBubbleObject.transform.SetParent(canvasGO.transform, false);
        RectTransform bubbleRect = speechBubbleObject.AddComponent<RectTransform>();
        bubbleRect.anchorMin = new Vector2(0.5f, 1f);
        bubbleRect.anchorMax = new Vector2(0.5f, 1f);
        bubbleRect.anchoredPosition = new Vector2(0f, 0f);
        bubbleRect.sizeDelta = new Vector2(400f, 160f);
        Image bubbleImage = speechBubbleObject.AddComponent<Image>();
        bubbleImage.color = new Color(0.08f, 0.07f, 0.06f, 0.92f); // matches the scene-authored panel, not the old opaque white

        GameObject textGO = new GameObject("SpeechText");
        textGO.transform.SetParent(speechBubbleObject.transform, false);
        RectTransform textRect = textGO.AddComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0f, 0f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.offsetMin = new Vector2(16f, 12f);
        textRect.offsetMax = new Vector2(-16f, -12f);
        speechText = textGO.AddComponent<TextMeshProUGUI>();
        speechText.fontSize = 22;
        speechText.alignment = TextAlignmentOptions.Center;
        speechText.color = friendlyColor;
    }
}
