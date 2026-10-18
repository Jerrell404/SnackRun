using System;
using System.Collections;
using UnityEngine;

// Edgar stays completely hidden in the Front Yard until Bandit gets caught by a garden light for the second time.
public class EdgarFrontYardCutscene : MonoBehaviour
{
    [Tooltip("Name of Edgar's model GameObject already placed in this scene. Looked up by name at Start - his transform is never touched, only his renderers.")]
    [SerializeField] private string edgarObjectName = "EdgarModel";

    [Header("Slow-motion reveal")]
    [Tooltip("How slow time runs while Edgar reveals himself (1 = normal speed, lower = slower/more dramatic). Uses real seconds underneath so the reveal itself always takes revealDuration regardless of this value.")]
    [SerializeField] private float slowMotionScale = 0.25f;
    [Tooltip("How long, in real seconds, the slow-motion reveal lasts before control is handed back for the caught message and life loss.")]
    [SerializeField] private float revealDuration = 4f;

    [Header("Audio - plays the instant Edgar becomes visible")]
    [SerializeField] private AudioClip revealSound;
    [SerializeField] private AudioSource audioSource;

    private GameObject edgar;
    private Renderer[] edgarRenderers;
    private bool played = false;

    private void Start()
    {
        edgar = GameObject.Find(edgarObjectName);

        if (edgar != null)
        {
            edgarRenderers = edgar.GetComponentsInChildren<Renderer>(true);
            SetEdgarVisible(false); // hidden by default every time this scene (re)loads
        }
        else
        {
            Debug.LogWarning("EdgarFrontYardCutscene: couldn't find a GameObject named '" + edgarObjectName + "' in the scene - Edgar won't reveal himself.");
        }

        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
    }

    private void SetEdgarVisible(bool visible)
    {
        if (edgarRenderers == null) return;

        foreach (Renderer r in edgarRenderers)
        {
            if (r != null) r.enabled = visible;
        }
    }

    // Fires the instant Edgar's renderers switch on. PlayOneShot's own playback isn't scaled by Time.timeScale.
    private void PlayRevealSound()
    {
        if (audioSource == null || revealSound == null) return;
        audioSource.PlayOneShot(revealSound, SoundFX.SfxVolumeScale);
    }

    // Called by HumanDetection the instant Bandit is caught for the second time.
    public void PlayCutscene(Action onComplete)
    {
        if (played)
        {
            // Edgar's already been revealed once this attempt - no need to
            // repeat the slow-mo beat, just go straight to the message.
            onComplete?.Invoke();
            return;
        }

        StartCoroutine(CutsceneRoutine(onComplete));
    }

    private IEnumerator CutsceneRoutine(Action onComplete)
    {
        played = true;

        PlayerController player = FindObjectOfType<PlayerController>();
        if (player != null) player.enabled = false;

        Time.timeScale = slowMotionScale;
        SetEdgarVisible(true);
        PlayRevealSound();

        yield return new WaitForSecondsRealtime(revealDuration);

        Time.timeScale = 1f;
        if (player != null) player.enabled = true;

        onComplete?.Invoke();
    }

    private void OnDestroy()
    {
        // Safety net in case this gets destroyed mid-cutscene for any
        // reason - never leave the game permanently slowed down.
        if (played) Time.timeScale = 1f;
    }
}
