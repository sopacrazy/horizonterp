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
        while(player&&(!AccountSession.HasCharacter||StartupLoader.IsLoading))yield return null;
        while(player&&AccountSession.HasCharacter)
        {
            if(paused){yield return new WaitForSecondsRealtime(.25f);continue;}
            float started=Time.realtimeSinceStartup;
            var position=player.transform.position;
            var frame=new MultiplayerFrame {clientId=clientId,sequence=++sequence,
                x=position.x,y=position.y,z=position.z,yaw=Mathf.Repeat(player.transform.eulerAngles.y,360),
                speed=Mathf.Clamp(MissionVehicle.Driver==player?MissionVehicle.DriverSpeed:player.CurrentSpeed,0,8),driving=MissionVehicle.Driver==player,jumping=player.IsJumping,jumpPhase=player.JumpPhase,
                afterChat=LastChatId};
            MultiplayerReply reply=null;
            yield return AccountSession.Instance.MultiplayerRequest("/v1/multiplayer/sync",JsonUtility.ToJson(frame),r=>reply=r);
            if(!AccountSession.HasCharacter)break;
            if(reply!=null&&string.IsNullOrEmpty(reply.code)&&reply.peers!=null)
            {
                connectionFailures=0;Connected=true;PlayerCount=reply.count;ReceivedFrames++;
                SetStatus("ONLINE "+reply.count+"/"+reply.capacity);
                UpdatePeers(reply.peers,reply.serverTime,Time.realtimeSinceStartup-started);
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
    void ClearRemotes(){foreach(var remote in remotes.Values)remote.Destroy();remotes.Clear();}
    void OnDestroy(){ClearRemotes();if(Instance==this)Instance=null;}

    sealed class Remote
    {
        readonly GameObject root;
        GameObject avatar,vehicle;
        bool driving;
        readonly Animator animator;
        readonly RemoteAvatarAnimation animation;
        readonly TextMesh nameplate;
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
            var label=new GameObject("Nome",typeof(TextMesh));label.layer=2;label.transform.SetParent(root.transform,false);
            label.transform.localPosition=new Vector3(0,2.05f,0);
            nameplate=label.GetComponent<TextMesh>();nameplate.text=PlayerDisplayName.From(peer.firstName,peer.name);nameplate.richText=false;
            nameplate.anchor=TextAnchor.MiddleCenter;nameplate.alignment=TextAlignment.Center;
            nameplate.fontSize=36;nameplate.characterSize=.025f;nameplate.color=Color.white;
            nameplate.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.GetComponent<MeshRenderer>().sharedMaterial=nameplate.font.material;
            targetPosition=root.transform.position;
        }
        public void Apply(MultiplayerPeer peer,long serverTime,float roundTrip)
        {
            driving=peer.driving;avatar.SetActive(!driving);
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
            nameplate.text=PlayerDisplayName.From(peer.firstName,peer.name);
        }
        public void Tick()
        {
            if(motion.Sample(Time.unscaledTime,out var position,out var rotation))
            {
                root.transform.SetPositionAndRotation(position,rotation);
                if(!driving)animation.Apply(motion.Speed,motion.Jumping,motion.JumpPhase,Time.unscaledDeltaTime);
            }
            if(Camera.main)nameplate.transform.rotation=Camera.main.transform.rotation;
        }
        public void Destroy(){if(root)UnityEngine.Object.Destroy(root);}
    }
}
