using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Explicit account-controller contract test. The action requests use the real
// AdminRequest iterator, with its existing in-memory verification transport.
// No production account, saved credential or network endpoint is used.
[InitializeOnLoad]
public static class PoliceVehicleCityVerification
{
    const string ActiveKey="Hbrp.PoliceCar.CityCheck.Active";
    const BindingFlags InstanceFlags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    const BindingFlags StaticFlags=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    const string CarId="pm-1";
    [Serializable] sealed class ActionBody {public string action,carId;}
    [Serializable] sealed class Report
    {
        public bool passed;
        public string transport="AccountSession.AdminRequest with isolated verification hooks";
        public int actionRequests,actionReplies,unexpectedAccountRequests,vaultMutations;
        public List<string> checks=new List<string>();
        public List<string> errors=new List<string>();
    }
    static Report report;
    static readonly Dictionary<FieldInfo,object> hooks=new Dictionary<FieldInfo,object>();
    static ThirdPersonPlayer player;
    static PoliceVehicleController car;
    static PoliceCarWorld cars;
    static AccountSession account;
    static MultiplayerWorld multiplayer;
    static int stage;
    static double started,stageStarted;
    static bool finishing,nextReplyLost,serverDriving;
    static string ReportPath=>Path.GetFullPath("../tools/police-car-animation/city-validation.json");

    static PoliceVehicleCityVerification(){EditorApplication.playModeStateChanged+=OnPlayState;}
    static bool Requested=>Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-police-city")>=0&&
        Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-landing")>=0;
    public static void Run()
    {
        if(!Requested)throw new InvalidOperationException("Pass --verify-police-city and --verify-landing for this isolated test.");
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play before running the city vehicle test.");
        for(int index=0;index<UnityEngine.SceneManagement.SceneManager.sceneCount;index++)
            if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(index).isDirty)
                throw new InvalidOperationException("Save open scene changes before running the city vehicle test.");
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));File.WriteAllText(ReportPath,JsonUtility.ToJson(new Report(),true));
        SessionState.SetBool(ActiveKey,true);
        PoliceVehicleAnimationVerification.OpenPreviewScene(false);
        EditorApplication.delayCall+=()=>EditorApplication.isPlaying=true;
    }
    static void OnPlayState(PlayModeStateChange mode)
    {
        if(!SessionState.GetBool(ActiveKey,false))return;
        if(mode==PlayModeStateChange.EnteredPlayMode)
        {
            report=new Report();finishing=false;nextReplyLost=serverDriving=false;stage=0;
            started=stageStarted=EditorApplication.timeSinceStartup;
            // --verify-landing also bootstraps this unrelated test. Stop it before
            // it can reach its login/UI assertions or quit this verification run.
            foreach(var other in UnityEngine.Object.FindObjectsByType<CharacterFlowVerification>(FindObjectsSortMode.None))
            {other.StopAllCoroutines();UnityEngine.Object.DestroyImmediate(other.gameObject);}
            Application.logMessageReceived+=OnLog;EditorApplication.update+=Tick;
        }
        else if(mode==PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update-=Tick;Application.logMessageReceived-=OnLog;
            SessionState.SetBool(ActiveKey,false);
            if(!File.Exists(ReportPath))return;
            var saved=JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath));
            if(Requested)EditorApplication.delayCall+=()=>EditorApplication.Exit(saved!=null&&saved.passed?0:1);
        }
    }
    static void OnLog(string message,string trace,LogType type)
    {if(report!=null&&(type==LogType.Error||type==LogType.Exception||type==LogType.Assert))report.errors.Add(message+"\n"+trace);}
    static void Check(bool pass,string message)
    {if(!pass)throw new InvalidOperationException(message);report.checks.Add(message);}
    static void Next(){stage++;stageStarted=EditorApplication.timeSinceStartup;}
    static void Set(object owner,string name,object value)
    {
        Type type=owner as Type??owner.GetType();object target=owner is Type?null:owner;
        var property=type.GetProperty(name,target==null?StaticFlags:InstanceFlags);
        if(property!=null){property.GetSetMethod(true).Invoke(target,new[]{value});return;}
        var field=type.GetField(name,target==null?StaticFlags:InstanceFlags);
        if(field==null)throw new MissingFieldException(type.FullName,name);
        field.SetValue(target,value);
    }
    static void Hook(Type type,string name,object value)
    {
        var field=type.GetField(name,StaticFlags);
        if(field==null)throw new MissingFieldException(type.FullName,name);
        if(!hooks.ContainsKey(field))hooks.Add(field,field.GetValue(null));
        field.SetValue(null,value);
    }
    static IEnumerator UnexpectedAccountRequest(string method,string path,string json,bool authenticated,Action<ApiReply,string> done)
    {
        report.unexpectedAccountRequests++;done(new ApiReply{code="UNEXPECTED_FIXTURE_REQUEST"},"Unexpected account request: "+path);yield break;
    }
    static IEnumerator ActionRequest(string method,string path,string json,Action<AdminReply> done)
    {
        Check(method=="POST"&&path=="/v1/police/car","Vehicle action uses the authenticated police endpoint");
        Check(AccountSession.Authenticated&&AccountSession.HasCharacter&&!car.LocalPreview,"Vehicle action requires the authenticated fixture and normal city mode");
        var body=JsonUtility.FromJson<ActionBody>(json);
        Check(body!=null&&body.carId==CarId&&(body.action=="enter"||body.action=="exit"),"Vehicle action carries the owned server car ID");
        report.actionRequests++;bool lost=nextReplyLost;nextReplyLost=false;
        serverDriving=body.action=="enter";
        yield return new WaitForSecondsRealtime(1);
        report.actionReplies++;
        done(lost?new AdminReply{code="CONNECTION_LOST",error="Simulated lost action response"}:
            new AdminReply{driving=serverDriving,x=player.transform.position.x,y=player.transform.position.y,z=player.transform.position.z});
    }
    static void Fixture()
    {
        // Suspend unrelated service loops before authentication/duty can wake
        // them. AdminRequest remains strict: only /v1/police/car is mocked here.
        PauseServices<PoliceRadio>();PauseServices<VoiceChat>();
        PauseServices<Hbrp.Realtime.VivoxVoice>();PauseServices<Hbrp.Realtime.RealtimeMovement>();
        PauseServices<AdminPlayerPanel>();
        multiplayer=MultiplayerWorld.Instance;
        // --verify-landing suppresses automatic multiplayer creation; install the
        // actual guard component, then keep its network loop disabled in this fixture.
        if(!multiplayer){multiplayer=player.gameObject.AddComponent<MultiplayerWorld>();Set(multiplayer,"player",player);}
        multiplayer.StopAllCoroutines();multiplayer.enabled=false;
        Set(multiplayer,"PoliceVehicleAvailable",true);
        var preview=UnityEngine.Object.FindAnyObjectByType<PoliceVehiclePreview>();if(preview)preview.enabled=false;
        Hook(typeof(AccountSession),"VerificationTransport",(Func<string,string,string,bool,Action<ApiReply,string>,IEnumerator>)UnexpectedAccountRequest);
        Hook(typeof(AccountSession),"AdminVerificationTransport",(Func<string,string,string,Action<AdminReply>,IEnumerator>)ActionRequest);
        var vault=typeof(AccountSession).Assembly.GetType("SessionVault",true);
        Hook(vault,"TestRead",(Func<string>)(()=>null));
        Hook(vault,"TestWrite",(Action<string>)(_=>report.vaultMutations++));
        Hook(vault,"TestClear",(Action)(()=>report.vaultMutations++));
        Check(!AccountSession.Instance,"Preview setup has not opened an account session");
        account=AccountSession.Ensure();account.enabled=false;
        Set(account,"token",new string('V',43));
        Set(account,"State",new ApiPlayerState{uid="1",characterId="police-city-fixture",accountId="local-fixture",firstName="Oficial",model="bybeyby",world="centro_teste",accountRole="admin",adminLevel=1,organization=new ApiOrganization{code="pm"}});
        Set(account,"AuthVisible",false);Set(account,"ReadyToEnterCity",true);
        // Account callbacks on focus/quit must not start a checkpoint save.
        Set(account,"player",null);Set(account,"allowQuit",true);Set(account,"nextSave",float.PositiveInfinity);
        Set(typeof(AdminPlayerPanel),"PoliceDuty",true);
        car.LocalPreview=false;Set(car,"Id",CarId);
        cars=PoliceCarWorld.Instance;
        var visuals=(IDictionary)typeof(PoliceCarWorld).GetField("visuals",InstanceFlags).GetValue(cars);
        object visual=visuals["previa-viatura"];visuals.Remove("previa-viatura");visuals.Add(CarId,visual);
        Snapshot(false,PoliceVehicleController.StateRevision);
        Check(AccountSession.HasCharacter&&!AccountSession.InputBlocked&&!car.LocalPreview,"Account fixture enters the normal authenticated vehicle path without saved access");
    }
    static void PauseServices<T>() where T:MonoBehaviour
    {
        foreach(var service in UnityEngine.Object.FindObjectsByType<T>(FindObjectsSortMode.None))
        {service.StopAllCoroutines();service.enabled=false;}
    }
    static void Snapshot(bool occupied,int revision)
    {
        var position=car.transform.position;
        cars.Apply(new[]{new PoliceCarState{id=CarId,ownerUid="1",driverUid=occupied?"1":"",x=position.x,y=position.y,z=position.z,yaw=car.transform.eulerAngles.y}},
            occupied?CarId:"",player.transform.position,revision);
    }
    static void StaleEmptySnapshot(int revision)
    {
        cars.Apply(Array.Empty<PoliceCarState>(),"",player.transform.position,revision);
        Check(car&&car.gameObject.activeInHierarchy,"Snapshot started before the action cannot clear the vehicle");
    }
    static void Standing()
    {
        Check(!car.Occupied&&!car.Busy&&!PoliceVehicleController.Active,"Confirmed exit returns ownership and releases vehicle controls");
        Check(player.GetComponent<CharacterController>().enabled&&player.animator.gameObject.activeSelf&&!player.CombatLocked,"Confirmed exit restores the original standing controller and avatar");
        Check(Vector3.Distance(player.transform.position,car.transform.position)>1.5f,"Exit finishes outside the car cabin");
        Check(car.GetComponent<BoxCollider>().enabled&&!car.GetComponent<CharacterController>().enabled,"Released vehicle restores its parked collider");
    }
    static void Tick()
    {
        if(finishing||!EditorApplication.isPlaying)return;
        try
        {
            if(report.errors.Count>0)throw new InvalidOperationException("Runtime errors occurred during city vehicle verification.");
            if(EditorApplication.timeSinceStartup-started>60)throw new TimeoutException("City vehicle verification timed out at stage "+stage);
            double elapsed=EditorApplication.timeSinceStartup-stageStarted;
            if(stage==0)
            {
                player=UnityEngine.Object.FindAnyObjectByType<ThirdPersonPlayer>();
                car=UnityEngine.Object.FindObjectsByType<PoliceVehicleController>(FindObjectsSortMode.None).FirstOrDefault(x=>x.LocalPreview);
                if(!player||!car||elapsed<1)return;
                Fixture();int before=PoliceVehicleController.StateRevision;car.RequestEnter(player);
                Check(car.Busy&&!car.Occupied,"Authenticated entry waits for server confirmation");StaleEmptySnapshot(before);Next();
            }
            else if(stage==1&&elapsed>.25)
            {
                Check(report.actionRequests==1&&report.actionReplies==0,"Entry response is delayed while room sync continues");
                Snapshot(true,PoliceVehicleController.StateRevision);
                Check(car.Occupied&&PoliceVehicleController.Active==car,"Room ownership confirms entry before its action reply");Next();
            }
            else if(stage==2&&elapsed>3.3&&!car.Busy)
            {
                Check(car.Occupied&&report.actionReplies==1,"Late entry reply cannot reset an already confirmed seat");
                int before=PoliceVehicleController.StateRevision;car.RequestExit();
                Check(car.Busy&&car.Occupied&&!PoliceVehicleController.Driving&&PoliceVehicleController.DrivingCarId==null,"Pending exit sends neutral frames without the released police car ID");
                Check(Vector3.Distance(player.transform.position,car.transform.position)>1.5f,"Pending neutral frames carry the checked external foot position");
                StaleEmptySnapshot(before);Snapshot(true,PoliceVehicleController.StateRevision);
                Check(car.Busy&&car.Occupied,"Reserved-seat snapshot during pending exit does not abort the transition");Next();
            }
            else if(stage==3&&elapsed>.25)
            {Snapshot(false,PoliceVehicleController.StateRevision);Check(car.Busy&&car.Occupied,"Room release starts the exit animation before its delayed action reply");Next();}
            else if(stage==4&&elapsed>3&&!car.Busy)
            {Standing();Check(report.actionReplies==2,"Delayed exit reply completed without clearing the scene");nextReplyLost=true;car.RequestEnter(player);Next();}
            else if(stage==5&&elapsed>1.3)
            {
                Check(report.actionReplies==3&&!car.Occupied,"Lost entry response leaves controls available for room reconciliation");
                Snapshot(true,PoliceVehicleController.StateRevision);Check(car.Occupied,"Confirmed room ownership recovers a lost entry response");Next();
            }
            else if(stage==6&&elapsed>3.3&&!car.Busy)
            {nextReplyLost=true;car.RequestExit();Next();}
            else if(stage==7&&elapsed>1.3)
            {
                Check(report.actionReplies==4&&car.Busy&&car.Occupied&&!PoliceVehicleController.Driving,"Lost exit response keeps neutral frames until fresh room confirmation");
                Snapshot(false,PoliceVehicleController.StateRevision);Next();
            }
            else if(stage==8&&elapsed>3&&!car.Busy)
            {
                Standing();Check(!serverDriving&&report.actionRequests==4&&report.actionReplies==4,"Both delayed and lost-response action cycles return the server-owned seat");
                Check(report.unexpectedAccountRequests==0&&report.vaultMutations==0,"Fixture performs no autosave or saved-credential mutation");
                report.passed=true;Finish();
            }
        }
        catch(Exception error){report.errors.Add(error.ToString());Finish();}
    }
    static void Finish()
    {
        if(finishing)return;finishing=true;EditorApplication.update-=Tick;
        if(account){Set(account,"player",null);Set(account,"token",null);Set(account,"allowQuit",true);account.StopAllCoroutines();}
        if(car)car.Abort();Set(typeof(AdminPlayerPanel),"PoliceDuty",false);
        foreach(var saved in hooks)saved.Key.SetValue(null,saved.Value);hooks.Clear();
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));File.WriteAllText(ReportPath,JsonUtility.ToJson(report,true));
        Debug.Log("POLICE_VEHICLE_CITY_VERIFIED passed="+report.passed+" checks="+report.checks.Count);
        EditorApplication.isPlaying=false;
    }
}
