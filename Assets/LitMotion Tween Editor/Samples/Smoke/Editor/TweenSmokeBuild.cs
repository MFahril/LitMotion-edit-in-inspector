using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace LitMotion.TweenEditor.Samples.Editor
{
    /// <summary>
    /// Builds the demo rig into a Windows player, runs it with the smoke flag and reports.
    /// </summary>
    /// <remarks>
    /// Uses IL2CPP when that module is installed and Mono otherwise. Either way managed code
    /// stripping is set to High for the build, so Unity's linker runs and a channel found only
    /// by reflection would be stripped here first rather than in someone's shipped game. The
    /// project's own scripting backend and stripping level are put back afterwards.
    ///
    /// From a terminal, with the editor closed:
    /// <c>Unity -batchmode -projectPath . -executeMethod
    /// LitMotion.TweenEditor.Samples.Editor.TweenSmokeBuild.BuildAndRunFromCommandLine</c>
    /// exits with 0 on a pass.
    /// </remarks>
    public static class TweenSmokeBuild
    {
        const string Scene = "Assets/Scenes/TestScene.unity";
        const string OutputDirectory = "Builds/LMTESmoke";
        const string ExecutableName = "LMTESmoke.exe";
        const int TimeoutMilliseconds = 120000;

        [MenuItem("Tools/LitMotion/Smoke Test/Build and Run Player")]
        static void BuildAndRunMenu()
        {
            var passed = BuildAndRun(out var report);

            if (passed) Debug.Log("[LMTE Smoke] PASS\n" + report);
            else Debug.LogError("[LMTE Smoke] FAIL\n" + report);

            EditorUtility.DisplayDialog("LitMotion Tween Editor smoke test",
                (passed ? "Passed." : "Failed.") + " The full report is in the console.", "OK");
        }

        /// <summary>Entry point for <c>-executeMethod</c>; exits the editor with the result.</summary>
        public static void BuildAndRunFromCommandLine()
        {
            var passed = BuildAndRun(out var report);
            Debug.Log("[LMTE Smoke] " + (passed ? "PASS" : "FAIL") + "\n" + report);
            EditorApplication.Exit(passed ? 0 : 1);
        }

        /// <summary>Builds the player, runs it, and returns whether every check passed.</summary>
        public static bool BuildAndRun(out string report)
        {
            if (!File.Exists(Scene))
            {
                report = "The demo rig scene is missing: " + Scene;
                return false;
            }

            var executable = Path.GetFullPath(Path.Combine(OutputDirectory, ExecutableName));
            if (!Build(executable, out report)) return false;

            return Run(executable, ref report);
        }

        static bool Build(string executable, out string report)
        {
            var target = NamedBuildTarget.Standalone;
            var previousBackend = PlayerSettings.GetScriptingBackend(target);
            var previousStripping = PlayerSettings.GetManagedStrippingLevel(target);
            var backend = Il2CppInstalled() ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x;

            try
            {
                PlayerSettings.SetScriptingBackend(target, backend);
                PlayerSettings.SetManagedStrippingLevel(target, ManagedStrippingLevel.High);

                var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { Scene },
                    locationPathName = executable,
                    target = BuildTarget.StandaloneWindows64,

                    // Development, so the player can read its own GC allocations per frame.
                    options = BuildOptions.Development,
                });

                var summary = result.summary;
                report = "Build: " + summary.result + " with " + backend + ", stripping High, "
                         + summary.totalErrors + " errors, " + summary.totalTime.TotalSeconds.ToString("F0") + " s";
                return summary.result == BuildResult.Succeeded;
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(target, previousBackend);
                PlayerSettings.SetManagedStrippingLevel(target, previousStripping);

                // The build saved the project settings with the smoke values in them; write the
                // restored ones, or the file on disk keeps the build's.
                AssetDatabase.SaveAssets();
            }
        }

        static bool Run(string executable, ref string report)
        {
            var directory = Path.GetDirectoryName(executable);
            var results = Path.Combine(directory, "smoke-results.txt");
            var log = Path.Combine(directory, "Player.log");
            if (File.Exists(results)) File.Delete(results);

            var start = new ProcessStartInfo(executable,
                "-lmteSmoke -lmteSmokeResults \"" + results + "\" -logFile \"" + log + "\""
                + " -screen-fullscreen 0 -screen-width 640 -screen-height 360")
            {
                UseShellExecute = false,
                WorkingDirectory = directory,
            };

            using var process = Process.Start(start);
            if (process == null)
            {
                report += "\nCould not start " + executable;
                return false;
            }

            if (!process.WaitForExit(TimeoutMilliseconds))
            {
                process.Kill();
                report += "\nThe player did not finish within " + TimeoutMilliseconds / 1000 + " s. Log: " + log;
                return false;
            }

            if (!File.Exists(results))
            {
                report += "\nThe player exited with " + process.ExitCode + " and wrote no report. Log: " + log;
                return false;
            }

            var text = File.ReadAllText(results);
            report += "\n" + text + "\nPlayer log: " + log;
            return process.ExitCode == 0 && text.Contains("RESULT: PASS");
        }

        /// <summary>True when the Windows IL2CPP player module is installed for this editor.</summary>
        static bool Il2CppInstalled()
        {
            var variations = Path.Combine(EditorApplication.applicationContentsPath,
                "PlaybackEngines", "windowsstandalonesupport", "Variations");

            return Directory.Exists(Path.Combine(variations, "win64_player_development_il2cpp"));
        }
    }
}
