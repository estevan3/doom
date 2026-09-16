using System.Collections;
using System.Collections.Generic;
using DoomClone.Automation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace DoomClone.Tests.PlayMode
{
    /// <summary>
    /// M7 — Shotgun PlayMode verification per GAME_SPEC.md §3.3 and TEST_PLAN.md
    /// §3.3 "Shotgun": 6–8 pellet cone, high close-range damage, slow (noticeable)
    /// cadence, limited ammo (scarcer than the pistol), and reload by block.
    ///
    /// Singleton per-shot determinism follows the pattern established in M6
    /// (PistolTests): one direct production <see cref="Shotgun.Fire()"/> for exact
    /// per-shot state assertions; input-path holds with loose windows for
    /// cadence/auto behavior. Pellet count is measured through the real damage
    /// path (<see cref="Enemy.OnEnemyDamaged"/> per pellet) and through the number
    /// of impact spheres the production feedback hook creates at a wall.
    /// </summary>
    public class ShotgunTests
    {
        const string Level = GameTestAPI.LevelSceneName;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            TestInputDevices.EnsureDevices();
            // A prior test in the same PlayMode session may have left the synthetic
            // mouse/keyboard pressed; force a clean baseline so a direct Fire()
            // call can never race the equipped Shotgun's auto-fire Update().
            TestInputDevices.SetFireButton(false);
            TestInputDevices.ReleaseAllKeys();
            yield return GameTestAPI.StartFreshLevel(30f);
            GameTestAPI.SetPlayerHealth(100); // guard against incidental wave damage
            yield return null;
        }

        static WeaponManager Weapons() => GameTestAPI.GetWeapons();

        static Shotgun EquipFreshShotgun(int ammo)
        {
            WeaponManager wm = Weapons();
            wm.EquipWeapon(GameTestAPI.WeaponShotgun);
            Shotgun shotgun = wm.GetCurrentWeapon() as Shotgun;
            Assert.IsNotNull(shotgun, "Slot 1 (hotkey 2) must be the Shotgun.");
            shotgun.SetAmmo(ammo);
            return shotgun;
        }

        static int PistolDamage(WeaponManager wm)
        {
            Pistol pistol = wm.GetWeapons()[GameTestAPI.WeaponPistol] as Pistol;
            return pistol != null ? pistol.damage : 0;
        }

        /// <summary>Holds the real Fire button (production input path) for a fixed real-time window.</summary>
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
        /// for the pellet-damage test. Skips enemy colliders so the chosen corridor
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

        static HashSet<SphereCollider> ImpactSpheres()
        {
            var ids = new HashSet<SphereCollider>();
            foreach (SphereCollider col in Object.FindObjectsByType<SphereCollider>(FindObjectsSortMode.None))
                ids.Add(col);
            return ids;
        }

        // ── Configuration (GAME_SPEC §3.3) ─────────────────────────────────

        [UnityTest]
        public IEnumerator Shotgun_Config_PelletCountDamageCadenceAmmo_MeetSpec()
        {
            yield return null;

            WeaponManager wm = Weapons();
            Shotgun shotgun = EquipFreshShotgun(wm.GetWeapons()[GameTestAPI.WeaponShotgun].maxAmmo);
            yield return null;

            Assert.That(shotgun.pelletCount, Is.InRange(6, 8),
                "GAME_SPEC §3.3 requires 6-8 pellets per shotgun shot (got " +
                shotgun.pelletCount + ").");
            Assert.Greater(shotgun.pelletDamage, 0,
                "Per-pellet damage must be positive.");
            Assert.GreaterOrEqual(shotgun.fireRate, 0.5f,
                "Shotgun cadence must be slow/noticeable (fireRate=" +
                shotgun.fireRate + "s).");
            Assert.IsTrue(shotgun.UsesAmmo(),
                "Shotgun must consume limited ammunition.");
            Assert.GreaterOrEqual(shotgun.reloadTime, 1f,
                "Shotgun must have a block/shell reload (reloadTime=" +
                shotgun.reloadTime + "s).");
            Assert.Greater(shotgun.maxAmmo, 0,
                "Shotgun must start with some ammunition.");

            int pistolMag = wm.GetWeapons()[GameTestAPI.WeaponPistol].maxAmmo;
            Assert.Less(shotgun.maxAmmo, pistolMag,
                "GAME_SPEC §3.3: shotgun ammo must be scarcer than pistol ammo (" +
                shotgun.maxAmmo + " vs " + pistolMag + ").");

            int pointBlankFullHit = shotgun.pelletCount * shotgun.pelletDamage;
            Assert.Greater(pointBlankFullHit, PistolDamage(wm),
                "A full point-blank shot must out-damage a single pistol shot (" +
                pointBlankFullHit + " vs " + PistolDamage(wm) + ").");
        }

        // ── Fires: one shell + ammo event ──────────────────────────────────

        [UnityTest]
        public IEnumerator Shotgun_Fire_DirectShot_ConsumesOneShellAndRaisesAmmoEvent()
        {
            Shotgun shotgun = EquipFreshShotgun(10);
            yield return null;

            int events = 0;
            void OnAmmo(int current, bool infinite) { events++; }
            shotgun.OnAmmoChanged += OnAmmo;

            shotgun.Fire();

            shotgun.OnAmmoChanged -= OnAmmo;

            Assert.AreEqual(9, shotgun.GetAmmo(),
                "One Shotgun Fire() must consume exactly one shell.");
            Assert.AreEqual(1, events,
                "Shotgun Fire() must raise OnAmmoChanged exactly once.");
            Assert.IsFalse(shotgun.CanFire(),
                "Shotgun must be on cadence lock immediately after a shot.");
        }

        // ── Pellets: cone + count through the real damage path ─────────────

        [UnityTest]
        public IEnumerator Shotgun_Pellets_PointBlankFullHit_DealsPelletCountTimesPelletDamage()
        {
            WeaponManager wm = Weapons();
            Shotgun shotgun = EquipFreshShotgun(50);
            PlayerController player = GameTestAPI.GetPlayer();
            yield return null;

            GameObject spawned = GameTestAPI.SpawnEnemy("brute");
            Assert.IsNotNull(spawned, "GameTestAPI.SpawnEnemy('brute') must return an enemy.");
            Enemy enemy = spawned.GetComponent<Enemy>();
            Assert.IsNotNull(enemy, "Spawned brute must carry an Enemy component.");

            // Freeze the target so it cannot chase out of the line of fire; the
            // production TakeDamage path still works with enemy.enabled == false.
            NavMeshAgent agent = spawned.GetComponent<NavMeshAgent>();
            if (agent != null) agent.enabled = false;
            enemy.enabled = false;

            int hpBefore = enemy.GetHealth();
            Assert.AreEqual(enemy.maxHealth, hpBefore,
                "Freshly spawned brute must start at full health.");
            Assert.Greater(hpBefore, 0, "Spawned enemy must be alive.");
            Assert.Greater(hpBefore, shotgun.pelletCount * shotgun.pelletDamage,
                "Brute must have enough HP to absorb a whole point-blank blast " +
                "(config sanity; hp=" + hpBefore + ", blast=" +
                (shotgun.pelletCount * shotgun.pelletDamage) + ").");

            // Aim down a clear corridor and drop the target 1.5m in front of the eye:
            // close enough that the 5° cone (≈0.075m at 1.5m) lands every pellet on the
            // brute's world collider (radius ≈1.0m at scale 2).
            Transform camT = player.playerCamera.transform;
            Vector3 dir = FindClearForwardDirection(camT.position, 5f);
            player.transform.rotation = Quaternion.LookRotation(dir);
            camT.localRotation = Quaternion.identity;
            yield return null;

            Vector3 targetPos = camT.position + camT.forward * 1.5f;
            if (NavMeshUtil.TrySnapToNavMesh(targetPos, 3f, out Vector3 snapped))
                targetPos = new Vector3(snapped.x, snapped.y + 1f, snapped.z);
            spawned.transform.position = targetPos;
            yield return null;

            // Sanity: the camera ray must strike our target first (no wall/other
            // enemy in between before we commit to firing).
            if (Physics.Raycast(camT.position, camT.forward, out RaycastHit sanity, shotgun.range))
            {
                Enemy hitEnemy = sanity.collider.GetComponentInParent<Enemy>();
                Assert.AreEqual(enemy, hitEnemy,
                    "Line of fire must strike the spawned brute (got: " +
                    (hitEnemy != null ? hitEnemy.name : sanity.collider.name) + ").");
            }
            else
            {
                Assert.Fail("Raycast must find the spawned brute in front of the camera.");
            }

            // Count pellet hits via the production damage event: every pellet that hits
            // calls Enemy.TakeDamage(pelletDamage) and fires OnEnemyDamaged.
            int pelletHits = 0;
            void OnDamage(Enemy e, int d) { pelletHits++; }
            enemy.OnEnemyDamaged += OnDamage;

            int ammoBefore = shotgun.GetAmmo();
            shotgun.Fire(); // one direct production shot
            enemy.OnEnemyDamaged -= OnDamage;
            yield return null;

            int ammoAfter = shotgun.GetAmmo();
            int hpAfter = enemy.GetHealth();

            Assert.AreEqual(ammoBefore - 1, ammoAfter,
                "One Shotgun shot must consume exactly one shell (before=" +
                ammoBefore + ", after=" + ammoAfter + ").");
            Assert.AreEqual(shotgun.pelletCount, pelletHits,
                "A point-blank blast must land every pellet (hits=" + pelletHits +
                ", pellets=" + shotgun.pelletCount + ").");
            Assert.AreEqual(hpBefore - pelletHits * shotgun.pelletDamage, hpAfter,
                "Each pellet must apply exactly the configured pelletDamage (" +
                shotgun.pelletDamage + ") (hp before=" + hpBefore +
                ", after=" + hpAfter + ").");
            Assert.Greater(hpBefore - hpAfter, PistolDamage(wm),
                "Close-range full blast (" + (hpBefore - hpAfter) + ") must exceed a " +
                "single pistol shot (" + PistolDamage(wm) + ").");
        }

        [UnityTest]
        public IEnumerator Shotgun_Pellets_ConeAtWall_ProducesPelletCountDistinctImpacts()
        {
            Shotgun shotgun = EquipFreshShotgun(50);
            PlayerController player = GameTestAPI.GetPlayer();
            AimPlayerAtWall(player);
            yield return null;

            // The production feedback hook (Weapon.CreateImpactEffect) spawns one
            // sphere per pellet that resolves against a surface. Fire() is synchronous,
            // so snapshotting sphere colliders immediately after it (same frame, before
            // the 0.1s destroy timer) counts exactly the pellets that struck the wall.
            HashSet<SphereCollider> before = ImpactSpheres();
            shotgun.Fire();
            HashSet<SphereCollider> after = ImpactSpheres();

            var newImpacts = new List<SphereCollider>();
            foreach (SphereCollider col in after)
                if (!before.Contains(col)) newImpacts.Add(col);

            Assert.AreEqual(shotgun.pelletCount, newImpacts.Count,
                "One Shotgun shot must produce exactly " + shotgun.pelletCount +
                " wall impacts (got " + newImpacts.Count + ").");

            var positions = new List<Vector3>();
            foreach (SphereCollider col in newImpacts)
                positions.Add(col.transform.position);

            Assert.AreEqual(newImpacts.Count, positions.Count,
                "Every counted impact must resolve to a live sphere this frame.");

            // Distinct impact points prove the pellets spread as a cone rather than
            // travelling a single straight ray.
            var distinct = new List<Vector3>();
            foreach (Vector3 p in positions)
            {
                bool duplicate = false;
                foreach (Vector3 q in distinct)
                {
                    if ((p - q).sqrMagnitude < 0.0001f) { duplicate = true; break; }
                }
                if (!duplicate) distinct.Add(p);
            }
            Assert.GreaterOrEqual(distinct.Count, 2,
                "A cone must strike the wall at multiple distinct points (distinct=" +
                distinct.Count + ").");

            // ...but the cone must stay tight: worst-case per-axis angular offset is
            // ±0.05 rad (spreadAngle 5° * 0.01), so at ≤2m standoff the impact cluster
            // can span at most ~0.3m. A loose 1.0m bound proves spread, not a scatter.
            float maxPairwise = 0f;
            for (int i = 0; i < positions.Count; i++)
            {
                for (int j = i + 1; j < positions.Count; j++)
                {
                    maxPairwise = Mathf.Max(maxPairwise, (positions[i] - positions[j]).magnitude);
                }
            }
            Assert.LessOrEqual(maxPairwise, 1.0f,
                "Pellet impacts must stay within the expected cone spread (max pairwise " +
                maxPairwise.ToString("F2") + "m).");
        }

        // ── Slow cadence ───────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Shotgun_Cadence_ImmediateRefireBlockedUntilFireRateElapses()
        {
            Shotgun shotgun = EquipFreshShotgun(50);
            yield return null;

            shotgun.Fire(); // shot at t0
            float shotAt = Time.time;
            Assert.IsFalse(shotgun.CanFire(),
                "Immediately after a shot CanFire() must be false.");

            shotgun.Fire(); // blocked by cadence
            Assert.AreEqual(49, shotgun.GetAmmo(),
                "An immediate second Fire() must be blocked (no shell consumed).");

            // Tolerance: the configured fireRate is 0.8s. The gate must open only after
            // fireRate (minus frame-scheduling slack) and well before any desync.
            yield return GameBootstrap.WaitUntil(() => shotgun.CanFire(), 2f);
            float elapsed = Time.time - shotAt;

            Assert.GreaterOrEqual(elapsed, shotgun.fireRate - 0.1f,
                "CanFire must not reopen before the configured fireRate (" +
                shotgun.fireRate + "s); reopened after " + elapsed.ToString("F3") + "s.");
            Assert.LessOrEqual(elapsed, shotgun.fireRate + 0.5f,
                "CanFire must reopen promptly after the configured fireRate; took " +
                elapsed.ToString("F3") + "s.");
        }

        [UnityTest]
        public IEnumerator Shotgun_Fire_Hold_ShotCountMatchesSlowCadence()
        {
            // Nominal: cadence 0.8s -> ~2 shots in a 1.6s hold. Window [1,3] absorbs
            // frame scheduling; a broken cadence (every-frame auto fire or a much
            // longer delay) falls far outside it.
            Shotgun shotgun = EquipFreshShotgun(50);
            PlayerController player = GameTestAPI.GetPlayer();
            AimPlayerAtWall(player);
            yield return null;

            int before = shotgun.GetAmmo();
            yield return HoldFire(1.6f);

            int consumed = before - shotgun.GetAmmo();
            Assert.That(consumed, Is.InRange(1, 3),
                "Holding fire 1.6s at the 0.8s shotgun cadence must produce a slow, " +
                "steady stream (nominal 2); consumed=" + consumed + ".");
            Assert.GreaterOrEqual(shotgun.GetAmmo(), 0,
                "Ammo must never go negative while firing.");
        }

        // ── Limited ammunition ─────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Shotgun_Ammo_LimitedMagazine_RunsToZeroAndZeroBlocksFiring()
        {
            Shotgun shotgun = EquipFreshShotgun(2);
            PlayerController player = GameTestAPI.GetPlayer();
            AimPlayerAtWall(player);
            yield return null;

            // Hold fire until both shells are consumed, then keep holding.
            TestInputDevices.SetFireButton(true);
            yield return GameBootstrap.WaitUntil(() => shotgun.GetAmmo() == 0, 4f);
            TestInputDevices.SetFireButton(false);
            yield return null;

            Assert.AreEqual(0, shotgun.GetAmmo(),
                "Ammo must run out at exactly 0 after firing the last shell.");

            TestInputDevices.SetFireButton(true);
            float startT = Time.realtimeSinceStartup;
            const float holdSeconds = 0.6f;
            while (Time.realtimeSinceStartup - startT < holdSeconds)
                yield return null;
            TestInputDevices.SetFireButton(false);
            yield return null;

            Assert.AreEqual(0, shotgun.GetAmmo(),
                "Holding fire at 0 ammo must never drive ammo below zero.");
            Assert.IsFalse(shotgun.CanFire(), "An empty Shotgun must not fire.");

            // Explicit 0-ammo gate on the direct production call.
            shotgun.SetAmmo(0);
            int noAmmoBefore = shotgun.GetAmmo();
            shotgun.Fire();
            Assert.AreEqual(noAmmoBefore, shotgun.GetAmmo(),
                "Fire() with 0 ammo must be a no-op (SetAmmo(0) blocks firing).");
            Assert.IsFalse(shotgun.CanFire(),
                "SetAmmo(0) must leave CanFire() false.");
        }

        // ── Reload (block reload per GAME_SPEC §3.3) ───────────────────────

        [UnityTest]
        public IEnumerator Shotgun_Reload_BlockReload_RestoresFullMagazineAndBlocksFire()
        {
            Shotgun shotgun = EquipFreshShotgun(10);
            yield return null;

            Assert.IsFalse(shotgun.IsReloading(), "A fresh Shotgun must not be reloading.");
            Assert.AreEqual(10, shotgun.GetAmmo());

            float reloadStarted = Time.time;
            shotgun.StartReload();

            Assert.IsTrue(shotgun.IsReloading(),
                "StartReload() must flip the blocking-reload flag immediately.");
            Assert.IsFalse(shotgun.CanFire(),
                "A reloading Shotgun must not be able to fire.");
            int ammoMidReload = shotgun.GetAmmo();
            shotgun.Fire(); // blocked by the reload
            Assert.AreEqual(ammoMidReload, shotgun.GetAmmo(),
                "Fire() during a reload must be a no-op (no shell consumed).");

            yield return GameBootstrap.WaitUntil(() => !shotgun.IsReloading(), 4f);
            float elapsed = Time.time - reloadStarted;

            Assert.GreaterOrEqual(elapsed, shotgun.reloadTime - 0.15f,
                "Block reload must not finish before the configured reloadTime (" +
                shotgun.reloadTime + "s); took " + elapsed.ToString("F3") + "s.");
            Assert.LessOrEqual(elapsed, shotgun.reloadTime + 0.5f,
                "Block reload must complete promptly after reloadTime; took " +
                elapsed.ToString("F3") + "s.");
            Assert.AreEqual(shotgun.maxAmmo, shotgun.GetAmmo(),
                "Block reload must restore the full magazine (" + shotgun.maxAmmo +
                " shells).");
            Assert.IsTrue(shotgun.CanFire(),
                "A reloaded Shotgun must be able to fire again.");
        }

        [UnityTest]
        public IEnumerator Shotgun_Reload_InputPath_RKey_TriggersBlockReload()
        {
            Shotgun shotgun = EquipFreshShotgun(3);
            PlayerHUD hud = Object.FindAnyObjectByType<PlayerHUD>();
            Assert.IsNotNull(hud, "PlayerHUD must exist.");
            yield return null;

            // R is bound to reloadAction (PlayerInputActions.SetupInputActions) and
            // WeaponManager routes it to the equipped weapon's StartReload().
            TestInputDevices.PressKeys(Key.R);
            yield return GameBootstrap.WaitUntil(() => shotgun.IsReloading(), 2f);
            TestInputDevices.ReleaseAllKeys();

            Assert.IsTrue(shotgun.IsReloading(),
                "Pressing R with the Shotgun equipped must start a block reload.");

            yield return GameBootstrap.WaitUntil(() => shotgun.GetAmmo() == shotgun.maxAmmo, 4f);

            Assert.AreEqual(shotgun.maxAmmo, shotgun.GetAmmo(),
                "The R-key reload must restore the full magazine.");
            Assert.IsFalse(shotgun.IsReloading(),
                "The R-key reload must complete.");
            Assert.AreEqual("Ammo: " + shotgun.maxAmmo, hud.ammoText.text,
                "HUD must reflect the reloaded magazine (got '" + hud.ammoText.text + "').");
        }
    }
}