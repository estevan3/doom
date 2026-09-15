using System.Collections;
using System.IO;
using DoomClone.Automation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DoomClone.Tests.PlayMode
{
    /// <summary>
    /// Minimal runtime smoke/acceptance fixtures. They load the real level and exercise
    /// the production pipeline through the GameTestAPI — this is the foundation that
    /// later milestones build game-specific PlayMode coverage on.
    /// </summary>
    public class AutomationSmokeTests
    {
        const string Level = GameTestAPI.LevelSceneName;

        static readonly string TestResultsDir =
            Path.Combine(Directory.GetCurrentDirectory(), "TestResults");

        [UnityTest]
        public IEnumerator Level01_Boots_PlayerWaveAndSpawnPointsReady()
        {
            yield return GameTestAPI.StartFreshLevel(30f);

            GameManager gameManager = GameManager.Instance;
            Assert.IsNotNull(gameManager, "GameManager should exist after boot.");
            Assert.IsTrue(gameManager.gameActive, "Game should be active after boot.");

            PlayerController player = GameTestAPI.GetPlayer();
            Assert.IsNotNull(player, "Player should exist after boot.");
            Assert.IsFalse(player.IsDead(), "Player should be alive after boot.");
            Assert.AreEqual(player.maxHealth, player.currentHealth,
                "Player health should start at max (spec: 100 HP).");
            Assert.IsNotNull(player.GetComponent<CharacterController>(),
                "Player needs a CharacterController.");
            Assert.IsNotNull(player.playerCamera, "Player needs a first-person camera.");

            WaveManager waveManager = WaveManager.Instance;
            Assert.IsNotNull(waveManager, "WaveManager should exist after boot.");
            Assert.GreaterOrEqual(waveManager.spawnPoints.Length, 1,
                "Level should expose at least one EnemySpawnPoint.");

            // Spec: Wave 1 spawns after the 2s initial delay.
            yield return GameBootstrap.WaitUntil(() => waveManager.GetCurrentWave() >= 1, 10f);
            Assert.GreaterOrEqual(waveManager.GetCurrentWave(), 1,
                "Wave 1 should start after the initial delay.");

            Debug.Log("[Smoke] Level booted, wave " + waveManager.GetCurrentWave() +
                      "; snapshot:\n" + GameTestAPI.GetStateSnapshot().ToJson());
        }

        [UnityTest]
        public IEnumerator AutomationApi_ResetInspectSnapshotScreenshot_Work()
        {
            yield return GameTestAPI.StartFreshLevel(30f);

            // Deterministic player state mutation via the real systems.
            GameTestAPI.SetPlayerHealth(42);
            PlayerController player = GameTestAPI.GetPlayer();
            Assert.IsNotNull(player, "Player should exist.");
            Assert.AreEqual(42, player.currentHealth, "SetPlayerHealth should take effect.");

            GameTestAPI.EquipWeapon(GameTestAPI.WeaponShotgun);
            WeaponManager weaponManager = GameTestAPI.GetWeapons();
            Assert.IsNotNull(weaponManager, "WeaponManager should exist.");
            Assert.AreEqual(GameTestAPI.WeaponShotgun, weaponManager.currentWeaponIndex,
                "EquipWeapon should select the requested weapon.");
            Assert.AreEqual("Shotgun", weaponManager.GetCurrentWeapon().GetName());

            GameTestAPI.SetPlayerAmmo(6);
            Assert.AreEqual(6, weaponManager.GetCurrentWeapon().GetAmmo(),
                "SetPlayerAmmo should take effect.");

            Vector3 target = player.transform.position + Vector3.right * 2f;
            GameTestAPI.TeleportPlayer(target);
            Assert.Less(Vector3.Distance(player.transform.position, target), 0.5f,
                "TeleportPlayer should move the player to the requested position.");

            GameObject spawned = GameTestAPI.SpawnEnemy("runner");
            Assert.IsNotNull(spawned, "SpawnEnemy(runner) should instantiate an enemy.");
            Assert.IsNotNull(spawned.GetComponent<ZombieRunner>(),
                "SpawnEnemy(runner) should yield a ZombieRunner.");

            yield return new WaitForSeconds(0.5f);

            GameStateSnapshot snapshot = GameTestAPI.GetStateSnapshot();
            Assert.IsNotNull(snapshot.player, "Snapshot must include player state.");
            Assert.IsNotNull(snapshot.weapon, "Snapshot must include weapon state.");
            Assert.IsNotNull(snapshot.wave, "Snapshot must include wave state.");
            Assert.IsNotNull(snapshot.enemies, "Snapshot must include enemy state.");
            Assert.GreaterOrEqual(snapshot.enemies.Length, 1,
                "Snapshot must observe at least the spawned enemy.");
            Assert.AreEqual(42, snapshot.player.health, "Snapshot must reflect player health.");

            Debug.Log("[Automation] snapshot:\n" + snapshot.ToJson());

            string screenshotPath = Path.Combine(TestResultsDir, "automation_acceptance.png");
            string saved = ScreenshotCapture.CaptureToFile(screenshotPath, player.playerCamera);
            Assert.IsNotNull(saved, "Screenshot capture should return a file path.");
            Assert.IsTrue(File.Exists(saved), "Screenshot file should exist on disk.");
            Assert.That(new FileInfo(saved).Length, Is.GreaterThan(0),
                "Screenshot file must not be empty.");

            Debug.Log("[Automation] screenshot saved: " + saved);

            GameTestAPI.ResetGame();
            yield return GameBootstrap.WaitForGameReady(30f);
            Assert.IsNotNull(GameTestAPI.GetPlayer(), "Level should restart into a clean state.");
        }
    }
}