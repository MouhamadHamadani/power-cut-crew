using System;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Reporting;

namespace PowerCutCrew.Editor
{
    /// <summary>Batchmode build entry points. Usage: -executeMethod PowerCutCrew.Editor.BuildTools.WindowsDevelopment</summary>
    public static class BuildTools
    {
        public static void WindowsDevelopment()
        {
            AddressableAssetSettings.BuildPlayerContent();
            var options = new BuildPlayerOptions
            {
                scenes = new[]
                {
                    "Assets/_Project/Scenes/Bootstrap.unity",
                    "Assets/_Project/Scenes/MainMenu.unity",
                    "Assets/_Project/Scenes/Street_Greybox.unity",
                },
                locationPathName = "Builds/Windows/PowerCutCrew.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            Console.WriteLine($"[Build] {report.summary.result}, {report.summary.totalErrors} errors, {report.summary.totalWarnings} warnings");
            if (report.summary.result != BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}
