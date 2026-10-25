using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class GameHud : MonoBehaviour
{
    public static GameHud Instance { get; private set; }
    public bool IsPanelOpen => !string.IsNullOrEmpty(CurrentPanel);
    public bool SprintRequested { get; private set; }
    public string CurrentPanel { get; private set; }
    public VisualElement Root => root;
    public ThirdPersonPlayer Player => player;
    public UIDocument Document => document;
    public string PlayerName { get; private set; }
    public float DistanceTravelled { get; private set; }
    public float TopSpeed { get; private set; }

    static readonly Color Mint=new Color(1f,.79f,.15f);
    const float MapCameraHeight=60f;
    const float AreaMapHalfExtent=27f;
    ThirdPersonPlayer player;
    ThirdPersonCamera orbit;
    CityDayNight dayNight;
    UIDocument document;
    VisualElement root,drawer,content,toast,speedFill,miniViewport,mapArea;
    Label avatarLabel,playerNameLabel,uidLabel,speedLabel,motionLabel,coordinates,clock,heading,drawerTitle,toastLabel,cloudSaveLabel;
    Label distanceMetric,timeMetric,topSpeedMetric;
    Button sprint;
    Button adminButton;
    bool HasAdminAccess=>AccountSession.HasCharacter&&AccountSession.Instance.State.accountRole=="admin"&&AccountSession.Instance.State.adminLevel>0;
    HudIcon miniMarker,mapMarker;
    Image sprintArtwork;
    Label levelLabel,cashLabel,dateLabel,healthLabel,energyLabel,waterLabel;
    VisualElement healthFill,energyFill,waterFill,xpFill;
    Image miniImage,mapImage;
    readonly List<VisualElement> pointerBlockers=new List<VisualElement>();
    Camera miniCamera,mapCamera;
    RenderTexture miniTexture,mapTexture;
    float nextMapUpdate,toastUntil;
    int lastScreenWidth,lastScreenHeight;
    Vector3 lastPosition;
    bool visible=true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void Bootstrap()
    {
        if(Instance)return;
        if(FindAnyObjectByType<ThirdPersonPlayer>())new GameObject("HUD • Interface do jogador").AddComponent<GameHud>();
    }

    void Awake()
    {
        Instance=this;
        player=FindAnyObjectByType<ThirdPersonPlayer>();
        orbit=FindAnyObjectByType<ThirdPersonCamera>();
        dayNight=FindAnyObjectByType<CityDayNight>();
        PlayerName=AccountSession.Authenticated?AccountSession.Instance.State.name:PlayerPrefs.GetString("hud.playerName","ADRIANO");
        orbit.sensitivity=PlayerPrefs.GetFloat("hud.sensitivity",1);
        orbit.distance=PlayerPrefs.GetFloat("hud.distance",4.2f);
        lastPosition=player.transform.position;
        document=gameObject.AddComponent<UIDocument>();
        document.panelSettings=Resources.Load<PanelSettings>("HudPanel");
        if(!document.panelSettings)throw new InvalidOperationException("HudPanel não encontrado. Execute Teste > Preparar HUD.");
        document.sortingOrder=50;
        root=document.rootVisualElement;
        root.name="game-hud";root.AddToClassList("hud-root");root.pickingMode=PickingMode.Ignore;root.focusable=true;root.tabIndex=-1;
        root.AddToClassList("asset-hud");
        root.AddToClassList("hub2-hud");
        root.styleSheets.Add(Resources.Load<StyleSheet>("HudStyles"));
        root.styleSheets.Add(Resources.Load<StyleSheet>("Hud2Styles"));
        CreateMapCameras();Build();InGameChat.Create(this);
        var version=Text("v"+Application.version,"",root);version.name="game-version";
        version.style.position=Position.Absolute;version.style.left=14;version.style.bottom=5;
        version.style.fontSize=10;version.style.color=new Color(.8f,.82f,.85f,.8f);
    }

    VisualElement Element(string css,VisualElement parent=null)
    {
        var e=new VisualElement();
        foreach(var c in css.Split(' '))e.AddToClassList(c);
        (parent??root).Add(e);return e;
    }
    Label Text(string value,string css,VisualElement parent)
    {
        var label=new Label(value);label.pickingMode=PickingMode.Ignore;
        foreach(var c in css.Split(' '))if(c!="")label.AddToClassList(c);
        parent.Add(label);return label;
    }
    Button IconButton(string icon,string css,string tooltip,Action action,VisualElement parent)
    {
        var button=new Button(action){tooltip=tooltip};button.AddToClassList(css);parent.Add(button);
        button.Add(new HudIcon(icon));return button;
    }
    void Block(VisualElement e)=>pointerBlockers.Add(e);

    Button AtlasButton(string asset,string css,string tooltip,Action action,VisualElement parent)
    {
        var button=new Button(action){tooltip=tooltip,name="atlas-"+asset};button.AddToClassList("atlas-button");button.AddToClassList(css);
        button.Add(HudAtlas.Image(asset));parent.Add(button);return button;
    }

    VisualElement AtlasStat(string icon,string css,string tooltip,out VisualElement fill,out Label value,VisualElement parent)
    {
        var row=Element("atlas-stat-row",parent);row.tooltip=tooltip;
        row.Add(HudAtlas.Image(icon,"atlas-stat-icon"));
        var track=Element("atlas-stat-track",row);fill=Element("atlas-stat-fill "+css,track);
        value=Text("100","atlas-stat-value",row);value.name="stat-"+icon+"-value";return row;
    }

    void Build()
    {
        var identity=Element("h2-identity");identity.style.display=DisplayStyle.None;
        playerNameLabel=Text(PlayerName.ToUpperInvariant(),"",identity);avatarLabel=Text("","",identity);
        var wallet=Element("h2-wallet");Block(wallet);
        wallet.Add(HudAtlas.Image("h2_cash","h2-currency"));cashLabel=Text("0","h2-cash",wallet);
        var level=Element("h2-level");Block(level);level.Add(HudAtlas.Image("h2_trophy","h2-trophy"));
        levelLabel=Text("LVL 1 | EXP 0/100","h2-level-text",level);
        xpFill=Element("h2-xp-hidden",level);xpFill.style.display=DisplayStyle.None;
        var stats=Element("h2-stats");Block(stats);
        AtlasStat("h2_heart","health","Vida",out healthFill,out healthLabel,stats);healthLabel.name="stat-heart-value";
        AtlasStat("h2_water","water","Hidratação",out waterFill,out waterLabel,stats);
        AtlasStat("h2_hunger","energy","Fome",out energyFill,out energyLabel,stats);

        var mini=Element("h2-mini");mini.name="minimap-card";Block(mini);
        var miniButton=new Button(()=>OpenPanel("map")){name="nav-map",tooltip="Mapa da área [M]"};miniButton.AddToClassList("h2-mini-button");mini.Add(miniButton);
        miniViewport=Element("mini-viewport",miniButton);
        miniImage=new Image{image=miniTexture,pickingMode=PickingMode.Ignore};miniImage.AddToClassList("mini-image");miniViewport.Add(miniImage);
        miniMarker=new HudIcon("arrow");miniMarker.AddToClassList("marker");miniMarker.SetInk(Color.white);miniViewport.Add(miniMarker);
        Text("N","h2-north",miniButton);

        var nav=Element("h2-nav");Block(nav);
        AtlasButton("h2_menu","h2-nav-button","Menu [Tab]",()=>OpenPanel("menu"),nav).name="nav-menu";
        AtlasButton("h2_tasks","h2-nav-button","Dados da sessão",()=>OpenPanel("session"),nav).name="nav-session";
        AtlasButton("h2_phone","h2-nav-button","Celular",()=>OpenPanel("phone"),nav).name="nav-phone";
        var top=Element("h2-top");Block(top);
        var rewards=AtlasButton("h2_rewards","h2-top-button","Recompensas ainda indisponíveis",()=>{},top);rewards.SetEnabled(false);
        var inventory=AtlasButton("h2_inventory","h2-top-button","Inventário ainda indisponível",()=>{},top);inventory.name="atlas-inventory";inventory.SetEnabled(false);
        var store=AtlasButton("h2_store","h2-top-button","Loja ainda indisponível",()=>{},top);store.SetEnabled(false);
        var tools=Element("h2-tools");Block(tools);
        adminButton=IconButton("admin","h2-admin","Administração · em breve",()=>{},tools);adminButton.name="action-admin";
        ((HudIcon)adminButton[0]).SetInk(new Color(1,.8f,.22f));Text("ADMIN","h2-admin-caption",adminButton);
        adminButton.style.display=HasAdminAccess?DisplayStyle.Flex:DisplayStyle.None;
        var microphone=AtlasButton("h2_microphone","h2-microphone","Chat de voz ainda indisponível",()=>{},tools);microphone.SetEnabled(false);
        AtlasButton("h2_camera","h2-camera","Centralizar câmera [C]",()=>TriggerAction("camera"),tools).name="action-camera";

        var actions=Element("h2-actions");Block(actions);
        AtlasButton("h2_fist","h2-fist","Ações do personagem",()=>OpenPanel("actions"),actions).name="action-list";
        sprint=AtlasButton("h2_sprint","h2-sprint","Ativar/desativar corrida [Shift]",()=>TriggerAction("sprint"),actions);
        sprint.AddToClassList("sprint-button");sprint.name="action-sprint";sprintArtwork=(Image)sprint[0];
        var jump=IconButton("jump","h2-jump","Pular [Espaço]",()=>TriggerAction("jump"),root);jump.name="action-jump";Block(jump);

        var safe=Element("h2-safe");safe.tooltip="Área de testes";safe.pickingMode=PickingMode.Ignore;safe.Add(HudAtlas.Image("h2_safe"));
        var footer=Element("h2-footer");Block(footer);
        var room=Element("h2-chip h2-room",footer);room.tooltip="Sala de testes";
        Text(AccountSession.Authenticated?"CONECTANDO":"LOCAL","",room).name="multiplayer-status";
        var id=Element("h2-chip h2-id",footer);uidLabel=Text(AccountSession.Authenticated?"ID: "+AccountSession.Instance.State.uid:"TESTE","",id);
        var time=Element("h2-chip h2-time",footer);clock=Text(DateTime.Now.ToString("HH:mm"),"",time);
        // These existing hooks remain available to menus without occupying the HUD.
        var compass=Element("compass");compass.name="hud-compass";heading=Text("","",compass);
        var motion=Element("locomotion");motion.name="hud-motion";motion.style.display=DisplayStyle.None;
        speedLabel=Text("0.0","",motion);motionLabel=Text("PARADO","",motion);speedFill=Element("speed-fill",motion);

        drawer=Element("drawer");drawer.name="hud-drawer";Block(drawer);drawer.style.display=DisplayStyle.None;
        var header=Element("drawer-header",drawer);var titleGroup=Element("drawer-titles",header);
        Text("CENTRAL DO JOGADOR","eyebrow",titleGroup);drawerTitle=Text("Menu","drawer-title",titleGroup);
        IconButton("close","close","Fechar [Esc]",ClosePanel,header).name="close-panel";
        content=Element("drawer-content",drawer);
        var back=new Button(ClosePanel){text="VOLTAR AO JOGO"};back.AddToClassList("primary");drawer.Add(back);
        toast=Element("toast");toast.pickingMode=PickingMode.Ignore;toastLabel=Text("","",toast);toast.style.display=DisplayStyle.None;
        root.RegisterCallback<GeometryChangedEvent>(_=>ApplySafeArea());ApplySafeArea();
    }

    public void OpenPanel(string panel)
    {
        if(InGameChat.Editing)InGameChat.Instance.Close();
        SetVisible(true);
        if(CurrentPanel==panel){ClosePanel();return;}
        CurrentPanel=panel;drawer.style.display=DisplayStyle.Flex;
        root.AddToClassList("hud-panel-open");
        root.Q("hud-compass").style.display=DisplayStyle.None;
        root.Q("hud-motion").style.display=DisplayStyle.None;
        content.Clear();distanceMetric=timeMetric=topSpeedMetric=null;mapArea=null;mapMarker=null;mapImage=null;
        switch(panel)
        {
            case "menu":
                drawerTitle.text="Menu";
                Text("Seu espaço para explorar.","panel-description",content);
                MenuItem("person","Personagem","Seu nome e dados da sessão",()=>OpenPanel("player"));
                MenuItem("map","Mapa da área","Veja sua posição no cenário",()=>OpenPanel("map"));
                MenuItem("settings","Configurações","Ajuste a câmera do seu jeito",()=>OpenPanel("settings"));
                MenuItem("help","Como jogar","Teclas e ações disponíveis",()=>OpenPanel("help"));
                if(AccountSession.Authenticated)MenuItem("close","Salvar e sair","Guardar progresso e voltar ao login",()=>AccountSession.Instance.Logout());
                break;
            case "session":
                drawerTitle.text="Sua sessão";
                Text("Explore a Rua do Centro no seu ritmo.","panel-description",content);
                distanceMetric=Metric("Distância percorrida","0 m");timeMetric=Metric("Tempo de exploração","00:00");topSpeedMetric=Metric("Maior velocidade","0 m/s");
                break;
            case "wallet":
                drawerTitle.text="Carteira";
                Text("Seus ganhos aparecerão aqui.","panel-description",content);
                Metric("Saldo disponível","R$ "+player.Cash.ToString("N0"));
                break;
            case "phone":
                drawerTitle.text="Celular";
                MenuItem("map","Mapa","Sua localização na área",()=>OpenPanel("map"));
                MenuItem("person","Personagem","Seus dados e nome",()=>OpenPanel("player"));
                MenuItem("settings","Configurações","Ajustes da câmera",()=>OpenPanel("settings"));
                break;
            case "vehicles":
                drawerTitle.text="Veículos";
                Text("Nenhum veículo disponível nesta área.","panel-description",content);
                break;
            case "actions":
                drawerTitle.text="Ações";
                MenuItem("camera","Centralizar câmera","Olhar na direção do personagem",()=>{TriggerAction("camera");ClosePanel();});
                MenuItem("reset","Voltar ao início","Retornar ao ponto de partida",()=>TriggerAction("reset"));
                MenuItem("help","Controles","Ver todas as teclas",()=>OpenPanel("help"));
                break;
            case "player":
                drawerTitle.text="Personagem";
                Text("Sua jornada começa aqui.","panel-description",content);
                Text("NOME E SOBRENOME","field-label",content);
                var nameInput=new TextField{value=PlayerName,maxLength=49};nameInput.name="player-name-input";nameInput.AddToClassList("name-input");content.Add(nameInput);
                var save=new Button(()=>SaveName(nameInput.value)){text="SALVAR NOME"};save.name="save-name";save.AddToClassList("primary");content.Add(save);
                Element("divider",content);
                if(AccountSession.Authenticated){Text("UID "+AccountSession.Instance.State.uid,"muted",content);cloudSaveLabel=Text(AccountSession.Instance.SaveStatus,"muted",content);}
                distanceMetric=Metric("Distância percorrida","0 m");
                timeMetric=Metric("Tempo na sessão","00:00");
                topSpeedMetric=Metric("Maior velocidade","0.0 m/s");
                break;
            case "map":
                drawerTitle.text="Mapa da Rua do Centro";
                Text("Explore livremente. O marcador mostra sua posição.","panel-description",content);
                mapArea=Element("map-area",content);
                mapImage=new Image{image=mapTexture,pickingMode=PickingMode.Ignore};mapImage.AddToClassList("mini-image");mapArea.Add(mapImage);
                Text("N","north",mapArea);
                mapMarker=new HudIcon("arrow");mapMarker.SetInk(Mint);mapMarker.AddToClassList("marker");mapArea.Add(mapMarker);
                Text("RUA DO CENTRO  /  EXPLORAÇÃO LIVRE","eyebrow",content);
                var center=new Button(()=>TriggerAction("reset")){text="VOLTAR AO PONTO INICIAL"};center.AddToClassList("primary");content.Add(center);
                break;
            case "settings":
                drawerTitle.text="Configurações";
                Text("Uma câmera confortável faz diferença.","panel-description",content);
                Slider("SENSIBILIDADE DA CÂMERA",.4f,2f,orbit.sensitivity,v=>{orbit.sensitivity=v;PlayerPrefs.SetFloat("hud.sensitivity",v);});
                Slider("DISTÂNCIA DA CÂMERA",2f,6f,orbit.distance,v=>{orbit.distance=v;PlayerPrefs.SetFloat("hud.distance",v);});
                var reset=new Button(()=>{PlayerPrefs.DeleteKey("hud.sensitivity");PlayerPrefs.DeleteKey("hud.distance");orbit.sensitivity=1;orbit.distance=4.2f;ClosePanel();ShowToast("Configurações de câmera restauradas");}){text="RESTAURAR PADRÕES"};reset.AddToClassList("primary");content.Add(reset);
                break;
            case "help":
                drawerTitle.text="Como jogar";
                Help("ESPAÇO","Pular (também disponível no botão de pulo)");
                Text("Explore no seu ritmo.","panel-description",content);
                Help("W A S D","Andar pelo cenário");Help("SHIFT","Correr enquanto segura");Help("MOUSE","Arrastar para girar a câmera");Help("RODA","Aproximar ou afastar");Help("C / R","Câmera / voltar ao início");Help("N","Alternar dia e noite");Help("TAB / M","Menu / mapa");Help("P / H","Perfil / ocultar interface");Help("ESC","Fechar um menu");
                break;
            default: ClosePanel();break;
        }
    }

    void MenuItem(string icon,string title,string description,Action action)
    {
        var button=IconButton(icon,"menu-item",title,action,content);
        var text=new VisualElement{pickingMode=PickingMode.Ignore};button.Add(text);
        Text(title,"item-title",text);Text(description,"item-description",text);
    }
    Label Metric(string title,string value)
    {
        var row=Element("metric-row",content);Text(title,"muted",row);return Text(value,"metric-value",row);
    }
    void Slider(string title,float low,float high,float value,Action<float> change)
    {
        Text(title,"field-label",content);
        var slider=new UnityEngine.UIElements.Slider(low,high){value=value,showInputField=true};slider.AddToClassList("settings-slider");
        slider.RegisterValueChangedCallback(e=>{change(e.newValue);PlayerPrefs.Save();});content.Add(slider);
    }
    void Help(string key,string meaning)
    {
        var row=Element("help-row",content);Text(key,"keyboard-key",row);Text(meaning,"",row);
    }
    public void SaveName(string value)
    {
        if(AccountSession.Authenticated){AccountSession.Instance.Rename(value);return;}
        string clean=(value??"").Trim();
        if(clean.Length==0){ShowToast("Digite um nome para o personagem");return;}
        PlayerName=clean.Length>18?clean.Substring(0,18):clean;
        PlayerPrefs.SetString("hud.playerName",PlayerName);PlayerPrefs.Save();
        playerNameLabel.text=PlayerName.ToUpperInvariant();avatarLabel.text=PlayerName.Substring(0,1).ToUpperInvariant();ShowToast("Nome do personagem atualizado");
    }
    public void ApplyAccountIdentity()
    {
        if(!AccountSession.HasCharacter)return;
        PlayerName=AccountSession.Instance.State.name;
        if(playerNameLabel!=null)playerNameLabel.text=PlayerName.ToUpperInvariant();
        if(avatarLabel!=null)avatarLabel.text=PlayerName.Substring(0,1).ToUpperInvariant();
        if(uidLabel!=null)uidLabel.text="ID: "+AccountSession.Instance.State.uid;
    }
    public void ShowAccountMessage(string message)=>ShowToast(message);
    public void ClosePanel()
    {
        CurrentPanel=null;drawer.style.display=DisplayStyle.None;
        root.RemoveFromClassList("hud-panel-open");
        root.Q("hud-compass").style.display=DisplayStyle.Flex;
        root.Q("hud-motion").style.display=DisplayStyle.None;
        root.Focus();PlayerPrefs.Save();
    }
    public void TriggerAction(string action)
    {
        switch(action)
        {
            case "sprint": SprintRequested=!SprintRequested;ShowToast(SprintRequested?"Corrida ativada · mova-se para correr":"Modo caminhada ativado");break;
            case "jump": player.RequestJump();break;
            case "camera": orbit.ResetView();ShowToast("Câmera centralizada");break;
            case "reset": player.ResetPosition();SprintRequested=false;ClosePanel();ShowToast("Você voltou ao ponto inicial");break;
            case "audio": AudioListener.pause=!AudioListener.pause;ShowToast(AudioListener.pause?"Som desativado":"Som ativado");break;
            case "zoom": orbit.distance=orbit.distance>3?2.4f:4.2f;ShowToast(orbit.distance<3?"Câmera aproximada":"Câmera afastada");break;
            case "day-night":
                if(!dayNight)dayNight=FindAnyObjectByType<CityDayNight>();
                if(!dayNight)break;
                dayNight.Toggle();RefreshMaps();
                break;
        }
    }
    public void SetVisible(bool value)
    {
        visible=value;
        if(!value){ClosePanel();SprintRequested=false;}
        root.style.display=value?DisplayStyle.Flex:DisplayStyle.None;
    }
    public void ShowToast(string message)
    {
        toastLabel.text=message;toast.style.display=DisplayStyle.Flex;toastUntil=Time.unscaledTime+2.6f;
    }
    public void NotifyRespawn()
    {
        lastPosition=player.transform.position;SprintRequested=false;
    }

    public bool BlocksPointer(Vector2 screen)
    {
        if(InGameChat.BlocksPointer(screen))return true;
        if(!visible||root.panel==null)return false;
        Vector2 point=RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(screen.x,Screen.height-screen.y));
        foreach(var e in pointerBlockers)
            if(e.resolvedStyle.display!=DisplayStyle.None&&e.worldBound.Contains(point))return true;
        return false;
    }
    void ApplySafeArea()
    {
        if(root.panel==null)return;
        Rect safe=Screen.safeArea;
        Vector2 panelSize=RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(Screen.width,Screen.height))-RuntimePanelUtils.ScreenToPanel(root.panel,Vector2.zero);
        float scale=Screen.height/Mathf.Max(1,panelSize.y);
        root.style.left=safe.xMin/scale;root.style.right=(Screen.width-safe.xMax)/scale;
        root.style.top=(Screen.height-safe.yMax)/scale;root.style.bottom=safe.yMin/scale;
        root.EnableInClassList("compact",Screen.width<1100);
        root.EnableInClassList("narrow-hud",safe.width/scale<940);
        if(lastScreenWidth!=Screen.width||lastScreenHeight!=Screen.height)
        {
            lastScreenWidth=Screen.width;lastScreenHeight=Screen.height;
            root.schedule.Execute(RefreshMaps);
        }
    }

    void Update()
    {
        if(!player)return;
        if(adminButton!=null)adminButton.style.display=HasAdminAccess?DisplayStyle.Flex:DisplayStyle.None;
        root.style.visibility=StartupLoader.IsLoading||CityEntryLoading.Active||
            (AccountSession.Instance&&AccountSession.Instance.AuthVisible)?Visibility.Hidden:Visibility.Visible;
        root.SetEnabled(!StartupLoader.IsLoading && !AccountSession.InputBlocked);
        if(!StartupLoader.IsLoading && !InGameChat.Editing && Input.GetKeyDown(KeyCode.Escape))ClosePanel();
        // Menu keyboard shortcuts are suspended while the name field is being edited.
        var focused=root.focusController?.focusedElement as VisualElement;
        bool editing=InGameChat.Editing||(IsPanelOpen&&(focused is TextField||focused?.GetFirstAncestorOfType<TextField>()!=null));
        if(!editing && !StartupLoader.IsLoading && !AccountSession.InputBlocked)
        {
            if(Input.GetKeyDown(KeyCode.Tab))OpenPanel("menu");
            if(Input.GetKeyDown(KeyCode.M))OpenPanel("map");
            if(Input.GetKeyDown(KeyCode.P))OpenPanel("player");
            if(Input.GetKeyDown(KeyCode.H))SetVisible(!visible);
            if(Input.GetKeyDown(KeyCode.C)&&!IsPanelOpen)TriggerAction("camera");
            if(Input.GetKeyDown(KeyCode.N)&&!IsPanelOpen)TriggerAction("day-night");
        }
        float distance=Vector3.ProjectOnPlane(player.transform.position-lastPosition,Vector3.up).magnitude;
        if(distance<1)DistanceTravelled+=distance;
        lastPosition=player.transform.position;TopSpeed=Mathf.Max(TopSpeed,player.CurrentSpeed);
        speedLabel.text=player.CurrentSpeed.ToString("0.0");
        motionLabel.text=player.IsJumping?"PULANDO":player.CurrentSpeed<.15f?"PARADO":player.CurrentSpeed<2.4f?"CAMINHANDO":"CORRENDO";
        speedFill.style.width=Length.Percent(Mathf.Clamp01(player.CurrentSpeed/player.runSpeed)*100);
        bool running=player.CurrentSpeed>2.4f||SprintRequested||Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift);
        sprint.EnableInClassList("active",running);sprintArtwork.style.opacity=running?1:.9f;
        levelLabel.text="LVL "+player.Level+" | EXP "+Mathf.RoundToInt(player.Experience)+"/100";
        cashLabel.text=player.Cash.ToString("N0",System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        xpFill.style.width=Length.Percent(Mathf.Clamp01(player.Experience/100f)*100);
        healthFill.style.width=Length.Percent(Mathf.Clamp(player.Health,0,100));healthLabel.text=Mathf.RoundToInt(player.Health).ToString();
        if(cloudSaveLabel!=null && AccountSession.Instance)cloudSaveLabel.text=AccountSession.Instance.SaveStatus;
        energyFill.style.width=Length.Percent(Mathf.Clamp(player.Hunger,0,100));energyLabel.text=Mathf.RoundToInt(player.Hunger).ToString();
        waterFill.style.width=Length.Percent(Mathf.Clamp(player.Hydration,0,100));waterLabel.text=Mathf.RoundToInt(player.Hydration).ToString();
        var pos=player.transform.position;if(coordinates!=null)coordinates.text=$"X {pos.x:0.0}    /    Z {pos.z:0.0}";
        if(clock!=null)clock.text=DateTime.Now.ToString("HH:mm");if(dateLabel!=null)dateLabel.text=DateTime.Now.ToString("dd/MM/yyyy");
        float yaw=Mathf.Repeat(orbit.yaw,360);
        string direction=yaw<22.5f||yaw>=337.5f?"N":yaw<67.5f?"NE":yaw<112.5f?"L":yaw<157.5f?"SE":yaw<202.5f?"S":yaw<247.5f?"SO":yaw<292.5f?"O":"NO";
        heading.text=direction+"  ·  "+Mathf.RoundToInt(yaw).ToString("D3")+"°";
        miniMarker.style.rotate=new Rotate(new Angle(player.transform.eulerAngles.y,AngleUnit.Degree));
        if(mapMarker!=null)
        {
            Vector3 viewport=mapCamera.WorldToViewportPoint(pos);
            float mapWidth=mapArea.resolvedStyle.width;
            float mapHeight=mapArea.resolvedStyle.height;
            if(float.IsNaN(mapWidth)||mapWidth<=0)mapWidth=320;
            if(float.IsNaN(mapHeight)||mapHeight<=0)mapHeight=320;
            mapMarker.style.left=Mathf.Clamp01(viewport.x)*mapWidth-10;
            mapMarker.style.top=(1-Mathf.Clamp01(viewport.y))*mapHeight-10;
            mapMarker.style.rotate=new Rotate(new Angle(player.transform.eulerAngles.y,AngleUnit.Degree));
        }
        if(distanceMetric!=null){distanceMetric.text=DistanceTravelled.ToString("0")+" m";timeMetric.text=SessionTime();topSpeedMetric.text=TopSpeed.ToString("0.0")+" m/s";}
        if(toast.resolvedStyle.display!=DisplayStyle.None&&Time.unscaledTime>toastUntil)toast.style.display=DisplayStyle.None;
    }
    string SessionTime()
    {
        int seconds=Mathf.FloorToInt(Time.timeSinceLevelLoad);return (seconds/60).ToString("D2")+":"+(seconds%60).ToString("D2");
    }
    void CreateMapCameras()
    {
        miniTexture=new RenderTexture(256,256,24){name="HUD Minimap"};miniTexture.Create();
        mapTexture=new RenderTexture(512,512,24){name="HUD Area Map"};mapTexture.Create();
        miniCamera=MapCamera("Minimapa",miniTexture,14);
        mapCamera=MapCamera("Mapa da Rua do Centro",mapTexture,AreaMapHalfExtent);
        mapCamera.transform.position=new Vector3(0,MapCameraHeight,0);
    }
    Camera MapCamera(string name,RenderTexture texture,float size)
    {
        var camera=new GameObject(name).AddComponent<Camera>();camera.transform.SetParent(transform);
        camera.orthographic=true;camera.orthographicSize=size;camera.transform.rotation=Quaternion.Euler(90,0,0);
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.065f,.105f,.14f);
        camera.cullingMask=1;camera.farClipPlane=100;camera.nearClipPlane=.1f;camera.targetTexture=texture;
        camera.allowHDR=false;camera.allowMSAA=false;camera.enabled=false;return camera;
    }
    void LateUpdate()
    {
        if(!visible||Time.unscaledTime<nextMapUpdate)return;
        nextMapUpdate=Time.unscaledTime+.1f;
        RefreshMaps();
    }
    public void RefreshMaps()
    {
        if(!miniCamera||miniImage==null)return;
        if(!miniTexture.IsCreated())miniTexture.Create();
        var p=player.transform.position;miniCamera.transform.position=new Vector3(p.x,MapCameraHeight,p.z);miniCamera.Render();miniImage.MarkDirtyRepaint();
        if(CurrentPanel=="map")
        {
            if(!mapTexture.IsCreated())mapTexture.Create();mapCamera.Render();mapImage?.MarkDirtyRepaint();
        }
    }
    void OnDestroy()
    {
        if(Instance==this)Instance=null;
        if(miniTexture){miniTexture.Release();Destroy(miniTexture);}
        if(mapTexture){mapTexture.Release();Destroy(mapTexture);}
    }
}
