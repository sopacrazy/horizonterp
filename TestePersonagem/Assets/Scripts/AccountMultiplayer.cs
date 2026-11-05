using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[Serializable] public class MultiplayerFrame
{
    public string clientId;
    public int sequence;
    public float x,y,z,yaw,speed,jumpPhase;
    public bool jumping,driving;
    public int afterChat;
}
[Serializable] public class MultiplayerPeer
{
    public string characterId,uid,name,firstName,gender,model,policeCarId,weaponType;
    public int sequence;
    public int adminLevel;
    public long updatedAt;
    public float x,y,z,yaw,speed,jumpPhase;
    public bool jumping,driving,policeDuty,cuffed,inPoliceCar,combatClient;
    public bool weaponEquipped,weaponAiming;
    public int weaponShotSequence;
    public float weaponAimPitch;
    public bool voiceActive,radioActive;
    public string escortedBy;
    public string voice;
    public int voiceSequence;
    public string radioVoice;
    public int radioSequence;
    public int radioChannel;
    public VoicePacket[] voiceFrames,radioFrames;
    public CombatState combat;
}
[Serializable] public class WeaponVisualFrame
{
    public bool weaponEquipped,weaponAiming;
    public int weaponShotSequence;
    public float weaponAimPitch;
}
[Serializable] public class CombatPeerState {public string uid;public CombatState combat;}
[Serializable] public class CombatAttackBody {public string clientId,attackId;}
[Serializable] public class RifleShotBody
{
    public string clientId,shotId,targetUid;
    public float originX,originY,originZ,directionX,directionY,directionZ;
}
[Serializable] public class CombatRespawnBody {public string clientId,requestId,epoch;public int deathSequence;}
[Serializable] public class VoicePacket {public int sequence;public string data;}
[Serializable] public class AdminCommandReply
{
    public string name,status,message,uid,gender;
    public string[] lines;
    public AdminHelpEntry[] help;
    public WantedEntry[] wanted;
    public int value;
    public float x,y,z;
}
[Serializable] public class AdminHelpEntry
{
    public string usage,description,group,permission,note;
    public int level;
    public bool available;
}
[Serializable] public class WantedEntry
{
    public string uid,name,reason;
    public int stars;
}
[Serializable] public class AdminTeleport {public int id;public float x,y,z;}
[Serializable] public class PoliceCarState
{
    public string id,ownerUid,occupantUid,occupantName,driverUid,driverName;
    public float x,y,z,yaw;
}
[Serializable] public class MultiplayerReply
{
    public string room,error,code,chatEpoch;
    public int capacity,count,sequence,serverHour;
    public bool hourOverride,selfCuffed,selfInPoliceCar;
    public bool voiceStateAvailable;
    public bool policeVehicleAvailable;
    public bool combatAvailable;
    public bool weaponStateAvailable;
    public bool pistolStateAvailable;
    public bool rifleDamageAvailable,respawnAvailable;
    public CombatState selfCombat;
    public CombatPeerState[] combatPeers;
    public string targetUid;
    public int damage;
    public string selfPoliceCarId;
    public string selfAccountRole;
    public int selfAdminLevel;
    public string selfEscortedBy;
    public float selfX,selfY,selfZ,selfYaw;
    public long serverTime;
    public MultiplayerPeer[] peers;
    public PoliceCarState[] policeCars;
    public ChatMessage[] messages;
    public ChatMessage message;
    public AdminCommandReply command;
    public AdminTeleport teleport;
}
[Serializable] public class ChatMessage
{
    public int id;
    public string messageId,uid,name,firstName,text,channel;
    public bool isAdmin;
    public long sentAt;
}
[Serializable] public class ChatBody {public string clientId,messageId,text;}

public sealed partial class AccountSession
{
    internal IEnumerator MultiplayerRequest(string path,string json,Action<MultiplayerReply> done)
    {
        if(!HasCharacter||!ReadyToEnterCity||(leaving&&!path.EndsWith("/leave"))||VerificationTransport!=null){done(null);yield break;}
        using(var request=new UnityWebRequest(config.baseUrl+path,"POST"))
        {
            string requestToken=token;
            request.timeout=4;request.redirectLimit=0;
            request.certificateHandler=new PinnedCertificate(config.certificateSha256);
            request.downloadHandler=new DownloadHandlerBuffer();
            request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.SetRequestHeader("Content-Type","application/json");
            request.SetRequestHeader("Authorization","Bearer "+requestToken);
            yield return request.SendWebRequest();
            MultiplayerReply reply=null;
            try{reply=JsonUtility.FromJson<MultiplayerReply>(request.downloadHandler.text);}catch{}
            if(request.result!=UnityWebRequest.Result.Success&&string.IsNullOrEmpty(reply?.code))
                reply=new MultiplayerReply {code="CONNECTION_LOST",error=request.error};
            if(request.responseCode==401)reply=new MultiplayerReply {code="UNAUTHORIZED"};
            if(token==requestToken)
            {
                if(reply?.code=="CHARACTER_BLOCKED")InvalidateSession(reply.error);
                else if(request.responseCode==401)yield return ConfirmSessionAfterUnauthorized(requestToken);
            }
            done(reply);
        }
    }
}
