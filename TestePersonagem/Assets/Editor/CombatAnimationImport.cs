using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Only these three FBXs are prepared. Do not install a global Model postprocessor:
// the city has a large import cache and unrelated assets must remain untouched.
[InitializeOnLoad]
public static class CombatAnimationImport
{
    public const string LayerName="Combat";
    const string ControllerPath="Assets/Animations/Locomocao.controller";
    static readonly string[] Names={"PunchCombo","HitReaction","Stunned"};
    static CombatAnimationImport(){EditorApplication.delayCall+=PrepareWhenIdle;}

    static void PrepareWhenIdle()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)
        {EditorApplication.delayCall+=PrepareWhenIdle;return;}
        if(Names.All(name=>File.Exists(PathFor(name))))PrepareAll(false);
    }

    static string PathFor(string name)=>"Assets/Resources/Combat/"+name+".fbx";
    static AnimationClip Clip(string name)=>AssetDatabase.LoadAllAssetsAtPath(PathFor(name))
        .OfType<AnimationClip>().FirstOrDefault(clip=>clip.name==name);

    static void Prepare(string name)
    {
        string path=PathFor(name);
        var importer=AssetImporter.GetAtPath(path) as ModelImporter;
        if(!importer)
        {AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);importer=AssetImporter.GetAtPath(path) as ModelImporter;}
        if(!importer)throw new InvalidOperationException("Animação ausente: "+path);
        bool changed=importer.animationType!=ModelImporterAnimationType.Human||
            importer.avatarSetup!=ModelImporterAvatarSetup.CreateFromThisModel||!importer.importAnimation||
            importer.importCameras||importer.importLights;
        importer.animationType=ModelImporterAnimationType.Human;
        importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation=true;importer.importCameras=false;importer.importLights=false;
        var clips=importer.clipAnimations;
        if(clips.Length!=1||clips[0].name!=name)
        {
            var source=importer.defaultClipAnimations.FirstOrDefault();
            if(source==null)throw new InvalidOperationException("FBX sem take: "+path);
            clips=new[]{new ModelImporterClipAnimation {name=name,takeName=source.takeName,
                firstFrame=source.firstFrame,lastFrame=source.lastFrame}};
            changed=true;
        }
        var clip=clips[0];
        changed|=clip.loopTime||clip.loopPose||!clip.lockRootRotation||!clip.lockRootHeightY||
            !clip.lockRootPositionXZ||!clip.keepOriginalOrientation||!clip.keepOriginalPositionY||
            !clip.keepOriginalPositionXZ||clip.heightFromFeet;
        clip.loopTime=false;clip.loopPose=false;clip.lockRootRotation=true;
        clip.lockRootHeightY=true;clip.lockRootPositionXZ=true;clip.keepOriginalOrientation=true;
        // Preserve the authored hip drop in Stunned. Feet-based height extraction
        // would raise a body lying on the ground back toward standing height.
        clip.keepOriginalPositionY=true;clip.keepOriginalPositionXZ=true;clip.heightFromFeet=false;
        if(changed){importer.clipAnimations=clips;importer.SaveAndReimport();}
    }

    static void EnsureController()
    {
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if(!controller)throw new InvalidOperationException("Controlador do personagem ausente");
        int index=Array.FindIndex(controller.layers,layer=>layer.name==LayerName);
        if(index<0){controller.AddLayer(LayerName);index=controller.layers.Length-1;}
        var layers=controller.layers;var layer=layers[index];
        bool changed=layer.defaultWeight!=0||layer.avatarMask||layer.blendingMode!=AnimatorLayerBlendingMode.Override;
        layer.defaultWeight=0;layer.avatarMask=null;layer.blendingMode=AnimatorLayerBlendingMode.Override;
        var machine=layer.stateMachine;
        var empty=machine.states.Select(x=>x.state).FirstOrDefault(x=>x.name=="Empty");
        if(!empty){empty=machine.AddState("Empty");changed=true;}
        if(empty.motion||!empty.writeDefaultValues){empty.motion=null;empty.writeDefaultValues=true;changed=true;}
        if(machine.defaultState!=empty){machine.defaultState=empty;changed=true;}
        foreach(string name in Names)
        {
            var clip=Clip(name);
            if(!clip||!clip.humanMotion||clip.length<=0)throw new InvalidOperationException("Animação Humanoid inválida: "+name);
            var state=machine.states.Select(x=>x.state).FirstOrDefault(x=>x.name==name);
            if(!state){state=machine.AddState(name);changed=true;}
            if(state.motion!=clip||state.speed!=1||!state.writeDefaultValues||state.transitions.Length>0)
            {
                state.motion=clip;state.speed=1;state.writeDefaultValues=true;
                foreach(var transition in state.transitions)state.RemoveTransition(transition);
                EditorUtility.SetDirty(state);changed=true;
            }
        }
        if(changed){layers[index]=layer;controller.layers=layers;EditorUtility.SetDirty(machine);EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();}
    }

    static void PrepareAll(bool verify)
    {
        foreach(string name in Names)Prepare(name);
        EnsureController();
        if(!verify)return;
        string folder=Path.GetFullPath("../tools/combat-animation");Directory.CreateDirectory(folder);
        var report=new ClipReport {passed=true,layer=LayerName,
            clips=Names.Select(name=>new ClipEntry {name=name,seconds=Clip(name).length,humanMotion=Clip(name).humanMotion}).ToArray()};
        File.WriteAllText(Path.Combine(folder,"clip-validation.json"),JsonUtility.ToJson(report,true));
        Debug.Log("COMBAT_CLIPS_VERIFIED "+string.Join(" ",report.clips.Select(x=>x.name+"="+x.seconds.ToString("0.000",System.Globalization.CultureInfo.InvariantCulture))));
    }

    [Serializable] sealed class ClipEntry {public string name;public float seconds;public bool humanMotion;}
    [Serializable] sealed class ClipReport {public bool passed;public string layer;public ClipEntry[] clips;}
    [MenuItem("Horizonte/Testes locais/Preparar animações de combate")]
    public static void Verify()=>PrepareAll(true);
}
