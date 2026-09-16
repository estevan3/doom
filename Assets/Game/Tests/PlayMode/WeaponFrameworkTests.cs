using System.Collections;
using DoomClone.Automation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace DoomClone.Tests.PlayMode
{
    /// <summary>
    /// M5 — Weapon framework PlayMode verification per GAME_SPEC.md §3 and
    /// TEST_PLAN.md "Weapons". Scope is the cross-cutting framework only:
    /// ownership/enumeration, switching (keys 1-4 + EquipWeapon), HUD reflection,
    /// the common ammo/ammo-changed contract, and firing/impact feedback hooks.
    /// Per-weapon damage/cadence/pellet behavior is Milestones 6-9.
    /// </summary>
    public class WeaponFrameworkTests
    {
        const string Level = GameTestAPI.LevelSceneName;

        static void EnsureInputDevices() => TestInputDevices.EnsureDevices();

        static void PressKeys(params Key[] keys) => TestInputDevices.PressKeys(keys);

        static void ReleaseAllKeys() => TestInputDevices.ReleaseAllKeys();

        static void SetFireButton(bool pressed) => TestInputDevices.SetFireButton(pressed);

        [UnitySetUp]
        public IEnumerator Setup()
        {
            EnsureInputDevices();
            yield return GameTestAPI.StartFreshLevel(30f);
            GameTestAPI.SetPlayerHealth(100);
            yield return null;
        }

        static WeaponManager Weapons() => GameTestAPI.GetWeapons();

        static int EnabledWeaponCount(WeaponManager wm)
        {
            int active = 0;
            foreach (Weapon w in wm.GetWeapons())
                if (w.enabled) active++;
            return active;
        }

        /// <summary>
        /// Rotates the player to look at a real level surface (wall/floor, ignoring
        /// enemies) and teleports the player ~2 m away so every weapon, including the
        /// short-range Chainsaw, actually reaches the wall. Shots then resolve against
        /// real geometry through the production impact-hook path.
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

        // ── Ownership / base contract ──────────────────────────────────────

        [UnityTest]
        public IEnumerator Weapon_Framework_ManagerOwnsAllFourWeapons()
        {
            yield return null;

            WeaponManager wm = Weapons();
            Assert.IsNotNull(wm, "Player must have a WeaponManager after boot.");

            Weapon[] ws = wm.GetWeapons();
            Assert.IsNotNull(ws, "WeaponManager must expose its weapon list.");
            Assert.AreEqual(4, ws.Length, "Exactly four weapons are required by GAME_SPEC.md §3.");

            // The 1-4 order contract (index == hotkey - 1). Pistol default at index 0.
            Assert.IsTrue(ws[0] is Pistol, "Slot 0 must be Pistol (default/fallback).");
            Assert.IsTrue(ws[1] is Shotgun, "Slot 1 must be Shotgun.");
            Assert.IsTrue(ws[2] is AssaultRifle, "Slot 2 must be AssaultRifle.");
            Assert.IsTrue(ws[3] is Chainsaw, "Slot 3 must be Chainsaw.");

            // No slot may be null; every weapon reveals metadata to the manager.
            for (int i = 0; i < ws.Length; i++)
            {
                Assert.IsNotNull(ws[i], "Weapon slot " + i + " must not be null.");
                Assert.IsFalse(string.IsNullOrEmpty(ws[i].GetName()),
                    "Weapon slot " + i + " must report a name.");
                Assert.IsTrue(ws[i].enabled == false || ws[i] == wm.GetCurrentWeapon(),
                    "Only the equipped weapon may be enabled before equip.");
            }
        }

        [UnityTest]
        public IEnumerator Weapon_BaseContract_AmmoFlagsAndMags_AreConsistent()
        {
            yield return null;

            WeaponManager wm = Weapons();
            Weapon[] ws = wm.GetWeapons();

            // Chainsaw: no ammo. Others: finite starting magazine.
            Assert.IsFalse(ws[3].UsesAmmo(), "Chainsaw must be ammo-free.");
            Assert.IsTrue(ws[0].UsesAmmo(), "Pistol must use ammo.");
            Assert.IsTrue(ws[1].UsesAmmo(), "Shotgun must use ammo.");
            Assert.IsTrue(ws[2].UsesAmmo(), "AssaultRifle must use ammo.");

            int[] expectedMag =
            {
                ws[0].maxAmmo, // Pistol generous starting ammo
                ws[1].maxAmmo, // Shotgun limited ammo (scarcer than pistol)
                ws[2].maxAmmo, // AssaultRifle drains fast
                0              // Chainsaw
            };

            for (int i = 0; i < ws.Length; i++)
            {
                Assert.AreEqual(expectedMag[i], ws[i].GetAmmo(),
                    "Weapon " + ws[i].GetName() + " must start with a full magazine.");
                Assert.AreEqual(ws[i].maxAmmo, ws[i].GetAmmo(),
                    "Weapon " + ws[i].GetName() + " must start at maxAmmo.");
            }

            Assert.Greater(ws[0].maxAmmo, ws[1].maxAmmo,
                "GAME_SPEC §3.3: pistol ammunition must be more generous than shotgun.");
            Assert.IsTrue(ws[3].CanFire(), "Chainsaw must always be able to attack (no ammo gate).");
        }

        // ── Default weapon ─────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Weapon_Boot_DefaultsToPistol()
        {
            yield return null;

            WeaponManager wm = Weapons();
            Assert.AreEqual(GameTestAPI.WeaponPistol, wm.currentWeaponIndex,
                "Player must boot with Pistol equipped.");
            Assert.IsTrue(wm.GetCurrentWeapon() is Pistol,
                "Equipped weapon at boot must be the Pistol.");
            Assert.IsTrue(wm.GetCurrentWeapon().enabled,
                "The equipped weapon must be enabled at boot.");
            Assert.AreEqual(1, EnabledWeaponCount(wm),
                "Exactly one weapon may be enabled at any time.");
        }

        // ── Switching ──────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Weapon_Switch_Keys1To4_SelectExpectedWeapon()
        {
            WeaponManager wm = Weapons();

            Key[] keys =
            {
                Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4
            };
            System.Type[] expected =
            {
                typeof(Pistol), typeof(Shotgun), typeof(AssaultRifle), typeof(Chainsaw)
            };

            for (int i = 0; i < keys.Length; i++)
            {
                // Force a real transition even if the target is (or becomes) the current slot.
                if (wm.currentWeaponIndex == i)
                {
                    PressKeys(keys[(i + 1) % keys.Length]);
                    yield return null;
                    ReleaseAllKeys();
                    yield return null;
                }

                ReleaseAllKeys();
                yield return null;

                PressKeys(keys[i]);
                bool switched = false;
                for (int f = 0; f < 10; f++)
                {
                    yield return null;
                    if (wm.currentWeaponIndex == i) { switched = true; break; }
                }
                ReleaseAllKeys();

                Assert.IsTrue(switched,
                    "Key " + (i + 1) + " must switch the equipped weapon.");
                Assert.AreEqual(i, wm.currentWeaponIndex,
                    "Key " + (i + 1) + " must select weapon slot " + i + ".");
                Assert.AreEqual(expected[i], wm.GetCurrentWeapon().GetType(),
                    "Key " + (i + 1) + " selects the wrong weapon type.");
                Assert.AreEqual(1, EnabledWeaponCount(wm),
                    "Exactly one weapon may be enabled after switching.");
            }
        }

        [UnityTest]
        public IEnumerator Weapon_Switch_EquipWeapon_SelectsAndActivatesExactlyOne()
        {
            WeaponManager wm = Weapons();
            Assert.AreEqual(GameTestAPI.WeaponPistol, wm.currentWeaponIndex,
                "Boot default must be Pistol before EquipWeapon tests.");

            int[] slots =
            {
                GameTestAPI.WeaponShotgun,
                GameTestAPI.WeaponAssaultRifle,
                GameTestAPI.WeaponChainsaw,
                GameTestAPI.WeaponPistol
            };
            System.Type[] expected =
            {
                typeof(Shotgun), typeof(AssaultRifle), typeof(Chainsaw), typeof(Pistol)
            };

            for (int i = 0; i < slots.Length; i++)
            {
                wm.EquipWeapon(slots[i]);
                yield return null;

                Assert.AreEqual(slots[i], wm.currentWeaponIndex,
                    "EquipWeapon must record the requested index [" + slots[i] + "].");
                Assert.AreEqual(expected[i], wm.GetCurrentWeapon().GetType(),
                    "EquipWeapon(" + slots[i] + ") must equip the expected weapon.");
                Assert.IsTrue(wm.GetCurrentWeapon().enabled,
                    "The freshly equipped weapon must be enabled.");
                Assert.AreEqual(1, EnabledWeaponCount(wm),
                    "Exactly one weapon may be enabled after EquipWeapon.");
            }
        }

        // ── HUD ────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Weapon_HUD_ReflectsEquippedWeaponAndAmmo()
        {
            PlayerHUD hud = Object.FindAnyObjectByType<PlayerHUD>();
            Assert.IsNotNull(hud, "PlayerHUD must exist.");
            WeaponManager wm = Weapons();

            (int, string, string)[] cases =
            {
                (GameTestAPI.WeaponPistol, "Pistol", "Ammo: 999"),
                (GameTestAPI.WeaponShotgun, "Shotgun", "Ammo: 50"),
                (GameTestAPI.WeaponAssaultRifle, "Assault Rifle", "Ammo: 120"),
                (GameTestAPI.WeaponChainsaw, "Chainsaw", "Ammo: Infinite")
            };

            for (int i = 0; i < cases.Length; i++)
            {
                wm.EquipWeapon(cases[i].Item1);
                yield return null;

                Assert.AreEqual("Weapon: " + cases[i].Item2, hud.weaponText.text,
                    "HUD weaponText must reflect the equipped weapon " + cases[i].Item2 +
                    " (got: '" + hud.weaponText.text + "').");
                Assert.AreEqual(cases[i].Item3, hud.ammoText.text,
                    "HUD ammoText must reflect the equipped weapon ammo (got: '" +
                    hud.ammoText.text + "').");
            }
        }

        // ── Ammo contract (SetAmmo + firing gates) ─────────────────────────

        [UnityTest]
        public IEnumerator Weapon_SetAmmo_FiresEventClampsAndUpdatesHUD()
        {
            WeaponManager wm = Weapons();
            GameTestAPI.EquipWeapon(GameTestAPI.WeaponPistol);
            yield return null;

            Weapon pistol = wm.GetCurrentWeapon();
            PlayerHUD hud = Object.FindAnyObjectByType<PlayerHUD>();
            int lastCurrent = -1;
            int calls = 0;

            void OnAmmo(int current, bool infinite)
            {
                calls++;
                lastCurrent = current;
            }

            pistol.OnAmmoChanged += OnAmmo;

            pistol.SetAmmo(50);
            yield return null;

            Assert.AreEqual(50, pistol.GetAmmo(), "SetAmmo(50) must set ammo.");
            Assert.AreEqual(1, calls, "SetAmmo must fire OnAmmoChanged exactly once.");
            Assert.AreEqual(50, lastCurrent, "Event must carry the new ammo value.");
            Assert.AreEqual("Ammo: 50", hud.ammoText.text,
                "HUD must update from the OnAmmoChanged -> UpdateHUD wiring.");

            pistol.SetAmmo(5000);
            yield return null;
            Assert.AreEqual(pistol.maxAmmo, pistol.GetAmmo(),
                "SetAmmo must clamp to maxAmmo.");

            pistol.SetAmmo(-3);
            yield return null;
            Assert.AreEqual(0, pistol.GetAmmo(), "SetAmmo must clamp to 0.");
            Assert.IsFalse(pistol.CanFire(), "A weapon at 0 ammo must not be able to fire.");

            pistol.OnAmmoChanged -= OnAmmo;
        }

        [UnityTest]
        public IEnumerator Weapon_Fire_SingleShot_DecrementsAmmoAndFiresEvent()
        {
            WeaponManager wm = Weapons();

            int[] ammoSlots =
            {
                GameTestAPI.WeaponPistol,
                GameTestAPI.WeaponShotgun,
                GameTestAPI.WeaponAssaultRifle
            };

            foreach (int slot in ammoSlots)
            {
                wm.EquipWeapon(slot);
                Weapon w = wm.GetCurrentWeapon();
                w.SetAmmo(10);
                int before = w.GetAmmo();
                int fires = 0;

                void OnAmmo(int current, bool infinite) { fires++; }
                w.OnAmmoChanged += OnAmmo;
                w.Fire();
                w.OnAmmoChanged -= OnAmmo;

                Assert.AreEqual(before - 1, w.GetAmmo(),
                    "One production Fire() shot must consume exactly one round (" +
                    w.GetName() + ").");
                Assert.AreEqual(1, fires,
                    "Fire() must raise OnAmmoChanged for an ammo weapon (" +
                    w.GetName() + ").");
                yield return null;
            }

            wm.EquipWeapon(GameTestAPI.WeaponChainsaw);
            int sawBefore = wm.GetCurrentWeapon().GetAmmo();
            wm.GetCurrentWeapon().Fire();
            yield return null;
            Assert.AreEqual(sawBefore, wm.GetCurrentWeapon().GetAmmo(),
                "Chainsaw Fire() must never consume ammo.");
        }

        // ── Firing through real input at real geometry ─────────────────────

        [UnityTest]
        public IEnumerator Weapon_Fire_AtWall_AllWeaponsNoExceptionsAmmoConsumed()
        {
            WeaponManager wm = Weapons();
            PlayerController player = GameTestAPI.GetPlayer();
            AimPlayerAtWall(player);
            yield return null;

            int[] slots =
            {
                GameTestAPI.WeaponPistol,
                GameTestAPI.WeaponShotgun,
                GameTestAPI.WeaponAssaultRifle,
                GameTestAPI.WeaponChainsaw
            };

            for (int i = 0; i < slots.Length; i++)
            {
                wm.EquipWeapon(slots[i]);
                Weapon w = wm.GetCurrentWeapon();
                yield return null;

                int ammoBefore = w.GetAmmo();

                // Hold the fire button well past the slowest cadence (shotgun 0.8s).
                SetFireButton(true);
                const float holdSeconds = 1.0f;
                float startT = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - startT < holdSeconds)
                    yield return null;
                SetFireButton(false);
                yield return null;

                if (w.UsesAmmo())
                {
                    Assert.Less(w.GetAmmo(), ammoBefore,
                        w.GetName() + " must consume ammo while the fire button is held " +
                        "(before=" + ammoBefore + ", after=" + w.GetAmmo() + ").");
                }
                else
                {
                    Assert.AreEqual(0, w.GetAmmo(),
                        "Chainsaw must keep zero ammo while attacking.");
                }
            }

            PlayerHUD hud = Object.FindAnyObjectByType<PlayerHUD>();
            Assert.AreEqual("Weapon: Chainsaw", hud.weaponText.text,
                "HUD must reflect the last equipped weapon after firing.");
            Assert.AreEqual("Ammo: Infinite", hud.ammoText.text,
                "HUD must show infinite ammo for the Chainsaw.");
        }

        [UnityTest]
        public IEnumerator Weapon_Ammo_Zero_BlocksFiringAndNeverGoesNegative()
        {
            WeaponManager wm = Weapons();
            GameTestAPI.EquipWeapon(GameTestAPI.WeaponPistol);
            yield return null;

            Weapon pistol = wm.GetCurrentWeapon();
            pistol.SetAmmo(1);
            yield return null;

            AimPlayerAtWall(GameTestAPI.GetPlayer());

            SetFireButton(true);
            const float holdSeconds = 0.6f;
            float startT = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - startT < holdSeconds)
                yield return null;
            SetFireButton(false);
            yield return null;

            Assert.AreEqual(0, pistol.GetAmmo(),
                "Ammo must run out at exactly 0 after firing the last round.");

            SetFireButton(true);
            startT = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - startT < 0.4f)
                yield return null;
            SetFireButton(false);
            yield return null;

            Assert.AreEqual(0, pistol.GetAmmo(),
                "Holding fire at 0 ammo must not drive ammo below zero.");
            Assert.IsFalse(pistol.CanFire(), "Empty weapon must not fire.");
        }
    }
}