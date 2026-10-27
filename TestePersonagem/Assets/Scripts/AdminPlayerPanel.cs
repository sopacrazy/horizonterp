using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

// /apanel is a read-only roster. Administrative actions are issued in chat.
public sealed class AdminPlayerPanel : MonoBehaviour
{
    public static AdminPlayerPanel Instance {get;private set;}
    public bool IsOpen {get;private set;}
    public bool Busy=>false;

    VisualElement overlay;
    ScrollView table;
    TextField search;
    Label status,count,page;
    Button previous,next;
    Button[] speedButtons;
    GameHud hud;
    int offset,total,generation;
    IVisualElementScheduledItem debounce;

    public static void Open(GameHud owner)
    {
        if(!AccountSession.HasCharacter ||
            AccountSession.Instance.State.accountRole!="admin" ||
            AccountSession.Instance.State.adminLevel<1)return;
        if(!Instance)owner.gameObject.AddComponent<AdminPlayerPanel>().Build(owner);
        Instance.Show();
    }

    void Awake(){Instance=this;}
    VisualElement Box(string css,VisualElement parent)
    {var element=new VisualElement();element.AddToClassList(css);parent.Add(element);return element;}
    Label Text(string value,string css,VisualElement parent)
    {var label=new Label(value){enableRichText=false,pickingMode=PickingMode.Ignore};label.AddToClassList(css);parent.Add(label);return label;}
    Button Button(string value,string css,Action action,VisualElement parent)
    {var button=new Button(action){text=value};button.AddToClassList(css);parent.Add(button);return button;}

    void Build(GameHud owner)
    {
        hud=owner;var styles=Resources.Load<StyleSheet>("AdminStyles");if(styles)hud.Root.styleSheets.Add(styles);
        overlay=Box("admin-backdrop",hud.Root);overlay.name="admin-overlay";overlay.style.display=DisplayStyle.None;
        var modal=Box("admin-modal",overlay);
        var header=Box("admin-header",modal);var titles=Box("admin-titles",header);
        Text("ADMINISTRAÇÃO","admin-eyebrow",titles);Text("Jogadores","admin-title",titles);
        Text("Lista de jogadores. Use os comandos pelo chat.","admin-subtitle",titles);
        Button("×","admin-close",()=>Close(),header).name="admin-close";
        Text("VELOCIDADE DE TESTE","admin-eyebrow",modal);
        var speedRow=Box("admin-speed-row",modal);var values=new[]{1,2,4,6,8,10,12};speedButtons=new Button[values.Length];
        for(int index=0;index<values.Length;index++)
        {
            int value=values[index];var option=Button("×"+value,"admin-speed-option",()=>SetSpeed(value),speedRow);
            option.name="admin-speed-"+value;speedButtons[index]=option;
        }
        var toolbar=Box("admin-toolbar",modal);
        search=new TextField{maxLength=64,name="admin-search"};
        search.textEdition.placeholder="Pesquisar por primeiro nome ou ID";search.AddToClassList("admin-search");toolbar.Add(search);
        search.RegisterValueChangedCallback(_=>{
            debounce?.Pause();
            debounce=search.schedule.Execute(()=>{offset=0;Load();}).StartingIn(350);
        });
        Button("Atualizar","admin-outline",Load,toolbar).name="admin-refresh";
        var headings=Box("admin-table-head",modal);
        Text("ID","admin-col-id",headings);Text("NOME","admin-col-name",headings);
        Text("PERMISSÃO","admin-col-role",headings);Text("PERSONAGEM","admin-col-state",headings);
        Text("AÇÕES","admin-col-actions",headings);
        table=new ScrollView(ScrollViewMode.Vertical){name="admin-table"};
        table.verticalScrollerVisibility=ScrollerVisibility.Hidden;
        table.horizontalScrollerVisibility=ScrollerVisibility.Hidden;
        table.contentContainer.style.width=Length.Percent(100);
        table.AddToClassList("admin-table");modal.Add(table);
        status=Text("","admin-status",modal);status.name="admin-status";
        var footer=Box("admin-footer",modal);
        count=Text("","admin-count",footer);page=Text("","admin-page",footer);
        previous=Button("‹","admin-page-button",()=>{offset=Mathf.Max(0,offset-25);Load();},footer);
        previous.name="admin-previous";
        next=Button("›","admin-page-button",()=>{offset+=25;Load();},footer);next.name="admin-next";
    }

    void Show()
    {
        InGameChat.Instance?.Close();hud.ClosePanel();
        IsOpen=true;overlay.style.display=DisplayStyle.Flex;
        hud.Root.AddToClassList("hud-panel-open");
        offset=0;search.SetValueWithoutNotify("");Load();RefreshSpeed();
    }
    void SetSpeed(int value)
    {
        if(!hud.Player.SetAdminSpeedMultiplier(value))return;
        RefreshSpeed();hud.ShowAccountMessage("Velocidade administrativa ×"+value);
    }
    void RefreshSpeed()
    {
        if(speedButtons==null)return;int selected=hud.Player.AdminSpeedMultiplier;
        foreach(var option in speedButtons)option.EnableInClassList("active",option.name=="admin-speed-"+selected);
    }
    public void Close(bool force=false)
    {
        IsOpen=false;generation++;debounce?.Pause();
        if(overlay!=null)overlay.style.display=DisplayStyle.None;
        if(hud)hud.Root.RemoveFromClassList("hud-panel-open");
    }
    void Update()
    {
        if(IsOpen&&(!AccountSession.HasCharacter||AccountSession.Instance.State.accountRole!="admin"||
            AccountSession.Instance.State.adminLevel<1))Close(true);
    }
    void Load()
    {
        if(!IsOpen)return;
        int current=++generation;status.text="Carregando jogadores...";table.Clear();
        StartCoroutine(Fetch(current));
    }
    IEnumerator Fetch(int current)
    {
        AdminReply reply=null;
        yield return AccountSession.Instance.AdminRequest("GET",
            "/v1/admin/players?q="+Uri.EscapeDataString(search.value.Trim())+"&offset="+offset,null,r=>reply=r);
        if(!IsOpen||current!=generation)yield break;
        if(reply==null||!string.IsNullOrEmpty(reply.code))
        {status.text=reply?.error??"Não foi possível carregar a lista.";yield break;}
        total=reply.total;
        foreach(var player in reply.players??Array.Empty<AdminPlayer>())Row(player);
        status.text=table.childCount==0?"Nenhum jogador encontrado.":"Comandos: use /ahelp no chat.";
        count.text=total+" jogador"+(total==1?"":"es");page.text="Página "+(offset/25+1);
        previous.SetEnabled(offset>0);next.SetEnabled(offset+25<total);
    }
    void Row(AdminPlayer player)
    {
        var row=Box("admin-table-row",table);row.name="admin-row-"+player.uid;
        Text(player.uid,"admin-col-id",row);
        Text(player.firstName,"admin-col-name",row);
        Text(player.adminLevel>0?"Admin · nível "+player.adminLevel:"Jogador","admin-col-role",row);
        var badge=Text(player.blocked?"BLOQUEADO":"ATIVO","admin-col-state",row);
        badge.AddToClassList(player.blocked?"admin-blocked":"admin-active");
        Text("No chat","admin-col-actions",row);
    }
    void OnDestroy(){if(Instance==this)Instance=null;}
}
