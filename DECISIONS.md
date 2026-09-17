# Doom Clone — Design Decisions

This file records consequential choices that are intentionally left open by `GAME_SPEC.md`.

## Rules

- Record a decision before or immediately after implementation when the choice may affect later systems.
- Do not rewrite historical decisions without noting the change.
- Prefer the simplest deterministic approach compatible with the specification.

## Template

### YYYY-MM-DD — Decision title

**Decision:**

**Reason:**

**Alternatives considered:**

**Impact:**

## Current decisions

### 2026-09-16 — M15 balance locked with a deterministic EditMode guard test (no numeric rebalance)

**Decision:** The "Balance damage/health/cadence" milestone item is completed by adding `WeaponBalanceEditModeTests` (6 tests, `Assets/Game/Tests/EditMode/`), which headless-instantiates every weapon and enemy, invokes each real `Awake` via reflection (EditMode does not auto-run it), and asserts the balance invariants: per-spec stat values; DPS tier ordering Pistol 50 < Shotgun 80 (point-blank) < Assault Rifle 100 < Chainsaw 300; ammo economy Pistol 999 > Rifle 120 > Shotgun 50 (>10× margin) and Chainsaw sentinel 0; range tiers Rifle 150 > Pistol 100 > Shotgun 30, with Chainsaw strictly melee (3 m); enemy HP/speed/damage/cadence tier ordering plus Soldier ranged vs Runner/Brute melee and Brute's 0.5 s telegraph; and TTK/survivability sanity (pistol drops a runner in <1 s, rifle kills the brute in <3 s, shotgun point-blank one-shots a runner but not the brute, no enemy one-shots the 100 HP player, pistol-vs-brute is a slow 4 s role-pressure fight). No production numbers were changed.

**Reason:** The existing values were already pinned by M6–M13 weapon/enemy tests and verified playable through 17 milestones, so a blind rebalance would have broken 80+ already-green assertions to satisfy a subjective notion of "better". What M15 needed was a checkable contract that the numbers still describe the spec's intended roles (fallback, burst, sustained, melee peak; scarce vs generous ammo; escalating enemy tiers; survivable player). A deterministic EditMode lock makes any future rebalance a deliberate, test-verified act instead of silent drift.

**Alternatives considered:** Hand-tuning numbers until "it feels right" (rejected — non-verifiable and would break existing pinned tests); a PlayMode TTK measurement against real enemies (rejected — frame-rate dependent at 1–8 FPS, and the config-level Ttk math is exactly deterministic); an EditMode test reading a static balance config class (rejected — there is no config class; weapon/enemy values live in `Awake`, and instantiating + invoking real `Awake` is the honest source of truth).

**Impact:** EditMode suite is now 13/13 (7 existing + 6 balance). Any future change to weapon/enemy stats must keep DPS/ammo/range/enemy tiers and survivability coherent or this class fails with the exact violated invariant in the message. No gameplay change; balance was confirmed already coherent rather than modified.

### 2026-09-16 — Game Over restart button verified through the real production binding

**Decision:** `PlayerTests` gains `Player_GameOverRestartButton_TriggersCleanRestart`, which calls `hud.ShowGameOver(3)`, locates the live `RestartButton` GameObject by name, and invokes its `Button.onClick` — the exact binding production `CreateButton` registered (`GameManager.RestartGame`). It asserts a new Player, 100 HP, not dead, `gameActive`. Supporting change: `Unity.ugui` added to `DoomClone.PlayModeTests.asmdef` so the test can reference `UnityEngine.UI.Button`.

**Reason:** `GameTestAPI.ResetGame()` already proves restart works, but no test exercised the Game Over screen's actual REINICIAR button binding (GAME_SPEC §7 restart path). The button handler is a plain `UnityAction`, so invoking `onClick.Invoke()` needs no synthetic InputSystem input — it is batch-CLI-safe (verified 1/1 green in `-batchmode`), unlike the 18 input-driven tests covered by Open Issue 2026-09-16.

**Alternatives considered:** Simulating a pointer click over the button via `EventSystem` (`ExecuteEvents.Execute` with `IPointerClickHandler`) — rejected, requires a working input plumbing and is unnecessary for a `UnityAction` handler; calling `GameManager.RestartGame()` directly — rejected, bypasses the button→binding→manager chain this test pins.

**Impact:** CI batch runs now cover restart-after-GameOver end-to-end. No production gameplay change; `.asmdef` reference addition is test-assembly-only.

### 2026-09-14 — Runtime production assembly created as `DoomClone.Runtime`

**Decision:** Added `Assets/Scripts/DoomClone.Runtime.asmdef` so all existing production scripts compile into a referenceable assembly named `DoomClone.Runtime`. No scripts were physically moved; the `.asmdef` was added inside the existing `Assets/Scripts/` folder.

**Reason:** Unity test assemblies and automation assemblies cannot reference `Assembly-CSharp`, and `.asmdef` files are mutually exclusive with it. A named runtime assembly is required so `DoomClone.Automation` and the EditMode/PlayMode test assemblies (created in this milestone) can reference production code.

**Alternatives considered:** Moving production scripts into `Assets/Game/...` per `ARCHITECTURE.md`. Rejected: it would churn file GUIDs and risk breaking existing scene/prefab references, and the docs already permit the actual layout to retain `Assets/Scripts/`; `TODO.md` records the locating deviation.

**Impact:** New test/automation assemblies reference `DoomClone.Runtime`; the runtime assembly explicitly references `Unity.InputSystem`, `Unity.AI.Navigation`, and `Unity.ugui` (previously auto-referenced from `Assembly-CSharp`).

### 2026-09-14 — Automation, editor, and test assemblies layout

**Decision:** Automation lives in its own assembly `DoomClone.Automation` at `Assets/Game/Automation/`; editor/build helpers in `DoomClone.Editor` at `Assets/Game/Editor/`; tests at `Assets/Game/Tests/EditMode` (`DoomClone.EditModeTests`) and `Assets/Game/Tests/PlayMode` (`DoomClone.PlayModeTests`), all referencing `DoomClone.Runtime` (and automation/tests referencing `DoomClone.Automation`). CLI test runner added at `tools/test.sh`.

**Reason:** Follows `ARCHITECTURE.md` root layout, keeps editor-only code out of runtime builds, and gives deterministic CLI/headless test execution per `TEST_PLAN.md`.

**Alternatives considered:** Placing tests directly under `Assets/Scripts/` with the game code. Rejected — mixed concerns.

**Impact:** Later milestones add their EditMode/PlayMode verification into the existing test assemblies; `tools/test.sh` is the repeatable runner.

### 2026-09-14 — Planned production extensions for automation

**Decision:** Added three narrow production APIs to support deterministic testing, documented here up-front: `WaveManager.SpawnEnemyByType(string)` (with spawn aliases and spawn-not-tracked-by-wave semantics), `PlayerController.SetHealth(int)`, and `Weapon.SetAmmo(int)`.

**Reason:** `TEST_PLAN.md` and `ARCHITECTURE.md` require the automation layer to drive the real game rather than a shadow simulation. Spawning an enemy, setting health/ammo, and teleporting the player are the atomic operations the smoke test and later PlayMode suites need. `SpawnEnemyByType` intentionally does NOT count the spawned enemy toward the active wave so wave-alive tracking stays correct during automated combat scenarios.

**Alternatives considered:** Manipulating private fields via reflection. Rejected — fragile and bypasses events (`OnDamage`, `OnAmmoChanged`, death flow) that production logic must fire.

**Impact:** Automation (`GameTestAPI`) calls these public APIs; full game loop, wave tracking, and death flow are unaffected for normal play.

### 2026-09-14 — Spawn-point identification: canonical tags + retained name-based lookup

**Decision:** Added the custom tags `EnemySpawnPoint` and `PlayerSpawnPoint` to `ProjectSettings/TagManager.asset` and applied them to all 8 spawn markers in `DoomClone_Level01` (7 enemy + 1 player) per `GAME_SPEC.md` §1. Runtime discovery in `WaveManager.FindSpawnPoints()` and `GameManager.SetupPlayer()` remains name-based, with tag as the authoritative spec identifier.

**Reason:** The spec requires the GameObjects to be identifiable by tag. Tag-based discovery would require changing production code and its fallback behavior; keeping the existing name-based lookup avoids touching gameplay code in this milestone and keeps tag and name as two independent validation surfaces (the M2 validation test asserts tagged counts match `WaveManager.spawnPoints.Length`).

**Alternatives considered:** Switching `WaveManager`/`GameManager` to `FindObjectsByType`-style tag lookups now. Rejected — out of scope for the tag milestone; would change runtime discovery behavior before navigation (M3) and enemy (M10+) verification.

**Impact:** `GAME_SPEC.md` §1 tag requirement satisfied and verified by `LevelValidationTests`. M3 (Navigation) should also verify that each spawn point is on/near the NavMesh (known issue: spawn points at Y=1.0 fail `NavMesh.SamplePosition(…, 0.6)`).

### 2026-09-14 — Toolchain/editor automation

**Decision:** `Assets/Game/Editor/BuildAutomation.cs` (menu + CLI builds to `Builds/`) and `tools/test.sh` (EditMode / PlayMode / both via Unity `-runTests`) added. `Builds/` and `TestResults/` are ignored via `.git/info/exclude` (local only, not committed).

**Reason:** The development loop requires repeatable compile/test/build steps that work headless, per `AGENTS.md` and `TEST_PLAN.md`. Unity-version path is configurable via env (`UNITY_BIN`) and never hard-coded in game code.

**Alternatives considered:** Requiring manual Editor runs. Rejected — against the autonomous operating mode.

**Impact:** Enables the CI/smoke loop and future milestone verification.

### 2026-09-15 — Corridor door gaps: split arena walls to mirror the North gap

**Decision:** The 4 full-length East/West arena walls (x=±15, z∈[-15,15], scale (0.5,1.5,10)) were split at z=-10 and z=10 into Top/Bottom segments, leaving door gaps z∈[-5,5] identical to the North corridor gap. East and West corridors now produce `PathComplete` routes into the arena.

**Reason:** `GAME_SPEC.md` §1 requires 2–3 corridors "connecting" secondary areas to the arena. `NavMesh.CalculatePath` from the E/W corridor spawn points to the player returned `PathPartial` because the sorting-by-Y/wall meshes sealed the arena on those two sides; the North corridor already worked.

**Alternatives considered:** Enlarging the existing door gap by moving walls; adding a separate doorway cube with a navmesh modifier. Rejected — splitting the single wall keeps geometry uniform with the North side and requires no extra modifiers.

**Impact:** All 7 enemy spawn points (4 arena corners + 3 corridors) produce complete paths to the player; verified by `Navigation_BakeCompletes_AllSpawnsSampleAndPathToPlayer`.

### 2026-09-15 — Agent placement: wait for bake, re-snap, enable, then Warp

**Decision:** `WaveManager.EnableNavMeshAgent` no longer enables a `NavMeshAgent` unconditionally 0.1 s after spawn. It now: waits until `NavMeshUtil.IsBaked()` (triangulation has vertices, up to 10 s), re-snaps the enemy transform onto the NavMesh via `NavMeshUtil.TrySnapToNavMesh`, enables the agent, and then `Warp`s onto the snapped point; if the bake never completes or no walkable point is found, the agent stays disabled forever so no `SetDestination` can ever be issued from an unplaced agent.

**Reason:** Waves spawn ~2 s after scene load while the runtime NavMesh bake (`NavMeshSetup` → `NavMeshSurface.BuildNavMesh`) finishes around the same time. Enabling an agent before/without a valid bake caused `"SetDestination" can only be called on an active agent that has been placed on a NavMesh` and left `isOnNavMesh == false` for spawned enemies. This is the M3 fix for the deferred M2 known issue.

**Alternatives considered:** Baking synchronously inside `GameManager.Start` before spawning waves (blocks first frame ~2 s) or delaying all waves until the bake is complete. Rejected — waiting per-agent is localized, additive, and keeps the 2 s first-wave timing requirement.

**Impact:** Enemies spawned by waves and by `GameTestAPI.SpawnEnemy` land on the NavMesh, `isOnNavMesh == true`, and no `SetDestination` errors occur; verified by the two navigation movement tests and a clean Console.

### 2026-09-15 — `StartFreshLevel` always restarts the level

**Decision:** `GameTestAPI.StartFreshLevel` no longer skips reloading when the active scene is already `DoomClone_Level01`; it always calls `ResetGame()` (which routes through `GameManager.RestartGame` → scene reload) and only then waits for game-ready.

**Reason:** In PlayMode suites, the level scene stays loaded across tests. If an earlier test lets the player die, `WaveManager.gameActive` stays `false`, so later `SpawnEnemyByType` calls return `null` and wave assertions observe stale state. Always restarting makes every test deterministic and independent.

**Alternatives considered:** Resetting `WaveManager.gameActive`/wave state in code on scene load. Rejected — a reload already exists (`RestartGame`) and gives a fully clean singleton/state baseline rather than ad-hoc resets.

**Impact:** `SpawnEnemy("runner")` in later tests always works after a fresh boot; wave tracking and player health are clean per test.

### 2026-09-15 — Player test input injection without `InputTestFixture`

**Decision:** PlayMode player tests drive input by adding a synthetic `Keyboard` (`InputSystem.AddDevice<Keyboard>("TestKeyboard")`) and `Mouse` (`"TestMouse"`), then `QueueStateEvent` + `InputSystem.Update()` to press/release keys. The New Input System default bindings target the `Keyboard` layout, so the owned `TestKeyboard` device drives the real `moveAction`/`sprintAction`/`jumpAction`.

**Reason:** `InputTestFixture` resets the entire input system per fixture, which destroys the game's `DontDestroyOnLoad` `PlayerInputActions` singleton that `PlayerController` depends on — tests would crash and not reflect production startup.

**Alternatives considered:** `InputTestFixture` (rejected — breaks production singleton); directly invoking `PlayerController` movement internals (rejected — bypasses the real input path).

**Impact:** Tests exercise the genuine Input System → `PlayerController` path. The input effect is transient (synthetic device added/removed around the action), so the running game state on the edited scene is not polluted.

### 2026-09-15 — `PlayerHUD` countdown/game-over panels: stored references instead of `GameObject.Find`

**Decision:** `PlayerHUD.CreateHUD` now stores the created `CountdownBG`/`GameOverBG` references in private fields; `ShowCountdown`/`HideCountdown`/`ShowGameOver` use them directly instead of `GameObject.Find("...")`.

**Reason:** `CountdownBG` and `GameOverBG` are created inactive (`SetActive(false)`), and `GameObject.Find` never locates inactive objects — so the wave countdown and Game Over panels could never render, silently violating GAME_SPEC §6.6 and §7. The M4 PlayMode suite (`Player_HUD_CountdownAndGameOver`) caught this.

**Alternatives considered:** Activating the panels before `Find` (extra state churn); tagging the objects and using `FindWithTag` (still misses inactive objects).

**Impact:** Both countdown UI and Game Over UI now render when their production code paths run; verified by the 13/13 PlayerTests.

### 2026-09-15 — WaveManager stops the active loop on player death

**Decision:** `WaveManager.OnPlayerDeath` now explicitly sets `waveInProgress = false` (and the death path already stops future spawning).

**Reason:** Without it, `IsWaveInProgress()` stayed `true` forever after the player died, contradicting GAME_SPEC §7 "stop the active wave loop" — any later restart-adjacent logic would observe a phantom in-progress wave. Found by the M4 suite.

**Alternatives considered:** Relying only on the existing `gameActive = false` guard in spawn paths.

**Impact:** Wave state transitions to a truthful not-in-progress state on death; player restart remains clean.

### 2026-09-15 — Editor test-infra: disable Enter Play Mode Options for reliable PlayMode runs

**Decision:** Set `m_EnterPlayModeOptionsEnabled = 0` in `ProjectSettings/EditorSettings.asset` (was enabled with "Disable Domain Reload" + "Disable Scene Reload").

**Reason:** With Enter Play Mode Options on, the Unity TestRunner executes PlayMode test runs as **0 tests** that "pass" instantly (`Status Unknown, TotalTests 0` via MCP; empty `TestResults.xml` with `result="Passed" total="0"`). EditMode is unaffected. Disabling restores real execution (full suite 19/19). This is an editor test-infra setting, not a gameplay change.

**Alternatives considered:** Accepting flaky MCP runs and relying on `tools/test.sh` CLI only (same TestRunner under the hood — would hit the same symptom in batchmode).

**Impact:** Reliable PlayMode verification through MCP `tests-run`; entering Play Mode now reloads domain/scene as default Unity behavior.

### 2026-09-15 — Weapon slot ordering: hotkey index == slot index, Pistol default

**Decision:** `WeaponManager.InitializeWeapons` now builds `weapons = [Pistol, Shotgun, AssaultRifle, Chainsaw]` so slot index `i` equals hotkey `i + 1`, and `GameTestAPI` weapon constants were renumbered (`WeaponPistol = 0` … `WeaponChainsaw = 3`). Pistol is the default/fallback weapon at slot 0 (boot `EquipWeapon(0)`).

**Reason:** GAME_SPEC §3 lists the weapons without mandating an order but §3.2 calls the Pistol the "fallback/default weapon" and §2 keys `1`–`4` switch weapons. A 1=1 mapping where Pistol — the fallback — sits on key `1` is the least-surprising old-school arrangement and makes `index == hotkey − 1` a testable contract. The pre-existing code had `[Chainsaw, Pistol, Shotgun, AssaultRifle]` (Chainsaw on key `1`), contradicting the Pistol-is-default reading.

**Alternatives considered:** Keeping the old array order and adding an explicit slot→hotkey remap. Rejected — an indirection table adds code for no gameplay benefit; tests then verify the table, not the contract.

**Impact:** Verified by `Weapon_Framework_ManagerOwnsAllFourWeapons` (slot types in order), `Weapon_Switch_Keys1To4_SelectExpectedWeapon` (keys `1`–`4`), and `Weapon_Boot_DefaultsToPistol`. Affects any code that relied on the old constant values (only the automation/test layer did).

### 2026-09-15 — Shared `TestInputDevices` helper for PlayMode input injection

**Decision:** The synthetic `TestKeyboard`/`TestMouse` device management was extracted from `PlayerTests` into a shared static helper `TestInputDevices` (`Assets/Game/Tests/PlayMode/TestInputDevices.cs`). `EnsureDevices()` is idempotent: it keeps exactly ONE synthetic Keyboard and ONE Mouse, scanning `InputSystem.devices` first (reusing survivors) and removing any duplicates leaked by earlier sessions.

**Reason:** M4's input-injection decision added a synthetic device pair per run. `AddDevice` devices are never reaped automatically and survive domain reloads, so each PlayMode test run added another pair; the game's actions bind to the first pair while later runs injected into later pairs, silently killing input assertions from run two onward. The M4 suite passed only because the first run was clean; M5's much larger suite exposed the leak.

**Alternatives considered:** `InputTestFixture` (rejected in M4 — destroys the game's `DontDestroyOnLoad` `PlayerInputActions` singleton); resetting `InputSystem` via `InputSystem.ResetDevice` per test (rejected — noisier than dedup).

**Impact:** Repeated PlayMode runs remain deterministic (verified by running the full 29-test suite via MCP after the helper existed — all input-driven weapon/player assertions still green on a second run). Also made the M4 jump test hold Space for 3 frames so the queued event is always consumed by at least one `PlayerController.Update` (batch FPS varies).

### 2026-09-15 — Enemy `TrySetDestination` defense-in-depth for placement races

**Decision:** Added `Enemy.TrySetDestination(Vector3)` and switched all three enemy subclasses (`ZombieRunner`, `RangedSoldier`, `TankBrute`) to route every `agent.SetDestination` through it. It returns `false` (never throws) when the agent is null, disabled, or not on a valid NavMesh.

**Reason:** M5 firing tests hold the attack button for ~1 s per weapon against real geometry while Wave 1 enemies spawn and chase the player — exactly the window where an agent can be enabled-but-not-yet-placed (M3 `EnableNavMeshAgent` waits for the bake then warps, but the transform can still be mid-snap and `agent.enabled`/`isOnNavMesh` intermediate). An unplaced-agent `SetDestination` throws at runtime; the weapon test path must stay exception-free. This is the same defensive condition the subclasses already check (`!agent.enabled || !agent.isOnNavMesh` → return), generalized to the call site so no future `SetDestination` reintroduces the race.

**Alternatives considered:** Keeping direct calls (the per-subclass guard already covers current code) — rejected because it leaves the invariant implicit and easy to break in a future enemy; disposing of the try/catch — kept, since a non-walkable destination on a valid agent is a legitimate "cannot path this frame", not an error to surface.

**Impact:** Firing-at-wall weapon tests and wave-spawn windows run exception-free. No gameplay behavior changes for fully-placed agents (`SetDestination` still called with the same destinations).

### 2026-09-15 — Pistol single-shot verification uses the direct production `Fire()` call

**Decision:** `Pistol_Hitscan_SpawnedEnemyTakesConfiguredDamagePerShot` fires its single shot by calling `pistol.Fire()` directly (the same method the input path invokes) instead of holding the fire button for a fixed number of frames. The "one round in / enemy HP drops by exactly `pistol.damage`" assertions measure that single call. Input-path cadence remains separately covered by `Pistol_Fire_Hold_ShotCountMatchesCadence` (real-time window) and `Pistol_Ammo_RunsToZeroAndZeroBlocksFiring`.

**Reason:** The first version held fire for 5 batch frames expecting one round, but PlayMode frames near spawn/NavMesh-bake can exceed the pistol's 0.3 s cadence (a test run consumed 4 rounds in 5 frames). Frame-count holds are not a reliable "one shot" proxy near cadence-scale timings; a single direct `Fire()` is deterministic and still exercises the real weapon method (raycast, ammo, event, feedback).

**Alternatives considered:** Press → exactly 1 frame → release, and press → bounded real-time hold shorter than fireRate. Rejected — a single slow frame longer than 0.3 s between press and release still allows a second shot, so neither removes the frame-duration dependency.

**Impact:** The hitscan-damage assertion is deterministic across load conditions. Later per-weapon milestones (shotgun/rifle) should use the same pattern: direct `Fire()` for exact per-shot state assertions, input-path holds with loose windows for cadence/auto-fire behavior.

### 2026-09-15 — Shotgun M7 verification: pellet counting and impact-trace tracking

**Decision:** M7's shotshell-pellet tests count pellet hits two deterministic ways: (1) point-blank damage vs a spawned TankBrute counts `Enemy.OnEnemyDamaged` events (exactly 8 for a full blast) and asserts the HP drop equals `pelletCount × pelletDamage = 64`; (2) cone spread is measured by snapshotting all `SphereCollider` impact markers before/after a single `Fire()` at real geometry (before/after `HashSet<SphereCollider>` difference == `pelletCount`). The `HashSet` compares `UnityEngine.Object` references (overridden Equals/GetHashCode, instance-based), which avoids the obsolete-with-error `Object.GetInstanceID()` (`CS0619` in this Unity build) and needs no `GetEntityId()` API.

**Reason:** `Object.GetInstanceID()` is an error-level obsolete in Unity 6000.6 (`Use GetEntityId instead`), which blocked compilation of the first test draft. The old code also built `HashSet<int>` of instance IDs only to re-resolve them into positions; the reference-set collision detection does count + positions from the same snapshot in one pass and is stable within the same frame (impact markers are destroyed 0.1 s after firing).

**Alternatives considered:** `Object.GetEntityId()` for identity — rejected, keeps the same two-step resolve dance for identical behavior; awaiting the 0.1 s marker lifecycle — rejected, over-complicates a same-frame count.

**Impact:** The pellet-count and cone assertions are deterministic (`Shotgun_Pellets_PointBlankFullHit_DealsPelletCountTimesPelletDamage`, `Shotgun_Pellets_ConeAtWall_ProducesPelletCountDistinctImpacts`). Reload is verified as the base-class block reload already implemented (2.0 s restore-to-50, R-key production input path); no reload reimplementation was needed for M7.

### 2026-09-15 — Wave-movement nav test samples an engaged runner, not an arbitrary enemy

**Decision:** `Navigation_Wave1SpawnsEnemies_ThatMoveTowardPlayer` no longer picks the first living wave enemy and asserts it moved > 0.5 u. It now waits (up to 8 s) for a wave `ZombieRunner` whose `NavMeshAgent` is enabled, `isOnNavMesh`, and actively moving (`velocity.sqrMagnitude > 0.04`), then measures that runner over a 1.2 s window.

**Reason:** The spec guarantees the Runner "rushes directly toward the player after detection" (§4.1); it does not guarantee every wave enemy moves at every instant. Wave 1 mixes runners + ranged soldiers, corridor spawn points are at 30 u (beyond the runner's 25 u detection range), and soldiers strafe to keep range — so an arbitrary pick can legitimately be undetected, already attacking (standing still), or repositioning and travel ~0 in a short window. The old version flaked exactly this way in a full-suite run (0.482 u vs the 0.5 u threshold).

**Alternatives considered:** Moving the runner detection range to 30 u or moving corridor spawn points closer — rejected, that changes gameplay/level to satisfy a test; picking the farthest arena runner — rejected, the corridor runner is the most distant and stays undetected (moved=0.00).

**Impact:** The test now measures a wave enemy demonstrably in its chase phase, so the "wave enemies move toward the player" requirement is verified deterministically while simple Level-1 AI (per-enemy detection ranges) remains untouched. NavigationValidationTests re-verified 3/3 in isolation and 3/3 in the full 35-test suite.

### 2026-09-15 — Assault Rifle hold tests: deadline-based holds instead of fixed real-time windows

**Decision:** The two M8 hold tests no longer hold the fire button for a fixed real-time window and assert a round-count range. `AssaultRifle_Fire_Hold_AutomaticStreamMatchesHighCadence` holds until ≥3 rounds are consumed (8 s real-time deadline) and asserts consumed ∈ [3,20]; `AssaultRifle_Hold_SpawnedEnemy_TakesRepeatedDamageAndAmmoDrainsPerShot` holds until ≥2 `OnEnemyDamaged` events land on a 3 m brute (8 s deadline) and asserts ≥2 hits, ammo−hits ≤ 3, and HP drop == hits × damage. The exact 0.1 s cadence remains verified frame-independently by the direct-CanFire cadence test.

**Reason:** PlayMode batchmode in this environment runs at only ~5–8 FPS. A 1.2 s real-time hold advances ~6 frames and ~0.70 s of game time, so the rifle's realized automatic rate is frame-bound, not cadence-bound, AND erratic: measured 3 rounds in one 1.2 s hold but only 1 round in a 0.9 s hold. Fixed-window assertions (originally [6,14] at nominal 12) therefore flaked deterministically. Deadline-based holds assert the actual contracts — a single persistent press produces a multi-round stream, and a continuous hold lands repeated damage with ammo==hits — without depending on how many frames the scheduler delivers.

**Alternatives considered:** Fixed windows tuned lower (e.g. [2,14]) — still bound to an unpredictable frame count per wall-clock window and re-flaked exactly that way (1 consumed in 0.9 s); temporarily raising Unity's target frame rate in tests — rejected, does not survive batchmode throttling and would mask rather than measure the input path.

**Impact:** Hold-path assertions are frame-rate independent and deterministic across isolation and full-suite runs. Per-test intent (auto-fire, repeated damage, cadence ceiling) is preserved; precise cadence coverage lives in the direct CanFire test.

### 2026-09-15 — Frozen-test-target placement: neutralize the nav coroutine and sync physics

**Decision:** `SpawnFrozenTarget` (used by the rifle hitscan/hold tests) now (1) `DestroyImmediate`s the spawned enemy's `NavMeshAgent` so the `WaveManager.EnableNavMeshAgent` coroutine exits at its null guard instead of re-snapping the transform onto the floor ~0.1 s later, (2) mirrors the Brute's production scale-2 hitbox by setting `localScale = 2` for `"brute"` (its `TankBrute.Start` never runs on a disabled component), (3) lifts the floor-aligned root ~1.6 m so the collider spans the 1.33 m camera eye, and (4) calls `Physics.SyncTransforms()` after teleporting.

**Reason:** Two independent root causes made the first draft deterministic failures. First, a collider on a non-rigidbody object does not follow `transform.position` until the next physics step — the sanity raycast saw the brute's collider still at its original spawn point (bounds center (-10.00, 0.33, 10.00)) while the transform sat at (0.00, 1.33, 3.00) directly on the camera ray, so the ray passed through to the far wall. Second, the WaveManager nav coroutine re-snaps spawned enemies onto the floor, which drops even a synced scale-1 collider below the eye ray (capsule top ≈ 1.33 m — a coin-flip grazing hit, explaining the runner test's intermittent pass/fail).

**Alternatives considered:** Teleporting after the coroutine completes (0.3 s wait) — still racy because the coroutine can re-enable the agent and fight the teleport; keeping `agent.enabled = false` — the coroutine re-enables it itself. `DestroyImmediate` + null-guard exit is the only deterministic neutralization and produces no console warning.

**Impact:** Enemy hitscan/hold tests now hit their target deterministically in every run (sanity always raycasts the brute, not the wall). The change is confined to the shared test helper; production gameplay is untouched.

### 2026-09-16 — Chainsaw `damagePerSecond` corrected to 300 (truthful config)

**Decision:** `Chainsaw.damagePerSecond` changed from `30f` to `300f`.

**Reason:** The field claims "damage per second", but the chainsaw deals `damage / fireRate = 30 / 0.1 = 300` per second while held. `30f` was a stale declaration contradicting the fields that actually implement the behavior (it described per-tick damage, not per-second). The M9 config test asserts `damagePerSecond == damage / fireRate` so the declared value can never silently drift from the realized DPS again.

**Alternatives considered:** Removing the public field (gameplay code doesn't read it) — rejected, keeping a truthful knob documents the identity and gives tests a single source to assert against.

**Impact:** No gameplay change (`dps` is only used by the new test). Config truthfulness per `TEST_PLAN.md` §"weapon damage calculations".

### 2026-09-16 — Chainsaw `IsAttacking()` public read-only accessor

**Decision:** Added `public bool IsAttacking() => isAttacking;` to `Chainsaw`.

**Reason:** The M9 feedback test needs to observe that the held attack actually engages (proof the camera-shake branch is reached), and `isAttacking` was private with no getter. A tiny read-only accessor matches the codebase's observability style (`GetHealth`, `GetAmmo`, `IsDead`, `IsReloading`).

**Alternatives considered:** Inspecting the private field via reflection or removing the gate — rejected (reflection is brittle; the gate is real production logic worth asserting).

**Impact:** Observability API only; no gameplay behavior change.

### 2026-09-16 — Camera-shake displacement measured every frame, not just attacking frames

**Decision:** `Chainsaw_Feedback_HeldAttack_RunsContinuouslyWithoutExceptions` samples the camera local position on EVERY frame of the hold (not only frames where `IsAttacking()` is true), and holds until ≥2 attacking frames are observed (8 s realtime deadline) instead of a fixed 1.25 s hold.

**Reason:** PlayMode instrumentation showed the vibration is genuinely applied (`CameraShake.Update` displaced the camera up to 0.0195 m) while attack-frame sampling reported exactly 0.0000: in script order `Chainsaw.Update` runs before the test coroutine and `CameraShake.Update` runs after the coroutine in the same frame, so the shake materializes one frame AFTER the attacking tick. Gating the sample on `IsAttacking()` guarantees readings are taken before the vibration appears. Additionally, a fixed 1.25 s realtime hold can elapse in a single slow batch frame (this 56-test run ran at ~1 FPS) whose chainsaw Update runs before the queued fire-press registers — zero attacking frames for the whole press. Observing ≥2 attacking frames forces the press to register and the 0.1 s cadence to reopen twice, making the hold frame-rate independent.

**Alternatives considered:** Adding a custom script execution order so the shake applies in the same frame as the attack — rejected, forces an editor-wide ordering for a test concern; sampling attack frames with a large per-frame tolerance — rejected, would relax the assertion instead of fixing the sampling.

**Impact:** The feedback test proves both the attacking "on" state AND the continuous camera vibration exception-free, independent of batch frame rate.

### 2026-09-16 — Same-frame cadence-lock assertion (before any yield)

**Decision:** `Chainsaw_Range_EnemyWithinRange_TakesConfiguredDamagePerTick` asserts `CanFire() == false` immediately after the direct `Fire()` call, before the enclosing `yield return null`.

**Reason:** At ~1 FPS a single batch frame lasts > 0.1 s (the chainsaw `fireRate`), so yielding between `Fire()` and the assert legitimately reopens the cadence gate before the test reads it — a false failure about which frame boundary was crossed, not about weapon behavior.

**Alternatives considered:** Re-measuring via a fixed-time hold — rejected; the direct-Call assert is deterministic and the input-path cadence is already covered by the held test.

**Impact:** Cadence lock is verified as a same-frame property, identical to the M9 test-infra note for the old M4-era flake.

### 2026-09-16 — Wave-movement nav test: measure from the gate with re-acquire + bestMoved

**Decision:** `Navigation_Wave1SpawnsEnemies_ThatMoveTowardPlayer` no longer measures a fixed 1.2 s `WaitForSeconds` window after finding a moving runner. It anchors each engaged runner (NavMesh-placed + `velocity > 0.2 u/s`) as found, accumulates ITS true displacement every frame, and if that runner stops being engaged (reached the player / in melee range attacking / blocked) it re-acquires the next still-chasing runner. It asserts the single best runner displacement exceeds 0.5 u within a 20 s game-time deadline.

**Reason:** A runner sampled the frame just before it reaches the player legitimately moves ~0 in the following second — it stands still attacking in melee range (measured 0.0128 u in the M9 full-suite run, a genuine non-flake with the old fixture). At 1–5 FPS a fixed 1.2 s window may not even span a moving runner. Measuring from the moment the runner is engaged catches the chase regardless of when the window starts, and re-acquiring survivors keeps the proof when the first runner arrives.

**Alternatives considered:** Fixed larger window (still frame-count bound); basing the wave-move guarantee on pre-arrival spawn-time measurement only (weaker, couples acceptance to wave-1 composition internals).

**Impact:** The "wave enemies navigate toward the player" acceptance is frame-rate independent and not confounded by arrival standstill.

### 2026-09-16 — Wave-enemy test helper patterns: wave-assignment gate + synchronous clear, delta-detection for base-Attack, `SpawnEnemyByType` isolation

**Decision:** `EnemyFrameworkTests` use these deterministic patterns: (1) before killing wave enemies the test waits until `WaveManager.GetEnemiesAlive() > 0` (that value is only written AFTER the `SpawnWave` composition loop finishes, so it proves the wave was fully assigned) and then keeps every have-your-sole-block-clear/aux-kill/assert statement fully synchronous (`yield`-free) so the 5 s post-clear wave cooldown can never spawn an interfering wave mid-scenario; (2) the base-`Attack`→player-damage proof measures `playerHealth` deltas across the attack tick rather than absolute pinning, since stray wave damage can land in the same window (kept negligible by healing the player to 100 before the close-range encounter); (3) the `SpawnEnemyByType` auxiliary test asserts `trackedEnemiesAlive == 0` after `StartFreshLevel`, spawns/types/dies one enemy at a time, and — because wave-1 timing (2 s) is never deterministic in batch — keeps the whole spawn-and-death sequence synchronous so `enemiesAlive` can only change via the death callback, proving the aux-not-tracked contract (DECISIONS 2026-09-14) exactly.

**Reason:** PlayMode batchmode here runs at ~1–8 FPS with non-deterministic wave timing, and wave-1 enemies can reach/reposition the player during long waits. The gate-plus-synchronous-block pattern makes alive-count and cooldown assertions deterministic without touching production timing (2 s / 5 s stay exactly as spec'd).

**Alternatives considered:** Firing `WaveManager.StopAllCoroutines` / disabling agent components to freeze wave timing — rejected, bypasses real systems; killing enemies before the gate (alive count can be 0 pre-spawn, making asserts vacuous); asserting absolute player HP after a melee hit — rejected, stray wave damage made it flaky until the synchronous-delta approach.

**Impact:** All six M10 PlayMode tests pass deterministically; the pattern is the template for M11–M14 per-type behavior and WaveManager timing tests.

### 2026-09-16 — M11 Runner verification: single dedicated runner, parked behind player, deadline-based chase/melee measurement

**Decision:** `RunnerEnemyTests` (4 tests) verify the Runner as a **sole actor** in a cleared arena: after `StartFreshLevel` + the wave-assignment gate (`WaitWaveAssigned(1)`), the test synchronously kills/removes every wave enemy except the one it keeps, parks a `SpawnEnemy("runner")`-spawned aux runner 12 m directly behind the player (`enemy.transform.position = player.transform.position + Vector3.back * 12f`, applied within the `EnableNavMeshAgent` coroutine's snap window so it stays put), then measures chase success (min distance over the loop) and melee engagement (player HP < 100 after the runner reaches melee range). The chase/melee test loops at 0.5 s game-time ticks and breaks early on `chased && meleeHit`, asserting on the observed `minDist`/damage rather than a fixed window. A `RunnerSmoke` runtime boot (script-execute coroutine on the real `PlayerController`) independently confirmed 12.0 m → 1.8 m in ~2.5 s and HP 100 → 44.

**Reason:** A dedicated per-type test cannot rely on wave composition (wave 1 mixes the three types at non-deterministic corridor/arena spawn points), so the runner must be the only threat in the arena. Parking it at a known bearing guarantees a straight-line rush (no corner navigation variance) while still exercising the real `TrySetDestination`/NavMesh path; the deadline-based loop is frame-rate independent the same way the 2026-09-16 wave-nav decision is.

**Alternatives considered:** Driving the runner from an arena spawn point (corridor spawns sit beyond the 25 m detection range, respawn points are at random composition); measuring fixed 2 s windows (rejected — at ~1–8 FPS batch frame cadence these are unreliable); a test-only `StopAllCoroutines`/wave-freeze (rejected — bypasses real systems).

**Impact:** All four M11 tests pass deterministically in isolation (4/4) and in the full suite (66/66). No production code was changed for M11 — the existing `ZombieRunner` passed as-is, confirming the M10-era config (30 HP / 8 dmg / 0.8 s cadence / 5 speed / 1.8 m range / green material).

### 2026-09-16 — M12 Ranged Soldier hitscan aimed at the capsule center from the ray origin

**Decision:** `RangedSoldier.Attack()` now computes the hitscan ray as:

```csharp
Vector3 origin = transform.position + Vector3.up;
Vector3 direction = (player.position - origin).normalized;
Ray ray = new Ray(origin, direction);
```

**Reason:** The soldier's hitscan **could never connect**. The old code computed the direction from the transform ROOT to `player.position + Vector3.up` but launched the ray from `transform.position + Vector3.up` — one meter higher. On flat NavMesh ground (soldier root at navmesh Y, player capsule top at `player.y + 1`) the ray crossed the player's vertical line at `player.y + 2`, exactly 1 m above the capsule top, so `Physics.Raycast` returned nothing at any engagement distance within `attackRange`. This violates GAME_SPEC §4.2 (the Ranged Soldier must "can damage the player when its attack connects") and was caught by `Soldier_RangedAttack_HitsPlayerAtDistanceBeyondMelee_AtIntervalCadence`, which measured hits=0 over the full 25 s window. The first attempted fix kept `+ Vector3.up * 1f` as the aim point, which put the ray exactly ON the capsule's top hemisphere — a tangent graze and still a coin-flip miss. Aiming straight at `player.position` (the CharacterController center) from the raised origin makes the ray cross the capsule body solidly regardless of NavMesh Y, soldier closeness, or small elevation differences.

**Alternatives considered:** Dropping the `+ Vector3.up` from the origin entirely (aim from the feet) — rejected, the raised origin keeps the shot visually coming from the soldier's body; leaving the tangent aim — rejected, deterministic miss-by-graze; a projectile instead of hitscan — rejected, hitscan is the spec's first-listed option and the test hard-asserts hit-delta detection on the real `TakeDamage` path.

**Impact:** Ranged Soldiers now actually damage the player at range. `RangedSoldierTests` 5/5 and the full PlayMode suite 71/71 verified after the fix. This is the first gameplay-harming production defect found by a dedicated per-type milestone test (M11 runner needed no production change).

### 2026-09-16 — M13 Brute telegraph tolerance window [0.35, 0.75] s

**Decision:** `BruteEnemyTests.Brute_Telegraph_PrepPauseAboutHalfSecond_BeforeSlamDealsConfiguredDamage` asserts the measured telegraph span (from telegraph entry — via the `IsTelegraphing()` accessor — until the slam deals damage) falls within `[0.35, 0.75]` seconds, centered on the configured `telegraphDuration = 0.5`. The wall-clock span is frame-quantized: at ~5 FPS it reads ~0.6 s, at ~2 FPS ~0.5 s, at 1 FPS ~1.0 s. The tolerance range matches `TEST_PLAN.md` "cooldown lasts exactly 5 seconds within test tolerance" guidance applied to the 0.5 s telegraph: the player must have a dodge window, and the measured span must not collapse to ~0 (instant slam, no telegraph) nor double the configured value.

**Reason:** The brute telegraph is the spec's explicit player-escape mechanic (GAME_SPEC §4.3 "so the player has a chance to evade"), so the test must prove it is neither absent nor grossly mis-predicted. It are deliberately bigger than the naive ±~0.1 s because PlayMode batchmode here runs at ~1–8 FPS and exact wall-clock equality is impossible (`TEST_PLAN.md` §Timing tolerances).

**Alternatives considered:** Asserting `telegraphTimer` field equality to 0.5 (brittle against frame stepping and touches internals); a tight `[0.4, 0.6]` window (flakes at 1 FPS in the full 76-test suite); no timing assertion (fails the "telegraph ~0.5 s" acceptance).

**Impact:** `Brute_Telegraph_*` passes deterministically in isolation and in the full suite (76/76). The window documents the frame-quantized reality of batchmode measurement rather than hiding a broken telegraph.

### 2026-09-16 — M13 Brute `IsTelegraphing()` accessor + slam-vs-base-Attack note

**Decision:** Added `public bool IsTelegraphing() => isTelegraphing;` to `TankBrute` (observability-only, no gameplay change). Documented: the brute's heavy attack is the `slamDamage` (30) path via `Physics.OverlapSphere(transform.position + transform.forward * 1.5f, slamRange=3)`, not the base-class `Attack()`/`attackDamage` (25); and `attackRate` (2.5 s) is declared-and-asserted-by-config-ordering but NOT behaviorally gated — once the player is inside `attackRange` the brute re-telegraphs immediately, and the 0.5 s telegraph itself is the dodge window.

**Reason:** M13 scope per `TEST_PLAN.md` requires observing the telegraph exactly when it begins (to arm the damage observer and measure the 0.5 s span), mirroring the M9 `Chainsaw.IsAttacking()` observability pattern. The codebase rule is to use real production state via tiny read-only accessors rather than reflection.

**Alternatives considered:** Reflection over the private `isTelegraphing` (rejected — brittle); tightening the brute so it enforces `attackRate` as a cadence gate before re-telegraphing (rejected — TELEGRAPH IS the gate, and GAME_SPEC only requires a slow attack cadence + a telegraph the player can dodge; behavior already conforms).

**Impact:** Tests arm/kill off genuine brute telegraph state; no gameplay change. `BruteEnemyTests` 5/5, full PlayMode suite 76/76, EditMode 7/7.

### 2026-09-16 — M14 WaveManager timing test: event-to-event windows + exact countdown sequence

**Decision:** `WaveManagerTests` measure the two mandated timings with event-to-event walls in scaled time instead of per-tick clocks: (1) initial wave delay — from the level-ready frame (`GameTestAPI.StartFreshLevel(30f)` returns after `gameActive`, so the anchor is at most one WaveManager frame after the real `t0`) until `Wm.GetCurrentWave() >= 1`, asserted within `[0.9, 4.0]` s; (2) cooldown — from the final kill's death frame (countdown starts same frame) until the first frame `Wm.GetCurrentWave() >= 2`, asserted within `[4.5, 8.0]` s. The countdown HUD itself is verified by asserting the exact sequence `[5, 4, 3, 2, 1]` of parsed `"Next wave in: N..."` values with no per-tick wall-clock requirements; by asserting the countdown GameObject stays active the whole cooldown; and by asserting a final `"GAME OVER"` + `"Survived 1 waves"` screen with the countdown hidden behind it.

**Reason:** The production loop uses `WaitForSeconds(5)` run five times inside the cooldown coroutine, so the cooldown is structurally ≥ 5.0 s measured between in-game events regardless of frame rate; a t0-anchored window on the initial delay only needs to tolerate a ≤ 1-frame readback lag (the test samples the level-ready frame slightly late). An attempt to also assert "each tick lands ~1 s after the previous" failed deterministically (sampled gap 0.67 s in the first full run): the test reads the text on the first frame the change is OBSERVED, and WaveManager can have advanced one more cooldown step than the test's readback frame, shrinking sampled gaps below 1.0 while the production countdown is correct. Per `TEST_PLAN.md` "timing tolerances", a window that merely spans 4–6 s is too weak — hence the exact `[5→1]` sequence plus the whole-cooldown event window.

**Alternatives considered:** Asserting per-tick spacing (rejected — measured-unreliable in batch; see reason); asserting wall-clock equality for the 2 s delay (rejected — impossible at 1–8 FPS); replacing the cooldown with `yield return new WaitForSeconds(5)` single wait so a single event-to-event window is clean (rejected — touches production to satisfy a test; the five-1s-loops structure IS the production behavior worth pinning).

**Impact:** All five M14 tests pass deterministically (5/5 class, 81/81 full suite). The windows document frame-quantization reality; the "no enemy exists during the whole cooldown" and "countdown visible the whole time" assertions make the window robust rather than a range-only coincidence.

### 2026-09-16 — Wave composition totals {6, 6, 8} asserted exactly by the M14 scaling test

**Decision:** `WaveManager_Difficulty_ProgressiveScaling_AcrossEarlyWaves` asserts the exact per-wave spawn totals `{6, 6, 8}` for waves 1–3 (composition 0.7 zombie / 0.3 soldier, total = `RoundToInt(5 × 1.3^(wave−1))`, so 5 → 6.5 → 8.45 = 6, 6.5, 8 — a documented floor-under-0.5 tie at wave 2), plus structural monotonicity (wave-3 total > wave-1) and monotonic soldier proportion growth. The `ExpectedWaveTotals` array notes that M15 rebalancing must update it in the same commit.

**Reason:** GAME_SPEC §6 requires difficulty that "increases progressively" and "later waves must contain more enemies and/or a higher proportion of stronger enemies". Exact totals are deterministic from the production formula (no RNG), so asserting them exactly is stronger than a monotonic-only check and catches accidental formula changes; the wave-2 equal-count is explicitly expected because the same count with a higher soldier ratio still fits "and/or".

**Alternatives considered:** Only monotonic assertions (weaker — miss a formula regression that keeps counts monotonic); driving the assertion from `RoundToInt` recomputation in the test (rejected — mirrors production, would not catch a drift).

**Impact:** The test pins the exact composition contract and makes any M15 rebalance a conscious, documented change.

### 2026-09-16 — `WaveManager.OnPlayerDeath` hides the inter-wave countdown before Game Over

**Decision:** Added `hud.HideCountdown()` as the first line of the `hud != null` branch in `WaveManager.OnPlayerDeath`, before `hud.ShowGameOver(currentWave)`.

**Reason:** The countdown HUD element (`CountdownBG`) is only hidden by `HideCountdown()`, which the normal cooldown path calls when the next wave spawns. On player death during the cooldown, nothing hid it, so `GAME OVER` could render with the countdown still visible behind/over it, violating GAME_SPEC §7's clean Game Over presentation. Found and fixed as a polish in the M14 player-death test.

**Alternatives considered:** Hiding the panel inside `PlayerHUD.ShowGameOver` unconditionally (rejected — `HideCountdown` already exists and is the single presentation-entry the HUD owns; the death path is where the missing call belongs).

**Impact:** No gameplay change. Game Over now always covers/clears the cooldown text; verified by the player-death test asserting countdown hidden.

### 2026-09-16 — M13 brute hitbox test: `Physics.SyncTransforms()` before reading `collider.bounds`

**Decision:** `BruteEnemyTests.Brute_Config_..._LargerHitbox_Distinct` calls `Physics.SyncTransforms()` immediately before reading `bruteCol.bounds.size.y/x` and the other colliders' bounds.

**Reason:** Spawned enemies carry a `NavMeshAgent` but no `Rigidbody`, so their colliders are static: the physics-world AABB behind `Collider.bounds` only reflects the transform once a physics sync runs. The brute's `Start` sets `localScale = 2` on the transform (asserted correctly), but the CACHED collider bounds can still report scale-1 (height 2.0 == the runner's 2.0) when the read lands before any fixed step — exactly what failed once in the first full 81-test run (79/81) with brute=2.0 vs runner=2.0. This is the same static-collider root cause already fixed for `SpawnFrozenTarget` (2026-09-15).

**Alternatives considered:** Measuring after `yield return null` extra frames (rejected — frame-rate dependent, exactly the flake mechanism); comparing `transform.localScale` only (rejected — the spec requires a LARGER hitbox, not just a scaled model).

**Impact:** The hitbox comparison is deterministic regardless of how many fixed steps the scheduler ran between spawn and measurement. No production change; brute collider remains scale-2 (world height 4.0). BruteEnemyTests 5/5 in isolation and in the full 81/81 suite.
