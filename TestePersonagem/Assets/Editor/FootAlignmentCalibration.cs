using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// A constant heading correction, not a replacement gait or an IK foot trajectory.
public static class FootAlignmentCalibration
{
    const string Walk="Assets/Animations/WalkingRetarget.anim";
    static readonly string[] Sides={" L"," R"};
    sealed class Sole
    {
        public int[] front,back;
    }
    sealed class Rig
    {
        public GameObject model;public SkinnedMeshRenderer skin;public Dictionary<string,Transform> bones;
        public Vector3[] vertices;public BoneWeight[] weights;public Matrix4x4[] binds;public Sole[] soles;
        public Matrix4x4[] matrices;
        public Vector3 Vertex(int index)
        {
            var w=weights[index];var v=vertices[index];return matrices[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0+
                matrices[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1+matrices[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2+matrices[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;
        }
        public void UpdateMatrices(){for(int i=0;i<matrices.Length;i++)matrices[i]=skin.bones[i].localToWorldMatrix*binds[i];}
        public float SoleYaw(int side)
        {
            var a=Vector3.zero;var b=Vector3.zero;
            foreach(int index in soles[side].front)a+=Vertex(index)/soles[side].front.Length;
            foreach(int index in soles[side].back)b+=Vertex(index)/soles[side].back.Length;
            return Mathf.Atan2(a.x-b.x,a.z-b.z)*Mathf.Rad2Deg;
        }
    }
    sealed class Stats
    {
        public Vector2[] shoe=new Vector2[2],knee=new Vector2[2];public int[] samples=new int[2];
        public float Shoe(int side)=>Mathf.Atan2(shoe[side].x,shoe[side].y)*Mathf.Rad2Deg;
        public float Knee(int side)=>Mathf.Atan2(knee[side].x,knee[side].y)*Mathf.Rad2Deg;
    }
    static Rig CreateRig()
    {
        var rig=new Rig();rig.model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Personagem/ByBeyby.fbx"));
        RetargetIdle.RestoreBindPose(rig.model);rig.skin=rig.model.GetComponentInChildren<SkinnedMeshRenderer>();
        rig.bones=rig.model.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name,t=>t);
        rig.vertices=rig.skin.sharedMesh.vertices;rig.weights=rig.skin.sharedMesh.boneWeights;rig.binds=rig.skin.sharedMesh.bindposes;
        rig.matrices=new Matrix4x4[rig.binds.Length];rig.UpdateMatrices();rig.soles=new Sole[2];
        for(int side=0;side<2;side++)
        {
            var indices=new List<int>();
            for(int i=0;i<rig.vertices.Length;i++)
            {
                var w=rig.weights[i];float weight=0;
                foreach(var pair in new[]{(w.boneIndex0,w.weight0),(w.boneIndex1,w.weight1),(w.boneIndex2,w.weight2),(w.boneIndex3,w.weight3)})
                    if(rig.skin.bones[pair.Item1].name==Sides[side]+" Foot"||rig.skin.bones[pair.Item1].name==Sides[side]+" Toe0")weight+=pair.Item2;
                if(weight>.7f)indices.Add(i);
            }
            if(indices.Count<8)throw new Exception("Vértices do calçado não encontrados");
            float low=indices.Min(i=>rig.Vertex(i).z),high=indices.Max(i=>rig.Vertex(i).z);
            rig.soles[side]=new Sole {front=indices.Where(i=>rig.Vertex(i).z>Mathf.Lerp(low,high,.7f)).ToArray(),back=indices.Where(i=>rig.Vertex(i).z<Mathf.Lerp(low,high,.3f)).ToArray()};
            if(rig.soles[side].front.Length==0||rig.soles[side].back.Length==0)throw new Exception("Marcadores de sola incompletos");
        }
        return rig;
    }
    static Stats Measure(Rig rig,AnimationClip clip,float[] legCorrection=null,float[] footCorrection=null)
    {
        var stats=new Stats();
        for(int frame=0;frame<240;frame++)
        {
            clip.SampleAnimation(rig.model,clip.length*frame/240);
            Correct(rig,legCorrection,footCorrection);rig.UpdateMatrices();
            for(int side=0;side<2;side++)
            {
                var hip=rig.bones[Sides[side]+" Thigh"].position;var knee=rig.bones[Sides[side]+" Calf"].position;var foot=rig.bones[Sides[side]+" Foot"].position;
                if(foot.y>.09f)continue; // Ignore the projected heading of a foot pointing down in recovery.
                var plane=Vector3.Cross(knee-hip,foot-knee).normalized;
                if(plane.x<.2f)continue;
                float kneeYaw=Mathf.Atan2(-plane.z,plane.x),shoeYaw=rig.SoleYaw(side)*Mathf.Deg2Rad;
                stats.knee[side]+=new Vector2(Mathf.Sin(kneeYaw),Mathf.Cos(kneeYaw));
                stats.shoe[side]+=new Vector2(Mathf.Sin(shoeYaw),Mathf.Cos(shoeYaw));stats.samples[side]++;
            }
        }
        if(stats.samples.Any(n=>n<30))throw new Exception("Amostras de apoio insuficientes para alinhar pés");
        return stats;
    }
    static void Correct(Rig rig,float[] legs,float[] feet)
    {
        for(int side=0;side<2;side++)
        {
            if(legs!=null){var thigh=rig.bones[Sides[side]+" Thigh"];thigh.rotation=Quaternion.AngleAxis(legs[side],Vector3.up)*thigh.rotation;}
            if(feet!=null){var foot=rig.bones[Sides[side]+" Foot"];foot.rotation=Quaternion.AngleAxis(feet[side],Vector3.up)*foot.rotation;}
        }
    }
    public static void Apply()
    {
        var rig=CreateRig();var original=AssetDatabase.LoadAssetAtPath<AnimationClip>(Walk);
        var snapshot=UnityEngine.Object.Instantiate(original);snapshot.hideFlags=HideFlags.HideAndDontSave;
        var idle=Measure(rig,AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/IdleRetarget.anim"));var before=Measure(rig,snapshot);
        float[] legs=new float[2],feet=new float[2];
        for(int side=0;side<2;side++)legs[side]=Mathf.Clamp(Mathf.DeltaAngle(before.Knee(side),idle.Knee(side)),-12,12);
        var alignedLegs=Measure(rig,snapshot,legs);
        for(int side=0;side<2;side++)feet[side]=Mathf.Clamp(Mathf.DeltaAngle(alignedLegs.Shoe(side),idle.Shoe(side)),-20,20);
        // The boot is skinned to both foot and calf. Solve its visible heading,
        // rather than assuming a bone rotation maps one-to-one to sole rotation.
        for(int iteration=0;iteration<5;iteration++)
        {
            var measured=Measure(rig,snapshot,legs,feet);
            for(int side=0;side<2;side++)feet[side]=Mathf.Clamp(feet[side]+Mathf.DeltaAngle(measured.Shoe(side),idle.Shoe(side)),-20,20);
        }
        var corrected=Measure(rig,snapshot,legs,feet);
        var lines=new List<string>();
        for(int side=0;side<2;side++)
        {
            lines.Add(Sides[side]+" knee before/idle/after="+before.Knee(side)+" / "+idle.Knee(side)+" / "+corrected.Knee(side)+" correction="+legs[side]);
            lines.Add(Sides[side]+" sole before/idle/after="+before.Shoe(side)+" / "+idle.Shoe(side)+" / "+corrected.Shoe(side)+" correction="+feet[side]);
            if(Mathf.Abs(Mathf.DeltaAngle(corrected.Knee(side),idle.Knee(side)))>1||Mathf.Abs(Mathf.DeltaAngle(corrected.Shoe(side),idle.Shoe(side)))>1.5f)
                throw new Exception("Correção de alinhamento fora do limite: "+string.Join("; ",lines));
        }
        var names=new[]{" L Thigh"," R Thigh"," L Foot"," R Foot"};var samples=names.ToDictionary(n=>n,n=>new List<Quaternion>());
        int frames=Mathf.CeilToInt(snapshot.length*120);float maxHeightChange=0,maxBendChange=0;
        for(int frame=0;frame<=frames;frame++)
        {
            snapshot.SampleAnimation(rig.model,snapshot.length*frame/frames);
            var heights=Sides.Select(s=>rig.bones[s+" Foot"].position.y).ToArray();
            var bends=Sides.Select(s=>Vector3.Angle(rig.bones[s+" Thigh"].position-rig.bones[s+" Calf"].position,rig.bones[s+" Foot"].position-rig.bones[s+" Calf"].position)).ToArray();
            Correct(rig,legs,feet);
            for(int side=0;side<2;side++)
            {
                maxHeightChange=Mathf.Max(maxHeightChange,Mathf.Abs(rig.bones[Sides[side]+" Foot"].position.y-heights[side]));
                maxBendChange=Mathf.Max(maxBendChange,Mathf.Abs(Vector3.Angle(rig.bones[Sides[side]+" Thigh"].position-rig.bones[Sides[side]+" Calf"].position,rig.bones[Sides[side]+" Foot"].position-rig.bones[Sides[side]+" Calf"].position)-bends[side]));
            }
            foreach(var name in names)
            {
                var list=samples[name];var q=rig.bones[name].localRotation;
                if(list.Count>0&&Quaternion.Dot(list[list.Count-1],q)<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);
                list.Add(q);
            }
        }
        foreach(var name in names)for(int axis=0;axis<4;axis++)
        {
            var curve=new AnimationCurve();for(int frame=0;frame<=frames;frame++)curve.AddKey(snapshot.length*frame/frames,samples[name][frame][axis]);
            RetargetIdle.SmoothPeriodic(curve);
            AnimationUtility.SetEditorCurve(original,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(rig.bones[name],rig.model.transform),typeof(Transform),"m_LocalRotation."+"xyzw"[axis]),curve);
        }
        original.EnsureQuaternionContinuity();EditorUtility.SetDirty(original);AssetDatabase.SaveAssets();
        var baked=Measure(rig,original);
        for(int side=0;side<2;side++)
            if(Mathf.Abs(Mathf.DeltaAngle(baked.Knee(side),idle.Knee(side)))>1||Mathf.Abs(Mathf.DeltaAngle(baked.Shoe(side),idle.Shoe(side)))>1.5f)
                throw new Exception("A animação gravada não preservou o alinhamento");
        lines.Add("maximumAnkleHeightChange="+maxHeightChange+" maximumKneeFlexionChange="+maxBendChange);
        Directory.CreateDirectory("../tools/authored-walk");File.WriteAllLines("../tools/authored-walk/alignment.txt",lines);
        if(maxHeightChange>.0001f||maxBendChange>.05f)throw new Exception("Correção alterou altura ou flexão da passada");
        UnityEngine.Object.DestroyImmediate(snapshot);UnityEngine.Object.DestroyImmediate(rig.model);
        Debug.Log("FOOT_ALIGNMENT_VERIFIED "+string.Join("; ",lines));
    }
}
