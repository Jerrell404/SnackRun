using System.Collections;
using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    // True once Bandit has taken one hit this life.
    private bool recentlyHit = false;

    // Health bar reads this: full while safe, half after the first hit this
    // life, back to full the instant a life is lost (fresh life = fresh health).
    public float HealthFraction => recentlyHit ? 0.5f : 1f;

    [Header("Hit feedback (Task 14 - Player Health, all scenes)")]
    [SerializeField] private AudioClip hitSound;
    [SerializeField] private float hitFlashDuration = 0.2f;
    [SerializeField] private Color hitFlashColor = Color.red;

    private AudioSource audioSource;
    private Renderer[] bodyRenderers;
    private Color[] originalColors;
    private Coroutine flashRoutine;
    private AudioClip placeholderHitClip;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        bodyRenderers = GetComponentsInChildren<Renderer>();
        originalColors = new Color[bodyRenderers.Length];
        for (int i = 0; i < bodyRenderers.Length; i++)
        {
            originalColors[i] = bodyRenderers[i].material.color;
        }

        // Carry health across scene loads.
        if (GameManager.Instance != null)
        {
            recentlyHit = GameManager.Instance.playerAtHalfHealth;
        }
    }

    public void TakeHit()
    {
        PlayHitFeedback();
        DangerFeedback.Hit();

        if (recentlyHit)
        {
            recentlyHit = false; // fresh life, fresh health
            SyncHealthToGameManager();
            GameManager.Instance.LoseLife();
            Debug.Log("Second hit taken - life lost!");
        }
        else
        {
            recentlyHit = true;
            SyncHealthToGameManager();
            Debug.Log("Hit taken - one more before you lose a life.");
        }
    }

    // For a future healing source (e.g. Hammy) to restore health without costing a life.
    public void Heal()
    {
        recentlyHit = false;
        SyncHealthToGameManager();
    }

    public bool IsAtHalfHealth() => recentlyHit;

    private void SyncHealthToGameManager()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.playerAtHalfHealth = recentlyHit;
        }
    }

    private void PlayHitFeedback()
    {
        audioSource.PlayOneShot(hitSound != null ? hitSound : GetPlaceholderHitSound(), SoundFX.SfxVolumeScale);

        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashRed());
    }

    private IEnumerator FlashRed()
    {
        for (int i = 0; i < bodyRenderers.Length; i++)
            if (bodyRenderers[i] != null) bodyRenderers[i].material.color = hitFlashColor;

        yield return new WaitForSeconds(hitFlashDuration);

        for (int i = 0; i < bodyRenderers.Length; i++)
            if (bodyRenderers[i] != null) bodyRenderers[i].material.color = originalColors[i];

        flashRoutine = null;
    }

    // Roblox-style "oof" placeholder, generated on the fly so Task 14 works
    // out of the box even before a real sound effect is assigned.
    private AudioClip GetPlaceholderHitSound()
    {
        if (placeholderHitClip != null) return placeholderHitClip;

        int sampleRate = 44100;
        float duration = 0.15f;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float freq = 220f;
        float[] data = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float t = (float)i / sampleRate;
            float envelope = 1f - (t / duration);
            data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * envelope * 0.5f;
        }

        placeholderHitClip = AudioClip.Create("HitPlaceholder", sampleCount, 1, sampleRate, false);
        placeholderHitClip.SetData(data, 0);
        return placeholderHitClip;
    }
}
