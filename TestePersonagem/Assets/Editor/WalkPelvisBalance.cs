using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class WalkPelvisBalance
{
    const string ModelPath="Assets/Personagem/ByBeyby.fbx";
    const string WalkPath="Assets/Animations/WalkingRetarget.anim";
    static readonly string[] Sides={" L"," R"};
    static float LegCenterLean(Dictionary<string,Transform> bones)
    {
        var hips=(bones[" L Thigh"].position+bones[" R Thigh"].position)*.5f;
        var feet=(bones[" L Foot"].position+bones[" R Foot"].position)*.5f;
        return Mathf.Atan2(hips.x-feet.x,hips.y-feet.y)*Mathf.Rad2Deg;
    }

    public static void Apply()
    {
        var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        AnimationClip reference=null;
        try
        {
            RetargetIdle.RestoreBindPose(model);
            var bones=model.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name,t=>t);
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(WalkPath);
            reference=UnityEngine.Object.Instantiate(clip);reference.hideFlags=HideFlags.HideAndDontSave;
            float beforeMean=0;
            for(int i=0;i<240;i++)
            {
                reference.SampleAnimation(model,reference.length*i/240);
                beforeMean+=LegCenterLean(bones)/240;
            }
            // Remove only the constant side bias inherited from the turning walk.
            // Keep the authored sway, knee flexion, hip height and upper-body pose.
            float correction=Mathf.Clamp(beforeMean*.65f,-2.5f,2.5f);
            var names=new[]{" L Thigh"," R Thigh"," L Foot"," R Foot"};
            var samples=names.ToDictionary(name=>name,name=>new List<Quaternion>());
            int frames=Mathf.CeilToInt(reference.length*120);
            for(int frame=0;frame<=frames;frame++)
            {
                reference.SampleAnimation(model,reference.length*frame/frames);
                foreach(string side in Sides)
                {
                    var foot=bones[side+" Foot"];var orientation=foot.rotation;
                    var thigh=bones[side+" Thigh"];
                    thigh.rotation=Quaternion.AngleAxis(correction,Vector3.forward)*thigh.rotation;
                    foot.rotation=orientation;
                }
                foreach(string name in names)
                {
                    var list=samples[name];var q=bones[name].localRotation;
                    if(list.Count>0&&Quaternion.Dot(list[list.Count-1],q)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);
                    list.Add(q);
                }
            }
            var allowedPaths=new HashSet<string>(names.Select(name=>AnimationUtility.CalculateTransformPath(bones[name],model.transform)));
            foreach(string name in names)for(int axis=0;axis<4;axis++)
            {
                var curve=new AnimationCurve();
                for(int frame=0;frame<=frames;frame++)curve.AddKey(reference.length*frame/frames,samples[name][frame][axis]);
                RetargetIdle.SmoothPeriodic(curve);
                AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(bones[name],model.transform),typeof(Transform),"m_LocalRotation."+"xyzw"[axis]),curve);
            }
            clip.EnsureQuaternionContinuity();EditorUtility.SetDirty(clip);AssetDatabase.SaveAssets();
            foreach(var binding in AnimationUtility.GetCurveBindings(reference).Where(binding=>!allowedPaths.Contains(binding.path)))
                if(!AnimationUtility.GetEditorCurve(reference,binding).keys.SequenceEqual(AnimationUtility.GetEditorCurve(clip,binding).keys))
                    throw new Exception("Equilíbrio alterou canal não autorizado: "+binding.path+" "+binding.propertyName);

            float afterMean=0,maxFootHeightChange=0,maxSupportHeightChange=0,maxBendChange=0,maxFootHeadingChange=0,minGap=float.MaxValue,maxUpperPositionChange=0;
            for(int frame=0;frame<480;frame++)
            {
                float time=clip.length*frame/480;
                reference.SampleAnimation(model,time);
                var positions=Sides.Select(side=>bones[side+" Foot"].position).ToArray();
                var directions=Sides.Select(side=>bones[side+" Foot"].rotation*Vector3.forward).ToArray();
                var bends=Sides.Select(side=>KneeBend(bones,side)).ToArray();
                var upper=bones[" Spine"].position;
                clip.SampleAnimation(model,time);
                afterMean+=LegCenterLean(bones)/480;
                maxUpperPositionChange=Mathf.Max(maxUpperPositionChange,Vector3.Distance(upper,bones[" Spine"].position));
                minGap=Mathf.Min(minGap,bones[" R Foot"].position.x-bones[" L Foot"].position.x);
                for(int side=0;side<2;side++)
                {
                    var foot=bones[Sides[side]+" Foot"];
                    float heightChange=Mathf.Abs(foot.position.y-positions[side].y);
                    maxFootHeightChange=Mathf.Max(maxFootHeightChange,heightChange);
                    if(positions[side].y<.09f)maxSupportHeightChange=Mathf.Max(maxSupportHeightChange,heightChange);
                    maxBendChange=Mathf.Max(maxBendChange,Mathf.Abs(bends[side]-KneeBend(bones,Sides[side])));
                    maxFootHeadingChange=Mathf.Max(maxFootHeadingChange,Mathf.Abs(Vector3.SignedAngle(Vector3.ProjectOnPlane(directions[side],Vector3.up),Vector3.ProjectOnPlane(foot.rotation*Vector3.forward,Vector3.up),Vector3.up)));
                }
            }
            var lines=new[]{"meanLegSideLeanBefore="+beforeMean,"constantLegCorrection="+correction,"meanLegSideLeanAfter="+afterMean,
                "maximumFootHeightChange="+maxFootHeightChange,"maximumSupportFootHeightChange="+maxSupportHeightChange,
                "maximumKneeBendChange="+maxBendChange,"maximumFootHeadingChange="+maxFootHeadingChange,
                "maximumUpperBodyPositionChange="+maxUpperPositionChange,"minimumFootSeparation="+minGap,"hipAndTorsoCurvesUnchanged=true"};
            Directory.CreateDirectory("../tools/authored-walk");File.WriteAllLines("../tools/authored-walk/pelvis-balance.txt",lines);
            // Quaternion curve interpolation may change knee angles by a fraction
            // of a degree at the seam; keep that below 0.15 degrees.
            if(Mathf.Abs(afterMean)>Mathf.Abs(beforeMean)*.5f||maxSupportHeightChange>.01f||maxFootHeightChange>.015f||maxBendChange>.15f||maxFootHeadingChange>.1f||maxUpperPositionChange>.00001f||minGap<.03f)
                throw new Exception("Equilíbrio excedeu limites da passada: "+string.Join("; ",lines));
            Debug.Log("PELVIS_BALANCE_VERIFIED "+string.Join("; ",lines));
        }
        finally
        {
            if(reference)UnityEngine.Object.DestroyImmediate(reference);
            UnityEngine.Object.DestroyImmediate(model);
        }
    }

    static float KneeBend(Dictionary<string,Transform> bones,string side)
    {
        var knee=bones[side+" Calf"].position;
        return Vector3.Angle(bones[side+" Thigh"].position-knee,bones[side+" Foot"].position-knee);
    }

    public static void Inspect()
    {
        var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        try
        {
            RetargetIdle.RestoreBindPose(model);
            var bones=model.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name,t=>t);
            var rest=bones[" Pelvis"].rotation;
            var rows=new List<string>{"clip,phase,pelvisX,pelvisY,pelvisRoll,legCenterLean,leftHipX,rightHipX,leftFootX,rightFootX,leftFootY,rightFootY,spineX"};
            foreach(string path in new[]{"Assets/Animations/IdleRetarget.anim",WalkPath})
            {
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                for(int frame=0;frame<120;frame++)
                {
                    clip.SampleAnimation(model,clip.length*frame/120);
                    var pelvis=bones[" Pelvis"];var left=bones[" L Foot"].position;var right=bones[" R Foot"].position;
                    var hip=(bones[" L Thigh"].position+bones[" R Thigh"].position)*.5f;
                    var axis=hip-(left+right)*.5f;
                    var delta=pelvis.rotation*Quaternion.Inverse(rest);var across=delta*Vector3.right;
                    var values=new[]{frame/120f,pelvis.position.x,pelvis.position.y,Mathf.Atan2(across.y,across.x)*Mathf.Rad2Deg,Mathf.Atan2(axis.x,axis.y)*Mathf.Rad2Deg,
                        bones[" L Thigh"].position.x,bones[" R Thigh"].position.x,left.x,right.x,left.y,right.y,bones[" Spine"].position.x};
                    rows.Add(clip.name+","+string.Join(",",values.Select(v=>v.ToString("R",CultureInfo.InvariantCulture))));
                }
            }
            Directory.CreateDirectory("../tools/authored-walk");File.WriteAllLines("../tools/authored-walk/pelvis-inspect.csv",rows);
            Debug.Log("PELVIS_BALANCE_INSPECTED");
        }
        finally { UnityEngine.Object.DestroyImmediate(model); }
    }
}
