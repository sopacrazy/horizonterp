using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class TutorialJourney : MonoBehaviour
{
    public const string WelcomeText="Fala, cria! Seja bem-vindo ao Horizonte Brasil RP! Vi que você tá chegando agora na cidade, então vou te dar uma força nesse começo. Vou emprestar meu carro pessoal pra você conseguir se locomover e conhecer a cidade. Só preciso que cuide bem dele, fechou? Bora começar seguindo até a Prefeitura!";
    public static TutorialJourney Instance {get;private set;}
    internal static bool AllowVerification;
    public bool ModalOpen=>modalOpen;
    public bool HasRG=>false;
    public bool HasCNH=>false;
    public long DemoCoins=>0;
    public long DemoDonations=>0;
    public string Phase=>mission?.phase??"loading";
    public MissionVehicle LoanCar=>car;
    public ArrivalConfig Config=>config;
    public UIDocument Document=>document;
    ThirdPersonPlayer player;
    ArrivalConfig config;
    ArrivalState mission;
    GameObject giver,cityHall,uiObject,gpsObject;
    MissionVehicle car;
    UIDocument document;
    VisualElement quest,prompt,modal;
    Label title,detail,promptTitle,dialogTitle,dialogText,dialogError;
    Button promptButton,primary,retry;
    AudioSource narration;
    LineRenderer gps;
    string dialogAction;
    bool modalOpen,busy,previousCombat;
    int waypoint;
    float nextInteraction,nextRefresh;
    public static void Create(ThirdPersonPlayer target)
    {
        if(!target||!NeighborhoodMap.Active||!NeighborhoodMap.Active.isOfficialCity||target.GetComponent<TutorialJourney>()||
            (AccountSession.VerificationTransport!=null&&!AllowVerification))return;
        target.gameObject.AddComponent<TutorialJourney>();
    }
    void Awake(){Instance=this;player=GetComponent<ThirdPersonPlayer>();narration=gameObject.AddComponent<AudioSource>();narration.playOnAwake=false;narration.spatialBlend=0;}
    IEnumerator Start()
    {
        while(player&&(StartupLoader.IsLoading||!AccountSession.HasCharacter||AccountSession.InputBlocked))yield return null;
        if(!player)yield break;
        config=JsonUtility.FromJson<ArrivalConfig>(Resources.Load<TextAsset>("Arrival/Config").text);
        CreateUI();giver=Npc("Morador · boas-vindas",config.giver,new Color(.3f,.85f,1));cityHall=Npc("Prefeitura · atendimento",config.cityHall,new Color(1,.82f,.3f));
        yield return Load();
    }
    IEnumerator Load()
    {
        if(busy)yield break;busy=true;ApiReply reply=null;
        yield return AccountSession.Instance.ArrivalRequest(null,r=>reply=r);busy=false;
        if(reply?.mission==null){title.text="MISSÃO DE CHEGADA";detail.text="Não foi possível carregar a missão. Tente novamente.";retry.style.display=DisplayStyle.Flex;yield break;}
        Apply(reply.mission);
    }
    void Apply(ArrivalState value)
    {
        mission=value;config=value.config??config;retry.style.display=DisplayStyle.None;
        if(Phase=="borrowed"&&!car){car=MissionVehicle.Create(config.car);waypoint=0;BuildGps();}
        if(Phase!="borrowed"){RemoveCar();if(gpsObject)Destroy(gpsObject);}
        quest.style.display=Phase=="completed"?DisplayStyle.None:DisplayStyle.Flex;
        title.text=Phase=="borrowed"?"PRIMEIRA MISSÃO  ·  PREFEITURA":"BEM-VINDO AO HORIZONTE";
        detail.text=Phase=="borrowed"?"Carro emprestado · siga o GPS":"Aproxime-se do morador e converse para começar.";
        nextRefresh=Time.unscaledTime+5;
    }
    void Update()
    {
        if(!document||!player||mission==null)return;
        bool available=!busy&&!modalOpen&&!player.IsTeleporting&&!player.IsJumping&&!InGameChat.Editing&&!StartupLoader.IsLoading&&!AccountSession.InputBlocked&&!(GameHud.Instance&&GameHud.Instance.IsPanelOpen)&&Time.unscaledTime>=nextInteraction;
        string action=null;string caption="";
        if(Phase=="available"&&Vector3.Distance(player.transform.position,config.giver)<2.8f){action="talk";caption="E  CONVERSAR";}
        if(Phase=="borrowed")
        {
            if(car&&car.Occupied){action="exit";caption="F  SAIR DO CARRO";}
            else if(Vector3.Distance(player.transform.position,config.cityHall)<2.8f){action="finish";caption="E  FALAR COM ATENDENTE";}
            else if(car&&Vector3.Distance(player.transform.position,car.transform.position)<3.8f){action="enter";caption="E  ENTRAR NO CARRO";}
            UpdateGps();
            detail.text=car&&car.Occupied?"Siga o GPS · Prefeitura a "+Mathf.CeilToInt(Vector3.Distance(player.transform.position,config.cityHall))+" m  ·  "+Mathf.RoundToInt(Mathf.Abs(car.Speed)*3.6f)+" km/h":"Pegue o carro emprestado e vá até a Prefeitura.";
            if(Time.unscaledTime>=nextRefresh&&DateTime.TryParse(mission.loanExpiresAt,null,System.Globalization.DateTimeStyles.RoundtripKind,out var expires)&&expires.ToUniversalTime()<=DateTime.UtcNow)
            {nextRefresh=Time.unscaledTime+10;StartCoroutine(Load());}
        }
        else if(Phase=="available")detail.text="Converse com o morador · "+Mathf.CeilToInt(Vector3.Distance(player.transform.position,config.giver))+" m";
        prompt.style.display=available&&action!=null?DisplayStyle.Flex:DisplayStyle.None;
        promptTitle.text=action=="finish"?"PREFEITURA":action=="talk"?"MORADOR DA CIDADE":"EMPRÉSTIMO TEMPORÁRIO";promptButton.text=caption;promptButton.userData=action;
        if(available&&action!=null&&Input.GetKeyDown(KeyCode.E)&&action!="exit")Interact(action);
    }
    internal void Interact(string action)
    {
        if(busy||modalOpen||Time.unscaledTime<nextInteraction)return;
        if(action=="talk"&&Phase=="available"&&Near(config.giver))ShowDialog("BOAS-VINDAS À CIDADE",WelcomeText,"INICIAR MISSÃO","start");
        else if(action=="enter"&&car&&Phase=="borrowed"){if(car.Enter(player))nextInteraction=Time.unscaledTime+.4f;}
        else if(action=="exit"&&car){if(!car.Exit())GameHud.Instance?.ShowToast("Pare em um local livre para sair do carro.");}
        else if(action=="finish"&&Phase=="borrowed"&&Near(config.cityHall)&&!(car&&car.Occupied))
            ShowDialog("PREFEITURA · ATENDIMENTO","Bem-vindo à Prefeitura! Você chegou ao local certo. Vamos encerrar sua chegada à cidade e preparar o próximo atendimento. O carro emprestado será devolvido ao morador.","CONTINUAR","finish");
    }
    bool Near(Vector3 point)=>Vector3.Distance(player.transform.position,point)<2.8f;
    internal IEnumerator SubmitMission(string action)
    {
        if(busy)yield break;busy=true;primary.SetEnabled(false);dialogError.text="Salvando...";
        yield return AccountSession.Instance.Save();ApiReply reply=null;
        yield return AccountSession.Instance.ArrivalRequest(action,r=>reply=r);busy=false;primary.SetEnabled(true);
        if(reply?.mission==null){dialogError.text=AccountSession.Instance.LastError??"Não foi possível confirmar. Tente novamente.";yield break;}
        Apply(reply.mission);HideDialog();
        if(action=="start")GameHud.Instance?.ShowToast("Missão iniciada. Use o carro do morador; o empréstimo não é uma compra.");
        if(action=="finish")ShowDialog("MISSÃO CONCLUÍDA","Você recebeu "+config.rewardXp+" XP e R$ "+(config.rewardCashCents/100)+". O carro foi devolvido ao NPC. O próximo atendimento na Prefeitura será a continuação da sua jornada.","CONTINUAR","close");
    }
    void CreateUI()
    {
        uiObject=new GameObject("Missão de chegada · interface");uiObject.transform.SetParent(transform,false);
        document=uiObject.AddComponent<UIDocument>();document.panelSettings=Resources.Load<PanelSettings>("HudPanel");document.sortingOrder=90;
        var root=document.rootVisualElement;root.styleSheets.Add(Resources.Load<StyleSheet>("ArrivalStyles"));
        quest=Box("arrival-quest",root);quest.name="arrival-quest";title=Text("MISSÃO DE CHEGADA","arrival-title",quest);detail=Text("Carregando...","arrival-detail",quest);
        retry=Button("TENTAR NOVAMENTE",()=>StartCoroutine(Load()),quest);retry.style.display=DisplayStyle.None;
        prompt=Box("arrival-prompt",root);promptTitle=Text("","arrival-title",prompt);promptButton=Button("",()=>Interact(promptButton.userData as string),prompt);promptButton.name="arrival-interact";prompt.style.display=DisplayStyle.None;
        modal=Box("arrival-shade",root);modal.name="arrival-dialog";var card=Box("arrival-dialog-card",modal);
        Text("HORIZONTE BRASIL RP  ·  SUA CHEGADA","arrival-kicker",card);dialogTitle=Text("","arrival-dialog-title",card);dialogText=Text("","arrival-dialog-text",card);
        dialogError=Text("","arrival-error",card);
        primary=Button("",()=>{if(dialogAction=="close")HideDialog();else StartCoroutine(SubmitMission(dialogAction));},card);primary.name="arrival-start";
        Button("AGORA NÃO",()=>{if(!busy)HideDialog();},card).name="arrival-cancel";modal.style.display=DisplayStyle.None;
    }
    void ShowDialog(string caption,string body,string button,string action)
    {
        modalOpen=true;dialogAction=action;dialogTitle.text=caption;dialogText.text=body;primary.text=button;dialogError.text="";modal.style.display=DisplayStyle.Flex;previousCombat=player.CombatLocked;player.CombatLocked=true;
        var clip=action=="start"?Resources.Load<AudioClip>("Arrival/WelcomeVoice"):null;
        narration.Stop();if(clip){narration.clip=clip;narration.Play();}
    }
    void HideDialog(){modalOpen=false;modal.style.display=DisplayStyle.None;narration.Stop();if(player)player.CombatLocked=previousCombat;nextInteraction=Time.unscaledTime+.35f;}
    GameObject Npc(string caption,Vector3 position,Color color)
    {
        var npc=new GameObject(caption);npc.layer=2;npc.transform.position=position;
        var visual=Instantiate(player.animator.gameObject,npc.transform,false);visual.name="Personagem NPC";
        foreach(var component in visual.GetComponentsInChildren<MonoBehaviour>())component.enabled=false;
        foreach(var collider in visual.GetComponentsInChildren<Collider>())collider.enabled=false;
        var animator=visual.GetComponent<Animator>();if(animator){animator.applyRootMotion=false;animator.SetFloat("Speed",0);animator.SetBool("Jumping",false);if(animator.layerCount>1)animator.SetLayerWeight(1,0);}
        var shape=npc.AddComponent<CapsuleCollider>();shape.center=Vector3.up*.9f;shape.height=1.8f;shape.radius=.3f;
        var label=new GameObject("Identificação NPC");label.layer=2;label.transform.SetParent(npc.transform,false);label.transform.localPosition=Vector3.up*2.2f;
        var text=label.AddComponent<TextMesh>();text.text=caption;text.anchor=TextAnchor.MiddleCenter;text.fontSize=40;text.characterSize=.035f;text.color=color;text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.GetComponent<MeshRenderer>().sharedMaterial=text.font.material;
        label.AddComponent<ArrivalBillboard>();return npc;
    }
    void BuildGps()
    {
        if(gpsObject)Destroy(gpsObject);gpsObject=new GameObject("GPS · rota até a Prefeitura");gpsObject.layer=2;gps=gpsObject.AddComponent<LineRenderer>();gps.useWorldSpace=true;gps.material=new Material(Shader.Find("Sprites/Default"));gps.startColor=gps.endColor=new Color(.12f,.74f,1,.85f);gps.widthMultiplier=.16f;
    }
    void UpdateGps()
    {
        if(!gps||config.route==null||config.route.Length==0)return;
        while(waypoint<config.route.Length-1&&Vector3.Distance(player.transform.position,config.route[waypoint])<5)waypoint++;
        gps.positionCount=config.route.Length-waypoint+1;gps.SetPosition(0,player.transform.position+Vector3.up*.15f);
        for(int i=waypoint;i<config.route.Length;i++)gps.SetPosition(i-waypoint+1,config.route[i]+Vector3.up*.15f);
    }
    void RemoveCar(){if(car){car.Exit(true);Destroy(car.gameObject);car=null;}}
    static VisualElement Box(string css,VisualElement parent){var value=new VisualElement();value.AddToClassList(css);parent.Add(value);return value;}
    static Label Text(string value,string css,VisualElement parent){var label=new Label(value){enableRichText=false};label.AddToClassList(css);parent.Add(label);return label;}
    static Button Button(string value,Action callback,VisualElement parent){var button=new Button(callback){text=value};button.AddToClassList("arrival-button");parent.Add(button);return button;}
    public bool BlocksPointer(Vector2 screen)
    {
        if(!document||document.rootVisualElement.panel==null)return false;if(modalOpen)return true;
        var point=RuntimePanelUtils.ScreenToPanel(document.rootVisualElement.panel,new Vector2(screen.x,Screen.height-screen.y));
        return (quest.resolvedStyle.display!=DisplayStyle.None&&quest.worldBound.Contains(point))||(prompt.resolvedStyle.display!=DisplayStyle.None&&prompt.worldBound.Contains(point));
    }
    void OnDestroy(){RemoveCar();if(giver)Destroy(giver);if(cityHall)Destroy(cityHall);if(gpsObject)Destroy(gpsObject);if(modalOpen&&player)player.CombatLocked=previousCombat;if(Instance==this)Instance=null;}
}
public sealed class ArrivalBillboard:MonoBehaviour{void LateUpdate(){if(Camera.main)transform.rotation=Camera.main.transform.rotation;}}
