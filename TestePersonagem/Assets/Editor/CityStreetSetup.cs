using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// A reproducible, metre-scale slice of Downtown City. Keeps the existing character and HUD.
public static class CityStreetSetup
{
    const string RootPath = "Assets/DowntownCity";
    const string ScenePath = "Assets/Scenes/TestePersonagem.unity";
    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
    static readonly List<string> report = new List<string>();
    static Transform pavement, buildings, props, limits;

    // Shared asset preparation for the larger neighbourhood; the original street builder is unchanged.
    public static Dictionary<string, Material> PrepareNeighborhoodAssets()
    {
        Directory.CreateDirectory(RootPath + "/Materials");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ConfigureImports();
        CreateMaterials();
        return new Dictionary<string, Material>(materials);
    }

    public static void PrepareNeighborhoodLighting(GameObject city)
    {
        ConfigureLighting();
        ConfigureDayNight(city);
    }

    [MenuItem("Teste/Montar Rua do Centro")]
    public static void Create()
    {
        Directory.CreateDirectory("../tools/city-review");
        Directory.CreateDirectory(RootPath + "/Materials");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ConfigureImports();
        CreateMaterials();
        EditorSceneManager.OpenScene(ScenePath);
        var player = UnityEngine.Object.FindFirstObjectByType<ThirdPersonPlayer>();
        if (!player) throw new Exception("Personagem da cena original ausente.");
        foreach (var obj in player.gameObject.scene.GetRootGameObjects())
            if (obj != player.gameObject && !obj.GetComponent<Camera>() && !obj.GetComponent<Light>())
                UnityEngine.Object.DestroyImmediate(obj);

        var city = new GameObject("Centro - Rua de teste");
        pavement = Group(city.transform, "Pavimentacao");
        buildings = Group(city.transform, "Edificios");
        props = Group(city.transform, "Mobiliario");
        limits = Group(city.transform, "Limites");
        BuildRoad();
        BuildBuildings();
        BuildStreetFurniture();
        ConfigureLighting();
        ConfigureDayNight(city);
        FitCharacter(player);

        var camera = Camera.main;
        var orbit = camera.GetComponent<ThirdPersonCamera>();
        orbit.yaw = 90; orbit.pitch = 16; orbit.distance = 4.2f;
        camera.farClipPlane = 180;
        camera.nearClipPlane = .15f;
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.transform.SetPositionAndRotation(new Vector3(-4.04f, 2.3f, 0), Quaternion.Euler(16, 90, 0));
        QualitySettings.shadowDistance = 65;
        QualitySettings.shadows = ShadowQuality.All;
        QualitySettings.shadowResolution = ShadowResolution.High;
        QualitySettings.shadowCascades = 4;
        QualitySettings.antiAliasing = 4;
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
        QualitySettings.pixelLightCount = 8;
        Physics.SyncTransforms();
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        EditorSceneManager.SaveScene(player.gameObject.scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        PrefabUtility.SaveAsPrefabAsset(player.gameObject, "Assets/Prefabs/Jogador.prefab");
        AssetDatabase.SaveAssets();
        report.Add("Cena=" + ScenePath);
        report.Add("Rua=36 m; asfalto=6 m; calcadas=3 m; meio-fio=0.15 m; terreno=48 x 48 m");
        report.Add("Predios=4; escala dos assets=1 unidade por metro; colliders estaticos no pavimento, predios e objetos");
        report.Add("Inicio noturno; ceu estrelado procedural com lua; 6 postes com luz real; alternancia dia/noite pelo HUD ou N");
        File.WriteAllLines("../tools/city-review/setup.txt", report);
        Debug.Log("CITY_SETUP_OK\n" + string.Join("\n", report));
    }

    static void ConfigureImports()
    {
        foreach (string path in AssetDatabase.FindAssets("t:Texture2D", new[] { RootPath + "/Textures" }).Select(AssetDatabase.GUIDToAssetPath))
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = path.Contains("_Normal") ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !path.Contains("_UnityMask") && !path.Contains("_Normal");
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = path.EndsWith("T_Street_Decals.png");
            importer.maxTextureSize = 2048;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
        }
        foreach (string path in AssetDatabase.FindAssets("t:Model", new[] { RootPath + "/Models" }).Select(AssetDatabase.GUIDToAssetPath))
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = true;
            importer.globalScale = 1;
            importer.addCollider = false;
            importer.SaveAndReimport();
        }
    }

    static Texture2D Texture(string name)
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(RootPath + "/Textures/" + name + ".png");
        if (!texture) throw new Exception("Textura da cidade ausente: " + name);
        return texture;
    }

    static Material Surface(string name, string textureSet, Color tint)
    {
        string path = RootPath + "/Materials/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat) { mat = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat, path); }
        mat.shader = Shader.Find("Standard");
        mat.color = tint;
        mat.SetFloat("_Glossiness", .2f);
        if (!string.IsNullOrEmpty(textureSet))
        {
            mat.mainTexture = Texture(textureSet + "_BaseColor");
            string normals = textureSet == "T_Concrete_Asphalt" ? "T_Concrete" : textureSet;
            mat.SetTexture("_BumpMap", Texture(normals + "_Normal"));
            mat.SetFloat("_BumpScale", .55f);
            mat.EnableKeyword("_NORMALMAP");
            // Packed once from ORM: R=metallic, G=occlusion, A=1-roughness (Built-in Standard).
            var mask = Texture(normals + "_UnityMask");
            mat.SetTexture("_MetallicGlossMap", mask);
            mat.SetTexture("_OcclusionMap", mask);
            mat.SetFloat("_GlossMapScale", .6f);
            mat.SetFloat("_OcclusionStrength", .7f);
            mat.EnableKeyword("_METALLICGLOSSMAP");
        }
        materials[name] = mat;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static void CreateMaterials()
    {
        materials.Clear(); report.Clear();
        Surface("MI_Asphalt", "T_Concrete_Asphalt", Color.white);
        Surface("MI_Concrete", "T_Concrete", Color.white);
        Surface("MI_Trim_MetalConcrete", "T_MetalConcrete", Color.white);
        Surface("MI_Trim", "T_Trim", Color.white);
        Surface("MI_Trim_Dark", "T_Trim", new Color(.58f, .6f, .62f));
        Surface("MI_Trim_Green", "T_Trim", new Color(.7f, .83f, .68f));
        Surface("MI_RedBrick", "T_RedBrick", Color.white);
        Surface("MI_RedBrick_Pale", "T_RedBrick", new Color(.9f, .93f, .87f));
        Surface("MI_InteriorWall", "T_RedBrick", Color.white);
        Surface("MI_InteriorFloor", "T_MarbleFloor", Color.white);
        Surface("MI_InteriorRoof", "T_Concrete", Color.white);
        Surface("MI_Ornaments", "T_Ornaments", Color.white);
        Surface("MI_Roof_Slate", "T_RoofSlate", Color.white);
        Surface("MI_Dirt", "T_Dirt", Color.white);
        for (int i = 1; i <= 4; i++)
        {
            var mat = Surface("MI_FakeInterior_" + i, null, Color.white);
            mat.mainTexture = Texture(i == 4 ? "T_dark_interior" : "T_lit_interior_" + (i == 2 ? 2 : 1));
            mat.SetFloat("_Glossiness", 0);
            mat.SetTexture("_EmissionMap", mat.mainTexture);
            mat.SetColor("_EmissionColor", new Color(.12f, .11f, .09f));
            mat.EnableKeyword("_EMISSION");
        }
        Surface("MI_FakeInterior", null, new Color(.12f, .14f, .15f));
        var glass = Surface("MI_Glass", null, new Color(.6f, .75f, .8f, .18f));
        glass.SetFloat("_Mode", 2); glass.SetFloat("_Glossiness", .7f);
        glass.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        glass.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        glass.SetInt("_ZWrite", 0); glass.EnableKeyword("_ALPHABLEND_ON");
        glass.renderQueue = 3000;
        var decals = Surface("MI_StreetDecals", null, Color.white);
        decals.mainTexture = Texture("T_Street_Decals");
        decals.SetFloat("_Mode", 1); decals.SetFloat("_Cutoff", .35f);
        decals.EnableKeyword("_ALPHATEST_ON"); decals.renderQueue = 2450;
        Surface("City_Metal", null, new Color(.12f, .15f, .16f));
        Surface("City_Leaves", null, new Color(.22f, .36f, .15f));
        Surface("City_BrickBoundary", "T_RedBrick", new Color(.72f, .74f, .7f));
        var ground = Surface("City_Ground", "T_Concrete", new Color(.87f, .87f, .84f));
        ground.mainTextureScale = new Vector2(8, 8);
        var lamp = Surface("City_Lamp", null, new Color(1, .88f, .65f));
        lamp.SetColor("_EmissionColor", new Color(.7f, .53f, .29f)); lamp.EnableKeyword("_EMISSION");
        report.Add("Materiais Standard com albedo, normais, metalicidade, oclusao e suavidade; decals com recorte alpha");
    }

    static Transform Group(Transform parent, string name)
    {
        var obj = new GameObject(name); obj.transform.SetParent(parent, false); return obj.transform;
    }

    static GameObject Model(string name, Transform parent, Vector3 position, float yaw = 0)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(RootPath + "/Models/" + name + ".fbx");
        if (!asset) throw new Exception("Modelo ausente: " + name);
        var obj = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        obj.name = name;
        obj.transform.SetParent(parent, false);
        obj.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
        foreach (Transform child in obj.GetComponentsInChildren<Transform>()) { child.gameObject.layer = 0; child.gameObject.isStatic = true; }
        foreach (var renderer in obj.GetComponentsInChildren<MeshRenderer>())
        {
            renderer.sharedMaterials = renderer.sharedMaterials.Select(m =>
            {
                string key = m.name.Replace(" (Instance)", "");
                if (!materials.TryGetValue(key, out Material material)) throw new Exception("Material sem mapeamento: " + key);
                return material;
            }).ToArray();
        }
        return obj;
    }

    static Bounds GeometryBounds(GameObject obj)
    {
        var bounds = new Bounds(); bool first = true;
        foreach (var filter in obj.GetComponentsInChildren<MeshFilter>())
            foreach (var vertex in filter.sharedMesh.vertices)
            {
                Vector3 world = filter.transform.TransformPoint(vertex);
                if (first) { bounds = new Bounds(world, Vector3.zero); first = false; } else bounds.Encapsulate(world);
            }
        return bounds;
    }

    static GameObject Box(string name, Transform parent, Vector3 center, Vector3 size, Material mat, bool collision = true)
    {
        var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = name; obj.transform.SetParent(parent, false);
        obj.transform.position = center; obj.transform.localScale = size; obj.isStatic = true;
        if (mat) obj.GetComponent<Renderer>().sharedMaterial = mat;
        else UnityEngine.Object.DestroyImmediate(obj.GetComponent<Renderer>());
        if (!collision) UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());
        return obj;
    }

    static void BuildRoad()
    {
        Box("Base solida 48m", pavement, new Vector3(0, -.45f, 0), new Vector3(48, .6f, 48), materials["MI_Asphalt"]).GetComponent<Renderer>().enabled=false;
        for (int i = 0; i < 6; i++)
            Model("Street_2Lane", pavement, new Vector3(-15 + i * 6, 0, 0));
        foreach (int side in new[] { -1, 1 })
        {
            Box("Colisao continua da calcada " + side, pavement, new Vector3(0, -.12f, side * 4.5f), new Vector3(36, .24f, 3), null);
            Box("Patio dos edificios " + side, pavement, new Vector3(0, -.15f, side * 15), new Vector3(48, .3f, 18), materials["City_Ground"]);
            Box("Muro ao fundo " + side, limits, new Vector3(0, 1, side * 24), new Vector3(48, 2, .3f), materials["City_BrickBoundary"]);
        }
        foreach (int side in new[] { -1, 1 })
        {
            Box("Passeio de fechamento " + side, pavement, new Vector3(side * 21, -.15f, 0), new Vector3(6, .3f, 12), materials["City_Ground"]);
            Box("Muro de fechamento " + side, limits, new Vector3(side * 24, 1, 0), new Vector3(.3f, 2, 48), materials["City_BrickBoundary"]);
        }
        var crossing = Model("Decal_Crosswalk", pavement, new Vector3(1, 0, 0), 90);
        var bounds = GeometryBounds(crossing);
        crossing.transform.position += new Vector3(1 - bounds.center.x, -.142f - bounds.min.y, -bounds.center.z);
        report.Add("Faixa de pedestres=" + GeometryBounds(crossing).size);
    }

    static void Building(string asset, string label, float x, int side, bool stairs)
    {
        var obj = Model(asset, buildings, Vector3.zero, side > 0 ? 180 : 0);
        obj.name = label;
        var bounds = GeometryBounds(obj);
        float front = stairs ? 4.5f : 6.35f;
        obj.transform.position += new Vector3(x - bounds.center.x, -bounds.min.y, side > 0 ? front - bounds.min.z : -front - bounds.max.z);
        bounds = GeometryBounds(obj);
        foreach (var filter in obj.GetComponentsInChildren<MeshFilter>())
        {
            var collider = filter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
        }
        if(stairs)StairSetup.ConfigureBuilding(obj);
        // A solid core prevents entry into fake interiors; stairs use a smooth collision ramp.
        float back = side > 0 ? bounds.max.z : bounds.min.z;
        float face = side * 6.45f;
        Box(label + " - volume solido", buildings,
            new Vector3(bounds.center.x, bounds.center.y, (back + face) * .5f),
            new Vector3(bounds.size.x * .97f, bounds.size.y, Mathf.Abs(back - face)), null);
        report.Add(label + " dimensoes(m)=" + bounds.size.ToString("F3") + "; escala=" + obj.transform.localScale);
    }

    static void BuildBuildings()
    {
        Building("Building_Small_1", "Edificio sul - residencial", -11.5f, -1, true);
        Building("Building_Medium_2_001", "Edificio sul - comercio", 5.5f, -1, false);
        Building("Building_Large_2", "Edificio norte - esquina", -8.5f, 1, false);
        Building("Building_Small_1", "Edificio norte - residencial", 12, 1, true);
    }

    static void BuildStreetFurniture()
    {
        foreach (int side in new[] { -1, 1 })
        {
            foreach (float x in new[] { -16.5f, 16.5f })
            {
                var planter = Model("Prop_Planter_Single", props, new Vector3(x, 0, side * 4.65f));
                var pb = GeometryBounds(planter);
                Box("Colisao floreira", props, pb.center, pb.size, null);
                Box("Arbusto", props, new Vector3(x, .7f, side * 4.65f), new Vector3(1.5f, .45f, 1.5f), materials["City_Leaves"], false);
            }
            foreach (float x in new[] { -14f, -3f, 10f }) StreetLamp(x, side * 3.55f, side);
            foreach (float x in new[] { -13f, 13f })
            {
                var drain = Model("Prop_Drain", props, new Vector3(x, -.148f, side * 2.65f));
                drain.name = "Bueiro na sarjeta";
            }
        }
        foreach (float x in new[] { -18.5f, 18.5f })
            foreach (float z in new[] { -2.4f, -1.2f, 0, 1.2f, 2.4f })
            {
                var bollard = Model("Prop_Bollard", props, new Vector3(x, 0, z));
                var bounds = GeometryBounds(bollard);
                Box("Colisao balizador", props, bounds.center, bounds.size, null);
            }
        Model("Prop_ManholeCover", props, new Vector3(-7, -.14f, 1.4f));
        Model("Prop_ManholeCover", props, new Vector3(11, -.14f, -1.4f));
        StreetSign(new Vector3(16, 0, 3.7f), 0);
    }

    static void StreetLamp(float x, float z, int side)
    {
        var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pole.name = "Poste de iluminacao"; pole.transform.SetParent(props, false);
        pole.transform.position = new Vector3(x, 2.4f, z); pole.transform.localScale = new Vector3(.1f, 2.4f, .1f);
        pole.GetComponent<Renderer>().sharedMaterial = materials["City_Metal"]; pole.isStatic = true;
        Box("Braco do poste", props, new Vector3(x, 4.75f, z - side * .4f), new Vector3(.09f, .09f, .8f), materials["City_Metal"], false);
        Box("Luminaria", props, new Vector3(x, 4.7f, z - side * .8f), new Vector3(.35f, .08f, .6f), materials["City_Lamp"], false);
        var lamp = new GameObject("Luz do poste", typeof(Light)).GetComponent<Light>();
        lamp.transform.SetParent(props, false);
        lamp.transform.SetPositionAndRotation(new Vector3(x, 4.61f, z - side * .8f), Quaternion.Euler(90, 0, 0));
        lamp.type = LightType.Spot; lamp.range = 13; lamp.spotAngle = 118; lamp.innerSpotAngle = 72;
        lamp.intensity = 1.8f; lamp.color = new Color(1, .82f, .62f);
        lamp.renderMode = LightRenderMode.ForcePixel; lamp.shadows = LightShadows.Soft;
        lamp.shadowCustomResolution = 512; lamp.shadowStrength = .8f;
        lamp.shadowBias = .025f; lamp.shadowNormalBias = .15f; lamp.shadowNearPlane = .2f;
    }

    static void StreetSign(Vector3 position, float yaw)
    {
        Box("Suporte da placa", props, position + Vector3.up * 1.25f, new Vector3(.06f, 2.5f, .06f), materials["City_Metal"]);
        var sign = Box("Placa Rua do Centro", props, position + Vector3.up * 2.35f, new Vector3(2.3f, .38f, .06f), materials["City_Metal"], false);
        sign.transform.rotation = Quaternion.Euler(0, yaw, 0);
        var text = new GameObject("Rua do Centro", typeof(TextMesh)); text.transform.SetParent(sign.transform, false);
        text.transform.localPosition = new Vector3(0, 0, -.55f);
        text.transform.localScale = new Vector3(1 / 2.3f, 1 / .38f, 1 / .06f);
        var mesh = text.GetComponent<TextMesh>();
        mesh.text = "RUA DO CENTRO"; mesh.fontSize = 48; mesh.characterSize = .055f;
        mesh.anchor = TextAnchor.MiddleCenter; mesh.alignment = TextAlignment.Center; mesh.color = new Color(.9f, .94f, .88f);
        text.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    static void ConfigureLighting()
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.64f, .71f, .82f);
        RenderSettings.ambientEquatorColor = new Color(.44f, .46f, .47f);
        RenderSettings.ambientGroundColor = new Color(.28f, .27f, .25f);
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(.7f, .77f, .82f);
        RenderSettings.fogStartDistance = 65; RenderSettings.fogEndDistance = 150;
        var sun = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).First(light => light.type == LightType.Directional);
        sun.transform.rotation = Quaternion.Euler(58, -25, 0);
        sun.type = LightType.Directional; sun.intensity = 1.12f;
        sun.color = new Color(1, .95f, .86f); sun.shadows = LightShadows.Soft;
        sun.shadowBias = .025f; sun.shadowNormalBias = .2f;
        RenderSettings.sun = sun;
        string path = RootPath + "/Materials/City_Sky.mat";
        var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!sky) { sky = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(sky, path); }
        sky.SetColor("_SkyTint", new Color(.5f, .59f, .7f));
        sky.SetColor("_GroundColor", new Color(.45f, .46f, .43f));
        sky.SetFloat("_AtmosphereThickness", .85f); sky.SetFloat("_Exposure", 1.1f);
        RenderSettings.skybox = sky;
        EditorUtility.SetDirty(sky);
    }

    static void ConfigureDayNight(GameObject city)
    {
        var environment = city.AddComponent<CityDayNight>();
        environment.startAtNight = true;
        environment.sun = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).First(light => light.type == LightType.Directional);
        var moon = new GameObject("Lua", typeof(Light)).GetComponent<Light>();
        moon.transform.SetParent(city.transform, false);
        moon.transform.rotation = Quaternion.Euler(32, -65, 0);
        moon.type = LightType.Directional; moon.color = new Color(.52f, .66f, 1);
        moon.intensity = .35f; moon.shadows = LightShadows.Soft;
        moon.shadowBias = .025f; moon.shadowNormalBias = .2f;
        environment.moon = moon;
        environment.streetLights = city.GetComponentsInChildren<Light>().Where(light => light.type == LightType.Spot).ToArray();
        environment.luminaireRenderers = city.GetComponentsInChildren<Renderer>().Where(renderer => renderer.name == "Luminaria").ToArray();
        environment.daySky = RenderSettings.skybox;
        string path = RootPath + "/Materials/City_NightSky.mat";
        var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find("City/StarryNightSky");
        if (!shader) throw new Exception("Shader de ceu estrelado ausente.");
        if (!sky) { sky = new Material(shader); AssetDatabase.CreateAsset(sky, path); }
        sky.shader = shader; sky.SetFloat("_StarIntensity", 2.4f);
        sky.SetVector("_MoonDirection", -moon.transform.forward);
        environment.nightSky = sky;
        environment.SetNight(true);
        foreach (var material in materials.Values) EditorUtility.SetDirty(material);
        EditorUtility.SetDirty(sky);
    }

    static void FitCharacter(ThirdPersonPlayer player)
    {
        var model = player.animator.gameObject;
        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/IdleRetarget.anim");
        if (idle) idle.SampleAnimation(model, 0);
        var skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
        Bounds bounds = CharacterGeometry.GetWorldBounds(skin);
        float originalHeight = bounds.size.y;
        if (originalHeight < .5f || originalHeight > 4) throw new Exception("Altura do personagem inesperada: " + originalHeight);
        model.transform.localScale *= 1.8f / originalHeight;
        bounds = CharacterGeometry.GetWorldBounds(skin);
        model.transform.localPosition += Vector3.up * (player.transform.position.y + .01f - bounds.min.y);
        bounds = CharacterGeometry.GetWorldBounds(skin);
        var controller = player.GetComponent<CharacterController>();
        controller.height = 1.8f; controller.center = new Vector3(0, .91f, 0);
        controller.radius = .26f; controller.stepOffset = .25f; controller.skinWidth = .035f;
        player.spawnPosition = new Vector3(0, -.10f, 0); player.spawnYaw = 90;
        controller.enabled = false;
        player.transform.SetPositionAndRotation(player.spawnPosition, Quaternion.Euler(0, player.spawnYaw, 0));
        controller.enabled = true;
        report.Add("Altura visual antes(m)=" + originalHeight.ToString("F4") + "; altura visual final medida(m)=" + bounds.size.y.ToString("F4") + "; escala uniforme do modelo=" + model.transform.localScale.ToString("F4"));
        report.Add("Controller=1.80 m, raio=.26 m, degrau=.25 m; spawn=" + player.spawnPosition);
    }

    public static void CreateAndRender()
    {
        Create(); RenderPreview();
    }

    public static void CreateRenderAndBuild()
    {
        CreateAndRender();
        HudSetup.Prepare();
        PrototypeSetup.BuildWindows();
    }

    public static void RenderPreview()
    {
        var camera = Camera.main;
        Render(camera, "editor-street", new Vector3(-4.04f, 2.2f, 0), new Vector3(4, 1.4f, 0));
        Render(camera, "editor-diagonal", new Vector3(-13, 4, -2), new Vector3(2, 7, 5));
        Render(camera, "editor-overview", new Vector3(34, 42, -35), new Vector3(0, 6, 0));
        Debug.Log("CITY_PREVIEW_OK");
    }

    static void Render(Camera camera, string name, Vector3 position, Vector3 target)
    {
        var oldPosition = camera.transform.position; var oldRotation = camera.transform.rotation;
        camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
        var rt = new RenderTexture(1280, 720, 24);
        camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
        var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
        File.WriteAllBytes("../tools/city-review/" + name + ".png", texture.EncodeToPNG());
        camera.targetTexture = null; RenderTexture.active = null;
        UnityEngine.Object.DestroyImmediate(texture); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
        camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
    }
}
