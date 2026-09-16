using System.Collections;
using DoomClone.Automation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace DoomClone.Tests.PlayMode
{
    /// <summary>
    /// M9 — Chainsaw PlayMode verification per GAME_SPEC.md §3.1 and TEST_PLAN.md
    /// "Weapons: Chainsaw" (attacks only at short range; held attack deals repeated
    /// damage over time; no ammo is ever consumed) plus the M9 scope: continuous
    /// on/attack feedback (camera vibration) running exception-free over a sustained
    /// hold.
    ///
    /// Per-attack determinism follows the M6–M8 pattern: one direct production
    /// <see cref="Chainsaw.Fire()"/> for exact per-tick damage/event asserts;
    /// input-path holds with bounded windows for the sustained-DPS behavior.
    /// </summary>
    public class ChainsawTests
    {
        const string Level = GameTestAPI.LevelSceneName;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            TestInputDevices.EnsureDevices();
            // A prior test in the same PlayMode session may have left the synthetic
            // mouse/keyboard pressed; force a clean baseline so the Chainsaw's own
            // auto-fire Update() can never race a direct Fire() call.
            TestInputDevices.SetFireButton(false);
            TestInputDevices.ReleaseAllKeys();
            yield return GameTestAPI.StartFreshLevel(30f);
            GameTestAPI.SetPlayerHealth(100); // guard against incidental wave damage
            yield return null;
        }

        static WeaponManager Weapons() => GameTestAPI.GetWeapons();

        static Chainsaw EquipFreshChainsaw()
        {
            WeaponManager wm = Weapons();
            wm.EquipWeapon(GameTestAPI.WeaponChainsaw);
            Chainsaw saw = wm.GetCurrentWeapon() as Chainsaw;
            Assert.IsNotNull(saw, "Slot 3 (hotkey 4) must be the Chainsaw.");
            return saw;
        }

        /// <summary>
        /// Rotates the player to look at a real level surface (wall/floor, ignoring
        /// enemies) and teleports the player ~2 m away so every held chainsaw tick
        /// resolves against real geometry through the production raycast/audio path
        /// (the M5 A1 pattern).
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
        /// <paramref name="clearance"/> meters from the camera eye, guaranteeing a
        /// demonstrably empty line of fire for the melee ray. Skips enemy colliders
        /// so (temporarily wandering) wave enemies are ignored when choosing.
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

            Assert.IsTrue(bestStep >= 0, "No direction found for the melee test.");
            Assert.Greater(bestLen, clearance,
                "Must be a clear corridor of at least " + clearance +
                "m for the melee test (best=" + bestLen.ToString("F1") + "m).");

            return Quaternion.Euler(0, bestStep * 15f, 0) * Vector3.forward;
        }

        /// <summary>
        /// Spawns a target enemy of the given type, freezes it on the spot and aims the
        /// player down a clear corridor with the target <paramref name="distance"/> meters
        /// in front of the camera eye. Returns the spawned Enemy.
        ///
        /// The freeze is belt-and-braces (same fixture as the M8 rifle tests): the
        /// WaveManager EnableNavMeshAgent coroutine is neutralized by synchronously
        /// removing the NavMeshAgent (the coroutine exits at its null guard), the Enemy
        /// component is disabled so no AI runs, the brute's production scale-2 hitbox is
        /// mirrored (its Start never runs on a disabled component), the floor-aligned root
        /// is lifted ~1.6m to span the camera eye, and physics is synced explicitly
        /// because a collider on a non-rigidbody object does not follow transform.position
        /// until the next physics step.
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

            Vector3 targetPos = camT.position + camT.forward * distance;
            if (NavMeshUtil.TrySnapToNavMesh(targetPos, 3f, out Vector3 snapped))
                targetPos = new Vector3(snapped.x, snapped.y + 1.6f, snapped.z);
            if (type == "brute")
                spawned.transform.localScale = Vector3.one * 2f;
            spawned.transform.position = targetPos;
            Physics.SyncTransforms();

            return enemy;
        }

        /// <summary>
        /// Asserts the camera eye ray strikes <paramref name="expected"/> when cast at the
        /// full <paramref name="maxDistance"/>, proving the target really sits in the line
        /// of fire (so a chainsaw miss can only be imputed to range, not alignment).
        /// </summary>
        static void AssertLineOfFireClearTo(Enemy expected, float maxDistance)
        {
            Transform camT = GameTestAPI.GetPlayer().playerCamera.transform;
            if (Physics.Raycast(camT.position, camT.forward, out RaycastHit sanity, maxDistance))
            {
                Enemy hitEnemy = sanity.collider.GetComponentInParent<Enemy>();
                Assert.AreEqual(expected, hitEnemy,
                    "Line of fire must strike the spawned target (got: " +
                    (hitEnemy != null ? hitEnemy.name : sanity.collider.name) + ").");
            }
            else
            {
                Assert.Fail("Raycast must find the spawned enemy in front of the camera.");
            }
        }

        // ── Configuration (GAME_SPEC §3.1) ─────────────────────────────────

        [UnityTest]
        public IEnumerator Chainsaw_Config_ShortRangeHighDamagePerSecondUnlimited_MatchSpec()
        {
            yield return null;

            Chainsaw saw = EquipFreshChainsaw();
            WeaponManager wm = Weapons();
            yield return null;

            // Short-range melee: well below any firearm's range, tight enough to be melee.
            Assert.Greater(saw.attackRange, 0f,
                "Chainsaw attack range must be positive.");
            Assert.LessOrEqual(saw.attackRange, 4f,
                "Chainsaw must be a short-range melee weapon (range=" +
                saw.attackRange + "m).");
            Assert.Less(saw.attackRange, 10f,
                "Chainsaw range must be far below the pistol's hitscan range (" +
                saw.attackRange + "m).");

            // High damage per second while held: per-tick damage plus a fast cadence.
            Assert.GreaterOrEqual(saw.damage, 20,
                "Each chainsaw tick must deal high damage (damage=" + saw.damage + ").");
            Assert.LessOrEqual(saw.fireRate, 0.2f,
                "Chainsaw cadence must be fast enough to feel continuous (fireRate=" +
                saw.fireRate + "s).");

            // The realized sustained DPS (damage / cadence) must out-pace the rifle tier,
            // so the melee weapon's high per-second damage has a clear gameplay identity.
            Weapon rifle = wm.GetWeapons()[GameTestAPI.WeaponAssaultRifle];
            float sawDps = saw.damage / saw.fireRate;
            float rifleDps = rifle.damage / rifle.fireRate;
            Assert.Greater(sawDps, rifleDps,
                "Chainsaw sustained DPS must exceed the Assault Rifle's (" +
                sawDps.ToString("F0") + " vs " + rifleDps.ToString("F0") + ").");

            // Config truthfulness: the declared damagePerSecond must match what the damage/
            // cadence fields actually produce (30 per 0.1s tick == 300/s).
            Assert.AreEqual(saw.damage / saw.fireRate, saw.damagePerSecond, 0.5f,
                "Chainsaw damagePerSecond must match damage/fireRate (" +
                (saw.damage / saw.fireRate).ToString("F0") + " vs " +
                saw.damagePerSecond.ToString("F0") + ").");

            // Unlimited use / no ammunition (GAME_SPEC §3.1).
            Assert.IsFalse(saw.UsesAmmo(), "Chainsaw must not consume ammunition.");
            Assert.AreEqual(0, saw.maxAmmo, "Chainsaw must have no magazine capacity.");
            Assert.AreEqual(0, saw.GetAmmo(), "Chainsaw ammo must read as the 0 sentinel.");
            Assert.IsTrue(saw.CanFire(), "Chainsaw must always be able to attack (no ammo gate).");
        }

        // ── Range: attacks only at short range ─────────────────────────────

        [UnityTest]
        public IEnumerator Chainsaw_Range_EnemyWithinRange_TakesConfiguredDamagePerTick()
        {
            Chainsaw saw = EquipFreshChainsaw();
            PlayerController player = GameTestAPI.GetPlayer();
            Assert.IsNotNull(player, "Player must exist.");
            yield return null;

            // Brute (200 HP) at 2m: clearly inside the 3m chainsaw range, huge collider,
            // and it absorbs a full 30-damage tick without dying mid-test.
            Enemy enemy = SpawnFrozenTarget("brute", 2f);
            yield return null;

            AssertLineOfFireClearTo(enemy, saw.attackRange * 2f);

            int damageEvents = 0;
            void OnDamage(Enemy e, int d) { damageEvents++; }
            enemy.OnEnemyDamaged += OnDamage;

            TestInputDevices.SetFireButton(false);
            yield return null;

            int hpBefore = enemy.GetHealth();
            int ammoBefore = saw.GetAmmo();

            saw.Fire(); // one melee tick through the real production method

            // Cadence lock is a same-frame property: Fire() arms nextFireTime and a
            // frame can never elapse between the call and this read, so the 0.1 s
            // fireRate cannot be consumed by a slow batch frame (1-5 FPS) before the
            // assert observes it.
            Assert.IsFalse(saw.CanFire(),
                "Chainsaw must be on cadence lock immediately after a tick.");
            yield return null;

            enemy.OnEnemyDamaged -= OnDamage;

            Assert.AreEqual(1, damageEvents,
                "One chainsaw tick in range must land exactly one damage event.");
            Assert.AreEqual(hpBefore - saw.damage, enemy.GetHealth(),
                "One in-range chainsaw tick must deal exactly the configured damage (" +
                saw.damage + ") (before=" + hpBefore + ", after=" + enemy.GetHealth() + ").");
            Assert.Greater(enemy.GetHealth(), 0,
                "A single tick must not kill the 200 HP brute.");
            Assert.AreEqual(ammoBefore, saw.GetAmmo(),
                "An in-range attack must never consume ammo.");
        }

        [UnityTest]
        public IEnumerator Chainsaw_Range_EnemyBeyondRange_TakesNoDamage()
        {
            Chainsaw saw = EquipFreshChainsaw();
            PlayerController player = GameTestAPI.GetPlayer();
            Assert.IsNotNull(player, "Player must exist.");
            yield return null;

            // Runner at 5m: well beyond the 3m melee range. Its capsule's near edge
            // (~4.5m) never comes within the 3m ray, so the tick must be a clean miss.
            Enemy enemy = SpawnFrozenTarget("runner", 5f);
            yield return null;

            // Prove the target really sits in the line of fire at full ray distance, so the
            // miss below can only be blamed on RANGE, not alignment or occlusion.
            AssertLineOfFireClearTo(enemy, 60f);

            int damageEvents = 0;
            void OnDamage(Enemy e, int d) { damageEvents++; }
            enemy.OnEnemyDamaged += OnDamage;

            TestInputDevices.SetFireButton(false);
            yield return null;

            int hpBefore = enemy.GetHealth();
            int ammoBefore = saw.GetAmmo();

            saw.Fire(); // ray never reaches the 5m runner (range-limited)
            yield return null;

            enemy.OnEnemyDamaged -= OnDamage;

            Assert.AreEqual(0, damageEvents,
                "An enemy beyond the chainsaw range must take no damage.");
            Assert.AreEqual(hpBefore, enemy.GetHealth(),
                "HP must be untouched for an out-of-range enemy (before=" +
                hpBefore + ", after=" + enemy.GetHealth() + ").");
            Assert.AreEqual(ammoBefore, saw.GetAmmo(),
                "Chainsaw ammo must remain at the 0 sentinel after a miss.");
        }

        // ── Held attack: repeated damage over time, no ammo ────────────────

        [UnityTest]
        public IEnumerator Chainsaw_Held_SpawnedEnemy_RepeatedDamageOverTimeAndNoAmmoConsumed()
        {
            Chainsaw saw = EquipFreshChainsaw();
            PlayerController player = GameTestAPI.GetPlayer();
            Assert.IsNotNull(player, "Player must exist.");
            GameTestAPI.SetPlayerHealth(100);
            yield return null;

            // Brute at 2m: inside the 3m range and its 200 HP absorbs the whole held burst.
            Enemy enemy = SpawnFrozenTarget("brute", 2f);
            yield return null;

            AssertLineOfFireClearTo(enemy, saw.attackRange * 2f);

            int hits = 0;
            int ammoEvents = 0;
            void OnDamage(Enemy e, int d) { hits++; }
            void OnAmmo(int current, bool infinite) { ammoEvents++; }
            enemy.OnEnemyDamaged += OnDamage;
            saw.OnAmmoChanged += OnAmmo;

            int hpBefore = enemy.GetHealth();
            int ammoBefore = saw.GetAmmo();

            // Hold until at least 2 ticks LAND on the brute (deadline based, so the
            // assertion is independent of the erratic ~5-8 FPS batch frame rate), then
            // release while the brute still has HP to spare.
            TestInputDevices.SetFireButton(true);
            float deadline = Time.realtimeSinceStartup + 8f;
            while (hits < 2 && Time.realtimeSinceStartup < deadline)
                yield return null;
            TestInputDevices.SetFireButton(false);
            yield return null;

            enemy.OnEnemyDamaged -= OnDamage;
            saw.OnAmmoChanged -= OnAmmo;

            Assert.GreaterOrEqual(hits, 2,
                "Holding the attack against an in-range enemy must land repeated damage " +
                "(hits=" + hits + ").");
            Assert.AreEqual(hpBefore - hits * saw.damage, enemy.GetHealth(),
                "Every landed chainsaw tick must apply exactly the configured damage (" +
                saw.damage + ") (hp before=" + hpBefore + ", after=" + enemy.GetHealth() + ").");
            Assert.Greater(enemy.GetHealth(), 0,
                "The 200 HP brute must survive the measured burst.");
            Assert.AreEqual(ammoBefore, saw.GetAmmo(),
                "A sustained chainsaw attack must never decrement ammo (stays at the " +
                ammoBefore + " sentinel).");
            Assert.AreEqual(0, ammoEvents,
                "A no-ammo weapon must never raise OnAmmoChanged while attacking.");
        }

        // ── Continuous on/attack feedback ──────────────────────────────────

        [UnityTest]
        public IEnumerator Chainsaw_Feedback_HeldAttack_RunsContinuouslyWithoutExceptions()
        {
            Chainsaw saw = EquipFreshChainsaw();
            PlayerController player = GameTestAPI.GetPlayer();
            Assert.IsNotNull(player, "Player must exist.");

            CameraShake shake = player.playerCamera.GetComponent<CameraShake>();
            Assert.IsNotNull(shake, "The player camera must carry the CameraShake component.");

            // Aim at a real wall so every held tick resolves through the production
            // raycast/audio path without hitting an enemy.
            AimPlayerAtWall(player);
            yield return null;

            Vector3 basePos = player.playerCamera.transform.localPosition;
            float maxDisplacement = 0f;
            int attackFrames = 0;
            TestInputDevices.SetFireButton(true);
            // Hold until at least 2 attacking frames are OBSERVED (not a fixed wall-clock
            // window): in a busy batch run a frame can last longer than any short hold, and
            // in its single frame the chainsaw Update may run before the queued fire-press
            // registers — leaving isAttacking false for the whole press. Requiring observed
            // attack frames forces the press to register and the 0.1s cadence to reopen at
            // least twice, independent of frame rate.
            //
            // Displacement is sampled on EVERY frame, not just attacking ones: the camera
            // vibration (CameraShake.Update) materializes one frame AFTER the chainsaw's
            // attacking tick in PlayMode script order (Chainsaw.Update runs before the
            // test coroutine, and CameraShake.Update runs after it in the same frame), so
            // reading the camera position only on attacking frames can miss it entirely.
            float deadline = Time.realtimeSinceStartup + 8f;
            while (attackFrames < 2 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                float d = Vector3.Distance(player.playerCamera.transform.localPosition, basePos);
                if (d > maxDisplacement) maxDisplacement = d;
                if (saw.IsAttacking()) attackFrames++;
            }
            TestInputDevices.SetFireButton(false);
            yield return null;

            Assert.GreaterOrEqual(attackFrames, 2,
                "Holding fire must drive the chainsaw through repeated attacking frames " +
                "(attackFrames=" + attackFrames + ").");
            // The continuous on/attack feedback ran for the hold without an exception
            // (any thrown exception fails this UnityTest) AND the camera-vibration method
            // actually displaced the camera off its rest pose (shakeAmount 0.02).
            Assert.Greater(maxDisplacement, 0.005f,
                "Camera vibration must displace the camera during a sustained attack " +
                "(attackFrames=" + attackFrames +
                ", max displacement=" + maxDisplacement.ToString("F4") + "m).");
        }
    }
}