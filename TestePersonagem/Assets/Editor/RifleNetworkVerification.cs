using System;
using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Hbrp.Realtime;
using UnityEditor;
using UnityEngine;
using Debug=UnityEngine.Debug;

// Exercises actual encrypted sockets and mixed legacy/extended clients on loopback.
public static class RifleNetworkVerification
{
    static string B64(byte[] bytes)=>Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
    static string Ticket(int number,string secret,out string character)
    {
        character=number==1?"11111111-1111-4111-8111-111111111111":number==2?"22222222-2222-4222-8222-222222222222":"33333333-3333-4333-8333-333333333333";
        var claims=new RealtimeTicketClaims {v=1,characterId=character,uid=number.ToString(),world="centro_teste",clientId=Guid.NewGuid().ToString(),sessionId=Guid.NewGuid().ToString(),jti=Guid.NewGuid().ToString(),exp=UtpWire.Now/1000+15};
        string payload=B64(Encoding.UTF8.GetBytes(JsonUtility.ToJson(claims)));
        using(var hash=new HMACSHA256(Encoding.UTF8.GetBytes(secret)))return payload+"."+B64(hash.ComputeHash(Encoding.UTF8.GetBytes("hbrp-utp-v1."+payload)));
    }
    static void Check(bool value,string reason){if(!value)throw new Exception(reason);}
    [MenuItem("Horizonte/Testes locais/Validar rifle na rede UDP")]
    public static void Verify()
    {
        string folder=Path.GetFullPath("../tools/realtime-integration/credentials");
        string cert=File.ReadAllText(Path.Combine(folder,"localhost.pem")),key=File.ReadAllText(Path.Combine(folder,"localhost.key")),ca=File.ReadAllText(Path.Combine(folder,"localhost-ca.pem")),secret=File.ReadAllText(Path.Combine(folder,"test-secret.txt"));
        string firstTicket=Ticket(1,secret,out var first),secondTicket=Ticket(2,secret,out _),thirdTicket=Ticket(3,secret,out _);
        using(var server=new UtpRoomServer("127.0.0.1",17779,cert,key,secret))
        using(var a=new UtpMovementClient("127.0.0.1",17779,ca,"localhost",firstTicket))
        using(var b=new UtpMovementClient("127.0.0.1",17779,ca,"localhost",secondTicket))
        using(var legacy=new UtpMovementClient("127.0.0.1",17779,ca,"localhost",thirdTicket))
        {
            var time=Stopwatch.StartNew();double next=0;int sequence=0,seenShots=0;bool sawArmed=false,sawHolster=false,sawLegacyMove=false;
            while(time.Elapsed.TotalSeconds<4)
            {
                server.Tick();a.Tick();b.Tick();legacy.Tick();
                if(a.Authenticated&&b.Authenticated&&legacy.Authenticated&&time.Elapsed.TotalSeconds>=next)
                {
                    next=time.Elapsed.TotalSeconds+.05;sequence++;bool equipped=time.Elapsed.TotalSeconds<2.8;
                    var pose=new UdpPose {Sequence=sequence,Position=new Vector3((float)time.Elapsed.TotalSeconds,0,0),Yaw=90,Speed=1};
                    a.SendVisual(pose,new UdpWeaponPose {Sequence=sequence,Equipped=equipped,Aiming=equipped,Pitch=-12,ShotSequence=sequence/4});
                    b.SendVisual(new UdpPose {Sequence=sequence,Position=Vector3.zero},new UdpWeaponPose {Sequence=sequence});
                    legacy.Send(new UdpPose {Sequence=sequence,Position=Vector3.zero});
                }
                if(b.TryGetWeapon(first,out var visual))
                {sawArmed|=visual.Equipped&&visual.Aiming&&Mathf.Abs(visual.Pitch+12)<.01f;seenShots=Mathf.Max(seenShots,visual.ShotSequence);sawHolster|=!visual.Equipped&&visual.Sequence>20;}
                if(legacy.TryGet(first,out var seen))sawLegacyMove|=seen.Position.x>2;
                Thread.Sleep(5);
            }
            Check(a.WeaponStateAvailable&&b.WeaponStateAvailable,"Extended capability was not negotiated.");
            Check(sawArmed&&sawHolster&&seenShots>6,"Equip, aim, shots or holster did not reach observer.");
            Check(sawLegacyMove&&legacy.SnapshotsReceived>30,"Legacy snapshot compatibility regressed.");
            Check(server.RejectedPackets==0,"Valid mixed-client packets were rejected.");
            string report=JsonUtility.ToJson(new Report {passed=true,encrypted=true,localOnly=true,shots=seenShots,movementPackets=server.AcceptedMoves,observerSnapshots=b.SnapshotsReceived,legacySnapshots=legacy.SnapshotsReceived},true);
            Directory.CreateDirectory("../tools/rifle-review");File.WriteAllText("../tools/rifle-review/network-validation.json",report);
            Debug.Log("RIFLE_NETWORK_VALIDATION_OK "+report);
        }
    }
    [Serializable] sealed class Report {public bool passed,encrypted,localOnly;public int shots,movementPackets,observerSnapshots,legacySnapshots;}
}
