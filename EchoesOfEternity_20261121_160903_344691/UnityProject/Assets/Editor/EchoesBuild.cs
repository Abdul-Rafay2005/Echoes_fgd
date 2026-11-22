using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Echoes.Editor
{
    /// <summary>
    /// Standalone player build.
    ///   Menu: Echoes > Build Standalone Player
    ///   CLI:  Unity -batchmode -quit -projectPath PATH -executeMethod Echoes.Editor.BuildGame.Build -logFile build.log
    /// Optional CLI arguments:
    ///   -echoesOutput DIR      output folder (default: ../Builds/Windows next to the project)
    ///   -echoesTarget NAME     win64 (default) | linux64 | osx  (the matching build module must be installed)
    /// Exits with 0 only when BuildReport.summary.result == Succeeded.
    /// </summary>
    public static class BuildGame
    {
        public const string ProductFile = "EchoesOfEternity";

        [MenuItem("Echoes/Build Standalone Player", priority = 10)]
        public static void Build()
        {
            int exitCode = 1;
            try
            {
                exitCode = BuildInternal() ? 0 : 1;
            }
            catch (Exception e)
            {
                Debug.LogError("[EchoesBuild] Exception: " + e);
            }
            if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static bool BuildInternal()
        {
            string targetName = (Arg("-echoesTarget") ?? "win64").ToLowerInvariant();
            BuildTarget target;
            string folder, exe;
            switch (targetName)
            {
                case "linux64": target = BuildTarget.StandaloneLinux64; folder = "Linux"; exe = ProductFile + ".x86_64"; break;
                case "osx": target = BuildTarget.StandaloneOSX; folder = "macOS"; exe = ProductFile + ".app"; break;
                default: target = BuildTarget.StandaloneWindows64; folder = "Windows"; exe = ProductFile + ".exe"; break;
            }

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
            {
                Debug.LogError($"[EchoesBuild] BUILD_RESULT: MissingModule - the {target} build support module is not installed for Unity {Application.unityVersion}. Install it from Unity Hub > Installs > Add modules.");
                return false;
            }

            if (!File.Exists(ProjectBuilder.ScenePath))
            {
                Debug.Log("[EchoesBuild] Scene missing - running the project builder first.");
                ProjectBuilder.BuildAll();
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string defaultOutput = Path.GetFullPath(Path.Combine(projectRoot, "..", "Builds", folder));
            string output = Path.GetFullPath(Arg("-echoesOutput") ?? defaultOutput);

            // Only clean folders we own: the default output or one that already contains our player.
            if (Directory.Exists(output) && (output == defaultOutput || File.Exists(Path.Combine(output, exe)) || Directory.Exists(Path.Combine(output, exe))))
                Directory.Delete(output, true);
            Directory.CreateDirectory(output);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ProjectBuilder.ScenePath },
                locationPathName = Path.Combine(output, exe),
                target = target,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

            Debug.Log($"[EchoesBuild] Building {target} -> {options.locationPathName}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary s = report.summary;

            var sb = new StringBuilder();
            sb.AppendLine("Echoes of Eternity build report");
            sb.AppendLine("Date:        " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Unity:       " + Application.unityVersion);
            sb.AppendLine("Target:      " + s.platform);
            sb.AppendLine("Result:      " + s.result);
            sb.AppendLine("Output:      " + s.outputPath);
            sb.AppendLine("Size (MB):   " + (s.totalSize / (1024.0 * 1024.0)).ToString("0.0"));
            sb.AppendLine("Duration:    " + s.totalTime);
            sb.AppendLine("Errors:      " + s.totalErrors);
            sb.AppendLine("Warnings:    " + s.totalWarnings);
            foreach (var step in report.steps)
                foreach (var m in step.messages.Where(m => m.type == LogType.Error || m.type == LogType.Exception))
                    sb.AppendLine("ERROR: " + m.content);
            string reportText = sb.ToString();
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(output), "build_report_" + folder + ".txt"), reportText);
            Debug.Log("[EchoesBuild] " + reportText);
            Debug.Log("[EchoesBuild] BUILD_RESULT: " + s.result);
            return s.result == BuildResult.Succeeded;
        }
    }
}
