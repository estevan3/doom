using System.Collections;
using System.Collections.Generic;
using DoomClone.Automation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace DoomClone.Tests.PlayMode
{
    /// <summary>
    /// M14 — WaveManager timing and state-transition verification per GAME_SPEC.md §6
    /// and TEST_PLAN.md "Waves":
    ///
    ///   1. initial delay: exactly ~2 s of scaled time before Wave 1 starts while NOTHING
    ///      (no wave flag, no spawned enemy) exists before it;
    ///   2. alive-enemy tracking matches the actual living set and killing one non-final
    ///      enemy does not end the wave;
    ///   3. final-enemy death starts the 5 s cooldown (same frame), the HUD shows a clear
    ///      countdown that must tick exactly 5 → 4 → 3 → 2 → 1, and Wave 2 then starts
    ///      automatically with the whole cooldown measuring ~5 s;
    ///   4. difficulty scales progressively across waves;
    ///   5. player death during a cooldown stops the wave loop permanently (Game Over).
    ///
    /// Timing is measured in scaled time (Time.time) because the production loop uses
    /// WaitForSeconds. Frame-quantization tolerances follow the milestones before: event-to-
    /// event windows (final-death → next wave) are robust at any frame rate while t0-based
    /// anchors (initial delay) tolerate a ≤1-frame readback lag from the level-ready frame.
    /// </summary>
    public class WaveManagerTests
    {
        const int LethalDamage = int.MaxValue;

        // Expected per-wave total spawns derived deterministically from the production
        // tunables (baseEnemiesPerWave=5, enemyScalePerWave=1.3, ratios 0.7 zombie / 0.3
        // soldier / 0 brute before wave 4):
        //     wave 1: RoundToInt(5)        → 4 zombies + 2 soldiers = 6
        //     wave 2: RoundToInt(6.5)=6    → 4 zombies + 2 soldiers = 6
        //     wave 3: RoundToInt(8.45)=8   → 6 zombies + 2 soldiers = 8
        // A deliberate balance change to those tunables (M15) MUST update these expectations.
        static readonly IReadOnlyList<int> ExpectedWaveTotals = new[] { 6, 6, 8 };

        static WaveManager Wm => WaveManager.Instance;

        static PlayerController PlayerCtrl() => GameTestAPI.GetPlayer();

        static PlayerHUD Hud() => Object.FindAnyObjectByType<PlayerHUD>();

        static IEnumerator WaitBakeReady()
        {
            yield return GameBootstrap.WaitUntil(
                () => NavMesh.CalculateTriangulation().vertices.Length > 0, 10f);
            yield return new WaitForSeconds(0.5f);
        }

        /// <summary>Waits until the given wave is fully spawned AND assigned, healing the
        /// player every frame so incidental wave damage cannot end the scenario.
        /// enemiesAlive is only written after the spawn loop, so alive &gt; 0 implies the
        /// complete wave was counted first (same gate as EnemyFrameworkTests).</summary>
        static IEnumerator WaitWaveFullyAssigned(int wave, int atLeastAlive = 2)
        {
            yield return WaitBakeReady();
            float deadline = Time.realtimeSinceStartup + 25f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (Wm != null && Wm.GetCurrentWave() == wave && Wm.GetEnemiesAlive() >= atLeastAlive)
                    break;
                GameTestAPI.SetPlayerHealth(100);
                yield return null;
            }
            Assert.IsNotNull(Wm, "WaveManager must exist.");
            Assert.GreaterOrEqual(Wm.GetCurrentWave(), wave,
                "Wave " + wave + " must be reached (got " + Wm.GetCurrentWave() + ").");
            Assert.GreaterOrEqual(Wm.GetEnemiesAlive(), atLeastAlive,
                "Wave " + wave + " must be fully spawned and assigned before the scenario continues.");
            GameTestAPI.SetPlayerHealth(100);
        }

        static void KillAllLivingEnemies()
        {
            Enemy[] enemies = Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None);
            for (int i = 0; i < enemies.Length; i++)
            {
                if (enemies[i] != null && !enemies[i].IsDead())
                    enemies[i].TakeDamage(LethalDamage);
            }
        }

        static int CountLivingEnemies()
        {
            int count = 0;
            Enemy[] enemies = Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None);
            for (int i = 0; i < enemies.Length; i++)
            {
                if (enemies[i] != null && !enemies[i].IsDead()) count++;
            }
            return count;
        }

        /// <summary>Reads the seconds value from the HUD countdown text
        /// ("Next wave in: {n}..."), asserting the label is clear and the value positive.</summary>
        static int ReadCountdownSeconds(PlayerHUD hud)
        {
            string text = hud.waveCountdownText.text;
            Assert.IsTrue(text.StartsWith("Next wave in:"),
                "HUD countdown must begin with a clear label, got: '" + text + "'");
            Assert.IsTrue(text.EndsWith("..."),
                "HUD countdown must display a running value, got: '" + text + "'");
            int colon = text.IndexOf(':');
            string digits = text.Substring(colon + 1).Replace(".", "").Trim();
            Assert.IsTrue(int.TryParse(digits, out int seconds) && seconds > 0,
                "HUD countdown must contain a positive integer seconds value, got: '" + text + "'");
            return seconds;
        }

        [UnitySetUp]
        public IEnumerator Setup()
        {
            TestInputDevices.EnsureDevices();
            // A previous test may have left synthetic input pressed; reset it so no stray
            // fire/movement disturbs wave timing or enemy behavior assertions.
            TestInputDevices.SetFireButton(false);
            TestInputDevices.ReleaseAllKeys();
            // No trailing extra yield after StartFreshLevel: the test body must sample t0 on
            // the frame StartFreshLevel returns (level-ready + ≤1 frame lag), keeping the
            // "no wave before ~2 s" lower bound tight.
            yield return GameTestAPI.StartFreshLevel(30f);
            GameTestAPI.SetPlayerHealth(100);
        }

        // ── Initial delay (GAME_SPEC §6.1–§6.3) ───────────────────────────

        [UnityTest]
        public IEnumerator WaveManager_InitialDelay_Wave1StartsAfterTwoSeconds_NotBefore()
        {
            // t0 is sampled on the level-ready frame (± 1 frame readback lag). The 2 s timer
            // uses scaled time, matching WaitForSeconds(initialDelay) in StartFirstWave.
            float t0 = Time.time;

            Assert.AreEqual(0, Wm.GetCurrentWave(),
                "No wave may exist at the moment the level is ready.");
            Assert.AreEqual(0, Wm.GetEnemiesAlive(),
                "No enemy may exist at the moment the level is ready.");
            Assert.IsFalse(Wm.IsWaveInProgress(),
                "No wave may be in progress before the initial delay elapses.");

            float tFirst = -1f;
            float deadline = t0 + 8f;
            while (Time.time < deadline)
            {
                if (Wm.GetCurrentWave() >= 1)
                {
                    tFirst = Time.time;
                    break;
                }
                // While Wave 1 has not officially started, NOTHING may have spawned.
                Assert.AreEqual(0, Wm.GetEnemiesAlive(),
                    "No enemy may spawn before the wave officially starts (2 s delay).");
                Assert.IsFalse(Wm.IsWaveInProgress(),
                    "Enemy spawns must be gated behind the wave being in progress.");
                yield return null;
            }

            Assert.Greater(tFirst, 0f, "Wave 1 must start within 8 s of level-ready.");
            float measured = tFirst - t0;
            Assert.GreaterOrEqual(measured, 0.9f,
                "Wave 1 must NOT start before ~2 s of the initial delay (measured " +
                measured.ToString("F2") + " s from the ready frame).");
            Assert.LessOrEqual(measured, 4.0f,
                "Wave 1 must start around the 2 s delay within frame-scheduling tolerance " +
                "(measured " + measured.ToString("F2") + " s).");
        }

        // ── Alive-enemy tracking (GAME_SPEC §6.4) ──────────────────────────

        [UnityTest]
        public IEnumerator WaveManager_AliveCount_TracksLivingSet_SingleKillDoesNotEndWave()
        {
            yield return WaitWaveFullyAssigned(1, 2);

            Assert.AreEqual(CountLivingEnemies(), Wm.GetEnemiesAlive(),
                "WaveManager alive count must equal the actual living-enemy set once the wave is assigned.");

            Enemy target = null;
            Enemy[] enemies = Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None);
            for (int i = 0; i < enemies.Length; i++)
            {
                if (enemies[i] != null && !enemies[i].IsDead())
                {
                    target = enemies[i];
                    break;
                }
            }
            Assert.IsNotNull(target, "A living tracked enemy is required.");
            Assert.GreaterOrEqual(Wm.GetEnemiesAlive(), 2,
                "Need a wave with >= 2 living enemies so this kill cannot end the wave.");

            int aliveBefore = Wm.GetEnemiesAlive();
            int livingBefore = CountLivingEnemies();

            // All assertions in this block are synchronous with the kill: the final-death
            // cooldown can only start if this was the last enemy, which it is not.
            target.TakeDamage(LethalDamage);

            int aliveAfter = Wm.GetEnemiesAlive();
            Assert.AreEqual(aliveBefore - 1, aliveAfter,
                "Killing one tracked wave enemy must decrement the alive count by exactly 1 " +
                "(" + aliveBefore + " -> " + aliveAfter + ").");
            Assert.AreEqual(livingBefore - 1, CountLivingEnemies(),
                "The dead enemy must immediately leave the living set.");
            Assert.AreEqual(aliveAfter, CountLivingEnemies(),
                "Remaining tracked count must match the remaining living set.");
            Assert.IsTrue(Wm.IsWaveInProgress(),
                "Killing a single non-final enemy must NOT end the wave; the 5 s cooldown " +
                "may only start on the last death.");

            KillAllLivingEnemies();
            GameTestAPI.SetPlayerHealth(100);
        }

        // ── Final death → 5 s cooldown → HUD countdown → auto next wave (GAME_SPEC §6.5–§6.7)

        [UnityTest]
        public IEnumerator WaveManager_FinalDeath_StartsCooldown_Countdown5To1_NextWaveAutoStarts()
        {
            yield return WaitWaveFullyAssigned(1, 2);
            PlayerHUD hud = Hud();
            Assert.IsNotNull(hud, "PlayerHUD must exist to verify the countdown.");

            // Kill the entire wave synchronously. The final death starts the cooldown
            // coroutine in the same frame (coroutine body runs to its first yield), so all
            // post-clear state and the first countdown value must already be observable.
            KillAllLivingEnemies();
            float tDeath = Time.time;

            Assert.AreEqual(0, Wm.GetEnemiesAlive(),
                "Clearing the final wave enemy must reset the tracked alive count to 0.");
            Assert.AreEqual(0, CountLivingEnemies(),
                "No enemy may survive the final-wave clear.");
            Assert.IsFalse(Wm.IsWaveInProgress(),
                "An emptied wave is not in progress; the cooldown state has begun.");

            // First countdown tick lands in the same frame as the final death.
            List<int> countdownSequence = new List<int>();
            countdownSequence.Add(ReadCountdownSeconds(hud));
            Assert.IsTrue(hud.waveCountdownText.gameObject.activeSelf,
                "The HUD countdown must be visible during the inter-wave cooldown.");

            float tNext = -1f;
            float deadline = tDeath + 10f;
            while (Time.time < deadline)
            {
                if (Wm.GetCurrentWave() >= 2)
                {
                    tNext = Time.time;
                    break;
                }
                // During the whole cooldown nothing may spawn and the countdown may not
                // disappear early.
                Assert.AreEqual(0, Wm.GetEnemiesAlive(),
                    "No enemy may spawn during the inter-wave cooldown.");
                Assert.AreEqual(0, CountLivingEnemies(),
                    "No enemy object may exist during the inter-wave cooldown.");
                Assert.IsTrue(hud.waveCountdownText.gameObject.activeSelf,
                    "The countdown must remain visible for the full cooldown.");

                if (hud.waveCountdownText.text != "")
                {
                    int seconds = ReadCountdownSeconds(hud);
                    if (seconds != countdownSequence[countdownSequence.Count - 1])
                    {
                        countdownSequence.Add(seconds);
                    }
                }
                GameTestAPI.SetPlayerHealth(100);
                yield return null;
            }

            Assert.Greater(tNext, 0f, "Wave 2 must start automatically after the cooldown.");

            // The structural proof of the 5 second mechanic: exactly five distinct ticks in
            // strict descending order 5,4,3,2,1 (one value change per WaveManager frame).
            int[] expected = { 5, 4, 3, 2, 1 };
            Assert.AreEqual(expected, countdownSequence.ToArray(),
                "The HUD countdown must tick exactly 5 -> 4 -> 3 -> 2 -> 1 (got [" +
                string.Join(",", countdownSequence) + "]).");

            // The duration proof is the total span between two in-game events: the final-death
            // frame (tDeath, exact — the first countdown tick is shown synchronously with the
            // last kill) and the first frame the next wave is observed. StartNextWave can only
            // run after five 1 s scaled waits, so this span is structurally >= 5.0 s; the upper
            // bound absorbs frame scheduling. No per-tick wall-clock assertion is made because a
            // test-frame readback lag (up to one WaveManager frame per tick) with variable frame
            // lengths can squeeze a sampled gap below the true 1 s display gap.
            float cooldownSpan = tNext - tDeath;
            Assert.GreaterOrEqual(cooldownSpan, 4.5f,
                "The end-of-wave cooldown must last ~5 seconds, never less (" +
                cooldownSpan.ToString("F2") + " s between final death and Wave 2).");
            Assert.LessOrEqual(cooldownSpan, 8.0f,
                "The end-of-wave cooldown must complete around 5 seconds within frame " +
                "scheduling tolerance (" + cooldownSpan.ToString("F2") + " s).");

            Assert.IsFalse(hud.waveCountdownText.gameObject.activeSelf,
                "The countdown must be hidden once the next wave starts.");
            Assert.AreEqual(2, Wm.GetCurrentWave(),
                "The next wave must begin automatically at the end of the cooldown.");

            KillAllLivingEnemies();
            GameTestAPI.SetPlayerHealth(100);
        }

        // ── Progressive difficulty (GAME_SPEC §6.9) ───────────────────────

        [UnityTest]
        public IEnumerator WaveManager_Difficulty_ProgressiveScaling_AcrossEarlyWaves()
        {
            int[] totals = new int[ExpectedWaveTotals.Count];

            for (int wave = 1; wave <= ExpectedWaveTotals.Count; wave++)
            {
                yield return WaitWaveFullyAssigned(wave, 2);
                totals[wave - 1] = Wm.GetEnemiesAlive();
                GameTestAPI.SetPlayerHealth(100);

                if (wave < ExpectedWaveTotals.Count)
                    KillAllLivingEnemies(); // last death starts the 5 s cooldown to the next wave
            }

            // Exact per-wave totals (from baseEnemiesPerWave=5, enemyScalePerWave=1.3, and the
            // early-wave ratio split). Documented in DECISIONS.md 2026-09-16 — a deliberate
            // rebalance must update ExpectedWaveTotals.
            for (int wave = 0; wave < ExpectedWaveTotals.Count; wave++)
            {
                Assert.AreEqual(ExpectedWaveTotals[wave], totals[wave],
                    "Wave " + (wave + 1) + " must spawn the configured progressive total " +
                    "(base x 1.3^(wave-1) composed by ratio), got " + totals[wave] + ".");
            }

            // Structural monotonicity independent of exact tuning: later waves are not easier.
            Assert.GreaterOrEqual(totals[1], totals[0],
                "Wave 2 must carry at least as many enemies as Wave 1.");
            Assert.GreaterOrEqual(totals[2], totals[1],
                "Wave 3 must carry at least as many enemies as Wave 2.");
            Assert.Greater(totals[2], totals[0],
                "Difficulty must rise progressively: Wave 3 must clearly out-scale Wave 1.");

            KillAllLivingEnemies();
            GameTestAPI.SetPlayerHealth(100);
        }

        // ── Player death stops the loop (GAME_SPEC §6.10, §7) ─────────────

        [UnityTest]
        public IEnumerator WaveManager_PlayerDeath_DuringCooldown_StopsFutureWaveSpawns()
        {
            yield return WaitWaveFullyAssigned(1, 2);
            PlayerHUD hud = Hud();
            Assert.IsNotNull(hud, "PlayerHUD must exist to verify the Game Over screen.");

            // Kill the wave → cooldown starts; then kill the player mid-cooldown. The wave
            // loop must stop permanently: Wave 2 may never arrive, no enemy may ever spawn,
            // and the countdown panel must be cleared behind the Game Over screen.
            KillAllLivingEnemies();
            Assert.IsFalse(Wm.IsWaveInProgress(), "The cleared wave must drop out of progress.");

            GameTestAPI.SetPlayerHealth(0);
            Assert.IsTrue(PlayerCtrl().IsDead(), "Player must be dead after SetHealth(0).");
            Assert.AreEqual(0, Wm.GetEnemiesAlive(),
                "Player death must leave no tracked enemies alive.");
            Assert.IsFalse(Wm.IsWaveInProgress(),
                "Player death must stop the active wave loop (GAME_SPEC §7).");

            Assert.IsTrue(hud.gameOverText.gameObject.activeSelf,
                "Game Over UI must appear on player death.");
            Assert.AreEqual("GAME OVER", hud.gameOverText.text,
                "Game Over UI must show the Game Over label.");
            Assert.IsTrue(hud.waveSurvivedText.gameObject.activeSelf,
                "Game Over must display the survived-wave count.");
            Assert.AreEqual("Survived 1 waves", hud.waveSurvivedText.text,
                "Game Over must show the number of waves survived (1).");
            Assert.IsFalse(hud.waveCountdownText.gameObject.activeSelf,
                "The inter-wave countdown must be hidden behind Game Over.");

            // gameActive is private; its observable contract is that spawns never occur —
            // SpawnEnemyByType returns null once the game is over.
            Assert.IsNull(GameTestAPI.SpawnEnemy("runner"),
                "No enemy may be spawned after player death (gameActive must be false).");

            float deadline = Time.realtimeSinceStartup + 7.5f;
            while (Time.realtimeSinceStartup < deadline)
            {
                Assert.AreEqual(1, Wm.GetCurrentWave(),
                    "No future wave may start after player death (Wave 2 must never arrive).");
                Assert.AreEqual(0, Wm.GetEnemiesAlive(),
                    "No new enemy may be tracked into a wave after player death.");
                Assert.AreEqual(0, CountLivingEnemies(),
                    "No enemy may ever spawn after player death.");
                Assert.IsTrue(PlayerCtrl().IsDead(),
                    "Player must remain dead during Game Over.");
                yield return null;
            }

            Assert.IsTrue(hud.gameOverText.gameObject.activeSelf,
                "Game Over UI must persist until the level is restarted.");
        }
    }
}