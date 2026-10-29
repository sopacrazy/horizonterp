using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Android;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

public static class AndroidSetup
{
    public static void Build()
    {
        ArrivalMissionSetup.Prepare();
        HudSetup.Prepare();
        BrandingSetup.Prepare();
#if UNITY_ANDROID
        string tools=Path.GetFullPath("../tools/android");
        string bundled=Path.Combine(Path.GetDirectoryName(EditorApplication.applicationPath),"Data/PlaybackEngines/AndroidPlayer");
        string androidTools=Directory.Exists(Path.Combine(tools,"SDK")) ? tools : bundled;
        AndroidExternalToolsSettings.sdkRootPath=Path.Combine(androidTools,"SDK");
        AndroidExternalToolsSettings.ndkRootPath=Path.Combine(androidTools,"NDK");
        AndroidExternalToolsSettings.jdkRootPath=Path.Combine(androidTools,"OpenJDK");
#endif
        PlayerSettings.companyName="Adriano";
        PlayerSettings.productName="Horizonte";
        GameVersionBuildSetup.Apply();
        PlayerSettings.defaultInterfaceOrientation=UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToPortrait=false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown=false;
        PlayerSettings.allowedAutorotateToLandscapeLeft=true;
        PlayerSettings.allowedAutorotateToLandscapeRight=true;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android,ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion=AndroidSdkVersions.AndroidApiLevel26;
        PlayerSettings.Android.targetSdkVersion=AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android,false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,new[]{GraphicsDeviceType.OpenGLES3});
        PlayerSettings.Android.useCustomKeystore=false;
        EditorUserBuildSettings.buildAppBundle=false;
        Directory.CreateDirectory("Builds/Android");
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{
            scenes=GameUpdateSetup.Scenes,
            locationPathName="Builds/Android/TestePersonagem.apk",
            target=BuildTarget.Android,options=BuildOptions.CompressWithLz4HC});
        if(report.summary.result!=BuildResult.Succeeded)throw new Exception("APK falhou: "+report.summary.result);
        Debug.Log("ANDROID_APK_OK "+report.summary.totalSize);
    }
}
