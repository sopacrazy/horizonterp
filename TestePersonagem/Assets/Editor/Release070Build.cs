using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class Release070Build
{
    public static void Windows()
    {
        GameVersionBuildSetup.Apply();
        Hud2Setup.Prepare();
        var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes=GameUpdateSetup.Scenes,target=BuildTarget.StandaloneWindows64,
            locationPathName="Builds/Windows070/Horizonte.exe",options=BuildOptions.CompressWithLz4HC });
        if(result.summary.result!=BuildResult.Succeeded)throw new Exception("Windows build failed: "+result.summary.result);
        Debug.Log("WINDOWS_070_OK bytes="+result.summary.totalSize);
    }
    public static void Android()
    {
        AndroidMapOptimization.OptimizeAndBuild();
    }
}
