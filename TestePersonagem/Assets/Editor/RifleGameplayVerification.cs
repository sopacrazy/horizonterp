using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

// Small offline Play Mode smoke test. The locomotion component is stepped in demo
// mode without invoking its city/account/portal/network bootstrap in Start.
[InitializeOnLoad]
public static class RifleGameplayVerification
{
    const string Active="Hbrp.RiflePlay.Active",Saved="Hbrp.RiflePlay.Saved",Finished="Hbrp.RiflePlay.Finished";
    static readonly string Folder=Path.GetFullPath("../tools/rifle-verification");
    [Serializable] sealed class SceneRecord {public string path;public bool loaded,active;}
    [Serializable] sealed class Setup {public bool quit;public SceneRecord[] scenes;public string[] builds;}
    [Serializable] sealed class Report
    {
        public bool passed,localOnly=true;
        public List<string> checks=new List<string>(),errors=new List<string>();
        public List<string> aimDiagnostics=new List<string>();
        public float walkDistance,runDistance,walkSpeed,runSpeed;
        public float respawnElapsed;
        public int shots,ammoAfterShot;
    }
    static Setup setup;static Report report;static bool ending;static int stage,lastFrame;
    static double started,phaseStarted;static Vector3 phasePosition;
    static ThirdPersonPlayer player;static RifleWeapon weapon;static InGameChat chat;
    static CombatAvatarAnimation lifeCombat;
    static float deathStarted;
    static MethodInfo move;
    static RifleGameplayVerification(){EditorApplication.playModeStateChanged+=OnMode;}

    [MenuItem("Horizonte/Testes locais/Validar comando e controle do rifle")]
    public static void RunPlay()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Pare o Play antes de verificar o rifle.");
        if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
        setup=new Setup {quit=Environment.GetCommandLineArgs().Contains("--verify-rifle-play"),
            scenes=EditorSceneManager.GetSceneManagerSetup().Select(x=>new SceneRecord {path=x.path,loaded=x.isLoaded,active=x.isActive}).ToArray(),
            builds=EditorBuildSettings.scenes.Select(x=>x.path+"|"+x.enabled).ToArray()};
        if(!setup.quit&&setup.scenes.Any(x=>x.loaded&&string.IsNullOrEmpty(x.path)))throw new InvalidOperationException("Salve a cena sem nome antes do teste.");
        SessionState.SetString(Saved,JsonUtility.ToJson(setup));SessionState.SetBool(Active,true);SessionState.SetBool(Finished,false);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        EditorApplication.delayCall+=()=>EditorApplication.isPlaying=true;
    }
    static void OnMode(PlayModeStateChange mode)
    {
        if(!SessionState.GetBool(Active,false))return;
        if(mode==PlayModeStateChange.EnteredPlayMode)
        {
            setup=JsonUtility.FromJson<Setup>(SessionState.GetString(Saved,""));report=new Report();ending=false;stage=0;lastFrame=-1;
            started=phaseStarted=EditorApplication.timeSinceStartup;
            Application.logMessageReceived-=OnLog;Application.logMessageReceived+=OnLog;
            EditorApplication.update-=Tick;EditorApplication.update+=Tick;
        }
        else if(mode==PlayModeStateChange.EnteredEditMode)
        {
            if(!SessionState.GetBool(Finished,false)){report??=new Report();report.errors.Add("Rifle gameplay check was interrupted.");Save();}
            EditorApplication.delayCall+=Restore;
        }
    }
    static void OnLog(string value,string stack,LogType kind)
    {if(report!=null&&(kind==LogType.Error||kind==LogType.Exception||kind==LogType.Assert))report.errors.Add(value+"\n"+stack);}
    static void Check(bool value,string caption){if(!value)throw new Exception(caption);report.checks.Add(caption);}
    static void Next(){stage++;phaseStarted=EditorApplication.timeSinceStartup;phasePosition=player?player.transform.position:Vector3.zero;}
    static void Fixture()
    {
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Offline rifle floor";
        floor.transform.position=new Vector3(0,-.1f,0);floor.transform.localScale=new Vector3(50,.2f,50);
        var camera=new GameObject("Main Camera",typeof(Camera),typeof(ThirdPersonCamera));camera.tag="MainCamera";
        var root=new GameObject("Offline rifle player");root.SetActive(false);root.layer=2;
        var controller=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/Locomocao.controller");
        var avatar=GangsterPlayerVisual.CreateRemoteVisual("male",controller,root.transform);
        foreach(var bone in avatar.GetComponentsInChildren<Transform>(true))bone.gameObject.layer=2;
        player=root.AddComponent<ThirdPersonPlayer>();player.enabled=false;player.animator=avatar.GetComponent<Animator>();
        player.cameraTransform=camera.transform;player.DemoMode=true;
        var capsule=root.GetComponent<CharacterController>();capsule.height=1.75f;capsule.center=new Vector3(0,.875f,0);capsule.radius=.28f;capsule.skinWidth=.03f;
        camera.GetComponent<ThirdPersonCamera>().target=root.transform;
        root.SetActive(true);GameHud.Bootstrap();weapon=RifleWeapon.Create(player);chat=InGameChat.Instance;
        move=typeof(ThirdPersonPlayer).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic);
        Check(move!=null&&chat&&weapon,"Real player, chat composer, HUD and rifle are created in the offline fixture");
    }
    static void Command(string text)
    {
        chat.Open();Check(chat.IsOpen&&InGameChat.Editing,"Chat composer is open before command "+text);
        var field=GameHud.Instance.Root.Q<TextField>("chat-input");Check(field!=null,"Actual chat input is available");
        field.value=text;chat.Send();Check(!chat.IsOpen,"Local command closes the composer");
    }
    static void AimDiagnostic(string stage,Button button=null)
    {
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        object mobile=typeof(RifleWeapon).GetField("mobileAim",flags)?.GetValue(weapon);
        object blocked=typeof(RifleWeapon).GetProperty("Blocked",flags)?.GetValue(weapon);
        var orbit=Camera.main?Camera.main.GetComponent<ThirdPersonCamera>():null;
        report.aimDiagnostics.Add(stage+": mobileAim="+mobile+" blocked="+blocked+" aiming="+weapon.Aiming+
            " orbitAim="+(orbit&&orbit.AimActive)+" chat="+InGameChat.Editing+" panel="+GameHud.Instance.IsPanelOpen+
            " attached="+(button?.panel!=null)+" enabled="+button?.enabledInHierarchy);
    }
    static void Click(Button button)
    {
        // NavigationSubmit sent directly from EditorApplication.update does not
        // pass through the runtime panel dispatcher in this Unity version. Use
        // Clickable's own single-click path to exercise the bound UI callback.
        var simulate=typeof(Clickable).GetMethod("SimulateSingleClick",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,
            null,new[]{typeof(EventBase),typeof(int)},null);
        Check(simulate!=null,"UI Toolkit single-click simulation entry point exists");
        button.Focus();using(var evt=NavigationSubmitEvent.GetPooled())simulate.Invoke(button.clickable,new object[]{evt,0});
    }
    static Vector2 FreeTouchPoint(bool right)
    {
        foreach(float y in new[]{.35f,.45f,.55f,.25f})
        foreach(float x in right?new[]{.68f,.6f,.75f}:new[]{.3f,.38f,.2f})
        {
            var point=new Vector2(Screen.width*x,Screen.height*y);
            if(!GameHud.Instance.BlocksPointer(point))return point;
        }
        throw new Exception("No free gameplay touch area found in "+(right?"right":"left")+" half.");
    }
    static void VerifyMobileGestures()
    {
        var go=new GameObject("Synthetic armed touch verification");var controls=go.AddComponent<MobileControls>();controls.enabled=false;
        try
        {
            var left=FreeTouchPoint(false);var right=FreeTouchPoint(true);
            controls.ProcessTouch(101,left,Vector2.zero,TouchPhase.Began);
            controls.ProcessTouch(101,left+new Vector2(20,40),new Vector2(20,40),TouchPhase.Moved);
            Vector2 movement=MobileControls.Movement;
            Check(weapon.Equipped&&movement.x>0&&movement.y>0,"Armed left-half gesture keeps the normal movement joystick");
            controls.ProcessTouch(202,right,Vector2.zero,TouchPhase.Began);
            controls.ProcessTouch(202,right+new Vector2(30,-12),new Vector2(30,-12),TouchPhase.Moved);
            Check(MobileControls.Look.x>0&&MobileControls.Look.y<0&&MobileControls.Movement==movement,"Independent right-half gesture controls camera without stealing movement");
            var orbit=Camera.main.GetComponent<ThirdPersonCamera>();float yaw=orbit.yaw;
            typeof(ThirdPersonCamera).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(orbit,null);
            Check(Mathf.Abs(Mathf.DeltaAngle(yaw,orbit.yaw))>.1f,"Right-half drag rotates the real third-person camera while armed");
            var fire=GameHud.Instance.Root.Q<VisualElement>("rifle-fire");Check(fire!=null&&fire.panel!=null,"Armed fire touch control is attached to the runtime HUD");
            var panel=fire.panel.visualTree;Vector2 center=fire.worldBound.center;
            var firePoint=new Vector2(center.x*Screen.width/panel.resolvedStyle.width,Screen.height-center.y*Screen.height/panel.resolvedStyle.height);
            Check(GameHud.Instance.BlocksPointer(firePoint),"Fire button is excluded from gameplay gesture acquisition");
            controls.ProcessTouch(303,firePoint,Vector2.zero,TouchPhase.Began);
            controls.ProcessTouch(303,firePoint+Vector2.one*12,Vector2.one*12,TouchPhase.Moved);
            Check(MobileControls.Movement==movement,"Third finger on fire control preserves the existing movement owner");
            int shots=weapon.ShotSequence;
            var downEvent=new Event{type=EventType.MouseDown,button=0,mousePosition=center};
            using(var pointer=PointerDownEvent.GetPooled(downEvent)){pointer.target=fire;fire.SendEvent(pointer);}
            Check(weapon.ShotSequence==shots+1,"Fire pointer-down starts a shot immediately while movement and camera fingers remain held");
            bool held=(bool)typeof(RifleWeapon).GetField("heldFire",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(weapon);
            Check(held,"Fire touch remains held independently until release");
            controls.ProcessTouch(202,right+new Vector2(40,-12),new Vector2(10,0),TouchPhase.Moved);
            Check(MobileControls.Look.x>0&&MobileControls.Movement==movement,"Camera can keep dragging while the fire finger is held");
            var upEvent=new Event{type=EventType.MouseUp,button=0,mousePosition=center};
            using(var pointer=PointerUpEvent.GetPooled(upEvent)){pointer.target=fire;fire.SendEvent(pointer);}
            held=(bool)typeof(RifleWeapon).GetField("heldFire",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(weapon);
            Check(!held,"Releasing the fire button stops held fire");
            controls.ProcessTouch(303,firePoint,Vector2.zero,TouchPhase.Ended);
            controls.ProcessTouch(101,left,Vector2.zero,TouchPhase.Ended);
            Check(MobileControls.Movement==Vector2.zero,"Movement release stops the joystick immediately");
            controls.ProcessTouch(202,right,Vector2.zero,TouchPhase.Canceled);
            Check(MobileControls.Look==Vector2.zero,"Camera cancel clears the look delta immediately");
        }
        finally
        {
            typeof(MobileControls).GetMethod("Clear",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controls,null);
            UnityEngine.Object.Destroy(go);
        }
    }
    static void Tick()
    {
        if(ending||!EditorApplication.isPlaying)return;
        try
        {
            double age=EditorApplication.timeSinceStartup-phaseStarted;
            if(EditorApplication.timeSinceStartup-started>35)throw new Exception("Offline rifle gameplay test timed out.");
            if(stage==0&&age>.25){Fixture();Next();return;}
            if(player&&Time.frameCount!=lastFrame){lastFrame=Time.frameCount;move.Invoke(player,null);}
            if(stage==1&&age>.35)
            {
                Check(!AccountSession.Instance&&!MultiplayerWorld.Instance&&!VoiceChat.Instance,"Test opens no account, multiplayer connection or microphone");
                Command("/arma");Check(weapon.Equipped,"/arma equips the rifle from the active chat composer");
                player.DemoInput=Vector2.up;player.DemoRun=false;Next();
            }
            else if(stage==2&&age>.75)
            {
                report.walkDistance=Vector3.Distance(phasePosition,player.transform.position);report.walkSpeed=player.CurrentSpeed;
                Check(report.walkDistance>.3f&&player.CurrentSpeed>.5f&&weapon.Equipped,"Equipped walking moves the real CharacterController");
                Check(player.animator.GetFloat("RifleSpeed")>.5f,"Rifle locomotion layer receives walking speed");
                player.DemoRun=true;Next();
            }
            else if(stage==3&&age>.75)
            {
                report.runDistance=Vector3.Distance(phasePosition,player.transform.position);report.runSpeed=player.CurrentSpeed;
                // Editor asset/GC stalls change wall-clock sample durations; use
                // measured per-frame velocity, plus evidence of real displacement.
                Check(report.runDistance>.3f&&report.runSpeed>report.walkSpeed*1.3f,"Equipped running is faster than walking");
                player.DemoInput=Vector2.zero;
                var aim=GameHud.Instance.Root.Q<VisualElement>("rifle-controls").Query<Button>().ToList().FirstOrDefault(x=>x.tooltip.StartsWith("Mirar"));
                Check(aim!=null,"Aim button exists in the actual weapon HUD");
                AimDiagnostic("before click",aim);Click(aim);AimDiagnostic("after click",aim);
                VerifyMobileGestures();
                Next();
            }
            else if(stage==4&&age>.15)
            {
                AimDiagnostic("after gameplay frame");
                Check(weapon.Aiming&&Camera.main.GetComponent<ThirdPersonCamera>().AimActive,"HUD aim action activates the weapon aim camera");
                int ammo=weapon.Ammo,sequence=weapon.ShotSequence;weapon.RequestFire();
                report.ammoAfterShot=weapon.Ammo;report.shots=weapon.ShotSequence;
                Check(weapon.Ammo==ammo-1&&weapon.ShotSequence==sequence+1,"A shot immediately consumes one round and advances its visual event sequence");
                weapon.Reload();Check(weapon.Reloading,"R reload handler starts the reload timer");Next();
            }
            else if(stage==5&&age>1.7)
            {
                Check(!weapon.Reloading&&weapon.Ammo==30,"Reload restores the full magazine after the gameplay timer");
                Command("/desarmar");Check(!weapon.Equipped&&!weapon.Aiming,"Holster command releases rifle and aim");
                player.spawnPosition=new Vector3(5,.05f,5);player.spawnYaw=90;
                PlayerLifeHud.Create(player);player.Health=25;Next();
            }
            else if(stage==6&&age>.15)
            {
                var ring=GameHud.Instance.Root.Q<HudStatusRing>("health-progress");
                Check(ring!=null&&Mathf.Abs(ring.Value-25)<.01f,"Health ring reflects reduced player health instead of staying full");
                lifeCombat=player.animator.GetComponent<CombatAvatarAnimation>()??player.animator.gameObject.AddComponent<CombatAvatarAnimation>();
                lifeCombat.Initialize(player.animator);Check(lifeCombat.Ready,"Death animation is ready for the respawn check");
                long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                lifeCombat.Apply(new CombatState{epoch="rifle-life-smoke",revision=1,deathSequence=1,deathAt=now,dead=true,health=0},now);
                player.Health=0;deathStarted=Time.unscaledTime;Next();
            }
            else if(stage==7&&age>1)
            {
                var ring=GameHud.Instance.Root.Q<HudStatusRing>("health-progress");
                Check(player.Health<=0&&ring!=null&&ring.Value==0,"Death empties the health circle and does not revive immediately");
                Check(GameHud.Instance.Root.Q("death-countdown")?.resolvedStyle.display==DisplayStyle.Flex,"Death countdown is visible during the five-second wait");
                Next();
            }
            else if(stage==8&&age>4.5)
            {
                report.respawnElapsed=Time.unscaledTime-deathStarted;
                Check(player.Health==100&&report.respawnElapsed>=5,"Offline preview revives with full health after five seconds");
                Check(Vector3.Distance(player.transform.position,player.spawnPosition)<.4f,"Revive returns to the configured spawn point");
                Check(!lifeCombat.Dead&&!lifeCombat.Busy,"Revive clears the death pose and releases locomotion");
                Check(setup.builds.SequenceEqual(EditorBuildSettings.scenes.Select(x=>x.path+"|"+x.enabled)),"Play verification preserves configured build scenes");
                Check(report.errors.Count==0,"No runtime exceptions occurred during chat, movement, aiming, firing and holster");
                report.passed=true;Finish();
            }
        }
        catch(Exception error){report.errors.Add(error.ToString());Finish();}
    }
    static void Save(){Directory.CreateDirectory(Folder);File.WriteAllText(Path.Combine(Folder,"gameplay-validation.json"),JsonUtility.ToJson(report,true));}
    static void Finish()
    {
        if(ending)return;ending=true;SessionState.SetBool(Finished,true);EditorApplication.update-=Tick;Save();
        Debug.Log("RIFLE_GAMEPLAY_VERIFIED passed="+report.passed+" checks="+report.checks.Count);EditorApplication.isPlaying=false;
    }
    static void Restore()
    {
        EditorApplication.update-=Tick;Application.logMessageReceived-=OnLog;
        setup??=JsonUtility.FromJson<Setup>(SessionState.GetString(Saved,""));SessionState.SetBool(Active,false);
        if(setup!=null&&!setup.quit)
        {
            var scenes=setup.scenes.Where(x=>!string.IsNullOrEmpty(x.path)).Select(x=>new SceneSetup {path=x.path,isLoaded=x.loaded,isActive=x.active}).ToArray();
            if(scenes.Length>0)EditorSceneManager.RestoreSceneManagerSetup(scenes);
        }
        if(setup?.quit==true)EditorApplication.Exit(report?.passed==true?0:1);
    }
}
