using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace DoomClone.Tests.EditMode
{
    /// <summary>
    /// M10 — Base <see cref="Enemy"/> contract, verified headless (compilation-level /
    /// reflection, no running scene). The runtime behavior of that contract (HP,
    /// death, wave notification, NavMesh placement, player damage) is covered by the
    /// PlayMode <c>EnemyFrameworkTests</c>.
    /// </summary>
    public class EnemyFrameworkEditModeTests
    {
        [Test]
        public void BaseEnemy_IsAbstractMonoBehaviour_InRuntimeAssembly()
        {
            Assert.IsTrue(typeof(Enemy).IsAbstract,
                "Enemy is a base class; it must not be instantiable directly.");
            Assert.IsTrue(typeof(MonoBehaviour).IsAssignableFrom(typeof(Enemy)),
                "Enemy must derive from MonoBehaviour to hook into the scene loop.");
            Assert.AreEqual("DoomClone.Runtime", typeof(Enemy).Assembly.GetName().Name,
                "Enemy must live in the production runtime assembly.");
        }

        [Test]
        public void BaseEnemy_ExposesHealthDeathDamageAndNavigationContract()
        {
            const BindingFlags NonPublic = BindingFlags.Instance | BindingFlags.NonPublic;

            // Health / death observability (used by the whole codebase and automation).
            Assert.IsNotNull(typeof(Enemy).GetMethod("TakeDamage", new[] { typeof(int) }),
                "Enemy.TakeDamage(int) is the shared damage entry point.");
            Assert.IsNotNull(typeof(Enemy).GetMethod("IsDead"));
            Assert.IsNotNull(typeof(Enemy).GetMethod("GetHealth"));
            Assert.IsNotNull(typeof(Enemy).GetMethod("GetMaxHealth"));
            Assert.IsNotNull(typeof(Enemy).GetMethod("GetName"));

            // Death notification contract that WaveManager subscribes to (GAME_SPEC §4).
            Assert.IsNotNull(typeof(Enemy).GetEvent("OnEnemyDeath"),
                "Enemy.OnEnemyDeath event is the WaveManager death-notification contract.");
            Assert.IsNotNull(typeof(Enemy).GetEvent("OnEnemyDamaged"),
                "Enemy.OnEnemyDamaged event feeds weapon-hit / HUD observability.");

            // Common damage-to-player path: the base Attack() and the player API it calls.
            Assert.IsNotNull(typeof(Enemy).GetMethod("Attack", NonPublic),
                "Base Enemy.Attack() is the shared enemy→player damage method.");
            Assert.IsNotNull(typeof(PlayerController).GetMethod("TakeDamage", new[] { typeof(int) }),
                "PlayerController.TakeDamage(int) must exist for the enemy attack to connect.");

            // Navigation integration (GAME_SPEC §4/§5) and the no-throw placement guard.
            Assert.IsNotNull(typeof(Enemy).GetMethod("TrySetDestination", NonPublic),
                "Enemy.TrySetDestination(Vector3) must never throw on unplaced agents.");
            Assert.IsNotNull(typeof(Enemy).GetField("agent", NonPublic),
                "Enemy must own a NavMeshAgent reference.");
        }

        [Test]
        public void ExactlyThreeConcreteEnemySubtypes_ExistAndDeriveFromBase()
        {
            string[] concrete = typeof(Enemy)
                .Assembly.GetTypes()
                .Where(t => t != typeof(Enemy) && typeof(Enemy).IsAssignableFrom(t) && !t.IsAbstract)
                .Select(t => t.Name)
                .OrderBy(n => n)
                .ToArray();

            CollectionAssert.AreEquivalent(
                new[] { "RangedSoldier", "TankBrute", "ZombieRunner" },
                concrete,
                "GAME_SPEC §4 requires exactly three concrete enemy types, all deriving from Enemy.");
        }

        [Test]
        public void WaveManager_ExposesAliveTrackingDeathSubscriptionAndSpawnByType()
        {
            Assert.IsNotNull(typeof(WaveManager).GetMethod("GetEnemiesAlive"),
                "WaveManager must expose the alive-enemy count (death tracking contract).");
            Assert.IsNotNull(typeof(WaveManager).GetMethod("GetCurrentWave"));
            Assert.IsNotNull(typeof(WaveManager).GetMethod("IsWaveInProgress"));
            Assert.IsNotNull(typeof(WaveManager).GetMethod("SpawnEnemyByType", new[] { typeof(string) }),
                "SpawnEnemyByType is the automation/determinism entry point (DECISIONS.md).");
        }
    }
}