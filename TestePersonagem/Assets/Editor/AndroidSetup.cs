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
        HudSetup.Prepare();
        BrandingSetup.Prepare();
        string tools=Path.GetFullPath("../tools/android");
        AndroidExternalToolsSettings.sdkRootPath=tools+"/SDK";
        AndroidExternalToolsSettings.ndkRootPath=tools+"/NDK";
        AndroidExternalToolsSettings.jdkRootPath=tools+"/OpenJDK";
        PlayerSettings.companyName="Adriano";
        PlayerSettings.productName="Horizonte";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,"com.adriano.testepersonagem");
        PlayerSettings.bundleVersion="0.4.1";
        PlayerSettings.Android.bundleVersionCode=7;
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
            target=BuildTarget.Android,options=BuildOptions.None});
        if(report.summary.result!=BuildResult.Succeeded)throw new Exception("APK falhou: "+report.summary.result);
        Debug.Log("ANDROID_APK_OK "+report.summary.totalSize);
    }
}
