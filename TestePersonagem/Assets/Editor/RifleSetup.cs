using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class RifleSetup
{
    [MenuItem("Horizonte/Armas/Preparar rifle e animações")]
    public static void Prepare()
    {
        RifleModelImport.Prepare();RifleAnimationImport.Prepare();
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Animations/Locomocao.controller");
        if(!controller)throw new Exception("Controlador do player ausente");
        AnimationClip Load(string name)=>Resources.LoadAll<AnimationClip>("RifleAnimations/"+name).First(c=>c&&c.humanMotion&&!c.name.StartsWith("__preview"));
        EnsureLayer(controller,"Rifle");EnsureLayer(controller,"RifleArms");EnsureLayer(controller,"RifleFire");
        // Keep unrelated layers in their existing relative order; firing is
        // the final weapon layer above the normal gait and rifle arms.
        string[] weaponOrder={"Rifle","RifleArms","RifleFire"};
        var originalLayers=controller.layers;
        controller.layers=originalLayers.Where(layer=>!weaponOrder.Contains(layer.name))
            .Concat(weaponOrder.Select(name=>originalLayers.First(layer=>layer.name==name))).ToArray();
        int carry=Array.FindIndex(controller.layers,layer=>layer.name=="Rifle");
        int arms=Array.FindIndex(controller.layers,layer=>layer.name=="RifleArms");
        int fire=Array.FindIndex(controller.layers,layer=>layer.name=="RifleFire");
        if(!controller.parameters.Any(p=>p.name=="RifleSpeed"))controller.AddParameter("RifleSpeed",AnimatorControllerParameterType.Float);
        var machine=controller.layers[carry].stateMachine;
        var state=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="Locomotion")??machine.AddState("Locomotion");
        var tree=state.motion as BlendTree;
        if(!tree){tree=new BlendTree{name="Rifle locomotion",blendType=BlendTreeType.Simple1D};AssetDatabase.AddObjectToAsset(tree,controller);}
        tree.blendParameter="RifleSpeed";tree.useAutomaticThresholds=false;
        // Keep the established walking/running pelvis and leg tracks. The
        // supplied armed run has a different lateral gait and is not blended.
        var baseTree=controller.layers[0].stateMachine.defaultState.motion as BlendTree;
        var baseWalk=baseTree?baseTree.children.OrderBy(child=>Mathf.Abs(child.threshold-1.65f)).First():default(ChildMotion);
        var baseRun=baseTree?baseTree.children.OrderBy(child=>Mathf.Abs(child.threshold-4.5f)).First():default(ChildMotion);
        var walking=Resources.LoadAll<AnimationClip>("GangsterMusculo/Animations/Walking")
            .FirstOrDefault(clip=>clip&&clip.humanMotion&&!clip.name.StartsWith("__preview"));
        var running=Resources.LoadAll<AnimationClip>("GangsterMusculo/Animations/Running")
            .FirstOrDefault(clip=>clip&&clip.humanMotion&&!clip.name.StartsWith("__preview"));
        if(!walking)throw new Exception("Caminhada normal Humanoid ausente");
        if(!running)throw new Exception("Corrida normal Humanoid ausente");
        float walkCadence=baseWalk.timeScale>0?baseWalk.timeScale:1;
        float runCadence=baseRun.timeScale>0?baseRun.timeScale:1;
        tree.children=new[]{new ChildMotion{motion=Load("Idle"),threshold=0,timeScale=1},
            new ChildMotion{motion=walking,threshold=1.65f,timeScale=walkCadence},
            new ChildMotion{motion=running,threshold=4.5f,timeScale=runCadence}};
        state.motion=tree;state.writeDefaultValues=true;machine.defaultState=state;
        var armsMachine=controller.layers[arms].stateMachine;
        var armsState=armsMachine.states.Select(child=>child.state).FirstOrDefault(child=>child.name=="Locomotion")??armsMachine.AddState("Locomotion");
        var armsTree=armsState.motion as BlendTree;
        if(!armsTree){armsTree=new BlendTree{name="Rifle authored arms",blendType=BlendTreeType.Simple1D};AssetDatabase.AddObjectToAsset(armsTree,controller);}
        armsTree.blendParameter="RifleSpeed";armsTree.useAutomaticThresholds=false;
        armsTree.children=new[]{new ChildMotion{motion=Load("Idle"),threshold=0,timeScale=1},
            new ChildMotion{motion=Load("Idle"),threshold=1.65f,timeScale=1},
            new ChildMotion{motion=Load("Idle"),threshold=4.5f,timeScale=1}};
        armsState.motion=armsTree;armsState.writeDefaultValues=true;armsMachine.defaultState=armsState;
        const string armsMaskPath="Assets/Animations/RifleArmsOnly.mask";
        var armsMask=AssetDatabase.LoadAssetAtPath<AvatarMask>(armsMaskPath);
        if(!armsMask){armsMask=new AvatarMask{name="Rifle arms and fingers only"};AssetDatabase.CreateAsset(armsMask,armsMaskPath);}
        for(int i=0;i<(int)AvatarMaskBodyPart.LastBodyPart;i++)
        {
            var part=(AvatarMaskBodyPart)i;
            // Body includes the hips: never include it in the arm overlay.
            // Socket calibration samples this exact composed reference pose.
            armsMask.SetHumanoidBodyPartActive(part,part==AvatarMaskBodyPart.LeftArm||part==AvatarMaskBodyPart.RightArm||
                part==AvatarMaskBodyPart.LeftFingers||part==AvatarMaskBodyPart.RightFingers);
        }
        var shooting=Load("Shoot");
        var fireMachine=controller.layers[fire].stateMachine;
        var shot=fireMachine.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="Shot")??fireMachine.AddState("Shot");
        shot.motion=shooting;shot.speed=1f;shot.writeDefaultValues=true;fireMachine.defaultState=shot;
        const string maskPath="Assets/Animations/RifleUpperBody.mask";
        var mask=AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath);
        if(!mask){mask=new AvatarMask{name="Rifle upper body"};AssetDatabase.CreateAsset(mask,maskPath);}
        for(int i=0;i<(int)AvatarMaskBodyPart.LastBodyPart;i++)
            mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i,i!=(int)AvatarMaskBodyPart.Root&&i!=(int)AvatarMaskBodyPart.LeftLeg&&i!=(int)AvatarMaskBodyPart.RightLeg&&
                i!=(int)AvatarMaskBodyPart.LeftFootIK&&i!=(int)AvatarMaskBodyPart.RightFootIK);
        var layers=controller.layers;
        layers[carry].defaultWeight=0;layers[carry].avatarMask=null;layers[carry].blendingMode=AnimatorLayerBlendingMode.Override;
        layers[arms].defaultWeight=0;layers[arms].avatarMask=armsMask;layers[arms].blendingMode=AnimatorLayerBlendingMode.Override;
        // Evaluate upper and lower poses at the same locomotion phase. An
        // independent layer clock can separate the hands during deceleration.
        layers[arms].syncedLayerIndex=carry;layers[arms].syncedLayerAffectsTiming=false;
        layers[arms].SetOverrideMotion(state,armsTree);
        layers[fire].defaultWeight=0;layers[fire].avatarMask=mask;layers[fire].blendingMode=AnimatorLayerBlendingMode.Override;
        controller.layers=layers;
        EditorUtility.SetDirty(tree);EditorUtility.SetDirty(armsTree);EditorUtility.SetDirty(armsMask);EditorUtility.SetDirty(mask);EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();
        Debug.Log("RIFLE_SETUP_OK idle="+Load("Idle").length+" walk="+walking.name+" cadence="+walkCadence+" run="+running.name+" runCadence="+runCadence+" shoot="+shooting.length);
    }
    static int EnsureLayer(AnimatorController controller,string name)
    {
        int index=Array.FindIndex(controller.layers,l=>l.name==name);
        if(index<0){controller.AddLayer(name);index=controller.layers.Length-1;}
        return index;
    }

    public static void PrepareVerifyAndBuild()
    {
        Prepare();RifleVerification.Run();ReleaseBuild.Windows();
    }
    public static void PrepareAndVerify(){Prepare();RifleSocketCalibration.Prepare();RifleVerification.Run();RifleNetworkVerification.Verify();}
    public static void ReviewAndPlay(){RifleVerification.Run();RifleGameplayVerification.RunPlay();}
    public static void FixPoseAndPlay()
    {
        Prepare();RifleSocketCalibration.Prepare();RifleGaitVerification.Run();RifleMotionReview.Run();
        RifleGameplayVerification.RunPlay();
    }
    public static void BuildWindowsPreview()
    {
        GameVersionBuildSetup.Apply();
        var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=GameUpdateSetup.Scenes,
            locationPathName="Builds/WindowsRifleReview/Horizonte.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.CompressWithLz4HC});
        if(result.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Build Windows do rifle falhou");
        Debug.Log("RIFLE_WINDOWS_BUILD_OK bytes="+result.summary.totalSize);
    }
}
