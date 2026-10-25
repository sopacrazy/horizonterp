using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

// Explicit integration test. Temporary account is removed on the server after verification.
public sealed class AccountVerification : MonoBehaviour
{
    [Serializable] public class Fixture{public string email,password,name,firstName,lastName,gender,accountId,uid;public float x,y,z;}
    Fixture fixture;
    string folder,fixturePath;
    bool second;
    readonly List<string> checks=new List<string>();
    readonly List<string> errors=new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        var args=Environment.GetCommandLineArgs();
        if(Array.IndexOf(args,"--verify-account")>=0||Array.IndexOf(args,"--verify-account-load")>=0)
            new GameObject("Verificação da conta").AddComponent<AccountVerification>();
    }
    void Awake()
    {
        DontDestroyOnLoad(gameObject);Application.runInBackground=true;
        second=Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-account-load")>=0;
        folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../../tools/account-review"));Directory.CreateDirectory(folder);
        fixturePath=Path.Combine(folder,"temporary-test-account.json");
        Application.logMessageReceived+=OnLog;
    }
    void OnLog(string message,string trace,LogType type)
    {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+trace);}
    void Check(bool value,string message){checks.Add((value?"OK: ":"FALHA: ")+message);if(!value)errors.Add(message);}
    IEnumerator Start()
    {
        float deadline=Time.realtimeSinceStartup+35;
        while((!AccountSession.Instance||!AccountSession.Instance.AuthVisible)&&Time.realtimeSinceStartup<deadline)yield return null;
        var account=AccountSession.Instance;
        if(account==null||!account.AuthVisible){Check(false,"Tela de login disponível");Finish();yield break;}
        var root=account.Document.rootVisualElement;
        Check(root.Q<TextField>("auth-password").isPasswordField,"Senha mascarada na interface");
        if(second)
        {
            Submit(root.Q<Button>("auth-login-email"));yield return null;
            fixture=JsonUtility.FromJson<Fixture>(File.ReadAllText(fixturePath));
        }
        else
        {
            string suffix=Guid.NewGuid().ToString("N").Substring(0,10);
            var nameSuffix=suffix.ToCharArray();
            for(int i=0;i<nameSuffix.Length;i++)if(char.IsDigit(nameSuffix[i]))nameSuffix[i]=(char)('g'+nameSuffix[i]-'0');
            fixture=new Fixture{email="unity-"+suffix+"@example.invalid",password=Guid.NewGuid().ToString("N"),
                firstName="Teste",lastName="Cidade"+new string(nameSuffix),gender="male"};
            fixture.name=fixture.firstName+" "+fixture.lastName;
            yield return Capture("01-login");
            Submit(root.Q<Button>("auth-register-email"));yield return null;
            root.Q<TextField>("auth-confirm").value=fixture.password;
        }
        root.Q<TextField>("auth-email").value=fixture.email;
        root.Q<TextField>("auth-password").value=fixture.password;
        if(!second)yield return Capture("02-cadastro");
        Submit(root.Q<Button>("auth-submit"));
        deadline=Time.realtimeSinceStartup+35;
        while(!AccountSession.Authenticated&&Time.realtimeSinceStartup<deadline)yield return null;
        if(!AccountSession.Authenticated){Check(false,"Conta autenticada: "+account.LastError);Finish();yield break;}
        yield return null;
        Check(account.AuthVisible&&!account.ReadyToEnterCity&&AccountSession.InputBlocked,
            "Autenticação mantém a entrada bloqueada até confirmar o personagem");
        if(second)
        {
            if(!IsVisible(root.Q("character-existing"))||!AccountSession.HasCharacter)
            {Check(false,"Login recupera a apresentação do personagem salvo");Finish();yield break;}
            Check(!IsVisible(root.Q("character-create")),"Personagem existente não abre novamente a criação");
            Check(root.Q<Image>("character-existing-image")?.image,"Prévia do personagem salvo carregada");
            var summary=root.Q<Label>("character-existing-name");
            Check(summary!=null&&summary.text==fixture.firstName,"Apresentação recupera o primeiro nome do personagem");
            yield return Capture("02-personagem-recuperado");
            Submit(root.Q<Button>("character-enter"));
        }
        else
        {
            if(!IsVisible(root.Q("character-create"))||AccountSession.HasCharacter)
            {Check(false,"Cadastro abre a criação sem personagem ou progresso antecipado");Finish();yield break;}
            root.Q<TextField>("character-first-name").value=fixture.firstName;
            root.Q<TextField>("character-last-name").value=fixture.lastName;
            Submit(root.Q<Button>(fixture.gender=="female"?"character-female":"character-male"));
            Check(account.CharacterFirstName==fixture.firstName&&account.CharacterLastName==fixture.lastName&&
                account.CharacterGender==fixture.gender,"Nome, sobrenome e gênero selecionados antes de salvar");
            yield return Capture("02-criacao-personagem");
            Submit(root.Q<Button>("character-save"));
        }
        deadline=Time.realtimeSinceStartup+35;
        while((!AccountSession.HasCharacter||!account.ReadyToEnterCity||account.AuthVisible||StartupLoader.IsLoading||CityEntryLoading.Active)&&
              Time.realtimeSinceStartup<deadline)yield return null;
        if(!AccountSession.HasCharacter||!account.ReadyToEnterCity||account.AuthVisible||StartupLoader.IsLoading||CityEntryLoading.Active)
        {Check(false,"Entrada na cidade concluída após confirmar o personagem: "+account.LastError);Finish();yield break;}
        yield return new WaitForSecondsRealtime(.5f);
        var player=FindAnyObjectByType<ThirdPersonPlayer>();
        Check(player && GameHud.Instance,"Jogo carregado depois da autenticação");
        if(!player||!GameHud.Instance){Finish();yield break;}
        Check(GameHud.Instance.Root.Q(className:"atlas-id") is Label id&&id.text.Contains(account.State.uid),"HUD exibe UID real da conta");
        Check(account.State.name==fixture.name&&GameHud.Instance.PlayerName==fixture.firstName,"Identidade completa salva; HUD mostra primeiro nome");
        if(!string.IsNullOrEmpty(fixture.firstName))
            Check(account.State.firstName==fixture.firstName&&account.State.lastName==fixture.lastName&&account.State.gender==fixture.gender,
                "Nome, sobrenome e gênero persistidos pela API");
        if(second)
        {
            Check(account.State.uid==fixture.uid,"UID permanece após fechar e abrir o executável");
            Check(Mathf.Abs(player.transform.position.x-fixture.x)<.1f&&Mathf.Abs(player.transform.position.z-fixture.z)<.1f,"Posição recuperada do servidor em novo processo");
            Check(account.State.name==fixture.name,"Nome recuperado do servidor");
            Check(player.Health==100&&player.Cash==0,"Atributos e saldo carregados do servidor");
            yield return Capture("04-progresso-recuperado");
            player.DemoMode=true;player.DemoInput=Vector2.up;
            yield return new WaitForSecondsRealtime(.4f);player.DemoInput=Vector2.zero;
            float expected=player.transform.position.x;
            deadline=Time.realtimeSinceStartup+20;
            while(Mathf.Abs(account.State.x-expected)>.1f&&Time.realtimeSinceStartup<deadline)yield return null;
            Check(Mathf.Abs(account.State.x-expected)<.1f,"Salvamento automático confirma novo movimento no servidor");
            account.Logout();deadline=Time.realtimeSinceStartup+25;
            while((AccountSession.Authenticated||!account.AuthVisible)&&Time.realtimeSinceStartup<deadline)yield return null;
            Check(!AccountSession.Authenticated&&account.AuthVisible,"Salvar e sair retorna ao login sem perder o serviço de conta");
            Screen.SetResolution(960,540,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.4f);
            yield return Capture("05-login-compacto");
        }
        else
        {
            fixture.accountId=account.State.accountId;fixture.uid=account.State.uid;
            File.WriteAllText(fixturePath,JsonUtility.ToJson(fixture));
            player.DemoMode=true;player.DemoInput=Vector2.up;
            yield return new WaitForSecondsRealtime(.8f);player.DemoInput=Vector2.zero;
            yield return new WaitForSecondsRealtime(.3f);
            yield return account.Save();
            Check(account.SaveStatus=="Progresso salvo","Movimento enviado por HTTPS e confirmado pelo servidor");
            Check(Mathf.Abs(account.State.x-player.transform.position.x)<.05f,"Estado salvo coincide com o personagem");
            fixture.firstName="Perfil";fixture.name=fixture.firstName+" "+fixture.lastName;GameHud.Instance.SaveName(fixture.firstName);
            deadline=Time.realtimeSinceStartup+20;
            while(account.State.name!=fixture.name&&Time.realtimeSinceStartup<deadline)yield return null;
            Check(account.State.name==fixture.name&&account.State.firstName==fixture.firstName&&account.State.lastName==fixture.lastName&&
                GameHud.Instance.PlayerName==fixture.firstName,"Primeiro nome alterado preserva o sobrenome salvo");
            fixture.x=account.State.x;fixture.y=account.State.y;fixture.z=account.State.z;
            File.WriteAllText(fixturePath,JsonUtility.ToJson(fixture));
            yield return Capture("03-conta-no-jogo");
        }
        Finish();
    }
    static bool IsVisible(VisualElement element)
    {
        if(element==null||element.panel==null)return false;
        for(var current=element;current!=null;current=current.parent)
            if(current.resolvedStyle.display==DisplayStyle.None||current.resolvedStyle.visibility==Visibility.Hidden||current.resolvedStyle.opacity<=0)return false;
        return true;
    }
    void Submit(Button button){button.Focus();using(var e=NavigationSubmitEvent.GetPooled())button.SendEvent(e);}
    IEnumerator Capture(string name)
    {
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(folder,name+".png"));yield return null;yield return null;
    }
    void Finish()
    {
        checks.Add("ERROS="+errors.Count);checks.AddRange(errors);
        File.WriteAllLines(Path.Combine(folder,second?"load-validation.txt":"signup-validation.txt"),checks);
        Application.Quit(errors.Count==0?0:1);
    }
    void OnDestroy(){Application.logMessageReceived-=OnLog;}
}
