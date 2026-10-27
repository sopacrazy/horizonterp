using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

// Opt-in standalone check; never changes normal gameplay or the saved scene.
public sealed class CityLightingVerification : MonoBehaviour
{
    const string OutputFolder = @"C:\SistemaAdr\gta\tools\night-review";
    readonly List<string> checks = new List<string>();
    readonly List<string> errors = new List<string>();
    readonly List<Measurement> measurements = new List<Measurement>();
    CityDayNight lighting;
    ThirdPersonPlayer player;
    ThirdPersonCamera orbit;
    GameHud hud;
    Vector3 initialPlayerPosition;
    float initialPlayerHeight;
    bool finished;

    [Serializable]
    sealed class Measurement
    {
        public string name;
        public float value;
    }

    [Serializable]
    sealed class Report
    {
        public string capturedUtc;
        public string unityVersion;
        public string graphicsDevice;
        public bool passed;
        public List<string> checks;
        public List<string> errors;
        public List<Measurement> measurements;
    }

    struct ImageStats
    {
        public float mean;
        public float variance;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-lighting") >= 0)
            new GameObject("City Lighting Verification").AddComponent<CityLightingVerification>();
    }

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        Directory.CreateDirectory(OutputFolder);
        Application.logMessageReceived += OnLog;
        Application.runInBackground = true;
    }

    void OnLog(string message, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors.Add(message + "\n" + trace);
    }

    IEnumerator Start()
    {
        while(StartupLoader.IsLoading)yield return null;
        var pending = new Stack<IEnumerator>();
        pending.Push(Verify());
        while (pending.Count > 0 && !finished)
        {
            object instruction = null;
            bool moved = false;
            Exception failure = null;
            try
            {
                moved = pending.Peek().MoveNext();
                if (moved) instruction = pending.Peek().Current;
            }
            catch (Exception exception) { failure = exception; }
            if (failure != null) { errors.Add(failure.ToString()); Finish(); yield break; }
            if (!moved) { pending.Pop(); continue; }
            if (instruction is IEnumerator nested) pending.Push(nested);
            else yield return instruction;
        }
        if (!finished) Finish();
    }

    IEnumerator Verify()
    {
        Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
        yield return new WaitForSecondsRealtime(1.5f);
        lighting = FindAnyObjectByType<CityDayNight>();
        player = FindAnyObjectByType<ThirdPersonPlayer>();
        orbit = FindAnyObjectByType<ThirdPersonCamera>();
        hud = GameHud.Instance;
        bool ready = Check(lighting && lighting.isActiveAndEnabled, "Controlador de dia e noite ativo") &
                     Check(player && orbit && hud != null, "Personagem, câmera e HUD disponíveis");
        if (!ready) yield break;
        player.DemoMode = true;
        player.DemoInput = Vector2.zero;
        player.DemoRun = false;
        hud.ClosePanel();
        hud.SetVisible(true);
        if (!Check(hud.Root.Q<Button>("action-day-night")==null, "Botão de dia e noite removido do HUD")) yield break;
        orbit.ResetView();
        orbit.yaw = 90;
        orbit.pitch = 14;
        orbit.distance = 4.2f;
        yield return new WaitForSecondsRealtime(.6f);

        initialPlayerPosition = player.transform.position;
        initialPlayerHeight = PlayerHeight();
        Measure("initialPlayerHeightM", initialPlayerHeight);
        Check(lighting.IsNight, "Cena inicia no modo noturno");
        ValidateNightSky();
        ValidateNightLights("inicial");
        var nightEmission = ReadLuminaireEmission();
        Check(nightEmission.Count > 0 && AllAbove(nightEmission, .05f), "Luminárias têm emissão visível à noite");
        CheckPlayer("à noite");
        yield return Capture("night-street");

        orbit.pitch = -10;
        yield return new WaitForSecondsRealtime(.5f);
        Check(orbit.pitch == -10, "Câmera inclinada para incluir céu estrelado na captura");
        yield return Capture("night-sky");
        orbit.pitch = 14;
        yield return new WaitForSecondsRealtime(.4f);

        yield return OpenAndInspectMap("noite", "lighting-map");
        hud.ClosePanel();
        yield return null;
        SubmitToggle();
        yield return new WaitForSecondsRealtime(.4f);
        Check(!lighting.IsNight, "Controle de iluminação alterna da noite para o dia");
        Check(RenderSettings.skybox == lighting.daySky && ValidShader(lighting.daySky), "Modo diurno aplica céu válido");
        Check(lighting.daySky.shader.name=="Skybox/Panoramic"&&lighting.daySky.HasProperty("_MainTex")&&
              lighting.daySky.GetTexture("_MainTex")!=null,
            "Modo diurno usa panorama com nuvens de fim de tarde");
        Check(lighting.sun && lighting.sun.enabled && lighting.sun.intensity > 0 &&
              lighting.sun.color.r>lighting.sun.color.b&&lighting.moon && !lighting.moon.enabled,
            "Tarde ativa o sol direcional quente e desativa a iluminação da lua");
        Check(StreetLightsOff(), "Postes apagam no modo diurno");
        var dayEmission = ReadLuminaireEmission();
        Check(EmissionReduced(nightEmission, dayEmission), "Emissão das luminárias apaga ou reduz no modo diurno");
        float dayDirectional = EffectiveIntensity(lighting.sun) + EffectiveIntensity(lighting.moon);
        Measure("dayDirectionalIntensity", dayDirectional);
        CheckPlayer("depois de alternar para dia");
        yield return Capture("day-street");
        yield return OpenAndInspectMap("dia", null);
        hud.ClosePanel();
        yield return null;

        SubmitToggle();
        yield return new WaitForSecondsRealtime(.4f);
        Check(lighting.IsNight, "Controle de iluminação alterna do dia para a noite");
        ValidateNightLights("após retorno");
        float nightDirectional = EffectiveIntensity(lighting.sun) + EffectiveIntensity(lighting.moon);
        Measure("nightDirectionalIntensity", nightDirectional);
        Check(Mathf.Abs(dayDirectional - nightDirectional) > .05f, "Intensidade direcional muda entre dia e noite");
        Check(EmissionRestored(nightEmission, ReadLuminaireEmission()), "Retorno à noite restaura emissão das luminárias");
        CheckPlayer("depois de retornar à noite");

        // Both states, HUD labels, map cameras and screenshots are warmed before comparing resources.
        HashSet<Light> initialLights = SceneLights();
        HashSet<Material> initialMaterials = LoadedMaterials();
        Measure("lightsBeforeRepeatedToggle", initialLights.Count);
        Measure("materialsBeforeRepeatedToggle", initialMaterials.Count);
        for (int cycle = 1; cycle <= 5; cycle++)
        {
            SubmitToggle();
            yield return new WaitForSecondsRealtime(.15f);
            bool dayPassed = !lighting.IsNight && StreetLightsOff() &&
                             EmissionReduced(nightEmission, ReadLuminaireEmission());
            SubmitToggle();
            yield return new WaitForSecondsRealtime(.15f);
            bool nightPassed = lighting.IsNight && StreetLightsOn() &&
                               EmissionRestored(nightEmission, ReadLuminaireEmission());
            Check(dayPassed && nightPassed, "Ciclo " + cycle + ": iluminação alterna postes e emissão nos dois sentidos");
        }
        HashSet<Light> finalLights = SceneLights();
        HashSet<Material> finalMaterials = LoadedMaterials();
        Measure("lightsAfterRepeatedToggle", finalLights.Count);
        Measure("materialsAfterRepeatedToggle", finalMaterials.Count);
        Check(initialLights.SetEquals(finalLights), "Cinco ciclos preservam as mesmas luzes sem duplicação");
        Check(finalMaterials.IsSubsetOf(initialMaterials), "Cinco ciclos não criam materiais adicionais");
        CheckPlayer("depois de cinco ciclos completos");
        ValidateNightSky();
        orbit.ResetView();
        orbit.yaw = 90;
        orbit.pitch = 14;
        orbit.distance = 4.2f;
        yield return new WaitForSecondsRealtime(.4f);
        Check(errors.Count == 0, "Execução sem erros, exceções ou asserts do Unity");
    }

    void ValidateNightSky()
    {
        Material sky = lighting.nightSky;
        Check(sky && RenderSettings.skybox == sky && ValidShader(sky) &&
              sky.shader.name == "City/StarryNightSky" && sky.HasProperty("_StarIntensity") &&
              sky.GetFloat("_StarIntensity") > 0 && sky.HasProperty("_MoonDirection"),
            "Noite aplica shader de céu estrelado válido com estrelas e direção da lua");
        if (sky && sky.HasProperty("_StarIntensity")) Measure("nightStarIntensity", sky.GetFloat("_StarIntensity"));
        Check(Camera.main && Camera.main.clearFlags == CameraClearFlags.Skybox, "Câmera principal renderiza o skybox");
    }

    void ValidateNightLights(string stage)
    {
        Check(StreetLightsOn(), "Pelo menos quatro postes acesos à noite (" + stage + ")");
        Check(lighting.moon && lighting.moon.type == LightType.Directional && lighting.moon.enabled &&
              lighting.moon.intensity > 0 && lighting.sun && !lighting.sun.enabled,
            "Noite ativa luz direcional da lua e desativa o sol (" + stage + ")");
    }

    bool StreetLightsOn()
    {
        if (lighting.streetLights == null || lighting.streetLights.Length < 4) return false;
        var distinctLights = new HashSet<Light>();
        foreach (Light light in lighting.streetLights)
        {
            if (!light || !light.gameObject.activeInHierarchy || !light.enabled || light.intensity <= 0) return false;
            distinctLights.Add(light);
        }
        return distinctLights.Count >= 4;
    }

    bool StreetLightsOff()
    {
        if (lighting.streetLights == null || lighting.streetLights.Length < 4) return false;
        foreach (Light light in lighting.streetLights)
            if (!light || light.enabled && light.intensity > .001f) return false;
        return true;
    }

    Dictionary<Material, float> ReadLuminaireEmission()
    {
        var result = new Dictionary<Material, float>();
        if (lighting.luminaireRenderers == null) return result;
        foreach (Renderer renderer in lighting.luminaireRenderers)
        {
            if (!renderer) continue;
            foreach (Material material in renderer.sharedMaterials)
            {
                if (!material || !material.HasProperty("_EmissionColor")) continue;
                Color emission = material.GetColor("_EmissionColor");
                result[material] = material.IsKeywordEnabled("_EMISSION") ?
                    Mathf.Max(emission.r, Mathf.Max(emission.g, emission.b)) : 0;
            }
        }
        return result;
    }

    static bool AllAbove(Dictionary<Material, float> values, float minimum)
    {
        foreach (float value in values.Values) if (value <= minimum) return false;
        return values.Count > 0;
    }

    static bool EmissionReduced(Dictionary<Material, float> night, Dictionary<Material, float> day)
    {
        if (night.Count == 0 || night.Count != day.Count) return false;
        foreach (var item in night)
            if (!day.TryGetValue(item.Key, out float value) || value > item.Value * .15f + .001f) return false;
        return true;
    }

    static bool EmissionRestored(Dictionary<Material, float> original, Dictionary<Material, float> current)
    {
        if (original.Count == 0 || original.Count != current.Count) return false;
        foreach (var item in original)
            if (!current.TryGetValue(item.Key, out float value) || Mathf.Abs(value - item.Value) > .01f) return false;
        return true;
    }

    void SubmitToggle()
    {
        hud.TriggerAction("day-night");
    }

    IEnumerator OpenAndInspectMap(string state, string capture)
    {
        hud.OpenPanel("map");
        yield return new WaitForSecondsRealtime(.25f);
        hud.RefreshMaps();
        yield return new WaitForEndOfFrame();
        var minimap = hud.Root.Q("minimap-card")?.Q<Image>(className: "mini-image");
        var map = hud.Root.Q(className: "map-area")?.Q<Image>(className: "mini-image");
        ImageStats miniStats = MapStats(minimap?.image as RenderTexture);
        ImageStats mapStats = MapStats(map?.image as RenderTexture);
        Measure(state + "MinimapMean", miniStats.mean);
        Measure(state + "MapMean", mapStats.mean);
        Check(hud.CurrentPanel == "map" && miniStats.mean > .001f && miniStats.variance > .000002f &&
              mapStats.mean > .001f && mapStats.variance > .000002f,
            "Minimapa e mapa ampliado renderizam geometria no modo " + state);
        if (!string.IsNullOrEmpty(capture)) yield return Capture(capture);
    }

    ImageStats MapStats(RenderTexture target)
    {
        if (!target || !target.IsCreated()) return default;
        RenderTexture previous = RenderTexture.active;
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        try
        {
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            texture.Apply();
            return PixelStats(texture.GetPixels32());
        }
        finally { RenderTexture.active = previous; Destroy(texture); }
    }

    static ImageStats PixelStats(Color32[] pixels)
    {
        double sum = 0, squareSum = 0;
        int count = 0;
        for (int i = 0; i < pixels.Length; i += 29)
        {
            Color32 pixel = pixels[i];
            float value = (pixel.r + pixel.g + pixel.b) / (3f * 255f);
            sum += value; squareSum += value * value; count++;
        }
        if (count == 0) return default;
        double mean = sum / count;
        return new ImageStats { mean = (float)mean, variance = (float)(squareSum / count - mean * mean) };
    }

    void CheckPlayer(string stage)
    {
        float height = PlayerHeight();
        Measure("playerHeightM " + stage, height);
        Check(Vector3.Distance(player.transform.position, initialPlayerPosition) < .025f &&
              Mathf.Abs(height - initialPlayerHeight) < .025f && height >= 1.77f && height <= 1.83f &&
              player.GetComponent<CharacterController>().isGrounded,
            "Posição, altura e apoio do personagem preservados " + stage);
    }

    float PlayerHeight()
    {
        Bounds bounds = default;
        bool found = false;
        foreach (SkinnedMeshRenderer skin in player.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!skin.enabled || !skin.sharedMesh) continue;
            Bounds current = CharacterGeometry.GetWorldBounds(skin);
            if (!found) { bounds = current; found = true; }
            else bounds.Encapsulate(current);
        }
        return found ? bounds.size.y : 0;
    }

    static HashSet<Light> SceneLights()
    {
        var result = new HashSet<Light>();
        foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Include))
            result.Add(light);
        return result;
    }

    static HashSet<Material> LoadedMaterials()
    {
        var result = new HashSet<Material>();
        foreach (Material material in Resources.FindObjectsOfTypeAll<Material>()) result.Add(material);
        return result;
    }

    static float EffectiveIntensity(Light light) { return light && light.enabled ? light.intensity : 0; }
    static bool ValidShader(Material material)
    {
        return material && material.shader && material.shader.isSupported && !material.shader.name.Contains("InternalErrorShader");
    }

    IEnumerator Capture(string name)
    {
        string path = Path.Combine(OutputFolder, name + ".png");
        if (File.Exists(path)) File.Delete(path);
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(path);
        float deadline = Time.unscaledTime + 4f;
        long previousLength = -1;
        int stableFrames = 0;
        while (stableFrames < 2 && Time.unscaledTime < deadline)
        {
            long length = File.Exists(path) ? new FileInfo(path).Length : 0;
            stableFrames = length > 2000 && length == previousLength ? stableFrames + 1 : 0;
            previousLength = length;
            yield return null;
        }
        if (!Check(stableFrames >= 2, "Captura gravada: " + name)) yield break;
        var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        bool decoded = texture.LoadImage(File.ReadAllBytes(path));
        ImageStats stats = decoded ? PixelStats(texture.GetPixels32()) : default;
        Destroy(texture);
        Measure(name + "ScreenshotMean", stats.mean);
        Check(decoded && stats.mean > .003f && stats.variance > .00005f,
            "Captura contém imagem renderizada, sem tela preta: " + name);
    }

    bool Check(bool passed, string description)
    {
        checks.Add((passed ? "OK: " : "FAIL: ") + description);
        if (!passed) errors.Add(description);
        return passed;
    }

    void Measure(string name, float value) { measurements.Add(new Measurement { name = name, value = value }); }

    void Finish()
    {
        if (finished) return;
        finished = true;
        Application.logMessageReceived -= OnLog;
        if (player) { player.DemoInput = Vector2.zero; player.DemoRun = false; player.DemoMode = false; }
        var report = new Report
        {
            capturedUtc = DateTime.UtcNow.ToString("o"), unityVersion = Application.unityVersion,
            graphicsDevice = SystemInfo.graphicsDeviceName, passed = errors.Count == 0,
            checks = checks, errors = errors, measurements = measurements
        };
        File.WriteAllText(Path.Combine(OutputFolder, "validation.json"), JsonUtility.ToJson(report, true));
        File.WriteAllText(Path.Combine(OutputFolder, "validation.txt"), string.Join("\n", checks) +
            "\nERRORS=" + errors.Count + "\n" + string.Join("\n", errors));
        Application.Quit(errors.Count == 0 ? 0 : 1);
    }

    void OnDestroy() { Application.logMessageReceived -= OnLog; }
}
