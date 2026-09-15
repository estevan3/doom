using System;
using System.Collections;
using UnityEngine;

namespace DoomClone.Automation
{
    /// <summary>Deterministic start helpers used by tests and the GameTestAPI.</summary>
    public static class GameBootstrap
    {
        public static void SetRandomSeed(int seed) => UnityEngine.Random.InitState(seed);

        public static bool IsGameReady()
        {
            if (GameManager.Instance == null) return false;
            if (WaveManager.Instance == null) return false;
            return UnityEngine.Object.FindAnyObjectByType<PlayerController>() != null;
        }

        /// <summary>Waits until the scene's GameManager/Player/WaveManager pipeline has booted.</summary>
        public static IEnumerator WaitForGameReady(float timeoutSeconds = 30f)
        {
            float start = Time.realtimeSinceStartup;

            while (!IsGameReady())
            {
                if (Time.realtimeSinceStartup - start > timeoutSeconds)
                {
                    throw new TimeoutException("Game did not become ready within " + timeoutSeconds + "s.");
                }

                yield return null;
            }

            // One extra frame so Start() pipelines (weapon init, wave start coroutine) have run.
            yield return null;
        }

        public static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds = 10f)
        {
            float start = Time.realtimeSinceStartup;

            while (!condition())
            {
                if (Time.realtimeSinceStartup - start > timeoutSeconds)
                {
                    throw new TimeoutException("Condition not satisfied within " + timeoutSeconds + "s.");
                }

                yield return null;
            }
        }
    }
}