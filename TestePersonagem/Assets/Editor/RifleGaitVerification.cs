using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Compare identical actual rigs at the same controller phase, with and without
// the rifle arm layer. A held weapon must not change the established leg gait.
public static class RifleGaitVerification
{
    const float Step=1f/30f;
    static readonly HumanBodyBones[] LowerBones={HumanBodyBones.Hips,HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,
        HumanBodyBones.LeftFoot,HumanBodyBones.LeftToes,HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,
        HumanBodyBones.RightFoot,HumanBodyBones.RightToes};
    [Serializable] sealed class Sequence
    {
        public string gender,scenario;
        public int frames;
        public float maxLowerPositionError,maxLowerRotationError,maxHipPositionError,maxHipRotationError,maxHandDifference,hipLateralRange;
    }
    [Serializable] sealed class Report
    {
        public bool passed;
        public List<string> checks=new List<string>(),errors=new List<string>();
        public List<Sequence> sequences=new List<Sequence>();
    }
    static void Check(Report report,bool ok,string text)
    {if(!ok)throw new InvalidOperationException(text);report.checks.Add(text);}

    [MenuItem("Horizonte/Testes locais/Verificar pernas da corrida armada")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Pare o Play antes de verificar a corrida armada.");
        var report=new Report();var scene=EditorSceneManager.NewPreviewScene();var roots=new List<GameObject>();
        try
        {
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Animations/Locomocao.controller");
            int carry=Array.FindIndex(controller.layers,layer=>layer.name=="Rifle");
            int arms=Array.FindIndex(controller.layers,layer=>layer.name=="RifleArms");
            Check(report,carry>=0&&arms>=0,"Rifle carry and arms layers are available");
            var mask=controller.layers[arms].avatarMask;
            Check(report,mask&&!mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Body)&&!mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Root)&&
                !mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg)&&!mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg)&&
                !mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK)&&!mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK),
                "Rifle arms mask excludes pelvis/body, root, legs and foot IK");
            var carryTree=controller.layers[carry].stateMachine.defaultState.motion as BlendTree;
            var running=Resources.LoadAll<AnimationClip>("GangsterMusculo/Animations/Running").First(clip=>clip&&clip.humanMotion&&!clip.name.StartsWith("__preview"));
            var runChild=carryTree.children.OrderBy(child=>Mathf.Abs(child.threshold-4.5f)).First();
            Check(report,runChild.motion==running,"Armed running uses the established normal Running clip");
            float duration=Mathf.Max(3f,2*running.length/runChild.timeScale);
            foreach(string gender in new[]{"male","female"})
            foreach(string scenario in new[]{"run","walk","run-stop","walk-run"})
            {
                var reference=Create(scene,roots,gender,controller,carry,arms,false);
                var armed=Create(scene,roots,gender,controller,carry,arms,true);
                var result=new Sequence{gender=gender,scenario=scenario,frames=Mathf.CeilToInt(duration/Step)};report.sequences.Add(result);
                var refBones=LowerBones.Select(bone=>reference.GetBoneTransform(bone)).ToArray();
                var armBones=LowerBones.Select(bone=>armed.GetBoneTransform(bone)).ToArray();
                // Toes are optional in a valid Humanoid (ByBeyby has none).
                Check(report,refBones.Take(4).All(bone=>bone)&&armBones.Take(4).All(bone=>bone)&&
                    Enumerable.Range(5,3).All(index=>refBones[index]&&armBones[index]),gender+" / "+scenario+": required hips, leg and foot bones exist");
                float hipMin=float.PositiveInfinity,hipMax=float.NegativeInfinity;
                for(int frame=0;frame<result.frames;frame++)
                {
                    float speed=scenario=="walk"?1.65f:scenario=="run-stop"&&frame>result.frames/2?0:
                        scenario=="walk-run"&&frame<result.frames/2?1.65f:4.5f;
                    foreach(var animator in new[]{reference,armed})
                    {animator.SetFloat("Speed",speed);animator.SetFloat("RifleSpeed",speed,.09f,Step);animator.SetBool("Jumping",false);animator.Update(Step);}
                    for(int bone=0;bone<LowerBones.Length;bone++)
                    {
                        if(!refBones[bone]||!armBones[bone])continue;
                        float position=Vector3.Distance(refBones[bone].position,armBones[bone].position);
                        float rotation=Quaternion.Angle(refBones[bone].localRotation,armBones[bone].localRotation);
                        result.maxLowerPositionError=Mathf.Max(result.maxLowerPositionError,position);
                        result.maxLowerRotationError=Mathf.Max(result.maxLowerRotationError,rotation);
                        if(bone==0){result.maxHipPositionError=Mathf.Max(result.maxHipPositionError,position);result.maxHipRotationError=Mathf.Max(result.maxHipRotationError,rotation);}
                    }
                    float hipX=armed.transform.InverseTransformPoint(armBones[0].position).x;
                    hipMin=Mathf.Min(hipMin,hipX);hipMax=Mathf.Max(hipMax,hipX);
                    result.maxHandDifference=Mathf.Max(result.maxHandDifference,Vector3.Distance(reference.GetBoneTransform(HumanBodyBones.RightHand).position,
                        armed.GetBoneTransform(HumanBodyBones.RightHand).position));
                }
                result.hipLateralRange=hipMax-hipMin;
                Check(report,result.maxHipPositionError<.005f&&result.maxHipRotationError<.5f,gender+" / "+scenario+": arm overlay leaves the normal pelvis unchanged (<5mm/0.5 degree)");
                Check(report,result.maxLowerPositionError<.01f&&result.maxLowerRotationError<.5f,gender+" / "+scenario+": legs and feet retain the normal gait (<1cm/0.5 degree)");
                Check(report,result.maxHandDifference>.02f,gender+" / "+scenario+": arm overlay actually changes the hand pose");
                reference.gameObject.SetActive(false);armed.gameObject.SetActive(false);
            }
            report.passed=true;Debug.Log("RIFLE_GAIT_VERIFIED checks="+report.checks.Count+" sequences="+report.sequences.Count);
        }
        catch(Exception error){report.errors.Add(error.ToString());throw;}
        finally
        {
            string folder=Path.GetFullPath("../tools/rifle-motion-review");Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"gait-verification.json"),JsonUtility.ToJson(report,true));
            foreach(var root in roots)UnityEngine.Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);
        }
    }
    static Animator Create(Scene scene,List<GameObject> roots,string gender,RuntimeAnimatorController controller,int carry,int arms,bool armLayer)
    {
        var root=new GameObject("Gait twin "+gender+" "+armLayer);SceneManager.MoveGameObjectToScene(root,scene);roots.Add(root);
        var model=GangsterPlayerVisual.CreateRemoteVisual(gender,controller,root.transform);
        var animator=model.GetComponent<Animator>();animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.Rebind();
        for(int index=1;index<animator.layerCount;index++)animator.SetLayerWeight(index,0);
        animator.SetLayerWeight(carry,1);animator.SetLayerWeight(arms,armLayer?1:0);
        animator.Play("Rifle.Locomotion",carry,0);animator.Update(0);return animator;
    }
}
