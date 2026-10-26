using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Restores city meshes which the first recovered-map pass discarded because
// their GameObjects had generic names. Keeps later hand edits to both scenes.
public static class GtaMissingCityGeometryRestore
{
    const string Source = "Assets/GTARecoveredMap/GTARecoveredGeometry.unity";
    const string Preview = "Assets/Scenes/GTAMapaTeste.unity";
    const string Official = "Assets/Scenes/TestePersonagem.unity";
    static readonly string[] MeshNames = { "sc1_12_build", "sc1_12_church", "sc1_12_apt" };

    [MenuItem("Horizonte/Cidade oficial/Restaurar 3 edificios ausentes")]
    public static void Restore()
    {
        const string backupRoot = "../tools/gta-map-review/restore-backup";
        string backup = Path.Combine(backupRoot, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(backup);
        foreach (string path in new[] { Preview, Official,
            "Assets/Scenes/CitySectors/CitySector_-2_0.unity",
            "Assets/Scenes/CitySectors/CitySector_-1_1.unity" })
            File.Copy(path, Path.Combine(backup, Path.GetFileName(path)), true);

        var source = EditorSceneManager.OpenScene(Source, OpenSceneMode.Single);
        var sourceRoot = source.GetRootGameObjects().Single(root => root.name == "Mapa GTA recuperado");
        GtaRecoveredCityRepair.Apply(sourceRoot);
        var sourceObjects = MeshNames.Select(name => sourceRoot.GetComponentsInChildren<MeshFilter>()
            .Single(filter => filter.sharedMesh && filter.sharedMesh.name == name).gameObject).ToArray();

        var preview = EditorSceneManager.OpenScene(Preview, OpenSceneMode.Additive);
        var previewRoot = preview.GetRootGameObjects().Single(root => root.name == "Mapa GTA recuperado");
        var previewObjects = new Dictionary<string, GameObject>();
        for (int i = 0; i < MeshNames.Length; i++)
        {
            string name = MeshNames[i];
            var existing = previewRoot.GetComponentsInChildren<MeshFilter>(true)
                .SingleOrDefault(filter => filter.sharedMesh && filter.sharedMesh.name == name);
            if (existing) { previewObjects[name] = existing.gameObject; continue; }
            var copy = Object.Instantiate(sourceObjects[i]);
            copy.name = name;
            copy.transform.SetParent(null, false);
            SceneManager.MoveGameObjectToScene(copy, preview);
            copy.transform.SetParent(previewRoot.transform, false);
            copy.transform.localPosition = sourceObjects[i].transform.localPosition;
            copy.transform.localRotation = sourceObjects[i].transform.localRotation;
            copy.transform.localScale = sourceObjects[i].transform.localScale;
            previewObjects[name] = copy;
        }
        EditorSceneManager.MarkSceneDirty(preview);
        if (!EditorSceneManager.SaveScene(preview)) throw new IOException("Falha ao salvar " + Preview);
        EditorSceneManager.CloseScene(source, true);

        var boundsBySector = new Dictionary<string, Bounds>();
        foreach (string name in MeshNames)
        {
            var original = previewObjects[name];
            Bounds bounds = original.GetComponent<MeshRenderer>().bounds;
            string sectorName = "CitySector_" + Mathf.FloorToInt(bounds.center.x / 128f) + "_" +
                Mathf.FloorToInt(bounds.center.z / 128f);
            string path = "Assets/Scenes/CitySectors/" + sectorName + ".unity";
            if (!File.Exists(path)) throw new FileNotFoundException("Setor ausente para " + name, path);
            var sector = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            var sectorRoot = sector.GetRootGameObjects().Single();
            var existing = sectorRoot.GetComponentsInChildren<MeshFilter>(true)
                .SingleOrDefault(filter => filter.sharedMesh && filter.sharedMesh.name == name);
            GameObject copy;
            if (!existing)
            {
                copy = Object.Instantiate(original);
                copy.name = name;
                copy.transform.SetParent(null, false);
                SceneManager.MoveGameObjectToScene(copy, sector);
                copy.transform.SetParent(sectorRoot.transform, true);
            }
            else copy = existing.gameObject;
            // Instantiate can retain the preview object's local coordinates while
            // losing its translated parent. Always copy the actual world pose.
            copy.transform.SetPositionAndRotation(original.transform.position, original.transform.rotation);
            copy.transform.localScale = original.transform.lossyScale;
            EditorSceneManager.MarkSceneDirty(sector);
            if (!EditorSceneManager.SaveScene(sector)) throw new IOException("Falha ao salvar " + path);
            if (boundsBySector.TryGetValue(sectorName, out var current))
            {
                current.Encapsulate(bounds);
                boundsBySector[sectorName] = current;
            }
            else boundsBySector.Add(sectorName, bounds);
            EditorSceneManager.CloseScene(sector, true);
        }

        var official = EditorSceneManager.OpenScene(Official, OpenSceneMode.Single);
        var streaming = official.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<CitySectorStreaming>(true)).Single();
        foreach (var entry in streaming.sectors)
            if (boundsBySector.TryGetValue(entry.scene, out var added))
            {
                var enlarged = entry.bounds;
                enlarged.Encapsulate(added);
                entry.bounds = enlarged;
            }
        EditorUtility.SetDirty(streaming);
        EditorSceneManager.MarkSceneDirty(official);
        if (!EditorSceneManager.SaveScene(official)) throw new IOException("Falha ao salvar " + Official);
        AssetDatabase.SaveAssets();
        Debug.Log("GTA_MISSING_CITY_RESTORED " + string.Join(", ", MeshNames) + " backup=" + Path.GetFullPath(backup));
    }
}
