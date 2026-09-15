using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DoomClone.Automation
{
    /// <summary>
    /// Deterministic entry point for tests and tooling. Every operation calls the
    /// real production systems (GameManager, PlayerController, WeaponManager,
    /// WaveManager); nothing here simulates gameplay.
    /// </summary>
    public static class GameTestAPI
    {
        public const string LevelSceneName = "DoomClone_Level01";

        public const int WeaponChainsaw = 0;
        public const int WeaponPistol = 1;
        public const int WeaponShotgun = 2;
        public const int WeaponAssaultRifle = 3;

        public const string ScenarioFreshLevel = "fresh_level";

        /// <summary>Reloads the currently active level via the real GameManager restart path.</summary>
        public static void ResetGame()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.RestartGame();
                return;
            }

            SceneManager.LoadScene(LevelSceneName);
        }

        /// <summary>Boots a known clean scenario and waits until the game is ready.</summary>
        public static IEnumerator StartFreshLevel(float timeoutSeconds = 30f)
        {
            GameBootstrap.SetRandomSeed(0x5EED);
            if (SceneManager.GetActiveScene().name != LevelSceneName)
            {
                SceneManager.LoadScene(LevelSceneName);
            }

            yield return GameBootstrap.WaitForGameReady(timeoutSeconds);
        }

        public static IEnumerator StartScenario(string scenario, float timeoutSeconds = 30f)
        {
            if (scenario == ScenarioFreshLevel)
            {
                yield return StartFreshLevel(timeoutSeconds);
                yield break;
            }

            throw new System.ArgumentException("Unknown scenario: " + scenario, nameof(scenario));
        }

        public static PlayerController GetPlayer() => UnityEngine.Object.FindAnyObjectByType<PlayerController>();
        public static WeaponManager GetWeapons() => UnityEngine.Object.FindAnyObjectByType<WeaponManager>();

        public static void SetPlayerHealth(int health)
        {
            PlayerController player = GetPlayer();
            if (player != null) player.SetHealth(health);
        }

        public static void SetPlayerAmmo(int ammo)
        {
            WeaponManager weaponManager = GetWeapons();
            Weapon weapon = weaponManager != null ? weaponManager.GetCurrentWeapon() : null;
            if (weapon == null) return;

            weapon.SetAmmo(ammo);
        }

        public static void EquipWeapon(int index)
        {
            WeaponManager weaponManager = GetWeapons();
            weaponManager?.EquipWeapon(index);
        }

        /// <summary>Type aliases: "runner"|"zombie", "soldier"|"ranged", "brute"|"tank".</summary>
        public static GameObject SpawnEnemy(string type)
        {
            if (WaveManager.Instance == null)
            {
                Debug.LogWarning("[GameTestAPI] WaveManager not ready; cannot spawn enemy.");
                return null;
            }

            return WaveManager.Instance.SpawnEnemyByType(type);
        }

        public static void TeleportPlayer(Vector3 position)
        {
            PlayerController player = GetPlayer();
            if (player == null) return;

            CharacterController controller = player.GetComponent<CharacterController>();
            if (controller != null && controller.enabled)
            {
                controller.enabled = false;
                player.transform.position = position;
                controller.enabled = true;
            }
            else
            {
                player.transform.position = position;
            }
        }

        public static GameStateSnapshot GetStateSnapshot() => GameStateInspector.BuildSnapshot();
    }
}