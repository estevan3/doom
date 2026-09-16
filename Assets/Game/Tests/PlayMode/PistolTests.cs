using System.Collections;
using DoomClone.Automation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace DoomClone.Tests.PlayMode
{
    /// <summary>
    /// M6 — Pistol PlayMode verification per GAME_SPEC.md §3.2 and TEST_PLAN.md
    /// "Weapons: Pistol" (fires, hitscan damage, ammo rules) plus the M6 scope:
    /// cadence respected within a small frame-scheduling tolerance, the
    /// pistol as default/fallback weapon on boot, and exception-free
    /// firing/impact feedback through the real production input path.
    /// </summary>
    public class PistolTests
    {
        const string Level = GameTestAPI.LevelSceneName;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            TestInputDevices.EnsureDevices();
            yield return GameTestAPI.StartFreshLevel(30f);
            GameTestAPI.SetPlayerHealth(100); // guard against incidental wave damage
            yield return null;
        }

        static WeaponManager Weapons() => GameTestAPI.GetWeapons();

        static Pistol EquipFreshPistol(int ammo)
        {
            WeaponManager wm = Weapons();
            wm.EquipWeapon(GameTestAPI.WeaponPistol);
            Pistol pistol = (Pistol)wm.GetCurrentWeapon();
            pistol.SetAmmo(ammo);
            return pistol;
        }

        /// <summary>
        /// Holds the real Fire button (left mouse, production input path) for a
        /// fixed real-time window, then releases and waits one frame.
        /// </summary>
        static IEnumerator HoldFire(float seconds)
        {
            TestInputDevices.SetFireButton(true);
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < seconds)
                yield return null;
            TestInputDevices.SetFireButton(false);
            yield return null;
        }

        /// <summary>
        /// Rotates the player to look at a real level surface (wall/floor, ignoring
        /// enemies) and teleports the player a couple of meters away so a shot
        /// resolves against real geometry through the production impact-hook path.
        /// </summary>
        static void AimPlayerAtWall(PlayerController player)
        {
            Vector3 origin = player.playerCamera.transform.position;
            int bestStep = -1;
            float bestDist = float.MaxValue;
            Vector3 bestHit = Vector3.zero;

            for (int step = 0; step < 24; step++)
            {
                Vector3 dir = Quaternion.Euler(0, step * 15f, 0) * Vector3.forward;
                if (Physics.Raycast(origin, dir, out RaycastHit hit, 60f))
                {
                    if (hit.collider.GetComponentInParent<Enemy>() != null) continue;
                    if (hit.distance < bestDist)
                    {
                        bestDist = hit.distance;
                        bestStep = step;
                        bestHit = hit.point;
                    }
                }
            }

            Assert.IsTrue(bestStep >= 0, "No non-enemy surface found to aim at.");

            Vector3 flat = (Quaternion.Euler(0, bestStep * 15f, 0) * Vector3.forward);
            flat = new Vector3(flat.x, 0, flat.z).normalized;
            player.transform.rotation = Quaternion.LookRotation(flat);

            float standoff = Mathf.Min(2.0f, bestDist - 0.5f);
            if (standoff < 0.5f) standoff = 0.5f;
            Vector3 position = bestHit - flat * standoff;
            position.y = player.transform.position.y;
            GameTestAPI.TeleportPlayer(position);
            player.playerCamera.transform.localRotation = Quaternion.identity;
        }

        /// <summary>
        /// Picks a horizontal direction free of walls/enemies for at least
        /// <paramref name="clearance"/> meters, guaranteeing a demonstrably empty
        /// line of fire for the hitscan damage test. Skips enemy colliders so the
        /// chosen corridor ignores (temporarily wandering) wave enemies.
        /// </summary>
        static Vector3 FindClearForwardDirection(Vector3 origin, float clearance)
        {
            float bestLen = -1f;
            int bestStep = -1;

            for (int step = 0; step < 24; step++)
            {
                Vector3 dir = Quaternion.Euler(0, step * 15f, 0) * Vector3.forward;
                if (Physics.Raycast(origin, dir, out RaycastHit hit, 60f))
                {
                    if (hit.collider.GetComponentInParent<Enemy>() != null) continue;
                    if (hit.distance > bestLen)
                    {
                        bestLen = hit.distance;
                        bestStep = step;
                    }
                }
                else
                {
                    bestLen = float.MaxValue;
                    bestStep = step;
                    break;
                }
            }

            Assert.IsTrue(bestStep >= 0, "No direction found for the hitscan test.");
            Assert.Greater(bestLen, clearance,
                "Must be a clear corridor of at least " + clearance +
                "m for the hitscan test (best=" + bestLen.ToString("F1") + "m).");

            return Quaternion.Euler(0, bestStep * 15f, 0) * Vector3.forward;
        }

        // ── Default / fallback weapon ──────────────────────────────────────

        [UnityTest]
        public IEnumerator Pistol_Boot_IsDefaultWeaponAtSlot0()
        {
            yield return null;

            WeaponManager wm = Weapons();
            Assert.AreEqual(GameTestAPI.WeaponPistol, wm.currentWeaponIndex,
                "Player must boot with the Pistol equipped.");
            Assert.IsTrue(wm.GetCurrentWeapon() is Pistol,
                "The equipped weapon at boot must be the Pistol.");
            Assert.IsTrue(wm.GetCurrentWeapon().enabled,
                "The equipped Pistol must be enabled at boot.");

            int active = 0;
            foreach (Weapon w in wm.GetWeapons())
                if (w.enabled) active++;
            Assert.AreEqual(1, active, "Exactly one weapon may be enabled at any time.");
        }

        // ── Fires: direct production call ──────────────────────────────────

        [UnityTest]
        public IEnumerator Pistol_Fire_DirectShot_ConsumesOneRoundAndRaisesEvent()
        {
            Pistol pistol = EquipFreshPistol(50);
            yield return null;

            int fires = 0;
            void OnAmmo(int current, bool infinite) { fires++; }
            pistol.OnAmmoChanged += OnAmmo;

            pistol.Fire();

            pistol.OnAmmoChanged -= OnAmmo;

            Assert.AreEqual(49, pistol.GetAmmo(),
                "One Pistol Fire() must consume exactly one round.");
            Assert.AreEqual(1, fires,
                "Pistol Fire() must raise OnAmmoChanged exactly once.");
            Assert.IsFalse(pistol.CanFire(),
                "Pistol must be on cadence lock immediately after a shot.");
        }

        // ── Cadence ────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Pistol_Cadence_ImmediateRefireBlockedUntilFireRateElapses()
        {
            Pistol pistol = EquipFreshPistol(50);
            yield return null;

            pistol.Fire(); // shot at t0
            float shotAt = Time.time;
            Assert.IsFalse(pistol.CanFire(),
                "Immediately after a shot CanFire() must be false.");

            pistol.Fire(); // blocked by cadence
            Assert.AreEqual(49, pistol.GetAmmo(),
                "An immediate second Fire() must be blocked (no ammo consumed).");

            // Tolerance: the configured fireRate is 0.3s. Gate must open only after
            // fireRate (minus frame-scheduling slack) and well before any desync.
            yield return GameBootstrap.WaitUntil(() => pistol.CanFire(), 2f);
            float elapsed = Time.time - shotAt;

            Assert.GreaterOrEqual(elapsed, pistol.fireRate - 0.05f,
                "CanFire must not reopen before the configured fireRate (" +
                pistol.fireRate + "s); reopened after " + elapsed.ToString("F3") + "s.");
            Assert.LessOrEqual(elapsed, pistol.fireRate + 0.5f,
                "CanFire must reopen promptly after the configured fireRate; " +
                "took " + elapsed.ToString("F3") + "s.");
        }

        [UnityTest]
        public IEnumerator Pistol_Fire_Hold_ShotCountMatchesCadence()
        {
            // Nominal: cadence 0.3s -> ~4 rounds in a 1.2s hold. Window [3,6]
            // absorbs frame scheduling; a broken cadence (every-frame auto fire or
            // a longer delay) falls far outside it.
            Pistol pistol = EquipFreshPistol(999);
            PlayerController player = GameTestAPI.GetPlayer();
            AimPlayerAtWall(player);
            yield return null;

            yield return HoldFire(1.2f);

            int consumed = 999 - pistol.GetAmmo();
            Assert.That(consumed, Is.InRange(3, 6),
                "Holding fire 1.2s at the 0.3s pistol cadence must produce a steady " +
                "shot stream (nominal 4); consumed=" + consumed + ".");
            Assert.GreaterOrEqual(pistol.GetAmmo(), 0,
                "Ammo must never go negative while firing.");
        }

        // ── Hitscan damage against a real spawned enemy ────────────────────

        [UnityTest]
        public IEnumerator Pistol_Hitscan_SpawnedEnemyTakesConfiguredDamagePerShot()
        {
            WeaponManager wm = Weapons();
            Pistol pistol = EquipFreshPistol(50);
            PlayerController player = GameTestAPI.GetPlayer();
            yield return null;

            GameObject spawned = GameTestAPI.SpawnEnemy("runner");
            Assert.IsNotNull(spawned, "GameTestAPI.SpawnEnemy('runner') must return an enemy.");
            Enemy enemy = spawned.GetComponent<Enemy>();
            Assert.IsNotNull(enemy, "Spawned runner must carry an Enemy component.");

            // Freeze the target so it cannot chase out of the line of fire; the
            // production TakeDamage path still works with enemy.enabled == false.
            NavMeshAgent agent = spawned.GetComponent<NavMeshAgent>();
            if (agent != null) agent.enabled = false;
            enemy.enabled = false;

            int hpBefore = enemy.GetHealth();
            Assert.AreEqual(enemy.maxHealth, hpBefore,
                "Freshly spawned enemy must start at full health.");
            Assert.Greater(hpBefore, 0, "Spawned enemy must be alive.");

            // Aim down a clear corridor and drop the target 3.5m in front of the camera's eye.
            Transform camT = player.playerCamera.transform;
            Vector3 dir = FindClearForwardDirection(camT.position, 5f);
            player.transform.rotation = Quaternion.LookRotation(dir);
            camT.localRotation = Quaternion.identity;
            yield return null;

            Vector3 targetPos = camT.position + camT.forward * 3.5f;
            if (NavMeshUtil.TrySnapToNavMesh(targetPos, 3f, out Vector3 snapped))
                targetPos = new Vector3(snapped.x, snapped.y + 1f, snapped.z);
            spawned.transform.position = targetPos;
            yield return null;

            // Sanity: the camera ray must strike our target first (no wall/other
            // enemy in between before we commit to firing).
            if (Physics.Raycast(camT.position, camT.forward, out RaycastHit sanity, pistol.range))
            {
                Enemy hitEnemy = sanity.collider.GetComponentInParent<Enemy>();
                Assert.AreEqual(enemy, hitEnemy,
                    "Line of fire must strike the spawned target (got: " +
                    (hitEnemy != null ? hitEnemy.name : sanity.collider.name) + ").");
            }
            else
            {
                Assert.Fail("Raycast must find the spawned enemy in front of the camera.");
            }

            int ammoBefore = pistol.GetAmmo();

            // Fire exactly one production shot. A direct Fire() call (the method the
            // input path invokes) is deterministic: a frame-count hold is not, because
            // PlayMode frames near spawn/NavMesh-bake can exceed the 0.3s pistol
            // cadence, letting multiple rounds off. Input-path cadence is verified
            // separately by Pistol_Fire_Hold_ShotCountMatchesCadence.
            pistol.Fire();
            yield return null;

            int hpAfter = enemy.GetHealth();
            int ammoAfter = pistol.GetAmmo();

            Assert.AreEqual(ammoBefore - 1, ammoAfter,
                "One Pistol shot must consume exactly one round (before=" +
                ammoBefore + ", after=" + ammoAfter + ").");
            Assert.AreEqual(hpBefore - pistol.damage, hpAfter,
                "One Pistol hit must reduce enemy HP by the configured damage (" +
                pistol.damage + ") (before=" + hpBefore + ", after=" + hpAfter + ").");
        }

        // ── Ammo rules ─────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Pistol_Ammo_RunsToZeroAndZeroBlocksFiring()
        {
            Pistol pistol = EquipFreshPistol(1);
            PlayerController player = GameTestAPI.GetPlayer();
            AimPlayerAtWall(player);
            yield return null;

            // Hold fire until the single round is consumed, then keep holding.
            TestInputDevices.SetFireButton(true);
            yield return GameBootstrap.WaitUntil(() => pistol.GetAmmo() == 0, 3f);
            TestInputDevices.SetFireButton(false);
            yield return null;

            Assert.AreEqual(0, pistol.GetAmmo(),
                "Ammo must run out at exactly 0 after firing the last round.");

            TestInputDevices.SetFireButton(true);
            float startT = Time.realtimeSinceStartup;
            const float holdSeconds = 0.4f;
            while (Time.realtimeSinceStartup - startT < holdSeconds)
                yield return null;
            TestInputDevices.SetFireButton(false);
            yield return null;

            Assert.AreEqual(0, pistol.GetAmmo(),
                "Holding fire at 0 ammo must never drive ammo below zero.");
            Assert.IsFalse(pistol.CanFire(), "An empty Pistol must not fire.");

            // Explicit 0-ammo gate on the direct production call.
            pistol.SetAmmo(0);
            int before = pistol.GetAmmo();
            pistol.Fire();
            Assert.AreEqual(before, pistol.GetAmmo(),
                "Fire() with 0 ammo must be a no-op (SetAmmo(0) blocks firing).");
            Assert.IsFalse(pistol.CanFire(),
                "SetAmmo(0) must leave CanFire() false.");
        }
    }
}