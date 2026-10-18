using UnityEngine;

// Hooked up as a listener on KitchenTimer's onTimerEnd UnityEvent. Delegates straight to the shared GameOverScreen "you lose" panel.
public class TimeUpScreen : MonoBehaviour
{
    public GameOverScreen gameOverScreen;

    public void OnTimeUp()
    {
        if (gameOverScreen != null)
        {
            gameOverScreen.HandleTimeUp();
        }
    }
}
