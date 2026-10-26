using UnityEngine;

// Scene-local quality and recovery rules for the official city.
public sealed class GtaCityEnvironment : MonoBehaviour
{
    public CityDayNight lighting;
    public ThirdPersonPlayer player;
    void Awake()
    {
        QualitySettings.shadowDistance = 90;
        QualitySettings.shadowCascades = 2;
        QualitySettings.antiAliasing = Application.isMobilePlatform ? 2 : 4;
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
        ConfigureSurfaces(transform);
    }
    public static void ConfigureSurfaces(Transform root)
    {
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
        {
            bool reconstructed = false;
            foreach (var material in renderer.sharedMaterials)
                if (material && material.shader && material.shader.name == "Horizonte/Recovered City Surface") { reconstructed = true; break; }
            if (!reconstructed) continue;
            uint seed = 2166136261;
            unchecked { foreach (char c in renderer.name) seed = (seed ^ c) * 16777619; }
            var block = new MaterialPropertyBlock();
            block.SetFloat("_BuildingBaseY", renderer.bounds.min.y);
            block.SetFloat("_Seed", (seed % 1000) / 1000f);
            renderer.SetPropertyBlock(block);
        }
    }
    void Update()
    {
        Shader.SetGlobalFloat("_RecoveredCityNight", lighting && lighting.IsNight ? 1 : 0);
        if (!player || !NeighborhoodMap.Active || StartupLoader.IsLoading || AccountSession.InputBlocked) return;
        // The official city's terrain is not flat: do not use the former -10 m fall limit.
        var bounds = NeighborhoodMap.Active;
        if (!bounds.Contains(player.transform.position)) player.ResetPosition();
    }
    void OnDestroy() { Shader.SetGlobalFloat("_RecoveredCityNight", 0); }
}
