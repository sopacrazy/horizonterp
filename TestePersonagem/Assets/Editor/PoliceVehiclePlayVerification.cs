using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

// Explicit local-only Play Mode check. Never opens a login or a production API.
[InitializeOnLoad]
public static class PoliceVehiclePlayVerification
{
    const string ActiveKey="Hbrp.PoliceCar.PlayCheck.Active",StateKey="Hbrp.PoliceCar.PlayCheck.State",FinishedKey="Hbrp.PoliceCar.PlayCheck.Finished";
    [Serializable] sealed class SceneRecord {public string path;public bool loaded,active;}
    [Serializable] sealed class State {public bool quitEditor;public SceneRecord[] scenes;public string[] buildScenes;}
    [Serializable] sealed class Report {public bool passed;public float travel;public List<string> checks=new List<string>();public List<string> errors=new List<string>();}
    static Report report;
    static State state;
    static int stage;
    static double started,stageStarted,requestAt;
    static Vector3 driveStart,parkedAfterExit;
    static ThirdPersonPlayer player;
    static PoliceVehicleController car;
    static bool finishing;

    static PoliceVehiclePlayVerification()
    {
        EditorApplication.playModeStateChanged+=OnPlayState;
        EditorApplication.update+=CheckRequest;
        if(SessionState.GetBool(ActiveKey,false))Application.logMessageReceived+=OnLog;
    }
    static void CheckRequest()
    {
        if(EditorApplication.timeSinceStartup<requestAt)return;requestAt=EditorApplication.timeSinceStartup+1;
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||SessionState.GetBool(ActiveKey,false))return;
        string request=Path.GetFullPath("../tools/police-car-animation/run-play-verification.request");
        if(!File.Exists(request))return;
        File.Delete(request);EditorApplication.delayCall+=RunPlay;
    }
    [MenuItem("Horizonte/Testes locais/Validar entrada, direção e saída da viatura")]
    public static void RunAll()
    {
        try{PoliceVehicleAnimationVerification.Run();RunPlay();}
        catch(Exception error)
        {
            Debug.LogException(error);
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-police-car-play")>=0)EditorApplication.Exit(1);
            else throw;
        }
    }
    public static void RunPlay()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode){Debug.LogWarning("Pare o Play antes de iniciar esta verificação.");return;}
        if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
        state=new State
        {
            quitEditor=Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-police-car-play")>=0,
            scenes=EditorSceneManager.GetSceneManagerSetup().Select(x=>new SceneRecord{path=x.path,loaded=x.isLoaded,active=x.isActive}).ToArray(),
            buildScenes=EditorBuildSettings.scenes.Select(x=>x.path+"|"+x.enabled).ToArray()
        };
        // An untitled scene cannot be restored by SceneManagerSetup. Preserve a
        // copy instead of discarding it if this check is invoked from the menu.
        if(!state.quitEditor)
        {
            for(int index=0;index<state.scenes.Length;index++)
            {
                var record=state.scenes[index];if(!string.IsNullOrEmpty(record.path)||!record.loaded)continue;
                var scene=UnityEngine.SceneManagement.SceneManager.GetSceneAt(index);
                string backup="Assets/Scenes/PoliceVehiclePreviousScene-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+index+".unity";
                if(!EditorSceneManager.SaveScene(scene,backup,true))throw new Exception("Não foi possível preservar a cena sem nome.");
                record.path=backup;
            }
        }
        SessionState.SetString(StateKey,JsonUtility.ToJson(state));SessionState.SetBool(ActiveKey,true);
        SessionState.SetBool(FinishedKey,false);
        finishing=false;report=new Report();
        PoliceVehicleAnimationVerification.OpenPreviewScene(false);
        EditorApplication.delayCall+=()=>EditorApplication.isPlaying=true;
    }
    static void OnPlayState(PlayModeStateChange mode)
    {
        if(!SessionState.GetBool(ActiveKey,false))return;
        if(mode==PlayModeStateChange.EnteredPlayMode)
        {
            state=JsonUtility.FromJson<State>(SessionState.GetString(StateKey,""));report=new Report();finishing=false;
            stage=0;started=stageStarted=EditorApplication.timeSinceStartup;
            Application.logMessageReceived-=OnLog;Application.logMessageReceived+=OnLog;
            EditorApplication.update-=Tick;EditorApplication.update+=Tick;
        }
        else if(mode==PlayModeStateChange.EnteredEditMode)
        {
            finishing=SessionState.GetBool(FinishedKey,false);
            if(!finishing)
            {
                report??=new Report();report.errors.Add("A verificação foi interrompida antes de concluir.");SaveReport();
            }
            EditorApplication.delayCall+=RestoreEditor;
        }
    }
    static void OnLog(string message,string stack,LogType type)
    {if(report!=null&&(type==LogType.Error||type==LogType.Exception||type==LogType.Assert))report.errors.Add(message+"\n"+stack);}
    static void Check(bool pass,string text)
    {if(!pass)throw new Exception(text);report.checks.Add(text);}
    static void Next(){stage++;stageStarted=EditorApplication.timeSinceStartup;}
    static void Pedal(string name,bool down)
    {
        var button=GameHud.Instance?.Root.Q<VisualElement>(name);
        Check(button!=null,"Vehicle pedal exists: "+name);
        if(down){using(var evt=PointerDownEvent.GetPooled()){evt.target=button;button.SendEvent(evt);}}
        else{using(var evt=PointerUpEvent.GetPooled()){evt.target=button;button.SendEvent(evt);}}
    }
    static void Input(Vector2? value)
    {
        var field=typeof(PoliceVehicleController).GetField("PreviewInput",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static);
        if(field!=null){field.SetValue(field.IsStatic?null:car,value);return;}
        var property=typeof(PoliceVehicleController).GetProperty("PreviewInput",BindingFlags.Public|BindingFlags.Instance);
        if(property==null)throw new Exception("PoliceVehicleController.PreviewInput is required for this explicit local test");
        property.SetValue(car,value);
    }
    static void Tick()
    {
        if(finishing||!EditorApplication.isPlaying)return;
        try
        {
            double elapsed=EditorApplication.timeSinceStartup-stageStarted;
            if(EditorApplication.timeSinceStartup-started>45)throw new Exception("Timed out waiting for the local vehicle sequence");
            if(stage==0)
            {
                player=UnityEngine.Object.FindAnyObjectByType<ThirdPersonPlayer>();
                car=UnityEngine.Object.FindObjectsByType<PoliceVehicleController>(FindObjectsSortMode.None).FirstOrDefault(x=>x.LocalPreview);
                if(!player||!car||elapsed<1)return;
                Check(!AccountSession.Instance,"Offline preview starts without opening any account session");
                Check(player.GetComponent<CharacterController>().enabled&&player.animator.gameObject.activeSelf,"Player begins standing with normal controls and visible avatar");
                car.RequestEnter(player);Next();
            }
            else if(stage==1&&elapsed>3&&!car.Busy)
            {
                Check(car.Occupied&&!car.Busy&&PoliceVehicleController.Active==car,"Entry finishes with the local player owning the driver's seat");
                Check(!player.GetComponent<CharacterController>().enabled&&!player.animator.gameObject.activeSelf,"Standing capsule and duplicate standing avatar are hidden while seated");
                Check(car.GetComponentsInChildren<PoliceSeatAvatar>().Any(x=>x.Seated),"A full-size driver remains visible inside the car");
                Check(car.GetComponent<PoliceCarRig>().DoorOpenAmount(true)<.01f,"Door closes after the entry animation");
                Check(GameHud.Instance.Root.Q("action-jump").resolvedStyle.display==DisplayStyle.None,"Vehicle HUD hides the on-foot jump control");
                Capture("play-seated.png");driveStart=car.transform.position;Input(new Vector2(.3f,0));Pedal("vehicle-accelerate",true);Next();
            }
            else if(stage==2&&elapsed>2)
            {
                report.travel=Vector3.Distance(car.transform.position,driveStart);
                Check(report.travel>1.5f&&car.Speed>1,"The occupied police car drives forward under local controls");
                Pedal("vehicle-accelerate",false);Pedal("vehicle-brake",true);Input(Vector2.zero);Next();
            }
            else if(stage==3&&elapsed>2)
            {
                Check(Mathf.Abs(car.Speed)<.1f,"Police car stops before the door is opened");
                Pedal("vehicle-brake",false);
                car.RequestExit();Next();
            }
            else if(stage==4&&elapsed>3&&!car.Busy)
            {
                Check(!car.Occupied&&!car.Busy&&!PoliceVehicleController.Active,"Exit releases the driver's seat and active vehicle controls");
                Check(player.GetComponent<CharacterController>().enabled&&player.animator.gameObject.activeSelf,"Exit restores the player's collider and original visible avatar");
                Check(!UnityEngine.Object.FindAnyObjectByType<ThirdPersonCamera>().VehicleFollowActive,"Camera returns to following the player after exit");
                Check(GameHud.Instance.Root.Q("action-jump").resolvedStyle.display!=DisplayStyle.None&&!VehicleHud.PedalsHeld,"Exit restores foot HUD and releases the vehicle pedals");
                Check(car.GetComponent<PoliceCarRig>().DoorOpenAmount(true)<.01f,"Door closes after the exit animation");
                Check(!AccountSession.Instance,"Vehicle test never creates an account session or touches saved login");
                Check(state.buildScenes.SequenceEqual(EditorBuildSettings.scenes.Select(x=>x.path+"|"+x.enabled)),"Build scene configuration remains unchanged");
                Capture("play-exited.png");Input(null);
                parkedAfterExit=car.transform.position;Next();
            }
            else if(stage==5&&elapsed>.75)
            {
                Check(Vector3.Distance(car.transform.position,parkedAfterExit)<.1f,"The parked preview car remains at the driven destination after exit");
                var rendered=PoliceAnimationPoseProbe.Data;
                Check(rendered!=null&&rendered.maxEntryKneeFlex>70&&rendered.maxExitKneeFlex>70,"Rendered entry and exit bend the knees instead of playing standing locomotion");
                Check(rendered.maxHipOffset<.02f,"Later pose writers do not overwrite the naturally sampled car clip");
                Check(rendered.maxOutsideFootHeight<.2f,"At least one foot supports the actor outside the car instead of floating beside the door");
                Check(rendered.maxSeatedHeading<10,"The entry clip ends facing toward the hood rather than sideways through the door");
                if(report.errors.Count>0)throw new Exception("Runtime errors occurred during the local preview; see the report");
                // Repeat in the rotated car, now approaching with the player's
                // exit heading. This catches stale clip time/offset and yaw state.
                car.RequestEnter(player);Next();
            }
            else if(stage==6&&elapsed>3&&!car.Busy)
            {
                Check(car.Occupied,"A second entry works after steering the car and leaving it");
                car.RequestExit();Next();
            }
            else if(stage==7&&elapsed>3&&!car.Busy)
            {
                Check(!car.Occupied&&player.GetComponent<CharacterController>().enabled&&player.animator.gameObject.activeSelf,"Repeated exit restores the original avatar and controls without accumulating clip offsets");
                if(report.errors.Count>0)throw new Exception("Runtime errors occurred during repeated entry/exit; see the report");
                report.passed=true;Finish();
            }
        }
        catch(Exception error){report.errors.Add(error.ToString());Finish();}
    }
    static void Capture(string name)
    {
        if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null||!Camera.main)return;
        string folder=Path.GetFullPath("../tools/police-car-animation");Directory.CreateDirectory(folder);
        var camera=Camera.main;var old=camera.targetTexture;var previous=RenderTexture.active;
        var image=new RenderTexture(1280,720,24);var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=image;camera.Render();RenderTexture.active=image;
            pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();
            File.WriteAllBytes(Path.Combine(folder,name),pixels.EncodeToPNG());
        }
        finally
        {camera.targetTexture=old;RenderTexture.active=previous;image.Release();UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(pixels);}
    }
    static void SaveReport()
    {
        string folder=Path.GetFullPath("../tools/police-car-animation");Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"play-validation.json"),JsonUtility.ToJson(report,true));
    }
    static void Finish()
    {
        if(finishing)return;finishing=true;SessionState.SetBool(FinishedKey,true);EditorApplication.update-=Tick;
        if(car){try{Input(null);}catch(Exception){}}
        SaveReport();Debug.Log("POLICE_VEHICLE_PLAY_VERIFIED passed="+report.passed+" checks="+report.checks.Count);
        EditorApplication.isPlaying=false;
    }
    static void RestoreEditor()
    {
        EditorApplication.update-=Tick;Application.logMessageReceived-=OnLog;
        state??=JsonUtility.FromJson<State>(SessionState.GetString(StateKey,""));
        string reportPath=Path.GetFullPath("../tools/police-car-animation/play-validation.json");
        if(report==null&&File.Exists(reportPath))report=JsonUtility.FromJson<Report>(File.ReadAllText(reportPath));
        SessionState.SetBool(ActiveKey,false);
        bool passed=report!=null&&report.passed;
        if(state!=null&&state.quitEditor){EditorApplication.Exit(passed?0:1);return;}
        if(state?.scenes!=null&&state.scenes.Length>0)
        {
            var setup=state.scenes.Where(x=>!string.IsNullOrEmpty(x.path)).Select(x=>new SceneSetup{path=x.path,isLoaded=x.loaded,isActive=x.active}).ToArray();
            if(setup.Length>0)EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
    }
}
