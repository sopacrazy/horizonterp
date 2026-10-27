using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

public sealed class TutorialJourney : MonoBehaviour
{
    static readonly string[] TutorialTitles={"BOAS-VINDAS","APRENDENDO OS COMANDOS","COMEÇANDO A VIDA NA CIDADE","O CARRO EMPRESTADO","RUMO À PREFEITURA"};
    static readonly string[] TutorialTexts={
        "Salve, meu cria! Bem-vindo ao Horizonte Brasil RP! Chega mais que eu vou te passar a visão de como funciona a cidade.",
        "Primeiro aprende a se movimentar, olhar pros lados e interagir com a rapaziada. Testa aí pra tu pegar o jeito!",
        "Aqui tu pode trampar, fazer tua grana, comprar tuas paradas e conquistar teu espaço. Mas tudo começa pelo primeiro corre!",
        "Pra tu não ficar andando a pé, vou te fortalecer com minha nave. Mas ó, é emprestada, hein! Cuida direitinho!",
        "Boa, cria! Agora entra na nave, segue o GPS e cola lá na Prefeitura. Troca uma ideia com o pessoal de lá pra tirar teu RG. Partiu!"
    };
    static readonly string[] TutorialButtons={"PRÓXIMO","ENTENDI","PRÓXIMO","INICIAR MISSÃO","FECHAR"};
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
    public GameObject GpsArrow=>gpsObject;
    ThirdPersonPlayer player;
    ArrivalConfig config;
    ArrivalState mission;
    GameObject giver,cityHall,uiObject,gpsObject;
    MissionVehicle car;
    UIDocument document;
    VisualElement quest,prompt,modal;
    Label title,detail,reward,promptTitle,dialogTitle,dialogText,dialogError;
    Button promptButton,primary,secondary,retry;
    AudioSource narration;
    string dialogAction;
    bool modalOpen,busy,previousCombat,rehearsal;
    int waypoint,tutorialPage;
    bool enteredLoanCar;
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
        rehearsal=reply.mission.phase=="completed";
        Apply(new ArrivalState{phase="available",config=reply.mission.config});
    }
    internal IEnumerator ReloadForTest(){yield return Load();}
    void Apply(ArrivalState value)
    {
        mission=value;config=value.config??config;retry.style.display=DisplayStyle.None;
        if(Phase=="borrowed"&&!car){car=MissionVehicle.Create(config.car);waypoint=0;BuildGps();}
        if(Phase!="borrowed"){RemoveCar();if(gpsObject)Destroy(gpsObject);}
        quest.style.display=Phase=="completed"?DisplayStyle.None:DisplayStyle.Flex;
        RefreshObjective();
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
            if(car&&car.Occupied){action="exit";caption="F  SAIR DO CARRO";enteredLoanCar=true;}
            else if(Vector3.Distance(player.transform.position,config.cityHall)<2.8f){action="finish";caption="E  FALAR COM ATENDENTE";}
            else if(car&&Vector3.Distance(player.transform.position,car.transform.position)<3.8f){action="enter";caption="E  ENTRAR NO CARRO";}
            UpdateGps();
            RefreshObjective();
            if(Time.unscaledTime>=nextRefresh&&DateTime.TryParse(mission.loanExpiresAt,null,System.Globalization.DateTimeStyles.RoundtripKind,out var expires)&&expires.ToUniversalTime()<=DateTime.UtcNow)
            {nextRefresh=Time.unscaledTime+10;StartCoroutine(Load());}
        }
        else if(Phase=="available"&&!modalOpen)RefreshObjective();
        prompt.style.display=available&&action!=null?DisplayStyle.Flex:DisplayStyle.None;
        prompt.EnableInClassList("arrival-exit-prompt",action=="exit");
        promptTitle.text=action=="finish"?"PREFEITURA":action=="talk"?"MORADOR DA CIDADE":"EMPRÉSTIMO TEMPORÁRIO";promptButton.text=caption;promptButton.userData=action;
        if(available&&action!=null&&Input.GetKeyDown(KeyCode.E)&&action!="exit")Interact(action);
    }
    internal void Interact(string action)
    {
        if(busy||modalOpen||Time.unscaledTime<nextInteraction)return;
        if(action=="talk"&&Phase=="available"&&Near(config.giver)){tutorialPage=0;ShowTutorialPage();}
        else if(action=="enter"&&car&&Phase=="borrowed"){if(car.Enter(player))nextInteraction=Time.unscaledTime+.4f;}
        else if(action=="exit"&&car){if(!car.Exit())GameHud.Instance?.ShowToast("Pare em um local livre para sair do carro.");}
        else if(action=="finish"&&Phase=="borrowed"&&Near(config.cityHall)&&!(car&&car.Occupied))
            ShowDialog("PREFEITURA · ATENDIMENTO","Bem-vindo à Prefeitura! Você chegou ao local certo. Vamos encerrar sua chegada à cidade e preparar o próximo atendimento. O carro emprestado será devolvido ao morador.","CONTINUAR","finish");
    }
    bool Near(Vector3 point)=>Vector3.Distance(player.transform.position,point)<2.8f;
    internal IEnumerator SubmitMission(string action)
    {
        if(busy)yield break;busy=true;primary.SetEnabled(false);dialogError.text="";
        ApiReply reply=null;
        if(rehearsal)
        {
            var local=action=="start"
                ?new ArrivalState{phase="borrowed",loanId="teste-local",loanExpiresAt=DateTime.UtcNow.AddHours(2).ToString("O"),config=config}
                :new ArrivalState{phase="completed",config=config};
            reply=new ApiReply{mission=local,state=AccountSession.Instance.State};yield return null;
        }
        else
        {
            yield return AccountSession.Instance.Save();
            yield return AccountSession.Instance.ArrivalRequest(action,r=>reply=r);
        }
        busy=false;primary.SetEnabled(true);
        if(reply?.mission==null){dialogError.text=AccountSession.Instance.LastError??"Não foi possível confirmar. Tente novamente.";yield break;}
        Apply(reply.mission);
        if(action=="start")
        {
            tutorialPage=4;ShowTutorialPage();
            GameHud.Instance?.ShowToast("Missão iniciada. Use o carro do morador; o empréstimo não é uma compra.");
        }
        else HideDialog();
        if(action=="finish")ShowDialog("MISSÃO CONCLUÍDA",rehearsal
            ?"Teste concluído. O carro foi devolvido ao NPC e a recompensa não foi repetida, pois esta conta já concluiu a missão oficial."
            :"Você recebeu "+config.rewardXp+" XP e R$ "+(config.rewardCashCents/100)+". O carro foi devolvido ao NPC. O próximo atendimento na Prefeitura será a continuação da sua jornada.","CONTINUAR","close");
    }
    void CreateUI()
    {
        uiObject=new GameObject("Missão de chegada · interface");uiObject.transform.SetParent(transform,false);
        document=uiObject.AddComponent<UIDocument>();document.panelSettings=Resources.Load<PanelSettings>("HudPanel");document.sortingOrder=90;
        var root=document.rootVisualElement;root.styleSheets.Add(Resources.Load<StyleSheet>("ArrivalStyles"));
        quest=Box("arrival-quest",root);quest.name="arrival-quest";title=Text("MISSÃO 01 · PRIMEIRO CORRE  1/6","arrival-title",quest);title.name="arrival-objective-title";detail=Text("Carregando...","arrival-detail",quest);detail.name="arrival-objective-detail";reward=Text("RECOMPENSA  ·  100 XP  +  R$ 150","arrival-reward",quest);
        retry=Button("TENTAR NOVAMENTE",()=>StartCoroutine(Load()),quest);retry.style.display=DisplayStyle.None;
        prompt=Box("arrival-prompt",root);prompt.name="arrival-prompt";promptTitle=Text("","arrival-title",prompt);promptButton=Button("",()=>Interact(promptButton.userData as string),prompt);promptButton.name="arrival-interact";prompt.style.display=DisplayStyle.None;
        modal=Box("arrival-shade",root);modal.name="arrival-dialog";var card=Box("arrival-dialog-card",modal);var content=Box("arrival-dialog-content",card);
        Text("HORIZONTE BRASIL RP  ·  MISSÃO 01","arrival-kicker",content);dialogTitle=Text("","arrival-dialog-title",content);dialogTitle.name="arrival-dialog-title";dialogText=Text("","arrival-dialog-text",content);dialogText.name="arrival-dialog-text";
        dialogError=Text("","arrival-error",content);dialogError.name="arrival-dialog-error";var actions=Box("arrival-dialog-actions",content);
        secondary=Button("FECHAR",()=>{if(!busy)HideDialog();},actions);secondary.name="arrival-cancel";
        primary=Button("",()=>{if(dialogAction=="tutorial-next")AdvanceTutorial();else if(dialogAction=="close")HideDialog();else StartCoroutine(SubmitMission(dialogAction));},actions);primary.name="arrival-start";
        modal.style.display=DisplayStyle.None;
    }
    void ShowTutorialPage()
    {
        tutorialPage=Mathf.Clamp(tutorialPage,0,TutorialTitles.Length-1);
        string action=tutorialPage<3?"tutorial-next":tutorialPage==3?"start":"close";
        ShowDialog(TutorialTitles[tutorialPage],TutorialTexts[tutorialPage],TutorialButtons[tutorialPage],action);
        secondary.style.display=tutorialPage<4?DisplayStyle.Flex:DisplayStyle.None;
        if(tutorialPage>0&&tutorialPage<3){title.text="MISSÃO 01 · PRIMEIRO CORRE  2/6";detail.text="Conversar com o NPC e aprender os comandos";}
        if(tutorialPage==3){title.text="MISSÃO 01 · PRIMEIRO CORRE  3/6";detail.text="Clicar em INICIAR MISSÃO";}
    }
    void AdvanceTutorial(){if(busy)return;tutorialPage++;ShowTutorialPage();}
    void ShowDialog(string caption,string body,string button,string action)
    {
        if(!modalOpen)previousCombat=player.CombatLocked;
        modalOpen=true;dialogAction=action;dialogTitle.text=caption;dialogText.text=body;primary.text=button;dialogError.text="";modal.style.display=DisplayStyle.Flex;secondary.style.display=action=="start"||action=="tutorial-next"?DisplayStyle.Flex:DisplayStyle.None;player.CombatLocked=true;
        var clip=action=="tutorial-next"&&tutorialPage==0?Resources.Load<AudioClip>("Arrival/WelcomeVoice"):null;
        narration.Stop();if(clip){narration.clip=clip;narration.Play();}
    }
    void RefreshObjective()
    {
        if(title==null||detail==null||reward==null||config==null||player==null)return;
        reward.text="RECOMPENSA  ·  "+config.rewardXp+" XP  +  R$ "+(config.rewardCashCents/100);
        if(Phase=="available")
        {
            title.text="MISSÃO 01 · PRIMEIRO CORRE  1/6";
            detail.text="Aproximar-se do NPC inicial  ·  "+Mathf.CeilToInt(Vector3.Distance(player.transform.position,config.giver))+" m";
            return;
        }
        if(Phase!="borrowed")return;
        float cityDistance=Vector3.Distance(player.transform.position,config.cityHall);
        if(cityDistance<12&&(car==null||!car.Occupied))
        {
            title.text="MISSÃO 01 · PRIMEIRO CORRE  6/6";detail.text="Conversar com o NPC da Prefeitura";
        }
        else if(enteredLoanCar)
        {
            title.text="MISSÃO 01 · PRIMEIRO CORRE  5/6";
            detail.text="Seguir o GPS até a Prefeitura  ·  "+Mathf.CeilToInt(cityDistance)+" m"+(car&&car.Occupied?"  ·  "+Mathf.RoundToInt(Mathf.Abs(car.Speed)*3.6f)+" km/h":"");
        }
        else
        {
            title.text="MISSÃO 01 · PRIMEIRO CORRE  4/6";detail.text="Entrar no carro emprestado";
        }
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
        if(gpsObject)Destroy(gpsObject);
        gpsObject=new GameObject("GPS · seta de direção");gpsObject.layer=2;
        var filter=gpsObject.AddComponent<MeshFilter>();filter.sharedMesh=ArrowMesh();
        var renderer=gpsObject.AddComponent<MeshRenderer>();renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        renderer.sharedMaterial=new Material(Shader.Find("Sprites/Default")){color=new Color(1f,.72f,.08f,1f)};
    }
    void UpdateGps()
    {
        if(!gpsObject||config.route==null||config.route.Length==0)return;
        Vector3 origin=player.transform.position;
        Vector3 target=car&&!enteredLoanCar&&!car.Occupied?car.transform.position:config.route[Mathf.Clamp(waypoint,0,config.route.Length-1)];
        if(enteredLoanCar||car&&car.Occupied)
        {
            while(waypoint<config.route.Length-1&&Vector3.Distance(origin,config.route[waypoint])<5)waypoint++;
            target=config.route[waypoint];
        }
        Vector3 direction=Vector3.ProjectOnPlane(target-origin,Vector3.up);
        gpsObject.SetActive(direction.sqrMagnitude>.25f);
        if(direction.sqrMagnitude<=.25f)return;
        gpsObject.transform.position=origin+Vector3.up*(car&&car.Occupied?2.65f:2.35f+Mathf.Sin(Time.time*3f)*.08f);
        gpsObject.transform.rotation=Quaternion.LookRotation(direction.normalized,Vector3.up);
        float pulse=1.12f+Mathf.Sin(Time.time*4f)*.09f;gpsObject.transform.localScale=new Vector3(pulse,pulse,pulse);
    }
    static Mesh ArrowMesh()
    {
        var mesh=new Mesh{name="Seta GPS flutuante"};
        mesh.vertices=new[]{new Vector3(-.16f,0,-.58f),new Vector3(.16f,0,-.58f),new Vector3(.16f,0,.08f),new Vector3(-.16f,0,.08f),new Vector3(-.48f,0,.06f),new Vector3(.48f,0,.06f),new Vector3(0,0,.82f)};
        mesh.triangles=new[]{0,2,1,0,3,2,4,6,5};mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
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
