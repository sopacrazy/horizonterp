using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Native player-sized patrol. Visual road geometry supplies routes independently
// of COL, so missing collision cannot silently remove a route from the test.
public static class LosSantosPatrolVerification
{
    const string Main = "Assets/Scenes/TestePersonagem.unity";
    const string SectorFolder = "Assets/Scenes/GTASanAndreasModFullSectors/";
    const string Output = "../tools/los-santos-patrol";
    const float Grid = 8, Speed = 54, MaxMove = .18f, Gravity = 22;
    static readonly Regex RoadModel = new Regex("road|freeway|hiway|highway|bridge|path|REPARO CHAO", RegexOptions.IgnoreCase);
    static readonly Regex NotRoad = new Regex("roadblock|sign|billboard|lamp|tree|house|apart|roof|alpha", RegexOptions.IgnoreCase);
    static readonly Regex RoadTexture = new Regex("asphalt|road|tarmac|snpedtest|craproad|sidelatino|sidewalk|pav(e|ing)|sjmhoodlawn|greyground", RegexOptions.IgnoreCase);
    static readonly Regex SolidTexture = new Regex("concrete|ground|grass|dirt|gravel|sand|plazatile", RegexOptions.IgnoreCase);
    static readonly Regex NotTexture = new Regex("roof|wall|glass|leaf|water|window|metal|fence|alpha", RegexOptions.IgnoreCase);
    static bool continueAfterFindings;
    public static Action<Vector3, Vector3, string> OnPatrolSample;
    public static Action<string, Vector3, string> OnIncident;

    [Serializable] sealed class Report
    {
        public string tag, utc, method, status;
        public int sectorCount, cellsInspected, roadCells, emptyCells, loadedSectorPeak, visualTriangles, roadNodes, edges, routesCompleted, routesWithFindings;
        public int continuationRestarts, skippedUnsafeNodes;
        public bool restartAfterFindings;
        public float speed, maximumMove, gridMetres, plannedMetres, traveledMetres, simulatedSeconds;
        public bool nativeControllerCapability;
        public Settings controller;
        public CellResult[] cells;
        public RouteResult[] routes;
        public Incident[] incidents;
        public string[] errors;
    }
    [Serializable] sealed class Settings
    {
        public float height, radius, stepOffset, slopeLimit, skinWidth, minMoveDistance;
        public Vector3 center;
    }
    [Serializable] sealed class CellResult
    {
        public string sector, status;
        public int triangles, nodes, edges, routes, findings;
        public float plannedMetres, traveledMetres;
    }
    [Serializable] public sealed class Incident
    {
        public string id, type, sector, routeId, model, collider, detail;
        public Vector3 position;
        public float expectedHeight, feetHeight, normalY;
        public bool nativeGrounded, sphereSupported, centerRayFound;
        public int collisionFlags;
    }
    [Serializable] sealed class RouteResult
    {
        public string id, sector, status;
        public Vector3 start, finalPosition;
        public Vector3[] waypoints;
        public float plannedMetres, traveledMetres, simulatedSeconds, maximumDrop, largestStep;
        public int steps, groundedSteps, firstUnfinishedWaypoint;
    }
    sealed class Cell { public string name; public Bounds bounds; }
    sealed class Node
    {
        public int id, x, z, layer;
        public Vector3 point;
        public string model;
    }
    sealed class Edge { public Node a, b; public bool used; public float Length => Vector3.Distance(a.point, b.point); }
    struct Triangle
    {
        public Vector3 a, b, c;
        public string model;
    }

    public static void RunWithContinuations()
    {
        continueAfterFindings = true;
        try { Run(); } finally { continueAfterFindings = false; }
    }

    [MenuItem("Horizonte/Los Santos/Patrulhar toda a cidade em alta velocidade")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Pare o Play Mode antes da patrulha de auditoria.");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        var report = new Report { tag = Arg("--ls-patrol-tag", "current"), utc = DateTime.UtcNow.ToString("o"), speed = Speed,
            maximumMove = MaxMove, gridMetres = Grid, restartAfterFindings = continueAfterFindings,
            method = "Every Los Santos sector cell. Visual asphalt/path triangles rasterised at 8m, with separate height layers; contiguous native CharacterController polylines up to 220m, including tile-border connections. 54m/s simulated with <=0.18m physics substeps. Each route starts separately; no teleport occurs within its measured travel. This is a static-geometry patrol, not a measured asynchronous runtime streaming/FPS test." };
        var errors = new List<string>(); var cells = new List<CellResult>(); var results = new List<RouteResult>(); var incidents = new List<Incident>();
        var loaded = new Dictionary<string, Scene>(StringComparer.Ordinal);
        bool previousBackfaces = Physics.queriesHitBackfaces;
        try
        {
            var source = EditorSceneManager.OpenScene(Main, OpenSceneMode.Single);
            var stream = source.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GtaSaSectorStreaming>(true)).Single();
            var entries = stream.sectors.Where(e => e != null && !string.IsNullOrEmpty(e.scene)).ToArray();
            var patrolCells = entries.Where(e => LosSantosAndroidSetup.Selected(e.scene)).Select(e => MakeCell(e.scene)).OrderBy(c => c.bounds.center.x).ThenBy(c => c.bounds.center.z).ToArray();
            report.sectorCount = patrolCells.Length;
            report.controller = ReadSettings();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            report.nativeControllerCapability = Capability(report.controller);
            if (!report.nativeControllerCapability) throw new Exception("Native CharacterController.Move did not collide with the temporary capability floor.");
            Physics.queriesHitBackfaces = false;
            foreach (var cell in patrolCells)
            {
                var needed = new HashSet<string>(entries.Where(e => Intersects(e.bounds, cell.bounds, 20)).Select(e => e.scene), StringComparer.Ordinal);
                Synchronize(loaded, needed);
                report.loadedSectorPeak = Mathf.Max(report.loadedSectorPeak, loaded.Count);
                Physics.SyncTransforms();
                var triangles = GatherRoads(loaded.Values, cell.bounds);
                var nodes = Rasterise(triangles, cell.bounds);
                var edges = BuildEdges(nodes, triangles, cell.bounds);
                var paths = BuildRoutes(edges);
                if (cell.name == "GTASASector_p8_m6")
                    paths.Insert(0, ForcedRoute(new Vector3(2213, 23, 1537), new Vector3(2213, 23, 1396), "mandatory-street14-north-extension"));
                if (cell.name == "GTASASector_p8_m7")
                    paths.Insert(0, ForcedRoute(new Vector3(2085, 13, 1672), new Vector3(2085, 13, 1740), "mandatory-alley42-continuation"));
                var item = new CellResult { sector = cell.name, triangles = triangles.Count, nodes = nodes.Count, edges = edges.Count, routes = paths.Count,
                    status = edges.Count == 0 ? nodes.Count > 0 ? "visual road samples without connected 8m corridor" : "no classified road corridor" : "patrolled" };
                if (edges.Count == 0) report.emptyCells++; else report.roadCells++;
                int beforeIncidents = incidents.Count;
                foreach (var path in paths)
                {
                    float uniquePlan = 0;
                    for (int k = 1; k < path.Count; k++) uniquePlan += Vector3.Distance(path[k - 1].point, path[k].point);
                    item.plannedMetres += uniquePlan; report.plannedMetres += uniquePlan;
                    int startIndex = 0;
                    while (startIndex < path.Count - 1)
                    {
                        var remainder = path.Skip(startIndex).ToList();
                        string id = cell.name + "-" + results.Count.ToString("D5");
                        var result = Patrol(remainder, cell.name, id, report.controller, incidents);
                        results.Add(result); item.traveledMetres += result.traveledMetres;
                        report.traveledMetres += result.traveledMetres; report.simulatedSeconds += result.simulatedSeconds;
                        if (result.status == "completed") { report.routesCompleted++; break; }
                        report.routesWithFindings++;
                        if (!continueAfterFindings) break;
                        // A finding ends the continuous measurement. Resume as a
                        // new explicitly separate route after a safe source node.
                        int next = startIndex + Mathf.Max(1, result.firstUnfinishedWaypoint) + 1;
                        while (next < path.Count - 1 && !SafeRestart(path[next].point, report.controller))
                        { report.skippedUnsafeNodes++; next++; }
                        if (next >= path.Count - 1) break;
                        startIndex = next; report.continuationRestarts++;
                    }
                }
                item.findings = incidents.Count - beforeIncidents;
                cells.Add(item); report.visualTriangles += triangles.Count; report.roadNodes += nodes.Count; report.edges += edges.Count; report.cellsInspected++;
                Write(report, cells, results, incidents, errors);
                Debug.Log("LOS_SANTOS_PATROL_PROGRESS cells=" + report.cellsInspected + "/" + report.sectorCount + " metres=" + report.traveledMetres + " findings=" + incidents.Count);
                if (report.cellsInspected % 6 == 0) EditorUtility.UnloadUnusedAssetsImmediate();
            }
            report.status = "completed";
        }
        catch (Exception exception) { report.status = "incomplete"; errors.Add(exception.ToString()); Debug.LogException(exception); }
        finally
        {
            Physics.queriesHitBackfaces = previousBackfaces;
            try
            {
                if (setup.Length > 0 && setup.All(s => !string.IsNullOrEmpty(s.path))) EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            catch (Exception exception) { errors.Add("Scene restore: " + exception.Message); }
            Write(report, cells, results, incidents, errors);
        }
        if (errors.Count > 0) throw new Exception("LOS_SANTOS_PATROL_INCOMPLETE " + string.Join(" | ", errors));
        Debug.Log("LOS_SANTOS_PATROL_COMPLETE cells=" + report.cellsInspected + " routes=" + results.Count + " metres=" + report.traveledMetres + " findings=" + incidents.Count);
    }

    static Settings ReadSettings()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Jogador.prefab");
        var c = prefab ? prefab.GetComponent<CharacterController>() : null;
        if (!c) throw new Exception("Player CharacterController prefab absent.");
        return new Settings { height = c.height, radius = c.radius, center = c.center, stepOffset = c.stepOffset,
            slopeLimit = c.slopeLimit, skinWidth = c.skinWidth, minMoveDistance = c.minMoveDistance };
    }
    static CharacterController Controller(Settings s)
    {
        var go = new GameObject("Temporary fast patrol capsule") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
        var c = go.AddComponent<CharacterController>(); c.enabled = false;
        c.height = s.height; c.radius = s.radius; c.center = s.center; c.stepOffset = s.stepOffset; c.slopeLimit = s.slopeLimit;
        c.skinWidth = s.skinWidth; c.minMoveDistance = s.minMoveDistance; c.detectCollisions = true;
        return c;
    }
    static bool Capability(Settings s)
    {
        var floor = new GameObject("Temporary patrol capability floor") { hideFlags = HideFlags.HideAndDontSave };
        floor.transform.position = new Vector3(-10000, 999.5f, -10000); floor.AddComponent<BoxCollider>().size = new Vector3(20, 1, 20);
        var c = Controller(s);
        try
        {
            c.transform.position = new Vector3(-10000, 1000.04f - Foot(s), -10000); c.enabled = true; Physics.SyncTransforms();
            for (int i = 0; i < 20; i++) c.Move(new Vector3(.1f, -.04f, 0));
            return c.isGrounded && c.transform.position.x > -9998.3f && Mathf.Abs(c.transform.position.y + Foot(s) - 1000) < .2f;
        }
        finally { Object.DestroyImmediate(c.gameObject); Object.DestroyImmediate(floor); }
    }
    static List<Triangle> GatherRoads(IEnumerable<Scene> scenes, Bounds cell)
    {
        var triangles = new List<Triangle>();
        foreach (var scene in scenes)
            foreach (var root in scene.GetRootGameObjects())
                foreach (var f in root.GetComponentsInChildren<MeshFilter>())
                {
                    var renderer = f.GetComponent<MeshRenderer>();
                    if (!renderer || !renderer.enabled || !f.gameObject.activeInHierarchy || !f.sharedMesh || !Intersects(renderer.bounds, cell, Grid + .1f)) continue;
                    string model = Instance(f.transform).name;
                    if (NotRoad.IsMatch(model)) continue;
                    bool road = RoadModel.IsMatch(model);
                    var materials = renderer.sharedMaterials;
                    if (!materials.Any(m => FloorMaterial(m, road))) continue;
                    using (var data = MeshUtility.AcquireReadOnlyMeshData(f.sharedMesh))
                    using (var vertices = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp))
                    {
                        data[0].GetVertices(vertices);
                        for (int slot = 0; slot < data[0].subMeshCount; slot++)
                        {
                            if (slot >= materials.Length || !FloorMaterial(materials[slot], road)) continue;
                            var descriptor = data[0].GetSubMesh(slot);
                            if (descriptor.topology != MeshTopology.Triangles) continue;
                            using (var indices = new NativeArray<int>(descriptor.indexCount, Allocator.Temp))
                            {
                                data[0].GetIndices(indices, slot);
                                for (int i = 0; i + 2 < indices.Length; i += 3)
                                {
                                    Vector3 a = f.transform.TransformPoint(vertices[indices[i]]), b = f.transform.TransformPoint(vertices[indices[i + 1]]), c = f.transform.TransformPoint(vertices[indices[i + 2]]);
                                    Vector3 cross = Vector3.Cross(b - a, c - a);
                                    if (cross.sqrMagnitude < .01f || cross.normalized.y < .8f ||
                                        Mathf.Max(a.x, b.x, c.x) < cell.min.x - Grid || Mathf.Min(a.x, b.x, c.x) > cell.max.x + Grid ||
                                        Mathf.Max(a.z, b.z, c.z) < cell.min.z - Grid || Mathf.Min(a.z, b.z, c.z) > cell.max.z + Grid) continue;
                                    triangles.Add(new Triangle { a = a, b = b, c = c, model = model });
                                }
                            }
                        }
                    }
                }
        return triangles;
    }
    static bool FloorMaterial(Material m, bool road)
    {
        if (!m || !m.mainTexture || NotTexture.IsMatch(m.mainTexture.name) ||
            (m.HasProperty("_Mode") && m.GetFloat("_Mode") != 0) || (m.HasProperty("_Color") && m.color.a < .95f)) return false;
        return RoadTexture.IsMatch(m.mainTexture.name) || (road && SolidTexture.IsMatch(m.mainTexture.name));
    }
    static Dictionary<string, Node> Rasterise(List<Triangle> triangles, Bounds cell)
    {
        var nodes = new Dictionary<string, Node>(StringComparer.Ordinal);
        foreach (var t in triangles)
        {
            int minX = Mathf.CeilToInt(Mathf.Max(-256, cell.min.x - Grid, Mathf.Min(t.a.x, t.b.x, t.c.x)) / Grid);
            int maxX = Mathf.FloorToInt(Mathf.Min(3072, cell.max.x + Grid, Mathf.Max(t.a.x, t.b.x, t.c.x)) / Grid);
            int minZ = Mathf.CeilToInt(Mathf.Max(512, cell.min.z - Grid, Mathf.Min(t.a.z, t.b.z, t.c.z)) / Grid);
            int maxZ = Mathf.FloorToInt(Mathf.Min(3072, cell.max.z + Grid, Mathf.Max(t.a.z, t.b.z, t.c.z)) / Grid);
            for (int x = minX; x <= maxX; x++) for (int z = minZ; z <= maxZ; z++)
                if (Height(t, x * Grid, z * Grid, out float y))
                {
                    int layer = Mathf.RoundToInt(y / 4);
                    string key = Key(x, z, layer);
                    if (nodes.TryGetValue(key, out var old)) { if (y > old.point.y) { old.point.y = y; old.model = t.model; } continue; }
                    nodes.Add(key, new Node { id = nodes.Count, x = x, z = z, layer = layer, point = new Vector3(x * Grid, y, z * Grid), model = t.model });
                }
        }
        return nodes;
    }
    static List<Edge> BuildEdges(Dictionary<string, Node> nodes, List<Triangle> triangles, Bounds cell)
    {
        var edges = new List<Edge>();
        var offsets = new[] { new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(1, -1) };
        // Spatial bins make continuity tests independent of triangle count.
        var bins = new Dictionary<string, List<Triangle>>(StringComparer.Ordinal);
        foreach (var t in triangles)
            for (int x = Mathf.FloorToInt(Mathf.Min(t.a.x, t.b.x, t.c.x) / Grid); x <= Mathf.FloorToInt(Mathf.Max(t.a.x, t.b.x, t.c.x) / Grid); x++)
                for (int z = Mathf.FloorToInt(Mathf.Min(t.a.z, t.b.z, t.c.z) / Grid); z <= Mathf.FloorToInt(Mathf.Max(t.a.z, t.b.z, t.c.z) / Grid); z++)
                {
                    string k = x + ":" + z; if (!bins.TryGetValue(k, out var list)) bins.Add(k, list = new List<Triangle>()); list.Add(t);
                }
        foreach (var a in nodes.Values.Where(n => Inside(n.point, cell)).OrderBy(n => n.id))
            foreach (var offset in offsets)
            {
                Node best = null; float difference = float.PositiveInfinity;
                for (int layer = a.layer - 2; layer <= a.layer + 2; layer++)
                    if (nodes.TryGetValue(Key(a.x + offset.x, a.z + offset.y, layer), out var b))
                    {
                        float gap = Mathf.Abs(b.point.y - a.point.y);
                        if (gap < difference) { best = b; difference = gap; }
                    }
                if (best == null || difference > Vector2.Distance(new Vector2(a.point.x, a.point.z), new Vector2(best.point.x, best.point.z)) * .65f) continue;
                bool continuous = true;
                foreach (float f in new[] { .25f, .5f, .75f })
                {
                    Vector3 p = Vector3.Lerp(a.point, best.point, f);
                    string k = Mathf.FloorToInt(p.x / Grid) + ":" + Mathf.FloorToInt(p.z / Grid);
                    if (!bins.TryGetValue(k, out var list) || !list.Any(t => Height(t, p.x, p.z, out float y) && Mathf.Abs(y - p.y) <= .8f)) { continuous = false; break; }
                }
                if (continuous) edges.Add(new Edge { a = a, b = best });
            }
        return edges;
    }
    static List<List<Node>> BuildRoutes(List<Edge> edges)
    {
        var adjacency = new Dictionary<int, List<Edge>>();
        foreach (var edge in edges) foreach (var node in new[] { edge.a, edge.b })
        { if (!adjacency.TryGetValue(node.id, out var list)) adjacency.Add(node.id, list = new List<Edge>()); list.Add(edge); }
        var routes = new List<List<Node>>();
        foreach (var seed in edges)
        {
            if (seed.used) continue;
            var path = new List<Node> { seed.a, seed.b }; seed.used = true;
            float distance = seed.Length;
            Vector3 direction = (seed.b.point - seed.a.point).normalized;
            Node at = seed.b;
            while (distance < 220)
            {
                Edge next = null; float best = float.NegativeInfinity;
                foreach (var candidate in adjacency[at.id].Where(e => !e.used))
                {
                    Node other = candidate.a == at ? candidate.b : candidate.a;
                    float score = Vector3.Dot(direction, (other.point - at.point).normalized);
                    if (score > best) { best = score; next = candidate; }
                }
                if (next == null) break;
                Node to = next.a == at ? next.b : next.a;
                next.used = true; distance += next.Length; direction = (to.point - at.point).normalized; path.Add(to); at = to;
            }
            routes.Add(path);
        }
        return routes;
    }

    static List<Node> ForcedRoute(Vector3 start, Vector3 end, string label)
    {
        // Explicit review routes deliberately cross suspected visual gaps. They
        // are not filtered by the visual connectivity checks used for the grid.
        // Only the initial point is snapped to a physical floor; motion within
        // the route remains continuous and unsupported movement is reported.
        var nodes = new List<Node>();
        int pieces = Mathf.CeilToInt(Vector3.Distance(start, end) / Grid);
        for (int i = 0; i <= pieces; i++) nodes.Add(new Node { id = i, point = Vector3.Lerp(start, end, (float)i / pieces), model = label });
        if (!FloorNear(start, 1, out _) && FloorNear(end, 1, out _)) nodes.Reverse();
        return nodes;
    }

    static RouteResult Patrol(List<Node> path, string sector, string id, Settings settings, List<Incident> incidents)
    {
        var result = new RouteResult { id = id, sector = sector, waypoints = path.Select(n => n.point).ToArray(), status = "completed" };
        for (int i = 1; i < path.Count; i++) result.plannedMetres += Vector3.Distance(path[i - 1].point, path[i].point);
        var controller = Controller(settings);
        float dt = Mathf.Min(.02f, MaxMove / Speed), verticalSpeed = -2, stalled = 0, sinceSample = 0;
        bool seamReported = false, airborneReported = false;
        try
        {
            Vector3 initial = path[0].point;
            controller.transform.position = initial;
            result.start = initial;
            if (!FloorNear(initial, 1, out var floor))
            {
                AddIncident(incidents, "missing-start-collision", sector, id, initial, initial.y, initial.y, path[0].model, "none", "Visual road exists, but no upward collider within one metre of its surface.");
                result.status = "finding"; result.start = result.finalPosition = initial; return result;
            }
            controller.transform.position = new Vector3(initial.x, floor.point.y + .04f - Foot(settings), initial.z);
            result.start = controller.transform.position; controller.enabled = true; Physics.SyncTransforms();
            for (int settle = 0; settle < 8; settle++) controller.Move(Vector3.down * .04f);
            for (int index = 1; index < path.Count; index++)
            {
                result.firstUnfinishedWaypoint = index;
                Vector3 segmentStart = path[index - 1].point, target = path[index].point;
                int maximum = Mathf.CeilToInt(Vector3.Distance(segmentStart, target) / MaxMove * 2 + .8f / dt);
                for (int iteration = 0; iteration < maximum; iteration++)
                {
                    Vector3 before = controller.transform.position;
                    Vector2 delta = new Vector2(target.x - before.x, target.z - before.z);
                    if (delta.magnitude <= .12f) break;
                    Vector2 movement = delta.normalized * Mathf.Min(Speed * dt, delta.magnitude);
                    if (controller.isGrounded && verticalSpeed < 0) verticalSpeed = -2;
                    verticalSpeed = Mathf.Max(verticalSpeed - Gravity * dt, -30);
                    var flags = controller.Move(new Vector3(movement.x, verticalSpeed * dt, movement.y));
                    Vector3 after = controller.transform.position;
                    result.steps++; result.simulatedSeconds += dt;
                    float moved = Vector2.Distance(new Vector2(before.x, before.z), new Vector2(after.x, after.z));
                    result.traveledMetres += moved; sinceSample += moved;
                    result.largestStep = Mathf.Max(result.largestStep, Mathf.Abs(after.y - before.y));
                    bool grounded = controller.isGrounded || (flags & CollisionFlags.Below) != 0;
                    if (grounded) result.groundedSteps++;
                    float along = Mathf.Clamp01(Vector2.Distance(new Vector2(segmentStart.x, segmentStart.z), new Vector2(after.x, after.z)) /
                        Mathf.Max(.01f, Vector2.Distance(new Vector2(segmentStart.x, segmentStart.z), new Vector2(target.x, target.z))));
                    float expected = Mathf.Lerp(segmentStart.y, target.y, along);
                    float feet = after.y + Foot(settings);
                    result.maximumDrop = Mathf.Max(result.maximumDrop, expected - feet);
                    bool physical = FloorNear(new Vector3(after.x, expected, after.z), 1.2f, out var support);
                    bool sphere = CapsuleFootSupport(controller, settings);
                    if (!physical && (grounded || sphere))
                    {
                        if (!seamReported)
                        {
                            var finding = AddIncident(incidents, "surface-probe-gap", sector, id, after, expected, feet, path[index].model,
                                "native capsule remains supported", "Centre ray misses the expected road, while native grounded/foot support remains present. This is not a player fall.");
                            finding.nativeGrounded = grounded; finding.sphereSupported = sphere; finding.centerRayFound = physical; finding.collisionFlags = (int)flags;
                            seamReported = true;
                        }
                    }
                    else if (physical) seamReported = false;
                    bool airborne = feet > expected + 1.5f && !grounded && !sphere;
                    if (airborne && !airborneReported)
                    {
                        var finding = AddIncident(incidents, "high-speed-airborne-over-grade", sector, id, after, expected, feet, path[index].model,
                            physical ? Instance(support.collider.transform).name : "none", "High-speed native capsule is above the descending grade; replay at normal walking speed before classifying terrain as defective.");
                        finding.nativeGrounded = grounded; finding.sphereSupported = sphere; finding.centerRayFound = physical; finding.collisionFlags = (int)flags;
                        airborneReported = true;
                    }
                    if (grounded) airborneReported = false;
                    bool unsupported = !physical && !grounded && !sphere && !airborne;
                    if (unsupported || feet < expected - 1 || (feet > expected + 1.5f && grounded))
                    {
                        string kind = unsupported ? "missing-road-collision" : feet < expected - 1 ? (grounded || sphere ? "visual-collision-height-mismatch" : "fall") : "unexpected-grounded-elevation";
                        var finding = AddIncident(incidents, kind, sector, id, after, expected, feet,
                            path[index].model, physical ? Instance(support.collider.transform).name : "none", "Native patrol requires local source-visual/COL review.");
                        finding.nativeGrounded = grounded; finding.sphereSupported = sphere; finding.centerRayFound = physical; finding.collisionFlags = (int)flags;
                        result.status = "finding"; return result;
                    }
                    float progress = delta.magnitude - Vector2.Distance(new Vector2(after.x, after.z), new Vector2(target.x, target.z));
                    stalled = progress > .002f ? 0 : stalled + dt;
                    if (stalled > .3f)
                    {
                        AddIncident(incidents, "blocked-corridor", sector, id, after, expected, feet, path[index].model, Obstacle(controller, settings, delta),
                            "Capsule could not advance for 0.3 simulated seconds. Props/trees and deliberate barriers must be reviewed before calling this a defect.");
                        result.status = "finding"; return result;
                    }
                    if (sinceSample >= 20)
                    { sinceSample = 0; OnPatrolSample?.Invoke(after, new Vector3(delta.x, 0, delta.y).normalized, id); }
                    if (iteration == maximum - 1)
                    {
                        AddIncident(incidents, "route-deviation", sector, id, after, expected, feet, path[index].model, Obstacle(controller, settings, delta), "Segment could not be completed within its movement budget.");
                        result.status = "finding"; return result;
                    }
                }
            }
        }
        catch (Exception exception)
        {
            result.status = "error";
            AddIncident(incidents, "patrol-exception", sector, id, controller.transform.position, 0, 0, path[0].model, "", exception.Message);
        }
        finally { result.finalPosition = controller.transform.position; Object.DestroyImmediate(controller.gameObject); }
        return result;
    }
    static bool SafeRestart(Vector3 expected, Settings settings)
    {
        if (!FloorNear(expected, .6f, out var hit)) return false;
        float radius = settings.radius * .9f;
        Vector3 bottom = new Vector3(expected.x, hit.point.y + radius + .12f, expected.z);
        Vector3 top = bottom + Vector3.up * Mathf.Max(0, settings.height - 2 * radius);
        return !Physics.CheckCapsule(bottom, top, radius, 1, QueryTriggerInteraction.Ignore);
    }

    static bool CapsuleFootSupport(CharacterController controller, Settings settings)
    {
        float feet = controller.transform.position.y + Foot(settings), radius = settings.radius * .8f;
        return Physics.SphereCast(new Vector3(controller.transform.position.x, feet + radius + .16f, controller.transform.position.z), radius,
            Vector3.down, out var hit, .45f, 1, QueryTriggerInteraction.Ignore) && hit.normal.y > .6f && Mathf.Abs(hit.point.y - feet) < .5f;
    }

    static string Obstacle(CharacterController controller, Settings settings, Vector2 direction)
    {
        Vector3 center = controller.transform.position + settings.center;
        float half = Mathf.Max(0, settings.height * .5f - settings.radius);
        var hits = Physics.CapsuleCastAll(center + Vector3.up * half, center - Vector3.up * half, settings.radius,
            new Vector3(direction.x, 0, direction.y).normalized, .7f, 1, QueryTriggerInteraction.Ignore);
        var hit = hits.Where(h => h.normal.y < .7f).OrderBy(h => h.distance).FirstOrDefault();
        return hit.collider ? Instance(hit.collider.transform).name : "not identified (possible step/slope/edge)";
    }
    static bool FloorNear(Vector3 expected, float tolerance, out RaycastHit chosen)
    {
        chosen = default; float best = float.PositiveInfinity;
        foreach (var hit in Physics.RaycastAll(expected + Vector3.up * tolerance, Vector3.down, tolerance * 2, 1, QueryTriggerInteraction.Ignore))
        {
            if (hit.normal.y < .7f) continue;
            float gap = Mathf.Abs(hit.point.y - expected.y);
            if (gap < best) { best = gap; chosen = hit; }
        }
        return best < tolerance;
    }
    static Incident AddIncident(List<Incident> incidents, string type, string sector, string route, Vector3 position, float expected, float feet, string model, string collider, string detail)
    {
        var item = new Incident { id = "P" + incidents.Count.ToString("D5"), type = type, sector = sector, routeId = route, position = position,
            expectedHeight = expected, feetHeight = feet, model = model, collider = collider, detail = detail };
        incidents.Add(item); OnIncident?.Invoke(type, position, route); return item;
    }
    static bool Height(Triangle t, float x, float z, out float y)
    {
        float denominator = (t.b.z - t.c.z) * (t.a.x - t.c.x) + (t.c.x - t.b.x) * (t.a.z - t.c.z);
        y = 0; if (Mathf.Abs(denominator) < .0001f) return false;
        float u = ((t.b.z - t.c.z) * (x - t.c.x) + (t.c.x - t.b.x) * (z - t.c.z)) / denominator;
        float v = ((t.c.z - t.a.z) * (x - t.c.x) + (t.a.x - t.c.x) * (z - t.c.z)) / denominator;
        float w = 1 - u - v; if (Mathf.Min(u, v, w) < -.0001f) return false;
        y = u * t.a.y + v * t.b.y + w * t.c.y; return true;
    }
    static string Key(int x, int z, int layer) => x + ":" + z + ":" + layer;
    static float Foot(Settings s) => s.center.y - s.height * .5f;
    static Transform Instance(Transform t)
    { for (var at = t; at; at = at.parent) if (at.name.Contains("#") || at.name.StartsWith("REPARO", StringComparison.Ordinal)) return at; return t; }
    static bool Inside(Vector3 p, Bounds b) => p.x >= b.min.x && p.x < b.max.x && p.z >= b.min.z && p.z < b.max.z;
    static bool Intersects(Bounds a, Bounds b, float margin) => a.min.x <= b.max.x + margin && a.max.x >= b.min.x - margin && a.min.z <= b.max.z + margin && a.max.z >= b.min.z - margin;
    static Cell MakeCell(string name)
    {
        var m = Regex.Match(name, @"^GTASASector_([mp])(\d+)_([mp])(\d+)$");
        int x = int.Parse(m.Groups[2].Value) * (m.Groups[1].Value == "m" ? -1 : 1), y = int.Parse(m.Groups[4].Value) * (m.Groups[3].Value == "m" ? -1 : 1);
        return new Cell { name = name, bounds = new Bounds(new Vector3(x * 256 + 128, 0, -y * 256 - 128), new Vector3(256, 2000, 256)) };
    }
    static void Synchronize(Dictionary<string, Scene> loaded, HashSet<string> wanted)
    {
        foreach (string name in loaded.Keys.Where(n => !wanted.Contains(n)).ToArray()) { EditorSceneManager.CloseScene(loaded[name], true); loaded.Remove(name); }
        foreach (string name in wanted.OrderBy(n => n, StringComparer.Ordinal))
        {
            if (loaded.ContainsKey(name)) continue;
            string path = SectorFolder + name + ".unity";
            if (!File.Exists(path)) throw new FileNotFoundException("Patrol sector missing", path);
            loaded.Add(name, EditorSceneManager.OpenScene(path, OpenSceneMode.Additive));
        }
        Physics.SyncTransforms();
    }
    static string Arg(string key, string fallback)
    {
        string prefix = key + "=";
        string value = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal));
        return value == null ? fallback : Regex.Replace(value.Substring(prefix.Length), "[^a-zA-Z0-9_-]", "_");
    }
    static void Write(Report report, List<CellResult> cells, List<RouteResult> routes, List<Incident> incidents, List<string> errors)
    {
        Directory.CreateDirectory(Output); report.cells = cells.ToArray(); report.routes = routes.ToArray(); report.incidents = incidents.ToArray(); report.errors = errors.ToArray();
        File.WriteAllText(Output + "/patrol-" + report.tag + ".json", JsonUtility.ToJson(report, true));
    }
}
