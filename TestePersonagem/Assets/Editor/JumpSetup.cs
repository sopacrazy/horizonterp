using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;

public static class JumpSetup
{
    public static void PrepareAndBuild()
    {
        RetargetIdle.ApplyJump();
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/JumpRetarget.anim");
        if(!clip||clip.isLooping||clip.length<1)throw new Exception("Conversão do Jump inválida");
        PrototypeSetup.BuildWindows();
    }
    public static void Inspect()
    {
        const string path="Assets/Animations/Source/Jump.fbx";
        AssetDatabase.Refresh();
        var importer=(ModelImporter)AssetImporter.GetAtPath(path);
        importer.animationType=ModelImporterAnimationType.Generic;
        importer.importAnimation=true;importer.animationCompression=ModelImporterAnimationCompression.Off;
        importer.SaveAndReimport();
        var clip=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview"));
        var model=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        var bones=model.GetComponentsInChildren<Transform>();
        var hip=bones.First(t=>t.name.EndsWith("Hips")||t.name==" Pelvis");
        var feet=bones.Where(t=>t.name.EndsWith("Foot")||t.name==" L Foot"||t.name==" R Foot").ToArray();
        using(var report=new StreamWriter("../tools/jump-source-inspection.txt"))
        {
            report.WriteLine("Duration="+clip.length+"\nFrames="+clip.frameRate+"\nBones="+string.Join(",",bones.Select(t=>t.name)));
            for(int i=0;i<=40;i++)
            {
                float time=clip.length*i/40f;clip.SampleAnimation(model,time);
                report.WriteLine($"{time:F3} hips={hip.position} feet={string.Join(",",feet.Select(t=>t.position.ToString("F3")))}");
            }
        }
        UnityEngine.Object.DestroyImmediate(model);
    }
}
