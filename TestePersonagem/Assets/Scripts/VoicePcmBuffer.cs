using System;

// Audio-thread safe, bounded PCM playout. A missing packet cannot accumulate seconds of delay.
public sealed class VoicePcmBuffer
{
    public const int Rate=8000,FrameSamples=800;
    readonly object gate=new object();
    readonly float[] samples=new float[Rate/2];
    int head,count,lastSequence;
    bool playing;
    double lastPacket;
    public int Underruns {get;private set;}
    public int DroppedFrames {get;private set;}
    public int QueuedSamples {get{lock(gate)return count;}}
    public void Reset(){lock(gate){head=count=lastSequence=0;playing=false;}}
    public bool Push(int sequence,byte[] pcm,double now)
    {
        if(sequence<=0||pcm==null||pcm.Length!=FrameSamples*2)return false;
        lock(gate)
        {
            if(sequence<=lastSequence)return false;
            if(now-lastPacket>.7){head=count=0;playing=false;}
            if(count+FrameSamples>samples.Length)
            {int removed=count+FrameSamples-samples.Length;head=(head+removed)%samples.Length;count-=removed;DroppedFrames++;}
            for(int i=0;i<FrameSamples;i++)samples[(head+count+i)%samples.Length]=(short)(pcm[i*2]|pcm[i*2+1]<<8)/32768f;
            count+=FrameSamples;lastSequence=sequence;lastPacket=now;
            return true;
        }
    }
    public void Read(float[] output,double now)
    {
        lock(gate)
        {
            Array.Clear(output,0,output.Length);
            // Two frames absorb arrival jitter; release a short final syllable after 120 ms.
            if(!playing&&count>0&&(count>=FrameSamples*2||now-lastPacket>=.12))playing=true;
            if(!playing)return;
            int read=Math.Min(count,output.Length);
            for(int i=0;i<read;i++)output[i]=samples[(head+i)%samples.Length];
            head=(head+read)%samples.Length;count-=read;
            if(read<output.Length)
            {
                int fade=Math.Min(32,read);
                for(int i=0;i<fade;i++)output[read-fade+i]*=1f-(float)i/fade;
                playing=false;Underruns++;
            }
        }
    }
}
