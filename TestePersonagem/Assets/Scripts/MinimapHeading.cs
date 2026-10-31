using UnityEngine;

// UI heading is evaluated every rendered frame, independently of network/map texture updates.
public sealed class MinimapHeading
{
    float yaw,velocity;
    bool initialized;
    public float Step(float target,float deltaTime)
    {
        if(!initialized){yaw=target;initialized=true;}
        yaw=Mathf.SmoothDampAngle(yaw,target,ref velocity,.10f,720f,Mathf.Clamp(deltaTime,0,.05f));
        return Mathf.Repeat(yaw,360);
    }
}
