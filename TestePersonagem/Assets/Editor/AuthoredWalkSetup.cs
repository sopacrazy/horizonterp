using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class AuthoredWalkSetup
{
    internal const string Source="Assets/Animations/Source/Walking.fbx";
    const string Output="Assets/Animations/WalkingRetarget.anim";
    public static void InspectRigFrames()
    {
        var source=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Source));
        var target=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Personagem/ByBeyby.fbx"));
        RetargetIdle.RestoreBindPose(source);RetargetIdle.RestoreBindPose(target);
        var src=source.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name,t=>t);
        var dst=target.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name,t=>t);
        var lines=new List<string>();
        lines.Add("source parent="+src[" Pelvis"].parent.name+" world="+src[" Pelvis"].parent.rotation.eulerAngles+" target parent="+dst[" Pelvis"].parent.name+" world="+dst[" Pelvis"].parent.rotation.eulerAngles);
        foreach(var name in new[]{"Root"," Pelvis"," L Thigh"," L Calf"," L Foot"," L Toe0"," R Thigh"," R Calf"," R Foot"})
        {
            if(!src.ContainsKey(name)||!dst.ContainsKey(name))continue;
            lines.Add(name+" localDifference="+Quaternion.Angle(src[name].localRotation,dst[name].localRotation)+" worldDifference="+Quaternion.Angle(src[name].rotation,dst[name].rotation)+" positionDifference="+Vector3.Distance(src[name].position,dst[name].position));
        }
        Directory.CreateDirectory("../tools/authored-walk");File.WriteAllLines("../tools/authored-walk/rig-frames.txt",lines);
        UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(target);Debug.Log("RIG_FRAMES_INSPECTED");
    }
    public static void InspectFeet()
    {
        var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Personagem/ByBeyby.fbx"));
        RetargetIdle.RestoreBindPose(model);
        var bones=model.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name,t=>t);
        var rest=bones.ToDictionary(p=>p.Key,p=>p.Value.rotation);
        var lines=new List<string>();
        foreach(var path in new[]{"Assets/Animations/IdleRetarget.anim",Output})
        {
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);lines.Add("CLIP "+path);
            for(int i=0;i<48;i++)
            {
                clip.SampleAnimation(model,clip.length*i/48);
                foreach(string side in new[]{" L"," R"})
                {
                    var foot=bones[side+" Foot"];var hip=bones[side+" Thigh"].position;var knee=bones[side+" Calf"].position;
                    var delta=foot.rotation*Quaternion.Inverse(rest[side+" Foot"]);var f=delta*Vector3.forward;var r=delta*Vector3.right;
                    var plane=Vector3.Cross(knee-hip,foot.position-knee).normalized;
                    lines.Add(i+side+" height="+foot.position.y+" yaw="+Mathf.Atan2(f.x,f.z)*Mathf.Rad2Deg+" roll="+Mathf.Asin(Mathf.Clamp(r.y,-1,1))*Mathf.Rad2Deg+" plane="+plane.ToString("F3"));
                }
            }
        }
        Directory.CreateDirectory("../tools/authored-walk");File.WriteAllLines("../tools/authored-walk/feet.txt",lines);
        UnityEngine.Object.DestroyImmediate(model);Debug.Log("FEET_INSPECTED");
    }
    public static void Apply()
    {
        if(!File.Exists(Source))throw new Exception("Arquivo Walking.fbx ausente");
        RetargetIdle.Transfer(Source,Output,1,"authored-walking",true);
        FootAlignmentCalibration.Apply();
        WalkPelvisBalance.Apply();
        WalkTorsoPosture.Apply();
    }
    public static void ApplyAndBuild()
    {
        var before=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<AnimationClip>(Output));before.hideFlags=HideFlags.HideAndDontSave;
        Apply();Verify();
        StraightWalkSetup.Render(before,"authored-before");UnityEngine.Object.DestroyImmediate(before);
        StraightWalkSetup.Render(AssetDatabase.LoadAssetAtPath<AnimationClip>(Output),"authored-after");
        EditorSceneManager.OpenScene("Assets/Scenes/TestePersonagem.unity");PrototypeSetup.BuildWindows();
    }
    internal static void Verify()
    {
        var source=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Source));
        var target=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Personagem/ByBeyby.fbx"));
        RetargetIdle.RestoreBindPose(source);RetargetIdle.RestoreBindPose(target);
        var src=source.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name,t=>t);
        var dst=target.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name,t=>t);
        var rest=dst[" Pelvis"].rotation;
        float srcRestHeight=src[" Pelvis"].position.y,dstRestHeight=dst[" Pelvis"].position.y;
        var raw=AssetDatabase.LoadAllAssetsAtPath(Source).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview"));
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(Output);
        float meanRaw=0,meanTarget=0,minHeight=float.MaxValue,maxHeight=float.MinValue,maxLegDifference=0,meanYaw=0,meanRoll=0,minGap=float.MaxValue;
        for(int i=0;i<240;i++)
        {
            float phase=i/240f;raw.SampleAnimation(source,raw.length*phase);clip.SampleAnimation(target,clip.length*phase);
            meanRaw+=src[" Pelvis"].position.y/240;meanTarget+=dst[" Pelvis"].position.y/240;
            minHeight=Mathf.Min(minHeight,dst[" Pelvis"].position.y);maxHeight=Mathf.Max(maxHeight,dst[" Pelvis"].position.y);
            var delta=dst[" Pelvis"].rotation*Quaternion.Inverse(rest);var forward=delta*Vector3.forward;var right=delta*Vector3.right;
            meanYaw+=Mathf.Atan2(forward.x,forward.z)*Mathf.Rad2Deg/240;
            meanRoll+=Mathf.Asin(Mathf.Clamp(right.y,-1,1))*Mathf.Rad2Deg/240;
            minGap=Mathf.Min(minGap,dst[" R Foot"].position.x-dst[" L Foot"].position.x);
            foreach(var side in new[]{" L"," R"})
            {
                float original=Vector3.Angle(src[side+" Thigh"].position-src[side+" Calf"].position,src[side+" Foot"].position-src[side+" Calf"].position);
                float converted=Vector3.Angle(dst[side+" Thigh"].position-dst[side+" Calf"].position,dst[side+" Foot"].position-dst[side+" Calf"].position);
                maxLegDifference=Mathf.Max(maxLegDifference,Mathf.Abs(original-converted));
            }
        }
        float heightError=Mathf.Abs((meanTarget-dstRestHeight)-(meanRaw-srcRestHeight));
        var lines=new[]{"source="+Source,"sourceDuration="+raw.length,"targetDuration="+clip.length,"sourceMeanHip="+meanRaw,"targetMeanHip="+meanTarget,
            "hipHeightPreservationError="+heightError,"hipVerticalRange="+(maxHeight-minHeight),"meanYaw="+meanYaw,"meanRoll="+meanRoll,"maximumKneeDifferenceFromSource="+maxLegDifference,"minimumFootSeparation="+minGap};
        Directory.CreateDirectory("../tools/authored-walk");File.WriteAllLines("../tools/authored-walk/quality.txt",lines);
        UnityEngine.Object.DestroyImmediate(source);UnityEngine.Object.DestroyImmediate(target);
        if(heightError>.005f||Mathf.Abs(meanYaw)>1||Mathf.Abs(meanRoll)>1||maxLegDifference>7||minGap<.03f)
            throw new Exception("Conversão não preservou a caminhada original: "+string.Join("; ",lines));
        Debug.Log("AUTHORED_WALK_VERIFIED "+string.Join("; ",lines));
    }
    public static void Inspect()
    {
        AssetDatabase.Refresh();
        var importer=(ModelImporter)AssetImporter.GetAtPath(Source);importer.animationType=ModelImporterAnimationType.Generic;
        importer.importAnimation=true;importer.animationCompression=ModelImporterAnimationCompression.Off;importer.SaveAndReimport();
        var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Source));
        RetargetIdle.RestoreBindPose(model);
        var bones=model.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name.Contains(":")?t.name.Substring(t.name.LastIndexOf(':')+1):t.name,t=>t);
        string hipName=bones.ContainsKey(" Pelvis")?" Pelvis":"Hips";
        var hip=bones[hipName];var rest=hip.rotation;
        var clip=AssetDatabase.LoadAllAssetsAtPath(Source).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview"));
        var lines=new List<string>{"length="+clip.length,"hip="+hipName};
        for(int i=0;i<=48;i++)
        {
            clip.SampleAnimation(model,clip.length*i/48);
            var delta=hip.rotation*Quaternion.Inverse(rest);
            var f=delta*Vector3.forward;var r=delta*Vector3.right;
            float yaw=Mathf.Atan2(f.x,f.z)*Mathf.Rad2Deg,roll=Mathf.Asin(Mathf.Clamp(r.y,-1,1))*Mathf.Rad2Deg;
            lines.Add("phase="+(i/48f)+" hip="+hip.position.ToString("F5")+" yaw="+yaw+" roll="+roll);
        }
        Directory.CreateDirectory("../tools/authored-walk");File.WriteAllLines("../tools/authored-walk/source.txt",lines);
        UnityEngine.Object.DestroyImmediate(model);Debug.Log("AUTHORED_WALK_INSPECTED");
    }
}
