using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Explicit per-file preparation: no global model postprocessor or map reimport.
[InitializeOnLoad]
public static class PoliceCarAnimationImport
{
    static readonly string[] Paths={"Assets/Resources/Vehicles/Police/ExitingCar.fbx","Assets/Resources/Vehicles/Police/EnteringCar.fbx"};
    static PoliceCarAnimationImport(){EditorApplication.delayCall+=PrepareWhenIdle;}
    static void PrepareWhenIdle()
    {
        if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)
        {EditorApplication.delayCall+=PrepareWhenIdle;return;}
        foreach(var path in Paths)if(File.Exists(path))Prepare(path,false);
    }
    static void Prepare(string path,bool verify)
    {
        var importer=AssetImporter.GetAtPath(path) as ModelImporter;
        if(!importer){AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);importer=AssetImporter.GetAtPath(path) as ModelImporter;}
        if(!importer)throw new Exception("Animação ausente: "+path);
        bool changed=importer.animationType!=ModelImporterAnimationType.Human||!importer.importAnimation;
        importer.animationType=ModelImporterAnimationType.Human;importer.avatarSetup=ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation=true;importer.importCameras=false;importer.importLights=false;
        var clips=importer.clipAnimations.Length>0?importer.clipAnimations:importer.defaultClipAnimations;
        if(!clips.Any(c=>c.name=="RearMirrored"))
        {
            var source=clips.First(c=>!c.name.StartsWith("__preview"));
            clips=clips.Concat(new[]{new ModelImporterClipAnimation {
                name="RearMirrored",takeName=source.takeName,firstFrame=source.firstFrame,lastFrame=source.lastFrame,
                mirror=true,keepOriginalPositionY=source.keepOriginalPositionY,
                keepOriginalOrientation=true,keepOriginalPositionXZ=true,
                lockRootRotation=true,lockRootHeightY=true,lockRootPositionXZ=true
            }}).ToArray();
            changed=true;
        }
        foreach(var clip in clips)
        {
            changed|=clip.loopTime||!clip.lockRootRotation||!clip.lockRootHeightY||!clip.lockRootPositionXZ;
            clip.loopTime=false;clip.loopPose=false;clip.lockRootRotation=true;clip.lockRootHeightY=true;clip.lockRootPositionXZ=true;
            clip.keepOriginalOrientation=true;clip.keepOriginalPositionXZ=true;
        }
        if(changed){importer.clipAnimations=clips;importer.SaveAndReimport();}
        if(!verify)return;
        var motions=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(x=>!x.name.StartsWith("__preview")).ToArray();
        var motion=motions.FirstOrDefault(x=>x.name!="RearMirrored");
        if(!motion||!motion.humanMotion||motion.length<=0)throw new Exception("Animação Humanoid inválida: "+path);
        if(!motions.Any(x=>x.name=="RearMirrored"&&x.humanMotion))throw new Exception("Animação traseira espelhada ausente: "+path);
        Directory.CreateDirectory("../tools/police-car-animation");
        File.WriteAllText("../tools/police-car-animation/"+System.IO.Path.GetFileNameWithoutExtension(path)+"-validation.txt","Humanoid=true\nclip="+motion.name+"\nseconds="+motion.length+"\nPlayback=forward\n");
        Debug.Log("CAR_CLIP_VERIFIED "+path+" seconds="+motion.length);
    }
    public static void Verify(){foreach(var path in Paths)Prepare(path,true);}
}
