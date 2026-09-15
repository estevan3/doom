using System;
using UnityEngine;

namespace DoomClone.Automation
{
    [Serializable]
    public class PlayerState
    {
        public string name;
        public bool alive;
        public int health;
        public int maxHealth;
        public float posX;
        public float posY;
        public float posZ;
    }

    [Serializable]
    public class WeaponState
    {
        public int index;
        public string name;
        public int ammo;
        public bool infinite;
    }

    [Serializable]
    public class WaveState
    {
        public bool gameActive;
        public int wave;
        public int trackedEnemiesAlive;
        public int livingEnemyCount;
        public bool waveInProgress;
        public int spawnPointCount;
    }

    [Serializable]
    public class EnemyState
    {
        public string name;
        public string type;
        public int health;
        public int maxHealth;
        public bool dead;
        public float posX;
        public float posY;
        public float posZ;
        public float distanceToPlayer;
    }

    [Serializable]
    public class GameStateSnapshot
    {
        public float capturedAt;
        public PlayerState player;
        public WeaponState weapon;
        public WaveState wave;
        public EnemyState[] enemies;

        public string ToJson() => JsonUtility.ToJson(this);
        public static GameStateSnapshot FromJson(string json) => JsonUtility.FromJson<GameStateSnapshot>(json);
    }

    /// <summary>Reads deterministic, parseable state from the real production systems.</summary>
    public static class GameStateInspector
    {
        public static GameStateSnapshot BuildSnapshot()
        {
            PlayerController player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
            WeaponManager weaponManager = UnityEngine.Object.FindAnyObjectByType<WeaponManager>();
            GameManager gameManager = GameManager.Instance;
            WaveManager waveManager = WaveManager.Instance;

            return new GameStateSnapshot
            {
                capturedAt = Time.time,
                player = BuildPlayer(player),
                weapon = BuildWeapon(weaponManager),
                wave = BuildWave(gameManager, waveManager),
                enemies = BuildEnemies(player)
            };
        }

        static PlayerState BuildPlayer(PlayerController player)
        {
            if (player == null)
            {
                return new PlayerState { name = "none", alive = false, health = 0, maxHealth = 0 };
            }

            Vector3 pos = player.transform.position;
            return new PlayerState
            {
                name = player.gameObject.name,
                alive = !player.IsDead(),
                health = player.currentHealth,
                maxHealth = player.maxHealth,
                posX = pos.x,
                posY = pos.y,
                posZ = pos.z
            };
        }

        static WeaponState BuildWeapon(WeaponManager weaponManager)
        {
            if (weaponManager == null || weaponManager.weapons == null || weaponManager.weapons.Length == 0)
            {
                return new WeaponState { index = -1, name = "none", ammo = 0, infinite = false };
            }

            Weapon weapon = weaponManager.GetCurrentWeapon();
            if (weapon == null)
            {
                return new WeaponState { index = weaponManager.currentWeaponIndex, name = "none", ammo = 0, infinite = false };
            }

            return new WeaponState
            {
                index = weaponManager.currentWeaponIndex,
                name = weapon.GetName(),
                ammo = weapon.GetAmmo(),
                infinite = !weapon.UsesAmmo()
            };
        }

        static WaveState BuildWave(GameManager gameManager, WaveManager waveManager)
        {
            if (waveManager == null)
            {
                return new WaveState { gameActive = gameManager != null && gameManager.gameActive };
            }

            return new WaveState
            {
                gameActive = gameManager != null && gameManager.gameActive,
                wave = waveManager.GetCurrentWave(),
                trackedEnemiesAlive = waveManager.GetEnemiesAlive(),
                livingEnemyCount = CountLivingEnemies(),
                waveInProgress = waveManager.IsWaveInProgress(),
                spawnPointCount = waveManager.spawnPoints != null ? waveManager.spawnPoints.Length : 0
            };
        }

        static int CountLivingEnemies()
        {
            Enemy[] enemies = UnityEngine.Object.FindObjectsByType<Enemy>();
            int count = 0;
            for (int i = 0; i < enemies.Length; i++)
            {
                if (!enemies[i].IsDead()) count++;
            }
            return count;
        }

        static EnemyState[] BuildEnemies(PlayerController player)
        {
            Enemy[] enemies = UnityEngine.Object.FindObjectsByType<Enemy>();
            EnemyState[] states = new EnemyState[enemies.Length];
            Vector3 playerPos = player != null ? player.transform.position : Vector3.zero;

            for (int i = 0; i < enemies.Length; i++)
            {
                Enemy enemy = enemies[i];
                Vector3 pos = enemy.transform.position;
                states[i] = new EnemyState
                {
                    name = enemy.gameObject.name,
                    type = enemy.GetType().Name,
                    health = enemy.GetHealth(),
                    maxHealth = enemy.GetMaxHealth(),
                    dead = enemy.IsDead(),
                    posX = pos.x,
                    posY = pos.y,
                    posZ = pos.z,
                    distanceToPlayer = Vector3.Distance(pos, playerPos)
                };
            }

            return states;
        }
    }
}