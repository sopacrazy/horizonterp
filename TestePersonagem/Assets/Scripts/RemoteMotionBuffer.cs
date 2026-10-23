using System.Collections.Generic;
using UnityEngine;

// Render slightly behind arrivals so movement spans the actual network interval.
// Never extrapolate indefinitely: a disconnected player must stop moving.
public sealed class RemoteMotionBuffer
{
    struct Snapshot { public float time,speed,jumpPhase; public bool jumping; public Vector3 position; public Quaternion rotation; }
    readonly List<Snapshot> samples=new List<Snapshot>();
    readonly Queue<float> intervals=new Queue<float>();
    float delay=.15f,playhead=float.NegativeInfinity;
    public float Speed {get;private set;}
    public bool Jumping {get;private set;}
    public float JumpPhase {get;private set;}
    public void Reset(){samples.Clear();intervals.Clear();delay=.15f;playhead=float.NegativeInfinity;}

    public void Push(float time,Vector3 position,Quaternion rotation,float speed=0,bool jumping=false,float jumpPhase=0)
    {
        if(samples.Count>0)
        {
            float gap=time-samples[samples.Count-1].time;
            if(gap<=0)return;
            intervals.Enqueue(gap);while(intervals.Count>6)intervals.Dequeue();
            float longest=.1f;foreach(float interval in intervals)longest=Mathf.Max(longest,interval);
            delay=Mathf.Clamp(longest*1.25f,.15f,2f);
            // A genuine world relocation is different from ordinary running updates.
            if(Vector3.Distance(position,samples[samples.Count-1].position)>12f)
            {samples.Clear();intervals.Clear();playhead=float.NegativeInfinity;}
        }
        samples.Add(new Snapshot {time=time,position=position,rotation=rotation,speed=speed,jumping=jumping,jumpPhase=jumpPhase});
        while(samples.Count>64)samples.RemoveAt(0);
    }

    public bool Sample(float now,out Vector3 position,out Quaternion rotation)
    {
        position=Vector3.zero;rotation=Quaternion.identity;if(samples.Count==0)return false;
        // Increasing jitter must not make the rendered timeline run backwards.
        playhead=Mathf.Max(playhead,now-delay);
        while(samples.Count>2&&samples[1].time<=playhead)samples.RemoveAt(0);
        var a=samples[0];var b=samples[samples.Count>1?1:0];
        float t=b.time>a.time?Mathf.Clamp01((playhead-a.time)/(b.time-a.time)):0;
        position=Vector3.Lerp(a.position,b.position,t);
        rotation=Quaternion.Slerp(a.rotation,b.rotation,t);
        Speed=Mathf.Lerp(a.speed,b.speed,t);
        if(now-samples[samples.Count-1].time>2f)Speed=0;
        var pose=t>=1?b:a;
        Jumping=pose.jumping;JumpPhase=pose.jumpPhase;
        return true;
    }
}
