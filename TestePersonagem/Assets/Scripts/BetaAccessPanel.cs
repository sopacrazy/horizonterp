using System;
using System.Collections;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class BetaAccessPanel : MonoBehaviour
{
    public static BetaAccessPanel Instance {get;private set;}
    public bool IsOpen {get;private set;}
    public bool Busy {get;private set;}
    GameHud hud;
    VisualElement overlay,confirmShade,credentialShade;
    ScrollView list;
    TextField label,search,secretPassword;
    Label status,totalLabel,pageLabel,confirmTitle,confirmDescription,confirmError,credentialUid,credentialStatus;
    Button issue,refresh,previous,next,confirmButton,showSecret;
    string actionUid,actionKind,actionId,pendingCreateId,pendingCreateLabel,notice;
    int visit,listVisit,offset,total;
    bool secretRevealed;
    readonly BetaOneTimeCredential credential=new BetaOneTimeCredential();
    IVisualElementScheduledItem debounce;

    public static bool CanManage(ApiPlayerState state)=>AdminPermissions.CanUseTools(state)&&state.canManageBeta;
    public static bool CanManageEntry(BetaAccessRecord record,string ownUid)=>record!=null&&record.canManage&&!record.isSelf&&
        BetaOneTimeCredential.ValidUid(record.uid)&&!string.Equals(record.uid,ownUid,StringComparison.Ordinal);
    bool Allowed=>AccountSession.HasCharacter&&CanManage(AccountSession.Instance.State);
    public static void Open(GameHud owner)
    {
        if(!owner||!AccountSession.HasCharacter||!CanManage(AccountSession.Instance.State))return;
        if(!Instance)owner.gameObject.AddComponent<BetaAccessPanel>().Build(owner);
        Instance.Show();
    }
    void Awake(){Instance=this;}
    static VisualElement Box(string css,VisualElement parent)
    {var element=new VisualElement();foreach(string token in css.Split(' '))if(token.Length>0)element.AddToClassList(token);parent.Add(element);return element;}
    static Label Text(string value,string css,VisualElement parent)
    {var element=new Label(value){enableRichText=false,pickingMode=PickingMode.Ignore};element.AddToClassList(css);parent.Add(element);return element;}
    static Button Button(string value,string css,Action action,VisualElement parent)
    {var button=new Button(action){text=value};button.AddToClassList(css);parent.Add(button);return button;}

    void Build(GameHud owner)
    {
        hud=owner;var style=Resources.Load<StyleSheet>("BetaAccessStyles");if(style)hud.Root.styleSheets.Add(style);
        overlay=Box("beta-backdrop",hud.Root);overlay.name="beta-access-overlay";overlay.style.display=DisplayStyle.None;
        var modal=Box("beta-modal",overlay);
        var header=Box("beta-header",modal);var headings=Box("beta-headings",header);
        Text("ACESSO RESTRITO","beta-eyebrow",headings);Text("Beta · equipe","beta-title",headings);
        Text("Emita acessos individuais por UID e senha para sua equipe.","beta-subtitle",headings);
        Button("×","beta-close",Close,header).name="beta-access-close";
        var entry=Box("beta-issue-card",modal);
        Text("NOVO ACESSO","beta-eyebrow",entry);
        var row=Box("beta-issue-row",entry);
        label=new TextField {name="beta-access-label",maxLength=48};label.AddToClassList("beta-input");
        label.textEdition.placeholder="Nome da pessoa ou identificação da equipe";row.Add(label);
        issue=Button("Emitir acesso","beta-gold",()=>StartCoroutine(Create()),row);issue.name="beta-access-create";
        Text("Cada acesso cria uma conta de jogador. O personagem é criado ao entrar.","beta-hint",entry);
        var tools=Box("beta-toolbar",modal);
        search=new TextField {name="beta-access-search",maxLength=64};search.AddToClassList("beta-input");
        search.textEdition.placeholder="Pesquisar por UID ou identificação";tools.Add(search);
        search.RegisterValueChangedCallback(_=>{debounce?.Pause();debounce=search.schedule.Execute(()=>{offset=0;Load();}).StartingIn(300);});
        refresh=Button("Atualizar","beta-outline",()=>{notice=null;Load();},tools);refresh.name="beta-access-refresh";
        list=new ScrollView(ScrollViewMode.Vertical) {name="beta-access-list"};list.AddToClassList("beta-list");
        list.horizontalScrollerVisibility=ScrollerVisibility.Hidden;modal.Add(list);
        status=Text("","beta-status",modal);status.name="beta-access-status";
        var footer=Box("beta-footer",modal);totalLabel=Text("","beta-total",footer);pageLabel=Text("","beta-page",footer);
        previous=Button("‹","beta-page-button",()=>{offset=Mathf.Max(0,offset-25);Load();},footer);
        next=Button("›","beta-page-button",()=>{offset+=25;Load();},footer);
        BuildConfirmation();BuildCredential();
        overlay.RegisterCallback<GeometryChangedEvent>(_=>overlay.EnableInClassList("beta-compact",overlay.resolvedStyle.width<760));
    }
    void BuildConfirmation()
    {
        confirmShade=Box("beta-dialog-shade",overlay);confirmShade.name="beta-access-confirm";confirmShade.style.display=DisplayStyle.None;
        var card=Box("beta-dialog",confirmShade);confirmTitle=Text("","beta-dialog-title",card);
        confirmDescription=Text("","beta-dialog-text",card);confirmError=Text("","beta-error",card);
        var buttons=Box("beta-dialog-buttons",card);
        Button("Cancelar","beta-outline",()=>{if(!Busy)HideConfirmation();},buttons);
        confirmButton=Button("Confirmar","beta-gold",()=>StartCoroutine(ApplyAction()),buttons);confirmButton.name="beta-access-confirm-action";
    }
    void BuildCredential()
    {
        credentialShade=Box("beta-dialog-shade",overlay);credentialShade.name="beta-access-credential";credentialShade.style.display=DisplayStyle.None;
        var card=Box("beta-dialog beta-credential-card",credentialShade);
        Text("ACESSO EMITIDO","beta-eyebrow",card);Text("Pronto para compartilhar","beta-dialog-title",card);
        Text("Copie o UID e a senha agora. A senha não aparece na lista de acessos.","beta-dialog-text",card);
        credentialUid=Text("","beta-credential-uid",card);credentialUid.name="beta-credential-uid";
        var secretRow=Box("beta-secret-row",card);
        secretPassword=new TextField {name="beta-credential-password",isPasswordField=true,isReadOnly=true};secretPassword.AddToClassList("beta-input");secretRow.Add(secretPassword);
        showSecret=Button("Mostrar","beta-outline",()=>{secretRevealed=!secretRevealed;secretPassword.isPasswordField=!secretRevealed;showSecret.text=secretRevealed?"Ocultar":"Mostrar";},secretRow);
        credentialStatus=Text("","beta-hint",card);
        var buttons=Box("beta-dialog-buttons",card);
        Button("Copiar acesso","beta-gold",()=>{
            if(!credential.HasValue)return;GUIUtility.systemCopyBuffer=credential.CopyText();credentialStatus.text="UID e senha copiados.";
        },buttons).name="beta-credential-copy";
        Button("Concluir","beta-outline",ClearCredential,buttons).name="beta-credential-done";
    }
    void Show()
    {
        ClearCredential();HideConfirmation();IsOpen=true;Busy=false;visit++;
        overlay.style.display=DisplayStyle.Flex;offset=0;search.SetValueWithoutNotify("");
        label.SetValueWithoutNotify(pendingCreateLabel??"");notice=pendingCreateId==null?null:"Há uma emissão pendente. Use Tentar novamente para confirmar o mesmo pedido.";SetBusy(false);Load();
    }
    public void Close()
    {
        visit++;listVisit++;IsOpen=false;Busy=false;debounce?.Pause();
        ClearCredential();HideConfirmation();notice=null;if(overlay!=null)overlay.style.display=DisplayStyle.None;
    }
    void Update()
    {
        if(IsOpen&&(!Allowed||!AdminPlayerPanel.Instance||!AdminPlayerPanel.Instance.IsOpen))Close();
    }
    void SetBusy(bool value)
    {
        Busy=value;issue?.SetEnabled(!value);refresh?.SetEnabled(!value);
        label?.SetEnabled(!value&&pendingCreateId==null);
        if(issue!=null)issue.text=value?"Processando…":pendingCreateId==null?"Emitir acesso":"Tentar novamente";
        confirmButton?.SetEnabled(!value);
    }
    void Load(){if(IsOpen&&Allowed)StartCoroutine(Fetch(visit,++listVisit));}
    IEnumerator Fetch(int current,int request)
    {
        status.text="Carregando acessos…";BetaAccessReply reply=null;
        yield return AccountSession.Instance.BetaAccessRequest("GET","/v1/admin/beta-access?q="+Uri.EscapeDataString(search.value.Trim())+"&offset="+offset,null,r=>reply=r);
        if(!IsOpen||visit!=current||request!=listVisit||!Allowed){if(reply!=null)reply.password=null;yield break;}
        if(reply==null||!string.IsNullOrEmpty(reply.code)){status.text=reply?.error??"Não foi possível carregar os acessos.";yield break;}
        reply.password=null;list.Clear();total=reply.total;
        foreach(var item in reply.accesses??Array.Empty<BetaAccessRecord>())Row(item);
        totalLabel.text=total+" acesso"+(total==1?"":"s");pageLabel.text="Página "+(offset/25+1);
        previous.SetEnabled(offset>0);next.SetEnabled(offset+25<total);
        status.text=notice??(list.childCount==0?"Nenhum acesso encontrado.":"Acesso revogado impede novos logins e encerra sessões ativas.");
    }
    void Row(BetaAccessRecord record)
    {
        var row=Box("beta-access-row",list);row.name="beta-access-row-"+record.uid;
        Text("UID "+record.uid,"beta-uid",row);
        var info=Box("beta-row-info",row);
        Text(string.IsNullOrWhiteSpace(record.label)?"Acesso da equipe":record.label,"beta-row-name",info);
        bool self=record.isSelf||record.uid==AccountSession.Instance.State.uid;
        bool manageable=CanManageEntry(record,AccountSession.Instance.State.uid);
        string detail=self?"Sua conta de gestão":record.hasCharacter?"Personagem criado":"Personagem pendente";
        if(DateTimeOffset.TryParse(record.createdAt,CultureInfo.InvariantCulture,DateTimeStyles.None,out var created))detail+=" · "+created.ToLocalTime().ToString("dd/MM/yyyy");
        Text(detail,"beta-row-detail",info);
        var badge=Text(record.active?"AUTORIZADO":"REVOGADO","beta-badge",row);badge.AddToClassList(record.active?"beta-active":"beta-revoked");
        var actions=Box("beta-row-actions",row);
        var reset=Button("Redefinir senha","beta-outline",()=>Confirm(record,"reset"),actions);
        var revoke=Button("Revogar","beta-danger",()=>Confirm(record,"revoke"),actions);
        reset.SetEnabled(manageable);revoke.SetEnabled(record.active&&manageable);
        if(!manageable){reset.tooltip="A conta de gestão e cargos iguais ou superiores são protegidos.";revoke.tooltip=reset.tooltip;}
    }
    IEnumerator Create()
    {
        if(Busy||!Allowed)yield break;
        if(pendingCreateId==null)
        {
            string value=(label.value??"").Normalize().Trim();
            if(value.Length<2){status.text="Informe uma identificação com pelo menos 2 caracteres.";label.Focus();yield break;}
            pendingCreateId=Guid.NewGuid().ToString();pendingCreateLabel=value;
        }
        int current=visit;notice=null;SetBusy(true);status.text="Emitindo acesso…";BetaAccessReply reply=null;
        var body=new BetaAccessCreateBody {requestId=pendingCreateId,label=pendingCreateLabel};
        yield return AccountSession.Instance.BetaAccessRequest("POST","/v1/admin/beta-access",JsonUtility.ToJson(body),r=>reply=r);
        if(!IsOpen||visit!=current||!Allowed){if(reply!=null)reply.password=null;yield break;}
        bool uncertain=reply==null||reply.code=="CONNECTION_LOST"||reply.code=="INTERNAL"||reply.code=="BETA_REQUEST_CONFLICT";
        if(!uncertain){pendingCreateId=null;pendingCreateLabel=null;}
        SetBusy(false);
        if(reply==null||!string.IsNullOrEmpty(reply.code))
        {
            notice=reply?.error??"Não foi possível confirmar a emissão. Tente novamente com o mesmo pedido.";status.text=notice;
            if(reply?.code=="CREDENTIAL_ALREADY_ISSUED")Load();
            if(reply!=null)reply.password=null;yield break;
        }
        label.SetValueWithoutNotify("");notice="Acesso emitido para UID "+reply.access?.uid+".";ShowCredential(reply);Load();
    }
    void Confirm(BetaAccessRecord record,string kind)
    {
        if(Busy||!Allowed||!CanManageEntry(record,AccountSession.Instance.State.uid))return;
        ClearCredential();actionUid=record.uid;actionKind=kind;actionId=Guid.NewGuid().ToString();confirmError.text="";
        confirmTitle.text=kind=="revoke"?"Revogar este acesso?":"Redefinir esta senha?";
        confirmDescription.text="UID "+record.uid+" · "+record.label+"\n"+(kind=="revoke"?
            "A pessoa não poderá entrar e suas sessões serão encerradas. O personagem será preservado.":
            "A senha anterior deixará de funcionar. Uma nova senha será exibida para você compartilhar.");
        confirmButton.text=kind=="revoke"?"Revogar acesso":"Gerar nova senha";
        confirmShade.style.display=DisplayStyle.Flex;
    }
    IEnumerator ApplyAction()
    {
        if(Busy||!Allowed||!BetaOneTimeCredential.ValidUid(actionUid))yield break;
        string kind=actionKind,uid=actionUid;int current=visit;SetBusy(true);confirmError.text="Processando…";BetaAccessReply reply=null;
        var body=new BetaAccessActionBody {requestId=actionId,reason=kind=="revoke"?"Acesso beta revogado pelo painel.":null};
        // Reset's exact body contains only requestId.
        string json=kind=="reset"?"{\"requestId\":\""+actionId+"\"}":JsonUtility.ToJson(body);
        yield return AccountSession.Instance.BetaAccessRequest("POST","/v1/admin/beta-access/"+uid+"/"+kind,json,r=>reply=r);
        if(!IsOpen||visit!=current||!Allowed){if(reply!=null)reply.password=null;yield break;}
        SetBusy(false);
        if(reply==null||!string.IsNullOrEmpty(reply.code))
        {
            confirmError.text=reply?.error??"Não foi possível confirmar. Tente novamente.";
            if(reply!=null)reply.password=null;
            if(reply?.code=="CREDENTIAL_ALREADY_ISSUED"){HideConfirmation();notice="A senha do UID "+uid+" já foi alterada. Escolha Redefinir senha para emitir outra credencial.";status.text=notice;Load();}
            yield break;
        }
        HideConfirmation();if(kind=="reset"){notice="Nova senha emitida para UID "+uid+".";ShowCredential(reply);}else {reply.password=null;notice="Acesso do UID "+uid+" revogado.";status.text=notice;}Load();
    }
    void HideConfirmation(){if(confirmShade!=null)confirmShade.style.display=DisplayStyle.None;actionUid=actionKind=actionId=null;if(confirmError!=null)confirmError.text="";}
    void ShowCredential(BetaAccessReply reply)
    {
        ClearCredential();
        if(!credential.Set(reply)){notice="O acesso foi emitido, mas a credencial está incompleta. Atualize a lista e redefina a senha.";status.text=notice;return;}
        credentialUid.text="UID "+credential.Uid;secretPassword.SetValueWithoutNotify(credential.Password);secretPassword.isPasswordField=true;
        secretRevealed=false;showSecret.text="Mostrar";credentialStatus.text="Exibida somente nesta emissão.";
        credentialShade.style.display=DisplayStyle.Flex;
    }
    void ClearCredential()
    {
        credential.Clear();secretRevealed=false;
        secretPassword?.SetValueWithoutNotify("");if(secretPassword!=null)secretPassword.isPasswordField=true;
        if(credentialUid!=null)credentialUid.text="";if(credentialStatus!=null)credentialStatus.text="";
        if(credentialShade!=null)credentialShade.style.display=DisplayStyle.None;
    }
    void OnDisable(){Close();}
    void OnDestroy(){ClearCredential();overlay?.RemoveFromHierarchy();if(Instance==this)Instance=null;}
}
