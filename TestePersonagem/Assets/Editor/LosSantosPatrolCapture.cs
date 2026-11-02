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

// Player-height photographs of the saved city. Never saves scenes or starts game/login scripts.
public static class LosSantosPatrolCapture
{
    const string Sectors = "Assets/Scenes/GTASanAndreasModFullSectors/";
    const string Output = "../tools/los-santos-patrol";
    static readonly Regex RoadTexture = new Regex(@"asphalt|road|tarmac|snpedtest|sjmhood|dualroad|freeway|hiway", RegexOptions.IgnoreCase);
    static readonly Regex GroundTexture = new Regex(@"grass|ground|grnd|dirt|sand|pave|concrete", RegexOptions.IgnoreCase);
    static readonly Regex Excluded = new Regex(@"roof|water|house|build|apart|shop|mall|tree|bush|palm|wall|alpha|sign|traffic|roadblock", RegexOptions.IgnoreCase);
    [Serializable] sealed class Report
    {
        public string tag, method, directory;
        public float cameraHeight = 1.65f, fieldOfView = 70, farClip = 180, loadedRadius = 240;
        public Color backgroundColor = new Color(.25f, .42f, .65f);
        public int sectorsInspected, sectorsPhotographed, frames;
        public View[] views;
        public string[] skipped, errors;
    }
    [Serializable] public sealed class View
    {
        public string id, sector, model, material, category, file;
        public Vector3 floor, eye, direction, requestedEye;
        public bool cameraRelocated;
        public float sourceFloorY, actualPlayerFloorY;
    }
    [Serializable] sealed class IncidentFile { public Incident[] incidents; public Route[] routes; }
    [Serializable] sealed class Incident { public string id, type, sector, routeId, model, collider, detail; public Vector3 position; public float expectedHeight; }
    [Serializable] sealed class Route { public string id; public Vector3[] waypoints; public Vector3 start, finalPosition; }
    sealed class Anchor { public Vector3 point, direction; public string model, material, category; public float score; }
    static Report live;
    static List<View> liveViews;
    static readonly Dictionary<string, int> LiveCounts = new Dictionary<string, int>();
    static readonly Dictionary<string, Vector3> LivePrevious = new Dictionary<string, Vector3>();

    [MenuItem("Horizonte/Los Santos/Patrulha visual ao nivel do jogador")]
    public static void CaptureCells()
    {
        var setup = EditorSceneManager.GetSceneManagerSetup();
        var report = NewReport("cells");
        var views = new List<View>(); var skipped = new List<string>(); var errors = new List<string>();
        try
        {
            var source = EditorSceneManager.OpenScene(GameUpdateSetup.GameScene, OpenSceneMode.Single);
            var stream = source.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GtaSaSectorStreaming>(true)).First();
            var entries = stream.sectors.Where(s => s != null && !string.IsNullOrEmpty(s.scene)).ToArray();
            var cells = entries.Where(s => LosSantosAndroidSetup.Selected(s.scene)).OrderBy(s => s.scene).ToArray();
            var loaded = new Dictionary<string, Scene>();
            int yaws = IntArgument("--patrol-views", 2, 2, 4);
            foreach (var cell in cells)
            {
                Bounds tile = Tile(cell.scene);
                Synchronize(loaded, entries, tile.center, 240);
                Physics.SyncTransforms();
                var anchors = new List<Anchor>();
                foreach (var scene in loaded.Values)
                    foreach (var root in scene.GetRootGameObjects())
                        foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
                            CollectAnchors(filter, tile, anchors);
                report.sectorsInspected++;
                bool photographed = false;
                foreach (var anchor in anchors.OrderBy(a => a.score).Take(40))
                {
                    if (!TryEye(anchor.point, anchor.direction, out var floor, out var eye)) continue;
                    for (int yaw = 0; yaw < yaws; yaw++)
                    {
                        Vector3 direction = Quaternion.Euler(0, yaw * 360f / yaws, 0) * anchor.direction;
                        var view = new View { id = cell.scene + "-yaw-" + yaw, sector = cell.scene,
                            model = anchor.model, material = anchor.material, category = anchor.category,
                            floor = floor, eye = eye, direction = direction };
                        view.file = Path.Combine(report.directory, view.id + ".png");
                        Photograph(view); views.Add(view);
                    }
                    photographed = true; report.sectorsPhotographed++; break;
                }
                if (!photographed) skipped.Add(cell.scene + ": no safe player-height road/ground anchor; not an aerial substitute");
                if (report.sectorsInspected % 8 == 0)
                {
                    Write(report, views, skipped, errors, "cells");
                    Debug.Log("LOS_SANTOS_PLAYER_CAPTURE_PROGRESS sectors=" + report.sectorsInspected + "/" + cells.Length + " frames=" + views.Count);
                    EditorUtility.UnloadUnusedAssetsImmediate();
                }
            }
        }
        catch (Exception ex) { errors.Add(ex.ToString()); Debug.LogException(ex); }
        finally { Write(report, views, skipped, errors, "cells"); Restore(setup); }
        if (errors.Count > 0) throw new Exception("LOS_SANTOS_PLAYER_CAPTURE_FAILED " + string.Join(" | ", errors));
        Debug.Log("LOS_SANTOS_PLAYER_CAPTURE_COMPLETE sectors=" + report.sectorsInspected + " photographed=" + report.sectorsPhotographed + " frames=" + views.Count);
    }

    [Serializable] sealed class ReviewPoseSelection { public View[] views; }

    // Reuse the exact original player camera poses for comparable review
    // photographs. Safety checks may reject a pose, never relocate its camera.
    [MenuItem("Horizonte/Los Santos/Patrulha - fotografar poses de revisao")]
    public static void CaptureReviewPoses()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Pare o Play Mode antes da captura de revisao.");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        var report = NewReport("source-views");
        report.method += " Original eye/floor/direction from visual-recheck-poses.json; when the old body pose is no longer safe after repairs, use a supported player pose within 2m and record requestedEye/cameraRelocated. Shared Photograph settings, including its original slight downward pitch, are unchanged. Unsafe poses are explicit skips.";
        var views = new List<View>(); var skipped = new List<string>(); var errors = new List<string>();
        try
        {
            string input = Output + "/visual-recheck-poses.json";
            var requested = JsonUtility.FromJson<ReviewPoseSelection>(File.ReadAllText(input));
            if (requested?.views == null || requested.views.Length == 0)
                throw new InvalidOperationException("Nenhuma pose de revisao em " + input);
            var source = EditorSceneManager.OpenScene(GameUpdateSetup.GameScene, OpenSceneMode.Single);
            var stream = source.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GtaSaSectorStreaming>(true)).Single();
            var entries = stream.sectors.Where(s => s != null && !string.IsNullOrEmpty(s.scene)).ToArray();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var loaded = new Dictionary<string, Scene>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pose in requested.views)
            {
                report.sectorsInspected++;
                if (pose == null || string.IsNullOrWhiteSpace(pose.id))
                { skipped.Add("review pose: missing id"); continue; }
                string fileId = Clean(pose.id);
                if (!ids.Add(fileId)) throw new InvalidOperationException("ID de imagem duplicado: " + pose.id);
                if (!FiniteReviewVector(pose.floor) || !FiniteReviewVector(pose.eye) || !FiniteReviewVector(pose.direction) ||
                    pose.direction.sqrMagnitude < .001f || Vector3.Distance(pose.eye, pose.floor + Vector3.up * 1.65f) > .05f)
                { skipped.Add(pose.id + ": invalid or non-player-height original camera pose"); continue; }
                Synchronize(loaded, entries, pose.eye, 240);
                Physics.SyncTransforms();
                // TryEye remains the shared broad safety gate, but its adjusted
                // outputs are intentionally not substituted into the original pose.
                bool safe = TryEye(pose.floor, pose.direction, out _, out _) &&
                    !Physics.CheckSphere(pose.eye, .18f, 1, QueryTriggerInteraction.Ignore) &&
                    !Physics.CheckCapsule(pose.floor + Vector3.up * .4f, pose.eye, .2f, 1, QueryTriggerInteraction.Ignore);
                bool supported = Physics.RaycastAll(pose.floor + Vector3.up * .2f, Vector3.down, .4f, 1, QueryTriggerInteraction.Ignore)
                    .Any(h => h.normal.y >= .65f && Mathf.Abs(h.point.y - pose.floor.y) < .15f);
                Vector3 actualFloor=pose.floor, actualEye=pose.eye;
                bool relocated=false;
                if (!safe || !supported)
                {
                    var offsets=new[] {new Vector2(.5f,0),new Vector2(-.5f,0),new Vector2(0,.5f),new Vector2(0,-.5f),
                        new Vector2(1,0),new Vector2(-1,0),new Vector2(0,1),new Vector2(0,-1),
                        new Vector2(1.5f,0),new Vector2(-1.5f,0),new Vector2(0,1.5f),new Vector2(0,-1.5f)};
                    foreach(var offset in offsets)
                    {
                        if(!TryRepairEye(pose.floor+new Vector3(offset.x,0,offset.y),pose.direction,out var candidateFloor,out var candidateEye))continue;
                        if(Vector2.Distance(new Vector2(candidateFloor.x,candidateFloor.z),new Vector2(pose.floor.x,pose.floor.z))>2)continue;
                        actualFloor=candidateFloor;actualEye=candidateEye;relocated=true;break;
                    }
                    if(!relocated){skipped.Add(pose.id+": original and nearby player poses are unsafe or unsupported");continue;}
                }
                var view = new View { id = pose.id, sector = pose.sector, model = pose.model, material = pose.material,
                    category = string.IsNullOrEmpty(pose.category) ? "review of exact original player pose" : pose.category,
                    floor = actualFloor, eye = actualEye, direction = pose.direction,requestedEye=pose.eye,cameraRelocated=relocated,
                    sourceFloorY=pose.floor.y,actualPlayerFloorY=actualFloor.y };
                view.file = Path.Combine(report.directory, fileId + ".png");
                Photograph(view); views.Add(view); report.sectorsPhotographed++;
            }
        }
        catch (Exception ex) { errors.Add(ex.ToString()); Debug.LogException(ex); }
        finally { Write(report, views, skipped, errors, "source-views"); Restore(setup); }
        if (errors.Count > 0) throw new Exception("LOS_SANTOS_REVIEW_POSES_CAPTURE_FAILED " + string.Join(" | ", errors));
        Debug.Log("LOS_SANTOS_REVIEW_POSES_CAPTURE_COMPLETE poses=" + report.sectorsInspected + " frames=" + views.Count + " skipped=" + skipped.Count);
    }

    static bool FiniteReviewVector(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
        !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);

    [Serializable] sealed class RepairSelection { public string[] patrolAdditions; }
    [Serializable] sealed class RepairManifest { public RepairDefinition[] patches; }
    [Serializable] sealed class RepairDefinition { public string id, sector, sourceModel, surfaceKind; public RepairLimits boundsUnity; }
    [Serializable] sealed class RepairLimits { public Vector3 min, max; }
    [Serializable] sealed class WedgeEvidence { public WedgeView[] queries; }
    [Serializable] sealed class WedgeView { public string view; public float[] eye; public WedgeHit[] hits; }
    [Serializable] sealed class WedgeHit { public string model; }
    [Serializable] sealed class BeachReview { public BeachProbe[] nativeBlockedProbes; }
    [Serializable] sealed class BeachProbe { public string id; public Vector3 position; }

    // Dedicated player-height proof of the six patrol additions and the three
    // collision-aligned structures. Every unsafe/absent pose is reported.
    public static void CaptureRepairs()
    {
        var setup=EditorSceneManager.GetSceneManagerSetup();
        var report=NewReport("repairs");
        report.method += " Dedicated anchors sampled from installed ground repair meshes; longitudinal corridor approaches; structure views reuse reviewed player camera poses. Repair cameras choose the highest physically supported safe player floor within source anchorY-1m..source anchorY+1.2m; sourceFloorY and actualPlayerFloorY are recorded. This changes camera pose only, never source geometry. Unsafe or missing anchors are explicit skips.";
        var views=new List<View>();var skipped=new List<string>();var errors=new List<string>();
        try
        {
            var selection=JsonUtility.FromJson<RepairSelection>(File.ReadAllText(Output+"/geometry-merge.json"));
            var manifest=JsonUtility.FromJson<RepairManifest>(File.ReadAllText("Assets/GTASanAndreasModFull/GroundRepairs/geometry-repair-manifest.json"));
            var selected=new HashSet<string>(selection.patrolAdditions??Array.Empty<string>(),StringComparer.Ordinal);
            if(selected.Count==0)throw new Exception("No patrol repair IDs selected.");
            var installed=manifest.patches.Where(p=>selected.Contains(p.id)).ToArray();
            if(installed.Length!=selected.Count)throw new Exception("Patrol repair IDs are missing from the installed manifest.");
            var patches=installed.Where(p=>p.surfaceKind!="foundation").ToArray();
            foreach(var foundation in installed.Where(p=>p.surfaceKind=="foundation"))
                skipped.Add(foundation.id+": vertical bases are reviewed from nearby ground/source views, not wall-centroid player anchors");
            var source=EditorSceneManager.OpenScene(GameUpdateSetup.GameScene,OpenSceneMode.Single);
            var stream=source.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<GtaSaSectorStreaming>(true)).First();
            var entries=stream.sectors.Where(s=>s!=null && !string.IsNullOrEmpty(s.scene)).ToArray();
            var loaded=new Dictionary<string,Scene>();
            foreach(var patch in patches)
            {
                var bounds=new Bounds((patch.boundsUnity.min+patch.boundsUnity.max)*.5f,patch.boundsUnity.max-patch.boundsUnity.min);
                Synchronize(loaded,entries,bounds.center,240);Physics.SyncTransforms();
                var instance=loaded.Values.SelectMany(s=>s.GetRootGameObjects()).SelectMany(r=>r.GetComponentsInChildren<Transform>(true))
                    .SingleOrDefault(t=>t.name=="REPARO CHAO — "+patch.id);
                if(!instance){skipped.Add(patch.id+": installed repair object not found");continue;}
                var points=RepairPoints(instance);
                if(points.Count==0){skipped.Add(patch.id+": no upward source-floor anchors");continue;}
                Vector3 axis=RepairAxis(points);
                bool corridor=patch.id=="patrol-north-street-14" || patch.id=="patrol-sidewalk-42";
                var anchors=new List<Vector3>();
                if(corridor)
                {
                    var sorted=points.OrderBy(p=>Vector3.Dot(p,axis)).ToArray();
                    anchors.Add(sorted[Mathf.Min(sorted.Length-1,Mathf.Max(0,sorted.Length/12))]);
                    anchors.Add(sorted[Mathf.Max(0,sorted.Length-1-Mathf.Max(0,sorted.Length/12))]);
                }
                else
                {
                    anchors.Add(points.OrderBy(p=>(p-bounds.center).sqrMagnitude).First());
                    while(anchors.Count<4)
                    {
                        var candidates=points.Where(p=>anchors.All(q=>Vector3.Distance(p,q)>.75f)).ToArray();
                        if(candidates.Length==0)break;
                        anchors.Add(candidates.OrderByDescending(p=>anchors.Min(q=>Vector3.ProjectOnPlane(p-q,Vector3.up).sqrMagnitude)).First());
                    }
                }
                int before=views.Count;
                for(int i=0;i<anchors.Count;i++)
                {
                    Vector3 direction=corridor ? (i==0?axis:-axis) : Vector3.ProjectOnPlane(bounds.center-anchors[i],Vector3.up).normalized;
                    if(direction.sqrMagnitude<.1f)direction=axis;
                    if(!TryRepairEye(anchors[i],direction,out var floor,out var eye)){skipped.Add(patch.id+" anchor"+i+": unsupported floor or unsafe player capsule/camera");continue;}
                    var view=new View { id=Clean(patch.id)+"-"+i,sector=patch.sector,model=patch.sourceModel,
                        category=corridor?"repair corridor approach":"repair floor",floor=floor,eye=eye,direction=direction,
                        sourceFloorY=anchors[i].y,actualPlayerFloorY=floor.y };
                    view.file=Path.Combine(report.directory,view.id+".png");Photograph(view);views.Add(view);
                }
                report.sectorsInspected++;if(views.Count>before)report.sectorsPhotographed++;
            }
            string evidencePath="../tools/los-santos-repair/floating-wedge-pixel-evidence.json";
            var evidence=File.Exists(evidencePath)?JsonUtility.FromJson<WedgeEvidence>(File.ReadAllText(evidencePath)):null;
            foreach(var model in new[]{"tvstudio01_LAw2","country_law2","BeachBlok02_LAe2"})
            {
                var query=(evidence?.queries??Array.Empty<WedgeView>()).FirstOrDefault(q=>q.eye!=null && q.eye.Length==3 &&
                    (q.hits??Array.Empty<WedgeHit>()).Any(h=>string.Equals(h.model,model,StringComparison.OrdinalIgnoreCase)));
                Vector3 nominalEye;string poseEvidence;
                if(query!=null){nominalEye=new Vector3(query.eye[0],query.eye[1],query.eye[2]);poseEvidence=query.view;}
                else if(model=="BeachBlok02_LAe2")
                {
                    string beachPath="../tools/los-santos-repair/beachblok02-collider-handoff.json";
                    var beach=File.Exists(beachPath)?JsonUtility.FromJson<BeachReview>(File.ReadAllText(beachPath)):null;
                    var probe=beach?.nativeBlockedProbes?.FirstOrDefault();
                    if(probe==null){skipped.Add(model+": reviewed native player probe not found");continue;}
                    nominalEye=probe.position+Vector3.up*1.65f;poseEvidence="native player obstruction probe "+probe.id;
                }
                else{skipped.Add(model+": reviewed player camera pose not found");continue;}
                Synchronize(loaded,entries,nominalEye,240);Physics.SyncTransforms();
                var structure=loaded.Values.SelectMany(s=>s.GetRootGameObjects()).SelectMany(r=>r.GetComponentsInChildren<Transform>(true))
                    .FirstOrDefault(t=>t.name.StartsWith(model+" #",StringComparison.OrdinalIgnoreCase));
                if(!structure){skipped.Add(model+": saved structure not found");continue;}
                var renderers=structure.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled).ToArray();
                if(renderers.Length==0){skipped.Add(model+": no visible structure");continue;}
                var bounds=renderers[0].bounds;foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);
                Vector3 direction=(bounds.center-nominalEye).normalized;
                var bases=new[]{nominalEye-Vector3.up*1.65f,nominalEye-Vector3.up*1.65f+Vector3.Cross(Vector3.up,direction).normalized*5};
                int before=views.Count;
                for(int i=0;i<bases.Length;i++)
                {
                    if(!TryRepairEye(bases[i],direction,out var floor,out var eye)){skipped.Add(model+" view"+i+": unsupported floor or unsafe player capsule/camera");continue;}
                    var view=new View {id=Clean(model)+"-"+i,model=model,material=poseEvidence,category="structure collision repair; visual retained",
                        floor=floor,eye=eye,direction=(bounds.center-eye).normalized,sourceFloorY=bases[i].y,actualPlayerFloorY=floor.y};
                    view.file=Path.Combine(report.directory,view.id+".png");Photograph(view);views.Add(view);
                }
                report.sectorsInspected++;if(views.Count>before)report.sectorsPhotographed++;
            }
        }
        catch(Exception ex){errors.Add(ex.ToString());Debug.LogException(ex);}
        finally{Write(report,views,skipped,errors,"repairs");Restore(setup);}
        if(errors.Count>0)throw new Exception("LOS_SANTOS_REPAIR_CAPTURE_FAILED "+string.Join(" | ",errors));
        Debug.Log("LOS_SANTOS_REPAIR_CAPTURE_COMPLETE targets="+report.sectorsInspected+" frames="+views.Count+" skipped="+skipped.Count);
    }

    static bool TryRepairEye(Vector3 point,Vector3 direction,out Vector3 floor,out Vector3 eye)
    {
        direction=Vector3.ProjectOnPlane(direction,Vector3.up).normalized;
        if(direction.sqrMagnitude<.1f)direction=Vector3.forward;
        var candidates=new List<Vector3>();
        foreach(var offset in new[]{Vector3.zero,direction*2,-direction*2,Vector3.Cross(Vector3.up,direction)*2,Vector3.Cross(direction,Vector3.up)*2})
            foreach(var hit in Physics.RaycastAll(point+offset+Vector3.up*1.3f,Vector3.down,2.31f,1,QueryTriggerInteraction.Ignore))
                if(hit.normal.y>=.65f && hit.point.y<=point.y+1.2001f && hit.point.y>=point.y-1.0001f)candidates.Add(hit.point);
        foreach(var candidate in candidates.OrderByDescending(p=>p.y).ThenBy(p=>Vector3.ProjectOnPlane(p-point,Vector3.up).sqrMagnitude))
        {
            Vector3 candidateEye=candidate+Vector3.up*1.65f;
            if(Physics.CheckSphere(candidateEye,.18f,1,QueryTriggerInteraction.Ignore) ||
                Physics.CheckCapsule(candidate+Vector3.up*.4f,candidateEye,.2f,1,QueryTriggerInteraction.Ignore))continue;
            floor=candidate;eye=candidateEye;return true;
        }
        floor=point;eye=point+Vector3.up*1.65f;return false;
    }

    static List<Vector3> RepairPoints(Transform instance)
    {
        var points=new List<Vector3>();
        foreach(var filter in instance.GetComponentsInChildren<MeshFilter>())
        {
            if(!filter.sharedMesh || !filter.gameObject.activeInHierarchy || !filter.GetComponent<MeshRenderer>() || !filter.GetComponent<MeshRenderer>().enabled)continue;
            using(var data=MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh))
            using(var vertices=new NativeArray<Vector3>(data[0].vertexCount,Allocator.Temp))
            {
                data[0].GetVertices(vertices);
                for(int slot=0;slot<data[0].subMeshCount;slot++)
                {
                    var sub=data[0].GetSubMesh(slot);if(sub.topology!=MeshTopology.Triangles)continue;
                    using(var indices=new NativeArray<int>(sub.indexCount,Allocator.Temp))
                    {
                        data[0].GetIndices(indices,slot);
                        for(int i=0;i+2<indices.Length;i+=3)
                        {
                            Vector3 a=filter.transform.TransformPoint(vertices[indices[i]]),b=filter.transform.TransformPoint(vertices[indices[i+1]]),c=filter.transform.TransformPoint(vertices[indices[i+2]]);
                            Vector3 cross=Vector3.Cross(b-a,c-a);if(cross.magnitude<.1f || cross.normalized.y<.72f)continue;
                            points.Add((a+b+c)/3);
                        }
                    }
                }
            }
        }
        return points;
    }
    static Vector3 RepairAxis(List<Vector3> points)
    {
        var mean=points.Aggregate(Vector3.zero,(a,p)=>a+p)/points.Count;
        double xx=0,zz=0,xz=0;foreach(var p in points){double x=p.x-mean.x,z=p.z-mean.z;xx+=x*x;zz+=z*z;xz+=x*z;}
        float angle=(float)(.5*Math.Atan2(2*xz,xx-zz));return new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
    }


    public static void CaptureFindings()
    {
        var setup = EditorSceneManager.GetSceneManagerSetup();
        var report = NewReport("findings");
        var views = new List<View>(); var skipped = new List<string>(); var errors = new List<string>();
        try
        {
            var data = JsonUtility.FromJson<IncidentFile>(File.ReadAllText(Path.Combine(Output, "patrol-" + Tag() + ".json")));
            var source = EditorSceneManager.OpenScene(GameUpdateSetup.GameScene, OpenSceneMode.Single);
            var stream = source.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GtaSaSectorStreaming>(true)).First();
            var entries = stream.sectors.Where(s => s != null && !string.IsNullOrEmpty(s.scene)).ToArray();
            var loaded = new Dictionary<string, Scene>();
            var positions = new List<Vector3>();
            int limit = IntArgument("--patrol-findings-limit", 200, 1, 2000);
            foreach (var incident in data.incidents ?? Array.Empty<Incident>())
            {
                if (views.Count >= limit * 2) { skipped.Add("Remaining incidents omitted by explicit capture limit " + limit); break; }
                if (positions.Any(p => Vector3.Distance(p, incident.position) < 8)) continue;
                positions.Add(incident.position);
                Synchronize(loaded, entries, incident.position, 240); Physics.SyncTransforms();
                Vector3 basePoint = incident.position;
                if (incident.expectedHeight != 0 && basePoint.y < incident.expectedHeight - 1) basePoint.y = incident.expectedHeight;
                var route = (data.routes ?? Array.Empty<Route>()).FirstOrDefault(r => r.id == incident.routeId);
                Vector3 direction = route != null ? Vector3.ProjectOnPlane(route.finalPosition - route.start, Vector3.up).normalized : Vector3.forward;
                if (direction.sqrMagnitude < .1f) direction = Vector3.forward;
                // Photograph the approach to a defect, rather than putting the lens in the obstacle.
                Vector3 approach = basePoint - direction * 3;
                if (!TryEye(approach, direction, out var floor, out var eye) &&
                    !TryEye(basePoint, direction, out floor, out eye))
                { skipped.Add(incident.id + ": no safe approach camera " + incident.position); continue; }
                for (int side = 0; side < 2; side++)
                {
                    Vector3 aim = Quaternion.Euler(0, side == 0 ? 0 : 90, 0) * direction;
                    var view = new View { id = Clean(incident.id ?? incident.routeId ?? "incident") + "-" + views.Count,
                        sector = incident.sector, model = incident.model, material = incident.collider,
                        category = incident.type + ": " + incident.detail, floor = floor, eye = eye, direction = aim };
                    view.file = Path.Combine(report.directory, view.id + ".png"); Photograph(view); views.Add(view);
                }
                report.sectorsInspected++;
                if (report.sectorsInspected % 8 == 0) { Write(report, views, skipped, errors, "findings"); EditorUtility.UnloadUnusedAssetsImmediate(); }
            }
        }
        catch (Exception ex) { errors.Add(ex.ToString()); Debug.LogException(ex); }
        finally { Write(report, views, skipped, errors, "findings"); Restore(setup); }
        if (errors.Count > 0) throw new Exception("LOS_SANTOS_FINDING_CAPTURE_FAILED " + string.Join(" | ", errors));
        Debug.Log("LOS_SANTOS_FINDING_CAPTURE_COMPLETE frames=" + views.Count);
    }

    // Optional adapters for the physical patrol's callbacks. Cameras are disposable
    // so scene synchronization by the patrol cannot leave stale camera references.
    public static void BeginPatrolCapture()
    { live = NewReport("movement"); liveViews = new List<View>(); LiveCounts.Clear(); LivePrevious.Clear(); }
    public static void CapturePatrolSample(Vector3 position, Vector3 direction, string routeId)
    {
        if (live == null) BeginPatrolCapture();
        routeId = routeId ?? "route";
        if (LiveCounts.TryGetValue(routeId, out int count) && count >= 2) return;
        if (LivePrevious.TryGetValue(routeId, out var previous) && Vector3.Distance(previous, position) < 35) return;
        if (!TryEye(position, direction, out var floor, out var eye)) return;
        direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        if (direction.sqrMagnitude < .1f) direction = Vector3.forward;
        var view = new View { id = Clean(routeId) + "-sample-" + count, category = "moving player", floor = floor, eye = eye, direction = direction };
        view.file = Path.Combine(live.directory, view.id + ".png"); Photograph(view); liveViews.Add(view);
        LiveCounts[routeId] = count + 1; LivePrevious[routeId] = position;
    }
    public static void CapturePatrolIncident(string type, Vector3 position, string detail)
    {
        if (live == null) BeginPatrolCapture();
        if (liveViews.Count(v => v.category != "moving player") >= 100) return;
        if (!TryEye(position - Vector3.forward * 2, Vector3.forward, out var floor, out var eye)) return;
        var view = new View { id = "incident-" + liveViews.Count, category = type + ": " + detail,
            floor = floor, eye = eye, direction = Vector3.forward };
        view.file = Path.Combine(live.directory, view.id + ".png"); Photograph(view); liveViews.Add(view);
    }
    public static void EndPatrolCapture()
    { if (live != null) Write(live, liveViews, new List<string>(), new List<string>(), "movement"); live = null; liveViews = null; }

    static void CollectAnchors(MeshFilter filter, Bounds tile, List<Anchor> anchors)
    {
        var renderer = filter.GetComponent<MeshRenderer>();
        if (!filter.sharedMesh || !renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
            !Intersects(renderer.bounds, tile.center, 128)) return;
        string model = Instance(filter.transform);
        if (Excluded.IsMatch(model)) return;
        var materials = renderer.sharedMaterials;
        if (!materials.Any(m => m && m.mainTexture && (RoadTexture.IsMatch(m.mainTexture.name) || GroundTexture.IsMatch(m.mainTexture.name)))) return;
        using (var data = MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh))
        using (var vertices = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp))
        {
            data[0].GetVertices(vertices);
            for (int slot = 0; slot < data[0].subMeshCount; slot++)
            {
                var mat = slot < materials.Length ? materials[slot] : null;
                if (!mat || !mat.mainTexture || Excluded.IsMatch(mat.mainTexture.name)) continue;
                string texture = mat.mainTexture.name;
                bool road = RoadTexture.IsMatch(texture);
                if (!road && !GroundTexture.IsMatch(texture)) continue;
                var sub = data[0].GetSubMesh(slot);
                if (sub.topology != MeshTopology.Triangles) continue;
                using (var indices = new NativeArray<int>(sub.indexCount, Allocator.Temp))
                {
                    data[0].GetIndices(indices, slot);
                    for (int i = 0; i + 2 < indices.Length; i += 3)
                    {
                        Vector3 a = filter.transform.TransformPoint(vertices[indices[i]]), b = filter.transform.TransformPoint(vertices[indices[i + 1]]), c = filter.transform.TransformPoint(vertices[indices[i + 2]]);
                        Vector3 cross = Vector3.Cross(b - a, c - a), point = (a + b + c) / 3f;
                        if (cross.magnitude < 6 || cross.normalized.y < .8f || !Inside(point, tile)) continue;
                        Vector3 ab = Vector3.ProjectOnPlane(b - a, Vector3.up), bc = Vector3.ProjectOnPlane(c - b, Vector3.up), ca = Vector3.ProjectOnPlane(a - c, Vector3.up);
                        Vector3 direction = new[] { ab, bc, ca }.OrderByDescending(v => v.sqrMagnitude).First().normalized;
                        float score = (new Vector2(point.x - tile.center.x, point.z - tile.center.z)).sqrMagnitude + (road ? 0 : 1000000);
                        anchors.Add(new Anchor { point = point, direction = direction, score = score, model = model, material = mat.name + "/" + texture, category = road ? "road surface" : "ground fallback" });
                    }
                }
            }
        }
    }

    static bool TryEye(Vector3 point, Vector3 direction, out Vector3 floor, out Vector3 eye)
    {
        direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        if (direction.sqrMagnitude < .1f) direction = Vector3.forward;
        foreach (var offset in new[] { Vector3.zero, direction * 2, -direction * 2, Vector3.Cross(Vector3.up, direction) * 2, Vector3.Cross(direction, Vector3.up) * 2 })
        {
            floor = point + offset;
            var hits = Physics.RaycastAll(floor + Vector3.up * .8f, Vector3.down, 2, 1, QueryTriggerInteraction.Ignore)
                .Where(h => h.normal.y >= .65f && Mathf.Abs(h.point.y - point.y) < 1).OrderBy(h => Mathf.Abs(h.point.y - point.y)).ToArray();
            if (hits.Length > 0) floor = hits[0].point;
            eye = floor + Vector3.up * 1.65f;
            if (!Physics.CheckSphere(eye, .18f, 1, QueryTriggerInteraction.Ignore) &&
                !Physics.CheckCapsule(floor + Vector3.up * .4f, eye, .2f, 1, QueryTriggerInteraction.Ignore)) return true;
        }
        floor = point; eye = point + Vector3.up * 1.65f; return false;
    }
    static void Synchronize(Dictionary<string, Scene> loaded, GtaSaSectorStreaming.Sector[] entries, Vector3 point, float radius)
    {
        var wanted = new HashSet<string>(entries.Where(s => Intersects(s.bounds, point, radius)).Select(s => s.scene));
        foreach (var name in loaded.Keys.Where(n => !wanted.Contains(n)).ToArray())
        { EditorSceneManager.CloseScene(loaded[name], true); loaded.Remove(name); }
        foreach (var name in wanted.OrderBy(n => n).Where(n => !loaded.ContainsKey(n)))
        {
            string path = Sectors + name + ".unity";
            if (!File.Exists(path)) throw new FileNotFoundException("Setor de captura ausente", path);
            loaded.Add(name, EditorSceneManager.OpenScene(path, OpenSceneMode.Additive));
        }
    }
    static bool Intersects(Bounds bounds, Vector3 point, float radius) => bounds.min.x <= point.x + radius && bounds.max.x >= point.x - radius && bounds.min.z <= point.z + radius && bounds.max.z >= point.z - radius;
    static bool Inside(Vector3 point, Bounds bounds) => point.x >= bounds.min.x && point.x < bounds.max.x && point.z >= bounds.min.z && point.z < bounds.max.z && point.x >= -256 && point.x <= 3072 && point.z >= 512 && point.z <= 3072;
    static Bounds Tile(string name)
    {
        var m = Regex.Match(name, @"GTASASector_([mp])(\d+)_([mp])(\d+)");
        if (!m.Success) throw new Exception("Nome de setor inválido: " + name);
        int x = int.Parse(m.Groups[2].Value) * (m.Groups[1].Value == "m" ? -1 : 1), y = int.Parse(m.Groups[4].Value) * (m.Groups[3].Value == "m" ? -1 : 1);
        return new Bounds(new Vector3(x * 256 + 128, 0, -y * 256 - 128), new Vector3(256, 2000, 256));
    }
    static string Instance(Transform transform)
    { for (var node = transform; node; node = node.parent) if (node.name.Contains("#") || node.name.StartsWith("REPARO", StringComparison.Ordinal)) return node.name; return transform.name; }
    static Report NewReport(string kind)
    {
        var report = new Report { tag = Tag(), directory = Path.GetFullPath(Output + "/views-" + kind + "-" + Tag()), method = "Player-height 1.65m, 70 degree camera, far clip 180m, source bounds loaded to 240m. Solid background RGB(0.25,0.42,0.65); road/ground visual triangles; no login or game simulation scripts; original water present for cell/finding captures. Skipped cells are explicitly recorded." };
        Directory.CreateDirectory(report.directory); return report;
    }
    static string Tag() => Clean(Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--patrol-tag="))?.Substring("--patrol-tag=".Length) ?? "current");
    static string Clean(string value) => Regex.Replace(value ?? "unknown", @"[^a-zA-Z0-9_-]", "_");
    static int IntArgument(string name, int fallback, int min, int max)
    { string text = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith(name + "="))?.Substring(name.Length + 1); return int.TryParse(text, out int value) ? Mathf.Clamp(value, min, max) : fallback; }
    static void Write(Report report, List<View> views, List<string> skipped, List<string> errors, string kind)
    { report.views = views.ToArray(); report.frames = views.Count; report.skipped = skipped.ToArray(); report.errors = errors.ToArray(); Directory.CreateDirectory(Output); File.WriteAllText(Output + "/capture-" + kind + "-" + report.tag + ".json", JsonUtility.ToJson(report, true)); }
    static void Restore(SceneSetup[] setup)
    { if (setup.Length > 0 && setup.All(s => !string.IsNullOrEmpty(s.path))) EditorSceneManager.RestoreSceneManagerSetup(setup); else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
    static void Photograph(View view)
    {
        var camera = new GameObject("Temporary player-height patrol camera").AddComponent<Camera>();
        var target = new RenderTexture(1024, 576, 24); var image = new Texture2D(1024, 576, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            camera.nearClipPlane = .06f; camera.farClipPlane = 180; camera.fieldOfView = 70; camera.allowHDR = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.25f, .42f, .65f);
            camera.transform.SetPositionAndRotation(view.eye, Quaternion.LookRotation(view.direction.normalized + Vector3.down * .025f));
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1024, 576), 0, 0); image.Apply(); File.WriteAllBytes(view.file, image.EncodeToPNG());
        }
        finally { camera.targetTexture = null; RenderTexture.active = previous; Object.DestroyImmediate(image); Object.DestroyImmediate(target); Object.DestroyImmediate(camera.gameObject); }
    }
}
