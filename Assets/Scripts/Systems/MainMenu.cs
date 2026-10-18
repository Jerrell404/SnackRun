using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Sits on the MainMenu scene's Canvas. Three buttons up front.
public class MainMenu : MonoBehaviour
{
    [Header("Panels")]
    public GameObject buttonRow;
    public GameObject controlsPanel;
    public GameObject howToPlayPanel;

    [Header("Scene to load on Play")]
    [SerializeField] private string firstSceneName = "FrontYard";

    [Header("Volume sliders (in the Controls panel)")]
    public Slider masterVolumeSlider;
    public Slider musicVolumeSlider;
    public Slider sfxVolumeSlider;

    private void Awake()
    {
        // The menu should never start with a locked/hidden cursor, no
        // matter what state the OS cursor was left in.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Same safety net every other UI screen in the game uses - buttons
        // are dead without an EventSystem in the scene.
        if (EventSystem.current == null)
        {
            GameObject es = new GameObject("EventSystem (auto)");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        // Reflect whatever's saved (or the default 100%) without firing the
        // onValueChanged handlers below - those would just re-save the same
        // numbers right back, harmlessly, but there's no reason to.
        if (masterVolumeSlider != null) masterVolumeSlider.SetValueWithoutNotify(GameAudioSettings.Master);
        if (musicVolumeSlider != null) musicVolumeSlider.SetValueWithoutNotify(GameAudioSettings.Music);
        if (sfxVolumeSlider != null) sfxVolumeSlider.SetValueWithoutNotify(GameAudioSettings.Sfx);

        ShowButtons();
    }

    public void OnPlay()
    {
        // The menu runs with the cursor free so it can be clicked.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        SceneFader.LoadScene(firstSceneName);
    }

    private void Update()
    {
        // The Back button is a full-height overlay pinned right at the very top and bottom edges of the 1080px canvas.
        if (!Input.GetKeyDown(KeyCode.Escape)) return;
        bool controlsOpen = controlsPanel != null && controlsPanel.activeSelf;
        bool howToPlayOpen = howToPlayPanel != null && howToPlayPanel.activeSelf;
        if (controlsOpen || howToPlayOpen) OnBack();
    }

    public void OnControls()
    {
        if (buttonRow != null) buttonRow.SetActive(false);
        if (controlsPanel != null) controlsPanel.SetActive(true);
        if (howToPlayPanel != null) howToPlayPanel.SetActive(false);
        RefreshLayout(controlsPanel);
    }

    public void OnHowToPlay()
    {
        if (buttonRow != null) buttonRow.SetActive(false);
        if (howToPlayPanel != null) howToPlayPanel.SetActive(true);
        if (controlsPanel != null) controlsPanel.SetActive(false);
        RefreshLayout(howToPlayPanel);
    }

    // Safety net for the content inside each panel, in case it's ever shown before a layout pass has run.
    private void RefreshLayout(GameObject panel)
    {
        if (panel == null) return;
        RectTransform panelRect = panel.GetComponent<RectTransform>();

        LayoutGroup[] groups = panel.GetComponentsInChildren<LayoutGroup>(true);
        System.Array.Sort(groups, (a, b) => Depth(b.transform).CompareTo(Depth(a.transform)));

        for (int pass = 0; pass < 2; pass++)
        {
            foreach (LayoutGroup g in groups)
            {
                RectTransform rt = g.transform as RectTransform;
                if (rt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            }
            if (panelRect != null) LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
            Canvas.ForceUpdateCanvases();
        }

        // Confirms Content is populated and sized correctly. There is no
        // ScrollRect/mask chain here - Content is a plain direct child of
        // the panel, so nothing in this subtree can clip or hide its rows.
        Transform content = panel.transform.Find("Content");
        if (content != null)
        {
            RectTransform contentRect2 = content as RectTransform;
            Debug.Log($"[MainMenu] {panel.name} content: {content.childCount} children, " +
                      $"height={(contentRect2 != null ? contentRect2.rect.height : 0f):F1}");
        }
    }

    private static int Depth(Transform t)
    {
        int d = 0;
        while (t != null) { d++; t = t.parent; }
        return d;
    }

    public void OnBack()
    {
        ShowButtons();
    }

    // Wired to the three sliders' onValueChanged. GameAudioSettings applies
    // the new level to the right audio system immediately and saves it.
    public void OnMasterVolumeChanged(float value) => GameAudioSettings.SetMaster(value);
    public void OnMusicVolumeChanged(float value) => GameAudioSettings.SetMusic(value);
    public void OnSfxVolumeChanged(float value) => GameAudioSettings.SetSfx(value);

    public void OnQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void ShowButtons()
    {
        if (buttonRow != null) buttonRow.SetActive(true);
        if (controlsPanel != null) controlsPanel.SetActive(false);
        if (howToPlayPanel != null) howToPlayPanel.SetActive(false);
    }
}
