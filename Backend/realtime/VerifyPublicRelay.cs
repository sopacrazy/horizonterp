using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Diagnostics;
using UnityEngine;
using Hbrp.Realtime;
using Debug=UnityEngine.Debug;

// Explicit release check: synthetic accounts only; no native saved login is read.
public static class VerifyPublicRelay
{
    [Serializable] sealed class Fixture {public string server,certificateSha256,password;public string[] emails;}
    [Serializable] sealed class State {public string accountId,characterId,uid;}
    [Serializable] sealed class Reply {public string token;public State state;}
    [Serializable] sealed class Ticket {public bool available;public string ticket,host,serverName,caCertificate;public int port;}
    [Serializable] sealed class Report {public bool passed,encrypted;public string remoteHost;public int snapshotsA,snapshotsB;public float rttMs,travelMetres;}
    static HttpClient client;
    static T Post<T>(string route,string json,string token=null)
    {
        using(var request=new HttpRequestMessage(HttpMethod.Post,route))
        {
            request.Content=new StringContent(json,Encoding.UTF8,"application/json");
            if(token!=null)request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",token);
            using(var response=client.SendAsync(request).GetAwaiter().GetResult())
            {
                if(!response.IsSuccessStatusCode)throw new Exception("API probe HTTP "+(int)response.StatusCode+" on "+route);
                string text=response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return typeof(T)==typeof(string)?(T)(object)text:JsonUtility.FromJson<T>(text);
            }
        }
    }
    public static void Run()
    {
        var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"--public-relay-fixture");
        if(index<0)throw new Exception("Explicit private fixture required");
        var fixture=JsonUtility.FromJson<Fixture>(File.ReadAllText(args[index+1]));
        if(fixture.server!="https://187.41.106.219:8443"||fixture.emails.Length!=2)throw new Exception("Invalid probe host");
        foreach(string email in fixture.emails)if(!email.StartsWith("udp071-")||!email.EndsWith("@example.invalid"))throw new Exception("Synthetic accounts only");
        var handler=new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback=(_,cert,chain,errors)=>
        {using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(cert.RawData)).Replace("-","").ToLowerInvariant()==fixture.certificateSha256;};
        client=new HttpClient(handler){BaseAddress=new Uri(fixture.server),Timeout=TimeSpan.FromSeconds(15)};
        var tokens=new string[2];var ids=new string[2];var tickets=new Ticket[2];
        try
        {
            for(int i=0;i<2;i++)
            {
                tokens[i]=Post<Reply>("/v1/auth/register",JsonUtility.ToJson(new Login{email=fixture.emails[i],password=fixture.password})).token;
                var reply=Post<Reply>("/v1/characters","{\"firstName\":\"Teste\",\"lastName\":\""+(i==0?"RedeUm":"RedeDois")+"\",\"gender\":\"male\"}",tokens[i]);ids[i]=reply.state.characterId;
                string cid=Guid.NewGuid().ToString();
                Post<string>("/v1/multiplayer/sync","{\"clientId\":\""+cid+"\",\"sequence\":1,\"x\":2489.3,\"y\":12.37,\"z\":1668.5,\"yaw\":40,\"speed\":0,\"driving\":false,\"jumping\":false,\"jumpPhase\":0,\"afterChat\":0}",tokens[i]);
                tickets[i]=Post<Ticket>("/v1/realtime/ticket","{\"clientId\":\""+cid+"\"}",tokens[i]);
                if(!tickets[i].available)throw new Exception("UDP not enabled at API");
            }
            var a=tickets[0];var b=tickets[1];
            using(var first=new UtpMovementClient(a.host,(ushort)a.port,a.caCertificate,a.serverName,a.ticket))
            using(var second=new UtpMovementClient(b.host,(ushort)b.port,b.caCertificate,b.serverName,b.ticket))
            {
                var timer=Stopwatch.StartNew();int seq=0;double next=0;float travel=0;Vector3 last=Vector3.zero;bool saw=false;
                while(timer.Elapsed.TotalSeconds<7)
                {
                    first.Tick();second.Tick();
                    if(first.Authenticated&&second.Authenticated&&timer.Elapsed.TotalSeconds>=next)
                    {
                        next=timer.Elapsed.TotalSeconds+.05;seq++;
                        first.Send(new UdpPose{Sequence=seq,Position=new Vector3(2489.3f+(float)timer.Elapsed.TotalSeconds,12.37f,1668.5f),Yaw=40,Speed=1});
                        second.Send(new UdpPose{Sequence=seq,Position=new Vector3(2491.3f,12.37f,1668.5f),Yaw=40});
                    }
                    if(second.TryGet(ids[0],out var pose)){if(saw)travel+=Vector3.Distance(pose.Position,last);last=pose.Position;saw=true;}
                    Thread.Sleep(10);
                }
                bool pass=first.Authenticated&&second.Authenticated&&travel>2&&first.SnapshotsReceived>50&&second.SnapshotsReceived>50;
                string report=JsonUtility.ToJson(new Report{passed=pass,encrypted=true,remoteHost=a.host,snapshotsA=first.SnapshotsReceived,snapshotsB=second.SnapshotsReceived,rttMs=first.RoundTripMs,travelMetres=travel},true);
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(args[index+1]),"public-udp-validation.json"),report);
                if(!pass)throw new Exception("Public UDP check failed");Debug.Log("PUBLIC_UDP_PROBE_PASSED "+report);
            }
        }
        finally
        {
            foreach(string token in tokens)if(token!=null)try{Post<string>("/v1/auth/logout","{}",token);}catch(Exception){}
            client.Dispose();
        }
    }
    [Serializable] sealed class Login {public string email,password;}
}
