using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class ArrivalVerification:MonoBehaviour
{
    const string Folder=@"C:\SistemaAdr\gta\tools\arrival-review";
    readonly List<string> checks=new List<string>(),errors=new List<string>();
    ApiPlayerState state;
    ArrivalState mission;
    AccountSession account;
    ThirdPersonPlayer player;
    string saved,oldEmail;
    bool hadEmail,finished;
    int starts,rewards;
    [Serializable]class Report{public bool passed;public List<string> checks,errors;public int remoteRequests;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot(){if(Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-arrival")>=0)new GameObject("QA chegada sem rede").AddComponent<ArrivalVerification>();}
    void Awake()
    {
        DontDestroyOnLoad(gameObject);Directory.CreateDirectory(Folder);Application.runInBackground=true;
        hadEmail=PlayerPrefs.HasKey("account.email");oldEmail=PlayerPrefs.GetString("account.email");
        AccountSession.VerificationTransport=Fake;SessionVault.TestRead=()=>saved;SessionVault.TestWrite=value=>saved=value;SessionVault.TestClear=()=>saved=null;TutorialJourney.AllowVerification=true;
        Application.logMessageReceived+=(message,trace,type)=>{if(type==LogType.Exception||type==LogType.Error||type==LogType.Assert)errors.Add(message+trace);};
        Application.quitting+=()=>{if(hadEmail)PlayerPrefs.SetString("account.email",oldEmail);else PlayerPrefs.DeleteKey("account.email");PlayerPrefs.Save();};
        state=new ApiPlayerState{accountId="arrival-account",characterId="arrival-character",uid="990003",firstName="Lucas",lastName="Teste",name="Lucas Teste",gender="male",accountRole="user",xp="0",cashCents="0",bankCents="0",hzCoins="0",playSeconds="0",model="bybeyby",world="centro_teste",version="1",level=1,health=100,energy=100,hunger=100,thirst=100,y=.08f,cameraPitch=14};
    }
    IEnumerator Start()
    {
        var stack=new Stack<IEnumerator>();stack.Push(Verify());
        while(stack.Count>0)
        {
            object value=null;bool moved=false;Exception error=null;
            try{moved=stack.Peek().MoveNext();if(moved)value=stack.Peek().Current;}catch(Exception exception){error=exception;}
            if(error!=null){errors.Add(error.ToString());break;}
            if(!moved){stack.Pop();continue;}if(value is IEnumerator nested)stack.Push(nested);else yield return value;
        }
        finished=true;MissionVehicle.VerificationInput=null;
        File.WriteAllText(Folder+"/validation.json",JsonUtility.ToJson(new Report{passed=errors.Count==0,checks=checks,errors=errors,remoteRequests=0},true));Application.Quit(errors.Count==0?0:1);
    }
    IEnumerator Wait(Func<bool> condition,string description,float timeout=60)
    {
        float deadline=Time.realtimeSinceStartup+timeout;while(!condition()&&Time.realtimeSinceStartup<deadline)yield return null;
        Check(condition(),description);if(!condition())throw new InvalidOperationException(description);
    }
    bool Check(bool value,string description){checks.Add((value?"OK: ":"FAIL: ")+description);if(!value)errors.Add(description);return value;}
    void Press(Button button){button.Focus();using(var evt=NavigationSubmitEvent.GetPooled())button.SendEvent(evt);}
    void Position(Vector3 point){var controller=player.GetComponent<CharacterController>();controller.enabled=false;player.transform.position=point;controller.enabled=true;}
    IEnumerator Capture(string name,bool unused=false){ScreenCapture.CaptureScreenshot(Folder+"/"+name+".png");yield return new WaitForEndOfFrame();yield return new WaitForEndOfFrame();}
    IEnumerator Verify()
    {
        yield return Wait(()=>AccountSession.Instance&&AccountSession.Instance.AuthVisible,"Login real disponível");account=AccountSession.Instance;var auth=account.Document.rootVisualElement;
        Press(auth.Q<Button>("auth-login-email"));auth.Q<TextField>("auth-email").value="arrival-fixture@example.invalid";auth.Q<TextField>("auth-password").value="ArrivalFixtureOnly123";Press(auth.Q<Button>("auth-submit"));
        yield return Wait(()=>AccountSession.HasCharacter,"Login local autenticado");Press(auth.Q<Button>("character-enter"));
        yield return Wait(()=>!StartupLoader.IsLoading&&!CityEntryLoading.Active&&TutorialJourney.Instance&&TutorialJourney.Instance.Phase=="available","Missão carrega da API simulada");
        player=UnityEngine.Object.FindAnyObjectByType<ThirdPersonPlayer>();player.DemoMode=true;player.DemoInput=Vector2.zero;var tutorial=TutorialJourney.Instance;
        Check(!tutorial.ModalOpen&&tutorial.LoanCar==null&&starts==0,"Entrada não abre diálogo automaticamente nem concede carro antes de conversar");
        Position(tutorial.Config.giver+Vector3.left);yield return new WaitForSecondsRealtime(.4f);tutorial.Interact("talk");yield return null;
        Check(tutorial.ModalOpen,"Aproximação e interação abrem diálogo do NPC");
        var ui=tutorial.Document.rootVisualElement;
        var dialogueTitle=ui.Q<Label>("arrival-dialog-title");var dialogueText=ui.Q<Label>("arrival-dialog-text");var objective=ui.Q<Label>("arrival-objective-title");
        Check(dialogueTitle.text=="BOAS-VINDAS"&&dialogueText.text.StartsWith("Salve, meu cria!")&&ui.Q<Button>("arrival-start").text=="PRÓXIMO","Banner começa pela boas-vindas com texto e ação corretos");yield return Capture("npc-dialogue");
        Press(ui.Q<Button>("arrival-cancel"));yield return new WaitForSecondsRealtime(.4f);Check(tutorial.LoanCar==null&&starts==0,"Cancelar diálogo não concede empréstimo");
        tutorial.Interact("talk");
        Press(ui.Q<Button>("arrival-start"));yield return null;Check(dialogueTitle.text=="APRENDENDO OS COMANDOS"&&ui.Q<Button>("arrival-start").text=="ENTENDI"&&objective.text.Contains("2/6"),"Etapa 2 ensina os comandos e atualiza o objetivo");
        Press(ui.Q<Button>("arrival-start"));yield return null;Check(dialogueTitle.text=="COMEÇANDO A VIDA NA CIDADE"&&ui.Q<Button>("arrival-start").text=="PRÓXIMO","Etapa 3 apresenta a vida na cidade");
        Press(ui.Q<Button>("arrival-start"));yield return null;Check(dialogueTitle.text=="O CARRO EMPRESTADO"&&ui.Q<Button>("arrival-start").text=="INICIAR MISSÃO"&&objective.text.Contains("3/6"),"Etapa 4 explica que o carro é somente emprestado");
        Press(ui.Q<Button>("arrival-start"));
        yield return Wait(()=>tutorial.Phase=="borrowed"&&tutorial.LoanCar,"Iniciar missão concede o carro temporário");
        Check(dialogueTitle.text=="RUMO À PREFEITURA"&&ui.Q<Button>("arrival-start").text=="FECHAR"&&objective.text.Contains("4/6"),"Etapa 5 orienta a entrar no carro e seguir o GPS");
        Check(starts==1&&rewards==0&&state.cashCents=="0","Empréstimo não é compra nem recompensa antecipada");yield return Capture("loan-and-gps");Press(ui.Q<Button>("arrival-start"));yield return null;
        Position(tutorial.LoanCar.transform.position+Vector3.left*2);yield return new WaitForSecondsRealtime(.4f);tutorial.Interact("enter");yield return null;
        Check(MissionVehicle.Driver==player&&!player.GetComponent<CharacterController>().enabled,"Entrar no veículo troca os controles e oculta o personagem");
        var car=tutorial.LoanCar;Vector3 initial=car.transform.position;MissionVehicle.VerificationInput=Vector2.up;yield return new WaitForSecondsRealtime(2);MissionVehicle.VerificationInput=Vector2.zero;
        Check(Vector3.Distance(car.transform.position,initial)>2,"Carro emprestado percorre a rua com direção funcional");yield return Capture("driving");
        Check(car.Exit(),"É possível sair em um local livre");Position(tutorial.Config.cityHall+Vector3.left);yield return new WaitForSecondsRealtime(.5f);tutorial.Interact("finish");
        Check(tutorial.ModalOpen,"Prefeitura exige aproximação e diálogo com o atendente");Press(ui.Q<Button>("arrival-start"));
        yield return Wait(()=>tutorial.Phase=="completed","Atendimento conclui missão");yield return null;
        Check(rewards==1&&state.xp=="100"&&state.cashCents=="15000"&&player.Cash==150,"Servidor confirma 100 XP e 150 reais uma única vez");
        Check(!tutorial.LoanCar&&!MissionVehicle.Driver&&mission.loanId==null,"Conclusão devolve o carro ao NPC sem propriedade permanente");yield return Capture("mission-completed");
        yield return account.VerifyRememberedAccess(Check,Capture);
    }
    IEnumerator Fake(string method,string path,string body,bool authenticated,Action<ApiReply,string> done)
    {
        yield return new WaitForSecondsRealtime(.06f);ApiReply reply=null;
        if(path=="/v1/auth/login")reply=new ApiReply{token=new string('A',43),state=state};
        else if(path=="/v1/player/checkpoint")
        {
            var save=JsonUtility.FromJson<CheckpointBody>(body);state.x=save.x;state.y=save.y;state.z=save.z;state.version=(int.Parse(state.version)+1).ToString();reply=new ApiReply{state=state};
        }
        else if(path=="/v1/missions/arrival")
        {
            if(mission==null)mission=new ArrivalState{phase="available",config=JsonUtility.FromJson<ArrivalConfig>(Resources.Load<TextAsset>("Arrival/Config").text)};
            if(method=="POST")
            {
                string action=JsonUtility.FromJson<ArrivalAction>(body).action;
                if(action=="start"&&mission.phase=="available"){starts++;mission.phase="borrowed";mission.loanId=Guid.NewGuid().ToString();mission.loanExpiresAt=DateTime.UtcNow.AddMinutes(30).ToString("o");}
                if(action=="finish"&&mission.phase=="borrowed"){rewards++;mission.phase="completed";mission.loanId=null;state.xp="100";state.cashCents="15000";}
            }
            reply=new ApiReply{mission=JsonUtility.FromJson<ArrivalState>(JsonUtility.ToJson(mission)),state=state};
        }
        else if(path=="/v1/auth/logout")reply=new ApiReply();
        if(reply==null)done(null,"Rota inesperada no teste local.");else done(reply,null);
    }
    void Update(){if(!finished&&Time.realtimeSinceStartup>240){errors.Add("Tempo global esgotado.");finished=true;File.WriteAllText(Folder+"/validation.json",JsonUtility.ToJson(new Report{passed=false,checks=checks,errors=errors},true));Application.Quit(1);}}
}
