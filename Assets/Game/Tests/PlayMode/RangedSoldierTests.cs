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
    /// M12 — RangedSoldier dedicated PlayMode verification per GAME_SPEC.md §4.2 and
    /// TEST_PLAN.md "Enemies: Ranged Soldier":
    ///
    ///   - uses a RANGED attack (hitscan ray up to its 25 m attackRange — long beyond
    ///     every melee type); the damage-connection test proves the player takes the
    ///     configured 12 damage while the soldier stays clearly outside melee reach;
    ///   - attempts to maintain an ideal range (~preferredDistance 12 m) instead of
    ///     closing to melee like the runner — verified by the settle-distance bounds;
    ///   - attacks at intervals — cadence 1.5 s is slower than the runner's 0.8 s and
    ///     connections land with at least that period between them;
    ///   - repositions/strafes simply when the player is at the preferred band — it
    ///     strafes laterally and backs off when parked inside preferredDistance - 2;
    ///   - distinct visual — code-driven orange material in RangedSoldier.Start.
    ///
    /// Death / wave notification / NavMesh placement are the base Enemy framework
    /// contract verified in M10 and are NOT re-verified here — only the soldier-specific
    /// spin. Determinism follows the established patterns: wave-assignment gate
    /// (RunnerEnemyTests), synchronous wave clear, clear-every-other-enemy each frame so
    /// the aux soldier is the sole actor, deadline-based measurement off the "actively
    /// moving" gate, and the player kept topped up so no stray damage kills it mid-assert.
    /// </summary>
    public class RangedSoldierTests
    {
        const int LethalDamage = int.MaxValue;
        const float PreferredDistance = 12f;

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
            GameTestAPI.SetPlayerHealth(100);
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

        /// <summary>Kills every living enemy except <paramref name="keep"/> so the aux
        /// soldier is the ONLY actor that can damage the player or pollute measurement.</summary>
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

        /// <summary>Wipes the wave synchronously (gate guarantees assignment) and parks the
        /// aux soldier at <paramref name="offsetFromPlayer"/> before its enable coroutine
        /// re-snaps the transform, so the agent enables exactly where we placed it.</summary>
        static IEnumerator SetupSoleSoldier(Vector3 offsetFromPlayer)
        {
            ClearWaveEnemiesExcept(null);
            Assert.AreEqual(0, Wm.GetEnemiesAlive(), "Field must be clear of wave enemies.");
            Assert.IsFalse(Wm.IsWaveInProgress(), "Cleared wave must not be in progress.");

            PlayerController player = Player();
            Assert.IsNotNull(player, "Player required as the soldier's target.");
            GameTestAPI.SetPlayerHealth(100);
            Vector3 playerPos = NavMeshUtil.SnapToNavMesh(player.transform.position, 3f);

            Enemy soldier = SpawnEnemyType("soldier");
            Vector3 park = NavMeshUtil.SnapToNavMesh(playerPos + offsetFromPlayer, 3f);
            soldier.transform.position = park;
            yield return WaitPlacedOnNavMesh(soldier);

            Assert.IsFalse(soldier.IsDead(), "Soldier must survive setup.");
            Assert.IsFalse(player.IsDead(), "Player must survive setup.");
            Assert.AreEqual(Player().transform, soldier.player,
                "Soldier must acquire the actual player transform as its target.");
            GameTestAPI.SetPlayerHealth(100);
        }

        /// <summary>Waits until the soldier's agent is placed AND actively navigating
        /// (velocity &gt; 0), so measurement begins from a genuinely engaged soldier.</summary>
        static IEnumerator WaitEngagedMoving(Enemy soldier)
        {
            NavMeshAgent agent = soldier.GetComponent<NavMeshAgent>();
            Assert.IsNotNull(agent, "Soldier must carry a NavMeshAgent.");
            yield return GameBootstrap.WaitUntil(
                () => agent.enabled && agent.isOnNavMesh && agent.velocity.sqrMagnitude > 0.04f, 8f);
            Assert.IsTrue(agent.enabled && agent.isOnNavMesh && agent.velocity.sqrMagnitude > 0.04f,
                "Soldier must be placed on the NavMesh and actively navigating before measurement.");
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

        // ── Ranged attack connecting at distance (GAME_SPEC §4.2) ──────────

        [UnityTest]
        public IEnumerator Soldier_RangedAttack_HitsPlayerAtDistanceBeyondMelee_AtIntervalCadence()
        {
            yield return WaitWaveAssigned(1);

            // Sole soldier ~14 m behind the player: inside attackRange (25 m) and clearly
            // far beyond ANY melee reach (~1.8–3 m). Ranged = the player can be damaged
            // while the soldier stays out of melee range.
            yield return SetupSoleSoldier(new Vector3(0f, 0f, 14f));
            Enemy soldier = FindSoleSoldier();
            GameTestAPI.SetPlayerHealth(100);

            yield return WaitEngagedMoving(soldier);

            PlayerController player = Player();
            GameTestAPI.SetPlayerHealth(100);

            int hits = 0;
            int lastHp = player.currentHealth;
            float earliestHitDistance = -1f;
            float latestHitTime = -1f;
            float cadenceBreak = -1f;
            float deadline = Time.realtimeSinceStartup + 25f;

            while (hits < 3 && Time.realtimeSinceStartup < deadline)
            {
                // Sweep any wave respawn before it can reach the player (keeps the aux
                // soldier the only damage source and stops wave cooldowns from resetting).
                ClearWaveEnemiesExcept(soldier);

                float soldierDist = Vector3.Distance(soldier.transform.position, player.transform.position);
                int hpNow = player.currentHealth;
                int delta = lastHp - hpNow;
                lastHp = hpNow;

                if (delta == soldier.attackDamage)
                {
                    if (hits == 0) earliestHitDistance = soldierDist;
                    if (latestHitTime >= 0f && cadenceBreak < 0f)
                        cadenceBreak = Time.realtimeSinceStartup - latestHitTime;
                    latestHitTime = Time.realtimeSinceStartup;
                    hits++;
                    Assert.GreaterOrEqual(soldierDist, 6f,
                        "The ranged attack must connect while the soldier is beyond melee reach " +
                        "(soldierDist=" + soldierDist.ToString("F1") + "u at connection).");
                }

                GameTestAPI.SetPlayerHealth(100);
                yield return null;
            }

            Assert.GreaterOrEqual(hits, 2,
                "A soldier kept inside its 25 m attack range must repeatedly connect its ranged " +
                "attack (hits=" + hits + ").");
            Assert.GreaterOrEqual(earliestHitDistance, 6f,
                "Every connecting hit must land from beyond melee reach (first hit at " +
                earliestHitDistance.ToString("F1") + "u).");
            if (cadenceBreak >= 0f)
            {
                Assert.GreaterOrEqual(cadenceBreak, soldier.attackRate * 0.9f,
                    "Connecting attacks must respect the soldier's interval cadence (attackRate=" +
                    soldier.attackRate + "s, gap=" + cadenceBreak.ToString("F2") + "s).");
            }
            Assert.IsFalse(player.IsDead(), "Player must survive the soldier's ranged fire.");
            Assert.IsFalse(soldier.IsDead(), "Soldier must survive the exchange.");

            KillEnemy(soldier);
            GameTestAPI.SetPlayerHealth(100);
        }

        // ── Ideal-range maintenance (GAME_SPEC §4.2) ───────────────────────

        [UnityTest]
        public IEnumerator Soldier_IdealRange_ParkedTooClose_BacksOffAndHoldsRangeInsteadOfRushingMelee()
        {
            yield return WaitWaveAssigned(1);

            // Parked 8 m from the player (preferredDistance − 4): inside preferredDistance − 2
            // so the soldier's range-keeping must back away rather than close to melee.
            yield return SetupSoleSoldier(new Vector3(0f, 0f, 8f));
            Enemy soldier = FindSoleSoldier();
            GameTestAPI.SetPlayerHealth(100);

            yield return WaitEngagedMoving(soldier);

            PlayerController player = Player();
            GameTestAPI.SetPlayerHealth(100);
            float soldierAnchor = Vector3.Distance(soldier.transform.position, player.transform.position);

            // Back-off can add up to ~3 m in one navigate step; allow the agent time to
            // actually traverse before sampling the settled band.
            float warmup = Time.realtimeSinceStartup + 2.5f;
            while (Time.realtimeSinceStartup < warmup)
            {
                ClearWaveEnemiesExcept(soldier);
                GameTestAPI.SetPlayerHealth(100);
                yield return null;
            }

            float minDist = float.MaxValue;
            float maxDist = 0f;
            float sumDist = 0f;
            int samples = 0;
            float measureUntil = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < measureUntil)
            {
                ClearWaveEnemiesExcept(soldier);
                GameTestAPI.SetPlayerHealth(100);

                float d = Vector3.Distance(soldier.transform.position, player.transform.position);
                if (d < minDist) minDist = d;
                if (d > maxDist) maxDist = d;
                sumDist += d;
                samples++;
                yield return null;
            }
            float meanDist = samples > 0 ? sumDist / samples : 0f;

            // GAME_SPEC §4.2: "tries to maintain an ideal range" — the soldier holds a
            // couple of metres around preferredDistance and NEVER closes to melee.
            Assert.Greater(minDist, 4f,
                "The soldier must never collapse to melee range (minDist=" + minDist.ToString("F1") + "u).");
            Assert.GreaterOrEqual(meanDist, PreferredDistance - 4f,
                "The soldier must hold an engaging distance near its preferred value " +
                "(meanDist=" + meanDist.ToString("F1") + "u).");
            Assert.LessOrEqual(meanDist, PreferredDistance + 6f,
                "The soldier must not run away from the player beyond its band " +
                "(meanDist=" + meanDist.ToString("F1") + "u).");
            Assert.Greater(meanDist, 4f,
                "Back-off behavior must re-establish range, not stand at melee " +
                "(soldierAnchor=" + soldierAnchor.ToString("F1") + "u).");

            KillEnemy(soldier);
            GameTestAPI.SetPlayerHealth(100);
        }

        // ── Strafe / simple repositioning at the preferred band (GAME_SPEC §4.2) ──

        [UnityTest]
        public IEnumerator Soldier_Strafe_PlayerAtPreferredBand_MovesLaterallyWithoutClosingOrFleeing()
        {
            yield return WaitWaveAssigned(1);

            // Parked exactly at preferredDistance (12 m): inside the [preferred−2, preferred+2]
            // strafe band, so the soldier must move LATERALLY (perpendicular to the aim line)
            // instead of closing to melee or fleeing.
            yield return SetupSoleSoldier(new Vector3(12f, 0f, 0f));
            Enemy soldier = FindSoleSoldier();
            GameTestAPI.SetPlayerHealth(100);

            yield return WaitEngagedMoving(soldier);

            PlayerController player = Player();
            GameTestAPI.SetPlayerHealth(100);

            // Anchor the aim line at the soldier's initial position: perpendicular travel is
            // measured against this fixed line so a partial closing drift cannot count as strafe.
            Vector3 anchor = soldier.transform.position;
            Vector3 toPlayer = (player.transform.position - anchor).normalized;
            Vector3 perpAxis = Vector3.Cross(Vector3.up, toPlayer).normalized;

            float bestPerp = 0f;
            float minDist = float.MaxValue;
            float sumDist = 0f;
            int samples = 0;
            float measureUntil = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < measureUntil)
            {
                ClearWaveEnemiesExcept(soldier);
                GameTestAPI.SetPlayerHealth(100);

                Vector3 offset = soldier.transform.position - anchor;
                float perp = Mathf.Abs(Vector3.Dot(offset, perpAxis));
                if (perp > bestPerp) bestPerp = perp;

                float d = Vector3.Distance(soldier.transform.position, player.transform.position);
                if (d < minDist) minDist = d;
                sumDist += d;
                samples++;
                yield return null;
            }
            float meanDist = samples > 0 ? sumDist / samples : 0f;

            Assert.Greater(bestPerp, 0.6f,
                "A soldier held at its preferred band must strafe laterally to reposition " +
                "(bestPerp=" + bestPerp.ToString("F2") + "u across 5.0s).");
            Assert.Greater(minDist, 4f,
                "Strafing must not collapse the soldier to melee range (minDist=" +
                minDist.ToString("F1") + "u).");
            Assert.GreaterOrEqual(meanDist, PreferredDistance - 4f,
                "Strafing must hold the ideal-range band, not run away (meanDist=" +
                meanDist.ToString("F1") + "u).");
            Assert.LessOrEqual(meanDist, PreferredDistance + 4f,
                "Strafing must hold the ideal-range band, not close (meanDist=" +
                meanDist.ToString("F1") + "u).");

            KillEnemy(soldier);
            GameTestAPI.SetPlayerHealth(100);
        }

        // ── Distinct config + visual (GAME_SPEC §4.2, §4 "Enemy visuals") ──

        [UnityTest]
        public IEnumerator Soldier_Config_MediumHealthDamageSpeed_RangedLongerAndCadenceBetweenOthers()
        {
            yield return BakeReady();

            Enemy soldier = SpawnEnemyType("soldier");
            Enemy runner = SpawnEnemyType("runner");
            Enemy brute = SpawnEnemyType("brute");
            yield return null; // run Start(): player target + per-type visuals/scale

            RangedSoldier soldierR = soldier as RangedSoldier;
            Assert.IsNotNull(soldierR, "Spawned 'soldier' must be a RangedSoldier.");

            // Medium health (GAME_SPEC §4.2): 60, between the runner's 30 and brute's 200.
            Assert.AreEqual(60, soldier.maxHealth, "Soldier must declare medium health (60).");
            Assert.AreEqual(soldier.maxHealth, soldier.GetHealth(), "Soldier must spawn at full health.");
            Assert.Greater(soldier.maxHealth, runner.maxHealth,
                "Soldier must be tougher than the runner.");
            Assert.Less(soldier.maxHealth, brute.maxHealth,
                "Soldier must be weaker than the brute.");

            // Medium speed: 3.5, between runner 5 and brute 1.5; agent follows config.
            Assert.AreEqual(3.5f, soldier.moveSpeed, 0.001f, "Soldier must declare medium speed (3.5).");
            Assert.Less(soldier.moveSpeed, runner.moveSpeed, "Soldier must be slower than the runner.");
            Assert.Greater(soldier.moveSpeed, brute.moveSpeed, "Soldier must outpace the brute.");
            NavMeshAgent agent = soldier.GetComponent<NavMeshAgent>();
            Assert.IsNotNull(agent, "Soldier must carry a NavMeshAgent.");
            Assert.AreEqual(soldier.moveSpeed, agent.speed, 0.001f,
                "Soldier agent speed must follow its moveSpeed config.");

            // Ranged damage / range: medium damage (12) delivered to far range (25 m) —
            // the behavioral difference from the melee types.
            Assert.AreEqual(12, soldier.attackDamage, "Soldier must declare medium damage (12).");
            Assert.AreEqual(25f, soldier.attackRange, 0.001f,
                "Soldier must declare the long 25 m attack range of its gun.");
            Assert.Greater(soldier.attackRange, runner.attackRange,
                "Soldier must outrange the melee runner.");
            Assert.Greater(soldier.attackRange, brute.attackRange,
                "Soldier must outrange the melee brute.");

            // Interval cadence: 1.5 s — slower than the runner's fast melee, faster than the brute.
            Assert.AreEqual(1.5f, soldier.attackRate, 0.001f, "Soldier must declare a 1.5 s interval.");
            Assert.Greater(soldier.attackRate, runner.attackRate,
                "Soldier must attack slower than the runner (interval, not fast melee).");
            Assert.Less(soldier.attackRate, brute.attackRate,
                "Soldier must attack faster than the brute.");

            // Ideal-range + strafe knobs: the soldier explicitly tracks a preferred
            // engagement distance and a strafe speed (GAME_SPEC §4.2).
            Assert.AreEqual(PreferredDistance, soldierR.preferredDistance, 0.001f,
                "Soldier must declare an ideal engagement distance (12 m).");
            Assert.AreEqual(2f, soldierR.strafeSpeed, 0.001f,
                "Soldier must declare a strafe speed for repositioning.");
            Assert.Greater(soldier.detectionRange, soldier.attackRange,
                "Soldier must detect the player slightly beyond its gun range.");
            Assert.Greater(soldier.detectionRange, PreferredDistance,
                "Soldier's detection must reach its preferred band.");

            // Targets the player: Enemy.Start resolved the actual player transform.
            Assert.IsNotNull(soldier.player, "Soldier must acquire the player target after Start.");
            Assert.AreEqual(Player().transform, soldier.player,
                "Soldier's target must be the actual player transform.");

            KillEnemy(soldier);
            KillEnemy(runner);
            KillEnemy(brute);
            GameTestAPI.SetPlayerHealth(100);
        }

        [UnityTest]
        public IEnumerator Soldier_Visual_DistinctOrangeMaterial_ContrastsWithRunnerAndBrute()
        {
            yield return BakeReady();

            Enemy soldier = SpawnEnemyType("soldier");
            Enemy runner = SpawnEnemyType("runner");
            Enemy brute = SpawnEnemyType("brute");
            yield return null; // Start() assigns each type's distinct material color

            Renderer soldierRend = soldier.GetComponent<Renderer>();
            Renderer runnerRend = runner.GetComponent<Renderer>();
            Renderer bruteRend = brute.GetComponent<Renderer>();
            Assert.IsNotNull(soldierRend, "Soldier must carry a Renderer for its code-driven visual.");
            Assert.IsNotNull(runnerRend, "Runner must carry a Renderer.");
            Assert.IsNotNull(bruteRend, "Brute must carry a Renderer.");

            Color soldierColor = soldierRend.material.color;
            Color runnerColor = runnerRend.material.color;
            Color bruteColor = bruteRend.material.color;

            // The visual assignment is code-driven (RangedSoldier.Start): the suggested orange.
            AssertColorNear(soldierColor, new Color(1f, 0.6f, 0f), 0.02f,
                "Soldier must use the distinct orange visual from its Start().");

            // Cross-check the other two types keep their own colors so all three differ.
            AssertColorNear(runnerColor, Color.green, 0.02f, "Runner must keep its green visual.");
            AssertColorNear(bruteColor, new Color(0.6f, 0f, 0f), 0.02f,
                "Brute must keep its dark-red visual.");

            Assert.Greater(ColorDistance(soldierColor, runnerColor), 0.5f,
                "Soldier vs runner must be visually distinct (orange vs green).");
            Assert.Greater(ColorDistance(soldierColor, bruteColor), 0.5f,
                "Soldier vs brute must be visually distinct (orange vs dark red).");

            // Runtime-oriented evidence: park the soldier in front of the player camera
            // and capture a real in-game screenshot (TestResults/ranged_soldier_milestone.png).
            GameTestAPI.SetPlayerHealth(100);
            PlayerController playerForShot = Player();
            Vector3 inFront = NavMeshUtil.SnapToNavMesh(
                playerForShot.transform.position + playerForShot.transform.forward * 6f, 3f);
            soldier.transform.position = inFront;
            yield return null;

            Directory.CreateDirectory(TestResultsDir);
            string shotPath = Path.Combine(TestResultsDir, "ranged_soldier_milestone.png");
            string saved = ScreenshotCapture.CaptureMain(shotPath, 1280, 720);
            Assert.IsNotNull(saved, "Screenshot capture must return a path.");
            Assert.IsTrue(File.Exists(saved), "Screenshot file must exist on disk: " + saved);
            Assert.That(new FileInfo(saved).Length, Is.GreaterThan(0),
                "Screenshot file must not be empty.");
            Debug.Log("[RangedSoldier] screenshot saved: " + saved);

            KillEnemy(soldier);
            KillEnemy(runner);
            KillEnemy(brute);
            GameTestAPI.SetPlayerHealth(100);
        }

        // ── Helper that sets up a sole soldier (carved out of the test bodies above) ──

        static Enemy FindSoleSoldier()
        {
            Enemy[] enemies = Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None);
            for (int i = 0; i < enemies.Length; i++)
            {
                RangedSoldier soldier = enemies[i] as RangedSoldier;
                if (soldier != null && !soldier.IsDead())
                    return soldier;
            }
            Assert.Fail("The sole aux RangedSoldier must be alive after setup.");
            return null;
        }
    }
}