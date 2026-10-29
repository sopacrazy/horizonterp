using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Builds the separate GTA SA test's water from the game's processed water.dat polygons.</summary>
public static class GtaSaWaterMesh
{
    const string MeshName = "GtaSaWaterPolygons.asset";
    const string MaterialName = "GtaSaWater.mat";

    /// <summary>Returns a water-only GameObject. The source file and official game scenes are never changed.</summary>
    public static GameObject Create(string waterDatPath, string generatedFolder)
    {
        if (!File.Exists(waterDatPath)) throw new FileNotFoundException("GTA SA water.dat não encontrado", waterDatPath);
        if (string.IsNullOrWhiteSpace(generatedFolder) || !generatedFolder.Replace('\\', '/').StartsWith("Assets/", StringComparison.Ordinal))
            throw new ArgumentException("generatedFolder deve estar sob Assets/", nameof(generatedFolder));
        Directory.CreateDirectory(generatedFolder);
        var vertices = new List<Vector3>(1300);
        var uv = new List<Vector2>(1300);
        var triangles = new List<int>(2000);
        int polygons = 0;
        foreach (string raw in File.ReadLines(waterDatPath))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.Equals("processed", StringComparison.OrdinalIgnoreCase) || line.StartsWith("#")) continue;
            string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 22 && parts.Length != 29)
                throw new FormatException("water.dat: linha com número inesperado de campos: " + parts.Length);
            int count = (parts.Length - 1) / 7;
            int start = vertices.Count;
            for (int i = 0; i < count; i++)
            {
                int offset = i * 7;
                float x = Read(parts[offset]);
                float y = Read(parts[offset + 1]);
                float z = Read(parts[offset + 2]);
                // GTA SA is Z-up; Unity is Y-up. Match the static DFF/OBJ importer.
                vertices.Add(new Vector3(x, z + .015f, -y));
                // World-space UVs avoid stretching one tile across very large ocean polygons.
                uv.Add(new Vector2(x / 64f, y / 64f));
            }
            // The processed file normally orders quad points bottom-left,
            // bottom-right, top-left, top-right. Some triangle records vary;
            // enforce an upward-facing winding for all polygons.
            AddUpwardTriangle(vertices, triangles, start, start + 1, start + 2);
            if (count == 4)
                AddUpwardTriangle(vertices, triangles, start + 2, start + 1, start + 3);
            polygons++;
        }
        if (polygons == 0) throw new FormatException("water.dat não contém polígonos de água");
        var generated = new Mesh { name = "GTA SA water.dat - " + polygons + " polígonos" };
        generated.SetVertices(vertices); generated.SetUVs(0, uv); generated.SetTriangles(triangles, 0);
        generated.RecalculateNormals(); generated.RecalculateBounds();
        string meshPath = generatedFolder.TrimEnd('/', '\\').Replace('\\', '/') + "/" + MeshName;
        var savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (savedMesh) { EditorUtility.CopySerialized(generated, savedMesh); Object.DestroyImmediate(generated); }
        else { AssetDatabase.CreateAsset(generated, meshPath); savedMesh = generated; }

        string materialPath = generatedFolder.TrimEnd('/', '\\').Replace('\\', '/') + "/" + MaterialName;
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (!material)
        {
            var shader = Shader.Find("Standard");
            if (!shader) throw new InvalidOperationException("Shader Standard indisponível para água GTA SA");
            material = new Material(shader) { name = "Água GTA SA - teste" };
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.SetFloat("_Mode", 3f);
        material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
        material.color = new Color(.06f, .28f, .43f, .90f);
        material.SetFloat("_Metallic", .14f);
        material.SetFloat("_Glossiness", .73f);
        EditorUtility.SetDirty(material);
        EditorUtility.SetDirty(savedMesh);

        var go = new GameObject("Água GTA SA - water.dat - TESTE SEPARADO", typeof(MeshFilter), typeof(MeshRenderer));
        go.GetComponent<MeshFilter>().sharedMesh = savedMesh;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        go.isStatic = true;
        return go;
    }

    static float Read(string text) => float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    static void AddUpwardTriangle(List<Vector3> vertices, List<int> triangles, int a, int b, int c)
    {
        float up = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).y;
        if (up < 0) { int swap = b; b = c; c = swap; }
        triangles.Add(a); triangles.Add(b); triangles.Add(c);
    }
}
