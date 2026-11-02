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

// Findings are evidence to review, never automatic permission to move a prop or
// to fill a pool. This verifier saves reports only, not source scenes/assets.
public static class LosSantosPatrolRecheck
{
    const string Folder = "../tools/los-santos-patrol/";
    const string Sectors = "Assets/Scenes/GTASanAndreasModFullSectors/";
    static readonly Regex RoadName = new Regex("road|highway|hiway|freeway|REPARO CHAO|ground|grnd|terrain|land|drain|river|path", RegexOptions.IgnoreCase);
    static readonly Regex GroundTexture = new Regex("asphalt|road|tarmac|snpedtest|sidelatino|sidewalk|pave|greyground|concrete|dirt|grass|gravel|plazatile", RegexOptions.IgnoreCase);
    static readonly Regex Excluded = new Regex("roof|ceiling|window|glass|water|leaf|leaves|alpha|fence|billboard|sign|metal", RegexOptions.IgnoreCase);
    [Serializable] sealed class Baseline { public int cellsInspected; public Incident[] incidents; public Route[] routes; }
    [Serializable] sealed class Incident
    {
        public string id, type, sector, routeId, model, collider, detail;
        public Vector3 position;
        public float expectedHeight, feetHeight;
    }
    [Serializable] sealed class Route { public string id, sector, status; public Vector3[] waypoints; }
    [Serializable] sealed class RouteReplayReport
    {
        public string method;
        public bool passed;
        public Route source;
        public Replay walk, fast;
        public string[] errors;
    }
    [Serializable] sealed class Settings
    {
        public Vector3 center;
        public float height, radius, stepOffset, slopeLimit, skinWidth, minMoveDistance;
    }
    [Serializable] public sealed class SourceAuditEntry
    {
        public string incidentId, sector, instance, asset, mesh, texture, hierarchy, classification;
        public Vector3 position, instancePosition;
        public float normalY;
        public int samples, missing, supported;
        public bool eligibleRepair, needsSemanticReview;
    }
    [Serializable] sealed class Surface
    {
        public bool found, roadName, groundTexture, centerPhysics;
        public string sector, instance, asset, mesh, texture, hierarchy;
        public Vector3 position, instancePosition;
        public float height, normalY, physicsHeight;
        public int visualSamples, missingPhysicsSamples, supportedPhysicsSamples;
    }
    [Serializable] sealed class Replay
    {
        public string status, detail, lastObstacle;
        public Vector3 start, finalPosition;
        public float speed, plannedMetres, traveledMetres, maxBelowVisual, maxAboveVisual;
        public int steps, groundedSteps, centerRayGapsWhileGrounded, sphereSupportedSteps, missingPhysicalSupportSteps, lastCollisionFlags;
        public bool finalGrounded;
    }
    [Serializable] sealed class BoxFinding
    {
        public string collider, instance, scene;
        public Vector3 contactPoint;
        public Bounds colliderBounds, rendererBounds;
        public float separationFromVisualBounds;
    }
    [Serializable] sealed class Item
    {
        public string id, type, sector, model, classification;
        public Vector3 position;
        public Surface sourceVisual;
        public Replay walk, fast;
        public BoxFinding[] possibleInvisibleBoxes;
        public bool confirmedMissingVisualSupport, physicalFallAtWalkSpeed;
    }
    [Serializable] sealed class BlockerReview
    {
        public string id, sector, collider, classification;
        public Vector3 position;
        public float distanceFromVisualBounds;
    }
    [Serializable] sealed class Report
    {
        public string utc, input, method, status;
        public int baselineCellsInspected, uniqueFindings, inspected, sourceEntries, confirmedMissingSurfaces, walkFalls, visibleBlockers, suspectBlockers;
        public Item[] items;
        public SourceAuditEntry[] sourceAuditEntries;
        public BlockerReview[] blockers;
        public string[] errors;
    }

    [MenuItem("Horizonte/Los Santos/Conferir achados da patrulha fisica")]
    public static void Run()
    {
        string tag = Arg("--ls-patrol-tag", "before");
        RunReport(tag, Arg("--ls-patrol-input-tag", tag), false);
    }

    public static void RunCurrentFindings()
    {
        string tag = Arg("--ls-patrol-tag", "after");
        RunReport(tag + "-current", tag, true);
    }

    public static void VerifyFoundationRoute()
    {
        const string input="../tools/los-santos-repair/generated-foundation-addon/foundation-native-route.json";
        var route=JsonUtility.FromJson<Route>(File.ReadAllText(input));
        if(route?.waypoints==null || route.waypoints.Length<2)throw new Exception("Rota de fundação inválida.");
        var setup=EditorSceneManager.GetSceneManagerSetup();
        bool backfaces=Physics.queriesHitBackfaces;
        var errors=new List<string>();
        var report=new RouteReplayReport {source=route,method="Continuous native CharacterController.Move on the complete measured roundtrip polyline, with player-prefab dimensions, 1.65 and 54m/s; original scene geometry and all repair colliders loaded by real bounds. No teleport within the route and no scene saved."};
        try
        {
            var main=EditorSceneManager.OpenScene("Assets/Scenes/TestePersonagem.unity",OpenSceneMode.Single);
            var stream=main.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<GtaSaSectorStreaming>(true)).Single();
            var entries=stream.sectors.Where(e=>e!=null && !string.IsNullOrEmpty(e.scene)).ToArray();
            var settings=SettingsFromPrefab();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Physics.queriesHitBackfaces=false;
            var path=route.waypoints.ToList();
            Synchronize(new Dictionary<string,Scene>(StringComparer.Ordinal),entries,path);
            report.walk=ReplayPath(path,settings,1.65f);
            report.fast=ReplayPath(path,settings,54);
            report.passed=report.walk.status=="completed" && report.fast.status=="completed";
            if(!report.passed)errors.Add("Foundation walk="+report.walk.status+" / fast="+report.fast.status);
        }
        catch(Exception exception){errors.Add(exception.ToString());}
        finally
        {
            Physics.queriesHitBackfaces=backfaces;
            try { if(setup.Length>0 && setup.All(s=>!string.IsNullOrEmpty(s.path)))EditorSceneManager.RestoreSceneManagerSetup(setup);else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single); }
            catch(Exception exception){errors.Add(exception.ToString());}
            report.errors=errors.ToArray(); report.passed &= errors.Count==0;
            File.WriteAllText(Folder+"foundation-walking-verification.json",JsonUtility.ToJson(report,true));
        }
        if(!report.passed)throw new Exception("LOS_SANTOS_FOUNDATION_WALKING_FAILED "+string.Join(" | ",errors));
        Debug.Log("LOS_SANTOS_FOUNDATION_WALKING_VERIFIED normal="+report.walk.traveledMetres+" fast="+report.fast.traveledMetres);
    }

    static void RunReport(string tag, string inputTag, bool actionableOnly)
    {
        if (EditorApplication.isPlaying) throw new Exception("Pare o Play Mode antes da verificacao de achados.");
        string input = Folder + "patrol-" + inputTag + ".json";
        var data = JsonUtility.FromJson<Baseline>(File.ReadAllText(input));
        if (data?.incidents == null || data.routes == null) throw new Exception("Relatorio de patrulha invalido.");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        var report = new Report { utc = DateTime.UtcNow.ToString("o"), input = Path.GetFullPath(input), baselineCellsInspected = data.cellsInspected,
            method = "Unique findings rounded to 2m; source visual triangles at the exact XZ, five source/Physics samples, replay of the original local polyline +/-8m at 1.65 and 54m/s. Native grounded/CollisionFlags plus capsule-foot sphere support distinguish centre-ray seams and high-speed airborne motion from actual falls. No scenes/assets saved." };
        var items = new List<Item>(); var sources = new List<SourceAuditEntry>(); var blockers = new List<BlockerReview>(); var errors = new List<string>();
        bool backfaces = Physics.queriesHitBackfaces;
        var loaded = new Dictionary<string, Scene>(StringComparer.Ordinal);
        try
        {
            var main = EditorSceneManager.OpenScene("Assets/Scenes/TestePersonagem.unity", OpenSceneMode.Single);
            var streamer = main.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GtaSaSectorStreaming>(true)).Single();
            var entries = streamer.sectors.Where(e => e != null && !string.IsNullOrEmpty(e.scene)).ToArray();
            var settings = SettingsFromPrefab();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Physics.queriesHitBackfaces = false;
            var routes = data.routes.GroupBy(r => r.id).ToDictionary(g => g.Key, g => g.First());
            var wanted = data.incidents.Where(i => i.type != "blocked-corridor" && i.type != "route-deviation" &&
                (!actionableOnly || i.type != "surface-probe-gap" && i.type != "high-speed-airborne-over-grade"))
                .GroupBy(i => i.sector + "|" + i.model + "|" + Mathf.RoundToInt(i.position.x / 2) + "|" + Mathf.RoundToInt(i.position.z / 2) + "|" + Mathf.RoundToInt(i.expectedHeight / 2))
                .Select(g => g.First()).OrderBy(i => i.sector, StringComparer.Ordinal).ToArray();
            report.uniqueFindings = wanted.Length;
            foreach (var incident in wanted)
            {
                if (!routes.TryGetValue(incident.routeId, out var route)) { errors.Add("Route absent: " + incident.routeId); continue; }
                var path = LocalPath(route.waypoints, incident.position, 8);
                if (path.Count < 2) { errors.Add("Insufficient local route: " + incident.id); continue; }
                Synchronize(loaded, entries, path);
                var visual = InspectSurface(loaded.Values, incident);
                var item = new Item { id = incident.id, type = incident.type, sector = incident.sector, model = incident.model, position = incident.position, sourceVisual = visual };
                item.possibleInvisibleBoxes = BoxesOutsideVisual(incident, settings, visual).ToArray();
                item.walk = ReplayPath(path, settings, 1.65f);
                item.fast = ReplayPath(path, settings, 54);
                bool capsuleCrossesSeam = item.walk.status == "completed" && item.walk.missingPhysicalSupportSteps == 0 &&
                    item.walk.centerRayGapsWhileGrounded > 0 && item.walk.maxBelowVisual < .25f;
                item.confirmedMissingVisualSupport = visual.found && visual.visualSamples >= 3 && visual.missingPhysicsSamples >= 3 && visual.supportedPhysicsSamples == 0;
                item.physicalFallAtWalkSpeed = item.walk.status == "physical-fall";
                if (item.confirmedMissingVisualSupport)
                {
                    item.classification = capsuleCrossesSeam ? "local surface-probe seam; native slow capsule crosses supported" :
                        visual.roadName && visual.groundTexture ? "confirmed visual road/floor without local collision" : "visual surface without local collision; floor/roof semantic review required";
                    sources.Add(new SourceAuditEntry { incidentId = incident.id, sector = visual.sector, instance = visual.instance, asset = visual.asset, mesh = visual.mesh, texture = visual.texture,
                        hierarchy = visual.hierarchy, position = visual.position, instancePosition = visual.instancePosition, normalY = visual.normalY,
                        samples = visual.visualSamples, missing = visual.missingPhysicsSamples, supported = visual.supportedPhysicsSamples,
                        eligibleRepair = visual.roadName && visual.groundTexture && !capsuleCrossesSeam, needsSemanticReview = !visual.roadName || !visual.groundTexture || capsuleCrossesSeam, classification = item.classification });
                    report.confirmedMissingSurfaces++;
                }
                else if (item.physicalFallAtWalkSpeed) item.classification = "native slow-speed fall; inspect source visual/COL relationship";
                else if (item.walk.status == "completed" && item.fast.status == "airborne-over-grade") item.classification = "high-speed airborne motion over existing grade";
                else if (item.walk.status == "completed" && item.fast.status == "completed" && (item.walk.centerRayGapsWhileGrounded + item.fast.centerRayGapsWhileGrounded > 0)) item.classification = "centre surface-probe seam; native capsule remains supported";
                else if (item.walk.status == "completed" && item.fast.status == "completed") item.classification = "local native replays pass";
                else if (item.possibleInvisibleBoxes.Length > 0) item.classification = "possible mismatched box outside its source visual bounds";
                else item.classification = "requires visual review: " + item.walk.status + " / " + item.fast.status;
                if (item.physicalFallAtWalkSpeed) report.walkFalls++;
                items.Add(item); report.inspected++;
                if (report.inspected % 12 == 0) { Write(report, items, sources, blockers, errors, tag); Debug.Log("LOS_SANTOS_PATROL_RECHECK_PROGRESS inspected=" + report.inspected + "/" + wanted.Length + " missing=" + sources.Count); }
            }
            foreach (var incident in data.incidents.Where(i => i.type == "blocked-corridor" || i.type == "route-deviation")
                .GroupBy(i => i.sector + "|" + i.collider + "|" + Mathf.RoundToInt(i.position.x / 2) + "|" + Mathf.RoundToInt(i.position.z / 2)).Select(g => g.First()).OrderBy(i => i.sector, StringComparer.Ordinal))
            {
                Synchronize(loaded, entries, new List<Vector3> { incident.position });
                var renderers = loaded.Values.SelectMany(s => s.GetRootGameObjects()).SelectMany(r => r.GetComponentsInChildren<MeshRenderer>())
                    .Where(r => r.enabled && Instance(r.transform).name == incident.collider).ToArray();
                float distance = renderers.Length > 0 ? Mathf.Sqrt(renderers.Min(r => r.bounds.SqrDistance(incident.position + Vector3.up * .8f))) : 1000;
                string classification = distance <= .8f ? "blocker has nearby visible model bounds; inspect the prop/path before calling it a defect" : "unidentified or collision outside nearby visible obstacle bounds; inspect";
                blockers.Add(new BlockerReview { id = incident.id, sector = incident.sector, collider = incident.collider, position = incident.position, distanceFromVisualBounds = distance, classification = classification });
                if (distance <= .8f) report.visibleBlockers++; else report.suspectBlockers++;
            }
            report.status = "completed";
        }
        catch (Exception exception) { errors.Add(exception.ToString()); report.status = "incomplete"; Debug.LogException(exception); }
        finally
        {
            Physics.queriesHitBackfaces = backfaces;
            try
            {
                if (setup.Length > 0 && setup.All(s => !string.IsNullOrEmpty(s.path))) EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            catch (Exception exception) { errors.Add("Scene restore: " + exception.Message); }
            Write(report, items, sources, blockers, errors, tag);
        }
        if (errors.Count > 0) throw new Exception("LOS_SANTOS_PATROL_RECHECK_INCOMPLETE " + string.Join(" | ", errors));
        Debug.Log("LOS_SANTOS_PATROL_RECHECK_COMPLETE findings=" + items.Count + " missing=" + sources.Count + " slowFalls=" + report.walkFalls);
    }

    static Surface InspectSurface(IEnumerable<Scene> scenes, Incident incident)
    {
        var result = new Surface(); MeshFilter selected = null; Triangle face = default; Material material = null; float best = .501f;
        foreach (var scene in scenes) foreach (var root in scene.GetRootGameObjects()) foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
        {
            var renderer = filter.GetComponent<MeshRenderer>();
            if (!renderer || !renderer.enabled || !filter.sharedMesh || !filter.gameObject.activeInHierarchy || !ContainsXZ(renderer.bounds, incident.position, .1f)) continue;
            var owner = Instance(filter.transform);
            bool forced = incident.model.StartsWith("mandatory-", StringComparison.Ordinal);
            if (!forced && owner.name != incident.model) continue;
            var materials = renderer.sharedMaterials;
            using (var data = MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh))
            using (var vertices = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp))
            {
                data[0].GetVertices(vertices);
                for (int slot = 0; slot < data[0].subMeshCount; slot++)
                {
                    if (data[0].GetSubMesh(slot).topology != MeshTopology.Triangles) continue;
                    var mat = slot < materials.Length ? materials[slot] : null;
                    if (forced && !IsGroundTexture(mat)) continue;
                    using (var indices = new NativeArray<int>(data[0].GetSubMesh(slot).indexCount, Allocator.Temp))
                    {
                        data[0].GetIndices(indices, slot);
                        for (int i = 0; i + 2 < indices.Length; i += 3)
                        {
                            var tri = new Triangle { a = filter.transform.TransformPoint(vertices[indices[i]]), b = filter.transform.TransformPoint(vertices[indices[i + 1]]), c = filter.transform.TransformPoint(vertices[indices[i + 2]]) };
                            float normal = Vector3.Cross(tri.b - tri.a, tri.c - tri.a).normalized.y;
                            if (normal < .72f || !Height(tri, incident.position.x, incident.position.z, out float y)) continue;
                            float difference = Mathf.Abs(y - incident.expectedHeight);
                            if (difference >= best) continue;
                            best = difference; selected = filter; face = tri; material = mat;
                            result = new Surface { found = true, sector = scene.name, instance = owner.name, instancePosition = owner.position,
                                asset = AssetDatabase.GetAssetPath(filter.sharedMesh), mesh = filter.sharedMesh.name, height = y, normalY = normal,
                                position = new Vector3(incident.position.x, y, incident.position.z), texture = mat && mat.mainTexture ? mat.mainTexture.name : "none",
                                hierarchy = Hierarchy(filter.transform), roadName = RoadName.IsMatch(owner.name) && !Excluded.IsMatch(owner.name), groundTexture = IsGroundTexture(mat) };
                        }
                    }
                }
            }
        }
        if (!selected) return result;
        // The same exact source triangle is used for nearby probes. Only points
        // inside that triangle count, so a courtyard edge is not extrapolated.
        foreach (var offset in new[] { Vector2.zero, new Vector2(.12f, 0), new Vector2(-.12f, 0), new Vector2(0, .12f), new Vector2(0, -.12f) })
        {
            float x = incident.position.x + offset.x, z = incident.position.z + offset.y;
            if (!Height(face, x, z, out float y)) continue;
            result.visualSamples++;
            bool support = PhysicsFloor(new Vector3(x, y, z), .45f, out var hit);
            if (support) result.supportedPhysicsSamples++; else result.missingPhysicsSamples++;
            if (offset == Vector2.zero) { result.centerPhysics = support; result.physicsHeight = support ? hit.point.y : -1000; }
        }
        return result;
    }
    struct Triangle { public Vector3 a, b, c; }
    static bool Height(Triangle t, float x, float z, out float y)
    {
        y = 0; float den = (t.b.z - t.c.z) * (t.a.x - t.c.x) + (t.c.x - t.b.x) * (t.a.z - t.c.z);
        if (Mathf.Abs(den) < .000001f) return false;
        float u = ((t.b.z - t.c.z) * (x - t.c.x) + (t.c.x - t.b.x) * (z - t.c.z)) / den;
        float v = ((t.c.z - t.a.z) * (x - t.c.x) + (t.a.x - t.c.x) * (z - t.c.z)) / den;
        float w = 1 - u - v; if (Mathf.Min(u, v, w) < -.0001f) return false;
        y = u * t.a.y + v * t.b.y + w * t.c.y; return true;
    }
    static bool IsGroundTexture(Material m) => m && m.mainTexture && GroundTexture.IsMatch(m.mainTexture.name) && !Excluded.IsMatch(m.mainTexture.name) &&
        (!m.HasProperty("_Mode") || m.GetFloat("_Mode") == 0) && (!m.HasProperty("_Color") || m.color.a >= .95f);
    static IEnumerable<BoxFinding> BoxesOutsideVisual(Incident incident, Settings settings, Surface surface)
    {
        if (!surface.found || !surface.groundTexture) yield break;
        Vector3 center = new Vector3(incident.position.x, incident.expectedHeight + settings.height * .5f, incident.position.z);
        foreach (var collider in Physics.OverlapCapsule(center + Vector3.up * .5f, center - Vector3.up * .5f, settings.radius + .15f, 1, QueryTriggerInteraction.Ignore).OfType<BoxCollider>())
        {
            var owner = Instance(collider.transform);
            var renderers = owner.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            if (renderers.Length == 0) continue;
            Vector3 contact = collider.ClosestPoint(center);
            float distance = Mathf.Sqrt(renderers.Min(r => r.bounds.SqrDistance(contact)));
            if (distance <= 1) continue;
            var bounds = renderers[0].bounds; foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            yield return new BoxFinding { collider = collider.name, instance = owner.name, scene = owner.gameObject.scene.name, contactPoint = contact,
                colliderBounds = collider.bounds, rendererBounds = bounds, separationFromVisualBounds = distance };
        }
    }
    static Replay ReplayPath(List<Vector3> path, Settings settings, float speed)
    {
        var replay = new Replay { speed = speed, status = "completed", detail = "", lastObstacle = "" };
        for (int i = 1; i < path.Count; i++) replay.plannedMetres += Vector3.Distance(path[i - 1], path[i]);
        var controller = CreateController(settings);
        float dt = Mathf.Min(.02f, .18f / speed), velocity = -2, stalled = 0;
        bool airborne = false;
        try
        {
            Vector3 initial = path[0]; controller.transform.position = initial; replay.start = initial;
            if (!PhysicsFloor(initial, 1.2f, out var floor)) { replay.status = "no-safe-start-collision"; return replay; }
            controller.transform.position = new Vector3(initial.x, floor.point.y + .04f - Foot(settings), initial.z); replay.start = controller.transform.position;
            controller.enabled = true; Physics.SyncTransforms();
            for (int settle = 0; settle < 8; settle++) controller.Move(Vector3.down * .04f);
            for (int node = 1; node < path.Count; node++)
            {
                Vector3 a = path[node - 1], b = path[node];
                int max = Mathf.CeilToInt(Vector3.Distance(a, b) / (speed * dt) * 2 + 2 / dt);
                for (int step = 0; step < max; step++)
                {
                    Vector3 before = controller.transform.position; Vector2 delta = new Vector2(b.x - before.x, b.z - before.z);
                    if (delta.magnitude < .12f) break;
                    Vector2 move = delta.normalized * Mathf.Min(speed * dt, delta.magnitude);
                    if (controller.isGrounded && velocity < 0) velocity = -2;
                    velocity = Mathf.Max(velocity - 22 * dt, -30);
                    var flags = controller.Move(new Vector3(move.x, velocity * dt, move.y));
                    Vector3 after = controller.transform.position;
                    replay.steps++; replay.lastCollisionFlags = (int)flags;
                    replay.traveledMetres += Vector2.Distance(new Vector2(before.x, before.z), new Vector2(after.x, after.z));
                    bool grounded = controller.isGrounded || (flags & CollisionFlags.Below) != 0;
                    if (grounded) replay.groundedSteps++;
                    float along = Mathf.Clamp01(Vector2.Distance(new Vector2(a.x, a.z), new Vector2(after.x, after.z)) / Mathf.Max(.01f, Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z))));
                    float expected = Mathf.Lerp(a.y, b.y, along), feet = after.y + Foot(settings);
                    bool center = PhysicsFloor(new Vector3(after.x, expected, after.z), 1.2f, out var physical);
                    bool sphere = SphereSupport(controller, settings, out var sphereHit);
                    if (sphere) replay.sphereSupportedSteps++;
                    if (!center && grounded) replay.centerRayGapsWhileGrounded++;
                    if (!grounded && !sphere) replay.missingPhysicalSupportSteps++;
                    replay.maxBelowVisual = Mathf.Max(replay.maxBelowVisual, expected - feet); replay.maxAboveVisual = Mathf.Max(replay.maxAboveVisual, feet - expected);
                    // A centre-ray seam while the native capsule is grounded is
                    // retained as evidence but never labelled a player fall.
                    if (!grounded && !sphere && feet < expected - 1)
                    { replay.status = "physical-fall"; replay.detail = "Native capsule is below expected source floor and lacks nearby physical foot support."; return replay; }
                    if (!grounded && feet > expected + 1.5f) airborne = true;
                    float progress = delta.magnitude - Vector2.Distance(new Vector2(after.x, after.z), new Vector2(b.x, b.z));
                    stalled = progress > .001f ? 0 : stalled + dt;
                    if (stalled > .6f)
                    {
                        replay.status = "blocked"; replay.lastObstacle = Obstacle(controller, settings, delta); replay.detail = "Native capsule stalls; obstacle visibility must be reviewed."; return replay;
                    }
                    if (step == max - 1) { replay.status = "local-route-deviation"; return replay; }
                }
            }
            if (airborne) replay.status = "airborne-over-grade";
        }
        catch (Exception exception) { replay.status = "error"; replay.detail = exception.Message; }
        finally { replay.finalPosition = controller.transform.position; replay.finalGrounded = controller.isGrounded; Object.DestroyImmediate(controller.gameObject); }
        return replay;
    }
    static bool SphereSupport(CharacterController c, Settings s, out RaycastHit hit)
    {
        float feet = c.transform.position.y + Foot(s), radius = s.radius * .8f;
        return Physics.SphereCast(new Vector3(c.transform.position.x, feet + radius + .16f, c.transform.position.z), radius, Vector3.down,
            out hit, .45f, 1, QueryTriggerInteraction.Ignore) && hit.normal.y >= .6f && Mathf.Abs(hit.point.y - feet) < .5f;
    }
    static bool PhysicsFloor(Vector3 expected, float tolerance, out RaycastHit selected)
    {
        selected = default; float best = float.PositiveInfinity;
        foreach (var hit in Physics.RaycastAll(expected + Vector3.up * (tolerance + .01f), Vector3.down, tolerance * 2 + .02f, 1, QueryTriggerInteraction.Ignore))
        {
            if (hit.normal.y < .6f) continue; float difference = Mathf.Abs(hit.point.y - expected.y);
            if (difference < best) { best = difference; selected = hit; }
        }
        return best <= tolerance;
    }
    static List<Vector3> LocalPath(Vector3[] path, Vector3 point, float halfLength)
    {
        var result = new List<Vector3>(); if (path == null || path.Length < 2) return result;
        var distances = new float[path.Length]; float best = float.PositiveInfinity, at = 0;
        for (int i = 1; i < path.Length; i++)
        {
            distances[i] = distances[i - 1] + Vector3.Distance(path[i - 1], path[i]);
            var a = new Vector2(path[i - 1].x, path[i - 1].z); var delta = new Vector2(path[i].x, path[i].z) - a;
            float f = Mathf.Clamp01(Vector2.Dot(new Vector2(point.x, point.z) - a, delta) / Mathf.Max(.00001f, delta.sqrMagnitude));
            float gap = Vector2.Distance(a + delta * f, new Vector2(point.x, point.z));
            if (gap < best) { best = gap; at = Mathf.Lerp(distances[i - 1], distances[i], f); }
        }
        float start = Mathf.Max(0, at - halfLength), end = Mathf.Min(distances.Last(), at + halfLength);
        result.Add(AtDistance(path, distances, start));
        for (int i = 1; i < path.Length - 1; i++) if (distances[i] > start && distances[i] < end) result.Add(path[i]);
        result.Add(AtDistance(path, distances, end)); return result;
    }
    static Vector3 AtDistance(Vector3[] path, float[] distances, float target)
    {
        for (int i = 1; i < path.Length; i++) if (target <= distances[i]) return Vector3.Lerp(path[i - 1], path[i], (target - distances[i - 1]) / Mathf.Max(.001f, distances[i] - distances[i - 1]));
        return path.Last();
    }
    static Settings SettingsFromPrefab()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Jogador.prefab"); var c = prefab ? prefab.GetComponent<CharacterController>() : null;
        if (!c) throw new Exception("CharacterController prefab absent.");
        return new Settings { center = c.center, height = c.height, radius = c.radius, stepOffset = c.stepOffset, slopeLimit = c.slopeLimit, skinWidth = c.skinWidth, minMoveDistance = c.minMoveDistance };
    }
    static CharacterController CreateController(Settings s)
    {
        var go = new GameObject("Temporary patrol replay capsule") { hideFlags = HideFlags.HideAndDontSave, layer = 2 }; var c = go.AddComponent<CharacterController>(); c.enabled = false;
        c.height = s.height; c.radius = s.radius; c.center = s.center; c.stepOffset = s.stepOffset; c.slopeLimit = s.slopeLimit; c.skinWidth = s.skinWidth; c.minMoveDistance = s.minMoveDistance; return c;
    }
    static string Obstacle(CharacterController c, Settings s, Vector2 direction)
    {
        Vector3 center = c.transform.position + s.center; float half = Mathf.Max(0, s.height * .5f - s.radius);
        var hit = Physics.CapsuleCastAll(center + Vector3.up * half, center - Vector3.up * half, s.radius, new Vector3(direction.x, 0, direction.y).normalized, .7f, 1, QueryTriggerInteraction.Ignore)
            .Where(h => h.normal.y < .7f).OrderBy(h => h.distance).FirstOrDefault();
        return hit.collider ? Instance(hit.collider.transform).name : "unidentified step/slope/edge";
    }
    static float Foot(Settings s) => s.center.y - s.height * .5f;
    static void Synchronize(Dictionary<string, Scene> loaded, GtaSaSectorStreaming.Sector[] entries, List<Vector3> path)
    {
        float minX = path.Min(p => p.x) - 3, maxX = path.Max(p => p.x) + 3, minZ = path.Min(p => p.z) - 3, maxZ = path.Max(p => p.z) + 3;
        var wanted = new HashSet<string>(entries.Where(e => e.bounds.min.x <= maxX && e.bounds.max.x >= minX && e.bounds.min.z <= maxZ && e.bounds.max.z >= minZ).Select(e => e.scene), StringComparer.Ordinal);
        foreach (string name in loaded.Keys.Where(n => !wanted.Contains(n)).ToArray()) { EditorSceneManager.CloseScene(loaded[name], true); loaded.Remove(name); }
        foreach (string name in wanted.OrderBy(n => n, StringComparer.Ordinal)) if (!loaded.ContainsKey(name)) loaded.Add(name, EditorSceneManager.OpenScene(Sectors + name + ".unity", OpenSceneMode.Additive));
        Physics.SyncTransforms();
    }
    static Transform Instance(Transform t) { for (var at = t; at; at = at.parent) if (at.name.Contains("#") || at.name.StartsWith("REPARO", StringComparison.Ordinal)) return at; return t; }
    static bool ContainsXZ(Bounds b, Vector3 p, float margin) => p.x >= b.min.x - margin && p.x <= b.max.x + margin && p.z >= b.min.z - margin && p.z <= b.max.z + margin;
    static string Hierarchy(Transform t) { var parts = new List<string>(); for (var at = t; at; at = at.parent) parts.Add(at.name + "[" + at.GetSiblingIndex() + "]"); parts.Reverse(); return string.Join("/", parts); }
    static string Arg(string key, string fallback) { var value = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(key + "=", StringComparison.Ordinal)); return value == null ? fallback : Regex.Replace(value.Substring(key.Length + 1), "[^a-zA-Z0-9_-]", "_"); }
    static void Write(Report report, List<Item> items, List<SourceAuditEntry> sources, List<BlockerReview> blockers, List<string> errors, string tag)
    {
        Directory.CreateDirectory(Folder); report.items = items.ToArray(); report.sourceAuditEntries = sources.ToArray(); report.sourceEntries = sources.Count; report.blockers = blockers.ToArray(); report.errors = errors.ToArray();
        File.WriteAllText(Folder + "patrol-recheck-" + tag + ".json", JsonUtility.ToJson(report, true));
        File.WriteAllText(Folder + "source-audit-" + tag + ".json", JsonUtility.ToJson(new SourceEnvelope { cellsInspected = report.baselineCellsInspected, ground = sources.ToArray() }, true));
    }
    [Serializable] sealed class SourceEnvelope { public int cellsInspected; public SourceAuditEntry[] ground; }
}
