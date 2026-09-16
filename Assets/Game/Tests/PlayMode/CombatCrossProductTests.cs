using System.Collections;
using DoomClone.Automation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace DoomClone.Tests.PlayMode
{
    /// <summary>
    /// M15 — Integration polish: validate every enemy type against every weapon.
    /// GAME_SPEC's cross-product (4 weapons × 3 enemy types) must resolve combat
    /// without exceptions and without "no damage despite a clear hit" cases. The
    /// dedicated per-weapon suites (M5–M9) fired at runners and brutes; this closes
    /// the remaining gap: firing a REAL player weapon at the Ranged Soldier, plus a
    /// deterministic check that every weapon damages every type.
    ///
    /// Isolation is the established EnemyFramework pattern:
    ///  - WipeAllEnemies() kills + immediately destroys every living/dead enemy so the
    ///    firing ray can never be intercepted by a wave enemy, a shrinking corpse or a
    ///    lingering impact sphere.
    ///  - Each (weapon, type) case is a single synchronous block (no yields), so no
    ///    wave can spawn mid-case and no AI can move between sanity-check and Fire().
    ///  - SpawnFrozenTarget freezes the target at a descending distance per slot so a
    ///    target is never placed behind the previous case's corpse.
    /// A single direct Fire() per case matches the M6/M7 pattern for exact per-shot
    /// state; every case asserts the ray really strikes the target first.
    /// </summary>
    public class CombatCrossProductTests
    {
        const string Level = GameTestAPI.LevelSceneName;

        static readonly int[] WeaponSlots =
        {
            GameTestAPI.WeaponPistol,
            GameTestAPI.WeaponShotgun,
            GameTestAPI.WeaponAssaultRifle,
            GameTestAPI.WeaponChainsaw,
        };

        // Descending per-slot distance so each new target sits CLOSER than every
        // previous case's corpse/impact: the firing ray always strikes the current
        // target before any leftover from an earlier case.
        static readonly float[] TargetDistances = { 4f, 3f, 2.5f, 1.8f };

        [UnitySetUp]
        public IEnumerator Setup()
        {
            TestInputDevices.EnsureDevices();
            // No stray fire button: a pressed weapon would auto-fire in the same frame
            // as our direct Fire() and break the one-shot accounting.
            TestInputDevices.SetFireButton(false);
            TestInputDevices.ReleaseAllKeys();
            yield return GameTestAPI.StartFreshLevel(30f);
            GameTestAPI.SetPlayerHealth(100); // guard against incidental wave damage
            yield return null;
        }

        // ── Matrix entry points ───────────────────────────────────────────

        [UnityTest]
        public IEnumerator CombatCrossProduct_EveryWeaponDamagesRunner_WithoutExceptions()
        {
            yield return CombatMatrixFor("runner");
        }

        [UnityTest]
        public IEnumerator CombatCrossProduct_EveryWeaponDamagesSoldier_WithoutExceptions()
        {
            yield return CombatMatrixFor("soldier");
        }

        [UnityTest]
        public IEnumerator CombatCrossProduct_EveryWeaponDamagesBrute_WithoutExceptions()
        {
            yield return CombatMatrixFor("brute");
        }

        static IEnumerator CombatMatrixFor(string type)
        {
            yield return null;

            PlayerController player = GameTestAPI.GetPlayer();
            Assert.IsNotNull(player, "Player must exist for the combat matrix.");
            WeaponManager wm = GameTestAPI.GetWeapons();
            Assert.IsNotNull(wm, "WeaponManager must exist for the combat matrix.");

            for (int i = 0; i < WeaponSlots.Length; i++)
            {
                yield return FireOneShotAtFrozenEnemy(player, wm, type, WeaponSlots[i], TargetDistances[i]);
            }

            GameTestAPI.SetPlayerHealth(100);
        }

        // ── One synchronous weapon×enemy case ─────────────────────────────

        static IEnumerator FireOneShotAtFrozenEnemy(
            PlayerController player, WeaponManager wm, string type, int slot, float distance)
        {
            // Purge every enemy (wave enemies, corpses, prior frozen targets) and every
            // lingering firing/impact marker so nothing can intercept the firing ray.
            WipeAllEnemies();
            DestroyImpactSpheres();

            wm.EquipWeapon(slot);
            Weapon weapon = wm.GetCurrentWeapon();
            Assert.IsNotNull(weapon, "Slot " + slot + " must resolve to a weapon.");
            if (weapon.UsesAmmo())
                weapon.SetAmmo(weapon.maxAmmo);
            Assert.IsTrue(weapon.CanFire(),
                weapon.GetName() + " must be fireable right after equipping a fresh mag.");

            // The ONLY enemy that exists now is the frozen target.
            Enemy enemy = SpawnFrozenTarget(player, type, distance);
            yield return null;
            Physics.SyncTransforms();

            GameTestAPI.SetPlayerHealth(100);

            // Sanity: a real ray from the camera eye must strike the spawned target's
            // collider before anything else.
            Transform camT = player.playerCamera.transform;
            Enemy struck = null;
            foreach (RaycastHit hit in Physics.RaycastAll(camT.position, camT.forward, 6f))
            {
                Enemy e = hit.collider.GetComponentInParent<Enemy>();
                if (e == enemy)
                {
                    struck = e;
                    break;
                }
            }
            Assert.AreEqual(enemy, struck,
                weapon.GetName() + " must have a clear line of fire to the frozen " + type +
                " (got " + (struck != null ? struck.GetName() : "no enemy") + " instead of " +
                type + ").");

            int hits = 0;
            int damageSum = 0;
            void OnDamaged(Enemy e, int d) { hits++; damageSum += d; }
            enemy.OnEnemyDamaged += OnDamaged;

            int hpBefore = enemy.GetHealth();
            weapon.Fire();
            yield return null;

            enemy.OnEnemyDamaged -= OnDamaged;

            int hpAfter = enemy.GetHealth();

            Assert.GreaterOrEqual(hits, 1,
                weapon.GetName() + " must land at least one hit on a frozen " + type +
                " at " + distance + " m (hits=" + hits + ").");
            Assert.AreEqual(hpBefore - damageSum, hpAfter,
                weapon.GetName() + " damage events must account for the full HP drop on " +
                type + " (before=" + hpBefore + ", after=" + hpAfter + ", eventSum=" +
                damageSum + ").");
            Assert.GreaterOrEqual(weapon.GetAmmo(), 0,
                weapon.GetName() + " ammo must never drop below zero.");
        }

        // ── Field purging ─────────────────────────────────────────────────

        /// <summary>
        /// Kills every living enemy and immediately removes every Enemy GameObject
        /// (living ones first via the real TakeDamage→Die→OnEnemyDeath path so wave
        /// tracking stays consistent, then DestroyImmediate to purge their colliders
        /// instead of waiting out the ~1 s death-shrink animation).
        /// </summary>
        static void WipeAllEnemies()
        {
            Enemy[] enemies = Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None);
            if (enemies == null) return;

            for (int i = 0; i < enemies.Length; i++)
            {
                Enemy enemy = enemies[i];
                if (enemy == null) continue;
                if (!enemy.IsDead())
                    enemy.TakeDamage(int.MaxValue);
            }

            for (int i = 0; i < enemies.Length; i++)
            {
                Enemy enemy = enemies[i];
                if (enemy == null) continue;
                if (enemy.gameObject != null)
                    Object.DestroyImmediate(enemy.gameObject);
            }
        }

        /// <summary>Destroys firing/impact marker primitives that still carry colliders and
        /// would otherwise intercept the firing ray for the next case.</summary>
        static void DestroyImpactSpheres()
        {
            SphereCollider[] spheres = Object.FindObjectsByType<SphereCollider>(FindObjectsSortMode.None);
            if (spheres == null) return;

            for (int i = 0; i < spheres.Length; i++)
            {
                SphereCollider sphere = spheres[i];
                if (sphere == null) continue;
                GameObject go = sphere.gameObject;
                if (go == null) continue;
                if (go.GetComponentInParent<Enemy>() != null) continue; // enemy capsule hitboxes
                if (go.name.StartsWith("Sphere"))
                    Object.DestroyImmediate(go);
            }
        }

        // ── Frozen target placement (AssaultRifleTests pattern) ───────────

        static Enemy SpawnFrozenTarget(PlayerController player, string type, float distance)
        {
            Assert.IsNotNull(player, "Player must exist to place a target.");

            GameObject spawned = GameTestAPI.SpawnEnemy(type);
            Assert.IsNotNull(spawned, "GameTestAPI.SpawnEnemy('" + type + "') must return an enemy.");
            Enemy enemy = spawned.GetComponent<Enemy>();
            Assert.IsNotNull(enemy, "Spawned '" + type + "' must carry an Enemy component.");

            // Freeze: neutralize the WaveManager nav coroutine (drops the agent so it exits
            // at its null guard) and stop the AI so the target cannot move or attack on us.
            NavMeshAgent agent = spawned.GetComponent<NavMeshAgent>();
            if (agent != null)
                Object.DestroyImmediate(agent);
            enemy.enabled = false;

            Assert.AreEqual(enemy.GetMaxHealth(), enemy.GetHealth(),
                "Freshly spawned '" + type + "' must start at full health.");

            // Because the field was purged, the only Enemy in the scene is this target, so
            // any direction is clear of rivals; still snap to a corridor free of walls by
            // picking the longest clear forward from the camera eye.
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

        /// <summary>Picks a horizontal direction free of walls for at least
        /// <paramref name="clearance"/> meters so the sanity ray and the weapon ray reach
        /// the target instead of real geometry.</summary>
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

            Assert.IsTrue(bestStep >= 0, "No direction found for the combat matrix target.");
            Assert.Greater(bestLen, clearance,
                "Must be a clear corridor of at least " + clearance +
                " m for the combat matrix target (best=" + bestLen.ToString("F1") + " m).");

            return Quaternion.Euler(0, bestStep * 15f, 0) * Vector3.forward;
        }
    }
}