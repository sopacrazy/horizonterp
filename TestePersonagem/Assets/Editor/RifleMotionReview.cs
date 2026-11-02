using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Evaluate the actual controller at successive frames, then freeze the current
// skinned vertices before Camera.Render can resample a Humanoid in edit mode.
public static class RifleMotionReview
{
    const float Step=1f/30f;
    const int ViewWidth=360,ViewHeight=520;
    static readonly int[] Captures={0,7,15,26,34,48};
    static readonly MethodInfo SkinWorld=typeof(ShootingGripReview).GetMethod("SkinWorld",BindingFlags.Static|BindingFlags.NonPublic);
    [Serializable] sealed class Sample
    {
        public string gender,scenario;
        public float seconds,blendSpeed,lowReadyAngle,muzzleBelowHead,rightArmRotationChange,segmentLengthChange,supportError,stockHeadDistance,weaponYaw,weaponPitch,shotNormalized;
        public Vector3 rightHand,muzzle,leftFoot,rightFoot;
    }
    [Serializable] sealed class Sequence
    {
        public string gender,scenario,sheet;
        public int requestedShots;
        public float rightArmRotationChange,segmentLengthChange,supportError,footTravel,shotProgress,maximumNeutralPitch,maximumPitchStep;
    }
    [Serializable] sealed class Report
    {
        public bool passed;
        public List<string> checks=new List<string>(),errors=new List<string>();
        public List<Sequence> sequences=new List<Sequence>();
        public List<Sample> samples=new List<Sample>();
    }
    sealed class Rig
    {
        public GameObject root;
        public Animator animator;
        public RifleAvatar rifle;
        public Transform upper,lower,hand,leftUpper,leftLower,leftHand,leftFoot,rightFoot,head;
    }
    static void Check(Report report,bool pass,string reason)
    {
        if(!pass)throw new InvalidOperationException(reason);
        report.checks.Add(reason);
    }

    [MenuItem("Horizonte/Testes locais/Revisar movimento armado em sequência")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Pare o Play para revisar a sequência armada.");
        string folder=Path.GetFullPath("../tools/rifle-motion-review");Directory.CreateDirectory(folder);
        var report=new Report();var scene=EditorSceneManager.NewPreviewScene();var roots=new List<GameObject>();
        GameObject cameraObject=null,keyObject=null,fillObject=null,labelObject=null;
        try
        {
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Animations/Locomocao.controller");
            Check(report,controller&&SkinWorld!=null,"Current controller and CPU skinning renderer are available");
            cameraObject=ObjectInScene("Rifle motion review camera",scene);
            var camera=cameraObject.AddComponent<Camera>();camera.scene=scene;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.13f,.17f,.21f);
            camera.orthographic=true;camera.orthographicSize=1.13f;camera.nearClipPlane=.03f;camera.farClipPlane=15;
            keyObject=ObjectInScene("Rifle motion key light",scene);var key=keyObject.AddComponent<Light>();
            key.type=LightType.Directional;key.intensity=1.15f;key.transform.rotation=Quaternion.Euler(28,-135,0);
            fillObject=ObjectInScene("Rifle motion fill light",scene);var fill=fillObject.AddComponent<Light>();
            fill.type=LightType.Directional;fill.intensity=.7f;fill.transform.rotation=Quaternion.Euler(15,35,0);
            labelObject=ObjectInScene("Review frame caption",scene);var caption=labelObject.AddComponent<TextMesh>();
            caption.anchor=TextAnchor.MiddleCenter;caption.alignment=TextAlignment.Center;
            caption.fontSize=40;caption.characterSize=.026f;caption.color=Color.white;caption.font=HudPack.LabelFont;
            caption.GetComponent<MeshRenderer>().sharedMaterial=caption.font.material;
            labelObject.transform.position=new Vector3(0,2.12f,0);

            foreach(string gender in new[]{"male","female"})
            foreach(string scenario in new[]{"idle","rest","rest-aim-rest","rest-shot","walk","aim-walk","run","run-stop","run-stop-shot","run-stop-burst","repeated-shots"})
            {
                var rig=CreateRig(scene,gender,controller);roots.Add(rig.root);
                var sequence=new Sequence{gender=gender,scenario=scenario,sheet=gender+"-"+scenario+".png"};
                report.sequences.Add(sequence);
                int shot=0,captureIndex=0;
                float initialSpeed=scenario.StartsWith("run")?4.5f:scenario.EndsWith("walk")?1.65f:0;
                bool resting=scenario.StartsWith("rest");
                bool initialAim=!resting&&(initialSpeed==0||scenario=="aim-walk");
                for(int frame=0;frame<30;frame++)
                {
                    rig.rifle.Apply(true,initialAim,0,0,initialSpeed);
                    rig.rifle.PrepareAnimationFrame(Step);rig.animator.Update(Step);rig.rifle.ApplyAttachmentPose();
                }
                int rifleLayer=rig.animator.GetLayerIndex("Rifle");
                rig.animator.Play("Rifle.Locomotion",rifleLayer,0);rig.animator.Update(0);
                var firstLeft=rig.leftFoot.position;var firstRight=rig.rightFoot.position;
                float minimumShotTime=float.PositiveInfinity,maximumShotTime=float.NegativeInfinity;
                float previousPitch=0;
                var sheet=new Texture2D(ViewWidth*2*3,ViewHeight*2,TextureFormat.RGB24,false);
                var sheetPixels=Enumerable.Repeat(new Color(.13f,.17f,.21f),sheet.width*sheet.height).ToArray();sheet.SetPixels(sheetPixels);
                try
                {
                    for(int frame=0;frame<60;frame++)
                    {
                        float time=frame*Step;
                        float speed=scenario=="run"?4.5f:scenario.StartsWith("run-stop")&&frame<21?4.5f:scenario.EndsWith("walk")?1.65f:0;
                        bool aiming=resting?(scenario=="rest-aim-rest"&&frame>=15&&frame<41)||
                            (scenario=="rest-shot"&&frame>=15&&frame<31):speed==0||scenario=="aim-walk";
                        if(scenario=="run-stop-shot"&&frame==30)shot++;
                        if(scenario=="run-stop-burst"&&(frame==18||frame==22))shot++;
                        if(scenario=="rest-shot"&&(frame==15||frame==19||frame==23))shot++;
                        if(scenario=="repeated-shots"&&frame%4==0)shot++;
                        rig.rifle.Apply(true,aiming,shot,0,speed);
                        rig.rifle.PrepareAnimationFrame(Step);rig.animator.Update(Step);
                        Quaternion[] beforeRot={rig.upper.localRotation,rig.lower.localRotation,rig.hand.localRotation};
                        float[] beforeLength={Vector3.Distance(rig.upper.position,rig.lower.position),Vector3.Distance(rig.lower.position,rig.hand.position),
                            Vector3.Distance(rig.leftUpper.position,rig.leftLower.position),Vector3.Distance(rig.leftLower.position,rig.leftHand.position)};
                        rig.rifle.ApplyAttachmentPose();
                        var currentGun=rig.rifle.Muzzle?rig.rifle.Muzzle.parent:null;
                        if(currentGun)sequence.maximumNeutralPitch=Mathf.Max(sequence.maximumNeutralPitch,
                            Mathf.Abs(Mathf.DeltaAngle(0,currentGun.eulerAngles.x)));
                        float currentPitch=currentGun?Mathf.DeltaAngle(0,currentGun.eulerAngles.x):0;
                        if(frame>0)sequence.maximumPitchStep=Mathf.Max(sequence.maximumPitchStep,Mathf.Abs(Mathf.DeltaAngle(previousPitch,currentPitch)));
                        previousPitch=currentPitch;
                        float rotation=Mathf.Max(Mathf.Abs(Quaternion.Angle(beforeRot[0],rig.upper.localRotation)-rig.rifle.LowReadyAngle),
                            Quaternion.Angle(beforeRot[1],rig.lower.localRotation),Quaternion.Angle(beforeRot[2],rig.hand.localRotation));
                        float[] afterLength={Vector3.Distance(rig.upper.position,rig.lower.position),Vector3.Distance(rig.lower.position,rig.hand.position),
                            Vector3.Distance(rig.leftUpper.position,rig.leftLower.position),Vector3.Distance(rig.leftLower.position,rig.leftHand.position)};
                        float lengthChange=0;for(int i=0;i<beforeLength.Length;i++)lengthChange=Mathf.Max(lengthChange,Mathf.Abs(beforeLength[i]-afterLength[i]));
                        sequence.rightArmRotationChange=Mathf.Max(sequence.rightArmRotationChange,rotation);
                        sequence.segmentLengthChange=Mathf.Max(sequence.segmentLengthChange,lengthChange);
                        sequence.supportError=Mathf.Max(sequence.supportError,rig.rifle.GripError);
                        if(scenario=="run"||scenario.EndsWith("walk"))sequence.footTravel=Mathf.Max(sequence.footTravel,
                            Vector3.Distance(firstLeft,rig.leftFoot.position),Vector3.Distance(firstRight,rig.rightFoot.position));
                        int fireLayer=rig.animator.GetLayerIndex("RifleFire");
                        float shotTime=fireLayer>=0?rig.animator.GetCurrentAnimatorStateInfo(fireLayer).normalizedTime:0;
                        if(scenario=="repeated-shots"&&fireLayer>=0&&rig.animator.GetLayerWeight(fireLayer)>.1f)
                        {minimumShotTime=Mathf.Min(minimumShotTime,shotTime);maximumShotTime=Mathf.Max(maximumShotTime,shotTime);}
                        if(captureIndex<Captures.Length&&frame==Captures[captureIndex])
                        {
                            var stock=rig.rifle.Muzzle?rig.rifle.Muzzle.parent.Find("Stock"):null;
                            var gun=rig.rifle.Muzzle?rig.rifle.Muzzle.parent:null;
                            report.samples.Add(new Sample{gender=gender,scenario=scenario,seconds=time,blendSpeed=rig.animator.GetFloat("RifleSpeed"),lowReadyAngle=rig.rifle.LowReadyAngle,
                                muzzleBelowHead=rig.head.position.y-(rig.rifle.Muzzle?rig.rifle.Muzzle.position.y:rig.head.position.y),
                                rightArmRotationChange=rotation,segmentLengthChange=lengthChange,supportError=rig.rifle.GripError,
                                stockHeadDistance=stock?Vector3.Distance(stock.position,rig.head.position):-1,
                                weaponYaw=gun?Mathf.DeltaAngle(0,gun.eulerAngles.y):0,weaponPitch=gun?Mathf.DeltaAngle(0,gun.eulerAngles.x):0,
                                shotNormalized=shotTime,rightHand=rig.hand.position,muzzle=rig.rifle.Muzzle?rig.rifle.Muzzle.position:Vector3.zero,
                                leftFoot=rig.leftFoot.position,rightFoot=rig.rightFoot.position});
                            CaptureTile(scene,rig,camera,caption,sheet,captureIndex,gender+" / "+scenario+" / "+time.ToString("0.00")+"s");captureIndex++;
                        }
                    }
                    sequence.requestedShots=shot;
                    sequence.shotProgress=float.IsFinite(minimumShotTime)&&float.IsFinite(maximumShotTime)?maximumShotTime-minimumShotTime:0;
                    sheet.Apply(false);File.WriteAllBytes(Path.Combine(folder,sequence.sheet),sheet.EncodeToPNG());
                    Check(report,captureIndex==6,gender+" / "+scenario+": six actual animated frames rendered from front and side");
                    Check(report,sequence.rightArmRotationChange<.1f,gender+" / "+scenario+": upper-arm change matches relaxed carry, preserving authored elbow and wrist tracks (<0.1 degree)");
                    Check(report,sequence.segmentLengthChange<.002f,gender+" / "+scenario+": neither arm is stretched");
                    Check(report,sequence.supportError<.10f,gender+" / "+scenario+": both weapon grip points stay aligned (<10cm)");
                    if(scenario=="run"||scenario.EndsWith("walk"))Check(report,sequence.footTravel>.08f,gender+": "+scenario+" advances the foot cycle instead of freezing in a single pose");
                    if(scenario=="repeated-shots")Check(report,sequence.shotProgress>.01f,gender+": repeated shots advance the authored shot rather than remaining at frame zero");
                    if(scenario=="rest")Check(report,report.samples.Where(s=>s.gender==gender&&s.scenario==scenario)
                        .All(s=>s.lowReadyAngle>30&&s.weaponPitch>25&&s.muzzleBelowHead>.12f),gender+": idle without aiming carries the rifle below the face with its barrel down");
                    if(scenario=="rest-aim-rest"||scenario=="rest-shot")
                    {
                        var middle=report.samples.First(s=>s.gender==gender&&s.scenario==scenario&&s.seconds>.85f&&s.seconds<.9f);
                        var final=report.samples.Last(s=>s.gender==gender&&s.scenario==scenario);
                        Check(report,middle.lowReadyAngle<.1f&&Mathf.Abs(middle.weaponPitch)<4,gender+" / "+scenario+": aim or fire raises the rifle back to the sight line");
                        Check(report,final.lowReadyAngle>30,gender+" / "+scenario+": releasing aim or fire returns to low ready");
                        Check(report,sequence.maximumPitchStep<13,gender+" / "+scenario+": raising and lowering stays smooth between frames");
                    }
                    if(scenario=="idle")
                    {
                        int before=rig.rifle.ShotAnimationStarts;
                        rig.rifle.PlayShot(1);int predicted=rig.rifle.ShotAnimationStarts;
                        rig.rifle.Apply(true,true,1,0,0);
                        Check(report,predicted==before+1&&rig.rifle.ShotAnimationStarts==predicted,
                            gender+": local PlayShot(1) followed by Apply(1) starts only one shot animation");
                    }
                }
                finally{UnityEngine.Object.DestroyImmediate(sheet);rig.root.SetActive(false);}
            }
            foreach(var sequence in report.sequences)
            {
                // Unaimed walking carries the barrel slightly down. Aimed
                // walking must stay level; transitions cannot flip it upward.
                float limit=sequence.scenario.StartsWith("rest")?45f:sequence.scenario=="walk"||sequence.scenario=="run"?40f:sequence.scenario=="aim-walk"?8f:40f;
                Check(report,sequence.maximumNeutralPitch<limit,sequence.gender+" / "+sequence.scenario+": rifle pitch stays below "+limit+" degrees through the entire sequence ("+sequence.maximumNeutralPitch+")");
            }
            report.passed=true;Debug.Log("RIFLE_MOTION_REVIEW_OK checks="+report.checks.Count+" samples="+report.samples.Count);
        }
        catch(Exception error){report.errors.Add(error.ToString());throw;}
        finally
        {
            File.WriteAllText(Path.Combine(folder,"validation.json"),JsonUtility.ToJson(report,true));
            foreach(var root in roots)UnityEngine.Object.DestroyImmediate(root);
            if(cameraObject)UnityEngine.Object.DestroyImmediate(cameraObject);if(keyObject)UnityEngine.Object.DestroyImmediate(keyObject);
            if(fillObject)UnityEngine.Object.DestroyImmediate(fillObject);if(labelObject)UnityEngine.Object.DestroyImmediate(labelObject);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
    static Rig CreateRig(Scene scene,string gender,RuntimeAnimatorController controller)
    {
        var root=ObjectInScene("Motion review "+gender,scene);
        var model=GangsterPlayerVisual.CreateRemoteVisual(gender,controller,root.transform);
        var animator=model.GetComponent<Animator>();animator.Rebind();animator.Update(0);
        var rifle=model.AddComponent<RifleAvatar>();rifle.Configure(animator,root.transform);
        if(!rifle.Ready)throw new InvalidOperationException("Rifle socket calibration is unavailable for "+gender);
        Transform Bone(HumanBodyBones bone)=>animator.GetBoneTransform(bone);
        return new Rig{root=root,animator=animator,rifle=rifle,upper=Bone(HumanBodyBones.RightUpperArm),lower=Bone(HumanBodyBones.RightLowerArm),hand=Bone(HumanBodyBones.RightHand),
            leftUpper=Bone(HumanBodyBones.LeftUpperArm),leftLower=Bone(HumanBodyBones.LeftLowerArm),leftHand=Bone(HumanBodyBones.LeftHand),
            leftFoot=Bone(HumanBodyBones.LeftFoot),rightFoot=Bone(HumanBodyBones.RightFoot),head=Bone(HumanBodyBones.Head)};
    }
    static GameObject ObjectInScene(string name,Scene scene){var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);return go;}
    static void CaptureTile(Scene scene,Rig rig,Camera camera,TextMesh caption,Texture2D sheet,int tile,string label)
    {
        var skins=rig.root.GetComponentsInChildren<SkinnedMeshRenderer>();var meshes=new List<Mesh>();var snapshots=new List<GameObject>();
        try
        {
            foreach(var skin in skins)
            {
                var mesh=(Mesh)SkinWorld.Invoke(null,new object[]{skin});meshes.Add(mesh);
                var snapshot=ObjectInScene("Frozen motion review skin",scene);snapshot.AddComponent<MeshFilter>().sharedMesh=mesh;
                snapshot.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;snapshots.Add(snapshot);skin.enabled=false;
            }
            for(int view=0;view<2;view++)
            {
                camera.transform.position=view==0?new Vector3(0,1.28f,3.3f):new Vector3(3.3f,1.28f,.25f);
                camera.transform.LookAt(new Vector3(0,1.02f,.03f));
                caption.text=label+" / "+(view==0?"front":"side");caption.transform.rotation=camera.transform.rotation;
                var target=new RenderTexture(ViewWidth,ViewHeight,24);var image=new Texture2D(ViewWidth,ViewHeight,TextureFormat.RGB24,false);
                var old=RenderTexture.active;
                try
                {
                    camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                    image.ReadPixels(new Rect(0,0,ViewWidth,ViewHeight),0,0);image.Apply(false);
                    int x=tile%3*ViewWidth*2+view*ViewWidth,y=(1-tile/3)*ViewHeight;
                    sheet.SetPixels(x,y,ViewWidth,ViewHeight,image.GetPixels());
                }
                finally{camera.targetTexture=null;RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(target);}
            }
        }
        finally
        {
            foreach(var snapshot in snapshots)UnityEngine.Object.DestroyImmediate(snapshot);
            foreach(var mesh in meshes)UnityEngine.Object.DestroyImmediate(mesh);
            foreach(var skin in skins)if(skin)skin.enabled=true;
        }
    }
}
