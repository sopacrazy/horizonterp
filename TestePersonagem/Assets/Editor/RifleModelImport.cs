using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Per-asset setup only. The source rifle is authored in metres with its pivot
// at the firing hand. Do not resize it from character or renderer bounds.
public static class RifleModelImport
{
    const string Folder = "Assets/Resources/Weapons/Rifle";
    const string ModelPath = Folder + "/HeavyRifle.fbx";

    [MenuItem("Horizonte/Armas/Preparar rifle importado")]
    public static void Prepare()
    {
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (!importer) throw new Exception("Rifle fornecido ausente: " + ModelPath);
        bool changed = importer.importAnimation || importer.importCameras || importer.importLights ||
            importer.addCollider || importer.animationType != ModelImporterAnimationType.None ||
            !Mathf.Approximately(importer.globalScale, 1f);
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.importCameras = false;
        importer.importLights = false;
        importer.addCollider = false;
        importer.globalScale = 1f;
        if (changed) importer.SaveAndReimport();

        string[] names = { "w_ar_heavyrifleh", "w_ar_heavyrifleh_stock", "w_ar_heavyrifleh_mag1" };
        var materials = names.Select(MaterialFor).ToArray();
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (!source) throw new Exception("Falha ao importar rifle FBX.");
        var root = new GameObject("Rifle");
        try
        {
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(source);
            visual.name = "HeavyRifle";
            visual.transform.SetParent(root.transform, false);
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                int material = renderer.name.StartsWith("Magazine", StringComparison.Ordinal) ? 2 :
                    renderer.name == "RifleBody2" ? 1 : 0;
                renderer.sharedMaterials = new[] { materials[material] };
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            Socket(root.transform, visual.transform, "GripLeft", "SupportGrip");
            // Grip the rear portion of the handguard, leaving the support
            // elbow bent on both characters instead of stretching to its tip.
            var support=root.transform.Find("SupportGrip");
            support.localPosition=new Vector3(support.localPosition.x,.055f,.20f);
            Socket(root.transform, visual.transform, "Muzzle", "Muzzle");
            Socket(root.transform, visual.transform, "Stock", "Stock");
            var bounds = new Bounds(root.transform.position, Vector3.zero);
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
            if (bounds.size.z < .6f || bounds.size.z > 1.2f || bounds.size.y > .6f)
                throw new Exception("Escala/eixo inesperado do rifle: " + bounds.size);
            PrefabUtility.SaveAsPrefabAsset(root, Folder + "/Rifle.prefab");
            AssetDatabase.SaveAssets();
            Debug.Log("RIFLE_MODEL_READY size=" + bounds.size + " muzzle=" + root.transform.Find("Muzzle").localPosition +
                " support=" + root.transform.Find("SupportGrip").localPosition);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static Material MaterialFor(string name)
    {
        string texturePath = Folder + "/" + name + ".png";
        AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer && (importer.maxTextureSize != 1024 || !importer.mipmapEnabled || importer.isReadable))
        {
            importer.maxTextureSize = 1024;
            importer.mipmapEnabled = true;
            importer.isReadable = false;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
        }
        string materialPath = Folder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (!material)
        {
            material = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        material.color = new Color(.24f, .26f, .28f, 1f);
        material.SetFloat("_Metallic", .25f);
        material.SetFloat("_Glossiness", .32f);
        EditorUtility.SetDirty(material);
        return material;
    }

    static void Socket(Transform root, Transform visual, string sourceName, string outputName)
    {
        var source = visual.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == sourceName);
        if (!source) throw new Exception("Socket do rifle ausente: " + sourceName);
        var socket = new GameObject(outputName).transform;
        socket.SetParent(root, false);
        socket.localPosition = root.InverseTransformPoint(source.position);
        socket.localRotation = Quaternion.identity;
    }
}
