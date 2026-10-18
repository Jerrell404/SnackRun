using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Random shopping list for the House.
public static class FoodShoppingList
{
    private static int wrongGrabsThisAttempt = 0;
    private static AudioClip crashSound;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Setup();
    }

    private static void OnSceneLoaded(Scene s, LoadSceneMode m) => Setup();

    private static void Setup()
    {
        wrongGrabsThisAttempt = 0;

        GameManager gm = GameManager.Instance;
        if (gm == null) return;

        FoodItem[] foods = Object.FindObjectsByType<FoodItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (foods.Length == 0) return; // not the House

        List<string> pool = new List<string>();
        foreach (FoodItem f in foods)
        {
            if (!string.IsNullOrEmpty(f.FoodName) && !pool.Contains(f.FoodName))
                pool.Add(f.FoodName);
        }

        // Re-roll if this is a new game, or if the saved list no longer
        // matches the food in the scene (e.g. an item was renamed).
        bool listValid = gm.shoppingListRolled && gm.requiredFood.Count > 0 &&
                         gm.requiredFood.TrueForAll(pool.Contains);
        if (!listValid)
        {
            gm.RollShoppingList(pool);
            EnsureOneFridgeItem(gm);
        }

        FillChecklist(gm, foods);
    }

    // Every list must send Bandit to the fridge at least once - otherwise the
    // fridge (and Whiskers waking up) could be skipped entirely.
    private static void EnsureOneFridgeItem(GameManager gm)
    {
        List<string> inFridge = new List<string>();
        foreach (Fridge fridge in Object.FindObjectsByType<Fridge>(FindObjectsSortMode.None))
        {
            if (fridge.doorContents == null) continue;
            foreach (GameObject g in fridge.doorContents)
            {
                if (g == null) continue;
                FoodItem f = g.GetComponent<FoodItem>();
                if (f != null && !inFridge.Contains(f.FoodName)) inFridge.Add(f.FoodName);
            }
        }
        if (inFridge.Count == 0 || gm.requiredFood.Count == 0) return;
        if (gm.requiredFood.Exists(inFridge.Contains)) return;

        string pick = inFridge[UnityEngine.Random.Range(0, inFridge.Count)];
        int slot = UnityEngine.Random.Range(0, gm.requiredFood.Count);
        Debug.Log("Shopping list had nothing from the fridge - swapped " + gm.requiredFood[slot] + " for " + pick + ".");
        gm.requiredFood[slot] = pick;
    }

    // Writes this game's list into the on-screen checklist.
    private static void FillChecklist(GameManager gm, FoodItem[] foods)
    {
        UIManager ui = Object.FindFirstObjectByType<UIManager>();
        if (ui == null || ui.foodChecklist == null) return;

        HashSet<string> inFridge = new HashSet<string>();
        foreach (Fridge fridge in Object.FindObjectsByType<Fridge>(FindObjectsSortMode.None))
        {
            if (fridge.doorContents == null) continue;
            foreach (GameObject g in fridge.doorContents)
            {
                if (g == null) continue;
                FoodItem f = g.GetComponent<FoodItem>();
                if (f != null) inFridge.Add(f.FoodName);
            }
        }

        for (int i = 0; i < ui.foodChecklist.Length; i++)
        {
            FoodChecklistEntry entry = ui.foodChecklist[i];
            if (entry == null) continue;

            if (i < gm.requiredFood.Count)
            {
                string food = gm.requiredFood[i];
                entry.foodName = food;
                entry.displayName = inFridge.Contains(food) ? food + " (in fridge)"
                                  : Cupboard.IsInACupboard(food) ? food + " (in cupboard)"
                                  : food;
                if (entry.label != null) entry.label.gameObject.SetActive(true);
            }
            else if (entry.label != null)
            {
                entry.label.gameObject.SetActive(false);
            }
        }
    }

    // Called by FoodItem when Bandit grabs something that isn't on the list.
    public static void ReportWrongItem(FoodItem item)
    {
        wrongGrabsThisAttempt++;

        if (crashSound == null)
            crashSound = Resources.Load<AudioClip>("Audio/ChipCrunch");
        if (crashSound != null)
            AudioSource.PlayClipAtPoint(crashSound, item.transform.position, SoundFX.SfxVolumeScale);

        Color red = new Color(1f, 0.35f, 0.25f);
        if (wrongGrabsThisAttempt <= 2)
        {
            // 1st and 2nd wrong grab: Edgar comes to check where the crash was.
            Debug.Log("Wrong item #" + wrongGrabsThisAttempt + " (" + item.FoodName + ") - Edgar is coming to investigate.");
            if (HumanNPC.Instance != null)
                HumanNPC.Instance.Investigate(item.transform.position);
            ScreenMessage.Show(wrongGrabsThisAttempt == 1
                ? item.FoodName + " isn't on your list! CRASH - Edgar heard that and is coming to check. Hide!"
                : item.FoodName + " isn't on your list either! Edgar's coming again - one more wrong grab and he'll come straight for you!",
                5f, red);
        }
        else
        {
            // 3rd+: he comes straight for Bandit.
            Debug.Log("Wrong item #" + wrongGrabsThisAttempt + " (" + item.FoodName + ") - Edgar is coming straight for Bandit!");
            if (HumanNPC.Instance != null)
                HumanNPC.Instance.StartPatrolling();
            ScreenMessage.Show("Another wrong snack! Edgar is coming straight for you - RUN and hide!", 5f, red);
        }
    }
}
