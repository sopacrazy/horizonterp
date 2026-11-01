#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Explicit verification only: measures the rendered bones after all pose writers.
[DefaultExecutionOrder(950)]
public sealed class PoliceAnimationPoseProbe : MonoBehaviour
{
    [Serializable] public sealed class Sample
    {public string phase;public float progress,kneeFlex,hipOffset,footHeight,heading;public Vector3 hips;}
    [Serializable] public sealed class Result
    {public List<Sample> samples=new List<Sample>();public float maxEntryKneeFlex,maxExitKneeFlex,maxHipOffset,maxOutsideFootHeight,maxSeatedHeading;}
    public static Result Data;
    float next;
    void Awake(){Data=new Result();}
    void LateUpdate()
    {
        if(Time.unscaledTime<next)return;next=Time.unscaledTime+.3f;
        foreach(var seat in FindObjectsByType<PoliceSeatAvatar>(FindObjectsSortMode.None))
        {
            if(!seat.AuthoredClipActive)continue;
            var a=seat.VisibleAnimator;
            var hip=a.GetBoneTransform(HumanBodyBones.Hips);
            float Flex(HumanBodyBones upper,HumanBodyBones lower,HumanBodyBones foot)
            {
                var h=a.GetBoneTransform(upper);var k=a.GetBoneTransform(lower);var f=a.GetBoneTransform(foot);
                return 180-Vector3.Angle(h.position-k.position,f.position-k.position);
            }
            float flex=Mathf.Max(Flex(HumanBodyBones.LeftUpperLeg,HumanBodyBones.LeftLowerLeg,HumanBodyBones.LeftFoot),Flex(HumanBodyBones.RightUpperLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.RightFoot));
            var hp=seat.transform.parent.InverseTransformPoint(hip.position);
            float offset=Vector3.Distance(hp,seat.ExpectedHip);
            float footHeight=Mathf.Min(seat.transform.parent.InverseTransformPoint(a.GetBoneTransform(HumanBodyBones.LeftFoot).position).y,
                seat.transform.parent.InverseTransformPoint(a.GetBoneTransform(HumanBodyBones.RightFoot).position).y);
            var side=a.GetBoneTransform(HumanBodyBones.RightUpperArm).position-a.GetBoneTransform(HumanBodyBones.LeftUpperArm).position;
            var up=a.GetBoneTransform(HumanBodyBones.Head).position-hip.position;
            var forward=seat.transform.parent.InverseTransformDirection(Vector3.Cross(side,up));
            float heading=Mathf.Atan2(forward.x,forward.z)*Mathf.Rad2Deg;
            if(!seat.Exiting&&seat.ClipProgress<.4f||seat.Exiting&&seat.ClipProgress>.45f&&seat.ClipProgress<.8f)
                Data.maxOutsideFootHeight=Mathf.Max(Data.maxOutsideFootHeight,footHeight);
            if(!seat.Exiting&&seat.ClipProgress>.9f)Data.maxSeatedHeading=Mathf.Max(Data.maxSeatedHeading,Mathf.Abs(heading));
            Data.samples.Add(new Sample{phase=seat.Exiting?"exit":"entry",progress=seat.ClipProgress,kneeFlex=flex,hipOffset=offset,footHeight=footHeight,heading=heading,hips=hp});
            if(seat.Exiting)Data.maxExitKneeFlex=Mathf.Max(Data.maxExitKneeFlex,flex);else Data.maxEntryKneeFlex=Mathf.Max(Data.maxEntryKneeFlex,flex);
            Data.maxHipOffset=Mathf.Max(Data.maxHipOffset,offset);
            Save();Capture(seat,Data.samples.Count);
        }
    }
    void Save(){Directory.CreateDirectory("../tools/police-car-animation");File.WriteAllText("../tools/police-car-animation/authored-pose-validation.json",JsonUtility.ToJson(Data,true));}
    void Capture(PoliceSeatAvatar seat,int frame)
    {
        if(!Camera.main)return;
        var folder="../tools/police-car-animation/aligned-pose-frames";Directory.CreateDirectory(folder);
        var camera=Camera.main;var position=camera.transform.position;var rotation=camera.transform.rotation;var target=camera.targetTexture;var active=RenderTexture.active;
        var rt=new RenderTexture(640,360,24);var image=new Texture2D(640,360,TextureFormat.RGB24,false);
        try
        {
            camera.transform.position=seat.transform.parent.TransformPoint(new Vector3(-4.8f,1.8f,3.2f));camera.transform.LookAt(seat.transform.parent.TransformPoint(new Vector3(-.3f,.9f,.1f)));
            camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,640,360),0,0);image.Apply();
            File.WriteAllBytes(folder+"/pose-"+frame.ToString("D3")+".png",image.EncodeToPNG());
        }
        finally{camera.targetTexture=target;camera.transform.SetPositionAndRotation(position,rotation);RenderTexture.active=active;rt.Release();Destroy(rt);Destroy(image);}
    }
}
#endif
