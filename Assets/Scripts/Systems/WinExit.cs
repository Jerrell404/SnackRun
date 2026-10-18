using UnityEngine;

// The final fence opening in the Backyard. Reaching it while all required food has been collected.
public class WinExit : MonoBehaviour
{
    [SerializeField] private WinScreen winScreen;
    [SerializeField] private bool requiresAllFood = true;

    private bool triggered = false;

    private void OnTriggerEnter(Collider other)
    {
        if (triggered) return;
        if (!other.CompareTag("Player")) return;

        if (requiresAllFood && GameManager.Instance != null && !GameManager.Instance.HasAllFood())
        {
            int remaining = GameManager.Instance.RemainingFoodCount();
            Debug.Log("Reached the fence without all the food! Remaining: " + remaining);
            return;
        }

        triggered = true;

        Debug.Log("Bandit made it to the fence - YOU WIN!");

        if (GameManager.Instance != null)
        {
            GameManager.Instance.WinGame();
        }

        if (winScreen != null)
        {
            winScreen.Show();
        }
    }
}
