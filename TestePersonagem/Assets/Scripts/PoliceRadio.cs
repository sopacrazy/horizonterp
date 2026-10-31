using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

// Radio is available only while the server confirms administrative police duty.
public sealed class PoliceRadio : MonoBehaviour
{
    public static PoliceRadio Instance {get;private set;}
    public int Channel {get;private set;}
    public bool Connected=>Channel>0&&AdminPlayerPanel.PoliceDuty&&MultiplayerWorld.Instance&&MultiplayerWorld.Instance.Connected;
    public bool Pressed {get;private set;}
    public float Volume {get;private set;}=.8f;
    public KeyCode pushKey=KeyCode.V;
    GameHud hud;
    VisualElement panel;
    Button openButton,talkButton,connectButton,disconnectButton;
    Label stateLabel,channelLabel,speakerLabel;
    int selected=1;
    bool busy,wasPolice;
    float speakerUntil;

    public static void Create(GameHud owner)
    {
        if(owner&&!Instance)owner.gameObject.AddComponent<PoliceRadio>().Build(owner);
    }
    void Awake(){Instance=this;}
    static Button Button(string text,Action action)
    {
        var b=new Button(action){text=text};
        b.style.height=38;b.style.marginBottom=5;b.style.backgroundColor=new Color(.1f,.16f,.23f,.96f);
        b.style.color=Color.white;b.style.borderTopLeftRadius=7;b.style.borderTopRightRadius=7;
        b.style.borderBottomLeftRadius=7;b.style.borderBottomRightRadius=7;
        return b;
    }
    static Label Label(string text,int size=13)
    {
        var l=new Label(text);l.style.fontSize=size;l.style.color=Color.white;l.style.marginBottom=7;
        l.pickingMode=PickingMode.Ignore;return l;
    }
    void Build(GameHud owner)
    {
        hud=owner;
        openButton=Button("RÁDIO",()=>{panel.style.display=panel.style.display==DisplayStyle.Flex?DisplayStyle.None:DisplayStyle.Flex;});
        openButton.style.position=Position.Absolute;openButton.style.right=90;openButton.style.top=205;
        openButton.style.width=72;openButton.style.backgroundColor=new Color(.12f,.3f,.43f,.92f);
        owner.Root.Add(openButton);
        owner.RegisterPointerBlocker(openButton);
        talkButton=Button("",null);
        talkButton.tooltip="Segure para falar no rádio";
        talkButton.style.position=Position.Absolute;talkButton.style.right=153;talkButton.style.bottom=126;
        talkButton.style.width=70;talkButton.style.height=70;talkButton.style.marginBottom=0;
        talkButton.style.flexDirection=FlexDirection.Column;
        talkButton.style.alignItems=Align.Center;talkButton.style.justifyContent=Justify.Center;
        talkButton.style.borderTopLeftRadius=35;talkButton.style.borderTopRightRadius=35;
        talkButton.style.borderBottomLeftRadius=35;talkButton.style.borderBottomRightRadius=35;
        talkButton.style.borderTopWidth=2;talkButton.style.borderBottomWidth=2;
        talkButton.style.borderLeftWidth=2;talkButton.style.borderRightWidth=2;
        talkButton.style.borderTopColor=new Color(.33f,.72f,.91f);
        talkButton.style.borderBottomColor=new Color(.33f,.72f,.91f);
        talkButton.style.borderLeftColor=new Color(.33f,.72f,.91f);
        talkButton.style.borderRightColor=new Color(.33f,.72f,.91f);
        talkButton.style.backgroundColor=new Color(.1f,.25f,.36f,.95f);
        var microphone=HudAtlas.Image("h2_microphone");
        microphone.scaleMode=ScaleMode.ScaleToFit;
        microphone.style.width=42;microphone.style.height=42;
        talkButton.Add(microphone);
        talkButton.RegisterCallback<PointerDownEvent>(e=>{if(!Connected||busy)return;talkButton.CapturePointer(e.pointerId);BeginTalk();e.StopPropagation();});
        talkButton.RegisterCallback<PointerUpEvent>(e=>{EndTalk();if(talkButton.HasPointerCapture(e.pointerId))talkButton.ReleasePointer(e.pointerId);e.StopPropagation();});
        talkButton.RegisterCallback<PointerCaptureOutEvent>(_=>EndTalk());
        owner.Root.Add(talkButton);
        owner.RegisterPointerBlocker(talkButton);
        panel=new VisualElement();panel.style.position=Position.Absolute;panel.style.right=102;panel.style.top=252;
        panel.style.width=270;panel.style.paddingLeft=14;panel.style.paddingRight=14;
        panel.style.paddingTop=13;panel.style.paddingBottom=13;
        panel.style.backgroundColor=new Color(.035f,.055f,.085f,.96f);
        panel.style.borderTopLeftRadius=11;panel.style.borderTopRightRadius=11;
        panel.style.borderBottomLeftRadius=11;panel.style.borderBottomRightRadius=11;
        panel.style.borderTopWidth=1;panel.style.borderBottomWidth=1;
        panel.style.borderLeftWidth=1;panel.style.borderRightWidth=1;
        panel.style.borderTopColor=new Color(.25f,.5f,.65f);panel.style.borderBottomColor=new Color(.25f,.5f,.65f);
        panel.style.borderLeftColor=new Color(.25f,.5f,.65f);panel.style.borderRightColor=new Color(.25f,.5f,.65f);
        panel.Add(Label("RÁDIO DA POLÍCIA",17));
        stateLabel=Label("Desconectado");panel.Add(stateLabel);
        channelLabel=Label("Selecione um canal");panel.Add(channelLabel);
        foreach(var option in new[]{(1,"1 · Central"),(2,"2 · Patrulha"),(3,"3 · Operações")})
        {
            int choice=option.Item1;
            var button=Button(option.Item2,()=>{selected=choice;Refresh();});panel.Add(button);
        }
        connectButton=Button("CONECTAR / TROCAR CANAL",()=>StartCoroutine(SetChannel(selected)));panel.Add(connectButton);
        disconnectButton=Button("DESCONECTAR",()=>StartCoroutine(SetChannel(0)));panel.Add(disconnectButton);
        var slider=new Slider("Volume",0,1){value=Volume};slider.style.color=Color.white;
        slider.RegisterValueChangedCallback(e=>Volume=e.newValue);panel.Add(slider);
        panel.Add(Button("FECHAR",()=>panel.style.display=DisplayStyle.None));
        speakerLabel=Label("");speakerLabel.style.position=Position.Absolute;
        speakerLabel.style.right=102;speakerLabel.style.top=177;owner.Root.Add(speakerLabel);
        owner.Root.Add(panel);owner.RegisterPointerBlocker(panel);panel.style.display=DisplayStyle.None;Refresh();
    }
    void Update()
    {
        bool police=AccountSession.HasCharacter&&AdminPlayerPanel.PoliceDuty;
        if(police&&!wasPolice)StartCoroutine(ReadChannel());
        if(!police&&wasPolice){EndTalk();Channel=0;panel.style.display=DisplayStyle.None;}
        wasPolice=police;
        if(Pressed&&(!Connected||!Input.GetKey(pushKey)&&keyPressed))EndTalk();
        if(Connected&&Input.GetKeyDown(pushKey)){keyPressed=true;BeginTalk();}
        if(keyPressed&&Input.GetKeyUp(pushKey)){keyPressed=false;EndTalk();}
        if(!Connected&&Pressed)EndTalk();
        if(speakerLabel!=null&&Time.unscaledTime>speakerUntil)speakerLabel.text="";
        Refresh();
    }
    bool keyPressed;
    void Refresh()
    {
        if(openButton==null)return;
        bool police=AccountSession.HasCharacter&&AdminPlayerPanel.PoliceDuty;
        openButton.style.display=police?DisplayStyle.Flex:DisplayStyle.None;
        openButton.text=Channel>0?"RÁDIO "+Channel:"RÁDIO";
        talkButton.style.display=Connected?DisplayStyle.Flex:DisplayStyle.None;
        talkButton.style.backgroundColor=Pressed?new Color(.05f,.78f,.52f,.98f):new Color(.1f,.25f,.36f,.95f);
        var ringColor=Pressed?new Color(.43f,1f,.69f):new Color(.33f,.72f,.91f);
        talkButton.style.borderTopColor=ringColor;talkButton.style.borderBottomColor=ringColor;
        talkButton.style.borderLeftColor=ringColor;talkButton.style.borderRightColor=ringColor;
        stateLabel.text=busy?"Conectando...":Connected?"Conectado · canal "+Channel:"Desconectado";
        channelLabel.text="Selecionado: "+selected+" · "+(selected==1?"Central":selected==2?"Patrulha":"Operações");
        connectButton.SetEnabled(police&&!busy&&MultiplayerWorld.Instance&&MultiplayerWorld.Instance.Connected);
        disconnectButton.SetEnabled(!busy&&Channel>0);
    }
    IEnumerator ReadChannel()
    {
        if(busy)yield break;busy=true;AdminReply reply=null;
        yield return AccountSession.Instance.AdminRequest("GET","/v1/police/radio",null,r=>reply=r);
        busy=false;if(reply!=null&&string.IsNullOrEmpty(reply.code))Channel=reply.radioChannel;
        Refresh();
    }
    IEnumerator SetChannel(int channel)
    {
        if(busy||!AdminPlayerPanel.PoliceDuty)yield break;
        EndTalk();busy=true;Refresh();AdminReply reply=null;
        yield return AccountSession.Instance.AdminRequest("POST","/v1/police/radio","{\"channel\":"+channel+"}",r=>reply=r);
        busy=false;
        if(reply!=null&&string.IsNullOrEmpty(reply.code))Channel=reply.radioChannel;
        else hud.ShowAccountMessage(reply?.error??"Não foi possível conectar ao rádio");
        Refresh();
    }
    void BeginTalk()
    {
        if(Pressed||!Connected)return;
        Pressed=true;VoiceChat.Instance?.BeginRadio();Refresh();
    }
    void EndTalk()
    {
        if(!Pressed)return;
        Pressed=false;keyPressed=false;
        if(VoiceChat.Instance&&VoiceChat.Instance.RadioTransmitting)VoiceChat.Instance.Stop();
        Refresh();
    }
    public void Received(string name)
    {
        if(!Connected)return;
        speakerLabel.text="RÁDIO · "+(string.IsNullOrEmpty(name)?"Policial":name);
        speakerUntil=Time.unscaledTime+1.5f;
    }
    void OnApplicationPause(bool pause){if(pause)EndTalk();}
    void OnApplicationFocus(bool focus){if(!focus)EndTalk();}
    void OnDestroy(){EndTalk();if(Instance==this)Instance=null;}
}
