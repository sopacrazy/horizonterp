using System;
using System.Collections;
using System.Collections.Generic;
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
    Button openButton,connectButton,disconnectButton;
    VisualElement talkButton;
    StyleColor talkIdleBackground,talkIdleBorder;
    readonly RadioTalkGesture gesture=new RadioTalkGesture();
    public bool Latched=>gesture.Latched;
    Label stateLabel,channelLabel,speakerLabel,openChannel;
    int selected=1;
    int openedFrame,outsideFinger=-1;
    Vector2 outsideStart;
    bool busy,wasPolice;
    float speakerUntil;
    static readonly string[] rightHudNames={"radio-open","radio-push-to-talk","police-car-interact","vehicle-passenger-interact",
        "action-list","action-sprint","action-jump","vehicle-controls","vehicle-options"};
    static readonly string[] rightHudClasses={"h2-microphone","h2-room","h2-latency","h2-top","h2-wallet","realcity-info"};
    readonly Dictionary<VisualElement,StyleEnum<Visibility>> hiddenRightHud=new Dictionary<VisualElement,StyleEnum<Visibility>>();

    void TogglePanel(){SetPanelOpen(panel.style.display!=DisplayStyle.Flex);}
    void SetPanelOpen(bool open)
    {
        panel.style.display=open?DisplayStyle.Flex:DisplayStyle.None;
        if(open)openedFrame=Time.frameCount;
        outsideFinger=-1;
        UpdateRightHudMask();
    }
    bool InsideRadio(Vector2 screenPosition)
    {
        if(panel?.panel==null)return false;
        var panelPoint=RuntimePanelUtils.ScreenToPanel(panel.panel,
            new Vector2(screenPosition.x,Screen.height-screenPosition.y));
        return panel.worldBound.Contains(panelPoint);
    }
    void CloseOnOutsideTap()
    {
        if(panel==null||panel.style.display!=DisplayStyle.Flex||Time.frameCount<=openedFrame)return;
        if(Application.isMobilePlatform)
        {
            foreach(var touch in Input.touches)
            {
                if(touch.phase==TouchPhase.Began&&outsideFinger<0&&!InsideRadio(touch.position))
                {outsideFinger=touch.fingerId;outsideStart=touch.position;}
                if(touch.fingerId!=outsideFinger)continue;
                if(touch.phase==TouchPhase.Canceled)outsideFinger=-1;
                else if(touch.phase==TouchPhase.Ended)
                {
                    outsideFinger=-1;
                    if(Vector2.Distance(outsideStart,touch.position)<35f&&!InsideRadio(touch.position))
                    {SetPanelOpen(false);break;}
                }
            }
        }
        else if(Input.GetMouseButtonUp(0)&&!InsideRadio(Input.mousePosition))SetPanelOpen(false);
    }
    void UpdateRightHudMask()
    {
        if(panel==null||hud==null)return;
        if(panel.style.display==DisplayStyle.Flex)
        {
            foreach(var name in rightHudNames)
                Hide(hud.Root.Q<VisualElement>(name));
            foreach(var className in rightHudClasses)
                Hide(hud.Root.Q<VisualElement>(className:className));
        }
        else
        {
            foreach(var item in hiddenRightHud)
                if(item.Key?.panel!=null)item.Key.style.visibility=item.Value;
            hiddenRightHud.Clear();
        }
    }
    void Hide(VisualElement element)
    {
        if(element==null||hiddenRightHud.ContainsKey(element))return;
        hiddenRightHud.Add(element,element.style.visibility);
        element.style.visibility=Visibility.Hidden;
    }

    public static void Create(GameHud owner)
    {
        if(owner&&!Instance)owner.gameObject.AddComponent<PoliceRadio>().Build(owner);
    }
    void Awake(){Instance=this;}
    static Button Button(string text,Action action)
    {
        var b=new Button(action){text=text};
        b.style.height=38;b.style.marginBottom=5;b.style.color=Color.white;HudPack.Skin(b,10);
        return b;
    }
    static Label Label(string text,int size=13)
    {
        var l=new Label(text);l.style.fontSize=size;l.style.color=Color.white;l.style.marginBottom=7;
        l.pickingMode=PickingMode.Ignore;return l;
    }
    static Button ScreenButton(string text,Action action)
    {
        var button=Button(text,action);
        button.style.height=31;button.style.marginBottom=3;button.style.fontSize=11;
        button.style.paddingLeft=5;button.style.paddingRight=5;
        return button;
    }
    void Build(GameHud owner)
    {
        hud=owner;
        openButton=Button("",TogglePanel);
        openButton.name="radio-open";openButton.tooltip="Rádio policial";
        openButton.style.position=Position.Absolute;openButton.style.right=24;openButton.style.top=202;
        openButton.style.width=46;openButton.style.height=46;openButton.style.alignItems=Align.Center;openButton.style.justifyContent=Justify.Center;HudPack.Skin(openButton,14);
        var openIcon=HudPack.Image("radio");openIcon.name="radio-open-icon";openIcon.style.width=26;openIcon.style.height=26;openButton.Add(openIcon);
        openChannel=Label("",10);openChannel.name="radio-channel-badge";openChannel.style.position=Position.Absolute;openChannel.style.right=3;openChannel.style.bottom=0;openChannel.style.marginBottom=0;openChannel.style.unityFontStyleAndWeight=FontStyle.Bold;openButton.Add(openChannel);
        owner.Root.Add(openButton);
        owner.RegisterPointerBlocker(openButton);
        // A plain element avoids Button's Clickable manipulator stealing touch capture.
        talkButton=new VisualElement {name="radio-push-to-talk"};
        talkButton.tooltip="Segure para falar · Dois toques para fixar · Toque para desligar";
        talkButton.style.position=Position.Absolute;talkButton.style.right=130;talkButton.style.bottom=132;
        talkButton.style.width=64;talkButton.style.height=64;talkButton.style.marginBottom=0;
        talkButton.style.flexDirection=FlexDirection.Column;
        talkButton.style.alignItems=Align.Center;talkButton.style.justifyContent=Justify.Center;
        HudPack.Skin(talkButton,32);
        talkIdleBackground=talkButton.style.backgroundColor;talkIdleBorder=talkButton.style.borderTopColor;
        var talkIcon=HudPack.Image("radio");talkIcon.name="radio-talk-icon";talkIcon.style.width=28;talkIcon.style.height=28;talkButton.Add(talkIcon);
        talkButton.RegisterCallback<PointerDownEvent>(e=>
        {
            if(e.button!=0||!Connected||busy||!gesture.Down(e.pointerId,Time.unscaledTimeAsDouble))return;
            talkButton.CapturePointer(e.pointerId);ApplyTalkInput();e.StopImmediatePropagation();
        },TrickleDown.TrickleDown);
        talkButton.RegisterCallback<PointerUpEvent>(e=>
        {
            if(!gesture.Up(e.pointerId,Time.unscaledTimeAsDouble))return;
            ApplyTalkInput();
            if(talkButton.HasPointerCapture(e.pointerId))talkButton.ReleasePointer(e.pointerId);
            e.StopImmediatePropagation();
        });
        talkButton.RegisterCallback<PointerCancelEvent>(e=>
        {if(gesture.Up(e.pointerId,Time.unscaledTimeAsDouble,true))ApplyTalkInput();});
        talkButton.RegisterCallback<PointerCaptureOutEvent>(e=>
        {if(gesture.Up(e.pointerId,Time.unscaledTimeAsDouble,true))ApplyTalkInput();});
        owner.Root.Add(talkButton);
        owner.RegisterPointerBlocker(talkButton);
        panel=new VisualElement{name="radio-panel"};panel.style.position=Position.Absolute;panel.style.right=24;panel.style.top=24;
        panel.style.width=400;panel.style.height=600;
        var device=new Image{image=Resources.Load<Texture2D>("PoliceRadio/Device"),scaleMode=ScaleMode.StretchToFill,pickingMode=PickingMode.Ignore};
        device.name="radio-device";device.style.position=Position.Absolute;device.style.left=0;device.style.right=0;device.style.top=0;device.style.bottom=0;panel.Add(device);
        var screen=new ScrollView(ScrollViewMode.Vertical){name="radio-screen"};screen.style.position=Position.Absolute;
        screen.style.left=125;screen.style.top=229;screen.style.width=151;screen.style.height=196;
        screen.style.paddingLeft=3;screen.style.paddingRight=3;
        screen.verticalScrollerVisibility=ScrollerVisibility.Auto;
        panel.Add(screen);
        var header=new VisualElement{name="radio-panel-header"};header.style.flexDirection=FlexDirection.Row;header.style.alignItems=Align.Center;screen.Add(header);
        var title=Label("RÁDIO POLICIAL",12);title.name="radio-panel-title";title.style.flexGrow=1;title.style.marginBottom=0;header.Add(title);
        var close=new Button(()=>SetPanelOpen(false)){name="radio-close",tooltip="Fechar rádio"};close.style.width=26;close.style.height=26;close.style.alignItems=Align.Center;close.style.justifyContent=Justify.Center;HudPack.Skin(close,8);
        var closeIcon=HudPack.Image("close");closeIcon.style.width=15;closeIcon.style.height=15;close.Add(closeIcon);header.Add(close);
        stateLabel=Label("Desconectado",11);stateLabel.name="radio-state";stateLabel.style.marginTop=5;stateLabel.style.marginBottom=3;screen.Add(stateLabel);
        channelLabel=Label("Selecione um canal",10);channelLabel.name="radio-selected-channel";channelLabel.style.marginBottom=4;screen.Add(channelLabel);
        foreach(var option in new[]{(1,"1 · Central"),(2,"2 · Patrulha"),(3,"3 · Operações")})
        {
            int choice=option.Item1;
            var button=ScreenButton(option.Item2,()=>{selected=choice;Refresh();});button.name="radio-select-"+choice;screen.Add(button);
        }
        connectButton=ScreenButton("CONECTAR / TROCAR",()=>StartCoroutine(SetChannel(selected)));connectButton.name="radio-connect";connectButton.style.backgroundColor=new Color(.08f,.35f,.25f,.95f);screen.Add(connectButton);
        disconnectButton=ScreenButton("DESCONECTAR",()=>StartCoroutine(SetChannel(0)));disconnectButton.name="radio-disconnect";screen.Add(disconnectButton);
        var volumeTitle=Label("VOLUME",10);volumeTitle.style.marginTop=2;volumeTitle.style.marginBottom=0;screen.Add(volumeTitle);
        var slider=new Slider(0,1){name="radio-volume",value=Volume};slider.style.height=25;
        slider.RegisterValueChangedCallback(e=>Volume=e.newValue);screen.Add(slider);
        speakerLabel=Label("");speakerLabel.name="radio-current-speaker";speakerLabel.style.position=Position.Absolute;
        speakerLabel.style.right=82;speakerLabel.style.top=216;owner.Root.Add(speakerLabel);
        owner.Root.Add(panel);owner.RegisterPointerBlocker(panel);panel.style.display=DisplayStyle.None;Refresh();
    }
    void Update()
    {
        bool police=AccountSession.HasCharacter&&AdminPlayerPanel.PoliceDuty;
        if(police&&!wasPolice)StartCoroutine(ReadChannel());
        if(!police&&wasPolice){EndTalk();Channel=0;SetPanelOpen(false);}
        wasPolice=police;
        bool blocked=hud.IsPanelOpen||InGameChat.Editing;
        if(!Connected||blocked)EndTalk();
        else
        {
            if(Input.GetKeyDown(pushKey))keyPressed=true;
            if(keyPressed&&!Input.GetKey(pushKey))keyPressed=false;
            ApplyTalkInput();
        }
        if(speakerLabel!=null&&Time.unscaledTime>speakerUntil)speakerLabel.text="";
        Refresh();
        UpdateRightHudMask();
        CloseOnOutsideTap();
    }
    bool keyPressed;
    void Refresh()
    {
        if(openButton==null)return;
        bool police=AccountSession.HasCharacter&&AdminPlayerPanel.PoliceDuty;
        openButton.style.display=police?DisplayStyle.Flex:DisplayStyle.None;
        openChannel.text=Channel>0?Channel.ToString():"";
        openButton.tooltip=Channel>0?"Rádio policial · canal "+Channel:"Rádio policial";
        talkButton.style.display=Connected?DisplayStyle.Flex:DisplayStyle.None;
        talkButton.style.backgroundColor=Pressed?new StyleColor(new Color(.1f,.66f,.49f,.9f)):talkIdleBackground;
        var ringColor=Pressed?new StyleColor(new Color(.48f,1f,.79f,.95f)):talkIdleBorder;
        talkButton.style.borderTopColor=ringColor;talkButton.style.borderBottomColor=ringColor;
        talkButton.style.borderLeftColor=ringColor;talkButton.style.borderRightColor=ringColor;
        talkButton.style.opacity=Pressed ? .82f+.18f*Mathf.Abs(Mathf.Sin(Time.unscaledTime*5)) : 1f;
        talkButton.tooltip=Latched?"Rádio fixado · Toque para desligar":"Segure para falar · Dois toques para fixar";
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
    void ApplyTalkInput()
    {
        bool wanted=Connected&&(gesture.Active||keyPressed);
        if(wanted==Pressed)return;
        Pressed=wanted;
        if(wanted)VoiceChat.Instance?.BeginRadio();
        else VoiceChat.Instance?.StopRadio();
        Refresh();
    }
    void EndTalk()
    {
        int pointer=gesture.Pointer;
        gesture.Cancel();Pressed=false;keyPressed=false;
        // Cancel even while Vivox is still awaiting its channel switch.
        VoiceChat.Instance?.StopRadio();
        if(pointer>=0&&talkButton!=null&&talkButton.HasPointerCapture(pointer))talkButton.ReleasePointer(pointer);
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
    void OnDisable(){EndTalk();if(panel!=null)SetPanelOpen(false);}
    void OnDestroy(){EndTalk();if(panel!=null)SetPanelOpen(false);if(Instance==this)Instance=null;}
}
