using UnityEngine;
using UnityEngine.UIElements;

// Display-only for network players: healing/respawn is confirmed by the API.
public sealed class PlayerLifeHud : MonoBehaviour
{
    ThirdPersonPlayer player;
    VisualElement card;
    Label countdown;
    float diedAt=-1;
    public static PlayerLifeHud Create(ThirdPersonPlayer player)
        =>player.GetComponent<PlayerLifeHud>()??player.gameObject.AddComponent<PlayerLifeHud>();
    void Awake(){player=GetComponent<ThirdPersonPlayer>();}
    void Update()
    {
        if(!player||!GameHud.Instance)return;
        if(card==null)Build();
        bool dead=player.Health<=0;card.style.display=dead?DisplayStyle.Flex:DisplayStyle.None;
        if(!dead){diedAt=-1;return;}
        if(diedAt<0)diedAt=Time.unscaledTime;
        float deadline=MultiplayerWorld.Instance&&MultiplayerWorld.Instance.RespawnDeadline>0?
            MultiplayerWorld.Instance.RespawnDeadline:diedAt+5;
        int remaining=Mathf.Max(0,Mathf.CeilToInt(deadline-Time.unscaledTime));
        countdown.text=remaining>0?"RENASCENDO EM "+remaining+"s":"AGUARDANDO CONFIRMAÇÃO PARA RENASCER";
        // The offline scene is a local preview with no persisted player data.
        if(!AccountSession.HasCharacter&&Time.unscaledTime>=diedAt+5)
            player.ApplyConfirmedRespawn(player.spawnPosition,player.spawnYaw);
    }
    void Build()
    {
        card=new VisualElement{name="death-countdown",pickingMode=PickingMode.Ignore};
        card.style.position=Position.Absolute;card.style.left=Length.Percent(50);card.style.top=Length.Percent(48);
        card.style.width=340;card.style.marginLeft=-170;card.style.paddingTop=card.style.paddingBottom=16;
        card.style.alignItems=Align.Center;HudPack.Skin(card,12);
        var title=new Label("VOCÊ MORREU"){pickingMode=PickingMode.Ignore};title.style.color=new Color(1,.38f,.38f);
        title.style.fontSize=23;title.style.unityFontStyleAndWeight=FontStyle.Bold;card.Add(title);
        countdown=new Label{pickingMode=PickingMode.Ignore};countdown.style.color=Color.white;countdown.style.fontSize=13;
        countdown.style.marginTop=5;countdown.style.unityTextAlign=TextAnchor.MiddleCenter;card.Add(countdown);
        GameHud.Instance.Root.Add(card);
    }
    void OnDestroy(){card?.RemoveFromHierarchy();}
}
