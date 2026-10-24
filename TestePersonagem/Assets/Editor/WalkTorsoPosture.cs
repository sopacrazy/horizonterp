using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class WalkTorsoPosture
{
    const string Path="Assets/Animations/WalkingRetarget.anim";
    public static void VerifySaved()
    {
        var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Personagem/ByBeyby.fbx"));
        try
        {
            RetargetIdle.RestoreBindPose(model);
            var bones=model.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name,t=>t);
            var idle=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/IdleRetarget.anim");
            var walk=AssetDatabase.LoadAssetAtPath<AnimationClip>(Path);
            float idleLean=0;
            for(int i=0;i<120;i++)
            {
                idle.SampleAnimation(model,idle.length*i/120);
                var axis=bones[" Neck"].position-bones[" Spine"].position;
                idleLean+=Mathf.Atan2(axis.x,axis.y)*Mathf.Rad2Deg/120;
            }
            float maximumSideLean=0,minimumFrontLean=float.MaxValue,maximumFrontLean=float.MinValue;
            // Sample the saved curves, including interpolated frames and the loop seam.
            for(int i=0;i<=480;i++)
            {
                walk.SampleAnimation(model,walk.length*i/480);
                var axis=bones[" Neck"].position-bones[" Spine"].position;
                maximumSideLean=Mathf.Max(maximumSideLean,Mathf.Abs(Mathf.Atan2(axis.x,axis.y)*Mathf.Rad2Deg-idleLean));
                float pitch=Mathf.Atan2(axis.z,axis.y)*Mathf.Rad2Deg;
                minimumFrontLean=Mathf.Min(minimumFrontLean,pitch);
                maximumFrontLean=Mathf.Max(maximumFrontLean,pitch);
            }
            string result="maximumSideLean="+maximumSideLean+"; frontLeanRange="+minimumFrontLean+".."+maximumFrontLean+"; samples=481";
            if(maximumSideLean>2f)throw new Exception("Clip salvo excedeu inclinação lateral: "+result);
            File.WriteAllText("../tools/authored-walk/torso-saved.txt",result);
            Debug.Log("TORSO_POSTURE_SAVED_VERIFIED "+result);
        }
        finally { UnityEngine.Object.DestroyImmediate(model); }
    }
    public static void Apply()
    {
        var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Personagem/ByBeyby.fbx"));
        RetargetIdle.RestoreBindPose(model);var bones=model.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name,t=>t);
        var spine=bones[" Spine"];var neck=bones[" Neck"];
        var idle=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/IdleRetarget.anim");
        float idleLean=0;
        for(int i=0;i<120;i++){idle.SampleAnimation(model,idle.length*i/120);var axis=neck.position-spine.position;idleLean+=Mathf.Atan2(axis.x,axis.y)*Mathf.Rad2Deg/120;}
        var original=AssetDatabase.LoadAssetAtPath<AnimationClip>(Path);var reference=UnityEngine.Object.Instantiate(original);reference.hideFlags=HideFlags.HideAndDontSave;
        int frames=Mathf.CeilToInt(reference.length*120);var rotations=new List<Quaternion>();
        float beforeMax=0,afterMax=0,maxLowerChange=0,maxFootRotationChange=0;
        var lowerNames=new[]{" Pelvis"," L Thigh"," L Calf"," L Foot"," R Thigh"," R Calf"," R Foot"};
        for(int frame=0;frame<=frames;frame++)
        {
            reference.SampleAnimation(model,reference.length*frame/frames);
            var positions=lowerNames.Select(name=>bones[name].position).ToArray();var lowerRotations=lowerNames.Select(name=>bones[name].rotation).ToArray();
            var axis=neck.position-spine.position;float lean=Mathf.Atan2(axis.x,axis.y)*Mathf.Rad2Deg;
            beforeMax=Mathf.Max(beforeMax,Mathf.Abs(lean-idleLean));
            // Keep a small natural sway. Counterbalance only upper-body side lean.
            float target=idleLean+(lean-idleLean)*.35f;
            float correction=Mathf.Clamp(lean-target,-3f,3f);
            spine.rotation=Quaternion.AngleAxis(correction,Vector3.forward)*spine.rotation;
            axis=neck.position-spine.position;afterMax=Mathf.Max(afterMax,Mathf.Abs(Mathf.Atan2(axis.x,axis.y)*Mathf.Rad2Deg-idleLean));
            var q=spine.localRotation;if(rotations.Count>0&&Quaternion.Dot(rotations[rotations.Count-1],q)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);rotations.Add(q);
            for(int i=0;i<lowerNames.Length;i++)
            {
                maxLowerChange=Mathf.Max(maxLowerChange,Vector3.Distance(positions[i],bones[lowerNames[i]].position));
                maxFootRotationChange=Mathf.Max(maxFootRotationChange,Quaternion.Angle(lowerRotations[i],bones[lowerNames[i]].rotation));
            }
        }
        var bindingPath=AnimationUtility.CalculateTransformPath(spine,model.transform);
        for(int axis=0;axis<4;axis++)
        {
            var curve=new AnimationCurve();for(int i=0;i<=frames;i++)curve.AddKey(reference.length*i/frames,rotations[i][axis]);
            RetargetIdle.SmoothPeriodic(curve);AnimationUtility.SetEditorCurve(original,EditorCurveBinding.FloatCurve(bindingPath,typeof(Transform),"m_LocalRotation."+"xyzw"[axis]),curve);
        }
        original.EnsureQuaternionContinuity();EditorUtility.SetDirty(original);AssetDatabase.SaveAssets();
        // The baked animation must preserve every lower-body channel exactly.
        foreach(var binding in AnimationUtility.GetCurveBindings(reference).Where(b=>b.path!=bindingPath))
        {
            var oldCurve=AnimationUtility.GetEditorCurve(reference,binding);var newCurve=AnimationUtility.GetEditorCurve(original,binding);
            if(!newCurve.keys.SequenceEqual(oldCurve.keys))throw new Exception("Postura alterou canal não autorizado: "+binding.path+" "+binding.propertyName);
        }
        var lines=new[]{"idleSideLean="+idleLean,"maximumSideLeanBefore="+beforeMax,"maximumSideLeanAfter="+afterMax,
            "maximumLowerBodyPositionChange="+maxLowerChange,"maximumLowerBodyRotationChange="+maxFootRotationChange,"onlySpineRotationCurvesChanged=true"};
        Directory.CreateDirectory("../tools/authored-walk");File.WriteAllLines("../tools/authored-walk/torso.txt",lines);
        if(afterMax>Mathf.Max(1,beforeMax*.5f)||maxLowerChange>.00001f||maxFootRotationChange>.01f)throw new Exception("Ajuste de postura excedeu limites: "+string.Join("; ",lines));
        UnityEngine.Object.DestroyImmediate(reference);UnityEngine.Object.DestroyImmediate(model);Debug.Log("TORSO_POSTURE_VERIFIED "+string.Join("; ",lines));
    }
}
