using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

public class WaveManager : MonoBehaviour
{
    public static WaveManager Instance;

    [Header("Wave Settings")]
    public float initialDelay = 2f;
    public float cooldownBetweenWaves = 5f;
    public int baseEnemiesPerWave = 5;
    public float enemyScalePerWave = 1.3f;

    [Header("Spawn Points")]
    public Transform[] spawnPoints;

    [Header("Enemy Prefabs")]
    public GameObject zombiePrefab;
    public GameObject soldierPrefab;
    public GameObject brutePrefab;

    private int currentWave = 0;
    private int enemiesAlive = 0;
    private bool waveInProgress = false;
    private bool gameActive = true;
    private PlayerHUD hud;
    private PlayerController playerController;

    private List<Enemy> activeEnemies = new List<Enemy>();

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
        hud = FindObjectOfType<PlayerHUD>();
        playerController = FindObjectOfType<PlayerController>();

        if (playerController != null)
        {
            playerController.OnPlayerDeath += OnPlayerDeath;
        }

        CreateEnemyPrefabs();
        FindSpawnPoints();

        StartCoroutine(StartFirstWave());
    }

    void CreateEnemyPrefabs()
    {
        zombiePrefab = CreateEnemyPrefab("ZombieRunner", typeof(ZombieRunner));
        soldierPrefab = CreateEnemyPrefab("RangedSoldier", typeof(RangedSoldier));
        brutePrefab = CreateEnemyPrefab("TankBrute", typeof(TankBrute));
    }

    GameObject CreateEnemyPrefab(string name, System.Type enemyType)
    {
        GameObject prefab = new GameObject(name + "_Prefab");
        prefab.SetActive(false);

        CapsuleCollider col = prefab.AddComponent<CapsuleCollider>();
        col.height = 2f;
        col.radius = 0.5f;

        MeshRenderer rend = prefab.AddComponent<MeshRenderer>();
        Shader shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader != null)
            rend.material = new Material(shader);
        else
            rend.material = new Material(Shader.Find("Unlit/Color"));

        prefab.AddComponent<NavMeshAgent>();

        if (enemyType == typeof(ZombieRunner))
            prefab.AddComponent<ZombieRunner>();
        else if (enemyType == typeof(RangedSoldier))
            prefab.AddComponent<RangedSoldier>();
        else if (enemyType == typeof(TankBrute))
            prefab.AddComponent<TankBrute>();

        return prefab;
    }

    void FindSpawnPoints()
    {
        List<Transform> points = new List<Transform>();
        foreach (GameObject obj in FindObjectsOfType<GameObject>())
        {
            if (obj.name.Contains("EnemySpawnPoint"))
            {
                points.Add(obj.transform);
            }
        }
        spawnPoints = points.ToArray();
    }

    IEnumerator StartFirstWave()
    {
        yield return new WaitForSeconds(initialDelay);
        StartNextWave();
    }

    void StartNextWave()
    {
        if (!gameActive) return;

        currentWave++;
        waveInProgress = true;

        if (hud != null)
        {
            hud.UpdateWave(currentWave);
            hud.HideCountdown();
        }

        int enemyCount = Mathf.RoundToInt(baseEnemiesPerWave * Mathf.Pow(enemyScalePerWave, currentWave - 1));

        StartCoroutine(SpawnWave(enemyCount));
    }

    IEnumerator SpawnWave(int enemyCount)
    {
        int zombies = Mathf.RoundToInt(enemyCount * GetZombieRatio());
        int soldiers = Mathf.RoundToInt(enemyCount * GetSoldierRatio());
        int brutes = Mathf.RoundToInt(enemyCount * GetBruteRatio());

        int total = zombies + soldiers + brutes;
        while (total < enemyCount)
        {
            zombies++;
            total++;
        }

        for (int i = 0; i < zombies; i++)
        {
            SpawnEnemy(zombiePrefab);
            yield return new WaitForSeconds(0.3f);
        }

        for (int i = 0; i < soldiers; i++)
        {
            SpawnEnemy(soldierPrefab);
            yield return new WaitForSeconds(0.3f);
        }

        for (int i = 0; i < brutes; i++)
        {
            SpawnEnemy(brutePrefab);
            yield return new WaitForSeconds(0.5f);
        }

        enemiesAlive = enemyCount;
    }

    float GetZombieRatio()
    {
        if (currentWave <= 3) return 0.7f;
        if (currentWave <= 6) return 0.5f;
        return 0.3f;
    }

    float GetSoldierRatio()
    {
        if (currentWave <= 3) return 0.3f;
        if (currentWave <= 6) return 0.4f;
        return 0.4f;
    }

    float GetBruteRatio()
    {
        if (currentWave <= 3) return 0f;
        if (currentWave <= 6) return 0.1f;
        return 0.3f;
    }

    void SpawnEnemy(GameObject prefab)
    {
        if (spawnPoints.Length == 0) return;

        Transform spawnPoint = spawnPoints[Random.Range(0, spawnPoints.Length)];

        GameObject enemyObj = Instantiate(prefab, spawnPoint.position, spawnPoint.rotation);
        enemyObj.SetActive(true);

        NavMeshAgent agent = enemyObj.GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.enabled = false;
            StartCoroutine(EnableNavMeshAgent(enemyObj, agent));
        }

        Enemy enemy = enemyObj.GetComponent<Enemy>();
        if (enemy != null)
        {
            activeEnemies.Add(enemy);
            enemy.OnEnemyDeath += OnEnemyDeath;
        }
    }

    IEnumerator EnableNavMeshAgent(GameObject enemyObj, NavMeshAgent agent)
    {
        yield return new WaitForSeconds(0.1f);
        if (enemyObj != null && agent != null)
        {
            agent.enabled = true;
        }
    }

    void OnEnemyDeath(Enemy enemy)
    {
        enemiesAlive--;
        activeEnemies.Remove(enemy);
        enemy.OnEnemyDeath -= OnEnemyDeath;

        if (enemiesAlive <= 0 && waveInProgress)
        {
            waveInProgress = false;
            StartCoroutine(CooldownBetweenWaves());
        }
    }

    IEnumerator CooldownBetweenWaves()
    {
        if (!gameActive) yield break;

        int countdown = (int)cooldownBetweenWaves;

        for (int i = countdown; i > 0; i--)
        {
            if (hud != null)
            {
                hud.ShowCountdown(i);
            }
            yield return new WaitForSeconds(1f);
        }

        if (hud != null)
        {
            hud.HideCountdown();
        }

        StartNextWave();
    }

    void OnPlayerDeath()
    {
        gameActive = false;
        StopAllCoroutines();

        if (hud != null)
        {
            hud.ShowGameOver(currentWave);
        }

        foreach (Enemy enemy in activeEnemies)
        {
            if (enemy != null && !enemy.IsDead())
            {
                enemy.enabled = false;
                NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
                if (agent != null) agent.enabled = false;
            }
        }
    }

    public int GetCurrentWave() => currentWave;
    public int GetEnemiesAlive() => enemiesAlive;
    public bool IsWaveInProgress() => waveInProgress;
}
