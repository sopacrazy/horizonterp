using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class ReleaseBuild
{
    public static void Windows()
    {WindowsAt("Builds/WindowsRelease/Horizonte.exe");}
    public static void WindowsHudReview()
    {FemaleCharacterVerification.Verify();WindowsAt("Builds/WindowsHudReview/Horizonte.exe");}
    static void WindowsAt(string output)
    {
        GameVersionBuildSetup.Apply();Hud2Setup.Prepare();
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=GameUpdateSetup.Scenes,
            target=BuildTarget.StandaloneWindows64,locationPathName=output,
            options=BuildOptions.CompressWithLz4HC});
        if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Windows release failed");
        Debug.Log("WINDOWS_RELEASE_OK version="+GameVersion.Current.version+" bytes="+report.summary.totalSize);
    }
    public static void Android(){AndroidMapOptimization.OptimizeAndBuild();}
}
