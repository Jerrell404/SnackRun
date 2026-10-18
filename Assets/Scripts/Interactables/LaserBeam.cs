using UnityEngine;

// A backyard laser beam: same 2-hit-consecutive damage pattern as the AnimalTrap/Cat scratch (PlayerHealth.TakeHit.
public class LaserBeam : MonoBehaviour
{
    [SerializeField] private float damageCooldown = 2.5f; // prevents multiple hits per frame while standing in it
    // Static: shared by every laser in the scene, so touching ANY laser starts the same shared cooldown that ALL lasers respect.
    private static float lastHitTime = -999f;

    [Tooltip("Every touch of a laser slows Bandit down (slower movement, lower jump) for this many seconds - even when the damage itself is still on cooldown. The strength of the slow is set on PlayerController.")]
    [SerializeField] private float slowDuration = 2f;

    private void OnTriggerEnter(Collider other)
    {
        // Ignore anything that isn't Bandit BEFORE touching the cooldown timer.
        if (!other.CompareTag("Player"))
        {
            return;
        }

        Debug.Log("LaserBeam trigger overlap with '" + other.name + "' (tag: " + other.tag + ")");

        // Slowness applies on EVERY touch, independent of the damage cooldown
        // below - brushing a beam always leaves Bandit sluggish for a moment.
        PlayerController movement = other.GetComponent<PlayerController>();
        if (movement == null) movement = other.GetComponentInParent<PlayerController>();
        if (movement != null)
        {
            movement.ApplySlow(slowDuration);
            Debug.Log("Laser slowed Bandit for " + slowDuration + "s.");
        }

        if (Time.time - lastHitTime <= damageCooldown)
        {
            Debug.Log("LaserBeam hit Bandit but is still on cooldown - ignored.");
            return;
        }

        // The collider that actually entered the trigger isn't guaranteed to
        // be the exact GameObject PlayerHealth lives on (e.g. if the
        // CharacterController capsule sits on a child) - check the object
        // itself first, then its parents, rather than failing silently.
        PlayerHealth health = other.GetComponent<PlayerHealth>();
        if (health == null) health = other.GetComponentInParent<PlayerHealth>();

        if (health == null)
        {
            Debug.LogWarning("LaserBeam hit something tagged Player but couldn't find a PlayerHealth component on it or its parents.");
            return;
        }

        lastHitTime = Time.time;

        bool willCostLife = health.IsAtHalfHealth(); // this hit is the SECOND hit -> a life is about to go
        bool isLastLife = GameManager.Instance != null && GameManager.Instance.currentLives <= 1;

        if (willCostLife && GameManager.Instance != null)
        {
            if (isLastLife)
            {
                GameManager.Instance.pendingGameOverMessage =
                    "Bandit fried by laser!\n\nGAME OVER! YOU LOSE.\n\nWanna try again?";
            }
            else
            {
                GameManager.Instance.pendingLifeLostMessage =
                    "Bandit fried by laser! You lost a life.\nTry again and watch out for the beams.";
            }
        }

        Debug.Log("ZAP! Bandit hit a laser beam.");
        SoundFX.Play("laser_zap", 1f, 0.08f);   // electric zap, on top of the usual hurt sound
        health.TakeHit();
    }
}
