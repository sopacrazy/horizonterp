using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

// Explicit offline boot regression. No real token or native credential is read.
public sealed class RememberedCityEntryVerification : MonoBehaviour
{
    static string replyMode="ok";
    static int missionReads;
    public static void Install()
    {
        SessionVault.TestRead=()=>new string('V',43);
        SessionVault.TestWrite=_=>{};SessionVault.TestClear=()=>{};
        AccountSession.VerificationTransport=Reply;
        TutorialJourney.AllowVerification=true;
        DontDestroyOnLoad(new GameObject("Verificação reentrada").AddComponent<RememberedCityEntryVerification>().gameObject);
    }
    static IEnumerator Reply(string method,string path,string body,bool authenticated,Action<ApiReply,string> done)
    {
        yield return null;
        if(replyMode=="offline"){done(null,"Sem conexão de teste");yield break;}
        if(replyMode=="expired"){done(new ApiReply {code="UNAUTHORIZED"},"Sessão de teste expirada");yield break;}
        if(path=="/v1/missions/arrival")
        {
            missionReads++;
            var missionOnly=JsonUtility.FromJson<ApiReply>("{\"mission\":{\"phase\":\"available\"}}");
            Debug.Log("SA_MISSION_ONLY_JSON emptyState="+(missionOnly.state!=null&&string.IsNullOrEmpty(missionOnly.state.characterId)));
            done(missionOnly,null);yield break;
        }
        done(new ApiReply {state=new ApiPlayerState {accountId="offline",characterId="offline",uid="18",
            firstName="Teste",name="Teste",gender="male",model="bybeyby",world="centro_teste",version="1",
            level=1,health=100,energy=100,hunger=100,thirst=100,cashCents="0",xp="0",
            x=2489.2969f,y=62.296875f,z=1668.5f,yaw=40,cameraYaw=40,cameraPitch=12}},null);
    }
    IEnumerator Start()
    {
        float deadline=Time.realtimeSinceStartup+120;
        Button play=null;
        while(Time.realtimeSinceStartup<deadline)
        {
            play=AccountSession.Instance?.Document?.rootVisualElement.Q<Button>("remembered-play");
            if(play!=null&&play.enabledInHierarchy&&AccountSession.HasCharacter)break;
            yield return null;
        }
        if(play==null){Finish(false,"Jogar não apareceu");yield break;}
        play.Focus();using(var e=NavigationSubmitEvent.GetPooled())play.SendEvent(e);
        while(StartupLoader.IsLoading&&Time.realtimeSinceStartup<deadline)yield return null;
        yield return new WaitForSecondsRealtime(5);
        var a=AccountSession.Instance;
        // The tutorial is disabled in normal play; explicitly exercise the mission-only
        // response that used to replace the authenticated character with an empty state.
        yield return a.ArrivalRequest(null,_=>{});
        bool presentation=true;
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-local-network-ui")>=0)
        {
            var hud=GameHud.Instance;
            var marker=hud.Root.Q<HudIcon>(className:"marker");
            presentation=marker!=null&&marker.Ink.b>.9f&&marker.Ink.r<.3f;
            hud.OpenPanel("map");yield return null;
            var fullMarker=hud.Root.Q<HudIcon>(className:"city-map-player");
            presentation&=fullMarker!=null&&fullMarker.Ink.b>.9f&&fullMarker.Ink.r<.3f;
            hud.ClosePanel();
            var original=hud.Player.transform.rotation;
            float start=hud.MinimapYaw,target=Mathf.Repeat(start+90,360);
            hud.Player.transform.rotation=Quaternion.Euler(0,target,0);
            yield return new WaitForEndOfFrame();
            float first=Mathf.Abs(Mathf.DeltaAngle(start,hud.MinimapYaw));
            yield return new WaitForSecondsRealtime(.8f);
            presentation&=first<45&&Mathf.Abs(Mathf.DeltaAngle(target,hud.MinimapYaw))<1;
            hud.Player.transform.rotation=original;
            var speaker=new GameObject("Áudio de teste local");var audio=speaker.AddComponent<AudioSource>();
            var playback=new VoicePlayback(audio);string packet=Convert.ToBase64String(new byte[1600]);
            playback.Push(1,packet);var stream=audio.clip;playback.Push(2,packet);
            presentation&=stream&&stream==audio.clip&&audio.loop;
            playback.Dispose();Destroy(speaker);
            Debug.Log("LOCAL_NETWORK_PRESENTATION_"+(presentation?"PASSED":"FAILED")+" firstTurn="+first);
        }
        bool pass=missionReads>0&&a&&a.ReadyToEnterCity&&!a.AuthVisible&&AccountSession.HasCharacter&&
            a.Document.rootVisualElement.resolvedStyle.display==DisplayStyle.None&&presentation;
        string detail="authenticated="+AccountSession.Authenticated+" ready="+a?.ReadyToEnterCity+
            " authVisible="+a?.AuthVisible+" root="+a?.Document.rootVisualElement.resolvedStyle.display+" missionReads="+missionReads;
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.GetFullPath(Path.Combine(Application.dataPath,"../../../../tools/map-integration/session-entry.png")));
        yield return null;yield return null;
        yield return a.ConfirmSessionAfterUnauthorized(new string('V',43));
        pass&=AccountSession.HasCharacter&&a.ReadyToEnterCity&&!a.AuthVisible;
        yield return new WaitForSecondsRealtime(2.1f);
        replyMode="offline";
        yield return a.ConfirmSessionAfterUnauthorized(new string('V',43));
        pass&=AccountSession.HasCharacter&&a.ReadyToEnterCity&&!a.AuthVisible;
        detail+=" offlinePreserved="+AccountSession.HasCharacter;
        replyMode="expired";
        yield return a.ConfirmSessionAfterUnauthorized(new string('V',43));
        pass&=!AccountSession.Authenticated&&!a.ReadyToEnterCity&&a.AuthVisible;
        detail+=" expiredRejected="+!AccountSession.Authenticated;
        Finish(pass,detail);
    }
    void Finish(bool pass,string detail)
    {
        Debug.Log("SA_REMEMBERED_ENTRY_"+(pass?"PASSED ":"FAILED ")+detail);
        // The test transport stays installed until the process exits.
        Application.Quit(pass?0:1);
    }
}
