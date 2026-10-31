using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

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
    Label statusLabel;
    int sequence;
    bool paused;
    int connectionFailures;
    string lastFailure;

    public static void Create(ThirdPersonPlayer player)
    {
        if(Instance||AccountSession.VerificationTransport!=null)return;
        var world=player.gameObject.AddComponent<MultiplayerWorld>();world.player=player;
    }
    void Awake(){Instance=this;}
    IEnumerator Start()
    {
        policeCarWorld=gameObject.AddComponent<PoliceCarWorld>();policeCarWorld.Initialize(player);
        while(player&&(!AccountSession.HasCharacter||StartupLoader.IsLoading))yield return null;
        while(player&&AccountSession.HasCharacter)
        {
            if(paused||player.MapTeleportLoading){yield return new WaitForSecondsRealtime(.25f);continue;}
            float started=Time.realtimeSinceStartup;
            var position=player.transform.position;
            var frame=new MultiplayerFrame {clientId=clientId,sequence=++sequence,
                x=position.x,y=position.y,z=position.z,yaw=Mathf.Repeat(player.transform.eulerAngles.y,360),
                speed=Mathf.Clamp(MissionVehicle.Driver==player?MissionVehicle.DriverSpeed:player.CurrentSpeed,0,8),driving=MissionVehicle.Driver==player,jumping=player.IsJumping,jumpPhase=player.JumpPhase,
                afterChat=LastChatId};
            int voiceSequence=0;
            string voice=VoiceChat.Instance ? VoiceChat.Instance.Capture(out voiceSequence) : null;
            string json=JsonUtility.ToJson(frame);
            if(!string.IsNullOrEmpty(voice))
            {
                if(VoiceChat.Instance.RadioTransmitting&&PoliceRadio.Instance&&PoliceRadio.Instance.Connected)
                    json=json.Substring(0,json.Length-1)+",\"radioVoice\":\""+voice+"\",\"radioSequence\":"+voiceSequence+
                        ",\"radioChannel\":"+PoliceRadio.Instance.Channel+"}";
                else json=json.Substring(0,json.Length-1)+",\"voice\":\""+voice+"\",\"voiceSequence\":"+voiceSequence+"}";
            }
            MultiplayerReply reply=null;
            yield return AccountSession.Instance.MultiplayerRequest("/v1/multiplayer/sync",json,r=>reply=r);
            if(!string.IsNullOrEmpty(voice)&&(reply?.code=="INVALID_FRAME"||reply?.code=="RADIO_FORBIDDEN"))
            {VoiceChat.Instance?.Stop();GameHud.Instance?.ShowAccountMessage("Voz ainda indisponível no servidor");}
            if(!AccountSession.HasCharacter)break;
            if(reply!=null&&string.IsNullOrEmpty(reply.code)&&reply.peers!=null)
            {
                connectionFailures=0;Connected=true;PlayerCount=reply.count;ReceivedFrames++;
                SetStatus("ONLINE "+reply.count+"/"+reply.capacity);
                UpdatePeers(reply.peers,reply.serverTime,Time.realtimeSinceStartup-started);
                policeCarWorld.Apply(reply.policeCars);
                player.SetCustody(reply.selfCuffed,reply.selfEscortedBy,
                    new Vector3(reply.selfX,reply.selfY,reply.selfZ),reply.selfYaw);
                player.SetPoliceCarPassenger(reply.selfInPoliceCar,
                    new Vector3(reply.selfX,reply.selfY,reply.selfZ),reply.selfYaw);
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
                    if(hour>=0)
                    {
                        var dayNight=FindAnyObjectByType<CityDayNight>();
                        if(dayNight)dayNight.SetNight(hour<6||hour>=18);
                    }
                }
                float remaining=.1f-(Time.realtimeSinceStartup-started);
                if(remaining>0)yield return new WaitForSecondsRealtime(remaining);
            }
            else
            {
                Connected=false;
                string failure=(reply?.code??"NO_REPLY")+": "+(reply?.error??"sem detalhes");
                if(failure!=lastFailure)
                {
                    lastFailure=failure;
                    Debug.LogWarning("MULTIPLAYER_SYNC_FAILURE "+failure);
                }
                // A single dropped request must not destroy and respawn every remote avatar.
                if(++connectionFailures>=3||reply?.code=="UNAUTHORIZED"||reply?.code=="SESSION_IN_USE")
                {PlayerCount=0;ClearRemotes();}
                string code=reply?.code;
                SetStatus(code=="ROOM_FULL"?"SALA CHEIA":code=="SESSION_IN_USE"?"CONTA EM USO":code=="UNAUTHORIZED"?"SESSÃO ENCERRADA":"RECONECTANDO");
                yield return new WaitForSecondsRealtime(2);
            }
        }
        Connected=false;ClearRemotes();SetStatus("DESCONECTADO");
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
    void UpdatePeers(MultiplayerPeer[] peers,long serverTime,float roundTrip)
    {
        var live=new HashSet<string>();
        foreach(var peer in peers)
        {
            if(string.IsNullOrEmpty(peer.characterId)||peer.model!="bybeyby")continue;
            live.Add(peer.characterId);
            if(!remotes.TryGetValue(peer.characterId,out var remote))
            {
                remote=new Remote(player,peer);remotes.Add(peer.characterId,remote);
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
    void LateUpdate(){foreach(var remote in remotes.Values)remote.Tick();}
    void OnApplicationPause(bool value)
    {
        paused=value;
        if(value){Connected=false;ClearRemotes();SetStatus("RECONECTANDO");}
    }
    public IEnumerator Leave()
    {
        paused=true;
        if(AccountSession.HasCharacter)
            yield return AccountSession.Instance.MultiplayerRequest("/v1/multiplayer/leave","{\"clientId\":\""+clientId+"\"}",_=>{});
        Connected=false;ClearRemotes();
    }
    void ClearRemotes(){foreach(var remote in remotes.Values)remote.Destroy();remotes.Clear();if(policeCarWorld)policeCarWorld.Clear();}
    void OnDestroy(){ClearRemotes();if(Instance==this)Instance=null;}

    sealed class Remote
    {
        readonly GameObject root;
        GameObject avatar,vehicle;
        bool driving;
        readonly Animator animator;
        readonly RemoteAvatarAnimation animation;
        readonly CuffedAvatarPose cuffedPose;
        readonly TextMesh nameplate;
        readonly TextMesh[] nameOutline;
        readonly TextMesh statusPlate;
        readonly TextMesh[] statusOutline;
        readonly AudioSource voiceSource,radioSource;
        int lastVoiceSequence,lastRadioSequence;
        readonly RemoteMotionBuffer motion=new RemoteMotionBuffer();
        int lastSequence=-1;
        Vector3 targetPosition;
        long sourceOrigin;
        float localOrigin;
        public Vector3 TargetPosition=>targetPosition;
        public string Uid {get;private set;}
        public Transform Transform=>root?root.transform:null;
        public Remote(ThirdPersonPlayer local,MultiplayerPeer peer)
        {
            Uid=peer.uid;
            root=new GameObject("Jogador online "+peer.uid);root.layer=2;
            voiceSource=root.AddComponent<AudioSource>();
            voiceSource.playOnAwake=false;voiceSource.spatialBlend=1;voiceSource.minDistance=2;voiceSource.maxDistance=35;
            radioSource=root.AddComponent<AudioSource>();radioSource.playOnAwake=false;radioSource.spatialBlend=0;
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
            targetPosition=root.transform.position;
        }
        public void Apply(MultiplayerPeer peer,long serverTime,float roundTrip)
        {
            if(peer.voiceSequence>lastVoiceSequence&&!string.IsNullOrEmpty(peer.voice))
            {lastVoiceSequence=peer.voiceSequence;VoiceChat.Play(voiceSource,peer.voice);}
            if(peer.radioSequence>lastRadioSequence&&!string.IsNullOrEmpty(peer.radioVoice)&&PoliceRadio.Instance&&PoliceRadio.Instance.Connected)
            {lastRadioSequence=peer.radioSequence;VoiceChat.Play(radioSource,peer.radioVoice,PoliceRadio.Instance.Volume);PoliceRadio.Instance.Received(peer.firstName);}
            driving=peer.driving;avatar.SetActive(!driving&&!peer.inPoliceCar);
            nameplate.gameObject.SetActive(!peer.inPoliceCar);
            statusPlate.gameObject.SetActive(!peer.inPoliceCar&&!string.IsNullOrEmpty(statusPlate.text));
            if(cuffedPose)cuffedPose.Cuffed=peer.cuffed;
            if(driving&&!vehicle){vehicle=MissionVehicle.CreateRemoteVisual();vehicle.transform.SetParent(root.transform,false);}
            if(vehicle)vehicle.SetActive(driving);
            Uid=peer.uid;
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
        void UpdateNameplate(MultiplayerPeer peer)
        {
            nameplate.text=PlayerDisplayName.From(peer.firstName,peer.name)+" ("+peer.uid+")";
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
            if(motion.Sample(Time.unscaledTime,out var position,out var rotation))
            {
                root.transform.SetPositionAndRotation(position,rotation);
                if(!driving)animation.Apply(motion.Speed,motion.Jumping,motion.JumpPhase,Time.unscaledDeltaTime);
            }
            if(Camera.main){nameplate.transform.rotation=Camera.main.transform.rotation;
                statusPlate.transform.rotation=Camera.main.transform.rotation;}
        }
        public void Destroy(){if(root)UnityEngine.Object.Destroy(root);}
    }
}
