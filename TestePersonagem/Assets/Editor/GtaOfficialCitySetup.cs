using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class GtaOfficialCitySetup
{
    [MenuItem("Horizonte/Cidade oficial/Aplicar cidade reconstruida")]
    public static void Apply()
    {
        const string official = "Assets/Scenes/TestePersonagem.unity";
        Directory.CreateDirectory("../tools/gta-map-review/official-backup");
        string backup = "../tools/gta-map-review/official-backup/TestePersonagem-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity";
        File.Copy(official, backup, false);
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/GTAMapaTeste.unity");
        var preview = Object.FindAnyObjectByType<GtaRecoveredPreview>();
        if (preview) Object.DestroyImmediate(preview);
        if (Object.FindAnyObjectByType<GtaRecoveredPreview>()) throw new Exception("Transporte de teste permaneceu na cidade oficial.");
        var city = GameObject.Find("Mapa GTA recuperado");
        if (!city) throw new Exception("Cidade corrigida ausente.");
        city.name = "Cidade Horizonte - Mapa Oficial";
        var bounds = city.AddComponent<NeighborhoodMap>();
        bounds.halfExtent = 1001; bounds.groundLevel = 0; bounds.minY = -80; bounds.maxY = 100;
        bounds.spawnPosition = new Vector3(0, .08f, 0); bounds.mapCameraHeight = 160;
        bounds.isOfficialCity = true; bounds.worldCode = "centro_teste"; bounds.displayName = "Cidade Horizonte";
        var dayNight = city.AddComponent<CityDayNight>();
        dayNight.startAtNight = false;
        dayNight.sun = RenderSettings.sun;
        dayNight.daySky = RenderSettings.skybox;
        var moon = new GameObject("Luz da lua", typeof(Light)).GetComponent<Light>();
        moon.transform.SetParent(city.transform); moon.transform.rotation = Quaternion.Euler(55, 120, 0);
        moon.type = LightType.Directional; moon.shadows = LightShadows.Soft; moon.enabled = false;
        dayNight.moon = moon;
        const string skyPath = "Assets/GTARecoveredMap/Restored/CityNightSky.mat";
        var nightSky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
        if (!nightSky) { nightSky = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(nightSky, skyPath); }
        nightSky.SetFloat("_SunDisk", 0); nightSky.SetFloat("_Exposure", .13f);
        nightSky.SetColor("_SkyTint", new Color(.12f, .18f, .31f));
        dayNight.nightSky = nightSky;
        dayNight.SetNight(false);
        var movement = Object.FindAnyObjectByType<ThirdPersonPlayer>();
        var environment = city.AddComponent<GtaCityEnvironment>();
        environment.player = movement; environment.lighting = dayNight;
        EditorSceneManager.SaveScene(scene, official);
        EditorBuildSettings.scenes = GameUpdateSetup.Scenes.Select(s => new EditorBuildSettingsScene(s, true)).ToArray();
        AssetDatabase.SaveAssets();
        File.WriteAllText("../tools/gta-map-review/official-integration.txt", "Official scene=" + official + "\nBackup=" + Path.GetFullPath(backup) + "\nWorld=centro_teste\nLogin and multiplayer transports unchanged\nBounds X/Z +/-1000, Y -80..100\nTest transport components=0\n");
        Debug.Log("OFFICIAL_CITY_APPLIED");
    }

    public static void CreateAndBuildWindows()
    {
        GtaRecoveredTestSetup.CreateAndBuildWindows();
        Apply();
        PrototypeSetup.BuildWindows();
    }
}
