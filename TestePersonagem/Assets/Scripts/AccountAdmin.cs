using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[Serializable] public sealed class AdminPlayer
{
    public string uid,characterId,firstName,accountRole,blockedReason;
    public int adminLevel;
    public bool blocked,isSelf,canManage;
}
[Serializable] public sealed class AdminReply
{
    public string error,code,viewerUid;
    public int total,offset,pageSize,viewerAdminLevel;
    public AdminPlayer[] players;
    public AdminPlayer player;
}
[Serializable] public sealed class AdminAction {public string characterId,action,reason;public int adminLevel;}
public sealed partial class AccountSession
{
    internal static Func<string,string,string,Action<AdminReply>,IEnumerator> AdminVerificationTransport;
    internal IEnumerator AdminRequest(string method,string path,string json,Action<AdminReply> done)
    {
        if(VerificationTransport!=null)
        {
            if(AdminVerificationTransport!=null&&Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-landing")>=0)
                yield return AdminVerificationTransport(method,path,json,done);
            else done(new AdminReply{code="ADMIN_REQUIRED",error="Transporte de teste administrativo indisponível."});
            yield break;
        }
        if(!HasCharacter){done(new AdminReply{code="UNAUTHORIZED",error="Entre novamente na sua conta."});yield break;}
        using(var request=new UnityWebRequest(config.baseUrl+path,method))
        {
            string requestToken=token;
            request.timeout=12;request.redirectLimit=0;
            request.certificateHandler=new PinnedCertificate(config.certificateSha256);
            request.downloadHandler=new DownloadHandlerBuffer();
            request.SetRequestHeader("Authorization","Bearer "+requestToken);
            if(json!=null){request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));request.SetRequestHeader("Content-Type","application/json");}
            yield return request.SendWebRequest();
            AdminReply reply=null;try{reply=JsonUtility.FromJson<AdminReply>(request.downloadHandler.text);}catch{}
            if(token==requestToken)
            {
                if(reply?.code=="CHARACTER_BLOCKED")InvalidateSession(reply.error);
                else if(request.responseCode==401)yield return ConfirmSessionAfterUnauthorized(requestToken);
            }
            if(reply?.code=="ADMIN_REQUIRED"&&State!=null){State.accountRole="user";State.adminLevel=0;}
            if(request.result!=UnityWebRequest.Result.Success&&string.IsNullOrEmpty(reply?.code))
                reply=new AdminReply{code="CONNECTION_LOST",error="A resposta não chegou. Atualize a lista para confirmar o resultado."};
            done(reply??new AdminReply{code="CONNECTION_LOST",error="Não foi possível consultar os jogadores."});
        }
    }
}
