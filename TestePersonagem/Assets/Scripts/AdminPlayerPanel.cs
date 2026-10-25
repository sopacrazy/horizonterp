using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class AdminPlayerPanel : MonoBehaviour
{
    public static AdminPlayerPanel Instance {get;private set;}
    public bool IsOpen {get;private set;}
    public bool Busy {get;private set;}
    VisualElement overlay,modal,confirmation;
    ScrollView table;
    TextField search,reason;
    IntegerField level;
    Label status,count,page,error;
    Button previous,next,close,confirm;
    GameHud hud;
    AdminPlayer selected;
    string selectedAction;
    int offset,total,viewerLevel,generation;
    IVisualElementScheduledItem debounce;

    public static void Open(GameHud hud)
    {
        if(!AccountSession.HasCharacter||AccountSession.Instance.State.accountRole!="admin"||AccountSession.Instance.State.adminLevel<1)return;
        if(!Instance){var panel=hud.gameObject.AddComponent<AdminPlayerPanel>();panel.Build(hud);}
        Instance.Show();
    }
    void Awake(){Instance=this;}
    VisualElement Box(string css,VisualElement parent){var element=new VisualElement();element.AddToClassList(css);parent.Add(element);return element;}
    Label Text(string value,string css,VisualElement parent)
    {var label=new Label(value){enableRichText=false,pickingMode=PickingMode.Ignore};label.AddToClassList(css);parent.Add(label);return label;}
    Button Button(string value,string css,Action action,VisualElement parent)
    {var button=new Button(action){text=value};button.AddToClassList(css);parent.Add(button);return button;}
    void Build(GameHud owner)
    {
        hud=owner;hud.Root.styleSheets.Add(Resources.Load<StyleSheet>("AdminStyles"));
        overlay=Box("admin-backdrop",hud.Root);overlay.name="admin-overlay";overlay.style.display=DisplayStyle.None;
        modal=Box("admin-modal",overlay);
        var header=Box("admin-header",modal);var titles=Box("admin-titles",header);
        Text("ADMINISTRAÇÃO","admin-eyebrow",titles);Text("Jogadores","admin-title",titles);
        Text("Gerencie permissões e acesso à cidade.","admin-subtitle",titles);
        close=Button("×","admin-close",()=>Close(),header);close.name="admin-close";
        var toolbar=Box("admin-toolbar",modal);
        search=new TextField{maxLength=64,name="admin-search"};search.textEdition.placeholder="Pesquisar por primeiro nome ou ID";search.AddToClassList("admin-search");toolbar.Add(search);
        search.RegisterValueChangedCallback(_=>{debounce?.Pause();debounce=search.schedule.Execute(()=>{offset=0;Load();}).StartingIn(350);});
        Button("Atualizar","admin-outline",()=>Load(),toolbar).name="admin-refresh";
        var headings=Box("admin-table-head",modal);
        Text("ID","admin-col-id",headings);Text("NOME","admin-col-name",headings);Text("PERMISSÃO","admin-col-role",headings);Text("PERSONAGEM","admin-col-state",headings);Text("AÇÕES","admin-col-actions",headings);
        table=new ScrollView(ScrollViewMode.Vertical);table.verticalScrollerVisibility=ScrollerVisibility.Hidden;table.horizontalScrollerVisibility=ScrollerVisibility.Hidden;
        table.contentContainer.style.width=Length.Percent(100);table.AddToClassList("admin-table");table.name="admin-table";modal.Add(table);
        status=Text("","admin-status",modal);status.name="admin-status";
        var footer=Box("admin-footer",modal);count=Text("","admin-count",footer);page=Text("","admin-page",footer);
        previous=Button("‹","admin-page-button",()=>{offset=Mathf.Max(0,offset-25);Load();},footer);previous.name="admin-previous";
        next=Button("›","admin-page-button",()=>{offset+=25;Load();},footer);next.name="admin-next";
        confirmation=Box("admin-confirm-shade",overlay);confirmation.style.display=DisplayStyle.None;
    }
    void Show()
    {
        InGameChat.Instance?.Close();hud.ClosePanel();IsOpen=true;overlay.style.display=DisplayStyle.Flex;
        hud.Root.AddToClassList("hud-panel-open");offset=0;search.SetValueWithoutNotify("");Load();
    }
    public void Close(bool force=false)
    {
        if(Busy&&!force)return;
        IsOpen=false;generation++;debounce?.Pause();
        if(overlay!=null)overlay.style.display=DisplayStyle.None;
        if(confirmation!=null)confirmation.style.display=DisplayStyle.None;
        if(modal!=null)modal.SetEnabled(true);
        if(hud)hud.Root.RemoveFromClassList("hud-panel-open");
    }
    void Update()
    {
        if(IsOpen&&(!AccountSession.HasCharacter||AccountSession.Instance.State.accountRole!="admin"))Close(true);
    }
    void Load()
    {
        if(!IsOpen||Busy)return;int current=++generation;
        status.text="Carregando jogadores...";table.Clear();
        StartCoroutine(Fetch(current));
    }
    IEnumerator Fetch(int current)
    {
        AdminReply reply=null;
        yield return AccountSession.Instance.AdminRequest("GET","/v1/admin/players?q="+Uri.EscapeDataString(search.value.Trim())+"&offset="+offset,null,r=>reply=r);
        if(!IsOpen||current!=generation)yield break;
        if(reply==null||!string.IsNullOrEmpty(reply.code)){status.text=reply?.error??"Não foi possível carregar a lista.";yield break;}
        total=reply.total;viewerLevel=reply.viewerAdminLevel;
        foreach(var player in reply.players??Array.Empty<AdminPlayer>())Row(player);
        status.text=table.childCount==0?"Nenhum jogador encontrado.":"";
        count.text=total+" jogador"+(total==1?"":"es");page.text="Página "+(offset/25+1);
        previous.SetEnabled(offset>0);next.SetEnabled(offset+25<total);
    }
    void Row(AdminPlayer player)
    {
        var row=Box("admin-table-row",table);row.name="admin-row-"+player.uid;
        Text(player.uid,"admin-col-id",row);
        var name=Box("admin-col-name",row);Text(player.firstName,"admin-player-name",name);
        if(player.isSelf)Text("Você","admin-self",name);
        Text(player.adminLevel>0?"Admin · nível "+player.adminLevel:"Jogador","admin-col-role",row);
        var badge=Text(player.blocked?"BLOQUEADO":"ATIVO","admin-col-state",row);badge.AddToClassList(player.blocked?"admin-blocked":"admin-active");
        var actions=Box("admin-col-actions",row);
        var role=Button("Admin","admin-outline",()=>Choose(player,"set_admin"),actions);role.name="admin-role-"+player.uid;role.SetEnabled(player.canManage);
        var block=Button(player.blocked?"Liberar":"Bloquear",player.blocked?"admin-outline":"admin-danger",()=>Choose(player,player.blocked?"unblock":"block"),actions);
        block.name="admin-block-"+player.uid;block.SetEnabled(player.canManage);
        if(!player.canManage)actions.tooltip=player.isSelf?"Você não pode alterar a própria conta.":"Só é possível alterar níveis inferiores ao seu.";
    }
    void Choose(AdminPlayer player,string action)
    {
        if(Busy||!player.canManage)return;selected=player;selectedAction=action;
        modal.SetEnabled(false);
        confirmation.Clear();confirmation.style.display=DisplayStyle.Flex;
        var card=Box("admin-confirm-card",confirmation);
        Text(action=="set_admin"?"Permissões administrativas":action=="block"?"Bloquear personagem":"Liberar personagem","admin-confirm-title",card);
        Text(player.firstName+"  ·  ID "+player.uid,"admin-confirm-player",card);
        if(action=="set_admin")
        {
            level=new IntegerField("Nível administrativo"){value=player.adminLevel>0?player.adminLevel:1,name="admin-level"};level.AddToClassList("admin-field");card.Add(level);
            level.RegisterValueChangedCallback(evt=>level.SetValueWithoutNotify(Mathf.Clamp(evt.newValue,0,viewerLevel)));
            Text("Nível 0: jogador. Você pode conceder até o nível "+viewerLevel+".","admin-hint",card);
        }
        else Text(action=="block"?"O personagem sairá da cidade e ficará impedido de entrar até ser liberado.":"O personagem poderá voltar a entrar na cidade.","admin-hint",card);
        if(action=="unblock"&&!string.IsNullOrEmpty(player.blockedReason))Text("Motivo registrado: "+player.blockedReason,"admin-hint",card);
        reason=new TextField(action=="block"?"Motivo do bloqueio":"Observação (opcional)"){maxLength=180,name="admin-reason"};reason.AddToClassList("admin-field");card.Add(reason);
        error=Text("","admin-error",card);error.name="admin-action-error";
        reason.RegisterValueChangedCallback(_=>{if(!Busy)error.text="";});
        var buttons=Box("admin-confirm-buttons",card);
        Button("Cancelar","admin-outline",()=>{if(!Busy){confirmation.style.display=DisplayStyle.None;modal.SetEnabled(true);}},buttons).name="admin-cancel";
        confirm=Button("Confirmar",action=="block"?"admin-danger":"admin-gold",()=>StartCoroutine(ApplyAction()),buttons);confirm.name="admin-confirm";
    }
    IEnumerator ApplyAction()
    {
        if(Busy||selected==null)yield break;
        if(selectedAction=="block"&&reason.value.Trim().Length<3){error.text="Informe o motivo do bloqueio.";yield break;}
        Busy=true;generation++;confirm.SetEnabled(false);close.SetEnabled(false);error.text="Salvando alteração...";
        error.AddToClassList("admin-saving");
        var action=new AdminAction{characterId=selected.characterId,action=selectedAction,adminLevel=selectedAction=="set_admin"?level.value:0,reason=reason.value.Trim()};
        AdminReply reply=null;
        yield return AccountSession.Instance.AdminRequest("POST","/v1/admin/players/"+selected.uid,JsonUtility.ToJson(action),r=>reply=r);
        Busy=false;close.SetEnabled(true);confirm.SetEnabled(true);
        error.RemoveFromClassList("admin-saving");
        if(!IsOpen)yield break;
        if(reply==null||!string.IsNullOrEmpty(reply.code)){error.text=reply?.error??"Não foi possível confirmar. Atualize a lista antes de tentar novamente.";yield break;}
        confirmation.style.display=DisplayStyle.None;modal.SetEnabled(true);hud.ShowToast("Alteração salva para "+selected.firstName+".");Load();
    }
    void OnDestroy(){if(Instance==this)Instance=null;}
}
