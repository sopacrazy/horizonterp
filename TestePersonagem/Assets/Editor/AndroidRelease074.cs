using UnityEditor;
using UnityEngine;

// Build from the synchronized, warmed Los Santos project without reimporting
// or reducing the quality of the unchanged city assets.
public static class AndroidRelease074
{
    public static void Build()
    {
        GameVersionBuildSetup.Apply();
        RifleSetup.Prepare();
        RifleSocketCalibration.Prepare();
        FemaleCharacterVerification.Verify();
        BetaUidLoginVerification.Run();
        RifleVerification.Run();
        RifleGaitVerification.Run();
        RifleMotionReview.Run();
        RifleNetworkVerification.Verify();
        LosSantosAndroidSetup.Prepare();
        Hud2Setup.Prepare();
        AssetDatabase.SaveAssets();
        Debug.Log("ANDROID_074_PREFLIGHT_OK female=true namedLogin=true rifle=true normalGait=true losSantos=true");
        AndroidSetup.Build();
    }
}
