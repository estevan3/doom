using DoomClone.Automation;
using NUnit.Framework;

namespace DoomClone.Tests.EditMode
{
    /// <summary>
    /// Deterministic checks that do not need a running scene: assembly wiring and the
    /// parseable snapshot contract used by GameStateInspector.
    /// </summary>
    public class EditModeAutomationTests
    {
        [Test]
        public void ProductionTypes_LiveInExpectedRuntimeAssembly()
        {
            Assert.AreEqual("DoomClone.Runtime", typeof(GameManager).Assembly.GetName().Name,
                "GameManager must live in the production runtime assembly.");
            Assert.AreEqual("DoomClone.Runtime", typeof(WaveManager).Assembly.GetName().Name,
                "WaveManager must live in the production runtime assembly.");
            Assert.AreEqual("DoomClone.Automation", typeof(GameStateInspector).Assembly.GetName().Name,
                "Automation types must live in the automation assembly.");
        }

        [Test]
        public void AutomationApi_ExposesSpecRequiredOperations()
        {
            // The operations mandated by ARCHITECTURE.md's automation section must exist
            // on the public API surface. This is a compile-time contract.
            Assert.IsNotNull(typeof(GameTestAPI).GetMethod(nameof(GameTestAPI.ResetGame)));
            Assert.IsNotNull(typeof(GameTestAPI).GetMethod(nameof(GameTestAPI.SpawnEnemy)));
            Assert.IsNotNull(typeof(GameTestAPI).GetMethod(nameof(GameTestAPI.EquipWeapon)));
            Assert.IsNotNull(typeof(GameTestAPI).GetMethod(nameof(GameTestAPI.SetPlayerHealth)));
            Assert.IsNotNull(typeof(GameTestAPI).GetMethod(nameof(GameTestAPI.SetPlayerAmmo)));
            Assert.IsNotNull(typeof(GameTestAPI).GetMethod(nameof(GameTestAPI.TeleportPlayer)));
            Assert.IsNotNull(typeof(GameTestAPI).GetMethod(nameof(GameTestAPI.GetStateSnapshot)));
            Assert.IsNotNull(typeof(ScreenshotCapture).GetMethod(nameof(ScreenshotCapture.CaptureMain)));
        }

        [Test]
        public void GameStateSnapshot_JsonRoundTrip_KeepsData()
        {
            var snapshot = new GameStateSnapshot
            {
                capturedAt = 12.5f,
                player = new PlayerState { name = "Player", alive = true, health = 42, maxHealth = 100 },
                weapon = new WeaponState { index = GameTestAPI.WeaponShotgun, name = "Shotgun", ammo = 8, infinite = false },
                wave = new WaveState
                {
                    gameActive = true,
                    wave = 3,
                    trackedEnemiesAlive = 2,
                    livingEnemyCount = 2,
                    waveInProgress = true,
                    spawnPointCount = 7
                },
                enemies = new[]
                {
                    new EnemyState { name = "ZombieRunner", type = "ZombieRunner", health = 25, maxHealth = 25, distanceToPlayer = 4.2f }
                }
            };

            string json = snapshot.ToJson();
            Assert.That(json, Does.Contain("\"health\":42"));
            Assert.That(json, Does.Contain("\"wave\":3"));
            Assert.That(json, Does.Contain("\"distanceToPlayer\":"), "distanceToPlayer should be serialized.");

            GameStateSnapshot restored = GameStateSnapshot.FromJson(json);

            Assert.AreEqual(12.5f, restored.capturedAt, 0.001f);
            Assert.AreEqual(42, restored.player.health);
            Assert.AreEqual(100, restored.player.maxHealth);
            Assert.IsTrue(restored.player.alive);
            Assert.AreEqual("Shotgun", restored.weapon.name);
            Assert.AreEqual(GameTestAPI.WeaponShotgun, restored.weapon.index);
            Assert.AreEqual(3, restored.wave.wave);
            Assert.AreEqual(1, restored.enemies.Length);
            Assert.AreEqual("ZombieRunner", restored.enemies[0].name);
            Assert.AreEqual(4.2f, restored.enemies[0].distanceToPlayer, 0.01f);
        }
    }
}