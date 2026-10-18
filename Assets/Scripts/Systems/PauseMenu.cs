using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Creates the one and only Pause Menu the first time a scene other than the Main Menu loads, and keeps it alive for the rest of the game.
public static class PauseMenuBootstrap
{
    private static bool created;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        TryCreate(SceneManager.GetActiveScene());
        SceneManager.sceneLoaded += (scene, mode) => TryCreate(scene);
    }

    private static void TryCreate(Scene scene)
    {
        if (created) return;
        if (scene.name == "MainMenu") return; // nothing to pause there

        GameObject go = new GameObject("PauseMenu");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<PauseMenuController>();
        created = true;
    }
}

// Press P in Front Yard, House or Backyard to open/close this. Freezes the whole game.
public class PauseMenuController : MonoBehaviour
{
    // Key -> what it does, same table as the Main Menu's Controls panel
    // (kept in sync by hand - see MainMenuSetup.cs's own ControlRows) plus
    // the Pause key itself, which only makes sense to list here/in-game.
    private static readonly (string key, string action)[] ControlRows =
    {
        ("W A S D", "Moves Bandit - W forward, A left, S back, D right"),
        ("Mouse", "Look around and aim the camera"),
        ("Space", "Jump"),
        ("R (hold)", "Sprint forward"),
        ("Shift (hold)", "Crouch and sneak quietly"),
        ("E", "Interact - collect food, open fridge/cupboard, hide, talk to Toby"),
        ("P", "Pause / resume the game"),
    };

    private const string ControlsTip =
        "Tip: crouching keeps Bandit low and quiet - perfect for sneaking past Edgar and staying out of garden lights.";

    // Same palette as MainMenuSetup.cs (Assets/Editor/MainMenuSetup.cs) so
    // this reads as the same menu, not a different screen.
    private static readonly Color NightNavy = new Color(0.078f, 0.128f, 0.190f);
    private static readonly Color PanelNavy = new Color(0.115f, 0.150f, 0.205f, 0.97f);
    private static readonly Color RowNavy = new Color(0.170f, 0.143f, 0.158f, 1f);
    private static readonly Color Gold = new Color(0.957f, 0.773f, 0.592f);
    private static readonly Color GrassGreen = new Color(0.30f, 0.86f, 0.22f);
    private static readonly Color VibrantRed = new Color(0.92f, 0.14f, 0.12f);
    private static readonly Color VibrantOrange = new Color(1.00f, 0.50f, 0.06f);
    private static readonly Color VividGold = new Color(1.00f, 0.82f, 0.20f);
    private static readonly Color VividCoral = new Color(0.98f, 0.36f, 0.20f);
    private static readonly Color VividAmber = new Color(0.95f, 0.62f, 0.04f);
    private static readonly Color BodyTextColor = new Color(0.93f, 0.90f, 0.87f, 1f);
    private static readonly Color ControlsChip = new Color(0.816f, 0.412f, 0.278f);
    private static readonly Color ControlsRowLine = new Color(1f, 1f, 1f, 0.08f);

    private GameObject pausePanel;
    private GameObject buttonRow;
    private GameObject controlsPanel;
    private TextMeshProUGUI levelText;
    private Slider masterVolumeSlider, musicVolumeSlider, sfxVolumeSlider;
    private bool isPaused;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        BuildUi();

        // Safety net: never leave the game frozen (or the pause screen
        // sitting open) across a scene change - without this, pausing
        // right as a level transition/checkpoint reload happens would
        // leave Time.timeScale stuck at 0 in the new scene.
        SceneManager.sceneLoaded += (scene, mode) => { if (isPaused) ForceResume(); };
    }

    private void Update()
    {
        // Same reasoning as the Main Menu's own Controls/How to Play panels.
        if (isPaused && controlsPanel != null && controlsPanel.activeSelf && Input.GetKeyDown(KeyCode.Escape))
        {
            OnBack();
            return;
        }

        if (!Input.GetKeyDown(KeyCode.P)) return;

        // Belt-and-suspenders - this object is never even created while
        // the Main Menu is the active scene (see PauseMenuBootstrap), but
        // keep the guard here too in case that ever changes.
        if (SceneManager.GetActiveScene().name == "MainMenu") return;

        if (isPaused)
        {
            // Closes the pause screen and resumes, no matter which panel
            // (button row or Controls) happens to be showing.
            Resume();
        }
        else
        {
            // Don't hijack a life-lost/game-over/win freeze that's already
            // showing its own screen - all of those also zero
            // Time.timeScale, so only open the pause menu if nothing else
            // already paused the game.
            if (Time.timeScale <= 0f) return;
            Pause();
        }
    }

    private void Pause()
    {
        isPaused = true;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (EventSystem.current == null)
        {
            GameObject es = new GameObject("EventSystem (auto)");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        // Reflect whatever's actually saved right now - covers the case
        // where the level's own volume was last changed from a different
        // scene's pause menu (GameAudioSettings is the single shared
        // source of truth, so this is just re-displaying it).
        masterVolumeSlider.SetValueWithoutNotify(GameAudioSettings.Master);
        musicVolumeSlider.SetValueWithoutNotify(GameAudioSettings.Music);
        sfxVolumeSlider.SetValueWithoutNotify(GameAudioSettings.Sfx);

        levelText.text = "PAUSE: " + GetLevelDisplayName(SceneManager.GetActiveScene().name);

        ShowButtons();
        pausePanel.SetActive(true);
    }

    // Wired to the Resume button, and also what P calls to close the menu.
    public void Resume()
    {
        isPaused = false;
        Time.timeScale = 1f;
        pausePanel.SetActive(false);

        // Back into "game mode" exactly where Bandit stopped - nothing
        // moved while frozen, so locking/hiding the cursor again is all
        // that's needed to drop straight back into gameplay.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // Used only by the scene-change safety net in Awake() - same effect as
    // Resume(), just without touching the cursor (the new scene's own
    // startup code handles that).
    private void ForceResume()
    {
        isPaused = false;
        Time.timeScale = 1f;
        pausePanel.SetActive(false);
    }

    // Same three scene names Build Settings actually uses (see
    // EditorBuildSettings.asset) mapped to the friendly names players see.
    private static string GetLevelDisplayName(string sceneName)
    {
        switch (sceneName)
        {
            case "FrontYard": return "Front Yard";
            case "House": return "House";
            case "Back-Yard": return "Backyard";
            default: return sceneName;
        }
    }

    private void OnQuitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OnControls()
    {
        buttonRow.SetActive(false);
        controlsPanel.SetActive(true);
    }

    private void OnBack()
    {
        ShowButtons();
    }

    private void OnMasterVolumeChanged(float value) => GameAudioSettings.SetMaster(value);
    private void OnMusicVolumeChanged(float value) => GameAudioSettings.SetMusic(value);
    private void OnSfxVolumeChanged(float value) => GameAudioSettings.SetSfx(value);

    private void ShowButtons()
    {
        buttonRow.SetActive(true);
        controlsPanel.SetActive(false);
    }

    // ---- UI construction.

    private void BuildUi()
    {
        GameObject canvasGO = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        Canvas canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500; // always drawn above the in-game HUD
        CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        pausePanel = new GameObject("PausePanel", typeof(RectTransform));
        pausePanel.transform.SetParent(canvasGO.transform, false);
        Stretch((RectTransform)pausePanel.transform);

        Image navyBase = CreatePanel(pausePanel.transform, "BackdropBase", NightNavy);
        navyBase.raycastTarget = false;
        Stretch((RectTransform)navyBase.transform);

        // Same poster art the Main Menu uses.
        Sprite backdropArtSprite = Resources.Load<Sprite>("UI/MainMenuBackdropArt");
        if (backdropArtSprite != null)
        {
            GameObject backdropArtGO = new GameObject("BackdropArt", typeof(RectTransform), typeof(Image));
            backdropArtGO.transform.SetParent(pausePanel.transform, false);
            Image backdropArtImg = backdropArtGO.GetComponent<Image>();
            backdropArtImg.sprite = backdropArtSprite;
            backdropArtImg.preserveAspect = false;
            backdropArtImg.raycastTarget = false;
            Stretch((RectTransform)backdropArtGO.transform);
        }

        // One combined "PAUSE: <Level>" line, beneath Bandit's feet in the backdrop art and centered above the button row.
        levelText = CreateText(pausePanel.transform, "LevelText", "", 42, FontStyles.Bold, Gold);
        RectTransform levelRect = levelText.rectTransform;
        levelRect.anchorMin = levelRect.anchorMax = new Vector2(0.5f, 0f);
        levelRect.pivot = new Vector2(0.5f, 0f);
        levelRect.anchoredPosition = new Vector2(0f, 300f);
        levelRect.sizeDelta = new Vector2(1100f, 60f);

        buttonRow = new GameObject("ButtonRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        buttonRow.transform.SetParent(pausePanel.transform, false);
        RectTransform groupRect = (RectTransform)buttonRow.transform;
        groupRect.anchorMin = groupRect.anchorMax = new Vector2(0.5f, 0f);
        groupRect.pivot = new Vector2(0.5f, 0f);
        groupRect.anchoredPosition = new Vector2(0f, 90f);
        groupRect.sizeDelta = new Vector2(700f, 176f);
        HorizontalLayoutGroup hLayout = buttonRow.GetComponent<HorizontalLayoutGroup>();
        hLayout.spacing = 40f;
        hLayout.childAlignment = TextAnchor.MiddleCenter;
        hLayout.childControlHeight = true;
        hLayout.childControlWidth = true;
        hLayout.childForceExpandHeight = false;
        hLayout.childForceExpandWidth = false;

        Button resumeButton = CreateIconButton(buttonRow.transform, "ResumeButton", "Resume", GrassGreen, MenuIcon.Play);
        Button controlsButton = CreateIconButton(buttonRow.transform, "ControlsButton", "Controls", VibrantOrange, MenuIcon.Controls);
        Button quitButton = CreateIconButton(buttonRow.transform, "QuitButton", "Quit", VibrantRed, MenuIcon.Exit);

        controlsPanel = BuildControlsPanel(pausePanel.transform, out Button backButton,
            out masterVolumeSlider, out musicVolumeSlider, out sfxVolumeSlider);
        controlsPanel.SetActive(false);

        resumeButton.onClick.AddListener(Resume);
        controlsButton.onClick.AddListener(OnControls);
        quitButton.onClick.AddListener(OnQuitClicked);
        backButton.onClick.AddListener(OnBack);
        masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
        musicVolumeSlider.onValueChanged.AddListener(OnMusicVolumeChanged);
        sfxVolumeSlider.onValueChanged.AddListener(OnSfxVolumeChanged);

        ForceLayoutRebuild(canvasGO);

        pausePanel.SetActive(false);
    }

    private GameObject BuildControlsPanel(Transform parent, out Button backButton,
        out Slider masterSlider, out Slider musicSlider, out Slider sfxSlider)
    {
        GameObject panel = new GameObject("ControlsPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);
        Image bg = panel.GetComponent<Image>();
        bg.color = PanelNavy;
        RectTransform panelRect = (RectTransform)panel.transform;
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        // Deliberately well short of the Canvas's own 1080-tall reference resolution (see BuildUi's CanvasScaler)
        panelRect.sizeDelta = new Vector2(1180f, 860f);
        panelRect.anchoredPosition = Vector2.zero;

        TextMeshProUGUI headingText = CreateText(panel.transform, "Heading", "Controls", 46, FontStyles.Bold, Gold);
        RectTransform headingRect = headingText.rectTransform;
        headingRect.anchorMin = new Vector2(0f, 1f);
        headingRect.anchorMax = new Vector2(1f, 1f);
        headingRect.pivot = new Vector2(0.5f, 1f);
        headingRect.anchoredPosition = new Vector2(0f, -28f);
        headingRect.sizeDelta = new Vector2(-80f, 60f);

        Image divider = CreatePanel(panel.transform, "Divider", new Color(1f, 1f, 1f, 0.12f));
        RectTransform dividerRect = divider.rectTransform;
        dividerRect.anchorMin = new Vector2(0f, 1f);
        dividerRect.anchorMax = new Vector2(1f, 1f);
        dividerRect.pivot = new Vector2(0.5f, 1f);
        dividerRect.anchoredPosition = new Vector2(0f, -92f);
        dividerRect.sizeDelta = new Vector2(-80f, 2f);

        // Content area - takes exactly the space between the heading/divider and the Back button.
        GameObject contentAreaGO = new GameObject("ContentArea", typeof(RectTransform));
        contentAreaGO.transform.SetParent(panel.transform, false);
        RectTransform contentAreaRect = (RectTransform)contentAreaGO.transform;
        contentAreaRect.anchorMin = Vector2.zero;
        contentAreaRect.anchorMax = Vector2.one;
        contentAreaRect.offsetMin = new Vector2(50f, 110f);
        contentAreaRect.offsetMax = new Vector2(-50f, -110f);

        GameObject contentGO = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup));
        contentGO.transform.SetParent(contentAreaGO.transform, false);
        RectTransform contentRect = (RectTransform)contentGO.transform;
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;
        VerticalLayoutGroup contentLayout = contentGO.GetComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 7f;
        contentLayout.padding = new RectOffset(10, 26, 6, 10);
        contentLayout.childAlignment = TextAnchor.UpperLeft;
        contentLayout.childControlHeight = true;
        contentLayout.childControlWidth = true;
        contentLayout.childForceExpandHeight = false;
        contentLayout.childForceExpandWidth = true;

        PopulateControlsContent(contentGO.transform, out masterSlider, out musicSlider, out sfxSlider);

        // Every row now exists.
        ForceLayoutRebuild(panel);
        float requiredContentHeight = LayoutUtility.GetPreferredSize(contentRect, 1);
        contentRect.sizeDelta = new Vector2(0f, requiredContentHeight);
        float availableContentHeight = contentAreaRect.rect.height;
        float fitScale = requiredContentHeight > availableContentHeight && requiredContentHeight > 0f
            ? Mathf.Clamp(availableContentHeight / requiredContentHeight, 0.6f, 1f)
            : 1f;
        contentRect.localScale = new Vector3(fitScale, fitScale, 1f);
        ForceLayoutRebuild(panel);

        backButton = CreateButton(panel.transform, "BackButton", "Back", new Color(0.3f, 0.3f, 0.35f));
        RectTransform backRect = backButton.GetComponent<RectTransform>();
        backRect.anchorMin = backRect.anchorMax = new Vector2(0.5f, 0f);
        backRect.pivot = new Vector2(0.5f, 0f);
        backRect.anchoredPosition = new Vector2(0f, 30f);
        backRect.sizeDelta = new Vector2(220f, 64f);

        ForceLayoutRebuild(panel);
        return panel;
    }

    private static void PopulateControlsContent(Transform content, out Slider masterSlider, out Slider musicSlider, out Slider sfxSlider)
    {
        GameObject headerRow = new GameObject("ColumnHeader", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        headerRow.transform.SetParent(content, false);
        HorizontalLayoutGroup hh = headerRow.GetComponent<HorizontalLayoutGroup>();
        hh.padding = new RectOffset(10, 20, 0, 0);
        hh.spacing = 20f;
        hh.childAlignment = TextAnchor.MiddleLeft;
        hh.childControlHeight = true;
        hh.childControlWidth = true;
        hh.childForceExpandHeight = false;
        hh.childForceExpandWidth = false;
        LayoutElement headerRowLayout = headerRow.GetComponent<LayoutElement>();
        headerRowLayout.minHeight = 28f;
        headerRowLayout.preferredHeight = 28f;

        TextMeshProUGUI keyLabel = CreateText(headerRow.transform, "KeyLabel", "KEY", 17, FontStyles.Bold,
            new Color(ControlsChip.r, ControlsChip.g, ControlsChip.b, 0.85f));
        keyLabel.alignment = TextAlignmentOptions.MidlineLeft;
        keyLabel.characterSpacing = 2f;
        LayoutElement keyLabelLayout = keyLabel.gameObject.AddComponent<LayoutElement>();
        keyLabelLayout.preferredWidth = 170f;
        keyLabelLayout.minWidth = 170f;

        TextMeshProUGUI actionLabel = CreateText(headerRow.transform, "ActionLabel", "ACTION", 17, FontStyles.Bold,
            new Color(ControlsChip.r, ControlsChip.g, ControlsChip.b, 0.85f));
        actionLabel.alignment = TextAlignmentOptions.MidlineLeft;
        actionLabel.characterSpacing = 2f;
        LayoutElement actionLabelLayout = actionLabel.gameObject.AddComponent<LayoutElement>();
        actionLabelLayout.flexibleWidth = 1f;

        Image headerDivider = CreatePanel(content, "ColumnHeaderDivider", ControlsRowLine);
        LayoutElement headerDividerLayout = headerDivider.gameObject.AddComponent<LayoutElement>();
        headerDividerLayout.minHeight = 2f;
        headerDividerLayout.preferredHeight = 2f;

        foreach ((string key, string action) in ControlRows)
        {
            CreateControlRow(content, key, action);
        }

        TextMeshProUGUI tip = CreateText(content, "Tip", ControlsTip, 20, FontStyles.Italic,
            new Color(BodyTextColor.r, BodyTextColor.g, BodyTextColor.b, 0.75f));
        tip.alignment = TextAlignmentOptions.MidlineLeft;
        LayoutElement tipLayout = tip.gameObject.AddComponent<LayoutElement>();
        tipLayout.minHeight = 36f;
        RectTransform tipRect = tip.rectTransform;
        tipRect.offsetMin = new Vector2(4f, tipRect.offsetMin.y);
        tipRect.offsetMax = new Vector2(-4f, tipRect.offsetMax.y);

        TextMeshProUGUI volHeading = CreateText(content, "VolumeHeading", "Volume", 27, FontStyles.Bold, ControlsChip);
        volHeading.alignment = TextAlignmentOptions.MidlineLeft;
        LayoutElement volHeadingLayout = volHeading.gameObject.AddComponent<LayoutElement>();
        volHeadingLayout.minHeight = 26f;

        GameObject volumeGroup = new GameObject("VolumeRows", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        volumeGroup.transform.SetParent(content, false);
        VerticalLayoutGroup vv = volumeGroup.GetComponent<VerticalLayoutGroup>();
        vv.spacing = 26f;
        vv.childAlignment = TextAnchor.UpperLeft;
        vv.childControlHeight = true;
        vv.childControlWidth = true;
        vv.childForceExpandHeight = false;
        vv.childForceExpandWidth = true;

        masterSlider = CreateVolumeRow(volumeGroup.transform, "Master", VividGold);
        musicSlider = CreateVolumeRow(volumeGroup.transform, "Music", VividCoral);
        sfxSlider = CreateVolumeRow(volumeGroup.transform, "SFX", VividAmber);
    }

    private static void CreateControlRow(Transform parent, string key, string action)
    {
        GameObject row = new GameObject("Row_" + key, typeof(RectTransform), typeof(Image),
            typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        Image rowBg = row.GetComponent<Image>();
        rowBg.color = RowNavy;
        LayoutElement rowLayout = row.GetComponent<LayoutElement>();
        rowLayout.minHeight = 56f;
        rowLayout.preferredHeight = 56f;
        HorizontalLayoutGroup h = row.GetComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(0, 20, 0, 0);
        h.spacing = 0f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlHeight = true;
        h.childControlWidth = true;
        h.childForceExpandHeight = true;
        h.childForceExpandWidth = false;

        GameObject chip = new GameObject("Key", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        chip.transform.SetParent(row.transform, false);
        Image chipImg = chip.GetComponent<Image>();
        chipImg.color = ControlsChip;
        LayoutElement chipLayout = chip.GetComponent<LayoutElement>();
        chipLayout.preferredWidth = 185f;
        chipLayout.minWidth = 185f;
        chipLayout.preferredHeight = 56f;
        chipLayout.minHeight = 56f;

        TextMeshProUGUI chipText = CreateText(chip.transform, "Label", key, 20, FontStyles.Bold, Color.white);
        Stretch(chipText.rectTransform);
        chipText.alignment = TextAlignmentOptions.Center;
        chipText.enableAutoSizing = true;
        chipText.fontSizeMin = 14;
        chipText.fontSizeMax = 20;
        chipText.margin = new Vector4(10f, 0f, 10f, 0f);

        GameObject descGO = new GameObject("Description", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        descGO.transform.SetParent(row.transform, false);
        LayoutElement descLayout = descGO.GetComponent<LayoutElement>();
        descLayout.flexibleWidth = 1f;
        descLayout.minHeight = 48f;
        TextMeshProUGUI descText = descGO.GetComponent<TextMeshProUGUI>();
        descText.text = action;
        descText.fontSize = 21f;
        descText.color = BodyTextColor;
        descText.alignment = TextAlignmentOptions.MidlineLeft;
        descText.margin = new Vector4(20f, 0f, 0f, 0f);
        descText.raycastTarget = false;
        descText.ForceMeshUpdate();

        Image divider = CreatePanel(parent, "Divider_" + key, ControlsRowLine);
        LayoutElement dividerLayout = divider.gameObject.AddComponent<LayoutElement>();
        dividerLayout.minHeight = 1f;
        dividerLayout.preferredHeight = 1f;
    }

    private static Slider CreateVolumeRow(Transform parent, string label, Color fillColor)
    {
        GameObject row = new GameObject("Row_" + label, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        LayoutElement rowLayout = row.GetComponent<LayoutElement>();
        rowLayout.minHeight = 48f;
        rowLayout.preferredHeight = 48f;
        HorizontalLayoutGroup h = row.GetComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(10, 40, 0, 0);
        h.spacing = 20f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlHeight = true;
        h.childControlWidth = true;
        h.childForceExpandHeight = true;
        h.childForceExpandWidth = false;

        GameObject chip = new GameObject("Label", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        chip.transform.SetParent(row.transform, false);
        Image chipBg = chip.GetComponent<Image>();
        chipBg.color = ControlsChip;
        LayoutElement chipLayout = chip.GetComponent<LayoutElement>();
        chipLayout.preferredWidth = 160f;
        chipLayout.minWidth = 160f;
        chipLayout.preferredHeight = 42f;
        chipLayout.minHeight = 42f;
        TextMeshProUGUI chipText = CreateText(chip.transform, "Text", label, 19, FontStyles.Bold, Color.white);
        Stretch(chipText.rectTransform);
        chipText.alignment = TextAlignmentOptions.Center;

        GameObject sliderWrap = new GameObject("SliderWrap", typeof(RectTransform), typeof(LayoutElement));
        sliderWrap.transform.SetParent(row.transform, false);
        LayoutElement sliderWrapLayout = sliderWrap.GetComponent<LayoutElement>();
        sliderWrapLayout.flexibleWidth = 1f;
        sliderWrapLayout.preferredHeight = 44f;
        sliderWrapLayout.minHeight = 44f;

        Slider slider = CreateSlider(sliderWrap.transform, fillColor);
        Stretch(slider.GetComponent<RectTransform>());

        return slider;
    }

    private static Slider CreateSlider(Transform parent, Color fillColor)
    {
        GameObject sliderGO = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
        sliderGO.transform.SetParent(parent, false);

        GameObject bgGO = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bgGO.transform.SetParent(sliderGO.transform, false);
        Image bgImg = bgGO.GetComponent<Image>();
        bgImg.sprite = MakeRoundedTrackSprite();
        bgImg.type = Image.Type.Sliced;
        bgImg.color = new Color(0.32f, 0.30f, 0.34f);
        RectTransform bgRect = (RectTransform)bgGO.transform;
        bgRect.anchorMin = new Vector2(0f, 0.3f);
        bgRect.anchorMax = new Vector2(1f, 0.7f);
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        GameObject fillAreaGO = new GameObject("Fill Area", typeof(RectTransform));
        fillAreaGO.transform.SetParent(sliderGO.transform, false);
        RectTransform fillAreaRect = (RectTransform)fillAreaGO.transform;
        fillAreaRect.anchorMin = new Vector2(0f, 0.3f);
        fillAreaRect.anchorMax = new Vector2(1f, 0.7f);
        fillAreaRect.offsetMin = Vector2.zero;
        fillAreaRect.offsetMax = new Vector2(-18f, 0f);

        GameObject fillGO = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGO.transform.SetParent(fillAreaGO.transform, false);
        Image fillImg = fillGO.GetComponent<Image>();
        fillImg.sprite = MakeRoundedTrackSprite();
        fillImg.type = Image.Type.Sliced;
        fillImg.color = fillColor;
        RectTransform fillRect = (RectTransform)fillGO.transform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        const float handleSize = 24f;
        const float handleHalf = handleSize * 0.5f;
        const float handleShiftLeft = 3f;

        GameObject handleAreaGO = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleAreaGO.transform.SetParent(sliderGO.transform, false);
        RectTransform handleAreaRect = (RectTransform)handleAreaGO.transform;
        handleAreaRect.anchorMin = Vector2.zero;
        handleAreaRect.anchorMax = Vector2.one;
        handleAreaRect.offsetMin = new Vector2(handleHalf - handleShiftLeft, 0f);
        handleAreaRect.offsetMax = new Vector2(-handleHalf - handleShiftLeft, 0f);

        GameObject handleAnchorGO = new GameObject("HandleAnchor", typeof(RectTransform));
        handleAnchorGO.transform.SetParent(handleAreaGO.transform, false);
        RectTransform handleAnchorRect = (RectTransform)handleAnchorGO.transform;

        GameObject handleGO = new GameObject("HandleVisual", typeof(RectTransform), typeof(Image));
        handleGO.transform.SetParent(handleAnchorGO.transform, false);
        Image handleImg = handleGO.GetComponent<Image>();
        handleImg.sprite = BuildBadgeSprite(Color.white, Color.white);
        handleImg.color = Color.white;
        handleImg.preserveAspect = true;
        RectTransform handleRect = (RectTransform)handleGO.transform;
        handleRect.anchorMin = handleRect.anchorMax = new Vector2(0.5f, 0.5f);
        handleRect.pivot = new Vector2(0.5f, 0.5f);
        handleRect.anchoredPosition = Vector2.zero;
        handleRect.sizeDelta = new Vector2(handleSize, handleSize);

        Slider slider = sliderGO.GetComponent<Slider>();
        slider.targetGraphic = handleImg;
        slider.fillRect = fillRect;
        slider.handleRect = handleAnchorRect;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.value = 1f;

        return slider;
    }

    private enum MenuIcon { Play, Controls, Exit }

    private static Button CreateIconButton(Transform parent, string name, string label, Color color, MenuIcon icon)
    {
        GameObject root = new GameObject(name, typeof(RectTransform), typeof(Button), typeof(LayoutElement));
        root.transform.SetParent(parent, false);
        LayoutElement rootLE = root.GetComponent<LayoutElement>();
        rootLE.preferredWidth = 180f;
        rootLE.preferredHeight = 176f;

        GameObject badgeGO = new GameObject("Badge", typeof(RectTransform), typeof(Image));
        badgeGO.transform.SetParent(root.transform, false);
        Image badgeImg = badgeGO.GetComponent<Image>();
        badgeImg.sprite = BuildBadgeSprite(color, Color.Lerp(color, Color.black, 0.32f));
        badgeImg.color = Color.white;
        RectTransform badgeRect = badgeGO.GetComponent<RectTransform>();
        badgeRect.anchorMin = badgeRect.anchorMax = badgeRect.pivot = new Vector2(0.5f, 1f);
        badgeRect.anchoredPosition = Vector2.zero;
        badgeRect.sizeDelta = new Vector2(108f, 108f);

        Button button = root.GetComponent<Button>();
        button.targetGraphic = badgeImg;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.85f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        button.colors = colors;

        GameObject iconGO = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGO.transform.SetParent(badgeGO.transform, false);
        Image iconImg = iconGO.GetComponent<Image>();
        iconImg.sprite = BuildIconSprite(icon);
        iconImg.color = Color.white;
        iconImg.raycastTarget = false;
        RectTransform iconRect = iconGO.GetComponent<RectTransform>();
        iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = Vector2.zero;
        iconRect.sizeDelta = new Vector2(52f, 52f);

        TextMeshProUGUI labelText = CreateText(root.transform, "Label", label.ToUpperInvariant(), 18, FontStyles.Bold, BodyTextColor);
        labelText.alignment = TextAlignmentOptions.Top;
        RectTransform labelRect = labelText.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 0f);
        labelRect.pivot = new Vector2(0.5f, 0f);
        labelRect.anchoredPosition = new Vector2(0f, 4f);
        labelRect.sizeDelta = new Vector2(0f, 40f);

        // Same subtle hover animation the Main Menu's own buttons use -
        // still until the cursor is actually over it.
        root.AddComponent<ButtonHoverPulse>();

        return button;
    }

    private static Sprite BuildBadgeSprite(Color fill, Color ring)
    {
        const int size = 128;
        float outerR = size * 0.5f - 2f;
        float ringWidth = size * 0.055f;
        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);

        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                float d = dist - outerR;
                float alpha = Mathf.Clamp01(1f - (d + 1f));
                Color c = dist > outerR - ringWidth ? ring : fill;
                c.a *= alpha;
                tex.SetPixel(x, y, c);
            }
        }
        return MakeSprite(tex);
    }

    private static Sprite BuildIconSprite(MenuIcon icon)
    {
        const int size = 96;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x + 0.5f) / size * 2f - 1f;
                float ny = (y + 0.5f) / size * 2f - 1f;
                bool inside;
                switch (icon)
                {
                    case MenuIcon.Play: inside = InsideTriangle(nx, ny); break;
                    case MenuIcon.Controls: inside = InsideMouse(nx, ny); break;
                    case MenuIcon.Exit: inside = InsideArrow(nx, ny); break;
                    default: inside = false; break;
                }
                tex.SetPixel(x, y, inside ? Color.white : new Color(1f, 1f, 1f, 0f));
            }
        }
        return MakeSprite(tex);
    }

    private static bool InsideTriangle(float x, float y)
    {
        Vector2 a = new Vector2(-0.5f, -0.62f);
        Vector2 b = new Vector2(-0.5f, 0.62f);
        Vector2 c = new Vector2(0.62f, 0f);
        return SameSideAllEdges(x, y, a, b, c);
    }

    private static bool InsideMouse(float x, float y)
    {
        float ex = x / 0.36f;
        float ey = (y - 0.02f) / 0.62f;
        bool body = (ex * ex + ey * ey) <= 1f;
        bool verticalGap = Mathf.Abs(x) < 0.055f && y > 0.05f && y < 0.60f;
        bool horizontalNotch = y > 0.03f && y < 0.10f && Mathf.Abs(x) > 0.055f && Mathf.Abs(x) < 0.30f;
        return body && !verticalGap && !horizontalNotch;
    }

    private static bool InsideArrow(float x, float y)
    {
        bool stem = x >= -0.55f && x <= 0.05f && Mathf.Abs(y) < 0.16f;
        Vector2 a = new Vector2(0.02f, -0.42f);
        Vector2 b = new Vector2(0.02f, 0.42f);
        Vector2 c = new Vector2(0.62f, 0f);
        bool head = SameSideAllEdges(x, y, a, b, c);
        return stem || head;
    }

    private static bool SameSideAllEdges(float px, float py, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = EdgeSign(px, py, a, b);
        float d2 = EdgeSign(px, py, b, c);
        float d3 = EdgeSign(px, py, c, a);
        bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
        bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(hasNeg && hasPos);
    }

    private static float EdgeSign(float px, float py, Vector2 v1, Vector2 v2)
    {
        return (px - v2.x) * (v1.y - v2.y) - (v1.x - v2.x) * (py - v2.y);
    }

    // Plain rounded-rectangle track/fill sprite for the volume sliders -
    // generated in memory (Sprite.Create), not baked to a .png asset like
    // MainMenuSetup.cs's editor-time equivalent, since this needs to work
    // from a real build too.
    private static Sprite MakeRoundedTrackSprite()
    {
        const int w = 64, h = 32, r = 14;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float d = RoundedRectSdf(x, y, w, h, r);
                Color c = Color.white;
                c.a = d > 1f ? 0f : Mathf.Clamp01(1f - (d + 0.5f));
                tex.SetPixel(x, y, c);
            }
        }
        float b = r + 2;
        return MakeSprite(tex, new Vector4(b, b, b, b));
    }

    private static float RoundedRectSdf(int px, int py, int width, int height, int r)
    {
        float x = px + 0.5f - width * 0.5f;
        float y = py + 0.5f - height * 0.5f;
        float hw = width * 0.5f - r;
        float hh = height * 0.5f - r;
        float qx = Mathf.Abs(x) - hw;
        float qy = Mathf.Abs(y) - hh;
        float outsideDist = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
        float insideDist = Mathf.Min(Mathf.Max(qx, qy), 0f);
        return outsideDist + insideDist - r;
    }

    private static Sprite MakeSprite(Texture2D tex)
    {
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
    }

    private static Sprite MakeSprite(Texture2D tex, Vector4 border)
    {
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
    }

    private static Image CreatePanel(Transform parent, string name, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static TextMeshProUGUI CreateText(Transform parent, string name, string text, float fontSize,
        FontStyles style, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        tmp.ForceMeshUpdate();
        return tmp;
    }

    private static Button CreateButton(Transform parent, string name, string label, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        Image img = go.GetComponent<Image>();
        img.color = color;

        LayoutElement le = go.GetComponent<LayoutElement>();
        le.preferredHeight = 64f;
        le.preferredWidth = 420f;

        Button button = go.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.85f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        button.colors = colors;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(420f, 64f);

        TextMeshProUGUI text = CreateText(go.transform, "Label", label, 30, FontStyles.Bold, Color.white);
        Stretch(text.rectTransform);

        return button;
    }

    private static void ForceLayoutRebuild(GameObject root)
    {
        if (root == null) return;
        RectTransform rootRect = root.GetComponent<RectTransform>();

        LayoutGroup[] groups = root.GetComponentsInChildren<LayoutGroup>(true);
        System.Array.Sort(groups, (a, b) => Depth(b.transform).CompareTo(Depth(a.transform)));

        for (int pass = 0; pass < 2; pass++)
        {
            foreach (LayoutGroup g in groups)
            {
                RectTransform rt = g.transform as RectTransform;
                if (rt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            }
            if (rootRect != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rootRect);
            Canvas.ForceUpdateCanvases();
        }
    }

    private static int Depth(Transform t)
    {
        int d = 0;
        while (t != null) { d++; t = t.parent; }
        return d;
    }
}
