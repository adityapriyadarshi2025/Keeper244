using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Keeper244.EditorTools
{
    /// <summary>
    /// Keeper244 → Build Game → Windows / macOS: standalone builds into unity/Keeper244/Builds/
    /// (git-ignored). Lab PCs only need the Windows build folder, not Unity (brief §5.1).
    ///
    /// Building for Windows on a Mac needs the "Windows Build Support (Mono)" module for this exact
    /// editor version (Unity Hub → Installs → ⚙ → Add modules). Teammates on Windows have it by default.
    /// </summary>
    public static class BuildMenu
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Keeper244/Build Game/Windows (64-bit)", priority = 20)]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Windows", "Keeper244.exe", "Windows Build Support (Mono)");

        [MenuItem("Keeper244/Build Game/macOS", priority = 21)]
        public static void BuildMac() => Build(BuildTarget.StandaloneOSX, "macOS", "Keeper244.app", "Mac Build Support (Mono)");

        static void Build(BuildTarget target, string folder, string file, string module)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
            {
                EditorUtility.DisplayDialog("Keeper244 build",
                    $"This editor can't build for {folder} yet.\n\nInstall the \"{module}\" module for Unity {Application.unityVersion}: " +
                    "Unity Hub → Installs → ⚙ next to this version → Add modules. Then restart Unity.", "OK");
                return;
            }
            if (!File.Exists(ScenePath))
            {
                EditorUtility.DisplayDialog("Keeper244 build", "Main.unity is missing. Run Keeper244 → Build Main Scene first.", "OK");
                return;
            }

            // the game must keep running (and receiving UDP) while the Python window has focus
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;

            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", folder));
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(outDir, file),
                target = target,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            if (s.result == BuildResult.Succeeded)
            {
                Debug.Log($"[Keeper244] {folder} build OK: {options.locationPathName} ({s.totalSize / (1024f * 1024f):0} MB, {s.totalTime.TotalSeconds:0} s). " +
                          "Copy the whole folder to the lab PC; logs go to the game's persistent data folder (docs/GAME_LOGS.md).");
                EditorUtility.RevealInFinder(options.locationPathName);
            }
            else
            {
                Debug.LogError($"[Keeper244] {folder} build {s.result}: {s.totalErrors} errors. See the Console above.");
            }
        }
    }
}
