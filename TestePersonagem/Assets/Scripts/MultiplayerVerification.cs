using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

// Explicit end-to-end test only. Credentials are short-lived fixtures removed by the admin verifier.
public sealed class MultiplayerVerification : MonoBehaviour
{
    [Serializable] sealed class Fixture {public string email,password;public int role;}
    [Serializable] sealed class Report {public bool passed;public int role,receivedFrames;public bool sawRemoteJump;public float remoteTravel;public List<string> checks,errors;}
    readonly List<string> checks=new List<string>(),errors=new List<string>();
    Fixture fixture;
    string previousEmail;
    bool hadEmail,finished;
    const string Folder=@"C:\SistemaAdr\gta\tools\multiplayer-review";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-multiplayer")>=0&&!FindAnyObjectByType<MultiplayerVerification>())
            new GameObject("Verificação multiplayer").AddComponent<MultiplayerVerification>();
    }
    void Awake()
    {
        DontDestroyOnLoad(gameObject);Application.runInBackground=true;
        Directory.CreateDirectory(Folder);
        var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--multiplayer-fixture");
        fixture=JsonUtility.FromJson<Fixture>(File.ReadAllText(args[index+1]));
        if(fixture.role<0||fixture.role>1||!fixture.email.EndsWith("@example.invalid"))throw new Exception("Fixture inválida");
        hadEmail=PlayerPrefs.HasKey("account.email");previousEmail=PlayerPrefs.GetString("account.email","");
        Application.logMessageReceived+=Log;
    }
    void Log(string value,string trace,LogType type)
    {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(value);}
    IEnumerator Start()
    {
        var stack=new Stack<IEnumerator>();stack.Push(Verify());
        while(stack.Count>0)
        {
            object current=null;bool more=false;Exception failure=null;
            try{more=stack.Peek().MoveNext();if(more)current=stack.Peek().Current;}catch(Exception ex){failure=ex;}
            if(failure!=null){errors.Add(failure.Message);break;}
            if(!more){stack.Pop();continue;}
            if(current is IEnumerator nested)stack.Push(nested);else yield return current;
        }
        yield return Finish();
    }
    IEnumerator Until(Func<bool> condition,float seconds,string caption)
    {
        float deadline=Time.realtimeSinceStartup+seconds;
        while(!condition()&&Time.realtimeSinceStartup<deadline)yield return null;
        Check(condition(),caption);if(!condition())throw new Exception("Tempo esgotado: "+caption);
    }
    void Check(bool value,string caption){checks.Add((value?"OK: ":"FAIL: ")+caption);if(!value)errors.Add(caption);}
    static void Press(Button button)
    {button.Focus();using(var evt=NavigationSubmitEvent.GetPooled())button.SendEvent(evt);}
    IEnumerator Verify()
    {
        Screen.SetResolution(960,540,FullScreenMode.Windowed);
        yield return Until(()=>AccountSession.Instance&&AccountSession.Instance.Document&&
            AccountSession.Instance.Document.rootVisualElement.Q("auth-landing")?.resolvedStyle.display==DisplayStyle.Flex,45,"Tela de login disponível");
        var account=AccountSession.Instance;var root=account.Document.rootVisualElement;
        Press(root.Q<Button>("auth-login-email"));
        root.Q<TextField>("auth-email").value=fixture.email;root.Q<TextField>("auth-password").value=fixture.password;
        Press(root.Q<Button>("auth-submit"));
        yield return Until(()=>AccountSession.HasCharacter,30,"Conta temporária autenticada via HTTPS real");
        Press(root.Q<Button>("character-enter"));
        yield return Until(()=>MultiplayerWorld.Instance&&MultiplayerWorld.Instance.Connected&&MultiplayerWorld.Instance.PlayerCount==2,
            60,"Dois clientes Unity conectados à mesma sala");
        var world=MultiplayerWorld.Instance;var player=FindAnyObjectByType<ThirdPersonPlayer>();
        player.DemoMode=true;
        var chat=InGameChat.Instance;var hud=GameHud.Instance.Root;
        var chatArea=hud.Q("chat-area");chatArea.Focus();
        using(var evt=NavigationSubmitEvent.GetPooled())chatArea.SendEvent(evt);
        yield return null;
        Check(InGameChat.Editing,"Histórico clicável abre o editor e bloqueia os controles");
        hud.Q<TextField>("chat-input").value="Olá do jogador "+fixture.role+"!";
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"chat-compose-"+fixture.role+".png"));
        Press(hud.Q<Button>("chat-send"));
        yield return Until(()=>!chat.Sending&&!chat.IsOpen,15,"Mensagem confirmada pelo servidor fecha o editor");
        yield return Until(()=>chat.HasMessage("Olá do jogador 0!")&&chat.HasMessage("Olá do jogador 1!"),20,
            "Cada cliente recebe a própria mensagem e a do outro jogador");
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"chat-messages-"+fixture.role+".png"));
        float started=Time.realtimeSinceStartup,nextJump=0;
        while(Time.realtimeSinceStartup-started<16)
        {
            float elapsed=Time.realtimeSinceStartup-started;
            player.DemoInput=Vector2.up*Mathf.Sin(elapsed*2)*.7f;
            player.DemoRun=((int)(elapsed/3))%2==1;
            if(elapsed>=nextJump){player.RequestJump();nextJump=elapsed+3;}
            yield return null;
        }
        player.DemoInput=Vector2.zero;player.DemoRun=false;
        Check(world.Connected&&world.RemoteCount==1,"Cada cliente mostra exatamente o outro personagem");
        Check(world.ReceivedFrames>20,"Movimento recebido continuamente do servidor");
        Check(world.RemoteTravel>.5f,"Outro personagem se desloca no cenário");
        Check(world.SawRemoteJump,"Animação de pulo recebida do outro jogador");
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"two-players-"+fixture.role+".png"));
        yield return new WaitForSecondsRealtime(6);
    }
    IEnumerator Finish()
    {
        if(finished)yield break;finished=true;
        var world=MultiplayerWorld.Instance;
        var report=new Report {passed=errors.Count==0,role=fixture.role,receivedFrames=world?world.ReceivedFrames:0,
            remoteTravel=world?world.RemoteTravel:0,sawRemoteJump=world&&world.SawRemoteJump,checks=checks,errors=errors};
        File.WriteAllText(Path.Combine(Folder,"validation-"+fixture.role+".json"),JsonUtility.ToJson(report,true));
        if(AccountSession.Authenticated)
        {
            AccountSession.Instance.Logout();float deadline=Time.realtimeSinceStartup+10;
            while(AccountSession.Authenticated&&Time.realtimeSinceStartup<deadline)yield return null;
        }
        if(hadEmail)PlayerPrefs.SetString("account.email",previousEmail);else PlayerPrefs.DeleteKey("account.email");
        PlayerPrefs.Save();Application.Quit(errors.Count==0?0:1);
    }
    void OnDestroy(){Application.logMessageReceived-=Log;}
}
