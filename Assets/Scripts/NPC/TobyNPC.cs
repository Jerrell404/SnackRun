using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// Toby: friendly tortoise NPC at the Front Yard start. Gives Bandit the stealth briefing on interact (E).
public class TobyNPC : MonoBehaviour, IInteractable
{
    private enum LineType { Greeting, Hint, Warning, Info, Send }

    private struct Line
    {
        public readonly string text;
        public readonly LineType type;
        public Line(string text, LineType type) { this.text = text; this.type = type; }
    }

    [Header("Visual")]
    [Tooltip("Toby's model prefab (e.g. Toby.fbx). Instantiated as a child at Awake if Visual Model is empty.")]
    [SerializeField] private GameObject modelPrefab;
    [Tooltip("A model instance already placed as a child in the scene. Leave empty if using modelPrefab above.")]
    [SerializeField] private GameObject visualModel;

    [Header("Proximity")]
    [Tooltip("How close Bandit needs to be for the 'Press E to talk to Toby' prompt to appear.")]
    [SerializeField] private float promptRange = 3f;
    [Tooltip("UNUSED - Toby now always turns to face wherever Bandit is, at any distance, so this no longer gates anything (kept only so any Inspector value already set on the prefab isn't lost). See FacePlayer().")]
    [SerializeField] private float lookAtRange = 15f;
    [SerializeField] private float turnSpeed = 60f; // slow, calm, tortoise-like turn - not a snappy 180
    [Tooltip("Below this distance, Toby turns up to Close Range Turn Speed Multiplier times FASTER than Turn Speed above.")]
    [SerializeField] private float closeRangeTurnSpeedMultiplier = 4f;
    [Tooltip("Distance at which the close-range turn speed boost above has fully faded back down to the normal Turn Speed.")]
    [SerializeField] private float closeRangeTurnBoostDistance = 3f;
    [Tooltip("Degrees (around Y) from the model's local +Z that its visual front actually faces. This is the ONLY thing that calibrates which way Toby faces.")]
    [SerializeField] private float modelForwardOffsetY = 0f;

    [Header("World-space UI (assigned in the Inspector)")]
    [SerializeField] private GameObject promptObject;
    [SerializeField] private GameObject speechBubble;
    [SerializeField] private TextMeshProUGUI speechText;

    [Header("Speech bubble colours (white bubble, coloured text per line type)")]
    // These were originally picked as dark colours assuming an opaque WHITE speech bubble.
    [SerializeField] private Color greetingColor = new Color(0.58f, 0.95f, 0.58f);  // light leaf green - ~11-14:1 on the dark panel
    [SerializeField] private Color hintColor = new Color(0.60f, 0.80f, 1.00f);      // light sky blue - ~9-11:1 on the dark panel
    [SerializeField] private Color warningColor = new Color(1.00f, 0.70f, 0.30f);   // warm amber - ~8.5-10.5:1 on the dark panel
    [SerializeField] private Color infoColor = new Color(0.92f, 0.92f, 0.92f);      // near-white - ~13-16:1 on the dark panel

    [Header("Talking")]
    [Tooltip("Letters per second when a line types out.")]
    [SerializeField] private float lettersPerSecond = 38f;
    [Tooltip("Plays a soft turtle 'babble' blip while letters appear (used for lines with no recorded clip).")]
    [SerializeField] private bool babbleVoice = true;
    [SerializeField, Range(0f, 1f)] private float voiceVolume = 0.6f;
    // Toby's recorded lines are no longer wired up as Inspector arrays -
    // see LoadVoiceClip() below, which loads each clip by name straight
    // out of Resources/Audio/Voices instead (same approach as Edgar and
    // Hammy), so there's nothing to manually assign here.

    [Tooltip("How much Toby nods his head while talking (degrees).")]
    [SerializeField] private float nodAmount = 7f;
    [Tooltip("Off = Toby never turns around (he keeps the way he's placed in the scene).")]
    [SerializeField] private bool turnToFacePlayer = false;
    [Tooltip("Off = Toby's head stays still while he talks.")]
    [SerializeField] private bool nodWhileTalking = false;

    private Transform player;
    private bool playerInRange = false;
    private int dialogueIndex = -1;          // -1 = hasn't started talking this visit
    private bool isReturnVisit = false;
    private List<int> extraTipOrder; // shuffled indices into ExtraTips, so the clip name for a picked tip stays known

    private AudioSource voice;
    private AudioClip babbleClip;
    private Coroutine typing;
    private bool isTyping;
    private Transform head;
    private Quaternion headRest;
    private float talkAmount;               // 0..1, drives the head nod

    // Captured once at Awake, before FacePlayer ever runs: whichever way the model happens to be facing as hand-placed in the scene.
    private Quaternion modelRestRotation = Quaternion.identity;
    private Vector3 modelRestForwardFlat = Vector3.forward;
    // Heartbeat log timer - see the LIVE CHECK log in Update() below.
    private float facingDebugTimer = 0f;

    // Heard the whole briefing at least once this game.
    private static bool heardBriefing = false;

    // How many EXTRA TIPS (beyond the initial briefing/send-off) Toby has
    // handed out so far this game - capped so mashing E over and over
    // doesn't just cycle through every tip forever. Mirrors heardBriefing:
    // static, persists for the whole game.
    private static int extraTipsShown = 0;
    private const int MaxExtraTips = 2;

    private static readonly List<TobyNPC> all = new List<TobyNPC>();
    private void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    private void OnDisable() { all.Remove(this); }

    /// Called by PlayerInteract on E: if Bandit is close to Toby, Toby talks -
    /// no need to aim the camera exactly at him.
    public static bool TryHandleNearby(Vector3 playerPos)
    {
        foreach (TobyNPC t in all)
        {
            if (t != null && Vector3.Distance(t.transform.position, playerPos) <= t.promptRange)
            {
                t.Interact();
                return true;
            }
        }
        return false;
    }

    // ---------------------------------------------------------------------
    //  Dialogue
    // ---------------------------------------------------------------------

    // Short conversations
    // Press E once: Toby says hello, then 2 tips, each moving on by itself.
    // Talking again gives 2 different tips (they rotate through all of them).
    private const int TipsPerTalk = 4;
    private static readonly Line Greeting =
        new Line("Hey Bandit! Name's Toby. Here's what you need to know.", LineType.Greeting);
    private static readonly Line GoodLuck =
        new Line("Good luck, Bandit!", LineType.Send);
    // Always all four, in this order - one tip per area, so one chat covers everything.
    private static readonly Line[] Tips =
    {
        new Line("Out here, stay out of the garden lights and watch for traps.", LineType.Warning),
        new Line("Hold Shift to sneak. If Edgar spots you, hide somewhere and press E.", LineType.Hint),
        new Line("Inside, only grab the snacks on your list, and don't leave the fridge open. It wakes Whiskers.", LineType.Info),
        new Line("Out back, dodge the lasers and leave Rex's toys alone. Hurt? Find Hammy.", LineType.Warning),
    };
    private static readonly List<int> tipBag = new List<int>();
    private Coroutine conversation;
    private bool advanceNow;

    // First visit, in this order. (Record voice lines in this order too.)
    private static readonly Line[] BriefingLines =
    {
        new Line("Psst! Down here, Bandit! Name's Toby. Before you go charging up that path, you'd better hear this.", LineType.Greeting),
        new Line("See those garden lights? Step in their glow and the clock starts ticking. Linger too long and Edgar, the human who lives here, will spot you.", LineType.Warning),
        new Line("And watch the grass - there are animal traps all over this yard. Step on one and it'll snap right shut on you!", LineType.Warning),
        new Line("Hold Shift to crouch and creep along quietly. Hold R only when you really need to run for it.", LineType.Hint),
        new Line("Spotted? Find a hiding spot and press E to tuck yourself away. E again to pop back out.", LineType.Hint),
        new Line("Inside the house there's a shopping list - five snacks, different every night. Grab the WRONG snack and you'll make a racket.", LineType.Info),
        new Line("Whiskers wakes up the second that fridge opens. Get in, get the goods, and get out fast.", LineType.Warning),
        new Line("Out back there's lasers, and Rex. Whatever you do, don't touch his toys. Rex doesn't give second chances.", LineType.Warning),
        new Line("You've got three lives. Scratches and lasers hurt you first - a second hit costs a life. Traps and getting caught cost one straight away.", LineType.Info),
    };

    private static readonly Line SendOff =
        new Line("That's all I've got. Now go on - slow and steady... well, maybe not TOO slow. Good luck, Bandit!", LineType.Send);

    private static readonly Line[] ReturnGreetings =
    {
        new Line("Back again, Bandit? Here's one more tip for you.", LineType.Greeting),
        new Line("Still here? Alright, alright - listen up.", LineType.Greeting),
    };

    private static readonly Line[] ExtraTips =
    {
        new Line("Jumping or sprinting will pop you right out of a hiding spot.", LineType.Hint),
        new Line("Stepping on a chip bag in the kitchen crunches loudly - and noise gets noticed.", LineType.Warning),
        new Line("The kitchen clock is ticking. Two minutes, then Edgar comes looking.", LineType.Warning),
        new Line("Check the list in the corner of your screen - it tells you which snacks to grab this time.", LineType.Hint),
        new Line("There's a squirrel called Hammy out in the backyard. Hammy might patch you up if you're hurt.", LineType.Info),
        new Line("Keep to the shadows. The dark is a raccoon's best friend.", LineType.Hint),
    };

    private static readonly Line NoMoreTips =
        new Line("That's everything I know, Bandit. You're on your own from here now - good luck!", LineType.Send);

    // ---------------------------------------------------------------------

    private void Awake()
    {
        BuildPlaceholderVisualIfNeeded();

        voice = GetComponent<AudioSource>();
        if (voice == null) voice = gameObject.AddComponent<AudioSource>();
        voice.playOnAwake = false;
        voice.spatialBlend = 0.6f;
        voice.minDistance = 2f;
        voice.maxDistance = 20f;

        head = FindChild(visualModel != null ? visualModel.transform : transform, "Head");
        if (head != null) headRest = head.localRotation;

        if (visualModel != null)
        {
            modelRestRotation = visualModel.transform.rotation;

            // modelForwardOffsetY is what calibrates Toby's facing (exactly what its own tooltip tells you to do if Toby's ever facing the wrong way)
            Vector3 localForward = Quaternion.Euler(0f, modelForwardOffsetY, 0f) * Vector3.forward;

            Vector3 f = modelRestRotation * localForward;
            f.y = 0f;
            if (f.sqrMagnitude > 0.0001f) modelRestForwardFlat = f.normalized;

            Vector3 headGuess = Vector3.zero;
            if (head != null)
            {
                Vector3 headLocal = visualModel.transform.InverseTransformDirection(head.position - visualModel.transform.position);
                headLocal.y = 0f;
                headGuess = headLocal;
            }

            Debug.Log($"[TobyNPC] Facing calibration - using modelForwardOffsetY ({modelForwardOffsetY} degrees): " +
                      $"local forward: {localForward}, world rest forward: {modelRestForwardFlat}. " +
                      $"(Head child found: {head != null}, its own guess would have been {headGuess}, for comparison " +
                      $"only.) If Toby's still facing the wrong way, nudge Model Forward Offset Y in the Inspector " +
                      $"by 90 at a time - that's what controls this.");
        }
    }

    private void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) player = playerObj.transform;

        if (promptObject != null) promptObject.SetActive(false);
        if (speechBubble != null) speechBubble.SetActive(false);
    }

    private void Update()
    {
        if (player == null) return;

        float dist = Vector3.Distance(transform.position, player.position);
        // Toby stays exactly as placed in the scene - no turning, no head nod.
        if (turnToFacePlayer) FacePlayer();
        if (nodWhileTalking) AnimateHead();

        // TEMPORARY heartbeat log.

        bool inRange = dist <= promptRange;
        if (inRange == playerInRange) return;

        playerInRange = inRange;

        if (!playerInRange)
        {
            // Walked off mid-conversation - hide everything; next visit
            // starts fresh (or with a short "back again" if he's heard it all).
            if (conversation != null) { StopCoroutine(conversation); conversation = null; }
            StopTyping();
            if (promptObject != null) promptObject.SetActive(false);
            if (speechBubble != null) speechBubble.SetActive(false);
            dialogueIndex = -1;
            extraTipOrder = null;
        }
        else if (speechBubble == null || !speechBubble.activeSelf)
        {
            if (promptObject != null) promptObject.SetActive(true);
        }
    }

    // Called by PlayerInteract when Bandit presses E near/at Toby.
    public void Interact()
    {
        if (promptObject != null) promptObject.SetActive(false);

        // Already talking? E finishes the current line, or skips to the next.
        if (conversation != null)
        {
            if (isTyping) FinishTyping();
            else advanceNow = true;
            return;
        }
        conversation = StartCoroutine(Converse());
    }

    private IEnumerator Converse()
    {
        var lines = new List<Line> { Greeting };
        var clips = new List<string> { "toby_greeting_1" };
        for (int k = 0; k < TipsPerTalk; k++)
        {
            int idx = k;   // same four tips, in order, every time
            lines.Add(Tips[idx]);
            clips.Add("toby_tip_" + (idx + 1));
        }
        lines.Add(GoodLuck);
        clips.Add("toby_goodluck_1");

        for (int i = 0; i < lines.Count; i++)
        {
            advanceNow = false;
            ShowLine(lines[i], LoadVoiceClip(clips[i]));
            while (isTyping && !advanceNow) yield return null;
            float wait = i == lines.Count - 1 ? 2.5f : 0.9f;   // short pause, then the next line on its own
            float t = 0f;
            while (t < wait && !advanceNow) { t += Time.deltaTime; yield return null; }
        }

        if (speechBubble != null) speechBubble.SetActive(false);
        if (promptObject != null && playerInRange) promptObject.SetActive(true);
        conversation = null;
    }

    private Line GetLine(int index, out AudioClip clip)
    {
        clip = null;

        if (!isReturnVisit)
        {
            if (index < BriefingLines.Length)
            {
                clip = LoadVoiceClip("toby_briefing_" + (index + 1));
                return BriefingLines[index];
            }
            heardBriefing = true;
            if (index == BriefingLines.Length)
            {
                clip = LoadVoiceClip("toby_sendoff_1");
                return SendOff;
            }
            // Kept pressing E after the send-off - hand out extra tips.
            return NextExtraTip(out clip);
        }

        if (index == 0)
        {
            int i = Random.Range(0, ReturnGreetings.Length);
            clip = LoadVoiceClip("toby_returngreeting_" + (i + 1));
            return ReturnGreetings[i];
        }
        return NextExtraTip(out clip);
    }

    // Toby's recorded lines.
    private static AudioClip LoadVoiceClip(string name) => Resources.Load<AudioClip>("Audio/Voices/" + name);

    private Line NextExtraTip(out AudioClip clip)
    {
        if (extraTipsShown >= MaxExtraTips)
        {
            clip = LoadVoiceClip("toby_nomoretips_1");
            return NoMoreTips;
        }
        extraTipsShown++;

        // Shuffle INDICES rather than the lines themselves, so whichever
        // tip comes up we still know its original position in ExtraTips
        // and can load the matching recorded clip by that same number.
        if (extraTipOrder == null || extraTipOrder.Count == 0)
        {
            extraTipOrder = new List<int>();
            for (int i = 0; i < ExtraTips.Length; i++) extraTipOrder.Add(i);
            Shuffle(extraTipOrder);
        }
        int idx = extraTipOrder[0];
        extraTipOrder.RemoveAt(0);
        clip = LoadVoiceClip("toby_extratip_" + (idx + 1));
        return ExtraTips[idx];
    }

    // ---------------------------------------------------------------------
    //  Talking: typewriter + voice + head nod
    // ---------------------------------------------------------------------
    private void ShowLine(Line line, AudioClip clip)
    {
        if (speechBubble != null) speechBubble.SetActive(true);
        if (speechText == null) return;

        speechText.text = line.text;
        speechText.color = ColorFor(line.type);

        StopTyping();
        typing = StartCoroutine(TypeOut(line.text, clip));
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

        // With a recorded clip, spread the typing over the clip's length.
        float cps = clip != null ? Mathf.Max(lettersPerSecond * 0.5f, total / Mathf.Max(0.5f, clip.length)) : lettersPerSecond;

        float shown = 0f;
        int lastBlip = -10;
        while (shown < total)
        {
            shown += cps * Time.deltaTime;
            int visible = Mathf.Min(total, Mathf.FloorToInt(shown));
            speechText.maxVisibleCharacters = visible;
            talkAmount = 1f;

            // Babble: a blip every couple of letters (skipping spaces).
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
            if (visible > 0 && visible <= text.Length)
            {
                char c = text[visible - 1];
                if (c == ',' || c == '.' || c == '!' || c == '?' || c == '-')
                {
                    talkAmount = 0.3f;
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
        talkAmount = 0f;
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
        AudioClip clip = AudioClip.Create("TobyBabble", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private void AnimateHead()
    {
        if (head == null) return;
        if (!isTyping) talkAmount = Mathf.MoveTowards(talkAmount, 0f, Time.deltaTime * 3f);
        float nod = Mathf.Sin(Time.time * 13f) * nodAmount * talkAmount;
        float tilt = Mathf.Sin(Time.time * 3.1f) * nodAmount * 0.4f * talkAmount;
        head.localRotation = headRest * Quaternion.Euler(nod, 0f, tilt);
    }

    // Turns Toby's MODEL.
    private void FacePlayer()
    {
        if (visualModel == null || player == null) return;

        // No range check at all, by design: Toby always turns to face wherever Bandit actually is.
        Vector3 to = player.position - visualModel.transform.position;
        to.y = 0f;
        float dist = to.magnitude;
        if (dist < 0.01f) return;
        to /= dist;

        Quaternion delta = Quaternion.FromToRotation(modelRestForwardFlat, to);
        Quaternion want = delta * modelRestRotation;

        // Close-range boost: at dist=0 he turns closeRangeTurnSpeedMultiplier times faster than normal, fading linearly back down to the plain turnSpeed by.
        float closeness = 1f - Mathf.Clamp01(dist / Mathf.Max(closeRangeTurnBoostDistance, 0.0001f));
        float effectiveTurnSpeed = turnSpeed * Mathf.Lerp(1f, closeRangeTurnSpeedMultiplier, closeness);

        visualModel.transform.rotation = Quaternion.RotateTowards(visualModel.transform.rotation, want, effectiveTurnSpeed * Time.deltaTime);
    }

    // ---------------------------------------------------------------------

    private Color ColorFor(LineType type)
    {
        switch (type)
        {
            case LineType.Hint: return hintColor;
            case LineType.Warning: return warningColor;
            case LineType.Info: return infoColor;
            default: return greetingColor;
        }
    }

    private static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static Transform FindChild(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    // Priority: an already-placed scene instance (visualModel) > a prefab
    // asset to instantiate (modelPrefab) > a simple generated placeholder.
    private void BuildPlaceholderVisualIfNeeded()
    {
        if (visualModel != null) return;

        if (modelPrefab != null)
        {
            visualModel = Instantiate(modelPrefab, transform);
            visualModel.name = modelPrefab.name;
            visualModel.transform.localPosition = Vector3.zero;
            visualModel.transform.localRotation = Quaternion.identity;
            return;
        }

        GameObject shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shell.name = "TobyShell (placeholder)";
        shell.transform.SetParent(transform, false);
        shell.transform.localPosition = new Vector3(0f, 0.32f, 0f);
        shell.transform.localScale = new Vector3(1.0f, 0.6f, 1.3f);
        SafeDestroyCollider(shell);
        ApplyColor(shell, new Color(0.16f, 0.5f, 0.2f));

        GameObject headObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        headObj.name = "Head";
        headObj.transform.SetParent(transform, false);
        headObj.transform.localPosition = new Vector3(0f, 0.38f, 0.72f);
        headObj.transform.localScale = new Vector3(0.38f, 0.38f, 0.42f);
        SafeDestroyCollider(headObj);
        ApplyColor(headObj, new Color(0.4f, 0.68f, 0.32f));

        for (int i = 0; i < 4; i++)
        {
            GameObject leg = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            leg.name = "TobyLeg (placeholder)";
            float x = (i % 2 == 0) ? -0.45f : 0.45f;
            float z = (i < 2) ? 0.5f : -0.5f;
            leg.transform.SetParent(transform, false);
            leg.transform.localPosition = new Vector3(x, 0.12f, z);
            leg.transform.localScale = new Vector3(0.22f, 0.22f, 0.22f);
            SafeDestroyCollider(leg);
            ApplyColor(leg, new Color(0.4f, 0.68f, 0.32f));
        }

        visualModel = shell;
    }

    private static void SafeDestroyCollider(GameObject go)
    {
        Collider c = go.GetComponent<Collider>();
        if (c != null) Destroy(c);
    }

    private static void ApplyColor(GameObject go, Color color)
    {
        Renderer r = go.GetComponent<Renderer>();
        if (r == null) return;

        Shader shader = Shader.Find("Standard");
        Material mat = new Material(shader != null ? shader : r.sharedMaterial.shader);
        mat.color = color;
        r.sharedMaterial = mat;
    }
}
