using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Recover only measured, opaque ground faces omitted from a source COL.
// Original collision components and visual meshes are preserved in full.
public static class LosSantosSurfaceCollisionRepair
{
    const string SectorFolder = "Assets/Scenes/GTASanAndreasModFullSectors/";
    const string MeshFolder = "Assets/GTASanAndreasModFull/CollisionRepairs";
    const string DefaultOutput = "../tools/los-santos-repair";
    static string Output = DefaultOutput;
    static string AuditPath = DefaultOutput + "/ground-audit-before.json";
    static bool PatrolAudit;
    static Approval[] PatrolApprovals = Array.Empty<Approval>();
    static AuditItem[] PatrolSourceSamples = Array.Empty<AuditItem>();
    const string NodePrefix = "COL REPARO PISO — ";
    const float Tolerance = .45f;
    const int MaxSubdivision = 3;
    static readonly Regex GroundName = new Regex(@"road|land|hill|terrain|ground|grnd|beach|coast|pave|sidewalk|drain|grasspatch|river", RegexOptions.IgnoreCase);
    static readonly Regex ExcludeName = new Regex(@"house|build|apart|blok|roadblock|tree|alpha|lod|sign|billboard|bridge", RegexOptions.IgnoreCase);
    static readonly Regex GroundTexture = new Regex(@"asphalt|road|tarmac|snpedtest|greyground|pave|grass|sand|dirt|gravel|concrete|sidelatino|sidewalk|plazatile", RegexOptions.IgnoreCase);
    static readonly Regex ExcludeTexture = new Regex(@"roof|wall|glass|window|metal|fence|leaf|leaves|water|alpha", RegexOptions.IgnoreCase);

    // Each model has missing support in the saved city-wide before report.
    // A matching model name alone never authorizes a repair: a missing audit
    // result, exact source mesh, opaque floor texture and live probes are required.
    static readonly HashSet<string> ConfirmedModels = new HashSet<string>(new[] {
        "cuntwland54b", "Roads38CE", "cnteHillClif01", "CE_grndPALCST02", "HillsEast05_LAe",
        "lae2_ground17", "Beach1spt_las2", "cunte_landF4_03", "Roads35_LAw2", "Roads17_LAw2",
        "Roads33_LAw2", "Roads09_LAw2", "bevgrnd03b_law", "road_lawn33", "law_vengrnd",
        "venlaw_grnd", "LAland_08", "hillpalos02_LAs", "road_lawn10", "road_lawn03", "Beach1_LAs04",
        "Lae2_landHUB01", "BTOLAND5_las2", "IndusLand2_las2", "Lae2_roads36", "lae2_ground08",
        "Lae2_roads90", "lae2_ground12", "stormdrainLAE2_05", "grasspatchlan2", "LAriverSec1_LAn"
    }, StringComparer.OrdinalIgnoreCase);

    [Serializable] sealed class Audit { public int cellsInspected; public AuditItem[] ground; }
    [Serializable] sealed class AuditItem
    {
        public string sector, instance, mesh, asset, texture;
        public int missing, supported, samples;
        public bool eligibleRepair, needsSemanticReview;
        public Vector3 position;
    }
    [Serializable] sealed class Approvals { public Approval[] items; }
    [Serializable] sealed class Approval
    {
        public string sector, instance, asset, mesh, texture;
        public bool approvedRepair;
        public ReviewedBounds boundsUnity;
        public ReviewedTriangle[] trianglesWorld;
    }
    [Serializable] sealed class ReviewedBounds { public Vector3 min, max; }
    [Serializable] sealed class ReviewedTriangle { public Vector3 a, b, c; }
    [Serializable] sealed class Report
    {
        public string generatedUtc, backupDirectory, sourceAudit, method;
        public int sourceAuditCells, candidates, inspectedMeshes, changedScenes, addedNodes, addedTriangles, verifiedPatchSamples, failedPatchSamples;
        public bool passed;
        public Item[] items;
        public string[] errors;
    }
    [Serializable] sealed class Item
    {
        public string sector, instance, sourceMesh, sourceAsset, patchAsset, patchNode;
        public int eligibleSourceTriangles, missingSourceCentroidsBefore, missingSourceCentroidsAfter;
        public int addedTriangles, existingTriangles, skippedMixedFragments, verifiedSamples, failedSamples;
        public int missingMeasuredWitnessesBefore, missingMeasuredWitnessesAfter;
        public bool noChange;
        public Bounds bounds;
        public Point[] repairedExamples;
    }
    [Serializable] sealed class Point { public Vector3 position; public bool supportedBefore, supportedAfter; }
    [Serializable] sealed class Verification
    {
        public bool passed, idempotent;
        public Report firstPass, repeatPass;
        public string[] changedFilesOnRepeat;
    }
    struct Triangle
    {
        public Vector3 a, b, c;
        public Triangle(Vector3 a, Vector3 b, Vector3 c) { this.a = a; this.b = b; this.c = c; }
    }

    public static void ApplyAndVerify()
    {
        Apply();
        string reportPath = Path.Combine(Output, "surface-collision-repair.json");
        var first = JsonUtility.FromJson<Report>(File.ReadAllText(reportPath));
        File.Copy(reportPath, Path.Combine(Output, "surface-collision-repair-first-pass.json"), true);
        var paths = first.items.Select(i => SectorFolder + i.sector + ".unity")
            .Concat(first.items.Where(i => i.addedTriangles + i.existingTriangles > 0).Select(i => i.patchAsset))
            .Distinct(StringComparer.Ordinal).ToArray();
        var hashes = paths.ToDictionary(p => p, FileHash, StringComparer.Ordinal);
        Apply();
        var repeated = JsonUtility.FromJson<Report>(File.ReadAllText(reportPath));
        string[] changed = paths.Where(p => hashes[p] != FileHash(p)).ToArray();
        bool idempotent = changed.Length == 0 && repeated.changedScenes == 0 && repeated.addedNodes == 0 && repeated.addedTriangles == 0;
        var verification = new Verification { passed = first.passed && repeated.passed && idempotent,
            idempotent = idempotent, firstPass = first, repeatPass = repeated, changedFilesOnRepeat = changed };
        File.WriteAllText(Path.Combine(Output, "surface-focused-before-after.json"), JsonUtility.ToJson(verification, true));
        if (!verification.passed) throw new Exception("LOS_SANTOS_SURFACE_IDEMPOTENCE_FAILED: " + string.Join(", ", changed));
        Debug.Log("LOS_SANTOS_SURFACE_VERIFIED triangles=" + first.addedTriangles + " samples=" + first.verifiedPatchSamples + " idempotent=true");
    }

    // The native player patrol has stricter, local source evidence. Keep its
    // reports separate from the original city-wide audit and repairs.
    public static void ApplyPatrolAndVerify()
    {
        ApplyPatrolAudit("source-audit-before.json", "semantic-floor-approvals.json");
    }

    public static void ApplyPatrolSupplementAndVerify()
    {
        ApplyPatrolAudit("source-audit-supplement.json", "semantic-floor-all-approvals.json");
    }

    static void ApplyPatrolAudit(string auditFile, string approvalsFile)
    {
        Output = "../tools/los-santos-patrol";
        AuditPath = Output + "/" + auditFile;
        PatrolAudit = true;
        string approvalsPath = Output + "/" + approvalsFile;
        PatrolApprovals = File.Exists(approvalsPath) ? JsonUtility.FromJson<Approvals>(File.ReadAllText(approvalsPath))?.items ?? Array.Empty<Approval>() : Array.Empty<Approval>();
        try { ApplyAndVerify(); }
        finally
        {
            Output = DefaultOutput;
            AuditPath = DefaultOutput + "/ground-audit-before.json";
            PatrolAudit = false;
            PatrolApprovals = Array.Empty<Approval>();
            PatrolSourceSamples = Array.Empty<AuditItem>();
        }
    }

    [MenuItem("Horizonte/Los Santos/Recuperar faces de piso sem colisao comprovadas")]
    public static void Apply()
    {
        if (!File.Exists(AuditPath)) throw new FileNotFoundException("Execute a auditoria de chão antes do reparo.", AuditPath);
        var audit = JsonUtility.FromJson<Audit>(File.ReadAllText(AuditPath));
        if (audit == null || audit.ground == null || audit.cellsInspected == 0) throw new Exception("Auditoria anterior inválida.");
        if (PatrolAudit) PatrolSourceSamples = audit.ground.Where(EligibleCandidate).ToArray();
        var candidates = audit.ground.Where(EligibleCandidate)
            .GroupBy(i => i.sector + "|" + i.instance + "|" + i.asset + "|" + i.mesh, StringComparer.Ordinal)
            .Select(g => g.First()).OrderBy(i => i.sector, StringComparer.Ordinal).ThenBy(i => i.instance, StringComparer.Ordinal).ToArray();
        var setup = EditorSceneManager.GetSceneManagerSetup();
        bool backfaces = Physics.queriesHitBackfaces;
        var report = new Report {
            generatedUtc = DateTime.UtcNow.ToString("O"), sourceAudit = Path.GetFullPath(AuditPath),
            sourceAuditCells = audit.cellsInspected, candidates = candidates.Length,
            backupDirectory = Path.GetFullPath(Output + "/surface-backup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")),
            method = "Explicit ground-model allowlist or independently approved source component faces, intersected with missing before-audit results; exact placement/source mesh; opaque floor texture only; normal.y > 0.72. Patrol semantic approvals require exact material, component bounds and reviewed world triangles. Probe centroid plus 3 vertices inset 12%; preserve supported faces. Subdivide mixed triangles up to 3 levels, or 12 only beside exact native patrol samples on the same height layer; a missing witness inside a face overrides sparse supported probes; add only fragments whose 4 probes lack collision within 0.45m. Persist separate editable collision mesh/node; never replace original COL or visual meshes."
        };
        var items = new List<Item>();
        var errors = new List<string>();
        Directory.CreateDirectory(report.backupDirectory);
        try
        {
            foreach (string name in candidates.Select(i => i.sector).Distinct())
                File.Copy(SectorFolder + name + ".unity", Path.Combine(report.backupDirectory, name + ".unity"), false);
            var main = EditorSceneManager.OpenScene(GameUpdateSetup.GameScene, OpenSceneMode.Single);
            var streaming = main.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GtaSaSectorStreaming>(true)).FirstOrDefault();
            if (!streaming) throw new Exception("Streamer do mapa completo ausente.");
            var entries = streaming.sectors.Where(s => s != null && !string.IsNullOrEmpty(s.scene)).ToArray();
            EnsureMeshFolder();
            Physics.queriesHitBackfaces = false;
            foreach (var group in candidates.GroupBy(i => i.sector))
            {
                var owner = EditorSceneManager.OpenScene(SectorFolder + group.Key + ".unity", OpenSceneMode.Single);
                var placements = owner.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                    .Where(t => t.name.Contains("#")).ToArray();
                var bounds = new List<Bounds>();
                foreach (var candidate in group)
                {
                    var matching = placements.Where(t => t.name == candidate.instance).ToArray();
                    if (matching.Length != 1) throw new Exception("Instância ambígua: " + candidate.sector + "/" + candidate.instance);
                    bounds.AddRange(MatchingFilters(matching[0], candidate).Select(f => f.GetComponent<MeshRenderer>().bounds));
                }
                // A visual ground model may span several owner tiles. Load the
                // source sectors whose real bounds intersect its visible extent.
                foreach (var entry in entries)
                    if (entry.scene != group.Key && bounds.Any(b => IntersectsXZ(entry.bounds, b)))
                    {
                        string path = SectorFolder + entry.scene + ".unity";
                        if (!File.Exists(path)) throw new FileNotFoundException("Setor vizinho ausente", path);
                        EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    }
                Physics.SyncTransforms();
                bool changed = false;
                foreach (var candidate in group)
                {
                    var placement = placements.Single(t => t.name == candidate.instance);
                    var filters = MatchingFilters(placement, candidate).ToArray();
                    if (filters.Length == 0) throw new Exception("Malha auditada mudou: " + candidate.instance + " / " + candidate.asset);
                    foreach (var filter in filters)
                    {
                        var item = RepairFilter(candidate, filter, report.backupDirectory);
                        items.Add(item);
                        changed |= item.addedTriangles > 0;
                        report.inspectedMeshes++;
                        report.addedTriangles += item.addedTriangles;
                        report.addedNodes += item.addedTriangles > 0 && item.existingTriangles == 0 ? 1 : 0;
                        report.verifiedPatchSamples += item.verifiedSamples;
                        report.failedPatchSamples += item.failedSamples;
                        if (item.failedSamples > 0) errors.Add("Piso suplementar sem apoio após criação: " + candidate.instance + " samples=" + item.failedSamples);
                        if (item.missingMeasuredWitnessesAfter > 0) errors.Add("Testemunha nativa continua sem piso: " + candidate.instance + " points=" + item.missingMeasuredWitnessesAfter);
                    }
                }
                if (changed)
                {
                    EditorSceneManager.MarkSceneDirty(owner);
                    if (!EditorSceneManager.SaveScene(owner)) throw new IOException("Falha ao salvar setor: " + group.Key);
                    report.changedScenes++;
                }
                AssetDatabase.SaveAssets();
                Debug.Log("LOS_SANTOS_SURFACE_REPAIR_SECTOR " + group.Key + " changed=" + changed + " triangles=" + report.addedTriangles);
            }
        }
        catch (Exception exception) { errors.Add(exception.ToString()); Debug.LogException(exception); }
        finally
        {
            Physics.queriesHitBackfaces = backfaces;
            try
            {
                if (setup.Length > 0 && setup.All(s => !string.IsNullOrEmpty(s.path))) EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            catch (Exception exception) { errors.Add("Scene restore: " + exception.Message); }
            report.items = items.ToArray(); report.errors = errors.ToArray(); report.passed = errors.Count == 0;
            File.WriteAllText(Path.Combine(Output, "surface-collision-repair.json"), JsonUtility.ToJson(report, true));
        }
        if (!report.passed) throw new Exception("LOS_SANTOS_SURFACE_REPAIR_FAILED: " + string.Join(" | ", errors));
        Debug.Log("LOS_SANTOS_SURFACE_REPAIR_OK meshes=" + report.inspectedMeshes + " scenes=" + report.changedScenes +
            " triangles=" + report.addedTriangles + " verifiedSamples=" + report.verifiedPatchSamples);
    }

    static bool EligibleCandidate(AuditItem item)
    {
        if (item == null || item.missing <= 0 || string.IsNullOrEmpty(item.instance) || string.IsNullOrEmpty(item.asset) ||
            string.IsNullOrEmpty(item.mesh) || string.IsNullOrEmpty(item.sector) || !LosSantosAndroidSetup.Selected(item.sector)) return false;
        string model = item.instance.Split(new[] { " #" }, StringSplitOptions.None)[0];
        if (PatrolAudit)
        {
            if (item.supported != 0 || item.missing < 3) return false;
            if (ApprovedFor(item).Any()) return true;
            if (!item.eligibleRepair || item.needsSemanticReview) return false;
        }
        bool confirmed = ConfirmedModels.Contains(model) || (PatrolAudit && model.Equals("lae2_ground16", StringComparison.OrdinalIgnoreCase));
        return confirmed && GroundName.IsMatch(model) && !ExcludeName.IsMatch(model);
    }
    static IEnumerable<MeshFilter> MatchingFilters(Transform placement, AuditItem item) =>
        placement.GetComponentsInChildren<MeshFilter>(true).Where(filter => {
            var renderer = filter.GetComponent<MeshRenderer>();
            return filter.sharedMesh && renderer && renderer.enabled && filter.gameObject.activeInHierarchy &&
                filter.sharedMesh.name == item.mesh && AssetDatabase.GetAssetPath(filter.sharedMesh) == item.asset;
        });

    static Item RepairFilter(AuditItem candidate, MeshFilter filter, string backup)
    {
        string identifier = Hash(candidate.sector + "|" + candidate.instance + "|" + candidate.asset + "|" + candidate.mesh + "|" + Hierarchy(filter.transform)).Substring(0, 20);
        string assetPath = MeshFolder + "/surface-" + identifier + ".asset";
        string nodeName = NodePrefix + identifier;
        var item = new Item { sector = candidate.sector, instance = candidate.instance, sourceMesh = candidate.mesh,
            sourceAsset = candidate.asset, patchAsset = assetPath, patchNode = nodeName, bounds = filter.GetComponent<MeshRenderer>().bounds };
        var existingNodes = Enumerable.Range(0, filter.transform.childCount).Select(i => filter.transform.GetChild(i)).Where(t => t.name == nodeName).ToArray();
        if (existingNodes.Length > 1) throw new Exception("Reparo duplicado: " + nodeName);
        Transform node = existingNodes.FirstOrDefault();
        var patchMesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        if (node)
        {
            var colliders = node.GetComponents<MeshCollider>();
            if (!patchMesh || colliders.Length != 1 || colliders[0].sharedMesh != patchMesh || !colliders[0].enabled ||
                colliders[0].isTrigger || colliders[0].convex || !node.gameObject.activeSelf)
                throw new Exception("Reparo existente inconsistente; restaurar pelo backup: " + nodeName);
            item.existingTriangles = patchMesh.triangles.Length / 3;
        }
        else if (patchMesh) throw new Exception("Asset de reparo sem nó correspondente: " + assetPath);

        var eligible = new List<Triangle>();
        var patches = new List<Triangle>();
        var materials = filter.GetComponent<MeshRenderer>().sharedMaterials;
        var measuredPoints = PatrolAudit ? PatrolSourceSamples.Where(p => p.sector == candidate.sector && p.instance == candidate.instance &&
            p.asset == candidate.asset && p.mesh == candidate.mesh).Select(p => p.position).Where(p=>!Supported(p)).ToArray() : Array.Empty<Vector3>();
        item.missingMeasuredWitnessesBefore=measuredPoints.Length;
        using (var data = MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh))
        using (var vertices = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp))
        {
            data[0].GetVertices(vertices);
            for (int slot = 0; slot < data[0].subMeshCount; slot++)
            {
                if (slot >= materials.Length || !SolidFloorMaterial(materials[slot], candidate)) continue;
                var descriptor = data[0].GetSubMesh(slot);
                if (descriptor.topology != MeshTopology.Triangles) continue;
                using (var indices = new NativeArray<int>(descriptor.indexCount, Allocator.Temp))
                {
                    data[0].GetIndices(indices, slot);
                    for (int index = 0; index + 2 < indices.Length; index += 3)
                    {
                        var triangle = new Triangle(vertices[indices[index]], vertices[indices[index + 1]], vertices[indices[index + 2]]);
                        Vector3 a = filter.transform.TransformPoint(triangle.a), b = filter.transform.TransformPoint(triangle.b), c = filter.transform.TransformPoint(triangle.c);
                        Vector3 cross = Vector3.Cross(b - a, c - a);
                        Vector3 center = (a + b + c) / 3f;
                        if (cross.sqrMagnitude < 1e-8f || cross.normalized.y <= .72f || center.x < -256 || center.x > 3072 || center.z < 512 || center.z > 3072) continue;
                        if (PatrolAudit && (!candidate.eligibleRepair || candidate.needsSemanticReview) &&
                            !ReviewedFace(candidate, materials[slot].mainTexture.name, a, b, c)) continue;
                        eligible.Add(triangle);
                        if (!Supported(center)) item.missingSourceCentroidsBefore++;
                        bool densePlane=PatrolAudit && candidate.instance=="stormdrainLAE2_05 #17500" &&
                            materials[slot].mainTexture.name.Equals("tarmacplain_bank",StringComparison.OrdinalIgnoreCase) && Mathf.Abs(center.y-12.1929073f)<.01f;
                        SelectFragments(triangle, filter.transform, 0, patches, item, measuredPoints, densePlane);
                    }
                }
            }
        }
        item.eligibleSourceTriangles = eligible.Count;
        item.addedTriangles = patches.Count;
        item.noChange = patches.Count == 0;
        var examples = new List<Point>();
        foreach (var triangle in patches.Take(32))
            examples.Add(new Point { position = filter.transform.TransformPoint((triangle.a + triangle.b + triangle.c) / 3f), supportedBefore = false });
        if (patches.Count > 0)
        {
            var vertices = patchMesh ? patchMesh.vertices.ToList() : new List<Vector3>();
            var indices = patchMesh ? patchMesh.triangles.ToList() : new List<int>();
            foreach (var triangle in patches)
            {
                int start = vertices.Count;
                vertices.Add(triangle.a); vertices.Add(triangle.b); vertices.Add(triangle.c);
                indices.Add(start); indices.Add(start + 1); indices.Add(start + 2);
            }
            if (patchMesh)
            {
                File.Copy(assetPath, Path.Combine(backup, Path.GetFileName(assetPath)), false);
                if (File.Exists(assetPath + ".meta")) File.Copy(assetPath + ".meta", Path.Combine(backup, Path.GetFileName(assetPath) + ".meta"), false);
            }
            else patchMesh = new Mesh { name = "Recovered solid ground " + identifier };
            patchMesh.indexFormat = vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            patchMesh.SetVertices(vertices); patchMesh.SetTriangles(indices, 0); patchMesh.RecalculateBounds();
            if (AssetDatabase.Contains(patchMesh)) EditorUtility.SetDirty(patchMesh); else AssetDatabase.CreateAsset(patchMesh, assetPath);
            if (!node)
            {
                var created = new GameObject(nodeName);
                SceneManager.MoveGameObjectToScene(created, filter.gameObject.scene);
                node = created.transform; node.SetParent(filter.transform, false); created.isStatic = true;
                created.AddComponent<MeshCollider>();
            }
            var collider = node.GetComponent<MeshCollider>();
            collider.sharedMesh = null; collider.sharedMesh = patchMesh; collider.convex = false; collider.isTrigger = false;
            Physics.SyncTransforms();
        }
        // Verify every persisted triangle, including those from a previous run.
        // This also makes a no-op rerun a useful check rather than blind skipping.
        if (node && patchMesh)
        {
            var collider = node.GetComponent<MeshCollider>();
            var vertices = patchMesh.vertices;
            var indices = patchMesh.triangles;
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                var triangle = new Triangle(vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]]);
                foreach (var local in ProbePoints(triangle))
                {
                    Vector3 point = filter.transform.TransformPoint(local);
                    var ray = new Ray(point + Vector3.up * (Tolerance + .01f), Vector3.down);
                    bool hit = collider.Raycast(ray, out var result, Tolerance * 2 + .02f) && Mathf.Abs(result.point.y - point.y) <= Tolerance;
                    if (hit && Supported(point)) item.verifiedSamples++; else item.failedSamples++;
                }
            }
        }
        foreach (var triangle in eligible)
            if (!Supported(filter.transform.TransformPoint((triangle.a + triangle.b + triangle.c) / 3f))) item.missingSourceCentroidsAfter++;
        item.missingMeasuredWitnessesAfter=measuredPoints.Count(p=>!Supported(p));
        foreach (var example in examples) example.supportedAfter = Supported(example.position);
        item.repairedExamples = examples.ToArray();
        return item;
    }

    static void SelectFragments(Triangle triangle, Transform transform, int depth, List<Triangle> patches, Item item, Vector3[] measuredPoints, bool densePlane)
    {
        int supported = ProbePoints(triangle).Count(local => Supported(transform.TransformPoint(local)));
        Vector3 a = transform.TransformPoint(triangle.a), b = transform.TransformPoint(triangle.b), c = transform.TransformPoint(triangle.c);
        bool missingWitness=measuredPoints.Any(p=>ContainsWitness(a,b,c,p));
        float longest=Mathf.Max(Vector3.Distance(a,b),Mathf.Max(Vector3.Distance(b,c),Vector3.Distance(c,a)));
        // Four sparse probes can all hit on a very large face while a native
        // patrol witness inside it is missing. Refine that local branch too.
        if (supported == 4 && !missingWitness && (!densePlane || longest<=.35f)) return;
        if (supported == 0) { patches.Add(triangle); return; }
        // Refine only around the exact missing native patrol samples. This can
        // close a small corner gap without extending collision over supported
        // floor or refining unrelated layers of the same ground model.
        float minX=Mathf.Min(a.x,Mathf.Min(b.x,c.x))-.3f, maxX=Mathf.Max(a.x,Mathf.Max(b.x,c.x))+.3f;
        float minZ=Mathf.Min(a.z,Mathf.Min(b.z,c.z))-.3f, maxZ=Mathf.Max(a.z,Mathf.Max(b.z,c.z))+.3f;
        float centerY=(a.y+b.y+c.y)/3;
        bool focused=measuredPoints.Any(p=>p.x>=minX && p.x<=maxX && p.z>=minZ && p.z<=maxZ && Mathf.Abs(p.y-centerY)<1.5f);
        if (depth >= (densePlane || missingWitness ? 12 : focused ? 9 : MaxSubdivision) || Vector3.Cross(b - a, c - a).magnitude < (densePlane || focused ? .0005f : .05f))
        { item.skippedMixedFragments++; return; }
        Vector3 ab = (triangle.a + triangle.b) * .5f, bc = (triangle.b + triangle.c) * .5f, ca = (triangle.c + triangle.a) * .5f;
        SelectFragments(new Triangle(triangle.a, ab, ca), transform, depth + 1, patches, item, measuredPoints, densePlane);
        SelectFragments(new Triangle(ab, triangle.b, bc), transform, depth + 1, patches, item, measuredPoints, densePlane);
        SelectFragments(new Triangle(ca, bc, triangle.c), transform, depth + 1, patches, item, measuredPoints, densePlane);
        SelectFragments(new Triangle(ab, bc, ca), transform, depth + 1, patches, item, measuredPoints, densePlane);
    }
    static bool ContainsWitness(Vector3 a, Vector3 b, Vector3 c, Vector3 p)
    {
        double denominator=(b.z-c.z)*(double)(a.x-c.x)+(c.x-b.x)*(double)(a.z-c.z);
        if (Math.Abs(denominator)<1e-12) return false;
        double u=((b.z-c.z)*(double)(p.x-c.x)+(c.x-b.x)*(double)(p.z-c.z))/denominator;
        double v=((c.z-a.z)*(double)(p.x-c.x)+(a.x-c.x)*(double)(p.z-c.z))/denominator;
        double w=1-u-v;
        return u>=-.00002 && v>=-.00002 && w>=-.00002 && Math.Abs(u*a.y+v*b.y+w*c.y-p.y)<.08;
    }
    static IEnumerable<Vector3> ProbePoints(Triangle triangle)
    {
        Vector3 center = (triangle.a + triangle.b + triangle.c) / 3f;
        yield return center;
        yield return Vector3.Lerp(triangle.a, center, .12f);
        yield return Vector3.Lerp(triangle.b, center, .12f);
        yield return Vector3.Lerp(triangle.c, center, .12f);
    }
    static bool Supported(Vector3 point) => Physics.RaycastAll(point + Vector3.up * (Tolerance + .01f), Vector3.down,
        Tolerance * 2 + .02f, ~0, QueryTriggerInteraction.Ignore).Any(hit => hit.normal.y > .6f && Mathf.Abs(hit.point.y - point.y) <= Tolerance);
    static bool SolidFloorMaterial(Material material, AuditItem candidate)
    {
        if (!material || !material.mainTexture || !material.HasProperty("_Mode") || Mathf.Abs(material.GetFloat("_Mode")) > .001f ||
            !material.HasProperty("_Color") || material.color.a < .95f || material.renderQueue >= 2450 ||
            material.IsKeywordEnabled("_ALPHATEST_ON") || material.IsKeywordEnabled("_ALPHABLEND_ON") || material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON")) return false;
        // Match the texture itself. Matching a material name such as Roads38CE_0
        // would accidentally authorize every material on that road model.
        string texture = material.mainTexture.name;
        return !ExcludeTexture.IsMatch(texture) && (GroundTexture.IsMatch(texture) || PatrolAudit && ApprovedFor(candidate).Any(a => a.texture == texture));
    }
    static IEnumerable<Approval> ApprovedFor(AuditItem item) => PatrolApprovals.Where(a => a.approvedRepair &&
        a.sector == item.sector && a.instance == item.instance && a.asset == item.asset && a.mesh == item.mesh);
    static bool ReviewedFace(AuditItem item, string texture, Vector3 a, Vector3 b, Vector3 c)
    {
        foreach (var review in ApprovedFor(item).Where(r => r.texture == texture && r.boundsUnity != null))
        {
            var bounds = new Bounds(); bounds.SetMinMax(review.boundsUnity.min - Vector3.one * .03f, review.boundsUnity.max + Vector3.one * .03f);
            if (!bounds.Contains(a) || !bounds.Contains(b) || !bounds.Contains(c)) continue;
            if (review.trianglesWorld == null || review.trianglesWorld.Length == 0) return true;
            if (review.trianglesWorld.Any(t => SameVertex(a,t.a,t.b,t.c) && SameVertex(b,t.a,t.b,t.c) && SameVertex(c,t.a,t.b,t.c))) return true;
        }
        return false;
    }
    static bool SameVertex(Vector3 point, Vector3 a, Vector3 b, Vector3 c) =>
        (point-a).sqrMagnitude < .0001f || (point-b).sqrMagnitude < .0001f || (point-c).sqrMagnitude < .0001f;
    static bool IntersectsXZ(Bounds a, Bounds b) => a.min.x <= b.max.x + 1 && a.max.x >= b.min.x - 1 && a.min.z <= b.max.z + 1 && a.max.z >= b.min.z - 1;
    static string Hierarchy(Transform node)
    {
        var parts = new List<string>();
        for (var current = node; current; current = current.parent) parts.Add(current.name + "[" + current.GetSiblingIndex() + "]");
        parts.Reverse(); return string.Join("/", parts);
    }
    static string Hash(string value)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
    }
    static string FileHash(string path)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }
    static void EnsureMeshFolder()
    {
        string parent = "Assets";
        foreach (string part in MeshFolder.Split('/').Skip(1))
        { if (!AssetDatabase.IsValidFolder(parent + "/" + part)) AssetDatabase.CreateFolder(parent, part); parent += "/" + part; }
    }
}
