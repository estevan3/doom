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
    public float navMeshSnapRadius = 3f;

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
        hud = FindAnyObjectByType<PlayerHUD>();
        playerController = FindAnyObjectByType<PlayerController>();

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

        GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        tmp.SetActive(false);
        Mesh capsuleMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(tmp);

        MeshFilter meshFilter = prefab.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = capsuleMesh;

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
        foreach (GameObject obj in FindObjectsByType<GameObject>())
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
        if (spawnPoints.Length == 0)
        {
            Debug.LogError("WaveManager: nenhum EnemySpawnPoint encontrado. Onda ignorada.");
            yield return new WaitForSeconds(1f);
            waveInProgress = false;
            StartCoroutine(CooldownBetweenWaves());
            yield break;
        }

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

        enemiesAlive = total;
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

    public GameObject SpawnEnemyByType(string type)
    {
        if (!gameActive) return null;
        if (spawnPoints.Length == 0) return null;

        GameObject prefab = GetPrefabByType(type);
        if (prefab == null) return null;

        return SpawnEnemyInstance(prefab, trackInWave: false);
    }

    GameObject GetPrefabByType(string type)
    {
        if (string.IsNullOrEmpty(type)) return null;

        switch (type.Trim().ToLowerInvariant())
        {
            case "runner":
            case "zombie":
            case "zombierunner":
                return zombiePrefab;

            case "soldier":
            case "ranged":
            case "rangedsoldier":
                return soldierPrefab;

            case "brute":
            case "tank":
            case "tankbrute":
                return brutePrefab;

            default:
                return null;
        }
    }

    void SpawnEnemy(GameObject prefab)
    {
        SpawnEnemyInstance(prefab, trackInWave: true);
    }

    GameObject SpawnEnemyInstance(GameObject prefab, bool trackInWave)
    {
        if (spawnPoints.Length == 0) return null;

        Transform spawnPoint = spawnPoints[Random.Range(0, spawnPoints.Length)];

        // EnemySpawnPoint markers are authoring floats above the walkable surface, so snap
        // the spawn onto the baked NavMesh. Spawning the transform ON the NavMesh guarantees
        // the agent places cleanly when enabled and SetDestination never throws.
        Vector3 spawnPosition = NavMeshUtil.SnapToNavMesh(spawnPoint.position, navMeshSnapRadius);

        GameObject enemyObj = Instantiate(prefab, spawnPosition, spawnPoint.rotation);
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
            enemy.OnEnemyDeath += trackInWave ? OnEnemyDeath : OnAuxEnemyDeath;
        }

        return enemyObj;
    }

    void OnAuxEnemyDeath(Enemy enemy)
    {
        activeEnemies.Remove(enemy);
        enemy.OnEnemyDeath -= OnAuxEnemyDeath;
    }

    IEnumerator EnableNavMeshAgent(GameObject enemyObj, NavMeshAgent agent)
    {
        yield return new WaitForSeconds(0.1f);
        if (enemyObj == null || agent == null) yield break;

        // Wait until the runtime NavMesh is actually baked. Spawns can occur before the
        // async bake finishes; enabling an agent then would leave it unplaced and every
        // HandleBehavior SetDestination would throw.
        const float bakeWaitTimeout = 10f;
        float waitDeadline = Time.time + bakeWaitTimeout;
        while (!NavMeshUtil.IsBaked() && Time.time < waitDeadline)
        {
            yield return null;
        }

        if (enemyObj == null || agent == null) yield break;
        if (!NavMeshUtil.IsBaked())
        {
            agent.enabled = false;
            yield break;
        }

        // Re-snap the transform onto the baked NavMesh while the agent is still disabled.
        // If no walkable point is found near the spawn, keep the agent disabled so it never
        // issues SetDestination from an unplaced state.
        if (!NavMeshUtil.TrySnapToNavMesh(enemyObj.transform.position, navMeshSnapRadius, out Vector3 snapped))
        {
            agent.enabled = false;
            yield break;
        }

        enemyObj.transform.position = snapped;

        if (agent.enabled) yield break;
        agent.enabled = true;
        yield return null;

        // Defense in depth: force placement with Warp so isOnNavMesh is reliably true
        // before any HandleBehavior runs. Disable the agent if Warp still cannot place it.
        if (agent.isOnNavMesh == false)
        {
            if (agent.Warp(snapped) == false)
            {
                agent.enabled = false;
            }
            yield return null;
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
        waveInProgress = false;
        StopAllCoroutines();

        if (hud != null)
        {
            // The cooldown coroutine may be mid-countdown when the player dies; its frozen
            // "Next wave in: N..." panel must never linger behind the Game Over screen.
            hud.HideCountdown();
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
