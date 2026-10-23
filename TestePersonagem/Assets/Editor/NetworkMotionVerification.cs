using System;
using UnityEngine;

public static class NetworkMotionVerification
{
    public static void Run()
    {
        foreach(float interval in new[]{.1f,.35f,.7f})
        {
            var buffer=new RemoteMotionBuffer();float next=0,previous=0,maxStep=0;int moving=0,frames=0;
            for(int frame=0;frame<600;frame++)
            {
                float now=frame/60f;
                if(now>=next){buffer.Push(now,new Vector3(now*4.5f,0,0),Quaternion.Euler(0,now*10,0));next+=interval;}
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
            if(now>=arrival){jitter.Push(now,new Vector3(now*4.5f,0,0),Quaternion.identity);arrival+=gaps[index++%gaps.Length];}
            jitter.Sample(now,out var point,out _);
            if(frame>180&&(point.x<last-.001f||point.x-last>.25f))throw new Exception("Jitter caused a visual position jump");
            last=point.x;
        }
        Debug.Log("NETWORK_MOTION_VERIFIED: 60 FPS rendering with 100/350/700 ms updates and jitter; no backward motion or indefinite drift");
        PrototypeSetup.BuildWindows();
    }
}
