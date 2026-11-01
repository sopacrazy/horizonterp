using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Offline responsiveness fixture. No account, API, world or production save is opened.
[InitializeOnLoad]
public static class CombatResponsivenessVerification
{
    const string Active="Hbrp.Combat.Response.Active",Saved="Hbrp.Combat.Response.Saved",Finished="Hbrp.Combat.Response.Finished";
    static readonly string Folder=Path.GetFullPath("../tools/combat-animation");
    [Serializable] sealed class SceneRecord {public string path;public bool loaded,active;}
    [Serializable] sealed class Setup {public bool quit;public SceneRecord[] scenes;public string[] builds;}
    [Serializable] sealed class Report
    {
        public bool passed;
        public List<string> checks=new List<string>(),errors=new List<string>(),screenshots=new List<string>();
        public float attackHandTravel,reactionHeadTravel,phaseBeforeConfirmation,phaseAfterConfirmation;
        public float predictionStartedUnityTime,onsetUnityElapsed,onsetEditorElapsed,onsetLayerWeight,onsetAnimatorPhase;
        public int predictionStartedFrame,onsetFrames;
        public string onsetKind;
        public bool onsetPredictionPending;
        public List<OnsetSample> onsetSamples=new List<OnsetSample>();
    }
    [Serializable] sealed class OnsetSample
    {
        public int frame;
        public float unityElapsed,layerWeight,animatorPhase,clipSeconds;
        public double editorElapsed;
        public string kind;
        public bool predictionPending;
        public Vector3 head;
    }
    static Setup setup;
    static Report report;
    static CombatAvatarAnimation attacker,victim;
    static Animator attackerAnimator,victimAnimator;
    static ThirdPersonPlayer healthOwner;
    static FighterCombo fighter;
    static Vector3 handBefore,headBefore;
    static CombatState lastHit;
    static int stage,playsBefore;
    static double started,phaseStarted;
    static bool ending;
    static float predictionStartedUnityTime;
    static int predictionStartedFrame,lastSampleFrame;
    static double predictionStartedEditorTime;
    static long Now=>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    static CombatResponsivenessVerification(){EditorApplication.playModeStateChanged+=OnMode;}

    [MenuItem("Horizonte/Testes locais/Validar resposta imediata de soco e dano")]
    public static void RunPlay()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Pare o Play antes de verificar o combate.");
        if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
        CombatAnimationImport.Verify();
        setup=new Setup {quit=Environment.GetCommandLineArgs().Contains("--verify-combat-response"),
            scenes=EditorSceneManager.GetSceneManagerSetup().Select(x=>new SceneRecord {path=x.path,loaded=x.isLoaded,active=x.isActive}).ToArray(),
            builds=EditorBuildSettings.scenes.Select(x=>x.path+"|"+x.enabled).ToArray()};
        if(!setup.quit&&setup.scenes.Any(x=>x.loaded&&string.IsNullOrEmpty(x.path)))
            throw new InvalidOperationException("Salve a cena sem nome antes da verificação.");
        SessionState.SetString(Saved,JsonUtility.ToJson(setup));SessionState.SetBool(Active,true);SessionState.SetBool(Finished,false);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var generated=new List<UnityEngine.Object>();
        var factory=typeof(PoliceVehicleAnimationVerification).GetMethod("CreateAvatar",BindingFlags.Static|BindingFlags.NonPublic);
        if(factory==null)throw new InvalidOperationException("Current humanoid preview factory is absent");
        for(int i=0;i<2;i++)
        {
            var holder=new GameObject(i==0?"Fast attack observer":"Fast damage observer");holder.transform.position=new Vector3(i==0?-.85f:.85f,0,0);
            var visual=(GameObject)factory.Invoke(null,new object[]{holder.transform,null,generated});
            visual.name="Current player avatar";visual.AddComponent<CombatAvatarAnimation>();
        }
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Offline test floor";
        floor.transform.position=new Vector3(0,-.1f,0);floor.transform.localScale=new Vector3(15,.2f,15);
        floor.GetComponent<Renderer>().sharedMaterial=new Material(Shader.Find("Standard")){color=new Color(.17f,.20f,.24f)};
        var light=new GameObject("Preview light").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.3f;light.transform.rotation=Quaternion.Euler(35,-30,0);
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
            setup=JsonUtility.FromJson<Setup>(SessionState.GetString(Saved,""));report=new Report();ending=false;stage=0;
            started=phaseStarted=EditorApplication.timeSinceStartup;
            Application.logMessageReceived-=OnLog;Application.logMessageReceived+=OnLog;
            EditorApplication.update-=Tick;EditorApplication.update+=Tick;
        }
        else if(mode==PlayModeStateChange.EnteredEditMode)
        {
            if(!SessionState.GetBool(Finished,false)){report??=new Report();report.errors.Add("Responsiveness verification was interrupted.");Save();}
            EditorApplication.delayCall+=Restore;
        }
    }
    static void OnLog(string message,string stack,LogType type)
    {if(report!=null&&(type==LogType.Error||type==LogType.Exception||type==LogType.Assert))report.errors.Add(message+"\n"+stack);}
    static void Check(bool condition,string description)
    {if(!condition)throw new Exception(description);report.checks.Add(description);}
    static void Next(){stage++;phaseStarted=EditorApplication.timeSinceStartup;}
    static CombatState Baseline()=>new CombatState {epoch="response-fixture",revision=0,health=100};
    static CombatState Hit(int revision,int sequence,string attackId,float health,long at)=>new CombatState {
        epoch="response-fixture",revision=revision,hitSequence=sequence,hitAttackId=attackId,hitAt=at,health=health};
    static void Apply(CombatState state)=>fighter.ApplyState(state,Now,0);
    static void Tick()
    {
        if(ending||!EditorApplication.isPlaying)return;
        try
        {
            double elapsed=EditorApplication.timeSinceStartup-phaseStarted;
            if(EditorApplication.timeSinceStartup-started>35)throw new Exception("Responsiveness test timed out");
            if(stage==0&&elapsed>.25)
            {
                attacker=GameObject.Find("Fast attack observer").GetComponentInChildren<CombatAvatarAnimation>();
                victim=GameObject.Find("Fast damage observer").GetComponentInChildren<CombatAvatarAnimation>();
                attackerAnimator=attacker.GetComponent<Animator>();victimAnimator=victim.GetComponent<Animator>();
                attacker.Initialize(attackerAnimator);victim.Initialize(victimAnimator);
                Check(attacker.Ready&&victim.Ready,"Both current humanoids load the combat clips");
                Check(!AccountSession.Instance&&!MultiplayerWorld.Instance,"The fixture uses no account, server or real player data");
                var owner=new GameObject("Inactive health fixture");owner.SetActive(false);
                healthOwner=owner.AddComponent<ThirdPersonPlayer>();healthOwner.animator=victimAnimator;healthOwner.Health=100;
                fighter=owner.AddComponent<FighterCombo>();
                attacker.Apply(Baseline(),Now,0);Apply(Baseline());
                handBefore=attackerAnimator.GetBoneTransform(HumanBodyBones.RightHand).position;
                headBefore=victimAnimator.GetBoneTransform(HumanBodyBones.Head).position;
                attacker.PredictAttack("predicted-a");
                Check(attacker.Kind=="attack"&&attacker.AttackPlays==1,"Punch starts on the initiating frame without waiting for HTTP");
                Check(attacker.ClipStartSeconds>=.49f&&attacker.CurrentClipSeconds>=.49f,"Punch Combo skips its defensive lead-in and begins at the authored strike at 0.50 seconds");
                Check(attacker.AttackDuration>=2.19f,"Removing the defensive lead-in preserves the server-aligned combo cooldown");
                Check(attackerAnimator.GetLayerWeight(attackerAnimator.GetLayerIndex("Combat"))>.99f,"The first punch pose is immediately visible at full layer weight");
                Check(victim.PredictHit("predicted-a"),"A nearby target accepts the local visual hit prediction");
                predictionStartedUnityTime=Time.unscaledTime;predictionStartedFrame=Time.frameCount;
                predictionStartedEditorTime=EditorApplication.timeSinceStartup;lastSampleFrame=-1;
                report.predictionStartedUnityTime=predictionStartedUnityTime;report.predictionStartedFrame=predictionStartedFrame;
                Check(victim.Kind=="hit"&&victim.HasPredictedHit&&victim.HitPlays==1,"Damage reaction starts on the attack frame before confirmation");
                Check(victim.ClipStartSeconds>=.19f&&victim.CurrentClipSeconds>=.19f,"Hit Reaction skips its neutral lead-in and begins directly in recoil");
                Check(healthOwner.Health==100&&!victim.Dead,"Visual prediction cannot reduce health or predict death");
                Apply(Baseline());
                Check(victim.Kind=="hit"&&victim.HasPredictedHit&&victim.HitPlays==1,"An ordinary in-flight baseline snapshot does not erase the immediate response");
                SampleOnset();
                Next();
            }
            else if(stage==1)
            {
                SampleOnset();
                if(elapsed<=.18)return;
                report.attackHandTravel=Vector3.Distance(handBefore,attackerAnimator.GetBoneTransform(HumanBodyBones.RightHand).position);
                report.reactionHeadTravel=Vector3.Distance(headBefore,victimAnimator.GetBoneTransform(HumanBodyBones.Head).position);
                report.onsetUnityElapsed=Time.unscaledTime-predictionStartedUnityTime;
                report.onsetEditorElapsed=(float)(EditorApplication.timeSinceStartup-predictionStartedEditorTime);
                report.onsetFrames=Time.frameCount-predictionStartedFrame;
                report.onsetKind=victim.Kind;report.onsetPredictionPending=victim.HasPredictedHit;
                report.onsetLayerWeight=victimAnimator.GetLayerWeight(victimAnimator.GetLayerIndex("Combat"));
                report.onsetAnimatorPhase=victimAnimator.GetCurrentAnimatorStateInfo(victimAnimator.GetLayerIndex("Combat")).normalizedTime;
                Debug.Log("COMBAT_RESPONSE_ONSET kind="+report.onsetKind+" predicted="+report.onsetPredictionPending+
                    " unityElapsed="+report.onsetUnityElapsed+" editorElapsed="+report.onsetEditorElapsed+
                    " frames="+report.onsetFrames+" weight="+report.onsetLayerWeight+" phase="+report.onsetAnimatorPhase+
                    " headTravel="+report.reactionHeadTravel);
                Capture("response-onset-diagnostic.png");
                Save();
                Check(report.attackHandTravel>.03f,"The rendered attack hand moves within 180 milliseconds");
                Check(report.reactionHeadTravel>.02f,"The rendered target reacts within 180 milliseconds without a reply");
                report.phaseBeforeConfirmation=victim.NormalizedTime;
                lastHit=Hit(1,1,"predicted-a",80,Now-180);Apply(lastHit);
                report.phaseAfterConfirmation=victim.NormalizedTime;
                Check(!victim.HasPredictedHit&&victim.HitPlays==1&&victim.Kind=="hit","Matching server confirmation accepts the predicted hit without replaying it");
                Check(report.phaseAfterConfirmation>=report.phaseBeforeConfirmation-.01f,"Confirmation does not rewind the visible reaction");
                Check(healthOwner.Health==80,"Only the confirmed server state updates health");
                Apply(lastHit);Check(victim.HitPlays==1,"Repeated confirmation cannot duplicate the reaction");
                Capture("response-immediate.png");Next();
            }
            else if(stage==2&&elapsed>1.9)
            {
                Check(!victim.Busy,"The confirmed response returns to locomotion after the authored clip");
                Check(victim.PredictHit("rejected-b"),"A second prediction can start after the first reaction");
                victim.CancelPredictedHit("wrong-id");
                Check(victim.HasPredictedHit&&victim.Kind=="hit","Cancellation for another attack cannot erase a pending prediction");
                victim.CancelPredictedHit("rejected-b");
                Check(!victim.HasPredictedHit&&!victim.Busy&&healthOwner.Health==80,"A rejected or missed attack rolls back its visual response without changing health");
                Check(victim.PredictHit("speculative-c"),"A fresh prediction starts after rollback");playsBefore=victim.HitPlays;
                lastHit=Hit(2,2,"unrelated-confirmed",60,Now);Apply(lastHit);
                Check(!victim.HasPredictedHit&&victim.Kind=="hit"&&victim.HitPlays==playsBefore+1&&healthOwner.Health==60,"An unrelated authoritative hit supersedes speculative feedback");
                victim.CancelPredictedHit("speculative-c");
                Check(victim.Kind=="hit"&&victim.HitPlays==playsBefore+1,"An old rejection cannot cancel a newer confirmed reaction");Next();
            }
            else if(stage==3&&elapsed>1.9)
            {
                Check(victim.PredictHit("timeout-d"),"A timeout fixture starts a speculative response");playsBefore=victim.HitPlays;Next();
            }
            else if(stage==4&&elapsed>.9)
            {
                Check(!victim.HasPredictedHit&&!victim.Busy&&healthOwner.Health==60,"Unconfirmed prediction expires promptly and never changes health");
                lastHit=Hit(3,3,"timeout-d",40,Now-800);Apply(lastHit);
                Check(victim.Kind=="hit"&&victim.HitPlays==playsBefore+1&&healthOwner.Health==40,"Late valid confirmation still applies damage after prediction expires");
                Check(victim.NormalizedTime>.3f,"Late confirmation catches up to server time instead of restarting the full reaction");
                Apply(lastHit);Check(victim.HitPlays==playsBefore+1,"Late duplicate confirmation is deduplicated");Next();
            }
            else if(stage==5&&elapsed>1.8)
            {
                Check(victim.PredictHit("fatal-e"),"Fatal-hit prediction remains visual until the server answers");
                var death=Hit(5,4,"fatal-e",0,Now);death.dead=true;death.deathSequence=5;death.deathAt=Now;Apply(death);
                Check(victim.Dead&&victim.Kind=="death"&&!victim.HasPredictedHit&&healthOwner.Health==0,"Confirmed death immediately overrides and clears pending hit prediction");
                victim.CancelPredictedHit("fatal-e");
                Check(victim.Dead&&victim.Kind=="death","Cancellation cannot erase confirmed death");
                Apply(lastHit);Check(victim.Dead&&healthOwner.Health==0,"An older damage reply cannot restore health after death");Next();
            }
            else if(stage==6&&elapsed>2.5)
            {
                Check(victim.Dead&&victim.Kind=="death"&&victim.NormalizedTime>.98f,"Stunned remains on the ground after immediate-response reconciliation");
                Check(setup.builds.SequenceEqual(EditorBuildSettings.scenes.Select(x=>x.path+"|"+x.enabled)),"Build scene configuration is preserved");
                Check(report.errors.Count==0,"No runtime errors occurred");report.passed=true;Finish();
            }
        }
        catch(Exception error){report.errors.Add(error.ToString());Finish();}
    }
    static void SampleOnset()
    {
        if(Time.frameCount==lastSampleFrame||report.onsetSamples.Count>=20)return;
        lastSampleFrame=Time.frameCount;
        int layer=victimAnimator.GetLayerIndex("Combat");
        report.onsetSamples.Add(new OnsetSample {frame=Time.frameCount,
            unityElapsed=Time.unscaledTime-predictionStartedUnityTime,
            editorElapsed=EditorApplication.timeSinceStartup-predictionStartedEditorTime,
            kind=victim.Kind,predictionPending=victim.HasPredictedHit,
            layerWeight=victimAnimator.GetLayerWeight(layer),animatorPhase=victimAnimator.GetCurrentAnimatorStateInfo(layer).normalizedTime,
            clipSeconds=victim.CurrentClipSeconds,head=victimAnimator.GetBoneTransform(HumanBodyBones.Head).position});
    }
    static void Capture(string name)
    {
        if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null||!Camera.main)return;
        var camera=Camera.main;var previous=RenderTexture.active;var oldTarget=camera.targetTexture;
        var texture=new RenderTexture(1280,720,24);var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=texture;camera.Render();RenderTexture.active=texture;
            pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();Directory.CreateDirectory(Folder);
            File.WriteAllBytes(Path.Combine(Folder,name),pixels.EncodeToPNG());report.screenshots.Add(name);
        }
        finally{camera.targetTexture=oldTarget;RenderTexture.active=previous;texture.Release();UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(pixels);}
    }
    static void Save(){Directory.CreateDirectory(Folder);File.WriteAllText(Path.Combine(Folder,"response-validation.json"),JsonUtility.ToJson(report,true));}
    static void Finish()
    {
        if(ending)return;ending=true;EditorApplication.update-=Tick;SessionState.SetBool(Finished,true);Save();
        Debug.Log("COMBAT_RESPONSE_VERIFIED passed="+report.passed+" checks="+report.checks.Count);EditorApplication.isPlaying=false;
    }
    static void Restore()
    {
        EditorApplication.update-=Tick;Application.logMessageReceived-=OnLog;
        setup??=JsonUtility.FromJson<Setup>(SessionState.GetString(Saved,""));SessionState.SetBool(Active,false);
        if(setup!=null&&!setup.quit)
        {
            var records=setup.scenes.Where(x=>!string.IsNullOrEmpty(x.path)).Select(x=>new SceneSetup {path=x.path,isLoaded=x.loaded,isActive=x.active}).ToArray();
            if(records.Length>0)EditorSceneManager.RestoreSceneManagerSetup(records);
        }
        if(setup?.quit==true)EditorApplication.Exit(report?.passed==true?0:1);
    }
}
