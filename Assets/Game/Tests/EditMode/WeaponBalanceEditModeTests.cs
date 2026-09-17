using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace DoomClone.Tests.EditMode
{
    /// <summary>
    /// M15 — Deterministic balance guard. Instantiates every weapon and enemy
    /// headless, invokes their <c>Awake</c> so the real configured values load, and
    /// asserts the tier / cadence / ammo / range / survivability invariants that make
    /// the game an old-school FPS (GAME_SPEC §3–§4). This is a lock, not a rebalance:
    /// changing a number anywhere that breaks these invariants fails here.
    /// </summary>
    public class WeaponBalanceEditModeTests
    {
        static readonly BindingFlags InstanceNonPublic =
            BindingFlags.Instance | BindingFlags.NonPublic;

        static T Spawn<T>() where T : Component
        {
            GameObject go = new GameObject(typeof(T).Name);
            T comp = go.AddComponent<T>();
            // In EditMode, Awake is not auto-invoked; call the real configured values.
            MethodInfo awake = typeof(T).GetMethod("Awake", InstanceNonPublic);
            if (awake != null) awake.Invoke(comp, null);
            return comp;
        }

        static float Dps(float damage, float fireRate) => damage / fireRate;

        static float Ttk(int hp, float damage, float fireRate) => hp / Dps(damage, fireRate);

        [Test]
        public void WeaponDamage_Cadence_Ammo_Range_ConfigsExistPerSpec()
        {
            Pistol pistol = Spawn<Pistol>();
            Shotgun shotgun = Spawn<Shotgun>();
            AssaultRifle rifle = Spawn<AssaultRifle>();
            Chainsaw chainsaw = Spawn<Chainsaw>();
            try
            {
                // Amplitude of each stat reflects its spec role, not tuning noise.
                Assert.AreEqual(15, pistol.damage, "Pistol: low-medium damage.");
                Assert.AreEqual(0.3f, pistol.fireRate, "Pistol: medium rate of fire.");
                Assert.AreEqual(999, pistol.maxAmmo, "Pistol: generous starting ammunition.");
                Assert.AreEqual(100f, pistol.range, "Pistol: hitscan range.");

                Assert.AreEqual(8, shotgun.pelletCount, "Shotgun: 6–8 pellets.");
                Assert.AreEqual(8, shotgun.pelletDamage, "Shotgun: per-pellet damage.");
                Assert.AreEqual(64, shotgun.damage, "Shotgun: point-blank blast (8×8).");
                Assert.AreEqual(0.8f, shotgun.fireRate, "Shotgun: slow cadence.");
                Assert.AreEqual(50, shotgun.maxAmmo, "Shotgun: scarcer than pistol ammo.");
                Assert.AreEqual(30f, shotgun.range, "Shotgun: short hitscan range.");

                Assert.AreEqual(10, rifle.damage, "Rifle: medium damage.");
                Assert.AreEqual(0.1f, rifle.fireRate, "Rifle: high rate of fire.");
                Assert.AreEqual(150f, rifle.range, "Rifle: long range.");
                Assert.AreEqual(120, rifle.maxAmmo, "Rifle: limited, fast-draining ammo.");

                Assert.AreEqual(30, chainsaw.damage, "Chainsaw: per-tick damage.");
                Assert.AreEqual(0.1f, chainsaw.fireRate, "Chainsaw: continuous tick.");
                Assert.IsFalse(chainsaw.usesAmmo, "Chainsaw: unlimited use.");
                Assert.AreEqual(3f, chainsaw.attackRange, "Chainsaw: short-range melee.");
                Assert.AreEqual(300f, chainsaw.damagePerSecond, "Chainsaw: damage per second.");
            }
            finally
            {
                Object.DestroyImmediate(pistol.gameObject);
                Object.DestroyImmediate(shotgun.gameObject);
                Object.DestroyImmediate(rifle.gameObject);
                Object.DestroyImmediate(chainsaw.gameObject);
            }
        }

        [Test]
        public void WeaponDps_TierOrdering_FavorsSpecializationOverFallback()
        {
            Pistol pistol = Spawn<Pistol>();
            Shotgun shotgun = Spawn<Shotgun>();
            AssaultRifle rifle = Spawn<AssaultRifle>();
            Chainsaw chainsaw = Spawn<Chainsaw>();
            try
            {
                float pistolDps = Dps(pistol.damage, pistol.fireRate);
                float shotgunDps = Dps(shotgun.damage, shotgun.fireRate);
                float rifleDps = Dps(rifle.damage, rifle.fireRate);
                float chainsawDps = Dps(chainsaw.damage, chainsaw.fireRate);

                // Chainsaw (300) > Rifle (100) > Shotgun (80 point-blank) > Pistol (50).
                Assert.Greater(chainsawDps, rifleDps, "Sustained melee must be the DPS peak.");
                Assert.Greater(rifleDps, shotgunDps, "Sustained automatic fire beats an 0.8s blast.");
                Assert.Greater(shotgunDps, pistolDps, "Even a slow blast beats the fallback.");
                Assert.Greater(rifleDps, pistolDps,
                    "Rifle tier must clearly exceed the pistol (spread is cadence, not damage).");
            }
            finally
            {
                Object.DestroyImmediate(pistol.gameObject);
                Object.DestroyImmediate(shotgun.gameObject);
                Object.DestroyImmediate(rifle.gameObject);
                Object.DestroyImmediate(chainsaw.gameObject);
            }
        }

        [Test]
        public void WeaponAmmo_Economy_ScarcityMatchesFireRole()
        {
            Pistol pistol = Spawn<Pistol>();
            Shotgun shotgun = Spawn<Shotgun>();
            AssaultRifle rifle = Spawn<AssaultRifle>();
            Chainsaw chainsaw = Spawn<Chainsaw>();
            try
            {
                // Generous fallback > fast-draining auto > scarce blast shells.
                Assert.Greater(pistol.maxAmmo, rifle.maxAmmo,
                    "Pistol ammo must be the most generous (fallback weapon).");
                Assert.Greater(rifle.maxAmmo, shotgun.maxAmmo,
                    "Shotgun shells must be scarcer than rifle rounds.");
                Assert.Greater(pistol.maxAmmo, 10 * shotgun.maxAmmo,
                    "Fallback ammo should out-scale scarce shells by a clear margin.");
                Assert.AreEqual(0, chainsaw.maxAmmo, "Chainsaw ammo sentinel = 0.");
            }
            finally
            {
                Object.DestroyImmediate(pistol.gameObject);
                Object.DestroyImmediate(shotgun.gameObject);
                Object.DestroyImmediate(rifle.gameObject);
                Object.DestroyImmediate(chainsaw.gameObject);
            }
        }

        [Test]
        public void WeaponRange_MeleeVsHitscan_KeepsThreatZonesDistinct()
        {
            Pistol pistol = Spawn<Pistol>();
            Shotgun shotgun = Spawn<Shotgun>();
            AssaultRifle rifle = Spawn<AssaultRifle>();
            Chainsaw chainsaw = Spawn<Chainsaw>();
            try
            {
                Assert.Greater(rifle.range, pistol.range,
                    "Rifle must out-range the pistol (150 > 100).");
                Assert.Greater(pistol.range, shotgun.range,
                    "Shotgun is a close-quarters tool (100 > 30).");
                Assert.Less(chainsaw.attackRange, 5f,
                    "Chainsaw must be strictly melee.");
                Assert.Greater(shotgun.range, chainsaw.attackRange,
                    "Even the short-ranged shotgun out-ranges the chainsaw.");
            }
            finally
            {
                Object.DestroyImmediate(pistol.gameObject);
                Object.DestroyImmediate(shotgun.gameObject);
                Object.DestroyImmediate(rifle.gameObject);
                Object.DestroyImmediate(chainsaw.gameObject);
            }
        }

        [Test]
        public void EnemyStats_TierOrdering_ThreeBehavioralRoles()
        {
            ZombieRunner runner = Spawn<ZombieRunner>();
            RangedSoldier soldier = Spawn<RangedSoldier>();
            TankBrute brute = Spawn<TankBrute>();
            try
            {
                // Health: escalating.
                Assert.Less(runner.maxHealth, soldier.maxHealth);
                Assert.Less(soldier.maxHealth, brute.maxHealth);

                // Speed: runner > soldier > brute.
                Assert.Greater(runner.moveSpeed, soldier.moveSpeed);
                Assert.Greater(soldier.moveSpeed, brute.moveSpeed);

                // Attack: escalating, but slower the heavier the hit.
                Assert.Less(runner.attackDamage, soldier.attackDamage);
                Assert.Less(soldier.attackDamage, brute.attackDamage);
                Assert.Greater(runner.attackRate, 0f);
                Assert.Greater(soldier.attackRate, runner.attackRate,
                    "Soldier attacks slower than the runner.");
                Assert.Greater(brute.attackRate, soldier.attackRate,
                    "Brute attacks slower than the soldier.");

                // Soldier is the ranged threat; runner/brute are melee.
                Assert.Greater(soldier.attackRange, 10f,
                    "Soldier must attack from range (25 m).");
                Assert.Less(runner.attackRange, 5f, "Runner is melee-only.");
                Assert.Less(brute.attackRange, 5f, "Brute is melee-only.");
                Assert.AreEqual(0.5f, brute.telegraphDuration,
                    "Brute slam telegraphs ~0.5 s for the dodge window.");
            }
            finally
            {
                Object.DestroyImmediate(runner.gameObject);
                Object.DestroyImmediate(soldier.gameObject);
                Object.DestroyImmediate(brute.gameObject);
            }
        }

        [Test]
        public void TimeToKill_PlayerAndEnemy_SurvivabilityIsCoherent()
        {
            ZombieRunner runner = Spawn<ZombieRunner>();
            RangedSoldier soldier = Spawn<RangedSoldier>();
            TankBrute brute = Spawn<TankBrute>();
            Pistol pistol = Spawn<Pistol>();
            AssaultRifle rifle = Spawn<AssaultRifle>();
            Shotgun shotgun = Spawn<Shotgun>();
            try
            {
                const int playerHp = 100;

                // Player must kill the smallest threat quickly with the fallback pistol:
                // runner 30 HP at pistol 50 DPS = 0.6 s.
                float pistolRunnerTtk = Ttk(runner.maxHealth, pistol.damage, pistol.fireRate);
                Assert.Less(pistolRunnerTtk, 1f,
                    "Pistol kills a runner in under a second (got " + pistolRunnerTtk + " s).");

                // Even the tank dies to a full pistol mag scenario within reach:
                // brute 200 HP at rifle 100 DPS = 2 s of sustained fire.
                float rifleBruteTtk = Ttk(brute.maxHealth, rifle.damage, rifle.fireRate);
                Assert.Less(rifleBruteTtk, 3f,
                    "Rifle kills the brute in ~2 s of sustained fire (got " + rifleBruteTtk + " s).");

                // Shotgun point-blank one-shots the runner but NOT the brute:
                Assert.GreaterOrEqual(shotgun.damage, runner.maxHealth,
                    "Close blast must drop a runner in one hit.");
                Assert.Less(shotgun.damage, brute.maxHealth,
                    "No single blast one-shots the brute.");

                // No enemy can one-shot the 100 HP player (worst hit is brute slam 30).
                Assert.Less(brute.slamDamage, playerHp,
                    "Brute slam cannot instantly kill the player.");
                Assert.Less(brute.attackDamage, playerHp);
                Assert.Less(soldier.attackDamage, playerHp);
                Assert.Less(runner.attackDamage, playerHp);

                // Brute vs pistol: 200 / 50 = 4 s — a tank moving at 1.5 m/s gives
                // the player that whole window to back off or swap.
                float pistolBruteTtk = Ttk(brute.maxHealth, pistol.damage, pistol.fireRate);
                Assert.Greater(pistolBruteTtk, 3f,
                    "Pistol vs brute must be slow (role pressure), got " + pistolBruteTtk + " s.");
            }
            finally
            {
                Object.DestroyImmediate(runner.gameObject);
                Object.DestroyImmediate(soldier.gameObject);
                Object.DestroyImmediate(brute.gameObject);
                Object.DestroyImmediate(pistol.gameObject);
                Object.DestroyImmediate(rifle.gameObject);
                Object.DestroyImmediate(shotgun.gameObject);
            }
        }
    }
}