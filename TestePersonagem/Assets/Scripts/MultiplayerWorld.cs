using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Hbrp.Realtime;

// Two-player HTTPS prototype, sharing the API's authenticated and pinned TLS channel.
public sealed class MultiplayerWorld : MonoBehaviour
{
    public static MultiplayerWorld Instance {get;private set;}
    public bool Connected {get;private set;}
    public int PlayerCount {get;private set;}
    public int RemoteCount=>remotes.Count;
    public string Status {get;private set;}="CONECTANDO";
    public int ReceivedFrames {get;private set;}
    public bool SawRemoteJump {get;private set;}
    public float RemoteTravel {get;private set;}
    public int LastChatId {get;private set;}
    public int ServerHour {get;private set;}=-1;
    int lastTeleportId;
    string chatEpoch;
    readonly Dictionary<string,Remote> remotes=new Dictionary<string,Remote>();
    readonly string clientId=Guid.NewGuid().ToString();
    ThirdPersonPlayer player;
    PoliceCarWorld policeCarWorld;
    RealtimeMovement realtime;
    public string RealtimeClientId=>clientId;
    public bool UsingUdp=>realtime&&realtime.Active;
    public int UdpSnapshots=>realtime?.Client?.SnapshotsReceived??0;
    public float UdpRoundTrip=>realtime?.Client?.RoundTripMs??0;
    public bool HasLatencySample=>UsingUdp?realtime.Client.PingsReceived>0:ReceivedFrames>0;
    public float LatencyMilliseconds=>UsingUdp?UdpRoundTrip:RoundTripMilliseconds;
    public float LatencyAge=>UsingUdp?realtime.Client.PongAge:Time.realtimeSinceStartup-lastSuccessfulSync;
    public bool VoiceStateAvailable {get;private set;}
    public bool PoliceVehicleAvailable {get;private set;}
    public bool CombatAvailable {get;private set;}
    public bool CombatReady {get;private set;}
    public void GetRadarPlayers(List<RadarPlayer> result)
    {
        result.Clear();if(!Connected)return;
        foreach(var pair in remotes)
        {
            var remote=pair.Value;
            if(remote.Transform)result.Add(new RadarPlayer {characterId=pair.Key,uid=remote.Uid,name=remote.DisplayName,position=remote.Transform.position});
        }
    }
    Label statusLabel;
    int sequence;
    bool paused;
    int connectionFailures;
    string lastFailure;
    readonly SyncCadence cadence=new SyncCadence();
    int lastAppliedSequence,diagnosticFrames;
    float lastSuccessfulSync,diagnosticAt,maxFrameSeconds;
    public float RoundTripMilliseconds {get;private set;}
    public float UpdatesPerSecond {get;private set;}
    public int PendingRequests=>cadence.Pending;

    public static void Create(ThirdPersonPlayer player)
    {
        if(Instance||AccountSession.VerificationTransport!=null)return;
        var world=player.gameObject.AddComponent<MultiplayerWorld>();world.player=player;
    }
    void Awake(){Instance=this;}
    IEnumerator Start()
    {
        policeCarWorld=gameObject.AddComponent<PoliceCarWorld>();policeCarWorld.Initialize(player);
        realtime=gameObject.AddComponent<RealtimeMovement>();realtime.Initialize(this,player);
        while(player&&(!AccountSession.HasCharacter||StartupLoader.IsLoading))yield return null;
        diagnosticAt=Time.realtimeSinceStartup;
        while(player&&AccountSession.HasCharacter)
        {
            // Legacy PCM produces one block per 100 ms; retain that send rate until Vivox is enabled.
            bool legacyVoice=VoiceChat.Instance&&VoiceChat.Instance.Transmitting&&!VoiceChat.Instance.UsingVivox;
            // Combat events use independent reliable sequence numbers. Poll at 10 Hz
            // while this server supports combat, even when poses use the UDP relay.
            cadence.Interval=UsingUdp&&!legacyVoice&&!CombatAvailable ? .2 : .1;
            if(!paused&&!player.MapTeleportLoading&&cadence.TryBegin(Time.realtimeSinceStartupAsDouble))
                StartCoroutine(SyncOnce());
            yield return null;
        }
        Connected=false;ClearRemotes();SetStatus("DESCONECTADO");
    }
    IEnumerator SyncOnce()
    {
        float started=Time.realtimeSinceStartup;
        string characterId=AccountSession.Instance.State.characterId;
        try
        {
            int vehicleRevision=PoliceVehicleController.StateRevision;
            var police=PoliceVehicleController.Driving?PoliceVehicleController.Active:null;
            var position=police?police.transform.position:player.transform.position;
            var frame=new MultiplayerFrame {clientId=clientId,sequence=++sequence,
                x=position.x,y=position.y,z=position.z,yaw=Mathf.Repeat(police?police.transform.eulerAngles.y:player.transform.eulerAngles.y,360),
                speed=Mathf.Clamp(police?Mathf.Abs(police.Speed):MissionVehicle.Driver==player?MissionVehicle.DriverSpeed:player.CurrentSpeed,0,8),driving=police||MissionVehicle.Driver==player,jumping=player.IsJumping,jumpPhase=player.JumpPhase,
                afterChat=LastChatId};
            int voiceSequence=0;
            string voice=VoiceChat.Instance ? VoiceChat.Instance.Capture(out voiceSequence) : null;
            // Losing the radio connection must never redirect private radio speech to proximity chat.
            if(VoiceChat.Instance&&VoiceChat.Instance.RadioTransmitting&&(!PoliceRadio.Instance||!PoliceRadio.Instance.Connected))voice=null;
            bool combatClient=CombatAvailable;
            string json=JsonUtility.ToJson(frame);
            if(combatClient)json=json.Substring(0,json.Length-1)+",\"combatClient\":true}";
            if(police&&PoliceVehicleAvailable)json=json.Substring(0,json.Length-1)+",\"policeCarId\":\""+police.Id+"\"}";
            // Old installed servers strictly validate fields; negotiate before sending the new state.
            if(VoiceStateAvailable)
            {
                bool transmitting=VoiceChat.Instance&&VoiceChat.Instance.Transmitting;
                bool radio=transmitting&&VoiceChat.Instance.RadioTransmitting;
                json=json.Substring(0,json.Length-1)+",\"voiceActive\":"+(transmitting&&!radio?"true":"false")+
                    ",\"radioActive\":"+(radio?"true":"false")+"}";
            }
            if(!string.IsNullOrEmpty(voice))
            {
                if(VoiceChat.Instance.RadioTransmitting&&PoliceRadio.Instance&&PoliceRadio.Instance.Connected)
                    json=json.Substring(0,json.Length-1)+",\"radioVoice\":\""+voice+"\",\"radioSequence\":"+voiceSequence+
                        ",\"radioChannel\":"+PoliceRadio.Instance.Channel+"}";
                else json=json.Substring(0,json.Length-1)+",\"voice\":\""+voice+"\",\"voiceSequence\":"+voiceSequence+"}";
            }
            MultiplayerReply reply=null;
            yield return AccountSession.Instance.MultiplayerRequest("/v1/multiplayer/sync",json,r=>reply=r);
            if(!player||!AccountSession.HasCharacter||AccountSession.Instance.State.characterId!=characterId||paused)yield break;
            // Concurrent HTTPS replies can arrive out of order. Never move the room backwards.
            if(reply?.code=="STALE_FRAME"||frame.sequence<=lastAppliedSequence)yield break;
            if(reply?.code=="POLICE_CAR_FORBIDDEN"&&police)
            {
                // An exit may have released the seat while this driving frame was in flight.
                // A current denial releases controls; an older one cannot undo a newer action.
                if(vehicleRevision==PoliceVehicleController.StateRevision)police.Abort();
                yield break;
            }
            if(!string.IsNullOrEmpty(voice)&&(reply?.code=="INVALID_FRAME"||reply?.code=="RADIO_FORBIDDEN"))
            {VoiceChat.Instance?.Stop();GameHud.Instance?.ShowAccountMessage("Voz ainda indisponível no servidor");}
            if(reply!=null&&string.IsNullOrEmpty(reply.code)&&reply.peers!=null)
            {
                lastAppliedSequence=frame.sequence;
                float roundTrip=Time.realtimeSinceStartup-started;
                RoundTripMilliseconds=RoundTripMilliseconds==0?roundTrip*1000:Mathf.Lerp(RoundTripMilliseconds,roundTrip*1000,.2f);
                diagnosticFrames++;lastSuccessfulSync=Time.realtimeSinceStartup;
                connectionFailures=0;lastFailure=null;Connected=true;PlayerCount=reply.count;ReceivedFrames++;
                VoiceStateAvailable=reply.voiceStateAvailable;
                PoliceVehicleAvailable=reply.policeVehicleAvailable;
                CombatAvailable=reply.combatAvailable;
                CombatReady=reply.combatAvailable&&combatClient;
                // Refresh permission changes for this authenticated character without
                // requiring logout. Older servers omit these fields and are ignored.
                if((reply.selfAccountRole=="user"&&reply.selfAdminLevel==0)||
                    (reply.selfAccountRole=="admin"&&reply.selfAdminLevel>0))
                {
                    var state=AccountSession.Instance.State;
                    if(state.accountRole!=reply.selfAccountRole||state.adminLevel!=reply.selfAdminLevel)
                    {
                        state.accountRole=reply.selfAccountRole;state.adminLevel=reply.selfAdminLevel;
                        GameHud.Instance?.ApplyAccountIdentity();
                    }
                }
                player.GetComponent<FighterCombo>()?.ApplyState(reply.selfCombat,reply.serverTime,roundTrip);
                SetStatus("ONLINE "+reply.count+"/"+reply.capacity);
                UpdatePeers(reply.peers,reply.serverTime,roundTrip);
                policeCarWorld.Apply(reply.policeCars,reply.selfPoliceCarId,new Vector3(reply.selfX,reply.selfY,reply.selfZ),vehicleRevision);
                player.SetCustody(reply.selfCuffed,reply.selfEscortedBy,new Vector3(reply.selfX,reply.selfY,reply.selfZ),reply.selfYaw);
                player.SetPoliceCarPassenger(reply.selfInPoliceCar,new Vector3(reply.selfX,reply.selfY,reply.selfZ),reply.selfYaw);
                ReceiveChat(reply);
                if(reply.teleport!=null&&reply.teleport.id>lastTeleportId)
                {
                    lastTeleportId=reply.teleport.id;
                    player.TeleportTo(new Vector3(reply.teleport.x,reply.teleport.y,reply.teleport.z));
                }
                int hour=reply.hourOverride?reply.serverHour:-1;
                if(hour!=ServerHour)
                {
                    ServerHour=hour;
                    if(hour>=0){var dayNight=FindAnyObjectByType<CityDayNight>();if(dayNight)dayNight.SetNight(hour<6||hour>=18);}
                }
            }
            else
            {
                string code=reply?.code;
                string failure=(code??"NO_REPLY")+": "+(reply?.error??"sem detalhes");
                if(failure!=lastFailure){lastFailure=failure;Debug.LogWarning("MULTIPLAYER_SYNC_FAILURE "+failure);}
                bool terminal=code=="UNAUTHORIZED"||code=="SESSION_IN_USE"||code=="ROOM_FULL";
                if(terminal||Time.realtimeSinceStartup-lastSuccessfulSync>.5f)connectionFailures++;
                if(terminal||connectionFailures>=3)
                {
                    Connected=false;PlayerCount=0;ClearRemotes();
                    SetStatus(code=="ROOM_FULL"?"SALA CHEIA":code=="SESSION_IN_USE"?"CONTA EM USO":code=="UNAUTHORIZED"?"SESSÃO ENCERRADA":"RECONECTANDO");
                    cadence.Backoff(Time.realtimeSinceStartupAsDouble+(terminal?2:1));
                }
            }
        }
        finally{cadence.Complete();}
    }
    void Update()
    {
        maxFrameSeconds=Mathf.Max(maxFrameSeconds,Time.unscaledDeltaTime);
        float elapsed=Time.realtimeSinceStartup-diagnosticAt;
        if(elapsed<10||StartupLoader.IsLoading)return;
        UpdatesPerSecond=diagnosticFrames/elapsed;
        int underruns=0;foreach(var remote in remotes.Values)underruns+=remote.VoiceUnderruns;
        Debug.Log($"NETWORK_LOCAL_STATS transport={(UsingUdp?"UDP":"HTTPS")} udpRttMs={realtime?.Client?.RoundTripMs??0:F0} rttMs={RoundTripMilliseconds:F0} syncHz={UpdatesPerSecond:F1} pending={cadence.Pending} frameMaxMs={maxFrameSeconds*1000:F0} voiceUnderruns={underruns} micDropped={VoiceChat.Instance?.DroppedCaptureFrames??0}");
        if(statusLabel!=null)statusLabel.tooltip=$"RTT {RoundTripMilliseconds:F0} ms · {UpdatesPerSecond:F1} atualizações/s";
        diagnosticAt=Time.realtimeSinceStartup;diagnosticFrames=0;maxFrameSeconds=0;
    }
    void SetStatus(string value)
    {
        Status=value;
        if(statusLabel==null&&GameHud.Instance)statusLabel=GameHud.Instance.Root.Q<Label>("multiplayer-status");
        if(statusLabel!=null)statusLabel.text=value;
    }
    void ReceiveChat(MultiplayerReply reply)
    {
        if(!string.IsNullOrEmpty(reply.chatEpoch)&&chatEpoch!=reply.chatEpoch)
        {chatEpoch=reply.chatEpoch;LastChatId=0;InGameChat.Instance?.ResetHistory();}
        if(reply.messages!=null)foreach(var message in reply.messages)ReceiveMessage(message);
    }
    void ReceiveMessage(ChatMessage message)
    {
        if(message==null||message.id<=LastChatId)return;
        LastChatId=message.id;InGameChat.Instance?.Receive(message);
    }
    public IEnumerator SendChat(string messageId,string text,Action<MultiplayerReply> done)
    {
        if(!Connected||paused){done(new MultiplayerReply {code="NOT_IN_ROOM",error="Aguarde a conexão com a sala."});yield break;}
        MultiplayerReply reply=null;
        var body=new ChatBody {clientId=clientId,messageId=messageId,text=text};
        yield return AccountSession.Instance.MultiplayerRequest("/v1/multiplayer/chat",JsonUtility.ToJson(body),r=>reply=r);
        // The next sync delivers all messages in order, including our own confirmed message.
        done(reply);
    }
    public void Attack(string attackId)
    {
        if(!Connected||!CombatReady||paused)return;
        Remote closest=null;float distance=float.MaxValue;
        foreach(var remote in remotes.Values)
        {
            if(!remote.CanPredictHit)continue;
            Vector3 delta=remote.Transform.position-player.transform.position;
            float flat=new Vector2(delta.x,delta.z).magnitude;
            if(flat>2.2f||Mathf.Abs(delta.y)>1.5f||flat>=distance)continue;
            Vector3 direction=Vector3.ProjectOnPlane(delta,Vector3.up);
            if(flat>=.15f&&Vector3.Dot(player.transform.forward,direction/flat)<.5f)continue;
            closest=remote;distance=flat;
        }
        closest?.PredictHit(attackId);
        StartCoroutine(SendAttack(attackId));
    }
    IEnumerator SendAttack(string attackId)
    {
        string character=AccountSession.Instance.State.characterId;
        string json=JsonUtility.ToJson(new CombatAttackBody {clientId=clientId,attackId=attackId});
        MultiplayerReply reply=null;float started=Time.realtimeSinceStartup;
        for(int attempt=0;attempt<2;attempt++)
        {
            if(!AccountSession.HasCharacter||AccountSession.Instance.State.characterId!=character||paused)yield break;
            started=Time.realtimeSinceStartup;
            yield return AccountSession.Instance.MultiplayerRequest("/v1/multiplayer/attack",json,r=>reply=r);
            if(reply?.code!="CONNECTION_LOST")break;
            // A retry keeps the id: an accepted request with a lost reply cannot hit twice.
        }
        if(!player||!AccountSession.HasCharacter||AccountSession.Instance.State.characterId!=character||paused)yield break;
        if(reply!=null&&string.IsNullOrEmpty(reply.code)&&reply.combatAvailable)
        {
            float trip=Time.realtimeSinceStartup-started;
            player.GetComponent<FighterCombo>()?.ApplyState(reply.selfCombat,reply.serverTime,trip);
            if(reply.combatPeers!=null)foreach(var peer in reply.combatPeers)
                foreach(var remote in remotes.Values)if(remote.Uid==peer.uid)remote.ApplyCombat(peer.combat,reply.serverTime,trip);
            foreach(var remote in remotes.Values)
                if(remote.Uid!=reply.targetUid)remote.CancelPredictedHit(attackId);
        }
        else
        {
            foreach(var remote in remotes.Values)remote.CancelPredictedHit(attackId);
            GameHud.Instance?.ShowAccountMessage(reply?.code=="ATTACK_COOLDOWN"?"Aguarde o combo terminar.":reply?.error??"O golpe não foi confirmado. Verifique a conexão.");
        }
    }
    void UpdatePeers(MultiplayerPeer[] peers,long serverTime,float roundTrip)
    {
        var live=new HashSet<string>();
        foreach(var peer in peers)
        {
            if(string.IsNullOrEmpty(peer.characterId)||peer.model!="bybeyby")continue;
            live.Add(peer.characterId);
            if(!remotes.TryGetValue(peer.characterId,out var remote))
            {
                remote=new Remote(this,player,peer);remotes.Add(peer.characterId,remote);
            }
            else RemoteTravel+=Vector3.Distance(remote.TargetPosition,new Vector3(peer.x,peer.y,peer.z));
            SawRemoteJump|=peer.jumping;remote.Apply(peer,serverTime,roundTrip);
        }
        var stale=new List<string>();foreach(var id in remotes.Keys)if(!live.Contains(id))stale.Add(id);
        foreach(var id in stale){remotes[id].Destroy();remotes.Remove(id);}
    }
    public bool TryGetRemoteByUid(string uid,out Transform transform)
    {
        foreach(var remote in remotes.Values)
            if(remote.Uid==uid){transform=remote.Transform;return true;}
        transform=null;return false;
    }
    public bool TryGetRemotePoseByUid(string uid,out Vector3 position,out float yaw)
    {
        foreach(var remote in remotes.Values)
            if(remote.Uid==uid){position=remote.TargetPosition;yaw=remote.TargetYaw;return true;}
        position=default;yaw=0;return false;
    }
    void LateUpdate(){realtime?.SendPlayer();foreach(var remote in remotes.Values)remote.Tick();}
    void OnApplicationPause(bool value)
    {
        paused=value;
        if(value){Connected=false;ClearRemotes();SetStatus("RECONECTANDO");}
    }
    public IEnumerator Leave()
    {
        paused=true;
        realtime?.StopConnection();
        if(AccountSession.HasCharacter)
            yield return AccountSession.Instance.MultiplayerRequest("/v1/multiplayer/leave","{\"clientId\":\""+clientId+"\"}",_=>{});
        Connected=false;ClearRemotes();
    }
    void ClearRemotes(){foreach(var remote in remotes.Values)remote.Destroy();remotes.Clear();if(policeCarWorld)policeCarWorld.Clear();}
    void OnDestroy(){ClearRemotes();if(Instance==this)Instance=null;}

    sealed class Remote
    {
        readonly MultiplayerWorld world;
        readonly string characterId;
        bool udpMode;
        int lastUdpSequence=-1;
        readonly GameObject root;
        GameObject avatar,vehicle;
        bool driving,inPoliceCar,combatClient;
        float lastCombatSync;
        readonly Animator animator;
        readonly RemoteAvatarAnimation animation;
        readonly CombatAvatarAnimation combat;
        readonly CuffedAvatarPose cuffedPose;
        readonly TextMesh nameplate;
        readonly WorldMicrophoneIndicator microphone;
        bool voiceActive;
        float legacyVoiceUntil;
        readonly TextMesh[] nameOutline;
        readonly TextMesh statusPlate;
        readonly TextMesh[] statusOutline;
        readonly AudioSource voiceSource,radioSource;
        readonly VoicePlayback voicePlayback,radioPlayback;
        public int VoiceUnderruns=>voicePlayback.Underruns+radioPlayback.Underruns;
        int lastVoiceSequence,lastRadioSequence;
        int listeningRadioChannel;
        readonly RemoteMotionBuffer motion=new RemoteMotionBuffer();
        int lastSequence=-1;
        Vector3 targetPosition;
        float targetYaw;
        long sourceOrigin;
        float localOrigin;
        public Vector3 TargetPosition=>targetPosition;
        public float TargetYaw=>targetYaw;
        public string Uid {get;private set;}
        public string DisplayName {get;private set;}
        public Transform Transform=>root?root.transform:null;
        public bool CanPredictHit=>root&&avatar.activeInHierarchy&&combatClient&&combat&&combat.Ready&&!combat.Dead&&
            !driving&&!inPoliceCar&&Time.unscaledTime-lastCombatSync<1.5f&&
            !(world.policeCarWorld&&world.policeCarWorld.HasSeatedVisual(Uid));
        public void PredictHit(string attackId){combat?.PredictHit(attackId);}
        public void CancelPredictedHit(string attackId){combat?.CancelPredictedHit(attackId);}
        public Remote(MultiplayerWorld world,ThirdPersonPlayer local,MultiplayerPeer peer)
        {
            this.world=world;characterId=peer.characterId;
            Uid=peer.uid;
            root=new GameObject("Jogador online "+peer.uid);root.layer=2;
            voiceSource=root.AddComponent<AudioSource>();
            voiceSource.playOnAwake=false;voiceSource.spatialBlend=1;voiceSource.minDistance=2;voiceSource.maxDistance=35;
            voiceSource.dopplerLevel=0;
            radioSource=root.AddComponent<AudioSource>();radioSource.playOnAwake=false;radioSource.spatialBlend=0;
            voicePlayback=new VoicePlayback(voiceSource);radioPlayback=new VoicePlayback(radioSource);
            root.transform.SetPositionAndRotation(new Vector3(peer.x,peer.y,peer.z),Quaternion.Euler(0,peer.yaw,0));
            // Clone only the visual child: never clone the local controller, camera or save scripts.
            if(local.animator.gameObject==local.gameObject)throw new InvalidOperationException("O visual precisa ser um filho separado do controlador.");
            var visual=UnityEngine.Object.Instantiate(local.animator.gameObject,root.transform,false);
            avatar=visual;
            visual.name="ByBeyby remoto";animator=visual.GetComponent<Animator>();
            animation=new RemoteAvatarAnimation(animator);
            foreach(var collider in visual.GetComponentsInChildren<Collider>())collider.enabled=false;
            foreach(var listener in visual.GetComponentsInChildren<AudioListener>())listener.enabled=false;
            foreach(var behaviour in visual.GetComponentsInChildren<MonoBehaviour>())behaviour.enabled=false;
            combat=visual.GetComponent<CombatAvatarAnimation>();
            if(!combat)combat=visual.AddComponent<CombatAvatarAnimation>();
            combat.enabled=true;combat.Initialize(animator);
            cuffedPose=visual.GetComponent<CuffedAvatarPose>();
            if(!cuffedPose)cuffedPose=visual.AddComponent<CuffedAvatarPose>();
            cuffedPose.enabled=true;
            var label=new GameObject("Nome",typeof(TextMesh));label.layer=2;label.transform.SetParent(root.transform,false);
            label.transform.localPosition=new Vector3(0,1.88f,0);
            nameplate=label.GetComponent<TextMesh>();nameplate.richText=false;
            nameplate.anchor=TextAnchor.MiddleCenter;nameplate.alignment=TextAlignment.Center;
            nameplate.fontSize=36;nameplate.characterSize=.025f;nameplate.color=new Color(1f,.86f,.18f);
            nameplate.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.GetComponent<MeshRenderer>().sharedMaterial=nameplate.font.material;
            label.GetComponent<MeshRenderer>().sortingOrder=1;
            nameOutline=MakeOutline(nameplate);
            microphone=WorldMicrophoneIndicator.Create(nameplate);
            var status=new GameObject("Estado do jogador",typeof(TextMesh));status.layer=2;
            status.transform.SetParent(root.transform,false);
            status.transform.localPosition=new Vector3(0,2.11f,0);
            statusPlate=status.GetComponent<TextMesh>();statusPlate.richText=false;
            statusPlate.anchor=TextAnchor.MiddleCenter;statusPlate.alignment=TextAlignment.Center;
            statusPlate.fontSize=31;statusPlate.characterSize=.025f;
            statusPlate.font=nameplate.font;
            status.GetComponent<MeshRenderer>().sharedMaterial=statusPlate.font.material;
            status.GetComponent<MeshRenderer>().sortingOrder=1;
            statusOutline=MakeOutline(statusPlate);
            UpdateNameplate(peer);
            targetPosition=root.transform.position;targetYaw=peer.yaw;
        }
        public void Apply(MultiplayerPeer peer,long serverTime,float roundTrip)
        {
            combatClient=peer.combatClient;lastCombatSync=Time.unscaledTime;
            ApplyCombat(peer.combat,serverTime,roundTrip);
            voiceActive=peer.voiceActive||peer.radioActive;
            if(!string.IsNullOrEmpty(peer.voice)||!string.IsNullOrEmpty(peer.radioVoice))legacyVoiceUntil=Time.unscaledTime+.5f;
            if(peer.sequence<lastSequence)
            {voicePlayback.Reset();radioPlayback.Reset();lastVoiceSequence=lastRadioSequence=0;}
            if(peer.voiceFrames!=null)
            {
                foreach(var packet in peer.voiceFrames)if(packet.sequence>lastVoiceSequence)
                {lastVoiceSequence=packet.sequence;voicePlayback.Push(packet.sequence,packet.data);}
            }
            else if(peer.voiceSequence>lastVoiceSequence&&!string.IsNullOrEmpty(peer.voice))
            {lastVoiceSequence=peer.voiceSequence;voicePlayback.Push(peer.voiceSequence,peer.voice);}
            if(PoliceRadio.Instance&&PoliceRadio.Instance.Connected)
            {
                if(listeningRadioChannel!=PoliceRadio.Instance.Channel){radioPlayback.Reset();listeningRadioChannel=PoliceRadio.Instance.Channel;}
                if(peer.radioFrames!=null)
                {
                    foreach(var packet in peer.radioFrames)if(packet.sequence>lastRadioSequence)
                    {lastRadioSequence=packet.sequence;radioPlayback.Push(packet.sequence,packet.data,PoliceRadio.Instance.Volume);PoliceRadio.Instance.Received(peer.firstName);}
                }
                else if(peer.radioSequence>lastRadioSequence&&!string.IsNullOrEmpty(peer.radioVoice))
                {lastRadioSequence=peer.radioSequence;radioPlayback.Push(peer.radioSequence,peer.radioVoice,PoliceRadio.Instance.Volume);PoliceRadio.Instance.Received(peer.firstName);}
            }
            else {radioPlayback.Reset();listeningRadioChannel=0;}
            driving=peer.driving;inPoliceCar=peer.inPoliceCar;avatar.SetActive(!driving&&!peer.inPoliceCar);
            nameplate.gameObject.SetActive(!peer.inPoliceCar);
            statusPlate.gameObject.SetActive(!peer.inPoliceCar&&!string.IsNullOrEmpty(statusPlate.text));
            if(cuffedPose)cuffedPose.Cuffed=peer.cuffed;
            bool loanDriving=driving&&string.IsNullOrEmpty(peer.policeCarId);
            if(loanDriving&&!vehicle){vehicle=MissionVehicle.CreateRemoteVisual();vehicle.transform.SetParent(root.transform,false);}
            if(vehicle)vehicle.SetActive(loanDriving);
            Uid=peer.uid;
            UpdateNameplate(peer);
            // The reliable room state carries entry/exit endpoints even while UDP
            // interpolation still renders an older pose inside the car.
            targetPosition=new Vector3(peer.x,peer.y,peer.z);targetYaw=peer.yaw;
            if(world.realtime&&world.realtime.TryGet(characterId,out _))return;
            if(udpMode){udpMode=false;motion.SetRealtime(false);sourceOrigin=0;lastSequence=-1;}
            if(peer.sequence==lastSequence)return;
            // Sync requests are serialized. A lower sequence means that the other client restarted.
            if(peer.sequence<lastSequence){motion.Reset();sourceOrigin=0;}
            lastSequence=peer.sequence;targetPosition=new Vector3(peer.x,peer.y,peer.z);
            float arrival=Time.unscaledTime,sampleTime=arrival;
            if(peer.updatedAt>0&&serverTime>=peer.updatedAt)
            {
                if(sourceOrigin==0)
                {
                    sourceOrigin=peer.updatedAt;
                    localOrigin=arrival-Mathf.Clamp((serverTime-peer.updatedAt)*.001f,0,10)-Mathf.Clamp(roundTrip*.5f,0,.5f);
                }
                sampleTime=localOrigin+(peer.updatedAt-sourceOrigin)*.001f;
            }
            motion.Push(sampleTime,targetPosition,Quaternion.Euler(0,peer.yaw,0),peer.speed,peer.jumping,peer.jumpPhase,arrival);
            UpdateNameplate(peer);
        }
        public void ApplyCombat(CombatState state,long serverTime,float roundTrip)
        {if(combat)combat.Apply(state,serverTime,roundTrip);}
        void UpdateNameplate(MultiplayerPeer peer)
        {
            DisplayName=PlayerDisplayName.From(peer.firstName,peer.name);
            nameplate.text=DisplayName+" ("+peer.uid+")";
            nameplate.color=peer.cuffed?new Color(1f,.75f,.32f):
                peer.policeDuty?new Color(.36f,.83f,1f):new Color(1f,.86f,.18f);
            foreach(var edge in nameOutline)edge.text=nameplate.text;
            statusPlate.text=peer.cuffed?"ALGEMADO":peer.policeDuty?"POLÍCIA":"";
            statusPlate.color=peer.cuffed?new Color(1f,.75f,.32f):new Color(.36f,.83f,1f);
            foreach(var edge in statusOutline)edge.text=statusPlate.text;
            statusPlate.gameObject.SetActive(!peer.inPoliceCar&&!string.IsNullOrEmpty(statusPlate.text));
        }
        static TextMesh[] MakeOutline(TextMesh source)
        {
            var edges=new TextMesh[8];int index=0;
            foreach(int x in new[]{-1,0,1})foreach(int y in new[]{-1,0,1})
            {
                if(x==0&&y==0)continue;
                var edge=new GameObject("Contorno preto",typeof(TextMesh));edge.layer=2;
                edge.transform.SetParent(source.transform,false);
                edge.transform.localPosition=new Vector3(x*.008f,y*.008f,.005f);
                var text=edge.GetComponent<TextMesh>();text.richText=false;
                text.anchor=TextAnchor.MiddleCenter;text.alignment=TextAlignment.Center;
                text.fontSize=source.fontSize;text.characterSize=source.characterSize;
                text.font=source.font;text.color=Color.black;
                edge.GetComponent<MeshRenderer>().sharedMaterial=source.font.material;
                edge.GetComponent<MeshRenderer>().sortingOrder=0;
                edges[index++]=text;
            }
            return edges;
        }
        public void Tick()
        {
            bool seatVisible=world.policeCarWorld&&world.policeCarWorld.HasSeatedVisual(Uid);
            avatar.SetActive(!driving&&!inPoliceCar&&!seatVisible);
            nameplate.gameObject.SetActive(!inPoliceCar&&!seatVisible);
            statusPlate.gameObject.SetActive(!inPoliceCar&&!seatVisible&&!string.IsNullOrEmpty(statusPlate.text));
            bool speaking=voiceActive||Time.unscaledTime<legacyVoiceUntil||VoiceChat.Instance&&VoiceChat.Instance.IsPlayerSpeaking(Uid);
            microphone.Refresh(speaking,nameplate);
            if(world.realtime&&world.realtime.TryGet(characterId,out var snapshot))
            {
                if(!udpMode){udpMode=true;motion.SetRealtime(true);sourceOrigin=0;lastUdpSequence=-1;}
                if(snapshot.Sequence!=lastUdpSequence)
                {
                    if(snapshot.Sequence<lastUdpSequence){motion.Reset();sourceOrigin=0;}
                    lastUdpSequence=snapshot.Sequence;
                    float arrival=Time.unscaledTime;
                    if(sourceOrigin==0)
                    {sourceOrigin=snapshot.UpdatedAt;localOrigin=arrival-(snapshot.ServerTime-snapshot.UpdatedAt)*.001f-world.realtime.Client.RoundTripMs*.0005f;}
                    float time=localOrigin+(snapshot.UpdatedAt-sourceOrigin)*.001f;
                    motion.Push(time,snapshot.Position,Quaternion.Euler(0,snapshot.Yaw,0),snapshot.Speed,snapshot.Jumping,snapshot.JumpPhase,arrival);
                }
            }
            if(motion.Sample(Time.unscaledTime,out var position,out var rotation))
            {
                if(combat&&combat.Dead)root.transform.SetPositionAndRotation(targetPosition,Quaternion.Euler(0,targetYaw,0));
                else root.transform.SetPositionAndRotation(position,rotation);
                if(!driving)animation.Apply(combat&&combat.Busy?0:motion.Speed,!(combat&&combat.Busy)&&motion.Jumping,motion.JumpPhase,Time.unscaledDeltaTime);
            }
            if(Camera.main){nameplate.transform.rotation=Camera.main.transform.rotation;
                statusPlate.transform.rotation=Camera.main.transform.rotation;}
        }
        public void Destroy(){voicePlayback.Dispose();radioPlayback.Dispose();if(root)UnityEngine.Object.Destroy(root);}
    }
}
