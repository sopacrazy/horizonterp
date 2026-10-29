using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Explicit, removable repairs for source terrain gaps in the separate SA test.
// Each patch is a standalone editable OBJ in the sector where it is visible.
public static class GtaSaTerrainPatchSetup
{
    const string AssetFolder = "Assets/GTASanAndreasModFull/TestTerrainPatches";
    const string TextureFolder = "Assets/GTASanAndreasModFull/Textures";
    const string SectorFolder = "Assets/Scenes/GTASanAndreasModFullSectors";
    const string MainScene = "Assets/Scenes/GTASanAndreasModFullTeste.unity";
    const string Report = "../tools/gta-sa-import/review/full/terrain-patches.json";

    [Serializable] sealed class Patch
    {
        public string name, sector, texture, texture2;
        public Vector3 position;
        public float minX, maxX, minZ, maxZ;
    }
    [Serializable] sealed class PatchResult
    {
        public string name, sector;
        public int triangles, colliders, sampleCount, steepTriangles;
        public float minimumNormalY;
        public Bounds bounds;
    }
    [Serializable] sealed class PatchReport
    {
        public int patchCount, triangles, colliders;
        public PatchResult[] patches;
    }

    static readonly Patch[] Patches = {
        new Patch { name="rural-west", sector="GTASASector_m2_p3", texture="des_s/grasstype5_dirt.png",
            position=new Vector3(-338,0,-934), minX=-338,maxX=-210,minZ=-1046,maxZ=-934 },
        new Patch { name="rural-east", sector="GTASASector_m1_p4", texture="des_s/grasstype5_dirt.png",
            position=new Vector3(-164,0,-1064), minX=-164,maxX=-124,minZ=-1148,maxZ=-1064 },
        new Patch { name="los-santos-alley", sector="GTASASector_p8_m7", texture="glenpark1x_lae/greyground256.png",
            position=new Vector3(2076,0,1652), minX=2076,maxX=2096,minZ=1616,maxZ=1652 },
        new Patch { name="san-fierro-wharf", sector="GTASASector_m9_p5", texture="slapart01sfe/Bow_Abpave_Gen.png",
            position=new Vector3(-2072,0,-1268), minX=-2072,maxX=-2036,minZ=-1296,maxZ=-1268 },
        new Patch { name="des-aroadbit02-support", sector="GTASASector_m2_p3",
            texture="des_s/des_dirt1.png", texture2="des_s/des_dirt2stones.png",
            position=new Vector3(-302,0,-941), minX=-271.121f,maxX=-186.744f,minZ=-1074,maxZ=-968.43f }
    };

    [MenuItem("Horizonte/GTA San Andreas modificado/Aplicar reparos de terreno só no teste")]
    public static void Apply()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var results = new List<PatchResult>();
        var boundsByScene = new Dictionary<string,Bounds>(StringComparer.Ordinal);
        foreach (var patch in Patches)
        {
            string scenePath = SectorFolder + "/" + patch.sector + ".unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var sectorRoot = scene.GetRootGameObjects().FirstOrDefault(go => go.name.StartsWith("GTA SA — setor ", StringComparison.Ordinal));
            if (!sectorRoot) throw new Exception("Setor de teste ausente: " + scenePath);
            string objectName = "REPARO DE TESTE — " + patch.name;
            foreach (var old in sectorRoot.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name == objectName).ToArray())
                UnityEngine.Object.DestroyImmediate(old.gameObject);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetFolder + "/" + patch.name + ".obj");
            if (!prefab) throw new Exception("OBJ de reparo ausente: " + patch.name);
            var texturePaths = string.IsNullOrEmpty(patch.texture2)
                ? new[] { patch.texture } : new[] { patch.texture, patch.texture2 };
            var materials = new Material[texturePaths.Length];
            for (int i = 0; i < texturePaths.Length; i++)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "/" + texturePaths[i]);
                if (!texture) throw new Exception("Textura de reparo ausente: " + texturePaths[i]);
                string suffix = i == 0 ? "" : "-" + (i + 1);
                string materialPath = AssetFolder + "/" + patch.name + suffix + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (!material)
                {
                    material = new Material(Shader.Find("Standard")) { name = patch.name + suffix };
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                material.mainTexture = texture;
                material.color = Color.white;
                material.SetFloat("_Glossiness", 0);
                EditorUtility.SetDirty(material);
                materials[i] = material;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = objectName;
            instance.transform.SetParent(sectorRoot.transform, false);
            instance.transform.position = patch.position;
            instance.isStatic = true;
            int colliders = 0, triangles = 0;
            int steepTriangles = 0;
            float minimumNormalY = 1;
            var sampleCandidates = new List<Vector3>();
            Bounds worldBounds = default;
            bool hasBounds = false;
            foreach (var filter in instance.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                if (!mesh || mesh.triangles.Length == 0) continue;
                var vertices = mesh.vertices;
                var indices = mesh.triangles;
                Vector3 normal = filter.transform.TransformDirection(Vector3.Cross(
                    vertices[indices[1]] - vertices[indices[0]],
                    vertices[indices[2]] - vertices[indices[0]]));
                if (normal.y <= 0) throw new Exception("Reparo com faces invertidas: " + patch.name);
                triangles += indices.Length / 3;
                for (int face = 0; face + 2 < indices.Length; face += 3)
                {
                    Vector3 a = filter.transform.TransformPoint(vertices[indices[face]]);
                    Vector3 b = filter.transform.TransformPoint(vertices[indices[face + 1]]);
                    Vector3 c = filter.transform.TransformPoint(vertices[indices[face + 2]]);
                    float normalY = Vector3.Cross(b - a, c - a).normalized.y;
                    minimumNormalY = Mathf.Min(minimumNormalY, normalY);
                    if (normalY < .72f) steepTriangles++;
                    else
                        sampleCandidates.Add((a + b + c) / 3f);
                }
                var renderer = filter.GetComponent<MeshRenderer>();
                if (!renderer) throw new Exception("MeshRenderer ausente: " + patch.name);
                var sourceSlots = renderer.sharedMaterials;
                if (materials.Length > 1 && sourceSlots.Length != materials.Length)
                    throw new Exception("Número inesperado de materiais OBJ: " + patch.name + " slots=" + sourceSlots.Length);
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                filter.gameObject.isStatic = true;
                var collider = filter.GetComponent<MeshCollider>();
                if (!collider) collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                colliders++;
                if (!hasBounds) { worldBounds = renderer.bounds; hasBounds = true; }
                else worldBounds.Encapsulate(renderer.bounds);
            }
            if (!hasBounds || colliders != 1) throw new Exception("Reparo exige uma malha/colisão auditável: " + patch.name);
            if (worldBounds.min.x < patch.minX - 1 || worldBounds.max.x > patch.maxX + 1 ||
                worldBounds.min.z < patch.minZ - 1 || worldBounds.max.z > patch.maxZ + 1)
                throw new Exception("Reparo fora da área medida: " + patch.name + " " + worldBounds);
            if (sampleCandidates.Count < 5) throw new Exception("Reparo sem cinco pontos de piso: " + patch.name);
            var marker = instance.AddComponent<GtaSaTerrainPatchMarker>();
            marker.patchName = patch.name;
            marker.localSamples = ChooseSamples(sampleCandidates, worldBounds).Select(instance.transform.InverseTransformPoint).ToArray();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, scenePath);
            results.Add(new PatchResult { name=patch.name, sector=patch.sector,
                triangles=triangles, colliders=colliders, sampleCount=marker.localSamples.Length,
                steepTriangles=steepTriangles, minimumNormalY=minimumNormalY, bounds=worldBounds });
            if (boundsByScene.TryGetValue(patch.sector, out var previousBounds))
            {
                previousBounds.Encapsulate(worldBounds);
                boundsByScene[patch.sector] = previousBounds;
            }
            else boundsByScene[patch.sector] = worldBounds;
            Debug.Log("GTA_SA_TERRAIN_PATCH " + patch.name + " triangles=" + triangles);
        }
        var main = EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Single);
        var stream = main.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<GtaSaSectorStreaming>(true)).FirstOrDefault();
        if (!stream) throw new Exception("Streamer do teste completo ausente.");
        foreach (var sector in stream.sectors)
        {
            if (sector == null || !boundsByScene.TryGetValue(sector.scene, out var bounds)) continue;
            var combined = sector.bounds;
            combined.Encapsulate(bounds);
            sector.bounds = combined;
        }
        EditorUtility.SetDirty(stream);
        EditorSceneManager.MarkSceneDirty(main);
        EditorSceneManager.SaveScene(main, MainScene);
        AssetDatabase.SaveAssets();
        var report = new PatchReport { patchCount=results.Count, patches=results.ToArray(),
            triangles=results.Sum(item => item.triangles), colliders=results.Sum(item => item.colliders) };
        File.WriteAllText(Report, JsonUtility.ToJson(report, true));
        Debug.Log("GTA_SA_TERRAIN_PATCHES_OK count=" + report.patchCount + " triangles=" + report.triangles);
    }

    static IEnumerable<Vector3> ChooseSamples(List<Vector3> candidates, Bounds bounds)
    {
        var chosen = new List<int>();
        for (int sample = 0; sample < 5; sample++)
        {
            int bestIndex = -1;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (chosen.Contains(i)) continue;
                var point = candidates[i];
                float score;
                if (sample == 0)
                {
                    float dx = point.x - bounds.center.x, dz = point.z - bounds.center.z;
                    score = -(dx * dx + dz * dz);
                }
                else
                    score = chosen.Min(index => {
                        float dx = point.x - candidates[index].x, dz = point.z - candidates[index].z;
                        return dx * dx + dz * dz;
                    });
                if (score > bestScore) { bestScore = score; bestIndex = i; }
            }
            if (bestIndex < 0) throw new Exception("Falha ao selecionar pontos de piso do reparo.");
            chosen.Add(bestIndex);
            yield return candidates[bestIndex];
        }
    }
}
