using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class GameHud
{
    readonly List<RadarPlayer> radarPlayers=new List<RadarPlayer>();
    readonly Dictionary<string,VisualElement> miniDots=new Dictionary<string,VisualElement>();
    readonly Dictionary<string,Button> mapDots=new Dictionary<string,Button>();
    readonly List<string> staleDots=new List<string>();
    readonly HashSet<string> radarLive=new HashSet<string>();
    VisualElement radarInfo;
    Label radarInfoName,radarInfoId;
    string selectedRadarId;
    float nextRadarRefresh;

    void ResetMapRadar()
    {
        mapDots.Clear();selectedRadarId=null;
        radarInfo=null;radarInfoName=radarInfoId=null;
    }
    void ShowRadarPlayer(string id)
    {
        CancelCityMapSelection();selectedRadarId=id;
        if(radarInfo==null)
        {
            radarInfo=Element("radar-player-info",content);
            Text("JOGADOR ONLINE","radar-info-kicker",radarInfo);
            radarInfoName=Text("","radar-info-name",radarInfo);
            radarInfoId=Text("","radar-info-id",radarInfo);
            var close=new Button(()=>{selectedRadarId=null;radarInfo.style.display=DisplayStyle.None;}){text="×"};
            close.AddToClassList("radar-info-close");radarInfo.Add(close);
            radarInfo.RegisterCallback<PointerDownEvent>(e=>e.StopPropagation());
        }
        RefreshRadarInfo();
    }
    void RefreshRadarInfo()
    {
        if(radarInfo==null)return;
        foreach(var peer in radarPlayers)
        {
            if(peer.characterId!=selectedRadarId)continue;
            radarInfoName.text=peer.name;
            radarInfoId.text="ID: "+peer.uid+" · "+Mathf.RoundToInt(Vector2.Distance(new Vector2(player.transform.position.x,player.transform.position.z),new Vector2(peer.position.x,peer.position.z)))+" m";
            radarInfo.style.display=DisplayStyle.Flex;return;
        }
        selectedRadarId=null;radarInfo.style.display=DisplayStyle.None;
    }
    void RefreshPlayerRadar(bool force=false)
    {
        if(!force&&Time.unscaledTime<nextRadarRefresh)return;
        nextRadarRefresh=Time.unscaledTime+.05f;
        radarPlayers.Clear();MultiplayerWorld.Instance?.GetRadarPlayers(radarPlayers);
        radarLive.Clear();
        var self=player.transform.position;
        float width=miniViewport.resolvedStyle.width;
        if(float.IsNaN(width)||width<=0)return;
        foreach(var peer in radarPlayers)
        {
            radarLive.Add(peer.characterId);
            if(!miniDots.TryGetValue(peer.characterId,out var miniDot))
            {
                miniDot=new VisualElement{pickingMode=PickingMode.Ignore};
                miniDot.AddToClassList("radar-mini-dot");miniViewport.Add(miniDot);
                miniDots.Add(peer.characterId,miniDot);
            }
            bool near=PlayerRadar.Visible(false,false,self,peer.position);
            miniDot.style.display=near?DisplayStyle.Flex:DisplayStyle.None;
            if(near)
            {
                Vector2 point=PlayerRadar.OnMinimap(self,peer.position,cityMinimapYaw,width,MinimapVisibleMetres);
                // Players beyond the displayed streets remain direction markers on the rim.
                point=Vector2.ClampMagnitude(point,width*.5f-5);
                miniDot.style.left=width*.5f+point.x-4;miniDot.style.top=width*.5f+point.y-4;
            }
            if(CurrentPanel!="map"||mapArea==null)continue;
            bool visible=PlayerRadar.Visible(CanUseMapTeleport,true,self,peer.position);
            var uv=CityMapUV(peer.position);
            bool onAtlas=uv.x>=0&&uv.x<=1&&uv.y>=0&&uv.y<=1;
            visible&=CanUseMapTeleport||onAtlas;
            if(!mapDots.TryGetValue(peer.characterId,out var dot))
            {
                string id=peer.characterId;
                dot=new Button(()=>ShowRadarPlayer(id)){name="map-player-"+peer.uid};
                dot.AddToClassList("radar-map-hit");
                var circle=new VisualElement{pickingMode=PickingMode.Ignore};circle.AddToClassList("radar-map-dot");dot.Add(circle);
                dot.RegisterCallback<PointerDownEvent>(e=>e.StopPropagation());
                dot.RegisterCallback<PointerUpEvent>(e=>e.StopPropagation());
                mapArea.Add(dot);mapDots.Add(peer.characterId,dot);
            }
            dot.tooltip=peer.name+" ("+peer.uid+")"+(onAtlas?"":" · Fora da área deste mapa");
            dot.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;
            Vector2 mapPoint=cityImageOffset+uv*cityImageSize;
            if(!onAtlas)
            {
                mapPoint.x=Mathf.Clamp(mapPoint.x,14,mapArea.resolvedStyle.width-14);
                mapPoint.y=Mathf.Clamp(mapPoint.y,14,mapArea.resolvedStyle.height-14);
            }
            dot.style.left=mapPoint.x-14;dot.style.top=mapPoint.y-14;
        }
        staleDots.Clear();foreach(var id in miniDots.Keys)if(!radarLive.Contains(id))staleDots.Add(id);
        foreach(var id in staleDots){miniDots[id].RemoveFromHierarchy();miniDots.Remove(id);}
        staleDots.Clear();foreach(var id in mapDots.Keys)if(!radarLive.Contains(id))staleDots.Add(id);
        foreach(var id in staleDots){mapDots[id].RemoveFromHierarchy();mapDots.Remove(id);}
        // Keep the local arrow above the other players' dots.
        miniMarker?.BringToFront();mapMarker?.BringToFront();
        if(CurrentPanel=="map")RefreshRadarInfo();
    }
}
