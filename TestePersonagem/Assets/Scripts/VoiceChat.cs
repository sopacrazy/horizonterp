using System;
using UnityEngine;

// Short PCM frames ride on the authenticated multiplayer sync channel.
// The microphone remains off until the player taps its HUD icon.
public sealed class VoiceChat : MonoBehaviour
{
    public static VoiceChat Instance {get;private set;}
    public bool Transmitting {get;private set;}
    public bool RadioTransmitting {get;private set;}
    const int Rate=8000;
    const int MaxSamples=800;
    AudioClip recording;
    int readPosition,sequence;
    public static void Create(ThirdPersonPlayer player)
    {
        if(player&&!player.GetComponent<VoiceChat>())player.gameObject.AddComponent<VoiceChat>();
    }
    void Awake(){Instance=this;}
    public void Toggle()
    {
        if(Transmitting){Stop();GameHud.Instance?.ShowAccountMessage("Microfone desligado");return;}
        if(!AccountSession.HasCharacter||!MultiplayerWorld.Instance||!MultiplayerWorld.Instance.Connected)
        {GameHud.Instance?.ShowAccountMessage("Conecte-se à sala para usar a voz");return;}
#if UNITY_ANDROID && !UNITY_EDITOR
        if(!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
        {
            var callbacks=new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted+=_=>{if(this)StartRecording(false);};
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone,callbacks);
            return;
        }
#endif
        StartRecording(false);
    }
    public void BeginRadio()
    {
        if(!PoliceRadio.Instance||!PoliceRadio.Instance.Connected||!AdminPlayerPanel.PoliceDuty)return;
        if(Transmitting)Stop();
#if UNITY_ANDROID && !UNITY_EDITOR
        if(!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
        {
            var callbacks=new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted+=_=>{if(this&&PoliceRadio.Instance&&PoliceRadio.Instance.Pressed)StartRecording(true);};
            callbacks.PermissionDenied+=_=>GameHud.Instance?.ShowAccountMessage("Permissão de microfone negada");
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone,callbacks);
            return;
        }
#endif
        StartRecording(true);
    }
    void StartRecording(bool radio)
    {
        if(Transmitting||!AccountSession.HasCharacter||!MultiplayerWorld.Instance||!MultiplayerWorld.Instance.Connected)return;
        if(Microphone.devices.Length==0){GameHud.Instance?.ShowAccountMessage("Microfone não encontrado");return;}
        recording=Microphone.Start(null,true,1,Rate);
        if(!recording){GameHud.Instance?.ShowAccountMessage("Não foi possível abrir o microfone");return;}
        readPosition=0;Transmitting=true;RadioTransmitting=radio;
        if(!radio)GameHud.Instance?.ShowAccountMessage("Microfone ligado");
    }
    public string Capture(out int voiceSequence)
    {
        voiceSequence=0;
        if(!Transmitting||!recording||!Microphone.IsRecording(null))return null;
        int position=Microphone.GetPosition(null);
        int available=(position-readPosition+Rate)%Rate;
        if(available<MaxSamples)return null;
        // If HTTP is delayed, use the freshest 100 ms of speech.
        if(available>MaxSamples)readPosition=(position-MaxSamples+Rate)%Rate;
        var floats=new float[MaxSamples];
        recording.GetData(floats,readPosition);
        readPosition=(readPosition+MaxSamples)%Rate;
        var pcm=new byte[MaxSamples*2];
        for(int i=0;i<MaxSamples;i++)
        {
            short sample=(short)Mathf.RoundToInt(Mathf.Clamp(floats[i],-1,1)*32767);
            pcm[i*2]=(byte)sample;pcm[i*2+1]=(byte)(sample>>8);
        }
        voiceSequence=++sequence;
        return Convert.ToBase64String(pcm);
    }
    public static void Play(AudioSource source,string encoded,float volume=1f)
    {
        if(!source||string.IsNullOrEmpty(encoded))return;
        byte[] pcm;
        try{pcm=Convert.FromBase64String(encoded);}catch{return;}
        if(pcm.Length!=MaxSamples*2)return;
        var samples=new float[MaxSamples];
        for(int i=0;i<MaxSamples;i++)samples[i]=(short)(pcm[i*2]|pcm[i*2+1]<<8)/32768f;
        var clip=AudioClip.Create("Voz próxima",MaxSamples,1,Rate,false);
        clip.SetData(samples,0);
        source.PlayOneShot(clip,volume);
        Destroy(clip,.3f);
    }
    public void Stop()
    {
        if(!Transmitting)return;
        Microphone.End(null);Transmitting=false;RadioTransmitting=false;recording=null;
    }
    void OnApplicationPause(bool paused){if(paused)Stop();}
    void OnDestroy(){Stop();if(Instance==this)Instance=null;}
}
