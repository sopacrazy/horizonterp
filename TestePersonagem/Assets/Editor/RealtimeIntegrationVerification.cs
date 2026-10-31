using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Hbrp.Realtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug=UnityEngine.Debug;

public static class RealtimeIntegrationVerification
{
    static string Sign(RealtimeTicketClaims claims,string secret)
    {
        string payload=B64(Encoding.UTF8.GetBytes(JsonUtility.ToJson(claims)));
        using(var hmac=new HMACSHA256(Encoding.UTF8.GetBytes(secret)))return payload+"."+B64(hmac.ComputeHash(Encoding.UTF8.GetBytes("hbrp-utp-v1."+payload)));
    }
    static string B64(byte[] value)=>Convert.ToBase64String(value).TrimEnd('=').Replace('+','-').Replace('/','_');
    static RealtimeTicketClaims Identity(int id)=>new RealtimeTicketClaims {v=1,characterId=id==1?"11111111-1111-4111-8111-111111111111":"22222222-2222-4222-8222-222222222222",
        uid=id.ToString(),world="centro_teste",clientId=Guid.NewGuid().ToString(),sessionId=Guid.NewGuid().ToString(),jti=Guid.NewGuid().ToString(),exp=UtpWire.Now/1000+15};
    static void Check(bool pass,string caption){if(!pass)throw new Exception(caption);}
    [MenuItem("Horizonte/Testes locais/Unity Transport UDP e DTLS")]
    public static void Run()
    {
        string folder=Path.GetFullPath("../tools/realtime-integration/credentials");
        string cert=File.ReadAllText(Path.Combine(folder,"localhost.pem")),key=File.ReadAllText(Path.Combine(folder,"localhost.key")),ca=File.ReadAllText(Path.Combine(folder,"localhost-ca.pem")),secret=File.ReadAllText(Path.Combine(folder,"test-secret.txt"));
        var first=Identity(1);string ticket=Sign(first,secret);
        Check(RealtimeTicket.Verify(ticket,secret,UtpWire.Now/1000,out var decoded)&&decoded.characterId==first.characterId,"Valid ticket rejected");
        Check(!RealtimeTicket.Verify(ticket+"tampered",secret,UtpWire.Now/1000,out _),"Tampered ticket accepted");
        first.exp=UtpWire.Now/1000-1;Check(!RealtimeTicket.Verify(Sign(first,secret),secret,UtpWire.Now/1000,out _),"Expired ticket accepted");
        Check(!UtpWire.Valid(new UdpPose{Sequence=1,Position=new Vector3(float.NaN,0,0)}),"NaN position accepted");
        var results=new List<object>();
        foreach(int delay in new[]{0,120})
        {
            first=Identity(1);var second=Identity(2);
            ushort port=(ushort)(delay==0?17777:17778);
            using(var server=new UtpRoomServer("127.0.0.1",port,cert,key,secret,delay))
            using(var a=new UtpMovementClient("127.0.0.1",port,ca,"localhost",Sign(first,secret),delay))
            using(var b=new UtpMovementClient("127.0.0.1",port,ca,"localhost",Sign(second,secret),delay))
            using(var rejected=new UtpMovementClient("127.0.0.1",port,ca,"localhost",Sign(second,secret)+"x"))
            {
                var clock=Stopwatch.StartNew();double next=0;int sequence=0;float travel=0;Vector3 last=Vector3.zero;bool saw=false;
                while(clock.Elapsed.TotalSeconds<7)
                {
                    server.Tick();a.Tick();b.Tick();rejected.Tick();
                    if(a.Authenticated&&b.Authenticated&&clock.Elapsed.TotalSeconds>=next)
                    {
                        next=Math.Max(next+.05,clock.Elapsed.TotalSeconds+.025);sequence++;
                        a.Send(new UdpPose{Sequence=sequence,Position=new Vector3(10+(float)clock.Elapsed.TotalSeconds,0,10),Yaw=90,Speed=1,Jumping=sequence%20<5,JumpPhase=.3f});
                        b.Send(new UdpPose{Sequence=sequence,Position=new Vector3(-10,0,10),Yaw=0,Speed=0});
                    }
                    if(b.TryGet(first.characterId,out var snapshot))
                    {if(saw)travel+=Vector3.Distance(last,snapshot.Position);last=snapshot.Position;saw=true;}
                    Thread.Sleep(10);
                }
                Check(a.Authenticated&&b.Authenticated,"Two encrypted clients did not authenticate");
                Check(!rejected.Authenticated&&server.RejectedPackets>0,"Unauthorized client entered UDP room");
                Check(travel>2&&a.SnapshotsReceived>30&&b.SnapshotsReceived>30,"Movement stalled or snapshots missing");
                Check(server.AcceptedMoves>80,"Realtime movement throughput too low");
                results.Add(new{simulatedOneWayDelayMs=delay,simulatedJitterMs=delay==0?0:60,simulatedLossPercent=delay==0?0:10,
                    receivedA=a.SnapshotsReceived,receivedB=b.SnapshotsReceived,roundTripMs=a.RoundTripMs,acceptedMoves=server.AcceptedMoves,travelMetres=travel,authenticated=true,unauthorizedRejected=true});
            }
        }
        string result=Newtonsoft.Json.JsonConvert.SerializeObject(new{passed=true,encrypted=true,localOnly=true,tests=results},Newtonsoft.Json.Formatting.Indented);
        File.WriteAllText("../tools/realtime-integration/udp-validation.json",result);Debug.Log("UTP_LOCAL_INTEGRATION_PASSED "+result);
        Debug.Log("VIVOX_PROXIMITY_URI_PROPERTIES "+new Unity.Services.Vivox.Channel3DProperties(35,2,1,Unity.Services.Vivox.AudioFadeModel.InverseByDistance));
    }
    public static void BuildServer()
    {
        var original=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(string.IsNullOrEmpty(original.path))
        {
            if(!Application.isBatchMode)throw new InvalidOperationException("Salve a cena aberta antes de compilar o servidor local.");
            original=EditorSceneManager.OpenScene(GameUpdateSetup.BootScene,OpenSceneMode.Single);
        }
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
        new GameObject("HBRP UDP server").AddComponent<RealtimeServerHost>();
        const string path="Assets/Scenes/RealtimeServer.unity";EditorSceneManager.SaveScene(scene,path);
        var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new[]{path},target=BuildTarget.StandaloneWindows64,
            locationPathName="Builds/RealtimeServer/HbrpRealtimeServer.exe",options=BuildOptions.CompressWithLz4HC});
        if(result.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Realtime server build failed");
        Debug.Log("UTP_SERVER_BUILD_OK");
        }
        finally
        {
            if(scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);
            if(original.IsValid()&&original.isLoaded)UnityEngine.SceneManagement.SceneManager.SetActiveScene(original);
        }
    }
    public static void BuildAllLocal()
    {
        Run();BuildServer();
        BuildClient();
    }
    public static void BuildClient()
    {
        var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=GameUpdateSetup.Scenes,target=BuildTarget.StandaloneWindows64,
            locationPathName="Builds/WindowsRealtimeLocal/Horizonte.exe",options=BuildOptions.CompressWithLz4HC});
        if(result.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Realtime local client build failed");
        Debug.Log("UTP_CLIENT_BUILD_OK");
    }
}
