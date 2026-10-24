using System.Collections.Generic;
using UnityEngine;

// Short playback buffer with bounded prediction. Animation follows rendered motion.
public sealed class RemoteMotionBuffer
{
    struct Snapshot { public float time,speed,jumpPhase; public bool jumping; public Vector3 position; public Quaternion rotation; }
    readonly List<Snapshot> samples=new List<Snapshot>();
    float delay=.18f,playhead,lastRenderTime,lastArrival,jitter,interval=.1f;
    bool initialized,hasRendered;
    Vector3 lastPosition;
    public float Speed {get;private set;}
    public bool Jumping {get;private set;}
    public float JumpPhase {get;private set;}
    public float PlaybackDelay=>delay;
    public void Reset(){samples.Clear();delay=.18f;interval=.1f;jitter=0;initialized=hasRendered=false;Speed=0;Jumping=false;JumpPhase=0;}

    public void Push(float time,Vector3 position,Quaternion rotation,float speed=0,bool jumping=false,float jumpPhase=0,float receivedAt=float.NaN)
    {
        float arrival=float.IsNaN(receivedAt)?time:receivedAt;
        if(samples.Count>0)
        {
            var previous=samples[samples.Count-1];float gap=time-previous.time;
            if(gap<=0)return;
            if(Vector3.Distance(position,previous.position)>12f)Reset();
            else
            {
                interval=Mathf.Lerp(interval,Mathf.Clamp(gap,.05f,.7f),.2f);
                jitter=Mathf.Lerp(jitter,Mathf.Abs((arrival-lastArrival)-gap),.15f);
                delay=Mathf.Clamp(interval*1.25f+jitter,.15f,.45f);
            }
        }
        lastArrival=arrival;
        samples.Add(new Snapshot {time=time,position=position,rotation=rotation,speed=speed,jumping=jumping,jumpPhase=jumpPhase});
        while(samples.Count>64)samples.RemoveAt(0);
    }

    public bool Sample(float now,out Vector3 position,out Quaternion rotation)
    {
        position=Vector3.zero;rotation=Quaternion.identity;if(samples.Count==0)return false;
        float dt=hasRendered?Mathf.Max(0,now-lastRenderTime):0;
        float target=now-delay;
        if(!initialized){playhead=target;initialized=true;}
        else
        {
            // Slow or accelerate slightly instead of freezing when delay changes.
            float error=target-(playhead+dt);
            playhead+=dt*Mathf.Clamp(1+error*2,.85f,1.15f);
        }
        while(samples.Count>2&&samples[1].time<=playhead)samples.RemoveAt(0);
        var a=samples[0];var b=samples[samples.Count>1?1:0];
        float t=b.time>a.time?Mathf.Clamp01((playhead-a.time)/(b.time-a.time)):0;
        position=Vector3.Lerp(a.position,b.position,t);
        rotation=Quaternion.Slerp(a.rotation,b.rotation,t);
        var pose=t>=1?b:a;
        Jumping=pose.jumping;JumpPhase=a.jumping&&b.jumping?Mathf.Lerp(a.jumpPhase,b.jumpPhase,t):pose.jumpPhase;
        if(playhead>b.time&&samples.Count>1)
        {
            float age=Mathf.Min(playhead-b.time,.2f);
            var velocity=(b.position-a.position)/Mathf.Max(.001f,b.time-a.time);
            if(b.speed<.1f)velocity=Vector3.zero;else velocity=Vector3.ClampMagnitude(velocity,10);
            position=b.position+velocity*age;
            if(Jumping)JumpPhase=Mathf.Clamp01(b.jumpPhase+age/1.15f);
        }
        if(hasRendered&&dt>0)
        {
            // Recover from a late stop or direction change without a position pop.
            var delta=position-lastPosition;
            position=lastPosition+Vector3.ClampMagnitude(Vector3.ProjectOnPlane(delta,Vector3.up),8*dt)+Vector3.up*Mathf.Clamp(delta.y,-12*dt,12*dt);
            Speed=Mathf.Clamp(Vector3.ProjectOnPlane(position-lastPosition,Vector3.up).magnitude/dt,0,8);
        }
        else Speed=b.speed;
        if(now-lastArrival>1f){Speed=0;Jumping=false;}
        lastPosition=position;lastRenderTime=now;hasRendered=true;
        return true;
    }
}
