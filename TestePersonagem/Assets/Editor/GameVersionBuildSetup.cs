using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Every build, including one started from the Editor UI, uses the same version.
public sealed class GameVersionBuildSetup : IPreprocessBuildWithReport
{
    public int callbackOrder => -1000;

    public static void Apply()
    {
        var version=GameVersion.Current;
        if(version==null || string.IsNullOrEmpty(version.version) ||
            !Regex.IsMatch(version.version,@"^\d+\.\d+\.\d+$") ||
            version.versionCode<=0 || string.IsNullOrWhiteSpace(version.packageId))
            throw new BuildFailedException("Configuração GameVersion.json inválida.");

        PlayerSettings.bundleVersion=version.version;
        PlayerSettings.Android.bundleVersionCode=version.versionCode;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,version.packageId);
        Debug.Log("GAME_VERSION_APPLIED version="+version.version+" code="+version.versionCode);
    }

    public void OnPreprocessBuild(BuildReport report) { Apply(); }
}
