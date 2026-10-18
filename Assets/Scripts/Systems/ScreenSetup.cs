using UnityEngine;
using UnityEngine.SceneManagement;

// Locks the game to real fixed-size windows.
public static class ScreenSetup
{
    public const int GameplayWidth = 1920;
    public const int GameplayHeight = 1080;

    public const int MainMenuWidth = 2400; // 1080 * 20 / 9 - exact 20:9
    public const int MainMenuHeight = 1080;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        Apply(SceneManager.GetActiveScene().name);
        SceneManager.sceneLoaded += (scene, mode) => Apply(scene.name);
    }

    private static void Apply(string sceneName)
    {
        int width = sceneName == "MainMenu" ? MainMenuWidth : GameplayWidth;
        int height = sceneName == "MainMenu" ? MainMenuHeight : GameplayHeight;

        if (Screen.width != width || Screen.height != height || Screen.fullScreenMode != FullScreenMode.Windowed)
        {
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
        }
    }
}
