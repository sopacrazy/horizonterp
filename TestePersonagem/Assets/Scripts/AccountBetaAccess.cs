using System;
using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

[Serializable] public sealed class BetaAccessRecord
{
    public string uid,label,createdAt,lastLoginAt,accountRole;
    public int adminLevel;
    public bool active,hasCharacter,isSelf,canManage;
}
[Serializable] public sealed class BetaAccessReply
{
    public string error,code,uid,password;
    public string[] uids;
    public bool credentialAvailable,revoked;
    public int total,offset,pageSize;
    public BetaAccessRecord[] accesses;
    public BetaAccessRecord access;
}
[Serializable] public sealed class BetaAccessCreateBody {public string requestId,label;}
[Serializable] public sealed class BetaAccessActionBody {public string requestId,reason;}

// The list model intentionally has no password field. Issued credentials live
// only in the one-time response and the open credential modal, never in prefs.
public sealed class BetaOneTimeCredential
{
    string uid,password;
    public bool HasValue=>!string.IsNullOrEmpty(uid)&&!string.IsNullOrEmpty(password);
    public string Uid=>uid;
    public string Password=>password;
    public bool Set(BetaAccessReply reply)
    {
        Clear();
        if(reply?.credentialAvailable!=true||!ValidUid(reply.access?.uid)||
            string.IsNullOrWhiteSpace(reply.password)||reply.password.Length<10||reply.password.Length>128)
        {if(reply!=null)reply.password=null;return false;}
        uid=reply.access.uid;password=reply.password;reply.password=null;return true;
    }
    public string CopyText()=>HasValue?"Horizonte Brasil RP · Beta equipe\nUID: "+uid+"\nSenha: "+password:"";
    public void Clear(){uid=null;password=null;}
    public static bool ValidUid(string value)=>value!=null&&Regex.IsMatch(value,@"^[1-9][0-9]{0,18}$");
}

public sealed partial class AccountSession
{
    internal static Func<string,string,string,Action<BetaAccessReply>,IEnumerator> BetaVerificationTransport;
    internal IEnumerator BetaAccessRequest(string method,string path,string json,Action<BetaAccessReply> done)
    {
        if(!BetaAccessPanel.CanManage(State)||!HasCharacter||leaving)
        {done(new BetaAccessReply {code="BETA_MANAGER_REQUIRED",error="Sua conta não tem permissão para gerenciar acessos beta."});yield break;}
        bool validPath=path=="/v1/admin/beta-access"||path.StartsWith("/v1/admin/beta-access?",StringComparison.Ordinal)||
            Regex.IsMatch(path,@"^/v1/admin/beta-access/[1-9][0-9]{0,18}/(?:reset|revoke)$");
        if(!validPath||(method!="GET"&&method!="POST"))
        {done(new BetaAccessReply {code="INVALID_REQUEST",error="Operação beta inválida."});yield break;}
        if(VerificationTransport!=null)
        {
            if(BetaVerificationTransport!=null&&Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-beta-panel")>=0)
                yield return BetaVerificationTransport(method,path,json,done);
            else done(new BetaAccessReply {code="ADMIN_REQUIRED",error="Transporte beta de verificação indisponível."});
            yield break;
        }
        string requestToken=token;
        using(var request=new UnityWebRequest(config.baseUrl+path,method))
        {
            request.timeout=12;request.redirectLimit=0;
            request.certificateHandler=new PinnedCertificate(config.certificateSha256);
            request.downloadHandler=new DownloadHandlerBuffer();
            request.SetRequestHeader("Authorization","Bearer "+requestToken);
            if(json!=null){request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));request.SetRequestHeader("Content-Type","application/json");}
            yield return request.SendWebRequest();
            BetaAccessReply reply=null;
            try{reply=JsonUtility.FromJson<BetaAccessReply>(request.downloadHandler.text);}catch{}
            if(token==requestToken)
            {
                if(reply?.code=="CHARACTER_BLOCKED")InvalidateSession(reply.error);
                else if(request.responseCode==401)yield return ConfirmSessionAfterUnauthorized(requestToken);
            }
            if(token!=requestToken)
            {if(reply!=null)reply.password=null;done(new BetaAccessReply {code="UNAUTHORIZED",error="A sessão foi encerrada."});yield break;}
            if(request.result!=UnityWebRequest.Result.Success&&string.IsNullOrEmpty(reply?.code))
                reply=new BetaAccessReply {code="CONNECTION_LOST",error="Não foi possível confirmar a operação. Tente novamente."};
            // Neither the response nor the issued password is written to logs.
            done(reply??new BetaAccessReply {code="CONNECTION_LOST",error="Não foi possível consultar os acessos beta."});
        }
    }
}
