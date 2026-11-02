using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Read-only city-wide inspection. Reports samples and suspects; never saves game scenes.
public static class LosSantosGroundAudit
{
    const string SectorFolder = "Assets/Scenes/GTASanAndreasModFullSectors/";
    const float CellSize = 256f;
    const float HeightTolerance = .45f;
    const int MaxSamplesPerMesh = 192;
    static readonly Regex GroundNames = new Regex(@"road|ground|terrain|land|hill|pave|sidewalk|beach|coast|grass|park|alley|bridge|freeway|s_lodbit|REPARO", RegexOptions.IgnoreCase);
    static readonly Regex GroundTextures = new Regex(@"road|asphalt|pave|sidewalk|ground|grass|dirt|sand|concrete|greyground|tarmac", RegexOptions.IgnoreCase);
    static readonly Regex ExcludedSurfaces = new Regex(@"water|roof|ceiling|window|glass|wall|fence|billboard|sign|leaf|leaves", RegexOptions.IgnoreCase);

    [Serializable] sealed class Report
    {
        public string utc, sourceScene, tag, method;
        public int sectorCount, cellsInspected, loadedSectorPeak, meshesInspected, samples, supportedSamples, missingSamples;
        public int unreadableMeshes, unsupportedStructureSuspects;
        public string[] errors;
        public MeshResult[] ground;
        public StructureResult[] structures;
    }
    [Serializable] sealed class MeshResult
    {
        public string sector, instance, mesh, asset, classification;
        public int samples, supported, missing;
        public float greatestMissingGap;
        public Sample[] examples;
    }
    [Serializable] sealed class Sample
    {
        public Vector3 position;
        public float normalY, closestCollisionGap;
        public string material, supportingObject;
    }
    [Serializable] sealed class StructureResult
    {
        public string sector, instance, reason;
        public Bounds bounds;
        public int sampled, supported;
        public float minimumGap;
    }
    sealed class Cell
    {
        public string name;
        public Bounds bounds;
    }

    [MenuItem("Horizonte/Los Santos/Auditar chão e estruturas (somente leitura)")]
    public static void Run()
    {
        var setup = EditorSceneManager.GetSceneManagerSetup();
        var report = new Report {
            utc = DateTime.UtcNow.ToString("o"), sourceScene = GameUpdateSetup.GameScene,
            tag = Argument("--ls-audit-tag", "current"),
            method = "Every Los Santos 256m cell; actual intersecting sector bounds; upward ground triangles >=0.5m²; max 192 spatial samples per mesh per cell; collision height tolerance 0.45m. Structure results are review suspects, not confirmed errors."
        };
        var errors = new List<string>();
        var results = new List<MeshResult>();
        var structures = new List<StructureResult>();
        var structureIds = new HashSet<string>(StringComparer.Ordinal);
        bool previousBackfaces = Physics.queriesHitBackfaces;
        try
        {
            var source = EditorSceneManager.OpenScene(GameUpdateSetup.GameScene, OpenSceneMode.Single);
            var streamer = source.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GtaSaSectorStreaming>(true)).FirstOrDefault();
            if (!streamer || streamer.sectors == null) throw new Exception("Streamer San Andreas ausente.");
            var entries = streamer.sectors.Where(s => s != null && !string.IsNullOrEmpty(s.scene)).ToArray();
            var cells = entries.Where(s => LosSantosAndroidSetup.Selected(s.scene)).Select(s => CellFor(s.scene)).Where(c => c != null).ToArray();
            report.sectorCount = cells.Length;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var loaded = new Dictionary<string, Scene>(StringComparer.Ordinal);
            Physics.queriesHitBackfaces = false;
            foreach (var cell in cells)
            {
                // Bounds, rather than owner tile names, include terrain spanning neighbouring cells.
                var needed = new HashSet<string>(entries.Where(e => IntersectsXZ(e.bounds, cell.bounds, 2f)).Select(e => e.scene), StringComparer.Ordinal);
                Synchronize(loaded, needed, errors);
                report.loadedSectorPeak = Mathf.Max(report.loadedSectorPeak, loaded.Count);
                Physics.SyncTransforms();
                foreach (var pair in loaded)
                    foreach (var root in pair.Value.GetRootGameObjects())
                    {
                        foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
                        {
                            var renderer = filter.GetComponent<MeshRenderer>();
                            if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy || !filter.sharedMesh ||
                                filter.name.StartsWith("COL ", StringComparison.Ordinal) || !IntersectsXZ(renderer.bounds, cell.bounds, 0)) continue;
                            try
                            {
                                var result = InspectMesh(filter, renderer, cell.bounds, pair.Key);
                                if (result == null) continue;
                                results.Add(result);
                                report.meshesInspected++;
                                report.samples += result.samples;
                                report.supportedSamples += result.supported;
                                report.missingSamples += result.missing;
                            }
                            catch (Exception exception)
                            {
                                report.unreadableMeshes++;
                                if (errors.Count < 100) errors.Add(pair.Key + ": " + Instance(filter.transform).name + " / " + exception.Message);
                            }
                        }
                        foreach (var transform in root.GetComponentsInChildren<Transform>())
                        {
                            if (!transform.name.StartsWith("barraco #", StringComparison.OrdinalIgnoreCase)) continue;
                            string key = pair.Key + "/" + transform.name + "/" + transform.position;
                            if (!InsideXZ(transform.position, cell.bounds) || !structureIds.Add(key)) continue;
                            var suspect = InspectStructure(transform, pair.Key);
                            if (suspect != null) structures.Add(suspect);
                        }
                    }
                report.cellsInspected++;
                if (report.cellsInspected % 8 == 0)
                {
                    Debug.Log("LOS_SANTOS_AUDIT_PROGRESS cells=" + report.cellsInspected + "/" + cells.Length + " samples=" + report.samples + " missing=" + report.missingSamples);
                    Write(report, results, structures, errors);
                    EditorUtility.UnloadUnusedAssetsImmediate();
                }
            }
            foreach (var scene in loaded.Values) EditorSceneManager.CloseScene(scene, true);
        }
        catch (Exception exception)
        {
            errors.Add(exception.ToString());
            Debug.LogException(exception);
        }
        finally
        {
            Physics.queriesHitBackfaces = previousBackfaces;
            Write(report, results, structures, errors);
            RestoreSetup(setup);
        }
        Debug.Log("LOS_SANTOS_GROUND_AUDIT_COMPLETE cells=" + report.cellsInspected + " samples=" + report.samples + " missing=" + report.missingSamples + " errors=" + errors.Count);
        if (errors.Count > 0) throw new Exception("Auditoria incompleta: consulte ground-audit-" + report.tag + ".json");
    }

    static MeshResult InspectMesh(MeshFilter filter, MeshRenderer renderer, Bounds cell, string sector)
    {
        var instance = Instance(filter.transform);
        string model = instance.name;
        var materials = renderer.sharedMaterials;
        bool groundModel = GroundNames.IsMatch(model) && !ExcludedSurfaces.IsMatch(model);
        if (!groundModel && !materials.Any(GroundMaterial)) return null;
        var result = new MeshResult { sector = sector, instance = model, mesh = filter.sharedMesh.name,
            asset = AssetDatabase.GetAssetPath(filter.sharedMesh), classification = groundModel ? "ground model" : "ground texture (review heuristic)" };
        var examples = new List<Sample>();
        var occupied = new HashSet<long>();
        // Editor API reads compressed/non-readable imported meshes without changing their import settings.
        using (var data = MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh))
        using (var vertices = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp))
        {
            data[0].GetVertices(vertices);
            for (int slot = 0; slot < data[0].subMeshCount; slot++)
            {
                var descriptor = data[0].GetSubMesh(slot);
                if (descriptor.topology != MeshTopology.Triangles) continue;
                Material material = slot < materials.Length ? materials[slot] : null;
                if (!groundModel && !GroundMaterial(material)) continue;
                if (material && ExcludedSurfaces.IsMatch(TextureName(material))) continue;
                using (var indices = new NativeArray<int>(descriptor.indexCount, Allocator.Temp))
                {
                    data[0].GetIndices(indices, slot);
                    // A coprime permutation samples across the entire submesh when the cap is reached.
                    int triangles = indices.Length / 3;
                    int step = CoprimeStep(triangles);
                    for (int count = 0, triangle = 0; count < triangles && result.samples < MaxSamplesPerMesh; count++, triangle = (triangle + step) % triangles)
                    {
                        int index = triangle * 3;
                        Vector3 a = filter.transform.TransformPoint(vertices[indices[index]]);
                        Vector3 b = filter.transform.TransformPoint(vertices[indices[index + 1]]);
                        Vector3 c = filter.transform.TransformPoint(vertices[indices[index + 2]]);
                        Vector3 cross = Vector3.Cross(b - a, c - a);
                        if (cross.magnitude < 1f || cross.normalized.y < .72f) continue;
                        Vector3 point = (a + b + c) / 3f;
                        if (!InsideXZ(point, cell) || point.x < -256 || point.x > 3072 || point.z < 512 || point.z > 3072) continue;
                        // 3m spatial bins prevent tessellation density from dominating the report.
                        long key = ((long)Mathf.FloorToInt(point.x / 3f) << 32) ^ (uint)Mathf.FloorToInt(point.z / 3f);
                        if (!occupied.Add(key)) continue;
                        result.samples++;
                        if (HasSupport(point, HeightTolerance, null, out float gap, out string support)) result.supported++;
                        else
                        {
                            result.missing++;
                            result.greatestMissingGap = Mathf.Max(result.greatestMissingGap, gap);
                            if (examples.Count < 24) examples.Add(new Sample { position = point, normalY = cross.normalized.y,
                                closestCollisionGap = gap, material = TextureName(material), supportingObject = support });
                        }
                    }
                }
            }
        }
        result.examples = examples.ToArray();
        return result.samples > 0 ? result : null;
    }

    static bool HasSupport(Vector3 point, float tolerance, Transform exclude, out float gap, out string support)
    {
        gap = 1000;
        support = "none within 80m below";
        foreach (var hit in Physics.RaycastAll(point + Vector3.up * tolerance, Vector3.down, 80 + tolerance, ~0, QueryTriggerInteraction.Ignore))
        {
            if (exclude && hit.collider.transform.IsChildOf(exclude)) continue;
            if (hit.normal.y < .6f) continue;
            float difference = Mathf.Abs(point.y - hit.point.y);
            if (difference < gap) { gap = difference; support = Instance(hit.collider.transform).name; }
        }
        return gap <= tolerance;
    }

    static StructureResult InspectStructure(Transform instance, string sector)
    {
        var renderers = instance.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled).ToArray();
        if (renderers.Length == 0) return null;
        Bounds bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        int supported = 0;
        float minimumGap = 1000;
        foreach (var offset in new[] { Vector2.zero, new Vector2(-.3f, -.3f), new Vector2(.3f, -.3f), new Vector2(-.3f, .3f), new Vector2(.3f, .3f) })
        {
            var point = new Vector3(bounds.center.x + bounds.size.x * offset.x, bounds.min.y, bounds.center.z + bounds.size.z * offset.y);
            if (HasSupport(point, 2.5f, instance, out float gap, out _)) supported++;
            minimumGap = Mathf.Min(minimumGap, gap);
        }
        if (supported > 0) return null;
        return new StructureResult { sector = sector, instance = instance.name, bounds = bounds, sampled = 5,
            supported = supported, minimumGap = minimumGap, reason = "No external collider within 2.5m of five footprint base samples; source placement requires visual review." };
    }

    static Transform Instance(Transform transform)
    {
        for (var current = transform; current; current = current.parent)
            if (current.name.Contains("#") || current.name.StartsWith("REPARO", StringComparison.Ordinal)) return current;
        return transform;
    }
    static bool GroundMaterial(Material material) => material && GroundTextures.IsMatch(TextureName(material)) && !ExcludedSurfaces.IsMatch(TextureName(material));
    static string TextureName(Material material) => material ? material.name + "/" + (material.mainTexture ? material.mainTexture.name : "") : "none";
    static bool InsideXZ(Vector3 p, Bounds b) => p.x >= b.min.x && p.x < b.max.x && p.z >= b.min.z && p.z < b.max.z;
    static bool IntersectsXZ(Bounds a, Bounds b, float margin) => a.min.x <= b.max.x + margin && a.max.x >= b.min.x - margin && a.min.z <= b.max.z + margin && a.max.z >= b.min.z - margin;
    static int CoprimeStep(int count)
    {
        if (count <= 1) return 1;
        int step = Mathf.Min(7919, count - 1);
        while (Gcd(step, count) != 1) step--;
        return step;
    }
    static int Gcd(int a, int b) { while (b != 0) { int next = a % b; a = b; b = next; } return a; }
    static Cell CellFor(string name)
    {
        var match = Regex.Match(name, @"^GTASASector_([mp])(\d+)_([mp])(\d+)$");
        if (!match.Success) return null;
        int x = int.Parse(match.Groups[2].Value) * (match.Groups[1].Value == "m" ? -1 : 1);
        int y = int.Parse(match.Groups[4].Value) * (match.Groups[3].Value == "m" ? -1 : 1);
        return new Cell { name = name, bounds = new Bounds(new Vector3(x * CellSize + 128, 0, -y * CellSize - 128), new Vector3(CellSize, 2000, CellSize)) };
    }
    static void Synchronize(Dictionary<string, Scene> loaded, HashSet<string> needed, List<string> errors)
    {
        foreach (string key in loaded.Keys.Where(key => !needed.Contains(key)).ToArray())
        { EditorSceneManager.CloseScene(loaded[key], true); loaded.Remove(key); }
        foreach (string name in needed.OrderBy(n => n, StringComparer.Ordinal))
        {
            if (loaded.ContainsKey(name)) continue;
            string path = SectorFolder + name + ".unity";
            if (!File.Exists(path)) { if (!errors.Contains(path)) errors.Add("Missing sector: " + path); continue; }
            loaded.Add(name, EditorSceneManager.OpenScene(path, OpenSceneMode.Additive));
        }
    }
    static string Argument(string name, string fallback)
    {
        string prefix = name + "=";
        string arg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal));
        return arg == null ? fallback : Regex.Replace(arg.Substring(prefix.Length), @"[^a-zA-Z0-9_-]", "_");
    }
    static void Write(Report report, List<MeshResult> results, List<StructureResult> structures, List<string> errors)
    {
        string folder = Path.GetFullPath("../tools/los-santos-repair");
        Directory.CreateDirectory(folder);
        report.ground = results.ToArray();
        report.structures = structures.ToArray();
        report.unsupportedStructureSuspects = structures.Count;
        report.errors = errors.ToArray();
        File.WriteAllText(Path.Combine(folder, "ground-audit-" + report.tag + ".json"), JsonUtility.ToJson(report, true));
    }

    // Separate optional entry point; requires a graphics device (omit -nographics).
    public static void Capture()
    {
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            var source = EditorSceneManager.OpenScene(GameUpdateSetup.GameScene, OpenSceneMode.Single);
            var streamer = source.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GtaSaSectorStreaming>(true)).First();
            var entries = streamer.sectors;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.white * .75f;
            RenderSettings.fog = false;
            var sun = new GameObject("Audit sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1; sun.transform.rotation = Quaternion.Euler(45, -35, 0);
            var camera = new GameObject("Audit camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.35f, .48f, .63f);
            camera.farClipPlane = 1800; camera.nearClipPlane = .5f; camera.fieldOfView = 62;
            var loaded = new Dictionary<string, Scene>(StringComparer.Ordinal);
            var errors = new List<string>();
            string folder = Path.GetFullPath("../tools/los-santos-repair/captures-" + Argument("--ls-audit-tag", "current"));
            Directory.CreateDirectory(folder);
            foreach (int x in new[] { 200, 1000, 1800, 2600 })
                foreach (int z in new[] { 800, 1600, 2400 })
                {
                    var bounds = new Bounds(new Vector3(x, 0, z), new Vector3(900, 2000, 900));
                    Synchronize(loaded, new HashSet<string>(entries.Where(s => s != null && IntersectsXZ(s.bounds, bounds, 0)).Select(s => s.scene)), errors);
                    Vector3 focus = new Vector3(x, 30, z);
                    camera.transform.position = focus + new Vector3(-80, 650, -250);
                    camera.transform.rotation = Quaternion.LookRotation(focus - camera.transform.position);
                    var target = new RenderTexture(1280, 720, 24);
                    var previous = RenderTexture.active;
                    var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                    try
                    {
                        camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                        File.WriteAllBytes(Path.Combine(folder, "district-" + x + "-" + z + ".png"), image.EncodeToPNG());
                    }
                    finally
                    { camera.targetTexture = null; RenderTexture.active = previous; Object.DestroyImmediate(image); Object.DestroyImmediate(target); }
                    Debug.Log("LOS_SANTOS_CAPTURE x=" + x + " z=" + z);
                }
            if (errors.Count > 0) throw new Exception(string.Join("\n", errors));
        }
        finally { RestoreSetup(setup); }
    }

    static void RestoreSetup(SceneSetup[] setup)
    {
        if (setup.Length > 0 && setup.All(s => !string.IsNullOrEmpty(s.path))) EditorSceneManager.RestoreSceneManagerSetup(setup);
        else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }
}
