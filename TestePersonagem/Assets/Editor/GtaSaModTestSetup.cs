using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Reads the original GTA SA placement manifest and creates only separate test scenes.
public static class GtaSaModTestSetup
{
    const string NatanScenePath = "Assets/Scenes/GTASanAndreasModTeste.unity";
    const string FullScenePath = "Assets/Scenes/GTASanAndreasModFullTeste.unity";
    const string NatanSectorFolder = "Assets/Scenes/GTASanAndreasModSectors";
    const string FullSectorFolder = "Assets/Scenes/GTASanAndreasModFullSectors";
    static string ActiveScenePath = NatanScenePath;
    static string ActiveSectorFolder = NatanSectorFolder;
    const string NatanAssetsRoot = "Assets/GTASanAndreasTest";
    const string FullAssetsRoot = "Assets/GTASanAndreasModFull";
    static string ActiveAssetsRoot = NatanAssetsRoot;
    static string ModelFolder => ActiveAssetsRoot + "/ImportedModels";
    static string CollisionFolder => ActiveAssetsRoot + "/Colliders";
    static string MaterialFolder => ActiveAssetsRoot + "/Materials";
    const string Manifest = "../tools/gta-sa-import/output/placements.jsonl";
    const string NatanAlphaList = "../tools/gta-sa-import/output/alpha-textures.txt";
    const string FullAlphaList = "../tools/gta-sa-import/output/full-alpha-textures.txt";
    const string ReportFolder = "../tools/gta-sa-import/review";
    static string ActiveReportFolder = ReportFolder;
    static readonly string[] Protected = {
        "Assets/Scenes/TestePersonagem.unity",
        "Assets/Scenes/GTAMapaTeste.unity",
        "Assets/Scenes/GTABairroProntoTeste.unity",
        "Assets/Prefabs/Jogador.prefab",
        "ProjectSettings/EditorBuildSettings.asset",
        "ProjectSettings/QualitySettings.asset"
    };
    static readonly Quaternion Basis = Quaternion.Euler(-90, 0, 0);
    static readonly Dictionary<string,GameObject> ModelCache = new Dictionary<string,GameObject>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string,Material[]> MaterialCache = new Dictionary<string,Material[]>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string,string[]> MaterialNames = new Dictionary<string,string[]>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string,ColliderSidecar> CollisionCache = new Dictionary<string,ColliderSidecar>(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> MissingTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    static HashSet<string> TransparentTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    // These textures are low-alpha ground blends, not leaf cutouts. Cutout
    // discards almost all of their pixels and exposes the sky below terrain.
    static readonly HashSet<string> GroundBlendTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        "desn2_alphabits/crackedgroundb.png", "desn2_alphabits/roucghstonebrtb.png",
        "hub_alpha/roucghstonebrtb.png", "eastlstr_lae2/roucghstonebrtb.png",
        "cinemart_alpha/roucghstonebrtb.png", "eastbeach2a_lae2/waljundirt1.png",
        "lae2coast_alpha/lashad1.png"
    };

    [Serializable] sealed class Placement
    {
        public int id, lod_index, source_index;
        public string model_name, txd_name, source_ipl, sector_key, scene_layer;
        public float x,y,z,qx,qy,qz,qw,draw_distance;
    }
    [Serializable] sealed class MaterialSidecar
    {
        public string model_name, txd_name;
        public MaterialSlot[] materials;
    }
    [Serializable] sealed class MaterialSlot
    {
        public string @object, material;
        public string[] textures;
        public float[] color;
    }
    [Serializable] sealed class ColliderSidecar
    {
        public string model_name, mesh_obj;
        public ColliderBox[] boxes;
        public ColliderSphere[] spheres;
    }
    [Serializable] sealed class ColliderBox { public float[] center, size; }
    [Serializable] sealed class ColliderSphere { public float[] center; public float radius; }
    [Serializable] sealed class SetupReport
    {
        public string scene, manifest;
        public int sourcePlacements, exteriorPlacements, skippedInteriors, skippedOutliers;
        public string scope;
        public int outsideScope;
        public int placedInstances, missingInstances, uniqueAvailableModels, uniqueMissingModels;
        public int sectors, colliderMeshes, lodInstances, groundUnderlayInstances;
        public string[] missingExamples;
        public int missingTextureSlots;
        public string[] missingTextureExamples;
        public Vector3 spawn;
        public bool officialUnchanged;
    }
    [Serializable] sealed class MaterialRepairReport
    {
        public int sourceAlphaSlots, materialAssetsFound, materialAssetsMissing;
        public int changedMaterialAssets, cutoutPrecedence, destinationCount;
        public int recoveredTextureSlots, neutralizedMagentaSlots, disabledDuplicateInstances;
        public int disabledConflictingSourceInstances;
        public string[] changedExamples;
        public bool officialUnchanged;
    }

    [MenuItem("Horizonte/GTA San Andreas modificado/Criar cenas separadas com modelos convertidos")]
    public static void Create() { CreateInternal(false); }

    public static void CreateFullAndRepair() { Create(); RepairFullMaterialsAndPreview(); }

    [MenuItem("Horizonte/GTA San Andreas modificado/Criar prévia separada da região Natan")]
    public static void CreateNatanPreview() { CreateInternal(true); }

    static void CreateInternal(bool natanOnly)
    {
        ActiveAssetsRoot = natanOnly ? NatanAssetsRoot : FullAssetsRoot;
        ActiveScenePath = natanOnly ? NatanScenePath : FullScenePath;
        ActiveSectorFolder = natanOnly ? NatanSectorFolder : FullSectorFolder;
        ActiveReportFolder = natanOnly ? ReportFolder : ReportFolder + "/full";
        Directory.CreateDirectory(ActiveSectorFolder);
        Directory.CreateDirectory(MaterialFolder);
        Directory.CreateDirectory(ActiveReportFolder);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        if (!File.Exists(Manifest)) throw new FileNotFoundException("Manifesto GTA SA ainda não gerado", Path.GetFullPath(Manifest));
        var before = Protected.ToDictionary(path => path, Hash);
        ModelCache.Clear(); MaterialCache.Clear(); MaterialNames.Clear(); CollisionCache.Clear(); MissingTextures.Clear();
        string alphaList = natanOnly ? NatanAlphaList : FullAlphaList;
        TransparentTextures = File.Exists(alphaList)
            ? new HashSet<string>(File.ReadLines(alphaList).Select(line => line.Trim().Replace('\\','/')).Where(line => line.Length > 0), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bySector = new Dictionary<string,List<Placement>>(StringComparer.Ordinal);
        var lodTargets = new HashSet<string>(StringComparer.Ordinal);
        var placementsBySourceIndex = new Dictionary<string,Placement>(StringComparer.Ordinal);
        var possibleLodLinks = new List<Placement>();
        foreach (string line in File.ReadLines(Manifest))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var placement = JsonUtility.FromJson<Placement>(line);
            if (placement == null || string.IsNullOrEmpty(placement.source_ipl)) continue;
            placementsBySourceIndex[placement.source_ipl + "|" + placement.source_index] = placement;
            if (placement.lod_index >= 0) possibleLodLinks.Add(placement);
        }
        foreach (var placement in possibleLodLinks)
        {
            string key = placement.source_ipl + "|" + placement.lod_index;
            if (!placementsBySourceIndex.TryGetValue(key, out var target)) continue;
            // Several modded binary IPLs contain index values that point to unrelated objects.
            // Only hide a semantically named LOD at the same physical site.
            if (target.model_name.IndexOf("lod", StringComparison.OrdinalIgnoreCase) < 0) continue;
            float dx = target.x - placement.x, dy = target.y - placement.y, dz = target.z - placement.z;
            if (dx*dx + dy*dy + dz*dz <= 100) lodTargets.Add(key);
        }
        var missing = new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        int sourceCount = 0, exteriorCount = 0, skippedInterior = 0, skippedOutlier = 0, outsideScope = 0, availableCount = 0;
        foreach (string line in File.ReadLines(Manifest))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var placement = JsonUtility.FromJson<Placement>(line);
            sourceCount++;
            if (placement == null || string.IsNullOrWhiteSpace(placement.model_name) || string.IsNullOrWhiteSpace(placement.sector_key)) continue;
            if (placement.scene_layer == "interior") { skippedInterior++; continue; }
            if (Mathf.Abs(placement.x) > 3500 || Mathf.Abs(placement.y) > 3500) { skippedOutlier++; continue; }
            exteriorCount++;
            if (natanOnly && (!TrySector(placement.sector_key, out int ix, out int iy) || ix < 3 || ix > 5 || iy < -10 || iy > -8))
            { outsideScope++; continue; }
            if (!Model(placement.model_name))
            {
                if (!missing.ContainsKey(placement.model_name)) missing[placement.model_name] = 0;
                missing[placement.model_name]++;
                continue;
            }
            if (!bySector.TryGetValue(placement.sector_key, out var list)) bySector[placement.sector_key] = list = new List<Placement>();
            list.Add(placement);
            availableCount++;
        }
        if (availableCount == 0) throw new Exception("Nenhuma instância possui FBX convertido. Converta uma amostra antes de criar a cena.");
        var visibleModels = bySector.Values.SelectMany(v => v)
            .Where(p => KeepGroundUnderlay(p) ||
                (p.model_name.IndexOf("lod", StringComparison.OrdinalIgnoreCase) < 0 &&
                 !lodTargets.Contains(p.source_ipl + "|" + p.source_index)))
            .Select(p => p.model_name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        for (int start = 0; start < visibleModels.Length; start += 500)
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                for (int index = start; index < Mathf.Min(start + 500, visibleModels.Length); index++)
                {
                    string name = visibleModels[index];
                    MaterialCache[name] = ReadMaterials(name);
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.SaveAssets();
            Debug.Log("GTA_SA_MATERIAL_MODELS " + Mathf.Min(start + 500, visibleModels.Length) + "/" + visibleModels.Length);
        }
        var sectorEntries = new List<GtaSaSectorStreaming.Sector>();
        int colliderCount = 0, lodCount = 0, groundUnderlayCount = 0;
        int savedSectors = 0;
        foreach (var pair in bySector.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!TrySector(pair.Key, out int sx, out int sy)) throw new Exception("Chave de setor inválida: " + pair.Key);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("GTA SA — setor " + pair.Key);
            foreach (var placement in pair.Value)
            {
                var prefab = Model(placement.model_name);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.name = placement.model_name + " #" + placement.id;
                instance.transform.SetParent(root.transform, false);
                instance.transform.SetPositionAndRotation(ToUnity(placement.x, placement.y, placement.z),
                    Basis * new Quaternion(placement.qx, placement.qy, placement.qz, placement.qw) * Quaternion.Inverse(Basis));
                // Imported OBJ meshes are pre-adjusted for Unity's X-axis conversion.
                instance.isStatic = true;
                bool lod = placement.model_name.IndexOf("lod", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    lodTargets.Contains(placement.source_ipl + "|" + placement.source_index);
                bool underlay = !natanOnly && KeepGroundUnderlay(placement);
                if (lod) lodCount++;
                if (underlay) { groundUnderlayCount++; instance.transform.position += Vector3.down * .05f; }
                if (!lod || underlay) ApplyMaterials(instance, placement.model_name);
                if (!lod) colliderCount += AddCollision(instance, placement.model_name);
                foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>())
                {
                    renderer.shadowCastingMode = lod ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    renderer.receiveShadows = !lod;
                    renderer.enabled = !lod || underlay;
                }
            }
            string sectorName = "GTASASector_" + KeyPart(sx) + "_" + KeyPart(sy);
            string sectorPath = ActiveSectorFolder + "/" + sectorName + ".unity";
            var center = ToUnity(sx * 256 + 128, sy * 256 + 128, 0);
            var sectorBounds = new Bounds(new Vector3(center.x, 0, center.z), new Vector3(256, 800, 256));
            // SA terrain meshes often cross multiple 256 m cells; streaming must use their true extent.
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
                if (renderer.enabled) sectorBounds.Encapsulate(renderer.bounds);
            EditorSceneManager.SaveScene(scene, sectorPath);
            sectorEntries.Add(new GtaSaSectorStreaming.Sector {
                scene = sectorName, bounds = sectorBounds,
                instances = pair.Value.Count
            });
            savedSectors++;
            if (savedSectors % 25 == 0)
                Debug.Log("GTA_SA_SECTORS_SAVED " + savedSectors + "/" + bySector.Count);
        }
        var allPlaced = bySector.Values.SelectMany(v => v).ToArray();
        var spawnPlacement = natanOnly ? SelectSpawn(allPlaced) :
            allPlaced.FirstOrDefault(p => string.Equals(p.model_name, "Lae2_roads89", StringComparison.OrdinalIgnoreCase)) ?? SelectSpawn(allPlaced);
        var groundFocus = natanOnly ? ToUnity(1162, -2108, 65) : ToUnity(spawnPlacement.x, spawnPlacement.y, spawnPlacement.z);
        var spawn = groundFocus + Vector3.up * 50;
        var main = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var mapObject = new GameObject("Mapa GTA San Andreas modificado — TESTE SEPARADO");
        var map = mapObject.AddComponent<NeighborhoodMap>();
        map.isOfficialCity = false; map.worldCode = "gta_sa_modificado_local";
        map.displayName = "GTA San Andreas modificado · teste";
        map.halfExtent = 4200; map.minY = -200; map.maxY = 1000;
        map.spawnPosition = spawn; map.mapCameraHeight = 1100;
        var water = GtaSaWaterMesh.Create(
            @"C:\SistemaAdr\gta\GTA MODIFICADO\Grand Theft Auto\data\water.dat",
            ActiveAssetsRoot + "/Water");
        if (water) water.name = "Água original GTA SA (dados water.dat)";
        var playerObject = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Jogador.prefab"), main);
        playerObject.name = "Jogador — mapa GTA SA separado";
        var player = playerObject.GetComponent<ThirdPersonPlayer>();
        player.spawnPosition = spawn; player.spawnYaw = 40;
        playerObject.transform.SetPositionAndRotation(spawn, Quaternion.Euler(0, 40, 0));
        playerObject.SetActive(false); // The first sector must load before gravity starts.
        var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(ThirdPersonCamera)).GetComponent<Camera>();
        camera.tag = "MainCamera"; camera.farClipPlane = 650; camera.nearClipPlane = .1f; camera.fieldOfView = 60;
        var orbit = camera.GetComponent<ThirdPersonCamera>(); orbit.target = player.transform; orbit.yaw = 40; orbit.pitch = 15;
        player.cameraTransform = camera.transform;
        ConfigureLighting();
        var stream = mapObject.AddComponent<GtaSaSectorStreaming>(); stream.target = player.transform; stream.sectors = sectorEntries.ToArray();
        stream.desktopRadius = 350; stream.mobileRadius = 150;
        var preview = mapObject.AddComponent<GtaSaMapPreview>();
        preview.player = player; preview.streaming = stream;
        preview.reviewDirectory = Path.GetFullPath(ActiveReportFolder);
        preview.focus = groundFocus;
        preview.destinations = bySector.OrderByDescending(p => p.Value.Count).Take(16)
            .Select(p => {
                var point = p.Value.FirstOrDefault(v => v.model_name.IndexOf("road", StringComparison.OrdinalIgnoreCase) >= 0) ?? p.Value[0];
                return new GtaSaMapPreview.Destination { name = "Setor " + p.Key,
                    position = ToUnity(point.x, point.y, point.z) };
            }).ToArray();
        preview.reviewCameras = new[] {
            CameraAt(natanOnly ? "Vista aérea da favela GTA SA" : "Vista aérea de Los Santos GTA SA",
                groundFocus + new Vector3(-55, 220, -110), groundFocus),
            CameraAt(natanOnly ? "Panorama do morro GTA SA" : "Panorama de Los Santos GTA SA",
                groundFocus + new Vector3(-80, 62, -62), groundFocus + Vector3.up * 5)
        };
        preview.reviewCameras[1].farClipPlane = 470;
        EditorSceneManager.SaveScene(main, ActiveScenePath);
        AssetDatabase.SaveAssets();
        foreach (var item in before) if (Hash(item.Key) != item.Value) throw new Exception("Arquivo oficial alterado: " + item.Key);
        var report = new SetupReport {
            scene = ActiveScenePath, manifest = Path.GetFullPath(Manifest), sourcePlacements = sourceCount,
            exteriorPlacements = exteriorCount, skippedInteriors = skippedInterior, skippedOutliers = skippedOutlier,
            outsideScope = outsideScope, scope = natanOnly ? "Natan 3x3 setores de 256 metros" : "Exterior completo disponível",
            placedInstances = availableCount, missingInstances = exteriorCount - outsideScope - availableCount,
            uniqueAvailableModels = ModelCache.Values.Count(v => v), uniqueMissingModels = missing.Count,
            sectors = sectorEntries.Count, colliderMeshes = colliderCount, lodInstances = lodCount,
            groundUnderlayInstances = groundUnderlayCount,
            missingExamples = missing.OrderByDescending(p => p.Value).Take(30).Select(p => p.Key + ":" + p.Value).ToArray(),
            missingTextureSlots = MissingTextures.Count, missingTextureExamples = MissingTextures.Take(30).ToArray(),
            spawn = spawn, officialUnchanged = true
        };
        File.WriteAllText(Path.Combine(ActiveReportFolder, "scene-setup.json"), JsonUtility.ToJson(report, true));
        Debug.Log("GTA_SA_TEST_SCENE_OK sectors=" + report.sectors + " exterior=" + report.placedInstances + "/" + (exteriorCount - outsideScope));
    }

    [MenuItem("Horizonte/GTA San Andreas modificado/Compilar teste Windows separado")]
    public static void BuildWindows()
    {
        var protectedBefore = Protected.ToDictionary(path => path, Hash);
        var main = AssetDatabase.LoadAssetAtPath<SceneAsset>(ActiveScenePath);
        if (!main) throw new FileNotFoundException("Crie a cena de teste antes de compilar", ActiveScenePath);
        var mainScene = EditorSceneManager.OpenScene(ActiveScenePath, OpenSceneMode.Single);
        var stream = mainScene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<GtaSaSectorStreaming>()).FirstOrDefault();
        if (!stream) throw new Exception("Streamer de setores ausente da cena GTA SA.");
        var sectors = stream.sectors.Select(sector => ActiveSectorFolder + "/" + sector.scene + ".unity").ToArray();
        string output = ActiveScenePath == FullScenePath
            ? "Builds/GTASanAndreasModFullTeste/MapaSAModCompleto.exe"
            : "Builds/GTASanAndreasModTeste/MapaSAMod.exe";
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { ActiveScenePath }.Concat(sectors).ToArray(),
            locationPathName = output, target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
        });
        foreach (var item in protectedBefore) if (Hash(item.Key) != item.Value) throw new Exception("Arquivo oficial alterado na compilação: " + item.Key);
        if (result.summary.result != BuildResult.Succeeded) throw new Exception("Falha ao compilar teste GTA SA separado.");
    }

    public static void CreateAndBuildWindows() { Create(); BuildWindows(); }
    public static void CreateNatanPreviewAndBuildWindows() { CreateNatanPreview(); BuildWindows(); }
    [MenuItem("Horizonte/GTA San Andreas modificado/Compilar mundo completo separado")]
    public static void BuildFullWindows() { ActiveScenePath = FullScenePath; ActiveSectorFolder = FullSectorFolder; BuildWindows(); }

    [MenuItem("Horizonte/GTA San Andreas modificado/Reparar alfa e destinos do mundo completo")]
    public static void RepairFullMaterialsAndPreview()
    {
        if (!File.Exists(FullScenePath)) throw new FileNotFoundException("Cena completa de teste ausente", FullScenePath);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ActiveAssetsRoot = FullAssetsRoot;
        Directory.CreateDirectory(ReportFolder + "/full");
        var protectedBefore = Protected.Concat(new[] { NatanScenePath }).ToDictionary(path => path, Hash);
        TransparentTextures = File.Exists(FullAlphaList)
            ? new HashSet<string>(File.ReadLines(FullAlphaList).Select(line => line.Trim().Replace('\\', '/'))
                .Where(line => line.Length > 0), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var report = new MaterialRepairReport();
        var changed = new List<string>();
        foreach (string sidecarPath in Directory.EnumerateFiles(ModelFolder, "*.materials.json", SearchOption.TopDirectoryOnly))
        {
            var sidecar = JsonUtility.FromJson<MaterialSidecar>(File.ReadAllText(sidecarPath));
            if (sidecar == null || sidecar.materials == null) continue;
            string modelName = Path.GetFileName(sidecarPath);
            modelName = modelName.Substring(0, modelName.Length - ".materials.json".Length);
            for (int index = 0; index < sidecar.materials.Length; index++)
            {
                var slot = sidecar.materials[index];
                if (slot == null || slot.color == null || slot.color.Length < 4 || slot.color[3] >= .95f) continue;
                report.sourceAlphaSlots++;
                string assetPath = MaterialFolder + "/" + modelName + "_" + index.ToString(CultureInfo.InvariantCulture) + ".mat";
                if (!File.Exists(assetPath)) { report.materialAssetsMissing++; continue; }
                var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                if (!material) { report.materialAssetsMissing++; continue; }
                report.materialAssetsFound++;
                bool cutout = IsCutout(sidecar.txd_name, slot.textures);
                if (cutout) report.cutoutPrecedence++;
                if (!SetStandardSurfaceMode(material, cutout, !cutout)) continue;
                EditorUtility.SetDirty(material);
                report.changedMaterialAssets++;
                if (changed.Count < 30) changed.Add(modelName + "_" + index + (cutout ? ":Cutout" : ":Fade"));
            }
        }
        var recovered = new[] {
            ("roadworkbarrier1_0.mat", "barrier/plainboards64HV.png"),
            ("roadworkbarrier1_1.mat", "barrier/chevron_red_64HVa.png"),
            ("CEgroundT205A_0.mat", "changeme/cs_rockdetail2.png")
        };
        foreach (var item in recovered)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/" + item.Item1);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(FullAssetsRoot + "/Textures/" + item.Item2);
            if (!material || !texture) continue;
            if (material.mainTexture != texture) { material.mainTexture = texture; EditorUtility.SetDirty(material); }
            report.recoveredTextureSlots++;
        }
        for (int index = 0; index < 2; index++)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/coluna_" + index + ".mat");
            if (!material) continue;
            material.color = new Color(.55f, .55f, .55f, 1f);
            EditorUtility.SetDirty(material);
            report.neutralizedMagentaSlots++;
        }
        report.changedExamples = changed.ToArray();
        AssetDatabase.SaveAssets();

        // Two exact duplicate placements in PackFavelaNatan cause z-fighting.
        // Keep both editable source instances, but deactivate the second copy
        // only in this separate test sector.
        string duplicateSectorPath = FullSectorFolder + "/GTASASector_p4_m9.unity";
        var duplicateScene = EditorSceneManager.OpenScene(duplicateSectorPath, OpenSceneMode.Single);
        var duplicateTargets = new[] {
            ("sm_bushvbig", ToUnity(1114.5f, -2163.75f, 63.65625f)),
            ("pinetree07", ToUnity(1034.984375f, -2201.1015625f, 36.671875f))
        };
        foreach (var item in duplicateTargets)
        {
            var matches = duplicateScene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Where(transform => transform.name.StartsWith(item.Item1 + " #", StringComparison.OrdinalIgnoreCase) &&
                    Vector3.Distance(transform.position, item.Item2) < .05f).ToArray();
            if (matches.Length != 2) throw new Exception("Duplicata de teste mudou: " + item.Item1 + " count=" + matches.Length);
            matches[1].gameObject.SetActive(false);
            report.disabledDuplicateInstances++;
        }
        // This single mod sign is a 39 m red slab crossing the Natan hill.
        // Keep its source placement in the editable scene, but hide it and its
        // collider in this test. The second instance elsewhere stays active.
        Vector3 signPosition = ToUnity(1125.40969965f, -2275.08085633f, 57.9812492371f);
        var signs = duplicateScene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Where(transform => transform.name.StartsWith("sanpedlithus_LAw2 #", StringComparison.OrdinalIgnoreCase) &&
                Vector3.Distance(transform.position, signPosition) < .05f).ToArray();
        if (signs.Length != 1) throw new Exception("Placa Natan de teste mudou: count=" + signs.Length);
        signs[0].gameObject.SetActive(false);
        report.disabledConflictingSourceInstances = 1;
        EditorSceneManager.MarkSceneDirty(duplicateScene);
        EditorSceneManager.SaveScene(duplicateScene, duplicateSectorPath);

        var scene = EditorSceneManager.OpenScene(FullScenePath, OpenSceneMode.Single);
        var preview = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<GtaSaMapPreview>(true)).FirstOrDefault();
        if (!preview) throw new Exception("Controle da prévia full ausente.");
        preview.destinations = new[] {
            new GtaSaMapPreview.Destination { name = "Los Santos", position = ToUnity(2046f, -1613f, 12f) },
            new GtaSaMapPreview.Destination { name = "San Fierro", position = ToUnity(-1936.226563f, 1334.351563f, 6.1875f) },
            new GtaSaMapPreview.Destination { name = "Las Venturas", position = ToUnity(2517.390625f, 1583.226563f, 9.7421875f) },
            new GtaSaMapPreview.Destination { name = "Área rural", position = ToUnity(-229.56f, 1001.39f, 18.59f) },
            new GtaSaMapPreview.Destination { name = "Favela Natan", position = ToUnity(1162f, -2108f, 65f) }
        };
        report.destinationCount = preview.destinations.Length;
        EditorUtility.SetDirty(preview);
        RenderSettings.fog = true;
        RenderSettings.fogStartDistance = 140f;
        RenderSettings.fogEndDistance = 375f;
        if (preview.reviewCameras != null)
            foreach (var camera in preview.reviewCameras) if (camera) { camera.farClipPlane = 430f; EditorUtility.SetDirty(camera); }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, FullScenePath);
        AssetDatabase.SaveAssets();
        foreach (var item in protectedBefore)
            if (Hash(item.Key) != item.Value) throw new Exception("Arquivo protegido alterado: " + item.Key);
        report.officialUnchanged = true;
        File.WriteAllText(Path.Combine(ReportFolder, "full/material-repair.json"), JsonUtility.ToJson(report, true));
        Debug.Log("GTA_SA_FULL_MATERIAL_REPAIR_OK changed=" + report.changedMaterialAssets +
            " found=" + report.materialAssetsFound + " destinations=" + report.destinationCount);
    }

    static Placement SelectSpawn(Placement[] placements)
    {
        // Favor a quadra de Los Santos com maior número de modelos convertidos.
        var losSantos = placements.Where(p => p.x > 600 && p.y < -650 && p.y > -2700).ToArray();
        var candidates = losSantos.Length > 0 ? losSantos : placements;
        var dense = candidates.GroupBy(p => p.sector_key).OrderByDescending(g => g.Count()).First();
        return dense.FirstOrDefault(p => p.model_name.IndexOf("road", StringComparison.OrdinalIgnoreCase) >= 0) ?? dense.First();
    }

    static GameObject Model(string name)
    {
        if (ModelCache.TryGetValue(name, out var model)) return model;
        string path = ModelFolder + "/" + name + ".obj";
        if (!File.Exists(path)) path = ModelFolder + "/" + name + ".fbx";
        model = File.Exists(path) ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
        ModelCache.Add(name, model);
        return model;
    }

    static bool KeepGroundUnderlay(Placement placement) =>
        string.Equals(placement.model_name, "s_lodbit_06_2", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(placement.model_name, "s_lodbit_06_3", StringComparison.OrdinalIgnoreCase);

    static void ApplyMaterials(GameObject instance, string modelName)
    {
        if (!MaterialCache.TryGetValue(modelName, out var materials))
        {
            materials = ReadMaterials(modelName);
            MaterialCache[modelName] = materials;
        }
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
        string path = ModelFolder + "/" + modelName + ".materials.json";
        if (!File.Exists(path)) return null;
        var sidecar = JsonUtility.FromJson<MaterialSidecar>(File.ReadAllText(path));
        if (sidecar == null || sidecar.materials == null) return null;
        MaterialNames[modelName] = sidecar.materials.Select(slot => slot.material).ToArray();
        var results = new Material[sidecar.materials.Length];
        for (int i = 0; i < results.Length; i++)
        {
            string asset = MaterialFolder + "/" + modelName + "_" + i.ToString(CultureInfo.InvariantCulture) + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(asset);
            if (!material)
            {
                material = new Material(Shader.Find("Standard"));
                material.name = modelName + "_" + i;
                AssetDatabase.CreateAsset(material, asset);
            }
            var slot = sidecar.materials[i];
            if (slot.color != null && slot.color.Length >= 4)
                material.color = new Color(slot.color[0],slot.color[1],slot.color[2],slot.color[3]);
            material.SetFloat("_Glossiness", 0f);
            material.SetFloat("_SpecularHighlights", 0f);
            material.SetFloat("_GlossyReflections", 0f);
            EditorUtility.SetDirty(material);
            var textures = slot.textures;
            if (textures != null && textures.Length > 0)
            {
                string textureRelativePath = SafeAssetName(sidecar.txd_name) + "/" + SafeAssetName(textures[0]) + ".png";
                string texturePath = ActiveAssetsRoot + "/Textures/" + textureRelativePath;
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if (texture) { material.mainTexture = texture; EditorUtility.SetDirty(material); }
                else { material.mainTexture = null; MissingTextures.Add(modelName + ":" + texturePath); }
            }
            bool sourceAlpha = (slot.color != null && slot.color.Length >= 4 && slot.color[3] < .95f) ||
                IsGroundBlend(sidecar.txd_name, textures);
            SetStandardSurfaceMode(material, IsCutout(sidecar.txd_name, textures), sourceAlpha);
            EditorUtility.SetDirty(material);
            results[i] = material;
        }
        return results;
    }

    static string SafeAssetName(string value) => Regex.Replace(value ?? "", @"[^A-Za-z0-9_.-]", "_");

    static string TextureKey(string txdName, string[] textures) =>
        textures != null && textures.Length > 0 ? SafeAssetName(txdName) + "/" + SafeAssetName(textures[0]) + ".png" : "";

    static bool IsGroundBlend(string txdName, string[] textures) => GroundBlendTextures.Contains(TextureKey(txdName, textures));

    static bool IsCutout(string txdName, string[] textures)
    {
        string key = TextureKey(txdName, textures);
        return !GroundBlendTextures.Contains(key) && TransparentTextures.Contains(key);
    }

    static bool SetStandardSurfaceMode(Material material, bool cutout, bool sourceAlpha)
    {
        bool fade = sourceAlpha && !cutout;
        float mode = cutout ? 1f : fade ? 2f : 0f;
        int src = (int)(fade ? BlendMode.SrcAlpha : BlendMode.One);
        int dst = (int)(fade ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero);
        int zWrite = fade ? 0 : 1;
        int queue = cutout ? (int)RenderQueue.AlphaTest : fade ? (int)RenderQueue.Transparent : -1;
        string tag = cutout ? "TransparentCutout" : fade ? "Transparent" : "";
        bool changed = Mathf.Abs(material.GetFloat("_Mode") - mode) > .001f ||
            material.GetInt("_SrcBlend") != src || material.GetInt("_DstBlend") != dst ||
            material.GetInt("_ZWrite") != zWrite || material.renderQueue != queue ||
            material.GetTag("RenderType", false, "") != tag ||
            material.IsKeywordEnabled("_ALPHATEST_ON") != cutout ||
            material.IsKeywordEnabled("_ALPHABLEND_ON") != fade;
        material.SetFloat("_Mode", mode);
        material.SetInt("_SrcBlend", src);
        material.SetInt("_DstBlend", dst);
        material.SetInt("_ZWrite", zWrite);
        material.SetFloat("_Cutoff", .5f);
        material.SetOverrideTag("RenderType", tag);
        if (cutout) material.EnableKeyword("_ALPHATEST_ON"); else material.DisableKeyword("_ALPHATEST_ON");
        if (fade) material.EnableKeyword("_ALPHABLEND_ON"); else material.DisableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = queue;
        return changed;
    }

    static int AddCollision(GameObject instance, string modelName)
    {
        if (!CollisionCache.TryGetValue(modelName, out var sidecar))
        {
            string path = CollisionFolder + "/" + modelName + ".collider.json";
            sidecar = File.Exists(path) ? JsonUtility.FromJson<ColliderSidecar>(File.ReadAllText(path)) : null;
            CollisionCache[modelName] = sidecar;
        }
        int count = 0;
        if (sidecar != null)
        {
            string meshPath = CollisionFolder + "/" + modelName + ".obj";
            if (File.Exists(meshPath))
            {
                var collisionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(meshPath);
                if (collisionPrefab)
                {
                    var collisionRoot = new GameObject("Colisão original GTA SA — hierarquia OBJ");
                    collisionRoot.transform.SetParent(instance.transform, false);
                    collisionRoot.transform.localPosition = collisionPrefab.transform.localPosition;
                    collisionRoot.transform.localRotation = collisionPrefab.transform.localRotation;
                    collisionRoot.transform.localScale = collisionPrefab.transform.localScale;
                    collisionRoot.isStatic = true;
                    foreach (var filter in collisionPrefab.GetComponentsInChildren<MeshFilter>())
                    {
                        if (!filter.sharedMesh) continue;
                        var chain = new Stack<Transform>();
                        for (var node = filter.transform; node && node != collisionPrefab.transform; node = node.parent)
                            chain.Push(node);
                        Transform parent = collisionRoot.transform;
                        while (chain.Count > 0)
                        {
                            var source = chain.Pop();
                            var copy = new GameObject("COL " + source.name).transform;
                            copy.SetParent(parent, false);
                            copy.localPosition = source.localPosition;
                            copy.localRotation = source.localRotation;
                            copy.localScale = source.localScale;
                            copy.gameObject.isStatic = true;
                            parent = copy;
                        }
                        parent.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                        count++;
                    }
                }
            }
            if (sidecar.boxes != null) foreach (var box in sidecar.boxes)
            {
                if (box.center == null || box.size == null || box.center.Length < 3 || box.size.Length < 3) continue;
                var child = new GameObject("Colisão original GTA SA — caixa");
                child.transform.SetParent(instance.transform, false);
                var collider = child.AddComponent<BoxCollider>();
                collider.center = new Vector3(box.center[0],box.center[1],box.center[2]);
                collider.size = new Vector3(box.size[0],box.size[1],box.size[2]);
                child.isStatic = true;
                count++;
            }
            if (sidecar.spheres != null) foreach (var sphere in sidecar.spheres)
            {
                if (sphere.center == null || sphere.center.Length < 3 || sphere.radius <= 0) continue;
                var child = new GameObject("Colisão original GTA SA — esfera");
                child.transform.SetParent(instance.transform, false);
                var collider = child.AddComponent<SphereCollider>();
                collider.center = new Vector3(sphere.center[0],sphere.center[1],sphere.center[2]);
                collider.radius = sphere.radius;
                child.isStatic = true;
                count++;
            }
            if (count > 0) return count;
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
        if (name.Contains("tree") || name.Contains("bush") || name.Contains("palm") ||
            name.Contains("veg_") || name.Contains("grass") || name.Contains("decal") ||
            name.Contains("lamp") || name.Contains("light") || name.Contains("pole")) return false;
        return new[] { "road", "ground", "grnd", "land", "hill", "beach", "bridge", "tunn", "stair", "house", "build", "shop", "barraco", "wall", "dock" }
            .Any(token => name.Contains(token));
    }

    static Vector3 ToUnity(float x, float y, float z) { return new Vector3(x, z, -y); }
    static string KeyPart(int value) { return value < 0 ? "m" + (-value) : "p" + value; }
    static bool TrySector(string key, out int sx, out int sy)
    {
        sx = sy = 0;
        var parts = key.Split('_');
        return parts.Length == 2 && int.TryParse(parts[0], out sx) && int.TryParse(parts[1], out sy);
    }
    static Camera CameraAt(string name, Vector3 eye, Vector3 target)
    {
        var camera = new GameObject(name, typeof(Camera)).GetComponent<Camera>();
        camera.enabled = false; camera.transform.position = eye; camera.transform.LookAt(target);
        camera.farClipPlane = 1100; camera.nearClipPlane = .1f; camera.fieldOfView = 54;
        return camera;
    }
    static void ConfigureLighting()
    {
        var sun = new GameObject("Sol GTA SA teste", typeof(Light)).GetComponent<Light>();
        sun.type = LightType.Directional; sun.intensity = 1.05f; sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(48,-32,0); RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.62f,.7f,.8f);
        RenderSettings.ambientEquatorColor = new Color(.47f,.5f,.54f);
        RenderSettings.ambientGroundColor = new Color(.27f,.27f,.28f);
        RenderSettings.skybox = Resources.Load<Material>("Environment/LateAfternoonSkyMaterial");
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 175; RenderSettings.fogEndDistance = 500;
        RenderSettings.fogColor = new Color(.65f,.72f,.79f);
    }
    static string Hash(string path)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
    }
}
