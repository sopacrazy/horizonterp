using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Removes detached high fragments in sc1_08 while retaining every ground triangle.
public static class GtaUnsupportedElevatedDetailsRepair
{
    const string OriginalMesh = "Assets/GTARecoveredMap/Mesh/sc1_08_ground.asset";
    const string GroundMesh = "Assets/GTARecoveredMap/Restored/Mesh/sc1_08_ground_without_floating_panels.asset";
    const string Preview = "Assets/Scenes/GTAMapaTeste.unity";
    const string Sector = "Assets/Scenes/CitySectors/CitySector_-4_0.unity";

    [MenuItem("Horizonte/Cidade oficial/Limpar paineis suspensos vizinhos")]
    public static void Repair()
    {
        string backup = Path.GetFullPath("../tools/map-visual-audit/elevated-backup-" +
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(backup);
        var original = AssetDatabase.LoadAssetAtPath<Mesh>(OriginalMesh);
        if (!original || original.subMeshCount <= 9)
            throw new InvalidOperationException("Malha sc1_08_ground original inesperada.");
        var replacement = AssetDatabase.LoadAssetAtPath<Mesh>(GroundMesh);

        foreach (string path in new[] { Preview, Sector })
        {
            File.Copy(path, Path.Combine(backup, Path.GetFileName(path)), true);
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var filters = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<MeshFilter>(true)).ToArray();
            var ground = filters.Single(filter => filter.gameObject.name == "sc1_08_ground");
            if (!replacement)
                replacement = CreateGroundVariant(original, ground.transform);
            ground.sharedMesh = replacement;
            var collider = ground.GetComponent<MeshCollider>();
            if (collider) collider.sharedMesh = replacement;

            foreach (string name in new[] { "sc1_08_sign_lights", "sc1_08_railings_02" })
                filters.Single(filter => filter.gameObject.name == name).gameObject.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new IOException("Falha ao salvar " + path);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("SC1_08_FLOATING_DETAILS_REPAIRED backup=" + backup);
    }

    static Mesh CreateGroundVariant(Mesh original, Transform placement)
    {
        int[] triangles = original.GetTriangles(9);
        Vector3[] vertices = original.vertices;
        var groundTriangles = new List<int>(triangles.Length);
        int removed = 0;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            float a = placement.TransformPoint(vertices[triangles[i]]).y;
            float b = placement.TransformPoint(vertices[triangles[i + 1]]).y;
            float c = placement.TransformPoint(vertices[triangles[i + 2]]).y;
            bool low = a < 8f && b < 8f && c < 8f;
            bool high = a > 8f && b > 8f && c > 8f;
            if (!low && !high)
                throw new InvalidOperationException("Triangulo sc1_08_ground atravessa o limite de altura.");
            if (low)
            {
                groundTriangles.Add(triangles[i]);
                groundTriangles.Add(triangles[i + 1]);
                groundTriangles.Add(triangles[i + 2]);
            }
            else removed++;
        }
        if (groundTriangles.Count != 525 * 3 || removed != 246)
            throw new InvalidOperationException("Contagem de triangulos sc1_08_ground inesperada: " +
                groundTriangles.Count / 3 + " no solo, " + removed + " elevados.");

        Directory.CreateDirectory(Path.GetDirectoryName(GroundMesh));
        Mesh replacement = Object.Instantiate(original);
        replacement.name = "sc1_08_ground_without_floating_panels";
        replacement.SetTriangles(groundTriangles, 9, false);
        AssetDatabase.CreateAsset(replacement, GroundMesh);
        return replacement;
    }
}
