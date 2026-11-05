using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

[InitializeOnLoad]
public static class SAPistolReloadSetup
{
    const string Source="Assets/Resources/"+PistolAvatar.ReloadResource+".fbx";
    const string ControllerPath="Assets/Animations/Locomocao.controller";

    static SAPistolReloadSetup(){EditorApplication.delayCall+=PrepareWhenIdle;}

    static void PrepareWhenIdle()
    {
        if(!File.Exists(Source)||!File.Exists(ControllerPath))return;
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)
        {EditorApplication.delayCall+=PrepareWhenIdle;return;}
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if(controller&&controller.layers.Any(l=>l.name==PistolAvatar.ReloadLayerName&&l.stateMachine.defaultState&&l.stateMachine.defaultState.motion))return;
        Prepare();
    }

    [MenuItem("Horizonte/Personagem SA/Preparar recarga da pistola")]
    public static void Prepare()
    {
        AssetDatabase.ImportAsset(Source,ImportAssetOptions.ForceSynchronousImport);
        var importer=(ModelImporter)AssetImporter.GetAtPath(Source);
        importer.animationType=ModelImporterAnimationType.Human;
        importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
        importer.sourceAvatar=null;
        var reference=(ModelImporter)AssetImporter.GetAtPath("Assets/Resources/GangsterMusculo/GangsterMusculo.fbx");
        var description=reference.humanDescription;
        description.human=description.human.Where(b=>b.boneName!="Raybans").ToArray();
        description.skeleton=Array.Empty<SkeletonBone>();description.hasTranslationDoF=false;
        importer.humanDescription=description;
        importer.bakeAxisConversion=true;importer.importAnimation=true;importer.importCameras=importer.importLights=false;
        importer.materialImportMode=ModelImporterMaterialImportMode.None;
        var take=importer.defaultClipAnimations.First();take.name="PistolReload";
        take.loopTime=take.loopPose=false;
        take.lockRootRotation=take.lockRootHeightY=take.lockRootPositionXZ=true;
        take.keepOriginalOrientation=false;take.keepOriginalPositionY=take.keepOriginalPositionXZ=true;
        importer.clipAnimations=new[]{take};importer.SaveAndReimport();
        var clip=AssetDatabase.LoadAllAssetsAtPath(Source).OfType<AnimationClip>().First(c=>c.name==take.name&&c.humanMotion);

        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if(!controller.parameters.Any(p=>p.name==PistolAvatar.ReloadTimeParameter))controller.AddParameter(PistolAvatar.ReloadTimeParameter,AnimatorControllerParameterType.Float);
        if(!controller.layers.Any(l=>l.name==PistolAvatar.ReloadLayerName))controller.AddLayer(PistolAvatar.ReloadLayerName);
        var layers=controller.layers;
        var layer=layers.First(l=>l.name==PistolAvatar.ReloadLayerName);
        layer.defaultWeight=0;
        layer.avatarMask=AssetDatabase.LoadAssetAtPath<AvatarMask>("Assets/Animations/RifleArmsOnly.mask");
        if(!layer.avatarMask)throw new InvalidOperationException("Máscara de braços da recarga ausente.");
        var machine=layer.stateMachine;
        var state=machine.defaultState??machine.AddState("Reload");
        state.motion=clip;state.timeParameter=PistolAvatar.ReloadTimeParameter;state.timeParameterActive=true;
        machine.defaultState=state;controller.layers=layers;
        EditorUtility.SetDirty(state);EditorUtility.SetDirty(machine);EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("SA_PISTOL_RELOAD_READY seconds="+clip.length);
    }
}
