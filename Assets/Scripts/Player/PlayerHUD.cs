using UnityEngine;
using UnityEngine.UI;

public class PlayerHUD : MonoBehaviour
{
    [Header("UI References")]
    public Text healthText;
    public Text weaponText;
    public Text ammoText;
    public Text waveText;
    public Text waveCountdownText;
    public Text gameOverText;
    public Text waveSurvivedText;

    private PlayerController playerController;
    private WeaponManager weaponManager;
    private Font uiFont;

    void Start()
    {
        playerController = FindObjectOfType<PlayerController>();
        weaponManager = FindObjectOfType<WeaponManager>();

        uiFont = LoadFont();

        if (playerController != null)
        {
            playerController.OnHealthChanged += UpdateHealth;
            playerController.OnPlayerDeath += ShowGameOver;
        }

        CreateHUD();
    }

    Font LoadFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null) return font;
        font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return font;
    }

    void CreateHUD()
    {
        GameObject canvasObj = new GameObject("HUDCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasObj.AddComponent<GraphicRaycaster>();

        CreateBackground("HealthBG", canvasObj.transform, new Vector2(10, -10), new Vector2(200, 40));
        healthText = CreateText("HealthText", canvasObj.transform, new Vector2(15, -15), new Vector2(190, 30), 20, Color.red, TextAnchor.MiddleLeft, "HP: 100/100");

        CreateBackground("WeaponBG", canvasObj.transform, new Vector2(10, -55), new Vector2(200, 30));
        weaponText = CreateText("WeaponText", canvasObj.transform, new Vector2(15, -55), new Vector2(190, 25), 16, Color.white, TextAnchor.MiddleLeft, "Weapon: Pistol");

        CreateBackground("AmmoBG", canvasObj.transform, new Vector2(10, -90), new Vector2(200, 30));
        ammoText = CreateText("AmmoText", canvasObj.transform, new Vector2(15, -90), new Vector2(190, 25), 16, Color.yellow, TextAnchor.MiddleLeft, "Ammo: Infinite");

        GameObject waveBg = CreateBackground("WaveBG", canvasObj.transform, new Vector2(-100, -10), new Vector2(200, 40));
        waveBg.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 1);
        waveBg.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 1);
        GameObject waveObj = CreateTextObject("WaveText", canvasObj.transform, new Vector2(-95, -15), new Vector2(190, 30));
        waveObj.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 1);
        waveObj.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 1);
        waveText = waveObj.GetComponent<Text>();
        waveText.fontSize = 22;
        waveText.color = Color.white;
        waveText.alignment = TextAnchor.MiddleCenter;
        waveText.text = "Wave: 1";
        if (uiFont != null) waveText.font = uiFont;

        GameObject countdownBg = CreateBackground("CountdownBG", canvasObj.transform, new Vector2(-100, 50), new Vector2(200, 50));
        countdownBg.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 0.5f);
        countdownBg.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.5f);
        countdownBg.SetActive(false);
        GameObject countdownObj = CreateTextObject("CountdownText", canvasObj.transform, new Vector2(-100, 50), new Vector2(200, 50));
        countdownObj.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 0.5f);
        countdownObj.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.5f);
        waveCountdownText = countdownObj.GetComponent<Text>();
        waveCountdownText.fontSize = 28;
        waveCountdownText.color = Color.cyan;
        waveCountdownText.alignment = TextAnchor.MiddleCenter;
        waveCountdownText.text = "";
        if (uiFont != null) waveCountdownText.font = uiFont;
        countdownObj.SetActive(false);

        GameObject gameOverBg = CreateBackground("GameOverBG", canvasObj.transform, Vector2.zero, new Vector2(400, 200));
        gameOverBg.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 0.5f);
        gameOverBg.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.5f);
        gameOverBg.GetComponent<Image>().color = new Color(0.5f, 0, 0, 0.9f);
        gameOverBg.SetActive(false);

        GameObject gameOverObj = CreateTextObject("GameOverText", canvasObj.transform, new Vector2(-195, 20), new Vector2(390, 60));
        gameOverObj.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 0.5f);
        gameOverObj.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.5f);
        gameOverText = gameOverObj.GetComponent<Text>();
        gameOverText.fontSize = 40;
        gameOverText.color = Color.red;
        gameOverText.alignment = TextAnchor.MiddleCenter;
        gameOverText.text = "GAME OVER";
        if (uiFont != null) gameOverText.font = uiFont;
        gameOverObj.SetActive(false);

        GameObject waveSurvivedObj = CreateTextObject("WaveSurvivedText", canvasObj.transform, new Vector2(-195, -30), new Vector2(390, 40));
        waveSurvivedObj.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 0.5f);
        waveSurvivedObj.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.5f);
        waveSurvivedText = waveSurvivedObj.GetComponent<Text>();
        waveSurvivedText.fontSize = 24;
        waveSurvivedText.color = Color.white;
        waveSurvivedText.alignment = TextAnchor.MiddleCenter;
        waveSurvivedText.text = "";
        if (uiFont != null) waveSurvivedText.font = uiFont;
        waveSurvivedObj.SetActive(false);

        GameObject crosshair = CreateBackground("Crosshair", canvasObj.transform, new Vector2(-8, -8), new Vector2(16, 16));
        crosshair.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 0.5f);
        crosshair.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.5f);
        crosshair.GetComponent<Image>().color = Color.white;
    }

    GameObject CreateBackground(string name, Transform parent, Vector2 position, Vector2 size)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        Image img = obj.AddComponent<Image>();
        img.color = new Color(0, 0, 0, 0.7f);
        return obj;
    }

    GameObject CreateTextObject(string name, Transform parent, Vector2 position, Vector2 size)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        Text t = obj.AddComponent<Text>();
        if (uiFont != null) t.font = uiFont;
        return obj;
    }

    Text CreateText(string name, Transform parent, Vector2 position, Vector2 size, int fontSize, Color color, TextAnchor alignment, string text)
    {
        GameObject obj = CreateTextObject(name, parent, position, size);
        Text t = obj.GetComponent<Text>();
        t.fontSize = fontSize;
        t.color = color;
        t.alignment = alignment;
        t.text = text;
        if (uiFont != null) t.font = uiFont;
        return t;
    }

    public void UpdateHealth(int current, int max)
    {
        if (healthText != null)
            healthText.text = $"HP: {current}/{max}";
    }

    public void UpdateWeapon(string weaponName, int ammo, bool infinite)
    {
        if (weaponText != null)
            weaponText.text = $"Weapon: {weaponName}";
        if (ammoText != null)
            ammoText.text = infinite ? "Ammo: Infinite" : $"Ammo: {ammo}";
    }

    public void UpdateWave(int wave)
    {
        if (waveText != null)
            waveText.text = $"Wave: {wave}";
    }

    public void ShowCountdown(int seconds)
    {
        GameObject countdownBg = GameObject.Find("CountdownBG");
        GameObject countdownObj = GameObject.Find("CountdownText");
        if (countdownBg != null) countdownBg.SetActive(true);
        if (countdownObj != null) countdownObj.SetActive(true);
        if (waveCountdownText != null)
            waveCountdownText.text = $"Next wave in: {seconds}...";
    }

    public void HideCountdown()
    {
        GameObject countdownBg = GameObject.Find("CountdownBG");
        GameObject countdownObj = GameObject.Find("CountdownText");
        if (countdownBg != null) countdownBg.SetActive(false);
        if (countdownObj != null) countdownObj.SetActive(false);
    }

    public void ShowGameOver(int wavesSurvived)
    {
        GameObject gameOverBg = GameObject.Find("GameOverBG");
        if (gameOverBg != null) gameOverBg.SetActive(true);
        if (gameOverText != null) gameOverText.gameObject.SetActive(true);
        if (waveSurvivedText != null)
        {
            waveSurvivedText.gameObject.SetActive(true);
            waveSurvivedText.text = $"Survived {wavesSurvived} waves";
        }
    }

    void ShowGameOver()
    {
        ShowGameOver(0);
    }

    void OnDestroy()
    {
        if (playerController != null)
        {
            playerController.OnHealthChanged -= UpdateHealth;
            playerController.OnPlayerDeath -= ShowGameOver;
        }
    }
}
