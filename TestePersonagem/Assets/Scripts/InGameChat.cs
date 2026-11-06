using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class InGameChat : MonoBehaviour
{
    public static InGameChat Instance {get;private set;}
    public static bool Editing=>Instance&&Instance.IsOpen;
    public bool IsOpen {get;private set;}
    public bool Sending {get;private set;}
    public int ReceivedCount {get;private set;}
    sealed class ChatLine {public string text,id,uid,kind,author,body;public bool isAdmin;}
    sealed class Outgoing {public string text,id;public ChatLine line;}
    readonly List<ChatLine> lines=new List<ChatLine>();
    readonly Queue<Outgoing> outgoing=new Queue<Outgoing>();
    internal Func<string,string,Action<MultiplayerReply>,IEnumerator> VerificationSend;
    bool VerifyingSend=>AccountSession.VerificationTransport!=null&&VerificationSend!=null;
    readonly List<string> messageTexts=new List<string>();
    VisualElement root,area,composer;
    ScrollView feed;
    TextField input;
    Label hint,composePrompt;
    Button send;
    TouchScreenKeyboard keyboard;
    string spectateUid;
    int spectateGeneration;
    Transform fixedHead;
    Quaternion fixedHeadRotation;
    bool headMotionStopped;


    public static void Create(GameHud hud)
    {
        if(Instance)return;
        var chat=hud.gameObject.AddComponent<InGameChat>();chat.Build(hud.Root);
    }
    void Awake(){Instance=this;}
    void Build(VisualElement parent)
    {
        root=parent;root.AddToClassList("chat-enabled");
        var styles=Resources.Load<StyleSheet>("ChatStyles");if(styles)root.styleSheets.Add(styles);
        area=new VisualElement {name="chat-area",focusable=true,tooltip="Toque para escrever · T ou Enter no computador"};area.AddToClassList("chat-area");root.Add(area);
        var header=new VisualElement {pickingMode=PickingMode.Ignore};header.AddToClassList("chat-header");area.Add(header);
        var title=new Label("CHAT DA CIDADE"){pickingMode=PickingMode.Ignore};title.AddToClassList("chat-title");header.Add(title);
        composePrompt=new Label(ComposePrompt()){pickingMode=PickingMode.Ignore};composePrompt.AddToClassList("chat-prompt");header.Add(composePrompt);
        feed=new ScrollView(ScrollViewMode.Vertical) {name="chat-feed"};feed.AddToClassList("chat-feed");
        feed.horizontalScrollerVisibility=ScrollerVisibility.Hidden;feed.verticalScrollerVisibility=ScrollerVisibility.Auto;area.Add(feed);
        composer=new VisualElement {name="chat-composer"};composer.AddToClassList("chat-composer");area.Add(composer);
        var row=new VisualElement();row.AddToClassList("chat-input-row");composer.Add(row);
        input=new TextField {name="chat-input",maxLength=180,multiline=false,isReadOnly=Application.isMobilePlatform};
        input.AddToClassList("chat-input");input.textEdition.placeholder="";
        input.textEdition.hidePlaceholderOnFocus=true;row.Add(input);
        send=new Button(Send){name="chat-send",text="›",tooltip="Enviar mensagem"};send.AddToClassList("chat-send");row.Add(send);
        hint=new Label(""){name="chat-hint",enableRichText=false};hint.AddToClassList("chat-hint");composer.Add(hint);
        area.RegisterCallback<ClickEvent>(evt=>
        {
            var target=evt.target as VisualElement;
            if(!IsOpen&&(target==null||!composer.Contains(target)))Open();
        });
        area.RegisterCallback<NavigationSubmitEvent>(evt=>{if(evt.target==area){Open();evt.StopPropagation();}});
        input.RegisterCallback<PointerDownEvent>(_=>{if(Application.isMobilePlatform&&IsOpen)OpenKeyboard();});
        composer.RegisterCallback<KeyDownEvent>(evt=>
        {
            if(evt.keyCode==KeyCode.Return||evt.keyCode==KeyCode.KeypadEnter){Send();evt.StopPropagation();}
            else if(evt.keyCode==KeyCode.Escape){Close();evt.StopPropagation();}
        },TrickleDown.TrickleDown);
        root.RegisterCallback<GeometryChangedEvent>(_=>Layout());
        root.schedule.Execute(Layout);
        SystemLine("Bem-vindo ao Horizonte! Toque no chat para escrever.");Close();
    }
    string ComposePrompt()=>IsOpen?(Application.isMobilePlatform?"ESCREVA SUA MENSAGEM":"ENTER PARA ENVIAR"):
        (Application.isMobilePlatform?"TOQUE PARA ESCREVER":"T OU ENTER PARA ESCREVER");
    void Layout()
    {
        float panelWidth=root.resolvedStyle.width;
        if(float.IsNaN(panelWidth)||panelWidth<=0)return;
        root.EnableInClassList("chat-narrow",panelWidth<1000);
        float desired=Mathf.Lerp(360,480,Mathf.InverseLerp(960,1280,panelWidth));
        float width=Mathf.Min(desired,Mathf.Max(160,panelWidth-440));
        area.style.width=width;
        area.style.left=(panelWidth-width)*.5f;
        area.style.right=StyleKeyword.Auto;
    }
    public void Open()
    {
        if(StartupLoader.IsLoading||AccountSession.InputBlocked)return;
        if(GameHud.Instance.IsPanelOpen)GameHud.Instance.ClosePanel();
        IsOpen=true;composer.style.display=DisplayStyle.Flex;
        composePrompt.text=ComposePrompt();
        root.AddToClassList("chat-editing");Render();SetKeyboardInset(0);
        ShowHint("");
        if(Application.isMobilePlatform)OpenKeyboard();
        else
        {
            input.Focus();
            // The composer has just become visible; focus its editable element after layout.
            input.schedule.Execute(()=>
            {
                if(!IsOpen)return;
                var editor=input.Q<VisualElement>(className:"unity-text-field__input");
                if(editor!=null)editor.Focus();else input.Focus();
            }).ExecuteLater(1);
        }
    }
    void OpenKeyboard()
    {
        if(!Application.isMobilePlatform||(keyboard!=null&&keyboard.active))return;
        keyboard=TouchScreenKeyboard.Open(input.value,TouchScreenKeyboardType.Default,true,false,false,false,"Sua mensagem",180);
    }
    void CloseKeyboard(){if(keyboard!=null){keyboard.active=false;keyboard=null;}}
    public void Close()
    {
        IsOpen=false;CloseKeyboard();
        if(composer==null)return;
        input.Blur();composer.style.display=DisplayStyle.None;
        composePrompt.text=ComposePrompt();
        root.RemoveFromClassList("chat-editing");root.Focus();Render();
    }
    public void Send()
    {
        string text=(keyboard!=null?keyboard.text:input.value).Replace('\r',' ').Replace('\n',' ').Trim();
        if(string.IsNullOrEmpty(text)){input.Focus();return;}
        string localCommand=text.Split(' ')[0].ToLowerInvariant();
        string skinCommand=text.Trim().ToLowerInvariant();
        if (skinCommand=="/wuzimu" || skinCommand=="/skin wuzimu" || skinCommand=="/skin sa" || skinCommand=="/skin padrao")
        {
            var target=GameHud.Instance?GameHud.Instance.Player:null;
            var visual=target?target.GetComponent<GangsterPlayerVisual>():null;
            bool restored=skinCommand=="/skin padrao";
            bool changed=visual&&visual.SwitchPreviewModel(restored?"padrao":"sa");
            input.value="";Close();
            SystemLine(changed?(restored?"Personagem padrão restaurado.":"Personagem SA em teste local: parado e caminhada. Use /skin padrao para voltar."):"Não foi possível trocar agora. Guarde a arma, fique a pé e aguarde terminar a ação atual.");
            return;
        }
        if(localCommand=="/arma"||localCommand=="/rifle"||localCommand=="/pistola"||localCommand=="/desarmar")
        {
            input.value="";Close();
            var player=GameHud.Instance?GameHud.Instance.Player:null;
            if(player)
            {
                if(AdminPlayerPanel.PoliceDuty&&localCommand!="/desarmar")
                {SystemLine("Use /arsenal na delegacia para retirar uma arma da PM.");return;}
                var weapon=RifleWeapon.Create(player);
                bool equipped=localCommand!="/desarmar"&&!weapon.Equipped;
                bool changed=localCommand=="/pistola"?weapon.SetWeaponType(true):localCommand=="/rifle"||localCommand=="/arma"&&!weapon.Equipped?weapon.SetWeaponType(false):weapon.SetEquipped(equipped);
                if(changed)SystemLine(weapon.Equipped?
                    (weapon.IsPistol?"Pistola equipada.":"Rifle equipado.")+" Mouse direito: mirar · esquerdo/F: atirar · R: recarregar · /arma: guardar.":"Arma guardada.");
            }
            input.value="";Close();return;
        }
        QueueMessage(text);
    }
    internal void SendCommand(string command)
    {
        if(AccountSession.HasCharacter)QueueMessage(command);
    }
    void QueueMessage(string text)
    {
        if(!VerifyingSend&&(!MultiplayerWorld.Instance||!MultiplayerWorld.Instance.Connected))
        {if(Editing)ShowHint("Aguarde a conexão com a sala.");else GameHud.Instance?.ShowToast("Aguarde a conexão com a sala.");return;}
        if(outgoing.Count>=20){ShowHint("Muitas mensagens na fila. Aguarde um instante.");return;}
        string id=Guid.NewGuid().ToString();bool command=text.StartsWith("/");
        bool admin=AccountSession.Instance.State.accountRole=="admin"&&AccountSession.Instance.State.adminLevel>0;
        var line=new ChatLine {id=id,uid=AccountSession.Instance.State.uid,
            text=command?text:PlayerDisplayName.From(AccountSession.Instance.State)+": "+text,kind=command&&admin?"admin":"normal",
            isAdmin=admin,author=command?null:PlayerDisplayName.From(AccountSession.Instance.State),body=command?null:text};
        lines.Add(line);TrimLines();
        outgoing.Enqueue(new Outgoing {text=text,id=id,line=line});
        input.value="";Close(); // Local feedback never waits for the network.
        if(!Sending){Sending=true;StartCoroutine(FlushOutgoing());}
    }
    IEnumerator FlushOutgoing()
    {
        while(outgoing.Count>0)
        {
            var item=outgoing.Peek();MultiplayerReply reply=null;
            float started=Time.realtimeSinceStartup;
            for(int attempt=0;attempt<3;attempt++)
            {
                if(VerifyingSend)yield return VerificationSend(item.id,item.text,r=>reply=r);
                else if(MultiplayerWorld.Instance)yield return MultiplayerWorld.Instance.SendChat(item.id,item.text,r=>reply=r);
                else reply=new MultiplayerReply {code="CONNECTION_LOST"};
                if(reply?.code!="CHAT_RATE_LIMITED"&&reply?.code!="CONNECTION_LOST"&&reply!=null)break;
                if(attempt<2)yield return new WaitForSecondsRealtime(1.3f);
            }
            if(string.IsNullOrEmpty(reply?.code)&&reply?.command!=null&&
                !string.IsNullOrEmpty(reply.command.name))
            {
                bool policeCommand=IsPoliceCommand(reply.command.name);
                item.line.text=(IsPlayerCommand(reply.command.name)?"[JOGADOR] ":reply.command.name=="190"?"[190] ":policeCommand?"[POLÍCIA] ":"[ADMIN] ")+reply.command.message;
                item.line.kind=IsPlayerCommand(reply.command.name)?"system":"admin";Render();
                HandleCommand(reply.command);
            }
            else if(string.IsNullOrEmpty(reply?.code)&&reply?.message!=null&&
                !string.IsNullOrEmpty(reply.message.messageId))Receive(reply.message);
            else
            {
                // A sync may already have confirmed a request whose HTTP response was lost.
                if(item.line.kind!="confirmed")
                {
                    string failure=" — não enviado: "+(reply?.error??"resposta inválida do servidor. Tente novamente.");
                    item.line.text+=failure;if(item.line.body!=null)item.line.body+=failure;
                    item.line.kind="error";Render();
                }
            }
            outgoing.Dequeue();
            float remaining=1.3f-(Time.realtimeSinceStartup-started);
            if(remaining>0)yield return new WaitForSecondsRealtime(remaining);
        }
        Sending=false;
    }
    void ShowHint(string value){hint.text=value;hint.style.display=string.IsNullOrEmpty(value)?DisplayStyle.None:DisplayStyle.Flex;}
    void HandleCommand(AdminCommandReply command)
    {
        var player=GameHud.Instance?GameHud.Instance.Player:null;
        if(command.status=="open_help"&&GameHud.Instance)
            AdminHelpPanel.Open(GameHud.Instance,command.help);
        else if(command.name=="wanted"&&command.status=="ok"&&GameHud.Instance)
            WantedListPanel.Open(GameHud.Instance,command.wanted);
        else if(command.status=="police_dispatch")
            GameHud.Instance?.ShowPoliceDispatch(command.policeCalls);
        else if(command.status=="police_fines")
            GameHud.Instance?.ShowPoliceFines(command.policeFines);
        else if(command.status=="police_map")
            GameHud.Instance?.ShowPoliceWaypoint(new Vector3(command.x,command.y,command.z),command.name=="delegacia"?"DELEGACIA":"OCORRÊNCIA 190");
        else if(command.status=="police_weapon"&&player)
        {
            var weapon=RifleWeapon.Create(player);
            if(command.weapon=="guardar")weapon.SetEquipped(false);
            else weapon.SetWeaponType(command.weapon=="pistola");
        }
        else if(command.lines!=null)foreach(string line in command.lines){if(IsPlayerCommand(command.name))SystemLine(line);else AdminLine(line,IsPoliceCommand(command.name));}
        if((command.name=="proc"||command.name=="encerrar190")&&GameHud.Instance?.CurrentPanel=="police-dispatch")
            SendCommand("/chamados");
        if(command.name=="pagarmulta"&&GameHud.Instance?.CurrentPanel=="police-fines")
            SendCommand("/minhasmultas");
        if(player&&long.TryParse(command.cashCents,out long cash))
        {
            player.Cash=cash/100;
            if(AccountSession.HasCharacter)AccountSession.Instance.State.cashCents=command.cashCents;
        }
        if(command.status=="player_panel"&&GameHud.Instance)
        {
            string panel=command.name=="gps"?"map":command.name=="quests"?"missions":command.name=="stats"?"player":command.name=="phone"?"phone":"animations";
            GameHud.Instance.OpenPanel(panel);
        }
        else if(command.status=="headmove"&&player)
        {
            if(headMotionStopped){headMotionStopped=false;SystemLine("Movimentos da cabeça ativados.");}
            else
            {
                var animator=player.GetComponentInChildren<Animator>();
                fixedHead=animator&&animator.isHuman?animator.GetBoneTransform(HumanBodyBones.Head):null;
                if(fixedHead){fixedHeadRotation=fixedHead.localRotation;headMotionStopped=true;SystemLine("Movimentos da cabeça pausados. Use /headmove para retomar.");}
                else SystemLine("Este modelo não possui cabeça Humanoid configurada.");
            }
        }
        if(command.status=="teleport"&&player)
            player.TeleportTo(new Vector3(command.x,command.y,command.z));
        else if(command.status=="gender"&&command.name=="trocarsexo"&&
            AccountSession.Instance&&AccountSession.Instance.ApplyConfirmedGender(command.gender))
            player?.GetComponent<GangsterPlayerVisual>()?.SwitchGender(command.gender);
        else if(command.status=="speed"&&player)
            player.SetAdminSpeedMultiplier(command.value);
        else if(command.status=="open_panel"&&GameHud.Instance)
            AdminPlayerPanel.Open(GameHud.Instance);
        else if(command.status=="spectate"&&player)
            StartCoroutine(BeginSpectate(command.uid,++spectateGeneration));
        else if(command.status=="spectate_off")StopSpectating();
    }
    IEnumerator BeginSpectate(string uid,int generation)
    {
        float deadline=Time.realtimeSinceStartup+5f;
        while(generation==spectateGeneration&&Time.realtimeSinceStartup<deadline)
        {
            if(MultiplayerWorld.Instance&&MultiplayerWorld.Instance.TryGetRemoteByUid(uid,out var target))
            {
                var camera=FindAnyObjectByType<ThirdPersonCamera>();
                if(camera&&GameHud.Instance&&GameHud.Instance.Player)
                {
                    spectateUid=uid;camera.target=target;GameHud.Instance.Player.SetSpectating(true);
                    yield break;
                }
            }
            yield return null;
        }
        if(generation==spectateGeneration)
            AdminLine("Não foi possível localizar o jogador para observar.");
    }
    void StopSpectating()
    {
        spectateGeneration++;
        if(string.IsNullOrEmpty(spectateUid))return;
        spectateUid=null;
        var player=GameHud.Instance?GameHud.Instance.Player:null;
        if(player)player.SetSpectating(false);
        var camera=FindAnyObjectByType<ThirdPersonCamera>();
        if(camera&&player){camera.target=player.transform;camera.ResetView();}
    }
    static bool IsPlayerCommand(string name)=>name=="s"||name=="w"||name=="me"||name=="do"||name=="try"||name=="j"||name=="stats"||name=="myreports"||name=="report"||name=="gps"||name=="phone"||name=="mask"||name=="healme"||name=="house"||name=="biz"||name=="daily"||name=="quests"||name=="v"||name=="headmove"||name=="anim"||name=="ajuda"||name=="minhasmultas"||name=="pagarmulta"||name=="cancelar190";
    void LateUpdate()
    {
        if(!headMotionStopped||!fixedHead)return;
        var combat=fixedHead.GetComponentInParent<CombatAvatarAnimation>();
        if(!combat||!combat.Busy)fixedHead.localRotation=fixedHeadRotation;
    }
    static bool IsPoliceCommand(string name)=>name=="ide"||name=="wanted"||name=="su"||name=="clear"||name=="cuff"||name=="uncuff"||name=="follow"||name=="stay"||name=="put"||name=="eject"||name=="carpolicia"||name=="ticket"||name=="proc"||name=="megaphone"||name=="faction"||name=="departments"||name=="government"||
        name=="servico"||name=="pmrecrutar"||name=="pmpatente"||name=="pmdemitir"||name=="delegacia"||name=="prender"||
        name=="ocorrencias"||name=="chamados"||name=="despachar190"||name=="gpsocorrencia"||name=="encerrar190"||name=="cancelarmulta"||name=="revistar"||
        name=="documentos"||name=="placa"||name=="apreender"||name=="arsenal"||name=="giroflex"||name=="sirene";
    void AdminLine(string text,bool police=false)
    {lines.Add(new ChatLine{text=(police?"[POLÍCIA] ":"[ADMIN] ")+text,kind="admin"});TrimLines();Render();}
    public void Receive(ChatMessage message)
    {
        if(message==null||message.id<=0||string.IsNullOrEmpty(message.text))return;
        if(message.channel=="system"){SystemLine(message.text);return;}
        var existing=string.IsNullOrEmpty(message.messageId)?null:lines.Find(line=>line.id==message.messageId&&line.uid==message.uid);
        if(existing!=null&&existing.kind=="confirmed")return;
        string display=PlayerDisplayName.From(message.firstName,message.name);
        string body=message.channel=="shout"?"[GRITO] "+message.text:
            message.channel=="whisper"?"[SUSSURRO] "+message.text:
            message.channel=="admin"?"[A] "+message.text:
            message.channel=="announcement"?"[AVISO] "+message.text:
            message.channel=="police"?"[190/POLÍCIA] "+message.text:
            message.channel=="faction"?"[ORGANIZAÇÃO] "+message.text:
            message.channel=="departments"?"[DEPARTAMENTOS] "+message.text:
            message.channel=="government"?"[GOVERNO] "+message.text:
            message.channel=="megaphone"?"[MEGAFONE] "+message.text:message.text;
        ReceivedCount++;messageTexts.Add(message.text);if(messageTexts.Count>60)messageTexts.RemoveAt(0);
        if(existing!=null)
        {existing.text=display+": "+body;existing.author=display;existing.body=body;existing.isAdmin=message.isAdmin;existing.kind="confirmed";Render();}
        else {lines.Add(new ChatLine {text=display+": "+body,author=display,body=body,isAdmin=message.isAdmin,id=message.messageId,uid=message.uid,kind="confirmed"});TrimLines();Render();}
    }
    public bool HasMessage(string text)=>messageTexts.Contains(text);
    public void ResetHistory(){lines.RemoveAll(line=>!outgoingContains(line));messageTexts.Clear();ReceivedCount=0;SystemLine("Chat da sala · mensagens recentes");}
    bool outgoingContains(ChatLine line){foreach(var item in outgoing)if(item.line==line)return true;return false;}
    public void SystemLine(string text)=>AddLine("[Horizonte] "+text);
    void AddLine(string text){lines.Add(new ChatLine {text=text,kind="system"});TrimLines();Render();}
    void TrimLines(){while(lines.Count>60)lines.RemoveAt(0);}
    void Render()
    {
        if(feed==null)return;feed.Clear();feed.pickingMode=IsOpen?PickingMode.Position:PickingMode.Ignore;
        int start=IsOpen?0:Mathf.Max(0,lines.Count-6);
        VisualElement last=null;
        for(int i=start;i<lines.Count;i++)
        {
            var line=lines[i];
            if(line.isAdmin&&!string.IsNullOrEmpty(line.author))
            {
                var row=new VisualElement{pickingMode=PickingMode.Ignore};row.AddToClassList("chat-message");
                var author=new Label("[ADMIN] "+line.author+":"){enableRichText=false,pickingMode=PickingMode.Ignore};
                author.AddToClassList("chat-line");author.AddToClassList("chat-author-admin");ApplyChatOutline(author);row.Add(author);
                var body=new Label(line.body){enableRichText=false,pickingMode=PickingMode.Ignore};
                body.AddToClassList("chat-line");body.AddToClassList("chat-message-body");body.AddToClassList("chat-admin-message-body");if(line.kind=="error")body.AddToClassList("chat-error");ApplyChatOutline(body);row.Add(body);
                last=row;
            }
            else
            {
                var label=new Label(line.text){enableRichText=false,pickingMode=PickingMode.Ignore};label.AddToClassList("chat-line");ApplyChatOutline(label);
                if(line.kind=="system")label.AddToClassList("chat-system");
                if(line.kind=="admin")label.AddToClassList("chat-admin");
                if(line.kind=="error")label.AddToClassList("chat-error");
                last=label;
            }
            feed.Add(last);
        }
        if(last!=null){var target=last;feed.schedule.Execute(()=>{if(feed.Contains(target))feed.ScrollTo(target);});}
    }
    static void ApplyChatOutline(Label label)
    {
        // Apply this inline as well as in ChatStyles.uss. UI Toolkit can retain an
        // older computed text material while entering through the loading scene;
        // the inline values make the border deterministic on every rebuilt line.
        label.style.unityFont=HudPack.LabelFont;
        label.style.unityFontStyleAndWeight=FontStyle.Bold;
        label.style.unityTextOutlineColor=Color.black;
        label.style.unityTextOutlineWidth=1.25f;
        label.style.textShadow=new TextShadow
        {
            offset=Vector2.zero,
            blurRadius=0f,
            color=new Color(0f,0f,0f,0f)
        };
    }
    public static bool BlocksPointer(Vector2 screen)
    {
        if(!Instance||Instance.root?.panel==null)return false;
        if(Editing)return true;
        Vector2 point=RuntimePanelUtils.ScreenToPanel(Instance.root.panel,new Vector2(screen.x,Screen.height-screen.y));
        return Instance.area.worldBound.Contains(point);
    }
    internal void SetKeyboardInset(float pixels)
    {
        float scale=Screen.height/Mathf.Max(1,root.resolvedStyle.height);
        float inset=Mathf.Max(0,pixels/scale);
        float height=composer.resolvedStyle.height;
        if(float.IsNaN(height)||height<1)height=120;
        feed.style.maxHeight=IsOpen?Mathf.Min(225,Mathf.Max(40,root.resolvedStyle.height-inset-height-76)):133;
    }
    void Update()
    {
        if(root==null)return;
        if(!string.IsNullOrEmpty(spectateUid)&&(!MultiplayerWorld.Instance||
            !MultiplayerWorld.Instance.TryGetRemoteByUid(spectateUid,out _)))StopSpectating();
        if(StartupLoader.IsLoading||AccountSession.InputBlocked){if(IsOpen)Close();return;}
        if(!IsOpen&&!GameHud.Instance.IsPanelOpen&&(Input.GetKeyDown(KeyCode.T)||Input.GetKeyDown(KeyCode.Return)))Open();
        else if(IsOpen&&Input.GetKeyDown(KeyCode.Escape))Close();
        if(keyboard!=null)
        {
            input.value=keyboard.text;
            if(keyboard.status==TouchScreenKeyboard.Status.Done)
            {string text=keyboard.text;CloseKeyboard();input.value=text;if(string.IsNullOrWhiteSpace(text))Close();else Send();}
            else if(keyboard.status==TouchScreenKeyboard.Status.Canceled||keyboard.status==TouchScreenKeyboard.Status.LostFocus)Close();
        }
        if(IsOpen)SetKeyboardInset(TouchScreenKeyboard.visible?
            (TouchScreenKeyboard.area.height>0?TouchScreenKeyboard.area.height:Screen.height*.42f):0);
    }
    void OnDestroy(){StopSpectating();CloseKeyboard();if(Instance==this)Instance=null;}
}
