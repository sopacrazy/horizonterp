using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public sealed class StartupVerification : MonoBehaviour
{
    readonly List<string> report=new List<string>();
    readonly List<string> errors=new List<string>();
    string folder;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-startup")>=0)
            new GameObject("Verificação da entrada").AddComponent<StartupVerification>();
    }
    void Awake()
    {
        DontDestroyOnLoad(gameObject);Application.runInBackground=true;
        folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../../tools/startup-review"));
        Directory.CreateDirectory(folder);Application.logMessageReceived+=OnLog;
    }
    void OnLog(string message,string trace,LogType type)
    {if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+trace);}
    void Check(bool value,string description){report.Add((value?"OK: ":"FALHA: ")+description);if(!value)errors.Add(description);}
    IEnumerator Start()
    {
        float started=Time.realtimeSinceStartup;
        yield return new WaitForSecondsRealtime(3);
        var loader=FindAnyObjectByType<StartupLoader>();
        Check(loader && StartupLoader.IsLoading,"Entrada permanece visível aos 3 segundos");
        if(!loader){Finish();yield break;}
        Check(loader.Progress>0 && loader.Progress<1,"Barra avança durante o carregamento");
        Check(loader.Music && loader.Music.isPlaying && loader.Music.time>1,"Música intro tocando na entrada");
        var samples=new float[256];float energy=0;
        // Prime the DSP capture buffer, then sample several audio frames.
        for(int i=0;i<5;i++)
        {
            loader.Music.GetOutputData(samples,0);
            foreach(float sample in samples)energy+=sample*sample;
            yield return new WaitForSecondsRealtime(.08f);
        }
        report.Add($"Áudio: sinal={energy:F6}, volume={loader.Music.volume:F2}, listener={FindObjectsByType<AudioListener>().Length}, pausa={AudioListener.pause}");
        Check(energy>.000001f,"Saída de áudio da música contém sinal");
        yield return Capture("01-entrada");
        while(StartupLoader.IsLoading && Time.realtimeSinceStartup-started<60)yield return null;
        float duration=Time.realtimeSinceStartup-started;
        Check(!StartupLoader.IsLoading && duration>=9.9f && duration<60,"Jogo liberado apenas após 10 segundos e cena pronta: "+duration.ToString("F2")+"s");
        yield return null;
        Check(!FindAnyObjectByType<StartupLoader>(),"Tela e música da entrada encerradas");
        var player=FindAnyObjectByType<ThirdPersonPlayer>();
        Check(player && GameHud.Instance && FindAnyObjectByType<MobileControls>(),"Personagem, HUD e controles inicializados após troca de cena");
        var foundation=GameObject.Find("Base solida 48m");
        Check(foundation && !foundation.GetComponent<Renderer>().enabled && foundation.GetComponent<Collider>().enabled,"Base sobreposta invisível e colisão preservada");
        if(!player){Finish();yield break;}
        Check(Mathf.Abs(player.runSpeed-4.5f)<.001f,"Corrida configurada para 4.5 m/s");
        player.DemoMode=true;player.DemoRun=true;player.DemoInput=Vector2.up;
        Vector3 origin=player.transform.position;
        yield return new WaitForSecondsRealtime(1);
        Check(player.CurrentSpeed>4.3f && Vector3.ProjectOnPlane(player.transform.position-origin,Vector3.up).magnitude>4,"Corrida realmente desloca o personagem mais rápido");
        player.DemoInput=Vector2.zero;player.ResetPosition();
        yield return new WaitForSecondsRealtime(.4f);
        var cycle=FindAnyObjectByType<CityDayNight>();
        bool night=cycle && cycle.IsNight;if(cycle)cycle.SetNight(false);
        yield return new WaitForSecondsRealtime(.5f);
        yield return Capture("02-chao-dia");
        if(cycle)cycle.SetNight(night);
        yield return new WaitForSecondsRealtime(.5f);
        yield return Capture("03-jogo");
        Finish();
    }
    IEnumerator Capture(string name)
    {
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(folder,name+".png"));
        yield return null;yield return null;
    }
    void Finish()
    {
        report.Add("ERROS="+errors.Count);report.AddRange(errors);
        File.WriteAllLines(Path.Combine(folder,"validation.txt"),report);
        Application.Quit(errors.Count==0?0:1);
    }
    void OnDestroy(){Application.logMessageReceived-=OnLog;}
}
