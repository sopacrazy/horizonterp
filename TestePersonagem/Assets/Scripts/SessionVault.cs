using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

// Store only the revocable session token; passwords never reach local storage.
internal static class SessionVault
{
    static string Target="HorizonteBrasil/session/v1";
    internal static Func<string> TestRead;
    internal static Action<string> TestWrite;
    internal static Action TestClear;
    internal static string Read()
    {
        if(AccountSession.VerificationTransport!=null)return TestRead?.Invoke();
        try
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using(var bridge=new AndroidJavaClass("com.horizonte.update.HbrpSessionVault"))
            using(var unity=new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using(var activity=unity.GetStatic<AndroidJavaObject>("currentActivity"))return bridge.CallStatic<string>("read",activity);
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if(!CredRead(Target,1,0,out var pointer))return null;
            try{var entry=Marshal.PtrToStructure<Credential>(pointer);if(entry.size!=43)return null;var bytes=new byte[entry.size];Marshal.Copy(entry.blob,bytes,0,bytes.Length);return Encoding.UTF8.GetString(bytes);}
            finally{CredFree(pointer);}
#else
            return null;
#endif
        }
        catch{return null;}
    }
    internal static void Write(string value)
    {
        if(AccountSession.VerificationTransport!=null){TestWrite?.Invoke(value);return;}
        try
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using(var bridge=new AndroidJavaClass("com.horizonte.update.HbrpSessionVault"))
            using(var unity=new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using(var activity=unity.GetStatic<AndroidJavaObject>("currentActivity"))bridge.CallStatic("write",activity,value);
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            var bytes=Encoding.UTF8.GetBytes(value);var pointer=Marshal.AllocHGlobal(bytes.Length);
            try{Marshal.Copy(bytes,0,pointer,bytes.Length);var entry=new Credential{type=1,target=Target,size=(uint)bytes.Length,blob=pointer,persist=2,user="HBRP"};CredWrite(ref entry,0);}
            finally{Marshal.FreeHGlobal(pointer);}
#endif
        }
        catch{Debug.LogWarning("Não foi possível guardar o acesso neste dispositivo.");}
    }
    internal static void Clear()
    {
        if(AccountSession.VerificationTransport!=null){TestClear?.Invoke();return;}
        try
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using(var bridge=new AndroidJavaClass("com.horizonte.update.HbrpSessionVault"))
            using(var unity=new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using(var activity=unity.GetStatic<AndroidJavaObject>("currentActivity"))bridge.CallStatic("clear",activity);
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            CredDelete(Target,1,0);
#endif
        }
        catch{}
    }
    internal static bool VerifyNativeStorage()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        var transport=AccountSession.VerificationTransport;
        if(transport==null||Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-landing")<0)return false;
        string previous=Target;Target="HorizonteBrasil/verification/"+Guid.NewGuid();
        AccountSession.VerificationTransport=null;
        try{string sample=new string('V',43);Write(sample);bool saved=Read()==sample;Clear();return saved&&Read()==null;}
        finally{Clear();Target=previous;AccountSession.VerificationTransport=transport;}
#else
        return true;
#endif
    }
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct Credential{public uint flags,type;public string target,comment;public long written;public uint size;public IntPtr blob;public uint persist,count;public IntPtr attributes;public string alias,user;}
    [DllImport("advapi32.dll",EntryPoint="CredReadW",CharSet=CharSet.Unicode)]static extern bool CredRead(string target,uint type,uint flags,out IntPtr credential);
    [DllImport("advapi32.dll",EntryPoint="CredWriteW",CharSet=CharSet.Unicode)]static extern bool CredWrite(ref Credential credential,uint flags);
    [DllImport("advapi32.dll",EntryPoint="CredDeleteW",CharSet=CharSet.Unicode)]static extern bool CredDelete(string target,uint type,uint flags);
    [DllImport("advapi32.dll")]static extern void CredFree(IntPtr pointer);
#endif
}
