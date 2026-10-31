using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.TLS;
using Unity.Networking.Transport.Utilities;

namespace Hbrp.Realtime
{
    // In-memory movement relay. Wallets, inventory, roles and commands stay authoritative in the API.
    public sealed class UtpRoomServer:IDisposable
    {
        sealed class Peer {public NetworkConnection Connection;public RealtimeTicketClaims Identity;public UdpPose Pose;public long ConnectedAt,Window;public int Messages;}
        readonly List<Peer> peers=new List<Peer>();
        readonly Dictionary<string,Tuple<NetworkConnection,long>> consumed=new Dictionary<string,Tuple<NetworkConnection,long>>();
        readonly string secret;
        NetworkDriver driver;NetworkPipeline reliable,unreliable;
        long nextSnapshot;
        public int AuthenticatedPeers {get;private set;}
        public int AcceptedMoves {get;private set;}
        public int RejectedPackets {get;private set;}
        public UtpRoomServer(string bindAddress,ushort port,string certificate,string privateKey,string signingSecret,int simulatedDelayMs=0)
        {
            if(signingSecret==null||signingSecret.Length<32)throw new ArgumentException("Server signing secret required");secret=signingSecret;
            var settings=new NetworkSettings(Allocator.Temp);
            settings.WithNetworkConfigParameters(disconnectTimeoutMS:5000,heartbeatTimeoutMS:500);
            settings.WithSimulatorStageParameters(64,maxPacketSize:1400,mode:ApplyMode.SentPacketsOnly,packetDelayMs:simulatedDelayMs,packetJitterMs:simulatedDelayMs>0?60:0,packetDropPercentage:simulatedDelayMs>0?10:0,randomSeed:11);
            settings.WithSecureServerParameters(certificate,privateKey);
            driver=NetworkDriver.Create(settings);settings.Dispose();
            reliable=driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));unreliable=driver.CreatePipeline(typeof(UnreliableSequencedPipelineStage),typeof(SimulatorPipelineStage));
            if(driver.Bind(NetworkEndpoint.Parse(bindAddress,port))!=0){driver.Dispose();throw new InvalidOperationException("Cannot bind UDP endpoint");}
            driver.Listen();
        }
        public void Tick()
        {
            driver.ScheduleUpdate().Complete();long now=UtpWire.Now;
            NetworkConnection connection;
            while((connection=driver.Accept())!=default)
            {
                if(peers.Count>=16){driver.Disconnect(connection);continue;}
                peers.Add(new Peer{Connection=connection,ConnectedAt=now,Window=now});
            }
            foreach(var peer in peers.ToArray())
            {
                if(driver.GetConnectionState(peer.Connection)==NetworkConnection.State.Disconnected){peers.Remove(peer);continue;}
                if((peer.Identity==null&&now-peer.ConnectedAt>4000)||(peer.Identity!=null&&peer.Identity.exp<=now/1000))
                {driver.Disconnect(peer.Connection);peers.Remove(peer);continue;}
                NetworkEvent.Type type;int processed=0;
                while(processed++<64&&(type=driver.PopEventForConnection(peer.Connection,out var reader))!=NetworkEvent.Type.Empty)
                {
                    if(type==NetworkEvent.Type.Disconnect){peers.Remove(peer);break;}
                    if(type!=NetworkEvent.Type.Data)continue;
                    if(now-peer.Window>=1000){peer.Window=now;peer.Messages=0;}
                    if(++peer.Messages>60){RejectedPackets++;continue;}
                    try{Receive(peer,ref reader,now);}catch(FormatException){RejectedPackets++;}
                }
            }
            AuthenticatedPeers=0;foreach(var p in peers)if(p.Identity!=null)AuthenticatedPeers++;
            if(now>=nextSnapshot){nextSnapshot=Math.Max(nextSnapshot+50,now+25);Broadcast(now);}
            foreach(var entry in new List<string>(consumed.Keys))if(consumed[entry].Item2<=now/1000)consumed.Remove(entry);
        }
        void Receive(Peer peer,ref DataStreamReader reader,long now)
        {
            if(!UtpWire.Header(ref reader,out var kind))throw new FormatException();
            if(kind==PacketKind.Authenticate)
            {
                string token=UtpWire.Text(ref reader);
                if(reader.GetBytesRead()!=reader.Length||!RealtimeTicket.Verify(token,secret,now/1000,out var identity))throw new FormatException();
                if(peer.Identity!=null&&(identity.characterId!=peer.Identity.characterId||identity.clientId!=peer.Identity.clientId||identity.sessionId!=peer.Identity.sessionId))throw new FormatException();
                foreach(var p in peers)if(p!=peer&&p.Identity?.characterId==identity.characterId)throw new FormatException();
                if(consumed.TryGetValue(identity.jti,out var used)&&used.Item1!=peer.Connection)throw new FormatException();
                consumed[identity.jti]=Tuple.Create(peer.Connection,identity.exp);peer.Identity=identity;
                if(driver.BeginSend(reliable,peer.Connection,out var reply)==0){UtpWire.Header(ref reply,PacketKind.Authenticated);driver.EndSend(reply);}return;
            }
            if(peer.Identity==null)throw new FormatException();
            if(kind==PacketKind.Move)
            {
                var pose=UtpWire.Pose(ref reader);if(reader.GetBytesRead()!=reader.Length||pose.Sequence<=peer.Pose.Sequence)return;
                pose.CharacterId=peer.Identity.characterId;pose.UpdatedAt=now;peer.Pose=pose;AcceptedMoves++;
            }
            else if(kind==PacketKind.Ping&&reader.Length-reader.GetBytesRead()==8)
            {
                long sent=reader.ReadLong();
                if(driver.BeginSend(unreliable,peer.Connection,out var reply)==0){UtpWire.Header(ref reply,PacketKind.Pong);reply.WriteLong(sent);driver.EndSend(reply);}
            }
            else throw new FormatException();
        }
        void Broadcast(long now)
        {
            foreach(var peer in peers)
            {
                if(peer.Identity==null||driver.BeginSend(unreliable,peer.Connection,out var writer)!=0)continue;
                UtpWire.Header(ref writer,PacketKind.Snapshot);writer.WriteLong(now);
                var active=peers.FindAll(p=>p!=peer&&p.Identity!=null&&p.Pose.Sequence>0&&now-p.Pose.UpdatedAt<1000);
                writer.WriteByte((byte)Math.Min(10,active.Count));
                for(int i=0;i<active.Count&&i<10;i++)
                {var p=active[i].Pose;UtpWire.Text(ref writer,p.CharacterId);writer.WriteLong(p.UpdatedAt);UtpWire.Pose(ref writer,p);}
                driver.EndSend(writer);
            }
        }
        public void Dispose(){if(driver.IsCreated)driver.Dispose();peers.Clear();consumed.Clear();}
    }
}
