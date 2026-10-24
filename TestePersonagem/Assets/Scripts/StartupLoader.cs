using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class StartupLoader : MonoBehaviour
{
    public Texture2D artwork;
    public Texture2D loginArtwork;
    public AudioClip intro;
    public float minimumSeconds=10;
    public static bool IsLoading { get; private set; }
    public float Progress { get; private set; }
    public AudioSource Music { get; private set; }
    static StartupLoader activeLoader;
    float started,opacity=1;
    string status="PREPARANDO A CIDADE";
    GUIStyle label,percent;

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
        // Display the artwork before starting the asynchronous scene load.
        yield return null;
        var updater=gameObject.AddComponent<GameUpdater>();
        yield return updater.Check((message,value)=>{status=message;Progress=value;Music.volume=.6f;});
        status="PREPARANDO A CIDADE";Progress=0;
        var load=SceneManager.LoadSceneAsync("TestePersonagem",LoadSceneMode.Single);
        if(load==null){status="NÃO FOI POSSÍVEL CARREGAR A CIDADE";yield break;}
        load.allowSceneActivation=false;
        while(load.progress<.9f || Time.realtimeSinceStartup-started<minimumSeconds)
        {
            float elapsed=Time.realtimeSinceStartup-started;
            Progress=Mathf.Min(Mathf.Clamp01(elapsed/minimumSeconds),Mathf.Clamp01(load.progress/.9f))*.94f;
            Music.volume=Mathf.Min(elapsed/.4f,1)*.6f;
            yield return null;
        }
        Music.volume=.6f;
        yield return AccountSession.Ensure().RequireLogin(loginArtwork ? loginArtwork : artwork);
        var transition=CityEntryLoading.Show();
        float entering=Time.realtimeSinceStartup;
        status="FINALIZANDO";Progress=.96f;
        transition.Progress=.15f;
        load.allowSceneActivation=true;
        while(!load.isDone)yield return null;
        // Let Awake/Start, the HUD and the first gameplay render finish behind the artwork.
        yield return null;
        yield return new WaitForEndOfFrame();
        Progress=1;status="TUDO PRONTO";
        while(Time.realtimeSinceStartup-entering<1.6f)
        {transition.Progress=Mathf.Lerp(.35f,1,Mathf.Clamp01((Time.realtimeSinceStartup-entering)/1.6f));yield return null;}
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
        GUI.color=new Color(1,1,1,opacity);
        float scale=Screen.height/720f;
        var screen=new Rect(0,0,Screen.width,Screen.height);
        if(artwork)GUI.DrawTexture(screen,artwork,ScaleMode.ScaleAndCrop);
        else GUI.DrawTexture(screen,Texture2D.blackTexture);
        Rect safe=Screen.safeArea;
        float width=Mathf.Min(safe.width-48*scale,780*scale);
        float left=safe.center.x-width/2;
        float bottom=Screen.height-safe.yMin-19*scale;
        if(label==null)
        {
            label=new GUIStyle(GUI.skin.label){fontStyle=FontStyle.Bold};
            label.normal.textColor=new Color(.88f,.88f,.88f);
            percent=new GUIStyle(label){alignment=TextAnchor.UpperRight};
        }
        label.fontSize=percent.fontSize=Mathf.Max(10,Mathf.RoundToInt(12*scale));
        GUI.color=new Color(0,0,0,opacity*.8f);
        GUI.Label(new Rect(left+scale,bottom-42*scale,width,25*scale),status,label);
        GUI.Label(new Rect(left+scale,bottom-42*scale,width,25*scale),$"{Mathf.FloorToInt(Progress*100)}%",percent);
        GUI.color=new Color(1,1,1,opacity);
        GUI.Label(new Rect(left,bottom-43*scale,width,25*scale),status,label);
        GUI.Label(new Rect(left,bottom-43*scale,width,25*scale),$"{Mathf.FloorToInt(Progress*100)}%",percent);
        GUI.Label(new Rect(left,bottom-67*scale,width,22*scale),"HORIZONTE BRASIL · v"+Application.version,label);
        GUI.color=new Color(.17f,.18f,.19f,opacity);
        GUI.DrawTexture(new Rect(left,bottom-12*scale,width,5*scale),Texture2D.whiteTexture);
        GUI.color=new Color(1,.76f,.24f,opacity);
        GUI.DrawTexture(new Rect(left,bottom-12*scale,width*Progress,5*scale),Texture2D.whiteTexture);
        GUI.color=Color.white;
    }
    void OnDestroy(){if(activeLoader==this){activeLoader=null;IsLoading=false;}}
}
