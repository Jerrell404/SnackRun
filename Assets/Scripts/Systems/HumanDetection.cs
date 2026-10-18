using UnityEngine;
using UnityEngine.Events;

public class HumanDetection : MonoBehaviour
{
    public static HumanDetection Instance;

    [Header("Escalation (persists for the whole run)")]
    [Tooltip("How many times the player has been fully caught. Grows over the run and never resets.")]
    public int detectionCount = 0;

    [Header("Events (optional - hook up UI/audio if useful)")]
    public UnityEvent OnWarning;   // fires on the first strike
    public UnityEvent OnCaught;    // fires on the second strike, right before losing a life

    private bool warned = false;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // This is the method GardenLightZone (and any future trigger) calls directly.
    public void OnPlayerDetected()
    {
        // Tucked into a hiding spot - the human simply can't see Bandit.
        if (HidingSpot.PlayerIsHidden) return;

        // Bandit is mid-catch in some OTHER hazard (e.g. an animal trap's
        // fall/pin animation) that hasn't frozen Time.timeScale yet - don't
        // let detection sneak in an unrelated second life loss purely
        // because real time kept passing while that's playing out.
        if (GameManager.Instance != null && GameManager.Instance.hazardsSuspended) return;

        if (!warned)
        {
            warned = true;
            Debug.Log("Human spotted Bandit! WARNING!");
            OnWarning?.Invoke();

            // First strike: same black-background banner as the second
            // strike's "caught by Edgar" popup, but in warning yellow.
            SoundFX.Play("warning", 0.45f, 0f);
            ScreenMessage.Show("WARNING! Stay out of the lights - Edgar almost spotted you!", 3f, new Color(1f, 0.85f, 0.1f));
        }
        else
        {
            Debug.Log("Human caught Bandit!");
            warned = false;
            detectionCount++;
            OnCaught?.Invoke();

            // Same black-background, red-text popup AnimalTrap uses for its
            // own "Bandit is caught in a trap!" message - not the plain
            // white "life lost" restart screen. Names Edgar the same way
            // the House does when he catches Bandit there.
            ScreenMessage.Show("Bandit has been caught by Edgar!", 3f, new Color(1f, 0.3f, 0.25f));

            if (GameManager.Instance != null)
            {
                // This popup already told the player what happened, so
                // RestartSceneOnLifeLoss's own later "you lost a life..."
                // message would just be a redundant second popup right
                // before the loading screen - suppress that one, same as
                // AnimalTrap does for its own catch message.
                GameManager.Instance.suppressLifeLostMessage = true;
            }

            // Let Edgar dramatically reveal himself (if this scene has that
            // set up) before the caught message and life loss land - the
            // message/restart only happen once the reveal finishes.
            EdgarFrontYardCutscene cutscene = FindObjectOfType<EdgarFrontYardCutscene>();
            if (cutscene != null)
            {
                cutscene.PlayCutscene(() =>
                {
                    if (GameManager.Instance != null) GameManager.Instance.LoseLife();
                });
            }
            else if (GameManager.Instance != null)
            {
                GameManager.Instance.LoseLife();
            }
        }
    }
}