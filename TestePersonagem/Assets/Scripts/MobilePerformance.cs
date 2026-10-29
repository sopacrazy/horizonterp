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
        if(Object.FindAnyObjectByType<GtaSaSectorStreaming>())
        {
            Application.targetFrameRate=30;
            QualitySettings.antiAliasing=0;
            QualitySettings.shadowDistance=20;
            QualitySettings.shadows=ShadowQuality.HardOnly;
            QualitySettings.pixelLightCount=1;
            if(Camera.main)Camera.main.farClipPlane=320;
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;
            RenderSettings.fogStartDistance=130;RenderSettings.fogEndDistance=300;
        }
    }
}
