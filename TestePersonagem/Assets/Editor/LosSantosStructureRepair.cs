using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Only exact, independently verified IPL duplicates are suppressed. Source
// objects remain editable; existing stacked favela houses are never moved.
public static class LosSantosStructureRepair
{
    const string SectorFolder = "Assets/Scenes/GTASanAndreasModFullSectors";
    const string ReportFolder = "../tools/los-santos-repair";

    sealed class Target
    {
        public string sector, objectName, retainedSource, redundantSource;
        public Vector3 position;
    }

    static readonly Target[] Targets = {
        new Target {
            sector = "GTASASector_p5_m4", objectName = "mlamppost #1294",
            position = new Vector3(1378.59375f, 43.625f, 869.6953125f),
            retainedSource = "lawn_stream3.ipl#181", redundantSource = "lahills_stream2.ipl#52"
        },
        new Target {
            sector = "GTASASector_p2_m7", objectName = "MTraffic2 #1284",
            position = new Vector3(639.484375f, 15.4609375f, 1745.40625f),
            retainedSource = "law_stream4.ipl#81", redundantSource = "law2_stream0.ipl#141"
        }
    };

    [Serializable] public sealed class DuplicateResult
    {
        public string sector, objectName, sourceA, sourceB;
        public Vector3 position;
        public int matchingObjects, activeBefore, activeAfter, disabledCopies;
        public bool idempotent, retainedEditable;
    }

    [Serializable] sealed class HouseReview
    {
        public string source, issue, action;
        public Vector3 position, cameraPosition, cameraTarget;
        public float platformAbovePivotM;
    }

    [Serializable] sealed class Report
    {
        public string generatedUtc, backupDirectory, method;
        public int changedScenes, disabledCopies;
        public DuplicateResult[] duplicates;
        public HouseReview[] housesForVisualReview;
    }

    [MenuItem("Horizonte/Los Santos/Corrigir duas estruturas duplicadas verificadas")]
    public static void Repair()
    {
        Directory.CreateDirectory(ReportFolder);
        string backup = Path.GetFullPath(Path.Combine(ReportFolder, "backup",
            "structures-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")));
        Directory.CreateDirectory(backup);
        var results = new List<DuplicateResult>();
        int changedScenes = 0;
        foreach (string name in Targets.Select(target => target.sector).Distinct())
        {
            string path = SectorFolder + "/" + name + ".unity";
            if (!File.Exists(path)) throw new FileNotFoundException("Setor de Los Santos ausente", path);
            File.Copy(path, Path.Combine(backup, Path.GetFileName(path)), false);
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            DuplicateResult[] sceneResults = ApplyDuplicateOverrides(scene, name);
            results.AddRange(sceneResults);
            if (sceneResults.Any(result => result.disabledCopies > 0))
            {
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Falha ao salvar " + path);
                changedScenes++;
            }
        }
        var report = new Report {
            generatedUtc = DateTime.UtcNow.ToString("O"), backupDirectory = backup,
            method = "Suppress one redundant editable instance only when model, position, rotation, scale and prefab match. No houses are relocated.",
            changedScenes = changedScenes, disabledCopies = results.Sum(result => result.disabledCopies),
            duplicates = results.ToArray(), housesForVisualReview = HouseReviewPoints()
        };
        File.WriteAllText(Path.Combine(ReportFolder, "structure-repair.json"), JsonUtility.ToJson(report, true));
        Debug.Log("LOS_SANTOS_STRUCTURES_REPAIRED disabled=" + report.disabledCopies +
            " changedScenes=" + changedScenes + " backup=" + backup);
    }

    // Called by the scene generator too, so regeneration preserves the fix.
    // A supplied name is required before a newly created scene has been saved.
    public static DuplicateResult[] ApplyDuplicateOverrides(Scene scene, string sectorName = null)
    {
        sectorName = sectorName ?? Path.GetFileNameWithoutExtension(scene.path);
        var results = new List<DuplicateResult>();
        foreach (Target target in Targets.Where(target => target.sector == sectorName))
        {
            Transform[] matches = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Where(transform => transform.name == target.objectName &&
                    Vector3.Distance(transform.position, target.position) < .01f)
                .OrderBy(transform => transform.GetSiblingIndex()).ToArray();
            if (matches.Length != 2)
                throw new InvalidOperationException("Par duplicado mudou: " + target.objectName +
                    " em " + sectorName + " count=" + matches.Length);
            Transform first = matches[0], second = matches[1];
            var firstSource = PrefabUtility.GetCorrespondingObjectFromOriginalSource(first.gameObject);
            var secondSource = PrefabUtility.GetCorrespondingObjectFromOriginalSource(second.gameObject);
            if (!firstSource || firstSource != secondSource ||
                Quaternion.Angle(first.rotation, second.rotation) > .01f ||
                Vector3.Distance(first.lossyScale, second.lossyScale) > .0001f)
                throw new InvalidOperationException("Objetos não são cópias idênticas: " + target.objectName);
            int before = matches.Count(transform => transform.gameObject.activeSelf);
            if (before == 0)
                throw new InvalidOperationException("Ambas as cópias já foram desativadas: " + target.objectName);
            // Preserve the already active copy when re-running this repair.
            Transform keep = matches.First(transform => transform.gameObject.activeSelf);
            int disabled = 0;
            foreach (Transform match in matches)
            {
                bool active = match == keep;
                if (match.gameObject.activeSelf && !active) disabled++;
                if (match.gameObject.activeSelf != active) match.gameObject.SetActive(active);
            }
            results.Add(new DuplicateResult {
                sector = sectorName, objectName = target.objectName, position = target.position,
                sourceA = target.retainedSource, sourceB = target.redundantSource,
                matchingObjects = matches.Length, activeBefore = before,
                activeAfter = matches.Count(transform => transform.gameObject.activeSelf),
                disabledCopies = disabled, idempotent = before == 1 && disabled == 0,
                retainedEditable = true
            });
        }
        return results.ToArray();
    }

    static HouseReview[] HouseReviewPoints()
    {
        // Proven source intersections with the observatory plane, not inferred
        // errors from a house pivot alone. Ground/stacked levels need pictures.
        var houses = new[] {
            (44, new Vector3(1173.25f, 61.6625f, 2075.14f), 6.346f),
            (45, new Vector3(1179.8981f, 64.6625f, 2075.224f), 3.344f),
            (61, new Vector3(1198.66f, 64.6625f, 2076.16f), 3.344f),
            (42, new Vector3(1162.9907f, 65.5625f, 2074.1172f), 2.447f)
        };
        return houses.Select(house => new HouseReview {
            source = "PackFavelaNatan.IPL#" + house.Item1,
            position = house.Item2, platformAbovePivotM = house.Item3,
            issue = "Visual source geometry intersects griffithoblas observatory plane.",
            action = "Capture and inspect stacked houses and access before changing their heights.",
            cameraPosition = house.Item2 + new Vector3(-14f, 12f, 20f),
            cameraTarget = house.Item2 + Vector3.up * 5f
        }).ToArray();
    }
}
