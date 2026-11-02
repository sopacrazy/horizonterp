using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// These source COL records are empty even though their visible meshes include
// solid floors. Keep this allow-list narrow: alpha overlays are not ground.
public static class LosSantosCollisionRepair
{
    const string SectorFolder = "Assets/Scenes/GTASanAndreasModFullSectors";
    const string ReportFolder = "../tools/los-santos-repair";

    sealed class Target
    {
        public readonly string model, sector;
        public Target(string model, string sector) { this.model = model; this.sector = sector; }
    }

    static readonly Target[] Targets = {
        new Target("stordrablas2", "GTASASector_p10_m8"),
        new Target("stormd_fille", "GTASASector_p9_m8"),
        new Target("libtwrhelipda_lan2", "GTASASector_p6_m6")
    };

    [Serializable] sealed class Item
    {
        public string model, scene, objectName;
        public int existingColliders, addedColliders, triangles;
        public Vector3 position;
        public Bounds bounds;
    }

    [Serializable] sealed class Report
    {
        public string backup;
        public int inspected, changedObjects, addedColliders;
        public Item[] items;
    }

    public static bool NeedsVisualCollision(string modelName) =>
        Targets.Any(target => string.Equals(target.model, modelName, StringComparison.OrdinalIgnoreCase));

    [MenuItem("Horizonte/Los Santos/Reparar colisoes de pisos auditados")]
    public static void Apply()
    {
        // Save a copy before opening any scene. Both Windows and Android use
        // these same sector files, so no duplicate city scene needs editing.
        string backup = Path.GetFullPath(ReportFolder + "/collision-backup-" +
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(backup);
        foreach (string sector in Targets.Select(target => target.sector).Distinct())
        {
            string path = SectorFolder + "/" + sector + ".unity";
            if (!File.Exists(path)) throw new FileNotFoundException("Setor ausente", path);
            File.Copy(path, Path.Combine(backup, sector + ".unity"), false);
        }

        var setup = EditorSceneManager.GetSceneManagerSetup();
        var results = new List<Item>();
        try
        {
            foreach (var group in Targets.GroupBy(target => target.sector))
            {
                string path = SectorFolder + "/" + group.Key + ".unity";
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                bool changed = false;
                foreach (var target in group)
                {
                    var matches = scene.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                        .Where(transform => transform.name.StartsWith(target.model + " #", StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    if (matches.Length != 1)
                        throw new InvalidOperationException("Instancia auditada inesperada: " + target.model + " count=" + matches.Length);

                    var placement = matches[0];
                    var existing = placement.GetComponentsInChildren<Collider>(true);
                    var item = new Item {
                        model = target.model, scene = path, objectName = placement.name,
                        position = placement.position, existingColliders = existing.Length
                    };
                    bool hasBounds = false;
                    foreach (var filter in placement.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var mesh = filter.sharedMesh;
                        var renderer = filter.GetComponent<MeshRenderer>();
                        if (!mesh || !renderer || !renderer.enabled || !filter.gameObject.activeInHierarchy) continue;
                        if (!hasBounds) { item.bounds = renderer.bounds; hasBounds = true; }
                        else item.bounds.Encapsulate(renderer.bounds);
                        for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                            item.triangles += (int)mesh.GetIndexCount(submesh) / 3;

                        // Preserve every original collider; a second run adds
                        // nothing, and an imported repaired scene is a no-op.
                        if (existing.Length > 0) continue;
                        var collider = filter.gameObject.AddComponent<MeshCollider>();
                        collider.sharedMesh = mesh;
                        collider.convex = false;
                        collider.isTrigger = false;
                        item.addedColliders++;
                    }
                    if (!hasBounds || item.triangles == 0)
                        throw new InvalidOperationException("Malha visivel de piso ausente: " + target.model);
                    if (existing.Length == 0 && item.addedColliders == 0)
                        throw new InvalidOperationException("Nenhuma colisao adicionada: " + target.model);
                    changed |= item.addedColliders > 0;
                    results.Add(item);
                }
                if (changed)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene, path)) throw new IOException("Falha ao salvar " + path);
                }
            }
            var report = new Report {
                backup = backup, inspected = results.Count,
                changedObjects = results.Count(item => item.addedColliders > 0),
                addedColliders = results.Sum(item => item.addedColliders), items = results.ToArray()
            };
            File.WriteAllText(Path.GetFullPath(ReportFolder + "/collision-repair.json"), JsonUtility.ToJson(report, true));
            Debug.Log("LOS_SANTOS_COLLISION_REPAIR_OK inspected=" + report.inspected +
                " changed=" + report.changedObjects + " colliders=" + report.addedColliders + " backup=" + backup);
        }
        finally
        {
            if (setup.Length > 0 && setup.All(s => !string.IsNullOrEmpty(s.path)))
                EditorSceneManager.RestoreSceneManagerSetup(setup);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }
}
