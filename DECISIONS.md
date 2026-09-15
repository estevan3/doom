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

### 2026-09-14 — Toolchain/editor automation

**Decision:** `Assets/Game/Editor/BuildAutomation.cs` (menu + CLI builds to `Builds/`) and `tools/test.sh` (EditMode / PlayMode / both via Unity `-runTests`) added. `Builds/` and `TestResults/` are ignored via `.git/info/exclude` (local only, not committed).

**Reason:** The development loop requires repeatable compile/test/build steps that work headless, per `AGENTS.md` and `TEST_PLAN.md`. Unity-version path is configurable via env (`UNITY_BIN`) and never hard-coded in game code.

**Alternatives considered:** Requiring manual Editor runs. Rejected — against the autonomous operating mode.

**Impact:** Enables the CI/smoke loop and future milestone verification.
