using System.Collections;
using System.Collections.Generic;
using DoomClone.Automation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DoomClone.Tests.PlayMode
{
    /// <summary>
    /// Milestone 3 — Navigation validation per GAME_SPEC.md §5 and ARCHITECTURE.md's
    /// "Spawn points" section. Requires Unity 6 NavMesh API (4-arg SamplePosition /
    /// CalculatePath with area mask — the 3-arg overloads are gone in 6000.6).
    ///
    /// Spawn markers are authoring floats ~0.75 u above the walkable surface, so the
    /// marker-to-NavMesh tolerance is set explicitly (SpawnSampleTolerance): the sample
    /// must succeed from each marker within this small radius.
    /// </summary>
    public class NavigationValidationTests
    {
        const string EnemySpawnTag = "EnemySpawnPoint";
        const string Level = GameTestAPI.LevelSceneName;

        // Markers sit at Y=1.0 while the walk surface is ~0.25 (floor cube scaled 0.5 at
        // Y=0). 1.5 u is the explicit small tolerance for "on/near the NavMesh" (vertical
        // gap is only 0.75 u).
        const float SpawnSampleTolerance = 1.5f;

        [UnityTest]
        public IEnumerator Navigation_BakeCompletes_AllSpawnsSampleAndPathToPlayer()
        {
            yield return GameTestAPI.StartFreshLevel(30f);
            Assert.AreEqual(Level, SceneManager.GetActiveScene().name,
                "Root level must be active after boot.");

            yield return BakeReady();

            PlayerController player = GameTestAPI.GetPlayer();
            Assert.IsNotNull(player, "Player required to validate agent paths.");
            Vector3 playerPos = NavMeshUtil.SnapToNavMesh(player.transform.position, 2f);
            Assert.IsTrue(NavMesh.SamplePosition(playerPos, out _, 0.01f, NavMesh.AllAreas),
                "Player position must be walkable for path endpoints.");

            GameObject[] spawns = GameObject.FindGameObjectsWithTag(EnemySpawnTag);
            Assert.GreaterOrEqual(spawns.Length, 7,
                "The 7 EnemySpawnPoint markers must exist.");

            Assert.IsNotNull(WaveManager.Instance, "WaveManager required.");
            Assert.GreaterOrEqual(WaveManager.Instance.spawnPoints.Length, 7,
                "WaveManager must discover every spawn point at runtime.");

            var failures = new List<string>();
            foreach (GameObject spawn in spawns)
            {
                if (!NavMesh.SamplePosition(spawn.transform.position, out NavMeshHit hit,
                        SpawnSampleTolerance, NavMesh.AllAreas))
                {
                    failures.Add(spawn.name + " does not sample on the NavMesh within " +
                                 SpawnSampleTolerance + "u");
                    continue;
                }

                // Every spawn here is representative: 4 arena corners + 3 corridors (7 of 7).
                NavMeshPath path = new NavMeshPath();
                NavMesh.CalculatePath(hit.position, playerPos, NavMesh.AllAreas, path);
                if (path.status != NavMeshPathStatus.PathComplete)
                {
                    failures.Add(spawn.name + " path to player is " + path.status +
                                 " (from " + hit.position.ToString("F2") + ")");
                }
            }

            Assert.IsEmpty(failures, "Navigation failures:\n" + string.Join("\n", failures));

            Debug.Log("[Nav] spawnPoints=" + spawns.Length +
                      ", triangles=" + NavMesh.CalculateTriangulation().vertices.Length / 3 +
                      ", playerAt=" + playerPos.ToString("F2"));
        }

        [UnityTest]
        public IEnumerator Navigation_SpawnedEnemy_PlacedOnNavMesh_AndMovesTowardPlayer()
        {
            yield return GameTestAPI.StartFreshLevel(30f);
            yield return BakeReady();

            PlayerController player = GameTestAPI.GetPlayer();
            Assert.IsNotNull(player, "Player required.");

            GameObject spawned = GameTestAPI.SpawnEnemy("runner");
            Assert.IsNotNull(spawned, "SpawnEnemy(runner) must instantiate an enemy.");
            NavMeshAgent agent = spawned.GetComponent<NavMeshAgent>();
            Assert.IsNotNull(agent, "Spawned enemy needs a NavMeshAgent.");

            // WaveManager enables the agent 0.1 s after spawn; placement must then complete.
            yield return GameBootstrap.WaitUntil(
                () => agent != null && agent.enabled && agent.isOnNavMesh, 5f);

            Assert.IsTrue(agent.enabled, "Agent should be enabled after placement.");
            Assert.IsTrue(agent.isOnNavMesh,
                "Spawned enemy agent must sit on the NavMesh (no off-NavMesh enable).");
            Assert.IsTrue(agent.SetDestination(player.transform.position),
                "SetDestination to the player must be accepted without error.");

            Vector3 start = spawned.transform.position;
            yield return new WaitForSeconds(1.5f);

            float moved = Vector3.Distance(spawned.transform.position, start);
            float distanceToPlayer = Vector3.Distance(spawned.transform.position, player.transform.position);
            Assert.Greater(moved, 1f,
                "Spawned runner must move on the NavMesh (moved=" + moved.ToString("F2") +
                "u, distToPlayer=" + distanceToPlayer.ToString("F2") + "u).");
            Assert.Less(distanceToPlayer, Vector3.Distance(start, player.transform.position),
                "The runner should be strictly closer to the player after moving.");
        }

        [UnityTest]
        public IEnumerator Navigation_Wave1SpawnsEnemies_ThatMoveTowardPlayer()
        {
            yield return GameTestAPI.StartFreshLevel(30f);
            yield return BakeReady();

            // Wave 1 must spawn after the 2 s delay and its enemies must be alive/tracked.
            yield return GameBootstrap.WaitUntil(
                () => WaveManager.Instance != null &&
                      WaveManager.Instance.GetCurrentWave() >= 1 &&
                      WaveManager.Instance.GetEnemiesAlive() > 0, 15f);

            Assert.GreaterOrEqual(WaveManager.Instance.GetEnemiesAlive(), 1,
                "Wave 1 must track living enemies.");

            Enemy waveEnemy = Object.FindAnyObjectByType<Enemy>();
            Assert.IsNotNull(waveEnemy, "Wave 1 should have a living enemy in the scene.");
            Assert.IsTrue(waveEnemy.GetComponent<NavMeshAgent>().isOnNavMesh,
                "Wave 1 enemies must be placed on the NavMesh.");

            Vector3 waveStart = waveEnemy.transform.position;
            yield return new WaitForSeconds(1.2f);

            float waveMoved = Vector3.Distance(waveEnemy.transform.position, waveStart);
            Assert.Greater(waveMoved, 0.5f,
                "Wave-enemy NavMeshAgent must drive movement toward the player (moved=" +
                waveMoved.ToString("F2") + "u).");

            Debug.Log("[Nav] wave=" + WaveManager.Instance.GetCurrentWave() +
                      ", alive=" + WaveManager.Instance.GetEnemiesAlive() +
                      ", moved=" + waveMoved.ToString("F2"));
        }

        static IEnumerator BakeReady()
        {
            yield return GameBootstrap.WaitUntil(
                () => NavMesh.CalculateTriangulation().vertices.Length > 0, 10f);
            Assert.Greater(NavMesh.CalculateTriangulation().vertices.Length, 0,
                "NavMesh must produce baked triangles at runtime.");
            // Let the async BuildNavMesh finish propagating before sampling/pathing.
            yield return new WaitForSeconds(0.5f);
        }
    }
}