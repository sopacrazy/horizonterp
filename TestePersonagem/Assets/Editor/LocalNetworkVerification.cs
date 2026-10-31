using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class LocalNetworkVerification
{
    static readonly List<string> checks=new List<string>();
    static void Check(bool value,string caption){if(!value)throw new Exception(caption);checks.Add(caption);}
    [MenuItem("Horizonte/Testes locais/Mapa, rede e voz")]
    public static void Run()
    {
        checks.Clear();
        foreach(int fps in new[]{30,60,120})
        {
            var heading=new MinimapHeading();heading.Step(350,0);float previous=350,total=0;
            for(int i=0;i<fps;i++)
            {
                float next=heading.Step(10,1f/fps),step=Mathf.DeltaAngle(previous,next);
                Check(step>=-.001f&&step<8,"Heading crosses north smoothly at "+fps+" FPS, frame "+i);
                total+=step;previous=next;
            }
            Check(Mathf.Abs(total-20)<.1f,"Heading reaches the target by the shortest path at "+fps+" FPS");
        }
        var turn=new MinimapHeading();turn.Step(0,0);
        Check(turn.Step(180,1)<70,"A frame hitch does not snap the map through 180 degrees");
        int sends=0,oldSends=0;double oldNext=0;var pending=new List<double>();var cadence=new SyncCadence();
        for(int tick=0;tick<10000;tick++)
        {
            double now=tick*.001;
            for(int n=pending.Count-1;n>=0;n--)if(pending[n]<=now){pending.RemoveAt(n);cadence.Complete();}
            if(cadence.TryBegin(now)){sends++;pending.Add(now+.24);}
            Check(cadence.Pending<=3,"Bounded requests at simulation tick "+tick);
            if(now>=oldNext){oldSends++;oldNext=now+.24;}
        }
        Check(sends>=95&&oldSends<=43,"240 ms RTT: pipelined updates stay near 10 Hz instead of 4 Hz");
        var stalled=new SyncCadence();for(int i=0;i<10;i++)stalled.TryBegin(i*.1);
        Check(stalled.Pending==3,"A disconnected network cannot enqueue unbounded requests");
        stalled.Complete();stalled.Backoff(10);Check(!stalled.TryBegin(9),"Retry backoff is respected");
        var pcm=new VoicePcmBuffer();
        Check(pcm.Push(1,Packet(8192),0)&&pcm.Push(2,Packet(16384),.1),"Two ordered audio blocks accepted");
        Check(!pcm.Push(1,Packet(3276),.11),"Duplicate or reordered audio does not replay");
        var output=new float[1600];pcm.Read(output,.12);
        for(int n=0;n<1600;n++)Check(Mathf.Abs(output[n]-(n<800?.25f:.5f))<.0001f,"Continuous sample "+n);
        Check(pcm.QueuedSamples==0,"Audio duration is preserved without overlapping clips");
        pcm.Reset();pcm.Push(1,Packet(8192),1);var tail=new float[800];pcm.Read(tail,1.13);
        Check(Mathf.Abs(tail[799]-.25f)<.0001f,"Final short syllable is released without waiting forever");
        pcm.Reset();for(int n=1;n<=10;n++)pcm.Push(n,Packet((short)n),n*.1);
        Check(pcm.QueuedSamples==4000&&pcm.DroppedFrames>0,"Audio backlog remains capped at 500 ms");
        var newest=new float[800];pcm.Read(newest,1.01);
        Check(Mathf.Abs(newest[0]-6f/32768)<.000001f,"Overflow drops old speech instead of growing latency");
        pcm.Reset();Check(pcm.Push(1,Packet(1),2),"Reconnecting a speaker resets its packet sequence");
        Check(!pcm.Push(2,new byte[8],2),"Malformed PCM is rejected");
        var motion=new RemoteMotionBuffer();int received=0,stopped=0;float last=0,maxStep=0;
        for(int tick=0;tick<600;tick++)
        {
            float now=tick/60f;
            while(received<100&&received*.1f+.15f<=now)
            {float time=received++*.1f;motion.Push(time,new Vector3(1.7f*time,0,0),Quaternion.identity,1.7f,false,0,now);}
            if(motion.Sample(now,out var position,out _))
            {if(now>1&&now<9){if(motion.Speed<.1f)stopped++;maxStep=Mathf.Max(maxStep,Mathf.Abs(position.x-last));}last=position.x;}
        }
        Check(stopped==0&&maxStep<.08f,"Remote walking at 150 ms one-way latency stays moving and animating");
        string report="checks="+checks.Count+"\npipelinedUpdates10Seconds="+sends+"\nlegacyUpdates10Seconds="+oldSends+"\nremoteMaxStep="+maxStep+"\nlocalOnly=true\n";
        Directory.CreateDirectory("../tools/local-network");File.WriteAllText("../tools/local-network/unity-verification.txt",report);
        Debug.Log("LOCAL_NETWORK_VERIFIED "+report.Replace('\n',' '));
    }
    static byte[] Packet(short sample)
    {
        var bytes=new byte[1600];for(int i=0;i<800;i++){bytes[i*2]=(byte)sample;bytes[i*2+1]=(byte)(sample>>8);}return bytes;
    }
    public static void BuildWindows()
    {
        Run();
        var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=GameUpdateSetup.Scenes,
            target=BuildTarget.StandaloneWindows64,locationPathName="Builds/WindowsLocalNetwork/Horizonte.exe",
            options=BuildOptions.CompressWithLz4HC});
        if(result.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Local Windows build failed");
        Debug.Log("LOCAL_NETWORK_WINDOWS_OK");
    }
}
