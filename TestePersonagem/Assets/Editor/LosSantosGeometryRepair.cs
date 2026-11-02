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
using Object = UnityEngine.Object;

// Install clipped original ground faces, retaining their source UVs and texture slots.
// The bundled manifest also allows a regenerated sector to restore its repairs.
public static class LosSantosGeometryRepair
{
    const string Generated = "../tools/los-santos-repair/generated";
    const string ManifestName = "geometry-repair-manifest.json";
    const string Folder = "Assets/GTASanAndreasModFull/GroundRepairs";
    const string Sectors = "Assets/Scenes/GTASanAndreasModFullSectors/";
    const string Output = "../tools/los-santos-repair";
    const string Prefix = "REPARO CHAO — ";

    [Serializable] sealed class Manifest { public int formatVersion; public string coordinateSystem; public Patch[] patches; }
    [Serializable] sealed class Patch
    {
        public string id, sector, objFile, mtlFile, materialsFile, sourceModel, sourceAsset, sourceIpl;
        public string provenance, sourceObjSha256, clipMethod, surfaceKind;
        public bool reconstructedFromMeasuredBoundary, sourceGeometryRecovered;
        public int[] sourceTriangles;
        public int sourceIndex, triangles, numericSupportFaces;
        public float minimumNormalY, minimumAllowedNormalY, verticalOffset;
        public Vector3 positionUnity;
        public Limits boundsUnity;
        public MaterialDefinition[] materials;
    }
    [Serializable] sealed class Limits { public Vector3 min, max; }
    [Serializable] sealed class MaterialDefinition { public string name, textureAsset; }
    [Serializable] sealed class Sidecar { public MaterialSlot[] materials; }
    [Serializable] sealed class MaterialSlot { public string material; public float[] color; }
    [Serializable] sealed class Report
    {
        public string generatedUtc, backupDirectory, sourceManifest, method;
        public int patches, addedInstances, changedScenes, changedAssets, changedStreamers, triangles, colliders, verifiedSamples;
        public bool passed;
        public Result[] results;
        public string[] errors;
    }
    [Serializable] public sealed class Result
    {
        public string id, sector, sourceModel, sourceAsset, objectName;
        public string provenance, surfaceKind;
        public bool reconstructedFromMeasuredBoundary, sourceGeometryRecovered;
        public bool added, changed;
        public int triangles, colliders, samples, passedSamples;
        public float minimumNormalY;
        public Bounds bounds;
        public Vector3[] representativePoints;
        public FailedProbe[] failedProbes;
        public NumericFallback[] numericFallbacks;
        public SelfIntersectionSupport[] nearbySourceFaceContacts;
        public bool facetedNormalMismatch;
    }
    [Serializable] public sealed class SelfIntersectionSupport
    {
        public string mesh, reason;
        public int triangle, probe;
        public Vector3 point, hitPoint, sourceNormal, collisionNormal;
        public float pointError, normalDot;
        public bool facetedNormalMismatch;
    }
    [Serializable] public sealed class NumericFallback
    {
        public int triangle, probe;
        public string reason;
        public float minimumAltitude, worldFloatUlp, requestedInset, usedInset;
        public Vector3 originalPoint, replacementPoint, replacementLocalPoint;
        public bool succeeded;
    }
    [Serializable] public sealed class FailedProbe
    {
        public string mesh, cookingOptions;
        public int triangle, submesh, indexA, indexB, indexC;
        public Vector3 a, b, c, point, rayOrigin, hitPoint, hitNormal, normalRayHitPoint, normalRayHitNormal;
        public float area, projectedArea, minimumAltitude, aspectRatio, normalY, hitDistance, heightError, normalRayDistance, normalRayPointError;
        public bool rayHit, normalRayHit;
    }
    [Serializable] sealed class CanalDiagnostics { public CanalDiagnostic[] canals; public string[] errors; }
    [Serializable] sealed class CanalDiagnostic { public string id; public CookingDiagnostic[] modes; }
    [Serializable] sealed class CookingDiagnostic { public string options; public Result result; }
    [Serializable] sealed class Verification
    {
        public bool passed, idempotent;
        public Report firstPass, repeatPass;
        public string[] changedFilesOnRepeat;
    }

    public static void ApplyAndVerify()
    {
        Apply();
        string reportPath = Output + "/geometry-repair.json";
        var first = JsonUtility.FromJson<Report>(File.ReadAllText(reportPath));
        File.Copy(reportPath, Output + "/geometry-repair-first-pass.json", true);
        string[] paths = Directory.GetFiles(Folder, "*", SearchOption.AllDirectories)
            .Concat(first.results.Select(r => Sectors + r.sector + ".unity"))
            .Concat(StreamerScenes().Where(File.Exists)).Distinct(StringComparer.Ordinal).ToArray();
        var hashes = paths.ToDictionary(p => p, FileHash, StringComparer.Ordinal);
        Apply();
        var repeated = JsonUtility.FromJson<Report>(File.ReadAllText(reportPath));
        string[] changed = paths.Where(p => FileHash(p) != hashes[p]).ToArray();
        bool idempotent = changed.Length == 0 && repeated.addedInstances == 0 && repeated.changedScenes == 0 && repeated.changedAssets == 0 && repeated.changedStreamers == 0;
        var verification = new Verification { passed = first.passed && repeated.passed && idempotent,
            idempotent = idempotent, firstPass = first, repeatPass = repeated, changedFilesOnRepeat = changed };
        File.WriteAllText(Output + "/geometry-focused-before-after.json", JsonUtility.ToJson(verification, true));
        if (!verification.passed) throw new Exception("LOS_SANTOS_GEOMETRY_IDEMPOTENCE_FAILED: " + string.Join(", ", changed));
        Debug.Log("LOS_SANTOS_GEOMETRY_VERIFIED patches=" + first.patches + " triangles=" + first.triangles + " idempotent=true");
    }

    // Read-only experiment: unsaved temporary canal instances, identical mesh,
    // several cooking settings. This does not change importer or scene assets.
    public static void DiagnoseCanals()
    {
        var manifest = Read(Folder + "/" + ManifestName);
        var setup = EditorSceneManager.GetSceneManagerSetup();
        bool backfaces = Physics.queriesHitBackfaces;
        var diagnostics = new List<CanalDiagnostic>();
        var errors = new List<string>();
        var temporaryMeshes = new List<Mesh>();
        Directory.CreateDirectory(Output);
        try
        {
            Physics.queriesHitBackfaces = false;
            foreach (var patch in manifest.patches.Where(p => IsCanal(p.sourceModel)))
            {
                var scene = EditorSceneManager.OpenScene(Sectors + patch.sector + ".unity", OpenSceneMode.Single);
                var matches = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Where(t => t.name == Prefix + patch.id).ToArray();
                GameObject instance;
                if (matches.Length == 0)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + patch.objFile);
                    if (!prefab) throw new Exception("OBJ de diagnóstico ausente: " + patch.id);
                    instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    instance.name = Prefix + patch.id;
                    instance.transform.SetPositionAndRotation(patch.positionUnity, Quaternion.identity);
                    instance.transform.localScale = Vector3.one;
                }
                else if (matches.Length == 1) instance = matches[0].gameObject;
                else throw new Exception("Patch duplicado no diagnóstico: " + patch.id);
                var filters = instance.GetComponentsInChildren<MeshFilter>();
                foreach (var filter in filters)
                {
                    var collider = filter.GetComponent<MeshCollider>();
                    if (!collider) collider = filter.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh; collider.convex = false; collider.isTrigger = false;
                }
                var original = filters[0].GetComponent<MeshCollider>().cookingOptions;
                var clones = filters.ToDictionary(filter => filter, filter => {
                    var mesh = CollisionClone(filter.sharedMesh); temporaryMeshes.Add(mesh); return mesh;
                });
                var modes = new[] { original, original,
                    original & ~(MeshColliderCookingOptions.EnableMeshCleaning | MeshColliderCookingOptions.WeldColocatedVertices),
                    original & ~MeshColliderCookingOptions.UseFastMidphase };
                var results = new List<CookingDiagnostic>();
                for (int mode = 0; mode < modes.Length; mode++)
                {
                    foreach (var filter in filters)
                    {
                        var collider = filter.GetComponent<MeshCollider>();
                        collider.sharedMesh = null; collider.cookingOptions = modes[mode]; collider.sharedMesh = mode == 0 ? filter.sharedMesh : clones[filter];
                    }
                    Physics.SyncTransforms();
                    var result = ValidateGeometry(instance, patch, false, "-diagnostic-" + mode);
                    string label = (mode == 0 ? "imported-original: " : "readable-clone: ") + modes[mode];
                    results.Add(new CookingDiagnostic { options = label, result = result });
                    Debug.Log("LOS_SANTOS_CANAL_DIAGNOSTIC id=" + patch.id + " options=" + label + " samples=" + result.passedSamples + "/" + result.samples);
                }
                diagnostics.Add(new CanalDiagnostic { id = patch.id, modes = results.ToArray() });
            }
        }
        catch (Exception exception) { errors.Add(exception.ToString()); Debug.LogException(exception); }
        finally
        {
            Physics.queriesHitBackfaces = backfaces;
            try { Restore(setup); } catch (Exception exception) { errors.Add("Scene restore: " + exception.Message); }
            foreach (var mesh in temporaryMeshes) if (mesh) Object.DestroyImmediate(mesh);
            File.WriteAllText(Output + "/canal-collision-diagnostics.json", JsonUtility.ToJson(new CanalDiagnostics { canals = diagnostics.ToArray(), errors = errors.ToArray() }, true));
        }
        if (errors.Count > 0) throw new Exception("LOS_SANTOS_CANAL_DIAGNOSTIC_FAILED: " + string.Join(" | ", errors));
    }

    // A fresh editor process can validate saved patches without the external
    // generation folder and without installing, repairing or saving anything.
    public static void VerifyExisting()
    {
        Directory.CreateDirectory(Output);
        string bundled = Folder + "/" + ManifestName;
        if (!File.Exists(bundled)) throw new FileNotFoundException("Manifesto instalado ausente", bundled);
        var manifest = Read(bundled); ValidateManifest(manifest);
        var setup = EditorSceneManager.GetSceneManagerSetup();
        bool backfaces = Physics.queriesHitBackfaces;
        var results = new List<Result>();
        var errors = new List<string>();
        var report = new Report { generatedUtc = DateTime.UtcNow.ToString("O"), sourceManifest = bundled,
            method = "Read-only reload verification of installed geometry, transforms, material textures, exact MeshColliders, bounds, source normals and every triangle's centroid plus inset vertices.",
            patches = manifest.patches.Length };
        try
        {
            Physics.queriesHitBackfaces = false;
            foreach (var group in manifest.patches.GroupBy(p => p.sector))
            {
                var scene = EditorSceneManager.OpenScene(Sectors + group.Key + ".unity", OpenSceneMode.Single);
                Physics.SyncTransforms();
                foreach (var patch in group)
                {
                    var matches = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                        .Where(t => t.name == Prefix + patch.id).ToArray();
                    if (matches.Length != 1 || !matches[0].gameObject.activeInHierarchy) throw new Exception("Reparo instalado ausente/duplicado/inativo: " + patch.id);
                    var instance = matches[0].gameObject;
                    if (Vector3.Distance(instance.transform.position, patch.positionUnity) > .001f || Quaternion.Angle(instance.transform.rotation, Quaternion.identity) > .001f ||
                        Vector3.Distance(instance.transform.localScale, Vector3.one) > .001f) throw new Exception("Transform instalado diverge da fonte: " + patch.id);
                    var materialMap = LoadMaterials(patch);
                    foreach (var definition in patch.materials)
                        if (materialMap[definition.name].mainTexture != AssetDatabase.LoadAssetAtPath<Texture2D>(definition.textureAsset))
                            throw new Exception("Textura instalada diverge da fonte: " + patch.id + "/" + definition.name);
                    var allowedMaterials = materialMap.Values.ToArray();
                    foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var renderer = filter.GetComponent<MeshRenderer>();
                        var colliders = filter.GetComponents<MeshCollider>();
                        if (!renderer || !renderer.enabled || renderer.sharedMaterials.Length != filter.sharedMesh.subMeshCount || renderer.sharedMaterials.Any(m => !allowedMaterials.Contains(m)) ||
                            colliders.Length != 1 || colliders[0].sharedMesh != filter.sharedMesh || colliders[0].isTrigger || colliders[0].convex || !colliders[0].enabled)
                            throw new Exception("Material ou colisão instalada diverge da fonte: " + patch.id);
                    }
                    results.Add(ValidateGeometry(instance, patch));
                }
            }
        }
        catch (Exception exception) { errors.Add(exception.ToString()); Debug.LogException(exception); }
        finally
        {
            Physics.queriesHitBackfaces = backfaces;
            try { Restore(setup); } catch (Exception exception) { errors.Add("Scene restore: " + exception.Message); }
            report.results = results.ToArray(); report.errors = errors.ToArray(); report.passed = errors.Count == 0;
            report.triangles = results.Sum(r => r.triangles); report.colliders = results.Sum(r => r.colliders); report.verifiedSamples = results.Sum(r => r.passedSamples);
            File.WriteAllText(Output + "/geometry-installed-verification.json", JsonUtility.ToJson(report, true));
        }
        if (!report.passed) throw new Exception("LOS_SANTOS_INSTALLED_GEOMETRY_FAILED: " + string.Join(" | ", errors));
        Debug.Log("LOS_SANTOS_INSTALLED_GEOMETRY_VERIFIED patches=" + report.patches + " samples=" + report.verifiedSamples);
    }

    [MenuItem("Horizonte/Los Santos/Restaurar geometria de chao original auditada")]
    public static void Apply()
    {
        string source = Path.Combine(Generated, ManifestName);
        if (!File.Exists(source)) throw new FileNotFoundException("Geometria auditada não foi gerada", source);
        var manifest = Read(source);
        ValidateManifest(manifest);
        string backup = Path.GetFullPath(Output + "/geometry-backup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(backup);
        var report = new Report { generatedUtc = DateTime.UtcNow.ToString("O"), backupDirectory = backup,
            sourceManifest = Path.GetFullPath(source), patches = manifest.patches.Length,
            method = "Clipped original LOD faces and explicitly labeled reconstructions from measured source boundaries. Source UV/material textures retained; Unity OBJ X preflip, baked rotation and vertical offset. Verify triangle count, source normals, tight world bounds and every collider centroid/inset. Ground uses upward normals and vertical first contact; explicit foundation surfaces use short normal-aligned rays, point error under 1cm and normal dot above .98."
        };
        var setup = EditorSceneManager.GetSceneManagerSetup();
        var results = new List<Result>();
        var errors = new List<string>();
        bool backfaces = Physics.queriesHitBackfaces;
        try
        {
            EnsureFolder(Folder);
            foreach (var patch in manifest.patches)
            {
                patch.sourceGeometryRecovered = !patch.reconstructedFromMeasuredBoundary;
                string original = ResolveGenerated(patch.objFile);
                string destination = Folder + "/" + patch.id + ".obj";
                // OBJ usemtl alone is insufficient: without a declared MTL the
                // importer merges distinct texture slots into defaultMat. The
                // library is staged first so the first OBJ import sees it.
                string originalMtl = ResolveGenerated(string.IsNullOrEmpty(patch.mtlFile) ? Path.ChangeExtension(original, ".mtl") : patch.mtlFile);
                string destinationMtl = Folder + "/" + patch.id + ".mtl";
                if (CopyChanged(originalMtl, destinationMtl, backup)) report.changedAssets++;
                patch.mtlFile = Path.GetFileName(destinationMtl);
                if (CopyChanged(original, destination, backup)) report.changedAssets++;
                patch.objFile = Path.GetFileName(destination);
                if (!string.IsNullOrEmpty(patch.materialsFile))
                {
                    string sidecar = ResolveGenerated(patch.materialsFile);
                    string target = Folder + "/" + patch.id + ".materials.json";
                    if (CopyChanged(sidecar, target, backup)) report.changedAssets++;
                    patch.materialsFile = Path.GetFileName(target);
                }
            }
            string bundled = Folder + "/" + ManifestName;
            string manifestText = JsonUtility.ToJson(manifest, true);
            if (!File.Exists(bundled) || File.ReadAllText(bundled) != manifestText)
            {
                BackupFile(bundled, backup); File.WriteAllText(bundled, manifestText);
                AssetDatabase.ImportAsset(bundled, ImportAssetOptions.ForceSynchronousImport); report.changedAssets++;
            }
            foreach (var patch in manifest.patches)
            {
                string path = Folder + "/" + patch.objFile;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (!importer) throw new Exception("Importador OBJ ausente: " + path);
                if (Mathf.Abs(importer.globalScale - 1) > .0001f || importer.meshCompression != ModelImporterMeshCompression.Off)
                {
                    BackupFile(path + ".meta", backup);
                    importer.globalScale = 1; importer.meshCompression = ModelImporterMeshCompression.Off;
                    importer.SaveAndReimport(); report.changedAssets++;
                }
                PrepareMaterials(patch, backup, ref report.changedAssets);
            }
            foreach (string sector in manifest.patches.Select(p => p.sector).Distinct())
                BackupFile(Sectors + sector + ".unity", backup);
            Physics.queriesHitBackfaces = false;
            foreach (string sector in manifest.patches.Select(p => p.sector).Distinct())
            {
                var scene = EditorSceneManager.OpenScene(Sectors + sector + ".unity", OpenSceneMode.Single);
                var applied = ApplyForScene(scene, sector);
                results.AddRange(applied);
                if (applied.Any(r => r.changed))
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Falha ao salvar " + sector);
                    report.changedScenes++;
                }
            }
            report.changedStreamers = ExtendStreamerBounds(manifest, backup);
            AssetDatabase.SaveAssets();
            report.addedInstances = results.Count(r => r.added);
            report.triangles = results.Sum(r => r.triangles);
            report.colliders = results.Sum(r => r.colliders);
            report.verifiedSamples = results.Sum(r => r.passedSamples);
        }
        catch (Exception exception) { errors.Add(exception.ToString()); Debug.LogException(exception); }
        finally
        {
            Physics.queriesHitBackfaces = backfaces;
            try { Restore(setup); } catch (Exception exception) { errors.Add("Scene restore: " + exception.Message); }
            report.results = results.ToArray(); report.errors = errors.ToArray(); report.passed = errors.Count == 0;
            File.WriteAllText(Output + "/geometry-repair.json", JsonUtility.ToJson(report, true));
        }
        if (!report.passed) throw new Exception("LOS_SANTOS_GEOMETRY_REPAIR_FAILED: " + string.Join(" | ", errors));
        Debug.Log("LOS_SANTOS_GEOMETRY_REPAIR_OK patches=" + report.patches + " triangles=" + report.triangles + " samples=" + report.verifiedSamples);
    }

    // Called by GtaSaModTestSetup before saving a newly generated full-map sector.
    public static Result[] ApplyForScene(Scene scene, string sectorName = null)
    {
        string bundled = Folder + "/" + ManifestName;
        if (!File.Exists(bundled)) return Array.Empty<Result>();
        var manifest = Read(bundled);
        sectorName = sectorName ?? Path.GetFileNameWithoutExtension(scene.path);
        var results = new List<Result>();
        var sectorRoot = scene.GetRootGameObjects().FirstOrDefault(r => r.name.StartsWith("GTA SA — setor ", StringComparison.Ordinal));
        if (!sectorRoot && manifest.patches.Any(p => p.sector == sectorName)) throw new Exception("Raiz de setor ausente: " + sectorName);
        foreach (var patch in manifest.patches.Where(p => p.sector == sectorName))
        {
            string objectName = Prefix + patch.id;
            var matches = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Where(t => t.name == objectName).ToArray();
            if (matches.Length > 1) throw new Exception("Geometria de reparo duplicada: " + objectName);
            bool added = matches.Length == 0, changed = added;
            GameObject instance;
            if (added)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + patch.objFile);
                if (!prefab) throw new Exception("OBJ de reparo ausente: " + patch.objFile);
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.name = objectName; instance.transform.SetParent(sectorRoot.transform, false);
            }
            else instance = matches[0].gameObject;
            if (Vector3.Distance(instance.transform.position, patch.positionUnity) > .0001f || Quaternion.Angle(instance.transform.rotation, Quaternion.identity) > .0001f ||
                Vector3.Distance(instance.transform.localScale, Vector3.one) > .0001f)
            {
                instance.transform.SetPositionAndRotation(patch.positionUnity, Quaternion.identity);
                instance.transform.localScale = Vector3.one; changed = true;
            }
            if (!instance.activeSelf) { instance.SetActive(true); changed = true; }
            var materialMap = LoadMaterials(patch);
            foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                var renderer = filter.GetComponent<MeshRenderer>();
                if (!mesh || !renderer) throw new Exception("Malha sem renderer: " + objectName);
                var previous = renderer.sharedMaterials;
                // Reimporting a source OBJ may add/reorder material slots. Read
                // their names from the updated prefab, rather than stale array
                // overrides on an already installed instance.
                var sourceRenderer = PrefabUtility.GetCorrespondingObjectFromOriginalSource(renderer);
                var sourceMaterials = sourceRenderer ? sourceRenderer.sharedMaterials : previous;
                if (sourceMaterials.Length != mesh.subMeshCount)
                    throw new Exception("Slots OBJ não correspondem às submalhas: " + patch.id);
                var assigned = new Material[mesh.subMeshCount];
                for (int slot = 0; slot < assigned.Length; slot++)
                {
                    string name = sourceMaterials[slot] ? sourceMaterials[slot].name : "";
                    if (!materialMap.TryGetValue(name, out assigned[slot]))
                    {
                        // Already assigned repair materials have deterministic names.
                        var existing = slot < previous.Length ? materialMap.Values.FirstOrDefault(m => m == previous[slot]) : null;
                        if (existing) assigned[slot] = existing;
                        else if (assigned.Length == patch.materials.Length) assigned[slot] = materialMap[patch.materials[slot].name];
                        else throw new Exception("Slot de material não identificado: " + patch.id + " / " + name);
                    }
                }
                if (!previous.SequenceEqual(assigned)) { renderer.sharedMaterials = assigned; changed = true; }
                if (!renderer.enabled) { renderer.enabled = true; changed = true; }
                if (renderer.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)
                { renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; changed = true; }
                if (!renderer.receiveShadows) { renderer.receiveShadows = true; changed = true; }
                var colliders = filter.GetComponents<MeshCollider>();
                if (colliders.Length > 1) throw new Exception("Colisão de reparo duplicada: " + patch.id);
                var collider = colliders.FirstOrDefault();
                if (!collider) { collider = filter.gameObject.AddComponent<MeshCollider>(); changed = true; }
                if (collider.sharedMesh != mesh || collider.isTrigger || collider.convex || !collider.enabled)
                { collider.sharedMesh = mesh; collider.isTrigger = false; collider.convex = false; collider.enabled = true; changed = true; }
                if (!filter.gameObject.isStatic) { filter.gameObject.isStatic = true; changed = true; }
            }
            if (!instance.isStatic) { instance.isStatic = true; changed = true; }
            Physics.SyncTransforms();
            var result = ValidateGeometry(instance, patch);
            result.added = added; result.changed = changed; results.Add(result);
        }
        return results.ToArray();
    }

    static Result ValidateGeometry(GameObject instance, Patch patch, bool throwOnProbeFailure = true, string diagnosticSuffix = "")
    {
        var result = new Result { id = patch.id, sector = patch.sector, sourceModel = patch.sourceModel,
            sourceAsset = patch.sourceAsset, objectName = instance.name, minimumNormalY = 1,
            provenance = patch.provenance, reconstructedFromMeasuredBoundary = patch.reconstructedFromMeasuredBoundary,
            sourceGeometryRecovered = patch.sourceGeometryRecovered, surfaceKind=patch.surfaceKind };
        float allowedNormal = MinimumAllowedNormal(patch);
        var centers = new List<Vector3>();
        var failures = new List<FailedProbe>();
        var numericFallbacks = new List<NumericFallback>();
        var selfIntersections = new List<SelfIntersectionSupport>();
        bool foundation=IsFoundation(patch);
        bool canal = IsCanal(patch.sourceModel) || foundation;
        bool bounded = false;
        foreach (var filter in instance.GetComponentsInChildren<MeshFilter>())
        {
            var collider = filter.GetComponent<MeshCollider>();
            if (!collider) throw new Exception("Colisão de chão ausente: " + patch.id);
            result.colliders++;
            using (var data = MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh))
            using (var vertices = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp))
            {
                data[0].GetVertices(vertices);
                for (int slot = 0; slot < data[0].subMeshCount; slot++)
                {
                    var descriptor = data[0].GetSubMesh(slot);
                    if (descriptor.topology != MeshTopology.Triangles) throw new Exception("Face não triangular: " + patch.id);
                    using (var indices = new NativeArray<int>(descriptor.indexCount, Allocator.Temp))
                    {
                        data[0].GetIndices(indices, slot);
                        for (int i = 0; i + 2 < indices.Length; i += 3)
                        {
                            Vector3 localA = vertices[indices[i]], localB = vertices[indices[i + 1]], localC = vertices[indices[i + 2]];
                            Vector3 a = filter.transform.TransformPoint(localA), b = filter.transform.TransformPoint(localB), c = filter.transform.TransformPoint(localC);
                            // Canal fragments can be millimetres wide at world coordinates
                            // above 2000m. Subtracting rounded world positions first loses
                            // precision; transform their local edge vectors instead.
                            Vector3 edgeAB = filter.transform.TransformVector(localB - localA), edgeAC = filter.transform.TransformVector(localC - localA);
                            Vector3 cross = canal ? Vector3.Cross(edgeAB, edgeAC) : Vector3.Cross(b - a, c - a);
                            // Vector3.normalized returns zero below its fixed
                            // epsilon, even for a real positive-area triangle.
                            // Retain its measured direction; physical ray proof
                            // remains mandatory for every tiny source face.
                            float crossMagnitude=cross.magnitude;
                            Vector3 measuredNormal=crossMagnitude>0 ? cross/crossMagnitude : Vector3.zero;
                            float normal = measuredNormal.y;
                            if (crossMagnitude<=0 || normal < allowedNormal - .0005f)
                            {
                                Directory.CreateDirectory(Output);
                                File.WriteAllText(Output + "/geometry-invalid-face-" + patch.id + ".json", JsonUtility.ToJson(new FailedProbe {
                                    mesh=filter.sharedMesh.name, triangle=result.triangles, submesh=slot,
                                    indexA=indices[i], indexB=indices[i+1], indexC=indices[i+2],
                                    a=a, b=b, c=c, normalY=normal, area=cross.magnitude*.5f,
                                    projectedArea=Mathf.Abs(cross.y)*.5f, point=(a+b+c)/3f
                                }, true));
                                throw new Exception("Face invertida/íngreme no chão recuperado: " + patch.id + " normalY=" + normal);
                            }
                            result.minimumNormalY = Mathf.Min(result.minimumNormalY, normal);
                            if (!bounded) { result.bounds = new Bounds(a, Vector3.zero); bounded = true; }
                            result.bounds.Encapsulate(a); result.bounds.Encapsulate(b); result.bounds.Encapsulate(c);
                            result.triangles++;
                            Vector3 localCenter = (localA + localB + localC) / 3f;
                            Vector3 center = canal ? filter.transform.TransformPoint(localCenter) : (a + b + c) / 3f;
                            centers.Add(center);
                            float longest = canal ? Mathf.Max(edgeAB.magnitude, Mathf.Max(edgeAC.magnitude, (edgeAC - edgeAB).magnitude)) :
                                Mathf.Max(Vector3.Distance(a, b), Mathf.Max(Vector3.Distance(b, c), Vector3.Distance(c, a)));
                            float altitude = longest > 0 ? cross.magnitude / longest : 0;
                            float ulp = Mathf.Max(WorldFloatUlp(a), Mathf.Max(WorldFloatUlp(b), WorldFloatUlp(c)));
                            // A vertex-to-centroid inset has only inset/3 of the
                            // triangle altitude as edge clearance. Reserve four ULPs
                            // where possible; retain real collider hits as the proof.
                            float inset = canal && altitude > 0 ? Mathf.Clamp(Mathf.Max(.2f, 12f * ulp / altitude), .2f, .95f) : .2f;
                            Vector3[] localPoints = { localCenter, Vector3.Lerp(localA, localCenter, inset), Vector3.Lerp(localB, localCenter, inset), Vector3.Lerp(localC, localCenter, inset) };
                            Vector3[] roadPoints = { center, Vector3.Lerp(a, center, .2f), Vector3.Lerp(b, center, .2f), Vector3.Lerp(c, center, .2f) };
                            for (int probe = 0; probe < 4; probe++)
                            {
                                Vector3 point = canal ? filter.transform.TransformPoint(localPoints[probe]) : roadPoints[probe];
                                Vector3 replacementLocal = localPoints[probe];
                                Vector3 originalPoint = canal ? filter.transform.TransformPoint(probe == 0 ? localCenter :
                                    Vector3.Lerp(probe == 1 ? localA : probe == 2 ? localB : localC, localCenter, .2f)) : point;
                                result.samples++;
                                Vector3 faceNormal = measuredNormal;
                                var ray = canal ? new Ray(point + faceNormal * .005f, -faceNormal) : new Ray(point + Vector3.up * .3f, Vector3.down);
                                float rayLength = canal ? .02f : .6f;
                                bool rayHit = collider.Raycast(ray, out var hit, rayLength);
                                bool passed = canal ? FaceHitMatches(rayHit, hit, point, allowedNormal, foundation, faceNormal) :
                                    rayHit && Mathf.Abs(hit.point.y - point.y) < .12f && hit.normal.y > allowedNormal - .025f;
                                bool interiorFallback = false;
                                if (canal && !passed && altitude <= ulp * 32f)
                                {
                                    // The source face remains mandatory. If a rounded
                                    // edge sample is not representable, try explicit
                                    // interior barycentric points on this same face.
                                    var alternatives = new[] { localCenter, Vector3.Lerp(localPoints[probe], localCenter, .5f),
                                        Vector3.Lerp(localPoints[probe], localCenter, .85f),
                                        localA * .2f + localB * .3f + localC * .5f,
                                        localA * .3f + localB * .5f + localC * .2f,
                                        localA * .5f + localB * .2f + localC * .3f };
                                    foreach (Vector3 local in alternatives)
                                    {
                                        Vector3 alternative = filter.transform.TransformPoint(local);
                                        var alternativeRay = new Ray(alternative + faceNormal * .005f, -faceNormal);
                                        bool alternativeHit = collider.Raycast(alternativeRay, out var alternativeResult, .02f);
                                        if (!FaceHitMatches(alternativeHit, alternativeResult, alternative, allowedNormal, foundation, faceNormal)) continue;
                                        point = alternative; replacementLocal = local; ray = alternativeRay; rayHit = alternativeHit; hit = alternativeResult;
                                        passed = true; interiorFallback = true; break;
                                    }
                                }
                                if (!canal && !passed && altitude <= ulp * 32f)
                                {
                                    // A thin real floor can have a rounded edge
                                    // probe outside its cooked native triangle.
                                    // Keep the same vertical first-contact test
                                    // and measure an interior point on this face.
                                    var alternatives=new[] {
                                        Vector3.Lerp(localPoints[probe],localCenter,.8f),
                                        localA*.2f+localB*.3f+localC*.5f,
                                        localA*.3f+localB*.5f+localC*.2f,
                                        localA*.5f+localB*.2f+localC*.3f,
                                        localCenter };
                                    foreach(var local in alternatives)
                                    {
                                        Vector3 alternative=filter.transform.TransformPoint(local);
                                        var alternativeRay=new Ray(alternative+Vector3.up*.3f,Vector3.down);
                                        bool alternativeHit=collider.Raycast(alternativeRay,out var alternativeResult,.6f);
                                        if(!alternativeHit || Mathf.Abs(alternativeResult.point.y-alternative.y)>=.12f || alternativeResult.normal.y<=allowedNormal-.025f)continue;
                                        point=alternative;replacementLocal=local;ray=alternativeRay;rayHit=true;hit=alternativeResult;
                                        passed=true;interiorFallback=true;break;
                                    }
                                }
                                if (interiorFallback || canal && probe > 0 && inset > .20001f)
                                    numericFallbacks.Add(new NumericFallback { triangle = result.triangles - 1, probe = probe,
                                        reason = interiorFallback ? "unrepresentable edge probe replaced by a measured interior point on the same face" : "inset increased for world Float32 ULP clearance",
                                        minimumAltitude = altitude, worldFloatUlp = ulp, requestedInset = .2f, usedInset = inset,
                                        originalPoint = originalPoint, replacementPoint = point, replacementLocalPoint = replacementLocal, succeeded = passed });
                                if (passed)
                                {
                                    result.passedSamples++;
                                    if (canal && Vector3.Dot(hit.normal, faceNormal) <= .98f)
                                        selfIntersections.Add(new SelfIntersectionSupport {
                                            mesh = collider.sharedMesh.name, triangle = result.triangles - 1, probe = probe,
                                            reason = "Same MeshCollider has an intersecting upward face within 0.01m; the first physical support normal differs from the source face.",
                                            point = point, hitPoint = hit.point, sourceNormal = faceNormal, collisionNormal = hit.normal,
                                            pointError = Vector3.Distance(hit.point, point), normalDot = Vector3.Dot(hit.normal, faceNormal), facetedNormalMismatch = true });
                                }
                                else if (failures.Count < 512)
                                {
                                    var normalRay = new Ray(point + faceNormal * .005f, -faceNormal);
                                    bool normalHit = collider.Raycast(normalRay, out var normalResult, .02f);
                                    failures.Add(new FailedProbe { mesh = filter.sharedMesh.name, cookingOptions = collider.cookingOptions.ToString(),
                                        triangle = result.triangles - 1, submesh = slot, indexA = indices[i], indexB = indices[i + 1], indexC = indices[i + 2],
                                        a = a, b = b, c = c, point = point, rayOrigin = ray.origin, rayHit = rayHit,
                                        hitPoint = rayHit ? hit.point : Vector3.zero, hitNormal = rayHit ? hit.normal : Vector3.zero,
                                        area = cross.magnitude * .5f, projectedArea = Mathf.Abs(cross.y) * .5f,
                                        minimumAltitude = longest > 0 ? cross.magnitude / longest : 0,
                                        aspectRatio = cross.magnitude > 0 ? longest * longest / cross.magnitude : 0,
                                        normalY = normal, hitDistance = rayHit ? hit.distance : -1,
                                        heightError = rayHit ? Mathf.Abs(hit.point.y - point.y) : -1,
                                        normalRayHit = normalHit, normalRayHitPoint = normalHit ? normalResult.point : Vector3.zero,
                                        normalRayHitNormal = normalHit ? normalResult.normal : Vector3.zero,
                                        normalRayDistance = normalHit ? normalResult.distance : -1,
                                        normalRayPointError = normalHit ? Vector3.Distance(normalResult.point, point) : -1 });
                                }
                            }
                        }
                    }
                }
            }
        }
        if (result.triangles != patch.triangles || result.triangles == 0) throw new Exception("Contagem de faces mudou: " + patch.id + " actual=" + result.triangles + " expected=" + patch.triangles);
        if (Vector3.Distance(result.bounds.min, patch.boundsUnity.min) > .12f || Vector3.Distance(result.bounds.max, patch.boundsUnity.max) > .12f)
            throw new Exception("Chão fora das cotas originais: " + patch.id + " actual=" + result.bounds);
        if (result.minimumNormalY < patch.minimumNormalY - .025f) throw new Exception("Normal do chão diverge da fonte: " + patch.id);
        result.representativePoints = RepresentativePoints(centers, result.bounds.center);
        result.failedProbes = failures.ToArray();
        result.numericFallbacks = numericFallbacks.ToArray();
        result.nearbySourceFaceContacts = selfIntersections.ToArray();
        result.facetedNormalMismatch = selfIntersections.Count > 0;
        if (result.passedSamples != result.samples)
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/geometry-failed-probes-" + patch.id + diagnosticSuffix + ".json", JsonUtility.ToJson(result, true));
            if (throwOnProbeFailure) throw new Exception("Chão visual sem colisão: " + patch.id + " samples=" + result.passedSamples + "/" + result.samples);
        }
        return result;
    }
    // Raycast is performed on this patch's MeshCollider, not arbitrary world
    // geometry. At original source intersections a different nearby face can
    // be the first hit. It still proves real upward collision at the sample;
    // retain a strict 1cm distance limit and record the normal difference.
    static bool FaceHitMatches(bool hit, RaycastHit result, Vector3 point, float allowedNormal, bool foundation, Vector3 faceNormal) => hit &&
        Vector3.Distance(result.point, point) < .01f && (foundation ? Vector3.Dot(result.normal,faceNormal)>.98f : result.normal.y >= allowedNormal - .025f);
    static float WorldFloatUlp(Vector3 point)
    {
        float magnitude = Mathf.Max(Mathf.Abs(point.x), Mathf.Max(Mathf.Abs(point.y), Mathf.Abs(point.z)));
        return magnitude > 0 ? (float)Math.Pow(2, Math.Floor(Math.Log(magnitude, 2)) - 23) : float.Epsilon;
    }

    static void PrepareMaterials(Patch patch, string backup, ref int changed)
    {
        var sourceColors = new Dictionary<string, Color>(StringComparer.Ordinal);
        string sidecar = !string.IsNullOrEmpty(patch.materialsFile) ? Folder + "/" + patch.materialsFile : Path.ChangeExtension(patch.sourceAsset, ".materials.json");
        if (File.Exists(sidecar))
            foreach (var slot in JsonUtility.FromJson<Sidecar>(File.ReadAllText(sidecar)).materials ?? Array.Empty<MaterialSlot>())
                if (!string.IsNullOrEmpty(slot.material) && slot.color != null && slot.color.Length >= 4)
                    sourceColors[slot.material] = new Color(slot.color[0], slot.color[1], slot.color[2], slot.color[3]);
        foreach (var definition in patch.materials)
        {
            string path = MaterialPath(patch, definition);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(definition.textureAsset);
            if (!texture) throw new Exception("Textura original de chão ausente: " + definition.textureAsset);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Color color = sourceColors.TryGetValue(definition.name, out var original) ? original : Color.white;
            if (!material)
            {
                material = new Material(Shader.Find("Standard")) { name = patch.id + "__" + definition.name };
                AssetDatabase.CreateAsset(material, path); changed++;
            }
            if (material.mainTexture != texture || material.color != color || material.GetFloat("_Glossiness") != 0 || material.GetFloat("_Mode") != 0)
            {
                BackupFile(path, backup); material.mainTexture = texture; material.color = color;
                material.SetFloat("_Glossiness", 0); material.SetFloat("_Mode", 0); material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero); material.SetInt("_ZWrite", 1);
                material.DisableKeyword("_ALPHATEST_ON"); material.DisableKeyword("_ALPHABLEND_ON"); material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.renderQueue = -1; EditorUtility.SetDirty(material); changed++;
            }
        }
    }
    static Dictionary<string, Material> LoadMaterials(Patch patch) => patch.materials.ToDictionary(m => m.name,
        m => AssetDatabase.LoadAssetAtPath<Material>(MaterialPath(patch, m)) ?? throw new Exception("Material de reparo ausente: " + patch.id + "/" + m.name), StringComparer.Ordinal);
    static string MaterialPath(Patch patch, MaterialDefinition definition) => Folder + "/" + patch.id + "-" + Hash(definition.name).Substring(0, 12) + ".mat";
    static int ExtendStreamerBounds(Manifest manifest, string backup)
    {
        int changedScenes = 0;
        foreach (string path in StreamerScenes().Where(File.Exists).Distinct())
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var streaming = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GtaSaSectorStreaming>(true)).FirstOrDefault();
            if (!streaming) continue;
            bool changed = false;
            foreach (var entry in streaming.sectors)
                foreach (var patch in manifest.patches.Where(p => entry != null && entry.scene == p.sector))
                {
                    var bounds = entry.bounds; bounds.Encapsulate(patch.boundsUnity.min); bounds.Encapsulate(patch.boundsUnity.max);
                    if (Vector3.Distance(bounds.center, entry.bounds.center) < .0001f && Vector3.Distance(bounds.size, entry.bounds.size) < .0001f) continue;
                    entry.bounds = bounds; changed = true;
                }
            if (!changed) continue;
            BackupFile(path, backup); EditorUtility.SetDirty(streaming); EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Falha ao atualizar bounds: " + path);
            changedScenes++;
        }
        return changedScenes;
    }
    static string[] StreamerScenes() => new[] { GameUpdateSetup.GameScene, "Assets/Scenes/GTASanAndreasModFullTeste.unity", LosSantosAndroidSetup.Scene };
    static Manifest Read(string path) => JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
    static void ValidateManifest(Manifest manifest)
    {
        if (manifest == null || manifest.formatVersion != 1 || manifest.patches == null || manifest.patches.Length == 0) throw new Exception("Manifesto de geometria inválido.");
        if (manifest.patches.Select(p => p.id).Distinct(StringComparer.Ordinal).Count() != manifest.patches.Length) throw new Exception("IDs de reparo duplicados.");
        foreach (var patch in manifest.patches)
            if (string.IsNullOrEmpty(patch.id) || patch.id.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_') || !LosSantosAndroidSetup.Selected(patch.sector) ||
                patch.boundsUnity == null || patch.triangles <= 0 || patch.minimumNormalY < MinimumAllowedNormal(patch) - .0005f || patch.materials == null || patch.materials.Length == 0)
                throw new Exception("Reparo não auditável: " + patch.id);
    }
    static float MinimumAllowedNormal(Patch patch)
    {
        if (IsFoundation(patch))
        {
            if (patch.minimumAllowedNormalY!=0 && Mathf.Abs(patch.minimumAllowedNormalY+1)>.0001f)
                throw new Exception("Fundação exige validação normal-aligned explícita: "+patch.id);
            return -1;
        }
        bool canal = IsCanal(patch.sourceModel);
        float minimum = canal ? .1f : .72f;
        if (patch.minimumAllowedNormalY > 0 && Mathf.Abs(patch.minimumAllowedNormalY - minimum) > .0001f)
            throw new Exception("Limiar de normal não autorizado para a fonte: " + patch.sourceModel);
        return minimum;
    }
    static bool IsFoundation(Patch patch) => string.Equals(patch.surfaceKind,"foundation",StringComparison.Ordinal);
    static bool IsCanal(string sourceModel) => string.Equals(sourceModel, "LODrmDraifr1_las2", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(sourceModel, "LODrmDraifr2_las2", StringComparison.OrdinalIgnoreCase);
    static Mesh CollisionClone(Mesh source)
    {
        var mesh = new Mesh { name = source.name + " readable collision diagnostic" };
        using (var data = MeshUtility.AcquireReadOnlyMeshData(source))
        using (var vertices = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp))
        {
            data[0].GetVertices(vertices);
            mesh.indexFormat = vertices.Length > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = vertices.ToArray(); mesh.subMeshCount = data[0].subMeshCount;
            for (int slot = 0; slot < data[0].subMeshCount; slot++)
            {
                var descriptor = data[0].GetSubMesh(slot);
                using (var indices = new NativeArray<int>(descriptor.indexCount, Allocator.Temp))
                { data[0].GetIndices(indices, slot); mesh.SetIndices(indices.ToArray(), descriptor.topology, slot); }
            }
        }
        mesh.RecalculateBounds(); return mesh;
    }
    static Vector3[] RepresentativePoints(List<Vector3> centers, Vector3 middle)
    {
        var chosen = new List<Vector3>();
        while (chosen.Count < Mathf.Min(5, centers.Count))
        {
            int best = -1;
            float score = float.NegativeInfinity;
            for (int i = 0; i < centers.Count; i++)
            {
                if (chosen.Contains(centers[i])) continue;
                float candidate = chosen.Count == 0 ? -(centers[i] - middle).sqrMagnitude : chosen.Min(p => (centers[i] - p).sqrMagnitude);
                if (candidate > score) { score = candidate; best = i; }
            }
            if (best < 0) break;
            chosen.Add(centers[best]);
        }
        return chosen.ToArray();
    }
    static string ResolveGenerated(string path)
    {
        string directory = Path.GetFullPath(Generated) + Path.DirectorySeparatorChar;
        string resolved = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Generated, path));
        if (!resolved.StartsWith(directory, StringComparison.OrdinalIgnoreCase) || !File.Exists(resolved)) throw new Exception("Arquivo fora da pasta gerada: " + path);
        return resolved;
    }
    static bool CopyChanged(string source, string destination, string backup)
    {
        if (File.Exists(destination) && FileHash(source) == FileHash(destination)) return false;
        BackupFile(destination, backup); File.Copy(source, destination, true);
        AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport); return true;
    }
    static void BackupFile(string path, string backup)
    {
        if (!File.Exists(path)) return;
        string destination = Path.Combine(backup, Hash(path).Substring(0, 10) + "-" + Path.GetFileName(path));
        if (!File.Exists(destination)) File.Copy(path, destination, false);
    }
    static void EnsureFolder(string path)
    {
        string parent = "Assets";
        foreach (string part in path.Split('/').Skip(1)) { if (!AssetDatabase.IsValidFolder(parent + "/" + part)) AssetDatabase.CreateFolder(parent, part); parent += "/" + part; }
    }
    static string Hash(string text)
    { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant(); }
    static string FileHash(string path)
    { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
    static void Restore(SceneSetup[] setup)
    { if (setup.Length > 0 && setup.All(s => !string.IsNullOrEmpty(s.path))) EditorSceneManager.RestoreSceneManagerSetup(setup); else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
}
