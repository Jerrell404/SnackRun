using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Drop this into a scene where a non-fatal life loss (Bandit still has lives left) should send him back to the START of the CURRENT level rather than.
public class RestartSceneOnLifeLoss : MonoBehaviour
{
    [Tooltip("Shown right before the scene reloads, so the player understands why. Whatever hazard caused the life loss can override this via GameManager.pendingLifeLostMessage.")]
    [SerializeField] private string defaultMessage = "You lost a life! Restarting this level...";
    [Tooltip("How long everything freezes (Time.timeScale = 0) before the scene reloads.")]
    [SerializeField] private float freezeDuration = 1f;
    [Tooltip("How long the loading bar takes to fill 0%->100% for THIS reload (restarting after a lost life)")]
    [SerializeField] private float loadingScreenDuration = 3f;

    private void OnEnable()
    {
        GameManager.OnLifeLost += HandleLifeLost;
    }

    private void OnDisable()
    {
        GameManager.OnLifeLost -= HandleLifeLost;

        // Safety net: if this object gets disabled (e.g. the scene reloads)
        // while time is still frozen, make sure the next scene doesn't
        // inherit a stuck timeScale of 0.
        Time.timeScale = 1f;
    }

    private void HandleLifeLost()
    {
        string message = defaultMessage;
        float duration = freezeDuration;
        bool suppressMessage = false;

        if (GameManager.Instance != null)
        {
            // Some hazards (e.g. AnimalTrap) already showed their OWN
            // immediate message the instant they went off, so this later
            // "you lost a life, restarting..." popup would just be a
            // redundant second message right before the loading screen.
            suppressMessage = GameManager.Instance.suppressLifeLostMessage;
            GameManager.Instance.suppressLifeLostMessage = false;

            // Some hazards need their cutscene to run longer than the usual freeze (e.g. Edgar's catch in the House)
            duration += GameManager.Instance.pendingFreezeDurationBonus;
            GameManager.Instance.pendingFreezeDurationBonus = 0f;
            // Never hang on the frozen "caught" moment for long - just long
            // enough to read the message, then straight into the restart.
            duration = Mathf.Min(duration, 1.5f);

            // Fresh life = fresh health, always. Whatever half/full state
            // Bandit was in when the hit that cost this life landed, he
            // respawns at full - only currentLives remembers the cost.
            GameManager.Instance.playerAtHalfHealth = false;

            // This is a checkpoint restart of the CURRENT level, back to its very start.
            GameManager.Instance.collectedFood.Clear();

            if (!string.IsNullOrEmpty(GameManager.Instance.pendingLifeLostMessage))
            {
                message = GameManager.Instance.pendingLifeLostMessage;
                GameManager.Instance.pendingLifeLostMessage = null;
            }
        }

        Debug.Log("Life lost - freezing everything and restarting this scene from its checkpoint. " + message);

        if (!suppressMessage)
        {
            UIManager ui = FindObjectOfType<UIManager>();
            if (ui != null)
            {
                ui.ShowLifeLostRestartMessage(message);
            }
        }

        SoundFX.Play("life_lost", 1f, 0f);
        StartCoroutine(FreezeThenReload(duration));
    }

    // Freezes the ENTIRE game world.
    private IEnumerator FreezeThenReload(float duration)
    {
        Time.timeScale = 0f;
        yield return new WaitForSecondsRealtime(duration);
        // Fade to black and reload (the world stays frozen during the fade;
        // the fader unfreezes it once the fresh scene has loaded).
        SceneFader.ReloadCurrentScene(loadingScreenDuration);
    }
}
