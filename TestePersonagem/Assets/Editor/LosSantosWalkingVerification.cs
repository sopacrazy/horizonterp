using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Exercises native CharacterController.Move against the saved repaired scenes.
// No ThirdPersonPlayer, animator, login or networking component is instantiated.
public static class LosSantosWalkingVerification
{
    const string Main = "Assets/Scenes/AndroidLosSantos/TestePersonagem.unity";
    const string Sectors = "Assets/Scenes/GTASanAndreasModFullSectors/";
    const string Output = "../tools/los-santos-repair/walking-verification.json";
    const float Dt = .02f, Speed = 1.65f, Gravity = 22f;

    sealed class Route
    {
        public string name;
        public Vector2 start, end;
        public float referenceHeight;
    }
    static readonly Route[] Routes = {
        new Route { name="gap14-street", start=new Vector2(2212.56f,1449),
            end=new Vector2(2212.56f,1537), referenceHeight=23 },
        new Route { name="gap29-courtyard-former-ghost-box", start=new Vector2(2432,1759),
            end=new Vector2(2442,1759), referenceHeight=12.6f },
        new Route { name="patrol-north-street14-full", start=new Vector2(2213,1537),
            end=new Vector2(2213,1396), referenceHeight=23 },
        new Route { name="patrol-sidewalk42-full", start=new Vector2(2085,1672),
            end=new Vector2(2085,1740), referenceHeight=12.8f },
        new Route { name="patrol-road04-sidewalk", start=new Vector2(2431.35f,1743.35f),
            end=new Vector2(2441,1743), referenceHeight=12.7f }
    };

    [Serializable] sealed class Settings
    {
        public float height, radius, stepOffset, slopeLimit, skinWidth, minMoveDistance;
        public Vector3 center;
    }
    [Serializable] sealed class Capability
    {
        public bool supported, grounded;
        public float movedDistance;
        public string error;
    }
    [Serializable] sealed class Result
    {
        public string route, error;
        public bool passed, finalGrounded;
        public Vector3 start, destination, finalPosition;
        public float targetDistance, traveledDistance, remainingDistance, simulatedSeconds;
        public float largestStep, maximumFloorDeviation, maximumDropBelowFloor;
        public int steps, groundedSteps, centerRayGapsWithNativeSupport;
        public string[] loadedSectors;
    }
    [Serializable] sealed class Report
    {
        public string method, status;
        public Settings controller;
        public Capability editModeController;
        public Result[] routes;
        public string[] errors;
    }

    [MenuItem("Horizonte/Los Santos/Verificar caminhada nas ruas reparadas")]
    public static void Verify()
    {
        if (EditorApplication.isPlaying)
            throw new InvalidOperationException("Execute a verificacao fora do Play Mode.");
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Jogador.prefab");
        var template = source ? source.GetComponent<CharacterController>() : null;
        if (!template) throw new InvalidOperationException("CharacterController do prefab Jogador ausente.");
        var settings = new Settings {
            height=template.height, radius=template.radius, center=template.center,
            stepOffset=template.stepOffset, slopeLimit=template.slopeLimit,
            skinWidth=template.skinWidth, minMoveDistance=template.minMoveDistance
        };
        var errors = new List<string>();
        var results = new List<Result>();
        var opened = new List<Scene>();
        Scene initialActive = SceneManager.GetActiveScene();
        Scene temporary = default;
        Capability capability = null;
        try
        {
            temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(temporary);
            capability = CheckEditModeController(settings);
            if (!capability.supported)
                throw new InvalidOperationException("CharacterController.Move nao respondeu com colisao nativa no Edit Mode: " + capability.error);

            var main = SceneManager.GetSceneByPath(Main);
            if (!main.IsValid() || !main.isLoaded)
            {
                main = EditorSceneManager.OpenScene(Main, OpenSceneMode.Additive);
                opened.Add(main);
            }
            var stream = main.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<GtaSaSectorStreaming>(true)).Single();
            foreach (var route in Routes)
            {
                var needed = stream.sectors.Where(sector => sector != null && TouchesRoute(sector.bounds, route, 1.5f)).ToArray();
                if (needed.Length == 0) throw new InvalidOperationException("Rota sem bounds de setores: " + route.name);
                foreach (var sector in needed)
                {
                    string path = Sectors + sector.scene + ".unity";
                    var scene = SceneManager.GetSceneByPath(path);
                    if (scene.IsValid() && scene.isLoaded) continue;
                    if (!File.Exists(path)) throw new FileNotFoundException("Setor de caminhada ausente", path);
                    opened.Add(EditorSceneManager.OpenScene(path, OpenSceneMode.Additive));
                }
                SceneManager.SetActiveScene(temporary);
                Physics.SyncTransforms();
                var result = Walk(route, settings);
                result.loadedSectors = needed.Select(sector => sector.scene).ToArray();
                results.Add(result);
                if (!result.passed) errors.Add(route.name + ": " + result.error);
            }
        }
        catch (Exception exception) { errors.Add(exception.Message); }
        finally
        {
            // All probes live in the disposable scene. Source scenes are never
            // saved, and scenes already open before this call remain open.
            if (temporary.IsValid() && temporary.isLoaded) EditorSceneManager.CloseScene(temporary, true);
            for (int i=opened.Count-1;i>=0;i--)
                if (opened[i].IsValid() && opened[i].isLoaded) EditorSceneManager.CloseScene(opened[i], true);
            if (initialActive.IsValid() && initialActive.isLoaded) SceneManager.SetActiveScene(initialActive);
        }
        var report = new Report {
            method="Native CharacterController.Move in Edit Mode; prefab settings; dt=.02, walk=1.65m/s, gravity=22m/s2; static floor probes use layer 0 only. A missed centre ray is accepted only with native grounded/Below plus a separate upward foot-sphere contact within 0.25m; supported centre seams are counted. Unsupported segments still fail.",
            status=errors.Count==0 && results.Count==Routes.Length ? "passed" : "failed",
            controller=settings, editModeController=capability, routes=results.ToArray(), errors=errors.ToArray()
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(Output)));
        File.WriteAllText(Output, JsonUtility.ToJson(report, true));
        if (report.status != "passed")
            throw new InvalidOperationException("LOS_SANTOS_WALKING_VERIFICATION_FAILED " + string.Join("; ", errors));
        Debug.Log("LOS_SANTOS_WALKING_VERIFICATION_OK routes=" + results.Count +
            " distance=" + results.Sum(item => item.traveledDistance) + " report=" + Path.GetFullPath(Output));
    }

    static bool TouchesRoute(Bounds bounds, Route route, float margin)
    {
        float minX=Mathf.Min(route.start.x,route.end.x)-margin, maxX=Mathf.Max(route.start.x,route.end.x)+margin;
        float minZ=Mathf.Min(route.start.y,route.end.y)-margin, maxZ=Mathf.Max(route.start.y,route.end.y)+margin;
        return bounds.max.x>=minX && bounds.min.x<=maxX && bounds.max.z>=minZ && bounds.min.z<=maxZ;
    }

    static CharacterController CreateController(Settings settings)
    {
        var go = new GameObject("Temporary walking capsule") { hideFlags=HideFlags.HideAndDontSave, layer=2 };
        var controller = go.AddComponent<CharacterController>();
        controller.enabled=false;
        controller.height=settings.height; controller.radius=settings.radius; controller.center=settings.center;
        controller.stepOffset=settings.stepOffset; controller.slopeLimit=settings.slopeLimit;
        controller.skinWidth=settings.skinWidth; controller.minMoveDistance=settings.minMoveDistance;
        controller.detectCollisions=true;
        return controller;
    }

    static Capability CheckEditModeController(Settings settings)
    {
        var floor = new GameObject("Temporary native controller capability floor") {
            hideFlags=HideFlags.HideAndDontSave, layer=0
        };
        var box = floor.AddComponent<BoxCollider>();
        var at = new Vector3(-10000,1000,-10000);
        floor.transform.position=at+Vector3.down*.5f;
        box.size=new Vector3(20,1,20);
        var controller=CreateController(settings);
        try
        {
            controller.transform.position=at+Vector3.up*(.04f-FootOffset(settings));
            controller.enabled=true;
            Physics.SyncTransforms();
            Vector3 before=controller.transform.position;
            bool grounded=false;
            for (int i=0;i<20;i++)
            {
                Physics.SyncTransforms();
                var flags=controller.Move(new Vector3(Speed*Dt,-2*Dt,0));
                grounded |= controller.isGrounded || (flags & CollisionFlags.Below)!=0;
            }
            float moved=controller.transform.position.x-before.x;
            bool supported=moved>.45f && moved<.85f && grounded &&
                Mathf.Abs(controller.transform.position.y+FootOffset(settings)-at.y)<.2f;
            return new Capability { supported=supported, grounded=grounded, movedDistance=moved,
                error=supported ? "" : "flat-floor probe moved="+moved+" grounded="+grounded };
        }
        finally { Object.DestroyImmediate(controller.gameObject); Object.DestroyImmediate(floor); }
    }

    static float FootOffset(Settings settings) => settings.center.y-settings.height*.5f;

    static bool TryFloor(Vector2 point, Route route, out float height)
    {
        // The narrow height window intentionally excludes the old invisible
        // shop roof at y17.55 and the bridge above the street test route.
        var hits=Physics.RaycastAll(new Vector3(point.x,route.referenceHeight+1.5f,point.y),
            Vector3.down,4,1,QueryTriggerInteraction.Ignore);
        var floors=hits.Where(hit=>hit.normal.y>=.72f &&
            hit.point.y>=route.referenceHeight-2.5f).OrderByDescending(hit=>hit.point.y).ToArray();
        height=floors.Length>0 ? floors[0].point.y : 0;
        return floors.Length>0;
    }

    static Result Walk(Route route, Settings settings)
    {
        var result = new Result { route=route.name, targetDistance=Vector2.Distance(route.start,route.end), error="" };
        var controller=CreateController(settings);
        try
        {
            if (!TryFloor(route.start,route,out float startFloor))
                throw new InvalidOperationException("Piso inicial nao encontrado na faixa de altura auditada.");
            controller.transform.position=new Vector3(route.start.x,startFloor+.04f-FootOffset(settings),route.start.y);
            controller.enabled=true;
            Physics.SyncTransforms();
            result.start=controller.transform.position;
            result.destination=new Vector3(route.end.x,route.referenceHeight,route.end.y);
            float verticalSpeed=-2, stalledFor=0;
            int maxSteps=Mathf.CeilToInt((result.targetDistance/Speed*1.5f+10)/Dt);
            for (int step=0;step<maxSteps;step++)
            {
                Vector3 before=controller.transform.position;
                Vector2 position=new Vector2(before.x,before.z);
                Vector2 delta=route.end-position;
                if (delta.magnitude<.15f) break;
                Vector2 movement=delta.normalized*Mathf.Min(Speed*Dt,delta.magnitude);
                if (controller.isGrounded && verticalSpeed<0) verticalSpeed=-2;
                verticalSpeed=Mathf.Max(verticalSpeed-Gravity*Dt,-30);
                Physics.SyncTransforms();
                var flags=controller.Move(new Vector3(movement.x,verticalSpeed*Dt,movement.y));
                Physics.SyncTransforms();
                Vector3 after=controller.transform.position;
                float progress=Vector2.Distance(position,route.end)-Vector2.Distance(new Vector2(after.x,after.z),route.end);
                stalledFor=progress>.001f ? 0 : stalledFor+Dt;
                result.traveledDistance+=Vector2.Distance(position,new Vector2(after.x,after.z));
                result.largestStep=Mathf.Max(result.largestStep,Mathf.Abs(after.y-before.y));
                result.steps++;
                if (controller.isGrounded || (flags & CollisionFlags.Below)!=0) result.groundedSteps++;
                if (!TryFloor(new Vector2(after.x,after.z),route,out float floor))
                {
                    bool nativeGrounded=controller.isGrounded || (flags & CollisionFlags.Below)!=0;
                    if (!nativeGrounded || !TryFootSupport(controller,settings,route,out floor))
                        throw new InvalidOperationException("Trajeto ficou sem apoio fisico em "+after);
                    result.centerRayGapsWithNativeSupport++;
                }
                float feet=after.y+FootOffset(settings);
                result.maximumFloorDeviation=Mathf.Max(result.maximumFloorDeviation,Mathf.Abs(feet-floor));
                result.maximumDropBelowFloor=Mathf.Max(result.maximumDropBelowFloor,floor-feet);
                if (Mathf.Abs(feet-floor)>1)
                    throw new InvalidOperationException("Capsula afastou mais de 1m do piso: feet="+feet+" floor="+floor);
                if (stalledFor>3)
                    throw new InvalidOperationException("Capsula bloqueada por mais de 3s simulados em "+after);
            }
            // Settle at the actual floor instead of relying on a stale contact
            // flag immediately after the final horizontal step.
            for (int i=0;i<8;i++) { Physics.SyncTransforms(); controller.Move(Vector3.down*2*Dt); }
            result.finalPosition=controller.transform.position;
            result.remainingDistance=Vector2.Distance(new Vector2(result.finalPosition.x,result.finalPosition.z),route.end);
            result.finalGrounded=controller.isGrounded;
            result.simulatedSeconds=result.steps*Dt;
            result.passed=result.remainingDistance<.25f && result.finalGrounded;
            if (!result.passed) result.error="Destino nao alcancado ou capsula sem contato final: remaining="+
                result.remainingDistance+" grounded="+result.finalGrounded;
        }
        catch (Exception exception)
        {
            result.error=exception.Message;
            result.finalPosition=controller.transform.position;
            result.remainingDistance=Vector2.Distance(new Vector2(result.finalPosition.x,result.finalPosition.z),route.end);
            result.finalGrounded=controller.isGrounded;
            result.simulatedSeconds=result.steps*Dt;
        }
        finally { Object.DestroyImmediate(controller.gameObject); }
        return result;
    }

    static bool TryFootSupport(CharacterController controller, Settings settings, Route route, out float height)
    {
        Vector3 feet=controller.transform.position+Vector3.up*FootOffset(settings);
        float radius=settings.radius*.8f;
        var contacts=Physics.SphereCastAll(feet+Vector3.up*(radius+.12f),radius,Vector3.down,.5f,1,QueryTriggerInteraction.Ignore)
            .Where(hit=>hit.collider!=controller && hit.normal.y>=.72f && Mathf.Abs(feet.y-hit.point.y)<=.25f &&
                hit.point.y>=route.referenceHeight-2.5f && hit.point.y<=route.referenceHeight+1.5f)
            .OrderByDescending(hit=>hit.point.y).ToArray();
        height=contacts.Length>0 ? contacts[0].point.y : 0;
        return contacts.Length>0;
    }
}
