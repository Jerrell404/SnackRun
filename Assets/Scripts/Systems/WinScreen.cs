using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Sits on the Backyard's Canvas (always active), waiting for WinExit to call Show() the moment Bandit reaches the final fence opening.
public class WinScreen : MonoBehaviour
{
    public GameObject panel;
    public CanvasGroup panelCanvasGroup;
    public RectTransform panelContent;
    public TextMeshProUGUI messageText;

    [TextArea]
    public string winMessage = "YOU DID IT!\n\nBandit made it out with a bag full of snacks. Play again?";

    [Header("Fancy reveal")]
    [SerializeField] private float fadeInDuration = 0.8f;
    [SerializeField] private float scaleInStart = 0.7f;

    public void Show()
    {
        if (panel != null)
        {
            panel.SetActive(true);
        }

        if (messageText != null)
        {
            messageText.text = winMessage;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (EventSystem.current == null)
        {
            GameObject es = new GameObject("EventSystem (auto)");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        Time.timeScale = 0f;
        SoundFX.Play("victory", 1f, 0f);
        StartCoroutine(FadeAndScaleIn());
    }

    // Purely cosmetic - a soft fade + gentle scale-up so the win screen
    // feels like a deliberate celebration rather than an instant snap-in
    // panel like the "you lose" screens. Uses unscaled time since
    // Time.timeScale is 0 while this plays.
    private IEnumerator FadeAndScaleIn()
    {
        if (panelCanvasGroup == null && panelContent == null) yield break;

        float t = 0f;
        while (t < fadeInDuration)
        {
            t += Time.unscaledDeltaTime;
            float frac = Mathf.Clamp01(t / fadeInDuration);
            float eased = 1f - (1f - frac) * (1f - frac); // ease-out

            if (panelCanvasGroup != null) panelCanvasGroup.alpha = eased;
            if (panelContent != null) panelContent.localScale = Vector3.one * Mathf.Lerp(scaleInStart, 1f, eased);

            yield return null;
        }

        if (panelCanvasGroup != null) panelCanvasGroup.alpha = 1f;
        if (panelContent != null) panelContent.localScale = Vector3.one;
    }

    // Wired to the Yes button's OnClick.
    public void OnPlayAgainYes()
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
    public void OnPlayAgainNo()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
