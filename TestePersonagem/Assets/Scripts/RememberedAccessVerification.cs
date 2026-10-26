using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class AccountSession
{
    internal IEnumerator VerifyRememberedAccess(Func<bool,string,bool> check,Func<string,bool,IEnumerator> capture)
    {
        var originalTransport=VerificationTransport;string originalToken=token;var originalState=State;
        bool originalReady=ReadyToEnterCity,originalAuth=AuthVisible;string stored=new string('A',43);
        string scenario="ok";int writes=0,reads=0;
        SessionVault.TestRead=()=>stored;SessionVault.TestWrite=value=>{stored=value;writes++;};SessionVault.TestClear=()=>stored=null;
        VerificationTransport=(method,path,body,authenticated,done)=>RememberedReply(method,path,body,authenticated,done,scenario,originalState,()=>reads++);
        try
        {
            check(SessionVault.VerifyNativeStorage(),"Cofre nativo do Windows grava, recupera e remove uma credencial temporária isolada");
            SessionVault.Write(new string('A',43));check(writes==1&&stored.Length==43,"Acesso salvo contém apenas o token da sessão");
            yield return RestoreRemembered();yield return null;
            check(HasCharacter&&!ReadyToEnterCity&&AuthVisible&&rememberedPlay.text=="JOGAR"&&rememberedPlay.enabledInHierarchy,"Sessão salva válida mostra Jogar e aguarda confirmação");
            check(landing.resolvedStyle.display==DisplayStyle.None&&scroll.resolvedStyle.display==DisplayStyle.None&&characterLobby.resolvedStyle.display==DisplayStyle.None,"Acesso lembrado não exibe formulário nem seleção de personagem");
            check(reads==1&&State.characterId==originalState.characterId,"Reentrada recupera o personagem pela API autenticada");
            yield return capture("remembered-play",false);
            scenario="offline";yield return RestoreRemembered();
            check(!Authenticated&&!ReadyToEnterCity&&stored!=null&&rememberedPlay.enabledInHierarchy,"Falha de rede preserva acesso salvo e bloqueia entrada sem validação");
            scenario="ok";yield return RestoreRemembered();check(HasCharacter,"Repetir após falha de rede recupera a mesma sessão");
            scenario="pending";yield return RestoreRemembered();
            check(Authenticated&&!HasCharacter&&characterCreation.resolvedStyle.display==DisplayStyle.Flex,"Conta salva sem personagem abre criação");
            scenario="expired";yield return RestoreRemembered();
            check(!Authenticated&&stored==null&&scroll.resolvedStyle.display==DisplayStyle.Flex,"Sessão expirada é esquecida e pede login novamente");
            stored=new string('B',43);scenario="blocked";yield return RestoreRemembered();
            check(!Authenticated&&stored==null&&!ReadyToEnterCity,"Personagem bloqueado não reutiliza o acesso salvo");
            stored=new string('C',43);SessionVault.Clear();check(stored==null,"Trocar conta remove o acesso persistido");
        }
        finally
        {
            VerificationTransport=originalTransport;SessionVault.TestRead=null;SessionVault.TestWrite=null;SessionVault.TestClear=null;
            token=originalToken;State=originalState;ReadyToEnterCity=originalReady;AuthVisible=originalAuth;busy=false;HideRemembered();HideCharacterLobby();
            root.style.display=originalAuth?DisplayStyle.Flex:DisplayStyle.None;
        }
    }
    static IEnumerator RememberedReply(string method,string path,string body,bool authenticated,Action<ApiReply,string> done,string scenario,ApiPlayerState state,Action read)
    {
        yield return null;
        if(method!="GET"||path!="/v1/player"||body!=null||!authenticated)throw new InvalidOperationException("A restauração deve usar somente GET autenticado.");
        read();
        if(scenario=="offline")done(null,"Sem conexão de teste.");
        else if(scenario=="expired")done(new ApiReply{code="UNAUTHORIZED"},"Sessão expirada.");
        else if(scenario=="blocked")done(new ApiReply{code="CHARACTER_BLOCKED"},"Personagem bloqueado.");
        else done(new ApiReply{state=scenario=="pending"?null:state,needsCharacter=scenario=="pending"},null);
    }
}
