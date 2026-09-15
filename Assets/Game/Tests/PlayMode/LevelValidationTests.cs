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
    /// Level validation per GAME_SPEC.md §1 (§5 for navigation) and TEST_PLAN.md
    /// "Level" section. Verifies the real scene composition, the spec-mandated
    /// spawn-point tags, and that the NavMesh is baked/usable at runtime.
    /// </summary>
    public class LevelValidationTests
    {
        const string Level = GameTestAPI.LevelSceneName;
        const string PlayerSpawnTag = "PlayerSpawnPoint";
        const string EnemySpawnTag = "EnemySpawnPoint";

        [UnityTest]
        public IEnumerator Level01_Composition_SpawnTagsAndNavMesh_AreValid()
        {
            yield return GameTestAPI.StartFreshLevel(30f);

            Assert.AreEqual(Level, SceneManager.GetActiveScene().name,
                "DoomClone_Level01 must be the active scene after boot.");

            // --- Player spawn marker (GAME_SPEC §1) ---
            GameObject[] playerSpawns = GameObject.FindGameObjectsWithTag(PlayerSpawnTag);
            Assert.AreEqual(1, playerSpawns.Length,
                "Exactly one GameObject must carry the PlayerSpawnPoint tag.");
            Assert.AreEqual("PlayerSpawnPoint", playerSpawns[0].name,
                "The tagged player marker should keep its canonical name.");

            // --- Central arena (GAME_SPEC §1) ---
            int arenaObjects = CountObjectsByNameContaining("Arena");
            Assert.GreaterOrEqual(arenaObjects, 1,
                "At least one central arena object (e.g. Arena_Floor) must exist.");

            // --- Corridors + secondary rooms (GAME_SPEC §1) ---
            HashSet<string> corridorSets = DistinctNamingToken("Corridor");
            Assert.GreaterOrEqual(corridorSets.Count, 2,
                "At least 2 corridors must connect secondary areas.");
            Assert.LessOrEqual(corridorSets.Count, 4,
                "The blockout should stay within a small corridor count.");

            int roomObjects = CountObjectsByNameContaining("Room");
            Assert.GreaterOrEqual(roomObjects, 1,
                "At least one secondary room must exist.");

            // --- Enemy spawn points (GAME_SPEC §1) ---
            GameObject[] enemySpawns = GameObject.FindGameObjectsWithTag(EnemySpawnTag);
            Assert.GreaterOrEqual(enemySpawns.Length, 7,
                "The level blockout ships 7 distributed EnemySpawnPoint markers; none may be lost.");
            foreach (GameObject spawn in enemySpawns)
            {
                Assert.IsNotNull(spawn.transform, "A tagged EnemySpawnPoint must have a Transform.");
                Assert.IsTrue(spawn.name.Contains("EnemySpawnPoint"),
                    "Tagged EnemySpawnPoint objects should keep canonical names (was: " + spawn.name + ").");
            }

            Assert.AreEqual(enemySpawns.Length, WaveManager.Instance.spawnPoints.Length,
                "WaveManager should discover every tagged EnemySpawnPoint at runtime (name-based lookup).");

            // --- Navigation (GAME_SPEC §5) ---
            Assert.IsNotNull(GameManager.Instance.GetComponent<NavMeshSetup>(),
                "GameManager should expose NavMeshSetup at runtime (code-driven bake).");
            Assert.IsNotNull(Object.FindAnyObjectByType<Unity.AI.Navigation.NavMeshSurface>(),
                "A NavMeshSurface component must exist at runtime.");

            yield return GameBootstrap.WaitUntil(
                () => NavMesh.CalculateTriangulation().vertices.Length > 0, 10f);
            Assert.Greater(NavMesh.CalculateTriangulation().vertices.Length, 0,
                "NavMesh must produce baked triangles at runtime.");

            // --- End-to-end proof that spawn points are found and used ---
            yield return GameBootstrap.WaitUntil(() => WaveManager.Instance.GetCurrentWave() >= 1, 10f);
            Assert.GreaterOrEqual(WaveManager.Instance.GetCurrentWave(), 1,
                "Wave 1 must spawn after the 2s delay using the discovered spawn points.");

            Debug.Log("[LevelValidation] enemySpawnTagged=" + enemySpawns.Length +
                      ", corridorSets=" + corridorSets.Count +
                      ", arenaObjects=" + arenaObjects +
                      ", roomObjects=" + roomObjects +
                      ", navTriangleCount=" + NavMesh.CalculateTriangulation().vertices.Length / 3);
        }

        static int CountObjectsByNameContaining(string token)
        {
            int count = 0;
            foreach (GameObject obj in Object.FindObjectsOfType<GameObject>())
            {
                if (obj != null && obj.name.Contains(token)) count++;
            }
            return count;
        }

        static HashSet<string> DistinctNamingToken(string prefix)
        {
            var tokens = new HashSet<string>();
            foreach (GameObject obj in Object.FindObjectsOfType<GameObject>())
            {
                if (obj == null || !obj.name.StartsWith(prefix + "_")) continue;
                string rest = obj.name.Substring(prefix.Length + 1);
                int underscore = rest.IndexOf('_');
                string token = underscore >= 0 ? rest.Substring(0, underscore) : rest;
                if (token.Length > 0) tokens.Add(token);
            }
            return tokens;
        }
    }
}