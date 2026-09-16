using System.Collections;
using DoomClone.Automation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace DoomClone.Tests.PlayMode
{
    /// <summary>
    /// M11 — ZombieRunner dedicated PlayMode verification per GAME_SPEC.md §4.1 and
    /// TEST_PLAN.md "Enemies: Runner":
    ///
    ///   - detects/targets the player and approaches RAPIDLY (direct rush that closes
    ///     a substantial gap down to melee range);
    ///   - performs a fast melee attack (fast cadence vs the other two types, and every
    ///     connecting hit deals exactly the configured damage to the player);
    ///   - distinct configuration vs the other types (lowest health, highest speed,
    ///     fastest cadence, melee damage tier);
    ///   - distinct visual (code-driven green material in ZombieRunner.Start).
    ///
    /// Death / wave notification / NavMesh placement are the base Enemy framework
    /// contract verified in M10 and are NOT re-verified here — only the runner-specific
    /// spin. Determinism follows the established patterns: wave-assignment gate before
    /// any synchronous wave clear, removes every other damage source each frame so the
    /// runner is the sole actor, deadline-based measurement off the "actively moving"
    /// gate so it stays frame-rate independent, and the player is kept topped up so
    /// incidental wave damage / the runner's own melee can never kill it mid-assert.
    /// </summary>
    public class RunnerEnemyTests
    {
        const int LethalDamage = int.MaxValue;

        static WaveManager Wm => WaveManager.Instance;

        static PlayerController Player() => GameTestAPI.GetPlayer();

        [UnitySetUp]
        public IEnumerator Setup()
        {
            TestInputDevices.EnsureDevices();
            // A previous test may have left the synthetic mouse/keyboard pressed; reset
            // so no stray fire/movement can disturb runner AI assertions.
            TestInputDevices.SetFireButton(false);
            TestInputDevices.ReleaseAllKeys();
            yield return GameTestAPI.StartFreshLevel(30f);
            GameTestAPI.SetPlayerHealth(100); // guard against incidental wave damage
            yield return null;
        }

        // ── Helpers (shared patterns with EnemyFrameworkTests / NavigationValidationTests) ──

        static IEnumerator BakeReady()
        {
            yield return GameBootstrap.WaitUntil(
                () => NavMesh.CalculateTriangulation().vertices.Length > 0, 10f);
            Assert.Greater(NavMesh.CalculateTriangulation().vertices.Length, 0,
                "NavMesh must produce baked triangles at runtime.");
            yield return new WaitForSeconds(0.5f); // let the async bake propagate
        }

        /// <summary>Wave-assignment gate: alive &gt; 0 is only written after the SpawnWave
        /// loop finishes, so a synchronous clear afterwards can never corrupt tracking.</summary>
        static IEnumerator WaitWaveAssigned(int atLeastAlive = 1)
        {
            yield return BakeReady();
            yield return GameBootstrap.WaitUntil(
                () => Wm != null && Wm.GetCurrentWave() >= 1 && Wm.GetEnemiesAlive() >= atLeastAlive,
                20f);
            GameTestAPI.SetPlayerHealth(100);
        }

        static Enemy SpawnEnemyType(string type)
        {
            GameObject spawned = GameTestAPI.SpawnEnemy(type);
            Assert.IsNotNull(spawned, "GameTestAPI.SpawnEnemy('" + type + "') must return an enemy.");
            Enemy enemy = spawned.GetComponent<Enemy>();
            Assert.IsNotNull(enemy, "Spawned '" + type + "' must carry an Enemy component.");
            return enemy;
        }

        static void KillEnemy(Enemy enemy)
        {
            Assert.IsFalse(enemy.IsDead(), "Cannot kill an already-dead enemy.");
            enemy.TakeDamage(LethalDamage);
            Assert.IsTrue(enemy.IsDead(), enemy.GetName() + " must die from lethal damage.");
        }

        /// <summary>Kills every living enemy except <paramref name="keep"/> so the runner
        /// under test is the ONLY actor that can damage the player or pollute measurement.</summary>
        static void ClearWaveEnemiesExcept(Enemy keep)
        {
            Enemy[] enemies = Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None);
            for (int i = 0; i < enemies.Length; i++)
            {
                Enemy enemy = enemies[i];
                if (enemy != null && enemy != keep && !enemy.IsDead())
                    enemy.TakeDamage(LethalDamage);
            }
        }

        /// <summary>Waits until the enemy's NavMeshAgent is enabled and placed, topping the
        /// player up every frame so a concurrently-spawned wave can never kill it while the
        /// agent enable coroutine (0.1 s + bake wait) is still pending.</summary>
        static IEnumerator WaitPlacedOnNavMesh(Enemy enemy)
        {
            NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
            Assert.IsNotNull(agent, enemy.GetName() + " must carry a NavMeshAgent.");
            float deadline = Time.realtimeSinceStartup + 8f;
            while (!(agent.enabled && agent.isOnNavMesh) && Time.realtimeSinceStartup < deadline)
            {
                GameTestAPI.SetPlayerHealth(100);
                yield return null;
            }
            GameTestAPI.SetPlayerHealth(100);
            Assert.IsTrue(agent.enabled && agent.isOnNavMesh,
                enemy.GetName() + " agent must be placed on the NavMesh after spawn.");
        }

        static float ColorDistance(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.a);
        }

        static void AssertColorNear(Color actual, Color expected, float tolerance, string message)
        {
            float d = ColorDistance(actual, expected);
            Assert.LessOrEqual(d, tolerance,
                message + " (actual=" + actual.ToString("F3") +
                ", expected=" + expected.ToString("F3") + ", delta=" + d.ToString("F3") + ")");
        }

        // ── Configuration distinctness vs the other types (GAME_SPEC §4.1) ──

        [UnityTest]
        public IEnumerator Runner_Config_LowestHealthHighestSpeedFastestCadenceMelee_DistinctFromOtherTypes()
        {
            yield return BakeReady();

            Enemy runner = SpawnEnemyType("runner");
            Enemy soldier = SpawnEnemyType("soldier");
            Enemy brute = SpawnEnemyType("brute");
            yield return null; // run Start(): player target + per-type visuals/scale

            Assert.IsInstanceOf<ZombieRunner>(runner, "Spawned 'runner' must be a ZombieRunner.");

            // Low health (GAME_SPEC §4.1): runner is the lowest-HP type and spawns full.
            Assert.AreEqual(30, runner.maxHealth, "Runner must declare low health (30).");
            Assert.AreEqual(runner.maxHealth, runner.GetHealth(), "Runner must spawn at full health.");
            Assert.Less(runner.maxHealth, soldier.maxHealth, "Runner must be lower-health than the soldier.");
            Assert.Less(runner.maxHealth, brute.maxHealth, "Runner must be lower-health than the brute.");

            // High speed: runner is the fastest type, and its agent speed follows the config.
            Assert.AreEqual(5f, runner.moveSpeed, 0.001f, "Runner must declare high speed (5).");
            Assert.Greater(runner.moveSpeed, soldier.moveSpeed, "Runner must outpace the soldier.");
            Assert.Greater(runner.moveSpeed, brute.moveSpeed, "Runner must outpace the brute.");
            NavMeshAgent agent = runner.GetComponent<NavMeshAgent>();
            Assert.IsNotNull(agent, "Runner must carry a NavMeshAgent.");
            Assert.AreEqual(runner.moveSpeed, agent.speed, 0.001f,
                "Runner agent speed must follow its moveSpeed config.");

            // Fast attack cadence: runner attacks faster than both other types.
            Assert.AreEqual(0.8f, runner.attackRate, 0.001f, "Runner must declare a fast cadence (0.8s).");
            Assert.Less(runner.attackRate, soldier.attackRate, "Runner cadence must beat the soldier's (1.5s).");
            Assert.Less(runner.attackRate, brute.attackRate, "Runner cadence must beat the brute's (2.5s).");

            // Low-to-medium melee damage (GAME_SPEC §4.1).
            Assert.AreEqual(8, runner.attackDamage, "Runner must declare low-to-medium melee damage (8).");
            Assert.Less(runner.attackDamage, soldier.attackDamage, "Runner must hit softer than the soldier.");
            Assert.Less(runner.attackDamage, brute.attackDamage, "Runner must hit softer than the brute.");

            // Melee, NOT ranged: the runner's reach must be far below the soldier's 25m gun.
            Assert.LessOrEqual(runner.attackRange, 2f, "Runner melee reach must stay short (1.8m).");
            Assert.Less(runner.attackRange, soldier.attackRange,
                "Soldier must attack from range; the runner is a melee rusher.");

            // Detection range lets it spot the player far off and rush (chase test relies on this).
            Assert.GreaterOrEqual(runner.detectionRange, 25f,
                "Runner must detect the player from far enough to launch a rush (25m).");

            // Targets the player: Enemy.Start resolved the actual player transform.
            Assert.IsNotNull(runner.player, "Runner must acquire the player target after Start.");
            Assert.AreEqual(Player().transform, runner.player,
                "Runner's target must be the actual player transform.");

            KillEnemy(runner);
            KillEnemy(soldier);
            KillEnemy(brute);
            GameTestAPI.SetPlayerHealth(100);
        }

        // ── Rapid direct rush (GAME_SPEC §4.1, TEST_PLAN "approaches rapidly") ──

        [UnityTest]
        public IEnumerator Runner_Chase_DetectsPlayer_RushesDirectly_ClosesSubstantialGapToMelee()
        {
            yield return WaitWaveAssigned(1);

            // Wipe the wave synchronously (gate guarantees it is fully assigned) so the aux
            // runner is the only enemy in play during the whole chase measurement.
            ClearWaveEnemiesExcept(null);
            Assert.AreEqual(0, Wm.GetEnemiesAlive(), "Field must be clear of wave enemies.");
            Assert.IsFalse(Wm.IsWaveInProgress(), "Cleared wave must not be in progress.");

            PlayerController player = Player();
            Assert.IsNotNull(player, "Player required for the chase target.");
            GameTestAPI.SetPlayerHealth(100);
            Vector3 playerPos = NavMeshUtil.SnapToNavMesh(player.transform.position, 3f);

            // Spawn the runner and park it ~12 m from the player BEFORE the WaveManager
            // EnableNavMeshAgent coroutine (0.1 s wait) re-snaps the transform, so the agent
            // enables exactly where we placed it: inside detection range (25 m) and clearly
            // outside melee range (1.8 m).
            Enemy runner = SpawnEnemyType("runner");
            Vector3 park = NavMeshUtil.SnapToNavMesh(playerPos + Vector3.back * 12f, 3f);
            runner.transform.position = park;
            yield return WaitPlacedOnNavMesh(runner);

            NavMeshAgent agent = runner.GetComponent<NavMeshAgent>();
            Assert.IsNotNull(agent, "Runner must carry a NavMeshAgent.");

            // Anchor at the position the enable coroutine finally snapped to.
            Vector3 anchor = runner.transform.position;
            float gapStart = Vector3.Distance(anchor, player.transform.position);
            Assert.Greater(gapStart, 5f,
                "Runner must begin clearly outside melee range (gapStart=" + gapStart.ToString("F1") + "u).");
            Assert.LessOrEqual(gapStart, runner.detectionRange,
                "Runner must begin inside detection range so the rush is guaranteed.");

            // "Actively moving" gate (DECISIONS 2026-09-16): wait until the runner is placed
            // on the NavMesh AND navigating toward the player before starting the clock.
            yield return GameBootstrap.WaitUntil(
                () => agent.enabled && agent.isOnNavMesh && agent.velocity.sqrMagnitude > 0.04f, 8f);

            float bestClosed = 0f;
            float bestRunnerMoved = 0f;
            bool reachedMelee = false;
            float deadline = Time.realtimeSinceStartup + 25f;
            while (!reachedMelee && Time.realtimeSinceStartup < deadline)
            {
                // Remove every other damage source each frame: a re-spawned wave enemy must
                // never reach the player and pollute the single-source chase measurement.
                ClearWaveEnemiesExcept(runner);
                GameTestAPI.SetPlayerHealth(100);

                float gapNow = Vector3.Distance(runner.transform.position, player.transform.position);
                float closed = gapStart - gapNow;
                if (closed > bestClosed) bestClosed = closed;

                float moved = Vector3.Distance(anchor, runner.transform.position);
                if (moved > bestRunnerMoved) bestRunnerMoved = moved;

                if (gapNow <= runner.attackRange + 0.5f) reachedMelee = true;
                yield return null;
            }

            Assert.IsTrue(reachedMelee || bestClosed > 6f,
                "Runner must rush directly toward the player and close a substantial gap " +
                "(gapStart=" + gapStart.ToString("F1") + "u, bestClosed=" + bestClosed.ToString("F1") +
                "u, reachedMelee=" + reachedMelee + ").");
            Assert.Greater(bestRunnerMoved, 3f,
                "Runner must physically traverse the arena toward the player (moved=" +
                bestRunnerMoved.ToString("F1") + "u).");
            Assert.IsFalse(runner.IsDead(), "Runner must survive the chase.");
            Assert.IsFalse(player.IsDead(), "Player must survive the runner's rush.");

            KillEnemy(runner);
            GameTestAPI.SetPlayerHealth(100);
        }

        // ── Fast melee attack connecting with configured damage (GAME_SPEC §4.1) ──

        [UnityTest]
        public IEnumerator Runner_MeleeAttack_RepeatedConnections_DealExactlyConfiguredDamage_AtFastCadence()
        {
            yield return WaitWaveAssigned(1);

            // Only the runner may damage the player during the measurement.
            ClearWaveEnemiesExcept(null);
            Assert.AreEqual(0, Wm.GetEnemiesAlive(), "Field must be clear of wave enemies.");
            Assert.IsFalse(Wm.IsWaveInProgress(), "Cleared wave must not be in progress.");

            PlayerController player = Player();
            Assert.IsNotNull(player, "Player required as the melee target.");
            GameTestAPI.SetPlayerHealth(100);
            Vector3 playerPos = NavMeshUtil.SnapToNavMesh(player.transform.position, 3f);

            // Park the runner 1 m from the player, inside melee range (1.8 m), before the
            // enable coroutine snaps it, so it can land its first melee the moment its agent
            // is placed.
            Enemy runner = SpawnEnemyType("runner");
            Vector3 park = playerPos + Vector3.right * 1.0f;
            if (!NavMeshUtil.TrySnapToNavMesh(park, 1f, out park))
                park = playerPos;
            runner.transform.position = park;
            yield return WaitPlacedOnNavMesh(runner);

            // Cadence sanity: the runner's configured attack cadence is faster than both other
            // types (precision lives in the config test; here we prove the behavior repeats and
            // the base Attack() path drives it every connection).
            Assert.Less(runner.attackRate, 1f, "Runner must attack with sub-1s cadence (0.8s).");

            int lastHp = player.currentHealth;
            int connections = 0;
            bool wrongDelta = false;
            void OnHealth(int current, int max)
            {
                int delta = lastHp - current;
                lastHp = current;
                if (delta == runner.attackDamage)
                {
                    connections++;
                }
                else if (delta > 0)
                {
                    wrongDelta = true; // a player hit with an unexpected amount
                }
            }
            player.OnHealthChanged += OnHealth;

            float deadline = Time.realtimeSinceStartup + 20f;
            while (connections < 2 && Time.realtimeSinceStartup < deadline)
            {
                // Keep every other living enemy dead and the player topped up: incidental wave
                // hits (or player death) would break the delta-classification of the runner's
                // melee; healing produces only 0/negative deltas that are ignored.
                ClearWaveEnemiesExcept(runner);
                GameTestAPI.SetPlayerHealth(100);
                yield return null;
            }
            player.OnHealthChanged -= OnHealth;

            Assert.GreaterOrEqual(connections, 2,
                "A runner inside attack range must repeatedly connect its melee attack " +
                "(connections=" + connections + ").");
            Assert.IsFalse(wrongDelta,
                "Every connecting runner hit must deal exactly the configured damage (" +
                runner.attackDamage + ").");
            Assert.IsFalse(player.IsDead(), "Player must survive the runner's melee.");
            Assert.IsFalse(runner.IsDead(), "Runner must survive the exchange.");

            KillEnemy(runner);
            GameTestAPI.SetPlayerHealth(100);
        }

        // ── Distinct code-driven visual (GAME_SPEC §4 "Enemy visuals") ──

        [UnityTest]
        public IEnumerator Runner_Visual_DistinctGreenMaterial_ContrastsWithSoldierAndBrute()
        {
            yield return BakeReady();

            Enemy runner = SpawnEnemyType("runner");
            Enemy soldier = SpawnEnemyType("soldier");
            Enemy brute = SpawnEnemyType("brute");
            yield return null; // Start() assigns each type's distinct material color

            Renderer runnerRend = runner.GetComponent<Renderer>();
            Renderer soldierRend = soldier.GetComponent<Renderer>();
            Renderer bruteRend = brute.GetComponent<Renderer>();
            Assert.IsNotNull(runnerRend, "Runner must carry a Renderer for its code-driven visual.");
            Assert.IsNotNull(soldierRend, "Soldier must carry a Renderer.");
            Assert.IsNotNull(bruteRend, "Brute must carry a Renderer.");

            Color runnerColor = runnerRend.material.color;
            Color soldierColor = soldierRend.material.color;
            Color bruteColor = bruteRend.material.color;

            // The visual assignment is code-driven (ZombieRunner.Start): the suggested green.
            AssertColorNear(runnerColor, Color.green, 0.02f,
                "Runner must use the distinct green visual from its Start().");

            // Cross-check the other two types keep their own colors so all three differ.
            AssertColorNear(soldierColor, new Color(1f, 0.6f, 0f), 0.02f,
                "Soldier must keep its orange visual.");
            AssertColorNear(bruteColor, new Color(0.6f, 0f, 0f), 0.02f,
                "Brute must keep its dark-red visual.");

            Assert.Greater(ColorDistance(runnerColor, soldierColor), 0.5f,
                "Runner vs soldier must be visually distinct (green vs orange).");
            Assert.Greater(ColorDistance(runnerColor, bruteColor), 0.5f,
                "Runner vs brute must be visually distinct (green vs dark red).");

            KillEnemy(runner);
            KillEnemy(soldier);
            KillEnemy(brute);
            GameTestAPI.SetPlayerHealth(100);
        }
    }
}