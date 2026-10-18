using UnityEngine;
using UnityEngine.SceneManagement;

// Level hotkeys for testing / demoing:
//
//   Ctrl + 1  ->  Front Yard   (fresh game: 3 lives, full health)
//   Ctrl + 2  ->  House        (fresh game: 3 lives, fresh kitchen timer, fresh shopping list)
//   Ctrl + 3  ->  Backyard     (fresh game: 3 lives, food counted as already collected)
//
// Pressing one at any point during play (even mid-level, or from the pause
// menu) throws away the current run and starts a brand-new game in that level.
// They do nothing on the Main Menu.
//
// No scene setup needed - this creates itself when the game starts and
// survives scene changes, the same way SceneFader and the pause menu do.
public class LevelHotkeys : MonoBehaviour
{
    private const string FrontYardScene = "FrontYard";
    private const string HouseScene = "House";
    private const string BackyardScene = "Back-Yard";
    private const string MainMenuScene = "MainMenu";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FindFirstObjectByType<LevelHotkeys>() != null) return;
        GameObject go = new GameObject("LevelHotkeys");
        DontDestroyOnLoad(go);
        go.AddComponent<LevelHotkeys>();
    }

    private void Update()
    {
        if (!CtrlHeld()) return;

        string target = null;
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) target = FrontYardScene;
        else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) target = HouseScene;
        else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) target = BackyardScene;
        if (target == null) return;

        // Hotkeys are for jumping around the levels, not the menu.
        if (SceneManager.GetActiveScene().name == MainMenuScene) return;

        // A scene change is already in progress (door, restart, ...).
        if (SceneFader.IsBusy) return;

        GameManager gm = GameManager.Instance;
        if (gm == null)
        {
            Debug.LogWarning("LevelHotkeys: no GameManager in the scene, can't start a fresh game.");
            return;
        }

        Debug.Log("Hotkey: starting a fresh game in " + target);
        gm.StartFreshAtLevel(target);
    }

    private static bool CtrlHeld()
    {
        return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
            || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
    }
}
