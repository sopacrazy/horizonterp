using UnityEngine;

// Scene-specific navigation limits, spawn and overhead map configuration, in metres.
public sealed class NeighborhoodMap : MonoBehaviour
{
    public static NeighborhoodMap Active {get;private set;}
    public float halfExtent=60;
    public float groundLevel=-.15f;
    public float minY=-1;
    public float maxY=8;
    public Vector3 spawnPosition=new Vector3(0,-.1f,0);
    public bool isOfficialCity=false;
    public float mapCameraHeight=60;
    public string worldCode="centro_teste";
    public string displayName="Bairro Horizonte · Praça Central";
    public bool restrictRegion;
    public Vector2 regionMin,regionMax;
    public bool ContainsXZ(Vector3 position) => !restrictRegion||
        (position.x>=regionMin.x&&position.x<=regionMax.x&&position.z>=regionMin.y&&position.z<=regionMax.y);
    public Vector3 SpawnPosition => spawnPosition;
    public float MapHalfExtent => halfExtent+4;
    public bool Contains(Vector3 position) => Finite(position.x)&&Finite(position.y)&&Finite(position.z)&&
        Mathf.Abs(position.x)<=halfExtent-1&&Mathf.Abs(position.z)<=halfExtent-1&&position.y>=minY&&position.y<=maxY&&ContainsXZ(position);

    void Awake(){Active=this;}
    void OnDestroy(){if(Active==this)Active=null;}
    static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);

    // Resolve close to the saved floor; a roof high above a save is not a replacement street.
    // The caller disables its controller while finding a clear capsule.
    public Vector3 ResolveSavedPosition(Vector3 requested,CharacterController controller)
    {
        Physics.SyncTransforms();
        // The old test interior is generated at the origin, but is not a valid
        // arrival point in the imported San Andreas world.
        if (GetComponent<GtaSaSectorStreaming>() && requested.y < -45f &&
            Mathf.Abs(requested.x) < 15f && Mathf.Abs(requested.z) < 25f)
            requested = SpawnPosition;
        if(Contains(requested)&&TryStand(requested,controller,out var resolved))return resolved;
        if(TryStand(SpawnPosition,controller,out resolved))return resolved;
        foreach(var offset in new[]{new Vector3(2,0,0),new Vector3(-2,0,0),new Vector3(0,0,2),
            new Vector3(0,0,-2),new Vector3(4,0,0),new Vector3(-4,0,0),new Vector3(0,0,-4)})
            if(TryStand(SpawnPosition+offset,controller,out resolved))return resolved;
        return SpawnPosition;
    }

    bool TryStand(Vector3 point,CharacterController controller,out Vector3 result)
    {
        result=point;
        if(!controller||!Contains(point))return false;
        float feetOffset=controller.center.y-controller.height*.5f;
        float expectedFloor=point.y+feetOffset;
        var hits=Physics.RaycastAll(new Vector3(point.x,expectedFloor+3,point.z),Vector3.down,6,1,QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits,(a,b)=>Mathf.Abs(a.point.y-expectedFloor).CompareTo(Mathf.Abs(b.point.y-expectedFloor)));
        foreach(var hit in hits)
        {
            if(hit.normal.y<.7f||hit.point.y>expectedFloor+Mathf.Max(.5f,controller.stepOffset+.1f))continue;
            var candidate=new Vector3(point.x,hit.point.y-feetOffset+.04f,point.z);
            if(!Contains(candidate))continue;
            Vector3 center=candidate+controller.center;
            float radius=controller.radius*.96f;
            float segment=Mathf.Max(0,controller.height*.5f-radius);
            if(Physics.CheckCapsule(center-Vector3.up*segment,center+Vector3.up*segment,radius,1,QueryTriggerInteraction.Ignore))continue;
            result=candidate;return true;
        }
        return false;
    }
}
