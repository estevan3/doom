using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenu : MonoBehaviour
{
    private const string SensitivityKey = "MouseSensitivity";
    private const string LevelSceneName = "DoomClone_Level01";

    private Font uiFont;
    private Text sensitivityValueText;
    private Slider sensitivitySlider;
    private GameObject settingsPanel;

    void Start()
    {
        uiFont = LoadFont();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        EnsureEventSystem();
        CreateUI();
        Debug.Log("[MainMenu] UI criado");
        if (HasArg("-automationAutoStart"))
        {
            Debug.Log("[MainMenu] Automacao: auto-start do nivel");
            StartCoroutine(AutoStart());
        }
    }

    static bool HasArg(string flag)
    {
        return System.Array.Exists(System.Environment.GetCommandLineArgs(), a => a == flag);
    }

    IEnumerator AutoStart()
    {
        yield return null;
        StartGame();
    }

    Font LoadFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null) return font;
        return Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null) return;

        GameObject esObj = new GameObject("EventSystem");
        esObj.AddComponent<EventSystem>();
        esObj.AddComponent<InputSystemUIInputModule>();
    }

    void CreateUI()
    {
        GameObject canvasObj = new GameObject("MenuCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasObj.AddComponent<GraphicRaycaster>();

        CreateTitle(canvasObj.transform);
        CreateButton(canvasObj.transform, "StartButton", new Vector2(0, 40), new Vector2(320, 70), "INICIAR", () => StartGame());
        CreateButton(canvasObj.transform, "SettingsButton", new Vector2(0, -60), new Vector2(320, 70), "CONFIGURACOES", () => OpenSettings());
        CreateFooter(canvasObj.transform);

        CreateSettingsPanel(canvasObj.transform);
    }

    void CreateTitle(Transform parent)
    {
        GameObject titleObj = new GameObject("TitleText");
        titleObj.transform.SetParent(parent, false);
        RectTransform rect = titleObj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0, -120);
        rect.sizeDelta = new Vector2(800, 120);
        Text t = titleObj.AddComponent<Text>();
        t.text = "DOOM CLONE";
        t.fontSize = 84;
        t.fontStyle = FontStyle.Bold;
        t.color = new Color(0.9f, 0.2f, 0.2f);
        t.alignment = TextAnchor.MiddleCenter;
        if (uiFont != null) t.font = uiFont;
    }

    void CreateFooter(Transform parent)
    {
        GameObject footerObj = new GameObject("FooterText");
        footerObj.transform.SetParent(parent, false);
        RectTransform rect = footerObj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0, 20);
        rect.sizeDelta = new Vector2(800, 40);
        Text t = footerObj.AddComponent<Text>();
        t.text = "DOOM CLONE - v0.1";
        t.fontSize = 22;
        t.color = new Color(0.6f, 0.6f, 0.6f, 1f);
        t.alignment = TextAnchor.MiddleCenter;
        if (uiFont != null) t.font = uiFont;
    }

    void CreateButton(Transform parent, string name, Vector2 position, Vector2 size, string label, UnityEngine.Events.UnityAction onClick)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        Image img = obj.AddComponent<Image>();
        img.color = new Color(0.35f, 0.15f, 0.15f, 1f);
        Button btn = obj.AddComponent<Button>();
        btn.targetGraphic = img;

        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(obj.transform, false);
        RectTransform labelRect = labelObj.AddComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        Text labelText = labelObj.AddComponent<Text>();
        labelText.text = label;
        labelText.fontSize = 32;
        labelText.fontStyle = FontStyle.Bold;
        labelText.color = Color.white;
        labelText.alignment = TextAnchor.MiddleCenter;
        if (uiFont != null) labelText.font = uiFont;

        btn.onClick.AddListener(onClick);
    }

    void CreateSettingsPanel(Transform parent)
    {
        settingsPanel = new GameObject("SettingsPanel");
        settingsPanel.transform.SetParent(parent, false);
        RectTransform panelRect = settingsPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(600, 360);
        Image bg = settingsPanel.AddComponent<Image>();
        bg.color = new Color(0.1f, 0.1f, 0.12f, 0.98f);

        Text title = CreatePanelLabel(settingsPanel.transform, "SettingsTitle", new Vector2(0, 130), new Vector2(560, 60), 40, Color.white, "CONFIGURACOES");
        Text label = CreatePanelLabel(settingsPanel.transform, "SensitivityLabel", new Vector2(0, 55), new Vector2(560, 40), 26, Color.yellow, "Sensibilidade do mouse");

        CreateSensitivitySlider(settingsPanel.transform);

        CreateButton(settingsPanel.transform, "BackButton", new Vector2(0, -120), new Vector2(240, 60), "VOLTAR", () => CloseSettings());
        settingsPanel.SetActive(false);
    }

    Text CreatePanelLabel(Transform parent, string name, Vector2 position, Vector2 size, int fontSize, Color color, string text)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        Text t = obj.AddComponent<Text>();
        t.text = text;
        t.fontSize = fontSize;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        if (uiFont != null) t.font = uiFont;
        return t;
    }

    void CreateSensitivitySlider(Transform parent)
    {
        GameObject sliderObj = new GameObject("SensitivitySlider");
        sliderObj.transform.SetParent(parent, false);
        RectTransform rect = sliderObj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0, -10);
        rect.sizeDelta = new Vector2(420, 40);
        Image bg = sliderObj.AddComponent<Image>();
        bg.color = new Color(0.25f, 0.25f, 0.25f, 1f);

        sensitivitySlider = sliderObj.AddComponent<Slider>();

        GameObject fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderObj.transform, false);
        RectTransform fillAreaRect = fillArea.AddComponent<RectTransform>();
        fillAreaRect.anchorMin = Vector2.zero;
        fillAreaRect.anchorMax = Vector2.one;
        fillAreaRect.offsetMin = new Vector2(10, 5);
        fillAreaRect.offsetMax = new Vector2(-10, -5);

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(fillArea.transform, false);
        RectTransform fillRect = fill.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        Image fillImg = fill.AddComponent<Image>();
        fillImg.color = new Color(0.8f, 0.2f, 0.2f, 1f);
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        sensitivitySlider.fillRect = fillRect;

        GameObject fillArea2 = new GameObject("Handle Slide Area");
        fillArea2.transform.SetParent(sliderObj.transform, false);
        RectTransform fillArea2Rect = fillArea2.AddComponent<RectTransform>();
        fillArea2Rect.anchorMin = Vector2.zero;
        fillArea2Rect.anchorMax = Vector2.one;
        fillArea2Rect.offsetMin = new Vector2(10, 5);
        fillArea2Rect.offsetMax = new Vector2(-10, -5);

        GameObject handle = new GameObject("Handle");
        handle.transform.SetParent(fillArea2.transform, false);
        RectTransform handleRect = handle.AddComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(36, 36);
        Image handleImg = handle.AddComponent<Image>();
        handleImg.color = Color.white;
        sensitivitySlider.handleRect = handleRect;

        float defaultValue = PlayerPrefs.GetFloat(SensitivityKey, 1f);
        sensitivitySlider.minValue = 0.1f;
        sensitivitySlider.maxValue = 5f;
        sensitivitySlider.value = defaultValue;
        sensitivitySlider.onValueChanged.AddListener(OnSensitivityChanged);
        sensitivitySlider.gameObject.SetActive(true);

        CreateSensitivityValue(parent, defaultValue);
    }

    void CreateSensitivityValue(Transform parent, float value)
    {
        GameObject valueObj = new GameObject("SensitivityValue");
        valueObj.transform.SetParent(parent, false);
        RectTransform rect = valueObj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(230, -10);
        rect.sizeDelta = new Vector2(100, 40);
        Text t = valueObj.AddComponent<Text>();
        sensitivityValueText = t;
        t.fontSize = 26;
        t.color = Color.cyan;
        t.alignment = TextAnchor.MiddleLeft;
        t.text = value.ToString("F1");
        if (uiFont != null) t.font = uiFont;
    }

    void OnSensitivityChanged(float value)
    {
        if (sensitivityValueText != null)
            sensitivityValueText.text = value.ToString("F1");
        PlayerPrefs.SetFloat(SensitivityKey, value);
        PlayerPrefs.Save();
    }

    void OpenSettings()
    {
        if (settingsPanel != null) settingsPanel.SetActive(true);
    }

    void CloseSettings()
    {
        if (settingsPanel != null) settingsPanel.SetActive(false);
    }

    void StartGame()
    {
        Debug.Log("[MainMenu] INICIAR clicado - carregando " + LevelSceneName);
        SceneManager.LoadScene(LevelSceneName);
    }
}
