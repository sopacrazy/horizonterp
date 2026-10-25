using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

// Explicit offline verification: all account requests are intercepted in memory.
public sealed class CharacterFlowVerification : MonoBehaviour
{
    static bool LandingOnly=>Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-landing")>=0;
    static string Folder=>Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-hud2")>=0?@"C:\SistemaAdr\gta\tools\hub2-review":Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-walk")>=0?@"C:\SistemaAdr\gta\tools\walk-runtime-review":LandingOnly?@"C:\SistemaAdr\gta\tools\landing-review":@"C:\SistemaAdr\gta\tools\character-review";
    readonly List<string> checks = new List<string>();
    readonly List<string> errors = new List<string>();
    readonly List<RequestRecord> requests = new List<RequestRecord>();
    AccountSession account;
    VisualElement root;
    ApiPlayerState savedState;
    int registerAttempts, characterAttempts, loginAttempts, recoveryReads;
    bool recoveryScenario, finished, hadEmailPreference;
    string originalEmailPreference;

    [Serializable]
    sealed class RequestRecord
    {
        public string method, path;
        public bool authenticated;
        public string[] keys;
    }

    [Serializable]
    sealed class Report
    {
        public bool passed;
        public string capturedUtc;
        public List<string> checks;
        public List<string> errors;
        public List<RequestRecord> localRequests;
        public int remoteRequests;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if ((Array.IndexOf(Environment.GetCommandLineArgs(), "--verify-character-flow") >= 0||LandingOnly) &&
            !FindAnyObjectByType<CharacterFlowVerification>())
            new GameObject("Offline Character Flow Verification").AddComponent<CharacterFlowVerification>();
    }

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        Application.runInBackground = true;
        Directory.CreateDirectory(Folder);
        hadEmailPreference = PlayerPrefs.HasKey("account.email");
        originalEmailPreference = PlayerPrefs.GetString("account.email", "");
        AccountSession.VerificationTransport = FakeRequest;
        Application.logMessageReceived += OnLog;
        Application.quitting += OnQuitting;
    }

    void OnLog(string message, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors.Add(message + "\n" + trace);
    }

    IEnumerator Start()
    {
        var pending = new Stack<IEnumerator>();
        pending.Push(LandingOnly?VerifyLanding():Verify());
        while (pending.Count > 0 && !finished)
        {
            object instruction = null;
            bool moved = false;
            Exception failure = null;
            try
            {
                moved = pending.Peek().MoveNext();
                if (moved) instruction = pending.Peek().Current;
            }
            catch (Exception exception) { failure = exception; }
            if (failure != null) { errors.Add(failure.ToString()); Finish(); yield break; }
            if (!moved) { pending.Pop(); continue; }
            if (instruction is IEnumerator nested) pending.Push(nested);
            else yield return instruction;
        }
        if (!finished) Finish();
    }

    IEnumerator Verify()
    {
        Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
        yield return WaitForLanding();
        Check(AccountSession.VerificationTransport != null && requests.Count == 0,
            "Transporte local instalado antes de qualquer autenticação");

        OpenAuth(true, "offline-flow@example.invalid");
        Submit("auth-submit");
        yield return new WaitForSecondsRealtime(.1f);
        Check(AuthBusy() && AccountSession.InputBlocked && !account.ReadyToEnterCity,
            "Cadastro em andamento bloqueia formulário e entrada na cidade");
        yield return WaitUntil(() => Button("auth-submit").enabledInHierarchy, 5, "Resposta de erro local do cadastro");
        Check(!AccountSession.Authenticated && HasError("auth-message") && registerAttempts == 1,
            "Falha simulada de cadastro permite tentar novamente sem autenticar");
        SetCredentials("offline-flow@example.invalid");
        Submit("auth-submit");
        yield return WaitForCreationLobby();
        Check(AccountSession.Authenticated && !AccountSession.HasCharacter && account.State == null &&
              !account.ReadyToEnterCity && account.AuthVisible && !CityEntryLoading.Active,
            "Token com needsCharacter e state nulo abre criação sem carregar a cidade");
        Check(AccountSession.InputBlocked && StartupLoader.IsLoading && !FindAnyObjectByType<ThirdPersonPlayer>(),
            "Conta sem personagem continua bloqueada antes da ativação da cena");
        CheckMusic("durante a criação");
        yield return VerifyCharacterValidation();
        yield return VerifyResponsiveLobby(false);
        yield return VerifyGarageTransition();

        SetCharacterNames(" Ana-María ", " D’Ávila ");
        Submit("character-female");
        Submit("character-save");
        yield return new WaitForSecondsRealtime(.1f);
        int pendingCount = requests.Count;
        Check(CharacterBusy() && AccountSession.InputBlocked && !account.ReadyToEnterCity && !CityEntryLoading.Active,
            "Salvar bloqueia campos e escolhas sem antecipar o carregamento");
        Dispatch(Button("character-save"));
        Dispatch(Button("character-male"));
        yield return null;
        Check(requests.Count == pendingCount && account.CharacterGender == "female",
            "Envio e seleção repetidos durante busy não duplicam requisição nem alteram personagem");
        yield return WaitUntil(() => Button("character-save").enabledInHierarchy, 5, "Resposta de erro local da criação");
        Check(HasError("character-message") && !AccountSession.HasCharacter && !account.ReadyToEnterCity &&
              account.CharacterFirstName == "Ana-María" && account.CharacterLastName == "D'Ávila" &&
              account.CharacterGender == "female", "Erro de criação preserva identidade e escolha para repetir");
        yield return Capture("creation-retry");
        Submit("character-save");
        yield return VerifyCityEntry("created-character");
        Check(account.State.firstName == "Ana-María" && account.State.lastName == "D'Ávila" &&
              account.State.gender == "female" && characterAttempts == 2,
            "Criação confirmada usa identidade e gênero enviados no payload");
        yield return VerifyJump();
        yield return VerifyActionSpacing();
        yield return VerifyChatInterface();

        yield return LogoutAndReturnToLanding();
        OpenAuth(false, "offline-flow@example.invalid");
        Submit("auth-submit");
        yield return WaitUntil(() => Visible(root.Q("character-existing")), 5, "Preview de personagem existente");
        Check(AccountSession.HasCharacter && !account.ReadyToEnterCity && account.AuthVisible &&
              !CityEntryLoading.Active && !Visible(root.Q("character-create")),
            "Login com personagem salvo exige confirmação na apresentação existente");
        Check(root.Q<Label>("character-existing-name")?.text == "Ana-María D'Ávila" &&
              root.Q<Image>("character-existing-image")?.image == Resources.Load<Texture2D>("Characters/Feminina"),
            "Preview existente apresenta nome completo e imagem feminina salvos");
        int beforeExistingPreview = requests.Count;
        yield return VerifyResponsiveLobby(true);
        Check(!account.ReadyToEnterCity && !CityEntryLoading.Active && AccountSession.InputBlocked &&
              requests.Count == beforeExistingPreview, "Preview existente permanece aberto sem entrada automática ou novos requests");
        CheckMusic("aguardando confirmação do personagem existente");
        Submit("character-enter");
        yield return VerifyCityEntry("existing-character");
        Check(loginAttempts == 1 && characterAttempts == 2,
            "Entrar com personagem existente não cria outro personagem");

        // Simulate a successful commit whose original response was lost.
        yield return LogoutAndReturnToLanding();
        recoveryScenario = true;
        OpenAuth(true, "offline-recovery@example.invalid");
        Submit("auth-submit");
        yield return WaitForCreationLobby();
        SetCharacterNames("João", "D'Ávila");
        Submit("character-male");
        Submit("character-save");
        yield return WaitUntil(() => recoveryReads == 1, 5, "Recuperação local de CHARACTER_EXISTS");
        Check(CharacterBusy() && !account.ReadyToEnterCity,
            "CHARACTER_EXISTS mantém bloqueio enquanto consulta o personagem já salvo");
        yield return VerifyCityEntry("recovered-character");
        Check(account.State.gender == "male" && account.State.firstName == "João" &&
              characterAttempts == 3 && recoveryReads == 1,
            "CHARACTER_EXISTS recupera estado via GET /v1/player sem repetir a criação");

        account.Logout();
        yield return WaitUntil(() => !AccountSession.Authenticated, 8, "Encerramento da sessão local");
        Check(requests.Exists(r => r.path == "/v1/auth/register") && requests.Exists(r => r.path == "/v1/auth/login") &&
              requests.Exists(r => r.path == "/v1/characters") && requests.Exists(r => r.path == "/v1/player"),
            "Sequência local cobre conta, criação, retorno e recuperação");
        Check(AccountSession.VerificationTransport != null && !AccountSession.Authenticated,
            "Verificação inteira usou transporte em memória e encerrou a conta simulada");
        Check(errors.Count == 0, "Execução sem erros, exceções ou asserts do Unity");
    }

    IEnumerator VerifyLanding()
    {
        Screen.SetResolution(1280,720,FullScreenMode.Windowed);
        savedState=MakeState(new CharacterBody {firstName="Ana-María",lastName="D'Ávila",gender="female"});
        yield return WaitForLanding();
        // This focused test exercises physics, not native window focus/navigation.
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        typeof(AccountSession).GetMethod("DisplayEmailForm",flags).Invoke(account,new object[]{false});
        SetCredentials("offline-landing@example.invalid");
        yield return (IEnumerator)typeof(AccountSession).GetMethod("Submit",flags).Invoke(account,null);
        Check(AccountSession.HasCharacter,"Fixture local autenticada para teste de aterrissagem");
        Check(requests.Exists(request=>request.path=="/v1/auth/login"),"Login de aterrissagem foi interceptado em memória");
        typeof(AccountSession).GetMethod("EnterCity",flags).Invoke(account,null);
        yield return WaitUntil(()=>!StartupLoader.IsLoading&&!CityEntryLoading.Active&&FindAnyObjectByType<ThirdPersonPlayer>(),50,"Cidade pronta para teste de aterrissagem");
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-hud2")>=0)yield return VerifyHud2();
        else if(Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-walk")>=0)yield return VerifyWalkPlayback();
        else yield return VerifyJump();
        Check(AccountSession.VerificationTransport!=null,"Teste de aterrissagem não usa a VPS");
        account.Logout();yield return WaitUntil(()=>!AccountSession.Authenticated,8,"Fixture de aterrissagem encerrada");
    }

    IEnumerator VerifyWalkPlayback()
    {
        var player=FindAnyObjectByType<ThirdPersonPlayer>();
        var bones=player.animator.GetComponentsInChildren<Transform>().ToDictionary(t=>t.name,t=>t);
        player.DemoMode=true;player.DemoRun=false;player.DemoInput=Vector2.zero;
        yield return new WaitForSecondsRealtime(.3f);
        float standingHip=player.transform.InverseTransformPoint(bones[" Pelvis"].position).y;
        player.DemoInput=Vector2.up;
        yield return new WaitForSecondsRealtime(.5f);
        bool authored=player.animator.GetCurrentAnimatorClipInfo(0).Any(info=>info.clip.name=="Andar_WalkingOriginal");
        Check(player.animator.GetCurrentAnimatorStateInfo(0).IsName("Locomocao")&&player.CurrentSpeed>1.5f,
            "Caminhada é reproduzida no Animator enquanto o controlador anda");
        float minGap=float.MaxValue,maxPlantedVelocity=0,minHip=float.MaxValue,maxHip=float.MinValue,minRoot=float.MaxValue,maxRoot=float.MinValue,sumHip=0;int plantedSamples=0,poseSamples=0;
        var previous=new[]{bones[" L Foot"].position,bones[" R Foot"].position};
        var previousLocalHeight=new[]{float.MaxValue,float.MaxValue};
        float previousTime=Time.time;
        for(float elapsed=0;elapsed<2.8f;elapsed+=Time.deltaTime)
        {
            yield return new WaitForEndOfFrame();
            float dt=Time.time-previousTime;
            var state=player.animator.GetCurrentAnimatorStateInfo(0);
            var left=player.transform.InverseTransformPoint(bones[" L Foot"].position);
            var right=player.transform.InverseTransformPoint(bones[" R Foot"].position);
            float hip=player.transform.InverseTransformPoint(bones[" Pelvis"].position).y;
            minHip=Mathf.Min(minHip,hip);maxHip=Mathf.Max(maxHip,hip);
            sumHip+=hip;poseSamples++;
            minRoot=Mathf.Min(minRoot,player.transform.position.y);maxRoot=Mathf.Max(maxRoot,player.transform.position.y);
            minGap=Mathf.Min(minGap,right.x-left.x);
            for(int side=0;side<2;side++)
            {
                var point=bones[side==0?" L Foot":" R Foot"].position;
                float phase=Mathf.Repeat(state.normalizedTime+side*.5f,1);
                float localHeight=player.animator.transform.InverseTransformPoint(point).y;
                bool support=authored?localHeight<.09f&&previousLocalHeight[side]<.09f:phase>.1f&&phase<.42f;
                if(dt>0&&support)
                {maxPlantedVelocity=Mathf.Max(maxPlantedVelocity,Vector3.Distance(point,previous[side])/dt);plantedSamples++;}
                previous[side]=point;
                previousLocalHeight[side]=localHeight;
            }
            previousTime=Time.time;
        }
        Check(minGap>(authored?.03f:.18f),"Pés mantêm separação durante a caminhada real (distância mínima="+minGap.ToString("F3")+"m)");
        Check(maxHip-minHip<(authored?.085f:.022f),"Movimento vertical acompanha o perfil da animação selecionada (medida="+(maxHip-minHip).ToString("F4")+"m)");
        if(authored)Check(standingHip-sumHip/Mathf.Max(1,poseSamples)<.06f,"Altura natural de caminhada preservada, sem agachamento artificial (diferença média="+(standingHip-sumHip/Mathf.Max(1,poseSamples)).ToString("F3")+"m)");
        Check(maxRoot-minRoot<.015f&&!player.IsJumping,"Controlador permanece no chão sem pequenos saltos durante a caminhada");
        Check(plantedSamples>20&&maxPlantedVelocity<(authored?.85f:.35f),
            "Pé de apoio acompanha o chão com movimento do controlador (amostras="+plantedSamples+", velocidade máxima="+maxPlantedVelocity.ToString("F3")+"m/s)");
        player.DemoInput=Vector2.zero;yield return new WaitForSecondsRealtime(.4f);
        Check(player.CurrentSpeed<.1f,"Soltar o controle retorna ao estado parado");
        player.DemoInput=Vector2.up;player.DemoRun=true;yield return new WaitForSecondsRealtime(.5f);
        Check(player.CurrentSpeed>4.2f,"Corrida mantém a animação e a velocidade existentes");
        player.DemoInput=Vector2.zero;player.DemoRun=false;player.ResetPosition();
    }

    IEnumerator VerifyHud2()
    {
        var hud=GameHud.Instance;var ui=hud.Root;var player=hud.Player;
        ui.Q<Label>("multiplayer-status").text="TESTE LOCAL";
        Check(ui.ClassListContains("hub2-hud")&&HudAtlas.Get("h2_fist").texture==Resources.Load<Texture2D>("Hud2Atlas"),"HUD usa os recortes do HUB2 original");
        Check(ui.Q(className:"profile")==null&&ui.Q(className:"atlas-banners")==null,"Cartão de perfil e banners antigos removidos");
        long cash=player.Cash;float health=player.Health;player.Cash=30092;player.Health=75;yield return null;
        Check(ui.Q<Label>(className:"h2-cash").text=="30.092"&&ui.Q<Label>("stat-heart-value").text=="75","Dinheiro e vida são dados reais, sem números fixos da arte");
        player.Cash=cash;player.Health=health;
        Check(ui.Q<Label>("game-version").text=="v"+Application.version,"Número exibido acompanha a versão instalada");
        var identity=AccountSession.Instance.State;string layoutRole=identity.accountRole;int layoutLevel=identity.adminLevel;
        identity.accountRole="admin";identity.adminLevel=1;yield return null;
        foreach(string name in new[]{"nav-menu","nav-session","nav-phone"})
        {
            var button=ui.Q<Button>(name);button.Focus();Dispatch(button);yield return null;
            Check(hud.IsPanelOpen,"Atalho do HUB2 abre painel: "+name);hud.ClosePanel();
        }
        yield return VerifyActionSpacing();
        identity.accountRole=layoutRole;identity.adminLevel=layoutLevel;yield return null;
        var controls=FindAnyObjectByType<MobileControls>();
        var left=new Vector2(Screen.width*.36f,Screen.height*.2f);var right=new Vector2(Screen.width*.66f,Screen.height*.4f);
        controls.ProcessTouch(10,left,Vector2.zero,TouchPhase.Began);controls.ProcessTouch(10,left+Vector2.up*200,Vector2.up*200,TouchPhase.Moved);
        controls.ProcessTouch(11,right,Vector2.zero,TouchPhase.Began);controls.ProcessTouch(11,right+Vector2.right*30,Vector2.right*30,TouchPhase.Moved);
        Check(MobileControls.Movement.y>.99f&&MobileControls.Look.x>0,"Área livre permite joystick e câmera com dois dedos");
        controls.ProcessTouch(10,left,Vector2.zero,TouchPhase.Ended);controls.ProcessTouch(11,right,Vector2.zero,TouchPhase.Ended);
        var run=ui.Q<Button>("action-sprint");run.Focus();Dispatch(run);yield return null;Check(hud.SprintRequested,"Botão HUB2 ativa corrida");
        run.Focus();Dispatch(run);yield return null;Check(!hud.SprintRequested,"Botão HUB2 desativa corrida");
        yield return VerifyChatInterface();
        yield return Capture("hud2-final");
    }

    IEnumerator VerifyChatInterface()
    {
        var hud=GameHud.Instance;var chat=InGameChat.Instance;
        var player=FindAnyObjectByType<ThirdPersonPlayer>();var ui=hud.Root;
        Check(chat&&ui.Q("chat-area")!=null&&ui.Q("chat-open")==null,"Histórico clicável substitui o botão Conversar");
        var area=ui.Q("chat-area");area.Focus();
        using(var evt=NavigationSubmitEvent.GetPooled())area.SendEvent(evt);
        yield return null;
        Check(InGameChat.Editing&&Visible(ui.Q("chat-composer")),"Clicar no histórico abre o campo de mensagem");
        var chatBackground=ui.Q("chat-area").resolvedStyle.backgroundColor;
        Check(chatBackground.a>.25f&&chatBackground.a<.6f&&chatBackground.r<.05f,"Chat aberto tem fundo preto leve e transparente");
        Check(ui.Q("action-day-night")==null,"Botão de dia e noite removido do HUD");
        player.DemoInput=Vector2.up;Vector3 before=player.transform.position;
        player.RequestJump();yield return new WaitForSecondsRealtime(.3f);
        Check(Vector3.ProjectOnPlane(player.transform.position-before,Vector3.up).magnitude<.05f&&!player.IsJumping,
            "Digitar bloqueia movimento e pulo do personagem");
        int requestsBefore=requests.Count;
        var send=ui.Q<Button>("chat-send");send.Focus();Dispatch(send);yield return null;
        Check(ui.Q<Label>("chat-hint").text.Contains("Escreva")&&requests.Count==requestsBefore,
            "Chat vazio não envia requisição");
        ui.Q<TextField>("chat-input").value="Mensagem preservada";
        chat.Close();chat.Open();yield return null;
        Check(ui.Q<TextField>("chat-input").value=="Mensagem preservada","Fechar e reabrir preserva o rascunho");
        chat.Receive(new ChatMessage {id=1,name="Adriano",text="Olá! Quem está na cidade?"});
        chat.Receive(new ChatMessage {id=2,name="Teste",text="<b>Texto literal</b>"});
        var labels=ui.Q("chat-feed").Query<Label>().ToList();
        Check(labels.TrueForAll(label=>!label.enableRichText),"Texto recebido não interpreta marcação de interface");
        chat.SetKeyboardInset(Screen.height*.42f);
        yield return null;
        var conversation=ui.Q("chat-feed").worldBound;
        var composer=ui.Q("chat-composer").worldBound;
        Check(Mathf.Abs(composer.xMin-conversation.xMin)<1&&Mathf.Abs(composer.width-conversation.width)<1&&
            composer.yMin>=conversation.yMax&&composer.yMin-conversation.yMax<8,
            "Campo tem a largura do chat e fica imediatamente abaixo da conversa");
        yield return Capture("chat-composer");
        player.DemoInput=Vector2.zero;ui.Q<TextField>("chat-input").value="";chat.Close();
        Check(!InGameChat.Editing,"Fechar libera os controles novamente");
        var state=AccountSession.Instance.State;string oldRole=state.accountRole;int oldAdmin=state.adminLevel;
        state.accountRole="admin";state.adminLevel=1;
        yield return null;
        var adminButton=ui.Q<Button>("action-admin");
        Check(Visible(adminButton)&&adminButton.Q<HudIcon>().Kind=="admin","Conta administrativa vê o botão dourado de admin");
        string panelBefore=GameHud.Instance.CurrentPanel;adminButton.Focus();Dispatch(adminButton);yield return null;
        Check(GameHud.Instance.CurrentPanel==panelBefore,"Botão de admin permanece sem função");
        chat.VerificationSend=DelayedChatReply;
        chat.Open();ui.Q<TextField>("chat-input").value="Mensagem instantânea";chat.Send();
        Check(!chat.IsOpen&&!InGameChat.Editing&&ui.Q<TextField>("chat-input").value=="",
            "Enviar fecha o campo e libera controles antes da confirmação do servidor");
        Check(ui.Q("chat-feed").Query<Label>().ToList().Exists(label=>label.text.Contains("Mensagem instantânea")),
            "Mensagem aparece localmente no mesmo frame com servidor atrasado");
        yield return new WaitForSecondsRealtime(1.5f);
        Check(ui.Q("chat-feed").Query<Label>().ToList().FindAll(label=>label.text.Contains("Mensagem instantânea")).Count==1,
            "Confirmação e sincronização não duplicam o eco local");
        Check(ui.Q("chat-feed").Query<Label>(className:"chat-author-admin").ToList().Exists(label=>label.text=="[ADMIN] "+state.name+":"),
            "Mensagem de admin mantém tag e nome destacados após confirmação");
        chat.Receive(new ChatMessage{id=51,messageId="verified-admin",uid="902",name="Admin Remoto",isAdmin=true,text="<b>Texto admin literal</b>"});
        chat.Receive(new ChatMessage{id=52,messageId="normal-player",uid="903",name="Jogador Normal",isAdmin=false,text="Mensagem comum"});
        yield return null;
        var author=ui.Q("chat-feed").Query<Label>(className:"chat-author-admin").ToList().Find(label=>label.text=="[ADMIN] Admin Remoto:");
        Check(author!=null&&author.resolvedStyle.color.r>.8f&&author.resolvedStyle.color.b<.6f,"Nome de admin recebido de outro jogador aparece dourado");
        Check(ui.Q("chat-feed").Query<Label>().ToList().Exists(label=>label.text=="<b>Texto admin literal</b>"&&!label.enableRichText),"Texto de admin continua literal, sem executar marcação");
        Check(ui.Q("chat-feed").Query<Label>().ToList().Exists(label=>label.text=="Jogador Normal: Mensagem comum"&&!label.ClassListContains("chat-author-admin")),"Usuário normal não recebe tag administrativa");
        chat.Open();yield return null;yield return Capture("admin-chat");chat.Close();
        chat.Open();ui.Q<TextField>("chat-input").value="/goto 2";chat.Send();
        Check(ui.Q("chat-feed").Query<Label>().ToList().Exists(label=>label.text=="/goto 2"&&label.ClassListContains("chat-admin")),
            "Comando administrativo aparece imediatamente em verde neon");
        yield return new WaitForSecondsRealtime(1.5f);
        Check(ui.Q("chat-feed").Query<Label>().ToList().Exists(label=>label.text.StartsWith("[ADMIN]")&&label.ClassListContains("chat-admin")),
            "Resposta administrativa mantém estilo verde neon");
        chat.Open();ui.Q<TextField>("chat-input").value="Falha simulada";chat.Send();
        yield return new WaitForSecondsRealtime(1.5f);
        Check(ui.Q("chat-feed").Query<Label>().ToList().Exists(label=>label.text.Contains("Falha simulada")&&label.ClassListContains("chat-error")),
            "Envio rejeitado é marcado como falha e não como mensagem entregue");
        chat.VerificationSend=null;state.accountRole=oldRole;state.adminLevel=oldAdmin;
        yield return null;Check(!Visible(adminButton),"Botão de admin fica oculto para conta normal");
        yield return Capture("chat-feed");
    }

    IEnumerator DelayedChatReply(string id,string text,Action<MultiplayerReply> done)
    {
        yield return new WaitForSecondsRealtime(.5f);
        if(text.StartsWith("/")){done(new MultiplayerReply {command=new AdminCommandReply {name="goto",status="not_implemented",message="/goto: função ainda não disponível."}});yield break;}
        if(text=="Falha simulada"){done(new MultiplayerReply {code="INVALID_MESSAGE",error="Rejeição simulada."});yield break;}
        var message=new ChatMessage {id=50,messageId=id,uid=AccountSession.Instance.State.uid,name=AccountSession.Instance.State.name,
            isAdmin=AccountSession.Instance.State.accountRole=="admin"&&AccountSession.Instance.State.adminLevel>0,text=text};
        InGameChat.Instance.Receive(message);done(new MultiplayerReply {message=message});
        InGameChat.Instance.Receive(message);
    }

    IEnumerator VerifyActionSpacing()
    {
        foreach(var size in new[]{new Vector2Int(960,540),new Vector2Int(1280,720),new Vector2Int(1600,720)})
        {
            Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(.7f);
            Check(Screen.width==size.x&&Screen.height==size.y,"Resolução aplicada ao HUD: "+size.x+"x"+size.y);
            var hud=GameHud.Instance.Root;
            var bounds=hud.worldBound;
            var actions=hud.Q<Button>("action-list").worldBound;
            var sprint=hud.Q<Button>("action-sprint").worldBound;
            var camera=hud.Q<Button>("action-camera").worldBound;
            var jump=hud.Q<Button>("action-jump").worldBound;
            var buttons=new[]{actions,sprint,camera,jump};
            bool separated=true;for(int i=0;i<buttons.Length;i++)for(int j=i+1;j<buttons.Length;j++)separated&=!buttons[i].Overlaps(buttons[j]);
            Check(actions.yMax+10<=sprint.yMin&&separated,
                "Ações, corrida, câmera e pulo mantêm espaçamento em "+size.x+"x"+size.y);
            Check(buttons.All(rect=>rect.xMin>=bounds.xMin&&rect.yMin>=bounds.yMin&&rect.xMax<=bounds.xMax&&rect.yMax<=bounds.yMax),
                "Controles laterais ficam dentro da tela em "+size.x+"x"+size.y);
            yield return Capture("hud-spacing-"+size.x+"x"+size.y);
        }
        Screen.SetResolution(1280,720,FullScreenMode.Windowed);
        yield return new WaitForSecondsRealtime(.3f);
    }

    IEnumerator VerifyJump()
    {
        var player=FindAnyObjectByType<ThirdPersonPlayer>();
        var hud=GameHud.Instance;
        player.DemoMode=true;player.DemoInput=Vector2.zero;player.DemoRun=false;
        yield return WaitUntil(()=>player.IsGrounded,4,"Personagem apoiado antes do pulo");
        var button=hud.Root.Q<Button>("action-jump");
        Check(button!=null&&button.enabledInHierarchy&&button.worldBound.width>30,"Botão de pulo disponível no HUD");
        hud.OpenPanel("help");player.RequestJump();yield return null;
        Check(!player.IsJumping,"Menu aberto bloqueia o pulo");hud.ClosePanel();
        float startY=player.transform.position.y,maxY=startY;
        button.Focus();Dispatch(button);
        yield return WaitUntil(()=>player.IsJumping,3,"Botão do HUD solicita o pulo");
        yield return WaitUntil(()=>!player.IsGrounded&&player.transform.position.y>startY+.1f,3,"Impulso tira o personagem do chão");
        Check(player.IsJumping&&!player.IsGrounded&&player.transform.position.y>startY+.1f,
            "Botão do HUD inicia animação e eleva o CharacterController");
        yield return WaitUntil(()=>player.animator.GetCurrentAnimatorStateInfo(0).IsName("Jump"),2,"Animator reproduz Jump enviado");
        player.RequestJump(); // Repeated request in flight must not add another impulse.
        for(float elapsed=0;elapsed<.2f;elapsed+=Time.deltaTime)
        {maxY=Mathf.Max(maxY,player.transform.position.y);yield return null;}
        if(!LandingOnly)yield return Capture("jump-apex");
        float landingDeadline=Time.realtimeSinceStartup+4;
        while(player.IsJumping&&Time.realtimeSinceStartup<landingDeadline)
        {maxY=Mathf.Max(maxY,player.transform.position.y);yield return null;}
        yield return WaitUntil(()=>!player.IsJumping&&player.IsGrounded,4,"Pulo termina apoiado no chão");
        Check(maxY-startY>.4f&&maxY-startY<1.1f&&Mathf.Abs(player.transform.position.y-startY)<.12f,
            "Pulo tem altura controlada, sem salto duplo, e aterrissa na altura original (altura="+(maxY-startY).ToString("F3")+", diferença final="+(player.transform.position.y-startY).ToString("F3")+")");
        yield return new WaitForSecondsRealtime(.25f);
        Check(player.animator.GetCurrentAnimatorStateInfo(0).IsName("Locomocao"),"Aterrissagem retorna à locomoção sem repetir o clip");
        player.DemoInput=Vector2.up;player.DemoRun=true;
        Vector3 start=player.transform.position;player.RequestJump();
        yield return new WaitForSecondsRealtime(.37f);
        Check(player.IsJumping&&Vector3.ProjectOnPlane(player.transform.position-start,Vector3.up).magnitude>.8f,
            "Pulo durante a corrida mantém deslocamento horizontal");
        yield return WaitUntil(()=>player.IsGrounded,3,"Corrida aterrissa sem aguardar o fim do clip");
        yield return null;
        Check(!player.IsJumping&&player.CurrentSpeed>4&& !player.animator.GetBool("Jumping"),
            "Primeiro contato com o chão encerra o pulo mantendo a velocidade de corrida");
        yield return new WaitForSecondsRealtime(.1f);
        Check(player.animator.GetCurrentAnimatorStateInfo(0).IsName("Locomocao"),
            "Corrida volta à locomoção em até 100 ms após aterrissar");
        player.ResetPosition();player.DemoInput=Vector2.up;player.DemoRun=false;
        yield return WaitUntil(()=>player.IsGrounded,3,"Preparação do pulo caminhando");
        float walkingStartY=player.transform.position.y;
        player.RequestJump();yield return WaitUntil(()=>player.IsJumping&&!player.IsGrounded&&player.transform.position.y>walkingStartY+.15f,3,"Caminhada sai do chão com impulso do pulo");
        yield return WaitUntil(()=>player.IsGrounded,3,"Caminhada aterrissa");yield return null;
        Check(!player.IsJumping&&player.CurrentSpeed>1.4f&&player.CurrentSpeed<2,
            "Aterrissagem caminhando retoma a caminhada sem travar na pose de pulo (velocidade="+player.CurrentSpeed.ToString("F3")+", pulando="+player.IsJumping+")");
        player.DemoInput=Vector2.zero;player.DemoRun=false;
        player.ResetPosition();yield return new WaitForSecondsRealtime(.3f);
        Check(!player.IsJumping&&player.IsGrounded,"Respawn cancela o pulo e deixa o personagem apoiado");
    }

    IEnumerator VerifyGarageTransition()
    {
        int before=requests.Count;
        var male=root.Q<Image>("character-male-preview");
        var female=root.Q<Image>("character-female-preview");
        Check(root.Q<Image>("character-garage")?.image==Resources.Load<Texture2D>("Characters/garagem"),
            "Criação usa a arte garagem como fundo");
        SetCharacterNames("Lucas","Silva");
        Check(root.Q<Label>("character-preview-name").text=="LUCAS SILVA",
            "Nome completo acompanha a edição no preview");
        Submit("character-male");yield return new WaitForSecondsRealtime(.6f);
        Submit("character-female");yield return new WaitForSecondsRealtime(.09f);
        Check(male.resolvedStyle.translate.x<0&&female.resolvedStyle.translate.x>0,
            "Troca de sexo desliza o atual para a esquerda e traz o próximo pela direita");
        yield return Capture("garage-transition");
        yield return new WaitForSecondsRealtime(.6f);
        Check(female.resolvedStyle.opacity>.99f&&male.resolvedStyle.opacity<.01f&&
            Mathf.Abs(female.resolvedStyle.translate.x)<1,"Transição termina com apenas a personagem selecionada visível");
        yield return Capture("garage-female");
        Submit("character-male");yield return new WaitForSecondsRealtime(.08f);
        Submit("character-female");yield return new WaitForSecondsRealtime(.08f);
        Submit("character-male");yield return new WaitForSecondsRealtime(.6f);
        Check(male.resolvedStyle.opacity>.99f&&female.resolvedStyle.opacity<.01f&&
            Mathf.Abs(male.resolvedStyle.translate.x)<1&&account.CharacterGender=="male"&&requests.Count==before,
            "Trocas rápidas terminam na última seleção sem requests ou previews sobrepostos");
        yield return Capture("garage-male");
    }

    IEnumerator VerifyCharacterValidation()
    {
        int before = requests.Count;
        SetCharacterNames("", "");
        Submit("character-save");
        yield return null;
        Check(HasError("character-first-name-error") && HasError("character-last-name-error") &&
              HasError("character-gender-error") && requests.Count == before,
            "Nome, sobrenome e escolha vazios são rejeitados sem requisição");
        yield return Capture("creation-required-fields");
        Submit("character-female");
        foreach (string[] invalid in new[]
        {
            new[] { "A", "B" }, new[] { "Ana7", "Silva_" },
            new[] { new string('A', 25), new string('B', 25) },
            new[] { "-Ana", "Silva-" }, new[] { "Ana--Maria", "D''Ávila" }
        })
        {
            SetCharacterNames(invalid[0], invalid[1]);
            Submit("character-save");
            yield return null;
            Check(HasError("character-first-name-error") && HasError("character-last-name-error") && requests.Count == before,
                "Identidade inválida é barrada localmente: " + invalid[0] + " / " + invalid[1]);
        }
        SetCharacterNames("𐐀𐐨", "");
        Submit("character-save");
        yield return null;
        Check(!HasError("character-first-name-error") && HasError("character-last-name-error") && requests.Count == before,
            "Duas letras Unicode suplementares são aceitas no nome; sobrenome vazio mantém envio bloqueado");
        SetCharacterNames("A\u0301na", "");
        Submit("character-save");
        yield return null;
        Check(!HasError("character-first-name-error") && account.CharacterFirstName == "Ána" && requests.Count == before,
            "Nome com acento combinado é normalizado para NFC sem envio parcial");
        SetCharacterNames("Ana-María", "D'Ávila");
    }

    IEnumerator VerifyResponsiveLobby(bool existing)
    {
        int before = requests.Count;
        foreach (Vector2Int resolution in new[] { new Vector2Int(1280, 720), new Vector2Int(960, 540),
            new Vector2Int(1600, 900), new Vector2Int(1024, 768) })
        {
            Screen.SetResolution(resolution.x, resolution.y, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(.4f);
            var scroll = root.Q<ScrollView>("character-scroll");
            if (scroll != null) scroll.scrollOffset = Vector2.zero;
            root.Focus();
            yield return new WaitForSecondsRealtime(.2f);
            string suffix = resolution.x + "x" + resolution.y;
            Check(Screen.width == resolution.x && Screen.height == resolution.y && Visible(root.Q("character-lobby")),
                "Lobby disponível em " + suffix);
            Rect panel = root.worldBound, lobby = root.Q("character-lobby").worldBound;
            Check(lobby.width <= panel.width + 2 && lobby.height <= panel.height + 2 && lobby.width > 500,
                "Lobby cabe na tela e permite rolagem em " + suffix);
            if (!existing)
            {
                Submit("character-female");
                yield return null;
                Check(SelectionMatches("female"), "Seleção feminina tem destaque e rótulo em " + suffix);
                Submit("character-male");
                yield return null;
                Check(SelectionMatches("male"), "Seleção masculina tem destaque e rótulo em " + suffix);
                var female = root.Q<Image>("character-female-preview");
                var male = root.Q<Image>("character-male-preview");
                Check(female?.image && male?.image && female.image != male.image,
                    "Duas imagens de personagem diferentes estão disponíveis em " + suffix);
            }
            Check(requests.Count == before && !account.ReadyToEnterCity && !CityEntryLoading.Active,
                "Navegação e seleção não fazem request nem abrem a cidade em " + suffix);
            var action=Button(existing?"character-enter":"character-save");
            Rect actionBounds=action.worldBound;
            Check(Visible(action)&&actionBounds.height>30&&actionBounds.yMin>=panel.yMin&&
                  actionBounds.yMax<=panel.yMax&&actionBounds.xMin>=panel.xMin&&actionBounds.xMax<=panel.xMax,
                "Ação principal fica visível sem rolar em "+suffix);
            yield return Capture((existing ? "existing-" : "creation-") + suffix);
        }
    }

    bool SelectionMatches(string gender)
    {
        bool female = gender == "female";
        Button selected = Button(female ? "character-female" : "character-male");
        Button other = Button(female ? "character-male" : "character-female");
        Label status = root.Q<Label>(selected.name + "-status");
        return account.CharacterGender == gender && selected.ClassListContains("selected") &&
               !other.ClassListContains("selected") && status != null && status.text.Contains("SELECIONADO");
    }

    IEnumerator VerifyCityEntry(string name)
    {
        yield return WaitUntil(() => account.ReadyToEnterCity, 8, "Gate de entrada confirmado: " + name);
        Check(AccountSession.HasCharacter && CityEntryLoading.Active && AccountSession.InputBlocked && !account.AuthVisible,
            "Entrada confirmada aplica carregamento preto e bloqueia entrada: " + name);
        yield return Capture(name + "-black-loading", true);
        yield return WaitUntil(() => !StartupLoader.IsLoading && !CityEntryLoading.Active &&
            FindAnyObjectByType<ThirdPersonPlayer>() && GameHud.Instance, 30, "Cidade pronta: " + name);
        Check(!AccountSession.InputBlocked && GameHud.Instance.Root.resolvedStyle.visibility == Visibility.Visible,
            "Cidade libera personagem e HUD depois de terminar o carregamento: " + name);
        var player = FindAnyObjectByType<ThirdPersonPlayer>();
        player.DemoMode = true; player.DemoInput = Vector2.zero;
        Check(Vector3.ProjectOnPlane(player.transform.position - new Vector3(account.State.x, account.State.y, account.State.z), Vector3.up).magnitude < .15f,
            "Personagem recebe a posição do estado simulado: " + name);
        yield return Capture(name + "-city");
    }

    IEnumerator LogoutAndReturnToLanding()
    {
        account.Logout();
        yield return WaitUntil(() => !AccountSession.Authenticated, 8, "Logout local confirmado");
        yield return WaitForLanding();
    }

    IEnumerator WaitForLanding()
    {
        yield return WaitUntil(() => AccountSession.Instance && AccountSession.Instance.AuthVisible &&
            AccountSession.Instance.Document && Visible(AccountSession.Instance.Document.rootVisualElement.Q("auth-landing")),
            45, "Apresentação inicial disponível");
        account = AccountSession.Instance;
        root = account.Document.rootVisualElement;
        yield return new WaitForSecondsRealtime(.2f);
    }

    IEnumerator WaitForCreationLobby()
    {
        yield return WaitUntil(() => Visible(root.Q("character-create")), 8, "Lobby de criação disponível");
        yield return new WaitForSecondsRealtime(.2f);
    }

    IEnumerator WaitUntil(Func<bool> condition, float seconds, string description)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        if (!Check(condition(), description)) throw new InvalidOperationException("Tempo esgotado: " + description);
    }

    void OpenAuth(bool register, string email)
    {
        Submit(register ? "auth-register-email" : "auth-login-email");
        SetCredentials(email);
    }

    void SetCredentials(string email)
    {
        root.Q<TextField>("auth-email").value = email;
        root.Q<TextField>("auth-password").value = "OfflinePassword12345";
        root.Q<TextField>("auth-confirm").value = "OfflinePassword12345";
    }

    void SetCharacterNames(string firstName, string lastName)
    {
        root.Q<TextField>("character-first-name").value = firstName;
        root.Q<TextField>("character-last-name").value = lastName;
    }

    bool AuthBusy()
    {
        return !Button("auth-submit").enabledInHierarchy && !Button("auth-toggle").enabledInHierarchy &&
               !Button("auth-back").enabledInHierarchy && !root.Q<TextField>("auth-email").enabledInHierarchy &&
               !root.Q<TextField>("auth-password").enabledInHierarchy;
    }

    bool CharacterBusy()
    {
        return !Button("character-save").enabledInHierarchy && !Button("character-female").enabledInHierarchy &&
               !Button("character-male").enabledInHierarchy && !root.Q<TextField>("character-first-name").enabledInHierarchy &&
               !root.Q<TextField>("character-last-name").enabledInHierarchy;
    }

    void CheckMusic(string stage)
    {
        StartupLoader loader = FindAnyObjectByType<StartupLoader>();
        Check(loader && loader.Music && loader.Music.isPlaying && loader.Music.volume > .01f,
            "Música continua tocando " + stage);
    }

    IEnumerator FakeRequest(string method, string path, string body, bool authenticated, Action<ApiReply, string> done)
    {
        HashSet<string> keys = JsonKeys(body);
        requests.Add(new RequestRecord { method = method, path = path, authenticated = authenticated, keys = new List<string>(keys).ToArray() });
        ApiReply reply = null;
        string error = null;
        if (method == "POST" && (path == "/v1/auth/register" || path == "/v1/auth/login"))
        {
            Check(!authenticated && keys.SetEquals(new[] { "email", "password" }),
                "Payload de conta contém somente e-mail e senha: " + path);
            AuthBody credentials = JsonUtility.FromJson<AuthBody>(body);
            Check(credentials.email.EndsWith("@example.invalid", StringComparison.Ordinal) && credentials.password == "OfflinePassword12345",
                "Credenciais enviadas pertencem exclusivamente à fixture local");
            if (path.EndsWith("register", StringComparison.Ordinal))
            {
                registerAttempts++;
                if (registerAttempts == 1) error = "Falha de cadastro simulada. Tente novamente.";
                else reply = new ApiReply { token = "offline-fixture-token", state = null, needsCharacter = true };
            }
            else
            {
                loginAttempts++;
                reply = new ApiReply { token = "offline-fixture-token", state = Clone(savedState), needsCharacter = false };
            }
        }
        else if (method == "POST" && path == "/v1/characters")
        {
            characterAttempts++;
            Check(authenticated && keys.SetEquals(new[] { "firstName", "lastName", "gender" }),
                "Payload de personagem autenticado contém somente nome, sobrenome e gênero");
            CharacterBody character = JsonUtility.FromJson<CharacterBody>(body);
            Check(character.firstName == (recoveryScenario ? "João" : "Ana-María") && character.lastName == "D'Ávila" &&
                  character.gender == (recoveryScenario ? "male" : "female"),
                "Payload preserva nomes normalizados Unicode e seleção atual");
            if (characterAttempts == 1) error = "Falha de criação simulada. Tente novamente.";
            else
            {
                savedState = MakeState(character);
                if (recoveryScenario)
                {
                    reply = new ApiReply { code = "CHARACTER_EXISTS", error = "Este personagem já foi salvo." };
                    error = reply.error;
                }
                else reply = new ApiReply { state = Clone(savedState) };
            }
        }
        else if (method == "GET" && path == "/v1/player")
        {
            recoveryReads++;
            Check(authenticated && savedState != null && recoveryScenario, "Consulta de recuperação usa conta autenticada e estado salvo");
            reply = new ApiReply { state = Clone(savedState) };
        }
        else if (method == "PUT" && path == "/v1/player/checkpoint")
        {
            Check(authenticated && savedState != null, "Salvamento incidental permanece no transporte local");
            CheckpointBody checkpoint = JsonUtility.FromJson<CheckpointBody>(body);
            savedState.x = checkpoint.x; savedState.y = checkpoint.y; savedState.z = checkpoint.z;
            savedState.yaw = checkpoint.yaw; savedState.cameraYaw = checkpoint.cameraYaw; savedState.cameraPitch = checkpoint.cameraPitch;
            savedState.version = (int.Parse(savedState.version) + 1).ToString();
            reply = new ApiReply { state = Clone(savedState) };
        }
        else if (method == "POST" && path == "/v1/auth/logout")
        {
            Check(authenticated, "Logout de fixture é interceptado localmente");
            reply = new ApiReply();
        }
        else { Check(false, "Request inesperado foi bloqueado sem HTTPS: " + method + " " + path); error = "Rota não prevista no teste offline."; }
        yield return new WaitForSecondsRealtime(.65f);
        done(reply, error);
    }

    ApiPlayerState MakeState(CharacterBody character)
    {
        return new ApiPlayerState
        {
            accountId = recoveryScenario ? "offline-recovery-account" : "offline-account", uid = recoveryScenario ? "900002" : "900001",
            characterId = recoveryScenario ? "offline-recovery-character" : "offline-character", firstName = character.firstName,
            lastName = character.lastName, gender = character.gender, name = character.firstName + " " + character.lastName,
            xp = "0", playSeconds = "0", cashCents = "0", bankCents = "0", hzCoins = "0", model = "bybeyby",
            world = "centro_teste", version = "1", health = 100, energy = 100, hunger = 100, thirst = 100,
            x = 0, y = -.1f, z = 0, yaw = 90, cameraYaw = 90, cameraPitch = 14, level = 1
        };
    }

    static ApiPlayerState Clone(ApiPlayerState state)
    {
        return state == null ? null : JsonUtility.FromJson<ApiPlayerState>(JsonUtility.ToJson(state));
    }

    static HashSet<string> JsonKeys(string body)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(body ?? "", "\"([^\"\\\\]+)\"\\s*:")) result.Add(match.Groups[1].Value);
        return result;
    }

    Button Button(string name)
    {
        Button button = root.Q<Button>(name);
        if (button == null) throw new InvalidOperationException("Botão ausente: " + name);
        return button;
    }

    void Submit(string name)
    {
        Button button = Button(name);
        if (!button.enabledInHierarchy) throw new InvalidOperationException("Botão indisponível: " + name);
        button.Focus(); Dispatch(button);
    }

    static void Dispatch(Button button)
    {
        using (var submit = NavigationSubmitEvent.GetPooled()) button.SendEvent(submit);
    }

    bool HasError(string name)
    {
        Label label = root.Q<Label>(name);
        return Visible(label) && !string.IsNullOrWhiteSpace(label.text);
    }

    static bool Visible(VisualElement element)
    {
        if (element == null || element.panel == null) return false;
        for (VisualElement current = element; current != null; current = current.parent)
            if (current.resolvedStyle.display == DisplayStyle.None || current.resolvedStyle.visibility == Visibility.Hidden || current.resolvedStyle.opacity <= 0) return false;
        return true;
    }

    IEnumerator Capture(string name, bool blackLoading = false)
    {
        string path = Path.Combine(Folder, name + ".png");
        if (File.Exists(path)) File.Delete(path);
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(path);
        float deadline = Time.realtimeSinceStartup + 4;
        long previous = -1;
        int stable = 0;
        while (stable < 2 && Time.realtimeSinceStartup < deadline)
        {
            long length = File.Exists(path) ? new FileInfo(path).Length : 0;
            stable = length > 2000 && length == previous ? stable + 1 : 0;
            previous = length; yield return null;
        }
        if (!Check(stable >= 2, "Captura gravada: " + name)) yield break;
        var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        bool decoded = texture.LoadImage(File.ReadAllBytes(path));
        double sum = 0, square = 0; int count = 0;
        Color32[] pixels = decoded ? texture.GetPixels32() : Array.Empty<Color32>();
        for (int i = 0; i < pixels.Length; i += 29)
        {
            float value = (pixels[i].r + pixels[i].g + pixels[i].b) / (3f * 255f);
            sum += value; square += value * value; count++;
        }
        Destroy(texture);
        double mean = count > 0 ? sum / count : 0;
        double variance = count > 0 ? square / count - mean * mean : 0;
        Check(decoded && (blackLoading ? mean < .08 && variance > .00005 : mean > .003 && variance > .00005),
            blackLoading ? "Carregamento tem fundo preto e texto/progresso visível: " + name : "Captura contém imagem renderizada: " + name);
    }

    bool Check(bool passed, string description)
    {
        checks.Add((passed ? "OK: " : "FAIL: ") + description);
        if (!passed) errors.Add(description);
        return passed;
    }

    void RestorePreference()
    {
        if (hadEmailPreference) PlayerPrefs.SetString("account.email", originalEmailPreference);
        else PlayerPrefs.DeleteKey("account.email");
        PlayerPrefs.Save();
    }

    void Finish()
    {
        if (finished) return;
        finished = true;
        RestorePreference();
        var report = new Report { passed = errors.Count == 0, capturedUtc = DateTime.UtcNow.ToString("o"),
            checks = checks, errors = errors, localRequests = requests, remoteRequests = 0 };
        File.WriteAllText(Path.Combine(Folder, "validation.json"), JsonUtility.ToJson(report, true));
        File.WriteAllText(Path.Combine(Folder, "validation.txt"), string.Join("\n", checks) + "\nERRORS=" + errors.Count + "\n" + string.Join("\n", errors));
        Application.Quit(errors.Count == 0 ? 0 : 1);
    }

    void OnQuitting()
    {
        RestorePreference();
        AccountSession.VerificationTransport = null;
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= OnLog;
        Application.quitting -= OnQuitting;
    }
}
