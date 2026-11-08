using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class GameHud : MonoBehaviour
{
    AudioSource phoneAudio;
    bool takingPhonePhoto;
    string selectedCharacterUid,selectedCharacterName;
    VisualElement notesOverlay,characterCard;
    TextField notesField;
    Label characterCardTitle,characterCardDetail;
    Transform selectedCharacterTransform;
    float characterCardExpiresAt;
    int characterTapFinger=-1;
    Vector2 characterTapStart;

    void PlayPhoneSound(string clipName)
    {
        var clip = Resources.Load<AudioClip>("RealCityAudio/" + clipName);
        if (!clip) return;
        if (!phoneAudio)
        {
            phoneAudio = gameObject.AddComponent<AudioSource>();
            phoneAudio.playOnAwake = false;
            phoneAudio.spatialBlend = 0;
            phoneAudio.volume = .4f;
        }
        phoneAudio.clip = clip;
        phoneAudio.Play();
    }

    IEnumerator TakePhonePhoto()
    {
        if (takingPhonePhoto) yield break;
        takingPhonePhoto = true;
        ClosePanel();
        yield return new WaitForEndOfFrame();

        Texture2D photo = null;
        try
        {
            string folder = Path.Combine(Application.persistentDataPath, "Fotos");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff") + ".png");
            photo = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(path, photo.EncodeToPNG());
            PlayPhoneSound("photosound");
            ShowToast("Foto salva na pasta Fotos do jogo.");
        }
        catch (Exception error)
        {
            Debug.LogWarning("Não foi possível salvar a foto: " + error.Message);
            ShowToast("Não foi possível salvar a foto.");
        }
        finally
        {
            if (photo) Destroy(photo);
            takingPhonePhoto = false;
        }
    }
    public static GameHud Instance { get; private set; }
    public bool IsPanelOpen => VehicleHud.MenuOpen|| !string.IsNullOrEmpty(CurrentPanel)||notesOverlay!=null&&notesOverlay.resolvedStyle.display==DisplayStyle.Flex||(AdminPlayerPanel.Instance&&AdminPlayerPanel.Instance.IsOpen)||
        (AdminHelpPanel.Instance&&AdminHelpPanel.Instance.IsOpen)||CriminalEconomyPilotPanel.IsOpen;
    public bool SprintRequested { get; private set; }
    public string CurrentPanel { get; private set; }
    public VisualElement Root => root;
    public ThirdPersonPlayer Player => player;
    public UIDocument Document => document;
    public string PlayerName { get; private set; }
    public float DistanceTravelled { get; private set; }
    public float TopSpeed { get; private set; }
    public float MinimapYaw => cityMapAtlas?cityMinimapYaw:miniCamera?Mathf.Repeat(miniCamera.transform.eulerAngles.y,360):0;
    public float MinimapArrowYaw => miniMarker==null?float.NaN:miniMarker.resolvedStyle.rotate.angle.value;

    static readonly Color Mint=new Color(1f,.79f,.15f);
    static readonly Color PlayerMapBlue=new Color(.12f,.56f,1f);
    float MapCameraHeight => NeighborhoodMap.Active?NeighborhoodMap.Active.mapCameraHeight:60f;
    float AreaMapHalfExtent => NeighborhoodMap.Active?NeighborhoodMap.Active.MapHalfExtent:27f;
    string MapDisplayName => NeighborhoodMap.Active?NeighborhoodMap.Active.displayName:"Rua do Centro";
    ThirdPersonPlayer player;
    ThirdPersonCamera orbit;
    CityDayNight dayNight;
    UIDocument document;
    VisualElement root,drawer,content,toast,speedFill,miniViewport,mapArea,coordinatePanel;
    Label avatarLabel,playerNameLabel,uidLabel,speedLabel,motionLabel,coordinates,clock,heading,drawerTitle,toastLabel,cloudSaveLabel,minimapNorth,hzCoinsLabel;
    Label distanceMetric,timeMetric,topSpeedMetric;
    Button sprint,adminButton,microphoneButton;
    VisualElement microphoneRing;
    HudIcon idMicrophone;
    Label latencyLabel;
    Label jailTimeLabel;
    float jailDeadline;
    HudIcon miniMarker,mapMarker;
    Image sprintArtwork;
    VisualElement minimapHealth, minimapEnergy;
    Label levelLabel,cashLabel,dateLabel,healthLabel,energyLabel,waterLabel;
    HudStatusRing healthRing,energyRing,waterRing;
    VisualElement xpFill;
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
        PlayerName=AccountSession.Authenticated?PlayerDisplayName.From(AccountSession.Instance.State):PlayerDisplayName.From(null,PlayerPrefs.GetString("hud.playerName","ADRIANO"));
        orbit.sensitivity=PlayerPrefs.GetFloat("hud.sensitivity",1);
        orbit.distance=PlayerPrefs.GetFloat("hud.distance",4.2f);
        AudioListener.volume=Mathf.Clamp01(PlayerPrefs.GetFloat("hud.masterVolume",1));
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
        root.AddToClassList("modern-hud");
        root.style.unityFont=Resources.Load<Font>("HudMinimal/Barlow-Medium");
        CreateMapCameras();Build();InGameChat.Create(this);PoliceRadio.Create(this);
        var modernStyles=Resources.Load<StyleSheet>("HudModernStyles");
        if(modernStyles)root.styleSheets.Add(modernStyles);
        root.styleSheets.Add(Resources.Load<StyleSheet>("RealCityHud/Hud"));
        var menuStyles=Resources.Load<StyleSheet>("PlayerMenu");
        if(menuStyles)root.styleSheets.Add(menuStyles);
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
        var button=new Button(action){tooltip=tooltip};
        foreach(var styleClass in css.Split(' '))if(styleClass!="")button.AddToClassList(styleClass);
        parent.Add(button);
        var modern=HudPack.Texture(icon);
        if(modern)button.Add(HudPack.Image(icon));
        else button.Add(new HudIcon(icon));
        return button;
    }
    void Block(VisualElement e)=>pointerBlockers.Add(e);

    Image RealCityImage(string asset)
        => new Image
        {
            image=Resources.Load<Texture2D>("RealCityHud/"+asset),
            scaleMode=ScaleMode.ScaleToFit,
            pickingMode=PickingMode.Ignore
        };

    Button RealCityButton(string asset,string css,string tooltip,Action action,VisualElement parent)
    {
        var button=new Button(action){tooltip=tooltip};
        button.AddToClassList(css);
        button.Add(RealCityImage(asset));
        parent.Add(button);
        return button;
    }
    public void RegisterPointerBlocker(VisualElement element)
    {
        if(element!=null&&!pointerBlockers.Contains(element))pointerBlockers.Add(element);
    }

    void StatusRing(string icon,string title,Color color,out HudStatusRing ring,out Label value,VisualElement parent)
    {
        var cell=Element("status-cell",parent);cell.tooltip=title;
        ring=new HudStatusRing(color);cell.Add(ring);
        var symbol=icon=="health"?HudPack.Image(icon):RealCityImage(icon=="hunger"?"food":icon);
        symbol.AddToClassList("status-symbol");ring.Add(symbol);
        value=Text("100","status-value",ring);value.name="stat-"+icon+"-value";
        Text(title.ToUpperInvariant(),"status-caption",cell);
    }

    void Build()
    {
        var identity=Element("h2-identity");identity.style.display=DisplayStyle.None;
        playerNameLabel=Text(PlayerName.ToUpperInvariant(),"",identity);avatarLabel=Text("","",identity);
        var wallet=Element("h2-wallet");Block(wallet);
        cashLabel=Text("R$ 0","h2-cash",wallet);
        var walletIcon=RealCityImage("money");walletIcon.AddToClassList("h2-currency");wallet.Add(walletIcon);
        var level=Element("h2-level");Block(level);var levelIcon=HudPack.Image("level");levelIcon.AddToClassList("h2-trophy");level.Add(levelIcon);
        levelLabel=Text("LVL 1 | EXP 0/100","h2-level-text",level);
        xpFill=Element("h2-xp-hidden",level);xpFill.style.display=DisplayStyle.None;
        var stats=Element("h2-stats");Block(stats);
        StatusRing("health","Vida",new Color(.94f,.43f,.43f),out healthRing,out healthLabel,stats);healthLabel.name="stat-heart-value";
        healthRing.name="health-progress";
        StatusRing("water","Água",new Color(.39f,.73f,.9f),out waterRing,out waterLabel,stats);
        StatusRing("hunger","Comida",new Color(.91f,.73f,.38f),out energyRing,out energyLabel,stats);

        var mini=Element("h2-mini");mini.name="minimap-card";Block(mini);
        var miniButton=new Button(()=>OpenPanel("map")){name="nav-map",tooltip="Mapa da área [M]"};miniButton.AddToClassList("h2-mini-button");mini.Add(miniButton);
        miniViewport=Element("mini-viewport",miniButton);
        if(cityMapAtlas)BuildCityMinimap();
        else {miniImage=new Image{image=miniTexture,pickingMode=PickingMode.Ignore};miniImage.AddToClassList("mini-image");miniViewport.Add(miniImage);}
        miniMarker=new HudIcon("arrow");miniMarker.AddToClassList("marker");miniMarker.SetInk(Color.white);miniViewport.Add(miniMarker);
        minimapNorth=Text("N","h2-north",miniButton);
        var meters=Element("realcity-minimap-meters",miniButton);meters.pickingMode=PickingMode.Ignore;
        minimapHealth=Element("realcity-minimap-health",Element("realcity-minimap-track",meters));
        minimapEnergy=Element("realcity-minimap-energy",Element("realcity-minimap-track",meters));

        var nav=Element("h2-nav");Block(nav);
        RealCityButton("menu","h2-nav-button","Menu [Tab]",()=>OpenPanel("menu"),nav).name="nav-menu";
        RealCityButton("phone","h2-nav-button","Celular",()=>OpenPanel("phone"),nav).name="nav-phone";
        RealCityButton("backpack","h2-nav-button","Inventário",()=>OpenPanel("inventory"),nav).name="nav-inventory";
        RealCityButton("dance","h2-nav-button","Animações",()=>OpenPanel("animations"),nav).name="nav-animations";
        var top=Element("h2-top");Block(top);
        adminButton=IconButton("user","h2-top-button","Administração",()=>AdminPlayerPanel.Open(this),top);
        adminButton.name="action-admin";
        IconButton("list","h2-top-button","Bloco de anotações",OpenNotes,top).name="action-notes";
        var store=IconButton("store","h2-top-button","Loja ainda indisponível",()=>{},top);
        store.name="atlas-h2_store";store.SetEnabled(false);
        microphoneButton=RealCityButton("mic","h2-microphone","Ligar/desligar microfone",()=>VoiceChat.Instance?.Toggle(),root);Block(microphoneButton);
        microphoneRing=new VisualElement {pickingMode=PickingMode.Ignore};
        microphoneRing.AddToClassList("h2-microphone-ring");
        microphoneButton.Add(microphoneRing);
        var camera=RealCityButton("camera","realcity-camera","Centralizar câmera [C]",()=>TriggerAction("camera"),root);camera.name="action-camera";Block(camera);
        var chat=RealCityButton("chat","realcity-chat","Mostrar ou ocultar chat",()=>InGameChat.Instance?.ToggleVisibility(),root);chat.name="action-chat";Block(chat);

        var actions=Element("h2-actions");Block(actions);
        var fist=RealCityButton("fist","h2-fist","Soco [F]",()=>TriggerAction("attack"),root);fist.name="action-list";Text("PUNHO","realcity-weapon",fist);Block(fist);
        sprint=RealCityButton("run","h2-sprint","Ativar/desativar corrida [Shift]",()=>TriggerAction("sprint"),actions);
        sprint.AddToClassList("sprint-button");sprint.name="action-sprint";sprintArtwork=(Image)sprint[0];
        var jump=RealCityButton("jump","h2-jump","Pular [Espaço]",()=>TriggerAction("jump"),root);jump.name="action-jump";Block(jump);
        SetupActionLayout(fist,jump,microphoneButton,sprint);

        var footer=Element("h2-footer");Block(footer);
        jailTimeLabel=Text("","h2-chip",footer);
        jailTimeLabel.style.display=DisplayStyle.None;
        jailTimeLabel.style.color=new Color(1f,.76f,.31f);
        var room=Element("h2-chip h2-room",footer);room.tooltip="Sala de testes";
        Text(AccountSession.Authenticated?"CONECTANDO":"LOCAL","",room).name="multiplayer-status";
        var playerInfo=Element("realcity-info");Block(playerInfo);
        var id=Element("h2-chip h2-id",playerInfo);uidLabel=Text(AccountSession.Authenticated?"ID: "+AccountSession.Instance.State.uid:"TESTE","",id);
        id.style.flexDirection=FlexDirection.Row;id.style.alignItems=Align.Center;
        idMicrophone=new HudIcon("microphone"){name="id-microphone"};
        idMicrophone.style.width=16;idMicrophone.style.height=18;idMicrophone.style.marginLeft=5;
        idMicrophone.SetInk(new Color(.18f,1f,.5f));idMicrophone.style.display=DisplayStyle.None;id.Add(idMicrophone);
        var time=Element("h2-chip h2-time",playerInfo);time.Add(RealCityImage("clock"));clock=Text(DateTime.Now.ToString("HH:mm"),"",time);
        var coins=Element("h2-chip realcity-coins",playerInfo);Text("HZ","realcity-coin-symbol",coins);
        hzCoinsLabel=Text("0","",coins);
        var latency=Element("h2-chip h2-latency",footer);
        latencyLabel=Text("PING —","",latency);latencyLabel.name="network-latency";
        // These existing hooks remain available to menus without occupying the HUD.
        var compass=Element("compass");compass.name="hud-compass";heading=Text("","",compass);
        var motion=Element("locomotion");motion.name="hud-motion";motion.style.display=DisplayStyle.None;
        speedLabel=Text("0.0","",motion);motionLabel=Text("PARADO","",motion);speedFill=Element("speed-fill",motion);

        coordinatePanel=Element("coordinate-panel");coordinatePanel.name="coordinate-panel";Block(coordinatePanel);
        var coordinateHeader=Element("coordinate-header",coordinatePanel);
        Text("POSIÇÃO DO PLAYER","coordinate-title",coordinateHeader);
        Text("F3","coordinate-shortcut",coordinateHeader);
        coordinates=Text("","coordinate-values",coordinatePanel);coordinates.name="coordinate-values";
        var copyCoordinates=new Button(CopyCoordinates){text="COPIAR COORDENADAS",name="copy-coordinates"};
        copyCoordinates.AddToClassList("coordinate-copy");coordinatePanel.Add(copyCoordinates);
        coordinatePanel.style.display=DisplayStyle.None;

        menuShade=Element("player-menu-shade");menuShade.style.display=DisplayStyle.None;Block(menuShade);
        menuShade.RegisterCallback<ClickEvent>(_=>{if(CurrentPanel=="phone")ClosePanel();});
        drawer=Element("drawer");drawer.name="hud-drawer";Block(drawer);drawer.style.display=DisplayStyle.None;
        var header=Element("drawer-header",drawer);var titleGroup=Element("drawer-titles",header);
        Text("CENTRAL DO JOGADOR","eyebrow",titleGroup);drawerTitle=Text("Menu","drawer-title",titleGroup);
        IconButton("close","close","Fechar [Esc]",ClosePanel,header).name="close-panel";
        menuTabs=Element("player-menu-tabs",drawer);
        content=Element("drawer-content",drawer);
        content.RegisterCallback<GeometryChangedEvent>(OnCityMapLayout);
        var back=new Button(ClosePanel){name="drawer-back",text="VOLTAR AO JOGO"};back.AddToClassList("primary");drawer.Add(back);
        toast=Element("toast");toast.pickingMode=PickingMode.Ignore;toastLabel=Text("","",toast);toast.style.display=DisplayStyle.None;
        BuildNotesOverlay();
        BuildCharacterCard();
        root.RegisterCallback<GeometryChangedEvent>(_=>ApplySafeArea());ApplySafeArea();
    }

    public void OpenPanel(string panel)
    {
        if (actionEditor != null) return;
        CloseCharacterCard();
        if(notesOverlay!=null)notesOverlay.style.display=DisplayStyle.None;
        CriminalEconomyPilotPanel.CloseOverlay();
        AdminPlayerPanel.Instance?.Close();
        if(InGameChat.Editing)InGameChat.Instance.Close();
        SetVisible(true);
        if(CurrentPanel==panel){ClosePanel();return;}
        if(CurrentPanel=="animations")root.Q("animation-test-wheel")?.RemoveFromHierarchy();
        if(CurrentPanel=="phone"&&phoneAudio)phoneAudio.Stop();
        if(CurrentPanel=="map")CancelCityMapSelection();
        CurrentPanel=panel;drawer.style.display=DisplayStyle.Flex;
        drawer.EnableInClassList("menu-home",panel=="menu");
        drawer.EnableInClassList("city-map-fullscreen",panel=="map"&&cityMapAtlas);
        root.EnableInClassList("menu-home-open",panel=="menu");
        root.AddToClassList("hud-panel-open");
        root.Q("hud-compass").style.display=DisplayStyle.None;
        root.Q("hud-motion").style.display=DisplayStyle.None;
        content.Clear();distanceMetric=timeMetric=topSpeedMetric=null;mapArea=null;mapMarker=null;mapImage=null;
        UpdateMenuChrome(panel);
        switch(panel)
        {
            case "menu":
                drawerTitle.text="Central Horizonte";
                BuildHomeMenu();
                break;
            case "animations":
                drawer.style.display=DisplayStyle.None;
                AnimationTestWheel.Open(root,player.animator,onClose:() =>
                {
                    if(this&&CurrentPanel=="animations")ClosePanel();
                },playInWorld:clip =>
                {
                    ClosePanel();
                    if (player.IsGrounded && !player.InPoliceCar && !player.IsCuffed)
                        AnimationTestWheel.PlayInWorld(player,clip);
                    else ShowToast("Pare em terra firme para testar a animação.");
                });
                break;
            case "inventory":
                drawerTitle.text="Inventário";
                BuildInventoryMenu(null,"Consultando itens do personagem...");
                if(AccountSession.HasCharacter)StartCoroutine(RefreshInventoryMenu());
                break;
            case "online-players":
                drawerTitle.text="Jogadores online";
                var online=MultiplayerWorld.Instance;
                Text("Na sala agora: "+(online?.PlayerCount??0),"panel-description",content);
                if(AccountSession.HasCharacter)MenuItem("user",PlayerName+" ["+AccountSession.Instance.State.uid+"]","Você",()=>ShowCharacterCard(AccountSession.Instance.State.uid,PlayerName));
                var players=new List<RadarPlayer>();online?.GetRadarPlayers(players);
                foreach(var entry in players)
                {var current=entry;MenuItem("user",current.name+" ["+current.uid+"]","Ver identidade e status",()=>ShowCharacterCard(current.uid,current.name));}
                break;
            case "session":
                drawerTitle.text="Sua sessão";
                Text("Explore a Rua do Centro no seu ritmo.","panel-description",content);
                distanceMetric=Metric("Distância percorrida","0 m");timeMetric=Metric("Tempo de exploração","00:00");topSpeedMetric=Metric("Maior velocidade","0 m/s");
                break;
            case "police-dispatch":
                drawerTitle.text="Central 190";
                BuildPoliceDispatch();
                break;
            case "police-fines":
                drawerTitle.text="Minhas multas";
                BuildPoliceFines();
                break;
            case "missions":
                drawerTitle.text="Central de Missões";
                BuildMissionPanel();
                break;
            case "wallet":
                drawerTitle.text="Carteira";
                BuildWalletMenu();
                break;
            case "phone":
                drawerTitle.text="Celular";
                BuildPhoneHome();
                break;
            case "vehicles":
                drawerTitle.text="Veículos";
                Text("Nenhum veículo disponível nesta área.","panel-description",content);
                break;
            case "actions":
                drawerTitle.text="Ações";
                MenuItem("help","Soco","Um soco por clique",()=>{ClosePanel();TriggerAction("attack");});
                MenuItem("camera","Centralizar câmera","Olhar na direção do personagem",()=>{TriggerAction("camera");ClosePanel();});
                MenuItem("reset","Voltar ao início","Retornar ao ponto de partida",()=>TriggerAction("reset"));
                MenuItem("help","Controles","Ver todas as teclas",()=>OpenPanel("help"));
                break;
            case "player":
                drawerTitle.text="Personagem";
                Text("Sua jornada começa aqui.","panel-description",content);
                Text("PRIMEIRO NOME","field-label",content);
                var nameInput=new TextField{value=PlayerName,maxLength=24};nameInput.name="player-name-input";nameInput.AddToClassList("name-input");content.Add(nameInput);
                var save=new Button(()=>SaveName(nameInput.value)){text="SALVAR NOME"};save.name="save-name";save.AddToClassList("primary");content.Add(save);
                Element("divider",content);
                if(AccountSession.Authenticated){
                    Text("UID "+AccountSession.Instance.State.uid,"muted",content);
                    var organization=AccountSession.Instance.State.organization;
                    Text("ORGANIZAÇÃO: "+(organization==null?"Nenhuma":organization.name+" · "+organization.rank),"muted",content);
                    cloudSaveLabel=Text(AccountSession.Instance.SaveStatus,"muted",content);
                }
                distanceMetric=Metric("Distância percorrida","0 m");
                timeMetric=Metric("Tempo na sessão","00:00");
                topSpeedMetric=Metric("Maior velocidade","0.0 m/s");
                break;
            case "map":
                if(cityMapAtlas){BuildCityMapPanel();break;}
                drawerTitle.text="Mapa · "+MapDisplayName;
                Text("Explore livremente. O marcador mostra sua posição.","panel-description",content);
                mapArea=Element("map-area",content);
                mapImage=new Image{image=mapTexture,pickingMode=PickingMode.Ignore};mapImage.AddToClassList("mini-image");mapArea.Add(mapImage);
                Text("N","north",mapArea);
                mapMarker=new HudIcon("arrow");mapMarker.SetInk(PlayerMapBlue);mapMarker.AddToClassList("marker");mapArea.Add(mapMarker);
                Text("RUA DO CENTRO  /  EXPLORAÇÃO LIVRE","eyebrow",content);
                var center=new Button(()=>TriggerAction("reset")){text="VOLTAR AO PONTO INICIAL"};center.AddToClassList("primary");content.Add(center);
                break;
            case "settings":
                drawerTitle.text="Configurações";
                BuildSettingsMenu("camera");
                break;
            case "help":
                drawerTitle.text="Como jogar";
                Help("ESPAÇO","Pular (também disponível no botão de pulo)");
                Text("Explore no seu ritmo.","panel-description",content);
                Help("/arma","Equipar ou guardar o rifle de teste");
                Help("RIFLE","Botão direito: mirar · esquerdo/F: disparar · R: recarregar. No celular, use os botões da arma.");
                Help("W A S D","Andar pelo cenário");Help("SHIFT","Correr enquanto segura");Help("F / PUNHO","Um soco por clique");Help("MOUSE","Arrastar para girar a câmera");Help("RODA","Aproximar ou afastar");Help("C / R","Câmera / voltar ao início");Help("N","Alternar dia e noite");Help("TAB / M","Menu / mapa");Help("P / H","Perfil / ocultar interface");Help("ESC","Fechar um menu");
                break;
            default: ClosePanel();break;
        }
        ScrollSimpleMenu(panel);
    }

    void MenuItem(string icon,string title,string description,Action action)
    {
        var button=IconButton(icon,"menu-item",title,action,content);
        var text=new VisualElement{pickingMode=PickingMode.Ignore};button.Add(text);
        Text(title,"item-title",text);Text(description,"item-description",text);
    }
    void BuildPhoneHome()
    {
        var phone=Element("phone-screen",content);
        var wallpaper=Resources.Load<Texture2D>("RealCityHud/PhoneWallpaperNew");
        if(wallpaper)phone.style.backgroundImage=new StyleBackground(wallpaper);
        var status=Element("phone-status",phone);
        Text(DateTime.Now.ToString("HH:mm"),"phone-time",status);
        Text("●  4G  ▰","phone-signal",status);
        var island=Element("phone-island",phone);
        var hero=Element("phone-hero",phone);
        Text(DateTime.Now.ToString("dd"),"phone-day",hero);
        Text(DateTime.Now.ToString("dddd",new System.Globalization.CultureInfo("pt-BR")).ToUpperInvariant(),"phone-weekday",hero);
        Text("HORIZONTE","phone-brand",hero);
        var grid=Element("phone-app-grid",phone);
        PhoneApp(grid,"camera","Câmera",new Color(.18f,.2f,.23f),()=>StartCoroutine(TakePhonePhoto()));
        PhoneApp(grid,"user","Personagem",new Color(.54f,.28f,.86f),()=>OpenPanel("player"));
        PhoneApp(grid,"clock","Relógio",new Color(.95f,.4f,.2f),()=>PlayPhoneSound("alarm"));
        PhoneApp(grid,"cash","Carteira",new Color(.12f,.66f,.43f),()=>OpenPanel("wallet"));
        PhoneApp(grid,"settings","Ajustes",new Color(.42f,.46f,.52f),()=>OpenPanel("settings"));
        PhoneApp(grid,"missions","Missões",new Color(.91f,.3f,.29f),()=>OpenPanel("missions"));
        PhoneApp(grid,"phone","Toques",new Color(.18f,.72f,.38f),()=>PlayPhoneSound("ringtone"));
        var dock=Element("phone-dock",phone);
        PhoneDockApp(dock,"phone","Telefone",new Color(.16f,.76f,.34f),()=>PlayPhoneSound("ringtone"));
        PhoneDockApp(dock,"chat","Mensagens",new Color(.2f,.7f,.4f),()=>ShowToast("Mensagens estarão disponíveis em breve."));
        PhoneDockApp(dock,"camera","Câmera",new Color(.35f,.38f,.43f),()=>StartCoroutine(TakePhonePhoto()));
        var home=new Button(ClosePanel){name="phone-home",tooltip="Fechar celular"};home.AddToClassList("phone-home");phone.Add(home);
    }
    void PhoneApp(VisualElement parent,string icon,string title,Color color,Action action)
    {
        var app=new Button(action){tooltip=title};app.AddToClassList("phone-app");parent.Add(app);
        var badge=Element("phone-app-icon",app);badge.style.backgroundColor=color;badge.Add(HudPack.Image(icon));
        Text(title,"phone-app-name",app);
    }
    void PhoneDockApp(VisualElement parent,string icon,string title,Color color,Action action)
    {
        var app=new Button(action){tooltip=title};app.AddToClassList("phone-dock-app");parent.Add(app);
        app.style.backgroundColor=color;app.Add(HudPack.Image(icon));
    }
    void BuildHomeMenu()
    {
        var introduction=Element("home-intro",content);
        Text("ATALHOS DA CIDADE","home-kicker",introduction);
        Text("Central do jogador","home-headline",introduction);
        Text("Acesse seu personagem, serviços e ferramentas da cidade.","home-subtitle",introduction);
        var scroll=new ScrollView(ScrollViewMode.Vertical);scroll.AddToClassList("home-scroll");content.Add(scroll);
        var grid=Element("home-grid",scroll);
        HomeTile(grid,"bag","Inventário","ITENS",()=>OpenPanel("inventory"));
        HomeTile(grid,"run","Ações","PERSONAGEM",()=>OpenPanel("actions"));
        HomeTile(grid,"wallet","Carteira","SALDO",()=>OpenPanel("wallet"));
        HomeTile(grid,"list","Jogadores online","SALA",()=>OpenPanel("online-players"));
        HomeTile(grid,"help","Reportar ao admin","SUPORTE",()=>{ClosePanel();InGameChat.Instance?.OpenWithText("/report ");});
        HomeTile(grid,"store","Loja","EM BREVE",()=>ShowToast("Loja em breve."));
        if(AdminPermissions.CurrentCanUseTools)HomeTile(grid,"user","Administração","EQUIPE",()=>AdminPlayerPanel.Open(this));
        HomeTile(grid,"help","Como jogar","CONTROLES",()=>OpenPanel("help"));
        HomeTile(grid,"settings","Configurações","PREFERÊNCIAS",()=>OpenPanel("settings"));
        var footer=Element("home-footer",content);
        Text("HORIZONTE 02","home-footer-brand",footer);
        Text("EXPLORE  •  DESCUBRA  •  EVOLUA","home-footer-copy",footer);
    }
    void ShowCharacterCard(string uid,string name)
    {
        if(!string.IsNullOrEmpty(CurrentPanel))ClosePanel();
        selectedCharacterUid=uid;selectedCharacterName=name;
        selectedCharacterTransform=AccountSession.Instance?.State?.uid==uid?player.transform:
            MultiplayerWorld.Instance&&MultiplayerWorld.Instance.TryGetRemoteByUid(uid,out var remote)?remote:null;
        characterCardTitle.text=name+" ["+uid+"]";characterCardDetail.text="";
        characterCardDetail.style.display=DisplayStyle.None;
        characterCardExpiresAt=Time.unscaledTime+5f;
        characterCard.style.display=DisplayStyle.Flex;PositionCharacterCard();
    }
    void CloseCharacterCard(){if(characterCard!=null)characterCard.style.display=DisplayStyle.None;selectedCharacterTransform=null;}
    void PositionCharacterCard()
    {
        if(characterCard==null||selectedCharacterTransform==null||root?.panel==null)return;
        var camera=Camera.main;if(!camera)return;
        var screen=camera.WorldToScreenPoint(selectedCharacterTransform.position+Vector3.up*1.2f);
        if(screen.z<=0){CloseCharacterCard();return;}
        var point=RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(screen.x,Screen.height-screen.y));
        float width=root.resolvedStyle.width,height=root.resolvedStyle.height;
        float cardWidth=Application.isMobilePlatform?240f:176f;
        characterCard.style.left=Mathf.Clamp(point.x+22+cardWidth<width?point.x+22:point.x-cardWidth-8,8,Mathf.Max(8,width-cardWidth-8));
        characterCard.style.top=Mathf.Clamp(point.y-35,8,Mathf.Max(8,height-155));
    }
    void BuildCharacterCard()
    {
        characterCard=new VisualElement{name="character-quick-card"};characterCard.style.position=Position.Absolute;
        characterCard.style.width=Application.isMobilePlatform?240:176;characterCard.style.paddingLeft=9;characterCard.style.paddingRight=9;
        characterCard.style.paddingTop=9;characterCard.style.paddingBottom=9;
        characterCard.style.backgroundColor=new Color(.05f,.08f,.13f,.97f);characterCard.style.borderTopLeftRadius=10;
        characterCard.style.borderTopRightRadius=10;characterCard.style.borderBottomLeftRadius=10;characterCard.style.borderBottomRightRadius=10;
        root.Add(characterCard);Block(characterCard);
        characterCardTitle=new Label{name="character-quick-title"};characterCardTitle.style.color=Color.white;
        characterCardTitle.style.fontSize=12;characterCardTitle.style.unityFontStyleAndWeight=FontStyle.Bold;
        characterCardTitle.style.whiteSpace=WhiteSpace.Normal;characterCard.Add(characterCardTitle);
        characterCardDetail=new Label{name="character-quick-detail"};characterCardDetail.style.color=new Color(.78f,.85f,.9f);
        characterCardDetail.style.fontSize=11;characterCardDetail.style.whiteSpace=WhiteSpace.Normal;characterCardDetail.style.marginTop=6;characterCard.Add(characterCardDetail);
        var actions=new VisualElement();actions.style.flexDirection=FlexDirection.Column;actions.style.marginTop=7;characterCard.Add(actions);
        var identity=new Button(()=>{characterCardDetail.text="Nome: "+selectedCharacterName+"\nID: "+selectedCharacterUid;
            characterCardDetail.style.display=DisplayStyle.Flex;characterCardExpiresAt=Time.unscaledTime+5f;}){text="Identidade",name="character-identity"};
        var status=new Button(()=>{bool self=AccountSession.Instance?.State?.uid==selectedCharacterUid;
            characterCardDetail.text="Situação: "+(self||selectedCharacterTransform?"Online":"Desconectado")+
                (self?"\nNível: "+player.Level+" · Vida: "+Mathf.RoundToInt(player.Health):"");
            characterCardDetail.style.display=DisplayStyle.Flex;characterCardExpiresAt=Time.unscaledTime+5f;}){text="Status",name="character-status"};
        foreach(var button in new[]{identity,status}){button.style.height=Application.isMobilePlatform?48:31;button.style.marginTop=3;
            button.style.color=Color.white;button.style.backgroundColor=new Color(.13f,.2f,.3f);
            button.style.borderTopLeftRadius=5;button.style.borderTopRightRadius=5;
            button.style.borderBottomLeftRadius=5;button.style.borderBottomRightRadius=5;actions.Add(button);}
        characterCard.style.display=DisplayStyle.None;
    }
    void BuildNotesOverlay()
    {
        notesOverlay=new VisualElement{name="notes-overlay"};notesOverlay.style.position=Position.Absolute;
        notesOverlay.style.left=0;notesOverlay.style.right=0;notesOverlay.style.top=0;notesOverlay.style.bottom=0;
        notesOverlay.style.alignItems=Align.Center;notesOverlay.style.justifyContent=Justify.Center;
        notesOverlay.style.backgroundColor=new Color(.01f,.02f,.05f,.78f);root.Add(notesOverlay);Block(notesOverlay);
        notesOverlay.RegisterCallback<ClickEvent>(evt=>{if(evt.target==notesOverlay)notesOverlay.style.display=DisplayStyle.None;});
        var paper=new VisualElement{name="notes-paper"};paper.style.width=Length.Percent(78);paper.style.maxWidth=550;
        paper.style.height=Length.Percent(72);paper.style.maxHeight=540;paper.style.paddingLeft=18;paper.style.paddingRight=18;
        paper.style.paddingTop=15;paper.style.paddingBottom=15;paper.style.backgroundColor=new Color(.035f,.055f,.09f,.98f);
        paper.style.borderTopLeftRadius=12;paper.style.borderTopRightRadius=12;
        paper.style.borderBottomLeftRadius=12;paper.style.borderBottomRightRadius=12;
        paper.style.borderTopWidth=2;paper.style.borderTopColor=new Color(.97f,.73f,.21f);notesOverlay.Add(paper);
        var eyebrow=new Label("HORIZONTE  /  FERRAMENTAS");eyebrow.style.fontSize=10;
        eyebrow.style.color=new Color(.97f,.73f,.21f);eyebrow.style.unityFontStyleAndWeight=FontStyle.Bold;paper.Add(eyebrow);
        var title=new Label("Meu caderno");title.style.fontSize=22;title.style.color=Color.white;
        title.style.unityFontStyleAndWeight=FontStyle.Bold;title.style.marginTop=5;paper.Add(title);
        var hint=new Label("Escreva suas anotações. Elas ficam salvas neste aparelho.");hint.style.fontSize=11;
        hint.style.color=new Color(.7f,.77f,.82f);hint.style.marginTop=3;paper.Add(hint);
        notesField=new TextField{multiline=true,name="notes-text",maxLength=4000};notesField.style.flexGrow=1;
        notesField.style.marginTop=13;notesField.style.marginBottom=12;notesField.style.backgroundColor=new Color(.95f,.94f,.86f);
        notesField.style.color=new Color(.1f,.14f,.19f);notesField.style.borderTopLeftRadius=6;
        notesField.style.borderTopRightRadius=6;notesField.style.borderBottomLeftRadius=6;
        notesField.style.borderBottomRightRadius=6;paper.Add(notesField);
        notesField.AddToClassList("horizon-notes-input");
        var footer=new VisualElement();footer.style.flexDirection=FlexDirection.Row;footer.style.justifyContent=Justify.FlexEnd;
        footer.style.alignItems=Align.Center;paper.Add(footer);
        var close=new Button(()=>notesOverlay.style.display=DisplayStyle.None){text="FECHAR",name="notes-close"};footer.Add(close);
        var save=new Button(()=>{PlayerPrefs.SetString("chat.notes",notesField.value);PlayerPrefs.Save();notesOverlay.style.display=DisplayStyle.None;ShowToast("Anotações salvas.");})
            {text="SALVAR NOTAS",name="notes-save"};save.style.marginLeft=8;footer.Add(save);
        foreach(var button in new[]{close,save}){button.style.height=38;button.style.minWidth=96;
            button.style.paddingLeft=14;button.style.paddingRight=14;button.style.fontSize=12;
            button.style.unityFontStyleAndWeight=FontStyle.Bold;button.style.borderTopLeftRadius=5;
            button.style.borderTopRightRadius=5;button.style.borderBottomLeftRadius=5;button.style.borderBottomRightRadius=5;}
        close.style.backgroundColor=new Color(.13f,.19f,.27f);close.style.color=Color.white;
        save.style.backgroundColor=new Color(.97f,.73f,.21f);save.style.color=new Color(.07f,.1f,.15f);
        notesOverlay.style.display=DisplayStyle.None;
    }
    void OpenNotes()
    {
        if(!string.IsNullOrEmpty(CurrentPanel))ClosePanel();
        CloseCharacterCard();InGameChat.Instance?.Close();
        notesField.value=PlayerPrefs.GetString("chat.notes","");notesOverlay.style.display=DisplayStyle.Flex;
    }
    void HomeTile(VisualElement parent,string icon,string title,string category,Action action)
    {
        var tile=new Button(action){name="home-"+icon,tooltip=title};tile.AddToClassList("home-tile");parent.Add(tile);
        tile.RegisterCallback<GeometryChangedEvent>(_=>
        {
            float side=tile.resolvedStyle.width;
            if(side>0&&Mathf.Abs(tile.resolvedStyle.height-side)>1)tile.style.height=side;
        });
        var top=Element("home-tile-top",tile);
        var badge=Element("home-tile-badge",top);badge.Add(new HudIcon(icon));
        Text(category,"home-tile-category",top);
        Text(title,"home-tile-title",tile);
        Element("home-tile-accent",tile);
    }
    void BuildMissionPanel()
    {
        BuildMissionMenu("tutorial");
    }
    static string MissionStatus(MissionDefinition mission,string phase)
    {
        if(!mission.implemented)return mission.category=="normal"?"BLOQUEADA · conclua o tutorial e aguarde a mecânica":"EM PREPARAÇÃO · conteúdo cadastrado";
        if(mission.order==1)return phase=="available"?"DISPONÍVEL":phase=="borrowed"?"EM ANDAMENTO":"CONCLUÍDA";
        if(mission.order==2)return phase=="rg_pending"||phase=="rg_processing"?"EM ANDAMENTO":phase=="available"||phase=="borrowed"?"BLOQUEADA":"CONCLUÍDA";
        if(mission.order==3)return phase=="rg_issued"?"DISPONÍVEL":phase=="driving_school"?"EM ANDAMENTO":phase=="completed"?"CONCLUÍDA":"BLOQUEADA";
        return "EM PREPARAÇÃO";
    }
    Label Metric(string title,string value)
    {
        var row=Element("metric-row",content);Text(title,"muted",row);return Text(value,"metric-value",row);
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
        PlayerName=PlayerDisplayName.From(AccountSession.Instance.State);
        if(playerNameLabel!=null)playerNameLabel.text=PlayerName.ToUpperInvariant();
        if(avatarLabel!=null)avatarLabel.text=PlayerName.Substring(0,1).ToUpperInvariant();
        if(uidLabel!=null)uidLabel.text="ID: "+AccountSession.Instance.State.uid;
        RefreshAdminAccess();
        RefreshMicrophoneIndicator();
    }
    void RefreshMicrophoneIndicator()
    {
        if(microphoneButton==null)return;
        bool active=VoiceChat.Instance&&VoiceChat.Instance.Transmitting;
        if(idMicrophone!=null)
        {
            idMicrophone.style.display=active?DisplayStyle.Flex:DisplayStyle.None;
            idMicrophone.style.opacity=.5f+.5f*Mathf.Abs(Mathf.Sin(Time.unscaledTime*5));
        }
        microphoneButton.tooltip=active?"Microfone ligado — tocar para desligar":"Ligar microfone";
        microphoneButton.EnableInClassList("voice-active",active);
        if(microphoneRing!=null)
        {
            microphoneRing.style.display=active?DisplayStyle.Flex:DisplayStyle.None;
            microphoneRing.style.opacity=active ? .65f+.35f*Mathf.Sin(Time.unscaledTime*5f) : 0f;
        }
    }
    void RefreshAdminAccess()
    {
        if(adminButton==null)return;
        bool admin=AdminPermissions.CurrentCanUseTools;
        adminButton.style.display=admin?DisplayStyle.Flex:DisplayStyle.None;
    }
    public void ShowAccountMessage(string message)=>ShowToast(message);
    public void SetJailTime(long jailUntil,long serverTime)
    {
        jailDeadline=jailUntil>serverTime?Time.unscaledTime+(jailUntil-serverTime)/1000f:0;
    }
    public void ClosePanel()
    {
        if(notesOverlay!=null&&notesOverlay.resolvedStyle.display==DisplayStyle.Flex){notesOverlay.style.display=DisplayStyle.None;return;}
        CloseCharacterCard();
        if(actionEditor!=null){FinishActionEditor(false);return;}
        if(AdminPlayerPanel.Instance&&AdminPlayerPanel.Instance.IsOpen&&AdminPlayerPanel.Instance.Busy)return;
        root.Q("animation-test-wheel")?.RemoveFromHierarchy();
        if(CurrentPanel=="phone"&&phoneAudio)phoneAudio.Stop();
        AdminPlayerPanel.Instance?.Close();
        if(CurrentPanel=="map")CancelCityMapSelection();
        CurrentPanel=null;drawer.style.display=DisplayStyle.None;
        UpdateMenuChrome(null);
        drawer.RemoveFromClassList("menu-home");
        drawer.RemoveFromClassList("city-map-fullscreen");
        root.RemoveFromClassList("menu-home-open");
        root.RemoveFromClassList("hud-panel-open");
        root.Q("hud-compass").style.display=DisplayStyle.Flex;
        root.Q("hud-motion").style.display=DisplayStyle.None;
        root.Focus();PlayerPrefs.Save();
    }
    public void TriggerAction(string action)
    {
        switch(action)
        {
            case "attack":
                if(ShootingRange.Instance&&ShootingRange.Instance.IsActive)ShootingRange.Instance.RequestFire();
                else player.GetComponent<FighterCombo>()?.Trigger();
                break;
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

    string CoordinateSnapshot()
    {
        Vector3 position=player.transform.position;
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "X: {0:0.000}\nY: {1:0.000}\nZ: {2:0.000}\nROTAÇÃO Y: {3:0.0}°\nCÂMERA: {4:0.0}°",
            position.x,position.y,position.z,Mathf.Repeat(player.transform.eulerAngles.y,360),Mathf.Repeat(orbit.yaw,360));
    }
    public void ToggleCoordinates()
    {
        bool show=coordinatePanel.resolvedStyle.display==DisplayStyle.None;
        coordinatePanel.style.display=show?DisplayStyle.Flex:DisplayStyle.None;
        if(show)coordinates.text=CoordinateSnapshot();
        ShowToast(show?"Coordenadas ativadas · F3 para fechar":"Coordenadas ocultadas");
    }
    void CopyCoordinates()
    {
        string value=CoordinateSnapshot().Replace("\n","  |  ");
        GUIUtility.systemCopyBuffer=value;
        ShowToast("Coordenadas copiadas");
    }

    public bool BlocksPointer(Vector2 screen)
    {
        if(InGameChat.BlocksPointer(screen))return true;
        if(TutorialJourney.Instance&&TutorialJourney.Instance.BlocksPointer(screen))return true;
        if(!visible||root.panel==null)return false;
        Vector2 point=RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(screen.x,Screen.height-screen.y));
        foreach(var e in pointerBlockers)
            if(e.resolvedStyle.display!=DisplayStyle.None&&e.resolvedStyle.visibility==Visibility.Visible&&e.worldBound.Contains(point))return true;
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
        if(jailTimeLabel!=null)
        {
            int seconds=Mathf.CeilToInt(Mathf.Max(0,jailDeadline-Time.unscaledTime));
            jailTimeLabel.style.display=seconds>0?DisplayStyle.Flex:DisplayStyle.None;
            if(seconds>0)jailTimeLabel.text=$"PRESO  {seconds/60:00}:{seconds%60:00}";
        }
        if(characterCard!=null&&characterCard.resolvedStyle.display==DisplayStyle.Flex){
            if(Time.unscaledTime>=characterCardExpiresAt)CloseCharacterCard();else PositionCharacterCard();}
        if(Application.isMobilePlatform)
        {
            foreach(var touch in Input.touches)
            {
                if(touch.phase==TouchPhase.Began&&characterTapFinger<0)
                {characterTapFinger=touch.fingerId;characterTapStart=touch.position;}
                if(touch.fingerId!=characterTapFinger)continue;
                if(touch.phase==TouchPhase.Canceled)characterTapFinger=-1;
                if(touch.phase==TouchPhase.Ended)
                {
                    characterTapFinger=-1;
                    if(Vector2.Distance(characterTapStart,touch.position)<35f&&CanPickCharacter(touch.position))PickCharacter(touch.position);
                }
            }
        }
        else if(Input.GetMouseButtonUp(0)&&CanPickCharacter(Input.mousePosition))PickCharacter(Input.mousePosition);
        RefreshAdminAccess();
        RefreshMicrophoneIndicator();
        RefreshLatencyIndicator();
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
            if(Input.GetKeyDown(KeyCode.F3))ToggleCoordinates();
            if(Input.GetKeyDown(KeyCode.H))SetVisible(!visible);
            if(Input.GetKeyDown(KeyCode.C)&&!IsPanelOpen)TriggerAction("camera");
            if(Input.GetKeyDown(KeyCode.N)&&!IsPanelOpen)TriggerAction("day-night");
        }
        float distance=Vector3.ProjectOnPlane(player.transform.position-lastPosition,Vector3.up).magnitude;
        if(distance<1)DistanceTravelled+=distance;
        lastPosition=player.transform.position;TopSpeed=Mathf.Max(TopSpeed,player.CurrentSpeed);
        speedLabel.text=player.CurrentSpeed.ToString("0.0");
        motionLabel.text=player.IsJumping?"PULANDO":player.CurrentSpeed<.15f?"PARADO":player.CurrentSpeed<2.4f?"CAMINHANDO":"CORRENDO";
        speedFill.style.width=Length.Percent(Mathf.Clamp01(player.CurrentSpeed/(player.runSpeed*player.AdminSpeedMultiplier))*100);
        bool running=player.CurrentSpeed>2.4f||SprintRequested||Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift);
        sprint.EnableInClassList("active",running);sprintArtwork.style.opacity=running?1:.9f;
        levelLabel.text="NÍVEL "+player.Level+"  ·  "+Mathf.RoundToInt(player.Experience)+" XP";
        cashLabel.text="R$ "+(player.Cash+(TutorialJourney.Instance?TutorialJourney.Instance.DemoDonations:0))
            .ToString("N0",System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        xpFill.style.width=Length.Percent(Mathf.Clamp01(player.Experience/100f)*100);
        healthRing.SetValue(player.Health);healthLabel.text=Mathf.RoundToInt(player.Health).ToString();
        minimapHealth.style.width=Length.Percent(Mathf.Clamp(player.Health,0,100));
        minimapEnergy.style.width=Length.Percent(Mathf.Clamp(player.Energy,0,100));
        if(cloudSaveLabel!=null && AccountSession.Instance)cloudSaveLabel.text=AccountSession.Instance.SaveStatus;
        energyRing.SetValue(player.Hunger);energyLabel.text=Mathf.RoundToInt(player.Hunger).ToString();
        waterRing.SetValue(player.Hydration);waterLabel.text=Mathf.RoundToInt(player.Hydration).ToString();
        var pos=player.transform.position;
        if(coordinates!=null&&coordinatePanel.resolvedStyle.display!=DisplayStyle.None)coordinates.text=CoordinateSnapshot();
        int serverHour=MultiplayerWorld.Instance?MultiplayerWorld.Instance.ServerHour:-1;
        if(clock!=null)clock.text=serverHour>=0?serverHour.ToString("D2")+":00":DateTime.Now.ToString("HH:mm");
        hzCoinsLabel.text=AccountSession.Instance?.State?.hzCoins??"0";
        if(dateLabel!=null)dateLabel.text=DateTime.Now.ToString("dd/MM/yyyy");
        float yaw=Mathf.Repeat(orbit.yaw,360);
        string direction=yaw<22.5f||yaw>=337.5f?"N":yaw<67.5f?"NE":yaw<112.5f?"L":yaw<157.5f?"SE":yaw<202.5f?"S":yaw<247.5f?"SO":yaw<292.5f?"O":"NO";
        heading.text=direction+"  ·  "+Mathf.RoundToInt(yaw).ToString("D3")+"°";
        miniMarker.style.rotate=new Rotate(new Angle(0,AngleUnit.Degree));
        if(mapMarker!=null&&!cityMapAtlas)
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
    bool CanPickCharacter(Vector2 screen)=>!IsPanelOpen&&!StartupLoader.IsLoading&&!AccountSession.InputBlocked&&
        !BlocksPointer(screen)&&!InGameChat.BlocksPointer(screen)&&!VehicleHud.Driving;
    void PickCharacter(Vector2 screen)
    {
        var view=Camera.main;if(!view||!AccountSession.HasCharacter)return;
        float closest=Application.isMobilePlatform?Mathf.Max(85f,Screen.height*.075f):70f;string uid=null,name=null;
        var own=view.WorldToScreenPoint(player.transform.position+Vector3.up*1.1f);
        if(own.z>0){float distance=Vector2.Distance(screen,new Vector2(own.x,own.y));if(distance<closest){closest=distance;uid=AccountSession.Instance.State.uid;name=PlayerName;}}
        var peers=new List<RadarPlayer>();MultiplayerWorld.Instance?.GetRadarPlayers(peers);
        foreach(var peer in peers)
        {
            var point=view.WorldToScreenPoint(peer.position+Vector3.up*1.1f);
            if(point.z<=0)continue;
            float distance=Vector2.Distance(screen,new Vector2(point.x,point.y));
            if(distance>=closest)continue;
            closest=distance;uid=peer.uid;name=peer.name;
        }
        if(!string.IsNullOrEmpty(uid))ShowCharacterCard(uid,name);
        else CloseCharacterCard();
    }
    string SessionTime()
    {
        int seconds=Mathf.FloorToInt(Time.timeSinceLevelLoad);return (seconds/60).ToString("D2")+":"+(seconds%60).ToString("D2");
    }
    void CreateMapCameras()
    {
        cityMapAtlas=Resources.Load<Texture2D>("Maps/LosSantos");
        if(cityMapAtlas)return;
        miniTexture=new RenderTexture(256,256,24){name="HUD Minimap"};miniTexture.Create();
        mapTexture=new RenderTexture(512,512,24){name="HUD Area Map"};mapTexture.Create();
        miniCamera=MapCamera("Minimapa",miniTexture,14);
        mapCamera=MapCamera("Mapa · "+MapDisplayName,mapTexture,AreaMapHalfExtent);
        mapCamera.transform.position=new Vector3(0,MapCameraHeight,0);
    }
    Camera MapCamera(string name,RenderTexture texture,float size)
    {
        var camera=new GameObject(name).AddComponent<Camera>();camera.transform.SetParent(transform);
        camera.orthographic=true;camera.orthographicSize=size;camera.transform.rotation=Quaternion.Euler(90,0,0);
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.065f,.105f,.14f);
        camera.cullingMask=1;camera.farClipPlane=MapFarClip(size);camera.nearClipPlane=.1f;camera.targetTexture=texture;
        camera.allowHDR=false;camera.allowMSAA=false;camera.enabled=false;return camera;
    }
    float MapFarClip(float size)
    {
        var map=NeighborhoodMap.Active;
        float minimum=map?map.minY:-1;
        float maximum=map?map.maxY:8;
        return Mathf.Max(100,MapCameraHeight-minimum+10,maximum-minimum+20,size*2+20);
    }
    void LateUpdate()
    {
        if(!visible)return;
        if(cityMapAtlas){RefreshCityMap();return;}
        if(Time.unscaledTime<nextMapUpdate)return;
        nextMapUpdate=Time.unscaledTime+.1f;
        RefreshMaps();
    }
    public void RefreshMaps()
    {
        if(cityMapAtlas){RefreshCityMap();return;}
        if(!miniCamera||miniImage==null)return;
        if(!miniTexture.IsCreated())miniTexture.Create();
        var p=player.transform.position;
        float playerYaw=Mathf.Repeat(player.transform.eulerAngles.y,360);
        miniCamera.transform.position=new Vector3(p.x,MapCameraHeight,p.z);
        miniCamera.transform.rotation=Quaternion.Euler(90,playerYaw,0);
        miniCamera.farClipPlane=MapFarClip(miniCamera.orthographicSize);
        if(minimapNorth!=null)
        {
            float radians=playerYaw*Mathf.Deg2Rad;
            float size=miniViewport.resolvedStyle.width;
            minimapNorth.style.left=size*.5f-12-Mathf.Sin(radians)*(size*.5f-17);
            minimapNorth.style.top=size*.5f-7-Mathf.Cos(radians)*(size*.5f-17);
        }
        miniCamera.Render();miniImage.MarkDirtyRepaint();
        if(CurrentPanel=="map")
        {
            mapCamera.transform.position=new Vector3(0,MapCameraHeight,0);mapCamera.orthographicSize=AreaMapHalfExtent;
            mapCamera.farClipPlane=MapFarClip(AreaMapHalfExtent);
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
