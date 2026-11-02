using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Keep the verified supplemental faces when the original IPL sectors are rebuilt.
public static class LosSantosCollisionPatchCatalog
{
    const string CatalogPath = "Assets/GTASanAndreasModFull/CollisionRepairs/catalog.json";
    [Serializable] sealed class Catalog { public Entry[] items; }
    [Serializable] sealed class Entry
    {
        public string sector, instance, sourceMesh, sourceAsset, patchAsset, patchNode;
    }

    public static void Capture()
    {
        CaptureFromReport("../tools/los-santos-repair/surface-collision-repair.json");
    }

    public static void CaptureFromReport(string reportPath)
    {
        var report = JsonUtility.FromJson<Catalog>(File.ReadAllText(reportPath));
        if (report?.items == null) throw new Exception("Relatório de colisões suplementares ausente.");
        var previous = File.Exists(CatalogPath) ? JsonUtility.FromJson<Catalog>(File.ReadAllText(CatalogPath)) : null;
        var catalog = new Catalog { items = (previous?.items ?? Array.Empty<Entry>()).Concat(report.items).Where(item =>
            !string.IsNullOrEmpty(item.patchAsset) && File.Exists(item.patchAsset))
            .GroupBy(item => item.patchAsset, StringComparer.Ordinal).Select(group => group.Last())
            .OrderBy(item => item.sector, StringComparer.Ordinal).ThenBy(item => item.patchAsset, StringComparer.Ordinal).ToArray() };
        Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath));
        string text = JsonUtility.ToJson(catalog, true);
        if (!File.Exists(CatalogPath) || File.ReadAllText(CatalogPath) != text)
        {
            File.WriteAllText(CatalogPath, text);
            AssetDatabase.ImportAsset(CatalogPath, ImportAssetOptions.ForceSynchronousImport);
        }
        Debug.Log("LOS_SANTOS_COLLISION_CATALOG_READY patches=" + catalog.items.Length);
    }

    public static void ApplyForScene(Scene scene, string sectorName)
    {
        if (!File.Exists(CatalogPath)) return;
        var catalog = JsonUtility.FromJson<Catalog>(File.ReadAllText(CatalogPath));
        if (catalog?.items == null) throw new Exception("Catálogo de reparos de colisão inválido.");
        var entries = catalog.items.Where(item => item.sector == sectorName).ToArray();
        if (entries.Length == 0) return;
        var transforms = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
        foreach (var entry in entries)
        {
            var placements = transforms.Where(t => t.name == entry.instance).ToArray();
            if (placements.Length != 1) throw new Exception("Instância de piso restaurado mudou: " + sectorName + "/" + entry.instance);
            var filters = placements[0].GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh &&
                f.sharedMesh.name == entry.sourceMesh && AssetDatabase.GetAssetPath(f.sharedMesh) == entry.sourceAsset).ToArray();
            if (filters.Length != 1) throw new Exception("Malha de piso restaurado mudou: " + entry.sourceAsset);
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(entry.patchAsset);
            if (!mesh) throw new Exception("Colisão de reparo ausente: " + entry.patchAsset);
            var filter = filters[0];
            var nodes = Enumerable.Range(0, filter.transform.childCount).Select(i => filter.transform.GetChild(i))
                .Where(t => t.name == entry.patchNode).ToArray();
            if (nodes.Length > 1) throw new Exception("Colisão de reparo duplicada: " + entry.patchNode);
            Transform node = nodes.FirstOrDefault();
            if (!node)
            {
                var child = new GameObject(entry.patchNode);
                SceneManager.MoveGameObjectToScene(child, scene);
                node = child.transform;
                node.SetParent(filter.transform, false);
                child.isStatic = true;
            }
            var colliders = node.GetComponents<MeshCollider>();
            if (colliders.Length > 1) throw new Exception("Componentes de reparo duplicados: " + entry.patchNode);
            var collider = colliders.FirstOrDefault();
            if (!collider) collider = node.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = false; collider.isTrigger = false; collider.enabled = true;
        }
    }
}
