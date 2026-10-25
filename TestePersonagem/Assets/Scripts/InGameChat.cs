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
    Label hint;
    Button send;
    TouchScreenKeyboard keyboard;


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
        feed=new ScrollView(ScrollViewMode.Vertical) {name="chat-feed"};feed.AddToClassList("chat-feed");
        feed.horizontalScrollerVisibility=ScrollerVisibility.Hidden;feed.verticalScrollerVisibility=ScrollerVisibility.Auto;area.Add(feed);
        composer=new VisualElement {name="chat-composer"};composer.AddToClassList("chat-composer");area.Add(composer);
        var row=new VisualElement();row.AddToClassList("chat-input-row");composer.Add(row);
        input=new TextField {name="chat-input",maxLength=180,multiline=false,isReadOnly=Application.isMobilePlatform};
        input.AddToClassList("chat-input");input.textEdition.placeholder="Escreva sua mensagem...";
        input.textEdition.hidePlaceholderOnFocus=false;row.Add(input);
        send=new Button(Send){name="chat-send",text="›",tooltip="Enviar mensagem"};send.AddToClassList("chat-send");row.Add(send);
        var close=new Button(Close){name="chat-close",text="×",tooltip="Fechar chat"};close.AddToClassList("chat-close");row.Add(close);
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
        root.RegisterCallback<GeometryChangedEvent>(_=>
        {
            root.EnableInClassList("chat-narrow",root.resolvedStyle.width<1000);
            bool clean=root.ClassListContains("hub2-hud");
            area.style.width=Mathf.Clamp(root.resolvedStyle.width-(clean?700:798),220,430);
        });
        SystemLine("Bem-vindo ao Horizonte! Toque no chat para escrever.");Close();
    }
    public void Open()
    {
        if(StartupLoader.IsLoading||AccountSession.InputBlocked)return;
        if(GameHud.Instance.IsPanelOpen)GameHud.Instance.ClosePanel();
        IsOpen=true;composer.style.display=DisplayStyle.Flex;
        root.AddToClassList("chat-editing");Render();SetKeyboardInset(0);
        ShowHint("");
        if(Application.isMobilePlatform)OpenKeyboard();else input.Focus();
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
        root.RemoveFromClassList("chat-editing");root.Focus();Render();
    }
    public void Send()
    {
        string text=(keyboard!=null?keyboard.text:input.value).Replace('\r',' ').Replace('\n',' ').Trim();
        if(string.IsNullOrEmpty(text)){ShowHint("Escreva uma mensagem antes de enviar.");return;}
        if(!VerifyingSend&&(!MultiplayerWorld.Instance||!MultiplayerWorld.Instance.Connected)){ShowHint("Aguarde a conexão com a sala.");return;}
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
            if(string.IsNullOrEmpty(reply?.code)&&reply?.message!=null)Receive(reply.message);
            else if(string.IsNullOrEmpty(reply?.code)&&reply?.command!=null)
            {item.line.text="[ADMIN] "+reply.command.message;item.line.kind="admin";Render();}
            else
            {
                // A sync may already have confirmed a request whose HTTP response was lost.
                if(item.line.kind!="confirmed")
                {
                    string failure=" — não enviado: "+(reply?.error??"falha na conexão. Envie novamente.");
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
    public void Receive(ChatMessage message)
    {
        if(message==null)return;
        var existing=string.IsNullOrEmpty(message.messageId)?null:lines.Find(line=>line.id==message.messageId&&line.uid==message.uid);
        if(existing!=null&&existing.kind=="confirmed")return;
        string display=PlayerDisplayName.From(message.firstName,message.name);
        ReceivedCount++;messageTexts.Add(message.text);if(messageTexts.Count>60)messageTexts.RemoveAt(0);
        if(existing!=null)
        {existing.text=display+": "+message.text;existing.author=display;existing.body=message.text;existing.isAdmin=message.isAdmin;existing.kind="confirmed";Render();}
        else {lines.Add(new ChatLine {text=display+": "+message.text,author=display,body=message.text,isAdmin=message.isAdmin,id=message.messageId,uid=message.uid,kind="confirmed"});TrimLines();Render();}
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
                author.AddToClassList("chat-line");author.AddToClassList("chat-author-admin");row.Add(author);
                var body=new Label(line.body){enableRichText=false,pickingMode=PickingMode.Ignore};
                body.AddToClassList("chat-line");body.AddToClassList("chat-message-body");if(line.kind=="error")body.AddToClassList("chat-error");row.Add(body);
                last=row;
            }
            else
            {
                var label=new Label(line.text){enableRichText=false,pickingMode=PickingMode.Ignore};label.AddToClassList("chat-line");
                if(line.kind=="system")label.AddToClassList("chat-system");
                if(line.kind=="admin")label.AddToClassList("chat-admin");
                if(line.kind=="error")label.AddToClassList("chat-error");
                last=label;
            }
            feed.Add(last);
        }
        if(last!=null){var target=last;feed.schedule.Execute(()=>{if(feed.Contains(target))feed.ScrollTo(target);});}
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
        feed.style.maxHeight=IsOpen?Mathf.Min(225,Mathf.Max(40,root.resolvedStyle.height-inset-height-38)):133;
    }
    void Update()
    {
        if(root==null)return;
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
    void OnDestroy(){CloseKeyboard();if(Instance==this)Instance=null;}
}
