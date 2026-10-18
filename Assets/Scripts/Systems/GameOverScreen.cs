using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

// Sits on the Canvas (always active) so it's guaranteed to be listening for GameManager.OnGameOver as soon as the scene starts.
public class GameOverScreen : MonoBehaviour
{
    public GameObject panel;
    public TextMeshProUGUI messageText;

    [TextArea]
    public string caughtByCatMessage = "YOU LOSE.\n\nBandit has died! Try again?";
    [TextArea]
    public string timeUpMessage = "YOU LOSE.\n\nBandit was caught! Try again?";

    [Header("Hidden along with the panel (e.g. the food checklist)")]
    public GameObject[] hideWhilePanelShown;

    private void OnEnable()
    {
        GameManager.OnGameOver += HandleGameOver;
    }

    private void OnDisable()
    {
        GameManager.OnGameOver -= HandleGameOver;
    }

    private void HandleGameOver()
    {
        // A hazard (e.g. LaserBeam on the last hit, or Rex catching Bandit)
        // can set its own specific message right before triggering the
        // game over - use that if present, otherwise fall back to the
        // generic message below.
        string message = caughtByCatMessage;
        if (GameManager.Instance != null && !string.IsNullOrEmpty(GameManager.Instance.pendingGameOverMessage))
        {
            message = GameManager.Instance.pendingGameOverMessage;
            GameManager.Instance.pendingGameOverMessage = null;
        }

        Show(message);
    }

    // Hooked up (via TimeUpScreen) to KitchenTimer's onTimerEnd.
    public void HandleTimeUp()
    {
        Show(timeUpMessage);
    }

    private void Show(string message)
    {
        if (messageText != null)
        {
            messageText.text = message;
        }

        if (panel != null)
        {
            panel.SetActive(true);
        }

        // The food checklist doesn't mean anything once the run is over.
        if (hideWhilePanelShown != null)
        {
            foreach (GameObject go in hideWhilePanelShown)
            {
                if (go != null) go.SetActive(false);
            }
        }

        // Some third-person camera/character rigs lock or hide the OS cursor
        // during normal gameplay - make sure it's free and visible so the
        // player can actually click the Yes/No buttons.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Safety net: buttons are dead without an EventSystem in the scene.
        // If one somehow isn't present, create one on the fly rather than
        // leaving the player stuck on an unclickable Game Over screen.
        if (EventSystem.current == null)
        {
            GameObject es = new GameObject("EventSystem (auto)");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        Time.timeScale = 0f;
        SoundFX.Play("game_over", 1f, 0f);
    }

    // Wired to the Yes button's OnClick.
    public void OnTryAgainYes()
    {
        Time.timeScale = 1f;

        if (panel != null)
        {
            panel.SetActive(false);
        }

        if (GameManager.Instance != null)
        {
            GameManager.Instance.RestartGame();
        }
    }

    // Wired to the No button's OnClick.
    public void OnTryAgainNo()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
