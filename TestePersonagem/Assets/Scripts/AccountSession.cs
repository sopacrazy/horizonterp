using System;
using System.Collections;
using System.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

[Serializable] public class ApiPlayerState
{
    public int adminLevel;
    public string accountRole;
    public string accountId,uid,characterId,name,firstName,lastName,gender,xp,playSeconds,cashCents,bankCents,hzCoins,model,world,version;
    public float health,energy,hunger,thirst,x,y,z,yaw,cameraYaw,cameraPitch;
    public int level;
}
[Serializable] public class ApiReply {public string token,error,code;public ApiPlayerState state;public ArrivalState mission;public bool needsCharacter;}
[Serializable] public class ApiPublicConfig {public string baseUrl,certificateSha256;}
[Serializable] public class AuthBody {public string email,password;}
[Serializable] public class LoginBody {public string email,password;}
[Serializable] public class CharacterBody {public string firstName,lastName,gender;}
[Serializable] public class ProfileBody {public string firstName,lastName;}
[Serializable] public class CheckpointBody {public string requestId,version;public float x,y,z,yaw,cameraYaw,cameraPitch;}

public sealed partial class AccountSession : MonoBehaviour
{
    public static AccountSession Instance {get;private set;}
    public static bool Authenticated => Instance && !string.IsNullOrEmpty(Instance.token);
    public static bool HasCharacter => Authenticated && Instance.State!=null && !string.IsNullOrEmpty(Instance.State.characterId);
    public static bool InputBlocked => CityEntryLoading.Active || (Instance && (Instance.AuthVisible || !HasCharacter || Instance.leaving));
    public bool AuthVisible {get;private set;}
    public bool ReadyToEnterCity {get;private set;}
    public ApiPlayerState State {get;private set;}
    public string SaveStatus {get;private set;}="Conta conectada";
    public string LastError {get;private set;}
    public string LastErrorCode {get;private set;}
    public int RequestCount {get;private set;}
    public UIDocument Document=>document;
    ApiPublicConfig config;
    string token;
    bool busy,register,leaving,allowQuit;
    float nextSave;
    CheckpointBody pending;
    ThirdPersonPlayer player;
    // A local transport is available only during the explicitly requested offline verification run.
    internal static Func<string,string,string,bool,Action<ApiReply,string>,IEnumerator> VerificationTransport;

    public static AccountSession Ensure()
    {
        if(!Instance)new GameObject("Conta HBRP").AddComponent<AccountSession>();
        return Instance;
    }
    void Awake()
    {
        Instance=this;DontDestroyOnLoad(gameObject);
        config=JsonUtility.FromJson<ApiPublicConfig>(Resources.Load<TextAsset>("ApiConfig").text);
        Application.wantsToQuit+=WantsToQuit;
    }
    public IEnumerator RequireLogin(Texture2D artwork)
    {
        Build(artwork);ReadyToEnterCity=false;
        if(Authenticated)ShowAccountCharacter();
        else if(!string.IsNullOrEmpty(SessionVault.Read()))yield return RestoreRemembered();
        else ShowLogin();
        while(!ReadyToEnterCity||!HasCharacter)yield return null;
    }
    IEnumerator Submit()
    {
        if(busy)yield break;
        if(!ValidateForm())yield break;
        string path=register?"/v1/auth/register":"/v1/auth/login";
        string body=register?JsonUtility.ToJson(new AuthBody{email=email.value,password=password.value}):JsonUtility.ToJson(new LoginBody{email=email.value,password=password.value});
        busy=true;SetFormBusy(true);SetGeneralMessage(register?"Criando sua conta...":"Acessando sua conta...",false);
        ApiReply result=null;yield return Request("POST",path,body,false,r=>result=r);
        busy=false;SetFormBusy(false);
        if(string.IsNullOrEmpty(result?.token)||(!result.needsCharacter&&(result.state==null||string.IsNullOrEmpty(result.state.characterId))))
        {SetGeneralMessage(string.IsNullOrEmpty(LastError)?"Não foi possível acessar sua conta. Tente novamente.":LastError,true);yield break;}
        ClearSecrets();
        token=result.token;State=result.state;pending=null;leaving=false;SessionVault.Write(token);
        PlayerPrefs.SetString("account.email",email.value.Trim());PlayerPrefs.Save();
        ShowAccountCharacter();
    }
    void ShowAccountCharacter()
    {
        HideRemembered();
        ReadyToEnterCity=false;AuthVisible=true;root.style.display=DisplayStyle.Flex;
        landing.style.display=DisplayStyle.None;scroll.style.display=DisplayStyle.None;
        if(HasCharacter)ShowExistingCharacter(State);else ShowCharacterLobby();
    }
    IEnumerator SubmitCharacter()
    {
        if(busy||!Authenticated||HasCharacter||!ValidateCharacterForm())yield break;
        var body=new CharacterBody{firstName=CharacterFirstName,lastName=CharacterLastName,gender=CharacterGender};
        busy=true;SetCharacterBusy(true);SetCharacterMessage("Salvando seu personagem...",false);
        ApiReply reply=null;
        yield return Request("POST","/v1/characters",JsonUtility.ToJson(body),true,r=>reply=r);
        // The previous create may have committed even when its response was lost.
        if(reply==null&&Authenticated&&LastErrorCode=="CHARACTER_EXISTS")
            yield return Request("GET","/v1/player",null,true,r=>reply=r);
        busy=false;SetCharacterBusy(false);
        if(!Authenticated)yield break;
        if(reply?.state==null||string.IsNullOrEmpty(reply.state.characterId))
        {SetCharacterMessage(string.IsNullOrEmpty(LastError)?"Não foi possível salvar. Tente novamente.":LastError,true);yield break;}
        State=reply.state;pending=null;EnterCity();
    }
    void EnterCity()
    {
        if(busy||!HasCharacter||ReadyToEnterCity)return;
        HideRemembered();
        CityEntryLoading.Show();
        ReadyToEnterCity=true;AuthVisible=false;root.style.display=DisplayStyle.None;HideCharacterLobby();
        nextSave=Time.realtimeSinceStartup+15;
        if(player&&!StartupLoader.IsLoading)StartCoroutine(ResumeCity());
    }
    IEnumerator ResumeCity()
    {
        var transition=CityEntryLoading.Show();
        yield return new WaitForSecondsRealtime(.25f);
        AttachPlayer(player);
        float started=Time.realtimeSinceStartup;
        while(Time.realtimeSinceStartup-started<1.25f)
        {transition.Progress=Mathf.Clamp01((Time.realtimeSinceStartup-started)/1.25f);yield return null;}
        Destroy(transition.gameObject);
    }
    public void AttachPlayer(ThirdPersonPlayer target)
    {
        if(!HasCharacter||!ReadyToEnterCity)return;
        player=target;
        var controller=player.GetComponent<CharacterController>();controller.enabled=false;
        var savedPosition=new Vector3(State.x,State.y,State.z);
        if(NeighborhoodMap.Active)savedPosition=NeighborhoodMap.Active.ResolveSavedPosition(savedPosition,controller);
        player.transform.SetPositionAndRotation(savedPosition,Quaternion.Euler(0,State.yaw,0));controller.enabled=true;
        player.Health=State.health;player.Energy=State.energy;player.Hunger=State.hunger;player.Hydration=State.thirst;
        player.Level=State.level;float.TryParse(State.xp,out player.Experience);
        if(long.TryParse(State.cashCents,out long cash))player.Cash=cash/100;
        var orbit=FindAnyObjectByType<ThirdPersonCamera>();orbit.yaw=State.cameraYaw;orbit.pitch=State.cameraPitch;
        if(GameHud.Instance){GameHud.Instance.ApplyAccountIdentity();GameHud.Instance.NotifyRespawn();if(GameHud.Instance.IsPanelOpen)GameHud.Instance.ClosePanel();}
    }
    void Update()
    {
        if(VerificationTransport==null&&ReadyToEnterCity&&!HasCharacter&&!AuthVisible&&!leaving)
        {
            ReadyToEnterCity=false;
            ShowLogin(string.IsNullOrEmpty(LastError)?"Sua sessão foi encerrada. Entre novamente para continuar.":LastError);
            return;
        }
        if(HasCharacter&&ReadyToEnterCity&&player&&!StartupLoader.IsLoading&&!AuthVisible&&!busy&&!leaving&&Time.realtimeSinceStartup>=nextSave)
            StartCoroutine(Save());
    }
    public IEnumerator Save()
    {
        if(!HasCharacter||!ReadyToEnterCity||!player||StartupLoader.IsLoading)yield break;
        while(busy)yield return null;
        if(pending==null)
        {
            var p=player.transform.position;var orbit=FindAnyObjectByType<ThirdPersonCamera>();
            pending=new CheckpointBody{requestId=Guid.NewGuid().ToString(),version=State.version,x=p.x,y=p.y,z=p.z,yaw=Mathf.Repeat(player.transform.eulerAngles.y,360),cameraYaw=Mathf.Repeat(orbit.yaw,360),cameraPitch=orbit.pitch};
        }
        busy=true;SaveStatus="Salvando...";ApiReply reply=null;
        yield return Request("PUT","/v1/player/checkpoint",JsonUtility.ToJson(pending),true,r=>reply=r);
        if(reply?.state==null&&Authenticated&&LastErrorCode=="SAVE_CONFLICT")
        {
            ApiReply current=null;
            yield return Request("GET","/v1/player",null,true,r=>current=r);
            if(current?.state!=null&&Authenticated)
            {
                State=current.state;
                pending.version=State.version;
                pending.requestId=Guid.NewGuid().ToString();
                SaveStatus="Sincronizando progresso...";
                yield return Request("PUT","/v1/player/checkpoint",JsonUtility.ToJson(pending),true,r=>reply=r);
            }
        }
        busy=false;nextSave=Time.realtimeSinceStartup+15;
        if(reply?.state!=null){State=reply.state;pending=null;SaveStatus="Progresso salvo";}
        else SaveStatus="Sem salvar: "+LastError;
    }
    public void Rename(string value){if(!busy&&HasCharacter)StartCoroutine(RenameRoutine(value));}
    IEnumerator RenameRoutine(string value)
    {
        busy=true;ApiReply reply=null;
        yield return Request("PUT","/v1/player/profile",JsonUtility.ToJson(new ProfileBody{firstName=value,lastName=State.lastName}),true,r=>reply=r);
        busy=false;
        if(reply?.state!=null){State=reply.state;GameHud.Instance.ApplyAccountIdentity();}
        if(GameHud.Instance)GameHud.Instance.ShowAccountMessage(reply?.state!=null?"Nome salvo na conta":LastError);
    }
    public void Logout(){if(!leaving)StartCoroutine(LogoutRoutine());}
    IEnumerator LogoutRoutine()
    {
        leaving=true;yield return Save();
        if(pending!=null&&Authenticated){leaving=false;GameHud.Instance.ShowAccountMessage("Não foi possível salvar. Tente sair novamente.");yield break;}
        while(busy)yield return null;
        if(MultiplayerWorld.Instance)yield return MultiplayerWorld.Instance.Leave();
        busy=true;yield return Request("POST","/v1/auth/logout","{}",true,_=>{});busy=false;
        SessionVault.Clear();HideRemembered();token=null;State=null;player=null;pending=null;leaving=false;ReadyToEnterCity=false;
        SceneManager.LoadScene("Entrada");
    }
    void OnApplicationPause(bool pause)
    {if(pause&&Authenticated&&player&&!busy&&!StartupLoader.IsLoading)StartCoroutine(Save());}
    void OnApplicationFocus(bool focus)
    {if(!focus&&Authenticated&&player&&!busy&&!StartupLoader.IsLoading)StartCoroutine(Save());}
    bool WantsToQuit()
    {
        if(allowQuit || !HasCharacter || !ReadyToEnterCity || !player || StartupLoader.IsLoading)return true;
        if(!leaving)StartCoroutine(QuitAfterSaving());
        return false;
    }
    IEnumerator QuitAfterSaving(){leaving=true;yield return Save();if(MultiplayerWorld.Instance)yield return MultiplayerWorld.Instance.Leave();allowQuit=true;Application.Quit();}
    void OnDestroy(){Application.wantsToQuit-=WantsToQuit;if(Instance==this)Instance=null;}
    IEnumerator Request(string method,string path,string body,bool authenticated,Action<ApiReply> done,bool invalidateUnauthorized=true)
    {
        RequestCount++;LastError="";LastErrorCode="";
        if(VerificationTransport!=null)
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-character-flow")<0&&
                Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-neighborhood")<0&&
                Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-official-city")<0&&
                Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-arrival")<0&&
                Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-landing")<0)
            {LastError="Modo de verificação local não autorizado.";done(null);yield break;}
            yield return VerificationTransport(method,path,body,authenticated,(reply,error)=>
            {LastError=error??"";LastErrorCode=reply?.code??"";
                if(invalidateUnauthorized&&authenticated&&!string.IsNullOrEmpty(error)&&(reply?.code=="UNAUTHORIZED"||reply?.code=="CHARACTER_BLOCKED"))InvalidateSession(LastError);
                done(string.IsNullOrEmpty(error)?reply:null);});
            yield break;
        }
        using(var request=new UnityWebRequest(config.baseUrl+path,method))
        {
            request.downloadHandler=new DownloadHandlerBuffer();request.timeout=20;request.redirectLimit=0;
            request.certificateHandler=new PinnedCertificate(config.certificateSha256);
            if(body!=null){request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));request.SetRequestHeader("Content-Type","application/json");}
            if(authenticated)request.SetRequestHeader("Authorization","Bearer "+token);
            yield return request.SendWebRequest();
            ApiReply reply=null;
            if(!string.IsNullOrEmpty(request.downloadHandler.text))
                try{reply=JsonUtility.FromJson<ApiReply>(request.downloadHandler.text);}catch{}
            if(request.result==UnityWebRequest.Result.Success){LastError=LastErrorCode="";done(reply);yield break;}
            LastError=reply?.error??"Não foi possível conectar ao HBRP. Verifique sua internet e tente novamente.";
            LastErrorCode=reply?.code??"";
            if(invalidateUnauthorized&&authenticated&&!leaving&&(request.responseCode==401||reply?.code=="CHARACTER_BLOCKED"))
            {Debug.LogWarning("ACCOUNT_SESSION_INVALIDATED status="+request.responseCode+" code="+LastErrorCode+" error="+LastError);SessionVault.Clear();token=null;State=null;pending=null;ReadyToEnterCity=false;if(document)ShowLogin(LastError);}
            done(null);
        }
    }
    internal void InvalidateSession(string message)
    {
        // A delayed checkpoint/leave reply must never erase a still-valid remembered
        // access while the application is closing. It is revalidated on next launch.
        if(leaving){Debug.LogWarning("ACCOUNT_SESSION_INVALIDATION_IGNORED_WHILE_LEAVING error="+message);return;}
        Debug.LogWarning("ACCOUNT_SESSION_INVALIDATED_EXTERNAL error="+message);SessionVault.Clear();token=null;State=null;pending=null;ReadyToEnterCity=false;if(document)ShowLogin(message);
    }
    sealed class PinnedCertificate:CertificateHandler
    {
        readonly string expected;
        public PinnedCertificate(string fingerprint){expected=fingerprint;}
        protected override bool ValidateCertificate(byte[] data)
        {
            try
            {
                using(var certificate=new X509Certificate2(data))
                using(var sha=SHA256.Create())
                {
                    if(DateTime.UtcNow<certificate.NotBefore.ToUniversalTime()||DateTime.UtcNow>certificate.NotAfter.ToUniversalTime())return false;
                    string actual=BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();
                    return actual==expected;
                }
            }catch{return false;}
        }
    }
}
