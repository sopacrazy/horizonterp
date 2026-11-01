using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Isolated model checks. No access is issued and no account or network is opened.
public static class BetaPanelVerification
{
    [Serializable] sealed class Report {public bool passed;public List<string> checks=new List<string>();}
    [MenuItem("Horizonte/Testes locais/Validar painel Beta equipe")]
    public static void Run()
    {
        var result=new Report();
        void Check(bool condition,string text){if(!condition)throw new InvalidOperationException(text);result.checks.Add(text);}
        Check(!BetaAccessPanel.CanManage(null),"Missing identity never grants beta management");
        foreach(int level in new[]{0,1,2,3,4})
            Check(!BetaAccessPanel.CanManage(new ApiPlayerState {accountRole=level==0?"user":"admin",adminLevel=level}),"Level "+level+" cannot issue beta access");
        Check(BetaAccessPanel.CanManage(new ApiPlayerState {accountRole="admin",adminLevel=1,canManageBeta=true}),"An explicitly authorized level 1 manager can issue team access without promotion");
        Check(!BetaAccessPanel.CanManage(new ApiPlayerState {accountRole="admin",adminLevel=4,canManageBeta=true}),"The promotion-only role cannot use beta management even with a stale grant");
        Check(!BetaAccessPanel.CanManage(new ApiPlayerState {accountRole="admin",adminLevel=0,canManageBeta=true}),"A beta grant requires a positive admin role");
        Check(!BetaAccessPanel.CanManage(new ApiPlayerState {accountRole="user",adminLevel=3,canManageBeta=true}),"A numeric level and beta grant without the admin role grant no access");
        Check(!BetaAccessPanel.CanManageEntry(new BetaAccessRecord {uid="18",canManage=true},"18"),"The current UID remains protected even if self metadata is missing");
        Check(!BetaAccessPanel.CanManageEntry(new BetaAccessRecord {uid="52",isSelf=true,canManage=true},"18"),"Server self-protection cannot be bypassed by a different local UID");
        Check(!BetaAccessPanel.CanManageEntry(new BetaAccessRecord {uid="52",canManage=false},"18"),"Server hierarchy restrictions disable account actions");
        Check(BetaAccessPanel.CanManageEntry(new BetaAccessRecord {uid="52",canManage=true},"18"),"A manageable teammate account may be reset or revoked");
        Check(typeof(BetaAccessRecord).GetFields().All(field=>field.Name!="password"&&field.Name!="passwordHash"&&field.Name!="token"),"The list model cannot retain passwords, hashes or tokens");
        var oneTime=new BetaOneTimeCredential();
        var response=new BetaAccessReply {credentialAvailable=true,access=new BetaAccessRecord {uid="123",label="Fixture"},password="FixtureOnly-Password-123"};
        Check(oneTime.Set(response),"A valid one-time credential can be displayed");
        Check(response.password==null,"Displaying consumes the password field from the transport reply");
        Check(oneTime.HasValue&&oneTime.Uid=="123"&&oneTime.CopyText().Contains("UID: 123")&&oneTime.CopyText().Contains("Senha: FixtureOnly-Password-123"),"An explicit copy includes the individual UID and issued password");
        oneTime.Clear();
        Check(!oneTime.HasValue&&oneTime.Password==null&&oneTime.Uid==null&&oneTime.CopyText()=="","Closing clears the in-memory credential and copied-text source");
        foreach(string invalid in new[]{"0","-1","1/2","https://outside.invalid","1 2","abc",""})
            Check(!BetaOneTimeCredential.ValidUid(invalid),"Invalid UID is rejected: "+invalid);
        Check(BetaOneTimeCredential.ValidUid("1")&&BetaOneTimeCredential.ValidUid("9999999999999999999"),"Valid numeric UID bounds are accepted");
        response=new BetaAccessReply {credentialAvailable=true,access=new BetaAccessRecord {uid="bad"},password="FixtureOnly-Password-123"};
        Check(!oneTime.Set(response)&&!oneTime.HasValue&&response.password==null,"Invalid credential metadata is discarded without exposing the password");
        response=new BetaAccessReply {credentialAvailable=false,access=new BetaAccessRecord {uid="123"},password="FixtureOnly-Password-123"};
        Check(!oneTime.Set(response)&&response.password==null,"A list or repeated response cannot become a new one-time display");
        string serialized=JsonUtility.ToJson(new BetaAccessRecord {uid="123",label="Equipe",active=true});
        Check(!serialized.Contains("password")&&!serialized.Contains("token"),"Serializing the list entry exposes no credential fields");
        Check(!AccountSession.Instance&&!MultiplayerWorld.Instance,"Verification creates no session and contacts no server");
        result.passed=true;string folder=Path.GetFullPath("../tools/beta-access");Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"panel-validation.json"),JsonUtility.ToJson(result,true));
        Debug.Log("BETA_PANEL_VERIFIED checks="+result.checks.Count);
    }
}
