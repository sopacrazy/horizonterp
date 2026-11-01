using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.TLS;
using Unity.Networking.Transport.Utilities;
using UnityEngine;

namespace Hbrp.Realtime
{
    public sealed class UtpMovementClient:IDisposable
    {
        NetworkDriver driver;NetworkConnection connection;NetworkPipeline reliable,unreliable;
        readonly Dictionary<string,UdpPose> states=new Dictionary<string,UdpPose>();
        string ticket;long nextPing;
        public bool Authenticated {get;private set;}
        public bool Disconnected {get;private set;}
        public float RoundTripMs {get;private set;}
        public int PingsReceived {get;private set;}
        float lastPong;
        public float PongAge=>PingsReceived==0?float.PositiveInfinity:Time.realtimeSinceStartup-lastPong;
        public int SnapshotsReceived {get;private set;}
        public UtpMovementClient(string host,ushort port,string caCertificate,string serverName,string ticket,int simulatedDelayMs=0)
        {
            if(string.IsNullOrEmpty(caCertificate)||string.IsNullOrEmpty(serverName))throw new ArgumentException("UDP TLS trust configuration required");
            this.ticket=ticket;var settings=new NetworkSettings(Allocator.Temp);
            settings.WithNetworkConfigParameters(disconnectTimeoutMS:5000,heartbeatTimeoutMS:500);
            settings.WithSimulatorStageParameters(64,maxPacketSize:1400,mode:ApplyMode.SentPacketsOnly,packetDelayMs:simulatedDelayMs,packetJitterMs:simulatedDelayMs>0?60:0,packetDropPercentage:simulatedDelayMs>0?10:0,randomSeed:17);
            settings.WithSecureClientParameters(caCertificate,serverName);
            driver=NetworkDriver.Create(settings);settings.Dispose();
            reliable=driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));unreliable=driver.CreatePipeline(typeof(UnreliableSequencedPipelineStage),typeof(SimulatorPipelineStage));
            driver.Bind(NetworkEndpoint.AnyIpv4);connection=driver.Connect(NetworkEndpoint.Parse(host,port));
        }
        public void Renew(string value){ticket=value;if(connection.IsCreated)Authenticate();}
        void Authenticate()
        {if(driver.BeginSend(reliable,connection,out var writer)!=0)return;UtpWire.Header(ref writer,PacketKind.Authenticate);UtpWire.Text(ref writer,ticket);driver.EndSend(writer);}
        public void Tick()
        {
            if(Disconnected)return;driver.ScheduleUpdate().Complete();
            NetworkEvent.Type type;
            while((type=driver.PopEventForConnection(connection,out var reader))!=NetworkEvent.Type.Empty)
            {
                if(type==NetworkEvent.Type.Connect){Authenticate();continue;}
                if(type==NetworkEvent.Type.Disconnect){Disconnected=true;Authenticated=false;states.Clear();continue;}
                if(type!=NetworkEvent.Type.Data)continue;
                try
                {
                    if(!UtpWire.Header(ref reader,out var kind))continue;
                    if(kind==PacketKind.Authenticated){Authenticated=true;continue;}
                    if(!Authenticated)continue;
                    if(kind==PacketKind.Pong&&reader.Length-reader.GetBytesRead()==8)
                    {
                        float sample=Mathf.Clamp(UtpWire.Now-reader.ReadLong(),0,5000);
                        RoundTripMs=PingsReceived==0?sample:Mathf.Lerp(RoundTripMs,sample,.3f);
                        PingsReceived++;lastPong=Time.realtimeSinceStartup;continue;
                    }
                    if(kind!=PacketKind.Snapshot||reader.Length-reader.GetBytesRead()<9)continue;
                    long time=reader.ReadLong();int count=reader.ReadByte();if(count>10)continue;
                    var batch=new List<UdpPose>(count);
                    for(int i=0;i<count;i++)
                    {
                        string id=UtpWire.Text(ref reader);if(!Guid.TryParse(id,out _))throw new FormatException();
                        long updated=reader.ReadLong();var p=UtpWire.Pose(ref reader);p.CharacterId=id;p.ServerTime=time;p.UpdatedAt=updated;p.ReceivedAt=Time.realtimeSinceStartupAsDouble;
                        if(updated>time||time-updated>1000)throw new FormatException();batch.Add(p);
                    }
                    if(reader.GetBytesRead()!=reader.Length)continue;
                    foreach(var p in batch)if(!states.TryGetValue(p.CharacterId,out var old)||p.Sequence>old.Sequence)states[p.CharacterId]=p;
                    SnapshotsReceived++;
                }
                catch(FormatException){/* Malformed or truncated datagrams never update rendered state. */}
            }
            if(Authenticated&&UtpWire.Now>=nextPing)
            {
                nextPing=UtpWire.Now+1000;
                if(driver.BeginSend(unreliable,connection,out var writer)==0){UtpWire.Header(ref writer,PacketKind.Ping);writer.WriteLong(UtpWire.Now);driver.EndSend(writer);}
            }
        }
        public void Send(UdpPose pose)
        {if(!Authenticated||!UtpWire.Valid(pose)||driver.BeginSend(unreliable,connection,out var writer)!=0)return;UtpWire.Header(ref writer,PacketKind.Move);UtpWire.Pose(ref writer,pose);driver.EndSend(writer);}
        public bool TryGet(string id,out UdpPose pose)=>states.TryGetValue(id,out pose)&&Time.realtimeSinceStartupAsDouble-pose.ReceivedAt<.6;
        public void Dispose(){if(driver.IsCreated)driver.Dispose();Authenticated=false;Disconnected=true;states.Clear();}
    }
}
