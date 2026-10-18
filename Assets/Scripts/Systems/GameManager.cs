using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    // Raised when lives hit zero. A GameOverScreen (if one exists in the
    // current scene) subscribes to this to show the "Try again?" cutscene.
    public static event Action OnGameOver;

    // Raised whenever a life is lost but there's still at least one left.
    public static event Action OnLifeLost;

    [Header("Lives")]
    public int startingLives = 3;
    public int currentLives;

    [Header("Health (carried across scene transitions)")]
    // The player's CURRENT half/full health status - kept in sync live by
    // PlayerHealth as hits land, and read back by PlayerHealth.Awake() in
    // whatever scene loads next so health doesn't just reset to full every
    // time Bandit walks through a door.
    public bool playerAtHalfHealth = false;

    // Optional flavour text set by whatever hazard caused a checkpoint restart.
    [HideInInspector] public string pendingLifeLostMessage;

    // Optional flavour text set by whatever hazard causes a FULL game over (last life lost) right before it calls LoseLife()/GameOver()
    [HideInInspector] public string pendingGameOverMessage;

    // Optional extra seconds added on top of a RestartSceneOnLifeLoss checkpoint reload's own freezeDuration.
    [HideInInspector] public float pendingFreezeDurationBonus = 0f;

    // Set by a hazard right before calling LoseLife() when its OWN message.
    [HideInInspector] public bool suppressLifeLostMessage = false;

    // Set true the instant a hazard begins a "Bandit is caught" sequence that plays out at normal Time.timeScale BEFORE LoseLife() actually gets called.
    [HideInInspector] public bool hazardsSuspended = false;

    // Hammy (Backyard) will restore health/a life exactly once per GAME, not
    // once per visit to the Backyard - true for the rest of this playthrough
    // once used, reset only by a full RestartGame().
    public bool hammyHealUsed = false;

    // Rex (Backyard): how many times he's failed to catch Bandit so far this PLAYTHROUGH, not just the current scene load.
    public int rexFailedChaseCount = 0;

    // Rex (Backyard): the highest one-time-permanent bypass speed multiplier Bandit has triggered so far this PLAYTHROUGH, by tripping one of Rex's bypass.
    public float rexBypassSpeedMultiplier = 1f;

    [Header("Food")]
    public List<string> requiredFood = new List<string>();
    public List<string> collectedFood = new List<string>();

    [Header("Random shopping list")]
    [Tooltip("How many of the House's food items are picked at random as the shopping list each new game.")]
    public int foodToCollect = 5;
    // True once this game's random shopping list has been picked. It stays
    // the same through House retries and only re-rolls on a brand-new game.
    [HideInInspector] public bool shoppingListRolled = false;

    [Header("Backyard")]
    [Tooltip("Tick = Bandit already has all the House food (Yes). Untick = No. Defaults to No. " +
             "It flips to Yes by itself when Bandit walks out of the House with everything, " +
             "and the Ctrl+3 hotkey sets it to Yes because starting in the Backyard assumes the food is already collected.")]
    [FormerlySerializedAs("allRequiredFoodSecured")]
    public bool collectedAllFoodFromHouse = false;

    [Tooltip("Shopping list used when the Backyard is started directly with the Ctrl+3 hotkey " +
             "(the House is skipped, so no random list has been rolled). Names match the House food items.")]
    public List<string> backyardHotkeyFood = new List<string> { "Donut", "Cookie", "Toast", "Apple", "Milk" };

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            // This GameManager isn't going to survive.
            if (!Instance.shoppingListRolled && requiredFood != null && requiredFood.Count > 0)
            {
                Instance.requiredFood = requiredFood;
            }

            Destroy(gameObject);
        }
    }

    private void Start()
    {
        currentLives = startingLives;
    }

    public void CollectFood(string foodName)
    {
        if (collectedFood.Contains(foodName))
            return;

        collectedFood.Add(foodName);

        Debug.Log(
            "Collected: " + foodName +
            " (" + collectedFood.Count + "/" + requiredFood.Count + ")"
        );
    }

    public bool HasAllFood()
    {
        // Locked in permanently once Bandit has actually walked out of the
        // House with everything - see collectedAllFoodFromHouse above.
        if (collectedAllFoodFromHouse) return true;

        if (requiredFood.Count == 0) return false;
        foreach (string food in requiredFood)
            if (!collectedFood.Contains(food)) return false;
        return true;
    }

    // Called once by LevelExit when Bandit actually walks out of the House through its food-gated door with everything already collected.
    public void LockInAllFoodCollected()
    {
        collectedAllFoodFromHouse = true;
    }

    // How many more required food items still need to be collected.
    public int RemainingFoodCount()
    {
        if (collectedAllFoodFromHouse) return 0;

        int remaining = 0;
        foreach (string food in requiredFood)
            if (!collectedFood.Contains(food)) remaining++;
        return remaining;
    }

    public void LoseLife()
    {
        currentLives--;

        Debug.Log("Life lost! Remaining lives: " + currentLives);

        if (currentLives <= 0)
        {
            GameOver();
        }
        else
        {
            OnLifeLost?.Invoke();
        }
    }

    public void GameOver()
    {
        Debug.Log("GAME OVER");

        if (OnGameOver != null)
        {
            // A Game Over screen is listening (e.g. the one in the House scene) -
            // let it show the "Try again?" cutscene and call RestartGame() itself.
            OnGameOver.Invoke();
        }
        else
        {
            // No Game Over UI in this scene to hand off to - fall back to the
            // old behaviour so the game doesn't just silently freeze.
            SceneFader.ReloadCurrentScene();
        }
    }

    // For hazards where getting caught is an instant, unconditional full
    // loss regardless of remaining lives or health (Rex catching Bandit) -
    // straight to GameOver() rather than ticking currentLives down one at a
    // time through the usual LoseLife() checkpoint path.
    public void LoseAllLivesInstantly()
    {
        currentLives = 0;
        Debug.Log("All lives lost instantly!");
        GameOver();
    }

    // Wipes everything that belongs to one playthrough (lives, health, food,
    // Rex/Hammy progress, pending hazard messages) back to a brand-new game.
    // Does NOT load a scene - callers do that.
    public void ResetRunState()
    {
        currentLives = startingLives;
        collectedFood.Clear();
        playerAtHalfHealth = false;
        pendingLifeLostMessage = null;
        pendingGameOverMessage = null;
        pendingFreezeDurationBonus = 0f;
        suppressLifeLostMessage = false;
        hazardsSuspended = false;
        hammyHealUsed = false;
        rexFailedChaseCount = 0;
        rexBypassSpeedMultiplier = 1f;
        collectedAllFoodFromHouse = false;
        shoppingListRolled = false; // brand-new game = brand-new random shopping list
    }

    // Called by the Game Over screen's "Yes" button - full reset back to the
    // start of the game.
    public void RestartGame()
    {
        ResetRunState();
        SceneFader.LoadScene("FrontYard");
    }

    // Level hotkeys (see LevelHotkeys): a completely fresh game that starts
    // in the given level - 3 lives, full health, nothing collected.
    // The House scene gets a fresh kitchen timer and a freshly rolled shopping
    // list simply by being (re)loaded.
    public void StartFreshAtLevel(string sceneName)
    {
        ResetRunState();

        if (sceneName == "Back-Yard")
        {
            // Starting in the Backyard assumes Bandit already cleared the House.
            requiredFood = new List<string>(backyardHotkeyFood);
            collectedFood = new List<string>(backyardHotkeyFood);
            shoppingListRolled = true;
            collectedAllFoodFromHouse = true;
        }

        Time.timeScale = 1f;
        SceneFader.LoadScene(sceneName);
    }

    // Picks this game's shopping list at random from every food in the House.
    public void RollShoppingList(List<string> pool)
    {
        List<string> bag = new List<string>(pool);
        requiredFood = new List<string>();
        int n = Mathf.Min(foodToCollect, bag.Count);
        for (int i = 0; i < n; i++)
        {
            int pick = UnityEngine.Random.Range(0, bag.Count);
            requiredFood.Add(bag[pick]);
            bag.RemoveAt(pick);
        }
        collectedFood.Clear();
        shoppingListRolled = true;
        Debug.Log("This game's shopping list: " + string.Join(", ", requiredFood));
    }

    public void WinGame()
    {
        Debug.Log("YOU WIN!");
    }
}
