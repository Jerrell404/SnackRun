using UnityEngine;
using UnityEngine.Events;

public class KitchenTimer : MonoBehaviour
{
    public float timeRemaining = 300f;
    public bool timerRunning = true;

    // Hook up TV flicker, or anything else that should react purely to the clock hitting zero, in the Inspector.
    public UnityEvent onTimerEnd;

    private void Update()
    {
        if (!timerRunning)
            return;

        float before = timeRemaining;
        timeRemaining -= Time.deltaTime;

        // Heads-up as the clock runs down (it keeps going while hiding).
        foreach (int mark in new[] { 60, 30, 10 })
        {
            if (before > mark && timeRemaining <= mark)
                SoundFX.Play("timer_warning", 0.8f, 0f);
            if (before > mark && timeRemaining <= mark)
                ScreenMessage.Show(mark + " seconds left on the kitchen clock! Grab the food and get out!", 4f,
                                   mark <= 10 ? new Color(1f, 0.35f, 0.25f) : new Color(1f, 0.85f, 0.3f));
        }

        // Tick-tock for the last 30 seconds (louder in the last 10).
        if (timeRemaining > 0f && timeRemaining <= 30f && Mathf.CeilToInt(before) != Mathf.CeilToInt(timeRemaining))
            SoundFX.Play("timer_tick", timeRemaining <= 10f ? 0.8f : 0.35f, 0f);

        if (timeRemaining <= 0)
        {
            timeRemaining = 0;
            timerRunning = false;

            Debug.Log("TIME UP!");

            bool wasLastLife = GameManager.Instance == null || GameManager.Instance.currentLives <= 1;

            // If this isn't the last life, RestartSceneOnLifeLoss will do a
            // quiet checkpoint reload on its own - give it a specific message
            // to show instead of its generic fallback, and definitely no
            // "restart? yes/no" prompt (that's reserved for a real game over).
            if (!wasLastLife && GameManager.Instance != null)
            {
                GameManager.Instance.pendingLifeLostMessage =
                    "Time's up! Edgar found you in the kitchen. You lost a life - restarting this level...";
            }

            // LoseLife() decides what happens next on its own: lives left -> OnLifeLost -> a RestartSceneOnLifeLoss checkpoint reload of this scene; no lives left.
            GameManager.Instance.LoseLife();

            if (wasLastLife)
            {
                onTimerEnd.Invoke();
            }
        }
    }

    // Called by ChipPacket (second crunch) to instantly shorten the timer to however long it will take Edgar to reach the player.
    public void CollapseToTime(float newTime)
    {
        if (!timerRunning) return;
        timeRemaining = Mathf.Min(timeRemaining, Mathf.Max(newTime, 0f));
    }

    // Called the instant Edgar actually catches Bandit.
    public void StopTimer()
    {
        timerRunning = false;
    }
}
