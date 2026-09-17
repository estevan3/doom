using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("Game State")]
    public bool gameActive = false;
    public int currentWave = 0;

    private PlayerController playerController;
    private WeaponManager weaponManager;
    private PlayerHUD hud;
    private WaveManager waveManager;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        NavMeshSetup nav = gameObject.GetComponent<NavMeshSetup>();
        if (nav == null)
        {
            nav = gameObject.AddComponent<NavMeshSetup>();
        }

        if (PlayerInputActions.Instance == null)
        {
            GameObject inputObj = new GameObject("PlayerInputActions");
            inputObj.AddComponent<PlayerInputActions>();
        }

        SetupPlayer();
        SetupGame();
        Debug.Log("[GameManager] Nivel iniciado - gameActive=" + gameActive);
    }

    void SetupPlayer()
    {
        GameObject playerSpawn = GameObject.Find("PlayerSpawnPoint");
        Vector3 spawnPos = playerSpawn != null ? playerSpawn.transform.position : Vector3.zero;

        GameObject playerObj = new GameObject("Player");
        playerObj.tag = "Player";
        playerObj.transform.position = spawnPos;

        playerController = playerObj.AddComponent<PlayerController>();
        weaponManager = playerObj.AddComponent<WeaponManager>();
        hud = gameObject.AddComponent<PlayerHUD>();
    }

    void SetupGame()
    {
        gameActive = true;
        currentWave = 0;

        waveManager = gameObject.AddComponent<WaveManager>();
    }

    void Update()
    {
        if (!gameActive) return;

        if (playerController != null && playerController.IsDead())
        {
            gameActive = false;
        }
    }

    public void RestartGame()
    {
        Debug.Log("[GameManager] Reiniciando nivel - " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    }
}
