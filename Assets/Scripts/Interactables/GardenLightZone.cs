using UnityEngine;

// A garden light doesn't just get ONE free detection and then go dead for the rest of the attempt.
public class GardenLightZone : MonoBehaviour
{
    [SerializeField] private float detectionTime = 5f;
    private float timeInLight = 0f;
    private bool playerInside = false;

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInside = true;
            timeInLight = 0f;
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInside = false;
            timeInLight = 0f;
        }
    }

    void Update()
    {
        // Bandit is mid-catch in some OTHER hazard (e.g. an animal trap's
        // fall/pin animation) that hasn't frozen Time.timeScale yet - don't
        // let this light sneak in an unrelated second life loss purely
        // because real time kept passing while that's playing out.
        if (GameManager.Instance != null && GameManager.Instance.hazardsSuspended) return;

        if (playerInside)
        {
            timeInLight += Time.deltaTime;
            if (timeInLight >= detectionTime)
            {
                HumanDetection.Instance.OnPlayerDetected();

                // Don't clear playerInside.
                timeInLight = 0f;
            }
        }
    }
}