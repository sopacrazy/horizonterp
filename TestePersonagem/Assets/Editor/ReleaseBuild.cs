using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class ReleaseBuild
{
    public static void Windows()
    {
        GameVersionBuildSetup.Apply();Hud2Setup.Prepare();
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=GameUpdateSetup.Scenes,
            target=BuildTarget.StandaloneWindows64,locationPathName="Builds/WindowsRelease/Horizonte.exe",
            options=BuildOptions.CompressWithLz4HC});
        if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Windows release failed");
        Debug.Log("WINDOWS_RELEASE_OK version="+GameVersion.Current.version+" bytes="+report.summary.totalSize);
    }
    public static void Android(){AndroidMapOptimization.OptimizeAndBuild();}
}
