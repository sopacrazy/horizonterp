using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class StartupLoader : MonoBehaviour
{
    public Texture2D artwork;
    public Texture2D loginArtwork;
    public AudioClip intro;
    public float minimumSeconds=10;
    public string cityScene="TestePersonagem";
    public static bool IsLoading { get; private set; }
    public float Progress { get; private set; }
    public AudioSource Music { get; private set; }
    static StartupLoader activeLoader;
    float started,opacity=1;
    string status="PREPARANDO A CIDADE";
    string detail="Carregando o cenário";

    void Awake()
    {
        if(activeLoader && activeLoader!=this){enabled=false;Destroy(gameObject);return;}
        activeLoader=this;
        IsLoading=true;started=Time.realtimeSinceStartup;
        DontDestroyOnLoad(gameObject);
        Application.targetFrameRate=60;
        Music=GetComponent<AudioSource>();
        if(!Music)Music=gameObject.AddComponent<AudioSource>();
        Music.clip=intro;Music.loop=true;Music.spatialBlend=0;Music.volume=0;
        Music.playOnAwake=false;Music.ignoreListenerPause=true;
        if(intro)Music.Play();
    }

    IEnumerator Start()
    {
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "--verify-gta-sa-map") >= 0)
        {
            yield return SceneManager.LoadSceneAsync(cityScene,LoadSceneMode.Single);
            IsLoading=false;Music.Stop();Destroy(gameObject);yield break;
        }
        yield return new WaitForSecondsRealtime(.75f);
        bool verifySession=System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"--verify-sa-session")>=0;
        if(verifySession)RememberedCityEntryVerification.Install();
        else
        {
            var updater=gameObject.AddComponent<GameUpdater>();
            yield return updater.Check((message,value)=>{status="DADOS DO JOGO";detail=message;Progress=value;Music.volume=.6f;});
        }
        status="PREPARANDO A CIDADE";detail="Carregando o cenário";Progress=0;
        var load=SceneManager.LoadSceneAsync(cityScene,LoadSceneMode.Single);
        if(load==null){status="NÃO FOI POSSÍVEL CARREGAR A CIDADE";yield break;}
        load.allowSceneActivation=false;
        while(load.progress<.9f || Time.realtimeSinceStartup-started<minimumSeconds)
        {
            float elapsed=Time.realtimeSinceStartup-started;
            Progress=Mathf.Clamp01(load.progress/.9f);
            Music.volume=Mathf.Min(elapsed/.4f,1)*.6f;
            yield return null;
        }
        Music.volume=.6f;
        yield return AccountSession.Ensure().RequireLogin(loginArtwork ? loginArtwork : artwork);
        var transition=CityEntryLoading.Show();
        transition.Progress=.90f;
        status="FINALIZANDO";Progress=.96f;
        load.allowSceneActivation=true;
        while(!load.isDone)yield return null;
        // Let Awake/Start, the HUD and the first gameplay render finish behind the artwork.
        yield return null;
        yield return new WaitForEndOfFrame();
        transition.Detail="Preparando as ruas e o ponto de chegada";
        transition.Progress=.96f;
        while (IntegratedMapEntry.Loading) yield return null;
        Progress=1;status="TUDO PRONTO";
        transition.Detail="Tudo pronto";
        transition.Progress=1;
        float fade=0;
        while(fade<.45f)
        {
            fade+=Time.unscaledDeltaTime;
            opacity=1-Mathf.Clamp01(fade/.45f);
            transition.Opacity=opacity;
            Music.volume=.6f*opacity;
            yield return null;
        }
        Music.Stop();IsLoading=false;
        Destroy(transition.gameObject);
        Debug.Log($"STARTUP_READY elapsed={Time.realtimeSinceStartup-started:F2} progress={Progress:F2}");
        Destroy(gameObject);
    }

    void OnGUI()
    {
        if(CityEntryLoading.Active || (AccountSession.Instance && AccountSession.Instance.AuthVisible))return;
        GUI.depth=-1000;
        bool splash=Time.realtimeSinceStartup-started<.75f;
        HorizonteEntryVisuals.DrawLoading(splash?"Carregando o launcher":status,detail,Progress,opacity,splash);
    }
    void OnDestroy(){if(activeLoader==this){activeLoader=null;IsLoading=false;}}
}
