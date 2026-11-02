using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Applies the narrowly scoped repairs only after recording their actual starting state.
// All probe data is reacquired from saved sector scenes between repair passes.
public static class LosSantosRepairVerification
{
    const string Sectors = "Assets/Scenes/GTASanAndreasModFullSectors/";
    const string Output = "../tools/los-santos-repair";
    const float Tolerance = .1f;

    sealed class Target
    {
        public readonly string model, sector;
        public readonly Vector3 position;
        public Target(string model, string sector, Vector3 position = default) { this.model = model; this.sector = sector; this.position = position; }
    }
    static readonly Target[] GroundTargets = {
        new Target("stordrablas2", "GTASASector_p10_m8"),
        new Target("stormd_fille", "GTASASector_p9_m8"),
        new Target("libtwrhelipda_lan2", "GTASASector_p6_m6")
    };
    static readonly Target[] StructureTargets = {
        new Target("mlamppost #1294", "GTASASector_p5_m4", new Vector3(1378.59375f, 43.625f, 869.6953125f)),
        new Target("MTraffic2 #1284", "GTASASector_p2_m7", new Vector3(639.484375f, 15.4609375f, 1745.40625f))
    };

    [Serializable] sealed class Report
    {
        public string generatedUtc, method;
        public bool passed, idempotent;
        public int firstAddedColliders, firstDisabledCopies, secondAddedColliders, secondDisabledCopies;
        public GroundResult[] before, after, repeated;
        public StructureResult[] structuresBefore, structuresAfter, structuresRepeated;
        public FileResult[] filesAfterRepeat;
        public string[] errors;
    }
    [Serializable] sealed class GroundResult
    {
        public string model, sector, instance;
        public int visibleMeshFilters, colliderComponents, validMatchingMeshColliders, horizontalTriangles;
        public int supportedByTarget, supportedByWorld, missingTarget, missingWorld, degenerateTriangles;
        public bool exactlyOneColliderPerVisibleFilter;
        public Vector3 position;
        public Bounds bounds;
        public FailedSample[] examples;
    }
    [Serializable] sealed class FailedSample
    {
        public Vector3 position;
        public float normalY;
        public string mesh;
        public bool targetSupported, worldSupported;
    }
    [Serializable] sealed class StructureResult
    {
        public string sector, objectName;
        public int matchingObjects, activeObjects, inactiveObjects, totalColliders, activeColliders;
    }
    [Serializable] sealed class FileResult
    {
        public string scene, firstSha256, secondSha256;
        public bool unchanged;
    }
    // Read just the totals from each repair's own JSON report.
    [Serializable] sealed class RepairTotals
    {
        public int addedColliders, disabledCopies;
    }

    [MenuItem("Horizonte/Los Santos/Aplicar e verificar reparos auditados")]
    public static void Targeted()
    {
        Directory.CreateDirectory(Output);
        var setup = EditorSceneManager.GetSceneManagerSetup();
        bool backfaces = Physics.queriesHitBackfaces;
        var errors = new List<string>();
        var report = new Report {
            generatedUtc = DateTime.UtcNow.ToString("O"),
            method = "Reload saved target sectors before each measurement. Every nondegenerate triangle with world normal.y > 0.72 is probed at its centroid, with target collider and world collider tolerance 0.1m. Require one exact non-trigger MeshCollider per visible MeshFilter, one active copy per exact duplicate, and unchanged scene SHA256 after repeating both repairs."
        };
        try
        {
            Physics.queriesHitBackfaces = false;
            report.before = MeasureGround();
            report.structuresBefore = MeasureStructures();
            LosSantosCollisionRepair.Apply();
            LosSantosStructureRepair.Repair();
            var firstCollision = ReadTotals("collision-repair.json");
            var firstStructure = ReadTotals("structure-repair.json");
            report.firstAddedColliders = firstCollision.addedColliders;
            report.firstDisabledCopies = firstStructure.disabledCopies;
            // Preserve first pass evidence; the repair classes overwrite their current report on a no-op rerun.
            File.Copy(Path.Combine(Output, "collision-repair.json"), Path.Combine(Output, "collision-repair-first-pass.json"), true);
            File.Copy(Path.Combine(Output, "structure-repair.json"), Path.Combine(Output, "structure-repair-first-pass.json"), true);
            report.after = MeasureGround();
            report.structuresAfter = MeasureStructures();
            VerifyGround(report.after, "first pass", errors);
            VerifyStructures(report.structuresAfter, "first pass", errors);
            string[] sceneNames = GroundTargets.Concat(StructureTargets).Select(t => t.sector).Distinct().ToArray();
            var firstHashes = sceneNames.ToDictionary(n => n, n => Hash(Sectors + n + ".unity"), StringComparer.Ordinal);

            LosSantosCollisionRepair.Apply();
            LosSantosStructureRepair.Repair();
            report.secondAddedColliders = ReadTotals("collision-repair.json").addedColliders;
            report.secondDisabledCopies = ReadTotals("structure-repair.json").disabledCopies;
            report.repeated = MeasureGround();
            report.structuresRepeated = MeasureStructures();
            VerifyGround(report.repeated, "repeat pass", errors);
            VerifyStructures(report.structuresRepeated, "repeat pass", errors);
            report.filesAfterRepeat = sceneNames.Select(n => {
                string second = Hash(Sectors + n + ".unity");
                return new FileResult { scene = n, firstSha256 = firstHashes[n], secondSha256 = second, unchanged = firstHashes[n] == second };
            }).ToArray();
            bool sameCounts = report.after.Length == report.repeated.Length && report.after.Zip(report.repeated, (a, b) =>
                a.model == b.model && a.colliderComponents == b.colliderComponents && a.validMatchingMeshColliders == b.validMatchingMeshColliders &&
                a.horizontalTriangles == b.horizontalTriangles && a.supportedByTarget == b.supportedByTarget && a.supportedByWorld == b.supportedByWorld).All(v => v);
            bool sameStructures = report.structuresAfter.Length == report.structuresRepeated.Length &&
                report.structuresAfter.Zip(report.structuresRepeated, (a, b) => a.sector == b.sector && a.matchingObjects == b.matchingObjects &&
                    a.activeObjects == b.activeObjects && a.totalColliders == b.totalColliders && a.activeColliders == b.activeColliders).All(v => v);
            report.idempotent = sameCounts && sameStructures && report.filesAfterRepeat.All(f => f.unchanged) &&
                report.secondAddedColliders == 0 && report.secondDisabledCopies == 0;
            if (!report.idempotent) errors.Add("Repeating both repairs changed collider counts, active copies or saved sector bytes.");
        }
        catch (Exception exception)
        {
            errors.Add(exception.ToString());
            Debug.LogException(exception);
        }
        finally
        {
            Physics.queriesHitBackfaces = backfaces;
            try
            {
                if (setup.Length > 0 && setup.All(s => !string.IsNullOrEmpty(s.path))) EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            catch (Exception exception) { errors.Add("Scene setup restore: " + exception.Message); }
            report.errors = errors.ToArray();
            report.passed = errors.Count == 0 && report.idempotent;
            File.WriteAllText(Path.Combine(Output, "focused-before-after.json"), JsonUtility.ToJson(report, true));
        }
        if (!report.passed) throw new Exception("LOS_SANTOS_FOCUSED_VERIFICATION_FAILED: " + string.Join(" | ", errors));
        Debug.Log("LOS_SANTOS_FOCUSED_VERIFICATION_OK samples=" + report.after.Sum(r => r.horizontalTriangles) +
            " addedColliders=" + report.firstAddedColliders + " disabledCopies=" + report.firstDisabledCopies + " idempotent=true");
    }

    static GroundResult[] MeasureGround()
    {
        // Opening from disk also avoids measuring stale objects restored by an inner repair call.
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var scenes = GroundTargets.Select(t => t.sector).Distinct().ToDictionary(n => n,
            n => EditorSceneManager.OpenScene(Sectors + n + ".unity", OpenSceneMode.Additive), StringComparer.Ordinal);
        Physics.SyncTransforms();
        var results = new List<GroundResult>();
        foreach (var target in GroundTargets)
        {
            var matches = scenes[target.sector].GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .Where(t => t.name.StartsWith(target.model + " #", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1) throw new Exception("Expected one target placement: " + target.model + " actual=" + matches.Length);
            var placement = matches[0];
            var result = new GroundResult { model = target.model, sector = target.sector, instance = placement.name,
                position = placement.position, colliderComponents = placement.GetComponentsInChildren<Collider>(true).Length,
                exactlyOneColliderPerVisibleFilter = true };
            var samples = new List<FailedSample>();
            var targetColliders = placement.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger).ToArray();
            bool bounded = false;
            foreach (var filter in placement.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (!filter.sharedMesh || !renderer || !renderer.enabled || !filter.gameObject.activeInHierarchy) continue;
                result.visibleMeshFilters++;
                if (!bounded) { result.bounds = renderer.bounds; bounded = true; } else result.bounds.Encapsulate(renderer.bounds);
                var onFilter = filter.GetComponents<MeshCollider>();
                bool exact = onFilter.Length == 1 && onFilter[0].sharedMesh == filter.sharedMesh && !onFilter[0].convex &&
                    !onFilter[0].isTrigger && onFilter[0].enabled;
                result.exactlyOneColliderPerVisibleFilter &= exact;
                result.validMatchingMeshColliders += onFilter.Count(c => c.sharedMesh == filter.sharedMesh && c.enabled && !c.isTrigger && !c.convex);
                using (var data = MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh))
                using (var vertices = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp))
                {
                    data[0].GetVertices(vertices);
                    for (int submesh = 0; submesh < data[0].subMeshCount; submesh++)
                    {
                        var descriptor = data[0].GetSubMesh(submesh);
                        if (descriptor.topology != MeshTopology.Triangles) continue;
                        using (var indices = new NativeArray<int>(descriptor.indexCount, Allocator.Temp))
                        {
                            data[0].GetIndices(indices, submesh);
                            for (int i = 0; i + 2 < indices.Length; i += 3)
                            {
                                Vector3 a = filter.transform.TransformPoint(vertices[indices[i]]);
                                Vector3 b = filter.transform.TransformPoint(vertices[indices[i + 1]]);
                                Vector3 c = filter.transform.TransformPoint(vertices[indices[i + 2]]);
                                Vector3 cross = Vector3.Cross(b - a, c - a);
                                if (cross.sqrMagnitude < 1e-10f) { result.degenerateTriangles++; continue; }
                                float normalY = cross.normalized.y;
                                if (normalY <= .72f) continue;
                                Vector3 point = (a + b + c) / 3;
                                result.horizontalTriangles++;
                                var ray = new Ray(point + Vector3.up * (Tolerance + .01f), Vector3.down);
                                bool own = targetColliders.Any(collider => collider.Raycast(ray, out var hit, Tolerance * 2 + .02f) &&
                                    Mathf.Abs(hit.point.y - point.y) <= Tolerance && hit.normal.y > .72f);
                                bool world = Physics.RaycastAll(ray, Tolerance * 2 + .02f, ~0, QueryTriggerInteraction.Ignore)
                                    .Any(hit => Mathf.Abs(hit.point.y - point.y) <= Tolerance && hit.normal.y > .72f);
                                if (own) result.supportedByTarget++; else result.missingTarget++;
                                if (world) result.supportedByWorld++; else result.missingWorld++;
                                if ((!own || !world) && samples.Count < 64)
                                    samples.Add(new FailedSample { position = point, normalY = normalY, mesh = filter.sharedMesh.name,
                                        targetSupported = own, worldSupported = world });
                            }
                        }
                    }
                }
            }
            result.examples = samples.ToArray();
            results.Add(result);
            Debug.Log("LOS_SANTOS_TARGET_PROBE model=" + result.model + " triangles=" + result.horizontalTriangles +
                " targetSupported=" + result.supportedByTarget + " worldSupported=" + result.supportedByWorld + " colliders=" + result.colliderComponents);
        }
        return results.ToArray();
    }

    static StructureResult[] MeasureStructures()
    {
        var results = new List<StructureResult>();
        foreach (var target in StructureTargets)
        {
            var scene = EditorSceneManager.OpenScene(Sectors + target.sector + ".unity", OpenSceneMode.Single);
            var matches = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .Where(t => t.name == target.model && Vector3.Distance(t.position, target.position) < .01f).ToArray();
            var colliders = matches.SelectMany(t => t.GetComponentsInChildren<Collider>(true)).ToArray();
            results.Add(new StructureResult { sector = target.sector, objectName = target.model,
                matchingObjects = matches.Length, activeObjects = matches.Count(t => t.gameObject.activeSelf),
                inactiveObjects = matches.Count(t => !t.gameObject.activeSelf), totalColliders = colliders.Length,
                activeColliders = colliders.Count(c => c.enabled && c.gameObject.activeInHierarchy) });
        }
        return results.ToArray();
    }

    static void VerifyGround(GroundResult[] values, string phase, List<string> errors)
    {
        if (values.Length != GroundTargets.Length) errors.Add(phase + ": target count mismatch.");
        foreach (var value in values)
        {
            if (value.horizontalTriangles == 0 || value.visibleMeshFilters == 0)
                errors.Add(phase + ": no horizontal surface measured for " + value.model);
            if (value.missingTarget != 0 || value.missingWorld != 0)
                errors.Add(phase + ": unsupported faces in " + value.model + " target=" + value.missingTarget + " world=" + value.missingWorld);
            if (!value.exactlyOneColliderPerVisibleFilter || value.validMatchingMeshColliders != value.visibleMeshFilters || value.colliderComponents != value.visibleMeshFilters)
                errors.Add(phase + ": expected exactly one collider per visible MeshFilter in " + value.model);
        }
    }
    static void VerifyStructures(StructureResult[] values, string phase, List<string> errors)
    {
        if (values.Length != StructureTargets.Length) errors.Add(phase + ": structure target count mismatch.");
        foreach (var value in values)
            if (value.matchingObjects != 2 || value.activeObjects != 1 || value.inactiveObjects != 1)
                errors.Add(phase + ": expected two editable copies with exactly one active: " + value.objectName);
    }
    static RepairTotals ReadTotals(string file) => JsonUtility.FromJson<RepairTotals>(File.ReadAllText(Path.Combine(Output, file)));
    static string Hash(string path)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }
}
