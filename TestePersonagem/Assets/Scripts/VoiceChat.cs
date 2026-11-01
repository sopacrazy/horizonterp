using System;
using System.Collections.Generic;
using UnityEngine;
using Hbrp.Realtime;

// Ordered PCM blocks ride on the authenticated multiplayer sync channel.
// The microphone remains off until the player taps its HUD icon.
public sealed class VoiceChat : MonoBehaviour
{
    public static VoiceChat Instance {get;private set;}
    bool legacyTransmitting;
    VivoxVoice vivox;
    public bool UsingVivox=>vivox&&vivox.Configured;
    public bool Transmitting=>vivox&&vivox.Configured?vivox.Transmitting:legacyTransmitting;
    bool legacyRadio;
    public bool RadioTransmitting=>vivox&&vivox.Configured?vivox.RadioTransmitting:legacyRadio;
    public bool RadioRequested=>legacyRadio||vivox&&vivox.RadioRequested;
    public bool IsPlayerSpeaking(string uid)=>vivox&&vivox.IsPlayerSpeaking(uid);
    const int Rate=8000;
    const int MaxSamples=800;
    AudioClip recording;
    int readPosition,sequence;
    struct Frame {public int sequence;public string encoded;}
    readonly Queue<Frame> captured=new Queue<Frame>();
    public int DroppedCaptureFrames {get;private set;}
    float[] captureSamples;
    byte[] capturePcm;
    public static void Create(ThirdPersonPlayer player)
    {
        if(player&&!player.GetComponent<VoiceChat>())player.gameObject.AddComponent<VoiceChat>();
    }
    void Awake(){Instance=this;vivox=gameObject.AddComponent<VivoxVoice>();}
    public void Toggle()
    {
        if(Transmitting||(vivox&&vivox.Requested)){Stop();GameHud.Instance?.ShowAccountMessage("Microfone desligado");return;}
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
        if(vivox&&vivox.Configured){vivox.Talk(radio);return;}
        if(Transmitting||!AccountSession.HasCharacter||!MultiplayerWorld.Instance||!MultiplayerWorld.Instance.Connected)return;
        if(Microphone.devices.Length==0){GameHud.Instance?.ShowAccountMessage("Microfone não encontrado");return;}
        recording=Microphone.Start(null,true,1,Rate);
        if(!recording){GameHud.Instance?.ShowAccountMessage("Não foi possível abrir o microfone");return;}
        readPosition=0;captured.Clear();
        captureSamples=new float[MaxSamples];capturePcm=new byte[MaxSamples*2];
        legacyTransmitting=true;legacyRadio=radio;
        if(!radio)GameHud.Instance?.ShowAccountMessage("Microfone ligado");
    }
    void Update()
    {
        if(legacyTransmitting&&vivox&&vivox.Configured)
        {bool radio=legacyRadio;Microphone.End(null);legacyTransmitting=legacyRadio=false;recording=null;captured.Clear();vivox.Talk(radio);}
        Collect();
    }
    void Collect()
    {
        if(!legacyTransmitting||!recording||!Microphone.IsRecording(null))return;
        int position=Microphone.GetPosition(null);
        if(position<0)return;
        int available=(position-readPosition+recording.samples)%recording.samples;
        // Retain ordered 100 ms blocks. Only discard when more than 300 ms behind.
        if(available>MaxSamples*3)
        {
            int skipped=(available-MaxSamples*3)/MaxSamples;
            readPosition=(readPosition+skipped*MaxSamples)%recording.samples;
            DroppedCaptureFrames+=skipped;available-=skipped*MaxSamples;
        }
        while(available>=MaxSamples)
        {
            if(!recording.GetData(captureSamples,readPosition))break;
            readPosition=(readPosition+MaxSamples)%recording.samples;available-=MaxSamples;
            for(int i=0;i<MaxSamples;i++)
            {
                short sample=(short)Mathf.RoundToInt(Mathf.Clamp(captureSamples[i],-1,1)*32767);
                capturePcm[i*2]=(byte)sample;capturePcm[i*2+1]=(byte)(sample>>8);
            }
            while(captured.Count>=3){captured.Dequeue();DroppedCaptureFrames++;}
            captured.Enqueue(new Frame{sequence=++sequence,encoded=Convert.ToBase64String(capturePcm)});
        }
    }
    public string Capture(out int voiceSequence)
    {
        voiceSequence=0;if(vivox&&vivox.Configured)return null;
        Collect();
        if(!Transmitting||captured.Count==0)return null;
        var frame=captured.Dequeue();voiceSequence=frame.sequence;return frame.encoded;
    }
    public void Stop()
    {
        vivox?.StopTalk();
        if(!legacyTransmitting)return;
        Microphone.End(null);legacyTransmitting=false;legacyRadio=false;recording=null;captured.Clear();
    }
    public void StopRadio(){if(RadioRequested||RadioTransmitting)Stop();}
    void OnApplicationPause(bool paused){if(paused)Stop();}
    void OnDestroy(){Stop();if(Instance==this)Instance=null;}
}
