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
