using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DoomClone.Editor
{
    /// <summary>
    /// Editor/build automation: repeatable standalone builds and build directory
    /// management. Test invocation is handled by the Test Runner (in-editor via the
    /// Unity MCP 'tests-run' tool, or headless via Tools/test.sh).
    /// </summary>
    public static class BuildAutomation
    {
        public const string BuildRoot = "Builds";
        public const string LevelScene = "Assets/Scenes/DoomClone_Level01.unity";
        public const string MainMenuScene = "Assets/Scenes/MainMenu.unity";

        static readonly string[] ScenesForBuild =
        {
            MainMenuScene,
            LevelScene
        };

        [MenuItem("Tools/Doom Clone/Build Linux")]
        public static void BuildLinux() =>
            Build(BuildTarget.StandaloneLinux64, Path.Combine(BuildRoot, "Linux", "DoomClone.x86_64"));

        [MenuItem("Tools/Doom Clone/Build Windows")]
        public static void BuildWindows() =>
            Build(BuildTarget.StandaloneWindows64, Path.Combine(BuildRoot, "Windows", "DoomClone.exe"));

        public static void BuildLinuxFromCommandLine() => BuildLinux();
        public static void BuildWindowsFromCommandLine() => BuildWindows();

        static void Build(BuildTarget target, string outputPath)
        {
            foreach (string scene in ScenesForBuild)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scene) == null)
                {
                    Debug.LogError($"[BuildAutomation] Scene '{scene}' not found. Aborting build.");
                    return;
                }
            }

            string title = $"{target} build";
            Directory.CreateDirectory(BuildRoot);

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = ScenesForBuild,
                locationPathName = outputPath,
                target = target,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[BuildAutomation] {title} succeeded in {summary.totalSize} bytes -> {outputPath}");
            }
            else
            {
                Debug.LogError($"[BuildAutomation] {title} FAILED: {summary.result} ({summary.totalErrors} errors)");
            }
        }
    }
}