using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Rebuilds only the isolated recovered city. No source scene or shared city material is edited.
public static class GtaRecoveredCityRepair
{
    const string Root = "Assets/GTARecoveredMap";
    const string Output = Root + "/Restored";
    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
    static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
    static readonly List<string> findings = new List<string>();
    static Shader surface;

    public static void Apply(GameObject root)
    {
        Directory.CreateDirectory(Output + "/Materials");
        Directory.CreateDirectory(Output + "/Textures");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        surface = Shader.Find("Horizonte/Recovered City Surface");
        if (!surface) throw new Exception("Shader de recuperacao do mapa ausente.");
        PrepareSourceFiltering();
        materials.Clear(); textures.Clear(); findings.Clear();
        var overlapRemoved = RemoveOverlappingCopies(root);
        var removed = 0; var replaced = 0; var cloned = 0; var colliderCount = 0;
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>().ToArray())
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (!filter || !filter.sharedMesh) { Object.DestroyImmediate(renderer.gameObject); removed++; continue; }
            var mesh = filter.sharedMesh;
            var name = renderer.name.ToLowerInvariant();
            // The exported demo also contains abandoned car parts, UI planes and display props.
            // Some genuine city meshes were exported as Build, Church and Apartment.
            if (!name.StartsWith("sc1") && !mesh.name.StartsWith("sc1", StringComparison.OrdinalIgnoreCase))
            { Object.DestroyImmediate(renderer.gameObject); removed++; continue; }
            if (!name.StartsWith("sc1"))
            {
                renderer.gameObject.name = mesh.name;
                name = mesh.name.ToLowerInvariant();
            }
            var original = renderer.sharedMaterials;
            var result = new Material[mesh.subMeshCount];
            for (int slot = 0; slot < result.Length; slot++)
            {
                var mat = slot < original.Length ? original[slot] : null;
                if (NeedsRebuild(mat))
                {
                    var kind = Category(name, mat ? mat.name.ToLowerInvariant() : "", mesh, slot, renderer.transform);
                    result[slot] = Rebuilt(kind, StableHash(name) % 4);
                    replaced++;
                }
                else
                {
                    result[slot] = Preserve(mat);
                    cloned++;
                }
            }
            renderer.sharedMaterials = result;
            renderer.shadowCastingMode = name.Contains("decal") ? ShadowCastingMode.Off : ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.gameObject.layer = 0;
            renderer.gameObject.isStatic = true;
            var block = new MaterialPropertyBlock();
            block.SetFloat("_BuildingBaseY", renderer.bounds.min.y);
            block.SetFloat("_Seed", (StableHash(name) % 1000) / 1000f);
            renderer.SetPropertyBlock(block);
            bool decorative = name.Contains("decal") || name.Contains("sign") || name.Contains("cloth") || name.Contains("bb_") || name.Contains("billboard");
            if (!decorative)
            {
                var collider = renderer.GetComponent<MeshCollider>();
                if (!collider) collider = renderer.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh; collider.enabled = true;
                collider.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation |
                    MeshColliderCookingOptions.EnableMeshCleaning | MeshColliderCookingOptions.WeldColocatedVertices;
                colliderCount++;
            }
        }
        findings.Add("Reconstructed material slots=" + replaced);
        findings.Add("Preserved textured slots=" + cloned);
        findings.Add("Removed demo objects=" + removed);
        findings.Add("Removed overlapping road/fence copies=" + overlapRemoved);
        findings.Add("Scenery colliders=" + colliderCount);
        findings.Add("Generated materials=" + materials.Count);
        findings.Add("Missing originals were reconstructed by surface type; this is not exact source recovery.");
        File.WriteAllLines("../tools/gta-map-review/material-repair.txt", findings);
    }

    static void PrepareSourceFiltering()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/Texture2D" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!importer || importer.textureType == TextureImporterType.Sprite) continue;
            if (importer.anisoLevel == 8 && importer.filterMode == FilterMode.Trilinear && importer.wrapMode == TextureWrapMode.Repeat) continue;
            importer.anisoLevel = 8; importer.filterMode = FilterMode.Trilinear;
            importer.wrapMode = TextureWrapMode.Repeat; importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }
    }

    static int RemoveOverlappingCopies(GameObject root)
    {
        int removed = 0;
        var renderers = root.GetComponentsInChildren<MeshRenderer>();
        foreach (var group in renderers.GroupBy(r => System.Text.RegularExpressions.Regex.Replace(r.name.ToLowerInvariant(), @"\.\d+$", "")))
        {
            var list = group.Where(r => r.GetComponent<MeshFilter>()).OrderByDescending(r => r.sharedMaterials.Count(m => !NeedsRebuild(m))).ToArray();
            for (int i = 0; i < list.Length; i++)
            {
                if (!list[i]) continue;
                for (int j = i + 1; j < list.Length; j++)
                {
                    if (!list[j]) continue;
                    bool road = group.Key.StartsWith("sc1_rd_r");
                    float tolerance = road ? 1.2f : .06f;
                    var a = list[i].bounds; var b = list[j].bounds;
                    if (Vector3.Distance(a.center, b.center) > tolerance) continue;
                    if ((a.size - b.size).magnitude > Mathf.Max(.1f, a.size.magnitude * .025f)) continue;
                    findings.Add("Overlap removed=" + list[j].name + ", kept=" + list[i].name);
                    Object.DestroyImmediate(list[j].gameObject); removed++;
                }
            }
        }
        return removed;
    }

    static bool NeedsRebuild(Material mat)
    {
        if (!mat || !mat.shader || mat.shader.name.Contains("InternalError") || mat.name.Contains("Recuperacao_Neutro")) return true;
        if (!mat.mainTexture) return true;
        return false;
    }

    static string Category(string name, string material, Mesh mesh, int slot, Transform transform)
    {
        string hint = material + " " + name;
        if (material.Contains("glass") || material.Contains("window")) return "Glass";
        if (hint.Contains("hedge") || hint.Contains("grass")) return "Grass";
        if (hint.Contains("wood") || hint.Contains("picket")) return "Wood";
        if (hint.Contains("ladder") || hint.Contains("pipe") || hint.Contains("rail") || hint.Contains("metal") || hint.Contains("fence") || hint.Contains("gate") || hint.Contains("armco") || hint.Contains("gantry")) return "Metal";
        if (hint.Contains("roof")) return "Roof";
        if (material.Contains("sidewalk") || material.Contains("kerb")) return "Pavement";
        if (material.Contains("road") || material.Contains("tarmac")) return "Asphalt";
        if (name.StartsWith("sc1_rd"))
        {
            // Up-facing road surfaces use asphalt; raised kerbs/vertical strips use concrete.
            var descriptor = mesh.GetSubMesh(slot);
            return descriptor.bounds.size.y > 2 && descriptor.bounds.size.x < 2 ? "Pavement" : "Asphalt";
        }
        if (hint.Contains("ground") || hint.Contains("grnd") || name.EndsWith("grd") || name.Contains("gnd")) return "Ground";
        if (name.Contains("wall") || name.Contains("barrier") || name.Contains("stair") || name.Contains("step") || name.Contains("ramp")) return "Concrete";
        if (name.Contains("sign") || name.Contains("shutr") || name.Contains("decal")) return "Metal";
        if (name.Contains("rocks")) return "Concrete";
        return "Facade";
    }

    static Material Rebuilt(string kind, int variant)
    {
        var key = kind + "_" + variant;
        if (materials.TryGetValue(key, out var existing)) return existing;
        var path = Output + "/Materials/City_" + key + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat) { mat = new Material(surface); AssetDatabase.CreateAsset(mat, path); }
        mat.shader = surface; mat.name = "City_" + key; mat.enableInstancing = true;
        string main = "Concrete", top = "Roof";
        Color tint = Color.white, topTint = new Color(.78f, .8f, .82f);
        float scale = 4, gloss = .13f, metal = 0, facade = 0;
        switch (kind)
        {
            case "Asphalt": main = top = "Asphalt"; scale = 5; tint = new Color(.85f, .85f, .86f); break;
            case "Pavement": main = top = "Concrete"; scale = 3; tint = new Color(.85f, .84f, .80f); break;
            case "Ground": main = top = "Concrete"; scale = 5; tint = new Color(.72f, .71f, .66f); break;
            case "Grass": main = top = "Grass"; scale = 4; tint = new Color(.75f, .86f, .65f); break;
            case "Roof": main = top = "Roof"; scale = 4; tint = new Color(.6f, .62f, .63f); break;
            case "Wood": main = top = "Wood"; scale = 3; tint = new Color(.75f, .72f, .66f); break;
            case "Metal": main = top = "Metal"; scale = 3; tint = new Color(.56f, .59f, .60f); metal = .65f; gloss = .28f; break;
            case "Glass": main = top = "Concrete"; tint = new Color(.12f, .20f, .25f); gloss = .72f; metal = .4f; break;
            case "Facade":
                main = variant == 0 ? "Brick" : "Concrete"; facade = 1;
                tint = new[] { new Color(.78f, .73f, .67f), new Color(.91f, .85f, .73f), new Color(.73f, .78f, .79f), new Color(.82f, .76f, .65f) }[variant];
                scale = variant == 0 ? 4 : 5; break;
        }
        if (main == top) topTint = tint;
        mat.SetTexture("_MainTex", Texture(main)); mat.SetTexture("_TopTex", Texture(top));
        mat.SetColor("_Color", tint); mat.SetColor("_TopColor", topTint);
        mat.SetFloat("_MetersPerTile", scale); mat.SetFloat("_TopMetersPerTile", scale);
        mat.SetFloat("_Smoothness", gloss); mat.SetFloat("_Metallic", metal); mat.SetFloat("_Facade", facade);
        EditorUtility.SetDirty(mat); materials[key] = mat;
        return mat;
    }

    static Material Preserve(Material original)
    {
        string key = "Original_" + original.name;
        if (materials.TryGetValue(key, out var found)) return found;
        string file = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));
        string path = Output + "/Materials/" + file + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat) { mat = new Material(original); AssetDatabase.CreateAsset(mat, path); }
        else mat.CopyPropertiesFromMaterial(original);
        mat.name = key; mat.enableInstancing = true;
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", Mathf.Min(.3f, mat.GetFloat("_Glossiness")));
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0);
        // Preserve source UVs, albedo and cutout/transparency settings on intact surfaces.
        EditorUtility.SetDirty(mat); materials[key] = mat; return mat;
    }

    static Texture2D Texture(string kind)
    {
        if (textures.TryGetValue(kind, out var found)) return found;
        string source;
        switch (kind)
        {
            case "Asphalt": source = "Assets/DowntownCity/Textures/T_Concrete_Asphalt_BaseColor.png"; break;
            case "Brick": source = "Assets/DowntownCity/Textures/T_RedBrick_BaseColor.png"; break;
            case "Roof": source = "Assets/DowntownCity/Textures/T_RoofSlate_BaseColor.png"; break;
            case "Metal": source = "Assets/DowntownCity/Textures/T_MetalConcrete_BaseColor.png"; break;
            case "Grass": source = Root + "/Texture2D/im_grass04.png"; break;
            case "Wood": source = Root + "/Texture2D/rnbj_wood_004.png"; break;
            default: source = "Assets/DowntownCity/Textures/T_Concrete_BaseColor.png"; break;
        }
        string path = Output + "/Textures/" + kind + ".png";
        if (!File.Exists(path)) File.Copy(source, path);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer.anisoLevel != 8 || importer.wrapMode != TextureWrapMode.Repeat || importer.maxTextureSize != 2048)
        {
            importer.anisoLevel = 8; importer.wrapMode = TextureWrapMode.Repeat; importer.maxTextureSize = 2048;
            importer.mipmapEnabled = true; importer.filterMode = FilterMode.Trilinear;
            importer.textureCompression = TextureImporterCompression.CompressedHQ; importer.SaveAndReimport();
        }
        found = AssetDatabase.LoadAssetAtPath<Texture2D>(path); textures[kind] = found; return found;
    }

    static int StableHash(string value)
    { unchecked { uint h = 2166136261; foreach (char c in value) h = (h ^ c) * 16777619; return (int)(h & 0x7fffffff); } }
}
