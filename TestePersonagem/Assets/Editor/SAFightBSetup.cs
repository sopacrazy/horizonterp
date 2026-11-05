using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SAFightBSetup
{
    const string Source="Assets/Resources/Combat/FightBCombo.fbx";
    const string Output="Assets/Resources/Combat/FightBCombo_Personagem.anim";
    const string Report="../tools/animation-work/sa-fight-b-ready.txt";
    static SAFightBSetup(){EditorApplication.delayCall+=Auto;}
    static string Stamp=>"SA_FIGHT_B_V1|"+new FileInfo(Source).LastWriteTimeUtc.Ticks;
    static void Auto()
    {
        if(!File.Exists(Source))return;
        if(File.Exists(Report)&&File.ReadAllText(Report).StartsWith(Stamp)&&File.Exists(Output))return;
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)
        {EditorApplication.delayCall+=Auto;return;}
        try{Prepare();}catch(Exception e){File.WriteAllText(Report+".error",e.ToString());Debug.LogException(e);}
    }
    [MenuItem("Horizonte/Personagem SA/Preparar sequencia Fight B 1 2 3")]
    public static void Prepare()
    {
        AssetDatabase.ImportAsset(Source,ImportAssetOptions.ForceSynchronousImport);
        var importer=(ModelImporter)AssetImporter.GetAtPath(Source);
        importer.animationType=ModelImporterAnimationType.Human;
        importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
        importer.sourceAvatar=null;
        var description=((ModelImporter)AssetImporter.GetAtPath("Assets/Resources/GangsterMusculo/GangsterMusculo.fbx")).humanDescription;
        description.human=description.human.Where(b=>b.boneName!="Raybans").ToArray();
        description.skeleton=Array.Empty<SkeletonBone>();description.hasTranslationDoF=false;
        importer.humanDescription=description;
        importer.importAnimation=true;importer.importCameras=importer.importLights=false;importer.bakeAxisConversion=true;
        importer.materialImportMode=ModelImporterMaterialImportMode.None;
        var take=importer.defaultClipAnimations.First();take.name="PunchCombo";
        take.loopTime=take.loopPose=false;
        take.lockRootRotation=take.lockRootHeightY=take.lockRootPositionXZ=true;
        take.keepOriginalOrientation=false;take.keepOriginalPositionY=take.keepOriginalPositionXZ=true;
        take.heightFromFeet=false;
        importer.clipAnimations=new[]{take};importer.SaveAndReimport();
        var source=AssetDatabase.LoadAllAssetsAtPath(Source).OfType<AnimationClip>().First(c=>c.name=="PunchCombo"&&c.humanMotion);
        Calibrate(source);
        AssetDatabase.SaveAssets();
        CombatAnimationImport.Verify();
        Verify();
        File.WriteAllText(Report,Stamp+"\nFightB_1 -> FightB_2 -> FightB_3\nClip="+Output+"\nSeconds="+source.length+"\nSkinChanged=false\n");
        Debug.Log("SA_FIGHT_B_READY - sequencia B1 B2 B3 aplicada ao ataque");
    }
    static PlayableGraph Sample(Animator animator,AnimationClip clip,float time)
    {
        var graph=PlayableGraph.Create("Amostra FightB");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var playable=AnimationClipPlayable.Create(graph,clip);playable.SetApplyFootIK(false);
        AnimationPlayableOutput.Create(graph,"FightB",animator).SetSourcePlayable(playable);
        graph.Play();playable.SetTime(time);graph.Evaluate(0);return graph;
    }
    static float Lowest(GameObject visual)
    {
        float value=float.MaxValue;
        foreach(var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh=new Mesh();
            try{skin.BakeMesh(mesh);foreach(var v in mesh.vertices)value=Mathf.Min(value,skin.transform.TransformPoint(v).y);}
            finally{UnityEngine.Object.DestroyImmediate(mesh);}
        }
        return value;
    }
    static void Calibrate(AnimationClip source)
    {
        var previous=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
        var clip=UnityEngine.Object.Instantiate(source);clip.name="PunchCombo";
        try
        {
            foreach(string side in new[]{"LeftHand","RightHand"})
                foreach(string finger in new[]{"Thumb","Index","Middle","Ring","Little"})
                    for(int joint=1;joint<=3;joint++)
                        AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("",typeof(Animator),side+"."+finger+"."+joint+" Stretched"),
                            AnimationCurve.Constant(0,clip.length,finger=="Thumb"?-.5f:-.85f));
            var root=new GameObject("Contato Fight B no personagem atual");
            var visual=GangsterPlayerVisual.CreateRemoteVisual("male",null,root.transform);
            var animator=visual.GetComponent<Animator>();
            float FootAt(float t){var graph=Sample(animator,clip,t);try{return Lowest(visual);}finally{graph.Destroy();}}
            var binding=EditorCurveBinding.FloatCurve("",typeof(Animator),"RootT.y");
            var original=AnimationUtility.GetEditorCurve(source,binding);
            if(original==null)throw new InvalidOperationException("FightB sem curva de altura Humanoid");
            float baseline=FootAt(.5f);
            AnimationUtility.SetEditorCurve(clip,binding,new AnimationCurve(original.keys.Select(k=>new Keyframe(k.time,k.value+.05f)).ToArray()));
            float response=(FootAt(.5f)-baseline)/.05f;
            if(response<.1f)throw new InvalidOperationException("FightB sem resposta de altura");
            AnimationUtility.SetEditorCurve(clip,binding,original);
            int count=Mathf.CeilToInt(clip.length*60);
            var keys=new List<Keyframe>();
            for(int i=0;i<=count;i++)
            {
                float t=clip.length*i/count;
                keys.Add(new Keyframe(t,original.Evaluate(t)+Mathf.Max(0,-.012f-FootAt(t))/response));
            }
            var curve=new AnimationCurve(keys.ToArray());
            for(int i=0;i<curve.length;i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);
            }
            AnimationUtility.SetEditorCurve(clip,binding,curve);
            var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(Output);
            if(existing){EditorUtility.CopySerialized(clip,existing);EditorUtility.SetDirty(existing);UnityEngine.Object.DestroyImmediate(clip);}
            else {AssetDatabase.CreateAsset(clip,Output);clip.name="PunchCombo";EditorUtility.SetDirty(clip);}
        }
        finally{EditorSceneManager.CloseScene(scene,true);if(previous.IsValid())SceneManager.SetActiveScene(previous);}
    }
    [Serializable] public class Frame{public float t,soleY,hipOffset;public Vector3 hip,leftHand,rightHand;}
    [Serializable] public class Result{public bool passed;public float length,minSole,maxSole,maxSoleStep,maxHipOffset,maxHipStep;public string[] order;public float[] stageHandTravel;public Frame[] frames;}
    static void Verify()
    {
        var previous=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
        try
        {
            var controller=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/Locomocao.controller");
            var root=new GameObject("Verificacao Fight B");
            var visual=GangsterPlayerVisual.CreateRemoteVisual("male",controller,root.transform);
            var animator=visual.GetComponent<Animator>();
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(Output);
            var graph=PlayableGraph.Create("FightB no controlador real");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var playable=AnimatorControllerPlayable.Create(graph,controller);
            AnimationPlayableOutput.Create(graph,"FightB",animator).SetSourcePlayable(playable);graph.Play();
            var frames=new List<Frame>();
            try
            {
                int layer=playable.GetLayerIndex("Combat");playable.SetLayerWeight(layer,1);
                for(int i=0;i<=Mathf.CeilToInt((clip.length-.5f)*60);i++)
                {
                    float t=Mathf.Min(clip.length-.0001f,.5f+i/60f);
                    playable.Play("Combat.PunchCombo",layer,t/clip.length);graph.Evaluate(0);
                    var hip=animator.GetBoneTransform(HumanBodyBones.Hips).position;
                    frames.Add(new Frame{t=t,soleY=Lowest(visual),hip=hip,hipOffset=new Vector2(hip.x,hip.z).magnitude,
                        leftHand=animator.GetBoneTransform(HumanBodyBones.LeftHand).position,rightHand=animator.GetBoneTransform(HumanBodyBones.RightHand).position});
                }
            }
            finally{graph.Destroy();}
            var starts=new[]{.5f,34/30f,64/30f,100/30f};
            var travel=new List<float>();
            for(int k=0;k<3;k++)
            {
                var stage=frames.Where(f=>f.t>=starts[k]&&f.t<starts[k+1]).ToArray();
                float distance=0;
                for(int i=1;i<stage.Length;i++)distance+=Vector3.Distance(stage[i-1].leftHand,stage[i].leftHand)+Vector3.Distance(stage[i-1].rightHand,stage[i].rightHand);
                travel.Add(distance);
            }
            var result=new Result{length=clip.length,order=new[]{"FightB_1","FightB_2","FightB_3"},stageHandTravel=travel.ToArray(),frames=frames.ToArray(),
                minSole=frames.Min(f=>f.soleY),maxSole=frames.Max(f=>f.soleY),maxHipOffset=frames.Max(f=>f.hipOffset),
                maxSoleStep=Enumerable.Range(1,frames.Count-1).Max(i=>Mathf.Abs(frames[i].soleY-frames[i-1].soleY)),
                maxHipStep=Enumerable.Range(1,frames.Count-1).Max(i=>Vector3.Distance(frames[i].hip,frames[i-1].hip))};
            // Strikes include a deliberate half-metre lean/step. Check its
            // continuity rather than applying the much smaller idle sway limit.
            result.passed=clip.humanMotion&&clip.length>3&&result.minSole>-.04f&&result.maxSole<.08f&&result.maxHipOffset<.75f&&result.maxHipStep<.12f&&result.maxSoleStep<.05f&&travel.All(v=>v>.2f);
            File.WriteAllText("../tools/animation-work/sa-fight-b-verification.json",JsonUtility.ToJson(result,true));
            var flags=BindingFlags.Static|BindingFlags.NonPublic;
            var render=typeof(SACharacterLocomotionSetup).GetMethod("ReviewPoseAt",flags);
            GameObject.CreatePrimitive(PrimitiveType.Plane).name="Chao y=0";
            var light=new GameObject("Luz de revisao Fight B").AddComponent<Light>();
            light.type=LightType.Directional;light.intensity=1.2f;light.transform.rotation=Quaternion.Euler(45,-35,0);
            // Separate poses from each strike, not three copies of the same time.
            foreach(var item in new[]{new[]{"1","0.82"},new[]{"2","1.65"},new[]{"3","2.65"}})
            {
                float t=float.Parse(item[1],System.Globalization.CultureInfo.InvariantCulture);
                render.Invoke(null,new object[]{animator,clip,"sa-fight-b-"+item[0]+".png",t});
            }
            if(!result.passed)throw new InvalidOperationException("Fight B falhou na verificacao de contato, deslocamento ou movimento. Veja sa-fight-b-verification.json");
        }
        finally{EditorSceneManager.CloseScene(scene,true);if(previous.IsValid())SceneManager.SetActiveScene(previous);}
    }
}
