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
            // steam_appid.txt (test App ID 480) is copied next to the exe for DEVELOPMENT builds only.
            // RELEASE BUILDS MUST NOT SHIP IT: with a real App ID Steam must start the game itself, and a stray file would pin the game to Spacewar.
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
            if ((options.options & BuildOptions.Development) != 0 && System.IO.File.Exists("steam_appid.txt"))
            {
                System.IO.File.Copy("steam_appid.txt", "Builds/Windows/steam_appid.txt", true);
            }
            Console.WriteLine($"[Build] {report.summary.result}, {report.summary.totalErrors} errors, {report.summary.totalWarnings} warnings");
            if (report.summary.result != BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}
