using System;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;
using System.Security.Cryptography;
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
            if(!CredRead(Target,1,0,out var pointer))return ReadProtectedFile();
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
            var bytes=Encoding.UTF8.GetBytes(value);WriteProtectedFile(bytes);var pointer=Marshal.AllocHGlobal(bytes.Length);
            try{Marshal.Copy(bytes,0,pointer,bytes.Length);var entry=new Credential{type=1,target=Target,size=(uint)bytes.Length,blob=pointer,persist=2,user="HBRP"};if(!CredWrite(ref entry,0))Debug.LogWarning("Cofre de credenciais indisponível; acesso guardado com proteção do Windows.");}
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
            if(File.Exists(ProtectedPath))File.Delete(ProtectedPath);
#endif
        }
        catch{}
    }
    internal static bool VerifyNativeStorage()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        var transport=AccountSession.VerificationTransport;
        if(transport==null||(Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-landing")<0&&Array.IndexOf(Environment.GetCommandLineArgs(),"--verify-arrival")<0))return false;
        string previous=Target;Target="HorizonteBrasil/verification/"+Guid.NewGuid();
        AccountSession.VerificationTransport=null;
        try{string sample=new string('V',43);Write(sample);bool saved=Read()==sample;CredDelete(Target,1,0);bool fallback=Read()==sample;Clear();return saved&&fallback&&Read()==null;}
        finally{Clear();Target=previous;AccountSession.VerificationTransport=transport;}
#else
        return true;
#endif
    }
    internal static bool CrossProcessFixture(string action,string nonce)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"--vault-fixture")<0||!Guid.TryParse(nonce,out var id))return false;
        string previous=Target;Target="HorizonteBrasil/verification/"+id.ToString("D");
        try
        {
            string sample=new string('V',43);
            if(action=="write"){Write(sample);return Read()==sample;}
            if(action=="read"){CredDelete(Target,1,0);return Read()==sample;}
            if(action=="clear"){Clear();return Read()==null;}
            return false;
        }
        finally{Target=previous;}
#else
        return false;
#endif
    }
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    static string ProtectedPath
    {
        get{using(var hash=SHA256.Create())return Path.Combine(Application.persistentDataPath,"access-"+BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Target))).Replace("-","").Substring(0,16)+".bin");}
    }
    [StructLayout(LayoutKind.Sequential)]struct Blob{public int length;public IntPtr bytes;}
    static byte[] Protect(byte[] data,bool encrypt)
    {
        var input=new Blob{length=data.Length,bytes=Marshal.AllocHGlobal(data.Length)};Blob output=default;
        try
        {
            Marshal.Copy(data,0,input.bytes,data.Length);
            bool ok=encrypt?CryptProtectData(ref input,null,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output):CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);
            if(!ok)throw new CryptographicException("Armazenamento protegido indisponível.");
            var result=new byte[output.length];Marshal.Copy(output.bytes,result,0,result.Length);return result;
        }
        finally{Marshal.FreeHGlobal(input.bytes);if(output.bytes!=IntPtr.Zero)LocalFree(output.bytes);}
    }
    static void WriteProtectedFile(byte[] value)
    {
        Directory.CreateDirectory(Application.persistentDataPath);
        string next=ProtectedPath+".tmp";File.WriteAllBytes(next,Protect(value,true));
        if(File.Exists(ProtectedPath))File.Replace(next,ProtectedPath,null);else File.Move(next,ProtectedPath);
    }
    static string ReadProtectedFile()
    {
        if(!File.Exists(ProtectedPath))return null;
        byte[] token=Protect(File.ReadAllBytes(ProtectedPath),false);return token.Length==43?Encoding.UTF8.GetString(token):null;
    }
    [DllImport("crypt32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CryptProtectData(ref Blob input,string description,IntPtr entropy,IntPtr reserved,IntPtr prompt,uint flags,out Blob output);
    [DllImport("crypt32.dll",SetLastError=true)]static extern bool CryptUnprotectData(ref Blob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,uint flags,out Blob output);
    [DllImport("kernel32.dll")]static extern IntPtr LocalFree(IntPtr pointer);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct Credential{public uint flags,type;public string target,comment;public long written;public uint size;public IntPtr blob;public uint persist,count;public IntPtr attributes;public string alias,user;}
    [DllImport("advapi32.dll",EntryPoint="CredReadW",CharSet=CharSet.Unicode)]static extern bool CredRead(string target,uint type,uint flags,out IntPtr credential);
    [DllImport("advapi32.dll",EntryPoint="CredWriteW",CharSet=CharSet.Unicode)]static extern bool CredWrite(ref Credential credential,uint flags);
    [DllImport("advapi32.dll",EntryPoint="CredDeleteW",CharSet=CharSet.Unicode)]static extern bool CredDelete(string target,uint type,uint flags);
    [DllImport("advapi32.dll")]static extern void CredFree(IntPtr pointer);
#endif
}
