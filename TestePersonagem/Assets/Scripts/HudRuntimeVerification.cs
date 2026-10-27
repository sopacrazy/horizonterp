using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

// Explicit command-line verification only; does not run during normal gameplay.
public sealed class HudRuntimeVerification : MonoBehaviour
{
    readonly List<string> checks=new List<string>();
    readonly List<string> errors=new List<string>();
    string folder;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-hud")>=0)
            new GameObject("HUD Runtime Verification").AddComponent<HudRuntimeVerification>();
    }
    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../../../tools/hud-review"));
        Directory.CreateDirectory(folder);Application.logMessageReceived+=OnLog;
    }
    void OnLog(string message,string trace,LogType type)
    {
        if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+trace);
    }
    IEnumerator Start()
    {
        while(StartupLoader.IsLoading)yield return null;
        yield return new WaitForSecondsRealtime(1.5f);
        var hud=GameHud.Instance;
        if(!Check(hud!=null,"HUD criado")||!Check(hud.Root.worldBound.width>500,"Layout calculado")){Finish();yield break;}
        Check(hud.Root.Q<Button>("nav-menu")!=null,"Botão de menu presente");
        Check(hud.Root.Q<Button>("action-sprint")!=null,"Botão de corrida presente");
        Check(HudAtlas.RegionCount>=35,"Recortes individuais do atlas disponíveis");
        Check(HudAtlas.Get("sprint").rect.width==126,"Botão de corrida usa o recorte original");
        Check(!hud.Root.Q<Button>("atlas-inventory").enabledInHierarchy,"Inventário inexistente fica desativado");
        float health=hud.Player.Health;hud.Player.Health=75;yield return null;
        Check(hud.Root.Q<Label>("stat-heart-value").text=="75","Vida exibida vem do personagem e não da imagem");
        hud.Player.Health=health;yield return null;
        Check(hud.BlocksPointer(new Vector2(50,Screen.height-50)),"Perfil bloqueia interação com câmera");
        Check(!hud.BlocksPointer(new Vector2(Screen.width*.6f,Screen.height*.5f)),"Área livre permite girar câmera");
        Check(hud.Root.Q(className:"atlas-safe-zone")==null && hud.Root.Q(className:"location")==null,"Cards marcados removidos");
        var controls=FindAnyObjectByType<MobileControls>();
        var left=new Vector2(Screen.width*.36f,Screen.height*.16f);
        var right=new Vector2(Screen.width*.65f,Screen.height*.4f);
        controls.ProcessTouch(10,left,Vector2.zero,TouchPhase.Began);
        controls.ProcessTouch(10,left+Vector2.up*200,Vector2.up*200,TouchPhase.Moved);
        Check(MobileControls.Movement.y>.99f && MobileControls.Movement.magnitude<=1.001f,"Joystick esquerdo limita movimento ao raio");
        controls.ProcessTouch(11,right,Vector2.zero,TouchPhase.Began);
        controls.ProcessTouch(11,right+Vector2.right*30,Vector2.right*30,TouchPhase.Moved);
        Check(MobileControls.Look.x>0 && MobileControls.Movement.y>.99f,"Dois dedos controlam movimento e câmera juntos");
        controls.ProcessTouch(10,left,Vector2.zero,TouchPhase.Canceled);
        Check(MobileControls.Movement==Vector2.zero,"Cancelar dedo interrompe movimento");
        controls.ProcessTouch(11,right,Vector2.zero,TouchPhase.Ended);
        controls.ProcessTouch(12,new Vector2(50,Screen.height-50),Vector2.zero,TouchPhase.Began);
        controls.ProcessTouch(12,left,Vector2.one*30,TouchPhase.Moved);
        Check(MobileControls.Movement==Vector2.zero,"Toque iniciado no HUD não vira joystick");
        yield return Capture("01-hud");
        Submit(hud.Root.Q<Button>("nav-menu"));yield return null;
        Check(hud.CurrentPanel=="menu","Botão abre menu");
        Check(hud.Root.Q("hud-compass").resolvedStyle.display==DisplayStyle.None,"Menu não deixa a bússola aparecer cortada");
        yield return Capture("02-menu");
        hud.OpenPanel("map");yield return new WaitForSecondsRealtime(.3f);
        Check(hud.CurrentPanel=="map","Mapa abre");yield return Capture("03-map");
        hud.ClosePanel();Submit(hud.Root.Q<Button>("nav-phone"));yield return null;
        Check(hud.CurrentPanel=="phone","Botão de celular abre atalhos");
        hud.ClosePanel();Submit(hud.Root.Q<Button>("nav-session"));yield return null;
        Check(hud.CurrentPanel=="missions"&&MissionCatalog.Data.missions.Length==17,"Prancheta abre catálogo com 10 missões de tutorial e 7 normais");
        hud.OpenPanel("player");yield return null;
        string originalName=hud.PlayerName;
        var field=hud.Root.Q<TextField>("player-name-input");field.value="TESTE HUD";
        Submit(hud.Root.Q<Button>("save-name"));yield return null;
        Check(hud.PlayerName=="TESTE HUD","Perfil salva nome pelo botão");
        hud.SaveName(originalName);field.value=originalName;yield return new WaitForSecondsRealtime(2.8f);
        yield return Capture("04-player");
        hud.OpenPanel("settings");yield return null;
        var slider=hud.Root.Q<Slider>();
        float old=slider.value;slider.value=1.3f;yield return null;
        Check(Mathf.Abs(FindAnyObjectByType<ThirdPersonCamera>().sensitivity-1.3f)<.01f,"Slider altera sensibilidade");
        slider.value=old;yield return Capture("05-settings");
        hud.ClosePanel();yield return null;
        Check(!hud.IsPanelOpen,"Menu fecha");
        Check(hud.Root.Q("hud-motion").resolvedStyle.display==DisplayStyle.Flex,"Fechar menu restaura indicador de movimento");
        Submit(hud.Root.Q<Button>("action-sprint"));yield return null;
        Check(hud.SprintRequested,"Botão ativa corrida");
        Submit(hud.Root.Q<Button>("action-sprint"));yield return null;
        Check(!hud.SprintRequested,"Botão desativa corrida");
        var camera=FindAnyObjectByType<ThirdPersonCamera>();camera.yaw=90;
        Submit(hud.Root.Q<Button>("action-camera"));yield return null;
        Check(Mathf.Abs(Mathf.DeltaAngle(camera.yaw,hud.Player.transform.eulerAngles.y))<.01f,"Botão centraliza câmera");
        hud.Player.DemoMode=true;hud.Player.DemoInput=Vector2.up;
        yield return new WaitForSecondsRealtime(.6f);
        hud.Player.DemoInput=Vector2.zero;yield return null;
        Submit(hud.Root.Q<Button>("action-reset"));yield return null;
        Check(Vector3.ProjectOnPlane(hud.Player.transform.position,Vector3.up).magnitude<.1f,"Botão retorna ao ponto inicial");
        hud.SetVisible(false);yield return null;Check(!hud.BlocksPointer(new Vector2(50,Screen.height-50)),"HUD oculto libera ponteiro");
        hud.SetVisible(true);yield return new WaitForSecondsRealtime(2.8f);
        Screen.SetResolution(960,540,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.6f);
        hud.RefreshMaps();yield return null;
        yield return Capture("06-compact");
        Finish();
    }
    void Submit(Button button)
    {
        if(button==null){errors.Add("Botão não encontrado");return;}
        button.Focus();using(var e=NavigationSubmitEvent.GetPooled())button.SendEvent(e);
    }
    IEnumerator Capture(string name)
    {
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(folder,name+".png"));
        yield return null;yield return null;
    }
    bool Check(bool value,string text)
    {
        checks.Add((value?"OK: ":"FAIL: ")+text);if(!value)errors.Add(text);return value;
    }
    void Finish()
    {
        File.WriteAllText(Path.Combine(folder,"validation.txt"),string.Join("\n",checks)+"\nERRORS="+errors.Count+"\n"+string.Join("\n",errors));
        Application.Quit(errors.Count==0?0:1);
    }
}
