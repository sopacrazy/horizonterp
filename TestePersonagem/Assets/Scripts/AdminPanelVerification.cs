using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Explicit offline fixture only; no real player is promoted or blocked by these tests.
internal static class AdminPanelVerification
{
    static readonly List<AdminPlayer> rows=new List<AdminPlayer>();
    static int mutations;
    static IEnumerator Settle(Func<bool> ready)
    {float deadline=Time.realtimeSinceStartup+3;while(!ready()&&Time.realtimeSinceStartup<deadline)yield return null;}
    static void Press(Button button){button.Focus();using(var evt=NavigationSubmitEvent.GetPooled())button.SendEvent(evt);}
    static IEnumerator Reply(string method,string path,string json,Action<AdminReply> done)
    {
        yield return new WaitForSecondsRealtime(.05f);
        var state=AccountSession.Instance.State;
        if(state.accountRole!="admin"){done(new AdminReply{code="ADMIN_REQUIRED",error="Acesso exclusivo da administração."});yield break;}
        if(method=="GET")
        {
            string q="";foreach(var part in new Uri("https://fixture.invalid"+path).Query.TrimStart('?').Split('&'))
                if(part.StartsWith("q="))q=Uri.UnescapeDataString(part.Substring(2));
            var found=rows.Where(p=>string.IsNullOrEmpty(q)||p.uid==q||p.firstName.IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
            foreach(var player in found)player.canManage=!player.isSelf&&player.adminLevel<state.adminLevel;
            done(new AdminReply{players=found.Select(p=>JsonUtility.FromJson<AdminPlayer>(JsonUtility.ToJson(p))).ToArray(),total=found.Length,pageSize=25,viewerAdminLevel=state.adminLevel});
        }
        else
        {
            var player=rows.First(p=>path.EndsWith("/"+p.uid));var action=JsonUtility.FromJson<AdminAction>(json);
            if(player.isSelf||player.adminLevel>=state.adminLevel){done(new AdminReply{code="ADMIN_HIERARCHY",error="Nível protegido."});yield break;}
            mutations++;
            if(action.action=="set_admin"){player.adminLevel=action.adminLevel;player.accountRole=player.adminLevel>0?"admin":"user";}
            else player.blocked=action.action=="block";
            done(new AdminReply{player=player});
        }
    }
    internal static IEnumerator Run(Func<bool,string,bool> check,Func<string,bool,IEnumerator> capture,Action<string,int> setRole)
    {
        var state=AccountSession.Instance.State;string oldRole=state.accountRole;int oldLevel=state.adminLevel;
        setRole("admin",2);rows.Clear();mutations=0;
        rows.Add(new AdminPlayer{uid=state.uid,characterId=state.characterId,firstName=state.firstName,adminLevel=2,accountRole="admin",isSelf=true});
        foreach(var value in new[]{("42","Lucas",0),("43","Marina",0),("44","Ribeiro",2),("45","Helena",3)})
            rows.Add(new AdminPlayer{uid=value.Item1,characterId=Guid.NewGuid().ToString(),firstName=value.Item2,adminLevel=value.Item3,accountRole=value.Item3>0?"admin":"user"});
        AccountSession.AdminVerificationTransport=Reply;
        try
        {
            yield return null;var ui=GameHud.Instance.Root;Press(ui.Q<Button>("action-admin"));yield return new WaitForSecondsRealtime(.3f);
            check(AdminPlayerPanel.Instance&&AdminPlayerPanel.Instance.IsOpen&&GameHud.Instance.IsPanelOpen,"Coroa abre modal administrativo e bloqueia controles do jogo");
            check(ui.Q("admin-row-42")!=null&&ui.Q("admin-row-43")!=null,"Tabela administrativa carrega jogadores da API simulada");
            check(!ui.Q<Button>("admin-role-"+state.uid).enabledInHierarchy&&!ui.Q<Button>("admin-role-44").enabledInHierarchy&&!ui.Q<Button>("admin-role-45").enabledInHierarchy,"Própria conta e níveis iguais ou superiores ficam protegidos");
            foreach(var resolution in new[]{new Vector2Int(960,540),new Vector2Int(1280,720),new Vector2Int(1600,720)})
            {
                Screen.SetResolution(resolution.x,resolution.y,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.5f);
                var panel=ui.Q(className:"admin-modal").worldBound;var bounds=ui.worldBound;
                check(panel.xMin>=bounds.xMin&&panel.xMax<=bounds.xMax&&panel.yMin>=bounds.yMin&&panel.yMax<=bounds.yMax,"Modal dentro da tela em "+resolution.x+"x"+resolution.y);
                yield return capture("admin-table-"+resolution.x+"x"+resolution.y,false);
            }
            Screen.SetResolution(1280,720,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.3f);
            var search=ui.Q<TextField>("admin-search");search.value="43";yield return new WaitForSecondsRealtime(.55f);
            check(ui.Q("admin-row-43")!=null&&ui.Q("admin-row-42")==null,"Pesquisa por ID filtra o jogador correto");
            search.value="Lucas";yield return new WaitForSecondsRealtime(.55f);
            check(ui.Q("admin-row-42")!=null&&ui.Q("admin-row-43")==null,"Pesquisa por primeiro nome filtra corretamente");
            Press(ui.Q<Button>("admin-role-42"));yield return null;yield return capture("admin-role-dialog",false);
            ui.Q<IntegerField>("admin-level").value=1;Press(ui.Q<Button>("admin-confirm"));yield return new WaitForSecondsRealtime(.3f);
            check(rows.First(p=>p.uid=="42").adminLevel==1&&mutations==1,"Confirmação promove o jogador selecionado");
            search.value="43";yield return new WaitForSecondsRealtime(.55f);Press(ui.Q<Button>("admin-block-43"));yield return null;
            Press(ui.Q<Button>("admin-confirm"));yield return null;
            check(mutations==1&&!string.IsNullOrEmpty(ui.Q<Label>("admin-action-error").text),"Bloqueio exige motivo antes de enviar a alteração");
            ui.Q<TextField>("admin-reason").value="Teste de moderação";yield return capture("admin-block-dialog",false);
            Press(ui.Q<Button>("admin-confirm"));yield return new WaitForSecondsRealtime(.3f);
            check(rows.First(p=>p.uid=="43").blocked,"Bloqueio é confirmado e a lista recebe o novo estado");
            yield return Settle(()=>ui.Q<Button>("admin-block-43")?.text=="Liberar");
            check(ui.Q<Button>("admin-block-43")?.text=="Liberar","Lista oferece liberar depois de confirmar o bloqueio");
            Press(ui.Q<Button>("admin-block-43"));yield return new WaitForSecondsRealtime(.15f);
            Press(ui.Q<Button>("admin-confirm"));yield return Settle(()=>!rows.First(p=>p.uid=="43").blocked);
            check(!rows.First(p=>p.uid=="43").blocked,"Desbloqueio é reversível pelo mesmo painel");
            Press(ui.Q<Button>("admin-close"));yield return null;check(!AdminPlayerPanel.Instance.IsOpen&&!GameHud.Instance.IsPanelOpen,"Fechar modal devolve os controles do jogo");
        }
        finally{AdminPlayerPanel.Instance?.Close(true);AccountSession.AdminVerificationTransport=null;setRole(oldRole,oldLevel);}
    }
}
