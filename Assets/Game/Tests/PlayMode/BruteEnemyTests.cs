using System.Collections;
using System.IO;
using DoomClone.Automation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace DoomClone.Tests.PlayMode
{
    /// <summary>
    /// M13 — TankBrute dedicated PlayMode verification per GAME_SPEC.md §4.3 and
    /// TEST_PLAN.md "Enemies: Brute":
    ///
    ///   - approaches SLOWLY (moveSpeed 1.5 is far below the runner's 5 and the
    ///     soldier's 3.5; the engaged-approach test proves it closes distance at a
    ///     crawl compared with a runner over the same window);
    ///   - high health (200, the toughest type) on a larger hitbox (start-scale 2,
    ///     collider grossly bigger, agent height 4 / radius 1.5);
    ///   - attack begins with an approximately 0.5 s telegraph (preparation pause) so
    ///     the player has time to evade — the timing test measures the pause between
    ///     the telegraph animation and the slam damage landing;
    ///   - heavy short-range damage when the slam connects: exactly slamDamage (30),
    ///     higher than the runner's 8 and the soldier's 12; repeated while the player
    ///     stays inside attackRange;
    ///   - distinct visual — code-driven dark-red material in TankBrute.Start.
    ///
    /// Death / wave notification / NavMesh placement are the base Enemy framework
    /// contract verified in M10 and are NOT re-verified here — only the brute-specific
    /// spin. Determinism follows the established patterns: wave-assignment gate before
    /// any synchronous wave clear, clear-every-other-enemy each frame so the aux brute is
    /// the sole actor, deadline-based measurement off the "actively moving" gate, and the
    /// player kept topped up so no stray wave/runner damage kills it mid-assert.
    /// </summary>
    public class BruteEnemyTests
    {
        const int LethalDamage = int.MaxValue;

        // Telegraph tolerance (recorded in DECISIONS.md): GAME_SPEC §4.3 says
        // "approximately 0.5 seconds" for the preparation pause. PlayMode batch frames
        // quantize the wall-clock start→damage measurement to frame boundaries, so a
        // tight window is asserted — telegraph must be a clearly visible pause (>0), not
        // an instant hit, while staying ~0.5 s (below a full second).
        const float TelegraphMinSeconds = 0.35f;
        const float TelegraphMaxSeconds = 0.75f;

        static readonly string TestResultsDir =
            Path.Combine(Directory.GetCurrentDirectory(), "TestResults");

        static WaveManager Wm => WaveManager.Instance;

        static PlayerController Player() => GameTestAPI.GetPlayer();

        [UnitySetUp]
        public IEnumerator Setup()
        {
            TestInputDevices.EnsureDevices();
            TestInputDevices.SetFireButton(false);
            TestInputDevices.ReleaseAllKeys();
            yield return GameTestAPI.StartFreshLevel(30f);
            GameTestAPI.SetPlayerHealth(100); // guard against incidental wave damage
            yield return null;
        }

        // ── Helpers (shared patterns with EnemyFrameworkTests / RunnerEnemyTests) ──

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

        /// <summary>Kills every living enemy except the kept actors so only the brute
        /// (and optionally a runner control) can damage the player or pollute measurement.</summary>
        static void ClearWaveEnemiesExcept(Enemy keepA, Enemy keepB = null)
        {
            Enemy[] enemies = Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None);
            for (int i = 0; i < enemies.Length; i++)
            {
                Enemy enemy = enemies[i];
                if (enemy != null && enemy != keepA && enemy != keepB && !enemy.IsDead())
                    enemy.TakeDamage(LethalDamage);
            }
        }

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

        static IEnumerator WaitEngagedMoving(Enemy enemy)
        {
            NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
            Assert.IsNotNull(agent, enemy.GetName() + " must carry a NavMeshAgent.");
            yield return GameBootstrap.WaitUntil(
                () => agent.enabled && agent.isOnNavMesh && agent.velocity.sqrMagnitude > 0.04f, 8f);
            Assert.IsTrue(agent.enabled && agent.isOnNavMesh && agent.velocity.sqrMagnitude > 0.04f,
                enemy.GetName() + " must be placed on the NavMesh and actively navigating before measurement.");
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

        static TankBrute FindSoleBrute()
        {
            Enemy[] enemies = Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None);
            for (int i = 0; i < enemies.Length; i++)
            {
                TankBrute brute = enemies[i] as TankBrute;
                if (brute != null && !brute.IsDead())
                    return brute;
            }
            Assert.Fail("The sole aux TankBrute must be alive after setup.");
            return null;
        }

        // ── Configuration distinctness + larger hitbox (GAME_SPEC §4.3) ──

        [UnityTest]
        public IEnumerator Brute_Config_HighestHealthSlowestMoveHeaviestDamageSlowestCadenceWithTelegraph_LargerHitbox_Distinct()
        {
            yield return BakeReady();

            Enemy brute = SpawnEnemyType("brute");
            Enemy runner = SpawnEnemyType("runner");
            Enemy soldier = SpawnEnemyType("soldier");
            yield return null; // run Start(): player target + per-type visuals/scale

            TankBrute tank = brute as TankBrute;
            Assert.IsNotNull(tank, "Spawned 'brute' must be a TankBrute.");

            // High health (GAME_SPEC §4.3): the brute is the toughest type and spawns full.
            Assert.AreEqual(200, brute.maxHealth, "Brute must declare high health (200).");
            Assert.AreEqual(brute.maxHealth, brute.GetHealth(), "Brute must spawn at full health.");
            Assert.Greater(brute.maxHealth, soldier.maxHealth, "Brute must out-tank the soldier.");
            Assert.Greater(brute.maxHealth, runner.maxHealth, "Brute must out-tank the runner.");

            // Low speed: brute is the slowest type, and its agent speed follows the config.
            Assert.AreEqual(1.5f, brute.moveSpeed, 0.001f, "Brute must declare low speed (1.5).");
            Assert.Less(brute.moveSpeed, soldier.moveSpeed, "Brute must be slower than the soldier.");
            Assert.Less(brute.moveSpeed, runner.moveSpeed, "Brute must be slower than the runner.");
            NavMeshAgent agent = brute.GetComponent<NavMeshAgent>();
            Assert.IsNotNull(agent, "Brute must carry a NavMeshAgent.");
            Assert.AreEqual(brute.moveSpeed, agent.speed, 0.001f,
                "Brute agent speed must follow its moveSpeed config.");

            // Heavy short-range damage: attackDamage is the highest declared value and the
            // telegraphed slam is harder still (GAME_SPEC §4.3 "high melee/short-range damage").
            Assert.AreEqual(25, brute.attackDamage, "Brute must declare heavy damage (25).");
            Assert.Greater(brute.attackDamage, soldier.attackDamage, "Brute must hit harder than the soldier.");
            Assert.Greater(brute.attackDamage, runner.attackDamage, "Brute must hit harder than the runner.");
            Assert.AreEqual(30, tank.slamDamage, "Brute's slam must deal the configured slam damage (30).");
            Assert.Greater(tank.slamDamage, brute.attackDamage,
                "The telegraphed slam must hit harder than the base declared attack.");

            // Slow attack cadence: the brute's configured cadence is the slowest of the three.
            Assert.AreEqual(2.5f, brute.attackRate, 0.001f, "Brute must declare the slowest cadence (2.5s).");
            Assert.Greater(brute.attackRate, soldier.attackRate, "Brute cadence must be slower than the soldier's (1.5s).");
            Assert.Greater(brute.attackRate, runner.attackRate, "Brute cadence must be slower than the runner's (0.8s).");

            // Telegraph: the ~0.5 s preparation pause knob (GAME_SPEC §4.3).
            Assert.AreEqual(0.5f, tank.telegraphDuration, 0.001f,
                "Brute must declare ~0.5 s telegraph (preparation pause).");
            Assert.AreEqual(3f, tank.slamRange, 0.001f,
                "Brute slam must have a short-range blast (3 m).");

            // Melee, not ranged: reach stays close to the player, far below any ranged gun.
            Assert.LessOrEqual(brute.attackRange, 3f, "Brute melee reach must stay short (3 m).");
            Assert.Less(brute.attackRange, soldier.attackRange,
                "Soldier must attack from range; the brute is a slow melee.");
            Assert.GreaterOrEqual(brute.detectionRange, 20f,
                "Brute must detect the player from enough distance to approach (20 m).");

            // Larger hitbox/scale (GAME_SPEC §4.3): model scale 2 and a fatter agent.
            Assert.AreEqual(Vector3.one * 2f, brute.transform.localScale,
                "Brute Start must scale the model 2x for a larger hitbox.");
            Assert.AreEqual(1f, runner.transform.localScale.x, "Runner must keep scale 1.");
            Assert.AreEqual(1f, soldier.transform.localScale.x, "Soldier must keep scale 1.");
            Assert.AreEqual(4f, agent.height, 0.001f, "Brute agent must be tall (height 4).");
            Assert.AreEqual(1.5f, agent.radius, 0.001f, "Brute agent must be fat (radius 1.5).");
            CapsuleCollider bruteCol = brute.GetComponent<CapsuleCollider>();
            CapsuleCollider runnerCol = runner.GetComponent<CapsuleCollider>();
            CapsuleCollider soldierCol = soldier.GetComponent<CapsuleCollider>();
            Assert.IsNotNull(bruteCol, "Brute must carry its CapsuleCollider.");
            Assert.IsNotNull(runnerCol, "Runner must carry its CapsuleCollider.");
            Assert.IsNotNull(soldierCol, "Soldier must carry its CapsuleCollider.");
            // These enemies have no Rigidbody: their colliders are static, so `bounds`
            // reflects the transform only after a physics sync. Sync now, otherwise the
            // brute's scale-2 AABB can still report scale-1 (height 2.0 == runner's) and
            // the hitbox assertions fail depending on how many fixed steps the scheduler
            // ran between SpawnEnemy and this line.
            Physics.SyncTransforms();
            float bruteHeight = bruteCol.bounds.size.y;
            float bruteWidth = bruteCol.bounds.size.x;
            Assert.Greater(bruteHeight, runnerCol.bounds.size.y + 0.5f,
                "Brute world-collider height must clearly exceed the runner's (brute=" +
                bruteHeight.ToString("F1") + ", runner=" + runnerCol.bounds.size.y.ToString("F1") + ").");
            Assert.Greater(bruteWidth, soldierCol.bounds.size.x + 0.5f,
                "Brute world-collider width must clearly exceed the soldier's (brute=" +
                bruteWidth.ToString("F1") + ", soldier=" + soldierCol.bounds.size.x.ToString("F1") + ").");

            // Melee, NOT ranged verification present; targets the actual player.
            Assert.IsNotNull(brute.player, "Brute must acquire the player target after Start.");
            Assert.AreEqual(Player().transform, brute.player,
                "Brute's target must be the actual player transform.");

            KillEnemy(brute);
            KillEnemy(runner);
            KillEnemy(soldier);
            GameTestAPI.SetPlayerHealth(100);
        }

        // ── Slow approach (GAME_SPEC §4.3 "low speed", TEST_PLAN "approaches slowly") ──
        // Comparative with an equal-distance runner so "slow" is proven behaviorally, not
        // only by config ordering.

        [UnityTest]
        public IEnumerator Brute_SlowApproach_ClosesDistanceAtCrawl_WhileRunnerCoversFarMoreOverSameWindow()
        {
            yield return WaitWaveAssigned(1);

            // Wipe the wave synchronously (gate guarantees assignment) so only our two aux
            // enemies exist during the whole comparison.
            ClearWaveEnemiesExcept(null);
            Assert.AreEqual(0, Wm.GetEnemiesAlive(), "Field must be clear of wave enemies.");
            Assert.IsFalse(Wm.IsWaveInProgress(), "Cleared wave must not be in progress.");

            PlayerController player = Player();
            Assert.IsNotNull(player, "Player required as the approach target.");
            GameTestAPI.SetPlayerHealth(100);
            Vector3 playerPos = NavMeshUtil.SnapToNavMesh(player.transform.position, 3f);

            // Spawn both and park them at equal distance (10 m) on opposite sides of the
            // player BEFORE the EnableNavMeshAgent coroutine re-snaps the transform, so each
            // agent enables exactly where we placed it: inside its own detection range and
            // clearly outside melee.
            Enemy brute = SpawnEnemyType("brute");
            Enemy runner = SpawnEnemyType("runner");
            Vector3 brutePark = NavMeshUtil.SnapToNavMesh(playerPos + Vector3.back * 10f, 3f);
            Vector3 runnerPark = NavMeshUtil.SnapToNavMesh(playerPos + Vector3.forward * 10f, 3f);
            brute.transform.position = brutePark;
            runner.transform.position = runnerPark;
            yield return WaitPlacedOnNavMesh(brute);
            yield return WaitPlacedOnNavMesh(runner);

            // Both must be placed AND actively navigating before the clock starts.
            yield return WaitEngagedMoving(brute);
            yield return WaitEngagedMoving(runner);

            Vector3 bruteAnchor = brute.transform.position;
            Vector3 runnerAnchor = runner.transform.position;
            Assert.AreEqual(brute.transform.position, bruteAnchor,
                "Brute must begin measurement from its NavMesh-snapped anchor.");
            float bruteGapStart = Vector3.Distance(bruteAnchor, player.transform.position);
            float runnerGapStart = Vector3.Distance(runnerAnchor, player.transform.position);
            Assert.Greater(bruteGapStart, 5f, "Brute must begin clearly outside melee range.");
            Assert.LessOrEqual(bruteGapStart, brute.detectionRange,
                "Brute must begin inside its detection range so the approach is guaranteed.");

            // Measure until the brute has meaningfully advanced (~2 m at its 1.5 crawl),
            // tracking BOTH enemies' best displacement over that identical window.
            float deadline = Time.realtimeSinceStartup + 15f;
            float bestBruteMoved = 0f;
            float bestRunnerMoved = 0f;
            float bestBruteClosed = 0f;
            while (bestBruteMoved < 2f && Time.realtimeSinceStartup < deadline)
            {
                ClearWaveEnemiesExcept(brute, runner);
                GameTestAPI.SetPlayerHealth(100);

                float bruteMoved = Vector3.Distance(bruteAnchor, brute.transform.position);
                if (bruteMoved > bestBruteMoved) bestBruteMoved = bruteMoved;
                float bruteGapNow = Vector3.Distance(brute.transform.position, player.transform.position);
                float bruteClosed = bruteGapStart - bruteGapNow;
                if (bruteClosed > bestBruteClosed) bestBruteClosed = bruteClosed;

                float runnerMoved = Vector3.Distance(runnerAnchor, runner.transform.position);
                if (runnerMoved > bestRunnerMoved) bestRunnerMoved = runnerMoved;

                yield return null;
            }

            Assert.GreaterOrEqual(bestBruteMoved, 1.5f,
                "The brute must physically advance toward the player over the measurement " +
                "window (moved=" + bestBruteMoved.ToString("F2") + "u).");
            Assert.Greater(bestBruteClosed, 0.5f,
                "The brute must close a measurable gap toward the player over the window " +
                "(closed=" + bestBruteClosed.ToString("F2") + "u).");

            // Runner covers ~3.3x the distance at speed 5 vs brute 1.5; a 1.5x margin is a
            // generous, robust lower bound for "clearly slower" at any frame cadence.
            Assert.Greater(bestRunnerMoved, bestBruteMoved * 1.5f,
                "A runner over the same window must cover clearly more ground than the brute " +
                "(runner=" + bestRunnerMoved.ToString("F2") + "u, brute=" +
                bestBruteMoved.ToString("F2") + "u).");

            // The brute must not have hurtled into the player already: slow approach means it
            // is STILL outside its melee band when the comparison ends.
            float bruteGapEnd = Vector3.Distance(brute.transform.position, player.transform.position);
            Assert.Greater(bruteGapEnd, brute.attackRange + 0.5f,
                "After the comparison window the brute must still be outside its slam reach " +
                "(gap=" + bruteGapEnd.ToString("F1") + "u, attackRange=" + brute.attackRange + "u).");
            Assert.IsFalse(brute.IsDead(), "Brute must survive the approach.");
            Assert.IsFalse(runner.IsDead(), "Runner control must survive the approach.");
            Assert.IsFalse(player.IsDead(), "Player must survive the approach.");

            KillEnemy(brute);
            KillEnemy(runner);
            GameTestAPI.SetPlayerHealth(100);
        }

        // ── ~0.5 s telegraph before the heavy slam (GAME_SPEC §4.3) ──

        [UnityTest]
        public IEnumerator Brute_Telegraph_PrepPauseAboutHalfSecond_BeforeSlamDealsConfiguredDamage()
        {
            yield return WaitWaveAssigned(1);

            ClearWaveEnemiesExcept(null);
            Assert.AreEqual(0, Wm.GetEnemiesAlive(), "Field must be clear of wave enemies.");

            PlayerController player = Player();
            Assert.IsNotNull(player, "Player required as the slam target.");
            GameTestAPI.SetPlayerHealth(100);
            Vector3 playerPos = NavMeshUtil.SnapToNavMesh(player.transform.position, 3f);

            // Park the brute just OUTSIDE attackRange (3 m) so it must step into range and
            // START its telegraph AFTER our observer is armed; the slam blast (radius 3 from a
            // point 1.5 m in front) still reaches the player at ~3 m engagement distance.
            Enemy brute = SpawnEnemyType("brute");
            TankBrute tank = brute as TankBrute;
            Assert.IsNotNull(tank, "Spawned 'brute' must be a TankBrute.");
            Vector3 park = NavMeshUtil.SnapToNavMesh(playerPos + Vector3.forward * 3.5f, 3f);
            brute.transform.position = park;
            yield return WaitPlacedOnNavMesh(brute);

            GameTestAPI.SetPlayerHealth(100);
            int lastHp = player.currentHealth;
            int slams = 0;
            bool wrongDelta = false;
            void OnHealth(int current, int max)
            {
                int delta = lastHp - current;
                lastHp = current;
                if (delta == tank.slamDamage)
                {
                    slams++;
                }
                else if (delta > 0)
                {
                    wrongDelta = true; // a player hit with an unexpected amount
                }
            }
            player.OnHealthChanged += OnHealth;

            // Gate: first frame the telegraph animation is on and record the wall-clock time,
            // then wait until the first slam damage lands while keeping the player topped up.
            float? telegraphStart = null;
            float? damageAt = null;
            const float timeout = 15f;
            float deadline = Time.realtimeSinceStartup + timeout;
            while (damageAt == null && Time.realtimeSinceStartup < deadline)
            {
                ClearWaveEnemiesExcept(brute);
                GameTestAPI.SetPlayerHealth(100);

                if (telegraphStart == null && tank.IsTelegraphing())
                    telegraphStart = Time.realtimeSinceStartup;

                if (slams > 0 && damageAt == null)
                    damageAt = Time.realtimeSinceStartup;

                yield return null;
            }
            player.OnHealthChanged -= OnHealth;

            Assert.IsNotNull(telegraphStart,
                "The brute must begin its telegraph (preparation pause) while within attack range.");
            Assert.IsNotNull(damageAt,
                "The telegraphed slam must land on the player (slam count=" + slams + ").");
            Assert.GreaterOrEqual(slams, 1,
                "The brute's slam must connect with the player at least once.");
            Assert.IsFalse(wrongDelta,
                "Every connecting brute slam must deal exactly the configured slam damage (" +
                tank.slamDamage + ").");

            float pause = damageAt.Value - telegraphStart.Value;
            Assert.Greater(pause, TelegraphMinSeconds,
                "The telegraph must be a real preparation pause, not an instant hit (pause=" +
                pause.ToString("F2") + "s).");
            Assert.LessOrEqual(pause, TelegraphMaxSeconds,
                "The telegraph pause must be ~0.5 s per GAME_SPEC §4.3 (pause=" +
                pause.ToString("F2") + "s).");

            Assert.IsFalse(player.IsDead(), "Player must survive the brute's slam.");
            Assert.IsFalse(brute.IsDead(), "Brute must survive the exchange.");

            KillEnemy(brute);
            GameTestAPI.SetPlayerHealth(100);
        }

        // ── Heavy attack: repeated slam connections at close range (GAME_SPEC §4.3) ──

        [UnityTest]
        public IEnumerator Brute_HeavyAttack_RepeatedSlamsAtCloseRange_DealExactlyConfiguredDamageEveryHit()
        {
            yield return WaitWaveAssigned(1);

            // Only the brute may damage the player during the measurement.
            ClearWaveEnemiesExcept(null);
            Assert.AreEqual(0, Wm.GetEnemiesAlive(), "Field must be clear of wave enemies.");

            PlayerController player = Player();
            Assert.IsNotNull(player, "Player required as the slam target.");
            GameTestAPI.SetPlayerHealth(100);
            Vector3 playerPos = NavMeshUtil.SnapToNavMesh(player.transform.position, 3f);

            // Park the brute 2 m from the player (inside attack range 3 m) before the enable
            // coroutine snaps it, so it can slam the moment its agent is placed and keep
            // re-telegraphing while the player stays in range.
            Enemy brute = SpawnEnemyType("brute");
            TankBrute tank = brute as TankBrute;
            Assert.IsNotNull(tank, "Spawned 'brute' must be a TankBrute.");
            Vector3 park = playerPos + Vector3.forward * 2f;
            if (!NavMeshUtil.TrySnapToNavMesh(park, 1f, out park))
                park = playerPos;
            brute.transform.position = park;
            yield return WaitPlacedOnNavMesh(brute);

            GameTestAPI.SetPlayerHealth(100);
            int lastHp = player.currentHealth;
            int slams = 0;
            bool wrongDelta = false;
            void OnHealth(int current, int max)
            {
                int delta = lastHp - current;
                lastHp = current;
                if (delta == tank.slamDamage)
                {
                    slams++;
                }
                else if (delta > 0)
                {
                    wrongDelta = true;
                }
            }
            player.OnHealthChanged += OnHealth;

            float deadline = Time.realtimeSinceStartup + 20f;
            while (slams < 2 && Time.realtimeSinceStartup < deadline)
            {
                ClearWaveEnemiesExcept(brute);
                GameTestAPI.SetPlayerHealth(100);
                yield return null;
            }
            player.OnHealthChanged -= OnHealth;

            Assert.GreaterOrEqual(slams, 2,
                "A brute held inside attack range must repeatedly connect its telegraphed " +
                "slam (slams=" + slams + ").");
            Assert.IsFalse(wrongDelta,
                "Every connecting brute slam must deal exactly the configured slam damage (" +
                tank.slamDamage + ").");
            Assert.IsFalse(player.IsDead(), "Player must survive the brute's repeated slams.");
            Assert.IsFalse(brute.IsDead(), "Brute must survive the exchange.");

            KillEnemy(brute);
            GameTestAPI.SetPlayerHealth(100);
        }

        // ── Distinct code-driven visual (GAME_SPEC §4 "Enemy visuals") ──

        [UnityTest]
        public IEnumerator Brute_Visual_DistinctDarkRedMaterial_ContrastsWithRunnerAndSoldier()
        {
            yield return BakeReady();

            Enemy brute = SpawnEnemyType("brute");
            Enemy runner = SpawnEnemyType("runner");
            Enemy soldier = SpawnEnemyType("soldier");
            yield return null; // Start() assigns each type's distinct material color

            Renderer bruteRend = brute.GetComponent<Renderer>();
            Renderer runnerRend = runner.GetComponent<Renderer>();
            Renderer soldierRend = soldier.GetComponent<Renderer>();
            Assert.IsNotNull(bruteRend, "Brute must carry a Renderer for its code-driven visual.");
            Assert.IsNotNull(runnerRend, "Runner must carry a Renderer.");
            Assert.IsNotNull(soldierRend, "Soldier must carry a Renderer.");

            // A brute spawned too close could already be in its telegraph flash (red↔yellow
            // lerp), which would corrupt the color read. Park it far away and let any in-flight
            // telegraph expire before sampling.
            PlayerController playerForShot = Player();
            Vector3 farPark = NavMeshUtil.SnapToNavMesh(
                playerForShot.transform.position + Vector3.right * 12f, 3f);
            brute.transform.position = farPark;
            yield return new WaitForSeconds(0.7f); // let a possible telegraph flash finish

            Color bruteColor = bruteRend.material.color;
            Color runnerColor = runnerRend.material.color;
            Color soldierColor = soldierRend.material.color;

            // The visual assignment is code-driven (TankBrute.Start): the suggested dark red.
            AssertColorNear(bruteColor, new Color(0.6f, 0f, 0f), 0.02f,
                "Brute must use the distinct dark-red visual from its Start().");

            // Cross-check the other two types keep their own colors so all three differ.
            AssertColorNear(runnerColor, Color.green, 0.02f, "Runner must keep its green visual.");
            AssertColorNear(soldierColor, new Color(1f, 0.6f, 0f), 0.02f,
                "Soldier must keep its orange visual.");

            Assert.Greater(ColorDistance(bruteColor, runnerColor), 0.5f,
                "Brute vs runner must be visually distinct (dark red vs green).");
            Assert.Greater(ColorDistance(bruteColor, soldierColor), 0.5f,
                "Brute vs soldier must be visually distinct (dark red vs orange).");

            // Runtime-oriented evidence: park the brute in front of the player camera and
            // capture a real in-game screenshot (TestResults/brute_milestone.png).
            GameTestAPI.SetPlayerHealth(100);
            Vector3 inFront = NavMeshUtil.SnapToNavMesh(
                playerForShot.transform.position + playerForShot.transform.forward * 5f, 3f);
            brute.transform.position = inFront;
            yield return null;

            Directory.CreateDirectory(TestResultsDir);
            string shotPath = Path.Combine(TestResultsDir, "brute_milestone.png");
            string saved = ScreenshotCapture.CaptureMain(shotPath, 1280, 720);
            Assert.IsNotNull(saved, "Screenshot capture must return a path.");
            Assert.IsTrue(File.Exists(saved), "Screenshot file must exist on disk: " + saved);
            Assert.That(new FileInfo(saved).Length, Is.GreaterThan(0),
                "Screenshot file must not be empty.");
            Debug.Log("[Brute] screenshot saved: " + saved);

            KillEnemy(brute);
            KillEnemy(runner);
            KillEnemy(soldier);
            GameTestAPI.SetPlayerHealth(100);
        }
    }
}