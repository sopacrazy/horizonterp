using System;
using System.Diagnostics;
using UnityEngine;

// One continuous stream per speaker: no overlapping PlayOneShot clips or per-packet AudioClips.
public sealed class VoicePlayback : IDisposable
{
    readonly AudioSource source;
    readonly VoicePcmBuffer buffer=new VoicePcmBuffer();
    AudioClip clip;
    public int Underruns=>buffer.Underruns;
    public VoicePlayback(AudioSource source){this.source=source;}
    static double Clock=>(double)Stopwatch.GetTimestamp()/Stopwatch.Frequency;
    public void Push(int sequence,string encoded,float volume=1f)
    {
        if(!source||string.IsNullOrEmpty(encoded))return;
        byte[] pcm;
        try{pcm=Convert.FromBase64String(encoded);}catch(FormatException){return;}
        if(!buffer.Push(sequence,pcm,Clock))return;
        source.volume=volume;
        if(!clip)
        {
            clip=AudioClip.Create("Voz contínua",VoicePcmBuffer.Rate,1,VoicePcmBuffer.Rate,true,data=>buffer.Read(data,Clock));
            source.clip=clip;source.loop=true;source.Play();
        }
    }
    public void Reset(){buffer.Reset();}
    public void Dispose(){if(source)source.Stop();if(clip)UnityEngine.Object.Destroy(clip);buffer.Reset();}
}
