using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Explicit offline Play Mode fixture. It never creates AccountSession or sends
// requests; measurements read the previous rendered frame, after all LateUpdate
// pose writers, without manually invoking Animator IK or the pose components.
[InitializeOnLoad]
public static class PoliceDetaineeBoardingVerification
{
    const string ActiveKey="Hbrp.PoliceCar.DetaineeCheck.Active";
    const string FinishedKey="Hbrp.PoliceCar.DetaineeCheck.Finished";
    const BindingFlags InstanceFlags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    [Serializable] sealed class Frame
    {
        public int frame;
        public string phase;
        public float elapsed,progress,rearDoor,driverDoor,kneeFlex,leftWristForward,rightWristForward,wristDistance;
        public float footClearance,hipSeatError;
        public Vector3 hips,leftWrist,rightWrist,leftFoot,rightFoot;
    }
    [Serializable] sealed class Report
    {
        public bool passed;
        public string fixture="Offline preview; direct seat reservation and local passenger handoff; no account or network";
        public string poseSampling="Editor update reads each completed rendered frame; no manual Animator IK or pose evaluation";
        public string enteringClip,enteringClipAsset;
        public int approachFrames,authoredFrames,seatedFrames,exitFrames,measuredCuffFrames,validCuffFrames,stableSeatedFrames;
        public float maxKneeFlex,maxRearDoor,maxDriverDoor,maxWristDistance,maxWristForward=-999;
        public float minApproachFootClearance=999,maxSeatedHipError,maxSeatedHipDrift,exitSeconds;
        public List<string> checks=new List<string>();
        public List<string> errors=new List<string>();
        public List<string> screenshots=new List<string>();
        public List<Frame> frames=new List<Frame>();
    }
    static Report report;
    static ThirdPersonPlayer player;
    static PoliceVehicleController car;
    static PoliceCarRig rig;
    static PoliceSeatAvatar rear,driver;
    static int stage,lastFrame=-1;
    static double started,stageStarted;
    static bool finishing,midCaptured,endCaptured,stableStarted;
    static Vector3 stableHip,exitPoint;
    static string[] buildScenes;
    static string Folder=>Path.GetFullPath("../tools/police-car-animation");
    static string ReportPath=>Path.Combine(Folder,"detainee-validation.json");
    static bool Requested=>Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-detainee-boarding")>=0;

    static PoliceDetaineeBoardingVerification()
    {EditorApplication.playModeStateChanged+=OnPlayState;}

    // Invoke with -executeMethod PoliceDetaineeBoardingVerification.RunPlay
    // --verify-detainee-boarding. The explicit flag authorizes quitting this
    // verification editor after its report has been written.
    public static void RunPlay()
    {
        report=new Report();
        try
        {
            Check(Requested,"Explicit --verify-detainee-boarding flag is present");
            Check(!EditorApplication.isPlayingOrWillChangePlaymode,"Verification begins outside Play Mode");
            for(int index=0;index<SceneManager.sceneCount;index++)
                Require(!SceneManager.GetSceneAt(index).isDirty,"Open scene has unsaved changes; verification will not save or discard it");
            Check(!AccountSession.Instance,"Editor has no account session before creating the offline preview");
            Directory.CreateDirectory(Folder);SaveReport();
            PoliceCarAnimationImport.Verify();
            SessionState.SetBool(ActiveKey,true);SessionState.SetBool(FinishedKey,false);
            PoliceVehicleAnimationVerification.OpenPreviewScene(false);
            EditorApplication.delayCall+=()=>EditorApplication.isPlaying=true;
        }
        catch(Exception error)
        {
            report.errors.Add(error.ToString());SaveReport();SessionState.SetBool(ActiveKey,false);
            Debug.LogError("POLICE_DETAINEE_BOARDING_FAILED "+error.Message);
            if(Requested)EditorApplication.Exit(1);else throw;
        }
    }
    static void OnPlayState(PlayModeStateChange mode)
    {
        if(!SessionState.GetBool(ActiveKey,false))return;
        if(mode==PlayModeStateChange.EnteredPlayMode)
        {
            report=new Report();stage=0;lastFrame=-1;finishing=midCaptured=endCaptured=stableStarted=false;
            started=stageStarted=EditorApplication.timeSinceStartup;
            buildScenes=EditorBuildSettings.scenes.Select(x=>x.path+"|"+x.enabled).ToArray();
            Application.logMessageReceived-=OnLog;Application.logMessageReceived+=OnLog;
            EditorApplication.update-=Tick;EditorApplication.update+=Tick;
        }
        else if(mode==PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.update-=Tick;Application.logMessageReceived-=OnLog;
            if(!SessionState.GetBool(FinishedKey,false))
            {
                report??=File.Exists(ReportPath)?JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath)):new Report();
                report.errors.Add("Play Mode ended before detainee verification completed");SaveReport();
            }
            SessionState.SetBool(ActiveKey,false);
            bool passed=File.Exists(ReportPath)&&JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath)).passed;
            if(Requested)EditorApplication.delayCall+=()=>EditorApplication.Exit(passed?0:1);
        }
    }
    static void OnLog(string message,string stack,LogType type)
    {if(report!=null&&(type==LogType.Error||type==LogType.Exception||type==LogType.Assert))report.errors.Add(message+"\n"+stack);}
    static void Require(bool pass,string message)
    {if(!pass)throw new InvalidOperationException(message);}
    static void Check(bool pass,string message)
    {Require(pass,message);report.checks.Add(message);}
    static void Next(){stage++;stageStarted=EditorApplication.timeSinceStartup;}

    static void Tick()
    {
        if(finishing||!EditorApplication.isPlaying||Time.frameCount==lastFrame)return;
        lastFrame=Time.frameCount;
        try
        {
            Require(EditorApplication.timeSinceStartup-started<20,"Detainee boarding timed out at stage "+stage);
            Require(report.errors.Count==0,"Runtime errors occurred; see the errors in the verification report");
            Require(!AccountSession.Instance,"Offline verification unexpectedly created an account session");
            double elapsed=EditorApplication.timeSinceStartup-stageStarted;
            if(stage==0)
            {
                player=UnityEngine.Object.FindAnyObjectByType<ThirdPersonPlayer>();
                car=UnityEngine.Object.FindObjectsByType<PoliceVehicleController>(FindObjectsSortMode.None).FirstOrDefault(x=>x.LocalPreview);
                if(!player||!car||elapsed<.5)return;
                PrepareFixture();Next();return;
            }
            if(stage==1)
            {
                Sample();
                Require(rear.VisibleAnimator.gameObject.activeInHierarchy,"The rear passenger must remain visible throughout boarding");
                NoDuplicatePassenger();
                if(rear.AuthoredClipActive&&rear.ClipProgress>=.4f&&rear.ClipProgress<=.8f&&!midCaptured)
                {CapturePair("mid");midCaptured=true;}
                if(!rear.Seated)return;
                Check(report.approachFrames>5,"The left-side detainee visibly walks around the rear of the car");
                Check(report.minApproachFootClearance>=.02f,"Both feet stay at least two centimetres outside the body footprint while approaching");
                Check(report.authoredFrames>5,"The rear passenger visibly samples the authored entering clip");
                Check(report.maxKneeFlex>60,"The entering clip bends the knees more than sixty degrees");
                Check(report.maxRearDoor>.9f,"The rear-right passenger door opens automatically during boarding");
                Check(midCaptured,"Front and rear right-side images capture the middle of the entering clip");
                stableStarted=false;Next();
            }
            else if(stage==2)
            {
                Sample();NoDuplicatePassenger();
                Require(rear.Seated&&rear.Uid=="55","The cuffed suspect retains the rear-seat reservation");
                Require(rig.DoorOpenAmount(false)<.01f,"The rear passenger door closes after boarding");
                var hips=car.transform.InverseTransformPoint(rear.VisibleAnimator.GetBoneTransform(HumanBodyBones.Hips).position);
                if(!stableStarted){stableStarted=true;stableHip=hips;}
                report.maxSeatedHipDrift=Mathf.Max(report.maxSeatedHipDrift,Vector3.Distance(stableHip,hips));
                report.stableSeatedFrames++;
                if(report.stableSeatedFrames<20)return;
                Check(report.maxSeatedHipError<.05f,"The seated pelvis stays within five centimetres of the rear seat");
                Check(report.maxSeatedHipDrift<.015f,"Twenty rendered seated frames do not accumulate pelvis drift");
                Check(report.maxWristDistance<.4f,"Cuffed wrists remain less than forty centimetres apart through authored entry and seating");
                Check(report.maxWristForward<-.04f,"Both cuffed wrists remain behind the body during authored entry and seating");
                Check(WristDistance(rear.VisibleAnimator)<.3f,"The final seated wrists stay less than thirty centimetres apart");
                Check(report.validCuffFrames==report.measuredCuffFrames&&report.measuredCuffFrames>20,"Rendered cuff checks cover every measured authored and seated frame");
                Check(report.maxDriverDoor<.01f,"The front-left driver door stays closed throughout detainee boarding");
                CapturePair("seated");endCaptured=true;
                exitPoint=car.transform.TransformPoint(new Vector3(2.1f,.04f,-.65f));
                rear.SetOccupant("",exitPoint);Check(rear.Exiting,"Eject starts the rear passenger exit");
                Check(!rear.AuthoredClipActive,"The rear passenger retains its short procedural exit");Next();
            }
            else if(stage==3)
            {
                Sample();
                Require(!player.animator.gameObject.activeInHierarchy,"The original suspect remains hidden until the exit visual completes");
                if(rear.Exiting){NoDuplicatePassenger();return;}
                report.exitSeconds=(float)elapsed;
                Check(report.exitFrames>5&&report.exitSeconds>=1.9f&&report.exitSeconds<3,"The procedural eject completes in about 2.1 seconds");
                Check(!rear.VisibleAnimator.gameObject.activeInHierarchy,"The released rear-seat visual hides after eject");
                Check(rig.DoorOpenAmount(false)<.01f&&rig.DoorOpenAmount(true)<.01f,"Both doors are closed after eject");
                // The offline fixture has no account UID. Simulate the reliable
                // local handoff only after the matching seat visual has exited.
                player.SetPoliceCarPassenger(false,exitPoint,90);Next();
            }
            else if(stage==4&&elapsed>.4)
            {
                Check(!player.InPoliceCar&&player.IsCuffed,"Eject restores on-foot status and retains the suspect's cuffs");
                Check(player.animator.gameObject.activeInHierarchy&&player.GetComponent<CharacterController>().enabled,"Eject restores the original suspect avatar and controller");
                Check(!rear.VisibleAnimator.gameObject.activeInHierarchy&&!driver.VisibleAnimator.gameObject.activeInHierarchy,"Eject leaves no duplicate passenger or driver visual");
                Check(player.animator.GetComponent<CuffedAvatarPose>().Cuffed,"The restored suspect retains its cuffed pose component");
                Check(!AccountSession.Instance,"The complete boarding and eject fixture never creates an account session");
                Check(buildScenes.SequenceEqual(EditorBuildSettings.scenes.Select(x=>x.path+"|"+x.enabled)),"Verification does not change build scene configuration");
                Check(endCaptured&&report.screenshots.Count==4,"Four right-side images cover front and rear views at mid-entry and seated");
                report.passed=true;Finish();
            }
        }
        catch(Exception error){report.errors.Add(error.ToString());Finish();}
    }

    static void PrepareFixture()
    {
        Check(Vector3.Distance(car.transform.position,Vector3.zero)<.01f,"The offline preview car is at the origin");
        Check(!car.Occupied&&!PoliceVehicleController.Active,"No driver owns the preview vehicle");
        var seats=car.GetComponentsInChildren<PoliceSeatAvatar>(true);
        rear=seats.FirstOrDefault(x=>!x.IsDriver);driver=seats.FirstOrDefault(x=>x.IsDriver);rig=car.GetComponent<PoliceCarRig>();
        Check(rear&&driver&&rig&&rig.HasDoors,"Separate full-size driver and rear passenger visuals and doors exist");
        var clip=typeof(PoliceSeatAvatar).GetField("enteringClip",InstanceFlags)?.GetValue(rear) as AnimationClip;
        Check(clip&&clip.humanMotion,"The rear passenger loads a humanoid entering clip");
        report.enteringClip=clip.name;report.enteringClipAsset=AssetDatabase.GetAssetPath(clip);
        Check(report.enteringClip=="RearMirrored"&&report.enteringClipAsset.Replace('\\','/').EndsWith("/EnteringCar.fbx",StringComparison.OrdinalIgnoreCase),"The rear-right passenger uses RearMirrored from the original EnteringCar resource");
        var cuffs=rear.VisibleAnimator.GetComponent<CuffedAvatarPose>();
        Check(cuffs&&cuffs.Cuffed&&cuffs.enabled,"The rear passenger retains its active cuffed pose");
        var lateProperty=typeof(CuffedAvatarPose).GetProperty("UseLatePose",InstanceFlags);
        var lateField=typeof(CuffedAvatarPose).GetField("UseLatePose",InstanceFlags);
        object late=lateProperty!=null?lateProperty.GetValue(cuffs):lateField?.GetValue(cuffs);
        Check(late is bool enabled&&enabled,"The passenger uses the late cuff overlay after manual clip sampling");
        Check(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Null&&Camera.main,"A rendering camera is available for the required screenshots");
        var start=car.transform.TransformPoint(new Vector3(-1.95f,.04f,-1.8f));
        player.GetComponent<CharacterController>().enabled=false;player.transform.SetPositionAndRotation(start,Quaternion.Euler(0,90,0));
        player.GetComponent<CharacterController>().enabled=true;player.SetCustody(true,"",start,90);
        rear.SetOccupant("55",start,startYaw:90);
        player.SetPoliceCarPassenger(true,car.transform.position,car.transform.eulerAngles.y);
        Check(rear.Uid=="55"&&rear.Approaching,"A cuffed left-side detainee first approaches the rear-right door");
        Check(!player.animator.gameObject.activeInHierarchy&&!player.GetComponent<CharacterController>().enabled,"The original suspect avatar and capsule hide during the rear-seat handoff");
    }
    static void NoDuplicatePassenger()
    {
        Require(!player.animator.gameObject.activeInHierarchy,"The original suspect must not duplicate the rear passenger visual");
        Require(!driver.VisibleAnimator.gameObject.activeInHierarchy,"Detainee boarding must not show a duplicate driver");
        Require(car.GetComponentsInChildren<PoliceSeatAvatar>(true).Count(x=>x.VisibleAnimator&&x.VisibleAnimator.gameObject.activeInHierarchy)==1,"Exactly one rear passenger visual is rendered");
    }
    static void Sample()
    {
        var animator=rear.VisibleAnimator;
        if(!animator||!animator.gameObject.activeInHierarchy)return;
        string phase=rear.Approaching?"approach":rear.Exiting?"exit":rear.AuthoredClipActive?"entry":rear.Seated?"seated":"bridge";
        var hips=animator.GetBoneTransform(HumanBodyBones.Hips);
        var left=animator.GetBoneTransform(HumanBodyBones.LeftHand);var right=animator.GetBoneTransform(HumanBodyBones.RightHand);
        var leftFoot=animator.GetBoneTransform(HumanBodyBones.LeftFoot);var rightFoot=animator.GetBoneTransform(HumanBodyBones.RightFoot);
        var shoulderSide=animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position-animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position;
        var torsoUp=animator.GetBoneTransform(HumanBodyBones.Head).position-hips.position;
        var forward=Vector3.Cross(shoulderSide,torsoUp).normalized;
        Require(forward.sqrMagnitude>.9f,"The rendered torso defines a valid body-forward direction");
        var sample=new Frame
        {
            frame=Time.frameCount,phase=phase,elapsed=(float)(EditorApplication.timeSinceStartup-started),progress=rear.ClipProgress,
            rearDoor=rig.DoorOpenAmount(false),driverDoor=rig.DoorOpenAmount(true),
            kneeFlex=Mathf.Max(KneeFlex(animator,true),KneeFlex(animator,false)),
            leftWristForward=Vector3.Dot(left.position-hips.position,forward),rightWristForward=Vector3.Dot(right.position-hips.position,forward),
            wristDistance=Vector3.Distance(left.position,right.position),
            hips=car.transform.InverseTransformPoint(hips.position),leftWrist=car.transform.InverseTransformPoint(left.position),rightWrist=car.transform.InverseTransformPoint(right.position),
            leftFoot=car.transform.InverseTransformPoint(leftFoot.position),rightFoot=car.transform.InverseTransformPoint(rightFoot.position)
        };
        sample.footClearance=Mathf.Min(FootClearance(sample.leftFoot),FootClearance(sample.rightFoot));
        sample.hipSeatError=Vector3.Distance(sample.hips,rig.RearSeat);
        report.maxRearDoor=Mathf.Max(report.maxRearDoor,sample.rearDoor);report.maxDriverDoor=Mathf.Max(report.maxDriverDoor,sample.driverDoor);
        Require(sample.driverDoor<.01f,"The driver door must stay closed during all rear passenger phases");
        if(rear.Approaching)
        {report.approachFrames++;report.minApproachFootClearance=Mathf.Min(report.minApproachFootClearance,sample.footClearance);}
        if(rear.AuthoredClipActive&&!rear.Exiting){report.authoredFrames++;report.maxKneeFlex=Mathf.Max(report.maxKneeFlex,sample.kneeFlex);}
        if(rear.Seated){report.seatedFrames++;report.maxSeatedHipError=Mathf.Max(report.maxSeatedHipError,sample.hipSeatError);}
        if(rear.Exiting)report.exitFrames++;
        if(rear.AuthoredClipActive||rear.Seated)
        {
            report.measuredCuffFrames++;report.maxWristDistance=Mathf.Max(report.maxWristDistance,sample.wristDistance);
            report.maxWristForward=Mathf.Max(report.maxWristForward,Mathf.Max(sample.leftWristForward,sample.rightWristForward));
            if(sample.wristDistance<.4f&&sample.leftWristForward<-.04f&&sample.rightWristForward<-.04f)report.validCuffFrames++;
        }
        if(rear.Exiting)Require(animator.GetComponent<CuffedAvatarPose>().Cuffed,"The procedural exit must retain the suspect's cuffs");
        report.frames.Add(sample);
    }
    static float KneeFlex(Animator animator,bool left)
    {
        var upper=animator.GetBoneTransform(left?HumanBodyBones.LeftUpperLeg:HumanBodyBones.RightUpperLeg);
        var knee=animator.GetBoneTransform(left?HumanBodyBones.LeftLowerLeg:HumanBodyBones.RightLowerLeg);
        var foot=animator.GetBoneTransform(left?HumanBodyBones.LeftFoot:HumanBodyBones.RightFoot);
        return 180-Vector3.Angle(upper.position-knee.position,foot.position-knee.position);
    }
    static float WristDistance(Animator animator)=>Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.LeftHand).position,animator.GetBoneTransform(HumanBodyBones.RightHand).position);
    static float FootClearance(Vector3 point)=>Mathf.Max(Mathf.Abs(point.x)-rig.Dimensions.x*.5f,Mathf.Abs(point.z)-rig.Dimensions.z*.5f);
    static void CapturePair(string phase)
    {
        Capture("detainee-"+phase+"-front-right.png",new Vector3(4.8f,1.8f,3.2f));
        Capture("detainee-"+phase+"-rear-right.png",new Vector3(4.8f,1.8f,-3.8f));
    }
    static void Capture(string name,Vector3 offset)
    {
        var camera=Camera.main;Require(camera,"The preview camera is required for a detainee capture");
        var position=camera.transform.position;var rotation=camera.transform.rotation;var target=camera.targetTexture;var previous=RenderTexture.active;
        var texture=new RenderTexture(1280,720,24);var pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);
        try
        {
            camera.transform.position=car.transform.TransformPoint(offset);camera.transform.LookAt(car.transform.TransformPoint(new Vector3(.35f,.9f,-.65f)));
            camera.targetTexture=texture;camera.Render();RenderTexture.active=texture;
            pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();Directory.CreateDirectory(Folder);
            File.WriteAllBytes(Path.Combine(Folder,name),pixels.EncodeToPNG());report.screenshots.Add(name);
        }
        finally
        {
            camera.targetTexture=target;camera.transform.SetPositionAndRotation(position,rotation);RenderTexture.active=previous;
            texture.Release();UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(pixels);
        }
    }
    static void SaveReport()
    {Directory.CreateDirectory(Folder);File.WriteAllText(ReportPath,JsonUtility.ToJson(report,true));}
    static void Finish()
    {
        if(finishing)return;finishing=true;SessionState.SetBool(FinishedKey,true);EditorApplication.update-=Tick;
        SaveReport();Debug.Log("POLICE_DETAINEE_BOARDING_VERIFIED passed="+report.passed+" checks="+report.checks.Count+" frames="+report.frames.Count);
        EditorApplication.isPlaying=false;
    }
}
