using System;
using System.Collections;
using UnityEngine;

[Serializable]public class ArrivalConfig
{
    public string code;public Vector3 giver,car,cityHall;public Vector3[] route;
    public int rewardXp,rewardCashCents,loanMinutes;
}
[Serializable]public class ArrivalState{public string phase,loanId,loanExpiresAt;public ArrivalConfig config;}
[Serializable]public class ArrivalAction{public string action;}
public sealed partial class AccountSession
{
    internal IEnumerator ArrivalRequest(string action,Action<ApiReply> done)
    {
        if(!HasCharacter){done(null);yield break;}
        ApiReply reply=null;
        yield return Request(action==null?"GET":"POST","/v1/missions/arrival",action==null?null:JsonUtility.ToJson(new ArrivalAction{action=action}),true,r=>reply=r);
        if(reply?.state!=null)
        {
            State=reply.state;
            if(player){if(long.TryParse(State.cashCents,out long cents))player.Cash=cents/100;float.TryParse(State.xp,out player.Experience);player.Level=State.level;}
            GameHud.Instance?.ApplyAccountIdentity();
        }
        done(reply);
    }
}
