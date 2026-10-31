using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using Hbrp.Realtime;

public static class BuildRelay
{
    public static void Linux()
    {
        PlayerSettings.companyName="Horizonte";PlayerSettings.productName="HBRP UDP relay";
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server,ScriptingImplementation.Mono2x);
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        new GameObject("HBRP authenticated UDP relay").AddComponent<RealtimeServerHost>();
        EditorSceneManager.SaveScene(scene,"Assets/Relay.unity");
        Directory.CreateDirectory("Builds");
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Relay.unity"},
            target=BuildTarget.StandaloneLinux64,subtarget=(int)StandaloneBuildSubtarget.Server,
            locationPathName="Builds/HbrpRealtimeServer",options=BuildOptions.CompressWithLz4HC});
        if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Linux relay build failed");
        Debug.Log("LINUX_RELAY_BUILD_OK bytes="+report.summary.totalSize);
    }
}
