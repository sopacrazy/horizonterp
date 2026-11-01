using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Core;
using Unity.Services.Vivox;
using UnityEngine;

namespace Hbrp.Realtime
{
    // Reuses the HBRP account through server-issued VATs; no signing key exists in the client.
    public sealed class VivoxVoice:MonoBehaviour,IVivoxTokenProvider
    {
        [Serializable] sealed class TokenRequest {public string action,channelUri,fromUserUri;}
        [Serializable] sealed class TokenReply {public string token;}
        public bool Configured=>setup!=null&&setup.available;
        public bool Connected=>initialized&&joined&&VivoxService.Instance.IsLoggedIn;
        public int ParticipantCount
        {get{if(!Connected)return 0;int count=0;foreach(var channel in VivoxService.Instance.ActiveChannels.Values)count+=channel.Count;return count;}}
        public bool Requested=>desired!=0;
        public bool RadioRequested=>desired==2;
        public bool IsPlayerSpeaking(string uid)
        {
            if(!Connected||string.IsNullOrEmpty(uid))return false;
            foreach(var channel in VivoxService.Instance.ActiveChannels.Values)
                foreach(var participant in channel)
                    if(!participant.IsSelf&&participant.PlayerId=="hbrp-"+uid&&!participant.IsMuted&&participant.SpeechDetected)return true;
            return false;
        }
        public bool Transmitting {get;private set;}
        public bool RadioTransmitting {get;private set;}
        public string Status {get;private set;}="Vivox aguardando ativação";
        VivoxSetup setup;
        readonly SemaphoreSlim gate=new SemaphoreSlim(1,1);
        bool initialized,joined,disposed;
        string character,radioChannel;
        int desired,generation;
        float nextPosition,nextRadioCheck;
        IEnumerator Start()
        {
            while(!AccountSession.HasCharacter||!MultiplayerWorld.Instance||!MultiplayerWorld.Instance.Connected)yield return null;
            character=AccountSession.Instance.State.characterId;
            var config=AccountSession.Instance.ServiceJsonAsync("/v1/services");
            while(!config.IsCompleted)yield return null;
            if(config.Status==TaskStatus.RanToCompletion&&!string.IsNullOrEmpty(config.Result))
            {
                try{setup=JsonUtility.FromJson<RealtimeServices>(config.Result)?.vivox;}catch(ArgumentException){}
            }
            if(Configured){Status="Vivox pronto para conectar";if(MicrophonePermission())RequestMode(0);}
        }
        static bool MicrophonePermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone);
#else
            return true;
#endif
        }
        public void Talk(bool radio){if(Configured)RequestMode(radio?2:1);}
        public void StopTalk(){if(Configured)RequestMode(0);}
        void RequestMode(int mode)
        {
            if(disposed)return;
            desired=mode;int visit=++generation;
            Transmitting=false;RadioTransmitting=false;
            // Mute synchronously before any awaited channel switch. Radio cannot spill into proximity.
            if(initialized)VivoxService.Instance.MuteInputDevice();
            _=ApplyMode(visit);
        }
        async Task EnsureConnected()
        {
            if(disposed||!AccountSession.HasCharacter||AccountSession.Instance.State.characterId!=character)throw new InvalidOperationException("Account changed");
            if(string.IsNullOrEmpty(Application.cloudProjectId))throw new InvalidOperationException("Vincule o projeto ao Unity Cloud e ative o Vivox.");
            if(!initialized)
            {
                var options=new InitializationOptions();options.SetVivoxCredentials(setup.server,setup.domain,setup.issuer);
                await UnityServices.InitializeAsync(options);
                if(disposed)throw new InvalidOperationException("Voice session closed");
                VivoxService.Instance.SetTokenProvider(this);
                await VivoxService.Instance.InitializeAsync();initialized=true;VivoxService.Instance.MuteInputDevice();
            }
            if(disposed)throw new InvalidOperationException("Voice session closed");
            if(!VivoxService.Instance.IsLoggedIn)
                await VivoxService.Instance.LoginAsync(new LoginOptions{PlayerId=setup.playerId,DisplayName=PlayerDisplayName.From(AccountSession.Instance.State.firstName,AccountSession.Instance.State.name)});
            if(!joined)
            {
                await VivoxService.Instance.JoinPositionalChannelAsync(setup.proximity,ChatCapability.AudioOnly,
                    new Channel3DProperties(35,2,1,AudioFadeModel.InverseByDistance),new ChannelOptions{MakeActiveChannelUponJoining=false});
                joined=true;
            }
        }
        async Task ApplyMode(int visit)
        {
            await gate.WaitAsync();
            try
            {
                if(disposed||visit!=generation)return;
                await EnsureConnected();
                if(disposed||visit!=generation)return;
                string expectedRadio=PoliceRadio.Instance&&PoliceRadio.Instance.Connected?setup.radioPrefix+PoliceRadio.Instance.Channel:null;
                if(radioChannel!=expectedRadio)
                {
                    if(radioChannel!=null)await VivoxService.Instance.LeaveChannelAsync(radioChannel);
                    radioChannel=null;
                    if(expectedRadio!=null)
                    {await VivoxService.Instance.JoinGroupChannelAsync(expectedRadio,ChatCapability.AudioOnly,new ChannelOptions{MakeActiveChannelUponJoining=false});radioChannel=expectedRadio;}
                }
                if(disposed||visit!=generation)return;
                if(desired==2&&radioChannel==null)throw new InvalidOperationException("Conecte-se ao rádio policial.");
                await VivoxService.Instance.SetChannelTransmissionModeAsync(desired==0?TransmissionMode.None:TransmissionMode.Single,desired==2?radioChannel:setup.proximity);
                if(disposed||visit!=generation)return;
                if(desired!=0)VivoxService.Instance.UnmuteInputDevice();
                Transmitting=desired!=0;RadioTransmitting=desired==2;Status=desired==0?"Vivox conectado":desired==2?"Transmitindo rádio":"Microfone ligado · Vivox";
            }
            catch(Exception e)
            {
                Transmitting=RadioTransmitting=false;Status="Vivox indisponível";
                if(initialized)VivoxService.Instance.MuteInputDevice();
                if(this&&!disposed&&desired!=0)GameHud.Instance?.ShowAccountMessage(e is InvalidOperationException?e.Message:"Não foi possível conectar ao Vivox.");
                Debug.LogWarning("VIVOX_CONNECT_FAILED "+e.GetType().Name);
            }
            finally{gate.Release();}
        }
        void Update()
        {
            if(!Configured)return;
            if(!AccountSession.HasCharacter||AccountSession.Instance.State.characterId!=character)
            {if(!disposed)Close();return;}
            if(!initialized||!VivoxService.Instance.IsLoggedIn)return;
            if(joined&&Time.unscaledTime>=nextPosition)
            {nextPosition=Time.unscaledTime+.1f;VivoxService.Instance.Set3DPosition(gameObject,setup.proximity);}
            if(Time.unscaledTime>=nextRadioCheck)
            {
                nextRadioCheck=Time.unscaledTime+.5f;
                string expected=PoliceRadio.Instance&&PoliceRadio.Instance.Connected?setup.radioPrefix+PoliceRadio.Instance.Channel:null;
                if(gate.CurrentCount>0&&expected!=radioChannel)RequestMode(expected==null&&desired==2?0:desired);
            }
        }
        public async Task<string> GetTokenAsync(string issuer=null,TimeSpan? expiration=null,string targetUserUri=null,string action=null,string channelUri=null,string fromUserUri=null,string realm=null)
        {
            if(disposed||issuer!=setup.issuer||!string.IsNullOrEmpty(targetUserUri))
                throw new InvalidOperationException("Invalid voice token scope");
            string json=await AccountSession.Instance.ServiceJsonAsync("/v1/voice/token",JsonUtility.ToJson(new TokenRequest{action=action,channelUri=channelUri,fromUserUri=fromUserUri}));
            var reply=string.IsNullOrEmpty(json)?null:JsonUtility.FromJson<TokenReply>(json);
            if(string.IsNullOrEmpty(reply?.token))throw new InvalidOperationException("Canal de voz não autorizado ou Vivox não configurado.");
            return reply.token;
        }
        async void Close()
        {
            if(disposed)return;
            disposed=true;generation++;Transmitting=RadioTransmitting=false;
            if(initialized)VivoxService.Instance.MuteInputDevice();
            await gate.WaitAsync();
            try{if(initialized&&VivoxService.Instance.IsLoggedIn)await VivoxService.Instance.LogoutAsync();}
            catch(Exception e){Debug.LogWarning("VIVOX_LOGOUT_FAILED "+e.GetType().Name);}
            finally{gate.Release();}
        }
        void OnApplicationPause(bool paused){if(paused)StopTalk();}
        void OnDestroy(){Close();}
    }
}
