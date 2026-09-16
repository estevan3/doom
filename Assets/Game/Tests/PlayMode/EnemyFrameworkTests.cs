using System.Collections;
using DoomClone.Automation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace DoomClone.Tests.PlayMode
{
    /// <summary>
    /// M10 — Base Enemy framework PlayMode verification per GAME_SPEC.md §4 and
    /// TEST_PLAN.md "Enemies": the common Enemy contract (health, death, damage to
    /// player, NavMeshAgent integration, WaveManager death notification) exercised
    /// against real spawned enemies inside the playable scene.
    ///
    /// The three concrete types (ZombieRunner, RangedSoldier, TankBrute) are checked
    /// for the distinct statistics/behavior GAME_SPEC §4.1–§4.3 requires; exact
    /// per-type behavior is covered by later milestones M11–M13.
    ///
    /// Wave tracking follows DECISIONS.md: enemies spawned by waves decrement the
    /// wave alive count on death and can end the wave; enemies spawned through
    /// GameTestAPI/SpawnEnemyByType are auxiliary and must never touch wave state.
    /// </summary>
    public class EnemyFrameworkTests
    {
        const int LethalDamage = int.MaxValue;

        static WaveManager Wm => WaveManager.Instance;

        static PlayerController Player() => GameTestAPI.GetPlayer();

        [UnitySetUp]
        public IEnumerator Setup()
        {
            TestInputDevices.EnsureDevices();
            // A previous test may have left the synthetic mouse/keyboard pressed; reset
            // so no stray fire/movement can disturb enemy AI assertions.
            TestInputDevices.SetFireButton(false);
            TestInputDevices.ReleaseAllKeys();
            yield return GameTestAPI.StartFreshLevel(30f);
            GameTestAPI.SetPlayerHealth(100); // guard against incidental wave damage
            yield return null;
        }

        // ── Helpers ────────────────────────────────────────────────────────

        static IEnumerator BakeReady()
        {
            yield return GameBootstrap.WaitUntil(
                () => NavMesh.CalculateTriangulation().vertices.Length > 0, 10f);
            Assert.Greater(NavMesh.CalculateTriangulation().vertices.Length, 0,
                "NavMesh must produce baked triangles at runtime.");
            // Let the async BuildNavMesh finish propagating before sampling/pathing.
            yield return new WaitForSeconds(0.5f);
        }

        /// <summary>
        /// Waits until the current wave is fully spawned AND assigned. enemiesAlive is only
        /// written after SpawnWave finishes its spawn loop, so alive &gt; 0 implies the whole
        /// wave was counted first — killing tracked enemies only after this gate can never
        /// corrupt the counter while the async spawn loop is still running.
        /// </summary>
        static IEnumerator WaitWaveAssigned(int atLeastAlive = 2)
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

        /// <summary>Kills every living enemy except <paramref name="keep"/> — used to isolate
        /// a single damage source or to wipe the field between tracking assertions.</summary>
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

        static int CountLivingEnemies()
        {
            int count = 0;
            Enemy[] enemies = Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None);
            for (int i = 0; i < enemies.Length; i++)
            {
                if (!enemies[i].IsDead()) count++;
            }
            return count;
        }

        static Enemy FindLivingWaveEnemy()
        {
            Enemy[] enemies = Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None);
            for (int i = 0; i < enemies.Length; i++)
            {
                if (!enemies[i].IsDead()) return enemies[i];
            }
            return null;
        }

        static IEnumerator WaitPlacedOnNavMesh(Enemy enemy)
        {
            NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
            Assert.IsNotNull(agent, enemy.GetName() + " must carry a NavMeshAgent.");
            yield return GameBootstrap.WaitUntil(
                () => agent.enabled && agent.isOnNavMesh, 8f);
            Assert.IsTrue(agent.enabled && agent.isOnNavMesh,
                enemy.GetName() + " agent must be placed on the NavMesh after spawn.");
        }

        // ── Configuration (GAME_SPEC §4) ───────────────────────────────────

        [UnityTest]
        public IEnumerator EnemyFramework_Config_ThreeDistinctTypes_DifferAsSpecified()
        {
            yield return BakeReady();

            Enemy runner = SpawnEnemyType("runner");
            Enemy soldier = SpawnEnemyType("soldier");
            Enemy brute = SpawnEnemyType("brute");
            yield return null; // run Start(): materials, brute scale 2 + giant agent

            ZombieRunner runnerR = runner as ZombieRunner;
            RangedSoldier soldierR = soldier as RangedSoldier;
            TankBrute bruteR = brute as TankBrute;
            Assert.IsNotNull(runnerR, "Spawned 'runner' must be a ZombieRunner.");
            Assert.IsNotNull(soldierR, "Spawned 'soldier' must be a RangedSoldier.");
            Assert.IsNotNull(bruteR, "Spawned 'brute' must be a TankBrute.");

            // Fresh, alive, at full HP (base Enemy.Awake initialises currentHealth).
            foreach (Enemy e in new[] { runner, soldier, brute })
            {
                Assert.AreEqual(e.GetMaxHealth(), e.GetHealth(),
                    e.GetName() + " must spawn at full health.");
                Assert.IsFalse(e.IsDead(), e.GetName() + " must spawn alive.");
            }

            // Health moves up runner → soldier → brute (GAME_SPEC §4.1–§4.3).
            Assert.Less(runner.maxHealth, soldier.maxHealth,
                "Runner must be the lowest-health type.");
            Assert.Less(soldier.maxHealth, brute.maxHealth,
                "Brute must be the highest-health type (200 HP tank).");

            // Movement speed: runner fast, soldier medium, brute slow.
            Assert.Greater(runner.moveSpeed, soldier.moveSpeed,
                "Runner must outpace the soldier.");
            Assert.Greater(soldier.moveSpeed, brute.moveSpeed,
                "Brute must be the slowest type.");

            // Attack cadence: runner fast, soldier medium, brute slow.
            Assert.Less(runner.attackRate, soldier.attackRate,
                "Runner must attack faster than the soldier.");
            Assert.Less(soldier.attackRate, brute.attackRate,
                "Brute must attack slower than the soldier.");

            // Damage rises with the enemy tier.
            Assert.Less(runner.attackDamage, soldier.attackDamage,
                "Soldier must hit harder than the runner.");
            Assert.Less(soldier.attackDamage, brute.attackDamage,
                "Brute must hit harder than the soldier.");

            // Ranged Soldier is the long-range attacker; runners/brutes are melee.
            Assert.Greater(soldier.attackRange, runner.attackRange,
                "Soldier must attack from further than the runner.");
            Assert.Greater(soldier.attackRange, brute.attackRange,
                "Soldier must attack from further than the brute.");
            Assert.Greater(soldierR.preferredDistance, 0f,
                "Soldier must define an ideal engagement distance.");

            // Brute: telegraphed heavy attack (~0.5 s) and a larger hitbox (scale 2).
            Assert.AreEqual(0.5f, bruteR.telegraphDuration, 0.05f,
                "Brute telegraph must be ~0.5 s (GAME_SPEC §4.3).");
            Assert.Greater(bruteR.slamDamage, brute.attackDamage,
                "The telegraphed slam must hit harder than the normal attack.");
            Assert.Greater(brute.transform.localScale.x, 1f,
                "Brute must use a larger model/hitbox scale than the melee types.");
            Assert.AreEqual(1f, runner.transform.localScale.x, 0.001f,
                "Runner must keep the default model scale.");
            Assert.AreEqual(1f, soldier.transform.localScale.x, 0.001f,
                "Soldier must keep the default model scale.");

            // Navigation integration (GAME_SPEC §5): every type owns an agent whose
            // speed follows the configured moveSpeed.
            foreach (Enemy e in new[] { runner, soldier, brute })
            {
                NavMeshAgent agent = e.GetComponent<NavMeshAgent>();
                Assert.IsNotNull(agent, e.GetName() + " must carry a NavMeshAgent.");
                Assert.AreEqual(e.moveSpeed, agent.speed, 0.001f,
                    e.GetName() + " agent speed must follow its moveSpeed config.");
            }

            KillEnemy(runner);
            KillEnemy(soldier);
            KillEnemy(brute);
        }

        // ── Death + WaveManager notification (GAME_SPEC §4, §6.5) ──────────

        [UnityTest]
        public IEnumerator EnemyFramework_Death_WaveEnemy_LethalDamage_NotifiesWaveExactlyOnce()
        {
            yield return WaitWaveAssigned(2);

            Enemy target = FindLivingWaveEnemy();
            Assert.IsNotNull(target, "A living wave enemy is required.");
            Assert.GreaterOrEqual(Wm.GetEnemiesAlive(), 2,
                "Need a wave with ≥2 living enemies so the kill cannot end the wave.");

            // Non-lethal damage: observable, exact, never lethal.
            int damagedCalls = 0;
            void OnDamaged(Enemy e, int d) { damagedCalls++; }
            target.OnEnemyDamaged += OnDamaged;
            int hpBefore = target.GetHealth();
            target.TakeDamage(5);
            target.OnEnemyDamaged -= OnDamaged;
            Assert.AreEqual(hpBefore - 5, target.GetHealth(),
                "TakeDamage must reduce HP by exactly the applied amount.");
            Assert.AreEqual(1, damagedCalls, "TakeDamage must raise OnEnemyDamaged exactly once.");
            Assert.IsFalse(target.IsDead(), "Non-lethal damage must not kill.");

            // Lethal damage: death fires exactly once and the wave counter decrements once.
            int deathCalls = 0;
            void OnDeath(Enemy e) { deathCalls++; }
            target.OnEnemyDeath += OnDeath;

            int aliveBefore = Wm.GetEnemiesAlive();
            int livingBefore = CountLivingEnemies();
            bool waveInProgressBefore = Wm.IsWaveInProgress();

            target.TakeDamage(LethalDamage);
            target.OnEnemyDeath -= OnDeath;

            Assert.AreEqual(1, deathCalls, "Enemy death must notify OnEnemyDeath exactly once.");
            Assert.IsTrue(target.IsDead(), "Lethal damage must kill the enemy.");
            Assert.LessOrEqual(target.GetHealth(), 0, "Dead enemy health must be <= 0.");
            Assert.AreEqual(aliveBefore - 1, Wm.GetEnemiesAlive(),
                "WaveManager must decrement the alive count exactly once for a tracked wave enemy.");
            Assert.AreEqual(livingBefore - 1, CountLivingEnemies(),
                "The dead enemy must immediately leave the living-enemy set.");
            Assert.AreEqual(waveInProgressBefore, Wm.IsWaveInProgress(),
                "Killing a single non-final enemy must not end the wave (only the last death does).");

            // Death effect + despawn (GAME_SPEC §4): the dead GameObject is destroyed.
            yield return GameBootstrap.WaitUntil(() => target == null, 6f);
            Assert.IsTrue(target == null,
                "The dead enemy GameObject must be destroyed by its death sequence.");

            ClearWaveEnemiesExcept(null);
            GameTestAPI.SetPlayerHealth(100);
        }

        // ── Auxiliary spawns must not pollute wave tracking (DECISIONS.md) ─

        [UnityTest]
        public IEnumerator EnemyFramework_SpawnEnemyByType_Auxiliary_NotTrackedInWave_AndDeathDoesNotDecrement()
        {
            yield return WaitWaveAssigned(1);

            // Wipe the full wave synchronously. No yields follow until every assertion
            // completes: the final wave death starts the 5 s cooldown and a future wave
            // must not spawn into the middle of these reads.
            ClearWaveEnemiesExcept(null);
            Assert.AreEqual(0, Wm.GetEnemiesAlive(),
                "Clearing the wave must reset the tracked alive count to 0.");
            Assert.IsFalse(Wm.IsWaveInProgress(), "An emptied wave is not in progress.");

            int waveBefore = Wm.GetCurrentWave();

            Enemy runner = SpawnEnemyType("runner"); // auxiliary: NOT tracked by the wave
            Assert.AreEqual(0, Wm.GetEnemiesAlive(),
                "An auxiliary spawn (SpawnEnemyByType) must NOT count toward the wave.");
            Assert.AreEqual(1, CountLivingEnemies(),
                "Only the aux runner should be alive after the field was wiped.");

            int deathCalls = 0;
            runner.OnEnemyDeath += (Enemy e) => deathCalls++;

            KillEnemy(runner);

            Assert.AreEqual(1, deathCalls, "Aux enemy death must fire OnEnemyDeath.");
            Assert.AreEqual(0, Wm.GetEnemiesAlive(),
                "Auxiliary enemy death must NOT decrement the wave alive count.");
            Assert.AreEqual(waveBefore, Wm.GetCurrentWave(),
                "Auxiliary death must not advance the wave number.");
        }

        // ── Navigation integration (GAME_SPEC §5) for all three types ──────

        [UnityTest]
        public IEnumerator EnemyFramework_NavMesh_AllSpawnTypesPlaceAndNavigateTowardPlayer()
        {
            yield return BakeReady();

            Enemy runner = SpawnEnemyType("runner");
            Enemy soldier = SpawnEnemyType("soldier");
            Enemy brute = SpawnEnemyType("brute");

            yield return WaitPlacedOnNavMesh(runner);
            yield return WaitPlacedOnNavMesh(soldier);
            yield return WaitPlacedOnNavMesh(brute);

            NavMeshAgent[] agents =
            {
                runner.GetComponent<NavMeshAgent>(),
                soldier.GetComponent<NavMeshAgent>(),
                brute.GetComponent<NavMeshAgent>(),
            };

            foreach (NavMeshAgent agent in agents)
            {
                Assert.IsTrue(agent.SetDestination(Player().transform.position),
                    "A placed agent of every type must accept a destination to the player without error.");
            }

            // Park all three inside their detection ranges (10 m from the player, inside the
            // arena) so the chase is guaranteed regardless of where they were spawned, then
            // measure the farthest NavMesh displacement across the three.
            Vector3 playerPos = NavMeshUtil.SnapToNavMesh(Player().transform.position, 3f);
            Vector3[] targets =
            {
                NavMeshUtil.SnapToNavMesh(playerPos + Vector3.forward * 10f, 3f),
                NavMeshUtil.SnapToNavMesh(playerPos + Vector3.back * 10f, 3f),
                NavMeshUtil.SnapToNavMesh(playerPos + Vector3.right * 10f, 3f),
            };
            Vector3[] anchors = new Vector3[3];
            for (int i = 0; i < 3; i++)
            {
                anchors[i] = targets[i];
                agents[i].Warp(anchors[i]);
            }
            Physics.SyncTransforms();

            float bestMoved = 0f;
            float deadline = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < deadline)
            {
                GameTestAPI.SetPlayerHealth(100); // wave enemies may also arrive at the player
                for (int i = 0; i < 3; i++)
                {
                    float moved = Vector3.Distance(anchors[i], agents[i].transform.position);
                    if (moved > bestMoved) bestMoved = moved;
                }
                yield return null;
            }

            Assert.Greater(bestMoved, 0.75f,
                "At least one spawned enemy type must navigate the NavMesh toward the player " +
                "(bestMoved=" + bestMoved.ToString("F2") + "u).");
            Assert.IsFalse(runner.IsDead() || soldier.IsDead() || brute.IsDead(),
                "Navigation must not harm the spawned enemies.");
            Assert.IsFalse(Player().IsDead(), "Player must survive navigation validation.");

            KillEnemy(runner);
            KillEnemy(soldier);
            KillEnemy(brute);
        }

        // ── Common damage-to-player path: base Enemy.Attack() ───────────────

        [UnityTest]
        public IEnumerator EnemyFramework_DamagePlayer_BaseAttack_HitsPlayerForConfiguredRunnerDamage()
        {
            yield return WaitWaveAssigned(1);

            PlayerController player = Player();
            Assert.IsNotNull(player, "Player required.");

            Enemy runner = SpawnEnemyType("runner");
            yield return WaitPlacedOnNavMesh(runner);

            // Park the runner inside its melee range (attackRange 1.8 m) on a valid NavMesh
            // position. The base Enemy.Attack() → PlayerController.TakeDamage() is the shared
            // player-damage path of the framework.
            Vector3 playerPos = NavMeshUtil.SnapToNavMesh(player.transform.position, 3f);
            Vector3 candidate = playerPos + Vector3.right * 1.0f;
            if (!NavMeshUtil.TrySnapToNavMesh(candidate, 1f, out Vector3 snapped))
                snapped = playerPos;
            runner.GetComponent<NavMeshAgent>().Warp(snapped);
            Physics.SyncTransforms();

            GameTestAPI.SetPlayerHealth(100);
            int lastHp = player.currentHealth;
            int runnerHitDelta = -1;
            void OnHealth(int current, int max)
            {
                int delta = lastHp - current;
                lastHp = current;
                if (delta == runner.attackDamage && runnerHitDelta < 0)
                    runnerHitDelta = delta;
            }
            player.OnHealthChanged += OnHealth;

            float deadline = Time.realtimeSinceStartup + 12f;
            while (runnerHitDelta < 0 && Time.realtimeSinceStartup < deadline)
            {
                // Keep only the parked runner as a damage source: any wave respawn is killed
                // on sight, before it can reach or attack the player (its own hits would land
                // long after the runner's first connection).
                ClearWaveEnemiesExcept(runner);
                GameTestAPI.SetPlayerHealth(100); // top up incidental runner damage
                yield return null;
            }
            player.OnHealthChanged -= OnHealth;

            Assert.AreEqual(runner.attackDamage, runnerHitDelta,
                "A runner inside attack range must connect the base Enemy.Attack() and deal " +
                "exactly the configured damage to the player (got " + runnerHitDelta + ").");
            Assert.IsFalse(player.IsDead(), "Player must survive the connecting hit.");
            Assert.IsFalse(runner.IsDead(), "Runner must survive the exchange.");

            GameTestAPI.SetPlayerHealth(100);
        }

        // ── Smoke: spawn all three, damage, death, tracking consistency ─────

        [UnityTest]
        public IEnumerator EnemyFramework_Smoke_SpawnAllTypes_Death_AliveTracking_NoExceptions()
        {
            yield return BakeReady();

            Enemy runner = SpawnEnemyType("runner");
            Enemy soldier = SpawnEnemyType("soldier");
            Enemy brute = SpawnEnemyType("brute");

            yield return WaitPlacedOnNavMesh(runner);
            yield return WaitPlacedOnNavMesh(soldier);
            yield return WaitPlacedOnNavMesh(brute);

            foreach (Enemy e in new[] { runner, soldier, brute })
            {
                e.GetComponent<NavMeshAgent>().SetDestination(Player().transform.position);
            }

            GameStateSnapshot before = GameTestAPI.GetStateSnapshot();
            int trackedBefore = before.wave.trackedEnemiesAlive;
            int livingBefore = before.wave.livingEnemyCount;

            // All following assertions run in the same frame, so no wave event can slip in.
            KillEnemy(runner);

            GameStateSnapshot after = GameTestAPI.GetStateSnapshot();
            Assert.AreEqual(trackedBefore, after.wave.trackedEnemiesAlive,
                "Auxiliary enemy death must not change the wave-tracked alive count.");
            Assert.AreEqual(livingBefore - 1, after.wave.livingEnemyCount,
                "The dead aux enemy must leave the global living-enemy set.");
            Assert.IsTrue(runner.IsDead(), "Runner must be dead after lethal damage.");
            Assert.IsFalse(soldier.IsDead(), "Soldier must remain alive.");
            Assert.IsFalse(brute.IsDead(), "Brute must remain alive.");

            KillEnemy(soldier);
            KillEnemy(brute);
            GameTestAPI.SetPlayerHealth(100);
        }
    }
}