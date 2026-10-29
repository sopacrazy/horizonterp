using UnityEngine;

[DefaultExecutionOrder(300)]
public sealed class MapRegionBoundary : MonoBehaviour
{
    public ThirdPersonPlayer player;
    float nextNotice;
    void LateUpdate()
    {
        var map=GetComponent<NeighborhoodMap>();
        if(!map||!map.restrictRegion||!player||!player.gameObject.activeInHierarchy||
            player.IsTeleporting||StartupLoader.IsLoading)return;
        var p=player.transform.position;
        // Keep intentional local interiors usable; old interior saves recover at entry.
        if(p.y< -45&&Mathf.Abs(p.x)<15&&Mathf.Abs(p.z)<25)return;
        if(map.ContainsXZ(p))return;
        p.x=Mathf.Clamp(p.x,map.regionMin.x+8,map.regionMax.x-8);
        p.z=Mathf.Clamp(p.z,map.regionMin.y+8,map.regionMax.y-8);
        player.TeleportTo(p,player.transform.eulerAngles.y);
        if(Time.unscaledTime>=nextNotice)
        {nextNotice=Time.unscaledTime+5;GameHud.Instance?.ShowToast("Limite da versão de teste: Los Santos");}
    }
}
