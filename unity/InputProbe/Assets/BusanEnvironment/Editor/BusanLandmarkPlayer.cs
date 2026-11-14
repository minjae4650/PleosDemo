using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BusanEnvironment.Editor
{
    // A Windows player of the landmark map alone (Landmarks/BusanLandmarks.unity), written outside the project. The build settings
    // list is not touched (the scene is passed to the build directly).
    //   Unity.exe -batchmode -quit -projectPath <project> -executeMethod BusanEnvironment.Editor.BusanLandmarkPlayer.Build -playerOut <dir>
    public static class BusanLandmarkPlayer
    {
        public const string DefaultOut = @"C:\c\busan-out\play";

        public static void Build()
        {
            string outDir = DefaultOut;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-playerOut") outDir = args[i + 1];
            Directory.CreateDirectory(outDir);
            var summary = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { BusanLandmarkScene.ScenePath },
                locationPathName = Path.Combine(outDir, "BusanLandmarks.exe"),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None
            }).summary;
            bool ok = summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
            Debug.Log($"LANDMARK PLAYER {(ok ? "OK" : "ERR")} result={summary.result} seconds={summary.totalTime.TotalSeconds:0} sizeMB={summary.totalSize / (1024f * 1024f):0.0} errors={summary.totalErrors} warnings={summary.totalWarnings} out={outDir}");
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
