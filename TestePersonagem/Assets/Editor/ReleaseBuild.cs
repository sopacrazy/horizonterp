using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class ReleaseBuild
{
    public static void WindowsEconomyTest()
    {
        // Build the current scenes/assets without running old setup routines.
        // A staging directory keeps the executable behind Jogar intact on failure.
        GameVersionBuildSetup.Apply();
        const string output="Builds/WindowsEconomyTest/Horizonte.exe";
        var scenes=GameUpdateSetup.Scenes;
        foreach(var scene in scenes)
            if(!System.IO.File.Exists(scene))throw new Exception("Cena ausente: "+scene);
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=scenes,
            target=BuildTarget.StandaloneWindows64,locationPathName=output,
            options=BuildOptions.CompressWithLz4HC});
        if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Windows economy build failed: "+report.summary.result);
        System.IO.File.WriteAllText("../tools/criminal-economy-validation/windows-build.json",
            JsonUtility.ToJson(new EconomyBuildInfo{version=GameVersion.Current.version,scenes=scenes.Length,
                bytes=(long)report.summary.totalSize,completedUtc=DateTime.UtcNow.ToString("O"),output=output},true));
        Debug.Log("WINDOWS_ECONOMY_BUILD_OK version="+GameVersion.Current.version+" scenes="+scenes.Length+" bytes="+report.summary.totalSize);
    }
    [Serializable] sealed class EconomyBuildInfo {public string version,completedUtc,output;public int scenes;public long bytes;}
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
