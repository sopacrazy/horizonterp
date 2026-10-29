using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Generates an independent, editable gallery from the original interior IPL placements.
// This editor file is installed under Assets/Editor only after exterior QA has finished.
public static class GtaSaInteriorGallerySetup
{
    const string Manifest = "../tools/gta-sa-import/output/placements.jsonl";
    const string Catalog = "../tools/gta-sa-import/output/interior-catalog-candidates.json";
    const string AlphaList = "../tools/gta-sa-import/output/full-alpha-textures.txt";
    const string AssetsRoot = "Assets/GTASanAndreasModFull";
    const string Models = AssetsRoot + "/ImportedModels";
    const string Materials = AssetsRoot + "/Materials";
    const string Colliders = AssetsRoot + "/Colliders";
    const string SceneFolder = "Assets/Scenes/GTASanAndreasModInteriors";
    const string MainScene = "Assets/Scenes/GTASanAndreasInterioresTeste.unity";
    const string ReportFolder = "../tools/gta-sa-import/review/interiors";
    const string BuildExe = "Builds/GTASanAndreasInterioresTeste/MapaSAInteriores.exe";
    static readonly Quaternion Basis = Quaternion.Euler(-90, 0, 0);
    static readonly string[] Protected = {
        "Assets/Scenes/TestePersonagem.unity", "Assets/Scenes/GTAMapaTeste.unity",
        "Assets/Scenes/GTABairroProntoTeste.unity", "Assets/Prefabs/Jogador.prefab",
        "Assets/Scenes/GTASanAndreasModTeste.unity", "Assets/Scenes/GTASanAndreasModFullTeste.unity",
        "ProjectSettings/EditorBuildSettings.asset", "ProjectSettings/QualitySettings.asset"
    };
    static readonly Dictionary<string, GameObject> ModelCache =
        new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, Material[]> MaterialCache =
        new Dictionary<string, Material[]>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, string[]> MaterialNames =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, ColliderSidecar> CollisionCache =
        new Dictionary<string, ColliderSidecar>(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> MissingTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    static HashSet<string> AlphaTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    [Serializable] sealed class Placement
    {
        public int id, interior, source_index;
        public string scene_layer, model_name, source_ipl;
        public float x,y,z,qx,qy,qz,qw;
    }
    [Serializable] sealed class MaterialSidecar { public string txd_name; public MaterialSlot[] materials; }
    [Serializable] sealed class MaterialSlot
    {
        public string material;
        public string[] textures;
        public float[] color;
    }
    [Serializable] sealed class ColliderSidecar
    {
        public ColliderBox[] boxes;
        public ColliderSphere[] spheres;
    }
    [Serializable] sealed class ColliderBox { public float[] center, size; }
    [Serializable] sealed class ColliderSphere { public float[] center; public float radius; }
    [Serializable] sealed class CatalogFile { public CatalogEntry[] entries; }
    [Serializable] sealed class CatalogEntry
    {
        public int interior_id, instances;
        public string catalog_key;
        public bool selectable;
        public float[] center_xyz;
    }
    [Serializable] sealed class InteriorReportEntry
    {
        public int id, sourcePlacements, placedInstances, collisionShapes, hiddenLods;
        public string scene;
    }
    [Serializable] sealed class SetupReport
    {
        public int sourcePlacements, placedInstances, uniqueModels, interiorScenes, catalogDestinations;
        public int rawInterior274, collisionShapes, hiddenLods, missingTextureSlots;
        public bool officialUnchanged;
        public string mainScene, manifest;
        public InteriorReportEntry[] interiors;
        public string[] missingTextures;
    }

    [MenuItem("Horizonte/GTA San Andreas modificado/Criar galeria separada de interiores")]
    public static void Create()
    {
        if (!File.Exists(Manifest) || !File.Exists(Catalog))
            throw new FileNotFoundException("Manifesto ou catálogo dos interiores ausente.");
        var before = Protected.Where(File.Exists).ToDictionary(path => path, Hash);
        Directory.CreateDirectory(SceneFolder);
        Directory.CreateDirectory(Materials);
        Directory.CreateDirectory(ReportFolder);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ModelCache.Clear(); MaterialCache.Clear(); MaterialNames.Clear(); CollisionCache.Clear(); MissingTextures.Clear();
        AlphaTextures = File.Exists(AlphaList)
            ? new HashSet<string>(File.ReadLines(AlphaList).Select(s => s.Trim().Replace('\\', '/'))
                .Where(s => s.Length != 0), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var placements = File.ReadLines(Manifest).Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(line => JsonUtility.FromJson<Placement>(line))
            .Where(p => p != null && p.scene_layer == "interior" && !string.IsNullOrEmpty(p.model_name)).ToArray();
        var grouped = placements.GroupBy(p => p.interior & 255)
            .OrderBy(group => group.Key).ToArray();
        if (placements.Length != 5666 || grouped.Length != 18)
            throw new InvalidDataException("Manifesto interior inesperado: " + placements.Length + " objetos, " + grouped.Length + " IDs.");
        var missing = placements.Where(p => !Model(p.model_name)).ToArray();
        if (missing.Length != 0)
            throw new InvalidDataException("OBJ interior ausente: " + missing.Length + "; exemplo " + missing[0].model_name);

        var visibleModels = placements.Where(p => !IsLod(p.model_name))
            .Select(p => p.model_name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        for (int start = 0; start < visibleModels.Length; start += 500)
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = start; i < Mathf.Min(start + 500, visibleModels.Length); i++)
                    MaterialCache[visibleModels[i]] = ReadMaterials(visibleModels[i]);
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.SaveAssets();
            Debug.Log("GTA_SA_INTERIOR_MATERIALS " + Mathf.Min(start + 500, visibleModels.Length) + "/" + visibleModels.Length);
        }

        var reportEntries = new List<InteriorReportEntry>();
        foreach (var group in grouped)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("GTA SA — interior canônico " + group.Key.ToString("00", CultureInfo.InvariantCulture));
            var entry = new InteriorReportEntry { id = group.Key, sourcePlacements = group.Count(), scene = ScenePath(group.Key) };
            foreach (var placement in group)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(Model(placement.model_name), scene);
                instance.name = placement.model_name + " #" + placement.id + " IPL " + placement.source_index +
                    " [interior " + placement.interior + "]";
                instance.transform.SetParent(root.transform, false);
                instance.transform.SetPositionAndRotation(ToUnity(placement.x, placement.y, placement.z),
                    Basis * new Quaternion(placement.qx, placement.qy, placement.qz, placement.qw) * Quaternion.Inverse(Basis));
                var source = instance.AddComponent<GtaSaInteriorSource>();
                source.sourceModelId = placement.id;
                source.rawInterior = placement.interior;
                source.canonicalInterior = group.Key;
                source.sourceIndex = placement.source_index;
                source.sourceIpl = placement.source_ipl;
                source.modelName = placement.model_name;
                source.gtaPosition = new Vector3(placement.x, placement.y, placement.z);
                source.gtaRotation = new Quaternion(placement.qx, placement.qy, placement.qz, placement.qw);
                bool lod = IsLod(placement.model_name);
                if (lod) entry.hiddenLods++;
                else
                {
                    ApplyMaterials(instance, placement.model_name);
                    entry.collisionShapes += AddCollision(instance, placement.model_name);
                }
                foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>())
                {
                    renderer.enabled = !lod;
                    renderer.shadowCastingMode = lod ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    renderer.receiveShadows = !lod;
                }
                entry.placedInstances++;
            }
            EditorSceneManager.SaveScene(scene, entry.scene);
            reportEntries.Add(entry);
            Debug.Log("GTA_SA_INTERIOR_SCENE " + group.Key + " " + entry.placedInstances);
        }

        var catalog = JsonUtility.FromJson<CatalogFile>(File.ReadAllText(Catalog));
        if (catalog == null || catalog.entries == null || catalog.entries.Length != 169)
            throw new InvalidDataException("Catálogo de 169 aglomerados inválido.");
        var main = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var controller = new GameObject("Galeria de interiores GTA SA — teste separado").AddComponent<GtaSaInteriorGallery>();
        controller.interiorScenes = grouped.Select(group => new GtaSaInteriorGallery.InteriorScene {
            id = group.Key, sceneName = SceneName(group.Key), instances = group.Count() }).ToArray();
        controller.destinations = catalog.entries.Select(item => new GtaSaInteriorGallery.Destination {
            key = item.catalog_key, interiorId = item.interior_id, instances = item.instances,
            sourceHasCollisionCandidate = item.selectable,
            center = ToUnity(item.center_xyz[0], item.center_xyz[1], item.center_xyz[2]) }).ToArray();
        controller.reviewDirectory = Path.GetFullPath(ReportFolder);
        ConfigureMainScene(controller);
        EditorSceneManager.SaveScene(main, MainScene);
        AssetDatabase.SaveAssets();

        foreach (var pair in before) if (Hash(pair.Key) != pair.Value)
            throw new IOException("Arquivo protegido alterado: " + pair.Key);
        var report = new SetupReport {
            sourcePlacements = placements.Length, placedInstances = reportEntries.Sum(e => e.placedInstances),
            uniqueModels = placements.Select(p => p.model_name).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            interiorScenes = reportEntries.Count, catalogDestinations = catalog.entries.Length,
            rawInterior274 = placements.Count(p => p.interior == 274),
            collisionShapes = reportEntries.Sum(e => e.collisionShapes),
            hiddenLods = reportEntries.Sum(e => e.hiddenLods),
            missingTextureSlots = MissingTextures.Count, missingTextures = MissingTextures.Take(50).ToArray(),
            mainScene = MainScene, manifest = Path.GetFullPath(Manifest),
            interiors = reportEntries.ToArray(), officialUnchanged = true
        };
        File.WriteAllText(Path.Combine(ReportFolder, "scene-setup.json"), JsonUtility.ToJson(report, true));
        Debug.Log("GTA_SA_INTERIORS_SCENE_OK scenes=" + report.interiorScenes +
            " placements=" + report.placedInstances + "/" + report.sourcePlacements);
    }

    [MenuItem("Horizonte/GTA San Andreas modificado/Compilar galeria de interiores Windows")]
    public static void BuildWindows()
    {
        var before = Protected.Where(File.Exists).ToDictionary(path => path, Hash);
        if (!File.Exists(MainScene))
            throw new FileNotFoundException("Crie primeiro a cena principal da galeria.", MainScene);
        var scenePaths = new[] { MainScene }.Concat(Enumerable.Range(0, 19)
            .Where(id => id != 13 && File.Exists(ScenePath(id))).Select(ScenePath)).ToArray();
        if (scenePaths.Length != 19)
            throw new FileNotFoundException("Crie a cena principal e as 18 cenas de interiores antes do build.");
        Directory.CreateDirectory(Path.GetDirectoryName(BuildExe));
        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = scenePaths, locationPathName = BuildExe,
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
        });
        foreach (var pair in before) if (Hash(pair.Key) != pair.Value)
            throw new IOException("Arquivo protegido alterado pelo build: " + pair.Key);
        if (result.summary.result != BuildResult.Succeeded)
            throw new Exception("Falha no build da galeria de interiores.");
        Debug.Log("GTA_SA_INTERIORS_BUILD_OK " + Path.GetFullPath(BuildExe));
    }

    public static void CreateAndBuildWindows() { Create(); BuildWindows(); }

    static string SceneName(int id) => "GTASAInterior_" + id.ToString("00", CultureInfo.InvariantCulture);
    static string ScenePath(int id) => SceneFolder + "/" + SceneName(id) + ".unity";
    static bool IsLod(string modelName) => modelName.IndexOf("lod", StringComparison.OrdinalIgnoreCase) >= 0;
    static Vector3 ToUnity(float x, float y, float z) => new Vector3(x, z, -y);
    static string SafeName(string value) => Regex.Replace(value ?? "", @"[^A-Za-z0-9_.-]", "_");

    static GameObject Model(string name)
    {
        if (ModelCache.TryGetValue(name, out var result)) return result;
        string path = Models + "/" + name + ".obj";
        result = File.Exists(path) ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
        ModelCache[name] = result;
        return result;
    }

    static void ApplyMaterials(GameObject instance, string modelName)
    {
        if (!MaterialCache.TryGetValue(modelName, out var materials))
            MaterialCache[modelName] = materials = ReadMaterials(modelName);
        if (materials == null || materials.Length == 0) return;
        var names = MaterialNames[modelName];
        int fallbackIndex = 0;
        foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>())
        {
            var assigned = renderer.sharedMaterials;
            for (int i = 0; i < assigned.Length; i++)
            {
                string sourceName = assigned[i] ? assigned[i].name : "";
                int match = Array.FindIndex(names, name => string.Equals(name, sourceName, StringComparison.OrdinalIgnoreCase));
                if (match < 0) match = fallbackIndex;
                fallbackIndex++;
                if (match >= 0 && match < materials.Length && materials[match]) assigned[i] = materials[match];
            }
            renderer.sharedMaterials = assigned;
        }
    }

    static Material[] ReadMaterials(string modelName)
    {
        string path = Models + "/" + modelName + ".materials.json";
        if (!File.Exists(path)) return null;
        var sidecar = JsonUtility.FromJson<MaterialSidecar>(File.ReadAllText(path));
        if (sidecar == null || sidecar.materials == null) return null;
        MaterialNames[modelName] = sidecar.materials.Select(slot => slot.material).ToArray();
        var results = new Material[sidecar.materials.Length];
        for (int i = 0; i < results.Length; i++)
        {
            string assetPath = Materials + "/" + modelName + "_" + i.ToString(CultureInfo.InvariantCulture) + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (!material)
            {
                material = new Material(Shader.Find("Standard"));
                material.name = modelName + "_" + i;
                AssetDatabase.CreateAsset(material, assetPath);
            }
            var slot = sidecar.materials[i];
            if (slot.color != null && slot.color.Length >= 4)
                material.color = new Color(slot.color[0], slot.color[1], slot.color[2], slot.color[3]);
            material.SetFloat("_Glossiness", 0f);
            material.SetFloat("_SpecularHighlights", 0f);
            material.SetFloat("_GlossyReflections", 0f);
            if (slot.textures != null && slot.textures.Length != 0)
            {
                string relative = SafeName(sidecar.txd_name) + "/" + SafeName(slot.textures[0]) + ".png";
                string texturePath = AssetsRoot + "/Textures/" + relative;
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if (texture) material.mainTexture = texture;
                else { material.mainTexture = null; MissingTextures.Add(modelName + ":" + relative); }
            }
            bool cutout = slot.textures != null && slot.textures.Length != 0 &&
                AlphaTextures.Contains(SafeName(sidecar.txd_name) + "/" + SafeName(slot.textures[0]) + ".png");
            bool fade = !cutout && slot.color != null && slot.color.Length >= 4 && slot.color[3] < .95f;
            SetSurfaceMode(material, cutout, fade);
            EditorUtility.SetDirty(material);
            results[i] = material;
        }
        return results;
    }

    static void SetSurfaceMode(Material material, bool cutout, bool fade)
    {
        material.SetFloat("_Mode", cutout ? 1 : fade ? 2 : 0);
        material.SetInt("_SrcBlend", (int)(fade ? BlendMode.SrcAlpha : BlendMode.One));
        material.SetInt("_DstBlend", (int)(fade ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
        material.SetInt("_ZWrite", fade ? 0 : 1);
        material.SetFloat("_Cutoff", .5f);
        material.SetOverrideTag("RenderType", cutout ? "TransparentCutout" : fade ? "Transparent" : "");
        if (cutout) material.EnableKeyword("_ALPHATEST_ON"); else material.DisableKeyword("_ALPHATEST_ON");
        if (fade) material.EnableKeyword("_ALPHABLEND_ON"); else material.DisableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = cutout ? (int)RenderQueue.AlphaTest : fade ? (int)RenderQueue.Transparent : -1;
    }

    static int AddCollision(GameObject instance, string modelName)
    {
        if (!CollisionCache.TryGetValue(modelName, out var sidecar))
        {
            string path = Colliders + "/" + modelName + ".collider.json";
            CollisionCache[modelName] = sidecar = File.Exists(path)
                ? JsonUtility.FromJson<ColliderSidecar>(File.ReadAllText(path)) : null;
        }
        int count = 0;
        if (sidecar != null)
        {
            string meshPath = Colliders + "/" + modelName + ".obj";
            var collisionPrefab = File.Exists(meshPath) ? AssetDatabase.LoadAssetAtPath<GameObject>(meshPath) : null;
            if (collisionPrefab)
            {
                var collisionRoot = new GameObject("Colisão GTA SA — hierarquia OBJ");
                collisionRoot.transform.SetParent(instance.transform, false);
                collisionRoot.transform.localPosition = collisionPrefab.transform.localPosition;
                collisionRoot.transform.localRotation = collisionPrefab.transform.localRotation;
                collisionRoot.transform.localScale = collisionPrefab.transform.localScale;
                foreach (var filter in collisionPrefab.GetComponentsInChildren<MeshFilter>())
                {
                    if (!filter.sharedMesh) continue;
                    var chain = new Stack<Transform>();
                    for (var node = filter.transform; node && node != collisionPrefab.transform; node = node.parent)
                        chain.Push(node);
                    Transform parent = collisionRoot.transform;
                    while (chain.Count != 0)
                    {
                        var source = chain.Pop();
                        var copy = new GameObject("COL " + source.name).transform;
                        copy.SetParent(parent, false);
                        copy.localPosition = source.localPosition;
                        copy.localRotation = source.localRotation;
                        copy.localScale = source.localScale;
                        parent = copy;
                    }
                    parent.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                    count++;
                }
            }
            if (sidecar.boxes != null) foreach (var box in sidecar.boxes)
            {
                if (box.center == null || box.size == null || box.center.Length < 3 || box.size.Length < 3) continue;
                var child = new GameObject("Colisão GTA SA — caixa");
                child.transform.SetParent(instance.transform, false);
                var collider = child.AddComponent<BoxCollider>();
                collider.center = new Vector3(box.center[0], box.center[1], box.center[2]);
                collider.size = new Vector3(box.size[0], box.size[1], box.size[2]);
                count++;
            }
            if (sidecar.spheres != null) foreach (var sphere in sidecar.spheres)
            {
                if (sphere.center == null || sphere.center.Length < 3 || sphere.radius <= 0) continue;
                var child = new GameObject("Colisão GTA SA — esfera");
                child.transform.SetParent(instance.transform, false);
                var collider = child.AddComponent<SphereCollider>();
                collider.center = new Vector3(sphere.center[0], sphere.center[1], sphere.center[2]);
                collider.radius = sphere.radius;
                count++;
            }
            if (count != 0) return count;
        }
        if (!NeedsFallbackCollision(modelName)) return 0;
        foreach (var filter in instance.GetComponentsInChildren<MeshFilter>())
        {
            if (!filter.sharedMesh || filter.GetComponent<Collider>()) continue;
            filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
            count++;
        }
        return count;
    }

    static bool NeedsFallbackCollision(string modelName)
    {
        string name = modelName.ToLowerInvariant();
        return new[] { "floor", "ground", "grnd", "wall", "stair", "room", "interior", "int_", "hotel", "house" }
            .Any(token => name.Contains(token));
    }

    static void ConfigureMainScene(GtaSaInteriorGallery controller)
    {
        var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
        camera.tag = "MainCamera";
        camera.nearClipPlane = .05f;
        camera.farClipPlane = 170f;
        camera.fieldOfView = 65f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.19f, .20f, .23f);
        controller.galleryCamera = camera;
        var light = new GameObject("Luz de inspeção interior", typeof(Light)).GetComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.shadows = LightShadows.None;
        light.transform.rotation = Quaternion.Euler(48, -32, 0);
        RenderSettings.sun = light;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.67f, .67f, .67f);
        RenderSettings.skybox = null;
        RenderSettings.fog = false;
    }

    static string Hash(string path)
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
    }
}
