using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class CityDayNight : MonoBehaviour
{
    public bool startAtNight = true;
    public Light sun;
    public Light moon;
    public Light[] streetLights = Array.Empty<Light>();
    public Renderer[] luminaireRenderers = Array.Empty<Renderer>();
    public Material daySky;
    public Material nightSky;
    [SerializeField] bool isNight = true;
    public bool IsNight => isNight;

    readonly List<Material> interiors = new List<Material>();
    readonly List<Material> luminaires = new List<Material>();
    readonly List<Material> runtimeMaterials = new List<Material>();
    bool materialsReady;

    void Awake()
    {
        var afternoonSky = Resources.Load<Material>("Environment/LateAfternoonSkyMaterial");
        if (afternoonSky) daySky = afternoonSky;
        QualitySettings.pixelLightCount = Mathf.Max(8, QualitySettings.pixelLightCount);
        SetNight(startAtNight);
    }

    public void Toggle() => SetNight(!isNight);

    public void SetNight(bool night)
    {
        PrepareMaterials();
        isNight = night;
        RenderSettings.skybox = night ? nightSky : daySky;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientIntensity = 1;
        // A visible ambient floor keeps unlit sidewalks and the player readable on mobile.
        // The sky stays dark; warm local lights provide the street-light contrast.
        RenderSettings.ambientSkyColor = night ? new Color(.34f, .38f, .49f) : new Color(.59f, .67f, .78f);
        RenderSettings.ambientEquatorColor = night ? new Color(.25f, .28f, .34f) : new Color(.62f, .58f, .52f);
        RenderSettings.ambientGroundColor = night ? new Color(.16f, .18f, .22f) : new Color(.32f, .29f, .26f);
        RenderSettings.fogColor = night ? new Color(.055f, .075f, .115f) : new Color(.68f, .72f, .76f);
        RenderSettings.reflectionIntensity = night ? .48f : .76f;
        if (sun) { sun.enabled = !night; sun.intensity = 1f; sun.color = new Color(1f,.88f,.72f); }
        if (moon) { moon.enabled = night; moon.intensity = .6f; moon.color = new Color(.68f, .75f, .88f); }
        RenderSettings.sun = night ? moon : sun;
        foreach (var light in streetLights) if (light) light.enabled = night;
        foreach (var material in interiors)
        {
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            material.EnableKeyword("_EMISSION");
            bool darkRoom = material.name.StartsWith("MI_FakeInterior_4", StringComparison.Ordinal);
            material.SetColor("_EmissionColor", night
                ? (darkRoom ? new Color(.08f, .095f, .13f) : new Color(1.15f, .91f, .64f))
                : new Color(.035f, .033f, .03f));
        }
        foreach (var material in luminaires)
        {
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", night ? new Color(3.2f, 2.35f, 1.25f) : new Color(.035f, .03f, .02f));
            material.color = night ? new Color(1, .91f, .73f) : new Color(.8f, .79f, .73f);
        }
        if (Application.isPlaying) DynamicGI.UpdateEnvironment();
    }

    void PrepareMaterials()
    {
        if (materialsReady) return;
        var copies = new Dictionary<Material, Material>();
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            var assigned = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < assigned.Length; i++)
            {
                var source = assigned[i];
                if (!source) continue;
                bool interior = source.name.StartsWith("MI_FakeInterior_", StringComparison.Ordinal);
                bool fixture = source.name.StartsWith("City_Lamp", StringComparison.Ordinal);
                if (!interior && !fixture) continue;
                if (!copies.TryGetValue(source, out Material material))
                {
                    material = source;
                    if (Application.isPlaying)
                    {
                        material = new Material(source) { name = source.name + " (Runtime)" };
                        runtimeMaterials.Add(material);
                    }
                    copies.Add(source, material);
                    (interior ? interiors : luminaires).Add(material);
                }
                assigned[i] = material;
                changed |= Application.isPlaying;
            }
            if (changed) renderer.sharedMaterials = assigned;
        }
        materialsReady = true;
    }

    void OnDestroy()
    {
        foreach (var material in runtimeMaterials) if (material) Destroy(material);
    }
}
