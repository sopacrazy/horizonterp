using System;
using System.Collections;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

[Serializable] public sealed class RealtimeAvailability {public bool available;}
[Serializable] public sealed class VivoxSetup
{public bool available;public string server,domain,issuer,environmentId,projectId,playerId,proximity,radioPrefix;}
[Serializable] public sealed class RealtimeServices {public RealtimeAvailability udp;public VivoxSetup vivox;}
[Serializable] public sealed class UdpTicketReply
{public bool available;public string host,serverName,caCertificate,ticket;public int port;public long expiresAt;}

public sealed partial class AccountSession
{
    SynchronizationContext serviceContext;
    void InitializeRealtimeContext(){serviceContext=SynchronizationContext.Current;}
    internal Task<string> ServiceJsonAsync(string path,string body=null)
    {
        var done=new TaskCompletionSource<string>();
        if(serviceContext==null){done.SetResult(null);return done.Task;}
        serviceContext.Post(_=>
        {
            if(!this){done.TrySetResult(null);return;}
            StartCoroutine(ServiceRequest(path,body,text=>done.TrySetResult(text)));
        },null);
        return done.Task;
    }
    IEnumerator ServiceRequest(string path,string body,Action<string> done)
    {
        if(!HasCharacter||VerificationTransport!=null||leaving||
            (path!="/v1/services"&&path!="/v1/realtime/ticket"&&path!="/v1/voice/token")){done(null);yield break;}
        string requestToken=token;
        using(var request=new UnityWebRequest(config.baseUrl+path,body==null?"GET":"POST"))
        {
            request.timeout=4;request.redirectLimit=0;request.certificateHandler=new PinnedCertificate(config.certificateSha256);
            request.downloadHandler=new DownloadHandlerBuffer();
            if(body!=null){request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));request.SetRequestHeader("Content-Type","application/json");}
            request.SetRequestHeader("Authorization","Bearer "+requestToken);
            yield return request.SendWebRequest();
            if(request.responseCode==401&&token==requestToken)yield return ConfirmSessionAfterUnauthorized(requestToken);
            done(token!=requestToken?null:request.responseCode==404?"{\"available\":false}":request.result==UnityWebRequest.Result.Success?request.downloadHandler.text:null);
        }
    }
}
