using UnityEngine;

public static class MobilePerformance
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void Configure()
    {
        if(!Application.isMobilePlatform)return;
        QualitySettings.vSyncCount=0;
        Application.targetFrameRate=60;
        QualitySettings.antiAliasing=2;
        QualitySettings.shadowDistance=30;
        QualitySettings.shadowResolution=ShadowResolution.Low;
        QualitySettings.shadowCascades=0;
        QualitySettings.pixelLightCount=2;
        foreach(var light in Object.FindObjectsByType<Light>())
            if(light.type==LightType.Point || light.type==LightType.Spot)
                light.shadows=LightShadows.None;
    }
}
