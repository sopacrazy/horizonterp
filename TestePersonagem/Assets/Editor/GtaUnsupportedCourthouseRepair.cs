using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// The recovered courthouse shell is absent from the supplied source map. Its
// upper ornaments otherwise render as a grid of loose objects over the plaza.
public static class GtaUnsupportedCourthouseRepair
{
    const string OriginalMesh = "Assets/GTARecoveredMap/Mesh/sc1_07_cor_cutdet.asset";
    const string SupportedMesh = "Assets/GTARecoveredMap/Restored/Mesh/sc1_07_cor_cutdet_ground_details.asset";
    const string Preview = "Assets/Scenes/GTAMapaTeste.unity";
    const string Sector = "Assets/Scenes/CitySectors/CitySector_-4_-1.unity";

    [MenuItem("Horizonte/Cidade oficial/Limpar ornamentos suspensos da praca")]
    public static void Repair()
    {
        var original = AssetDatabase.LoadAssetAtPath<Mesh>(OriginalMesh);
        if (!original || original.subMeshCount != 5)
            throw new InvalidOperationException("Malha sc1_07_cor_cutdet original inesperada.");

        var supported = AssetDatabase.LoadAssetAtPath<Mesh>(SupportedMesh);
        if (!supported)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SupportedMesh));
            supported = Object.Instantiate(original);
            supported.name = "sc1_07_cor_cutdet_ground_details";
            for (int i = 0; i < 3; i++)
                supported.SetTriangles(Array.Empty<int>(), i, false);
            AssetDatabase.CreateAsset(supported, SupportedMesh);
        }

        string backup = Path.GetFullPath("../tools/map-visual-audit/courthouse-backup-" +
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(backup);
        foreach (string path in new[] { Preview, Sector })
        {
            File.Copy(path, Path.Combine(backup, Path.GetFileName(path)), true);
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var filters = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<MeshFilter>(true)).ToArray();
            var details = filters.Single(filter => filter.gameObject.name == "sc1_07_cor_cutdet");
            details.sharedMesh = supported;
            var collider = details.GetComponent<MeshCollider>();
            if (collider) collider.sharedMesh = supported;

            foreach (string name in new[] { "sc1_07_scroll_det", "sc1_07_window_noint",
                "sc1_07_cupola_glass" })
                filters.Single(filter => filter.gameObject.name == name).gameObject.SetActive(false);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new IOException("Falha ao salvar " + path);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("COURTHOUSE_FLOATING_DETAILS_REPAIRED backup=" + backup);
    }
}
