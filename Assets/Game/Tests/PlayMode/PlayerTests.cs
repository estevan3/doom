using System.Collections;
using System.IO;
using DoomClone.Automation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace DoomClone.Tests.PlayMode
{
    /// <summary>
    /// M4 — Player + HUD PlayMode verification per GAME_SPEC.md §2 and
    /// TEST_PLAN.md "Player" section. Uses direct InputSystem event injection
    /// through TestInputDevices (not InputTestFixture) so the game's
    /// DontDestroyOnLoad PlayerInputActions stays intact across test restarts.
    /// </summary>
    public class PlayerTests
    {
        const string Level = GameTestAPI.LevelSceneName;

        static readonly string TestResultsDir =
            Path.Combine(Directory.GetCurrentDirectory(), "TestResults");

        static void EnsureInputDevices() => TestInputDevices.EnsureDevices();

        static void PressKeys(params Key[] keys) => TestInputDevices.PressKeys(keys);

        static void ReleaseAllKeys() => TestInputDevices.ReleaseAllKeys();

        static void SetMouseDelta(Vector2 delta) => TestInputDevices.SetMouseDelta(delta);

        [UnitySetUp]
        public IEnumerator Setup()
        {
            EnsureInputDevices();
            yield return GameTestAPI.StartFreshLevel(30f);
        }

        // ── Spawn ──────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Player_SpawnsWithCharacterController()
        {
            yield return null;

            PlayerController player = GameTestAPI.GetPlayer();
            Assert.IsNotNull(player, "Player must exist after boot.");
            Assert.IsNotNull(player.GetComponent<CharacterController>(),
                "Player must have a CharacterController.");
            Assert.IsNotNull(player.playerCamera, "Player must have a first-person camera.");
            Assert.AreEqual(player.maxHealth, player.currentHealth,
                "Starting health must equal maxHealth (100).");
            Assert.IsFalse(player.IsDead(), "Player must be alive after boot.");
        }

        // ── Movement ───────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Player_WASDMovement_ChangesPosition()
        {
            PlayerController player = GameTestAPI.GetPlayer();
            Assert.IsNotNull(player);

            yield return null; // let first Update settle

            Vector3 before = player.transform.position;

            // Time-based (not frame-count) so varying batch frame rates don't skew distance.
            const float durationSeconds = 1.0f;
            float startT = Time.realtimeSinceStartup;
            PressKeys(Key.W);
            while (Time.realtimeSinceStartup - startT < durationSeconds)
                yield return null;
            ReleaseAllKeys();
            yield return null;

            Vector3 after = player.transform.position;
            float moved = Vector3.Distance(before, after);
            Assert.Greater(moved, 0.5f,
                "Player must move forward when W is held (moved=" +
                moved.ToString("F2") + "u in " + durationSeconds + "s).");
        }

        [UnityTest]
        public IEnumerator Player_Sprint_MovesFasterThanWalk()
        {
            PlayerController player = GameTestAPI.GetPlayer();
            Assert.IsNotNull(player);

            yield return null;

            // Compare distance covered in the SAME real-time window. Frame-count comparisons
            // are unreliable in batch because frame rate varies between phases.
            const float durationSeconds = 1.0f;

            // Walk: hold W for 1s
            Vector3 walkStart = player.transform.position;
            float startT = Time.realtimeSinceStartup;
            PressKeys(Key.W);
            while (Time.realtimeSinceStartup - startT < durationSeconds)
                yield return null;
            ReleaseAllKeys();
            yield return null;
            float walkDist = Vector3.Distance(walkStart, player.transform.position);

            // Reset to the exact same spot
            GameTestAPI.TeleportPlayer(walkStart);
            yield return null;
            yield return null;

            // Sprint: hold W + LeftShift for 1s
            Vector3 sprintStart = player.transform.position;
            startT = Time.realtimeSinceStartup;
            PressKeys(Key.W, Key.LeftShift);
            while (Time.realtimeSinceStartup - startT < durationSeconds)
                yield return null;
            ReleaseAllKeys();
            yield return null;
            float sprintDist = Vector3.Distance(sprintStart, player.transform.position);

            Assert.Greater(sprintDist, walkDist,
                "Sprint distance (" + sprintDist.ToString("F2") +
                ") must exceed walk distance (" + walkDist.ToString("F2") + ").");
        }

        // ── Jump ───────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Player_Jump_ProducesVerticalMovement()
        {
            PlayerController player = GameTestAPI.GetPlayer();
            CharacterController cc = player.GetComponent<CharacterController>();
            Assert.IsNotNull(cc);

            // Wait until grounded
            yield return GameBootstrap.WaitUntil(() => cc.isGrounded, 5f);
            float beforeY = player.transform.position.y;

            // Press space for a few frames so the queued input event is always
            // consumed by at least one PlayerController.Update (batch FPS varies).
            PressKeys(Key.Space);
            for (int hold = 0; hold < 3; hold++)
                yield return null;
            ReleaseAllKeys();

            // Wait for jump apex (a few frames)
            float maxSeenY = beforeY;
            for (int i = 0; i < 10; i++)
            {
                yield return null;
                float y = player.transform.position.y;
                if (y > maxSeenY) maxSeenY = y;
            }

            Assert.Greater(maxSeenY, beforeY + 0.1f,
                "Player Y must increase after jump (before=" +
                beforeY.ToString("F3") + ", max=" + maxSeenY.ToString("F3") + ").");
        }

        // ── Health ─────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Player_Health_StartsAt100()
        {
            yield return null;

            PlayerController player = GameTestAPI.GetPlayer();
            Assert.AreEqual(100, player.currentHealth,
                "Health must start at 100.");
            Assert.AreEqual(100, player.maxHealth,
                "Max health must be 100.");
        }

        [UnityTest]
        public IEnumerator Player_TakeDamage_ReducesHealth()
        {
            yield return null;

            PlayerController player = GameTestAPI.GetPlayer();

            int healthBefore = player.currentHealth;
            bool eventFired = false;
            int eventCurrent = -1;
            int eventMax = -1;

            void OnHealth(int cur, int max) { eventFired = true; eventCurrent = cur; eventMax = max; }
            player.OnHealthChanged += OnHealth;

            player.TakeDamage(30);

            player.OnHealthChanged -= OnHealth;

            Assert.AreEqual(healthBefore - 30, player.currentHealth,
                "TakeDamage(30) must reduce health by 30.");
            Assert.IsTrue(eventFired, "OnHealthChanged must fire on TakeDamage.");
            Assert.AreEqual(healthBefore - 30, eventCurrent,
                "OnHealthChanged current must match new health.");
            Assert.AreEqual(player.maxHealth, eventMax,
                "OnHealthChanged max must match maxHealth.");
        }

        // ── Death ──────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Player_ZeroHealth_TriggersDeath()
        {
            PlayerController player = GameTestAPI.GetPlayer();

            bool deathFired = false;
            void OnDeath() { deathFired = true; }
            player.OnPlayerDeath += OnDeath;

            player.TakeDamage(100);

            player.OnPlayerDeath -= OnDeath;

            yield return null; // let GameManager.Update observe IsDead() and clear gameActive

            Assert.IsTrue(player.IsDead(), "Player must be dead at 0 HP.");
            Assert.IsTrue(deathFired, "OnPlayerDeath must fire at 0 HP.");
            Assert.IsFalse(GameManager.Instance.gameActive,
                "GameManager.gameActive must be false after player death.");
        }

        [UnityTest]
        public IEnumerator Player_SetHealth_TriggersGameOverAtZero()
        {
            PlayerController player = GameTestAPI.GetPlayer();
            GameTestAPI.SetPlayerHealth(0);

            yield return null; // let GameManager.Update detect death

            Assert.IsTrue(player.IsDead(), "SetHealth(0) must kill the player.");
            Assert.IsFalse(GameManager.Instance.gameActive,
                "Game must not be active after death.");

            // WaveManager should have stopped
            WaveManager wave = WaveManager.Instance;
            Assert.IsFalse(wave.IsWaveInProgress(),
                "WaveManager must stop waves after player death.");
        }

        // ── Restart ────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Player_Restart_RestoresCleanState()
        {
            PlayerController playerBefore = GameTestAPI.GetPlayer();

            // Kill the player
            GameTestAPI.SetPlayerHealth(0);
            yield return null;
            Assert.IsTrue(playerBefore.IsDead(), "Precondition: player must be dead.");

            // Restart
            GameTestAPI.ResetGame();
            yield return GameBootstrap.WaitForGameReady(30f);

            PlayerController playerAfter = GameTestAPI.GetPlayer();
            Assert.IsNotNull(playerAfter, "New player must exist after restart.");
            Assert.AreNotEqual(playerBefore, playerAfter,
                "Restart must create a new PlayerController instance.");
            Assert.IsFalse(playerAfter.IsDead(), "New player must be alive.");
            Assert.AreEqual(100, playerAfter.currentHealth,
                "Restarted player must have 100 HP.");
            Assert.IsTrue(GameManager.Instance.gameActive,
                "Game must be active after restart.");
        }

        // ── HUD ────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Player_HUD_ShowsRequiredFields()
        {
            GameTestAPI.SetPlayerHealth(100); // guard against incidental wave damage
            yield return null; // HUD already created during boot

            PlayerHUD hud = Object.FindAnyObjectByType<PlayerHUD>();
            Assert.IsNotNull(hud, "PlayerHUD must exist in the scene.");

            Assert.IsNotNull(hud.healthText, "HUD must have healthText.");
            Assert.IsNotNull(hud.weaponText, "HUD must have weaponText.");
            Assert.IsNotNull(hud.ammoText, "HUD must have ammoText.");
            Assert.IsNotNull(hud.waveText, "HUD must have waveText.");
            Assert.IsNotNull(hud.waveCountdownText, "HUD must have waveCountdownText.");

            Assert.IsTrue(hud.healthText.text.Contains("100"),
                "healthText must show current HP at boot (got: '" + hud.healthText.text + "').");
            Assert.IsFalse(string.IsNullOrEmpty(hud.weaponText.text),
                "weaponText must have content at boot.");
            Assert.IsFalse(string.IsNullOrEmpty(hud.ammoText.text),
                "ammoText must have content at boot.");
            Assert.IsFalse(string.IsNullOrEmpty(hud.waveText.text),
                "waveText must have content at boot.");
        }

        [UnityTest]
        public IEnumerator Player_HUD_HealthUpdatesOnDamage()
        {
            GameTestAPI.SetPlayerHealth(100); // guard against incidental wave damage
            yield return null;

            PlayerHUD hud = Object.FindAnyObjectByType<PlayerHUD>();
            PlayerController player = GameTestAPI.GetPlayer();
            Assert.IsNotNull(hud);
            Assert.IsNotNull(player);

            // Initial
            Assert.AreEqual("HP: 100/100", hud.healthText.text,
                "HUD must show full health at boot.");

            player.TakeDamage(25);
            yield return null;

            Assert.AreEqual("HP: 75/100", hud.healthText.text,
                "HUD must reflect damage (75/100).");

            player.Heal(10);
            yield return null;

            Assert.AreEqual("HP: 85/100", hud.healthText.text,
                "HUD must reflect healing (85/100).");
        }

        [UnityTest]
        public IEnumerator Player_HUD_CountdownAndGameOver()
        {
            PlayerHUD hud = Object.FindAnyObjectByType<PlayerHUD>();
            Assert.IsNotNull(hud);

            // Show countdown
            hud.ShowCountdown(5);
            yield return null;

            // Countdown text object should be active (set by ShowCountdown via Find)
            GameObject countdownBg = GameObject.Find("CountdownBG");
            Assert.IsNotNull(countdownBg, "CountdownBG must exist.");
            Assert.IsTrue(countdownBg.activeSelf, "CountdownBG must be active after ShowCountdown.");
            Assert.IsTrue(hud.waveCountdownText.text.Contains("5"),
                "Countdown text must show '5' (got: '" + hud.waveCountdownText.text + "').");

            hud.HideCountdown();
            yield return null;

            Assert.IsFalse(countdownBg.activeSelf,
                "CountdownBG must be hidden after HideCountdown.");

            // Show game over
            hud.ShowGameOver(7);
            yield return null;

            GameObject gameOverBg = GameObject.Find("GameOverBG");
            Assert.IsNotNull(gameOverBg, "GameOverBG must exist.");
            Assert.IsTrue(gameOverBg.activeSelf, "GameOverBG must be active after ShowGameOver.");
            Assert.IsTrue(hud.waveSurvivedText.text.Contains("7"),
                "waveSurvivedText must show survived wave count (got: '" +
                hud.waveSurvivedText.text + "').");
        }

        [UnityTest]
        public IEnumerator Player_GameOverRestartButton_TriggersCleanRestart()
        {
            PlayerHUD hud = Object.FindAnyObjectByType<PlayerHUD>();
            Assert.IsNotNull(hud, "PlayerHUD must exist.");

            hud.ShowGameOver(3);
            yield return null;

            GameObject restartButton = GameObject.Find("RestartButton");
            Assert.IsNotNull(restartButton, "RestartButton must exist after ShowGameOver.");
            Assert.IsTrue(restartButton.activeSelf, "RestartButton must be visible on Game Over.");
            Button button = restartButton.GetComponent<Button>();
            Assert.IsNotNull(button, "RestartButton must have a Button component.");
            Assert.GreaterOrEqual(button.onClick.GetPersistentEventCount(), 0,
                "RestartButton must carry the production restart binding.");

            PlayerController playerBefore = GameTestAPI.GetPlayer();
            button.onClick.Invoke();

            yield return GameBootstrap.WaitForGameReady(30f);

            PlayerController playerAfter = GameTestAPI.GetPlayer();
            Assert.IsNotNull(playerAfter, "Player must exist after button restart.");
            Assert.AreNotEqual(playerBefore, playerAfter,
                "Button restart must create a new PlayerController instance.");
            Assert.IsFalse(playerAfter.IsDead(), "Restarted player must be alive.");
            Assert.AreEqual(100, playerAfter.currentHealth,
                "Restarted player must have 100 HP.");
            Assert.IsTrue(GameManager.Instance.gameActive,
                "Game must be active after button restart.");
        }

        [UnityTest]
        public IEnumerator Player_HUD_ScreenshotCaptured()
        {
            yield return new WaitForSeconds(0.5f);

            PlayerController player = GameTestAPI.GetPlayer();
            Assert.IsNotNull(player);

            Directory.CreateDirectory(TestResultsDir);
            string path = Path.Combine(TestResultsDir, "player_hud_screenshot.png");
            string saved = ScreenshotCapture.CaptureMain(path, 1280, 720);

            Assert.IsNotNull(saved, "Screenshot capture must return a path.");
            Assert.IsTrue(File.Exists(saved), "Screenshot file must exist on disk: " + saved);
            Assert.That(new FileInfo(saved).Length, Is.GreaterThan(0),
                "Screenshot file must not be empty.");

            Debug.Log("[PlayerHUD] screenshot saved: " + saved);
        }
    }
}
