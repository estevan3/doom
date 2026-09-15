# Doom Clone — Autonomous Implementation TODO

Status values:

- `[ ]` not started
- `[~]` in progress
- `[x]` completed and verified
- `[!]` blocked

## Project state at inspection — 2026-09-14

Verified facts (from file/Unity inspection, no gameplay implemented in this pass):

- Unity version: `6000.6.0f1` (Unity 6).
- Installed packages relevant to the spec: `com.unity.ai.navigation` 2.0.14, `com.unity.inputsystem` 1.20.0, `com.unity.render-pipelines.universal` 17.6.0, `com.unity.test-framework` 1.8.0, `com.ivanmurzak.unity.mcp` 0.90.0 (OpenUPM).
- Current scene: `Assets/Scenes/DoomClone_Level01.unity` open, valid, not dirty, 60 root GameObjects; in build settings (index 1). `Assets/Scenes/MainMenu.unity` is index 0.
- The level blockout already exists: arena, 3 corridors (N/E/W), secondary rooms, point lights, `PlayerSpawnPoint`, 7 `EnemySpawnPoint_*` GameObjects, `GameManager`.
- Gameplay scripts live under `Assets/Scripts/` (not the `Assets/Game/...` layout from ARCHITECTURE.md) with no `Automation/`, `Editor/`, or `Tests/` folders.
- Architecture is code-driven at runtime: `GameManager` builds NavMeshSetup, Player (CharacterController + camera + `PlayerHUD`), and `WaveManager`; `WaveManager` procedurally creates enemy prefabs and finds spawn points by name.
- Named deviations from the spec/architecture: Runner = `ZombieRunner`, Brute = `TankBrute`; look/weapons combined into `PlayerController`/`WeaponManager` instead of `PlayerLook`/`PlayerWeaponController`.
- Gap: `EnemySpawnPoint` / `PlayerSpawnPoint` are GameObject NAMES. `TagManager.asset` defines no custom tags; all objects are `Untagged`. The spec requires real tags (found by name currently).
- Unity MCP (ai-game-developer) is connected and responding.
- Compilation: OK — EditMode test run completed with no compile errors.
- Tests: none exist in the project (EditMode run returned "No tests found"). No test asmdefs or fixtures.
- Console errors seen (non-blocking for runtime): Blender missing at import; `graphicsApiMask` mismatch (262148 → 2359300); an old MCP HubConnection failure from a prior launch (current connection works).
- Git: `main` HEAD `5854778` (up to date with `origin/main`). Working tree NOT clean: modified `.gitignore`, `Assets/Scenes/DoomClone_Level01.unity`, `ProjectSettings/URPProjectSettings.asset`; untracked art packs and folders (BloodDecalsAndEffects, Cyber Monsters 2, Deadly Kombat Free version, FightingMotionsVolume1, Kevin Iglesias, Material, SiuniaevCharacters, True_Horror_Creatures, Vefects, models).

## Milestone 0 — Repository and agent foundation

- [x] Confirm Unity project root and Unity version (6000.6.0f1).
- [~] Confirm Git repository and clean baseline commit (repo on `main` confirmed; working tree has uncommitted changes — not a clean baseline).
- [x] Confirm the existing `ai-game-developer` Unity MCP is installed and connected.
- [x] Confirm AI Navigation package is installed (com.unity.ai.navigation 2.0.14).
- [x] Confirm Unity Test Framework is available (com.unity.test-framework 1.8.0; no tests authored yet).
- [x] Add `AGENTS.md`, `GAME_SPEC.md`, `ARCHITECTURE.md`, `TEST_PLAN.md`, `TODO.md`, `DECISIONS.md` (present, `DECISIONS.md` still empty).
- [x] Add/verify project-side OpenCode instructions without replacing the existing MCP configuration (opencode.json references the 6 docs + remote MCP).
- [x] Verify OpenCode can read the project and Unity MCP (MCP tools respond; doc files readable).

Acceptance:

- OpenCode can inspect the project. ✔
- OpenCode can call the existing `ai-game-developer` Unity MCP tools. ✔
- Project compiles before gameplay work begins. ✔

## Milestone 1 — Automation foundation

- [x] Create `Assets/Game/Automation/`.
- [x] Implement `GameTestAPI`.
- [x] Implement `GameStateInspector`.
- [x] Implement `ScreenshotCapture`.
- [x] Implement `GameBootstrap` if needed (implemented: readiness/timeout helpers for tests).
- [x] Implement editor/build automation where useful (`Assets/Game/Editor/BuildAutomation.cs` + `tools/test.sh` CLI runner).
- [x] Add a minimal runtime smoke-test fixture.

Acceptance:

- Agent can reset a known game state. ✔ (verified by PlayMode test `AutomationApi_ResetInspectSnapshotScreenshot_Work`)
- Agent can inspect player/wave/enemy state. ✔ (verified — parseable `GameStateSnapshot` JSON logged + round-trip EditMode test)
- Agent can capture a runtime screenshot. ✔ (verified — `TestResults/automation_acceptance.png`, 1280x720, written during PlayMode suite)

Verification (2026-09-14):

- EditMode: 3/3 passed (`ProductionTypes_LiveInExpectedRuntimeAssembly`, `AutomationApi_ExposesSpecRequiredOperations`, `GameStateSnapshot_JsonRoundTrip_KeepsData`).
- PlayMode: 2/2 passed (level boot + automation reset/inspect/screenshot acceptance).
- Compiles clean; no new runtime errors from the automation path (only pre-existing Blender + graphicsApiMask console noise).

Assembly/structure notes affecting later milestones:

- Production code now lives in a named assembly `DoomClone.Runtime` (`Assets/Scripts/DoomClone.Runtime.asmdef`); no scripts were moved. References test/automation assemblies against it.
- Automation code lives in `DoomClone.Automation` (`Assets/Game/Automation/`); tests live under `Assets/Game/Tests/EditMode` and `Assets/Game/Tests/PlayMode`.
- Production additions to support deterministic automation (documented in `DECISIONS.md`): `WaveManager.SpawnEnemyByType`, `PlayerController.SetHealth`, `Weapon.SetAmmo`.

## Milestone 2 — Level blockout

- [x] Create `DoomClone_Level01`.
- [x] Build central arena.
- [x] Build 2–3 corridors and secondary areas (3 corridors: N/E/W + rooms).
- [x] Add PlayerSpawnPoint (exists; tag missing — see gap above).
- [~] Add tagged EnemySpawnPoints (7 exist by name; spec tag `EnemySpawnPoint` not defined).
- [x] Add floor, walls, ceiling.
- [x] Add basic dark point-lighting (6 point lights).
- [ ] Add level validation test.

Acceptance:

- Scene loads. ✔
- Required geometry and markers exist. ✔ (markers by name; tags still needed)
- No blocking console errors. ✔

## Milestone 3 — Navigation

- [~] Add/configure `NavMeshSurface` (runtime-baked via `NavMeshSetup` on GameManager; not present as scene component).
- [~] Bake navigation (automatic at runtime; no editor-baked surface).
- [ ] Validate arena and corridors are navigable.
- [ ] Validate enemy spawn points are usable.
- [ ] Add navigation validation test.

Acceptance:

- An agent can path from representative spawn points toward the player. Not yet verified.

## Milestone 4 — Player + HUD

- [~] Implement CharacterController movement (implemented in `PlayerController`).
- [~] Implement mouse look (in `PlayerController`).
- [~] Implement sprint.
- [~] Implement jump.
- [~] Implement 100 HP.
- [~] Implement death state.
- [~] Implement restart behavior.
- [~] Implement HUD shell (`PlayerHUD`, code-driven).
- [ ] Add Player PlayMode tests.

Acceptance:

- Player can move around the blockout. Not run-verified in this pass.
- Damage/death/restart works. Not run-verified in this pass.
- HUD displays required fields. Not run-verified in this pass.

## Milestone 5 — Weapon framework

- [~] Implement base `Weapon`.
- [~] Implement weapon switching 1–4 (`WeaponManager`).
- [~] Implement common firing/impact feedback.
- [ ] Add weapon tests.

Acceptance:

- Player can equip each weapon. Code exists; not run-verified.
- HUD reflects equipped weapon. Code exists; not run-verified.

## Milestone 6 — Pistol

- [~] Implement hitscan pistol.
- [~] Implement damage/cadence.
- [~] Implement ammo rules.
- [~] Implement impact/firing feedback.
- [ ] Add tests.

## Milestone 7 — Shotgun

- [~] Implement 6–8 pellet hitscan behavior.
- [~] Implement close-range damage.
- [~] Implement slow cadence.
- [~] Implement limited ammo.
- [~] Implement reload behavior.
- [ ] Add tests.

## Milestone 8 — Assault Rifle

- [~] Implement automatic hitscan.
- [~] Implement medium damage.
- [~] Implement limited ammo.
- [~] Implement high cadence.
- [ ] Add tests.

## Milestone 9 — Chainsaw

- [~] Implement short-range melee.
- [~] Implement continuous damage while held.
- [~] Implement no-ammo behavior.
- [~] Implement simple continuous feedback.
- [ ] Add tests.

## Milestone 10 — Enemy framework

- [~] Implement base `Enemy`.
- [~] Implement health/death.
- [~] Implement common damage to player.
- [~] Implement NavMeshAgent integration.
- [~] Implement death notification contract for `WaveManager`.
- [ ] Add base enemy tests.

## Milestone 11 — Runner (`ZombieRunner`)

- [~] Implement rapid direct chase.
- [~] Implement fast melee attack.
- [~] Add distinct visual (capsule + material color).
- [ ] Add tests.

## Milestone 12 — Ranged Soldier

- [~] Implement ideal range behavior.
- [~] Implement ranged attack.
- [~] Implement simple strafing/repositioning.
- [~] Add distinct visual (capsule + material color).
- [ ] Add tests.

## Milestone 13 — Brute (`TankBrute`)

- [~] Implement slow movement.
- [~] Implement heavy attack.
- [~] Implement 0.5 s telegraph.
- [~] Implement larger hitbox/scale.
- [~] Add distinct visual (capsule + material color).
- [ ] Add tests.

## Milestone 14 — WaveManager

- [~] Implement singleton `WaveManager`.
- [~] Implement 2 s initial delay.
- [~] Implement wave composition (ratio-based scaling).
- [~] Implement alive-enemy tracking.
- [~] Implement 5 s cooldown.
- [~] Implement HUD countdown.
- [~] Implement infinite progression.
- [~] Implement difficulty scaling.
- [~] Implement stop-on-player-death behavior.
- [ ] Add timing and state-transition tests.

Acceptance:

- Wave 1 starts after 2 s. Not run-verified in this pass.
- Final enemy death starts a 5 s cooldown. Not run-verified in this pass.
- Next wave begins automatically. Not run-verified in this pass.
- Waves continue indefinitely until player death. Not run-verified in this pass.

## Milestone 15 — Integration polish

- [~] Finish Game Over UI (code exists; button restart works per git history).
- [~] Finish weapon HUD/ammo behavior.
- [ ] Validate all enemy/weapon combinations.
- [ ] Validate spawn points and NavMesh.
- [ ] Run full EditMode suite (no tests exist yet).
- [ ] Run full PlayMode suite (no tests exist yet).
- [ ] Run full smoke test (no fixture exists).
- [ ] Capture representative screenshots.
- [ ] Fix remaining runtime errors (Blender import + graphicsApiMask console noise to clean up).
- [ ] Balance damage/health/cadence.

## Milestone 16 — Final playable build

- [ ] Build target platform (an old build exists at repo root, not committed).
- [ ] Run build smoke test.
- [ ] Verify no blocking console errors.
- [ ] Verify restart after Game Over.
- [ ] Verify wave loop continues.
- [ ] Tag/commit the playable milestone.

## Current agent instruction

Next milestone to start: **Milestone 1 — Automation foundation** (no gameplay until automation/test scaffolding exists).
The engine/level/gameplay code from milestones 2–14 already exists and compiles but is **unverified by tests** and has spec gaps (missing spawn-point tags, missing `Assets/Game/...` layout, no test suite). Each of those milestones still needs its PlayMode/EditMode verification per `TEST_PLAN.md` before it can be marked complete.
After completing each milestone, update this file and commit the result.