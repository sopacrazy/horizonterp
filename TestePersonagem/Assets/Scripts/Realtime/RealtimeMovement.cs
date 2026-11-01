using System;
using UnityEngine;

namespace Hbrp.Realtime
{
    public sealed class RealtimeMovement:MonoBehaviour
    {
        public UtpMovementClient Client {get;private set;}
        public bool Active=>Client!=null&&Client.Authenticated&&!Client.Disconnected;
        public string Status {get;private set;}="Aguardando sala";
        MultiplayerWorld world;ThirdPersonPlayer player;
        float nextTicket,nextSend,connectedAt;
        int sequence,generation;
        bool requesting,unsupported;
        public void Initialize(MultiplayerWorld owner,ThirdPersonPlayer target){world=owner;player=target;}
        void Update()
        {
            if(!world||!player||!AccountSession.HasCharacter||!world.Connected){if(Client!=null)StopConnection();return;}
            Client?.Tick();
            if(Client!=null&&(Client.Disconnected||(!Active&&Time.realtimeSinceStartup-connectedAt>6)))
            {StopConnection();nextTicket=Time.realtimeSinceStartup+2;}
            if(!unsupported&&!requesting&&Time.realtimeSinceStartup>=nextTicket)RequestTicket();
        }
        async void RequestTicket()
        {
            requesting=true;int visit=++generation;string character=AccountSession.Instance.State.characterId;
            try
            {
                string json=await AccountSession.Instance.ServiceJsonAsync("/v1/realtime/ticket","{\"clientId\":\""+world.RealtimeClientId+"\"}");
                if(!this||visit!=generation||!AccountSession.HasCharacter||AccountSession.Instance.State.characterId!=character)return;
                if(json==null){nextTicket=Time.realtimeSinceStartup+10;Status="HTTPS (UDP temporariamente indisponível)";return;}
                var reply=JsonUtility.FromJson<UdpTicketReply>(json);
                if(reply==null||!reply.available){unsupported=true;Status="HTTPS (UDP desativado)";return;}
                if(reply.port<1||reply.port>65535||reply.expiresAt<=UtpWire.Now/1000)throw new InvalidOperationException("Expired UDP configuration");
                if(Client==null)
                {
                    Client=new UtpMovementClient(reply.host,(ushort)reply.port,reply.caCertificate,reply.serverName,reply.ticket);
                    connectedAt=Time.realtimeSinceStartup;Status="Conectando UDP protegido";
                }
                else Client.Renew(reply.ticket);
                nextTicket=Time.realtimeSinceStartup+7;
            }
            catch(Exception e){Status="HTTPS (UDP temporariamente indisponível)";nextTicket=Time.realtimeSinceStartup+5;Debug.LogWarning("UDP_CONNECT_FAILED "+e.GetType().Name);}
            finally{requesting=false;}
        }
        public void SendPlayer()
        {
            if(!Active||player.MapTeleportLoading||Time.realtimeSinceStartup<nextSend)return;
            Status="UDP / DTLS · 20 Hz";float now=Time.realtimeSinceStartup;nextSend=Mathf.Max(nextSend+.05f,now+.025f);
            var police=PoliceVehicleController.Driving?PoliceVehicleController.Active:null;
            Client.Send(new UdpPose{Sequence=++sequence,Position=police?police.transform.position:player.transform.position,Yaw=Mathf.Repeat(police?police.transform.eulerAngles.y:player.transform.eulerAngles.y,360),
                Speed=Mathf.Clamp(police?Mathf.Abs(police.Speed):MissionVehicle.Driver==player?MissionVehicle.DriverSpeed:player.CurrentSpeed,0,8),Driving=police||MissionVehicle.Driver==player,
                Jumping=player.IsJumping,JumpPhase=player.JumpPhase});
        }
        public bool TryGet(string character,out UdpPose pose){pose=default;return Active&&Client.TryGet(character,out pose);}
        public void StopConnection(){generation++;Client?.Dispose();Client=null;Status="HTTPS";}
        void OnApplicationPause(bool pause){if(pause)StopConnection();}
        void OnDestroy(){StopConnection();}
    }
}
