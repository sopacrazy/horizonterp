using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class NetworkMotionVerification
{
    public static void Run()
    {
        Verify();PrototypeSetup.BuildWindows();
    }
    public static void Verify()
    {
        foreach(float interval in new[]{.1f,.35f,.7f})
        {
            var buffer=new RemoteMotionBuffer();float next=0,previous=0,maxStep=0;int moving=0,frames=0;
            for(int frame=0;frame<600;frame++)
            {
                float now=frame/60f;
                if(now>=next){buffer.Push(now,new Vector3(now*4.5f,0,0),Quaternion.Euler(0,now*10,0),4.5f);next+=interval;}
                buffer.Sample(now,out var position,out _);
                if(now>3){float step=position.x-previous;if(step<-.001f)throw new Exception("Remote moved backwards");maxStep=Mathf.Max(maxStep,step);if(step>.001f)moving++;frames++;}
                previous=position.x;
            }
            if(maxStep>.16f||moving<frames*.9f)throw new Exception("Discontinuous movement at interval "+interval+": "+maxStep+" / "+moving);
            buffer.Sample(30,out var stopped,out _);buffer.Sample(40,out var later,out _);
            if(stopped!=later)throw new Exception("Disconnected avatar drifts");
        }
        var jitter=new RemoteMotionBuffer();float last=0;int index=0;float arrival=0;
        float[] gaps={.18f,.32f,.2f,.45f,.23f,.3f};
        for(int frame=0;frame<1200;frame++)
        {
            float now=frame/60f;
            if(now>=arrival){jitter.Push(now,new Vector3(now*4.5f,0,0),Quaternion.identity,4.5f);arrival+=gaps[index++%gaps.Length];}
            jitter.Sample(now,out var point,out _);
            if(frame>180&&(point.x<last-.001f||point.x-last>.25f))throw new Exception("Jitter caused a visual position jump");
            last=point.x;
        }
        VerifyDelayedPackets();VerifyAnimator();
        Directory.CreateDirectory("../tools/network-review");
        File.WriteAllText("../tools/network-review/validation.txt","Passed: cadence100/350/700ms; varying delays and loss; bounded prediction; animation from rendered speed; remote clone reset; jump landing returns to locomotion.\n");
        Debug.Log("NETWORK_MOTION_VERIFIED: delayed/lost packets, bounded playback delay, rendered motion drives animation, actual remote Animator playback and landing");
    }

    struct Packet {public float source,arrival;}
    static void VerifyDelayedPackets()
    {
        var pending=new List<Packet>();
        float[] latency={.08f,.1f,.23f,.12f,.3f,.09f,.18f,.11f};
        for(int i=0;i<=140;i++)
        {
            if(i==49||i==50||i==51)continue;
            pending.Add(new Packet{source=i*.1f,arrival=i*.1f+latency[i%latency.Length]});
        }
        pending=pending.OrderBy(p=>p.arrival).ToList();
        var motion=new RemoteMotionBuffer();int packet=0,moving=0,total=0;float previous=0,maxStep=0,maxLag=0;
        for(int i=0;i<=1000;i++)
        {
            float now=i/60f;
            while(packet<pending.Count&&pending[packet].arrival<=now)
            {
                var p=pending[packet++];float distance=Mathf.Min(p.source,10)*4.5f;
                // Deliberately omit speed on some packets: visible motion must still animate.
                motion.Push(p.source,new Vector3(distance,0,0),Quaternion.identity,p.source<10&&packet%5!=0?4.5f:0,false,0,p.arrival);
            }
            if(!motion.Sample(now,out var point,out _))continue;
            if(now>2&&now<9)
            {
                float step=Mathf.Abs(point.x-previous);maxStep=Mathf.Max(maxStep,step);
                if(step>.002f){moving++;if(motion.Speed<.1f)throw new Exception("Moving avatar has idle animation speed");}
                total++;maxLag=Mathf.Max(maxLag,(now*4.5f-point.x)/4.5f);
            }
            if(motion.PlaybackDelay>.451f)throw new Exception("Unbounded interpolation delay");
            if(now>15&&(Mathf.Abs(point.x-45)>.02f||motion.Speed>.01f))throw new Exception("Remote did not settle after stop");
            previous=point.x;
        }
        if(maxStep>.14f||moving<total*.86f||maxLag>.8f)throw new Exception("Delayed packet regression step="+maxStep+" moving="+moving+"/"+total+" lag="+maxLag);
        Debug.Log("NETWORK_DELAY_VERIFIED maxStep="+maxStep+" moving="+moving+"/"+total+" maxLag="+maxLag);
    }

    static void VerifyAnimator()
    {
        var source=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Personagem/ByBeyby.fbx"));
        GameObject remote=null;
        try
        {
            var local=source.GetComponent<Animator>()??source.AddComponent<Animator>();
            local.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/Locomocao.controller");
            local.Rebind();local.SetBool("Jumping",true);local.SetFloat("Speed",4.5f);local.Play("Jump",0,.8f);local.Update(0);
            remote=UnityEngine.Object.Instantiate(source);var animator=remote.GetComponent<Animator>();
            var playback=new RemoteAvatarAnimation(animator);
            if(animator.GetBool("Jumping")||!animator.GetCurrentAnimatorStateInfo(0).IsName("Locomocao"))throw new Exception("Remote clone inherited local jump");
            var foot=remote.GetComponentsInChildren<Transform>().First(t=>t.name==" L Foot");
            float min=float.MaxValue,max=float.MinValue;
            for(int i=0;i<120;i++)
            {
                playback.Apply(1.65f,false,0,1f/60);animator.Update(1f/60);
                min=Mathf.Min(min,foot.position.z);max=Mathf.Max(max,foot.position.z);
            }
            if(max-min<.05f||animator.GetFloat("Speed")<1.5f)throw new Exception("Remote walking animation is frozen");
            playback.Apply(4.5f,true,.3f,1f/60);animator.Update(.1f);
            for(int i=0;i<12;i++){playback.Apply(4.5f,false,0,1f/60);animator.Update(1f/60);}
            if(!animator.GetCurrentAnimatorStateInfo(0).IsName("Locomocao")||animator.GetBool("Jumping"))throw new Exception("Remote remains in jump after landing");
            for(int i=0;i<45;i++){playback.Apply(0,false,0,1f/60);animator.Update(1f/60);}
            if(animator.GetFloat("Speed")>.05f)throw new Exception("Remote did not return to idle");
            Debug.Log("REMOTE_ANIMATION_VERIFIED walkingFootTravel="+(max-min)+" cloneReset=true landing=true idle=true");
        }
        finally {if(remote)UnityEngine.Object.DestroyImmediate(remote);UnityEngine.Object.DestroyImmediate(source);}
    }
}
