using System.Collections;
using DoomClone.Automation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace DoomClone.Tests.PlayMode
{
    /// <summary>
    /// M8 — Assault Rifle PlayMode verification per GAME_SPEC.md §3.4 and
    /// TEST_PLAN.md "Weapons: Assault Rifle" (fires while held, repeated shots
    /// respect cadence, ammo drains) plus the M8 scope: automatic fire through
    /// the real input path, hitscan damage against a real spawned enemy, medium
    /// per-shot damage with long range as configured, and ammo that drains
    /// quickly and never goes negative.
    ///
    /// Singleton per-shot determinism follows the M6/M7 pattern: one direct
    /// production <see cref="AssaultRifle.Fire()"/> for exact per-shot state
    /// assertions; input-path holds with explicit bounded windows for the
    /// auto-fire/cadence/ammo-drain behavior.
    /// </summary>
    public class AssaultRifleTests
    {
        const string Level = GameTestAPI.LevelSceneName;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            TestInputDevices.EnsureDevices();
            // A prior test in the same PlayMode session may have left the synthetic
            // mouse/keyboard pressed; force a clean baseline so a direct Fire()
            // call can never race the equipped rifle's auto-fire Update().
            TestInputDevices.SetFireButton(false);
            TestInputDevices.ReleaseAllKeys();
            yield return GameTestAPI.StartFreshLevel(30f);
            GameTestAPI.SetPlayerHealth(100); // guard against incidental wave damage
            yield return null;
        }

        static WeaponManager Weapons() => GameTestAPI.GetWeapons();

        static AssaultRifle EquipFreshRifle(int ammo)
        {
            WeaponManager wm = Weapons();
            wm.EquipWeapon(GameTestAPI.WeaponAssaultRifle);
            AssaultRifle rifle = wm.GetCurrentWeapon() as AssaultRifle;
            Assert.IsNotNull(rifle, "Slot 2 (hotkey 3) must be the Assault Rifle.");
            rifle.SetAmmo(ammo);
            return rifle;
        }

        /// <summary>
        /// Rotates the player to look at a real level surface (wall/floor, ignoring
        /// enemies) and teleports the player ~2 m away so a shot resolves against real
        /// geometry through the production impact-hook path.
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
        /// <paramref name="clearance"/> meters, guaranteeing a controlled line of fire
        /// for the hitscan damage test. Skips enemy colliders so the chosen corridor
        /// ignores (temporarily wandering) wave enemies.
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

        /// <summary>
        /// Spawns a target enemy of the given type, freezes it on the spot and aims the
        /// player down a clear corridor with the target <paramref name="distance"/> meters
        /// in front of the camera eye. Returns the spawned Enemy.
        ///
        /// The freeze is belt-and-braces. The WaveManager's EnableNavMeshAgent coroutine
        /// (started at spawn) is neutralized by synchronously removing the NavMeshAgent so
        /// the coroutine exits at its null guard, and the Enemy component is disabled so no
        /// AI runs. Without this the coroutine re-snaps the transform onto the floor seconds
        /// later, dropping the collider below the eye-height firing ray and flaking hitscan
        /// tests. Physics is synced explicitly because a collider on a non-rigidbody object
        /// does not follow transform.position until the next physics step -- without it the
        /// sanity raycast sees the collider still at its original spawn point.
        /// </summary>
        static Enemy SpawnFrozenTarget(string type, float distance)
        {
            PlayerController player = GameTestAPI.GetPlayer();
            Assert.IsNotNull(player, "Player must exist to place a target.");

            GameObject spawned = GameTestAPI.SpawnEnemy(type);
            Assert.IsNotNull(spawned, "GameTestAPI.SpawnEnemy('" + type + "') must return an enemy.");
            Enemy enemy = spawned.GetComponent<Enemy>();
            Assert.IsNotNull(enemy, "Spawned " + type + " must carry an Enemy component.");

            NavMeshAgent agent = spawned.GetComponent<NavMeshAgent>();
            if (agent != null)
                Object.DestroyImmediate(agent);
            enemy.enabled = false;

            Assert.AreEqual(enemy.maxHealth, enemy.GetHealth(),
                "Freshly spawned " + type + " must start at full health.");
            Assert.Greater(enemy.GetHealth(), 0, "Spawned enemy must be alive.");

            Transform camT = player.playerCamera.transform;
            Vector3 dir = FindClearForwardDirection(camT.position, 5f);
            player.transform.rotation = Quaternion.LookRotation(dir);
            camT.localRotation = Quaternion.identity;

            // Lift the floor-aligned root ~1.6m so the collider always spans the camera eye
            // height (~1.33m). The Brute's production Start() sets scale 2 (GAME_SPEC §4.3
            // "larger hitbox") but never runs on a disabled component, so mirror it here.
            Vector3 targetPos = camT.position + camT.forward * distance;
            if (NavMeshUtil.TrySnapToNavMesh(targetPos, 3f, out Vector3 snapped))
                targetPos = new Vector3(snapped.x, snapped.y + 1.6f, snapped.z);
            if (type == "brute")
                spawned.transform.localScale = Vector3.one * 2f;
            spawned.transform.position = targetPos;
            Physics.SyncTransforms();

            return enemy;
        }

        // ── Configuration (GAME_SPEC §3.4) ─────────────────────────────────

        [UnityTest]
        public IEnumerator AssaultRifle_Config_DamageCadenceRangeAmmo_MeetSpec()
        {
            yield return null;

            WeaponManager wm = Weapons();
            AssaultRifle rifle = EquipFreshRifle(wm.GetWeapons()[GameTestAPI.WeaponAssaultRifle].maxAmmo);
            yield return null;

            // Medium damage per shot, high rate of fire, long range (GAME_SPEC §3.4).
            Assert.That(rifle.damage, Is.InRange(5, 20),
                "Assault Rifle must deal medium per-shot damage (got " + rifle.damage + ").");
            Assert.LessOrEqual(rifle.fireRate, 0.15f,
                "Assault Rifle must have a high rate of fire (fireRate=" +
                rifle.fireRate + "s).");
            Assert.GreaterOrEqual(rifle.range, 100f,
                "Assault Rifle must be a long-range weapon (range=" + rifle.range + "m).");

            // It must out-pace the pistol: faster cadence AND higher sustained DPS
            // (rate x damage), while still being a limited-ammo weapon.
            Pistol pistol = wm.GetWeapons()[GameTestAPI.WeaponPistol] as Pistol;
            Assert.IsNotNull(pistol, "Pistol slot must exist for balance comparison.");
            Assert.Less(rifle.fireRate, pistol.fireRate,
                "Assault Rifle cadence must be faster than the Pistol (" +
                rifle.fireRate + " vs " + pistol.fireRate + "s).");
            Assert.GreaterOrEqual(rifle.range, pistol.range,
                "Assault Rifle range must be at least the Pistol's (" +
                rifle.range + " vs " + pistol.range + "m).");

            float rifleDps = rifle.damage / rifle.fireRate;
            float pistolDps = pistol.damage / pistol.fireRate;
            Assert.Greater(rifleDps, pistolDps,
                "Assault Rifle sustained DPS must exceed the Pistol (" +
                rifleDps.ToString("F1") + " vs " + pistolDps.ToString("F1") + ").");

            // Limited ammunition that drains quickly: a full magazine must empty in
            // a short continuous burst (120 rounds @ 0.1s == 12s of sustained fire).
            Assert.IsTrue(rifle.UsesAmmo(),
                "Assault Rifle must consume limited ammunition.");
            Assert.Greater(rifle.maxAmmo, 0,
                "Assault Rifle must start with ammunition.");
            Assert.LessOrEqual(rifle.maxAmmo * rifle.fireRate, 15f,
                "Full magazine must empty within a short burst (drains quickly; " +
                rifle.maxAmmo * rifle.fireRate + "s at nominal cadence).");
        }

        // ── Fires: one round + ammo event ──────────────────────────────────

        [UnityTest]
        public IEnumerator AssaultRifle_Fire_DirectShot_ConsumesOneRoundAndRaisesEvent()
        {
            AssaultRifle rifle = EquipFreshRifle(50);
            yield return null;

            int events = 0;
            void OnAmmo(int current, bool infinite) { events++; }
            rifle.OnAmmoChanged += OnAmmo;

            rifle.Fire();

            rifle.OnAmmoChanged -= OnAmmo;

            Assert.AreEqual(49, rifle.GetAmmo(),
                "One AssaultRifle Fire() must consume exactly one round.");
            Assert.AreEqual(1, events,
                "AssaultRifle Fire() must raise OnAmmoChanged exactly once.");
            Assert.IsFalse(rifle.CanFire(),
                "AssaultRifle must be on cadence lock immediately after a shot.");
        }

        // ── Cadence ────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator AssaultRifle_Cadence_ImmediateRefireBlockedUntilFireRateElapses()
        {
            AssaultRifle rifle = EquipFreshRifle(50);
            yield return null;

            rifle.Fire(); // shot at t0
            float shotAt = Time.time;
            Assert.IsFalse(rifle.CanFire(),
                "Immediately after a shot CanFire() must be false.");

            rifle.Fire(); // blocked by cadence
            Assert.AreEqual(49, rifle.GetAmmo(),
                "An immediate second Fire() must be blocked (no round consumed).");

            // Tolerance: the configured fireRate is 0.1s. The gate must open only after
            // fireRate (minus frame-scheduling slack) and well before any desync.
            yield return GameBootstrap.WaitUntil(() => rifle.CanFire(), 2f);
            float elapsed = Time.time - shotAt;

            Assert.GreaterOrEqual(elapsed, rifle.fireRate - 0.05f,
                "CanFire must not reopen before the configured fireRate (" +
                rifle.fireRate + "s); reopened after " + elapsed.ToString("F3") + "s.");
            Assert.LessOrEqual(elapsed, rifle.fireRate + 0.5f,
                "CanFire must reopen promptly after the configured fireRate; took " +
                elapsed.ToString("F3") + "s.");
        }

        // ── Automatic fire while held + cadence + ammo drain ───────────────

        [UnityTest]
        public IEnumerator AssaultRifle_Fire_Hold_AutomaticStreamMatchesHighCadence()
        {
            // What this proves is AUTOMATIC fire: one persistent press must yield a stream
            // of several rounds with no re-press. PlayMode batchmode runs at only ~5-8 FPS,
            // so a fixed real-time hold yields an unpredictable number of frames (measured
            // 3 rounds in 1.2s real, sometimes 1 in 0.9s), and the real rate is frame-bound,
            // not cadence-bound. So the test holds until at least 3 rounds are consumed (a
            // generous real-time deadline that a broken auto-fire would exceed) and then
            // asserts the stream both happened (>=3) and never exceeded a sane cadence
            // ceiling (<=20). The exact 0.1s gate is verified frame-independently by the
            // direct-CanFire cadence test above.
            AssaultRifle rifle = EquipFreshRifle(120);
            PlayerController player = GameTestAPI.GetPlayer();
            AimPlayerAtWall(player);
            yield return null;

            int before = rifle.GetAmmo();
            TestInputDevices.SetFireButton(true);
            float deadline = Time.realtimeSinceStartup + 8f;
            while (before - rifle.GetAmmo() < 3 && Time.realtimeSinceStartup < deadline)
                yield return null;
            TestInputDevices.SetFireButton(false);
            yield return null;

            int consumed = before - rifle.GetAmmo();
            Assert.That(consumed, Is.InRange(3, 20),
                "One persistent press must produce a multi-round automatic stream " +
                "(>=3, without blowing through the 0.1s cadence <=20); consumed=" +
                consumed + ".");
            Assert.GreaterOrEqual(rifle.GetAmmo(), 0,
                "Ammo must never go negative while firing.");
        }

        // ── Hitscan damage against a real spawned enemy ────────────────────

        [UnityTest]
        public IEnumerator AssaultRifle_Hitscan_SpawnedEnemyTakesConfiguredDamagePerShot()
        {
            AssaultRifle rifle = EquipFreshRifle(50);
            PlayerController player = GameTestAPI.GetPlayer();
            Assert.IsNotNull(player, "Player must exist.");
            yield return null;

            // Runner: 30 HP, capsule radius 0.5, well inside the rifle's small ±0.02
            // spread offset at 3.5m (~0.07m max). A single direct Fire() is the same
            // method the input path invokes and is frame-independent (M6 pattern).
            Enemy enemy = SpawnFrozenTarget("runner", 3.5f);
            yield return null;

            // Sanity: the camera ray must strike our target first (no wall/other
            // enemy in between before we commit to firing).
            Transform camT = player.playerCamera.transform;
            if (Physics.Raycast(camT.position, camT.forward, out RaycastHit sanity, rifle.range))
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

            int hpBefore = enemy.GetHealth();
            int ammoBefore = rifle.GetAmmo();

            rifle.Fire();
            yield return null;

            int hpAfter = enemy.GetHealth();
            int ammoAfter = rifle.GetAmmo();

            Assert.AreEqual(ammoBefore - 1, ammoAfter,
                "One AssaultRifle shot must consume exactly one round (before=" +
                ammoBefore + ", after=" + ammoAfter + ").");
            Assert.AreEqual(hpBefore - rifle.damage, hpAfter,
                "One AssaultRifle hit must reduce enemy HP by the configured damage (" +
                rifle.damage + ") (before=" + hpBefore + ", after=" + hpAfter + ").");
            Assert.Greater(hpAfter, 0,
                "A single shot must not kill the 30 HP runner.");
        }

        [UnityTest]
        public IEnumerator AssaultRifle_Hold_SpawnedEnemy_TakesRepeatedDamageAndAmmoDrainsPerShot()
        {
            AssaultRifle rifle = EquipFreshRifle(120);
            PlayerController player = GameTestAPI.GetPlayer();
            Assert.IsNotNull(player, "Player must exist.");
            yield return null;

            // Brute: 200 HP, huge collider (scale 2 per GAME_SPEC §4.3 via SpawnFrozenTarget),
            // so the ±0.02 spread (≈0.06m at 3m) can never miss its 1m radius. Its HP absorbs
            // the whole burst without dying mid-test.
            Enemy enemy = SpawnFrozenTarget("brute", 3f);
            yield return null;

            Transform camT = player.playerCamera.transform;
            if (Physics.Raycast(camT.position, camT.forward, out RaycastHit sanity, rifle.range))
            {
                Enemy hitEnemy = sanity.collider.GetComponentInParent<Enemy>();
                Assert.AreEqual(enemy, hitEnemy,
                    "Line of fire must strike the spawned brute for the hold test (got: " +
                    (hitEnemy != null ? hitEnemy.name : sanity.collider.name) + ").");
            }
            else
            {
                Assert.Fail("Raycast must find the spawned brute in front of the camera.");
            }

            int hits = 0;
            void OnDamage(Enemy e, int d) { hits++; }
            enemy.OnEnemyDamaged += OnDamage;

            int ammoBefore = rifle.GetAmmo();
            int hpBefore = enemy.GetHealth();

            // Hold until at least 2 rounds LAND on the brute (deadline based, so the
            // assertion is independent of the erratic ~5 FPS batch frame rate), then release.
            TestInputDevices.SetFireButton(true);
            float deadline = Time.realtimeSinceStartup + 8f;
            while (hits < 2 && Time.realtimeSinceStartup < deadline)
                yield return null;
            TestInputDevices.SetFireButton(false);
            yield return null;

            enemy.OnEnemyDamaged -= OnDamage;

            int ammoConsumed = ammoBefore - rifle.GetAmmo();
            int hpAfter = enemy.GetHealth();

            // Nominal cadence would burn ~9 rounds, but in batch ~5 FPS the realized burst
            // is frame-gated. The meaningful contracts are the ones below: a continuous hold
            // deals REPEATED damage (>=2 traced hits), ammo correlates with hits (a 3m
            // point-blank 1m-radius brute can barely miss), each hit applies exactly the
            // configured damage, and the brute's 200 HP absorbs it all.
            Assert.GreaterOrEqual(hits, 2,
                "A held burst must land repeated damage on the brute (hits=" + hits + ").");
            Assert.GreaterOrEqual(ammoConsumed, 1,
                "The hold must have drained ammo (consumed=" + ammoConsumed + ").");
            Assert.LessOrEqual(ammoConsumed - hits, 3,
                "At most a few rounds may miss the point-blank brute (ammo=" +
                ammoConsumed + ", hits=" + hits + ").");
            Assert.AreEqual(hpBefore - hits * rifle.damage, hpAfter,
                "Every landed hit must apply exactly the configured damage (" +
                rifle.damage + ") (hp before=" + hpBefore + ", after=" + hpAfter + ").");
            Assert.Greater(hpAfter, 0,
                "The 200 HP brute must survive the burst.");
            Assert.GreaterOrEqual(rifle.GetAmmo(), 0,
                "Ammo must never go negative while firing.");
        }

        // ── Limited ammunition ─────────────────────────────────────────────

        [UnityTest]
        public IEnumerator AssaultRifle_Ammo_Limited_RunsToZeroAndZeroBlocksFiring()
        {
            AssaultRifle rifle = EquipFreshRifle(1);
            PlayerController player = GameTestAPI.GetPlayer();
            AimPlayerAtWall(player);
            yield return null;

            // Hold fire until the single round is consumed, then keep holding.
            TestInputDevices.SetFireButton(true);
            yield return GameBootstrap.WaitUntil(() => rifle.GetAmmo() == 0, 3f);
            TestInputDevices.SetFireButton(false);
            yield return null;

            Assert.AreEqual(0, rifle.GetAmmo(),
                "Ammo must run out at exactly 0 after firing the last round.");

            TestInputDevices.SetFireButton(true);
            float startT = Time.realtimeSinceStartup;
            const float holdSeconds = 0.4f;
            while (Time.realtimeSinceStartup - startT < holdSeconds)
                yield return null;
            TestInputDevices.SetFireButton(false);
            yield return null;

            Assert.AreEqual(0, rifle.GetAmmo(),
                "Holding fire at 0 ammo must never drive ammo below zero.");
            Assert.IsFalse(rifle.CanFire(), "An empty Assault Rifle must not fire.");

            // Explicit 0-ammo gate on the direct production call.
            rifle.SetAmmo(0);
            int before = rifle.GetAmmo();
            rifle.Fire();
            Assert.AreEqual(before, rifle.GetAmmo(),
                "Fire() with 0 ammo must be a no-op (SetAmmo(0) blocks firing).");
            Assert.IsFalse(rifle.CanFire(),
                "SetAmmo(0) must leave CanFire() false.");
        }
    }
}