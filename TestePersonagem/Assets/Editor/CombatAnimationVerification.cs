using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Explicit two-observer, offline Play Mode check: no account, room, map or API.
[InitializeOnLoad]
public static class CombatAnimationVerification
{
    const string Active="Hbrp.Combat.Check.Active",StateKey="Hbrp.Combat.Check.State",Finished="Hbrp.Combat.Check.Finished";
    static readonly string Folder=Path.GetFullPath("../tools/combat-animation");
    [Serializable] sealed class SceneRecord {public string path;public bool loaded,active;}
    [Serializable] sealed class SavedState {public bool quit;public SceneRecord[] scenes;public string[] builds;}
    [Serializable] sealed class Report
    {
        public bool passed;
        public List<string> checks=new List<string>(),errors=new List<string>(),screenshots=new List<string>();
        public float standingHip,deathHip,rootTravel,lateAttackPhase;
    }
    static SavedState saved;
    static Report report;
    static CombatAvatarAnimation first,second;
    static Animator firstAnimator,secondAnimator;
    static Vector3 firstPosition,secondPosition;
    static long attackAt,hitAt,deathAt;
    static int stage;
    static double started,phaseStarted;
    static bool ending;
    static float phaseBeforeEcho;
    static long Now=>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    static CombatAnimationVerification()
    {
        EditorApplication.playModeStateChanged+=OnMode;
        if(SessionState.GetBool(Active,false))Application.logMessageReceived+=OnLog;
    }

    [MenuItem("Horizonte/Testes locais/Validar combate sincronizado em dois avatares")]
    public static void RunPlay()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Pare o Play antes da verificação de combate.");
        if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
        CombatAnimationImport.Verify();
        saved=new SavedState {quit=Environment.GetCommandLineArgs().Contains("--verify-combat-play"),
            scenes=EditorSceneManager.GetSceneManagerSetup().Select(x=>new SceneRecord {path=x.path,loaded=x.isLoaded,active=x.isActive}).ToArray(),
            builds=EditorBuildSettings.scenes.Select(x=>x.path+"|"+x.enabled).ToArray()};
        // Preserve unnamed scenes when invoked by a person in the Editor.
        if(!saved.quit)for(int i=0;i<saved.scenes.Length;i++)
        {
            var record=saved.scenes[i];if(!record.loaded||!string.IsNullOrEmpty(record.path))continue;
            var previous=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            record.path="Assets/Scenes/CombatPreviousScene-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+i+".unity";
            if(!EditorSceneManager.SaveScene(previous,record.path,true))throw new Exception("Não foi possível preservar a cena sem nome.");
        }
        SessionState.SetString(StateKey,JsonUtility.ToJson(saved));SessionState.SetBool(Active,true);SessionState.SetBool(Finished,false);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var generated=new List<UnityEngine.Object>();
        var create=typeof(PoliceVehicleAnimationVerification).GetMethod("CreateAvatar",BindingFlags.Static|BindingFlags.NonPublic);
        if(create==null)throw new InvalidOperationException("Current humanoid preview factory is absent");
        for(int i=0;i<2;i++)
        {
            var holder=new GameObject(i==0?"Combat observer A":"Combat observer B");holder.transform.position=new Vector3(i==0?-.85f:.85f,0,0);
            object[] arguments={holder.transform,null,generated};
            var visual=(GameObject)create.Invoke(null,arguments);visual.name="Current player avatar";
            visual.AddComponent<CombatAvatarAnimation>();
        }
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Offline combat floor";
        floor.transform.position=new Vector3(0,-.1f,0);floor.transform.localScale=new Vector3(15,.2f,15);
        floor.GetComponent<Renderer>().sharedMaterial=new Material(Shader.Find("Standard")){color=new Color(.17f,.20f,.24f)};
        var sun=new GameObject("Preview light").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.3f;sun.transform.rotation=Quaternion.Euler(35,-30,0);
        var camera=new GameObject("Main Camera",typeof(Camera)).GetComponent<Camera>();camera.tag="MainCamera";
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.06f,.09f,.13f);camera.fieldOfView=38;
        camera.transform.position=new Vector3(3.8f,2.5f,5.4f);camera.transform.LookAt(new Vector3(0,.9f,0));
        EditorApplication.delayCall+=()=>EditorApplication.isPlaying=true;
    }
    static void OnMode(PlayModeStateChange mode)
    {
        if(!SessionState.GetBool(Active,false))return;
        if(mode==PlayModeStateChange.EnteredPlayMode)
        {
            saved=JsonUtility.FromJson<SavedState>(SessionState.GetString(StateKey,""));report=new Report();ending=false;
            stage=0;started=phaseStarted=EditorApplication.timeSinceStartup;
            Application.logMessageReceived-=OnLog;Application.logMessageReceived+=OnLog;
            EditorApplication.update-=Tick;EditorApplication.update+=Tick;
        }
        else if(mode==PlayModeStateChange.EnteredEditMode)
        {
            if(!SessionState.GetBool(Finished,false))
            {report??=new Report();report.errors.Add("Combat verification was interrupted before completing.");Save();}
            EditorApplication.delayCall+=Restore;
        }
    }
    static void OnLog(string message,string stack,LogType type)
    {if(report!=null&&(type==LogType.Error||type==LogType.Exception||type==LogType.Assert))report.errors.Add(message+"\n"+stack);}
    static void Check(bool pass,string description)
    {if(!pass)throw new Exception(description);report.checks.Add(description);}
    static void Next(){stage++;phaseStarted=EditorApplication.timeSinceStartup;}
    static CombatState Attack(int revision=1)=>new CombatState {epoch="offline-verification",revision=revision,
        attackSequence=1,attackId="predicted-attack",attackAt=attackAt,health=100};
    static CombatState Hit()=>new CombatState {epoch="offline-verification",revision=2,attackSequence=1,
        attackId="predicted-attack",attackAt=attackAt,hitSequence=1,hitAt=hitAt,health=80};
    static CombatState Death()=>new CombatState {epoch="offline-verification",revision=3,attackSequence=1,
        attackId="predicted-attack",attackAt=attackAt,hitSequence=1,hitAt=hitAt,deathSequence=1,deathAt=deathAt,health=0,dead=true};
    static void Both(CombatState value){first.Apply(value,Now,0);second.Apply(value,Now,0);}
    static void Tick()
    {
        if(ending||!EditorApplication.isPlaying)return;
        try
        {
            double elapsed=EditorApplication.timeSinceStartup-phaseStarted;
            if(EditorApplication.timeSinceStartup-started>30)throw new Exception("Offline combat verification timed out");
            if(stage==0&&elapsed>.25)
            {
                var firstRoot=GameObject.Find("Combat observer A");var secondRoot=GameObject.Find("Combat observer B");
                first=firstRoot.GetComponentInChildren<CombatAvatarAnimation>();second=secondRoot.GetComponentInChildren<CombatAvatarAnimation>();
                firstAnimator=first.GetComponent<Animator>();secondAnimator=second.GetComponent<Animator>();
                first.Initialize(firstAnimator);second.Initialize(secondAnimator);
                Check(firstAnimator.isHuman&&secondAnimator.isHuman,"Both observers use the current humanoid player avatar");
                Check(!AccountSession.Instance&&!MultiplayerWorld.Instance,"The test opens no account session and connects to no server");
                var blockedPlayer=new GameObject("Inactive loan entry guard fixture");blockedPlayer.SetActive(false);
                var blocked=blockedPlayer.AddComponent<ThirdPersonPlayer>();blocked.animator=firstAnimator;
                var blockedCar=new GameObject("Inactive loan car guard fixture");blockedCar.SetActive(false);
                var loan=blockedCar.AddComponent<MissionVehicle>();
                blocked.Health=0;
                Check(!loan.Enter(blocked),"A dead player cannot hide the death animation by entering a loan vehicle");
                blocked.Health=100;blocked.CombatLocked=true;
                Check(!loan.Enter(blocked),"A player reacting to combat cannot bypass the animation through a loan vehicle");
                UnityEngine.Object.Destroy(blockedPlayer);UnityEngine.Object.Destroy(blockedCar);
                firstPosition=firstRoot.transform.position;secondPosition=secondRoot.transform.position;
                report.standingHip=firstAnimator.GetBoneTransform(HumanBodyBones.Hips).position.y;
                first.PredictAttack("predicted-attack");
                Check(first.Kind=="attack"&&first.AttackPlays==1,"The local punch starts immediately before any server confirmation");
                attackAt=Now;Both(Attack());
                Check(first.AttackPlays==1&&second.AttackPlays==1&&second.Kind=="attack","A confirmed attack appears for the observer without replaying the attacker's prediction");
                phaseBeforeEcho=first.NormalizedTime;Next();
            }
            else if(stage==1&&elapsed>.32)
            {
                Both(Attack());
                Check(first.AttackPlays==1&&second.AttackPlays==1&&first.NormalizedTime>=phaseBeforeEcho,"Repeated room snapshots never restart the combo");
                Check(first.Busy&&second.Busy,"Both observers retain the same active full-body combo");
                Capture("combat-punch.png");hitAt=Now;Both(Hit());
                Check(first.Kind=="hit"&&second.Kind=="hit"&&first.HitPlays==1&&second.HitPlays==1,"Confirmed damage interrupts the combo with Hit Reaction for both observers");
                Next();
            }
            else if(stage==2&&elapsed>.3)
            {
                Both(Hit());Check(first.HitPlays==1&&second.HitPlays==1,"A repeated damage snapshot does not replay Hit Reaction");
                Capture("combat-hit.png");deathAt=Now;Both(Death());
                Check(first.Dead&&second.Dead&&first.Kind=="death"&&second.Kind=="death","Zero health starts Stunned on both observers");
                Check(first.DeathPlays==1&&second.DeathPlays==1,"Death is played once per confirmed death sequence");Next();
            }
            else if(stage==3&&elapsed>3)
            {
                Both(Death());
                Check(first.Dead&&second.Dead&&first.Busy&&second.Busy,"The death pose stays active after the clip finishes");
                Check(first.NormalizedTime>=.98f&&second.NormalizedTime>=.98f,"Stunned holds its terminal pose instead of looping into standing");
                Check(first.DeathPlays==1&&second.DeathPlays==1,"Continuing death snapshots never restart Stunned");
                report.deathHip=firstAnimator.GetBoneTransform(HumanBodyBones.Hips).position.y;
                Check(report.deathHip<report.standingHip-.2f,"The rendered dying animation lowers the pelvis toward the floor");
                report.rootTravel=Mathf.Max(Vector3.Distance(firstPosition,first.transform.parent.position),Vector3.Distance(secondPosition,second.transform.parent.position));
                Check(report.rootTravel<.01f,"Authored animation does not teleport either authoritative player root");
                Capture("combat-death.png");Both(Hit());
                Check(first.Dead&&second.Dead&&first.Kind=="death"&&second.Kind=="death","A stale pre-death reply cannot revive or interrupt a dead player");
                Both(new CombatState {epoch="offline-verification",revision=4,attackSequence=1,hitSequence=1,deathSequence=1,
                    attackAt=attackAt,hitAt=hitAt,deathAt=deathAt,attackId="predicted-attack",health=100,dead=false});
                Next();
            }
            else if(stage==4&&elapsed>.35)
            {
                Check(!first.Dead&&!second.Dead&&!first.Busy&&!second.Busy,"A newer authoritative alive state releases the death animation");
                Check(firstAnimator.GetBoneTransform(HumanBodyBones.Hips).position.y>report.standingHip-.15f,"Alive recovery restores the normal standing pelvis");
                long late=Now-600;
                Both(new CombatState {epoch="offline-verification",revision=5,attackSequence=2,attackId="late-attack",attackAt=late,
                    hitSequence=1,hitAt=hitAt,deathSequence=1,deathAt=deathAt,health=100});
                report.lateAttackPhase=second.NormalizedTime;
                Check(second.Kind=="attack"&&second.NormalizedTime>.15f,"Delayed observers catch up to the event timestamp instead of starting an old combo from zero");
                Next();
            }
            else if(stage==5&&elapsed>2.5)
            {
                int plays=second.AttackPlays;
                Both(new CombatState {epoch="offline-verification",revision=6,attackSequence=3,attackId="finished-attack",attackAt=Now-10000,
                    hitSequence=1,hitAt=hitAt,deathSequence=1,deathAt=deathAt,health=100});
                Check(!first.Busy&&!second.Busy&&second.AttackPlays==plays,"Historical completed attacks do not replay when a player reconnects or a snapshot is delayed");
                Check(saved.builds.SequenceEqual(EditorBuildSettings.scenes.Select(x=>x.path+"|"+x.enabled)),"Offline checks preserve the project's build scene configuration");
                Check(report.errors.Count==0,"No runtime errors occurred during the two-observer animation sequence");
                report.passed=true;Finish();
            }
        }
        catch(Exception error){report.errors.Add(error.ToString());Finish();}
    }
    static void Capture(string name)
    {
        if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null||!Camera.main)return;
        var camera=Camera.main;var previous=RenderTexture.active;var target=camera.targetTexture;
        var image=new RenderTexture(1280,720,24);var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=image;camera.Render();RenderTexture.active=image;
            pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();Directory.CreateDirectory(Folder);
            File.WriteAllBytes(Path.Combine(Folder,name),pixels.EncodeToPNG());report.screenshots.Add(name);
        }
        finally{camera.targetTexture=target;RenderTexture.active=previous;image.Release();UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(pixels);}
    }
    static void Save(){Directory.CreateDirectory(Folder);File.WriteAllText(Path.Combine(Folder,"play-validation.json"),JsonUtility.ToJson(report,true));}
    static void Finish()
    {
        if(ending)return;ending=true;SessionState.SetBool(Finished,true);EditorApplication.update-=Tick;Save();
        Debug.Log("COMBAT_ANIMATION_VERIFIED passed="+report.passed+" checks="+report.checks.Count);EditorApplication.isPlaying=false;
    }
    static void Restore()
    {
        EditorApplication.update-=Tick;Application.logMessageReceived-=OnLog;
        saved??=JsonUtility.FromJson<SavedState>(SessionState.GetString(StateKey,""));
        SessionState.SetBool(Active,false);
        if(saved!=null&&!saved.quit)
        {
            var scenes=saved.scenes.Where(x=>!string.IsNullOrEmpty(x.path)).Select(x=>new SceneSetup {path=x.path,isLoaded=x.loaded,isActive=x.active}).ToArray();
            if(scenes.Length>0)EditorSceneManager.RestoreSceneManagerSetup(scenes);
        }
        if(saved?.quit==true)EditorApplication.Exit(report?.passed==true?0:1);
    }
}
