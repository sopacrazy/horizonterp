using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SAExpandedSetup
{
    const string Folder="Assets/Resources/SAExpanded/";
    const string Report="../tools/animation-work/sa-expanded-ready.txt";
    static readonly string[] Names={"HitReaction","PistolIdle","PistolWalk","PistolRun","PistolAim","PistolAimWalk","RifleIdle","RifleWalk","RifleRun","RifleAim","RifleAimWalk","RifleShoot"};
    [Serializable] class Entry{public string name;public float sourceSpeed;}
    [Serializable] class Entries{public Entry[] clips;}
    static SAExpandedSetup(){EditorApplication.delayCall+=Auto;}
    static void Auto()
    {
        if(Names.Any(n=>!File.Exists(Folder+n+".fbx")))return;
        if(File.Exists(Report)&&File.ReadAllText(Report).Contains("SA_WEAPONS_REVISED_V1"))return;
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)
        {EditorApplication.delayCall+=Auto;return;}
        try{Prepare();}catch(Exception e){File.WriteAllText(Report+".error",e.ToString());Debug.LogException(e);}
    }
    [MenuItem("Horizonte/Personagem SA/Preparar reacao e pistola")]
    public static void Prepare()
    {
        var clips=new Dictionary<string,AnimationClip>();
        foreach(string name in Names)
        {
            string path=Folder+name+".fbx";AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType=ModelImporterAnimationType.Human;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;importer.sourceAvatar=null;
            var desc=((ModelImporter)AssetImporter.GetAtPath("Assets/Resources/GangsterMusculo/GangsterMusculo.fbx")).humanDescription;
            desc.human=desc.human.Where(b=>b.boneName!="Raybans").ToArray();desc.skeleton=Array.Empty<SkeletonBone>();desc.hasTranslationDoF=false;
            importer.humanDescription=desc;importer.bakeAxisConversion=true;importer.importAnimation=true;importer.importCameras=importer.importLights=false;
            importer.materialImportMode=ModelImporterMaterialImportMode.None;
            var take=importer.defaultClipAnimations.First();take.name=name=="HitReaction"?"HitReactionSA":name;
            take.loopTime=take.loopPose=name!="HitReaction"&&name!="RifleShoot";take.lockRootRotation=take.lockRootHeightY=take.lockRootPositionXZ=true;
            take.keepOriginalOrientation=false;take.keepOriginalPositionY=take.keepOriginalPositionXZ=true;take.heightFromFeet=false;
            importer.clipAnimations=new[]{take};importer.SaveAndReimport();
            clips[name]=Ground(AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c=>c.humanMotion&&!c.name.StartsWith("__preview")),Folder+name+"_Personagem.anim");
        }
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Animations/Locomocao.controller");
        if(!controller.parameters.Any(p=>p.name=="SAPistolSpeed"))controller.AddParameter("SAPistolSpeed",AnimatorControllerParameterType.Float);
        if(!controller.parameters.Any(p=>p.name=="SAPistolAim"))controller.AddParameter("SAPistolAim",AnimatorControllerParameterType.Bool);
        int index=Array.FindIndex(controller.layers,l=>l.name=="SAPistol");
        if(index<0){controller.AddLayer("SAPistol");index=controller.layers.Length-1;}
        var layers=controller.layers;var layer=layers[index];layer.defaultWeight=0;layer.blendingMode=AnimatorLayerBlendingMode.Override;
        var machine=layer.stateMachine;
        var carry=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="Carry")??machine.AddState("Carry");
        var aim=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="Aim")??machine.AddState("Aim");
        var rates=JsonUtility.FromJson<Entries>("{\"clips\":"+File.ReadAllText("../tools/animation-work/sa-expanded-export.json")+"}").clips;
        float Rate(string name,float speed)=>speed/Mathf.Max(.1f,rates.First(e=>e.name==name).sourceSpeed);
        BlendTree Tree(AnimatorState state,string[] names,float[] thresholds)
        {
            var tree=state.motion as BlendTree;
            if(!tree){tree=new BlendTree{name="SA "+state.name};AssetDatabase.AddObjectToAsset(tree,controller);state.motion=tree;}
            tree.blendType=BlendTreeType.Simple1D;tree.blendParameter="SAPistolSpeed";tree.useAutomaticThresholds=false;
            tree.children=names.Select((n,i)=>new ChildMotion{motion=clips[n],threshold=thresholds[i],timeScale=i==0?1:Rate(n,thresholds[i])}).ToArray();
            EditorUtility.SetDirty(tree);EditorUtility.SetDirty(state);return tree;
        }
        Tree(carry,new[]{"PistolIdle","PistolWalk","PistolRun"},new[]{0f,1.65f,4.5f});
        Tree(aim,new[]{"PistolAim","PistolAimWalk"},new[]{0f,1.65f});
        foreach(var state in new[]{carry,aim})foreach(var transition in state.transitions)state.RemoveTransition(transition);
        var toAim=carry.AddTransition(aim);toAim.hasExitTime=false;toAim.hasFixedDuration=true;toAim.duration=.1f;toAim.AddCondition(AnimatorConditionMode.If,0,"SAPistolAim");
        var toCarry=aim.AddTransition(carry);toCarry.hasExitTime=false;toCarry.hasFixedDuration=true;toCarry.duration=.1f;toCarry.AddCondition(AnimatorConditionMode.IfNot,0,"SAPistolAim");
        machine.defaultState=carry;layers[index]=layer;controller.layers=layers;EditorUtility.SetDirty(controller);
        SetupRifle(controller,clips,rates);
        AssetDatabase.SaveAssets();CombatAnimationImport.Verify();
        CaptureSocketsAndReview(clips,controller);
        File.WriteAllText(Report,"SA_WEAPONS_REVISED_V1\nPistol: colt45_fire, one hand; normal light-weapon gait\nRifle: IDLE_armed/WALK_armed/run_armed; rifle.RIFLE_fire\nPhysical pistol grip and muzzle calibrated from mesh\nSkinChanged=false\n");
        Debug.Log("SA_EXPANDED_READY - reacao por golpe e pistola");
    }
    static PlayableGraph Sample(Animator animator,AnimationClip clip,float time)
    {
        var graph=PlayableGraph.Create("SA expanded sample");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var playable=AnimationClipPlayable.Create(graph,clip);playable.SetApplyFootIK(false);
        AnimationPlayableOutput.Create(graph,"SA",animator).SetSourcePlayable(playable);graph.Play();playable.SetTime(time);graph.Evaluate(0);return graph;
    }
    static void SetupRifle(AnimatorController controller,Dictionary<string,AnimationClip> clips,Entry[] rates)
    {
        foreach(var name in new[]{"SARifleSpeed","SARifleAim"})if(!controller.parameters.Any(p=>p.name==name))controller.AddParameter(name,name.EndsWith("Aim")?AnimatorControllerParameterType.Bool:AnimatorControllerParameterType.Float);
        foreach(string name in new[]{"SARifle","SARifleFire"})if(!controller.layers.Any(l=>l.name==name))controller.AddLayer(name);
        var layers=controller.layers;int index=Array.FindIndex(layers,l=>l.name=="SARifle");var layer=layers[index];
        layer.defaultWeight=0;layer.blendingMode=AnimatorLayerBlendingMode.Override;layer.avatarMask=null;
        var machine=layer.stateMachine;
        var carry=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="Carry")??machine.AddState("Carry");
        var aim=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="Aim")??machine.AddState("Aim");
        void Tree(AnimatorState state,string[] names,float[] speeds)
        {
            var tree=state.motion as BlendTree;if(!tree){tree=new BlendTree{name="SA rifle "+state.name};AssetDatabase.AddObjectToAsset(tree,controller);state.motion=tree;}
            tree.blendType=BlendTreeType.Simple1D;tree.blendParameter="SARifleSpeed";tree.useAutomaticThresholds=false;
            tree.children=names.Select((n,i)=>new ChildMotion{motion=clips[n],threshold=speeds[i],timeScale=i==0?1:speeds[i]/Mathf.Max(.1f,rates.First(r=>r.name==n).sourceSpeed)}).ToArray();EditorUtility.SetDirty(tree);EditorUtility.SetDirty(state);
        }
        Tree(carry,new[]{"RifleIdle","RifleWalk","RifleRun"},new[]{0f,1.65f,4.5f});
        Tree(aim,new[]{"RifleAim","RifleAimWalk"},new[]{0f,1.65f});
        foreach(var state in new[]{carry,aim})foreach(var transition in state.transitions)state.RemoveTransition(transition);
        var a=carry.AddTransition(aim);a.hasExitTime=false;a.hasFixedDuration=true;a.duration=.12f;a.AddCondition(AnimatorConditionMode.If,0,"SARifleAim");
        var b=aim.AddTransition(carry);b.hasExitTime=false;b.hasFixedDuration=true;b.duration=.12f;b.AddCondition(AnimatorConditionMode.IfNot,0,"SARifleAim");
        machine.defaultState=carry;layers[index]=layer;
        int fire=Array.FindIndex(layers,l=>l.name=="SARifleFire");var fireLayer=layers[fire];fireLayer.defaultWeight=0;fireLayer.blendingMode=AnimatorLayerBlendingMode.Override;
        fireLayer.avatarMask=AssetDatabase.LoadAssetAtPath<AvatarMask>("Assets/Animations/RifleArmsOnly.mask");
        var shot=fireLayer.stateMachine.states.Select(s=>s.state).FirstOrDefault(s=>s.name=="Shot")??fireLayer.stateMachine.AddState("Shot");shot.motion=clips["RifleShoot"];shot.speed=1;fireLayer.stateMachine.defaultState=shot;
        layers[fire]=fireLayer;controller.layers=layers;EditorUtility.SetDirty(shot);EditorUtility.SetDirty(controller);
    }
    static float Lowest(GameObject visual)
    {
        float low=float.MaxValue;
        foreach(var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh=new Mesh();try{skin.BakeMesh(mesh);foreach(var v in mesh.vertices)low=Mathf.Min(low,skin.transform.TransformPoint(v).y);}
            finally{UnityEngine.Object.DestroyImmediate(mesh);}
        }
        return low;
    }
    static AnimationClip Ground(AnimationClip source,string path)
    {
        var previous=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
        var clip=UnityEngine.Object.Instantiate(source);clip.name=source.name;
        try
        {
            var visual=GangsterPlayerVisual.CreateRemoteVisual("male",null,new GameObject("Calibracao SA").transform);var animator=visual.GetComponent<Animator>();
            float Sole(float t){var graph=Sample(animator,clip,t);try{return Lowest(visual);}finally{graph.Destroy();}}
            var binding=EditorCurveBinding.FloatCurve("",typeof(Animator),"RootT.y");var original=AnimationUtility.GetEditorCurve(source,binding);
            float baseline=Sole(0);
            AnimationUtility.SetEditorCurve(clip,binding,new AnimationCurve(original.keys.Select(k=>new Keyframe(k.time,k.value+.05f)).ToArray()));
            float response=(Sole(0)-baseline)/.05f;if(response<.1f)throw new InvalidOperationException("Sem resposta de altura: "+source.name);
            AnimationUtility.SetEditorCurve(clip,binding,original);
            int count=Mathf.CeilToInt(clip.length*60);var keys=new List<Keyframe>();
            for(int i=0;i<=count;i++){float t=clip.length*i/count;keys.Add(new Keyframe(t,original.Evaluate(t)+Mathf.Max(0,-.012f-Sole(t))/response));}
            var curve=new AnimationCurve(keys.ToArray());for(int i=0;i<curve.length;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);}
            AnimationUtility.SetEditorCurve(clip,binding,curve);
            var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(existing){EditorUtility.CopySerialized(clip,existing);UnityEngine.Object.DestroyImmediate(clip);clip=existing;EditorUtility.SetDirty(clip);}else AssetDatabase.CreateAsset(clip,path);
            clip.name=source.name;EditorUtility.SetDirty(clip);
            return clip;
        }
        finally{EditorSceneManager.CloseScene(scene,true);if(previous.IsValid())SceneManager.SetActiveScene(previous);}
    }
    static void CaptureSocketsAndReview(Dictionary<string,AnimationClip> clips,RuntimeAnimatorController controller)
    {
        var previous=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
        try
        {
            var poses=new List<RifleSocketPose>();var riflePoses=new List<RifleSocketPose>();
            foreach(string gender in new[]{"male","female"})
            {
                var root=new GameObject("Socket pistola "+gender);var visual=GangsterPlayerVisual.CreateRemoteVisual(gender,controller,root.transform);var animator=visual.GetComponent<Animator>();
                var graph=Sample(animator,clips["PistolAim"],0);
                var gun=PistolAvatar.CreateWeapon(root.transform);var grip=new ShootingGrip();grip.Configure(animator,gun,gun.Find("Muzzle"),root.transform,false);
                poses.Add(grip.CaptureAuthoredPistol(animator,root.transform.rotation));graph.Destroy();gun.gameObject.SetActive(false);
                graph=Sample(animator,clips["RifleAim"],0);
                var rifle=UnityEngine.Object.Instantiate(Resources.Load<GameObject>("Weapons/Rifle/Rifle"),root.transform,false).transform;
                var rifleGrip=new ShootingGrip();rifleGrip.Configure(animator,rifle,rifle.Find("Muzzle"),root.transform);
                riflePoses.Add(rifleGrip.CaptureAuthoredRifle(animator,root.transform.rotation));graph.Destroy();root.SetActive(false);
            }
            File.WriteAllText(Folder+"PistolSockets.json",JsonUtility.ToJson(new RifleSocketCalibrationData{entries=poses.ToArray()},true));AssetDatabase.ImportAsset(Folder+"PistolSockets.json");
            File.WriteAllText(Folder+"RifleSockets.json",JsonUtility.ToJson(new RifleSocketCalibrationData{entries=riflePoses.ToArray()},true));AssetDatabase.ImportAsset(Folder+"RifleSockets.json");
        }
        finally{EditorSceneManager.CloseScene(scene,true);if(previous.IsValid())SceneManager.SetActiveScene(previous);}
    }
}
