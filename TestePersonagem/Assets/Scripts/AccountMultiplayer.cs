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
    public bool jumping;
    public int afterChat;
}
[Serializable] public class MultiplayerPeer
{
    public string characterId,uid,name,firstName,model;
    public int sequence;
    public long updatedAt;
    public float x,y,z,yaw,speed,jumpPhase;
    public bool jumping;
}
[Serializable] public class AdminCommandReply
{
    public string name,status,message,uid;
    public string[] lines;
    public int value;
    public float x,y,z;
}
[Serializable] public class AdminTeleport {public int id;public float x,y,z;}
[Serializable] public class MultiplayerReply
{
    public string room,error,code,chatEpoch;
    public int capacity,count,sequence,serverHour;
    public bool hourOverride;
    public long serverTime;
    public MultiplayerPeer[] peers;
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
            request.timeout=4;request.redirectLimit=0;
            request.certificateHandler=new PinnedCertificate(config.certificateSha256);
            request.downloadHandler=new DownloadHandlerBuffer();
            request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.SetRequestHeader("Content-Type","application/json");
            request.SetRequestHeader("Authorization","Bearer "+token);
            yield return request.SendWebRequest();
            MultiplayerReply reply=null;
            try{reply=JsonUtility.FromJson<MultiplayerReply>(request.downloadHandler.text);}catch{}
            if(request.result!=UnityWebRequest.Result.Success&&string.IsNullOrEmpty(reply?.code))
                reply=new MultiplayerReply {code="CONNECTION_LOST",error=request.error};
            if(request.responseCode==401)reply=new MultiplayerReply {code="UNAUTHORIZED"};
            if(request.responseCode==401||reply?.code=="CHARACTER_BLOCKED")InvalidateSession(reply?.error??"Sua sessão foi encerrada. Entre novamente.");
            done(reply);
        }
    }
}
