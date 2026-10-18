using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    [Header("Lives (hearts)")]
    public GameObject[] hearts;

    [Header("Health Bar (current life)")]
    public Image healthBarFill;
    public PlayerHealth playerHealth;

    [Header("Food / Timer")]
    public TextMeshProUGUI foodText;
    public TextMeshProUGUI timerText;
    public KitchenTimer kitchenTimer;

    [Header("Timer warning (TV glitch at 10s)")]
    public TVFlickerController tvFlickerController;
    public float lowTimeThreshold = 10f;
    [Tooltip("Seconds left on the kitchen timer when the TV switches to static.")]
    public float tvStaticTime = 20f;
    private bool tvStaticTriggered = false;
    private bool tvWarningTriggered = false;

    [Header("All food collected message")]
    public GameObject allFoodCollectedMessage;
    private bool allFoodMessageShown = false;

    [Header("Exit door glow (turned on once all food is collected)")]
    public GameObject exitDoorGlow;

    [Header("Food checklist (right side)")]
    public FoodChecklistEntry[] foodChecklist;

    [Header("Insufficient food warning")]
    public GameObject insufficientFoodMessage;
    public TextMeshProUGUI insufficientFoodText;
    public float insufficientFoodDisplaySeconds = 2.5f;
    private Coroutine insufficientFoodCoroutine;

    [Header("Cat woke up warning (shown when the fridge is opened)")]
    public GameObject catWokeMessage;
    public float catWokeDisplaySeconds = 3f;
    private Coroutine catWokeCoroutine;

    [Header("Human chase warning (shown when the TV glitches, at low time)")]
    public GameObject humanChaseMessage;
    public float humanChaseDisplaySeconds = 4f;
    private Coroutine humanChaseCoroutine;

    [Header("Chip packet warning (shown the first time it's disturbed)")]
    public GameObject chipPacketWarningMessage;
    public float chipPacketWarningDisplaySeconds = 3f;
    private Coroutine chipPacketWarningCoroutine;

    [Header("Trap warning (shown whenever a trap goes off)")]
    public GameObject trapHitMessage;
    public float trapHitDisplaySeconds = 2f;
    private Coroutine trapHitCoroutine;

    [Header("Garden light warning (shown the FIRST time the human spots Bandit standing in a light - hooked up to HumanDetection.OnWarning)")]
    public GameObject lightWarningMessage;
    public float lightWarningDisplaySeconds = 2f;
    private Coroutine lightWarningCoroutine;

    [Header("Life-lost checkpoint restart message (shown right before a checkpoint scene reload)")]
    public GameObject lifeLostRestartMessage;
    public TextMeshProUGUI lifeLostRestartText;

    [Header("Objective / intro message (shown at the start of the level)")]
    public GameObject objectiveMessage;
    public float objectiveDisplaySeconds = 5f;
    [Tooltip("If true, the message hides itself after objectiveDisplaySeconds like the other warnings. If false, it stays on screen until the scene changes.")]
    public bool objectiveMessageAutoHide = true;

    private static readonly Color TimerNormalColor = Color.white;
    private static readonly Color TimerWarningColor = new Color(0.85f, 0.15f, 0.15f);
    private static readonly Color HealthFullColor = new Color(0.2f, 0.8f, 0.25f);
    private static readonly Color HealthHalfColor = new Color(0.95f, 0.8f, 0.15f);
    private static readonly Color FoodNormalColor = Color.white;
    private static readonly Color FoodCompleteColor = new Color(0.2f, 0.8f, 0.25f);
    private static readonly Color ChecklistPendingColor = Color.white;
    private static readonly Color ChecklistDoneColor = new Color(0.2f, 0.8f, 0.25f);

    private void OnEnable()
    {
        LevelExit.OnInsufficientFood += HandleInsufficientFood;
        AnimalTrap.OnTrapTriggered += HandleTrapTriggered;
        ChipPacket.OnFirstNoise += HandleChipPacketFirstNoise;
        ChipPacket.OnSecondNoise += HandleChipPacketSecondNoise;
    }

    private void OnDisable()
    {
        LevelExit.OnInsufficientFood -= HandleInsufficientFood;
        AnimalTrap.OnTrapTriggered -= HandleTrapTriggered;
        ChipPacket.OnFirstNoise -= HandleChipPacketFirstNoise;
        ChipPacket.OnSecondNoise -= HandleChipPacketSecondNoise;
    }

    private void Start()
    {
        // Call the human "Edgar" everywhere, including texts typed into the scene.
        foreach (TMP_Text t in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t.text == null) continue;
            string fixedText = t.text.Replace("the human", "Edgar").Replace("The human", "Edgar").Replace("the Human", "Edgar").Replace("The Human", "Edgar")
                                     .Replace("The cat", "Whiskers").Replace("the cat", "Whiskers").Replace("The Cat", "Whiskers").Replace("the Cat", "Whiskers")
                                     .Replace("The dog", "Rex").Replace("the dog", "Rex").Replace("The Dog", "Rex").Replace("the Dog", "Rex")
                                     .Replace("Tortist", "Tortoise");
            if (fixedText != t.text) t.text = fixedText;
        }

        // Auto-find PlayerHealth if it wasn't wired up in the Inspector -
        // without this the health bar silently skips every frame.
        if (playerHealth == null)
            playerHealth = FindObjectOfType<PlayerHealth>();

        if (objectiveMessage != null)
        {
            objectiveMessage.SetActive(true);

            if (objectiveMessageAutoHide)
            {
                StartCoroutine(HideObjectiveMessageAfterDelay());
            }
        }
    }

    private IEnumerator HideObjectiveMessageAfterDelay()
    {
        yield return new WaitForSeconds(objectiveDisplaySeconds);
        if (objectiveMessage != null)
        {
            objectiveMessage.SetActive(false);
        }
    }

    private void Update()
    {
        if (GameManager.Instance != null)
        {
            for (int i = 0; i < hearts.Length; i++)
            {
                if (hearts[i] != null)
                {
                    hearts[i].SetActive(i < GameManager.Instance.currentLives);
                }
            }

            int collected = GameManager.Instance.collectedFood.Count;
            int required = GameManager.Instance.requiredFood.Count;
            bool allFoodCollected = required > 0 && collected >= required;

            if (foodText != null)
            {
                if (allFoodCollected)
                {
                    foodText.text = "Food: " + collected + "/" + required + "  ESCAPE!";
                    foodText.color = FoodCompleteColor;
                }
                else
                {
                    foodText.text = "Food: " + collected + "/" + required;
                    foodText.color = FoodNormalColor;
                }
            }

            if (allFoodCollected && !allFoodMessageShown)
            {
                if (allFoodCollectedMessage != null)
                {
                    allFoodCollectedMessage.SetActive(true);
                }

                if (exitDoorGlow != null)
                {
                    exitDoorGlow.SetActive(true);
                }

                allFoodMessageShown = true;
            }

            if (foodChecklist != null)
            {
                for (int i = 0; i < foodChecklist.Length; i++)
                {
                    FoodChecklistEntry entry = foodChecklist[i];
                    if (entry == null || entry.label == null) continue;

                    // What's actually shown can carry a hint (e.g. "Milk (in
                    // fridge)") without disturbing foodName, which has to stay
                    // an exact match for GameManager.collectedFood entries.
                    string shownName = string.IsNullOrEmpty(entry.displayName) ? entry.foodName : entry.displayName;

                    bool done = GameManager.Instance.collectedFood.Contains(entry.foodName);
                    if (done)
                    {
                        entry.label.text = "<s>" + shownName + "</s> <color=#3ECC51>√</color>";
                        entry.label.color = ChecklistDoneColor;
                    }
                    else
                    {
                        entry.label.text = "○ " + shownName;
                        entry.label.color = ChecklistPendingColor;
                    }
                }
            }
        }

        if (playerHealth != null && healthBarFill != null)
        {
            // Full health = 1.0, half health = 0.5 (one hit taken this life).
            float fraction = playerHealth.HealthFraction;

            // Cover BOTH common health-bar Image setups so it works regardless of how the bar was configured in the Inspector: • Image.type = Filled  -> fillAmount.
            healthBarFill.fillAmount = fraction;

            RectTransform fillRect = healthBarFill.rectTransform;
            Vector2 anchorMax = fillRect.anchorMax;
            anchorMax.x = fraction;
            fillRect.anchorMax = anchorMax;

            // Green = full health, yellow = one hit taken.
            healthBarFill.color = fraction < 1f ? HealthHalfColor : HealthFullColor;
        }

        if (kitchenTimer != null)
        {
            int minutes = Mathf.FloorToInt(kitchenTimer.timeRemaining / 60f);
            int seconds = Mathf.FloorToInt(kitchenTimer.timeRemaining % 60f);

            if (timerText != null)
            {
                timerText.text = string.Format("Time: {0:00}:{1:00}", minutes, seconds);

                bool lowTime = kitchenTimer.timeRemaining <= lowTimeThreshold;
                timerText.color = lowTime ? TimerWarningColor : TimerNormalColor;

                if (lowTime && !tvWarningTriggered)
                {
                    ShowHumanChaseMessage();
                    tvWarningTriggered = true;
                }
            }

            // The TV switches to static once, when exactly 20 seconds are left on the timer - nothing else triggers it.
            if (!tvStaticTriggered && kitchenTimer.timeRemaining <= tvStaticTime)
            {
                if (tvFlickerController != null) tvFlickerController.StartFlicker();
                tvStaticTriggered = true;
            }
        }
    }

    private void HandleInsufficientFood(int remaining)
    {
        if (insufficientFoodMessage == null) return;

        if (insufficientFoodText != null)
        {
            insufficientFoodText.text = remaining == 1
                ? "You still need 1 more food item before Bandit can leave!"
                : "You still need " + remaining + " more food items before Bandit can leave!";
        }

        insufficientFoodMessage.SetActive(true);

        if (insufficientFoodCoroutine != null)
        {
            StopCoroutine(insufficientFoodCoroutine);
        }
        insufficientFoodCoroutine = StartCoroutine(HideInsufficientFoodAfterDelay());
    }

    // Short on-screen warning with custom text (reuses the insufficient-food
    // panel) - e.g. when Bandit grabs food that isn't on the shopping list.
    public void ShowWarning(string text)
    {
        // Own always-on-screen banner (the old panel could sit outside the
        // camera's view, so the message was never seen).
        Debug.Log("[UI] " + text);
        ScreenMessage.Show(text, 3.5f);
    }

    private IEnumerator HideInsufficientFoodAfterDelay()
    {
        yield return new WaitForSeconds(insufficientFoodDisplaySeconds);
        if (insufficientFoodMessage != null)
        {
            insufficientFoodMessage.SetActive(false);
        }
        insufficientFoodCoroutine = null;
    }

    // Hooked up to Fridge.onFridgeOpened so the player gets a clear warning
    // the moment Whiskers wakes up.
    public void ShowCatWokeMessage()
    {
        if (catWokeMessage == null) return;

        catWokeMessage.SetActive(true);

        if (catWokeCoroutine != null)
        {
            StopCoroutine(catWokeCoroutine);
        }
        catWokeCoroutine = StartCoroutine(HideCatWokeMessageAfterDelay());
    }

    private IEnumerator HideCatWokeMessageAfterDelay()
    {
        yield return new WaitForSeconds(catWokeDisplaySeconds);
        if (catWokeMessage != null)
        {
            catWokeMessage.SetActive(false);
        }
        catWokeCoroutine = null;
    }

    // Fired the moment the TV starts glitching (the low-time warning) -
    // warns the player Edgar is on his way so they should rush to finish up
    // and get out.
    public void ShowHumanChaseMessage()
    {
        // The old panel sat half off the top of the screen - use the
        // on-screen banner instead so the player can actually read it.
        ScreenMessage.Show("You woke up Edgar! RUN and hide!", 3.5f, new Color(1f, 0.35f, 0.25f));
        return;

        humanChaseMessage.SetActive(true);

        if (humanChaseCoroutine != null)
        {
            StopCoroutine(humanChaseCoroutine);
        }
        humanChaseCoroutine = StartCoroutine(HideHumanChaseMessageAfterDelay());
    }

    private IEnumerator HideHumanChaseMessageAfterDelay()
    {
        yield return new WaitForSeconds(humanChaseDisplaySeconds);
        if (humanChaseMessage != null)
        {
            humanChaseMessage.SetActive(false);
        }
        humanChaseCoroutine = null;
    }

    // The first time the chip packet is disturbed - the cat wakes up AND
    // the player is warned the human might have heard it too.
    private void HandleChipPacketFirstNoise()
    {
        if (chipPacketWarningMessage == null) return;

        chipPacketWarningMessage.SetActive(true);

        if (chipPacketWarningCoroutine != null)
        {
            StopCoroutine(chipPacketWarningCoroutine);
        }
        chipPacketWarningCoroutine = StartCoroutine(HideChipPacketWarningAfterDelay());
    }

    // The SECOND time the chip packet is disturbed Edgar wakes and charges at Bandit.
    private void HandleChipPacketSecondNoise()
    {
        ShowHumanChaseMessage();
    }

    private IEnumerator HideChipPacketWarningAfterDelay()
    {
        yield return new WaitForSeconds(chipPacketWarningDisplaySeconds);
        if (chipPacketWarningMessage != null)
        {
            chipPacketWarningMessage.SetActive(false);
        }
        chipPacketWarningCoroutine = null;
    }

    // Called by RestartSceneOnLifeLoss right before it reloads the scene on a checkpoint restart (a life lost, but the game continues)
    public void ShowLifeLostRestartMessage(string message)
    {
        if (lifeLostRestartMessage == null) return;

        lifeLostRestartMessage.SetActive(true);
        if (lifeLostRestartText != null)
        {
            lifeLostRestartText.text = message;
        }
    }

    // Any AnimalTrap going off anywhere in the scene.
    private void HandleTrapTriggered()
    {
        if (trapHitMessage == null) return;

        trapHitMessage.SetActive(true);

        if (trapHitCoroutine != null)
        {
            StopCoroutine(trapHitCoroutine);
        }
        trapHitCoroutine = StartCoroutine(HideTrapHitMessageAfterDelay());
    }

    private IEnumerator HideTrapHitMessageAfterDelay()
    {
        yield return new WaitForSeconds(trapHitDisplaySeconds);
        if (trapHitMessage != null)
        {
            trapHitMessage.SetActive(false);
        }
        trapHitCoroutine = null;
    }

    // Hooked up (via the Inspector) to HumanDetection's OnWarning UnityEvent.
    public void ShowLightWarningMessage()
    {
        if (lightWarningMessage == null) return;

        lightWarningMessage.SetActive(true);

        if (lightWarningCoroutine != null)
        {
            StopCoroutine(lightWarningCoroutine);
        }
        lightWarningCoroutine = StartCoroutine(HideLightWarningMessageAfterDelay());
    }

    private IEnumerator HideLightWarningMessageAfterDelay()
    {
        yield return new WaitForSeconds(lightWarningDisplaySeconds);
        if (lightWarningMessage != null)
        {
            lightWarningMessage.SetActive(false);
        }
        lightWarningCoroutine = null;
    }
}

[Serializable]
public class FoodChecklistEntry
{
    public string foodName;
    [Tooltip("Optional - what to actually show in the checklist.")]
    public string displayName;
    public TextMeshProUGUI label;
}
